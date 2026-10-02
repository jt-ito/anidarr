using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Tv
{
    public interface IAddSeriesService
    {
        Series AddSeries(Series newSeries);
        List<Series> AddSeries(List<Series> newSeries, bool ignoreErrors = false);
        List<AddSeriesOutcome> AddSeriesBackground(List<Series> newSeries, bool deferMetadata);
    }

    // What happened to one series of an add: Added is set when it was saved, otherwise Reason says why not
    public class AddSeriesOutcome
    {
        public Series Requested { get; set; }

        public Series Added { get; set; }

        public string Reason { get; set; }
    }

    public class AddSeriesService : IAddSeriesService
    {
        private readonly ISeriesService _seriesService;
        private readonly IMetadataDispatcher _metadataDispatcher;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IRefreshEpisodeService _refreshEpisodeService;
        private readonly IAddSeriesValidator _addSeriesValidator;
        private readonly MediaCover.IMapCoversToLocal _mediaCoverService;
        private readonly IAniDbSeriesMappingService _aniDbSeriesMappingService;
        private readonly Messaging.Events.IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public AddSeriesService(ISeriesService seriesService,
                                IMetadataDispatcher metadataDispatcher,
                                IBuildFileNames fileNameBuilder,
                                IRefreshEpisodeService refreshEpisodeService,
                                IAddSeriesValidator addSeriesValidator,
                                Logger logger,
                                Messaging.Events.IEventAggregator eventAggregator = null,
                                MediaCover.IMapCoversToLocal mediaCoverService = null,
                                IAniDbSeriesMappingService aniDbSeriesMappingService = null)
        {
            _aniDbSeriesMappingService = aniDbSeriesMappingService;
            _seriesService = seriesService;
            _metadataDispatcher = metadataDispatcher;
            _fileNameBuilder = fileNameBuilder;
            _refreshEpisodeService = refreshEpisodeService;
            _addSeriesValidator = addSeriesValidator;
            _mediaCoverService = mediaCoverService;
            _logger = logger;
            _eventAggregator = eventAggregator;
        }

        public Series AddSeries(Series newSeries)
        {
            Ensure.That(newSeries, () => newSeries).IsNotNull();

            MetadataSource.AniDb.AniDbRateLimiter.IsManualContext.Value = true;
            MetadataSource.AniList.AniListRateLimiter.IsManualContext.Value = true;

            _eventAggregator?.PublishEvent(new Events.SeriesAddProgressEvent("Preparing series metadata...", newSeries.AniDbId));

            var (seriesData, episodes, isIncomplete) = AddSkyhookData(newSeries);
            seriesData = SetPropertiesAndValidate(seriesData);

            // ponytail: always record fetch time so RefreshSeriesService's "alreadyFresh"
            // guard skips the redundant AniDB API call — even when AniDB returns zero
            // episodes (e.g. during a temporary ban).
            // Exception: when the provider could not be reached (e.g. an AniDB ban) the series was
            // added from the data we already had. Leave LastInfoSync empty so the next refresh fills in
            // the hub, seasons and episodes instead of treating it as up to date.
            seriesData.LastInfoSync = isIncomplete ? (DateTime?)null : DateTime.UtcNow;

            _eventAggregator?.PublishEvent(new Events.SeriesAddProgressEvent($"Saving series and {episodes.Count} episode(s) to library...", newSeries.AniDbId));
            _logger.Info("Adding Series {0} Path: [{1}]", seriesData, seriesData.Path);
            _seriesService.AddSeries(seriesData);

            if (episodes.Any())
            {
                _refreshEpisodeService.RefreshEpisodeInfo(seriesData, episodes);
            }

            try
            {
                _mediaCoverService?.EnsureCovers(seriesData);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to ensure covers during AddSeries for {0}", seriesData);
            }

            _eventAggregator?.PublishEvent(new Events.SeriesAddProgressEvent("Series successfully added!", newSeries.AniDbId));
            return seriesData;
        }

        public List<Series> AddSeries(List<Series> newSeries, bool ignoreErrors = false)
        {
            return AddSeriesCore(newSeries, ignoreErrors, isManual: true, deferMetadata: false)
                .Where(o => o.Added != null)
                .Select(o => o.Added)
                .ToList();
        }

        // Import job: runs at background priority (anything the user does by hand goes first) and
        // reports what happened to each series. With deferMetadata, AniDB-only series are added from
        // the lookup data alone, and their hub, seasons and episodes are filled in by a background refresh.
        public List<AddSeriesOutcome> AddSeriesBackground(List<Series> newSeries, bool deferMetadata)
        {
            return AddSeriesCore(newSeries, ignoreErrors: true, isManual: false, deferMetadata);
        }

        private List<AddSeriesOutcome> AddSeriesCore(List<Series> newSeries, bool ignoreErrors, bool isManual, bool deferMetadata)
        {
            MetadataSource.AniDb.AniDbRateLimiter.IsManualContext.Value = isManual;
            MetadataSource.AniList.AniListRateLimiter.IsManualContext.Value = isManual;

            var added = DateTime.UtcNow;
            var outcomes = newSeries.Select(s => new AddSeriesOutcome { Requested = s }).ToList();

            // ponytail: parallel episodes list so we can persist after bulk insert
            var seriesToAdd = new List<Series>();
            var episodesForSeries = new List<List<Episode>>();
            var outcomeForSeries = new List<AddSeriesOutcome>();
            var existingSeries = _seriesService.GetAllSeries();

            foreach (var outcome in outcomes)
            {
                var s = outcome.Requested;

                if (s.Path.IsNullOrWhiteSpace())
                {
                    _logger.Info("Adding Series {0} Root Folder Path: [{1}]", s, s.RootFolderPath);
                }
                else
                {
                    _logger.Info("Adding Series {0} Path: [{1}]", s, s.Path);
                }

                try
                {
                    var (series, episodes, isIncomplete) = deferMetadata && CanDefer(s) ? PrepareDeferred(s) : AddSkyhookData(s);
                    series = SetPropertiesAndValidate(series);
                    series.Added = added;

                    if (IsDuplicate(series, existingSeries))
                    {
                        _logger.Debug("Series {0} was not added due to validation failure: Series already exists in database", s);
                        outcome.Reason = "Already in your library (it is the same series, or a season of one)";
                        continue;
                    }

                    if (IsDuplicate(series, seriesToAdd))
                    {
                        _logger.Trace("Series {0} was already added from another import list, not adding again", s);
                        outcome.Reason = "Another folder in this import is the same series";
                        continue;
                    }

                    var duplicateSlug = seriesToAdd.FirstOrDefault(f => f.TitleSlug == series.TitleSlug);
                    if (duplicateSlug != null)
                    {
                        _logger.Debug("Series {0} was not added due to validation failure: Duplicate Slug {1} used by series {2}", GetProviderDisplay(s), s.TitleSlug, GetProviderDisplay(duplicateSlug));
                        outcome.Reason = "Another series in this import has the same name";
                        continue;
                    }

                    // ponytail: record fetch time so RefreshSeriesService's "alreadyFresh"
                    // guard skips the redundant AniDB API call. Not for series added without
                    // their metadata (deferred, or the provider was unreachable): leaving it empty is
                    // what makes the next refresh fetch the hub, seasons and episodes.
                    series.LastInfoSync = isIncomplete ? (DateTime?)null : DateTime.UtcNow;

                    seriesToAdd.Add(series);
                    episodesForSeries.Add(episodes);
                    outcomeForSeries.Add(outcome);
                }
                catch (ValidationException ex)
                {
                    if (!ignoreErrors)
                    {
                        throw;
                    }

                    outcome.Reason = string.Join("; ", ex.Errors.Select(e => e.ErrorMessage));
                    _logger.Debug("Series {0} with TVDB ID {1} was not added due to validation failures. {2}", s, s.TvdbId, ex.Message);
                }
            }

            var addedSeries = _seriesService.AddSeries(seriesToAdd);

            // Persist episodes and stamp LastInfoSync so RefreshSeriesService
            // skips the AniDB API call for each newly-added series.
            for (var i = 0; i < addedSeries.Count && i < episodesForSeries.Count; i++)
            {
                var series = addedSeries[i];
                var eps = episodesForSeries[i];

                outcomeForSeries[i].Added = series;

                if (eps.Any())
                {
                    _refreshEpisodeService.RefreshEpisodeInfo(series, eps);
                }

                try
                {
                    _mediaCoverService?.EnsureCovers(series);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to ensure covers during bulk AddSeries for {0}", series);
                }
            }

            return outcomes;
        }

        // AniDB-only series can be added from the lookup data; anything with a TVDB id is quick to fetch anyway
        private static bool CanDefer(Series series)
        {
            return series.AniDbId > 0 && series.TvdbId <= 0;
        }

        private static (Series Series, List<Episode> Episodes, bool IsIncomplete) PrepareDeferred(Series series)
        {
            series.PrimaryMetadataProvider = "anidb";

            return (series, new List<Episode>(), true);
        }

        private (Series Series, List<Episode> Episodes, bool IsIncomplete) AddSkyhookData(Series newSeries)
        {
            Tuple<Series, List<Episode>> tuple;
            var isIncomplete = false;

            try
            {
                tuple = _metadataDispatcher.GetSeriesInfo(newSeries);
            }
            catch (SeriesNotFoundException)
            {
                _logger.Error("Series {0} was not found using its primary metadata provider. Path: {1}", newSeries, newSeries.Path);

                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new ValidationFailure("", $"A series with this ID was not found. Path: {newSeries.Path}", newSeries.TvdbId)
                                              });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to retrieve series information from primary metadata provider. Proceeding with empty metadata due to provider ban or outage. Path: {0}", newSeries.Path);

                // If AniDB bans us, we still want to add the series to the database.
                // Episodes will be populated by the scheduled background refresh once the ban lifts.
                tuple = Tuple.Create(newSeries, new List<Episode>());
                isIncomplete = true;
            }

            var series = tuple.Item1;
            var episodes = tuple.Item2;

            // If seasons were passed in on the new series use them, otherwise use the seasons from provider
            newSeries.Seasons = newSeries.Seasons != null && newSeries.Seasons.Any() ? newSeries.Seasons : series.Seasons;

            series.ApplyChanges(newSeries);

            return (series, episodes, isIncomplete);
        }

        private Series SetPropertiesAndValidate(Series newSeries)
        {
            if (string.IsNullOrWhiteSpace(newSeries.Path))
            {
                var folderName = _fileNameBuilder.GetSeriesFolder(newSeries);
                newSeries.Path = Path.Combine(newSeries.RootFolderPath, folderName);
            }

            if (newSeries.PrimaryMetadataProvider == "anidb" ||
                newSeries.PrimaryMetadataProvider == "mal" ||
                newSeries.PrimaryMetadataProvider == "anilist")
            {
                newSeries.SeriesType = SeriesTypes.Anime;
            }

            newSeries.CleanTitle = newSeries.Title.CleanSeriesTitle();
            newSeries.SortTitle = SeriesTitleNormalizer.Normalize(newSeries.Title, newSeries.PrimaryMetadataProvider == "anidb" ? 0 : newSeries.TvdbId);
            if (string.IsNullOrWhiteSpace(newSeries.TitleSlug))
            {
                newSeries.TitleSlug = newSeries.Title.ToUrlSlug();
            }

            if (newSeries.TvdbId == 0)
            {
                if (newSeries.AniDbId.HasValue && newSeries.AniDbId.Value > 0)
                {
                    newSeries.TvdbId = -newSeries.AniDbId.Value;
                }
                else if (newSeries.AniListIds != null && newSeries.AniListIds.Any())
                {
                    newSeries.TvdbId = -(newSeries.AniListIds.First() + 1000000);
                }
                else if (newSeries.MalIds != null && newSeries.MalIds.Any())
                {
                    newSeries.TvdbId = -(newSeries.MalIds.First() + 2000000);
                }
            }

            var isDuplicateSlug = _seriesService.GetAllSeries().Any(s => s.Id != newSeries.Id && s.TitleSlug == newSeries.TitleSlug && s.TvdbId != newSeries.TvdbId);
            if (isDuplicateSlug)
            {
                newSeries.TitleSlug = $"{newSeries.Title.ToUrlSlug()}-{newSeries.Year}";
                isDuplicateSlug = _seriesService.GetAllSeries().Any(s => s.Id != newSeries.Id && s.TitleSlug == newSeries.TitleSlug && s.TvdbId != newSeries.TvdbId);

                if (isDuplicateSlug)
                {
                    var providerId = newSeries.AniDbId > 0 ? newSeries.AniDbId.Value : newSeries.TvdbId;
                    newSeries.TitleSlug = $"{newSeries.Title.ToUrlSlug()}-{newSeries.PrimaryMetadataProvider}-{providerId}";
                }
            }

            newSeries.Added = DateTime.UtcNow;

            if (newSeries.AddOptions != null && newSeries.AddOptions.Monitor == MonitorTypes.None)
            {
                newSeries.Monitored = false;
            }

            var validationResult = _addSeriesValidator.Validate(newSeries);

            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }

            return newSeries;
        }

        private bool IsDuplicate(Series candidate, IEnumerable<Series> existingSeries)
        {
            var existing = existingSeries.ToList();

            return existing.Any(series => candidate.PrimaryMetadataProvider switch
            {
                "anidb" => candidate.AniDbId.HasValue && candidate.AniDbId == series.AniDbId,
                "anilist" => candidate.AniListIds != null && series.AniListIds != null && candidate.AniListIds.Intersect(series.AniListIds).Any(),
                "mal" => candidate.MalIds != null && series.MalIds != null && candidate.MalIds.Intersect(series.MalIds).Any(),
                _ => candidate.TvdbId == series.TvdbId && series.TvdbId > 0,
            }) || IsSeasonOfExistingHub(candidate, existing);
        }

        // A later season is not its own series: if a hub already in the library lists this AniDB entry, it is covered
        private bool IsSeasonOfExistingHub(Series candidate, List<Series> existing)
        {
            if (!(candidate.AniDbId > 0) || _aniDbSeriesMappingService == null)
            {
                return false;
            }

            try
            {
                var mapping = _aniDbSeriesMappingService.GetMappingByAniDbId(candidate.AniDbId.Value);

                return mapping != null && mapping.SeriesId > 0 && existing.Any(s => s.Id == mapping.SeriesId);
            }
            catch (InvalidOperationException)
            {
                // more than one mapping row for this entry: don't guess
                return false;
            }
        }

        private static string GetProviderDisplay(Series series)
        {
            var provider = series.PrimaryMetadataProvider?.ToLowerInvariant() ?? "tvdb";
            var id = provider switch
            {
                "anidb" => series.AniDbId?.ToString(),
                "anilist" => series.AniListIds?.FirstOrDefault().ToString(),
                "mal" => series.MalIds?.FirstOrDefault().ToString(),
                _ => series.TvdbId.ToString()
            };
            return $"{provider}:{id}";
        }
    }
}
