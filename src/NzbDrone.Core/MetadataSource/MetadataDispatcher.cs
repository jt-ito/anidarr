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
            var providerKey = series.PrimaryMetadataProvider?.ToLowerInvariant() ?? "tvdb";
            var provider = _providers.FirstOrDefault(p => p.CanHandleId(providerKey));

            if (provider == null)
            {
                _logger.Warn("No metadata provider found for key '{0}', falling back to TVDB", providerKey);
                provider = _providers.First(p => p.ProviderType == MetadataProviderType.Tvdb);
            }

            // Resolve the right external ID for the selected provider
            var externalId = ResolveExternalId(series, provider.ProviderType);

            _logger.Debug("Fetching series info for '{0}' from {1} (id={2})", series.Title, provider.ProviderType, externalId);
            var result = provider.GetSeriesInfo(externalId);

            if (result.Item1 != null)
            {
                _animeOfflineDatabase.UpdateMetadata(result.Item1);
            }

            return result;
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
