using System.Data.SQLite;
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
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN SimklId;");
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN PrimaryMetadataProvider;");
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE Series DROP COLUMN FansubGroup;");
                    ExecuteNonQueryIgnoreErrors(connection, "ALTER TABLE QualityProfiles DROP COLUMN ReleaseRules;");

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
