using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class FolderLookupServiceFixture
    {
        private Mock<IMetadataDispatcher> _dispatcher;
        private Mock<IFolderProbeService> _probe;
        private Mock<IImportChoiceStore> _choices;
        private Mock<IAnimeOfflineDatabase> _offline;
        private Mock<IAniDbPrefetchService> _prefetch;
        private FolderLookupService _subject;

        [SetUp]
        public void Setup()
        {
            _dispatcher = new Mock<IMetadataDispatcher>();
            _probe = new Mock<IFolderProbeService>();
            _choices = new Mock<IImportChoiceStore>();
            _offline = new Mock<IAnimeOfflineDatabase>();
            _prefetch = new Mock<IAniDbPrefetchService>();

            _dispatcher.Setup(d => d.Search(It.IsAny<string>(), It.IsAny<MetadataProviderType?>())).Returns(new List<Series>());
            _probe.Setup(p => p.Probe(It.IsAny<string>(), It.IsAny<string>())).Returns(new FolderProbe());

            _subject = new FolderLookupService(_dispatcher.Object, _probe.Object, _choices.Object, _offline.Object, _prefetch.Object, LogManager.GetCurrentClassLogger());
        }

        private static Series Show(string title, int year, int tvdbId = 0, int aniDbId = 0, int aniListId = 0)
        {
            return new Series
            {
                Title = title,
                Year = year,
                TvdbId = tvdbId,
                AniDbId = aniDbId > 0 ? aniDbId : null,
                AniListIds = aniListId > 0 ? new HashSet<int> { aniListId } : new HashSet<int>()
            };
        }

        [Test]
        public void should_use_an_id_in_the_folder_without_any_title_matching()
        {
            var live = Show("ONE PIECE (2023)", 2023, tvdbId: 392276);
            _probe.Setup(p => p.Probe("/lib/One Piece", "One Piece [tvdbid-392276]"))
                  .Returns(new FolderProbe { IdHints = { ("tvdb", 392276) } });
            _dispatcher.Setup(d => d.Search("tvdb:392276", MetadataProviderType.Tvdb)).Returns(new List<Series> { live });

            var match = _subject.Lookup("One Piece [tvdbid-392276]", "/lib/One Piece");

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.Results.Should().ContainSingle().Which.TvdbId.Should().Be(392276);
            match.Reason.Should().Contain("found in the folder");
            _dispatcher.Verify(d => d.Search("One Piece [tvdbid-392276]", It.IsAny<MetadataProviderType?>()), Times.Never());
        }

        [Test]
        public void should_use_a_choice_remembered_from_before()
        {
            _choices.Setup(c => c.Find("One Piece")).Returns(new ImportChoice { Provider = "anidb", Id = 69, Title = "One Piece" });
            _dispatcher.Setup(d => d.Search("anidb:69", MetadataProviderType.AniDb)).Returns(new List<Series> { Show("One Piece", 1999, aniDbId: 69) });

            var match = _subject.Lookup("One Piece");

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.Results[0].AniDbId.Should().Be(69);
            match.Reason.Should().Contain("remembered");
        }

        [Test]
        public void should_fall_back_to_title_matching_when_an_id_does_not_resolve()
        {
            _probe.Setup(p => p.Probe(It.IsAny<string>(), It.IsAny<string>())).Returns(new FolderProbe { IdHints = { ("tvdb", 1) } });
            _dispatcher.Setup(d => d.Search("Some Show", MetadataProviderType.Tvdb)).Returns(new List<Series> { Show("Some Show", 2020, tvdbId: 5) });

            var match = _subject.Lookup("Some Show [tvdbid-1]", "/lib/x");

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.Results[0].TvdbId.Should().Be(5);
        }

        [Test]
        public void should_search_with_the_title_from_the_episode_files_too()
        {
            _probe.Setup(p => p.Probe(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new FolderProbe { FileTitles = { "Real Show Name" }, VideoFileCount = 12 });
            _dispatcher.Setup(d => d.Search("Real Show Name", MetadataProviderType.Tvdb)).Returns(new List<Series> { Show("Real Show Name", 2019, tvdbId: 7) });

            var match = _subject.Lookup("zzz", "/lib/zzz");

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.FileCount.Should().Be(12);
        }

        [Test]
        public void should_match_an_english_title_through_the_offline_database()
        {
            var daily = Show("Daily Lives of High School Boys", 2012, tvdbId: 3, aniListId: 5);
            _dispatcher.Setup(d => d.Search("Danshi Koukousei no Nichijou", MetadataProviderType.Tvdb)).Returns(new List<Series> { daily });
            _offline.Setup(o => o.GetSeriesById("anilist", 5)).Returns(new AnimeOfflineTitle
            {
                Title = "Daily Lives of High School Boys",
                RomajiTitle = "Danshi Koukousei no Nichijou"
            });

            var match = _subject.Lookup("Danshi Koukousei no Nichijou");

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.Results[0].TvdbId.Should().Be(3);
        }

        [Test]
        public void should_match_when_only_one_series_has_the_title_as_a_synonym()
        {
            var daily = Show("Daily Lives of High School Boys", 2012, tvdbId: 3, aniListId: 5);
            _dispatcher.Setup(d => d.Search("DKNN", MetadataProviderType.Tvdb)).Returns(new List<Series> { daily });
            _offline.Setup(o => o.GetSeriesById("anilist", 5)).Returns(new AnimeOfflineTitle
            {
                Title = "Daily Lives of High School Boys",
                SearchSynonyms = new List<string> { "DKNN" }
            });

            var match = _subject.Lookup("DKNN");

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.Reason.Should().Contain("alternate title");
        }

        [Test]
        public void should_not_pick_between_series_that_share_a_synonym()
        {
            var first = Show("First Show", 2012, tvdbId: 3, aniListId: 5);
            var second = Show("Second Show", 2014, tvdbId: 4, aniListId: 6);
            _dispatcher.Setup(d => d.Search("Shared Nickname", MetadataProviderType.Tvdb)).Returns(new List<Series> { first, second });
            _offline.Setup(o => o.GetSeriesById("anilist", It.IsAny<int>())).Returns(new AnimeOfflineTitle
            {
                SearchSynonyms = new List<string> { "Shared Nickname" }
            });

            _subject.Lookup("Shared Nickname").Status.Should().Be(LookupMatchStatus.Possible);
        }

        [Test]
        public void should_prefer_an_official_title_over_a_synonym_spelled_the_same()
        {
            var official = Show("Gate", 2015, tvdbId: 3, aniListId: 5);
            var other = Show("Something Else Entirely", 2016, tvdbId: 4, aniListId: 6);
            _dispatcher.Setup(d => d.Search("Gate", MetadataProviderType.Tvdb)).Returns(new List<Series> { official, other });
            _offline.Setup(o => o.GetSeriesById("anilist", 6)).Returns(new AnimeOfflineTitle { SearchSynonyms = new List<string> { "Gate" } });

            var match = _subject.Lookup("Gate");

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.Results[0].TvdbId.Should().Be(3);
        }

        [Test]
        public void should_list_the_other_season_right_after_the_match_and_show_the_cleaned_search()
        {
            var first = Show("Sex Exchange", 0, aniDbId: 4337);
            var second = Show("Sex Exchange (2)", 0, aniDbId: 4738);
            var junk1 = Show("Tomato Twins", 1986, tvdbId: 72871);
            var junk2 = Show("Monkey Twins", 2018, tvdbId: 99);
            _dispatcher.Setup(d => d.Search("Imouto Twins", MetadataProviderType.AniDb)).Returns(new List<Series> { first, second });
            _dispatcher.Setup(d => d.Search("Imouto Twins", MetadataProviderType.Tvdb)).Returns(new List<Series> { junk1, junk2 });
            _offline.Setup(o => o.GetSeriesById("anidb", 4337)).Returns(new AnimeOfflineTitle { SearchSynonyms = new List<string> { "Imouto Twins" } });

            var match = _subject.Lookup("( HT ) Imouto Twins");

            match.Results.Select(r => r.Title).Take(2).Should().Equal("Sex Exchange", "Sex Exchange (2)");
            match.SearchTerm.Should().Be("Imouto Twins");
        }

        [Test]
        public void should_look_up_aliases_once_per_result_however_many_searches_find_it()
        {
            var show = Show("Daily Lives of High School Boys", 2012, tvdbId: 3, aniListId: 5);

            // every term on both providers returns the same result
            _dispatcher.Setup(d => d.Search(It.IsAny<string>(), It.IsAny<MetadataProviderType?>())).Returns(new List<Series> { show });
            _offline.Setup(o => o.GetSeriesById("anilist", 5)).Returns(new AnimeOfflineTitle { Title = "Daily Lives of High School Boys" });

            _subject.Lookup("[Group] Some Other Name - 01 [1080p][HEVC]");

            _offline.Verify(o => o.GetSeriesById("anilist", 5), Times.Once());
        }

        [Test]
        public void should_prefetch_the_hub_of_a_confident_anidb_match()
        {
            _dispatcher.Setup(d => d.Search("Sex Exchange", MetadataProviderType.AniDb)).Returns(new List<Series> { Show("Sex Exchange", 2003, aniDbId: 4337) });

            _subject.Lookup("Sex Exchange").Status.Should().Be(LookupMatchStatus.Matched);

            _prefetch.Verify(p => p.Enqueue(4337), Times.Once());
        }

        [Test]
        public void should_not_prefetch_for_a_series_that_comes_from_tvdb()
        {
            _dispatcher.Setup(d => d.Search("Some Show", MetadataProviderType.Tvdb)).Returns(new List<Series> { Show("Some Show", 2020, tvdbId: 5) });

            _subject.Lookup("Some Show");

            _prefetch.Verify(p => p.Enqueue(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_prefetch_when_the_match_is_only_possible()
        {
            _dispatcher.Setup(d => d.Search("Kingdom", MetadataProviderType.AniDb))
                       .Returns(new List<Series> { Show("Kingdom", 2012, aniDbId: 8791), Show("Kingdom", 2019, aniDbId: 100) });

            _subject.Lookup("Kingdom").Status.Should().Be(LookupMatchStatus.Possible);

            _prefetch.Verify(p => p.Enqueue(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_search_both_providers()
        {
            _dispatcher.Setup(d => d.Search("Some Show", MetadataProviderType.Tvdb)).Returns(new List<Series> { Show("Some Show", 2020, tvdbId: 5) });
            _dispatcher.Setup(d => d.Search("Some Show", MetadataProviderType.AniDb)).Returns(new List<Series> { Show("Some Show", 2020, aniDbId: 9) });

            var match = _subject.Lookup("Some Show");

            _dispatcher.Verify(d => d.Search("Some Show", MetadataProviderType.Tvdb), Times.Once());
            _dispatcher.Verify(d => d.Search("Some Show", MetadataProviderType.AniDb), Times.Once());

            // TVDB result first, and the two are the same show (same title and year), so one entry
            match.Results.Should().ContainSingle().Which.TvdbId.Should().Be(5);
        }

        [Test]
        public void should_still_work_when_the_offline_database_fails()
        {
            _dispatcher.Setup(d => d.Search("Some Show", MetadataProviderType.Tvdb)).Returns(new List<Series> { Show("Some Show", 2020, tvdbId: 5, aniListId: 9) });
            _offline.Setup(o => o.GetSeriesById(It.IsAny<string>(), It.IsAny<int>())).Throws(new System.InvalidOperationException("db"));

            _subject.Lookup("Some Show").Status.Should().Be(LookupMatchStatus.Matched);
        }
    }
}
