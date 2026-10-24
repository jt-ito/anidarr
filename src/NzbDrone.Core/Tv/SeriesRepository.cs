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
        int CountPendingMetadata();
        Dictionary<int, string> AllSeriesPaths();
        Dictionary<int, List<int>> AllSeriesTags();
        Dictionary<int, int> AllSeriesQualityProfiles();
    }

    public class SeriesRepository : BasicRepository<Series>, ISeriesRepository, IHandle<SeriesAddedEvent>, IHandle<SeriesUpdatedEvent>, IHandle<SeriesDeletedEvent>
    {
        private readonly object _cacheLock = new object();
        private AlternateTitleIndex _alternateTitles;

        public SeriesRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        private class AlternateTitlesRow
        {
            public int Id { get; set; }
            public List<string> AlternateTitles { get; set; }
        }

        // Only the id and the alternate titles of every series that has some. Loading whole series for this
        // reads and parses every other column of every series as well (images, seasons, actors, ...).
        public Dictionary<int, List<string>> AllAlternateTitles()
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.Query<AlternateTitlesRow>("SELECT \"Id\", \"AlternateTitles\" FROM \"Series\" WHERE \"AlternateTitles\" IS NOT NULL")
                    .Where(r => r.AlternateTitles != null && r.AlternateTitles.Count > 0)
                    .ToDictionary(r => r.Id, r => r.AlternateTitles);
            }
        }

        private void EnsureCache()
        {
            if (_alternateTitles == null)
            {
                lock (_cacheLock)
                {
                    if (_alternateTitles == null)
                    {
                        // Cleaning every alternate title (a library has hundreds of thousands) is most of the time
                        // this takes, and each series is independent, so clean them in parallel
                        var cleanedTitles = AllAlternateTitles()
                            .AsParallel()
                            .Select(entry => new KeyValuePair<int, List<string>>(entry.Key, entry.Value.Select(NzbDrone.Core.Parser.Parser.CleanSeriesTitle).ToList()))
                            .ToList();

                        var index = new AlternateTitleIndex();

                        foreach (var entry in cleanedTitles)
                        {
                            index.Set(entry.Key, entry.Value);
                        }

                        _alternateTitles = index;
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
            if (_alternateTitles != null)
            {
                foreach (var series in message.Series)
                {
                    _alternateTitles.Remove(series.Id);
                }
            }
        }

        private void UpdateCache(Series series)
        {
            if (_alternateTitles != null)
            {
                if (series.AlternateTitles != null && series.AlternateTitles.Count > 0)
                {
                    _alternateTitles.Set(series.Id, series.AlternateTitles.Select(NzbDrone.Core.Parser.Parser.CleanSeriesTitle));
                }
                else
                {
                    _alternateTitles.Remove(series.Id);
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
                var matchingIds = _alternateTitles.FindExact(cleanTitle);
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
                var matchingIds = _alternateTitles.FindExact(cleanTitle);
                if (matchingIds.Any())
                {
                    series.AddRange(Get(matchingIds).Where(s => s.Year == year));
                }
            }

            return ReturnSingleSeriesOrThrow(series);
        }

        public List<Series> FindByTitleInexact(string cleanTitle)
        {
            var maxLength = cleanTitle.Length < 5 ? 0 : (int)(cleanTitle.Length / 0.4);
            var builder = Builder().Where($"(instr(@cleanTitle, \"Series\".\"CleanTitle\") > 0 OR (instr(\"Series\".\"CleanTitle\", @cleanTitle) > 0 AND length(\"Series\".\"CleanTitle\") <= @maxLength))", new { cleanTitle = cleanTitle, maxLength = maxLength });

            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                builder = Builder().Where($"(strpos(@cleanTitle, \"Series\".\"CleanTitle\") > 0 OR (strpos(\"Series\".\"CleanTitle\", @cleanTitle) > 0 AND length(\"Series\".\"CleanTitle\") <= @maxLength))", new { cleanTitle = cleanTitle, maxLength = maxLength });
            }

            var exactMatches = Query(builder).ToList();

            // Fetch matching IDs from the in-memory cache
            EnsureCache();
            var matchingIds = _alternateTitles.FindSimilar(cleanTitle);

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

        // AniDB-only series (no real tvdb id) that were added without their metadata and have not been
        // refreshed since: the ones still being updated in the background
        public int CountPendingMetadata()
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM \"Series\" WHERE \"LastInfoSync\" IS NULL AND \"AniDbId\" > 0 AND \"TvdbId\" <= 0");
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
