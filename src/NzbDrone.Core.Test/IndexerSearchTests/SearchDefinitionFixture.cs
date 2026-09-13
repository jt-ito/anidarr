using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    public class SearchDefinitionFixture : CoreTest<SingleEpisodeSearchCriteria>
    {
        [TestCase("Betty White's Off Their Rockers", "Betty+Whites+Off+Their+Rockers")]
        [TestCase("Star Wars: The Clone Wars", "Star+Wars+The+Clone+Wars")]
        [TestCase("Hawaii Five-0", "Hawaii+Five+0")]
        [TestCase("Franklin & Bash", "Franklin+and+Bash")]
        [TestCase("Chicago P.D.", "Chicago+PD")]
        [TestCase("Kourtney And Khlo\u00E9 Take The Hamptons", "Kourtney+And+Khloe+Take+The+Hamptons")]
        [TestCase("Betty White`s Off Their Rockers", "Betty+Whites+Off+Their+Rockers")]
        [TestCase("Betty White\u00b4s Off Their Rockers", "Betty+Whites+Off+Their+Rockers")]
        [TestCase("Betty White‘s Off Their Rockers", "Betty+Whites+Off+Their+Rockers")]
        [TestCase("Betty White’s Off Their Rockers", "Betty+Whites+Off+Their+Rockers")]
        public void should_replace_some_special_characters(string input, string expected)
        {
            Subject.SceneTitles = new List<string> { input };
            Subject.CleanSceneTitles.First().Should().Be(expected);
        }

        [TestCase("Onaji Zemi no Someya-san ga Sexy Joyuu Datta Hanashi.", "Onaji Zemi no Someya-san ga Sexy Joyuu Datta Hanashi")]
        [TestCase("同じゼミの染谷さんがセクシー女優だった話。", "同じゼミの染谷さんがセクシー女優だった話")]
        [TestCase("A Story about How Someya-san, a Girl from My College Seminar, Turned out to Be an AV Actress.", "A Story about How Someya-san, a Girl from My College Seminar, Turned out to Be an AV Actress")]
        [TestCase("My Classmate's a Sexy Actress, and Now We Live Together?!", "My Classmate's a Sexy Actress, and Now We Live Together")]
        [TestCase("My Classmate`s a Sexy Actress, and Now We Live Together?!", "My Classmate's a Sexy Actress, and Now We Live Together")]
        [TestCase("Some Anime Title!", "Some Anime Title")]
        [TestCase("Another Anime Title?!", "Another Anime Title")]
        [TestCase("Title with hyphens - stays", "Title with hyphens - stays")]
        [TestCase("Title with full-width question mark？", "Title with full-width question mark")]
        public void should_normalize_anime_titles(string input, string expected)
        {
            SearchCriteriaBase.NormalizeAnimeTitle(input).Should().Be(expected);
        }

        [Test]
        public void should_prioritize_romaji_title_for_anidb_series()
        {
            Subject.Series = new NzbDrone.Core.Tv.Series
            {
                Title = "English Title",
                AlternateTitles = new List<string> { "Romaji Title", "Japanese Title" },
                PrimaryMetadataProvider = "anidb"
            };

            var titles = Subject.AnimeSearchTitles;

            titles.First().Should().Be("Romaji Title");
            titles.Skip(1).First().Should().Be("English Title");
            titles.Should().HaveCount(3);
        }

        [Test]
        public void should_apply_policy_b_ordering_with_guaranteed_native_japanese_slot_2_for_delimited_series()
        {
            Subject.Series = new NzbDrone.Core.Tv.Series
            {
                Title = "Ane Jiru The Animation - Shirakawa San Shimai ni Omakase",
                AlternateTitles = new List<string>
                {
                    "Anejiru The Animation: Shirakawa Sanshimai ni Omakase",
                    "姉汁 THE ANIMATION 白川三姉妹におまかせ",
                    "Ane Jiru The Animation - Shirakawa San Shimai ni Omakase",
                    "Anejiru 2"
                },
                PrimaryMetadataProvider = "anidb"
            };

            var titles = Subject.AnimeSearchTitles;

            titles.Should().HaveCount(5);
            titles[0].Should().Be("Anejiru The Animation: Shirakawa Sanshimai ni Omakase");
            titles[1].Should().Be("姉汁 THE ANIMATION 白川三姉妹におまかせ");
            titles[2].Should().Be("Ane Jiru The Animation - Shirakawa San Shimai ni Omakase");
            titles[3].Should().Be("Anejiru The Animation");
            titles[4].Should().Be("Anejiru 2");
        }

        [Test]
        public void should_fallback_to_english_base_when_no_synonym_exists_for_delimited_series()
        {
            Subject.Series = new NzbDrone.Core.Tv.Series
            {
                Title = "Ane Jiru The Animation - Shirakawa San Shimai ni Omakase",
                AlternateTitles = new List<string>
                {
                    "Anejiru The Animation: Shirakawa Sanshimai ni Omakase",
                    "姉汁 THE ANIMATION 白川三姉妹におまかせ",
                    "Ane Jiru The Animation - Shirakawa San Shimai ni Omakase"
                },
                PrimaryMetadataProvider = "anidb"
            };

            var titles = Subject.AnimeSearchTitles;

            titles.Should().HaveCount(5);
            titles[0].Should().Be("Anejiru The Animation: Shirakawa Sanshimai ni Omakase");
            titles[1].Should().Be("姉汁 THE ANIMATION 白川三姉妹におまかせ");
            titles[2].Should().Be("Ane Jiru The Animation - Shirakawa San Shimai ni Omakase");
            titles[3].Should().Be("Anejiru The Animation");
            titles[4].Should().Be("Ane Jiru The Animation");
        }

        [Test]
        public void should_not_strip_variants_for_series_without_delimiter()
        {
            Subject.Series = new NzbDrone.Core.Tv.Series
            {
                Title = "Frieren: Beyond Journey's End",
                AlternateTitles = new List<string>
                {
                    "Sousou no Frieren",
                    "葬送のフリーレン",
                    "Frieren at the Funeral",
                    "Frieren"
                },
                PrimaryMetadataProvider = "anidb"
            };

            var titles = Subject.AnimeSearchTitles;

            titles.Should().HaveCount(5);
            titles[0].Should().Be("Sousou no Frieren");
            titles[1].Should().Be("葬送のフリーレン");
            titles[2].Should().Be("Frieren: Beyond Journey's End");
            titles[3].Should().Be("Frieren at the Funeral");
            titles[4].Should().Be("Frieren");
        }

        [Test]
        public void should_keep_fuller_title_when_base_title_fails_safety_guards()
        {
            // "Re" is < 4 characters and single word <= 3 chars
            SearchCriteriaBase.TryGetBaseTitle("Re:Zero kara Hajimeru Isekai Seikatsu", out var baseTitle).Should().BeFalse();

            // "OVA" is a stopword and < 4 characters
            SearchCriteriaBase.TryGetBaseTitle("OVA: The Animation", out baseTitle).Should().BeFalse();

            // "Air" is a single word <= 3 chars
            SearchCriteriaBase.TryGetCoreTitle("Air The Animation", out var coreTitle).Should().BeFalse();
        }

        [Test]
        public void should_not_apply_policy_b_for_non_anidb_series()
        {
            Subject.Series = new NzbDrone.Core.Tv.Series
            {
                Title = "TVDB Anime Series: The Subtitle",
                AlternateTitles = new List<string> { "TVDB Anime Series: The Subtitle (Alt)" },
                PrimaryMetadataProvider = "tvdb",
                AniDbId = null
            };

            var titles = Subject.AnimeSearchTitles;

            titles.First().Should().Be("TVDB Anime Series: The Subtitle");
            titles.Should().NotContain("TVDB Anime Series");
        }
    }
}
