using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public interface IFolderLookupService
    {
        LookupMatch Lookup(string folderName, string folderPath = null, MetadataProviderType? provider = null);
    }

    /// <summary>
    /// Finds the series a library folder holds. Uses what is certain first (a choice the user
    /// made before, ids written into the folder), then searches each provider with cleaned
    /// versions of the folder name and the episode file names, and scores the results so
    /// callers can tell a confident match from a guess.
    /// </summary>
    public class FolderLookupService : IFolderLookupService
    {
        private readonly IMetadataDispatcher _metadataDispatcher;
        private readonly IFolderProbeService _folderProbeService;
        private readonly IImportChoiceStore _importChoiceStore;
        private readonly IAnimeOfflineDatabase _animeOfflineDatabase;
        private readonly IAniDbPrefetchService _aniDbPrefetchService;
        private readonly Logger _logger;

        public FolderLookupService(
            IMetadataDispatcher metadataDispatcher,
            IFolderProbeService folderProbeService,
            IImportChoiceStore importChoiceStore,
            IAnimeOfflineDatabase animeOfflineDatabase,
            IAniDbPrefetchService aniDbPrefetchService,
            Logger logger)
        {
            _aniDbPrefetchService = aniDbPrefetchService;
            _metadataDispatcher = metadataDispatcher;
            _folderProbeService = folderProbeService;
            _importChoiceStore = importChoiceStore;
            _animeOfflineDatabase = animeOfflineDatabase;
            _logger = logger;
        }

        public LookupMatch Lookup(string folderName, string folderPath = null, MetadataProviderType? provider = null)
        {
            var probe = string.IsNullOrWhiteSpace(folderPath) ? new FolderProbe() : _folderProbeService.Probe(folderPath, folderName);

            var searchTerm = SearchTermCleaner.GetCandidates(folderName).FirstOrDefault() ?? folderName.Trim();
            var known = ResolveKnownSeries(folderName, probe);

            if (known != null)
            {
                known.FileCount = probe.VideoFileCount;
                known.SearchTerm = searchTerm;
                Prefetch(known);
                _logger.Info("Folder match '{0}' -> Matched ({1}): {2} ({3}; tvdb={4}, anidb={5})", folderName, known.Reason, known.Results[0].Title, known.Results[0].Year, known.Results[0].TvdbId, known.Results[0].AniDbId);

                return known;
            }

            var terms = SearchTermCleaner.GetCandidates(folderName);

            if (terms.Count == 0)
            {
                terms.Add(folderName.Trim());
            }

            terms.AddRange(probe.FileTitles.Where(f => !terms.Contains(f, StringComparer.OrdinalIgnoreCase)));

            var providers = provider.HasValue
                ? new List<MetadataProviderType> { provider.Value }
                : new List<MetadataProviderType> { MetadataProviderType.Tvdb, MetadataProviderType.AniDb };

            // The same result shows up for several terms and providers; look its aliases up once
            var aliasCache = new ConcurrentDictionary<Series, Lazy<List<(string Title, bool IsOfficial)>>>(ReferenceEqualityComparer.Instance);
            Func<Series, IEnumerable<(string Title, bool IsOfficial)>> aliases = s => aliasCache.GetOrAdd(s, key => new Lazy<List<(string Title, bool IsOfficial)>>(() => AliasesFor(key).ToList())).Value;

            // Providers are independent (one is a network call, one a local database), so
            // search them side by side. Results are joined in provider order, so TVDB still wins ties.
            var searches = providers
                .Select(providerType => Task.Run(() => SearchProvider(providerType, folderName, terms, probe, aliases)))
                .ToArray();

            Task.WaitAll(searches);

            var all = searches.SelectMany(s => s.Result).ToList();

            var match = SeriesMatcher.Evaluate(folderName, all, probe.FileTitles, aliases);

            match.FileCount = probe.VideoFileCount;
            match.SearchTerm = searchTerm;
            Prefetch(match);

            _logger.Info("Folder match '{0}' -> {1} ({2}; {3} exact, {4} result(s), {5} file(s), file title: {6}){7}",
                folderName,
                match.Status,
                match.Reason,
                match.StrongCount,
                match.Results.Count,
                probe.VideoFileCount,
                probe.FileTitles.FirstOrDefault() ?? "-",
                Describe(match));

            return match;
        }

        // A confident AniDB match is what the user will add next: have its hub data cached by then
        private void Prefetch(LookupMatch match)
        {
            if (match.Status != LookupMatchStatus.Matched || match.Results.Count == 0)
            {
                return;
            }

            var top = match.Results[0];

            if (top.AniDbId > 0 && top.TvdbId <= 0)
            {
                _aniDbPrefetchService.Enqueue(top.AniDbId.Value);
            }
        }

        private List<Series> SearchProvider(
            MetadataProviderType providerType,
            string folderName,
            List<string> terms,
            FolderProbe probe,
            Func<Series, IEnumerable<(string Title, bool IsOfficial)>> aliases)
        {
            var found = new List<Series>();

            foreach (var term in terms)
            {
                var results = _metadataDispatcher.Search(term, providerType);

                found.AddRange(results);

                // A term that already found an exact title match needs no further variants
                if (SeriesMatcher.Evaluate(folderName, results, probe.FileTitles, aliases).StrongCount > 0)
                {
                    break;
                }
            }

            return found;
        }

        // A choice the user made before, or an id written in the folder/NFO, settles it without any title matching
        private LookupMatch ResolveKnownSeries(string folderName, FolderProbe probe)
        {
            var hints = new List<(string Provider, int Id, string Reason)>();
            var remembered = _importChoiceStore.Find(folderName);

            if (remembered != null)
            {
                hints.Add((remembered.Provider, remembered.Id, "choice remembered from an earlier import"));
            }

            hints.AddRange(probe.IdHints.Select(h => (h.Provider, h.Id, $"{h.Provider} id {h.Id} found in the folder")));

            foreach (var (hintProvider, id, reason) in hints)
            {
                var series = hintProvider == "tvdb"
                    ? _metadataDispatcher.Search($"tvdb:{id}", MetadataProviderType.Tvdb).FirstOrDefault(s => s.TvdbId == id)
                    : _metadataDispatcher.Search($"anidb:{id}", MetadataProviderType.AniDb).FirstOrDefault(s => s.AniDbId == id);

                if (series != null)
                {
                    return new LookupMatch
                    {
                        Status = LookupMatchStatus.Matched,
                        Reason = reason,
                        StrongCount = 1,
                        Results = new List<Series> { series },
                        Scores = new List<double> { 1.0 }
                    };
                }
            }

            return null;
        }

        // Other names for a result, from the offline anime database (linked by AniList/AniDB/MAL id)
        private IEnumerable<(string Title, bool IsOfficial)> AliasesFor(Series series)
        {
            try
            {
                var entry = series.AniListIds?.Where(id => id > 0).Select(id => _animeOfflineDatabase.GetSeriesById("anilist", id)).FirstOrDefault(e => e != null)
                            ?? (series.AniDbId > 0 ? _animeOfflineDatabase.GetSeriesById("anidb", series.AniDbId.Value) : null)
                            ?? series.MalIds?.Where(id => id > 0).Select(id => _animeOfflineDatabase.GetSeriesById("mal", id)).FirstOrDefault(e => e != null);

                if (entry == null)
                {
                    return Enumerable.Empty<(string Title, bool IsOfficial)>();
                }

                return new[] { entry.Title, entry.EnglishTitle, entry.RomajiTitle, entry.NativeTitle }
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => (t, true))
                    .Concat((entry.SearchSynonyms ?? new List<string>())
                        .Where(t => !string.IsNullOrWhiteSpace(t) && !SearchCriteriaBase.IsSpacelessSlug(t))
                        .Select(t => (t, false)))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not load alias titles for '{0}'", series.Title);

                return Enumerable.Empty<(string Title, bool IsOfficial)>();
            }
        }

        // Top few results with their scores, so a wrong or missing match can be diagnosed from the log
        private static string Describe(LookupMatch match)
        {
            var top = match.Results.Take(4)
                .Select((s, i) => $"{s.Title} ({s.Year}; tvdb={s.TvdbId}, anidb={s.AniDbId}) score={match.Scores[i]:0.00}")
                .ToList();

            return top.Count == 0 ? string.Empty : ": " + string.Join(" | ", top);
        }
    }
}
