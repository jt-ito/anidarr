using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniList
{
    /// <summary>
    /// AniList GraphQL provider (https://graphql.anilist.co).
    /// No API key required for public read queries.
    /// Rate limit: 90 requests per minute.
    /// </summary>
    public class AniListProvider : IMetadataProvider
    {
        private const string GraphQlEndpoint = "https://graphql.anilist.co";
        private const int RateLimitPerMinute = 90;

        private readonly IHttpClient _httpClient;
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;

        public MetadataProviderType ProviderType => MetadataProviderType.AniList;

        // Simple in-memory sliding window rate tracker (90 req/min)
        private readonly System.Collections.Generic.Queue<DateTime> _requestTimestamps = new();

        public AniListProvider(IHttpClient httpClient,
                               ISeriesService seriesService,
                               Logger logger)
        {
            _httpClient = httpClient;
            _seriesService = seriesService;
            _logger = logger;
        }

        public bool CanHandleId(string externalIdKey) =>
            externalIdKey is "anilist" or "anilistid";

        public Tuple<Series, List<Episode>> GetSeriesInfo(string externalId)
        {
            if (!int.TryParse(externalId, out var aniListId) || aniListId <= 0)
            {
                throw new ArgumentException($"Invalid AniList ID: {externalId}");
            }

            ThrottleIfNeeded();

            const string query = @"
query ($id: Int) {
  Media(id: $id, type: ANIME) {
    id
    idMal
    title { romaji english native }
    description(asHtml: false)
    status
    startDate { year month day }
    endDate { year month day }
    seasonYear
    episodes
    duration
    genres
    coverImage { extraLarge large }
    bannerImage
    studios { nodes { name isAnimationStudio } }
    airingSchedule(notYetAired: false, page: 1, perPage: 50) {
      nodes { episode airingAt timeUntilAiring }
    }
  }
}";

            var payload = BuildPayload(query, new { id = aniListId });
            var response = PostGraphQl<AniListMediaResponse>(payload);
            var media = response?.Data?.Media;

            if (media == null)
            {
                throw new Exception($"AniList returned no data for ID {aniListId}");
            }

            var series = MapSeries(media);
            var episodes = GenerateEpisodes(media);

            return Tuple.Create(series, episodes);
        }

        public List<Series> Search(string query)
        {
            // Support anilist:12345 prefix
            var lower = query.ToLowerInvariant();
            if (lower.StartsWith("anilist:"))
            {
                var slug = lower.Split(':')[1].Trim();
                if (int.TryParse(slug, out var id) && id > 0)
                {
                    try
                    {
                        return new List<Series> { GetSeriesInfo(id.ToString()).Item1 };
                    }
                    catch
                    {
                        return new List<Series>();
                    }
                }

                return new List<Series>();
            }

            ThrottleIfNeeded();

            const string gql = @"
query ($search: String) {
  Page(page: 1, perPage: 20) {
    media(search: $search, type: ANIME) {
      id
      idMal
      title { romaji english native }
      description(asHtml: false)
      status
      startDate { year month day }
      episodes
      duration
      genres
      coverImage { extraLarge large }
    }
  }
}";

            var payload = BuildPayload(gql, new { search = query });
            var response = PostGraphQl<AniListSearchResponse>(payload);

            return response?.Data?.Page?.Media?
                .Select(m => MapSeries(m))
                .ToList() ?? new List<Series>();
        }

        private void ThrottleIfNeeded()
        {
            lock (_requestTimestamps)
            {
                var now = DateTime.UtcNow;
                var windowStart = now.AddMinutes(-1);

                // Remove timestamps outside the rolling window
                while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < windowStart)
                {
                    _requestTimestamps.Dequeue();
                }

                if (_requestTimestamps.Count >= RateLimitPerMinute)
                {
                    var oldest = _requestTimestamps.Peek();
                    var delay = oldest.AddMinutes(1) - now;
                    if (delay > TimeSpan.Zero)
                    {
                        _logger.Debug("AniList rate limit reached, sleeping {0}ms", delay.TotalMilliseconds);
                        System.Threading.Thread.Sleep(delay);
                    }
                }

                _requestTimestamps.Enqueue(DateTime.UtcNow);
            }
        }

        private T PostGraphQl<T>(object payload)
            where T : class, new()
        {
            var request = new HttpRequest(GraphQlEndpoint)
            {
                Method = HttpMethod.Post
            };
            request.Headers.ContentType = "application/json";
            request.Headers.Add("Accept", "application/json");
            request.SetContent(System.Text.Json.JsonSerializer.Serialize(payload));

            var response = _httpClient.Post<T>(request);
            return response?.Resource;
        }

        private static object BuildPayload(string query, object variables) =>
            new { query, variables };

        private static string CleanDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return description;
            }

            var decoded = System.Net.WebUtility.HtmlDecode(description);
            decoded = System.Net.WebUtility.HtmlDecode(decoded);

            // Replace all variants of <br> with newline
            var desc = System.Text.RegularExpressions.Regex.Replace(decoded, @"&lt;\s*br\s*/?\s*&gt;", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            desc = System.Text.RegularExpressions.Regex.Replace(desc, @"<\s*br\s*/?\s*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return System.Text.RegularExpressions.Regex.Replace(desc, "<.*?>", string.Empty).Trim();
        }

        private Series MapSeries(AniListMedia media)
        {
            var title = media.Title?.English.IsNotNullOrWhiteSpace() == true
                ? media.Title.English
                : media.Title?.Romaji ?? "Unknown";

            var series = new Series
            {
                Title = title,
                CleanTitle = title.CleanSeriesTitle(),
                SortTitle = SeriesTitleNormalizer.Normalize(title, media.Id),
                TitleSlug = title.ToUrlSlug(),
                Overview = CleanDescription(media.Description),
                Status = MapStatus(media.Status),
                Genres = media.Genres ?? new List<string>(),
                Runtime = media.Duration ?? 24,
                OriginalLanguage = Language.Japanese,
                SeriesType = SeriesTypes.Anime,
                PrimaryMetadataProvider = "anilist",
                Monitored = true
            };

            // AniList ID
            if (media.Id > 0)
            {
                series.AniListIds = new HashSet<int> { media.Id };
            }

            if (media.IdMal.HasValue)
            {
                series.MalIds = new HashSet<int> { media.IdMal.Value };
            }

            if (media.StartDate?.Year.HasValue == true)
            {
                series.Year = media.StartDate.Year.Value;
                if (media.StartDate.Month.HasValue && media.StartDate.Day.HasValue)
                {
                    series.FirstAired = new DateTime(media.StartDate.Year.Value, media.StartDate.Month.Value, media.StartDate.Day.Value, 0, 0, 0, DateTimeKind.Utc);
                }
            }

            var images = new List<MediaCover.MediaCover>();
            if (media.CoverImage?.ExtraLarge.IsNotNullOrWhiteSpace() == true)
            {
                images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, media.CoverImage.ExtraLarge));
            }

            if (media.BannerImage.IsNotNullOrWhiteSpace())
            {
                images.Add(new MediaCover.MediaCover(MediaCoverTypes.Banner, media.BannerImage));
            }

            series.Images = images;

            var studio = media.Studios?.Nodes?.FirstOrDefault(s => s.IsAnimationStudio);
            if (studio != null)
            {
                series.Network = studio.Name;
            }

            return series;
        }

        private static List<Episode> GenerateEpisodes(AniListMedia media)
        {
            // AniList doesn't expose individual episode details in the basic query.
            // We generate placeholder episodes using the episode count and airing schedule.
            var episodes = new List<Episode>();
            var count = media.Episodes ?? 0;

            for (var i = 1; i <= count; i++)
            {
                var episode = new Episode
                {
                    SeasonNumber = 1,
                    EpisodeNumber = i,
                    AbsoluteEpisodeNumber = i,
                    Title = $"Episode {i}",
                    Monitored = true
                };

                // Populate air date from airing schedule if available
                var scheduleNode = media.AiringSchedule?.Nodes?.FirstOrDefault(n => n.Episode == i);
                if (scheduleNode != null)
                {
                    episode.AirDateUtc = DateTimeOffset.FromUnixTimeSeconds(scheduleNode.AiringAt).UtcDateTime;
                    episode.AirDate = episode.AirDateUtc.Value.ToString("yyyy-MM-dd");
                }

                episodes.Add(episode);
            }

            return episodes;
        }

        private static SeriesStatusType MapStatus(string status) =>
            status?.ToUpperInvariant() switch
            {
                "FINISHED" => SeriesStatusType.Ended,
                "NOT_YET_RELEASED" => SeriesStatusType.Upcoming,
                _ => SeriesStatusType.Continuing
            };
    }

    // ── AniList response shapes ──────────────────────────────────────────────

    public class AniListMediaResponse
    {
        public AniListResponseData Data { get; set; }
    }

    public class AniListResponseData
    {
        public AniListMedia Media { get; set; }
    }

    public class AniListSearchResponse
    {
        public AniListSearchData Data { get; set; }
    }

    public class AniListSearchData
    {
        public AniListPage Page { get; set; }
    }

    public class AniListPage
    {
        public List<AniListMedia> Media { get; set; }
    }

    public class AniListMedia
    {
        public int Id { get; set; }
        public int? IdMal { get; set; }
        public AniListTitle Title { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public AniListDate StartDate { get; set; }
        public AniListDate EndDate { get; set; }
        public int? Episodes { get; set; }
        public int? Duration { get; set; }
        public List<string> Genres { get; set; }
        public AniListCoverImage CoverImage { get; set; }
        public string BannerImage { get; set; }
        public AniListStudios Studios { get; set; }
        public AniListAiringSchedule AiringSchedule { get; set; }
    }

    public class AniListTitle
    {
        public string Romaji { get; set; }
        public string English { get; set; }
        public string Native { get; set; }
    }

    public class AniListDate
    {
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
    }

    public class AniListCoverImage
    {
        public string ExtraLarge { get; set; }
        public string Large { get; set; }
    }

    public class AniListStudios
    {
        public List<AniListStudio> Nodes { get; set; }
    }

    public class AniListStudio
    {
        public string Name { get; set; }
        public bool IsAnimationStudio { get; set; }
    }

    public class AniListAiringSchedule
    {
        public List<AniListAiringNode> Nodes { get; set; }
    }

    public class AniListAiringNode
    {
        public int Episode { get; set; }
        public long AiringAt { get; set; }
        public int TimeUntilAiring { get; set; }
    }
}
