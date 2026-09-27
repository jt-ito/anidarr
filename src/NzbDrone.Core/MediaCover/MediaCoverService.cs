using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.MediaCover
{
    public interface IMapCoversToLocal
    {
        void ConvertToLocalUrls(int seriesId, IEnumerable<MediaCover> covers);
        string GetCoverPath(int seriesId, MediaCoverTypes coverType, int? height = null);
        bool EnsureCovers(Series series);
        string EnsureCover(int seriesId, MediaCoverTypes coverType, int? height = null);
    }

    public class MediaCoverService :
        IHandleAsync<SeriesAddedEvent>,
        IHandleAsync<SeriesUpdatedEvent>,
        IHandleAsync<SeriesDeletedEvent>,
        IMapCoversToLocal
    {
        private readonly IMediaCoverProxy _mediaCoverProxy;
        private readonly IImageResizer _resizer;
        private readonly IHttpClient _httpClient;
        private readonly IDiskProvider _diskProvider;
        private readonly ICoverExistsSpecification _coverExistsSpecification;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly ISeriesRepository _seriesRepository;
        private readonly MetadataSource.IAnimeOfflineTitleRepository _animeOfflineTitleRepository;
        private readonly Logger _logger;

        private readonly string _coverRootFolder;

        // ImageSharp is slow on ARM (no hardware acceleration on mono yet)
        // So limit the number of concurrent resizing tasks
        private static SemaphoreSlim _semaphore = new SemaphoreSlim((int)Math.Ceiling(Environment.ProcessorCount / 2.0));

        public MediaCoverService(IMediaCoverProxy mediaCoverProxy,
                                 IImageResizer resizer,
                                 IHttpClient httpClient,
                                 IDiskProvider diskProvider,
                                 IAppFolderInfo appFolderInfo,
                                 ICoverExistsSpecification coverExistsSpecification,
                                 IConfigFileProvider configFileProvider,
                                 IEventAggregator eventAggregator,
                                 Logger logger,
                                 ISeriesRepository seriesRepository = null,
                                 MetadataSource.IAnimeOfflineTitleRepository animeOfflineTitleRepository = null)
        {
            _mediaCoverProxy = mediaCoverProxy;
            _resizer = resizer;
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _coverExistsSpecification = coverExistsSpecification;
            _configFileProvider = configFileProvider;
            _eventAggregator = eventAggregator;
            _seriesRepository = seriesRepository;
            _animeOfflineTitleRepository = animeOfflineTitleRepository;
            _logger = logger;

            _coverRootFolder = appFolderInfo.GetMediaCoverPath();
        }

        public string GetCoverPath(int seriesId, MediaCoverTypes coverType, int? height = null)
        {
            var heightSuffix = height.HasValue ? "-" + height.ToString() : "";

            return Path.Combine(GetSeriesCoverPath(seriesId), coverType.ToString().ToLower() + heightSuffix + GetExtension(coverType));
        }

        public void ConvertToLocalUrls(int seriesId, IEnumerable<MediaCover> covers)
        {
            if (seriesId == 0)
            {
                // Series isn't in Sonarr yet, map via a proxy to circumvent referrer issues
                foreach (var mediaCover in covers)
                {
                    mediaCover.Url = _mediaCoverProxy.RegisterUrl(mediaCover.RemoteUrl);
                }
            }
            else
            {
                foreach (var mediaCover in covers)
                {
                    if (mediaCover.CoverType == MediaCoverTypes.Unknown)
                    {
                        continue;
                    }

                    var filePath = GetCoverPath(seriesId, mediaCover.CoverType);

                    mediaCover.Url = _configFileProvider.UrlBase + @"/MediaCover/" + seriesId + "/" + mediaCover.CoverType.ToString().ToLower() + GetExtension(mediaCover.CoverType);

                    if (_diskProvider.TryGetFileLastWrite(filePath, out var lastWrite))
                    {
                        mediaCover.Url += "?lastWrite=" + lastWrite.Ticks;
                    }
                }
            }
        }

        private string GetSeriesCoverPath(int seriesId)
        {
            return Path.Combine(_coverRootFolder, seriesId.ToString());
        }

        public bool EnsureCovers(Series series)
        {
            if (series == null)
            {
                return false;
            }

            if (series.Images == null)
            {
                series.Images = new List<MediaCover>();
            }

            var poster = series.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster);
            if ((poster == null || poster.RemoteUrl.IsNullOrWhiteSpace()) && _animeOfflineTitleRepository != null)
            {
                MetadataSource.AnimeOfflineTitle title = null;
                if (series.AniDbId.HasValue && series.AniDbId > 0)
                {
                    title = _animeOfflineTitleRepository.FindByAniDbId(series.AniDbId.Value);
                }

                if (title == null && series.AniListIds != null && series.AniListIds.Any())
                {
                    title = _animeOfflineTitleRepository.FindByAniListId(series.AniListIds.First());
                }

                if (title == null && series.MalIds != null && series.MalIds.Any())
                {
                    title = _animeOfflineTitleRepository.FindByMalId(series.MalIds.First());
                }

                if (title == null && !string.IsNullOrWhiteSpace(series.CleanTitle))
                {
                    title = _animeOfflineTitleRepository.FindSearchMatches(series.CleanTitle, "anidb")?.FirstOrDefault();
                }

                if (title != null && !string.IsNullOrWhiteSpace(title.PictureUrl))
                {
                    if (poster == null)
                    {
                        poster = new MediaCover(MediaCoverTypes.Poster, title.PictureUrl);
                        series.Images.Add(poster);
                    }
                    else
                    {
                        poster.RemoteUrl = title.PictureUrl;
                    }
                }
            }

            var updated = false;
            var toResize = new List<Tuple<MediaCover, bool>>();

            foreach (var cover in series.Images)
            {
                if (cover.CoverType == MediaCoverTypes.Unknown)
                {
                    continue;
                }

                var fileName = GetCoverPath(series.Id, cover.CoverType);
                var alreadyExists = false;

                try
                {
                    alreadyExists = _coverExistsSpecification.AlreadyExists(cover.RemoteUrl, fileName);

                    if (!alreadyExists)
                    {
                        DownloadCover(series, cover);
                        updated = true;
                    }
                }
                catch (HttpException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", series, e.Message);
                }
                catch (WebException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", series, e.Message);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't download media cover for {0}", series);
                }

                toResize.Add(Tuple.Create(cover, alreadyExists));
            }

            try
            {
                _semaphore.Wait();

                foreach (var tuple in toResize)
                {
                    EnsureResizedCovers(series, tuple.Item1, !tuple.Item2);
                }
            }
            finally
            {
                _semaphore.Release();
            }

            return updated;
        }

        public string EnsureCover(int seriesId, MediaCoverTypes coverType, int? height = null)
        {
            var mainFileName = GetCoverPath(seriesId, coverType);
            var requestedFileName = height.HasValue ? GetCoverPath(seriesId, coverType, height.Value) : mainFileName;

            if (_diskProvider.FileExists(requestedFileName) && _diskProvider.GetFileSize(requestedFileName) > 0)
            {
                return requestedFileName;
            }

            if (_diskProvider.FileExists(mainFileName) && _diskProvider.GetFileSize(mainFileName) > 0)
            {
                if (height.HasValue)
                {
                    try
                    {
                        _resizer.Resize(mainFileName, requestedFileName, height.Value);
                        if (_diskProvider.FileExists(requestedFileName) && _diskProvider.GetFileSize(requestedFileName) > 0)
                        {
                            return requestedFileName;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Couldn't resize cover {0} to {1}", mainFileName, requestedFileName);
                    }
                }

                return mainFileName;
            }

            if (_seriesRepository != null)
            {
                var series = _seriesRepository.Get(seriesId);
                if (series != null)
                {
                    EnsureCovers(series);

                    if (_diskProvider.FileExists(requestedFileName) && _diskProvider.GetFileSize(requestedFileName) > 0)
                    {
                        return requestedFileName;
                    }

                    if (_diskProvider.FileExists(mainFileName) && _diskProvider.GetFileSize(mainFileName) > 0)
                    {
                        return mainFileName;
                    }
                }
            }

            return null;
        }

        private void DownloadCover(Series series, MediaCover cover)
        {
            var fileName = GetCoverPath(series.Id, cover.CoverType);

            var targetFolder = Path.GetDirectoryName(fileName);
            if (!_diskProvider.FolderExists(targetFolder))
            {
                _diskProvider.CreateFolder(targetFolder);
            }

            if (!cover.RemoteUrl.IsNullOrWhiteSpace())
            {
                var hash = cover.RemoteUrl.SHA256Hash();
                var cacheFolder = Path.Combine(_coverRootFolder, "ProxyCache");
                var cacheFile = Path.Combine(cacheFolder, $"{hash}.bin");

                if (_diskProvider.FileExists(cacheFile) && _diskProvider.GetFileSize(cacheFile) > 0)
                {
                    _logger.Debug("Copying {0} for {1} from ProxyCache", cover.CoverType, series);
                    _diskProvider.CopyFile(cacheFile, fileName, true);
                    return;
                }
            }

            _logger.Info("Downloading {0} for {1} {2}", cover.CoverType, series, cover.RemoteUrl);
            _httpClient.DownloadFile(cover.RemoteUrl, fileName);
        }

        private void EnsureResizedCovers(Series series, MediaCover cover, bool forceResize)
        {
            int[] heights;

            switch (cover.CoverType)
            {
                default:
                    return;

                case MediaCoverTypes.Poster:
                case MediaCoverTypes.Headshot:
                    heights = new[] { 500, 250 };
                    break;

                case MediaCoverTypes.Banner:
                    heights = new[] { 70, 35 };
                    break;

                case MediaCoverTypes.Fanart:
                case MediaCoverTypes.Screenshot:
                    heights = new[] { 360, 180 };
                    break;
            }

            foreach (var height in heights)
            {
                var mainFileName = GetCoverPath(series.Id, cover.CoverType);
                var resizeFileName = GetCoverPath(series.Id, cover.CoverType, height);

                if (forceResize || !_diskProvider.FileExists(resizeFileName) || _diskProvider.GetFileSize(resizeFileName) == 0)
                {
                    _logger.Debug("Resizing {0}-{1} for {2}", cover.CoverType, height, series);

                    try
                    {
                        _resizer.Resize(mainFileName, resizeFileName, height);
                    }
                    catch
                    {
                        _logger.Debug("Couldn't resize media cover {0}-{1} for {2}, using full size image instead.", cover.CoverType, height, series);
                    }
                }
            }
        }

        private string GetExtension(MediaCoverTypes coverType)
        {
            switch (coverType)
            {
                default:
                    return ".jpg";

                case MediaCoverTypes.Clearlogo:
                    return ".png";
            }
        }

        public void HandleAsync(SeriesAddedEvent message)
        {
            var updated = EnsureCovers(message.Series);

            _eventAggregator.PublishEvent(new MediaCoversUpdatedEvent(message.Series, updated));
        }

        public void HandleAsync(SeriesUpdatedEvent message)
        {
            var updated = EnsureCovers(message.Series);

            _eventAggregator.PublishEvent(new MediaCoversUpdatedEvent(message.Series, updated));
        }

        public void HandleAsync(SeriesDeletedEvent message)
        {
            foreach (var series in message.Series)
            {
                var path = GetSeriesCoverPath(series.Id);
                if (_diskProvider.FolderExists(path))
                {
                    _diskProvider.DeleteFolder(path, true);
                }
            }
        }
    }
}
