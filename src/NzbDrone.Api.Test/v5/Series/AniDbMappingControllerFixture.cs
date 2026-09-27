using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using Sonarr.Api.V5.Series;
using Sonarr.Http.REST;

namespace NzbDrone.Api.Test.v5.Series
{
    [TestFixture]
    public class AniDbMappingControllerFixture : TestBase<AniDbMappingController>
    {
        [Test]
        public void CreateMapping_should_reject_anidb_id_already_mapped_to_another_series()
        {
            var resource = new AniDbMappingResource
            {
                SeriesId = 2,
                AniDbId = 100,
                SeasonNumber = 1
            };

            Mocker.GetMock<IAniDbSeriesMappingService>()
                  .Setup(s => s.GetMappingByAniDbId(100))
                  .Returns(new AniDbSeriesMapping { Id = 1, SeriesId = 1, AniDbId = 100, SeasonNumber = 1, RelationType = "Manual" });

            Assert.Throws<BadRequestException>(() => Subject.CreateMapping(resource));

            Mocker.GetMock<IAniDbSeriesMappingService>()
                  .Verify(s => s.UpdateMappings(It.IsAny<int>(), It.IsAny<System.Collections.Generic.List<AniDbSeriesMapping>>()), Times.Never());
        }

        [Test]
        public void CreateMapping_should_succeed_when_anidb_id_is_not_already_mapped()
        {
            var resource = new AniDbMappingResource
            {
                SeriesId = 2,
                AniDbId = 200,
                SeasonNumber = 1
            };

            Mocker.GetMock<IAniDbSeriesMappingService>()
                  .Setup(s => s.GetMappingByAniDbId(200))
                  .Returns((AniDbSeriesMapping)null);

            Mocker.GetMock<IAniDbSeriesMappingService>()
                  .Setup(s => s.GetMappingsForSeries(2))
                  .Returns(new System.Collections.Generic.List<AniDbSeriesMapping>());

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(2))
                  .Returns(Builder<NzbDrone.Core.Tv.Series>.CreateNew().With(x => x.Id = 2).Build());

            var result = Subject.CreateMapping(resource);

            result.Should().NotBeNull();

            Mocker.GetMock<IAniDbSeriesMappingService>()
                  .Verify(s => s.UpdateMappings(2, It.Is<System.Collections.Generic.List<AniDbSeriesMapping>>(m => m.Count == 1 && m[0].AniDbId == 200)), Times.Once());
        }
    }
}
