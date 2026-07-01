using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(235)]
    public class add_anidarr_ids_to_import_exclusions : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ImportListExclusions")
                .AddColumn("AniDbId").AsInt32().Nullable()
                .AddColumn("MalId").AsInt32().Nullable()
                .AddColumn("AniListId").AsInt32().Nullable()
                .AddColumn("SimklId").AsInt32().Nullable();
        }
    }
}
