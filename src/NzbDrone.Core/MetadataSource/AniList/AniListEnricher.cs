using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public class AniListMediaInfo
    {
        public int Id { get; set; }
        public string Format { get; set; }
        public int? Episodes { get; set; }
    }

    public class AniListEnrichmentData
    {
        public Dictionary<int, Dictionary<int, TimeSpan>> AiringTimes { get; set; } = new Dictionary<int, Dictionary<int, TimeSpan>>();
        public Dictionary<int, List<string>> Titles { get; set; } = new Dictionary<int, List<string>>();
    }

    public interface IAniListEnricher
    {
        bool IsRateLimited { get; }
        Dictionary<int, TimeSpan> GetAiringTimes(int aniListId);
        Dictionary<int, Dictionary<int, TimeSpan>> GetAiringTimesForMultiple(IEnumerable<int> aniListIds);
        AniListEnrichmentData GetEnrichmentForMultiple(IEnumerable<int> aniListIds);
        int? SearchAniListIdByTitle(string title, int expectedYear, int? expectedEpisodeCount);
        List<string> GetTitles(int aniListId);
        AniListMediaInfo GetMediaInfo(int aniListId);
        Dictionary<int, AniListMediaInfo> GetMediaInfoForMultiple(IEnumerable<int> aniListIds);
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

        public bool IsRateLimited => _rateLimiter.IsRateLimited;

        public Dictionary<int, TimeSpan> GetAiringTimes(int aniListId)
        {
            if (_rateLimiter.IsRateLimited)
            {
                _logger.Debug("AniList circuit breaker active until {0:u} UTC; skipping airing times for ID {1}.", _rateLimiter.RetryAfterUtc, aniListId);
                return new Dictionary<int, TimeSpan>();
            }

            try
            {
                return _rateLimiter.ExecuteAsync(() => FetchAiringTimes(aniListId)).GetAwaiter().GetResult();
            }
            catch (HttpException ex)
            {
                HandleHttpException(ex, $"fetching airing times for ID {aniListId}");
                return new Dictionary<int, TimeSpan>();
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to fetch AniList airing times for ID {0}: {1}", aniListId, ex.Message);
                return new Dictionary<int, TimeSpan>();
            }
        }

        public Dictionary<int, Dictionary<int, TimeSpan>> GetAiringTimesForMultiple(IEnumerable<int> aniListIds)
        {
            return GetEnrichmentForMultiple(aniListIds).AiringTimes;
        }

        public AniListEnrichmentData GetEnrichmentForMultiple(IEnumerable<int> aniListIds)
        {
            var idList = aniListIds.Distinct().ToList();
            if (!idList.Any())
            {
                return new AniListEnrichmentData();
            }

            if (_rateLimiter.IsRateLimited)
            {
                _logger.Debug("AniList circuit breaker active until {0:u} UTC; skipping batch enrichment for {1} IDs.", _rateLimiter.RetryAfterUtc, idList.Count);
                return new AniListEnrichmentData();
            }

            try
            {
                return _rateLimiter.ExecuteAsync(() => FetchEnrichmentForMultiple(idList)).GetAwaiter().GetResult();
            }
            catch (HttpException ex)
            {
                HandleHttpException(ex, $"batch fetching enrichment for {idList.Count} IDs");
                return new AniListEnrichmentData();
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to batch fetch AniList enrichment for {0} IDs: {1}", idList.Count, ex.Message);
                return new AniListEnrichmentData();
            }
        }

        public AniListMediaInfo GetMediaInfo(int aniListId)
        {
            if (_rateLimiter.IsRateLimited)
            {
                _logger.Debug("AniList circuit breaker active until {0:u} UTC; skipping media info for ID {1}.", _rateLimiter.RetryAfterUtc, aniListId);
                return null;
            }

            try
            {
                return _rateLimiter.ExecuteAsync(() => FetchMediaInfo(aniListId)).GetAwaiter().GetResult();
            }
            catch (HttpException ex)
            {
                HandleHttpException(ex, $"fetching media info for ID {aniListId}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to fetch AniList media info for ID {0}: {1}", aniListId, ex.Message);
                return null;
            }
        }

        public Dictionary<int, AniListMediaInfo> GetMediaInfoForMultiple(IEnumerable<int> aniListIds)
        {
            var idList = aniListIds.Distinct().ToList();
            if (!idList.Any())
            {
                return new Dictionary<int, AniListMediaInfo>();
            }

            if (_rateLimiter.IsRateLimited)
            {
                _logger.Debug("AniList circuit breaker active until {0:u} UTC; skipping batch media info for {1} IDs.", _rateLimiter.RetryAfterUtc, idList.Count);
                return new Dictionary<int, AniListMediaInfo>();
            }

            try
            {
                return _rateLimiter.ExecuteAsync(() => FetchMediaInfoForMultiple(idList)).GetAwaiter().GetResult();
            }
            catch (HttpException ex)
            {
                HandleHttpException(ex, $"batch fetching media info for {idList.Count} IDs");
                return new Dictionary<int, AniListMediaInfo>();
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to batch fetch AniList media info for {0} IDs: {1}", idList.Count, ex.Message);
                return new Dictionary<int, AniListMediaInfo>();
            }
        }

        private AniListMediaInfo FetchMediaInfo(int aniListId)
        {
            const string query = @"
query ($id: Int) {
  Media(id: $id, type: ANIME) {
    id
    format
    episodes
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
                RecordHttpException(ex);
                throw;
            }

            var media = response?.Resource?.Data?.Media;
            if (media == null)
            {
                return null;
            }

            return new AniListMediaInfo
            {
                Id = media.Id,
                Format = media.Format,
                Episodes = media.Episodes
            };
        }

        private Dictionary<int, AniListMediaInfo> FetchMediaInfoForMultiple(List<int> aniListIds)
        {
            const string query = @"
query ($ids: [Int]) {
  Page(page: 1, perPage: 50) {
    media(id_in: $ids, type: ANIME) {
      id
      format
      episodes
    }
  }
}";
            var payload = new { query, variables = new { ids = aniListIds } };
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
                RecordHttpException(ex);
                throw;
            }

            var mediaList = response?.Resource?.Data?.Page?.Media;
            var result = new Dictionary<int, AniListMediaInfo>();

            if (mediaList == null)
            {
                return result;
            }

            foreach (var media in mediaList)
            {
                result[media.Id] = new AniListMediaInfo
                {
                    Id = media.Id,
                    Format = media.Format,
                    Episodes = media.Episodes
                };
            }

            return result;
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
                RecordHttpException(ex);
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

        private AniListEnrichmentData FetchEnrichmentForMultiple(List<int> aniListIds)
        {
            const string query = @"
query ($ids: [Int]) {
  Page(page: 1, perPage: 50) {
    media(id_in: $ids, type: ANIME) {
      id
      title { romaji english native }
      synonyms
      airingSchedule(notYetAired: false, page: 1, perPage: 150) {
        nodes { episode airingAt timeUntilAiring }
      }
    }
  }
}";
            var payload = new { query, variables = new { ids = aniListIds } };
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
                RecordHttpException(ex);
                throw;
            }

            var mediaList = response?.Resource?.Data?.Page?.Media;
            var result = new AniListEnrichmentData();

            if (mediaList == null)
            {
                return result;
            }

            foreach (var media in mediaList)
            {
                var times = new Dictionary<int, TimeSpan>();
                if (media.AiringSchedule?.Nodes != null)
                {
                    foreach (var node in media.AiringSchedule.Nodes)
                    {
                        if (node.Episode > 0 && node.AiringAt > 0)
                        {
                            var utcTime = DateTimeOffset.FromUnixTimeSeconds(node.AiringAt).UtcDateTime;
                            var jstTime = utcTime.AddHours(9);
                            times[node.Episode] = jstTime.TimeOfDay;
                        }
                    }
                }

                result.AiringTimes[media.Id] = times;

                var titles = new List<string>();
                if (!string.IsNullOrWhiteSpace(media.Title?.Romaji))
                {
                    titles.Add(media.Title.Romaji);
                }

                if (!string.IsNullOrWhiteSpace(media.Title?.English))
                {
                    titles.Add(media.Title.English);
                }

                if (!string.IsNullOrWhiteSpace(media.Title?.Native))
                {
                    titles.Add(media.Title.Native);
                }

                if (media.Synonyms != null)
                {
                    foreach (var syn in media.Synonyms)
                    {
                        if (!string.IsNullOrWhiteSpace(syn))
                        {
                            titles.Add(syn);
                        }
                    }
                }

                result.Titles[media.Id] = titles.Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
            }

            return result;
        }

        public int? SearchAniListIdByTitle(string title, int expectedYear, int? expectedEpisodeCount)
        {
            if (_rateLimiter.IsRateLimited)
            {
                _logger.Debug("AniList circuit breaker active until {0:u} UTC; skipping title search for '{1}'.", _rateLimiter.RetryAfterUtc, title);
                return null;
            }

            try
            {
                return _rateLimiter.ExecuteAsync(() => FetchAniListIdByTitle(title, expectedYear, expectedEpisodeCount)).GetAwaiter().GetResult();
            }
            catch (HttpException ex)
            {
                HandleHttpException(ex, $"searching for '{title}'");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to search AniList for '{0}': {1}", title, ex.Message);
                return null;
            }
        }

        private int? FetchAniListIdByTitle(string title, int expectedYear, int? expectedEpisodeCount)
        {
            const string query = @"
query ($search: String) {
  Page(page:1, perPage:5) {
    media(search: $search, type: ANIME) {
      id
      title { romaji english native }
      synonyms
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
                RecordHttpException(ex);
                throw;
            }

            var mediaList = response?.Resource?.Data?.Page?.Media;
            if (mediaList == null || !mediaList.Any())
            {
                return null;
            }

            var cleanSearchTitle = title.CleanForSearch();
            var candidates = new List<AniListMedia>();
            foreach (var node in mediaList)
            {
                if (node.Format == "TV" && node.StartDate?.Year.HasValue == true)
                {
                    var yearDiff = Math.Abs(node.StartDate.Year.Value - expectedYear);
                    if (yearDiff <= 1)
                    {
                        var isMatch = false;

                        bool CheckMatch(string titleCandidate)
                        {
                            if (string.IsNullOrWhiteSpace(titleCandidate))
                            {
                                return false;
                            }

                            var cleanOther = titleCandidate.CleanForSearch();
                            var dist = cleanSearchTitle.LevenshteinDistance(cleanOther);
                            var allowed = cleanSearchTitle.GetAllowedEdits(cleanOther);

                            // Standard Levenshtein distance (cost=1). We allow a 20% edit rate (minimum 1).
                            return dist <= allowed;
                        }

                        if (CheckMatch(node.Title?.Romaji) ||
                            CheckMatch(node.Title?.English) ||
                            CheckMatch(node.Title?.Native))
                        {
                            isMatch = true;
                        }

                        if (!isMatch && node.Synonyms != null)
                        {
                            foreach (var syn in node.Synonyms)
                            {
                                if (CheckMatch(syn))
                                {
                                    isMatch = true;
                                    break;
                                }
                            }
                        }

                        if (isMatch)
                        {
                            candidates.Add(node);
                        }
                    }
                }
            }

            if (candidates.Count == 1)
            {
                return candidates[0].Id;
            }

            if (candidates.Count > 1)
            {
                if (expectedEpisodeCount.HasValue && expectedEpisodeCount.Value > 0)
                {
                    var exactEpMatches = candidates.Where(c => c.Episodes == expectedEpisodeCount.Value).ToList();
                    if (exactEpMatches.Count == 1)
                    {
                        return exactEpMatches[0].Id;
                    }
                }

                _logger.Warn("Ambiguous AniList match for title '{0}'. Found {1} candidates matching year {2} (+/- 1).", title, candidates.Count, expectedYear);
                return null;
            }

            return null;
        }

        public List<string> GetTitles(int aniListId)
        {
            if (_rateLimiter.IsRateLimited)
            {
                _logger.Debug("AniList circuit breaker active until {0:u} UTC; skipping titles for ID {1}.", _rateLimiter.RetryAfterUtc, aniListId);
                return new List<string>();
            }

            try
            {
                return _rateLimiter.ExecuteAsync(() => FetchTitles(aniListId)).GetAwaiter().GetResult();
            }
            catch (HttpException ex)
            {
                HandleHttpException(ex, $"fetching titles for ID {aniListId}");
                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.Warn("Failed to fetch AniList titles for ID {0}: {1}", aniListId, ex.Message);
                return new List<string>();
            }
        }

        private List<string> FetchTitles(int aniListId)
        {
            const string query = @"
query ($id: Int) {
  Media(id: $id, type: ANIME) {
    title { romaji english native }
    synonyms
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
                RecordHttpException(ex);
                throw;
            }

            var media = response?.Resource?.Data?.Media;
            if (media == null)
            {
                return new List<string>();
            }

            var titles = new List<string>();
            if (!string.IsNullOrWhiteSpace(media.Title?.Romaji))
            {
                titles.Add(media.Title.Romaji);
            }

            if (!string.IsNullOrWhiteSpace(media.Title?.English))
            {
                titles.Add(media.Title.English);
            }

            if (!string.IsNullOrWhiteSpace(media.Title?.Native))
            {
                titles.Add(media.Title.Native);
            }

            if (media.Synonyms != null)
            {
                foreach (var syn in media.Synonyms)
                {
                    if (!string.IsNullOrWhiteSpace(syn))
                    {
                        titles.Add(syn);
                    }
                }
            }

            return titles.Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
        }

        private void RecordHttpException(HttpException ex)
        {
            TimeSpan? explicitDelay = null;
            if (ex is TooManyRequestsException tmr && tmr.RetryAfter > TimeSpan.Zero)
            {
                explicitDelay = tmr.RetryAfter;
            }
            else if (ex.Response != null)
            {
                var retryHeader = ex.Response.Headers.Get("Retry-After");
                if (int.TryParse(retryHeader, out var seconds) && seconds > 0)
                {
                    explicitDelay = TimeSpan.FromSeconds(seconds);
                }
                else if (DateTime.TryParse(retryHeader, out var date))
                {
                    var diff = date.ToUniversalTime() - DateTime.UtcNow;
                    if (diff > TimeSpan.Zero)
                    {
                        explicitDelay = diff;
                    }
                }
            }

            var statusCode = (int?)ex.Response?.StatusCode;
            if (statusCode == 429 || statusCode == 403 || statusCode >= 500)
            {
                _rateLimiter.RecordFailure(explicitDelay);
            }
        }

        private void HandleHttpException(HttpException ex, string operation)
        {
            RecordHttpException(ex);

            var statusCode = (int?)ex.Response?.StatusCode;
            if (statusCode == 429)
            {
                _logger.Warn("AniList API rate limited (429) while {0}. Circuit breaker active until {1:u} UTC.", operation, _rateLimiter.RetryAfterUtc);
            }
            else if (statusCode == 403)
            {
                _logger.Warn("AniList API protected/forbidden (403 Cloudflare challenge) while {0}. Circuit breaker active until {1:u} UTC.", operation, _rateLimiter.RetryAfterUtc);
            }
            else if (statusCode >= 500)
            {
                _logger.Warn("AniList API server error ({0}) while {1}. Circuit breaker active until {2:u} UTC.", statusCode, operation, _rateLimiter.RetryAfterUtc);
            }
            else
            {
                _logger.Warn("AniList API HTTP error ({0}) while {1}: {2}", statusCode, operation, ex.Message);
            }
        }
    }
}
