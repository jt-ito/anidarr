using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private List<Series> GivenBatchWhereFirstSeriesFailsValidation()
        {
            var batch = new List<Series>
            {
                new Series { TvdbId = 1, Title = "Bad", Path = @"C:TestTVBad" },
                new Series { TvdbId = 2, Title = "Good", Path = @"C:TestTVGood" }
            };

            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(s => s.GetSeriesInfo(It.IsAny<Series>()))
                  .Returns<Series>(s => new Tuple<Series, List<Episode>>(s, new List<Episode>()));

            Mocker.GetMock<IAddSeriesValidator>()
                  .Setup(s => s.Validate(It.Is<Series>(x => x.TvdbId == 1)))
                  .Returns(new ValidationResult(new List<ValidationFailure> { new ValidationFailure("Path", "Test validation failure") }));
            Mocker.GetMock<IAddSeriesValidator>()
                  .Setup(s => s.Validate(It.Is<Series>(x => x.TvdbId == 2)))
                  .Returns(new ValidationResult());

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.AddSeries(It.IsAny<List<Series>>()))
                  .Returns<List<Series>>(l => l);

            return batch;
        }

        [Test]
        public void should_throw_and_add_nothing_for_batch_when_a_series_fails_validation_by_default()
        {
            var batch = GivenBatchWhereFirstSeriesFailsValidation();

            Assert.Throws<ValidationException>(() => Subject.AddSeries(batch));

            Mocker.GetMock<ISeriesService>()
                  .Verify(s => s.AddSeries(It.IsAny<List<Series>>()), Times.Never());
        }

        private void GivenSeriesServiceSavesWhatItIsGiven()
        {
            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.AddSeries(It.IsAny<List<Series>>()))
                  .Returns<List<Series>>(l => l);
        }

        private static Series AniDbOnly(int aniDbId, string title)
        {
            return new Series
            {
                Title = title,
                AniDbId = aniDbId,
                TvdbId = 0,
                PrimaryMetadataProvider = "anidb",
                Path = @"C:\Test\TV\" + title
            };
        }

        [Test]
        public void should_add_anidb_series_from_the_lookup_data_when_metadata_is_deferred()
        {
            GivenValidPath();
            GivenSeriesServiceSavesWhatItIsGiven();

            var outcome = Subject.AddSeriesBackground(new List<Series> { AniDbOnly(4738, "Sex Exchange (2)") }, true).Single();

            outcome.Added.Should().NotBeNull();
            outcome.Added.LastInfoSync.Should().BeNull();
            Mocker.GetMock<IMetadataDispatcher>().Verify(d => d.GetSeriesInfo(It.IsAny<Series>()), Times.Never());
        }

        [Test]
        public void should_fetch_metadata_when_it_is_not_deferred()
        {
            GivenValidPath();
            GivenSeriesServiceSavesWhatItIsGiven();
            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(d => d.GetSeriesInfo(It.IsAny<Series>()))
                  .Returns<Series>(s => new Tuple<Series, List<Episode>>(s, new List<Episode>()));

            var outcome = Subject.AddSeriesBackground(new List<Series> { AniDbOnly(4337, "Sex Exchange") }, false).Single();

            outcome.Added.LastInfoSync.Should().NotBeNull();
            Mocker.GetMock<IMetadataDispatcher>().Verify(d => d.GetSeriesInfo(It.IsAny<Series>()), Times.Once());
        }

        [Test]
        public void should_still_fetch_metadata_for_series_with_a_tvdb_id_when_deferring()
        {
            GivenValidPath();
            GivenSeriesServiceSavesWhatItIsGiven();
            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(d => d.GetSeriesInfo(It.IsAny<Series>()))
                  .Returns<Series>(s => new Tuple<Series, List<Episode>>(s, new List<Episode>()));

            var tvdb = new Series { Title = "Some Show", TvdbId = 5, PrimaryMetadataProvider = "tvdb", Path = @"C:\Test\TV\Some Show" };

            var outcome = Subject.AddSeriesBackground(new List<Series> { tvdb }, true).Single();

            outcome.Added.LastInfoSync.Should().NotBeNull();
            Mocker.GetMock<IMetadataDispatcher>().Verify(d => d.GetSeriesInfo(It.IsAny<Series>()), Times.Once());
        }

        [Test]
        public void should_leave_a_series_added_while_the_provider_was_unreachable_for_the_next_refresh()
        {
            GivenValidPath();
            GivenSeriesServiceSavesWhatItIsGiven();
            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(d => d.GetSeriesInfo(It.IsAny<Series>()))
                  .Throws(new Exception("AniDB error for ID 4337: banned"));

            var outcome = Subject.AddSeriesBackground(new List<Series> { AniDbOnly(4337, "Sex Exchange") }, false).Single();

            outcome.Added.Should().NotBeNull();
            outcome.Added.LastInfoSync.Should().BeNull();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_skip_a_season_of_a_hub_that_is_already_in_the_library()
        {
            GivenValidPath();
            GivenSeriesServiceSavesWhatItIsGiven();

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<Series> { new Series { Id = 5, AniDbId = 4337, TvdbId = -4337, Title = "Sex Exchange" } });
            Mocker.GetMock<IAniDbSeriesMappingService>()
                  .Setup(m => m.GetMappingByAniDbId(4738))
                  .Returns(new AniDbSeriesMapping { SeriesId = 5, AniDbId = 4738 });

            var outcome = Subject.AddSeriesBackground(new List<Series> { AniDbOnly(4738, "Sex Exchange (2)") }, true).Single();

            outcome.Added.Should().BeNull();
            outcome.Reason.Should().Contain("Already in your library");
        }

        [Test]
        public void should_say_why_a_series_that_failed_validation_was_not_added()
        {
            GivenValidSeries(1);
            GivenValidPath();
            GivenSeriesServiceSavesWhatItIsGiven();
            Mocker.GetMock<IAddSeriesValidator>()
                  .Setup(s => s.Validate(It.IsAny<Series>()))
                  .Returns(new ValidationResult(new List<ValidationFailure> { new ValidationFailure("Path", "That folder is already used by another series") }));

            var outcome = Subject.AddSeriesBackground(new List<Series> { new Series { TvdbId = 1, Title = "X", Path = @"C:\Test\TV\X" } }, false).Single();

            outcome.Added.Should().BeNull();
            outcome.Reason.Should().Contain("already used by another series");
        }

        [Test]
        public void should_add_in_the_background_only_when_asked_to()
        {
            GivenValidPath();
            GivenSeriesServiceSavesWhatItIsGiven();
            Mocker.GetMock<IMetadataDispatcher>()
                  .Setup(d => d.GetSeriesInfo(It.IsAny<Series>()))
                  .Returns<Series>(s => new Tuple<Series, List<Episode>>(s, new List<Episode>()));

            Subject.AddSeriesBackground(new List<Series> { AniDbOnly(1, "A") }, true);
            NzbDrone.Core.MetadataSource.AniDb.AniDbRateLimiter.IsManualContext.Value.Should().BeFalse();

            Subject.AddSeries(new List<Series> { AniDbOnly(2, "B") }, true);
            NzbDrone.Core.MetadataSource.AniDb.AniDbRateLimiter.IsManualContext.Value.Should().BeTrue();

            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_add_valid_series_in_batch_when_ignoring_errors()
        {
            var batch = GivenBatchWhereFirstSeriesFailsValidation();

            var added = Subject.AddSeries(batch, true);

            added.Should().ContainSingle().Which.TvdbId.Should().Be(2);
        }
    }
}
