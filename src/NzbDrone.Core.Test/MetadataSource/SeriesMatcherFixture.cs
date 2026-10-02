using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class SeriesMatcherFixture
    {
        private static Series Tvdb(string title, int year, int tvdbId, int aniListId = 0)
        {
            return new Series
            {
                Title = title,
                Year = year,
                TvdbId = tvdbId,
                PrimaryMetadataProvider = "tvdb",
                AniListIds = aniListId > 0 ? new HashSet<int> { aniListId } : new HashSet<int>()
            };
        }

        private static Series AniDb(string title, int year, int aniDbId, int aniListId = 0)
        {
            return new Series
            {
                Title = title,
                Year = year,
                AniDbId = aniDbId,
                PrimaryMetadataProvider = "anidb",
                AniListIds = aniListId > 0 ? new HashSet<int> { aniListId } : new HashSet<int>()
            };
        }

        private static List<Series> OnePieceResults()
        {
            return new List<Series>
            {
                Tvdb("One Piece", 1999, 81797, 21),
                Tvdb("THE ONE PIECE", 0, 464521),
                Tvdb("None Piece", 2011, 287080),
                Tvdb("ONE PIECE (2023)", 2023, 392276),
                AniDb("Toriko", 2011, 8142, 10033),
                AniDb("One Piece", 1999, 69, 21),
                AniDb("The One Piece", 2027, 18325, 171630)
            };
        }

        [Test]
        public void should_not_pick_between_an_anime_and_its_live_action_with_the_same_title()
        {
            var match = SeriesMatcher.Evaluate("[Erai-raws] One Piece - 1089 ~ 1104 [1080p][HEVC][Multiple Subtitle]", OnePieceResults());

            match.Status.Should().Be(LookupMatchStatus.Possible);
            match.Reason.Should().Contain("2 different series share this title");
            match.Scores.Should().HaveCount(match.Results.Count);
            match.StrongCount.Should().Be(2);
            match.Results.Take2Titles().Should().BeEquivalentTo("One Piece", "ONE PIECE (2023)");
        }

        [Test]
        public void should_use_the_year_in_the_folder_to_pick_the_live_action()
        {
            var match = SeriesMatcher.Evaluate("One Piece (2023)", OnePieceResults());

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.Results[0].TvdbId.Should().Be(392276);
        }

        [Test]
        public void should_treat_the_same_show_from_two_providers_as_one()
        {
            var results = new List<Series>
            {
                Tvdb("Bocchi the Rock!", 2022, 1, 130003),
                AniDb("Bocchi the Rock!", 2022, 2, 130003)
            };

            var match = SeriesMatcher.Evaluate("[Judas] Bocchi the Rock! (Season 01) [BD 1080p][HEVC x265 10bit]", results);

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.Results.Should().ContainSingle();
            match.Results[0].TvdbId.Should().Be(1);
        }

        [Test]
        public void should_match_the_single_exact_title_among_similar_ones()
        {
            var results = new List<Series>
            {
                AniDb("Oni Chichi 2 Harvest", 2016, 3),
                AniDb("Oni Chichi", 2014, 1),
                AniDb("Oni Chichi Refresh", 2018, 4)
            };

            var match = SeriesMatcher.Evaluate("[Abysswalker] Oni Chichi [1080p][WEB-DL]", results);

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.Results[0].AniDbId.Should().Be(1);
        }

        [Test]
        public void should_report_none_when_results_are_unrelated()
        {
            var results = new List<Series>
            {
                Tvdb("In Your Radiant Season", 2026, 1),
                Tvdb("Land of the Lustrous", 2017, 2),
                Tvdb("ASTRO Amigo TV", 2018, 3)
            };

            var match = SeriesMatcher.Evaluate("[Erai-raws] Kingdom 3rd Season - 01 ~ 16 [1080p][Multiple Subtitle]", results);

            match.Status.Should().Be(LookupMatchStatus.None);
        }

        [Test]
        public void should_match_season_wording_differences()
        {
            var results = new List<Series>
            {
                AniDb("Kingdom", 2012, 8791),
                AniDb("Kingdom Season 3", 2020, 15259)
            };

            var match = SeriesMatcher.Evaluate("[Erai-raws] Kingdom 3rd Season - 01 ~ 16 [1080p][Multiple Subtitle]", results);

            match.Status.Should().Be(LookupMatchStatus.Matched);
            match.Results[0].AniDbId.Should().Be(15259);
        }

        [Test]
        public void should_treat_spacing_and_hyphen_differences_as_the_same_title()
        {
            var match = SeriesMatcher.Evaluate("[Abysswalker] Oni Chichi Re-birth [1080p][WEB-DL]", new List<Series> { AniDb("Oni Chichi Rebirth", 2015, 9) });

            match.Status.Should().Be(LookupMatchStatus.Matched);
        }

        [Test]
        public void should_not_confuse_a_title_with_the_same_title_prefixed_by_the()
        {
            var match = SeriesMatcher.Evaluate("[Erai-raws] One Piece - 01 ~ 12 [1080p]", new List<Series> { AniDb("The One Piece", 2027, 18325) });

            match.Status.Should().Be(LookupMatchStatus.Possible);
        }

        [Test]
        public void should_not_trust_a_year_it_cannot_verify()
        {
            var match = SeriesMatcher.Evaluate("Some Show (2020)", new List<Series> { Tvdb("Some Show", 0, 1) });

            match.Status.Should().Be(LookupMatchStatus.Possible);
        }

        [Test]
        public void should_reject_an_exact_title_from_the_wrong_year()
        {
            var match = SeriesMatcher.Evaluate("Some Show (2020)", new List<Series> { Tvdb("Some Show", 1985, 1) });

            match.Status.Should().NotBe(LookupMatchStatus.Matched);
        }

        [Test]
        public void should_match_on_a_trailing_english_title()
        {
            var results = new List<Series> { AniDb("Call of the Night", 2022, 17000) };

            var match = SeriesMatcher.Evaluate("[Anime Time] Yofukashi No Uta (Season 01) [BD] [Dual Audio][1080p][Eng Sub] Call Of The Night", results);

            match.Status.Should().Be(LookupMatchStatus.Matched);
        }

        [Test]
        public void should_not_treat_the_start_of_a_longer_title_as_an_exact_match()
        {
            var match = SeriesMatcher.Evaluate("[Anime Time] Cross Ange - Tenshi to Ryuu no Rondo [BD][1080p]", new List<Series> { AniDb("Cross Ange", 2014, 5) });

            match.Status.Should().Be(LookupMatchStatus.Possible);
        }

        [Test]
        public void should_match_a_clean_title_that_contains_a_dash()
        {
            var results = new List<Series> { AniDb("Kimi ni Todoke: From Me to You", 2009, 6) };

            SeriesMatcher.Evaluate("Kimi ni Todoke - From Me to You", results).Status.Should().Be(LookupMatchStatus.Matched);
        }

        [Test]
        public void should_report_none_without_results()
        {
            SeriesMatcher.Evaluate("[Group] Whatever [1080p]", new List<Series>()).Status.Should().Be(LookupMatchStatus.None);
        }
    }

    internal static class MatchTestExtensions
    {
        public static List<string> Take2Titles(this List<Series> results)
        {
            return new List<string> { results[0].Title, results[1].Title };
        }
    }
}
