using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Commands;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public class SeriesAddedHandler : IHandle<SeriesAddedEvent>,
                                      IHandle<SeriesImportedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ISeriesService _seriesService;
        private readonly IPendingMetadataTracker _pendingMetadataTracker;

        public SeriesAddedHandler(IManageCommandQueue commandQueueManager, ISeriesService seriesService, IPendingMetadataTracker pendingMetadataTracker)
        {
            _commandQueueManager = commandQueueManager;
            _seriesService = seriesService;
            _pendingMetadataTracker = pendingMetadataTracker;
        }

        public void Handle(SeriesAddedEvent message)
        {
            // A series added without its metadata (provider unreachable) is background work
            if (message.Series.LastInfoSync == null)
            {
                _pendingMetadataTracker.Added(1);
                _commandQueueManager.Push(new RefreshSeriesCommand(new List<int> { message.Series.Id }, true), CommandPriority.Low, CommandTrigger.Unspecified);
                return;
            }

            _commandQueueManager.Push(new RefreshSeriesCommand(new List<int> { message.Series.Id }, true), CommandPriority.High, CommandTrigger.Manual);
        }

        public void Handle(SeriesImportedEvent message)
        {
            // Series added without their metadata have to fetch it from AniDB (hub, seasons, episodes).
            // That is bulk work: it runs at low priority so it never holds up anything the user does.
            var incomplete = (_seriesService.GetSeries(message.SeriesIds) ?? new List<Series>())
                .Where(s => s.LastInfoSync == null)
                .Select(s => s.Id)
                .ToHashSet();

            var complete = message.SeriesIds.Where(id => !incomplete.Contains(id)).ToList();

            if (complete.Any())
            {
                _commandQueueManager.PushMany(complete.Select(s => new RefreshSeriesCommand(new List<int> { s }, true)).ToList(), CommandPriority.High, CommandTrigger.Manual);
            }

            if (incomplete.Any())
            {
                _pendingMetadataTracker.Added(incomplete.Count);
                _commandQueueManager.PushMany(incomplete.Select(s => new RefreshSeriesCommand(new List<int> { s }, true)).ToList(), CommandPriority.Low, CommandTrigger.Unspecified);
            }
        }
    }
}
