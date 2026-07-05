using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tv;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Series
{
    [V5ApiController("series/anidb-mapping")]
    public class AniDbMappingController : RestController<AniDbMappingResource>
    {
        private readonly IAniDbSeriesMappingService _mappingService;
        private readonly ISeriesService _seriesService;

        public AniDbMappingController(IAniDbSeriesMappingService mappingService, ISeriesService seriesService)
        {
            _mappingService = mappingService;
            _seriesService = seriesService;
        }

        protected override AniDbMappingResource GetResourceById(int id)
        {
            return null!;
        }

        [HttpGet]
        public Ok<List<AniDbMappingResource>> GetMappings([FromQuery] int? seriesId)
        {
            if (seriesId.HasValue)
            {
                var mappings = _mappingService.GetMappingsForSeries(seriesId.Value);
                return TypedResults.Ok(mappings.Select(ToResource).ToList());
            }

            return TypedResults.Ok(new List<AniDbMappingResource>());
        }

        [HttpPost]
        public Created<AniDbMappingResource> CreateMapping([FromBody] AniDbMappingResource resource)
        {
            var model = ToModel(resource);
            var mappings = _mappingService.GetMappingsForSeries(model.SeriesId);
            mappings.Add(model);

            _mappingService.UpdateMappings(model.SeriesId, mappings);

            // Trigger series refresh so the new mapping's episodes are pulled
            var series = _seriesService.GetSeries(model.SeriesId);
            _seriesService.UpdateSeries(series, true, true);

            return TypedResults.Created("", ToResource(model));
        }

        [HttpDelete]
        public NoContent DeleteMapping([FromQuery] int seriesId, [FromQuery] int aniDbId)
        {
            var mappings = _mappingService.GetMappingsForSeries(seriesId);
            var toRemove = mappings.FirstOrDefault(m => m.AniDbId == aniDbId);
            if (toRemove != null)
            {
                mappings.Remove(toRemove);
                _mappingService.UpdateMappings(seriesId, mappings);

                // Trigger series refresh
                var series = _seriesService.GetSeries(seriesId);
                _seriesService.UpdateSeries(series, true, true);
            }

            return TypedResults.NoContent();
        }

        private AniDbMappingResource ToResource(AniDbSeriesMapping model)
        {
            return new AniDbMappingResource
            {
                Id = model.Id,
                SeriesId = model.SeriesId,
                AniDbId = model.AniDbId,
                SeasonNumber = model.SeasonNumber,
                RelationType = model.RelationType
            };
        }

        private AniDbSeriesMapping ToModel(AniDbMappingResource resource)
        {
            return new AniDbSeriesMapping
            {
                Id = resource.Id,
                SeriesId = resource.SeriesId,
                AniDbId = resource.AniDbId,
                SeasonNumber = resource.SeasonNumber,
                RelationType = resource.RelationType ?? "Manual"
            };
        }
    }
}
