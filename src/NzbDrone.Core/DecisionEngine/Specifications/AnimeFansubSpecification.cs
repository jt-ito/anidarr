using System;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class AnimeFansubSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public AnimeFansubSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, ReleaseDecisionInformation information)
        {
            var series = subject.Series;

            if (series.SeriesType != SeriesTypes.Anime || series.FansubGroup.IsNullOrWhiteSpace())
            {
                return DownloadSpecDecision.Accept();
            }

            var expectedFansubGroup = series.FansubGroup;
            var actualFansubGroup = subject.ParsedEpisodeInfo.ReleaseGroup;

            if (actualFansubGroup.IsNullOrWhiteSpace())
            {
                _logger.Debug("Release is missing a fansub group, but series {0} requires {1}", series.Title, expectedFansubGroup);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.UnknownReleaseGroup, "Missing fansub group");
            }

            if (!expectedFansubGroup.Equals(actualFansubGroup, StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.Debug("Release fansub group {0} does not match series required fansub group {1}", actualFansubGroup, expectedFansubGroup);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.ReleaseGroupDoesNotMatch, "Fansub group does not match");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
