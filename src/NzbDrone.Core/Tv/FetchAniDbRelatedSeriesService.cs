using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Tv.Commands;

namespace NzbDrone.Core.Tv
{
    public class FetchAniDbRelatedSeriesService : IExecute<FetchAniDbRelatedSeriesCommand>
    {
        private const string AniDbApiBase = "http://api.anidb.net:9001/httpapi";

        private readonly ISeriesService _seriesService;
        private readonly IAniDbSeriesMappingService _mappingService;
        private readonly IAniDbRelatedSeriesService _relatedSeriesService;
        private readonly IAniDbRelatedMetadataCacheRepository _cacheRepository;
        private readonly IConfigFileProvider _configService;
        private readonly IHttpClient _httpClient;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IAniDbRateLimiter _rateLimiter;
        private readonly Logger _logger;

        public FetchAniDbRelatedSeriesService(
            ISeriesService seriesService,
            IAniDbSeriesMappingService mappingService,
            IAniDbRelatedSeriesService relatedSeriesService,
            IAniDbRelatedMetadataCacheRepository cacheRepository,
            IConfigFileProvider configService,
            IHttpClient httpClient,
            IAppFolderInfo appFolderInfo,
            IAniDbRateLimiter rateLimiter,
            Logger logger)
        {
            _seriesService = seriesService;
            _mappingService = mappingService;
            _relatedSeriesService = relatedSeriesService;
            _cacheRepository = cacheRepository;
            _configService = configService;
            _httpClient = httpClient;
            _appFolderInfo = appFolderInfo;
            _rateLimiter = rateLimiter;
            _logger = logger;
        }

        public void Execute(FetchAniDbRelatedSeriesCommand message)
        {
            if (!_configService.IsRelatedSeriesEnabled)
            {
                return;
            }

            var series = _seriesService.GetSeries(message.SeriesId);
            if (series == null)
            {
                return;
            }

            var mappings = _mappingService.GetMappingsForSeries(series.Id);
            var hubIds = new HashSet<int>(mappings.Select(m => m.AniDbId));

            var related = _relatedSeriesService.GetRelatedSeries(series.Id);
            var queue = new Queue<(int Id, int Depth)>();

            foreach (var r in related)
            {
                queue.Enqueue((r.RelatedAniDbId, 1));
            }

            var visited = new HashSet<int>(hubIds);
            var newRelationsFound = false;

            while (queue.Count > 0)
            {
                if (!_configService.IsRelatedSeriesEnabled)
                {
                    _logger.Info("Related series fetching disabled mid-flight. Stopping.");
                    break;
                }

                var current = queue.Dequeue();

                if (current.Depth > 10)
                {
                    _logger.Debug("Hit related series depth cap of 10 hops for AniDB ID {0}. Stopping traversal on this branch.", current.Id);
                    continue;
                }

                if (!visited.Add(current.Id))
                {
                    continue;
                }

                XDocument doc;
                try
                {
                    doc = GetAnimeXml(current.Id);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to fetch AniDB XML for related series ID {0}. Skipping.", current.Id);
                    continue;
                }

                ParseAndCacheMetadata(doc, current.Id);

                var allRelations = GetAllRelations(doc);
                foreach (var relation in allRelations)
                {
                    if (!hubIds.Contains(relation.Id))
                    {
                        if (!related.Any(r => r.RelatedAniDbId == relation.Id))
                        {
                            related.Add(new AniDbRelatedSeries
                            {
                                SeriesId = series.Id,
                                RelatedAniDbId = relation.Id,
                                RelationType = relation.RelationType
                            });
                            newRelationsFound = true;
                        }

                        if (!visited.Contains(relation.Id))
                        {
                            queue.Enqueue((relation.Id, current.Depth + 1));
                        }
                    }
                }
            }

            if (newRelationsFound)
            {
                _relatedSeriesService.UpdateRelatedSeries(series.Id, related);
            }
        }

        private XDocument GetAnimeXml(int id)
        {
            var xml = FetchXml("anime", $"aid={id}");
            var doc = XDocument.Parse(xml);

            if (doc.Root?.Name.LocalName == "error")
            {
                if (doc.Root.Value.ToLowerInvariant().Contains("banned"))
                {
                    _configService.SetAniDbBanExpiration(DateTime.UtcNow.AddHours(24));
                }

                throw new Exception($"AniDB error for ID {id}: {doc.Root.Value}");
            }

            return doc;
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

            return _rateLimiter.ExecuteAsync(() =>
            {
                var httpRequest = new HttpRequest(url);
                var response = _httpClient.Execute(httpRequest);

                if (!response.Content.Contains("<error"))
                {
                    File.WriteAllText(cacheFile, response.Content);
                }

                return response.Content;
            }).GetAwaiter().GetResult();
        }

        private void ParseAndCacheMetadata(XDocument doc, int aniDbId)
        {
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var titleElements = doc.Root?.Elements(ns + "titles").Elements(ns + "title");
            var title = GetBestTitle(titleElements, $"AniDB {aniDbId}");

            var description = doc.Root?.Element(ns + "description")?.Value;
            if (!string.IsNullOrWhiteSpace(description))
            {
                // Basic cleanup
                description = System.Text.RegularExpressions.Regex.Replace(description, @"https?://anidb\.net/[^\s\[]+\s*\[(.*?)\]", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            var posterUrl = doc.Root?.Element(ns + "picture")?.Value;
            if (!string.IsNullOrWhiteSpace(posterUrl))
            {
                posterUrl = $"https://cdn.anidb.net/images/main/{posterUrl}";
            }

            var existing = _cacheRepository.GetByAniDbId(aniDbId);
            if (existing != null)
            {
                existing.Title = title;
                existing.PosterUrl = posterUrl;
                existing.Overview = description;
                _cacheRepository.Update(existing);
            }
            else
            {
                _cacheRepository.Insert(new AniDbRelatedMetadataCache
                {
                    AniDbId = aniDbId,
                    Title = title,
                    PosterUrl = posterUrl,
                    Overview = description
                });
            }
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

        private List<(int Id, string RelationType)> GetAllRelations(XDocument doc)
        {
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var related = doc.Root?.Element(ns + "relatedanime");
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
    }
}
