using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class ImportJobServiceFixture
    {
        private Mock<IAddSeriesService> _addSeriesService;
        private ImportJobService _subject;

        [SetUp]
        public void Setup()
        {
            _addSeriesService = new Mock<IAddSeriesService>();
            _subject = new ImportJobService(_addSeriesService.Object, LogManager.GetCurrentClassLogger());
        }

        private static Series Show(string title)
        {
            return new Series { Title = title, Path = "/lib/" + title };
        }

        private void WaitUntilFinished()
        {
            var timer = Stopwatch.StartNew();

            while (_subject.GetStatus(0, 0).IsRunning)
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(10))
                {
                    Assert.Fail("import job did not finish");
                }

                Thread.Sleep(10);
            }
        }

        private void GivenOutcome(string title, Series added, string reason = null)
        {
            _addSeriesService.Setup(a => a.AddSeriesBackground(It.Is<List<Series>>(l => l[0].Title == title), It.IsAny<bool>()))
                             .Returns<List<Series>, bool>((l, d) => new List<AddSeriesOutcome> { new AddSeriesOutcome { Requested = l[0], Added = added, Reason = reason } });
        }

        [Test]
        public void should_report_what_happened_to_every_series_in_order()
        {
            GivenOutcome("A", new Series { Id = 1, Title = "A" });
            GivenOutcome("B", null, "Already in your library");
            _addSeriesService.Setup(a => a.AddSeriesBackground(It.Is<List<Series>>(l => l[0].Title == "C"), It.IsAny<bool>()))
                             .Throws(new InvalidOperationException("boom"));

            _subject.Enqueue(new List<Series> { Show("A"), Show("B"), Show("C") }, false);
            WaitUntilFinished();

            var status = _subject.GetStatus(0, 0);

            status.Total.Should().Be(3);
            status.Done.Should().Be(3);
            status.Items.Select(i => i.State).Should().Equal(ImportItemState.Added, ImportItemState.Skipped, ImportItemState.Failed);
            status.Items[0].Series.Id.Should().Be(1);
            status.Items[1].Message.Should().Be("Already in your library");
            status.Items[2].Message.Should().Be("boom");
            status.Items.Select(i => i.Path).Should().Equal("/lib/A", "/lib/B", "/lib/C");
        }

        [Test]
        public void should_only_return_what_is_new_since_the_cursor()
        {
            GivenOutcome("A", new Series { Id = 1, Title = "A" });
            GivenOutcome("B", new Series { Id = 2, Title = "B" });

            _subject.Enqueue(new List<Series> { Show("A"), Show("B") }, false);
            WaitUntilFinished();

            var first = _subject.GetStatus(0, 0);
            var generation = first.Generation;

            _subject.GetStatus(first.Next, generation).Items.Should().BeEmpty();
            _subject.GetStatus(1, generation).Items.Select(i => i.Title).Should().Equal("B");
        }

        [Test]
        public void should_start_over_for_a_client_holding_an_older_generation()
        {
            GivenOutcome("A", new Series { Id = 1, Title = "A" });

            _subject.Enqueue(new List<Series> { Show("A") }, false);
            WaitUntilFinished();

            _subject.GetStatus(1, 999).Items.Should().ContainSingle();
        }

        [Test]
        public void should_pass_the_defer_flag_through()
        {
            GivenOutcome("A", new Series { Id = 1, Title = "A" });

            _subject.Enqueue(new List<Series> { Show("A") }, true);
            WaitUntilFinished();

            _addSeriesService.Verify(a => a.AddSeriesBackground(It.IsAny<List<Series>>(), true), Times.Once());
        }

        [Test]
        public void should_append_a_second_batch_to_the_same_list()
        {
            GivenOutcome("A", new Series { Id = 1, Title = "A" });
            GivenOutcome("B", new Series { Id = 2, Title = "B" });

            _subject.Enqueue(new List<Series> { Show("A") }, false);
            WaitUntilFinished();
            var generation = _subject.GetStatus(0, 0).Generation;

            _subject.Enqueue(new List<Series> { Show("B") }, false);
            WaitUntilFinished();

            var status = _subject.GetStatus(1, generation);

            status.Items.Should().ContainSingle().Which.Title.Should().Be("B");
            status.Total.Should().Be(2);
        }

        [Test]
        public void should_report_not_running_when_nothing_was_queued()
        {
            _subject.GetStatus(0, 0).IsRunning.Should().BeFalse();
        }
    }
}
