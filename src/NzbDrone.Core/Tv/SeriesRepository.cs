using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public interface ISeriesRepository : IBasicRepository<Series>
    {
        bool SeriesPathExists(string path);
        Series FindByTitle(string cleanTitle);
        Series FindByTitle(string cleanTitle, int year);
        List<Series> FindByTitleInexact(string cleanTitle);
        Series FindByTvdbId(int tvdbId);
        Series FindByTvRageId(int tvRageId);
        Series FindByImdbId(string imdbId);
        Series FindByPath(string path);
        List<int> AllSeriesTvdbIds();
        Dictionary<int, string> AllSeriesPaths();
        Dictionary<int, List<int>> AllSeriesTags();
        Dictionary<int, int> AllSeriesQualityProfiles();
    }

    public class SeriesRepository : BasicRepository<Series>, ISeriesRepository, IHandle<SeriesAddedEvent>, IHandle<SeriesUpdatedEvent>, IHandle<SeriesDeletedEvent>
    {
        private readonly object _cacheLock = new object();
        private ConcurrentDictionary<int, List<string>> _alternateTitlesCache;

        public SeriesRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        private void EnsureCache()
        {
            if (_alternateTitlesCache == null)
            {
                lock (_cacheLock)
                {
                    if (_alternateTitlesCache == null)
                    {
                        var seriesWithAlternateTitles = Query(s => s.AlternateTitles != null).ToList();
                        var cache = new ConcurrentDictionary<int, List<string>>();

                        foreach (var series in seriesWithAlternateTitles)
                        {
                            if (series.AlternateTitles != null && series.AlternateTitles.Count > 0)
                            {
                                var cleanedTitles = series.AlternateTitles.Select(NzbDrone.Core.Parser.Parser.CleanSeriesTitle).Distinct().ToList();
                                cache[series.Id] = cleanedTitles;
                            }
                        }

                        _alternateTitlesCache = cache;
                    }
                }
            }
        }

        public void Handle(SeriesAddedEvent message)
        {
            UpdateCache(message.Series);
        }

        public void Handle(SeriesUpdatedEvent message)
        {
            UpdateCache(message.Series);
        }

        public void Handle(SeriesDeletedEvent message)
        {
            if (_alternateTitlesCache != null)
            {
                foreach (var series in message.Series)
                {
                    _alternateTitlesCache.TryRemove(series.Id, out _);
                }
            }
        }

        private void UpdateCache(Series series)
        {
            if (_alternateTitlesCache != null)
            {
                if (series.AlternateTitles != null && series.AlternateTitles.Count > 0)
                {
                    var cleanedTitles = series.AlternateTitles.Select(NzbDrone.Core.Parser.Parser.CleanSeriesTitle).Distinct().ToList();
                    _alternateTitlesCache[series.Id] = cleanedTitles;
                }
                else
                {
                    _alternateTitlesCache.TryRemove(series.Id, out _);
                }
            }
        }

        public bool SeriesPathExists(string path)
        {
            return Query(c => c.Path == path).Any();
        }

        public Series FindByTitle(string cleanTitle)
        {
            cleanTitle = cleanTitle.ToLowerInvariant();

            var series = Query(s => s.CleanTitle == cleanTitle).ToList();

            if (series.Count == 0)
            {
                EnsureCache();
                var matchingIds = _alternateTitlesCache.Where(kvp => kvp.Value.Contains(cleanTitle)).Select(kvp => kvp.Key).ToList();
                if (matchingIds.Any())
                {
                    series.AddRange(Get(matchingIds));
                }
            }

            return ReturnSingleSeriesOrThrow(series);
        }

        public Series FindByTitle(string cleanTitle, int year)
        {
            cleanTitle = cleanTitle.ToLowerInvariant();

            var series = Query(s => s.CleanTitle == cleanTitle && s.Year == year).ToList();

            if (series.Count == 0)
            {
                EnsureCache();
                var matchingIds = _alternateTitlesCache.Where(kvp => kvp.Value.Contains(cleanTitle)).Select(kvp => kvp.Key).ToList();
                if (matchingIds.Any())
                {
                    series.AddRange(Get(matchingIds).Where(s => s.Year == year));
                }
            }

            return ReturnSingleSeriesOrThrow(series);
        }

        public List<Series> FindByTitleInexact(string cleanTitle)
        {
            var builder = Builder().Where($"instr(@cleanTitle, \"Series\".\"CleanTitle\")", new { cleanTitle = cleanTitle });

            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                builder = Builder().Where($"(strpos(@cleanTitle, \"Series\".\"CleanTitle\") > 0)", new { cleanTitle = cleanTitle });
            }

            var exactMatches = Query(builder).ToList();

            // Fetch matching IDs from the in-memory cache
            EnsureCache();
            var matchingIds = _alternateTitlesCache.Where(kvp => kvp.Value.Any(at => cleanTitle.Contains(at))).Select(kvp => kvp.Key).ToList();

            if (matchingIds.Any())
            {
                var alternateMatches = Get(matchingIds);
                return exactMatches.Union(alternateMatches).GroupBy(s => s.Id).Select(g => g.First()).ToList();
            }

            return exactMatches.GroupBy(s => s.Id).Select(g => g.First()).ToList();
        }

        public Series FindByTvdbId(int tvdbId)
        {
            return Query(s => s.TvdbId == tvdbId).SingleOrDefault();
        }

        public Series FindByTvRageId(int tvRageId)
        {
            return Query(s => s.TvRageId == tvRageId).SingleOrDefault();
        }

        public Series FindByImdbId(string imdbId)
        {
            return Query(s => s.ImdbId == imdbId).SingleOrDefault();
        }

        public Series FindByPath(string path)
        {
            return Query(s => s.Path == path)
                        .FirstOrDefault();
        }

        public List<int> AllSeriesTvdbIds()
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.Query<int>("SELECT \"TvdbId\" FROM \"Series\"").ToList();
            }
        }

        public Dictionary<int, string> AllSeriesPaths()
        {
            using (var conn = _database.OpenConnection())
            {
                var strSql = "SELECT \"Id\" AS Key, \"Path\" AS Value FROM \"Series\"";
                return conn.Query<KeyValuePair<int, string>>(strSql).ToDictionary(x => x.Key, x => x.Value);
            }
        }

        public Dictionary<int, List<int>> AllSeriesTags()
        {
            using (var conn = _database.OpenConnection())
            {
                var strSql = "SELECT \"Id\" AS Key, \"Tags\" AS Value FROM \"Series\" WHERE \"Tags\" IS NOT NULL";
                return conn.Query<KeyValuePair<int, List<int>>>(strSql).ToDictionary(x => x.Key, x => x.Value);
            }
        }

        public Dictionary<int, int> AllSeriesQualityProfiles()
        {
            using (var conn = _database.OpenConnection())
            {
                var strSql = "SELECT \"Id\" AS Key, \"QualityProfileId\" AS Value FROM \"Series\"";
                return conn.Query<KeyValuePair<int, int>>(strSql).ToDictionary(x => x.Key, x => x.Value);
            }
        }

        private Series ReturnSingleSeriesOrThrow(List<Series> series)
        {
            if (series.Count == 0)
            {
                return null;
            }

            if (series.Count == 1)
            {
                return series.First();
            }

            throw new MultipleSeriesFoundException(series, "Expected one series, but found {0}. Matching series: {1}", series.Count, string.Join(", ", series));
        }
    }
}
