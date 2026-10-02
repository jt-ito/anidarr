using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class SeriesAddedHandlerFixture : CoreTest<SeriesAddedHandler>
    {
        private Series _series;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                .With(s => s.Id = 12)
                .With(s => s.LastInfoSync = DateTime.UtcNow)
                .Build();
        }

        [Test]
        public void should_push_refresh_series_command_with_high_priority_on_series_added()
        {
            Subject.Handle(new SeriesAddedEvent(_series));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(
                      c => c.Push(
                          It.Is<RefreshSeriesCommand>(cmd => cmd.SeriesIds.Contains(_series.Id) && cmd.IsNewSeries),
                          CommandPriority.High,
                          CommandTrigger.Manual),
                      Times.Once);
        }

        [Test]
        public void should_push_a_series_added_without_its_metadata_at_low_priority()
        {
            _series.LastInfoSync = null;

            Subject.Handle(new SeriesAddedEvent(_series));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(
                      c => c.Push(
                          It.Is<RefreshSeriesCommand>(cmd => cmd.SeriesIds.Contains(_series.Id) && cmd.IsNewSeries),
                          CommandPriority.Low,
                          CommandTrigger.Unspecified),
                      Times.Once);

            Mocker.GetMock<IPendingMetadataTracker>().Verify(t => t.Added(1), Times.Once);
        }

        [Test]
        public void should_split_an_import_by_whether_the_series_still_needs_its_metadata()
        {
            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<Series>
                           {
                               new Series { Id = 10, LastInfoSync = DateTime.UtcNow },
                               new Series { Id = 20, LastInfoSync = null }
                           });

            Subject.Handle(new SeriesImportedEvent(new List<int> { 10, 20 }));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.PushMany(It.Is<List<RefreshSeriesCommand>>(l => l.Count == 1 && l[0].SeriesIds.Contains(10)), CommandPriority.High, CommandTrigger.Manual), Times.Once);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.PushMany(It.Is<List<RefreshSeriesCommand>>(l => l.Count == 1 && l[0].SeriesIds.Contains(20)), CommandPriority.Low, CommandTrigger.Unspecified), Times.Once);

            // only the one still waiting for its metadata is counted
            Mocker.GetMock<IPendingMetadataTracker>().Verify(t => t.Added(1), Times.Once);
        }

        [Test]
        public void should_not_count_series_that_already_have_their_metadata()
        {
            Subject.Handle(new SeriesAddedEvent(_series));

            Mocker.GetMock<IPendingMetadataTracker>().Verify(t => t.Added(It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void should_push_many_refresh_series_commands_with_high_priority_on_series_imported()
        {
            var seriesIds = new List<int> { 10, 20 };

            Subject.Handle(new SeriesImportedEvent(seriesIds));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(
                      c => c.PushMany(
                          It.Is<List<RefreshSeriesCommand>>(list => list.Count == 2),
                          CommandPriority.High,
                          CommandTrigger.Manual),
                      Times.Once);
        }
    }
}
