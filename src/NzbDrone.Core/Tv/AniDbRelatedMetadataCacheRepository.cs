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
            return Query(c => aniDbIds.Contains(c.AniDbId)).ToList();
        }
    }
}
