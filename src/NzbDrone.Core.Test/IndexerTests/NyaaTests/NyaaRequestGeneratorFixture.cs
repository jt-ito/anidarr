using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Nyaa;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.NyaaTests
{
    public class NyaaRequestGeneratorFixture : CoreTest<NyaaRequestGenerator>
    {
        private SeasonSearchCriteria _seasonSearchCriteria;
        private AnimeEpisodeSearchCriteria _animeSearchCriteria;
        private AnimeSeasonSearchCriteria _animeSeasonSearchCriteria;

        [SetUp]
        public void SetUp()
        {
            Subject.Settings = new NyaaSettings()
            {
                BaseUrl = "http://127.0.0.1:1234/",
            };

            _seasonSearchCriteria = new SeasonSearchCriteria()
            {
                SceneTitles = new List<string>() { "Naruto Shippuuden" },
                SeasonNumber = 1,
            };

            _animeSearchCriteria = new AnimeEpisodeSearchCriteria()
            {
                SceneTitles = new List<string>() { "Naruto Shippuuden" },
                AbsoluteEpisodeNumber = 9,
                SeasonNumber = 1,
                EpisodeNumber = 9
            };

            _animeSeasonSearchCriteria = new AnimeSeasonSearchCriteria()
            {
                SceneTitles = new List<string>() { "Naruto Shippuuden" },
                SeasonNumber = 3
            };
        }

        [Test]
        public void should_not_search_season()
        {
            var results = Subject.GetSearchRequests(_seasonSearchCriteria);

            results.GetAllTiers().Should().HaveCount(0);
        }

        [Test]
        public void should_search_season()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            var results = Subject.GetSearchRequests(_seasonSearchCriteria);

            results.GetAllTiers().Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.FullUri.Should().Contain("term=Naruto+Shippuuden+s01");
        }

        [Test]
        public void should_use_only_absolute_numbering_for_anime_search()
        {
            var results = Subject.GetSearchRequests(_animeSearchCriteria);

            results.GetTier(0).Should().HaveCount(2);
            var pages = results.GetTier(0).Take(2).Select(t => t.First()).ToList();

            pages[0].Url.FullUri.Should().Contain("term=Naruto+Shippuuden+9");
            pages[1].Url.FullUri.Should().Contain("term=Naruto+Shippuuden+09");
        }

        [Test]
        public void should_also_use_standard_numbering_for_anime_search()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            var results = Subject.GetSearchRequests(_animeSearchCriteria);

            results.GetTier(0).Should().HaveCount(3);
            var pages = results.GetTier(0).Take(3).Select(t => t.First()).ToList();

            pages[0].Url.FullUri.Should().Contain("term=Naruto+Shippuuden+9");
            pages[1].Url.FullUri.Should().Contain("term=Naruto+Shippuuden+09");
            pages[2].Url.FullUri.Should().Contain("term=Naruto+Shippuuden+s01e09");
        }

        [Test]
        public void should_search_by_standard_season_number()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            var results = Subject.GetSearchRequests(_animeSeasonSearchCriteria);

            results.GetAllTiers().Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.FullUri.Should().Contain("term=Naruto+Shippuuden+s03");
        }

        [Test]
        public void should_search_multiple_tiers_for_anime_series_with_alternate_titles()
        {
            var animeCriteria = new AnimeEpisodeSearchCriteria()
            {
                Series = new NzbDrone.Core.Tv.Series
                {
                    SeriesType = NzbDrone.Core.Tv.SeriesTypes.Anime,
                    Title = "Frieren: Beyond Journey's End",
                    AlternateTitles = new List<string> { "Sousou no Frieren" },
                    PrimaryMetadataProvider = "anidb"
                },
                AbsoluteEpisodeNumber = 12,
                SeasonNumber = 1,
                EpisodeNumber = 12
            };

            var results = Subject.GetSearchRequests(animeCriteria);

            results.Tiers.Should().BeGreaterThanOrEqualTo(2);

            var tier0 = results.GetTier(0).Select(t => t.First()).ToList();
            tier0.First().Url.FullUri.Should().Contain("Sousou+no+Frieren+12");

            var tier1 = results.GetTier(1).Select(t => t.First()).ToList();
            tier1.First().Url.FullUri.Should().Contain("Frieren");
        }
    }
}
