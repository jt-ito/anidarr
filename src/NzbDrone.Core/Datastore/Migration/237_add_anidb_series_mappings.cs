using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(237)]
    public class add_anidb_series_mappings : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("AniDbSeriesMappings")
                  .WithColumn("SeriesId").AsInt32().NotNullable()
                  .WithColumn("AniDbId").AsInt32().NotNullable()
                  .WithColumn("SeasonNumber").AsInt32().NotNullable()
                  .WithColumn("RelationType").AsString().NotNullable();

            Create.Index("IX_AniDbSeriesMappings_SeriesId")
                  .OnTable("AniDbSeriesMappings")
                  .OnColumn("SeriesId").Ascending();

            Create.Index("IX_AniDbSeriesMappings_AniDbId")
                  .OnTable("AniDbSeriesMappings")
                  .OnColumn("AniDbId").Ascending().WithOptions().Unique();
        }
    }
}
