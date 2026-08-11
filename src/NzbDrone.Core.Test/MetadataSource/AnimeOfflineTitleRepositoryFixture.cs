using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class AnimeOfflineTitleRepositoryFixture : DbTest<AnimeOfflineTitleRepository, AnimeOfflineTitle>
    {
        [SetUp]
        public void Setup()
        {
            var title1 = new AnimeOfflineTitle
            {
                Title = "Shingeki no Kyojin",
                CleanTitle = "shingekinokyojin",
                SearchSynonyms = new List<string> { "attackontitan", "advancinggiants", "é€²æ’ƒã®å·¨äºº", "aot" },
                AniDbId = 1,
                MalId = 1,
                AniListId = 1
            };

            var title2 = new AnimeOfflineTitle
            {
                Title = "Boku no Hero Academia",
                CleanTitle = "bokunoheroacademia",
                SearchSynonyms = new List<string> { "myheroacademia" },
                AniDbId = 2,
                MalId = 2,
                AniListId = 2
            };

            Subject.InsertMany(new List<AnimeOfflineTitle> { title1, title2 });
        }

        [Test]
        public void should_find_by_cleantitle()
        {
            var results = Subject.FindSearchMatches("shingeki", "anidb");

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("Shingeki no Kyojin");
        }

        [Test]
        public void should_find_by_english_synonym()
        {
            var results = Subject.FindSearchMatches("attackontitan", "anidb");

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("Shingeki no Kyojin");
        }

        [Test]
        public void should_find_by_native_synonym()
        {
            var results = Subject.FindSearchMatches("advancinggiants", "anidb");

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("Shingeki no Kyojin");
        }

        [Test]
        public void should_not_return_false_positives()
        {
            var results = Subject.FindSearchMatches("attackonacademia", "anidb");

            results.Should().BeEmpty();
        }

        [Test]
        public void should_find_by_native_title()
        {
            // CleanForSearch preserves CJK letter/digit characters.
            // The real code path (AnimeOfflineDatabase.Search) always calls CleanForSearch()
            // on the query before passing it to FindSearchMatches.
            var cleanQuery = "é€²æ'ƒã®å·¨äºº".CleanForSearch();
            var results = Subject.FindSearchMatches(cleanQuery, "anidb");

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("Shingeki no Kyojin");
        }

        [Test]
        public void should_find_by_short_title()
        {
            // AniDB often has "short" titles in its synonym list.
            var results = Subject.FindSearchMatches("aot", "anidb");

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("Shingeki no Kyojin");
        }

        [Test]
        public void should_filter_by_provider_key()
        {
            var title3 = new AnimeOfflineTitle
            {
                Title = "Test Anime",
                CleanTitle = "testanime",
                SearchSynonyms = new List<string> { "testsynonym" },
                AniDbId = 0,
                MalId = 3,
                AniListId = 0
            };
            Subject.Insert(title3);

            var anidbResults = Subject.FindSearchMatches("testsynonym", "anidb");
            anidbResults.Should().BeEmpty();

            var malResults = Subject.FindSearchMatches("testsynonym", "mal");
            malResults.Should().HaveCount(1);
        }

        [Test]
        public void should_match_fuzzy_single_char_missing_added_swapped()
        {
            // "shingekinokyojin" len 16 -> 16*0.2 = 3 edits allowed
            Subject.FindSearchMatches("shingeknokyojin", "anidb").Should().HaveCount(1); // missing i
            Subject.FindSearchMatches("shingekinnokyojin", "anidb").Should().HaveCount(1); // added n
            Subject.FindSearchMatches("shingkeinokyojin", "anidb").Should().HaveCount(1); // swapped e/k (distance 2)

            // "bokunoheroacademia" len 18 -> 3 edits allowed
            Subject.FindSearchMatches("bokunoheracademia", "anidb").Should().HaveCount(1); // missing o
        }

        [Test]
        public void should_not_produce_duplicates_when_substring_matches()
        {
            // Exact substring matches "attackontitan", it shouldn't be duplicated by fuzzy match
            var results = Subject.FindSearchMatches("attackontitan", "anidb");
            results.Should().HaveCount(1);
        }

        [Test]
        public void should_not_match_wildly_different_titles()
        {
            // "kon" length 3. query "xyz" length 3 -> dist 3. allowed is max(1, 0) = 1.
            var title4 = new AnimeOfflineTitle
            {
                Title = "K-On",
                CleanTitle = "kon",
                AniDbId = 4,
                SearchSynonyms = new List<string>()
            };
            Subject.Insert(title4);

            var results = Subject.FindSearchMatches("xyz", "anidb");
            results.Should().BeEmpty();
        }

        [Test]
        public void should_handle_native_script_fuzzy_matches_if_applicable()
        {
            // CJK characters: é€²æ'ƒã®å·¨äºº (Shingeki no Kyojin)
            // Query with one missing character: é€²æ'ƒãå·¨äºº
            // Real CleanForSearch will preserve these CJK characters
            var cleanQuery = "é€²æ'ƒãå·¨äºº".CleanForSearch();
            var results = Subject.FindSearchMatches(cleanQuery, "anidb");

            // Assuming distance is 1 and length is ~8, allowed is 1
            results.Should().HaveCount(1);
        }
    }
}
