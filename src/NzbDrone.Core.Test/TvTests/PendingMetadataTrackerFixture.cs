using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class PendingMetadataTrackerFixture
    {
        private PendingMetadataTracker _subject;

        [SetUp]
        public void Setup()
        {
            _subject = new PendingMetadataTracker();
        }

        [Test]
        public void should_report_nothing_when_nothing_is_waiting()
        {
            var progress = _subject.GetProgress(0);

            progress.Pending.Should().Be(0);
            progress.Total.Should().Be(0);
            progress.Done.Should().Be(0);
        }

        [Test]
        public void should_count_how_many_of_the_batch_are_done()
        {
            _subject.Added(60);

            for (var i = 0; i < 12; i++)
            {
                _subject.Completed();
            }

            var progress = _subject.GetProgress(48);

            progress.Pending.Should().Be(48);
            progress.Done.Should().Be(12);
            progress.Total.Should().Be(60);
        }

        [Test]
        public void should_grow_the_batch_when_more_series_are_added_while_it_runs()
        {
            _subject.Added(10);

            for (var i = 0; i < 4; i++)
            {
                _subject.Completed();
            }

            _subject.GetProgress(6).Total.Should().Be(10);

            _subject.Added(5);

            var progress = _subject.GetProgress(11);

            progress.Total.Should().Be(15);
            progress.Done.Should().Be(4);
            progress.Pending.Should().Be(11);
        }

        [Test]
        public void should_estimate_from_what_is_waiting_after_a_restart()
        {
            var progress = _subject.GetProgress(30);

            progress.Total.Should().Be(30);
            progress.Done.Should().Be(0);
            progress.Pending.Should().Be(30);
        }

        [Test]
        public void should_start_the_next_batch_from_zero()
        {
            _subject.Added(3);
            _subject.Completed();
            _subject.Completed();
            _subject.Completed();

            _subject.GetProgress(0).Total.Should().Be(0);

            _subject.Added(2);

            var progress = _subject.GetProgress(2);

            progress.Total.Should().Be(2);
            progress.Done.Should().Be(0);
        }

        [Test]
        public void should_never_report_more_done_than_the_total()
        {
            _subject.Added(2);
            _subject.Completed();
            _subject.Completed();
            _subject.Completed();

            var progress = _subject.GetProgress(1);

            progress.Done.Should().BeLessOrEqualTo(progress.Total);
        }
    }
}
