using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.SeriesStats;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using Sonarr.Api.V5.Series;

namespace NzbDrone.Api.Test.v5.Series
{
    [TestFixture]
    public class SeriesControllerFixture : TestBase<SeriesController>
    {
        private NzbDrone.Core.Tv.Series _series;
        private SeriesResource _seriesResource;

        [SetUp]
        public void Setup()
        {
            _series = Builder<NzbDrone.Core.Tv.Series>.CreateNew()
                            .With(s => s.Id = 1)
                            .With(s => s.Path = @"C:\Test\OldPath\Series")
                            .Build();

            _seriesResource = new SeriesResource
            {
                Id = 1,
                Path = @"C:\Test\NewPath\Series",
                RootFolderPath = @"C:\Test\NewPath",
                Title = "Test Series"
            };

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(1))
                  .Returns(_series);

            Mocker.GetMock<ISeriesStatisticsService>()
                  .Setup(s => s.SeriesStatistics(It.IsAny<int>(), It.IsAny<int>()))
                  .Returns(new SeriesStatistics());

            var mockUrlHelper = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
            mockUrlHelper.Setup(x => x.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>())).Returns("http://localhost");
            Subject.Url = mockUrlHelper.Object;
        }

        [Test]
        public void UpdateSeries_should_save_to_database_before_executing_hardlink()
        {
            _seriesResource.RootFolderAction = RootFolderAction.HardlinkToNew;

            var hardlinkResult = new HardlinkResult();

            // We want to verify that when HardlinkSeries is called, UpdateSeries has already been called
            var updateCalled = false;

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.UpdateSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Callback<NzbDrone.Core.Tv.Series, bool, bool>((s, _, __) => updateCalled = true)
                  .Returns(_series);

            Mocker.GetMock<IHardlinkSeriesFiles>()
                  .Setup(s => s.HardlinkSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), @"C:\Test\OldPath\Series"))
                  .Callback<NzbDrone.Core.Tv.Series, string>((s, oldPath) =>
                  {
                      updateCalled.Should().BeTrue("UpdateSeries must be called before HardlinkSeries");
                  })
                  .Returns(hardlinkResult);

            Subject.UpdateSeries(_seriesResource);

            Mocker.GetMock<ISeriesService>().Verify(v => v.UpdateSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once());
            Mocker.GetMock<IHardlinkSeriesFiles>().Verify(v => v.HardlinkSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), @"C:\Test\OldPath\Series"), Times.Once());
        }

        [Test]
        public void UpdateSeries_should_not_trigger_hardlink_for_path_update_only()
        {
            _seriesResource.RootFolderAction = RootFolderAction.PathUpdateOnly;

            Subject.UpdateSeries(_seriesResource);

            Mocker.GetMock<ISeriesService>().Verify(v => v.UpdateSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once());
            Mocker.GetMock<IHardlinkSeriesFiles>().Verify(v => v.HardlinkSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void UpdateSeries_should_not_trigger_hardlink_if_path_did_not_change()
        {
            // Set resource path to same as series path
            _seriesResource.Path = @"C:\Test\OldPath\Series";
            _seriesResource.RootFolderAction = RootFolderAction.HardlinkToNew;

            Subject.UpdateSeries(_seriesResource);

            Mocker.GetMock<ISeriesService>().Verify(v => v.UpdateSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once());
            Mocker.GetMock<IHardlinkSeriesFiles>().Verify(v => v.HardlinkSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), It.IsAny<string>()), Times.Never());
        }
    }
}
