using System;
using System.Linq;
using System.Net.Http;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public class IdCrossReferenceCommand : Command
    {
        public int SeriesId { get; set; }
        public override string CompletionMessage => "Cross-referenced metadata IDs";
    }

    /// <summary>
    /// Best-effort background service that populates sibling metadata IDs after a series is added.
    /// Uses AniList's rich cross-reference data (it exposes TVDB, MAL, and its own ID).
    /// Runs as a background Command — failures are logged but don't block the add flow.
    /// </summary>
    public class IdCrossReferenceService : IExecute<IdCrossReferenceCommand>
    {
        private const string AniListEndpoint = "https://graphql.anilist.co";

        private readonly ISeriesService _seriesService;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public IdCrossReferenceService(ISeriesService seriesService,
                                       IHttpClient httpClient,
                                       Logger logger)
        {
            _seriesService = seriesService;
            _httpClient = httpClient;
            _logger = logger;
        }

        public void Execute(IdCrossReferenceCommand message)
        {
            var series = _seriesService.GetSeries(message.SeriesId);
            if (series == null)
            {
                return;
            }

            _logger.Debug("Cross-referencing IDs for series '{0}'", series.Title);

            try
            {
                CrossReferenceViaAniList(series);
                _seriesService.UpdateSeries(series, publishUpdatedEvent: false);
                _logger.Debug("ID cross-reference complete for '{0}'", series.Title);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "ID cross-reference failed for '{0}': {1}", series.Title, ex.Message);
            }
        }

        private void CrossReferenceViaAniList(Series series)
        {
            // Determine the best search key we have
            string query;
            object variables;

            var aniListId = series.AniListIds?.FirstOrDefault();
            if (aniListId.HasValue && aniListId.Value > 0)
            {
                query = "query($id:Int){Media(id:$id,type:ANIME){id idMal externalLinks{site url}}}";
                variables = new { id = aniListId.Value };
            }
            else if (series.MalIds?.Any() == true)
            {
                query = "query($malId:Int){Media(idMal:$malId,type:ANIME){id idMal externalLinks{site url}}}";
                variables = new { malId = series.MalIds.First() };
            }
            else if (!string.IsNullOrWhiteSpace(series.Title))
            {
                query = "query($search:String){Media(search:$search,type:ANIME){id idMal externalLinks{site url}}}";
                variables = new { search = series.Title };
            }
            else
            {
                return;
            }

            var payload = System.Text.Json.JsonSerializer.Serialize(new { query, variables });

            var request = new HttpRequest(AniListEndpoint) { Method = HttpMethod.Post };
            request.Headers.ContentType = "application/json";
            request.SetContent(payload);

            var response = _httpClient.Get<AniListCrossRefResponse>(request);
            var media = response?.Resource?.Data?.Media;
            if (media == null)
            {
                return;
            }

            // Populate IDs we don't already have
            if (!series.AniListIds?.Any() == true || series.AniListIds.Count == 0)
            {
                series.AniListIds = new System.Collections.Generic.HashSet<int> { media.Id };
            }

            if (media.IdMal.HasValue && (series.MalIds == null || !series.MalIds.Any()))
            {
                series.MalIds = new System.Collections.Generic.HashSet<int> { media.IdMal.Value };
            }
        }
    }

    // Minimal response shapes for cross-reference
    internal class AniListCrossRefResponse
    {
        public AniListCrossRefData Data { get; set; }
    }

    internal class AniListCrossRefData
    {
        public AniListCrossRefMedia Media { get; set; }
    }

    internal class AniListCrossRefMedia
    {
        public int Id { get; set; }
        public int? IdMal { get; set; }
    }
}
