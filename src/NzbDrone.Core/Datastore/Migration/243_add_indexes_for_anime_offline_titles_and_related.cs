using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(243)]
    public class add_indexes_for_anime_offline_titles_and_related : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            if (Schema.Table("AnimeOfflineTitles").Exists())
            {
                Create.Index("IX_AnimeOfflineTitles_MalId")
                      .OnTable("AnimeOfflineTitles")
                      .OnColumn("MalId")
                      .Ascending()
                      .WithOptions()
                      .NonClustered();

                Create.Index("IX_AnimeOfflineTitles_AniListId")
                      .OnTable("AnimeOfflineTitles")
                      .OnColumn("AniListId")
                      .Ascending()
                      .WithOptions()
                      .NonClustered();
            }

            if (Schema.Table("AniDbRelatedSeries").Exists())
            {
                Create.Index("IX_AniDbRelatedSeries_RelatedAniDbId")
                      .OnTable("AniDbRelatedSeries")
                      .OnColumn("RelatedAniDbId")
                      .Ascending()
                      .WithOptions()
                      .NonClustered();
            }

            if (Schema.Table("ImportListExclusions").Exists())
            {
                Create.Index("IX_ImportListExclusions_AniDbId")
                      .OnTable("ImportListExclusions")
                      .OnColumn("AniDbId")
                      .Ascending()
                      .WithOptions()
                      .NonClustered();

                Create.Index("IX_ImportListExclusions_MalId")
                      .OnTable("ImportListExclusions")
                      .OnColumn("MalId")
                      .Ascending()
                      .WithOptions()
                      .NonClustered();

                Create.Index("IX_ImportListExclusions_AniListId")
                      .OnTable("ImportListExclusions")
                      .OnColumn("AniListId")
                      .Ascending()
                      .WithOptions()
                      .NonClustered();
            }
        }
    }
}
