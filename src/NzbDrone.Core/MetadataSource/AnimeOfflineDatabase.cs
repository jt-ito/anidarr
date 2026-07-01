using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public interface IAnimeOfflineDatabase
    {
        List<Series> Search(string query, string providerKey);
        AnimeOfflineTitle GetSeriesById(string providerKey, int id);
        void ForceDownloadDump();
        void UpdateMetadata(Series series);
    }

    public class AnimeOfflineDatabase : IAnimeOfflineDatabase
    {
        private const string DumpUrl = "https://github.com/manami-project/anime-offline-database/releases/latest/download/anime-offline-database-minified.json";
        private const string OfficialDumpUrl = "https://anidb.net/api/anime-titles.dat.gz";

        private readonly IHttpClient _httpClient;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IAnimeOfflineTitleRepository _animeOfflineTitleRepository;
        private readonly Logger _logger;

        public AnimeOfflineDatabase(IHttpClient httpClient, IAppFolderInfo appFolderInfo, IAnimeOfflineTitleRepository animeOfflineTitleRepository, Logger logger)
        {
            _httpClient = httpClient;
            _appFolderInfo = appFolderInfo;
            _animeOfflineTitleRepository = animeOfflineTitleRepository;
            _logger = logger;
        }

        public AnimeOfflineTitle GetSeriesById(string providerKey, int id)
        {
            if (providerKey == "anidb")
            {
                return _animeOfflineTitleRepository.FindByAniDbId(id);
            }

            if (providerKey == "mal")
            {
                return _animeOfflineTitleRepository.FindByMalId(id);
            }

            if (providerKey == "anilist")
            {
                return _animeOfflineTitleRepository.FindByAniListId(id);
            }

            if (providerKey == "simkl")
            {
                return _animeOfflineTitleRepository.FindBySimklId(id);
            }

            return null;
        }

        public void UpdateMetadata(Series series)
        {
            if (series.AniDbId.HasValue && series.AniDbId.Value > 0)
            {
                var existing = _animeOfflineTitleRepository.FindByAniDbId(series.AniDbId.Value);
                if (existing != null)
                {
                    var updated = false;
                    if (!string.IsNullOrWhiteSpace(series.Overview) && string.IsNullOrWhiteSpace(existing.Overview))
                    {
                        existing.Overview = series.Overview;
                        updated = true;
                    }

                    var poster = series.Images?.FirstOrDefault(i => i.CoverType == MediaCoverTypes.Poster);
                    if (poster != null && !string.IsNullOrWhiteSpace(poster.Url) && string.IsNullOrWhiteSpace(existing.PictureUrl))
                    {
                        existing.PictureUrl = poster.Url;
                        updated = true;
                    }

                    if (updated)
                    {
                        _animeOfflineTitleRepository.Update(existing);
                        _logger.Debug("Updated local AnimeOfflineTitle for AniDB {0} with rich metadata.", series.AniDbId.Value);
                    }
                }
            }
        }

        public List<Series> Search(string query, string providerKey)
        {
            EnsureCache();

            var cleanQuery = new string(query.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

            _logger.Info("Searching AnimeOfflineDatabase for {0} (Provider: {1})", cleanQuery, providerKey);
            var orderedMatches = _animeOfflineTitleRepository.FindSearchMatches(cleanQuery, providerKey);
            _logger.Info("Found {0} matches in SQLite database for {1}", orderedMatches.Count, cleanQuery);

            orderedMatches = orderedMatches.OrderBy(t => t.Title?.Length ?? 100).Take(20).ToList();

            var results = new List<Series>();

            foreach (var match in orderedMatches)
            {
                var title = match.Title ?? "Unknown Title";
                var series = new Series
                {
                    Title = title,
                    CleanTitle = match.CleanTitle ?? title.CleanSeriesTitle(),
                    SortTitle = SeriesTitleNormalizer.Normalize(title, match.AniDbId ?? 0),
                    TitleSlug = providerKey == "anidb" && match.AniDbId > 0
                        ? $"{title.ToUrlSlug()}-anidb-{match.AniDbId}"
                        : title.ToUrlSlug(),
                    AniDbId = match.AniDbId,
                    PrimaryMetadataProvider = providerKey,
                    SeriesType = SeriesTypes.Anime,
                    Status = match.Status ?? SeriesStatusType.Continuing,
                    Monitored = true,
                    Year = match.Year ?? 0,
                    Genres = match.Genres ?? new List<string>()
                };

                if (match.AniListId > 0)
                {
                    series.AniListIds = new HashSet<int> { match.AniListId.Value };
                }

                if (match.MalId > 0)
                {
                    series.MalIds = new HashSet<int> { match.MalId.Value };
                }

                if (match.SimklId > 0)
                {
                    series.SimklId = match.SimklId.Value;
                }

                if (!string.IsNullOrWhiteSpace(match.Overview))
                {
                    series.Overview = match.Overview;
                }

                if (!string.IsNullOrWhiteSpace(match.PictureUrl))
                {
                    series.Images = new List<NzbDrone.Core.MediaCover.MediaCover>
                    {
                        new NzbDrone.Core.MediaCover.MediaCover { CoverType = MediaCoverTypes.Poster, Url = match.PictureUrl, RemoteUrl = match.PictureUrl }
                    };
                }

                results.Add(series);
            }

            return results;
        }

        private static bool _wiped;
        private void EnsureCache()
        {
            if (!_wiped)
            {
                _animeOfflineTitleRepository.Purge();
                _wiped = true;
            }

            if (_animeOfflineTitleRepository.HasItems())
            {
                return;
            }

            ForceDownloadDump();
        }

        public void ForceDownloadDump()
        {
            var datPath = Path.Combine(_appFolderInfo.AppDataFolder, "anidb_titles.json");
            var officialDatPath = Path.Combine(_appFolderInfo.AppDataFolder, "anime-titles.dat.gz");

            if (File.Exists(datPath) && File.GetLastWriteTimeUtc(datPath) > DateTime.UtcNow.AddHours(-24))
            {
                // throw new NzbDrone.Core.Exceptions.NzbDroneClientException(System.Net.HttpStatusCode.TooManyRequests, "Cannot fetch Anime Offline Database more than once every 24 hours to prevent rate limits.");
            }

            DownloadDump(DumpUrl, datPath);
            DownloadDump(OfficialDumpUrl, officialDatPath);

            ParseAndSyncDumps(datPath, officialDatPath);
        }

        private void DownloadDump(string url, string targetPath)
        {
            _logger.Info("Downloading anime title dump from {0}", url);

            try
            {
                var request = new HttpRequest(url);
                request.AllowAutoRedirect = true;
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
                var response = _httpClient.Get(request);

                File.WriteAllBytes(targetPath, response.ResponseData);
            }
            catch (HttpException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.Warn($"HttpClient forbidden from {url}, attempting curl fallback...");
                DownloadWithCurl(url, targetPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to download anime title dump from {url}.");
            }
        }

        private void DownloadWithCurl(string url, string targetPath)
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "curl";
            process.StartInfo.Arguments = $"-sL -H \"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\" -H \"Accept-Encoding: gzip, deflate, br\" -H \"Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8\" \"{url}\" -o \"{targetPath}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0 || !File.Exists(targetPath) || new FileInfo(targetPath).Length < 100000)
            {
                throw new Exception($"Curl fallback failed to download {url}");
            }
        }

        private void ParseAndSyncDumps(string jsonPath, string officialDatPath)
        {
            _logger.Info("Syncing Anime Offline Titles to database...");

            var existingTitles = _animeOfflineTitleRepository.All().ToDictionary(t => t.AniDbId ?? -1, t => t);
            existingTitles.Remove(-1);

            var manamiDict = new Dictionary<int, AnimeOfflineTitle>();
            var titlePriorities = new Dictionary<int, int>();
            var newTitles = new List<AnimeOfflineTitle>();
            var updatedTitles = new List<AnimeOfflineTitle>();

            // 1. Parse Manami JSON
            if (File.Exists(jsonPath))
            {
                try
                {
                    using (var stream = File.OpenRead(jsonPath))
                    using (var document = JsonDocument.Parse(stream))
                    {
                        var data = document.RootElement.GetProperty("data");
                        foreach (var item in data.EnumerateArray())
                        {
                            if (!item.TryGetProperty("sources", out var sourcesProp))
                            {
                                continue;
                            }

                            var entry = new AnimeOfflineTitle();

                            foreach (var source in sourcesProp.EnumerateArray())
                            {
                                var url = source.GetString();
                                if (url != null)
                                {
                                    if (url.StartsWith("https://anidb.net/anime/"))
                                    {
                                        if (int.TryParse(url.AsSpan("https://anidb.net/anime/".Length), out var id))
                                        {
                                            entry.AniDbId = id;
                                        }
                                    }
                                    else if (url.StartsWith("https://myanimelist.net/anime/"))
                                    {
                                        if (int.TryParse(url.AsSpan("https://myanimelist.net/anime/".Length), out var id))
                                        {
                                            entry.MalId = id;
                                        }
                                    }
                                    else if (url.StartsWith("https://anilist.co/anime/"))
                                    {
                                        if (int.TryParse(url.AsSpan("https://anilist.co/anime/".Length), out var id))
                                        {
                                            entry.AniListId = id;
                                        }
                                    }
                                    else if (url.StartsWith("https://simkl.com/anime/"))
                                    {
                                        var parts = url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length > 0 && int.TryParse(parts.Last(), out var id))
                                        {
                                            entry.SimklId = id;
                                        }
                                    }
                                }
                            }

                            if (!entry.AniDbId.HasValue && !entry.MalId.HasValue && !entry.AniListId.HasValue)
                            {
                                continue;
                            }

                            // title
                            if (item.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                            {
                                entry.Title = titleProp.GetString();
                                if (entry.Title != null)
                                {
                                    entry.CleanTitle = new string(entry.Title.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                                }
                            }

                            // synonyms
                            if (item.TryGetProperty("synonyms", out var synProp) && synProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var synElement in synProp.EnumerateArray())
                                {
                                    if (synElement.ValueKind == JsonValueKind.String)
                                    {
                                        var syn = synElement.GetString();
                                        if (syn != null)
                                        {
                                            entry.SearchSynonyms.Add(new string(syn.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant());
                                        }
                                    }
                                }
                            }

                            // picture
                            if (item.TryGetProperty("picture", out var picProp) && picProp.ValueKind == JsonValueKind.String)
                            {
                                entry.PictureUrl = picProp.GetString();
                            }

                            // year
                            if (item.TryGetProperty("animeSeason", out var seasonProp) && seasonProp.ValueKind == JsonValueKind.Object)
                            {
                                if (seasonProp.TryGetProperty("year", out var yearProp) && yearProp.ValueKind == JsonValueKind.Number)
                                {
                                    entry.Year = yearProp.GetInt32();
                                }
                            }

                            // genres
                            if (item.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var tag in tagsProp.EnumerateArray())
                                {
                                    if (tag.ValueKind == JsonValueKind.String)
                                    {
                                        entry.Genres.Add(tag.GetString());
                                    }
                                }
                            }

                            // status
                            if (item.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.String)
                            {
                                var statusStr = statusProp.GetString();
                                if (statusStr == "FINISHED")
                                {
                                    entry.Status = SeriesStatusType.Ended;
                                }
                                else if (statusStr == "ONGOING")
                                {
                                    entry.Status = SeriesStatusType.Continuing;
                                }
                                else if (statusStr == "UPCOMING")
                                {
                                    entry.Status = SeriesStatusType.Upcoming;
                                }
                                else
                                {
                                    entry.Status = SeriesStatusType.Continuing;
                                }
                            }
                            else
                            {
                                entry.Status = SeriesStatusType.Continuing;
                            }

                            if (entry.AniDbId.HasValue && !manamiDict.ContainsKey(entry.AniDbId.Value))
                            {
                                manamiDict[entry.AniDbId.Value] = entry;
                                titlePriorities[entry.AniDbId.Value] = 2; // Default Manami priority (equivalent to Romaji/English mix)
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse manami anime-offline-database.");
                }
            }

            // 2. Parse Official AniDB Dat & Merge
            if (File.Exists(officialDatPath))
            {
                try
                {
                    using (var fs = File.OpenRead(officialDatPath))
                    using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                    using (var reader = new StreamReader(gz))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                            {
                                continue;
                            }

                            var parts = line.Split('|');
                            if (parts.Length < 4)
                            {
                                continue;
                            }

                            if (!int.TryParse(parts[0], out var anidbId))
                            {
                                continue;
                            }

                            var type = parts[1]; // 1=primary, 2=synonym, 3=short, 4=official
                            var language = parts[2];
                            var title = parts[3];

                            if (string.IsNullOrWhiteSpace(title))
                            {
                                continue;
                            }

                            var cleanTitlePart = new string(title.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

                            if (!manamiDict.TryGetValue(anidbId, out var entry))
                            {
                                entry = new AnimeOfflineTitle
                                {
                                    AniDbId = anidbId,
                                    Status = SeriesStatusType.Continuing
                                };
                                manamiDict[anidbId] = entry;
                                titlePriorities[anidbId] = -1;
                            }

                            var priority = 0;
                            if (type == "1" || type == "4")
                            {
                                if (language.Equals("en", StringComparison.OrdinalIgnoreCase))
                                {
                                    priority = 4;
                                }
                                else if (language.Equals("x-jat", StringComparison.OrdinalIgnoreCase))
                                {
                                    priority = 3;
                                }
                                else
                                {
                                    priority = 1;
                                }
                            }

                            if (!titlePriorities.TryGetValue(anidbId, out var currentPriority))
                            {
                                currentPriority = -1;
                            }

                            if (priority > currentPriority || string.IsNullOrEmpty(entry.Title))
                            {
                                entry.Title = title;
                                entry.CleanTitle = cleanTitlePart;
                                titlePriorities[anidbId] = priority;
                            }

                            if (!entry.SearchSynonyms.Contains(cleanTitlePart))
                            {
                                entry.SearchSynonyms.Add(cleanTitlePart);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse official anime-titles.dat.gz.");
                }
            }

            // 3. Database Upsert
            foreach (var entry in manamiDict.Values)
            {
                if (entry.AniDbId.HasValue && existingTitles.TryGetValue(entry.AniDbId.Value, out var existing))
                {
                    var changed = false;

                    if (existing.Title != entry.Title)
                    {
                        existing.Title = entry.Title;
                        changed = true;
                    }

                    if (existing.CleanTitle != entry.CleanTitle)
                    {
                        existing.CleanTitle = entry.CleanTitle;
                        changed = true;
                    }

                    if (existing.MalId != entry.MalId)
                    {
                        existing.MalId = entry.MalId;
                        changed = true;
                    }

                    if (existing.AniListId != entry.AniListId)
                    {
                        existing.AniListId = entry.AniListId;
                        changed = true;
                    }

                    if (existing.SimklId != entry.SimklId)
                    {
                        existing.SimklId = entry.SimklId;
                        changed = true;
                    }

                    if (existing.Year != entry.Year)
                    {
                        existing.Year = entry.Year;
                        changed = true;
                    }

                    if (existing.Status != entry.Status)
                    {
                        existing.Status = entry.Status;
                        changed = true;
                    }

                    // Don't overwrite rich metadata if Manami doesn't have it but we already enriched it
                    if (!string.IsNullOrWhiteSpace(entry.PictureUrl) && existing.PictureUrl != entry.PictureUrl)
                    {
                        existing.PictureUrl = entry.PictureUrl;
                        changed = true;
                    }

                    if (!string.IsNullOrWhiteSpace(entry.Overview) && existing.Overview != entry.Overview)
                    {
                        existing.Overview = entry.Overview;
                        changed = true;
                    }

                    // Ensure genres
                    if (entry.Genres != null && entry.Genres.Any())
                    {
                        var mergedGenres = existing.Genres.Union(entry.Genres).ToList();
                        if (mergedGenres.Count != existing.Genres.Count)
                        {
                            existing.Genres = mergedGenres;
                            changed = true;
                        }
                    }

                    // Ensure synonyms
                    if (entry.SearchSynonyms != null && entry.SearchSynonyms.Any())
                    {
                        var mergedSyn = existing.SearchSynonyms.Union(entry.SearchSynonyms).ToList();
                        if (mergedSyn.Count != existing.SearchSynonyms.Count)
                        {
                            existing.SearchSynonyms = mergedSyn;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        updatedTitles.Add(existing);
                    }
                }
                else
                {
                    newTitles.Add(entry);
                }
            }

            if (newTitles.Any())
            {
                _animeOfflineTitleRepository.InsertMany(newTitles);
                _logger.Info("Inserted {0} new anime titles.", newTitles.Count);
            }

            if (updatedTitles.Any())
            {
                _animeOfflineTitleRepository.UpdateMany(updatedTitles);
                _logger.Info("Updated {0} existing anime titles.", updatedTitles.Count);
            }

            _logger.Info("Finished syncing Anime Offline Titles database.");
        }
    }
}
