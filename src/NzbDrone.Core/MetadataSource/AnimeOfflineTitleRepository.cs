using System;
using System.Collections.Generic;
using System.Linq;
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
        AnimeOfflineTitle FindByMalId(int malId);
        AnimeOfflineTitle FindByAniListId(int anilistId);
        int GetUnpopulatedRomajiCount();
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
        private static List<CachedSearchEntry> _searchCache;
        private static DateTime _cacheTime = DateTime.MinValue;

        public void ClearFuzzyCache()
        {
            lock (_cacheLock)
            {
                _searchCache = null;
            }
        }

        public AnimeOfflineTitleRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        private List<CachedSearchEntry> GetSearchCache()
        {
            lock (_cacheLock)
            {
                if (_searchCache == null || (DateTime.UtcNow - _cacheTime).TotalHours > 1)
                {
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
                }

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
                else
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
            return Query(c => c.AniDbId == anidbId).FirstOrDefault();
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
    }
}
