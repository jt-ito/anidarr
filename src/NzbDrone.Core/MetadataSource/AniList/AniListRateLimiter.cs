using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public interface IAniListRateLimiter
    {
        Task<T> ExecuteAsync<T>(Func<T> action);
        void SetRetryAfter(TimeSpan delay);
        void RecordFailure(TimeSpan? explicitDelay = null);
        void RecordSuccess();
        bool IsRateLimited { get; }
        DateTime RetryAfterUtc { get; }
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
        private static int _consecutiveFailures;
        private static Logger _staticLogger = LogManager.GetCurrentClassLogger();

        static AniListRateLimiter()
        {
            _workerTask = Task.Run(ProcessQueueAsync);
        }

        public AniListRateLimiter(Logger logger)
        {
            _logger = logger;
        }

        public bool IsRateLimited
        {
            get
            {
                lock (_lock)
                {
                    return DateTime.UtcNow < _retryAfterTime;
                }
            }
        }

        public DateTime RetryAfterUtc
        {
            get
            {
                lock (_lock)
                {
                    return _retryAfterTime;
                }
            }
        }

        public void RecordFailure(TimeSpan? explicitDelay = null)
        {
            lock (_lock)
            {
                _consecutiveFailures++;
                var backoffMinutes = _consecutiveFailures switch
                {
                    1 => 2,
                    2 => 5,
                    3 => 15,
                    _ => 30
                };

                var delay = TimeSpan.FromMinutes(backoffMinutes);
                if (explicitDelay.HasValue && explicitDelay.Value > delay)
                {
                    delay = explicitDelay.Value;
                }

                var newRetryTime = DateTime.UtcNow + delay;
                if (newRetryTime > _retryAfterTime)
                {
                    _retryAfterTime = newRetryTime;
                }

                _staticLogger.Warn(
                    "AniList API circuit breaker active: consecutive failures={0}, backing off until {1:u} UTC ({2} min)",
                    _consecutiveFailures,
                    _retryAfterTime,
                    (int)delay.TotalMinutes);
            }
        }

        public void RecordSuccess()
        {
            lock (_lock)
            {
                _consecutiveFailures = 0;
            }
        }

        public void SetRetryAfter(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                RecordFailure();
                return;
            }

            RecordFailure(delay);
        }

        public Task<T> ExecuteAsync<T>(Func<T> action)
        {
            lock (_lock)
            {
                if (DateTime.UtcNow < _retryAfterTime)
                {
                    var tcsFast = new TaskCompletionSource<T>();
                    tcsFast.SetException(new HttpException(new HttpRequest("https://graphql.anilist.co"), null, $"AniList API is currently unavailable (circuit breaker active until {_retryAfterTime:u} UTC)"));
                    return tcsFast.Task;
                }
            }

            var tcs = new TaskCompletionSource<T>();

            Func<Task> wrappedAction = () =>
            {
                try
                {
                    var result = action();
                    RecordSuccess();
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
