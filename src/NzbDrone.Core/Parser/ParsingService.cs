using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Parser
{
    public interface IParsingService
    {
        Series GetSeries(string title);
        RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, int tvdbId, int tvRageId, string imdbId, SearchCriteriaBase searchCriteria = null);
        RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, Series series);
        RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, int seriesId, IEnumerable<int> episodeIds);
        List<Episode> GetEpisodes(ParsedEpisodeInfo parsedEpisodeInfo, Series series, bool sceneSource, SearchCriteriaBase searchCriteria = null);
        ParsedEpisodeInfo ParseSpecialEpisodeTitle(ParsedEpisodeInfo parsedEpisodeInfo, string releaseTitle, int tvdbId, int tvRageId, string imdbId, SearchCriteriaBase searchCriteria = null);
        ParsedEpisodeInfo ParseSpecialEpisodeTitle(ParsedEpisodeInfo parsedEpisodeInfo, string releaseTitle, Series series);
    }

    public class ParsingService : IParsingService
    {
        private readonly IEpisodeService _episodeService;
        private readonly ISeriesService _seriesService;
        private readonly ISceneMappingService _sceneMappingService;
        private readonly Logger _logger;

        public ParsingService(IEpisodeService episodeService,
                              ISeriesService seriesService,
                              ISceneMappingService sceneMappingService,
                              Logger logger)
        {
            _episodeService = episodeService;
            _seriesService = seriesService;
            _sceneMappingService = sceneMappingService;
            _logger = logger;
        }

        public Series GetSeries(string title)
        {
            var parsedEpisodeInfo = Parser.ParseTitle(title);

            if (parsedEpisodeInfo == null)
            {
                return _seriesService.FindByTitle(title);
            }

            var tvdbId = _sceneMappingService.FindTvdbId(parsedEpisodeInfo.SeriesTitle, parsedEpisodeInfo.ReleaseTitle, parsedEpisodeInfo.SeasonNumber);

            if (tvdbId.HasValue)
            {
                return _seriesService.FindByTvdbId(tvdbId.Value);
            }

            var series = _seriesService.FindByTitle(parsedEpisodeInfo.SeriesTitle);

            if (series == null && parsedEpisodeInfo.SeriesTitleInfo.AllTitles != null)
            {
                series = GetSeriesByAllTitles(parsedEpisodeInfo);
            }

            if (series == null)
            {
                series = _seriesService.FindByTitle(parsedEpisodeInfo.SeriesTitleInfo.TitleWithoutYear,
                                                    parsedEpisodeInfo.SeriesTitleInfo.Year);
            }

            return series;
        }

        private Series GetSeriesByAllTitles(ParsedEpisodeInfo parsedEpisodeInfo)
        {
            var year = parsedEpisodeInfo.SeriesTitleInfo.Year;
            Series foundSeries = null;
            int? foundTvdbId = null;

            // Match each title individually, they must all resolve to the same tvdbid
            foreach (var title in parsedEpisodeInfo.SeriesTitleInfo.AllTitles)
            {
                Series series = null;

                if (year > 0)
                {
                    series = _seriesService.FindByTitle(title, year);

                    // Fall back to title + year being part of the title, this will allow
                    // matching series with the same name that include the year in the title.
                    if (series == null)
                    {
                        series = _seriesService.FindByTitle($"{title} {year}");
                    }
                }
                else
                {
                    series = _seriesService.FindByTitle(title);
                }

                var tvdbId = series?.TvdbId;

                if (series == null)
                {
                    tvdbId = _sceneMappingService.FindTvdbId(title, parsedEpisodeInfo.ReleaseTitle, parsedEpisodeInfo.SeasonNumber);
                }

                if (!tvdbId.HasValue)
                {
                    _logger.Trace("Title {0} not matching any series.", title);
                    continue;
                }

                if (foundTvdbId.HasValue && tvdbId != foundTvdbId)
                {
                    _logger.Trace("Title {0} both matches tvdbid {1} and {2}, no series selected.", parsedEpisodeInfo.SeriesTitle, foundTvdbId, tvdbId);
                    return null;
                }

                if (foundSeries == null)
                {
                    foundSeries = series;
                }

                foundTvdbId = tvdbId;
            }

            if (foundSeries == null && foundTvdbId.HasValue)
            {
                foundSeries = _seriesService.FindByTvdbId(foundTvdbId.Value);
            }

            return foundSeries;
        }

        private Series GetSeriesAliasTitleAndYear(ParsedEpisodeInfo parsedEpisodeInfo)
        {
            var year = parsedEpisodeInfo.SeriesTitleInfo.Year;
            var titleWithoutyear = parsedEpisodeInfo.SeriesTitleInfo.TitleWithoutYear;
            var tvdbId = _sceneMappingService.FindTvdbId(titleWithoutyear, parsedEpisodeInfo.ReleaseTitle, parsedEpisodeInfo.SeasonNumber);

            if (tvdbId.HasValue)
            {
                var series = _seriesService.FindByTvdbId(tvdbId.Value);

                if (series != null && series.Year == year)
                {
                    return series;
                }
            }

            return null;
        }

        public RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, int tvdbId, int tvRageId, string imdbId, SearchCriteriaBase searchCriteria = null)
        {
            return Map(parsedEpisodeInfo, tvdbId, tvRageId, imdbId, null, searchCriteria);
        }

        public RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, Series series)
        {
            return Map(parsedEpisodeInfo, 0, 0, null, series, null);
        }

        public RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, int seriesId, IEnumerable<int> episodeIds)
        {
            var episodes = _episodeService.GetEpisodes(episodeIds);

            return new RemoteEpisode
            {
                ParsedEpisodeInfo = parsedEpisodeInfo,
                Series = _seriesService.GetSeries(seriesId),
                Episodes = episodes,
                MappedSeasonNumber = episodes.FirstOrDefault()?.SeasonNumber ?? parsedEpisodeInfo?.SeasonNumber ?? 0
            };
        }

        private RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, int tvdbId, int tvRageId, string imdbId, Series series, SearchCriteriaBase searchCriteria)
        {
            var sceneMapping = _sceneMappingService.FindSceneMapping(parsedEpisodeInfo.SeriesTitle, parsedEpisodeInfo.ReleaseTitle, parsedEpisodeInfo.SeasonNumber);

            var remoteEpisode = new RemoteEpisode
            {
                ParsedEpisodeInfo = parsedEpisodeInfo,
                SceneMapping = sceneMapping,
                MappedSeasonNumber = parsedEpisodeInfo.SeasonNumber
            };

            // For now we just detect tvdb vs scene, but we can do multiple 'origins' in the future.
            var sceneSource = true;
            if (sceneMapping != null)
            {
                if (sceneMapping.SeasonNumber.HasValue && sceneMapping.SeasonNumber.Value >= 0 &&
                    sceneMapping.SceneSeasonNumber <= parsedEpisodeInfo.SeasonNumber)
                {
                    remoteEpisode.MappedSeasonNumber += sceneMapping.SeasonNumber.Value - sceneMapping.SceneSeasonNumber.Value;
                }

                if (sceneMapping.SceneOrigin == "tvdb")
                {
                    sceneSource = false;
                }
                else if (sceneMapping.Type == "XemService" &&
                         sceneMapping.SceneSeasonNumber.NonNegative().HasValue &&
                         parsedEpisodeInfo.SeasonNumber == 1 &&
                         sceneMapping.SceneSeasonNumber != parsedEpisodeInfo.SeasonNumber)
                {
                    remoteEpisode.MappedSeasonNumber = sceneMapping.SceneSeasonNumber.Value;
                }
            }

            FindSeriesResult seriesMatch = null;
            if (series == null)
            {
                seriesMatch = FindSeries(parsedEpisodeInfo, tvdbId, tvRageId, imdbId, sceneMapping, searchCriteria);

                if (seriesMatch != null)
                {
                    series = seriesMatch.Series;
                    remoteEpisode.SeriesMatchType = seriesMatch.MatchType;
                }
            }

            if (series != null)
            {
                remoteEpisode.Series = series;

                if (seriesMatch?.MatchedSeasonNumber.HasValue == true && seriesMatch.MatchedSeasonNumber.Value > 1 && remoteEpisode.MappedSeasonNumber <= 1)
                {
                    remoteEpisode.MappedSeasonNumber = seriesMatch.MatchedSeasonNumber.Value;
                }

                if (ValidateParsedEpisodeInfo.ValidateForSeriesType(parsedEpisodeInfo, series))
                {
                    remoteEpisode.Episodes = GetEpisodes(parsedEpisodeInfo, series, remoteEpisode.MappedSeasonNumber, sceneSource, searchCriteria);
                }
            }

            remoteEpisode.Languages = parsedEpisodeInfo.Languages;

            if (remoteEpisode.Episodes == null)
            {
                remoteEpisode.Episodes = new List<Episode>();
            }

            if (searchCriteria != null)
            {
                var requestedEpisodes = searchCriteria.Episodes.ToDictionaryIgnoreDuplicates(v => v.Id);
                remoteEpisode.EpisodeRequested = remoteEpisode.Episodes.Any(v => requestedEpisodes.ContainsKey(v.Id));
            }

            return remoteEpisode;
        }

        public List<Episode> GetEpisodes(ParsedEpisodeInfo parsedEpisodeInfo, Series series, bool sceneSource, SearchCriteriaBase searchCriteria = null)
        {
            if (sceneSource)
            {
                var remoteEpisode = Map(parsedEpisodeInfo, 0, 0, null, series, searchCriteria);

                return remoteEpisode.Episodes;
            }

            return GetEpisodes(parsedEpisodeInfo, series, parsedEpisodeInfo.SeasonNumber, sceneSource, searchCriteria);
        }

        private List<Episode> GetEpisodes(ParsedEpisodeInfo parsedEpisodeInfo, Series series, int mappedSeasonNumber, bool sceneSource, SearchCriteriaBase searchCriteria)
        {
            if (parsedEpisodeInfo.FullSeason)
            {
                if (series.UseSceneNumbering && sceneSource)
                {
                    var episodes = _episodeService.GetEpisodesBySceneSeason(series.Id, mappedSeasonNumber);

                    // If episodes were found by the scene season number return them, otherwise fallback to look-up by season number
                    if (episodes.Any())
                    {
                        return episodes;
                    }
                }

                return _episodeService.GetEpisodesBySeason(series.Id, mappedSeasonNumber);
            }

            if (parsedEpisodeInfo.IsDaily)
            {
                var episodeInfo = GetDailyEpisode(series, parsedEpisodeInfo.AirDate, parsedEpisodeInfo.DailyPart, searchCriteria);

                if (episodeInfo != null)
                {
                    return new List<Episode> { episodeInfo };
                }

                return new List<Episode>();
            }

            if (parsedEpisodeInfo.IsAbsoluteNumbering)
            {
                return GetAnimeEpisodes(series, parsedEpisodeInfo, mappedSeasonNumber, sceneSource, searchCriteria);
            }

            if (parsedEpisodeInfo.IsPossibleSceneSeasonSpecial)
            {
                var parsedSpecialEpisodeInfo = ParseSpecialEpisodeTitle(parsedEpisodeInfo, parsedEpisodeInfo.ReleaseTitle, series);

                if (parsedSpecialEpisodeInfo != null)
                {
                    // Use the season number and disable scene source since the season/episode numbers that were returned are not scene numbers
                    return GetStandardEpisodes(series, parsedSpecialEpisodeInfo, parsedSpecialEpisodeInfo.SeasonNumber, false, searchCriteria);
                }
            }

            if (parsedEpisodeInfo.Special && mappedSeasonNumber != 0)
            {
                return new List<Episode>();
            }

            return GetStandardEpisodes(series, parsedEpisodeInfo, mappedSeasonNumber, sceneSource, searchCriteria);
        }

        private bool IsReleaseForSeries(Series series, string releaseTitle, SearchCriteriaBase searchCriteria = null)
        {
            if (series == null || string.IsNullOrWhiteSpace(releaseTitle))
            {
                return false;
            }

            var dummyInfo = new ParsedEpisodeInfo { ReleaseTitle = releaseTitle };

            var candidateAliases = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            if (!string.IsNullOrWhiteSpace(series.Title))
            {
                candidateAliases.Add(series.Title);
            }

            if (series.AlternateTitles != null)
            {
                foreach (var alt in series.AlternateTitles)
                {
                    if (!string.IsNullOrWhiteSpace(alt))
                    {
                        candidateAliases.Add(alt);
                    }
                }
            }

            if (searchCriteria != null && searchCriteria.Series != null && searchCriteria.Series.Id == series.Id)
            {
                if (!string.IsNullOrWhiteSpace(searchCriteria.SeasonTitle))
                {
                    candidateAliases.Add(searchCriteria.SeasonTitle);
                }

                if (searchCriteria.SeasonAlternateTitles != null)
                {
                    foreach (var sat in searchCriteria.SeasonAlternateTitles)
                    {
                        if (!string.IsNullOrWhiteSpace(sat))
                        {
                            candidateAliases.Add(sat);
                        }
                    }
                }

                if (searchCriteria.AllSeasonAliases != null)
                {
                    foreach (var kvp in searchCriteria.AllSeasonAliases)
                    {
                        foreach (var alias in kvp.Value)
                        {
                            if (!string.IsNullOrWhiteSpace(alias))
                            {
                                candidateAliases.Add(alias);
                            }
                        }
                    }
                }
            }

            var expandedAliases = new HashSet<string>(candidateAliases, StringComparer.InvariantCultureIgnoreCase);
            foreach (var alias in candidateAliases)
            {
                if (SearchCriteriaBase.TryGetBaseTitle(alias, out var baseTitle) && !string.IsNullOrWhiteSpace(baseTitle))
                {
                    expandedAliases.Add(baseTitle);
                }

                if (SearchCriteriaBase.TryGetCoreTitle(alias, out var coreTitle) && !string.IsNullOrWhiteSpace(coreTitle))
                {
                    expandedAliases.Add(coreTitle);
                }
            }

            foreach (var alias in expandedAliases)
            {
                var cleanAlias = alias.CleanForSearch();
                if (!SearchCriteriaBase.IsSafeTitle(cleanAlias))
                {
                    continue;
                }

                if (IsAliasMatch(dummyInfo, alias, cleanAlias, null))
                {
                    return true;
                }
            }

            return false;
        }

        public ParsedEpisodeInfo ParseSpecialEpisodeTitle(ParsedEpisodeInfo parsedEpisodeInfo, string releaseTitle, int tvdbId, int tvRageId, string imdbId, SearchCriteriaBase searchCriteria = null)
        {
            Series series = null;

            if (searchCriteria != null)
            {
                if (tvdbId != 0 && tvdbId == searchCriteria.Series.TvdbId)
                {
                    series = searchCriteria.Series;
                }
                else if (tvRageId != 0 && tvRageId == searchCriteria.Series.TvRageId)
                {
                    series = searchCriteria.Series;
                }
                else if (imdbId.IsNotNullOrWhiteSpace() && imdbId.Equals(searchCriteria.Series.ImdbId, StringComparison.Ordinal))
                {
                    series = searchCriteria.Series;
                }
                else if (IsReleaseForSeries(searchCriteria.Series, releaseTitle, searchCriteria))
                {
                    series = searchCriteria.Series;
                }
            }

            if (series == null)
            {
                series = GetSeries(releaseTitle);
            }

            if (series == null)
            {
                series = _seriesService.FindByTitleInexact(releaseTitle);
            }

            if (series == null && tvdbId > 0)
            {
                series = _seriesService.FindByTvdbId(tvdbId);
            }

            if (series == null && tvRageId > 0)
            {
                series = _seriesService.FindByTvRageId(tvRageId);
            }

            if (series == null && imdbId.IsNotNullOrWhiteSpace())
            {
                series = _seriesService.FindByImdbId(imdbId);
            }

            if (series == null)
            {
                _logger.Debug("No matching series {0}", releaseTitle);
                return null;
            }

            return ParseSpecialEpisodeTitle(parsedEpisodeInfo, releaseTitle, series);
        }

        public ParsedEpisodeInfo ParseSpecialEpisodeTitle(ParsedEpisodeInfo parsedEpisodeInfo, string releaseTitle, Series series)
        {
            // SxxE00 episodes are sometimes mapped via TheXEM, don't use episode title parsing in that case.
            if (parsedEpisodeInfo != null && parsedEpisodeInfo.IsPossibleSceneSeasonSpecial && series.UseSceneNumbering)
            {
                if (_episodeService.FindEpisodesBySceneNumbering(series.Id, parsedEpisodeInfo.SeasonNumber, 0).Any())
                {
                    return parsedEpisodeInfo;
                }
            }

            // find special episode in series season 0
            var episode = _episodeService.FindEpisodeByTitle(series.Id, 0, releaseTitle);

            if (episode != null)
            {
                // create parsed info from tv episode
                var info = new ParsedEpisodeInfo
                {
                    ReleaseTitle = releaseTitle,
                    SeriesTitle = series.Title,
                    SeriesTitleInfo = new SeriesTitleInfo
                    {
                        Title = series.Title
                    },
                    SeasonNumber = episode.SeasonNumber,
                    EpisodeNumbers = new int[1] { episode.EpisodeNumber },
                    FullSeason = false,
                    Quality = QualityParser.ParseQuality(releaseTitle),
                    ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(releaseTitle),
                    Languages = LanguageParser.ParseLanguages(releaseTitle),
                    Special = true
                };

                _logger.Debug("Found special episode {0} for title '{1}'", info, releaseTitle);
                return info;
            }

            // If parsedEpisodeInfo already specified explicit episode numbers that are not specials, do not override
            if (parsedEpisodeInfo != null && parsedEpisodeInfo.EpisodeNumbers != null && parsedEpisodeInfo.EpisodeNumbers.Any())
            {
                return null;
            }

            // Support single-episode series, OVAs, anime movies, or 1-episode seasons:
            // When release has no episode number and the anime series only has 1 regular episode,
            // the release represents that sole episode (e.g. S01E01).
            var allEpisodes = _episodeService.GetEpisodeBySeries(series.Id);
            if (allEpisodes != null && allEpisodes.Any())
            {
                var regularEpisodes = allEpisodes.Where(e => e.SeasonNumber > 0).ToList();
                var season1Episodes = allEpisodes.Where(e => e.SeasonNumber == 1).ToList();

                if (regularEpisodes.Count == 1 || allEpisodes.Count == 1 || (season1Episodes.Count == 1 && regularEpisodes.Count <= 1))
                {
                    var targetEpisode = regularEpisodes.FirstOrDefault() ?? allEpisodes.First();
                    var info = new ParsedEpisodeInfo
                    {
                        ReleaseTitle = releaseTitle,
                        SeriesTitle = series.Title,
                        SeriesTitleInfo = new SeriesTitleInfo
                        {
                            Title = series.Title
                        },
                        SeasonNumber = targetEpisode.SeasonNumber,
                        EpisodeNumbers = new int[1] { targetEpisode.EpisodeNumber },
                        AbsoluteEpisodeNumbers = targetEpisode.AbsoluteEpisodeNumber.HasValue
                            ? new int[1] { targetEpisode.AbsoluteEpisodeNumber.Value }
                            : new int[0],
                        FullSeason = false,
                        Quality = QualityParser.ParseQuality(releaseTitle),
                        ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(releaseTitle),
                        Languages = LanguageParser.ParseLanguages(releaseTitle),
                        Special = targetEpisode.SeasonNumber == 0
                    };

                    _logger.Debug("Matched single-episode anime/series {0} (S{1:D2}E{2:D2}) for title '{3}'",
                                  series.Title,
                                  targetEpisode.SeasonNumber,
                                  targetEpisode.EpisodeNumber,
                                  releaseTitle);
                    return info;
                }
            }

            return null;
        }

        private FindSeriesResult FindSeries(ParsedEpisodeInfo parsedEpisodeInfo, int tvdbId, int tvRageId, string imdbId, SceneMapping sceneMapping, SearchCriteriaBase searchCriteria)
        {
            Series series = null;

            if (sceneMapping != null)
            {
                if (searchCriteria != null && searchCriteria.Series.TvdbId == sceneMapping.TvdbId)
                {
                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Alias);
                }

                series = _seriesService.FindByTvdbId(sceneMapping.TvdbId);

                if (series == null)
                {
                    _logger.Debug("No matching series {0}", parsedEpisodeInfo.SeriesTitle);
                    return null;
                }

                return new FindSeriesResult(series, SeriesMatchType.Alias);
            }

            if (searchCriteria != null)
            {
                if (searchCriteria.Series.CleanTitle == parsedEpisodeInfo.SeriesTitle.CleanSeriesTitle())
                {
                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Title);
                }

                var isAniDbSourced = searchCriteria.Series.PrimaryMetadataProvider?.Equals("anidb", StringComparison.OrdinalIgnoreCase) == true ||
                                     (searchCriteria.Series.AniDbId ?? 0) > 0;

                if (isAniDbSourced)
                {
                    var cleanParsedTitle = parsedEpisodeInfo.SeriesTitle?.CleanForSearch() ?? string.Empty;
                    var cleanReleaseTitle = parsedEpisodeInfo.ReleaseTitle.IsNotNullOrWhiteSpace() ? parsedEpisodeInfo.ReleaseTitle.CleanForSearch() : null;

                    var seasonMap = new Dictionary<int, List<string>>();

                    // 1. Load any pre-computed season aliases from searchCriteria.AllSeasonAliases
                    if (searchCriteria.AllSeasonAliases != null)
                    {
                        foreach (var kvp in searchCriteria.AllSeasonAliases)
                        {
                            seasonMap[kvp.Key] = new List<string>(kvp.Value);
                        }
                    }

                    // 2. Incorporate explicit season titles & alternate titles from searchCriteria
                    if (searchCriteria.TargetSeasonNumber.HasValue)
                    {
                        if (!seasonMap.TryGetValue(searchCriteria.TargetSeasonNumber.Value, out var targetList))
                        {
                            targetList = new List<string>();
                            seasonMap[searchCriteria.TargetSeasonNumber.Value] = targetList;
                        }

                        if (searchCriteria.SeasonTitle != null && !targetList.Contains(searchCriteria.SeasonTitle, StringComparer.InvariantCultureIgnoreCase))
                        {
                            targetList.Add(searchCriteria.SeasonTitle);
                        }

                        if (searchCriteria.SeasonAlternateTitles != null)
                        {
                            foreach (var sat in searchCriteria.SeasonAlternateTitles)
                            {
                                if (!targetList.Contains(sat, StringComparer.InvariantCultureIgnoreCase))
                                {
                                    targetList.Add(sat);
                                }
                            }
                        }
                    }

                    // 3. Incorporate seasons from Series.Seasons
                    if (searchCriteria.Series.Seasons != null)
                    {
                        foreach (var s in searchCriteria.Series.Seasons)
                        {
                            if (!string.IsNullOrWhiteSpace(s.Title))
                            {
                                if (!seasonMap.TryGetValue(s.SeasonNumber, out var sList))
                                {
                                    sList = new List<string>();
                                    seasonMap[s.SeasonNumber] = sList;
                                }

                                if (!sList.Contains(s.Title, StringComparer.InvariantCultureIgnoreCase))
                                {
                                    sList.Add(s.Title);
                                }
                            }
                        }
                    }

                    // 4. Ensure Season 1 has the primary series title
                    if (searchCriteria.Series.Title != null)
                    {
                        if (!seasonMap.TryGetValue(1, out var s1List))
                        {
                            s1List = new List<string>();
                            seasonMap[1] = s1List;
                        }

                        if (!s1List.Contains(searchCriteria.Series.Title, StringComparer.InvariantCultureIgnoreCase))
                        {
                            s1List.Add(searchCriteria.Series.Title);
                        }
                    }

                    // 5. Partition any remaining Series.AlternateTitles into their respective seasons
                    if (searchCriteria.Series.AlternateTitles != null)
                    {
                        foreach (var alt in searchCriteria.Series.AlternateTitles)
                        {
                            if (string.IsNullOrWhiteSpace(alt) || SearchCriteriaBase.IsSpacelessSlug(alt))
                            {
                                continue;
                            }

                            var assignedSeason = 1;
                            if (seasonMap.Keys.Any(k => k > 1))
                            {
                                foreach (var seasonNum in seasonMap.Keys.Where(k => k > 1).OrderByDescending(k => k))
                                {
                                    if (Regex.IsMatch(alt, $@"(?:^|[^\p{{L}}\d]){seasonNum}(?:[^\p{{L}}\d]|$)"))
                                    {
                                        assignedSeason = seasonNum;
                                        break;
                                    }
                                }
                            }

                            if (!seasonMap.TryGetValue(assignedSeason, out var aList))
                            {
                                aList = new List<string>();
                                seasonMap[assignedSeason] = aList;
                            }

                            if (!aList.Contains(alt, StringComparer.InvariantCultureIgnoreCase))
                            {
                                aList.Add(alt);
                            }
                        }
                    }

                    // 6. Match against per-season candidate aliases
                    var seasonMatches = new List<(int SeasonNumber, string Alias, int Length, bool Exact)>();

                    foreach (var (seasonNum, rawAliases) in seasonMap)
                    {
                        var candidateAliases = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                        foreach (var alias in rawAliases)
                        {
                            if (string.IsNullOrWhiteSpace(alias))
                            {
                                continue;
                            }

                            candidateAliases.Add(alias);
                            if (SearchCriteriaBase.TryGetBaseTitle(alias, out var baseTitle))
                            {
                                candidateAliases.Add(baseTitle);
                            }

                            if (SearchCriteriaBase.TryGetCoreTitle(alias, out var coreTitle))
                            {
                                candidateAliases.Add(coreTitle);
                            }
                        }

                        foreach (var alias in candidateAliases)
                        {
                            var cleanAlias = alias.CleanForSearch();
                            if (!SearchCriteriaBase.IsSafeTitle(cleanAlias))
                            {
                                continue;
                            }

                            if (IsAliasMatch(parsedEpisodeInfo, alias, cleanAlias, cleanParsedTitle))
                            {
                                var exact = cleanParsedTitle == cleanAlias;
                                seasonMatches.Add((seasonNum, alias, cleanAlias.Length, exact));
                            }
                        }
                    }

                    if (seasonMatches.Any())
                    {
                        var targetSeasonNum = searchCriteria.TargetSeasonNumber ?? 1;
                        var bestMatch = seasonMatches
                            .OrderByDescending(m => m.Exact)
                            .ThenByDescending(m => m.Length)
                            .ThenByDescending(m => m.SeasonNumber == targetSeasonNum)
                            .ThenByDescending(m => m.SeasonNumber)
                            .First();

                        _logger.Debug("Matched AniDB series '{0}' season {1} by alias CleanForSearch containment '{2}' in '{3}'",
                            searchCriteria.Series.Title,
                            bestMatch.SeasonNumber,
                            bestMatch.Alias,
                            parsedEpisodeInfo.SeriesTitle);

                        return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Alias, bestMatch.SeasonNumber);
                    }

                    // 7. Fallback verification: relative-Levenshtein check on core titles for minor typos
                    var fallbackTitles = new List<Tuple<string, int>>();
                    if (searchCriteria.TargetSeasonNumber.HasValue && searchCriteria.SeasonTitle != null)
                    {
                        fallbackTitles.Add(Tuple.Create(searchCriteria.SeasonTitle, searchCriteria.TargetSeasonNumber.Value));
                    }

                    if (searchCriteria.Series.Seasons != null)
                    {
                        foreach (var s in searchCriteria.Series.Seasons)
                        {
                            if (!string.IsNullOrWhiteSpace(s.Title) && !fallbackTitles.Any(f => f.Item1.Equals(s.Title, StringComparison.OrdinalIgnoreCase)))
                            {
                                fallbackTitles.Add(Tuple.Create(s.Title, s.SeasonNumber));
                            }
                        }
                    }

                    if (searchCriteria.Series.Title != null && !fallbackTitles.Any(f => f.Item1.Equals(searchCriteria.Series.Title, StringComparison.OrdinalIgnoreCase)))
                    {
                        fallbackTitles.Add(Tuple.Create(searchCriteria.Series.Title, 1));
                    }

                    if (searchCriteria.Series.AlternateTitles != null && searchCriteria.Series.AlternateTitles.Any())
                    {
                        var firstAlt = searchCriteria.Series.AlternateTitles.First();
                        if (!fallbackTitles.Any(f => f.Item1.Equals(firstAlt, StringComparison.OrdinalIgnoreCase)))
                        {
                            fallbackTitles.Add(Tuple.Create(firstAlt, 1));
                        }
                    }

                    foreach (var fallback in fallbackTitles)
                    {
                        if (SearchCriteriaBase.TryGetCoreTitle(fallback.Item1, out var coreTitle) ||
                            SearchCriteriaBase.TryGetBaseTitle(fallback.Item1, out coreTitle))
                        {
                            var cleanCore = coreTitle.CleanForSearch();
                            if (SearchCriteriaBase.IsSafeTitle(cleanCore))
                            {
                                var allowed = cleanCore.GetAllowedEdits(cleanParsedTitle);
                                if (char.IsDigit(cleanParsedTitle[cleanParsedTitle.Length - 1]) == char.IsDigit(cleanCore[cleanCore.Length - 1]) &&
                                    Math.Abs(cleanCore.Length - cleanParsedTitle.Length) <= allowed &&
                                    cleanCore.LevenshteinDistance(cleanParsedTitle) <= allowed)
                                {
                                    _logger.Debug("Matched AniDB series '{0}' by relative-Levenshtein distance on core title '{1}' against parsed '{2}'",
                                        searchCriteria.Series.Title,
                                        coreTitle,
                                        parsedEpisodeInfo.SeriesTitle);

                                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Alias, fallback.Item2);
                                }

                                if (SearchCriteriaBase.TryGetCoreTitle(parsedEpisodeInfo.SeriesTitle, out var parsedCore) ||
                                    SearchCriteriaBase.TryGetBaseTitle(parsedEpisodeInfo.SeriesTitle, out parsedCore))
                                {
                                    var cleanParsedCore = parsedCore.CleanForSearch();
                                    if (SearchCriteriaBase.IsSafeTitle(cleanParsedCore))
                                    {
                                        var coreAllowed = cleanCore.GetAllowedEdits(cleanParsedCore);
                                        if (char.IsDigit(cleanParsedCore[cleanParsedCore.Length - 1]) == char.IsDigit(cleanCore[cleanCore.Length - 1]) &&
                                            Math.Abs(cleanCore.Length - cleanParsedCore.Length) <= coreAllowed &&
                                            cleanCore.LevenshteinDistance(cleanParsedCore) <= coreAllowed)
                                        {
                                            _logger.Debug("Matched AniDB series '{0}' by relative-Levenshtein distance on core title '{1}' against parsed core '{2}'",
                                                searchCriteria.Series.Title,
                                                coreTitle,
                                                parsedCore);

                                            return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Alias, fallback.Item2);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (tvdbId > 0 && tvdbId == searchCriteria.Series.TvdbId)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by TVDB ID {0}, an alias may be needed for: {1}", tvdbId, parsedEpisodeInfo.SeriesTitle)
                           .Property("TvdbId", tvdbId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("TvdbIdMatch", tvdbId.ToString(), parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Id);
                }

                if (tvRageId > 0 && tvRageId == searchCriteria.Series.TvRageId && tvdbId <= 0)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by TVRage ID {0}, an alias may be needed for: {1}", tvRageId, parsedEpisodeInfo.SeriesTitle)
                           .Property("TvRageId", tvRageId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("TvRageIdMatch", tvRageId.ToString(), parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Id);
                }

                if (imdbId.IsNotNullOrWhiteSpace() && imdbId.Equals(searchCriteria.Series.ImdbId, StringComparison.Ordinal) && tvdbId <= 0)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by IMDb ID {0}, an alias may be needed for: {1}", imdbId, parsedEpisodeInfo.SeriesTitle)
                           .Property("ImdbId", imdbId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("ImdbIdMatch", imdbId, parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Id);
                }
            }

            var matchType = SeriesMatchType.Unknown;
            series = _seriesService.FindByTitle(parsedEpisodeInfo.SeriesTitle);

            if (series != null)
            {
                matchType = SeriesMatchType.Title;
            }

            if (series == null && parsedEpisodeInfo.SeriesTitleInfo.AllTitles != null)
            {
                series = GetSeriesByAllTitles(parsedEpisodeInfo);
                matchType = SeriesMatchType.Title;
            }

            if (series == null && parsedEpisodeInfo.SeriesTitleInfo.Year > 0)
            {
                series = _seriesService.FindByTitle(parsedEpisodeInfo.SeriesTitleInfo.TitleWithoutYear, parsedEpisodeInfo.SeriesTitleInfo.Year);
                matchType = SeriesMatchType.Title;

                if (series == null)
                {
                    series = GetSeriesAliasTitleAndYear(parsedEpisodeInfo);
                    matchType = SeriesMatchType.Alias;
                }
            }

            if (series == null && tvdbId > 0)
            {
                series = _seriesService.FindByTvdbId(tvdbId);

                if (series != null)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by TVDB ID {0}, an alias may be needed for: {1}", tvdbId, parsedEpisodeInfo.SeriesTitle)
                           .Property("TvdbId", tvdbId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("TvdbIdMatch", tvdbId.ToString(), parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    matchType = SeriesMatchType.Id;
                }
            }

            if (series == null && tvRageId > 0 && tvdbId <= 0)
            {
                series = _seriesService.FindByTvRageId(tvRageId);

                if (series != null)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by TVRage ID {0}, an alias may be needed for: {1}", tvRageId, parsedEpisodeInfo.SeriesTitle)
                           .Property("TvRageId", tvRageId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("TvRageIdMatch", tvRageId.ToString(), parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    matchType = SeriesMatchType.Id;
                }
            }

            if (series == null && imdbId.IsNotNullOrWhiteSpace() && tvdbId <= 0)
            {
                series = _seriesService.FindByImdbId(imdbId);

                if (series != null)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by IMDb ID {0}, an alias may be needed for: {1}", imdbId, parsedEpisodeInfo.SeriesTitle)
                           .Property("ImdbId", imdbId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("ImdbIdMatch", imdbId, parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    matchType = SeriesMatchType.Id;
                }
            }

            if (series == null)
            {
                _logger.Debug("No matching series {0}", parsedEpisodeInfo.SeriesTitle);
                return null;
            }

            return new FindSeriesResult(series, matchType);
        }

        private Episode GetDailyEpisode(Series series, string airDate, int? part, SearchCriteriaBase searchCriteria)
        {
            Episode episodeInfo = null;

            if (searchCriteria != null)
            {
                episodeInfo = searchCriteria.Episodes.SingleOrDefault(
                    e => e.AirDate == airDate);
            }

            if (episodeInfo == null)
            {
                episodeInfo = _episodeService.FindEpisode(series.Id, airDate, part);
            }

            return episodeInfo;
        }

        private List<Episode> GetAnimeEpisodes(Series series, ParsedEpisodeInfo parsedEpisodeInfo, int seasonNumber, bool sceneSource, SearchCriteriaBase searchCriteria)
        {
            var result = new List<Episode>();

            var sceneSeasonNumber = _sceneMappingService.GetSceneSeasonNumber(parsedEpisodeInfo.SeriesTitle, parsedEpisodeInfo.ReleaseTitle);

            foreach (var absoluteEpisodeNumber in parsedEpisodeInfo.AbsoluteEpisodeNumbers)
            {
                var episodes = new List<Episode>();

                if (parsedEpisodeInfo.Special)
                {
                    var episode = _episodeService.FindEpisode(series.Id, 0, absoluteEpisodeNumber);
                    episodes.AddIfNotNull(episode);
                }
                else if (sceneSource)
                {
                    // Is there a reason why we excluded season 1 from this handling before?
                    // Might have something to do with the scene name to season number check
                    // If this needs to be reverted tests will need to be added
                    if (sceneSeasonNumber.HasValue)
                    {
                        episodes = _episodeService.FindEpisodesBySceneNumbering(series.Id, sceneSeasonNumber.Value, absoluteEpisodeNumber);

                        if (episodes.Empty())
                        {
                            var episode = _episodeService.FindEpisode(series.Id, sceneSeasonNumber.Value, absoluteEpisodeNumber);
                            episodes.AddIfNotNull(episode);
                        }
                    }
                    else if (parsedEpisodeInfo.SeasonNumber > 1 && parsedEpisodeInfo.EpisodeNumbers.Empty())
                    {
                        episodes = _episodeService.FindEpisodesBySceneNumbering(series.Id, parsedEpisodeInfo.SeasonNumber, absoluteEpisodeNumber);

                        if (episodes.Empty())
                        {
                            var episode = _episodeService.FindEpisode(series.Id, parsedEpisodeInfo.SeasonNumber, absoluteEpisodeNumber);
                            episodes.AddIfNotNull(episode);
                        }
                    }
                    else
                    {
                        episodes = _episodeService.FindEpisodesBySceneNumbering(series.Id, absoluteEpisodeNumber);

                        // Don't allow multiple results without a scene name mapping.
                        if (episodes.Count > 1)
                        {
                            episodes.Clear();
                        }
                    }
                }

                if (episodes.Empty())
                {
                    if (seasonNumber > 1)
                    {
                        var seasonEpisode = searchCriteria?.Episodes?.SingleOrDefault(e =>
                            e.SeasonNumber == seasonNumber &&
                            (e.EpisodeNumber == absoluteEpisodeNumber || e.AbsoluteEpisodeNumber == absoluteEpisodeNumber));

                        if (seasonEpisode != null)
                        {
                            episodes.Add(seasonEpisode);
                        }
                        else
                        {
                            seasonEpisode = _episodeService.FindEpisode(series.Id, seasonNumber, absoluteEpisodeNumber);
                            if (seasonEpisode != null)
                            {
                                episodes.Add(seasonEpisode);
                            }
                        }
                    }
                    else
                    {
                        var episode = _episodeService.FindEpisode(series.Id, absoluteEpisodeNumber);
                        episodes.AddIfNotNull(episode);
                    }
                }

                foreach (var episode in episodes)
                {
                    _logger.Debug("Using absolute episode number {0} for: {1} - TVDB: {2}x{3:00}",
                                absoluteEpisodeNumber,
                                series.Title,
                                episode.SeasonNumber,
                                episode.EpisodeNumber);

                    result.Add(episode);
                }
            }

            return result;
        }

        private List<Episode> GetStandardEpisodes(Series series, ParsedEpisodeInfo parsedEpisodeInfo, int mappedSeasonNumber, bool sceneSource, SearchCriteriaBase searchCriteria)
        {
            var result = new List<Episode>();

            if (parsedEpisodeInfo.EpisodeNumbers == null)
            {
                return new List<Episode>();
            }

            foreach (var episodeNumber in parsedEpisodeInfo.EpisodeNumbers)
            {
                if (series.UseSceneNumbering && sceneSource)
                {
                    var episodes = new List<Episode>();

                    if (searchCriteria != null)
                    {
                        episodes = searchCriteria.Episodes.Where(e => e.SceneSeasonNumber == parsedEpisodeInfo.SeasonNumber &&
                                                                      e.SceneEpisodeNumber == episodeNumber).ToList();
                    }

                    if (!episodes.Any())
                    {
                        episodes = _episodeService.FindEpisodesBySceneNumbering(series.Id, mappedSeasonNumber, episodeNumber);
                    }

                    if (episodes != null && episodes.Any())
                    {
                        _logger.Debug("Using Scene to TVDB Mapping for: {0} - Scene: {1}x{2:00} - TVDB: {3}",
                                    series.Title,
                                    episodes.First().SceneSeasonNumber,
                                    episodes.First().SceneEpisodeNumber,
                                    string.Join(", ", episodes.Select(e => string.Format("{0}x{1:00}", e.SeasonNumber, e.EpisodeNumber))));

                        result.AddRange(episodes);
                        continue;
                    }
                }

                Episode episodeInfo = null;

                if (searchCriteria != null)
                {
                    episodeInfo = searchCriteria.Episodes.SingleOrDefault(e => e.SeasonNumber == mappedSeasonNumber && e.EpisodeNumber == episodeNumber);
                }

                if (episodeInfo == null)
                {
                    episodeInfo = _episodeService.FindEpisode(series.Id, mappedSeasonNumber, episodeNumber);
                }

                if (episodeInfo != null)
                {
                    result.Add(episodeInfo);
                }
                else
                {
                    _logger.Debug("Unable to find {0}", parsedEpisodeInfo);
                }
            }

            return result;
        }

        private static readonly Regex BracketExtractorRegex = new Regex(
            @"[\[\(【（『「]([^\]\)】）』」]+)[\]\)】）』」]",
            RegexOptions.Compiled);

        private static readonly Regex SegmentSplitterRegex = new Regex(
            @"\s*[:：|/]\s*|\s+[-—–~～]\s+",
            RegexOptions.Compiled);

        private static readonly char[] WordTokenSeparators = new[]
        {
            ' ', '.', '_', '-', ':', '~', '–', '—', '/', '|', '(', ')', '[', ']', '{', '}', '"', '!', '?', ';', ',', '+'
        };

        private static List<string> TokenizeLatinWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            var normalized = text.Replace("'", "").Replace("’", "").Replace("`", "");

            return normalized.Split(WordTokenSeparators, StringSplitOptions.RemoveEmptyEntries)
                             .Select(w => w.Trim().ToLowerInvariant())
                             .Where(w => w.Length > 0)
                             .ToList();
        }

        private static List<string> GetTitleCandidateSegments(string seriesTitle, string releaseTitle)
        {
            var segments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddFrom(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                segments.Add(text);

                var matches = BracketExtractorRegex.Matches(text);
                foreach (Match match in matches)
                {
                    if (match.Success && match.Groups[1].Value.Length > 0)
                    {
                        segments.Add(match.Groups[1].Value);
                    }
                }

                var withoutBrackets = BracketExtractorRegex.Replace(text, " ");
                if (!string.IsNullOrWhiteSpace(withoutBrackets))
                {
                    segments.Add(withoutBrackets.Trim());

                    var parts = SegmentSplitterRegex.Split(withoutBrackets);
                    foreach (var part in parts)
                    {
                        var trimmed = part.Trim();
                        if (trimmed.Length > 0)
                        {
                            segments.Add(trimmed);
                        }
                    }
                }
            }

            AddFrom(seriesTitle);
            AddFrom(releaseTitle);

            return segments.ToList();
        }

        private static bool IsValidJapaneseSubstringMatch(string target, string alias)
        {
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(alias))
            {
                return false;
            }

            var idx = 0;
            while ((idx = target.IndexOf(alias, idx, StringComparison.Ordinal)) >= 0)
            {
                var nextIdx = idx + alias.Length;
                if (nextIdx < target.Length)
                {
                    var nextChar = target[nextIdx];
                    if (char.IsDigit(nextChar) && !char.IsDigit(alias[alias.Length - 1]))
                    {
                        idx = nextIdx;
                        continue;
                    }
                }

                if (idx > 0)
                {
                    var prevChar = target[idx - 1];
                    if (char.IsDigit(prevChar) && !char.IsDigit(alias[0]))
                    {
                        idx = nextIdx;
                        continue;
                    }
                }

                return true;
            }

            return false;
        }

        private static bool IsAliasMatch(ParsedEpisodeInfo parsedEpisodeInfo, string alias, string cleanAlias, string cleanParsedTitle)
        {
            if (parsedEpisodeInfo == null || string.IsNullOrWhiteSpace(alias))
            {
                return false;
            }

            if (cleanParsedTitle == cleanAlias)
            {
                return true;
            }

            var isJapanese = SearchCriteriaBase.IsNativeJapaneseTitle(alias);
            var candidateSegments = GetTitleCandidateSegments(parsedEpisodeInfo.SeriesTitle, parsedEpisodeInfo.ReleaseTitle);
            var aliasTokens = !isJapanese ? TokenizeLatinWords(alias) : null;

            foreach (var segment in candidateSegments)
            {
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                var cleanSegment = segment.CleanForSearch();
                if (cleanSegment.Length == 0)
                {
                    continue;
                }

                if (cleanSegment == cleanAlias)
                {
                    return true;
                }

                if (isJapanese)
                {
                    if (IsValidJapaneseSubstringMatch(cleanSegment, cleanAlias))
                    {
                        return true;
                    }
                }
                else if (aliasTokens != null && aliasTokens.Count > 0)
                {
                    var segmentTokens = TokenizeLatinWords(segment);
                    if (segmentTokens.Count >= aliasTokens.Count)
                    {
                        var matchesPrefix = true;
                        for (var i = 0; i < aliasTokens.Count; i++)
                        {
                            if (!segmentTokens[i].Equals(aliasTokens[i], StringComparison.OrdinalIgnoreCase))
                            {
                                matchesPrefix = false;
                                break;
                            }
                        }

                        if (matchesPrefix)
                        {
                            if (segmentTokens.Count > aliasTokens.Count)
                            {
                                var nextToken = segmentTokens[aliasTokens.Count];
                                if (nextToken.Length > 0 && char.IsDigit(nextToken[0]) && !char.IsDigit(aliasTokens[aliasTokens.Count - 1].LastOrDefault()))
                                {
                                    if (Regex.IsMatch(nextToken, @"^\d{3,4}p?$", RegexOptions.IgnoreCase))
                                    {
                                        return true;
                                    }

                                    continue;
                                }
                            }

                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
