using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.Tvdb
{
    /// <summary>
    /// TVDB API v4 provider (direct API — Anidarr does not use SkyHook).
    /// Requires a free TVDB API key configured in Settings > Metadata Providers.
    /// Docs: https://thetvdb.github.io/v4-api/
    /// </summary>
    public class TvdbProvider : IMetadataProvider
    {
        private const string TvdbApiBase = "https://api4.thetvdb.com/v4";

        private readonly IHttpClient _httpClient;
        private readonly IConfigFileProvider _configService;
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;
        private readonly ISearchForNewSeries _searchProxy;
        private readonly IProvideSeriesInfo _seriesInfo;

        // Token cache
        private string _bearerToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public MetadataProviderType ProviderType => MetadataProviderType.Tvdb;

        public TvdbProvider(IHttpClient httpClient,
                            IConfigFileProvider configService,
                            ISeriesService seriesService,
                            Logger logger,
                            ISearchForNewSeries searchProxy,
                            IProvideSeriesInfo seriesInfo)
        {
            _httpClient = httpClient;
            _configService = configService;
            _seriesService = seriesService;
            _logger = logger;
            _searchProxy = searchProxy;
            _seriesInfo = seriesInfo;
        }

        public bool CanHandleId(string externalIdKey) =>
            externalIdKey is "tvdb" or "tvdbid";

        public Tuple<Series, List<Episode>> GetSeriesInfo(string externalId)
        {
            if (!int.TryParse(externalId, out var tvdbId) || tvdbId <= 0)
            {
                throw new ArgumentException($"Invalid TVDB ID: {externalId}");
            }

            if (string.IsNullOrWhiteSpace(_configService.TvdbApiKey))
            {
                return _seriesInfo.GetSeriesInfo(tvdbId);
            }

            EnsureToken();

            var request = new HttpRequestBuilder($"{TvdbApiBase}/series/{tvdbId}/episodes/default")
                .AddQueryParam("page", 0)
                .Build();
            request.Headers.Add("Authorization", $"Bearer {_bearerToken}");

            HttpResponse<TvdbSeriesExtendedResponse> response;
            try
            {
                // Fetch extended record (includes episodes)
                var extRequest = new HttpRequestBuilder($"{TvdbApiBase}/series/{tvdbId}/extended")
                    .AddQueryParam("meta", "episodes")
                    .AddQueryParam("short", "false")
                    .Build();
                extRequest.Headers.Add("Authorization", $"Bearer {_bearerToken}");
                response = _httpClient.Get<TvdbSeriesExtendedResponse>(extRequest);
            }
            catch (HttpException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SeriesNotFoundException(tvdbId);
            }

            if (response?.Resource?.Data == null)
            {
                throw new SeriesNotFoundException(tvdbId);
            }

            var series = MapSeries(response.Resource.Data);
            var episodes = (response.Resource.Data.Episodes ?? new List<TvdbEpisode>())
                .Select(MapEpisode)
                .ToList();

            return Tuple.Create(series, episodes);
        }

        public List<Series> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(_configService.TvdbApiKey))
            {
                return _searchProxy.SearchForNewSeries(query);
            }

            EnsureToken();

            // Support tvdb:12345 prefix
            var lower = query.ToLowerInvariant();
            if (lower.StartsWith("tvdb:") || lower.StartsWith("tvdbid:"))
            {
                var slug = lower.Split(':')[1].Trim();
                if (int.TryParse(slug, out var tvdbId) && tvdbId > 0)
                {
                    try
                    {
                        return new List<Series> { GetSeriesInfo(tvdbId.ToString()).Item1 };
                    }
                    catch (SeriesNotFoundException)
                    {
                        return new List<Series>();
                    }
                }

                return new List<Series>();
            }

            var request = new HttpRequestBuilder($"{TvdbApiBase}/search")
                .AddQueryParam("query", query)
                .AddQueryParam("type", "series")
                .Build();
            request.Headers.Add("Authorization", $"Bearer {_bearerToken}");

            var response = _httpClient.Get<TvdbSearchResponse>(request);
            return response?.Resource?.Data?.Select(MapSearchResult).ToList() ?? new List<Series>();
        }

        private void EnsureToken()
        {
            if (_bearerToken != null && DateTime.UtcNow < _tokenExpiry)
            {
                return;
            }

            var apiKey = _configService.TvdbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "TVDB API key is not configured. Go to Settings > Metadata Providers and enter your free TVDB API key from thetvdb.com.");
            }

            var loginRequest = new HttpRequest($"{TvdbApiBase}/login")
            {
                Method = HttpMethod.Post
            };
            loginRequest.Headers.ContentType = "application/json";
            loginRequest.SetContent($"{{\"apikey\":\"{apiKey}\"}}");

            var loginResponse = _httpClient.Execute(loginRequest);
            var tokenResponse = loginResponse.Content;

            // Parse JWT from {"status":"success","data":{"token":"..."}}
            var tokenStart = tokenResponse.IndexOf("\"token\":\"") + 9;
            var tokenEnd = tokenResponse.IndexOf("\"", tokenStart);
            _bearerToken = tokenResponse.Substring(tokenStart, tokenEnd - tokenStart);
            _tokenExpiry = DateTime.UtcNow.AddDays(29); // TVDB tokens expire after 30 days
        }

        private Series MapSeries(TvdbSeriesExtended data)
        {
            var title = data.Name;
            var series = new Series
            {
                TvdbId = data.Id,
                Title = title,
                CleanTitle = title.CleanSeriesTitle(),
                SortTitle = SeriesTitleNormalizer.Normalize(title, data.Id),
                Overview = data.Overview,
                Network = data.OriginalNetwork,
                Status = MapStatus(data.Status?.Name),
                Genres = data.Genres?.Select(g => g.Name).ToList() ?? new List<string>(),
                Images = MapImages(data),
                Seasons = MapSeasons(data),
                OriginalLanguage = Language.English,
                PrimaryMetadataProvider = "tvdb",
                Monitored = true
            };

            if (data.FirstAired.IsNotNullOrWhiteSpace() &&
                DateTime.TryParse(data.FirstAired, out var firstAired))
            {
                series.FirstAired = firstAired.ToUniversalTime();
                series.Year = firstAired.Year;
            }

            if (data.AverageRuntime.HasValue)
            {
                series.Runtime = data.AverageRuntime.Value;
            }

            return series;
        }

        private static Episode MapEpisode(TvdbEpisode ep)
        {
            var episode = new Episode
            {
                TvdbId = ep.Id,
                SeasonNumber = ep.SeasonNumber,
                EpisodeNumber = ep.Number,
                AbsoluteEpisodeNumber = ep.AbsoluteNumber,
                Title = ep.Name,
                Overview = ep.Overview,
                AirDate = ep.Aired,
                Runtime = ep.Runtime ?? 0
            };

            if (ep.Aired.IsNotNullOrWhiteSpace() && DateTime.TryParse(ep.Aired, out var airDate))
            {
                episode.AirDateUtc = airDate.ToUniversalTime();
            }

            if (ep.Image.IsNotNullOrWhiteSpace())
            {
                episode.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Screenshot, ep.Image));
            }

            return episode;
        }

        private static SeriesStatusType MapStatus(string status)
        {
            if (status == null)
            {
                return SeriesStatusType.Continuing;
            }

            return status.ToLowerInvariant() switch
            {
                "ended" => SeriesStatusType.Ended,
                "upcoming" => SeriesStatusType.Upcoming,
                _ => SeriesStatusType.Continuing
            };
        }

        private static List<MediaCover.MediaCover> MapImages(TvdbSeriesExtended data)
        {
            var images = new List<MediaCover.MediaCover>();
            if (data.Image.IsNotNullOrWhiteSpace())
            {
                images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, data.Image));
            }

            return images;
        }

        private static List<Season> MapSeasons(TvdbSeriesExtended data)
        {
            return (data.Seasons ?? new List<TvdbSeason>())
                .Where(s => s.Type?.Type == "official")
                .Select(s => new Season
                {
                    SeasonNumber = s.Number,
                    Monitored = s.Number > 0
                })
                .ToList();
        }

        private Series MapSearchResult(TvdbSearchResult result)
        {
            var existing = result.TvdbId > 0 ? _seriesService.FindByTvdbId(result.TvdbId) : null;
            if (existing != null)
            {
                return existing;
            }

            return new Series
            {
                TvdbId = result.TvdbId,
                Title = result.Name,
                CleanTitle = result.Name.CleanSeriesTitle(),
                Overview = result.Overview,
                Status = MapStatus(result.Status),
                PrimaryMetadataProvider = "tvdb",
                Monitored = true
            };
        }
    }

    // ── Minimal TVDB API v4 response shapes ─────────────────────────────────

    public class TvdbLoginResponse
    {
        public string Status { get; set; }
        public TvdbToken Data { get; set; }
    }

    public class TvdbToken
    {
        public string Token { get; set; }
    }

    public class TvdbSeriesExtendedResponse
    {
        public string Status { get; set; }
        public TvdbSeriesExtended Data { get; set; }
    }

    public class TvdbSeriesExtended
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Overview { get; set; }
        public string Image { get; set; }
        public string FirstAired { get; set; }
        public string OriginalNetwork { get; set; }
        public int? AverageRuntime { get; set; }
        public TvdbStatus Status { get; set; }
        public List<TvdbGenre> Genres { get; set; }
        public List<TvdbSeason> Seasons { get; set; }
        public List<TvdbEpisode> Episodes { get; set; }
    }

    public class TvdbStatus
    {
        public string Name { get; set; }
    }

    public class TvdbGenre
    {
        public string Name { get; set; }
    }

    public class TvdbSeason
    {
        public int Number { get; set; }
        public TvdbSeasonType Type { get; set; }
    }

    public class TvdbSeasonType
    {
        public string Type { get; set; }
    }

    public class TvdbEpisode
    {
        public int Id { get; set; }
        public int SeasonNumber { get; set; }
        public int Number { get; set; }
        public int? AbsoluteNumber { get; set; }
        public string Name { get; set; }
        public string Overview { get; set; }
        public string Aired { get; set; }
        public string Image { get; set; }
        public int? Runtime { get; set; }
    }

    public class TvdbSearchResponse
    {
        public string Status { get; set; }
        public List<TvdbSearchResult> Data { get; set; }
    }

    public class TvdbSearchResult
    {
        [JsonProperty("tvdb_id")]
        public int TvdbId { get; set; }
        public string Name { get; set; }
        public string Overview { get; set; }
        public string Status { get; set; }
        [JsonProperty("image_url")]
        public string Image { get; set; }
    }
}
