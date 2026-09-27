using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MetadataSource
{
    public interface IAnimeOfflineTitleRepository : IBasicRepository<AnimeOfflineTitle>
    {
        List<AnimeOfflineTitle> FindSearchMatches(string cleanQuery, string providerKey);
        AnimeOfflineTitle FindByAniDbId(int anidbId);
        Dictionary<int, AnimeOfflineTitle> FindByAniDbIds(IEnumerable<int> anidbIds);
        AnimeOfflineTitle FindByMalId(int malId);
        AnimeOfflineTitle FindByAniListId(int anilistId);
        int GetUnpopulatedRomajiCount();
        int GetPopulatedPictureCount();
        void ClearFuzzyCache();
    }

    public class AnimeOfflineTitleRepository : BasicRepository<AnimeOfflineTitle>, IAnimeOfflineTitleRepository
    {
        private class CachedSearchEntry
        {
            public AnimeOfflineTitle Title { get; set; }
            public string CleanTitle { get; set; }
            public string[] CleanSynonyms { get; set; }
        }

        private static readonly object _cacheLock = new object();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, AnimeOfflineTitle> _anidbIdCache = new();
        private static List<CachedSearchEntry> _searchCache;
        private static DateTime _cacheTime = DateTime.MinValue;
        private static int _searchCacheRebuilding;

        public void ClearFuzzyCache()
        {
            lock (_cacheLock)
            {
                _searchCache = null;
            }

            _anidbIdCache.Clear();
        }

        public AnimeOfflineTitleRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        // Anidarr: stale-while-revalidate. Rebuilding scans the entire offline-title
        // table (tens of thousands of rows for the anime-offline-database) plus
        // per-title/synonym string cleanup, and this used to happen under a lock that
        // every concurrent caller — every add-series search, every cover-fallback
        // lookup — blocked on once an hour. Now a stale cache is still served
        // immediately, one background rebuild is kicked off (guarded by
        // _searchCacheRebuilding so concurrent stale hits don't each start their own),
        // and the reference is swapped in once it's ready. Only the very first,
        // cold-start build (no cache yet) still blocks, since there's nothing to serve.
        private List<CachedSearchEntry> GetSearchCache()
        {
            var cache = _searchCache;

            if (cache == null)
            {
                return RebuildSearchCache();
            }

            if ((DateTime.UtcNow - _cacheTime).TotalHours > 1 &&
                Interlocked.CompareExchange(ref _searchCacheRebuilding, 1, 0) == 0)
            {
                Task.Run(() =>
                {
                    try
                    {
                        RebuildSearchCache();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _searchCacheRebuilding, 0);
                    }
                });
            }

            return cache;
        }

        private List<CachedSearchEntry> RebuildSearchCache()
        {
            lock (_cacheLock)
            {
                // Re-check freshness inside the lock in case another thread already
                // rebuilt while this one was waiting (relevant for the cold-start path,
                // where multiple first callers can all reach here concurrently).
                if (_searchCache != null && (DateTime.UtcNow - _cacheTime).TotalHours <= 1)
                {
                    return _searchCache;
                }

                var all = All().ToList();
                var list = new List<CachedSearchEntry>(all.Count);

                foreach (var item in all)
                {
                    var cleanTitle = item.CleanTitle ?? item.Title?.CleanForSearch();
                    var cleanSyns = item.SearchSynonyms?
                        .Where(s => !SearchCriteriaBase.IsSpacelessSlug(s))
                        .Select(s => s.CleanForSearch())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Distinct()
                        .ToArray() ?? Array.Empty<string>();

                    list.Add(new CachedSearchEntry
                    {
                        Title = item,
                        CleanTitle = cleanTitle,
                        CleanSynonyms = cleanSyns
                    });
                }

                _searchCache = list;
                _cacheTime = DateTime.UtcNow;

                return _searchCache;
            }
        }

        public List<AnimeOfflineTitle> FindSearchMatches(string cleanQuery, string providerKey)
        {
            if (string.IsNullOrWhiteSpace(cleanQuery))
            {
                return new List<AnimeOfflineTitle>();
            }

            var entries = GetSearchCache();
            var substringMatches = new List<AnimeOfflineTitle>();
            var fuzzyMatches = new List<AnimeOfflineTitle>();

            foreach (var entry in entries)
            {
                if (providerKey == "anidb" && (entry.Title.AniDbId == null || entry.Title.AniDbId <= 0))
                {
                    continue;
                }

                if (providerKey == "mal" && (entry.Title.MalId == null || entry.Title.MalId <= 0))
                {
                    continue;
                }

                if (providerKey == "anilist" && (entry.Title.AniListId == null || entry.Title.AniListId <= 0))
                {
                    continue;
                }

                var isSubstringMatch = false;

                if (entry.CleanTitle != null && entry.CleanTitle.Contains(cleanQuery))
                {
                    isSubstringMatch = true;
                }
                else if (entry.CleanSynonyms.Length > 0)
                {
                    for (var i = 0; i < entry.CleanSynonyms.Length; i++)
                    {
                        if (entry.CleanSynonyms[i].Contains(cleanQuery))
                        {
                            isSubstringMatch = true;
                            break;
                        }
                    }
                }

                if (isSubstringMatch)
                {
                    substringMatches.Add(entry.Title);
                }
                else if (substringMatches.Count + fuzzyMatches.Count < 50)
                {
                    var isFuzzyMatch = false;

                    // 1. Check CleanTitle
                    if (entry.CleanTitle != null)
                    {
                        var allowed = entry.CleanTitle.GetAllowedEdits(cleanQuery);
                        if (Math.Abs(entry.CleanTitle.Length - cleanQuery.Length) <= allowed)
                        {
                            if (entry.CleanTitle.LevenshteinDistance(cleanQuery) <= allowed)
                            {
                                isFuzzyMatch = true;
                            }
                        }
                    }

                    // 2. Check Synonyms
                    if (!isFuzzyMatch && entry.CleanSynonyms.Length > 0)
                    {
                        for (var i = 0; i < entry.CleanSynonyms.Length; i++)
                        {
                            var cleanSynonym = entry.CleanSynonyms[i];
                            var allowed = cleanSynonym.GetAllowedEdits(cleanQuery);
                            if (Math.Abs(cleanSynonym.Length - cleanQuery.Length) <= allowed)
                            {
                                if (cleanSynonym.LevenshteinDistance(cleanQuery) <= allowed)
                                {
                                    isFuzzyMatch = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (isFuzzyMatch)
                    {
                        fuzzyMatches.Add(entry.Title);
                    }
                }
            }

            return substringMatches.Concat(fuzzyMatches).Take(50).ToList();
        }

        public new AnimeOfflineTitle Insert(AnimeOfflineTitle model)
        {
            var result = base.Insert(model);
            ClearFuzzyCache();
            return result;
        }

        public new void InsertMany(IList<AnimeOfflineTitle> models)
        {
            base.InsertMany(models);
            ClearFuzzyCache();
        }

        public new AnimeOfflineTitle Update(AnimeOfflineTitle model)
        {
            var result = base.Update(model);
            ClearFuzzyCache();
            return result;
        }

        public new void UpdateMany(IList<AnimeOfflineTitle> models)
        {
            base.UpdateMany(models);
            ClearFuzzyCache();
        }

        public new void Purge(bool vacuum = false)
        {
            base.Purge(vacuum);
            ClearFuzzyCache();
        }

        public AnimeOfflineTitle FindByAniDbId(int anidbId)
        {
            if (_anidbIdCache.TryGetValue(anidbId, out var cached))
            {
                return cached;
            }

            var result = Query(c => c.AniDbId == anidbId).FirstOrDefault();
            _anidbIdCache[anidbId] = result;

            return result;
        }

        public Dictionary<int, AnimeOfflineTitle> FindByAniDbIds(IEnumerable<int> anidbIds)
        {
            if (anidbIds == null)
            {
                return new Dictionary<int, AnimeOfflineTitle>();
            }

            var distinctIds = anidbIds.Where(id => id > 0).Distinct().ToList();
            var result = new Dictionary<int, AnimeOfflineTitle>(distinctIds.Count);
            var missingIds = new List<int>();

            foreach (var id in distinctIds)
            {
                if (_anidbIdCache.TryGetValue(id, out var cached))
                {
                    if (cached != null)
                    {
                        result[id] = cached;
                    }
                }
                else
                {
                    missingIds.Add(id);
                }
            }

            if (missingIds.Count > 0)
            {
                foreach (var chunk in missingIds.Chunk(500))
                {
                    var chunkList = chunk.ToList();
                    var titles = Query(c => chunkList.Contains(c.AniDbId.Value)).ToList();
                    var foundIds = new HashSet<int>();

                    foreach (var title in titles)
                    {
                        if (title.AniDbId.HasValue)
                        {
                            _anidbIdCache[title.AniDbId.Value] = title;
                            result[title.AniDbId.Value] = title;
                            foundIds.Add(title.AniDbId.Value);
                        }
                    }

                    foreach (var id in chunkList)
                    {
                        if (!foundIds.Contains(id))
                        {
                            _anidbIdCache[id] = null;
                        }
                    }
                }
            }

            return result;
        }

        public AnimeOfflineTitle FindByMalId(int malId)
        {
            return Query(c => c.MalId == malId).FirstOrDefault();
        }

        public AnimeOfflineTitle FindByAniListId(int anilistId)
        {
            return Query(c => c.AniListId == anilistId).FirstOrDefault();
        }

        public int GetUnpopulatedRomajiCount()
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{_table}\" WHERE RomajiTitle IS NULL");
            }
        }

        public int GetPopulatedPictureCount()
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{_table}\" WHERE PictureUrl IS NOT NULL AND PictureUrl != ''");
            }
        }
    }
}
