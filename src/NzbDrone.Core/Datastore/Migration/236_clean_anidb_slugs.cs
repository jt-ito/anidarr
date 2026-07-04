using System;
using System.Collections.Generic;
using System.Data;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(236)]
    public class clean_anidb_slugs : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(CleanAnidbSlugs);
        }

        private void CleanAnidbSlugs(IDbConnection conn, IDbTransaction tran)
        {
            var seriesToUpdate = new List<dynamic>();
            var existingSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tran;
                cmd.CommandText = "SELECT \"Id\", \"TitleSlug\" FROM \"Series\"";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetInt32(0);
                    // TitleSlug can theoretically be null, although unlikely
                    var titleSlug = reader.IsDBNull(1) ? null : reader.GetString(1);
                    if (titleSlug != null)
                    {
                        existingSlugs.Add(titleSlug);

                        if (titleSlug.Contains("-anidb-"))
                        {
                            var newSlug = titleSlug.Substring(0, titleSlug.IndexOf("-anidb-"));
                            seriesToUpdate.Add(new { Id = id, OldSlug = titleSlug, NewSlug = newSlug });
                        }
                    }
                }
            }

            foreach (var series in seriesToUpdate)
            {
                if (existingSlugs.Contains(series.NewSlug))
                {
                    continue; // Skip to avoid unique constraint violation
                }

                existingSlugs.Add(series.NewSlug);
                var updateSql = "UPDATE \"Series\" SET \"TitleSlug\" = @NewSlug WHERE \"Id\" = @Id";
                conn.Execute(updateSql, new { NewSlug = series.NewSlug, Id = series.Id }, transaction: tran);
            }
        }
    }
}
