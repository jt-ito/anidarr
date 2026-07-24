using System;
using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class AddSeriesFixture : CoreTest<AddSeriesService>
    {
        private Series _fakeSeries;

        [SetUp]
        public void Setup()
        {
            _fakeSeries = Builder<Series>
                .CreateNew()
                .With(s => s.Path = null)
                .Build();

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<Series>());
        }

        private void GivenValidSeries(int tvdbId)
        {
            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(s => s.GetSeriesInfo(It.IsAny<Series>()))
                  .Returns(new Tuple<Series, List<Episode>>(_fakeSeries, new List<Episode>()));
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetSeriesFolder(It.IsAny<Series>(), null))
                  .Returns<Series, NamingConfig>((c, n) => c.Title);

            Mocker.GetMock<IAddSeriesValidator>()
                  .Setup(s => s.Validate(It.IsAny<Series>()))
                  .Returns(new ValidationResult());
        }

        [Test]
        public void should_be_able_to_add_a_series_without_passing_in_title()
        {
            var newSeries = new Series
            {
                TvdbId = 1,
                RootFolderPath = @"C:\Test\TV"
            };

            GivenValidSeries(newSeries.TvdbId);
            GivenValidPath();

            var series = Subject.AddSeries(newSeries);

            series.Title.Should().Be(_fakeSeries.Title);
        }

        [Test]
        public void should_have_proper_path()
        {
            var newSeries = new Series
            {
                TvdbId = 1,
                RootFolderPath = @"C:\Test\TV"
            };

            GivenValidSeries(newSeries.TvdbId);
            GivenValidPath();

            var series = Subject.AddSeries(newSeries);

            series.Path.Should().Be(Path.Combine(newSeries.RootFolderPath, _fakeSeries.Title));
        }

        [TestCase(1, 1)] // Adding Season 1 (hub root)
        [TestCase(2, 1)] // Adding Season 2
        [TestCase(3, 1)] // Adding Season 3
        public void should_preserve_anidb_mappings_for_all_hub_seasons(int seasonAniDbId, int expectedHubAniDbId)
        {
            // Simulate that the user clicked to add ANY season in the hub, meaning newSeries has the AniDbId of that season.
            var newSeries = new Series
            {
                AniDbId = seasonAniDbId,
                PrimaryMetadataProvider = "anidb",
                RootFolderPath = @"C:\Test\TV"
            };

            // Simulate the MetadataDispatcher correctly fetching the full hub and generating AniDbMappings for all seasons.
            var expectedMappings = new List<AniDbSeriesMapping>
            {
                new AniDbSeriesMapping { AniDbId = 1, SeasonNumber = 1, RelationType = "Same" },
                new AniDbSeriesMapping { AniDbId = 2, SeasonNumber = 2, RelationType = "Sequel" },
                new AniDbSeriesMapping { AniDbId = 3, SeasonNumber = 3, RelationType = "Sequel" }
            };

            var hubSeries = Builder<Series>.CreateNew()
                .With(s => s.AniDbId = expectedHubAniDbId)
                .With(s => s.AniDbMappings = expectedMappings)
                .Build();

            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(s => s.GetSeriesInfo(It.IsAny<Series>()))
                  .Returns(new Tuple<Series, List<Episode>>(hubSeries, new List<Episode>()));

            GivenValidPath();

            var series = Subject.AddSeries(newSeries);

            // Assert that the AniDbMappings were correctly preserved during AddSeries (specifically during ApplyChanges)
            series.AniDbMappings.Should().NotBeNull();
            series.AniDbMappings.Should().HaveCount(3);
            series.AniDbMappings.Should().BeEquivalentTo(expectedMappings);

            // Assert that no matter which season was added, the resulting hub has the correct root AniDbId.
            series.AniDbId.Should().Be(expectedHubAniDbId);
        }

        [Test]
        public void should_throw_if_series_validation_fails()
        {
            var newSeries = new Series
            {
                TvdbId = 1,
                Path = @"C:\Test\TV\Title1"
            };

            GivenValidSeries(newSeries.TvdbId);

            Mocker.GetMock<IAddSeriesValidator>()
                  .Setup(s => s.Validate(It.IsAny<Series>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddSeries(newSeries));
        }

        [Test]
        public void should_throw_if_series_cannot_be_found()
        {
            var newSeries = new Series
            {
                TvdbId = 1,
                Path = @"C:\Test\TV\Title1"
            };

            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(s => s.GetSeriesInfo(It.IsAny<Series>()))
                  .Throws(new SeriesNotFoundException(newSeries.TvdbId));

            Mocker.GetMock<IAddSeriesValidator>()
                  .Setup(s => s.Validate(It.IsAny<Series>()))
                  .Returns(new ValidationResult(new List<ValidationFailure>
                                                {
                                                    new ValidationFailure("Path", "Test validation failure")
                                                }));

            Assert.Throws<ValidationException>(() => Subject.AddSeries(newSeries));

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
