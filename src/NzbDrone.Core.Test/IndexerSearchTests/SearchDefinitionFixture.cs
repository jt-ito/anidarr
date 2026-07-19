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
        [TestCase("Some Anime Title!", "Some Anime Title")]
        [TestCase("Another Anime Title?!", "Another Anime Title")]
        [TestCase("Title with hyphens - stays", "Title with hyphens - stays")]
        [TestCase("Title with full-width question mark？", "Title with full-width question mark")]
        public void should_normalize_anime_titles(string input, string expected)
        {
            SearchCriteriaBase.NormalizeAnimeTitle(input).Should().Be(expected);
        }
    }
}
