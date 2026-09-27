using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Tv
{
    public interface IAniDbRelatedMetadataCacheRepository : IBasicRepository<AniDbRelatedMetadataCache>
    {
        AniDbRelatedMetadataCache GetByAniDbId(int aniDbId);
        List<AniDbRelatedMetadataCache> GetByAniDbIds(List<int> aniDbIds);
    }

    public class AniDbRelatedMetadataCacheRepository : BasicRepository<AniDbRelatedMetadataCache>, IAniDbRelatedMetadataCacheRepository
    {
        public AniDbRelatedMetadataCacheRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public AniDbRelatedMetadataCache GetByAniDbId(int aniDbId)
        {
            return Query(c => c.AniDbId == aniDbId).SingleOrDefault();
        }

        public List<AniDbRelatedMetadataCache> GetByAniDbIds(List<int> aniDbIds)
        {
            if (aniDbIds == null || !aniDbIds.Any())
            {
                return new List<AniDbRelatedMetadataCache>();
            }

            var distinctIds = aniDbIds.Distinct().ToList();
            if (distinctIds.Count <= 500)
            {
                return Query(c => distinctIds.Contains(c.AniDbId)).ToList();
            }

            var results = new List<AniDbRelatedMetadataCache>(distinctIds.Count);
            foreach (var chunk in distinctIds.Chunk(500))
            {
                var chunkList = chunk.ToList();
                results.AddRange(Query(c => chunkList.Contains(c.AniDbId)));
            }

            return results;
        }
    }
}
