using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
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

        [Test]
        public void AllSeries_should_batch_load_offline_titles_mappings_and_related_series()
        {
            var series1 = Builder<NzbDrone.Core.Tv.Series>.CreateNew()
                .With(s => s.Id = 1)
                .With(s => s.AniDbId = 100)
                .With(s => s.Title = "Series 100")
                .With(s => s.Path = @"C:\Test\Series1")
                .Build();

            var series2 = Builder<NzbDrone.Core.Tv.Series>.CreateNew()
                .With(s => s.Id = 2)
                .With(s => s.AniDbId = 200)
                .With(s => s.Title = "Series 200")
                .With(s => s.Path = @"C:\Test\Series2")
                .Build();

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<NzbDrone.Core.Tv.Series> { series1, series2 });

            Mocker.GetMock<ISeriesStatisticsService>()
                  .Setup(s => s.SeriesStatistics())
                  .Returns(new List<SeriesStatistics>());

            var offlineTitle1 = new AnimeOfflineTitle
            {
                AniDbId = 100,
                SearchSynonyms = new List<string> { "Synonym 100", "duplicate title" }
            };

            var offlineTitle2 = new AnimeOfflineTitle
            {
                AniDbId = 200,
                SearchSynonyms = new List<string> { "Synonym 200" }
            };

            Mocker.GetMock<IAnimeOfflineTitleRepository>()
                  .Setup(r => r.FindByAniDbIds(It.IsAny<IEnumerable<int>>()))
                  .Returns(new Dictionary<int, AnimeOfflineTitle>
                  {
                      { 100, offlineTitle1 },
                      { 200, offlineTitle2 }
                  });

            var mappings = new List<AniDbSeriesMapping>
            {
                new AniDbSeriesMapping { Id = 1, SeriesId = 1, AniDbId = 100, SeasonNumber = 1, RelationType = "main" }
            };

            Mocker.GetMock<IAniDbSeriesMappingService>()
                  .Setup(s => s.GetAllMappings())
                  .Returns(mappings);

            var related = new List<AniDbRelatedSeries>
            {
                new AniDbRelatedSeries { Id = 1, SeriesId = 1, RelatedAniDbId = 200, RelationType = "sequel" }
            };

            Mocker.GetMock<IAniDbRelatedSeriesService>()
                  .Setup(s => s.GetAllRelatedSeries())
                  .Returns(related);

            var cache = new List<AniDbRelatedMetadataCache>
            {
                new AniDbRelatedMetadataCache { AniDbId = 200, Title = "Series 200 Sequel" }
            };

            Mocker.GetMock<IAniDbRelatedMetadataCacheRepository>()
                  .Setup(r => r.GetByAniDbIds(It.IsAny<List<int>>()))
                  .Returns(cache);

            var result = Subject.AllSeries(null);

            result.Should().NotBeNull();
            result.Value.Should().HaveCount(2);

            // Verify single batch calls were made instead of per-series N+1 loops
            Mocker.GetMock<ISeriesService>().Verify(s => s.GetAllSeries(), Times.Once());
            Mocker.GetMock<IAnimeOfflineTitleRepository>().Verify(r => r.FindByAniDbIds(It.IsAny<IEnumerable<int>>()), Times.Once());
            Mocker.GetMock<IAnimeOfflineTitleRepository>().Verify(r => r.FindByAniDbId(It.IsAny<int>()), Times.Never());

            Mocker.GetMock<IAniDbSeriesMappingService>().Verify(s => s.GetAllMappings(), Times.Once());
            Mocker.GetMock<IAniDbSeriesMappingService>().Verify(s => s.GetMappingsForSeries(It.IsAny<int>()), Times.Never());

            Mocker.GetMock<IAniDbRelatedSeriesService>().Verify(s => s.GetAllRelatedSeries(), Times.Once());
            Mocker.GetMock<IAniDbRelatedSeriesService>().Verify(s => s.GetRelatedSeries(It.IsAny<int>()), Times.Never());

            // Check populated items
            var res1 = result.Value.First(r => r.Id == 1);
            res1.AlternateTitles.Should().Contain(a => a.Title == "Synonym 100");
            res1.AniDbMappings.Should().HaveCount(1);
            res1.AniDbRelatedSeries.Should().HaveCount(1);
            res1.AniDbRelatedSeries[0].Title.Should().Be("Series 200 Sequel");
        }

        [Test]
        public void AllSeries_should_not_fetch_series_twice_when_no_related_series_exist()
        {
            var series1 = Builder<NzbDrone.Core.Tv.Series>.CreateNew()
                .With(s => s.Id = 1)
                .With(s => s.Path = @"C:\Test\Series1")
                .Build();

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<NzbDrone.Core.Tv.Series> { series1 });

            Mocker.GetMock<ISeriesStatisticsService>()
                  .Setup(s => s.SeriesStatistics())
                  .Returns(new List<SeriesStatistics>());

            Mocker.GetMock<IAnimeOfflineTitleRepository>()
                  .Setup(r => r.FindByAniDbIds(It.IsAny<IEnumerable<int>>()))
                  .Returns(new Dictionary<int, AnimeOfflineTitle>());

            Mocker.GetMock<IAniDbSeriesMappingService>()
                  .Setup(s => s.GetAllMappings())
                  .Returns(new List<AniDbSeriesMapping>());

            // No related series configured for this library — the common case.
            Mocker.GetMock<IAniDbRelatedSeriesService>()
                  .Setup(s => s.GetAllRelatedSeries())
                  .Returns(new List<AniDbRelatedSeries>());

            var result = Subject.AllSeries(null);

            result.Should().NotBeNull();
            result.Value.Should().HaveCount(1);

            // GetAllSeries should only be called once (from AllSeries itself), and
            // never a second time from inside PopulateAniDbRelatedSeries now that
            // it bails out before doing any full-library work.
            Mocker.GetMock<ISeriesService>().Verify(s => s.GetAllSeries(), Times.Once());
            Mocker.GetMock<IAniDbRelatedMetadataCacheRepository>().Verify(r => r.GetByAniDbIds(It.IsAny<List<int>>()), Times.Never());
        }
    }
}
