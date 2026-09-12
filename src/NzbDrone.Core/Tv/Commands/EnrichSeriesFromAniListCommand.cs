using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Tv.Commands
{
    public class EnrichSeriesFromAniListCommand : Command
    {
        public int SeriesId { get; set; }

        public EnrichSeriesFromAniListCommand(int seriesId)
        {
            SeriesId = seriesId;
        }

        public override bool SendUpdatesToClient => true;
    }
}
