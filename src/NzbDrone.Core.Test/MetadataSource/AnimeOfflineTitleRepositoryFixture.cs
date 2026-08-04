using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
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
            // CleanForSearch preserves non-alphanumeric CJK characters.
            // In the real pipeline, the Title parsing converts it using CleanForSearch.
            var results = Subject.FindSearchMatches("é€²æ’ƒã®å·¨äºº", "anidb");

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
    }
}
