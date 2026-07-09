using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.Rules
{
    public class ReleaseRule : IEmbeddedDocument
    {
        public string Name { get; set; }
        public List<ReleaseRuleCondition> Conditions { get; set; }

        public ReleaseRule()
        {
            Conditions = new List<ReleaseRuleCondition>();
        }
    }

    public class ReleaseRuleCondition
    {
        public ReleaseRuleConditionType ConditionType { get; set; }
        public ReleaseRuleConditionOperator Operator { get; set; }
        public string Value { get; set; }
    }

    public enum ReleaseRuleConditionType
    {
        ReleaseGroup = 1,
        AudioType = 2,
        CustomFormat = 3,
        Quality = 4,
        ReleaseTitle = 5
    }

    public enum ReleaseRuleConditionOperator
    {
        Exact = 1,
        Contains = 2
    }
}
