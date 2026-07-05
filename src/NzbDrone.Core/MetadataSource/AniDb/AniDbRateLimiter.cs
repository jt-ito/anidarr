using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    public interface IAniDbRateLimiter
    {
        Task<T> ExecuteAsync<T>(Func<T> action);
    }

    public class AniDbRateLimiter : IAniDbRateLimiter
    {
        private readonly Logger _logger;

        public static readonly AsyncLocal<bool> IsManualContext = new AsyncLocal<bool>();

        private static readonly PriorityQueue<Func<Task>, int> _queue = new PriorityQueue<Func<Task>, int>();
        private static readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private static readonly object _lock = new object();
        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private static readonly Task _workerTask;

        private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(2);
        private static DateTime _lastRequestTime = DateTime.MinValue;
        private static Logger _staticLogger = LogManager.GetCurrentClassLogger();

        static AniDbRateLimiter()
        {
            _workerTask = Task.Run(ProcessQueueAsync);
        }

        public AniDbRateLimiter(Logger logger)
        {
            _logger = logger;
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

                    // Enforce rate limit globally BEFORE dequeuing so higher priority items can jump the queue
                    var now = DateTime.UtcNow;
                    var elapsed = now - _lastRequestTime;
                    if (elapsed < MinRequestInterval)
                    {
                        var delay = MinRequestInterval - elapsed;
                        _staticLogger.Debug("AniDB global rate limiter: sleeping {0}ms", delay.TotalMilliseconds);
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

                    _staticLogger.Trace("Executing AniDB request from rate limiter queue");
                    await nextAction();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _staticLogger.Error(ex, "Error in AniDB rate limiter background queue");
                }
            }
        }
    }
}
