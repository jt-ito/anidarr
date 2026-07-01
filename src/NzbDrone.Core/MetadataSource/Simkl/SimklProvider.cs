using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.Simkl
{
    /// <summary>
    /// Simkl REST provider (https://api.simkl.com).
    /// Requires a free Simkl Client ID configured in Settings > Metadata Providers.
    /// Docs: https://simkl.docs.apiary.io/
    /// </summary>
    public class SimklProvider : IMetadataProvider
    {
        private const string SimklApiBase = "https://api.simkl.com";

        private readonly IHttpClient _httpClient;
        private readonly IConfigFileProvider _configService;
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;

        public MetadataProviderType ProviderType => MetadataProviderType.Simkl;

        public SimklProvider(IHttpClient httpClient,
                             IConfigFileProvider configService,
                             ISeriesService seriesService,
                             Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _seriesService = seriesService;
            _logger = logger;
        }

        public bool CanHandleId(string externalIdKey) =>
            externalIdKey is "simkl" or "simklid";

        public Tuple<Series, List<Episode>> GetSeriesInfo(string externalId)
        {
            if (!int.TryParse(externalId, out var simklId) || simklId <= 0)
            {
                throw new ArgumentException($"Invalid Simkl ID: {externalId}");
            }

            var clientId = GetClientId();

            var request = new HttpRequestBuilder($"{SimklApiBase}/anime/{simklId}")
                .AddQueryParam("extended", "full,episodes")
                .Build();
            request.Headers.Add("simkl-api-key", clientId);

            var response = _httpClient.Get<SimklShowResponse>(request);
            if (response?.Resource == null)
            {
                throw new Exception($"Simkl returned no data for ID {simklId}");
            }

            var series = MapSeries(response.Resource, simklId);
            var episodes = (response.Resource.Episodes ?? new List<SimklEpisode>())
                .Select((ep, i) => MapEpisode(ep, i + 1))
                .ToList();

            return Tuple.Create(series, episodes);
        }

        public List<Series> Search(string query)
        {
            var lower = query.ToLowerInvariant();
            if (lower.StartsWith("simkl:"))
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

            var clientId = GetClientId();

            var request = new HttpRequestBuilder($"{SimklApiBase}/search/anime")
                .AddQueryParam("q", query)
                .AddQueryParam("limit", 20)
                .Build();
            request.Headers.Add("simkl-api-key", clientId);

            var response = _httpClient.Get<List<SimklSearchResult>>(request);
            return response?.Resource?
                .Select(r => MapSearchResult(r))
                .ToList() ?? new List<Series>();
        }

        private string GetClientId()
        {
            var clientId = _configService.SimklClientId;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException(
                    "Simkl Client ID is not configured. Go to Settings > Metadata Providers and enter your free Simkl Client ID from simkl.com/apps.");
            }

            return clientId;
        }

        private static Series MapSeries(SimklShowResponse data, int simklId)
        {
            var series = new Series
            {
                Title = data.Title,
                CleanTitle = data.Title.CleanSeriesTitle(),
                SortTitle = SeriesTitleNormalizer.Normalize(data.Title, simklId),
                TitleSlug = data.Title.ToUrlSlug(),
                SimklId = simklId,
                Overview = data.Overview,
                Runtime = data.Runtime ?? 24,
                Genres = data.Genres ?? new List<string>(),
                OriginalLanguage = Language.Japanese,
                SeriesType = SeriesTypes.Anime,
                PrimaryMetadataProvider = "simkl",
                Monitored = true,
                Status = data.Status?.ToLowerInvariant() switch
                {
                    "ended" => SeriesStatusType.Ended,
                    "upcoming" => SeriesStatusType.Upcoming,
                    _ => SeriesStatusType.Continuing
                }
            };

            if (data.Year.HasValue)
            {
                series.Year = data.Year.Value;
            }

            if (data.Ids != null)
            {
                if (data.Ids.Tvdb > 0)
                {
                    series.TvdbId = data.Ids.Tvdb;
                }

                if (data.Ids.Mal > 0)
                {
                    series.MalIds = new HashSet<int> { data.Ids.Mal };
                }

                if (data.Ids.Anilist > 0)
                {
                    series.AniListIds = new HashSet<int> { data.Ids.Anilist };
                }
            }

            if (data.PosterUrl.IsNotNullOrWhiteSpace())
            {
                series.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover(MediaCoverTypes.Poster, data.PosterUrl)
                };
            }

            return series;
        }

        private static Episode MapEpisode(SimklEpisode ep, int absoluteNumber)
        {
            var episode = new Episode
            {
                SeasonNumber = ep.Season ?? 1,
                EpisodeNumber = ep.Episode,
                AbsoluteEpisodeNumber = absoluteNumber,
                Title = ep.Title ?? $"Episode {ep.Episode}",
                Runtime = ep.Runtime ?? 0,
                Monitored = true
            };

            if (ep.Date.IsNotNullOrWhiteSpace() && DateTime.TryParse(ep.Date, out var airDate))
            {
                episode.AirDateUtc = airDate.ToUniversalTime();
                episode.AirDate = airDate.ToString("yyyy-MM-dd");
            }

            return episode;
        }

        private static Series MapSearchResult(SimklSearchResult result)
        {
            return new Series
            {
                Title = result.Title,
                CleanTitle = result.Title.CleanSeriesTitle(),
                SimklId = result.Ids?.Simkl ?? 0,
                Status = SeriesStatusType.Continuing,
                SeriesType = SeriesTypes.Anime,
                PrimaryMetadataProvider = "simkl",
                Monitored = true
            };
        }
    }

    // ── Simkl response shapes ────────────────────────────────────────────────

    public class SimklShowResponse
    {
        public string Title { get; set; }
        public string Overview { get; set; }
        public string Status { get; set; }
        public int? Year { get; set; }
        public int? Runtime { get; set; }
        public string PosterUrl { get; set; }
        public List<string> Genres { get; set; }
        public SimklIds Ids { get; set; }
        public List<SimklEpisode> Episodes { get; set; }
    }

    public class SimklIds
    {
        public int Simkl { get; set; }
        public int Tvdb { get; set; }
        public int Mal { get; set; }
        public int Anilist { get; set; }
        public string Imdb { get; set; }
    }

    public class SimklEpisode
    {
        public string Title { get; set; }
        public int? Season { get; set; }
        public int Episode { get; set; }
        public string Date { get; set; }
        public int? Runtime { get; set; }
    }

    public class SimklSearchResult
    {
        public string Title { get; set; }
        public string Type { get; set; }
        public int? Year { get; set; }
        public SimklIds Ids { get; set; }
        public string PosterUrl { get; set; }
    }
}
