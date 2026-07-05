using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataDispatcher
    {
        Tuple<Series, List<Episode>> GetSeriesInfo(Series series);
        List<Series> Search(string query, MetadataProviderType? filter = null);
    }

    /// <summary>
    /// Routes metadata requests to the appropriate provider based on series.PrimaryMetadataProvider.
    /// Falls back to TVDB if no provider is set (backward compatibility).
    /// </summary>
    public class MetadataDispatcher : IMetadataDispatcher
    {
        private readonly IEnumerable<IMetadataProvider> _providers;
        private readonly IAnimeOfflineDatabase _animeOfflineDatabase;
        private readonly Logger _logger;

        public MetadataDispatcher(IEnumerable<IMetadataProvider> providers, IAnimeOfflineDatabase animeOfflineDatabase, Logger logger)
        {
            _providers = providers;
            _animeOfflineDatabase = animeOfflineDatabase;
            _logger = logger;
        }

        public Tuple<Series, List<Episode>> GetSeriesInfo(Series series)
        {
            var isAniDbFallback = series.PrimaryMetadataProvider?.ToLowerInvariant() == "anidb";
            var tvdbProvider = _providers.First(p => p.ProviderType == MetadataProviderType.Tvdb);

            try
            {
                if (series.TvdbId > 0 || !isAniDbFallback)
                {
                    var tvdbId = series.TvdbId > 0 ? series.TvdbId.ToString() : ResolveExternalId(series, MetadataProviderType.Tvdb);
                    _logger.Debug("Fetching series info for '{0}' from Tvdb (id={1})", series.Title, tvdbId);

                    var result = tvdbProvider.GetSeriesInfo(tvdbId);

                    if (result.Item1 != null)
                    {
                        result.Item1.PrimaryMetadataProvider = "tvdb"; // Ensure it's marked as TVDB
                        _animeOfflineDatabase.UpdateMetadata(result.Item1);
                    }

                    return result;
                }
            }
            catch (NzbDrone.Core.Exceptions.SeriesNotFoundException)
            {
                _logger.Debug("Series '{0}' not found on TVDB. Will attempt AniDB fallback if applicable.", series.Title);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error fetching from TVDB for '{0}'", series.Title);
            }

            // Fallback to AniDB if it was already AniDB or we couldn't find it on TVDB and it has an AniDB ID
            var anidbProvider = _providers.FirstOrDefault(p => p.ProviderType == MetadataProviderType.AniDb);
            if (anidbProvider != null && series.AniDbId.HasValue && series.AniDbId.Value > 0)
            {
                var anidbId = series.AniDbId.Value.ToString();
                _logger.Debug("Fetching series info for '{0}' from AniDb fallback (id={1})", series.Title, anidbId);

                var result = anidbProvider.GetSeriesInfo(anidbId);

                if (result.Item1 != null)
                {
                    result.Item1.PrimaryMetadataProvider = "anidb"; // Tag as AniDB fallback
                    _animeOfflineDatabase.UpdateMetadata(result.Item1);
                }

                return result;
            }

            // If it fails on both or AniDB isn't available, we just let the original TVDB exception bubble up or throw a new one
            if (series.TvdbId > 0)
            {
                return tvdbProvider.GetSeriesInfo(series.TvdbId.ToString()); // Will throw the appropriate exception
            }

            throw new NzbDrone.Core.Exceptions.SeriesNotFoundException(series.TvdbId, "Series not found on TVDB and no AniDB fallback available.");
        }

        public List<Series> Search(string query, MetadataProviderType? filter = null)
        {
            var targets = filter.HasValue
                ? _providers.Where(p => p.ProviderType == filter.Value)
                : _providers;

            var results = new List<Series>();

            foreach (var provider in targets)
            {
                try
                {
                    var providerResults = provider.Search(query);
                    results.AddRange(providerResults);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Search failed for provider {0}: {1}", provider.GetType().Name, ex.Message);
                }
            }

            // Deduplicate by provider and ID
            return results
                .GroupBy(s => s.PrimaryMetadataProvider switch
                {
                    "anidb" => $"anidb:{s.AniDbId}",
                    "simkl" => $"simkl:{s.SimklId}",
                    "anilist" => $"anilist:{s.AniListIds?.FirstOrDefault()}",
                    "mal" => $"mal:{s.MalIds?.FirstOrDefault()}",
                    "tvdb" => s.TvdbId > 0 ? $"tvdb:{s.TvdbId}" : $"title:{s.CleanTitle}",
                    _ => $"title:{s.CleanTitle}"
                })
                .Select(g => g.First())
                .ToList();
        }

        private static string ResolveExternalId(Series series, MetadataProviderType providerType)
        {
            return providerType switch
            {
                MetadataProviderType.Tvdb => series.TvdbId.ToString(),
                MetadataProviderType.AniDb => series.AniDbId?.ToString() ?? throw new InvalidOperationException($"Series '{series.Title}' has no AniDB ID"),
                MetadataProviderType.AniList => series.AniListIds?.FirstOrDefault().ToString() ?? throw new InvalidOperationException($"Series '{series.Title}' has no AniList ID"),
                MetadataProviderType.Simkl => series.SimklId?.ToString() ?? throw new InvalidOperationException($"Series '{series.Title}' has no Simkl ID"),
                MetadataProviderType.Mal => series.MalIds?.FirstOrDefault().ToString() ?? throw new InvalidOperationException($"Series '{series.Title}' has no MAL ID"),
                _ => throw new ArgumentOutOfRangeException(nameof(providerType), providerType, null)
            };
        }
    }
}
