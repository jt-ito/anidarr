using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;

namespace NzbDrone.Core.Test.TvTests.SeriesServiceTests
{
    // Anidarr: AniDB is fetched synchronously as part of add/refresh (so a newly
    // added series has real info immediately), while AniList is a secondary,
    // deferred enrichment step queued via the command queue so it never slows
    // down adding a series. These tests lock in that the deferred commands are
    // queued for every add path (single add and bulk import), not just one.
    [TestFixture]
    public class AddSeriesEnrichmentFixture : CoreTest<SeriesService>
    {
        private Series GivenAniDbSeries(int id)
        {
            return Builder<Series>.CreateNew()
                .With(s => s.Id = id)
                .With(s => s.PrimaryMetadataProvider = "anidb")
                .With(s => s.AniDbMappings = null)
                .With(s => s.AniDbRelatedSeries = null)
                .Build();
        }

        [Test]
        public void AddSeries_single_should_queue_anilist_enrichment_for_anidb_series()
        {
            var series = GivenAniDbSeries(1);

            Subject.AddSeries(series);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.Is<EnrichSeriesFromAniListCommand>(cmd => cmd.SeriesId == 1), CommandPriority.High, CommandTrigger.Unspecified), Times.Once());
        }

        [Test]
        public void AddSeries_bulk_should_also_queue_anilist_enrichment_for_anidb_series()
        {
            // This is the path used by "Import Existing Series" (SeriesImportController).
            // Before this fix, only the single-series add path queued enrichment.
            var series = GivenAniDbSeries(2);

            Subject.AddSeries(new List<Series> { series });

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.Is<EnrichSeriesFromAniListCommand>(cmd => cmd.SeriesId == 2), CommandPriority.High, CommandTrigger.Unspecified), Times.Once());
        }

        [Test]
        public void AddSeries_should_not_queue_anilist_enrichment_for_non_anidb_series()
        {
            var series = Builder<Series>.CreateNew()
                .With(s => s.Id = 3)
                .With(s => s.PrimaryMetadataProvider = "tvdb")
                .With(s => s.AniDbMappings = null)
                .With(s => s.AniDbRelatedSeries = null)
                .Build();

            Subject.AddSeries(series);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.IsAny<EnrichSeriesFromAniListCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }
    }
}
