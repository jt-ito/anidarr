using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tv
{
    public class AniDbRelatedMetadataCache : ModelBase
    {
        public int AniDbId { get; set; }
        public string Title { get; set; }
        public string PosterUrl { get; set; }
        public string Overview { get; set; }
    }
}
