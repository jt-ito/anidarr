using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(233)]
    public class add_anime_offline_titles_table : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.Sql("DROP TABLE IF EXISTS AnimeOfflineTitles");

            Create.TableForModel("AnimeOfflineTitles")
                .WithColumn("AniDbId").AsInt32().Nullable()
                .WithColumn("MalId").AsInt32().Nullable()
                .WithColumn("AniListId").AsInt32().Nullable()
                .WithColumn("SimklId").AsInt32().Nullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("CleanTitle").AsString().Nullable()
                .WithColumn("SearchSynonyms").AsString().Nullable()
                .WithColumn("PictureUrl").AsString().Nullable()
                .WithColumn("Year").AsInt32().Nullable()
                .WithColumn("Genres").AsString().Nullable()
                .WithColumn("Status").AsInt32().Nullable()
                .WithColumn("Overview").AsString().Nullable();

            Create.Index("IX_AnimeOfflineTitles_CleanTitle")
                .OnTable("AnimeOfflineTitles")
                .OnColumn("CleanTitle")
                .Ascending()
                .WithOptions()
                .NonClustered();

            Create.Index("IX_AnimeOfflineTitles_AniDbId")
                .OnTable("AnimeOfflineTitles")
                .OnColumn("AniDbId")
                .Ascending()
                .WithOptions()
                .NonClustered();
        }
    }
}
