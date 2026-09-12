using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Tv.Events
{
    public class SeriesAddProgressEvent : IEvent
    {
        public int? AniDbId { get; set; }
        public string Message { get; set; }

        public SeriesAddProgressEvent(string message, int? aniDbId = null)
        {
            Message = message;
            AniDbId = aniDbId;
        }
    }
}
