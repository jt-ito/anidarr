using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Tv.Commands;

namespace NzbDrone.Core.Tv
{
    public enum HubOutcome
    {
        // Nothing to do (not an AniDB series, or the hub root could not be determined)
        Unchanged,

        // The series now is the hub (its AniDB id, season mappings and related series were updated)
        BecameHub,

        // The series was a later season of a hub already in the library, so it was removed
        MergedIntoExisting
    }

    public interface IAniDbHubReconciler
    {
        HubOutcome Reconcile(Series pending, Series hubInfo);
    }

    /// <summary>
    /// A series that was added without its AniDB hub resolved (added "now, details later", or AniDB
    /// was unreachable) points at a single AniDB entry, which may be season 2 or later. Once the
    /// real hub is known this makes the library match what a normal add produces: one series, the
    /// first season's entry as the hub, later seasons inside it.
    /// </summary>
    public class AniDbHubReconciler : IAniDbHubReconciler
    {
        // Two series resolving at once must not both decide to become the hub
        private static readonly object Lock = new object();

        private readonly ISeriesService _seriesService;
        private readonly IAniDbSeriesMappingService _mappingService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public AniDbHubReconciler(ISeriesService seriesService,
                                  IAniDbSeriesMappingService mappingService,
                                  IManageCommandQueue commandQueueManager,
                                  Logger logger)
        {
            _seriesService = seriesService;
            _mappingService = mappingService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public HubOutcome Reconcile(Series pending, Series hubInfo)
        {
            var root = hubInfo.AniDbId.GetValueOrDefault();

            if (root <= 0 || !(pending.AniDbId > 0))
            {
                return HubOutcome.Unchanged;
            }

            lock (Lock)
            {
                var others = _seriesService.GetAllSeries().Where(s => s.Id != pending.Id).ToList();
                var mappings = _mappingService.GetAllMappings();

                // Another library series already is this hub (or still points at its first season)...
                var hub = others.FirstOrDefault(s => s.AniDbId == root)

                          // ...or already lists this entry as one of its seasons
                          ?? others.FirstOrDefault(s => mappings.Any(m => m.SeriesId == s.Id && (m.AniDbId == pending.AniDbId || m.AniDbId == root)));

                if (hub != null)
                {
                    _logger.Info("'{0}' (AniDB {1}) is a later season of '{2}' (hub {3}); removing it and keeping the hub. Its folder and files are left untouched.", pending.Title, pending.AniDbId, hub.Title, root);

                    _seriesService.DeleteSeries(new List<int> { pending.Id }, deleteFiles: false, addImportListExclusion: false);

                    // A hub that was added before this one may not know about this season yet
                    if (!mappings.Any(m => m.SeriesId == hub.Id && m.AniDbId == pending.AniDbId))
                    {
                        _commandQueueManager.Push(new RefreshSeriesCommand(new List<int> { hub.Id }), CommandPriority.Low, CommandTrigger.Unspecified);
                    }

                    return HubOutcome.MergedIntoExisting;
                }

                // Nothing else claims the hub: this series becomes it, exactly as if it had been added normally
                if (pending.AniDbId != root)
                {
                    _logger.Info("'{0}' was added as AniDB {1}; its hub is AniDB {2}, which it now becomes.", pending.Title, pending.AniDbId, root);

                    pending.AniDbId = root;

                    // A placeholder tvdb id is derived from the AniDB id when a series has no real one
                    if (pending.TvdbId < 0)
                    {
                        pending.TvdbId = -root;
                    }
                }

                pending.AniDbMappings = hubInfo.AniDbMappings;
                pending.AniDbRelatedSeries = hubInfo.AniDbRelatedSeries;

                return HubOutcome.BecameHub;
            }
        }
    }
}
