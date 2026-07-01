using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    public interface IImportListExclusionRepository : IBasicRepository<ImportListExclusion>
    {
        ImportListExclusion FindByTvdbId(int tvdbId);
        ImportListExclusion FindByAniDbId(int aniDbId);
        ImportListExclusion FindBySimklId(int simklId);
        ImportListExclusion FindByMalId(int malId);
        ImportListExclusion FindByAniListId(int aniListId);
    }

    public class ImportListExclusionRepository : BasicRepository<ImportListExclusion>, IImportListExclusionRepository
    {
        public ImportListExclusionRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public ImportListExclusion FindByTvdbId(int tvdbId)
        {
            return Query(m => m.TvdbId == tvdbId).SingleOrDefault();
        }

        public ImportListExclusion FindByAniDbId(int aniDbId)
        {
            return Query(m => m.AniDbId == aniDbId).SingleOrDefault();
        }

        public ImportListExclusion FindBySimklId(int simklId)
        {
            return Query(m => m.SimklId == simklId).SingleOrDefault();
        }

        public ImportListExclusion FindByMalId(int malId)
        {
            return Query(m => m.MalId == malId).SingleOrDefault();
        }

        public ImportListExclusion FindByAniListId(int aniListId)
        {
            return Query(m => m.AniListId == aniListId).SingleOrDefault();
        }
    }
}
