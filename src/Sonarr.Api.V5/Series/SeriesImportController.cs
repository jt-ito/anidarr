using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V5.Series
{
    public class ImportJobRequest
    {
        public List<SeriesResource> Series { get; set; } = new();

        // Add AniDB series from the lookup data and fetch hub/seasons/episodes afterwards
        public bool DeferMetadata { get; set; }
    }

    public class ImportEstimateRequest
    {
        // The AniDB ids of the AniDB-only series about to be imported
        public List<int> AniDbIds { get; set; } = new();
    }

    public class ImportEstimateResource
    {
        public int AniDbSeries { get; set; }

        // Already in the local AniDB cache: importing them needs no AniDB request
        public int Cached { get; set; }

        public int Uncached { get; set; }

        public int SecondsLow { get; set; }

        public int SecondsHigh { get; set; }
    }

    public class ImportJobItemResource
    {
        public int Index { get; set; }

        public string? Path { get; set; }

        public string? Title { get; set; }

        // added, skipped (nothing to add), failed
        public string State { get; set; } = "added";

        public string? Message { get; set; }

        public SeriesResource? Series { get; set; }
    }

    public class ImportJobStatusResource
    {
        public int Generation { get; set; }

        public int Total { get; set; }

        public int Done { get; set; }

        public bool IsRunning { get; set; }

        public int Next { get; set; }

        public List<ImportJobItemResource> Items { get; set; } = new();
    }

    [V5ApiController("series/import")]
    public class SeriesImportController : Controller
    {
        private readonly IAddSeriesService _addSeriesService;
        private readonly IImportJobService _importJobService;
        private readonly IAniDbXmlClient _aniDbXmlClient;

        public SeriesImportController(IAddSeriesService addSeriesService, IImportJobService importJobService, IAniDbXmlClient aniDbXmlClient)
        {
            _addSeriesService = addSeriesService;
            _importJobService = importJobService;
            _aniDbXmlClient = aniDbXmlClient;
        }

        // Roughly how long importing these AniDB series will take, so the user can decide whether
        // "add now, fetch details later" is worth it
        [HttpPost("estimate")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public Ok<ImportEstimateResource> Estimate([FromBody] ImportEstimateRequest request)
        {
            var ids = request.AniDbIds.Where(id => id > 0).Distinct().ToList();
            var cached = ids.Count(id => _aniDbXmlClient.IsCached(id));
            var (low, high) = ImportEstimate.Seconds(ids.Count - cached);

            return TypedResults.Ok(new ImportEstimateResource
            {
                AniDbSeries = ids.Count,
                Cached = cached,
                Uncached = ids.Count - cached,
                SecondsLow = low,
                SecondsHigh = high
            });
        }

        // Library import: starts adding the series in the background and returns at once.
        // Poll GET job for progress.
        [HttpPost("job")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public Accepted StartJob([FromBody] ImportJobRequest request)
        {
            _importJobService.Enqueue(request.Series.ToModel(), request.DeferMetadata);

            return TypedResults.Accepted(string.Empty);
        }

        [HttpGet("job")]
        [Produces("application/json")]
        public Ok<ImportJobStatusResource> JobStatus([FromQuery] int after = 0, [FromQuery] int generation = 0)
        {
            var status = _importJobService.GetStatus(after, generation);

            return TypedResults.Ok(new ImportJobStatusResource
            {
                Generation = status.Generation,
                Total = status.Total,
                Done = status.Done,
                IsRunning = status.IsRunning,
                Next = status.Next,
                Items = status.Items.Select(i => new ImportJobItemResource
                {
                    Index = i.Index,
                    Path = i.Path,
                    Title = i.Title,
                    State = i.State.ToString().ToLowerInvariant(),
                    Message = i.Message,
                    Series = i.Series?.ToResource()
                }).ToList()
            });
        }

        [HttpPost]
        [Consumes("application/json")]
        [Produces("application/json")]
        public Ok<List<SeriesResource>> Import([FromBody] List<SeriesResource> resource)
        {
            var newSeries = resource.ToModel();

            // Add what is valid; one failing series must not discard the rest of the batch.
            // Skipped series are simply absent from the response.
            return TypedResults.Ok(_addSeriesService.AddSeries(newSeries, ignoreErrors: true).ToResource());
        }
    }
}
