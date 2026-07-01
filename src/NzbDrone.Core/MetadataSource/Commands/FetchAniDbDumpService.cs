using System;
using System.IO;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MetadataSource.Commands
{
    public class FetchAniDbDumpService : IExecute<FetchAniDbDumpCommand>
    {
        private readonly IAnimeOfflineDatabase _animeOfflineDatabase;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;

        public FetchAniDbDumpService(IAnimeOfflineDatabase animeOfflineDatabase, IAppFolderInfo appFolderInfo, Logger logger)
        {
            _animeOfflineDatabase = animeOfflineDatabase;
            _appFolderInfo = appFolderInfo;
            _logger = logger;
        }

        public void Execute(FetchAniDbDumpCommand message)
        {
            var officialDatPath = Path.Combine(_appFolderInfo.AppDataFolder, "anime-titles.dat.gz");

            if (File.Exists(officialDatPath))
            {
                var lastModified = File.GetLastWriteTimeUtc(officialDatPath);
                if (lastModified > DateTime.UtcNow.AddHours(-24))
                {
                    _logger.Warn("AniDB dump was fetched recently. Next fetch allowed after {0}", lastModified.AddHours(24));
                    throw new CommandFailedException("AniDB dump can only be fetched once every 24 hours. Please try again later.");
                }
            }

            _logger.Info("Forcing download of AniDB dump...");
            _animeOfflineDatabase.ForceDownloadDump();
            _logger.Info("AniDB dump download complete.");
        }
    }
}
