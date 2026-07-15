using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public class AnimeOfflineTitle : ModelBase
    {
        public int? AniDbId { get; set; }
        public int? MalId { get; set; }
        public int? AniListId { get; set; }
        public string Title { get; set; }
        public string CleanTitle { get; set; }
        public List<string> SearchSynonyms { get; set; } = new List<string>();
        public string PictureUrl { get; set; }
        public int? Year { get; set; }
        public List<string> Genres { get; set; } = new List<string>();
        public SeriesStatusType? Status { get; set; }
        public string Overview { get; set; }
    }
}
