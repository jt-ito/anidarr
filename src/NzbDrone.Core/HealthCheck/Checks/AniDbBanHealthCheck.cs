using System;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    public class AniDbBanHealthCheck : HealthCheckBase
    {
        private readonly IConfigFileProvider _configService;

        public AniDbBanHealthCheck(ILocalizationService localizationService, IConfigFileProvider configService)
            : base(localizationService)
        {
            _configService = configService;
        }

        public override HealthCheck Check()
        {
            if (_configService.AniDbBanExpiration.HasValue && _configService.AniDbBanExpiration.Value > DateTime.UtcNow)
            {
                return new HealthCheck(GetType(), HealthCheckResult.Warning, HealthCheckReason.AniDbBanActive, "AniDB rate limit / ban active. Metadata updates will fail until the ban expires (approx 24 hours from the ban).");
            }

            return new HealthCheck(GetType());
        }
    }
}
