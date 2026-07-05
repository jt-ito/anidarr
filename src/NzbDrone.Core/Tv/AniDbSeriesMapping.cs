using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tv
{
    public class AniDbSeriesMapping : ModelBase
    {
        public int SeriesId { get; set; }
        public int AniDbId { get; set; }
        public int SeasonNumber { get; set; }

        /// <summary>
        /// Example: "Sequel", "Prequel", "Manual"
        /// </summary>
        public string RelationType { get; set; }
    }
}
