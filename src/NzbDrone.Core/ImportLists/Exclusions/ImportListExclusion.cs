using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    public class ImportListExclusion : ModelBase
    {
        public int TvdbId { get; set; }
        public string Title { get; set; }
        public int? AniDbId { get; set; }
        public int? SimklId { get; set; }
        public int? MalId { get; set; }
        public int? AniListId { get; set; }
    }
}
