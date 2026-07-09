using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public interface IAniListRateLimiter
    {
        Task<T> ExecuteAsync<T>(Func<T> action);
        void SetRetryAfter(TimeSpan delay);
    }

    public class AniListRateLimiter : IAniListRateLimiter
    {
        private readonly Logger _logger;

        public static readonly AsyncLocal<bool> IsManualContext = new AsyncLocal<bool>();

        private static readonly PriorityQueue<Func<Task>, int> _queue = new PriorityQueue<Func<Task>, int>();
        private static readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private static readonly object _lock = new object();
        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private static readonly Task _workerTask;

        // 90 requests per minute = 1.5 req/sec. We use 1000ms (60/min) for safety.
        private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(1000);
        private static DateTime _lastRequestTime = DateTime.MinValue;
        private static DateTime _retryAfterTime = DateTime.MinValue;
        private static Logger _staticLogger = LogManager.GetCurrentClassLogger();

        static AniListRateLimiter()
        {
            _workerTask = Task.Run(ProcessQueueAsync);
        }

        public AniListRateLimiter(Logger logger)
        {
            _logger = logger;
        }

        public void SetRetryAfter(TimeSpan delay)
        {
            var newRetryTime = DateTime.UtcNow + delay;
            lock (_lock)
            {
                if (newRetryTime > _retryAfterTime)
                {
                    _retryAfterTime = newRetryTime;
                }
            }
        }

        public Task<T> ExecuteAsync<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>();

            Func<Task> wrappedAction = () =>
            {
                try
                {
                    var result = action();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }

                return Task.CompletedTask;
            };

            lock (_lock)
            {
                // Priority 0 for manual (high priority), 1 for background (low priority)
                _queue.Enqueue(wrappedAction, IsManualContext.Value ? 0 : 1);
            }

            _signal.Release();

            return tcs.Task;
        }

        private static async Task ProcessQueueAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(_cts.Token);

                    var now = DateTime.UtcNow;

                    // 1. Check Retry-After from 429
                    DateTime retryTime;
                    lock (_lock)
                    {
                        retryTime = _retryAfterTime;
                    }

                    if (now < retryTime)
                    {
                        var retryDelay = retryTime - now;
                        _staticLogger.Debug("AniList rate limiter backing off for Retry-After: sleeping {0}ms", retryDelay.TotalMilliseconds);
                        await Task.Delay(retryDelay, _cts.Token);
                        now = DateTime.UtcNow; // update now
                    }

                    // 2. Check global rate limit interval
                    var elapsed = now - _lastRequestTime;
                    if (elapsed < MinRequestInterval)
                    {
                        var delay = MinRequestInterval - elapsed;
                        _staticLogger.Debug("AniList global rate limiter: sleeping {0}ms", delay.TotalMilliseconds);
                        await Task.Delay(delay, _cts.Token);
                    }

                    Func<Task> nextAction;
                    lock (_lock)
                    {
                        if (!_queue.TryDequeue(out nextAction, out _))
                        {
                            continue;
                        }
                    }

                    // Record the start time of the request
                    _lastRequestTime = DateTime.UtcNow;

                    _staticLogger.Trace("Executing AniList request from rate limiter queue");
                    await nextAction();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _staticLogger.Error(ex, "Error in AniList rate limiter background queue");
                }
            }
        }
    }
}
