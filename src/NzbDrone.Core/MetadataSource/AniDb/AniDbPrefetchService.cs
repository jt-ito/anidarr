using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    public interface IAniDbPrefetchService
    {
        void Enqueue(int aniDbId);
    }

    /// <summary>
    /// While the user reviews an import, fetches the AniDB hub data (the prequel chain) for the series
    /// that matched, so adding them later is served from the cache instead of the AniDB API. Strictly
    /// background work: lowest priority in the central limiter, one at a time, and it stops as soon as
    /// AniDB reports a ban.
    /// </summary>
    public class AniDbPrefetchService : IAniDbPrefetchService
    {
        private const int MaxQueued = 5000;

        private readonly IMetadataDispatcher _metadataDispatcher;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly Logger _logger;

        private readonly object _lock = new object();
        private readonly Queue<int> _queue = new Queue<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private bool _isRunning;

        public AniDbPrefetchService(IMetadataDispatcher metadataDispatcher, IConfigFileProvider configFileProvider, Logger logger)
        {
            _metadataDispatcher = metadataDispatcher;
            _configFileProvider = configFileProvider;
            _logger = logger;
        }

        public void Enqueue(int aniDbId)
        {
            lock (_lock)
            {
                if (aniDbId <= 0 || _queue.Count >= MaxQueued || !_seen.Add(aniDbId))
                {
                    return;
                }

                _queue.Enqueue(aniDbId);

                if (_isRunning)
                {
                    return;
                }

                _isRunning = true;
            }

            Task.Run(Work);
        }

        private void Work()
        {
            // Bulk work: anything the user triggers by hand goes first
            AniDb.AniDbRateLimiter.IsManualContext.Value = false;
            AniList.AniListRateLimiter.IsManualContext.Value = false;

            while (true)
            {
                int aniDbId;

                lock (_lock)
                {
                    if (_queue.Count == 0)
                    {
                        _isRunning = false;
                        return;
                    }

                    aniDbId = _queue.Dequeue();
                }

                if (!_configFileProvider.IsAniDbClientConfigured)
                {
                    Stop("the AniDB client name and version are not set (Settings > Metadata Source)");
                    return;
                }

                if (IsBanned())
                {
                    Stop("AniDB is rate limiting this client");
                    return;
                }

                try
                {
                    // The same call adding the series makes; it leaves the XML in the AniDB cache
                    _metadataDispatcher.GetSeriesInfo(new Series { AniDbId = aniDbId, PrimaryMetadataProvider = "anidb" });
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Could not prefetch AniDB {0}", aniDbId);

                    if (IsBanned() || ex.Message.Contains("ban", StringComparison.OrdinalIgnoreCase))
                    {
                        Stop("AniDB reported a ban");
                        return;
                    }
                }
            }
        }

        private bool IsBanned()
        {
            var expiration = _configFileProvider.AniDbBanExpiration;

            return expiration.HasValue && expiration.Value > DateTime.UtcNow;
        }

        // Forget what was waiting so it can be requested again once AniDB allows it
        private void Stop(string reason)
        {
            lock (_lock)
            {
                _logger.Info("Stopping AniDB prefetch: {0}. {1} queued series skipped.", reason, _queue.Count);

                _seen.ExceptWith(_queue);
                _queue.Clear();
                _isRunning = false;
            }
        }
    }
}
