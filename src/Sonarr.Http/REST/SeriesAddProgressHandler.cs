using System;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;
using NzbDrone.SignalR;

namespace Sonarr.Http.REST
{
    public class SeriesAddProgressHandler : IHandle<SeriesAddProgressEvent>
    {
        private readonly IBroadcastSignalRMessage _signalRBroadcaster;

        public SeriesAddProgressHandler(IBroadcastSignalRMessage signalRBroadcaster)
        {
            _signalRBroadcaster = signalRBroadcaster;
        }

        public void Handle(SeriesAddProgressEvent message)
        {
            _signalRBroadcaster.BroadcastMessage(new SignalRMessage
            {
                Name = "seriesaddprogress",
                Body = new
                {
                    aniDbId = message.AniDbId,
                    message = message.Message,
                    timestamp = DateTime.UtcNow
                }
            });
        }
    }
}
