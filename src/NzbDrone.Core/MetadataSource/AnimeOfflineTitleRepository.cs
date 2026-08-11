using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
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
    }

    public class AnimeOfflineTitleRepository : BasicRepository<AnimeOfflineTitle>, IAnimeOfflineTitleRepository
    {
        private static readonly object _fuzzyCacheLock = new object();
        private static List<AnimeOfflineTitle> _fuzzyCache;
        private static DateTime _fuzzyCacheTime = DateTime.MinValue;

        public AnimeOfflineTitleRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<AnimeOfflineTitle> FindSearchMatches(string cleanQuery, string providerKey)
        {
            // ponytail: load by CleanTitle match from DB, then check synonyms in-memory
            // with exact equality. Using Contains() on a serialized JSON list column would
            // be a substring match on raw JSON and produce false positives.
            IEnumerable<AnimeOfflineTitle> results = Query(c =>
                c.CleanTitle != null && c.CleanTitle.Contains(cleanQuery));

            // Add synonym matches that weren't caught by the CleanTitle query.
            var synonymMatches = Query(c => c.SearchSynonyms != null)
                .Where(c => (c.CleanTitle == null || !c.CleanTitle.Contains(cleanQuery)) &&
                            c.SearchSynonyms.Any(s => s.CleanForSearch().Contains(cleanQuery)));

            results = results.Union(synonymMatches);

            // Fuzzy matching
            List<AnimeOfflineTitle> allTitles;
            lock (_fuzzyCacheLock)
            {
                if (_fuzzyCache == null || (DateTime.UtcNow - _fuzzyCacheTime).TotalHours > 1)
                {
                    _fuzzyCache = All().ToList();
                    _fuzzyCacheTime = DateTime.UtcNow;
                }

                allTitles = _fuzzyCache;
            }

            var fuzzyMatches = new List<AnimeOfflineTitle>();
            var existingIds = new HashSet<int>(results.Select(r => r.Id));

            foreach (var candidate in allTitles)
            {
                if (existingIds.Contains(candidate.Id))
                {
                    continue;
                }

                var isMatch = false;

                // 1. Check CleanTitle
                if (candidate.CleanTitle != null)
                {
                    var allowed = candidate.CleanTitle.GetAllowedEdits(cleanQuery);
                    if (Math.Abs(candidate.CleanTitle.Length - cleanQuery.Length) <= allowed)
                    {
                        if (candidate.CleanTitle.LevenshteinDistance(cleanQuery) <= allowed)
                        {
                            isMatch = true;
                        }
                    }
                }

                // 2. Check Synonyms
                if (!isMatch && candidate.SearchSynonyms != null)
                {
                    foreach (var synonym in candidate.SearchSynonyms)
                    {
                        var cleanSynonym = synonym.CleanForSearch();
                        var allowed = cleanSynonym.GetAllowedEdits(cleanQuery);
                        if (Math.Abs(cleanSynonym.Length - cleanQuery.Length) <= allowed)
                        {
                            if (cleanSynonym.LevenshteinDistance(cleanQuery) <= allowed)
                            {
                                isMatch = true;
                                break;
                            }
                        }
                    }
                }

                if (isMatch)
                {
                    fuzzyMatches.Add(candidate);
                }
            }

            results = results.Union(fuzzyMatches);

            if (providerKey == "anidb")
            {
                results = results.Where(c => c.AniDbId > 0);
            }
            else if (providerKey == "mal")
            {
                results = results.Where(c => c.MalId > 0);
            }
            else if (providerKey == "anilist")
            {
                results = results.Where(c => c.AniListId > 0);
            }

            return results.Take(50).ToList();
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
