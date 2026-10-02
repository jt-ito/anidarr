using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Tv
{
    public enum ImportItemState
    {
        Queued,
        Added,

        // Not added because there was nothing to add (already in the library, a duplicate in this import)
        Skipped,

        // Could not be added
        Failed
    }

    public class ImportJobItem
    {
        public int Index { get; set; }

        public string Path { get; set; }

        public string Title { get; set; }

        public ImportItemState State { get; set; }

        public string Message { get; set; }

        // The saved series (when Added)
        public Series Series { get; set; }
    }

    public class ImportJobStatus
    {
        // Changes when finished items were dropped; a client holding an older cursor starts over
        public int Generation { get; set; }

        public int Total { get; set; }

        public int Done { get; set; }

        public bool IsRunning { get; set; }

        // Cursor for the next call: pass it back as `after`
        public int Next { get; set; }

        // Finished items from the cursor on
        public List<ImportJobItem> Items { get; set; } = new List<ImportJobItem>();
    }

    public interface IImportJobService
    {
        void Enqueue(List<Series> series, bool deferMetadata);

        ImportJobStatus GetStatus(int after, int generation);
    }

    /// <summary>
    /// Adds the series of a library import one at a time in the background, so a large import is not one
    /// request that stays open for as long as AniDB takes. Progress is polled; every series reports what
    /// happened to it.
    /// </summary>
    public class ImportJobService : IImportJobService
    {
        private const int MaxRetainedItems = 5000;

        private readonly IAddSeriesService _addSeriesService;
        private readonly Logger _logger;

        private readonly object _lock = new object();
        private readonly List<ImportJobItem> _items = new List<ImportJobItem>();
        private readonly Queue<(ImportJobItem Item, Series Series, bool Defer)> _work = new Queue<(ImportJobItem Item, Series Series, bool Defer)>();
        private int _generation = 1;
        private int _done;
        private bool _isRunning;

        public ImportJobService(IAddSeriesService addSeriesService, Logger logger)
        {
            _addSeriesService = addSeriesService;
            _logger = logger;
        }

        public void Enqueue(List<Series> series, bool deferMetadata)
        {
            lock (_lock)
            {
                // Everything finished and a lot retained: start a fresh list
                if (!_isRunning && _done == _items.Count && _items.Count > MaxRetainedItems)
                {
                    _items.Clear();
                    _done = 0;
                    _generation++;
                }

                foreach (var s in series)
                {
                    var item = new ImportJobItem
                    {
                        Index = _items.Count,
                        Path = s.Path,
                        Title = s.Title,
                        State = ImportItemState.Queued
                    };

                    _items.Add(item);
                    _work.Enqueue((item, s, deferMetadata));
                }

                if (_isRunning)
                {
                    return;
                }

                _isRunning = true;
            }

            Task.Run(Work);
        }

        public ImportJobStatus GetStatus(int after, int generation)
        {
            lock (_lock)
            {
                var start = generation == _generation ? Math.Max(0, after) : 0;

                return new ImportJobStatus
                {
                    Generation = _generation,
                    Total = _items.Count,
                    Done = _done,
                    IsRunning = _isRunning || _done < _items.Count,
                    Next = _done,
                    Items = _items.Skip(start).Take(Math.Max(0, _done - start)).ToList()
                };
            }
        }

        private void Work()
        {
            while (true)
            {
                (ImportJobItem Item, Series Series, bool Defer) next;

                lock (_lock)
                {
                    if (_work.Count == 0)
                    {
                        _isRunning = false;
                        return;
                    }

                    next = _work.Dequeue();
                }

                var (state, message, added) = Process(next.Series, next.Defer);

                lock (_lock)
                {
                    next.Item.State = state;
                    next.Item.Message = message;
                    next.Item.Series = added;
                    _done++;
                }
            }
        }

        private (ImportItemState State, string Message, Series Added) Process(Series series, bool deferMetadata)
        {
            try
            {
                var outcome = _addSeriesService.AddSeriesBackground(new List<Series> { series }, deferMetadata).Single();

                return outcome.Added != null
                    ? (ImportItemState.Added, null, outcome.Added)
                    : (ImportItemState.Skipped, outcome.Reason, null);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Import of {0} failed", series);

                return (ImportItemState.Failed, ex.Message, null);
            }
        }
    }
}
