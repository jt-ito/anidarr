using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public interface IAniListEnricher
    {
        Dictionary<int, TimeSpan> GetAiringTimes(int aniListId);
        int? SearchAniListIdByTitle(string title, int expectedYear, int? expectedEpisodeCount);
    }

    public class AniListEnricher : IAniListEnricher
    {
        private const string GraphQlEndpoint = "https://graphql.anilist.co";
        private readonly IHttpClient _httpClient;
        private readonly IAniListRateLimiter _rateLimiter;
        private readonly Logger _logger;

        public AniListEnricher(IHttpClient httpClient, IAniListRateLimiter rateLimiter, Logger logger)
        {
            _httpClient = httpClient;
            _rateLimiter = rateLimiter;
            _logger = logger;
        }

        public Dictionary<int, TimeSpan> GetAiringTimes(int aniListId)
        {
            return _rateLimiter.ExecuteAsync(() => FetchAiringTimes(aniListId)).GetAwaiter().GetResult();
        }

        private Dictionary<int, TimeSpan> FetchAiringTimes(int aniListId)
        {
            const string query = @"
query ($id: Int) {
  Media(id: $id, type: ANIME) {
    id
    airingSchedule(notYetAired: false, page: 1, perPage: 150) {
      nodes { episode airingAt timeUntilAiring }
    }
  }
}";
            var payload = new { query, variables = new { id = aniListId } };
            var request = new HttpRequest(GraphQlEndpoint)
            {
                Method = System.Net.Http.HttpMethod.Post
            };
            request.Headers.ContentType = "application/json";
            request.Headers.Add("Accept", "application/json");
            request.SetContent(System.Text.Json.JsonSerializer.Serialize(payload));

            HttpResponse<AniListMediaResponse> response = null;
            try
            {
                response = _httpClient.Post<AniListMediaResponse>(request);
            }
            catch (HttpException ex)
            {
                if (ex.Response != null)
                {
                    var retryAfterValue = ex.Response.Headers.Get("Retry-After");
                    if (retryAfterValue != null && int.TryParse(retryAfterValue, out var retrySeconds))
                    {
                        _rateLimiter.SetRetryAfter(TimeSpan.FromSeconds(retrySeconds));
                    }
                }

                throw;
            }

            var media = response?.Resource?.Data?.Media;
            var result = new Dictionary<int, TimeSpan>();

            if (media?.AiringSchedule?.Nodes == null)
            {
                return result;
            }

            foreach (var node in media.AiringSchedule.Nodes)
            {
                if (node.Episode > 0 && node.AiringAt > 0)
                {
                    // Convert UNIX timestamp to JST, then extract TimeOfDay.
                    var utcTime = DateTimeOffset.FromUnixTimeSeconds(node.AiringAt).UtcDateTime;
                    var jstTime = utcTime.AddHours(9);
                    result[node.Episode] = jstTime.TimeOfDay;
                }
            }

            return result;
        }

        public int? SearchAniListIdByTitle(string title, int expectedYear, int? expectedEpisodeCount)
        {
            return _rateLimiter.ExecuteAsync(() => FetchAniListIdByTitle(title, expectedYear, expectedEpisodeCount)).GetAwaiter().GetResult();
        }

        private int? FetchAniListIdByTitle(string title, int expectedYear, int? expectedEpisodeCount)
        {
            const string query = @"
query ($search: String) {
  Page(page:1, perPage:5) {
    media(search: $search, type: ANIME) {
      id
      title { romaji english }
      startDate { year }
      format
      episodes
    }
  }
}";
            var payload = new { query, variables = new { search = title } };
            var request = new HttpRequest(GraphQlEndpoint)
            {
                Method = System.Net.Http.HttpMethod.Post
            };
            request.Headers.ContentType = "application/json";
            request.Headers.Add("Accept", "application/json");
            request.SetContent(System.Text.Json.JsonSerializer.Serialize(payload));

            HttpResponse<AniListSearchResponse> response = null;
            try
            {
                response = _httpClient.Post<AniListSearchResponse>(request);
            }
            catch (HttpException ex)
            {
                if (ex.Response != null)
                {
                    var retryAfterValue = ex.Response.Headers.Get("Retry-After");
                    if (retryAfterValue != null && int.TryParse(retryAfterValue, out var retrySeconds))
                    {
                        _rateLimiter.SetRetryAfter(TimeSpan.FromSeconds(retrySeconds));
                    }
                }

                throw;
            }

            var mediaList = response?.Resource?.Data?.Page?.Media;
            if (mediaList == null || !mediaList.Any())
            {
                return null;
            }

            var candidates = new List<AniListMedia>();
            foreach (var node in mediaList)
            {
                if (node.Format == "TV" && node.StartDate?.Year == expectedYear)
                {
                    candidates.Add(node);
                }
            }

            if (candidates.Count == 1)
            {
                return candidates[0].Id;
            }

            if (candidates.Count > 1)
            {
                _logger.Warn("Ambiguous AniList match for title '{0}'. Found {1} candidates matching year {2}.", title, candidates.Count, expectedYear);
                return null;
            }

            return null;
        }
    }
}
