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
