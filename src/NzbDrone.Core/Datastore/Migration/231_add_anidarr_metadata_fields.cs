using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Anidarr migration 231: add AniDbId, SimklId, PrimaryMetadataProvider to Series table.
    // MalIds and AniListIds already exist from migration 217.
    [Migration(231)]
    public class add_anidarr_metadata_fields : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Series")
                .AddColumn("AniDbId").AsInt32().Nullable()
                .AddColumn("SimklId").AsInt32().Nullable()
                .AddColumn("PrimaryMetadataProvider").AsString().Nullable();
        }
    }
}
