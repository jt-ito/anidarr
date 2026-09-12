using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class AniDbRelatedSeriesServiceFixture : CoreTest<AniDbRelatedSeriesService>
    {
        [Test]
        public void should_reset_id_to_zero_when_inserting_existing_models()
        {
            var relations = new List<AniDbRelatedSeries>
            {
                new AniDbRelatedSeries { Id = 1, SeriesId = 10, RelatedAniDbId = 100, RelationType = "Sequel" },
                new AniDbRelatedSeries { Id = 2, SeriesId = 10, RelatedAniDbId = 101, RelationType = "Prequel" }
            };

            var inserted = new List<AniDbRelatedSeries>();
            Mocker.GetMock<IAniDbRelatedSeriesRepository>()
                .Setup(r => r.Insert(It.IsAny<AniDbRelatedSeries>()))
                .Callback<AniDbRelatedSeries>(item =>
                {
                    // If Id was not reset to 0, BasicRepository would throw InvalidOperationException
                    item.Id.Should().Be(0);
                    inserted.Add(item);
                });

            Subject.UpdateRelatedSeries(20, relations);

            Mocker.GetMock<IAniDbRelatedSeriesRepository>()
                .Verify(r => r.DeleteBySeriesId(20), Times.Once);

            inserted.Should().HaveCount(2);
            inserted.Should().OnlyContain(r => r.SeriesId == 20 && r.Id == 0);
        }
    }
}
