using System;

namespace NzbDrone.Core.Tv
{
    public class PendingMetadataProgress
    {
        // AniDB series still waiting for their hub, seasons and episodes
        public int Pending { get; set; }

        // Finished since this batch started
        public int Done { get; set; }

        // Pending plus done: the size of the batch
        public int Total { get; set; }
    }

    public interface IPendingMetadataTracker
    {
        // Series were added without their metadata
        void Added(int count);

        // A series that was waiting got its metadata (or was merged into its hub)
        void Completed();

        // `pendingInLibrary` is what the database says is still waiting
        PendingMetadataProgress GetProgress(int pendingInLibrary);
    }

    /// <summary>
    /// Counts how far a batch of series added without their metadata has got, so the library can show
    /// "12 of 60 updated". The database only knows what is still waiting; this remembers how many there
    /// were and how many have finished. The batch ends (and the counts reset) when nothing is waiting.
    /// </summary>
    public class PendingMetadataTracker : IPendingMetadataTracker
    {
        private readonly object _lock = new object();
        private int _total;
        private int _done;

        public void Added(int count)
        {
            lock (_lock)
            {
                _total += Math.Max(0, count);
            }
        }

        public void Completed()
        {
            lock (_lock)
            {
                _done++;
            }
        }

        public PendingMetadataProgress GetProgress(int pendingInLibrary)
        {
            lock (_lock)
            {
                if (pendingInLibrary <= 0)
                {
                    _total = 0;
                    _done = 0;

                    return new PendingMetadataProgress();
                }

                // After a restart the counts are lost: what is still waiting is the best estimate left
                _total = Math.Max(_total, _done + pendingInLibrary);

                return new PendingMetadataProgress
                {
                    Pending = pendingInLibrary,
                    Done = Math.Min(_done, _total),
                    Total = _total
                };
            }
        }
    }
}
