using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(238)]
    public class add_release_rules_to_quality_profiles : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("QualityProfiles").AddColumn("UseRuleListMode").AsBoolean().WithDefaultValue(false);
            Alter.Table("QualityProfiles").AddColumn("FallbackQualityProfileId").AsInt32().Nullable();
            Alter.Table("QualityProfiles").AddColumn("ReleaseRules").AsString().WithDefaultValue("[]");
        }
    }
}
