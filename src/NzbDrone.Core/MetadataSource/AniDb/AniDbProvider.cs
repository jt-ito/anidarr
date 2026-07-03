using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    public class AniDbProvider : IMetadataProvider
    {
        private const string AniDbApiBase = "http://api.anidb.net:9001/httpapi";
        private static readonly Regex AniDbLinkRegex = new Regex(@"https?://anidb\.net/[^\s\[]+\s*\[(.*?)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IHttpClient _httpClient;
        private readonly IConfigFileProvider _configService;
        private readonly IAnimeOfflineDatabase _titleSearch;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;

        private static readonly SemaphoreSlim _rateSemaphore = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(2);
        private static DateTime _lastRequestTime = DateTime.MinValue;

        public MetadataProviderType ProviderType => MetadataProviderType.AniDb;

        public AniDbProvider(IHttpClient httpClient, IConfigFileProvider configService, IAnimeOfflineDatabase titleSearch, IAppFolderInfo appFolderInfo, Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _titleSearch = titleSearch;
            _appFolderInfo = appFolderInfo;
            _logger = logger;
        }

        public bool CanHandleId(string externalIdKey) =>
            externalIdKey is "anidb" or "anidbid";

        public Tuple<Series, List<Episode>> GetSeriesInfo(string externalId)
        {
            if (!int.TryParse(externalId, out var aniDbId) || aniDbId <= 0)
            {
                throw new ArgumentException($"Invalid AniDB ID: {externalId}");
            }

            var xml = FetchXml("anime", $"aid={aniDbId}");
            var doc = XDocument.Parse(xml);

            if (doc.Root?.Name.LocalName == "error")
            {
                if (doc.Root.Value.ToLowerInvariant().Contains("banned"))
                {
                    _configService.SetAniDbBanExpiration(DateTime.UtcNow.AddHours(24));
                }

                throw new Exception($"AniDB error for ID {aniDbId}: {doc.Root.Value}");
            }

            _configService.SetAniDbBanExpiration(null);

            var series = MapSeries(doc.Root, aniDbId);
            var episodes = MapEpisodes(doc.Root);

            series.Seasons = episodes.Select(e => e.SeasonNumber)
                .Distinct()
                .Select(s => new Season { SeasonNumber = s, Monitored = s > 0 })
                .ToList();

            return Tuple.Create(series, episodes);
        }

        public List<Series> Search(string query)
        {
            var lower = query.ToLowerInvariant();
            if (lower.StartsWith("anidb:"))
            {
                var slug = lower.Split(':')[1].Trim();
                if (int.TryParse(slug, out var id) && id > 0)
                {
                    // ponytail: resolve from local DB — never hit the AniDB HTTP API during search.
                    // The full API call happens only when the user actually adds the series.
                    var local = _titleSearch.GetSeriesById("anidb", id);
                    if (local != null)
                    {
                        var title = local.Title ?? $"AniDB {id}";
                        var series = new Series
                        {
                            Title = title,
                            CleanTitle = title.CleanSeriesTitle(),
                            SortTitle = SeriesTitleNormalizer.Normalize(title, id),
                            TitleSlug = $"{title.ToUrlSlug()}-anidb-{id}",
                            AniDbId = id,
                            PrimaryMetadataProvider = "anidb",
                            SeriesType = SeriesTypes.Anime,
                            Status = local.Status ?? SeriesStatusType.Continuing,
                            Year = local.Year ?? 0,
                            Genres = local.Genres ?? new List<string>(),
                            Overview = local.Overview,
                            Monitored = true
                        };

                        if (!string.IsNullOrWhiteSpace(local.PictureUrl))
                        {
                            series.Images = new List<MediaCover.MediaCover>
                            {
                                new MediaCover.MediaCover(MediaCoverTypes.Poster, local.PictureUrl)
                            };
                        }

                        return new List<Series> { series };
                    }

                    return new List<Series>();
                }

                return new List<Series>();
            }

            try
            {
                return _titleSearch.Search(query, "anidb");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "AniDB title search failed for query: {0}", query);
                return new List<Series>();
            }
        }

        private string FetchXml(string request, string extraParams)
        {
            var clientName = _configService.AniDbClientName;
            var clientVersion = _configService.AniDbClientVersion;
            var url = $"{AniDbApiBase}?request={request}&client={clientName}&clientver={clientVersion}&protover=1&{extraParams}";

            var cacheDir = Path.Combine(_appFolderInfo.AppDataFolder, "AniDbCache");
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            var safeParams = new string(extraParams.Where(char.IsLetterOrDigit).ToArray());
            var cacheFile = Path.Combine(cacheDir, $"{request}_{safeParams}.xml");

            if (File.Exists(cacheFile))
            {
                var lastModified = File.GetLastWriteTimeUtc(cacheFile);
                if (lastModified > DateTime.UtcNow.AddHours(-24))
                {
                    _logger.Debug("Using cached AniDB response for {0} {1}", request, extraParams);
                    return File.ReadAllText(cacheFile);
                }
            }

            ThrottleRequest();
            var httpRequest = new HttpRequest(url);
            var response = _httpClient.Execute(httpRequest);

            if (!response.Content.Contains("<error"))
            {
                File.WriteAllText(cacheFile, response.Content);
            }

            return response.Content;
        }

        private void ThrottleRequest()
        {
            _rateSemaphore.Wait();
            try
            {
                var elapsed = DateTime.UtcNow - _lastRequestTime;
                if (elapsed < MinRequestInterval)
                {
                    var delay = MinRequestInterval - elapsed;
                    _logger.Debug("AniDB rate limit: sleeping {0}ms", delay.TotalMilliseconds);
                    Thread.Sleep(delay);
                }

                _lastRequestTime = DateTime.UtcNow;
            }
            finally
            {
                _rateSemaphore.Release();
            }
        }

        private static Series MapSeries(XElement root, int aniDbId)
        {
            var ns = root?.Name.Namespace ?? XNamespace.None;

            var title = GetBestTitle(root?.Elements(ns + "titles").Elements(ns + "title"), "Unknown");

            var series = new Series
            {
                Title = title,
                CleanTitle = title.CleanSeriesTitle(),
                SortTitle = SeriesTitleNormalizer.Normalize(title, aniDbId),
                TitleSlug = $"{title.ToUrlSlug()}-anidb-{aniDbId}",
                AniDbId = aniDbId,
                Overview = CleanDescription(root?.Element(ns + "description")?.Value),
                Runtime = int.TryParse(root?.Element(ns + "episodelength")?.Value, out var rt) ? rt : 24,
                OriginalLanguage = Language.Japanese,
                SeriesType = SeriesTypes.Anime,
                PrimaryMetadataProvider = "anidb",
                Monitored = true,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            var startDate = root?.Element(ns + "startdate")?.Value;
            if (startDate.IsNotNullOrWhiteSpace() && DateTime.TryParse(startDate, out var firstAired))
            {
                series.FirstAired = firstAired.ToUniversalTime();
                series.Year = firstAired.Year;
            }

            var endDate = root?.Element(ns + "enddate")?.Value;
            if (endDate.IsNotNullOrWhiteSpace())
            {
                series.Status = SeriesStatusType.Ended;
            }
            else
            {
                series.Status = SeriesStatusType.Continuing;
            }

            var posterUrl = root?.Element(ns + "picture")?.Value;
            if (posterUrl.IsNotNullOrWhiteSpace())
            {
                series.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover(MediaCoverTypes.Poster, $"https://cdn.anidb.net/images/main/{posterUrl}")
                };
            }

            return series;
        }

        private static List<Episode> MapEpisodes(XElement root)
        {
            var episodes = new List<Episode>();
            var ns = root?.Name.Namespace ?? XNamespace.None;

            foreach (var ep in root?.Elements(ns + "episodes").Elements(ns + "episode") ?? Enumerable.Empty<XElement>())
            {
                var epno = ep.Element(ns + "epno")?.Value ?? string.Empty;
                var type = (string)ep.Element(ns + "epno")?.Attribute("type") ?? "1";

                if (!int.TryParse(epno.TrimStart('S', 'C', 'T', 'P', 'O'), out var epNum))
                {
                    continue;
                }

                var titleEn = GetBestTitle(ep.Elements(ns + "title"), $"Episode {epNum}");

                var episode = new Episode
                {
                    SeasonNumber = type == "2" ? 0 : 1,
                    EpisodeNumber = epNum,
                    AbsoluteEpisodeNumber = type == "1" ? epNum : (int?)null,
                    Title = titleEn,
                    Overview = CleanDescription(ep.Element(ns + "summary")?.Value),
                    Runtime = int.TryParse(ep.Element(ns + "length")?.Value, out var epRt) ? epRt : 0,
                    Monitored = type == "1"
                };

                var airDate = ep.Element(ns + "airdate")?.Value;
                if (airDate.IsNotNullOrWhiteSpace() && DateTime.TryParse(airDate, out var aired))
                {
                    episode.AirDateUtc = aired.ToUniversalTime();
                    episode.AirDate = aired.ToString("yyyy-MM-dd");
                }

                episodes.Add(episode);
            }

            return episodes;
        }

        private static string CleanDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return description;
            }

            return AniDbLinkRegex.Replace(description, "$1");
        }

        private static string GetBestTitle(IEnumerable<XElement> titles, string defaultTitle)
        {
            if (titles == null || !titles.Any())
            {
                return defaultTitle;
            }

            var enTitle = titles.FirstOrDefault(t => (string)t.Attribute(XNamespace.Xml + "lang") == "en" || (string)t.Attribute("lang") == "en")?.Value;
            if (!string.IsNullOrWhiteSpace(enTitle))
            {
                return enTitle;
            }

            var xjatTitle = titles.FirstOrDefault(t => (string)t.Attribute(XNamespace.Xml + "lang") == "x-jat" || (string)t.Attribute("lang") == "x-jat")?.Value;
            if (!string.IsNullOrWhiteSpace(xjatTitle))
            {
                return xjatTitle;
            }

            var jaTitle = titles.FirstOrDefault(t => (string)t.Attribute(XNamespace.Xml + "lang") == "ja" || (string)t.Attribute("lang") == "ja")?.Value;
            if (!string.IsNullOrWhiteSpace(jaTitle))
            {
                return jaTitle;
            }

            return titles.FirstOrDefault()?.Value ?? defaultTitle;
        }
    }
}
