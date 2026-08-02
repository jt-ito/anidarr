using System.Collections.Generic;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public interface IAniDbRelatedSeriesService
    {
        List<AniDbRelatedSeries> GetRelatedSeries(int seriesId);
        void UpdateRelatedSeries(int seriesId, List<AniDbRelatedSeries> relatedSeries);
    }

    public class AniDbRelatedSeriesService : IAniDbRelatedSeriesService,
        IHandle<SeriesDeletedEvent>
    {
        private readonly IAniDbRelatedSeriesRepository _repository;

        public AniDbRelatedSeriesService(IAniDbRelatedSeriesRepository repository)
        {
            _repository = repository;
        }

        public List<AniDbRelatedSeries> GetRelatedSeries(int seriesId)
        {
            return _repository.GetBySeriesId(seriesId);
        }

        public void UpdateRelatedSeries(int seriesId, List<AniDbRelatedSeries> relatedSeries)
        {
            _repository.DeleteBySeriesId(seriesId);

            foreach (var relation in relatedSeries)
            {
                relation.SeriesId = seriesId;
                _repository.Insert(relation);
            }
        }

        public void Handle(SeriesDeletedEvent message)
        {
            foreach (var series in message.Series)
            {
                _repository.DeleteBySeriesId(series.Id);
            }
        }
    }
}
