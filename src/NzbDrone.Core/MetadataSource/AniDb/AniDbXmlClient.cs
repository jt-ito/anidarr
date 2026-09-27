using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    // Anidarr: shared AniDB XML fetch/cache/parse used by both AniDbProvider (the
    // primary add/refresh metadata path) and FetchAniDbRelatedSeriesService (the
    // background related-series crawler). These used to be two independently
    // maintained copies that had already drifted — this one is the more robust of
    // the two (in-flight request coalescing, atomic cache writes, cache-hit content
    // revalidation, and the more refined title-selection logic) — and having one
    // shared instance means the two features now genuinely coordinate in-flight
    // requests/cache writes instead of racing on the same cache file.
    public interface IAniDbXmlClient
    {
        // reportProgress is invoked (only) at the point a fetch is actually about
        // to wait on the AniDB rate limiter, so a caller with a progress UI (e.g.
        // AniDbProvider during an add) can surface that wait instead of the whole
        // operation looking stuck. Callers that don't have anywhere to put that
        // (e.g. the background related-series crawler) just omit it.
        XDocument GetAnimeXml(int aniDbId, Action<string> reportProgress = null);
        List<(int Id, string RelationType)> GetAllRelations(XElement root);
        string GetBestTitle(IEnumerable<XElement> titles, string defaultTitle);
    }

    public class AniDbXmlClient : IAniDbXmlClient
    {
        private const string AniDbApiBase = "http://api.anidb.net:9001/httpapi";

        private static readonly ConcurrentDictionary<string, Task<string>> _inFlightFetches = new ConcurrentDictionary<string, Task<string>>();

        public static void ClearCache()
        {
            _inFlightFetches.Clear();
        }

        private readonly IHttpClient _httpClient;
        private readonly IConfigFileProvider _configService;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IAniDbRateLimiter _rateLimiter;
        private readonly Logger _logger;

        public AniDbXmlClient(IHttpClient httpClient,
                              IConfigFileProvider configService,
                              IAppFolderInfo appFolderInfo,
                              IAniDbRateLimiter rateLimiter,
                              Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _appFolderInfo = appFolderInfo;
            _rateLimiter = rateLimiter;
            _logger = logger;
        }

        public XDocument GetAnimeXml(int aniDbId, Action<string> reportProgress = null)
        {
            var xml = FetchXml("anime", $"aid={aniDbId}", reportProgress);
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
            return doc;
        }

        private string FetchXml(string request, string extraParams, Action<string> reportProgress = null)
        {
            var clientName = _configService.AniDbClientName;
            var clientVersion = _configService.AniDbClientVersion;
            var url = $"{AniDbApiBase}?request={request}&client={clientName}&clientver={clientVersion}&protover=1&{extraParams}";

            var cacheDir = Path.Combine(_appFolderInfo.AppDataFolder, "AniDbCache");
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            // Keep the alnum prefix for human-readable cache dir browsing, but key
            // on a hash of the full extraParams too — stripping punctuation alone
            // can collapse two different queries onto the same cache file.
            var safeParams = new string(extraParams.Where(char.IsLetterOrDigit).ToArray());
            var paramsHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(extraParams)))[..12];
            var cacheFile = Path.Combine(cacheDir, $"{request}_{safeParams}_{paramsHash}.xml");

            if (File.Exists(cacheFile) && new FileInfo(cacheFile).Length > 0)
            {
                try
                {
                    var cached = File.ReadAllText(cacheFile);
                    if (!cached.Contains("<error"))
                    {
                        _logger.Debug("Using cached AniDB response for {0} {1}", request, extraParams);
                        return cached;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to read cached AniDB response for {0} {1}", request, extraParams);
                }
            }

            Task<string> fetchTask;
            lock (_inFlightFetches)
            {
                if (!_inFlightFetches.TryGetValue(cacheFile, out fetchTask))
                {
                    fetchTask = _rateLimiter.ExecuteAsync(() =>
                    {
                        reportProgress?.Invoke("Respecting AniDB rate limit (waiting 2s)...");

                        if (File.Exists(cacheFile) && new FileInfo(cacheFile).Length > 0)
                        {
                            try
                            {
                                var cached = File.ReadAllText(cacheFile);
                                if (!cached.Contains("<error"))
                                {
                                    return cached;
                                }
                            }
                            catch (Exception)
                            {
                                // Ignore concurrent read error and proceed to download
                            }
                        }

                        var httpRequest = new HttpRequest(url);
                        var response = _httpClient.Execute(httpRequest);

                        if (!response.Content.Contains("<error"))
                        {
                            try
                            {
                                var tempFile = $"{cacheFile}.{Guid.NewGuid():N}.tmp";
                                File.WriteAllText(tempFile, response.Content);
                                File.Move(tempFile, cacheFile, overwrite: true);
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug(ex, "Failed to write AniDB cache file {0}", cacheFile);
                            }
                        }

                        return response.Content;
                    });

                    _inFlightFetches[cacheFile] = fetchTask;
                }
            }

            try
            {
                return fetchTask.GetAwaiter().GetResult();
            }
            finally
            {
                lock (_inFlightFetches)
                {
                    _inFlightFetches.TryRemove(cacheFile, out _);
                }
            }
        }

        public List<(int Id, string RelationType)> GetAllRelations(XElement root)
        {
            var ns = root?.Name.Namespace ?? XNamespace.None;
            var related = root?.Element(ns + "relatedanime");
            if (related == null)
            {
                return new List<(int, string)>();
            }

            var results = new List<(int, string)>();
            foreach (var anime in related.Elements(ns + "anime"))
            {
                var type = (string)anime.Attribute("type");
                var idStr = (string)anime.Attribute("id");
                if (int.TryParse(idStr, out var id) && id > 0 && !string.IsNullOrWhiteSpace(type))
                {
                    results.Add((id, type));
                }
            }

            return results;
        }

        public string GetBestTitle(IEnumerable<XElement> titles, string defaultTitle)
        {
            if (titles == null || !titles.Any())
            {
                return defaultTitle;
            }

            string GetLang(XElement t) => ((string)t.Attribute(XNamespace.Xml + "lang") ?? (string)t.Attribute("lang"))?.Trim();
            string GetType(XElement t) => ((string)t.Attribute("type"))?.Trim();

            // Exclude 'short' titles (abbreviations such as 'ark', 'EVA', 'SAO') from primary title selection
            var nonShortTitles = titles.Where(t => !string.Equals(GetType(t), "short", StringComparison.OrdinalIgnoreCase)).ToList();
            var candidatePool = nonShortTitles.Any() ? nonShortTitles : titles.ToList();

            // 1. Official English title (e.g. "Animation Runner Kuromi")
            var officialEn = candidatePool.FirstOrDefault(t =>
                string.Equals(GetType(t), "official", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetLang(t), "en", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(officialEn))
            {
                return officialEn;
            }

            // 2. Any non-short English title
            var anyEn = candidatePool.FirstOrDefault(t =>
                string.Equals(GetLang(t), "en", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(anyEn))
            {
                return anyEn;
            }

            // 3. Main title
            var mainTitle = candidatePool.FirstOrDefault(t =>
                string.Equals(GetType(t), "main", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(mainTitle))
            {
                return mainTitle;
            }

            // 4. x-jat (Romaji) title
            var xjatTitle = candidatePool.FirstOrDefault(t =>
                string.Equals(GetLang(t), "x-jat", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(xjatTitle))
            {
                return xjatTitle;
            }

            // 5. Japanese title
            var jaTitle = candidatePool.FirstOrDefault(t =>
                string.Equals(GetLang(t), "ja", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(jaTitle))
            {
                return jaTitle;
            }

            return candidatePool.FirstOrDefault()?.Value?.Trim() ?? defaultTitle;
        }
    }
}
