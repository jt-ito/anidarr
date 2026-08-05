using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(242)]
    public class add_title_fields_to_anime_offline_titles : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            if (!Schema.Table("AnimeOfflineTitles").Column("RomajiTitle").Exists())
            {
                Alter.Table("AnimeOfflineTitles").AddColumn("RomajiTitle").AsString().Nullable();
            }

            if (!Schema.Table("AnimeOfflineTitles").Column("NativeTitle").Exists())
            {
                Alter.Table("AnimeOfflineTitles").AddColumn("NativeTitle").AsString().Nullable();
            }

            if (!Schema.Table("AnimeOfflineTitles").Column("EnglishTitle").Exists())
            {
                Alter.Table("AnimeOfflineTitles").AddColumn("EnglishTitle").AsString().Nullable();
            }
        }
    }
}
