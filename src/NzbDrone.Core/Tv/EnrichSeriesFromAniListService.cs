using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Tv.Commands;

namespace NzbDrone.Core.Tv
{
    public class EnrichSeriesFromAniListService : IExecute<EnrichSeriesFromAniListCommand>
    {
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IAniDbSeriesMappingService _mappingService;
        private readonly IAnimeOfflineDatabase _titleSearch;
        private readonly IAniListEnricher _aniListEnricher;
        private readonly Logger _logger;

        public EnrichSeriesFromAniListService(
            ISeriesService seriesService,
            IEpisodeService episodeService,
            IAniDbSeriesMappingService mappingService,
            IAnimeOfflineDatabase titleSearch,
            IAniListEnricher aniListEnricher,
            Logger logger)
        {
            _seriesService = seriesService;
            _episodeService = episodeService;
            _mappingService = mappingService;
            _titleSearch = titleSearch;
            _aniListEnricher = aniListEnricher;
            _logger = logger;
        }

        public void Execute(EnrichSeriesFromAniListCommand message)
        {
            var series = _seriesService.GetSeries(message.SeriesId);
            if (series == null)
            {
                return;
            }

            _logger.Info("Enriching {0} with AniList data", series.Title);

            var mappings = _mappingService.GetMappingsForSeries(series.Id);
            var episodes = _episodeService.GetEpisodeBySeries(series.Id);

            var aniListIds = new HashSet<int>();
            if (series.AniListIds != null)
            {
                foreach (var id in series.AniListIds)
                {
                    aniListIds.Add(id);
                }
            }

            var seasonToAniListId = new Dictionary<int, int>();

            foreach (var mapping in mappings)
            {
                var local = _titleSearch.GetSeriesById("anidb", mapping.AniDbId);
                var currentAniListId = local?.AniListId;

                if (!currentAniListId.HasValue && !string.IsNullOrWhiteSpace(series.Title))
                {
                    var seasonEpisodes = episodes.Where(e => e.SeasonNumber == mapping.SeasonNumber).ToList();
                    var expectedYear = series.Year > 0 ? series.Year : (seasonEpisodes.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.AirDate))?.AirDateUtc?.Year ?? 0);
                    var expectedCount = seasonEpisodes.Count(e => e.SeasonNumber > 0);

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
                            foreach (var syn in local.SearchSynonyms)
                            {
                                if (!string.IsNullOrWhiteSpace(syn) && !fallbackTitles.Contains(syn))
                                {
                                    fallbackTitles.Add(syn);
                                }
                            }
                        }
                    }

                    if (!fallbackTitles.Contains(series.Title))
                    {
                        fallbackTitles.Add(series.Title);
                    }

                    foreach (var title in fallbackTitles)
                    {
                        if (_aniListEnricher.IsRateLimited)
                        {
                            break;
                        }

                        currentAniListId = _aniListEnricher.SearchAniListIdByTitle(title, expectedYear, expectedCount > 0 ? expectedCount : (int?)null);
                        if (currentAniListId.HasValue)
                        {
                            _titleSearch.UpdateAniListId(mapping.AniDbId, currentAniListId.Value);
                            break;
                        }
                    }
                }

                if (currentAniListId.HasValue)
                {
                    aniListIds.Add(currentAniListId.Value);
                    seasonToAniListId[mapping.SeasonNumber] = currentAniListId.Value;
                }
            }

            var idList = aniListIds.Distinct().ToList();
            if (!idList.Any())
            {
                _logger.Debug("No AniList IDs resolved for series {0}. Enrichment complete.", series.Title);
                return;
            }

            series.AniListIds = new HashSet<int>(idList);

            AniListEnrichmentData enrichment = null;
            try
            {
                enrichment = _aniListEnricher.GetEnrichmentForMultiple(idList);
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to fetch AniList batch enrichment for {0}: {1}", series.Title, ex.Message);
            }

            var allAiringTimes = enrichment?.AiringTimes ?? new Dictionary<int, Dictionary<int, TimeSpan>>();
            var allTitles = enrichment?.Titles ?? new Dictionary<int, List<string>>();

            // Update AlternateTitles from AniList
            var existingCleanTitles = new HashSet<string>(
                (series.AlternateTitles ?? new List<string>()).Select(t => t.CleanForSearch()));

            var seriesUpdated = false;
            foreach (var titles in allTitles.Values)
            {
                foreach (var title in titles)
                {
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }

                    var clean = title.CleanForSearch();
                    if (existingCleanTitles.Add(clean))
                    {
                        series.AlternateTitles.Add(title);
                        seriesUpdated = true;
                    }
                }
            }

            TimeSpan? globalDefaultTime = null;
            var allTimes = allAiringTimes.Values.SelectMany(x => x.Values).ToList();
            if (allTimes.Any())
            {
                globalDefaultTime = allTimes.GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
            }

            // Update episode airtimes
            var episodesToUpdate = new List<Episode>();
            foreach (var episode in episodes)
            {
                if (string.IsNullOrWhiteSpace(episode.AirDate))
                {
                    continue;
                }

                var timeOfDay = TimeSpan.Zero;
                var hasTime = false;

                if (seasonToAniListId.TryGetValue(episode.SeasonNumber, out var alId) &&
                    allAiringTimes.TryGetValue(alId, out var seasonTimes) && seasonTimes.Any())
                {
                    if (episode.AbsoluteEpisodeNumber.HasValue && seasonTimes.TryGetValue(episode.AbsoluteEpisodeNumber.Value, out var absTime))
                    {
                        timeOfDay = absTime;
                        hasTime = true;
                    }
                    else if (seasonTimes.TryGetValue(episode.EpisodeNumber, out var relTime))
                    {
                        timeOfDay = relTime;
                        hasTime = true;
                    }
                    else
                    {
                        timeOfDay = seasonTimes.Values.GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
                        hasTime = true;
                    }
                }
                else if (globalDefaultTime.HasValue)
                {
                    timeOfDay = globalDefaultTime.Value;
                    hasTime = true;
                }

                if (hasTime && episode.AirDateUtc.HasValue)
                {
                    if (DateTime.TryParse(episode.AirDate, out var jstDate))
                    {
                        var preciseJstTime = jstDate.Add(timeOfDay);
                        var newAirDateUtc = DateTime.SpecifyKind(preciseJstTime.AddHours(-9), DateTimeKind.Utc);

                        if (episode.AirDateUtc != newAirDateUtc)
                        {
                            episode.AirDateUtc = newAirDateUtc;
                            episodesToUpdate.Add(episode);
                        }
                    }
                }
            }

            if (episodesToUpdate.Any())
            {
                _episodeService.UpdateEpisodes(episodesToUpdate);
                _logger.Debug("Updated airing times for {0} episode(s) from AniList", episodesToUpdate.Count);
            }

            if (seriesUpdated || series.AniListIds.Any())
            {
                _seriesService.UpdateSeries(series, publishUpdatedEvent: true);
                _logger.Debug("Updated series {0} with AniList alternate titles and IDs", series.Title);
            }

            _logger.Info("AniList enrichment complete for {0}", series.Title);
        }
    }
}
