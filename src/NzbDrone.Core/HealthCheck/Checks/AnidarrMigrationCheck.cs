using System.IO;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.HealthCheck.Checks
{
    public class AnidarrMigrationCheck : HealthCheckBase
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly ISeriesService _seriesService;

        public AnidarrMigrationCheck(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, ISeriesService seriesService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _seriesService = seriesService;
        }

        public override HealthCheck Check()
        {
            var anidarrAppData = _appFolderInfo.AppDataFolder;
            var sonarrAppData = Path.Combine(Directory.GetParent(anidarrAppData).FullName, "Sonarr");
            var sonarrDbPath = Path.Combine(sonarrAppData, "sonarr.db");

            if (_diskProvider.FileExists(sonarrDbPath) && !_seriesService.GetAllSeries().Any())
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.AnidarrMigration,
                    _localizationService.GetLocalizedString("AnidarrMigrationMessage"));
            }

            return new HealthCheck(GetType());
        }
    }
}
