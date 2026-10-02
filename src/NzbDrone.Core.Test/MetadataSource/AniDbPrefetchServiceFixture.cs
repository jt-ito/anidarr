using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class AniDbPrefetchServiceFixture
    {
        private Mock<IMetadataDispatcher> _dispatcher;
        private Mock<IConfigFileProvider> _config;
        private ConcurrentQueue<int> _fetched;
        private AniDbPrefetchService _subject;

        [SetUp]
        public void Setup()
        {
            _fetched = new ConcurrentQueue<int>();
            _dispatcher = new Mock<IMetadataDispatcher>();
            _config = new Mock<IConfigFileProvider>();
            _config.SetupGet(c => c.IsAniDbClientConfigured).Returns(true);

            _dispatcher.Setup(d => d.GetSeriesInfo(It.IsAny<Series>()))
                       .Callback<Series>(s => _fetched.Enqueue(s.AniDbId.Value))
                       .Returns((Tuple<Series, System.Collections.Generic.List<Episode>>)null);

            _subject = new AniDbPrefetchService(_dispatcher.Object, _config.Object, LogManager.GetCurrentClassLogger());
        }

        private void WaitFor(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();

            while (!condition())
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(10))
                {
                    Assert.Fail("timed out waiting");
                }

                Thread.Sleep(10);
            }
        }

        [Test]
        public void should_fetch_each_series_once_as_an_anidb_series()
        {
            _subject.Enqueue(4337);
            _subject.Enqueue(4337);
            _subject.Enqueue(4738);

            WaitFor(() => _fetched.Count >= 2);
            Thread.Sleep(100);

            _fetched.Should().BeEquivalentTo(new[] { 4337, 4738 });
            _dispatcher.Verify(d => d.GetSeriesInfo(It.Is<Series>(s => s.PrimaryMetadataProvider == "anidb")), Times.Exactly(2));
        }

        [Test]
        public void should_ignore_ids_that_are_not_valid()
        {
            _subject.Enqueue(0);
            _subject.Enqueue(-5);
            Thread.Sleep(100);

            _fetched.Should().BeEmpty();
        }

        [Test]
        public void should_not_call_anidb_when_the_client_name_and_version_are_not_set()
        {
            _config.SetupGet(c => c.IsAniDbClientConfigured).Returns(false);

            _subject.Enqueue(4337);
            Thread.Sleep(200);

            _fetched.Should().BeEmpty();
        }

        [Test]
        public void should_not_call_anidb_while_it_is_rate_limiting_us()
        {
            _config.SetupGet(c => c.AniDbBanExpiration).Returns(DateTime.UtcNow.AddHours(5));

            _subject.Enqueue(4337);
            Thread.Sleep(200);

            _fetched.Should().BeEmpty();
        }

        [Test]
        public void should_stop_and_drop_the_rest_when_anidb_reports_a_ban()
        {
            using var firstCallStarted = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();

            _dispatcher.Setup(d => d.GetSeriesInfo(It.Is<Series>(s => s.AniDbId == 1)))
                       .Callback<Series>(s =>
                       {
                           firstCallStarted.Set();
                           release.Wait(TimeSpan.FromSeconds(10));
                           throw new Exception("AniDB error for ID 1: banned");
                       });

            _subject.Enqueue(1);
            firstCallStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

            // queued while the first fetch is in flight
            _subject.Enqueue(2);
            release.Set();
            Thread.Sleep(300);

            _fetched.Should().BeEmpty();
            _dispatcher.Verify(d => d.GetSeriesInfo(It.Is<Series>(s => s.AniDbId == 2)), Times.Never());
        }

        [Test]
        public void should_accept_the_same_id_again_after_a_ban_dropped_it()
        {
            using var firstCallStarted = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var calls = 0;

            _dispatcher.Setup(d => d.GetSeriesInfo(It.Is<Series>(s => s.AniDbId == 1)))
                       .Callback<Series>(s =>
                       {
                           if (Interlocked.Increment(ref calls) == 1)
                           {
                               firstCallStarted.Set();
                               release.Wait(TimeSpan.FromSeconds(10));
                               throw new Exception("banned");
                           }

                           _fetched.Enqueue(1);
                       });

            _subject.Enqueue(1);
            firstCallStarted.Wait(TimeSpan.FromSeconds(10));
            _subject.Enqueue(2);
            release.Set();
            Thread.Sleep(300);

            // 2 was dropped by the ban, so it can be requested again
            _dispatcher.Setup(d => d.GetSeriesInfo(It.Is<Series>(s => s.AniDbId == 2))).Callback<Series>(s => _fetched.Enqueue(2));
            _subject.Enqueue(2);

            WaitFor(() => _fetched.ToArray().Contains(2));
        }
    }
}
