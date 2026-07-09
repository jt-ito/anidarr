using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class CustomFormatAllowedbyProfileSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly IQualityProfileService _qualityProfileService;
        private readonly Logger _logger;

        public CustomFormatAllowedbyProfileSpecification(IQualityProfileService qualityProfileService, Logger logger)
        {
            _qualityProfileService = qualityProfileService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, ReleaseDecisionInformation information)
        {
            var profile = subject.Series.QualityProfile.Value;
            var minScore = profile.MinFormatScore;

            if (profile.UseRuleListMode)
            {
                if (subject.ReleaseRuleIndex.HasValue)
                {
                    // Rule matched directly, so minimum custom format score doesn't apply to reject it.
                    return DownloadSpecDecision.Accept();
                }
                else
                {
                    // Fallback logic
                    if (!profile.FallbackQualityProfileId.HasValue)
                    {
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.ReleaseRuleMismatch, "Release did not match any priority rule and no fallback profile is configured.");
                    }

                    var fallbackProfile = _qualityProfileService.Get(profile.FallbackQualityProfileId.Value);
                    if (fallbackProfile == null)
                    {
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.ReleaseRuleMismatch, "Fallback profile is invalid.");
                    }

                    minScore = fallbackProfile.MinFormatScore;
                }
            }

            var score = subject.CustomFormatScore;

            if (score < minScore)
            {
                return DownloadSpecDecision.Reject(DownloadRejectionReason.CustomFormatMinimumScore, "Custom Formats {0} have score {1} below Series profile minimum {2}", subject.CustomFormats.ConcatToString(), score, minScore);
            }

            _logger.Trace("Custom Format Score of {0} [{1}] above Series profile minimum {2}", score, subject.CustomFormats.ConcatToString(), minScore);

            return DownloadSpecDecision.Accept();
        }
    }
}
