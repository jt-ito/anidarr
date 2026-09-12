using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    public class AniDbProvider : IMetadataProvider
    {
        private const string AniDbApiBase = "http://api.anidb.net:9001/httpapi";
        private static readonly Regex AniDbLinkRegex = new Regex(@"https?://anidb\.net/[^\s\[]+\s*\[(.*?)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly ConcurrentDictionary<int, Task<Tuple<Series, List<Episode>>>> _inFlightSeriesInfo = new ConcurrentDictionary<int, Task<Tuple<Series, List<Episode>>>>();
        private static readonly ConcurrentDictionary<int, (DateTime CachedAt, Tuple<Series, List<Episode>> Result)> _seriesInfoCache = new ConcurrentDictionary<int, (DateTime, Tuple<Series, List<Episode>>)>();
        private static readonly TimeSpan SeriesInfoCacheTtl = TimeSpan.FromMinutes(15);
        private static readonly ConcurrentDictionary<string, Task<string>> _inFlightFetches = new ConcurrentDictionary<string, Task<string>>();

        public static void ClearCache()
        {
            _seriesInfoCache.Clear();
            _inFlightSeriesInfo.Clear();
            _inFlightFetches.Clear();
        }

        private readonly IHttpClient _httpClient;
        private readonly IConfigFileProvider _configService;
        private readonly IAnimeOfflineDatabase _titleSearch;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;
        private readonly IAniDbRateLimiter _rateLimiter;
        private readonly IAniDbSeriesMappingService _mappingService;
        private readonly AniList.IAniListEnricher _aniListEnricher;
        private readonly Messaging.Events.IEventAggregator _eventAggregator;

        public MetadataProviderType ProviderType => MetadataProviderType.AniDb;

        public AniDbProvider(IHttpClient httpClient,
                             IConfigFileProvider configService,
                             IAnimeOfflineDatabase titleSearch,
                             IAppFolderInfo appFolderInfo,
                             IAniDbRateLimiter rateLimiter,
                             Logger logger,
                             IAniDbSeriesMappingService mappingService,
                             AniList.IAniListEnricher aniListEnricher,
                             Messaging.Events.IEventAggregator eventAggregator = null)
        {
            _httpClient = httpClient;
            _configService = configService;
            _titleSearch = titleSearch;
            _appFolderInfo = appFolderInfo;
            _rateLimiter = rateLimiter;
            _logger = logger;
            _mappingService = mappingService;
            _aniListEnricher = aniListEnricher;
            _eventAggregator = eventAggregator;
        }

        private void ReportProgress(int? aniDbId, string message)
        {
            _logger.Debug("AniDB Add Progress [{0}]: {1}", aniDbId, message);
            _eventAggregator?.PublishEvent(new Tv.Events.SeriesAddProgressEvent(message, aniDbId));
        }

        public bool CanHandleId(string externalIdKey) =>
            externalIdKey is "anidb" or "anidbid";

        public Tuple<Series, List<Episode>> GetSeriesInfo(string externalId)
        {
            if (!int.TryParse(externalId, out var aniDbId) || aniDbId <= 0)
            {
                throw new ArgumentException($"Invalid AniDB ID: {externalId}");
            }

            if (_seriesInfoCache.TryGetValue(aniDbId, out var cached) && DateTime.UtcNow - cached.CachedAt < SeriesInfoCacheTtl)
            {
                _logger.Debug("Using in-memory cached series info for AniDB ID {0}", aniDbId);
                return cached.Result;
            }

            Task<Tuple<Series, List<Episode>>> fetchTask;
            lock (_inFlightSeriesInfo)
            {
                if (!_inFlightSeriesInfo.TryGetValue(aniDbId, out fetchTask))
                {
                    fetchTask = Task.Run(() => FetchSeriesInfoInternal(aniDbId));
                    _inFlightSeriesInfo[aniDbId] = fetchTask;
                }
                else
                {
                    _logger.Debug("Joining existing in-flight AniDB series info task for ID {0}", aniDbId);
                }
            }

            try
            {
                var result = fetchTask.GetAwaiter().GetResult();
                _seriesInfoCache[aniDbId] = (DateTime.UtcNow, result);
                return result;
            }
            finally
            {
                lock (_inFlightSeriesInfo)
                {
                    _inFlightSeriesInfo.TryRemove(aniDbId, out _);
                }
            }
        }

        private Tuple<Series, List<Episode>> FetchSeriesInfoInternal(int aniDbId)
        {
            ReportProgress(aniDbId, $"Connecting to AniDB for series #{aniDbId}...");
            var (hubId, hubDocs) = FindHubId(aniDbId);
            ReportProgress(aniDbId, $"Traversing franchise seasons from root #{hubId}...");
            var (chainIds, chainDocs) = GetLinearChain(hubId, hubDocs);
            ReportProgress(aniDbId, $"Found {chainIds.Count} season(s) in franchise chain...");

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

            // Derive hasTvOrWebAnchor from already-fetched chain documents (no redundant fetches).
            // Consult AniList advisory if an entry has an unknown or missing AniDB type.
            var hasTvOrWebAnchor = false;
            foreach (var kvp in chainDocs)
            {
                var docNs = kvp.Value.Root?.Name.Namespace ?? XNamespace.None;
                var docType = kvp.Value.Root?.Element(docNs + "type")?.Value;
                if (string.Equals(docType, "TV Series", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(docType, "Web", StringComparison.OrdinalIgnoreCase))
                {
                    hasTvOrWebAnchor = true;
                    break;
                }

                if (string.IsNullOrWhiteSpace(docType) ||
                    docType.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                    docType.Equals("Other", StringComparison.OrdinalIgnoreCase))
                {
                    var adv = GetAdvisoryAniListInfo(kvp.Key);
                    if (adv != null && (string.Equals(adv.Format, "TV", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(adv.Format, "TV_SHORT", StringComparison.OrdinalIgnoreCase)))
                    {
                        hasTvOrWebAnchor = true;
                        break;
                    }
                }
            }

            foreach (var id in chainIds)
            {
                if (!chainDocs.TryGetValue(id, out var doc))
                {
                    continue;
                }

                var currentSeriesMetadata = MapSeries(doc.Root, id);
                ReportProgress(aniDbId, $"Processing season metadata: {currentSeriesMetadata.Title}...");
                if (hubSeries == null)
                {
                    hubSeries = currentSeriesMetadata;
                }

                _titleSearch.UpdateMetadata(currentSeriesMetadata);

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

                    AniListMediaInfo advisory = null;
                    var advisoryChecked = false;
                    AniListMediaInfo GetAdvisory()
                    {
                        if (!advisoryChecked)
                        {
                            advisoryChecked = true;
                            advisory = GetAdvisoryAniListInfo(id);
                        }

                        return advisory;
                    }

                    if (string.IsNullOrWhiteSpace(animeType) ||
                        animeType.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                        animeType.Equals("Other", StringComparison.OrdinalIgnoreCase))
                    {
                        var adv = GetAdvisory();
                        if (adv != null && !string.IsNullOrWhiteSpace(adv.Format))
                        {
                            if (adv.Format.Equals("TV", StringComparison.OrdinalIgnoreCase) ||
                                adv.Format.Equals("TV_SHORT", StringComparison.OrdinalIgnoreCase))
                            {
                                assignedSeasonNumber = seasonNumber;
                                seasonNumber++;
                            }
                            else if (adv.Format.Equals("MOVIE", StringComparison.OrdinalIgnoreCase) ||
                                     adv.Format.Equals("SPECIAL", StringComparison.OrdinalIgnoreCase) ||
                                     adv.Format.Equals("MUSIC", StringComparison.OrdinalIgnoreCase))
                            {
                                assignedSeasonNumber = 0;
                            }
                            else if (adv.Format.Equals("OVA", StringComparison.OrdinalIgnoreCase) ||
                                     adv.Format.Equals("ONA", StringComparison.OrdinalIgnoreCase))
                            {
                                if (hasTvOrWebAnchor)
                                {
                                    assignedSeasonNumber = 0;
                                }
                                else
                                {
                                    assignedSeasonNumber = seasonNumber;
                                    seasonNumber++;
                                }
                            }
                            else
                            {
                                assignedSeasonNumber = -1; // Flag for manual review
                            }
                        }
                        else
                        {
                            assignedSeasonNumber = -1; // Flag for manual review
                        }
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
                        if (!hasQualifyingHubRelation)
                        {
                            assignedSeasonNumber = seasonNumber;
                            seasonNumber++;
                        }
                        else if (animeType.Equals("Movie", StringComparison.OrdinalIgnoreCase) ||
                                 animeType.Equals("Music Video", StringComparison.OrdinalIgnoreCase) ||
                                 (GetAdvisory()?.Format?.Equals("MOVIE", StringComparison.OrdinalIgnoreCase) == true))
                        {
                            // Movies belong in Radarr; music videos are never canonical seasons.
                            // Also consult AniList advisory: if AniList classifies it as MOVIE, delegate to Radarr.
                            assignedSeasonNumber = 0;
                        }
                        else if (hasTvOrWebAnchor)
                        {
                            // In a TV/Web-anchored chain, non-TV entries (OVAs) default to Specials (Season 0)
                            assignedSeasonNumber = 0;
                        }
                        else
                        {
                            // In an OVA-native chain (no TV/Web anchor), use the AniDB type to
                            // distinguish canonical entries from supplements:
                            //   - "Special" type entries are bonus content → Season 0
                            //   - "OVA" type entries are canonical sequels → numbered season
                            // For 1-episode entries, check AniList advisory in case AniList classifies it as SPECIAL or MUSIC
                            if (animeType.Equals("Special", StringComparison.OrdinalIgnoreCase))
                            {
                                assignedSeasonNumber = 0;
                            }
                            else
                            {
                                var epCount = doc.Root?.Element(ns + "episodecount") != null
                                    ? (int?)doc.Root.Element(ns + "episodecount")
                                    : (int?)null;

                                var adv = epCount <= 1 ? GetAdvisory() : null;
                                if (adv != null && (string.Equals(adv.Format, "SPECIAL", StringComparison.OrdinalIgnoreCase) ||
                                                    string.Equals(adv.Format, "MUSIC", StringComparison.OrdinalIgnoreCase)))
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
                                    if (_aniListEnricher.IsRateLimited)
                                    {
                                        _logger.Debug("AniList circuit breaker active; skipping remaining title fallbacks for AniDB ID {0}.", id);
                                        break;
                                    }

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
                        _logger.Warn("Failed to resolve AniList ID for AniDB ID {0}: {1}", id, ex.Message);
                    }

                    chainData.Add((assignedSeasonNumber, episodes, currentAniListId));
                }
            }

            var allAniListIds = chainData.Where(x => x.AniListId.HasValue).Select(x => x.AniListId.Value).ToList();
            var allAiringTimes = new Dictionary<int, Dictionary<int, TimeSpan>>();
            var allAniListTitles = new Dictionary<int, List<string>>();

            if (allAniListIds.Any())
            {
                try
                {
                    ReportProgress(aniDbId, $"Fetching episode schedules & titles from AniList for {allAniListIds.Count} season(s)...");
                    _logger.Debug("Batch fetching enrichment data (airing times + titles) for {0} AniList IDs", allAniListIds.Count);
                    var enrichment = _aniListEnricher.GetEnrichmentForMultiple(allAniListIds);
                    if (enrichment != null && (enrichment.AiringTimes.Any() || enrichment.Titles.Any()))
                    {
                        allAiringTimes = enrichment.AiringTimes ?? new Dictionary<int, Dictionary<int, TimeSpan>>();
                        allAniListTitles = enrichment.Titles ?? new Dictionary<int, List<string>>();
                    }
                    else
                    {
                        allAiringTimes = _aniListEnricher.GetAiringTimesForMultiple(allAniListIds) ?? new Dictionary<int, Dictionary<int, TimeSpan>>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("Failed to batch fetch AniList enrichment data: {0}", ex.Message);
                }
            }

            TimeSpan? globalDefaultTime = null;
            var allTimes = allAiringTimes?.Values?.Where(x => x != null).SelectMany(x => x.Values).ToList() ?? new List<TimeSpan>();
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
                throw new Exception($"Could not fetch primary series data for AniDB ID {aniDbId}");
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

            foreach (var id in chainIds)
            {
                try
                {
                    var local = _titleSearch.GetSeriesById("anidb", id);
                    if (local != null)
                    {
                        var localTitles = new List<string>();
                        if (!string.IsNullOrWhiteSpace(local.RomajiTitle))
                        {
                            localTitles.Add(local.RomajiTitle);
                        }

                        if (!string.IsNullOrWhiteSpace(local.EnglishTitle))
                        {
                            localTitles.Add(local.EnglishTitle);
                        }

                        if (!string.IsNullOrWhiteSpace(local.NativeTitle))
                        {
                            localTitles.Add(local.NativeTitle);
                        }

                        if (local.SearchSynonyms != null)
                        {
                            localTitles.AddRange(local.SearchSynonyms);
                        }

                        foreach (var localTitle in localTitles)
                        {
                            if (string.IsNullOrWhiteSpace(localTitle))
                            {
                                continue;
                            }

                            var cleanLocalTitle = localTitle.CleanForSearch();
                            if (!existingCleanTitles.Contains(cleanLocalTitle))
                            {
                                hubSeries.AlternateTitles.Add(localTitle);
                                existingCleanTitles.Add(cleanLocalTitle);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to read local alternate titles for AniDB ID {0}", id);
                }
            }

            foreach (var anilistId in allAniListIds)
            {
                List<string> anilistTitles = null;
                if (allAniListTitles.TryGetValue(anilistId, out var titles) && titles != null && titles.Any())
                {
                    anilistTitles = titles;
                }
                else
                {
                    try
                    {
                        anilistTitles = _aniListEnricher.GetTitles(anilistId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Failed to fetch AniList titles for ID {0}", anilistId);
                    }
                }

                if (anilistTitles != null)
                {
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
            }

            ReportProgress(aniDbId, "Finalizing series and episode metadata...");
            var finalResult = Tuple.Create(hubSeries, allEpisodes);
            foreach (var id in chainIds)
            {
                _seriesInfoCache[id] = (DateTime.UtcNow, finalResult);
            }

            return finalResult;
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

        private (int HubId, Dictionary<int, XDocument> FetchedDocs) FindHubId(int startId)
        {
            var currentId = startId;
            var visited = new HashSet<int> { currentId };
            var lastValidId = startId;
            var fetchedDocs = new Dictionary<int, XDocument>();

            while (true)
            {
                XDocument doc;
                try
                {
                    ReportProgress(startId, $"Inspecting AniDB entry #{currentId}...");
                    doc = GetAnimeXml(currentId);
                    fetchedDocs[currentId] = doc;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "AniDB relation traversal hit an unavailable entry at ID {0}. Falling back to earliest available entry {1} as hub.", currentId, lastValidId);
                    return (lastValidId, fetchedDocs);
                }

                var prequels = GetRelations(doc, "Prequel");
                if (prequels.Count == 1)
                {
                    if (visited.Count >= 25)
                    {
                        _logger.Warn("Max prequel depth reached for AniDB ID {0}. Stopping traversal.", startId);
                        break;
                    }

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

            return (currentId, fetchedDocs);
        }

        private (List<int> Chain, Dictionary<int, XDocument> AllDocs) GetLinearChain(int hubId, Dictionary<int, XDocument> existingDocs)
        {
            var chain = new List<int>();
            var allDocs = new Dictionary<int, XDocument>(existingDocs);
            var currentId = hubId;
            var visited = new HashSet<int> { currentId };

            while (true)
            {
                XDocument doc;
                if (allDocs.TryGetValue(currentId, out var cachedDoc))
                {
                    doc = cachedDoc;
                }
                else
                {
                    try
                    {
                        doc = GetAnimeXml(currentId);
                        allDocs[currentId] = doc;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "AniDB relation traversal hit an unavailable entry at ID {0} while building chain. Stopping traversal.", currentId);
                        break;
                    }
                }

                chain.Add(currentId);
                var sequels = GetRelations(doc, "Sequel");

                if (sequels.Count == 1)
                {
                    if (visited.Count >= 30)
                    {
                        _logger.Warn("Max sequel depth reached for AniDB ID {0}. Stopping traversal.", hubId);
                        break;
                    }

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

            return (chain, allDocs);
        }

        private AniListMediaInfo GetAdvisoryAniListInfo(int anidbId)
        {
            if (_aniListEnricher == null || _aniListEnricher.IsRateLimited)
            {
                return null;
            }

            try
            {
                var local = _titleSearch.GetSeriesById("anidb", anidbId);
                if (local?.AniListId != null)
                {
                    return _aniListEnricher.GetMediaInfo(local.AniListId.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("AniList advisory lookup failed for AniDB ID {0}: {1}", anidbId, ex.Message);
            }

            return null;
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
                        ReportProgress(null, "Respecting AniDB rate limit (waiting 2s)...");
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

            // 2. Any non-short English title (e.g. "Big Sister Juice the Animation: Leave the Three Sisters to Shirakawa")
            var anyEn = candidatePool.FirstOrDefault(t =>
                string.Equals(GetLang(t), "en", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(anyEn))
            {
                return anyEn;
            }

            // 3. Main title (e.g. "Animation Seisaku Shinkou Kuromi-chan")
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
