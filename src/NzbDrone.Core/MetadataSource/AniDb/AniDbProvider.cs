using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
        private readonly IAniDbRateLimiter _rateLimiter;
        private readonly IAniDbSeriesMappingService _mappingService;
        private readonly AniList.IAniListEnricher _aniListEnricher;

        public MetadataProviderType ProviderType => MetadataProviderType.AniDb;

        public AniDbProvider(IHttpClient httpClient, IConfigFileProvider configService, IAnimeOfflineDatabase titleSearch, IAppFolderInfo appFolderInfo, IAniDbRateLimiter rateLimiter, Logger logger, IAniDbSeriesMappingService mappingService, AniList.IAniListEnricher aniListEnricher)
        {
            _httpClient = httpClient;
            _configService = configService;
            _titleSearch = titleSearch;
            _appFolderInfo = appFolderInfo;
            _rateLimiter = rateLimiter;
            _logger = logger;
            _mappingService = mappingService;
            _aniListEnricher = aniListEnricher;
        }

        public bool CanHandleId(string externalIdKey) =>
            externalIdKey is "anidb" or "anidbid";

        public Tuple<Series, List<Episode>> GetSeriesInfo(string externalId)
        {
            if (!int.TryParse(externalId, out var aniDbId) || aniDbId <= 0)
            {
                throw new ArgumentException($"Invalid AniDB ID: {externalId}");
            }

            var hubId = FindHubId(aniDbId);
            var chainIds = GetLinearChain(hubId);

            Series hubSeries = null;
            var allEpisodes = new List<Episode>();
            var mappings = new List<AniDbSeriesMapping>();
            var relatedSeries = new List<AniDbRelatedSeries>();
            var hubChainIds = new HashSet<int>(chainIds);
            var seenRelations = new HashSet<int>();
            var seasonMetadata = new Dictionary<int, (string Title, List<MediaCover.MediaCover> Images)>();
            var seasonNumber = 1;
            var absoluteEpisodeOffset = 0;
            var specialEpisodeCounter = 1;

            var chainData = new List<(int AssignedSeasonNumber, List<Episode> Episodes, int? AniListId)>();

            foreach (var id in chainIds)
            {
                XDocument doc;
                try
                {
                    doc = GetAnimeXml(id);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "AniDB relation traversal hit an unavailable entry at ID {0} while parsing series. Skipping.", id);
                    continue;
                }

                var currentSeriesMetadata = MapSeries(doc.Root, id);
                if (hubSeries == null)
                {
                    hubSeries = currentSeriesMetadata;
                }

                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                var animeType = doc.Root?.Element(ns + "type")?.Value;

                var existingMapping = _mappingService.GetMappingByAniDbId(id);
                int assignedSeasonNumber;

                if (existingMapping != null)
                {
                    assignedSeasonNumber = existingMapping.SeasonNumber;

                    if (assignedSeasonNumber > 0 && assignedSeasonNumber >= seasonNumber)
                    {
                        seasonNumber = assignedSeasonNumber + 1; // update counter to prevent collisions
                    }
                }
                else
                {
                    var hasQualifyingHubRelation = id != hubId;
                    var isAmbiguousHubRelation = false;

                    if (!hasQualifyingHubRelation && GetRelations(doc, "Prequel").Any())
                    {
                        isAmbiguousHubRelation = true;
                    }

                    if (string.IsNullOrWhiteSpace(animeType) || animeType.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        assignedSeasonNumber = -1; // Flag for manual review
                    }
                    else if (isAmbiguousHubRelation)
                    {
                        assignedSeasonNumber = -1; // Flag for manual review
                    }
                    else if (animeType.Equals("TV Series", StringComparison.OrdinalIgnoreCase) || animeType.Equals("Web", StringComparison.OrdinalIgnoreCase))
                    {
                        assignedSeasonNumber = seasonNumber;
                        seasonNumber++;
                    }
                    else
                    {
                        // OVA, Movie, Special, Music Video, etc.
                        if (hasQualifyingHubRelation)
                        {
                            assignedSeasonNumber = 0;
                        }
                        else
                        {
                            assignedSeasonNumber = seasonNumber;
                            seasonNumber++;
                        }
                    }
                }

                mappings.Add(new AniDbSeriesMapping
                {
                    AniDbId = id,
                    SeasonNumber = assignedSeasonNumber,
                    RelationType = id == hubId ? "Hub" : "Auto-Sequel"
                });

                if (_configService.IsRelatedSeriesEnabled)
                {
                    var allRelations = GetAllRelations(doc.Root);
                    foreach (var relation in allRelations)
                    {
                        if (!hubChainIds.Contains(relation.Id) && seenRelations.Add(relation.Id))
                        {
                            relatedSeries.Add(new AniDbRelatedSeries
                            {
                                RelatedAniDbId = relation.Id,
                                RelationType = relation.RelationType
                            });
                        }
                    }
                }

                if (assignedSeasonNumber > 0)
                {
                    seasonMetadata[assignedSeasonNumber] = (currentSeriesMetadata.Title, currentSeriesMetadata.Images);
                }

                if (assignedSeasonNumber != -1)
                {
                    var episodes = MapEpisodes(doc.Root);

                    int? currentAniListId = null;
                    try
                    {
                        var local = _titleSearch.GetSeriesById("anidb", id);
                        if (local != null && local.AniListId.HasValue)
                        {
                            currentAniListId = local.AniListId.Value;
                            if (hubSeries.AniListIds == null)
                            {
                                hubSeries.AniListIds = new HashSet<int>();
                            }

                            hubSeries.AniListIds.Add(local.AniListId.Value);
                        }
                        else
                        {
                            var expectedYear = currentSeriesMetadata.Year > 0 ? currentSeriesMetadata.Year : (episodes.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.AirDate))?.AirDateUtc?.Year ?? 0);
                            if (expectedYear > 0)
                            {
                                var expectedEpisodeCount = episodes.Count(e => e.SeasonNumber > 0);
                                var fallbackTitles = new List<string>();

                                if (local != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(local.RomajiTitle))
                                    {
                                        fallbackTitles.Add(local.RomajiTitle);
                                    }

                                    if (!string.IsNullOrWhiteSpace(local.NativeTitle))
                                    {
                                        fallbackTitles.Add(local.NativeTitle);
                                    }

                                    if (!string.IsNullOrWhiteSpace(local.EnglishTitle))
                                    {
                                        fallbackTitles.Add(local.EnglishTitle);
                                    }

                                    if (local.SearchSynonyms != null)
                                    {
                                        fallbackTitles.AddRange(local.SearchSynonyms);
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(currentSeriesMetadata.Title) && !fallbackTitles.Contains(currentSeriesMetadata.Title))
                                {
                                    fallbackTitles.Add(currentSeriesMetadata.Title);
                                }

                                foreach (var titleToSearch in fallbackTitles)
                                {
                                    _logger.Debug("No offline database mapping found for AniDB ID {0}. Attempting title-based fallback for '{1}'.", id, titleToSearch);
                                    currentAniListId = _aniListEnricher.SearchAniListIdByTitle(titleToSearch, expectedYear, expectedEpisodeCount > 0 ? expectedEpisodeCount : (int?)null);
                                    if (currentAniListId.HasValue)
                                    {
                                        break;
                                    }
                                }

                                if (currentAniListId.HasValue)
                                {
                                    if (hubSeries.AniListIds == null)
                                    {
                                        hubSeries.AniListIds = new HashSet<int>();
                                    }

                                    hubSeries.AniListIds.Add(currentAniListId.Value);
                                    _titleSearch.UpdateAniListId(id, currentAniListId.Value);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to resolve AniList ID for AniDB ID {0}", id);
                    }

                    chainData.Add((assignedSeasonNumber, episodes, currentAniListId));
                }
            }

            var allAniListIds = chainData.Where(x => x.AniListId.HasValue).Select(x => x.AniListId.Value).ToList();
            var allAiringTimes = new Dictionary<int, Dictionary<int, TimeSpan>>();

            if (allAniListIds.Any())
            {
                try
                {
                    _logger.Debug("Batch fetching time-of-day data for {0} AniList IDs", allAniListIds.Count);
                    allAiringTimes = _aniListEnricher.GetAiringTimesForMultiple(allAniListIds);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to batch fetch AniList airing times.");
                }
            }

            TimeSpan? globalDefaultTime = null;
            var allTimes = allAiringTimes.Values.SelectMany(x => x.Values).ToList();
            if (allTimes.Any())
            {
                globalDefaultTime = allTimes.GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
            }

            foreach (var data in chainData)
            {
                var assignedSeasonNumber = data.AssignedSeasonNumber;
                var episodes = data.Episodes;
                var currentAniListId = data.AniListId;

                var airingTimes = new Dictionary<int, TimeSpan>();
                TimeSpan? seasonDefaultTime = null;

                if (currentAniListId.HasValue && allAiringTimes.TryGetValue(currentAniListId.Value, out var times) && times.Any())
                {
                    airingTimes = times;
                    seasonDefaultTime = times.Values.GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
                }

                var maxEpisodeNumber = 0;
                foreach (var ep in episodes)
                {
                    if (ep.SeasonNumber == 1)
                    {
                        ep.SeasonNumber = assignedSeasonNumber;
                        if (assignedSeasonNumber > 0)
                        {
                            ep.AbsoluteEpisodeNumber = absoluteEpisodeOffset + ep.EpisodeNumber;
                            maxEpisodeNumber = Math.Max(maxEpisodeNumber, ep.EpisodeNumber);
                        }
                        else
                        {
                            ep.AbsoluteEpisodeNumber = null; // Specials shouldn't have absolute numbers
                            ep.EpisodeNumber = specialEpisodeCounter++;
                        }
                    }
                    else if (ep.SeasonNumber == 0)
                    {
                        ep.SeasonNumber = 0;
                        ep.EpisodeNumber = specialEpisodeCounter++;
                        ep.AbsoluteEpisodeNumber = null;
                    }

                    var timeOfDay = default(TimeSpan);
                    var hasMatch = false;

                    if (ep.AbsoluteEpisodeNumber.HasValue && airingTimes.TryGetValue(ep.AbsoluteEpisodeNumber.Value, out var absTime))
                    {
                        timeOfDay = absTime;
                        hasMatch = true;
                    }
                    else if (airingTimes.TryGetValue(ep.EpisodeNumber, out var relTime))
                    {
                        timeOfDay = relTime;
                        hasMatch = true;
                    }
                    else if (seasonDefaultTime.HasValue)
                    {
                        timeOfDay = seasonDefaultTime.Value;
                        hasMatch = true;
                    }
                    else if (globalDefaultTime.HasValue)
                    {
                        timeOfDay = globalDefaultTime.Value;
                        hasMatch = true;
                    }

                    if (ep.AirDateUtc.HasValue && !string.IsNullOrWhiteSpace(ep.AirDate) && hasMatch)
                    {
                        var jstDate = DateTime.Parse(ep.AirDate);
                        var preciseJstTime = jstDate.Add(timeOfDay);
                        ep.AirDateUtc = DateTime.SpecifyKind(preciseJstTime.AddHours(-9), DateTimeKind.Utc);
                    }

                    allEpisodes.Add(ep);
                }

                if (assignedSeasonNumber > 0)
                {
                    absoluteEpisodeOffset += maxEpisodeNumber;
                }
            }

            if (hubSeries == null)
            {
                throw new Exception($"Could not fetch primary series data for AniDB ID {externalId}");
            }

            hubSeries.Seasons = allEpisodes.Select(e => e.SeasonNumber)
                .Distinct()
                .OrderBy(s => s)
                .Select(s =>
                {
                    var season = new Season { SeasonNumber = s, Monitored = s > 0 };
                    if (seasonMetadata.TryGetValue(s, out var meta))
                    {
                        season.Title = meta.Title;
                        season.Images = meta.Images;
                    }

                    return season;
                })
                .ToList();

            hubSeries.AniDbMappings = mappings;
            hubSeries.AniDbRelatedSeries = relatedSeries;

            if (hubSeries.AlternateTitles == null)
            {
                hubSeries.AlternateTitles = new List<string>();
            }

            var existingCleanTitles = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            if (!string.IsNullOrWhiteSpace(hubSeries.Title))
            {
                existingCleanTitles.Add(hubSeries.Title.CleanForSearch());
            }

            foreach (var altTitle in hubSeries.AlternateTitles)
            {
                if (!string.IsNullOrWhiteSpace(altTitle))
                {
                    existingCleanTitles.Add(altTitle.CleanForSearch());
                }
            }

            foreach (var anilistId in allAniListIds)
            {
                try
                {
                    var anilistTitles = _aniListEnricher.GetTitles(anilistId);
                    foreach (var anilistTitle in anilistTitles)
                    {
                        if (string.IsNullOrWhiteSpace(anilistTitle))
                        {
                            continue;
                        }

                        var cleanAnilistTitle = anilistTitle.CleanForSearch();
                        if (!existingCleanTitles.Contains(cleanAnilistTitle))
                        {
                            hubSeries.AlternateTitles.Add(anilistTitle);
                            existingCleanTitles.Add(cleanAnilistTitle);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to fetch AniList titles for enrichment.");
                }
            }

            return Tuple.Create(hubSeries, allEpisodes);
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

            _configService.SetAniDbBanExpiration(null);
            return doc;
        }

        private int FindHubId(int startId)
        {
            var currentId = startId;
            var visited = new HashSet<int> { currentId };
            var lastValidId = startId;

            while (true)
            {
                XDocument doc;
                try
                {
                    doc = GetAnimeXml(currentId);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "AniDB relation traversal hit an unavailable entry at ID {0}. Falling back to earliest available entry {1} as hub.", currentId, lastValidId);
                    return lastValidId;
                }

                var prequels = GetRelations(doc, "Prequel");
                if (prequels.Count == 1)
                {
                    var nextId = prequels[0];
                    if (visited.Contains(nextId))
                    {
                        _logger.Warn("Circular relation detected in AniDB chain at ID {0}", nextId);
                        break;
                    }

                    lastValidId = currentId;
                    currentId = nextId;
                    visited.Add(currentId);
                }
                else if (prequels.Count > 1)
                {
                    _logger.Warn("Branching prequels detected for AniDB ID {0}. Stopping traversal.", currentId);
                    break;
                }
                else
                {
                    break; // No prequels, found the hub
                }
            }

            return currentId;
        }

        private List<int> GetLinearChain(int hubId)
        {
            var chain = new List<int>();
            var currentId = hubId;
            var visited = new HashSet<int> { currentId };

            while (true)
            {
                XDocument doc;
                try
                {
                    doc = GetAnimeXml(currentId);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "AniDB relation traversal hit an unavailable entry at ID {0} while building chain. Stopping traversal.", currentId);
                    break;
                }

                chain.Add(currentId);
                var sequels = GetRelations(doc, "Sequel");

                if (sequels.Count == 1)
                {
                    var nextId = sequels[0];
                    if (visited.Contains(nextId))
                    {
                        _logger.Warn("Circular relation detected in AniDB chain at ID {0}", nextId);
                        break;
                    }

                    currentId = nextId;
                    visited.Add(currentId);
                }
                else if (sequels.Count > 1)
                {
                    _logger.Warn("Branching sequels detected for AniDB ID {0}. Stopping traversal.", currentId);
                    break;
                }
                else
                {
                    break;
                }
            }

            return chain;
        }

        private List<int> GetRelations(XDocument doc, string relationType)
        {
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var related = doc.Root?.Element(ns + "relatedanime");
            if (related == null)
            {
                return new List<int>();
            }

            var results = new List<int>();
            foreach (var anime in related.Elements(ns + "anime"))
            {
                var type = (string)anime.Attribute("type");
                if (string.Equals(type, relationType, StringComparison.OrdinalIgnoreCase))
                {
                    var idStr = (string)anime.Attribute("id");
                    if (int.TryParse(idStr, out var id) && id > 0)
                    {
                        results.Add(id);
                    }
                }
            }

            return results;
        }

        private List<(int Id, string RelationType)> GetAllRelations(XElement root)
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
                            TitleSlug = title.ToUrlSlug(),
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

        private static Series MapSeries(XElement root, int aniDbId)
        {
            var ns = root?.Name.Namespace ?? XNamespace.None;

            var titleElements = root?.Elements(ns + "titles").Elements(ns + "title");
            var title = GetBestTitle(titleElements, "Unknown");
            var alternateTitles = new List<string>();
            if (titleElements != null)
            {
                var xjatTitle = titleElements.FirstOrDefault(t => (string)t.Attribute(XNamespace.Xml + "lang") == "x-jat" || (string)t.Attribute("lang") == "x-jat")?.Value?.Trim();

                if (!string.IsNullOrWhiteSpace(xjatTitle))
                {
                    alternateTitles.Add(xjatTitle);
                }

                foreach (var tElement in titleElements)
                {
                    var val = tElement.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(val) && val != xjatTitle)
                    {
                        alternateTitles.Add(val);
                    }
                }
            }

            var series = new Series
            {
                Title = title,
                CleanTitle = title.CleanSeriesTitle(),
                SortTitle = SeriesTitleNormalizer.Normalize(title, aniDbId),
                TitleSlug = title.ToUrlSlug(),
                AlternateTitles = alternateTitles.Distinct().ToList(),
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
            if (endDate.IsNotNullOrWhiteSpace() && !endDate.Contains('?'))
            {
                if (DateTime.TryParse(endDate, out var parsedEndDate) && parsedEndDate > DateTime.UtcNow)
                {
                    series.Status = SeriesStatusType.Continuing;
                }
                else
                {
                    series.Status = SeriesStatusType.Ended;
                }
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
                    SeasonNumber = type == "1" ? 1 : 0,
                    EpisodeNumber = epNum,
                    AbsoluteEpisodeNumber = null,
                    Title = titleEn,
                    Overview = CleanDescription(ep.Element(ns + "summary")?.Value),
                    Runtime = int.TryParse(ep.Element(ns + "length")?.Value, out var epRt) ? epRt : 0,
                    Monitored = type == "1"
                };

                var airDate = ep.Element(ns + "airdate")?.Value;
                if (airDate.IsNotNullOrWhiteSpace() && DateTime.TryParse(airDate, out var aired))
                {
                    // Default date-only episodes to end-of-day UTC (23:59:59)
                    // so we don't prematurely search before it has actually aired.
                    // This will be overridden by precise AniList times during enrichment.
                    episode.AirDateUtc = new DateTime(aired.Year, aired.Month, aired.Day, 23, 59, 59, DateTimeKind.Utc);
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
