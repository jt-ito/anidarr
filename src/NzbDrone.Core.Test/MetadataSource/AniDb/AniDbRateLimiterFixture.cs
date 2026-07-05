using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.AniDb
{
    [TestFixture]
    public class AniDbRateLimiterFixture : TestBase
    {
        private AniDbRateLimiter _rateLimiter;
        private Logger _logger;

        [SetUp]
        public void Setup()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _rateLimiter = new AniDbRateLimiter(_logger);
        }

        [Test]
        public async Task should_space_requests_by_at_least_two_seconds()
        {
            var executionTimes = new List<DateTime>();
            var lockObj = new object();

            Func<int> action = () =>
            {
                lock (lockObj)
                {
                    executionTimes.Add(DateTime.UtcNow);
                }

                return 1;
            };

            var tasks = new List<Task>();

            // Queue 3 background requests
            for (var i = 0; i < 3; i++)
            {
                tasks.Add(_rateLimiter.ExecuteAsync(action));
            }

            await Task.WhenAll(tasks);

            Assert.That(executionTimes.Count, Is.EqualTo(3));

            for (var i = 1; i < executionTimes.Count; i++)
            {
                var diff = executionTimes[i] - executionTimes[i - 1];
                Assert.That(diff.TotalMilliseconds, Is.GreaterThanOrEqualTo(1900)); // allow tiny margin for test execution
            }
        }

        [Test]
        public async Task should_prioritize_manual_requests_over_background()
        {
            var executionOrder = new List<string>();
            var lockObj = new object();

            Func<string> bgAction = () =>
            {
                lock (lockObj)
                {
                    executionOrder.Add("BG");
                }

                return "BG";
            };

            Func<string> manualAction = () =>
            {
                lock (lockObj)
                {
                    executionOrder.Add("MANUAL");
                }

                return "MANUAL";
            };

            // Setup a manual context flag helper
            Task<string> ExecuteWithManualContext(Func<string> action, bool manual)
            {
                // Note: AniDbRateLimiter.IsManualContext is a static AsyncLocal
                AniDbRateLimiter.IsManualContext.Value = manual;
                return _rateLimiter.ExecuteAsync(action);
            }

            var tasks = new List<Task>();

            // Queue a background request that will execute immediately (and block for 2 seconds before the next)
            tasks.Add(ExecuteWithManualContext(bgAction, false));

            // Wait slightly so the first BG gets picked up by the worker
            await Task.Delay(100);

            // Queue another background request
            tasks.Add(ExecuteWithManualContext(bgAction, false));

            // Wait slightly to ensure order in queue
            await Task.Delay(50);

            // Queue a manual request - it should jump ahead of the second background request
            tasks.Add(ExecuteWithManualContext(manualAction, true));

            await Task.WhenAll(tasks);

            Assert.That(executionOrder.Count, Is.EqualTo(3));
            Assert.That(executionOrder[0], Is.EqualTo("BG"));
            Assert.That(executionOrder[1], Is.EqualTo("MANUAL")); // jumped the queue!
            Assert.That(executionOrder[2], Is.EqualTo("BG"));
        }
    }
}
