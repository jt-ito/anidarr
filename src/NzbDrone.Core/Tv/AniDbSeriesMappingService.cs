using System.Collections.Generic;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public interface IAniDbSeriesMappingService
    {
        List<AniDbSeriesMapping> GetMappingsForSeries(int seriesId);
        AniDbSeriesMapping GetMappingByAniDbId(int aniDbId);
        void UpdateMappings(int seriesId, List<AniDbSeriesMapping> mappings);
    }

    public class AniDbSeriesMappingService : IAniDbSeriesMappingService,
        IHandle<SeriesDeletedEvent>
    {
        private readonly IAniDbSeriesMappingRepository _repository;

        public AniDbSeriesMappingService(IAniDbSeriesMappingRepository repository)
        {
            _repository = repository;
        }

        public List<AniDbSeriesMapping> GetMappingsForSeries(int seriesId)
        {
            return _repository.GetBySeriesId(seriesId);
        }

        public AniDbSeriesMapping GetMappingByAniDbId(int aniDbId)
        {
            return _repository.GetByAniDbId(aniDbId);
        }

        public void UpdateMappings(int seriesId, List<AniDbSeriesMapping> mappings)
        {
            _repository.DeleteBySeriesId(seriesId);

            foreach (var mapping in mappings)
            {
                mapping.SeriesId = seriesId;
                _repository.Insert(mapping);
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
