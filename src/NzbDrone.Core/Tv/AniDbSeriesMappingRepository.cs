using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Tv
{
    public interface IAniDbSeriesMappingRepository : IBasicRepository<AniDbSeriesMapping>
    {
        List<AniDbSeriesMapping> GetBySeriesId(int seriesId);
        AniDbSeriesMapping GetByAniDbId(int aniDbId);
        void DeleteBySeriesId(int seriesId);
    }

    public class AniDbSeriesMappingRepository : BasicRepository<AniDbSeriesMapping>, IAniDbSeriesMappingRepository
    {
        public AniDbSeriesMappingRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<AniDbSeriesMapping> GetBySeriesId(int seriesId)
        {
            return Query(c => c.SeriesId == seriesId).ToList();
        }

        public AniDbSeriesMapping GetByAniDbId(int aniDbId)
        {
            return Query(c => c.AniDbId == aniDbId).SingleOrDefault();
        }

        public void DeleteBySeriesId(int seriesId)
        {
            Delete(c => c.SeriesId == seriesId);
        }
    }
}
