using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;

namespace NzbDrone.Core.Test.TvTests
{
    // A series added without its hub resolved points at one AniDB entry, maybe season 2+. These lock in that
    // once the hub is known the library ends up the way a normal add produces it: one series, season one's
    // entry as the hub, later seasons inside it.
    [TestFixture]
    public class AniDbHubReconcilerFixture
    {
        private const int SeasonOne = 4337;
        private const int SeasonTwo = 4738;

        private Mock<ISeriesService> _seriesService;
        private Mock<IAniDbSeriesMappingService> _mappings;
        private Mock<IManageCommandQueue> _commands;
        private List<Series> _library;
        private List<AniDbSeriesMapping> _mappingRows;
        private AniDbHubReconciler _subject;

        [SetUp]
        public void Setup()
        {
            _library = new List<Series>();
            _mappingRows = new List<AniDbSeriesMapping>();
            _seriesService = new Mock<ISeriesService>();
            _mappings = new Mock<IAniDbSeriesMappingService>();
            _commands = new Mock<IManageCommandQueue>();

            _seriesService.Setup(s => s.GetAllSeries()).Returns(() => _library);
            _mappings.Setup(m => m.GetAllMappings()).Returns(() => _mappingRows);

            _subject = new AniDbHubReconciler(_seriesService.Object, _mappings.Object, _commands.Object, LogManager.GetCurrentClassLogger());
        }

        private static Series Pending(int id, int aniDbId, string title)
        {
            return new Series { Id = id, AniDbId = aniDbId, TvdbId = -aniDbId, Title = title };
        }

        private static Series HubInfo(int rootAniDbId)
        {
            return new Series
            {
                AniDbId = rootAniDbId,
                AniDbMappings = new List<AniDbSeriesMapping>
                {
                    new AniDbSeriesMapping { AniDbId = SeasonOne, SeasonNumber = 1 },
                    new AniDbSeriesMapping { AniDbId = SeasonTwo, SeasonNumber = 2 }
                },
                AniDbRelatedSeries = new List<AniDbRelatedSeries>()
            };
        }

        [Test]
        public void should_remove_a_later_season_when_season_one_is_already_in_the_library()
        {
            _library.Add(Pending(1, SeasonOne, "Sex Exchange"));
            var seasonTwo = Pending(2, SeasonTwo, "Sex Exchange (2)");

            var outcome = _subject.Reconcile(seasonTwo, HubInfo(SeasonOne));

            outcome.Should().Be(HubOutcome.MergedIntoExisting);
            _seriesService.Verify(s => s.DeleteSeries(It.Is<List<int>>(ids => ids.Count == 1 && ids[0] == 2), false, false), Times.Once());
        }

        [Test]
        public void should_refresh_the_hub_when_it_does_not_list_the_merged_season_yet()
        {
            _library.Add(Pending(1, SeasonOne, "Sex Exchange"));

            _subject.Reconcile(Pending(2, SeasonTwo, "Sex Exchange (2)"), HubInfo(SeasonOne));

            _commands.Verify(c => c.Push(It.Is<RefreshSeriesCommand>(cmd => cmd.SeriesIds.Contains(1)), CommandPriority.Low, CommandTrigger.Unspecified), Times.Once());
        }

        [Test]
        public void should_not_refresh_the_hub_when_it_already_lists_the_season()
        {
            _library.Add(Pending(1, SeasonOne, "Sex Exchange"));
            _mappingRows.Add(new AniDbSeriesMapping { SeriesId = 1, AniDbId = SeasonTwo });

            var outcome = _subject.Reconcile(Pending(2, SeasonTwo, "Sex Exchange (2)"), HubInfo(SeasonOne));

            outcome.Should().Be(HubOutcome.MergedIntoExisting);
            _commands.Verify(c => c.Push(It.IsAny<RefreshSeriesCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_find_the_hub_through_its_season_mappings()
        {
            // the hub row's own id is not the root, but it lists season two
            _library.Add(Pending(1, 9999, "Some Other Id"));
            _mappingRows.Add(new AniDbSeriesMapping { SeriesId = 1, AniDbId = SeasonTwo });

            _subject.Reconcile(Pending(2, SeasonTwo, "Sex Exchange (2)"), HubInfo(SeasonOne))
                    .Should().Be(HubOutcome.MergedIntoExisting);
        }

        [Test]
        public void should_keep_season_one_as_the_hub_and_adopt_the_season_mappings()
        {
            var seasonOne = Pending(1, SeasonOne, "Sex Exchange");
            var info = HubInfo(SeasonOne);

            var outcome = _subject.Reconcile(seasonOne, info);

            outcome.Should().Be(HubOutcome.BecameHub);
            seasonOne.AniDbId.Should().Be(SeasonOne);
            seasonOne.AniDbMappings.Should().BeSameAs(info.AniDbMappings);
            _seriesService.Verify(s => s.DeleteSeries(It.IsAny<List<int>>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_keep_season_one_when_season_two_is_waiting_too()
        {
            _library.Add(Pending(2, SeasonTwo, "Sex Exchange (2)"));

            _subject.Reconcile(Pending(1, SeasonOne, "Sex Exchange"), HubInfo(SeasonOne))
                    .Should().Be(HubOutcome.BecameHub);
        }

        [Test]
        public void should_make_a_lone_later_season_the_hub_using_the_root_ids()
        {
            var seasonTwo = Pending(2, SeasonTwo, "Sex Exchange (2)");

            var outcome = _subject.Reconcile(seasonTwo, HubInfo(SeasonOne));

            outcome.Should().Be(HubOutcome.BecameHub);
            seasonTwo.AniDbId.Should().Be(SeasonOne);
            seasonTwo.TvdbId.Should().Be(-SeasonOne);
        }

        [Test]
        public void should_fold_a_third_season_into_the_hub_a_second_season_became()
        {
            var seasonTwo = Pending(2, SeasonTwo, "Sex Exchange (2)");

            _subject.Reconcile(seasonTwo, HubInfo(SeasonOne));

            // season two now is the hub (root id); a third season resolving to the same root joins it
            _library.Add(seasonTwo);

            _subject.Reconcile(Pending(3, 5000, "Sex Exchange 3"), HubInfo(SeasonOne))
                    .Should().Be(HubOutcome.MergedIntoExisting);
        }

        [Test]
        public void should_not_touch_a_real_tvdb_id()
        {
            var series = new Series { Id = 2, AniDbId = SeasonTwo, TvdbId = 12345, Title = "Has a tvdb id" };

            _subject.Reconcile(series, HubInfo(SeasonOne));

            series.TvdbId.Should().Be(12345);
        }

        [Test]
        public void should_do_nothing_when_the_hub_root_is_unknown()
        {
            _subject.Reconcile(Pending(2, SeasonTwo, "x"), new Series { AniDbId = null }).Should().Be(HubOutcome.Unchanged);
            _subject.Reconcile(new Series { Id = 3, AniDbId = null }, HubInfo(SeasonOne)).Should().Be(HubOutcome.Unchanged);
        }
    }
}
