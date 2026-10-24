using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Tv
{
    /// <summary>
    /// Finds series by their alternate titles. Looking a title up by scanning every alternate title of
    /// every series costs time in proportion to the size of the library, for every release that is looked
    /// up. This keeps the titles in a hash table, grouped by length, so a lookup only touches the titles
    /// that can match.
    /// </summary>
    public class AlternateTitleIndex
    {
        private readonly object _lock = new object();
        private readonly Dictionary<int, List<string>> _titlesBySeries = new Dictionary<int, List<string>>();
        private readonly Dictionary<int, Dictionary<string, List<int>>> _seriesByTitleByLength = new Dictionary<int, Dictionary<string, List<int>>>();

        public void Set(int seriesId, IEnumerable<string> cleanTitles)
        {
            var titles = cleanTitles.Where(t => t != null).Distinct().ToList();

            lock (_lock)
            {
                RemoveLocked(seriesId);

                if (titles.Count == 0)
                {
                    return;
                }

                _titlesBySeries[seriesId] = titles;

                foreach (var title in titles)
                {
                    if (!_seriesByTitleByLength.TryGetValue(title.Length, out var bucket))
                    {
                        bucket = new Dictionary<string, List<int>>();
                        _seriesByTitleByLength[title.Length] = bucket;
                    }

                    if (!bucket.TryGetValue(title, out var ids))
                    {
                        ids = new List<int>();
                        bucket[title] = ids;
                    }

                    ids.Add(seriesId);
                }
            }
        }

        public void Remove(int seriesId)
        {
            lock (_lock)
            {
                RemoveLocked(seriesId);
            }
        }

        // Series that have exactly this alternate title
        public List<int> FindExact(string cleanTitle)
        {
            lock (_lock)
            {
                var result = new List<int>();

                AddExactLocked(cleanTitle, result);

                return result.Distinct().OrderBy(id => id).ToList();
            }
        }

        // Series with an alternate title that equals this title, or that contains it or is contained in it
        // when the shorter one is at least 5 characters and at least 40% of the length of the longer one.
        public List<int> FindSimilar(string cleanTitle)
        {
            lock (_lock)
            {
                var result = new List<int>();

                AddExactLocked(cleanTitle, result);

                var length = cleanTitle.Length;

                if (length >= 5)
                {
                    // Alternate titles inside this title: only the substrings long enough to qualify are looked up
                    for (var shorter = 5; shorter <= length; shorter++)
                    {
                        if ((double)shorter / length < 0.4 || !_seriesByTitleByLength.TryGetValue(shorter, out var bucket))
                        {
                            continue;
                        }

                        for (var start = 0; start + shorter <= length; start++)
                        {
                            if (bucket.TryGetValue(cleanTitle.Substring(start, shorter), out var ids))
                            {
                                result.AddRange(ids);
                            }
                        }
                    }

                    // Alternate titles that contain this title: only the longer titles that still qualify are scanned
                    foreach (var entry in _seriesByTitleByLength)
                    {
                        if (entry.Key <= length || (double)length / entry.Key < 0.4)
                        {
                            continue;
                        }

                        foreach (var title in entry.Value)
                        {
                            if (title.Key.Contains(cleanTitle))
                            {
                                result.AddRange(title.Value);
                            }
                        }
                    }
                }

                return result.Distinct().OrderBy(id => id).ToList();
            }
        }

        private void AddExactLocked(string cleanTitle, List<int> result)
        {
            if (_seriesByTitleByLength.TryGetValue(cleanTitle.Length, out var bucket) && bucket.TryGetValue(cleanTitle, out var ids))
            {
                result.AddRange(ids);
            }
        }

        private void RemoveLocked(int seriesId)
        {
            if (!_titlesBySeries.TryGetValue(seriesId, out var titles))
            {
                return;
            }

            _titlesBySeries.Remove(seriesId);

            foreach (var title in titles)
            {
                var bucket = _seriesByTitleByLength[title.Length];
                var ids = bucket[title];

                ids.Remove(seriesId);

                if (ids.Count == 0)
                {
                    bucket.Remove(title);
                }

                if (bucket.Count == 0)
                {
                    _seriesByTitleByLength.Remove(title.Length);
                }
            }
        }
    }
}
