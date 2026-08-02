using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tv
{
    public class AniDbRelatedSeries : ModelBase
    {
        public int SeriesId { get; set; }
        public int RelatedAniDbId { get; set; }

        /// <summary>
        /// Example: "Other", "Side Story", "Same Setting", "Alternate Version"
        /// </summary>
        public string RelationType { get; set; }
    }
}
