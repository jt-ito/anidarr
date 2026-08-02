using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(241)]
    public class add_anidb_related_series : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("AniDbRelatedSeries")
                  .WithColumn("SeriesId").AsInt32().NotNullable()
                  .WithColumn("RelatedAniDbId").AsInt32().NotNullable()
                  .WithColumn("RelationType").AsString().NotNullable();

            Create.Index("IX_AniDbRelatedSeries_SeriesId")
                  .OnTable("AniDbRelatedSeries")
                  .OnColumn("SeriesId").Ascending();

            Create.TableForModel("AniDbRelatedMetadataCache")
                  .WithColumn("AniDbId").AsInt32().NotNullable().Unique()
                  .WithColumn("Title").AsString().Nullable()
                  .WithColumn("PosterUrl").AsString().Nullable()
                  .WithColumn("Overview").AsString().Nullable();
        }
    }
}
