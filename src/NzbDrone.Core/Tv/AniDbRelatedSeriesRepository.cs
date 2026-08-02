using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Tv
{
    public interface IAniDbRelatedSeriesRepository : IBasicRepository<AniDbRelatedSeries>
    {
        List<AniDbRelatedSeries> GetBySeriesId(int seriesId);
        void DeleteBySeriesId(int seriesId);
    }

    public class AniDbRelatedSeriesRepository : BasicRepository<AniDbRelatedSeries>, IAniDbRelatedSeriesRepository
    {
        public AniDbRelatedSeriesRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<AniDbRelatedSeries> GetBySeriesId(int seriesId)
        {
            return Query(c => c.SeriesId == seriesId).ToList();
        }

        public void DeleteBySeriesId(int seriesId)
        {
            Delete(c => c.SeriesId == seriesId);
        }
    }
}
