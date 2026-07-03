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
    }

    public class AddSeriesService : IAddSeriesService
    {
        private readonly ISeriesService _seriesService;
        private readonly IMetadataDispatcher _metadataDispatcher;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IRefreshEpisodeService _refreshEpisodeService;
        private readonly IAddSeriesValidator _addSeriesValidator;
        private readonly Logger _logger;

        public AddSeriesService(ISeriesService seriesService,
                                IMetadataDispatcher metadataDispatcher,
                                IBuildFileNames fileNameBuilder,
                                IRefreshEpisodeService refreshEpisodeService,
                                IAddSeriesValidator addSeriesValidator,
                                Logger logger)
        {
            _seriesService = seriesService;
            _metadataDispatcher = metadataDispatcher;
            _fileNameBuilder = fileNameBuilder;
            _refreshEpisodeService = refreshEpisodeService;
            _addSeriesValidator = addSeriesValidator;
            _logger = logger;
        }

        public Series AddSeries(Series newSeries)
        {
            Ensure.That(newSeries, () => newSeries).IsNotNull();

            var (seriesData, episodes) = AddSkyhookData(newSeries);
            seriesData = SetPropertiesAndValidate(seriesData);

            // ponytail: always record fetch time so RefreshSeriesService's "alreadyFresh"
            // guard skips the redundant AniDB API call — even when AniDB returns zero
            // episodes (e.g. during a temporary ban).
            seriesData.LastInfoSync = DateTime.UtcNow;

            _logger.Info("Adding Series {0} Path: [{1}]", seriesData, seriesData.Path);
            _seriesService.AddSeries(seriesData);

            if (episodes.Any())
            {
                _refreshEpisodeService.RefreshEpisodeInfo(seriesData, episodes);
            }

            return seriesData;
        }

        public List<Series> AddSeries(List<Series> newSeries, bool ignoreErrors = false)
        {
            var added = DateTime.UtcNow;

            // ponytail: parallel episodes list so we can persist after bulk insert
            var seriesToAdd = new List<Series>();
            var episodesForSeries = new List<List<Episode>>();
            var existingSeries = _seriesService.GetAllSeries();

            foreach (var s in newSeries)
            {
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
                    var (series, episodes) = AddSkyhookData(s);
                    series = SetPropertiesAndValidate(series);
                    series.Added = added;

                    if (IsDuplicate(series, existingSeries))
                    {
                        _logger.Debug("Series {0} was not added due to validation failure: Series already exists in database", s);
                        continue;
                    }

                    if (IsDuplicate(series, seriesToAdd))
                    {
                        _logger.Trace("Series {0} was already added from another import list, not adding again", s);
                        continue;
                    }

                    var duplicateSlug = seriesToAdd.FirstOrDefault(f => f.TitleSlug == series.TitleSlug);
                    if (duplicateSlug != null)
                    {
                        _logger.Debug("Series {0} was not added due to validation failure: Duplicate Slug {1} used by series {2}", GetProviderDisplay(s), s.TitleSlug, GetProviderDisplay(duplicateSlug));
                        continue;
                    }

                    // ponytail: always record fetch time so RefreshSeriesService's "alreadyFresh"
                    // guard skips the redundant AniDB API call — even when AniDB returns zero
                    // episodes (e.g. during a temporary ban).
                    series.LastInfoSync = DateTime.UtcNow;

                    seriesToAdd.Add(series);
                    episodesForSeries.Add(episodes);
                }
                catch (ValidationException ex)
                {
                    if (!ignoreErrors)
                    {
                        throw;
                    }

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

                if (eps.Any())
                {
                    _refreshEpisodeService.RefreshEpisodeInfo(series, eps);
                }
            }

            return addedSeries;
        }

        private (Series Series, List<Episode> Episodes) AddSkyhookData(Series newSeries)
        {
            Tuple<Series, List<Episode>> tuple;

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
            }

            var series = tuple.Item1;
            var episodes = tuple.Item2;

            // If seasons were passed in on the new series use them, otherwise use the seasons from provider
            newSeries.Seasons = newSeries.Seasons != null && newSeries.Seasons.Any() ? newSeries.Seasons : series.Seasons;

            series.ApplyChanges(newSeries);

            return (series, episodes);
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
                newSeries.PrimaryMetadataProvider == "anilist" ||
                newSeries.PrimaryMetadataProvider == "simkl")
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
                else if (newSeries.SimklId.HasValue && newSeries.SimklId.Value > 0)
                {
                    newSeries.TvdbId = -(newSeries.SimklId.Value + 3000000);
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

        private static bool IsDuplicate(Series candidate, IEnumerable<Series> existingSeries)
        {
            return existingSeries.Any(series => candidate.PrimaryMetadataProvider switch
            {
                "anidb" => candidate.AniDbId.HasValue && candidate.AniDbId == series.AniDbId,
                "simkl" => candidate.SimklId.HasValue && candidate.SimklId == series.SimklId,
                "anilist" => candidate.AniListIds != null && series.AniListIds != null && candidate.AniListIds.Intersect(series.AniListIds).Any(),
                "mal" => candidate.MalIds != null && series.MalIds != null && candidate.MalIds.Intersect(series.MalIds).Any(),
                _ => candidate.TvdbId == series.TvdbId && series.TvdbId > 0,
            });
        }

        private static string GetProviderDisplay(Series series)
        {
            var provider = series.PrimaryMetadataProvider?.ToLowerInvariant() ?? "tvdb";
            var id = provider switch
            {
                "anidb" => series.AniDbId?.ToString(),
                "simkl" => series.SimklId?.ToString(),
                "anilist" => series.AniListIds?.FirstOrDefault().ToString(),
                "mal" => series.MalIds?.FirstOrDefault().ToString(),
                _ => series.TvdbId.ToString()
            };
            return $"{provider}:{id}";
        }
    }
}
