using System;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.AniDb;

namespace NzbDrone.Core.HealthCheck.Checks
{
    public class AniDbBanHealthCheck : HealthCheckBase
    {
        public AniDbBanHealthCheck(ILocalizationService localizationService)
            : base(localizationService)
        {
        }

        public override HealthCheck Check()
        {
            if (AniDbProvider.BanExpiration.HasValue && AniDbProvider.BanExpiration.Value > DateTime.UtcNow)
            {
                return new HealthCheck(GetType(), HealthCheckResult.Warning, HealthCheckReason.AniDbBanActive, "AniDB rate limit / ban active. Metadata updates will fail until the ban expires (approx 24 hours from the ban).");
            }

            return new HealthCheck(GetType());
        }
    }
}
