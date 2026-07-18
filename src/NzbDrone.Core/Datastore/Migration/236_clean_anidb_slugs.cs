using System;
using System.Data;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(236)]
    public class clean_anidb_slugs : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(CleanAniDbSlugs);
        }

        private void CleanAniDbSlugs(IDbConnection conn, IDbTransaction tran)
        {
            using (var getSeriesCmd = conn.CreateCommand())
            {
                getSeriesCmd.Transaction = tran;
                getSeriesCmd.CommandText = "SELECT \"Id\", \"TitleSlug\" FROM \"Series\" WHERE \"TitleSlug\" LIKE '%-anidb-%'";
                using (var seriesReader = getSeriesCmd.ExecuteReader())
                {
                    while (seriesReader.Read())
                    {
                        var id = seriesReader.GetInt32(0);
                        var titleSlug = seriesReader.GetString(1);

                        var index = titleSlug.IndexOf("-anidb-", StringComparison.Ordinal);
                        if (index <= 0)
                        {
                            continue;
                        }

                        var newSlug = titleSlug.Substring(0, index);

                        using (var checkCmd = conn.CreateCommand())
                        {
                            checkCmd.Transaction = tran;
                            checkCmd.CommandText = "SELECT COUNT(1) FROM \"Series\" WHERE \"TitleSlug\" = ?";
                            checkCmd.AddParameter(newSlug);

                            var exists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;

                            if (!exists)
                            {
                                using (var updateCmd = conn.CreateCommand())
                                {
                                    updateCmd.Transaction = tran;
                                    updateCmd.CommandText = "UPDATE \"Series\" SET \"TitleSlug\" = ? WHERE \"Id\" = ?";
                                    updateCmd.AddParameter(newSlug);
                                    updateCmd.AddParameter(id);

                                    updateCmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
