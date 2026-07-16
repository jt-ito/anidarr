using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(240)]
    public class remove_simkl_from_anidarr : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            if (Schema.Table("Series").Column("SimklId").Exists())
            {
                Delete.Column("SimklId").FromTable("Series");
            }

            if (Schema.Table("AnimeOfflineTitles").Column("SimklId").Exists())
            {
                Delete.Column("SimklId").FromTable("AnimeOfflineTitles");
            }

            if (Schema.Table("ImportListExclusions").Column("SimklId").Exists())
            {
                Delete.Column("SimklId").FromTable("ImportListExclusions");
            }
        }
    }
}
