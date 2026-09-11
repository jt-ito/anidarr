using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Newtonsoft.Json.Linq;
using NLog;

namespace NzbDrone.Core.Backup
{
    public interface ISonarrCompatibleBackupScrubber
    {
        void ScrubDatabase(string sqliteFilePath);
    }

    public class SonarrCompatibleBackupScrubber : ISonarrCompatibleBackupScrubber
    {
        private readonly Logger _logger;

        public SonarrCompatibleBackupScrubber(Logger logger)
        {
            _logger = logger;
        }

        public void ScrubDatabase(string sqliteFilePath)
        {
            var connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = sqliteFilePath,
                JournalMode = SQLiteJournalModeEnum.Off
            }.ToString();

            using (var connection = (SQLiteConnection)SQLiteFactory.Instance.CreateConnection())
            {
                connection.ConnectionString = connectionString;
                connection.Open();

                using (var pragmaCommand = connection.CreateCommand())
                {
                    pragmaCommand.CommandText = "PRAGMA synchronous = OFF; PRAGMA journal_mode = MEMORY; PRAGMA temp_store = MEMORY; PRAGMA cache_size = 10000;";
                    pragmaCommand.ExecuteNonQuery();
                }

                using (var transaction = connection.BeginTransaction())
                {
                    // Remove AniDB-only series and their episodes
                    ExecuteNonQuery(connection, "DELETE FROM Episodes WHERE SeriesId IN (SELECT Id FROM Series WHERE PrimaryMetadataProvider = 'anidb' AND (TvdbId = 0 OR TvdbId IS NULL));", transaction);
                    ExecuteNonQuery(connection, "DELETE FROM Series WHERE PrimaryMetadataProvider = 'anidb' AND (TvdbId = 0 OR TvdbId IS NULL);", transaction);

                    // Reset database migration version to Sonarr Vanilla max version (230)
                    ExecuteNonQuery(connection, "DELETE FROM VersionInfo WHERE Version > 230;", transaction);

                    // Drop Anidarr-specific columns. We use IgnoreErrors in case the columns are already missing or SQLite version is too old.
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN AniDbId;", transaction);
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN PrimaryMetadataProvider;", transaction);
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN FansubGroup;", transaction);
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN AlternateTitles;", transaction);
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE QualityProfiles DROP COLUMN ReleaseRules;", transaction);

                    // Scrub Anidarr-specific schema from JSON blobs
                    ScrubSeriesSeasonsJson(connection, transaction);

                    // Drop Anidarr-specific tables
                    ExecuteNonQuery(connection, "DROP TABLE IF EXISTS AnimeOfflineDatabase;", transaction);
                    ExecuteNonQuery(connection, "DROP TABLE IF EXISTS AnimeOfflineTitles;", transaction);
                    ExecuteNonQuery(connection, "DROP TABLE IF EXISTS AnimeOfflineMetadata;", transaction);
                    ExecuteNonQuery(connection, "DROP TABLE IF EXISTS AniDbMappings;", transaction);
                    ExecuteNonQuery(connection, "DROP TABLE IF EXISTS AniDbRelatedMetadataCache;", transaction);

                    transaction.Commit();
                }

                // Vacuum to reclaim space
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "VACUUM;";
                    command.ExecuteNonQuery();
                }
            }

            SQLiteConnection.ClearAllPools();
        }

        private void ScrubSeriesSeasonsJson(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var updates = new List<Tuple<int, string>>();

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT Id, Seasons FROM Series WHERE Seasons IS NOT NULL AND Seasons != '[]';";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetInt32(0);
                            var seasonsJson = reader.GetString(1);

                            try
                            {
                                var seasonsArray = JArray.Parse(seasonsJson);
                                var modified = false;

                                foreach (JObject season in seasonsArray)
                                {
                                    var removedTitle = season.Remove("Title");
                                    var removedImages = season.Remove("Images");
                                    if (removedTitle || removedImages)
                                    {
                                        modified = true;
                                    }
                                }

                                if (modified)
                                {
                                    updates.Add(Tuple.Create(id, seasonsArray.ToString(Newtonsoft.Json.Formatting.None)));
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn(ex, $"Failed to parse or scrub Seasons JSON for Series Id {id}");
                            }
                        }
                    }
                }

                foreach (var update in updates)
                {
                    using (var updateCommand = connection.CreateCommand())
                    {
                        updateCommand.Transaction = transaction;
                        updateCommand.CommandText = "UPDATE Series SET Seasons = @seasons WHERE Id = @id;";
                        updateCommand.Parameters.Add(new SQLiteParameter("@seasons", update.Item2));
                        updateCommand.Parameters.Add(new SQLiteParameter("@id", update.Item1));
                        updateCommand.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to scrub Seasons JSON in Sonarr-Compatible Backup.");
            }
        }

        private void ExecuteNonQuery(SQLiteConnection connection, string sql, SQLiteTransaction transaction = null)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private void ExecuteNonQueryIgnoreErrors(SQLiteConnection connection, string sql, SQLiteTransaction transaction = null)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
            catch (SQLiteException ex)
            {
                _logger.Warn(ex, $"Failed to execute script: {sql}. The column might not exist or the SQLite version doesn't support DROP COLUMN.");
            }
        }
    }
}
