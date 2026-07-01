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

namespace NzbDrone.Core.MetadataSource.Mal
{
    /// <summary>
    /// MyAnimeList REST v2 provider (https://api.myanimelist.net/v2).
    /// Requires a free MAL Client ID configured in Settings > Metadata Providers.
    /// Docs: https://myanimelist.net/apiconfig/references/api/v2
    /// </summary>
    public class MalProvider : IMetadataProvider
    {
        private const string MalApiBase = "https://api.myanimelist.net/v2";

        private readonly IHttpClient _httpClient;
        private readonly IConfigFileProvider _configService;
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;

        public MetadataProviderType ProviderType => MetadataProviderType.Mal;

        public MalProvider(IHttpClient httpClient,
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
            externalIdKey is "mal" or "myanimelist" or "malid";

        public Tuple<Series, List<Episode>> GetSeriesInfo(string externalId)
        {
            if (!int.TryParse(externalId, out var malId) || malId <= 0)
            {
                throw new ArgumentException($"Invalid MAL ID: {externalId}");
            }

            var clientId = GetClientId();

            var fields = "id,title,alternative_titles,synopsis,status,start_date,end_date," +
                         "num_episodes,average_episode_duration,genres,main_picture," +
                         "related_anime,studios";

            var request = new HttpRequestBuilder($"{MalApiBase}/anime/{malId}")
                .AddQueryParam("fields", fields)
                .Build();
            request.Headers.Add("X-MAL-CLIENT-ID", clientId);

            var response = _httpClient.Get<MalAnimeResponse>(request);
            if (response?.Resource == null)
            {
                throw new Exception($"MAL returned no data for ID {malId}");
            }

            var series = MapSeries(response.Resource);

            // MAL API v2 doesn't expose individual episode details without OAuth.
            // Generate placeholders from episode count.
            var episodes = GenerateEpisodes(response.Resource);

            return Tuple.Create(series, episodes);
        }

        public List<Series> Search(string query)
        {
            var lower = query.ToLowerInvariant();
            if (lower.StartsWith("mal:"))
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

            var request = new HttpRequestBuilder($"{MalApiBase}/anime")
                .AddQueryParam("q", query)
                .AddQueryParam("limit", 20)
                .AddQueryParam("fields", "id,title,synopsis,status,start_date,num_episodes,main_picture")
                .Build();
            request.Headers.Add("X-MAL-CLIENT-ID", clientId);

            var response = _httpClient.Get<MalSearchResponse>(request);

            return response?.Resource?.Data?
                .Select(d => MapSearchResult(d.Node))
                .ToList() ?? new List<Series>();
        }

        private string GetClientId()
        {
            var clientId = _configService.MalClientId;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException(
                    "MyAnimeList Client ID is not configured. Go to Settings > Metadata Providers and enter your free MAL Client ID from myanimelist.net/apiconfig.");
            }

            return clientId;
        }

        private static Series MapSeries(MalAnimeResponse data)
        {
            var title = data.AlternativeTitles?.En.IsNotNullOrWhiteSpace() == true
                ? data.AlternativeTitles.En
                : data.Title;

            var series = new Series
            {
                Title = title,
                CleanTitle = title.CleanSeriesTitle(),
                SortTitle = SeriesTitleNormalizer.Normalize(title, data.Id),
                TitleSlug = title.ToUrlSlug(),
                MalIds = new HashSet<int> { data.Id },
                Overview = data.Synopsis,
                Runtime = data.AverageEpisodeDuration.HasValue
                    ? (int)Math.Round(data.AverageEpisodeDuration.Value / 60.0) // seconds → minutes
                    : 24,
                Genres = data.Genres?.Select(g => g.Name).ToList() ?? new List<string>(),
                OriginalLanguage = Language.Japanese,
                SeriesType = SeriesTypes.Anime,
                PrimaryMetadataProvider = "mal",
                Monitored = true,
                Status = data.Status?.ToLowerInvariant() switch
                {
                    "finished_airing" => SeriesStatusType.Ended,
                    "not_yet_aired" => SeriesStatusType.Upcoming,
                    _ => SeriesStatusType.Continuing
                }
            };

            if (data.StartDate.IsNotNullOrWhiteSpace() && DateTime.TryParse(data.StartDate, out var firstAired))
            {
                series.FirstAired = firstAired.ToUniversalTime();
                series.Year = firstAired.Year;
            }

            var studio = data.Studios?.FirstOrDefault();
            if (studio != null)
            {
                series.Network = studio.Name;
            }

            if (data.MainPicture?.Large.IsNotNullOrWhiteSpace() == true)
            {
                series.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover(MediaCoverTypes.Poster, data.MainPicture.Large)
                };
            }

            return series;
        }

        private static List<Episode> GenerateEpisodes(MalAnimeResponse data)
        {
            var count = data.NumEpisodes ?? 0;
            var episodes = new List<Episode>(count);

            for (var i = 1; i <= count; i++)
            {
                episodes.Add(new Episode
                {
                    SeasonNumber = 1,
                    EpisodeNumber = i,
                    AbsoluteEpisodeNumber = i,
                    Title = $"Episode {i}",
                    Runtime = data.AverageEpisodeDuration.HasValue
                        ? (int)Math.Round(data.AverageEpisodeDuration.Value / 60.0)
                        : 0,
                    Monitored = true
                });
            }

            return episodes;
        }

        private static Series MapSearchResult(MalAnimeNode node)
        {
            return new Series
            {
                Title = node.Title,
                CleanTitle = node.Title.CleanSeriesTitle(),
                MalIds = new HashSet<int> { node.Id },
                SeriesType = SeriesTypes.Anime,
                PrimaryMetadataProvider = "mal",
                Monitored = true
            };
        }
    }

    // ── MAL response shapes ──────────────────────────────────────────────────

    public class MalAnimeResponse
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public MalAlternativeTitles AlternativeTitles { get; set; }
        public string Synopsis { get; set; }
        public string Status { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public int? NumEpisodes { get; set; }
        public double? AverageEpisodeDuration { get; set; }
        public List<MalGenre> Genres { get; set; }
        public MalPicture MainPicture { get; set; }
        public List<MalStudio> Studios { get; set; }
    }

    public class MalAlternativeTitles
    {
        public string En { get; set; }
        public string Ja { get; set; }
    }

    public class MalGenre
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class MalPicture
    {
        public string Medium { get; set; }
        public string Large { get; set; }
    }

    public class MalStudio
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class MalSearchResponse
    {
        public List<MalSearchNode> Data { get; set; }
    }

    public class MalSearchNode
    {
        public MalAnimeNode Node { get; set; }
    }

    public class MalAnimeNode
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public MalPicture MainPicture { get; set; }
    }
}
