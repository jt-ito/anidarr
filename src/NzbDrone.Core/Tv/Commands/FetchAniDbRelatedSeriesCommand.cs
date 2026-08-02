using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Tv.Commands
{
    public class FetchAniDbRelatedSeriesCommand : Command
    {
        public int SeriesId { get; set; }

        public FetchAniDbRelatedSeriesCommand(int seriesId)
        {
            SeriesId = seriesId;
        }

        public override bool SendUpdatesToClient => true;
    }
}
