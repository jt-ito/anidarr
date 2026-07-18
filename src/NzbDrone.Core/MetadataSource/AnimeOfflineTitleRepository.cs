using System.Collections.Generic;
using System.Linq;
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
    }

    public class AnimeOfflineTitleRepository : BasicRepository<AnimeOfflineTitle>, IAnimeOfflineTitleRepository
    {
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

            // Add synonym matches that weren't caught by the CleanTitle query
            var synonymMatches = Query(c =>
                    (c.CleanTitle == null || !c.CleanTitle.Contains(cleanQuery)) &&
                    c.SearchSynonyms != null && c.SearchSynonyms.Contains(cleanQuery))
                .Where(c => c.SearchSynonyms.Any(s => s.Contains(cleanQuery)));

            results = results.Union(synonymMatches);

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
    }
}
