using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Series
{
    public class AniDbMappingResource : RestResource
    {
        public int SeriesId { get; set; }
        public int AniDbId { get; set; }
        public int SeasonNumber { get; set; }
        public string? RelationType { get; set; }
    }
}
