using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class AniDbSeriesMappingServiceFixture : CoreTest<AniDbSeriesMappingService>
    {
        [Test]
        public void should_insert_mappings_as_new_rows_even_when_they_carry_an_id_from_an_earlier_add()
        {
            var insertedIds = new List<int>();

            Mocker.GetMock<IAniDbSeriesMappingRepository>()
                  .Setup(r => r.Insert(It.IsAny<AniDbSeriesMapping>()))
                  .Callback<AniDbSeriesMapping>(m => insertedIds.Add(m.Id))
                  .Returns<AniDbSeriesMapping>(m => m);

            var mappings = new List<AniDbSeriesMapping>
            {
                new AniDbSeriesMapping { Id = 41, AniDbId = 20442 },
                new AniDbSeriesMapping { Id = 42, AniDbId = 20443 }
            };

            Subject.UpdateMappings(7, mappings);

            Assert.That(insertedIds, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(mappings, Has.All.Matches<AniDbSeriesMapping>(m => m.SeriesId == 7));
            Mocker.GetMock<IAniDbSeriesMappingRepository>().Verify(r => r.DeleteBySeriesId(7), Times.Once());
        }
    }
}
