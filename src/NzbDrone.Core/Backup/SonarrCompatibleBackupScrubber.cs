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
                JournalMode = SQLiteJournalModeEnum.Truncate
            }.ToString();

            using (var connection = (SQLiteConnection)SQLiteFactory.Instance.CreateConnection())
            {
                connection.ConnectionString = connectionString;
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    // Remove AniDB-only series and their episodes
                    ExecuteNonQuery(connection, "DELETE FROM Episodes WHERE SeriesId IN (SELECT Id FROM Series WHERE PrimaryMetadataProvider = 'anidb' AND (TvdbId = 0 OR TvdbId IS NULL));");
                    ExecuteNonQuery(connection, "DELETE FROM Series WHERE PrimaryMetadataProvider = 'anidb' AND (TvdbId = 0 OR TvdbId IS NULL);");

                    // Reset database migration version to Sonarr Vanilla max version (230)
                    ExecuteNonQuery(connection, "DELETE FROM VersionInfo WHERE Version > 230;");

                    // Drop Anidarr-specific columns. We use IgnoreErrors in case the columns are already missing or SQLite version is too old.
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN AniDbId;");
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN PrimaryMetadataProvider;");
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN FansubGroup;");
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN AlternateTitles;");
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE QualityProfiles DROP COLUMN ReleaseRules;");

                    // Scrub Anidarr-specific schema from JSON blobs
                    ScrubSeriesSeasonsJson(connection);

                    // Drop Anidarr-specific tables
                    ExecuteNonQuery(connection, "DROP TABLE IF EXISTS AnimeOfflineDatabase;");
                    ExecuteNonQuery(connection, "DROP TABLE IF EXISTS AnimeOfflineTitles;");
                    ExecuteNonQuery(connection, "DROP TABLE IF EXISTS AniDbMappings;");

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

        private void ScrubSeriesSeasonsJson(SQLiteConnection connection)
        {
            var updates = new List<Tuple<int, string>>();

            try
            {
                using (var command = connection.CreateCommand())
                {
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
                                foreach (JObject season in seasonsArray)
                                {
                                    season.Remove("Title");
                                    season.Remove("Images");
                                }

                                updates.Add(Tuple.Create(id, seasonsArray.ToString(Newtonsoft.Json.Formatting.None)));
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

        private void ExecuteNonQuery(SQLiteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private void ExecuteNonQueryIgnoreErrors(SQLiteConnection connection, string sql)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
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
