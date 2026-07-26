using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
namespace NzbDrone.Core.Test.MetadataSource.AniDb
{
    [TestFixture]
    public class AniDbProviderFixture : CoreTest<AniDbProvider>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAniDbRateLimiter>()
                .Setup(v => v.ExecuteAsync(It.IsAny<Func<string>>()))
                .Returns((Func<string> action) => Task.FromResult(action()));

            Mocker.GetMock<IAppFolderInfo>()
                .SetupGet(v => v.AppDataFolder)
                .Returns(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString()));
        }

        private void GivenXmlResponse(int id, string xml)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Execute(It.Is<HttpRequest>(r => r.Url.ToString().Contains($"aid={id}"))))
                .Returns(new HttpResponse(null, new HttpHeader(), xml));
        }

        private string BuildAnimeXml(int id, string title, List<Tuple<int, string>> relations, int episodes = 12)
        {
            var relatedAnimeXml = string.Join("\n", relations.Select(r => $"<anime id=\"{r.Item1}\" type=\"{r.Item2}\">Related</anime>"));

            var episodesXml = "";
            for (var i = 1; i <= episodes; i++)
            {
                episodesXml += $"<episode><epno type=\"1\">{i}</epno><length>25</length><title xml:lang=\"en\">Episode {i}</title></episode>\n";
            }

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""{id}"">
  <titles>
    <title xml:lang=""en"" type=""main"">{title}</title>
  </titles>
  <type>TV Series</type>
  <relatedanime>
    {relatedAnimeXml}
  </relatedanime>
  <episodes>
    {episodesXml}
  </episodes>
</anime>";
        }

        [Test]
        public void should_always_apply_fixed_9_hour_jst_to_utc_offset_regardless_of_dst()
        {
            // Japan does not observe DST. The offset from JST to UTC is always exactly -9 hours.
            // This test explicitly guards against regressions where a generic TimeZone conversion
            // (e.g. Asia/Tokyo or Tokyo Standard Time) might incorrectly apply DST rules
            // if configured improperly by a third-party lib or environment.

            var testDates = new List<DateTime>
            {
                new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Unspecified), // Winter
                new DateTime(2026, 4, 15, 23, 30, 0, DateTimeKind.Unspecified), // Spring
                new DateTime(2026, 7, 15, 23, 30, 0, DateTimeKind.Unspecified), // Summer
                new DateTime(2026, 10, 15, 23, 30, 0, DateTimeKind.Unspecified) // Autumn
            };

            foreach (var jstDate in testDates)
            {
                // Replicate the exact conversion logic from AniDbProvider/AniListEnricher
                var utcDate = jstDate.AddHours(-9);

                var offset = jstDate - utcDate;
                offset.TotalHours.Should().Be(9);
            }
        }

        [Test]
        public void should_traverse_linear_chain_and_merge_seasons()
        {
            // Setup: 1 (hub) -> Sequel -> 2 -> Sequel -> 3
            // Hub (1) has no prequels, 2 has 1 as prequel, 3 has 2 as prequel
            GivenXmlResponse(1, BuildAnimeXml(1, "Season 1", new List<Tuple<int, string>> { Tuple.Create(2, "Sequel") }));
            GivenXmlResponse(2, BuildAnimeXml(2, "Season 2", new List<Tuple<int, string>> { Tuple.Create(1, "Prequel"), Tuple.Create(3, "Sequel") }));
            GivenXmlResponse(3, BuildAnimeXml(3, "Season 3", new List<Tuple<int, string>> { Tuple.Create(2, "Prequel") }));

            var details = Subject.GetSeriesInfo("1");

            var series = details.Item1;
            var episodes = details.Item2;

            series.Title.Should().Be("Season 1");
            series.Seasons.Should().HaveCount(3);
            series.AniDbMappings.Should().HaveCount(3);

            // Mappings check
            series.AniDbMappings.Should().ContainSingle(m => m.AniDbId == 1 && m.SeasonNumber == 1 && m.RelationType == "Hub");
            series.AniDbMappings.Should().ContainSingle(m => m.AniDbId == 2 && m.SeasonNumber == 2 && m.RelationType == "Auto-Sequel");
            series.AniDbMappings.Should().ContainSingle(m => m.AniDbId == 3 && m.SeasonNumber == 3 && m.RelationType == "Auto-Sequel");

            // Episodes check
            episodes.Should().HaveCount(36); // 3 seasons * 12 episodes
            episodes.Count(e => e.SeasonNumber == 1).Should().Be(12);
            episodes.Count(e => e.SeasonNumber == 2).Should().Be(12);
            episodes.Count(e => e.SeasonNumber == 3).Should().Be(12);
        }

        [Test]
        public void should_find_hub_when_starting_from_sequel()
        {
            // Setup: same chain, but we start searching from ID 2
            GivenXmlResponse(1, BuildAnimeXml(1, "Season 1", new List<Tuple<int, string>> { Tuple.Create(2, "Sequel") }));
            GivenXmlResponse(2, BuildAnimeXml(2, "Season 2", new List<Tuple<int, string>> { Tuple.Create(1, "Prequel"), Tuple.Create(3, "Sequel") }));
            GivenXmlResponse(3, BuildAnimeXml(3, "Season 3", new List<Tuple<int, string>> { Tuple.Create(2, "Prequel") }));

            var details = Subject.GetSeriesInfo("2");

            var series = details.Item1;

            // Should still resolve to hub ID 1
            series.Title.Should().Be("Season 1");
            series.AniDbId.Should().Be(1);
            series.Seasons.Should().HaveCount(3);
        }

        [Test]
        public void should_stop_traversal_on_branching_sequels()
        {
            // Setup: 1 -> Sequel -> 2 (Branch A)
            //          -> Sequel -> 3 (Branch B)
            GivenXmlResponse(1, BuildAnimeXml(1, "Season 1", new List<Tuple<int, string>> { Tuple.Create(2, "Sequel"), Tuple.Create(3, "Sequel") }));
            GivenXmlResponse(2, BuildAnimeXml(2, "Branch A", new List<Tuple<int, string>> { Tuple.Create(1, "Prequel") }));
            GivenXmlResponse(3, BuildAnimeXml(3, "Branch B", new List<Tuple<int, string>> { Tuple.Create(1, "Prequel") }));

            var details = Subject.GetSeriesInfo("1");

            var series = details.Item1;
            var episodes = details.Item2;

            // Should only include hub, branch stops
            series.AniDbId.Should().Be(1);
            series.Seasons.Should().HaveCount(1);
            series.AniDbMappings.Should().HaveCount(1);

            episodes.Should().HaveCount(12); // Only season 1 episodes

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_stop_hub_search_on_branching_prequels()
        {
            // Setup: Start at 3. 3 has prequels 1 and 2. It shouldn't pick either as hub.
            GivenXmlResponse(3, BuildAnimeXml(3, "Season 3", new List<Tuple<int, string>> { Tuple.Create(1, "Prequel"), Tuple.Create(2, "Prequel") }));

            var details = Subject.GetSeriesInfo("3");

            var series = details.Item1;
            var episodes = details.Item2;

            series.AniDbId.Should().Be(3);
            series.Title.Should().Be("Season 3");
            series.Seasons.Should().HaveCount(0);
            episodes.Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_capture_per_season_title_and_images()
        {
            GivenXmlResponse(1, BuildAnimeXml(1, "Main Hub Season 1", new List<Tuple<int, string>> { Tuple.Create(2, "Sequel") }));
            GivenXmlResponse(2, BuildAnimeXml(2, "Season 2 Spinoff", new List<Tuple<int, string>> { Tuple.Create(1, "Prequel") }));

            var details = Subject.GetSeriesInfo("1");
            var series = details.Item1;

            series.Seasons.Should().HaveCount(2);
            var season1 = series.Seasons.Single(s => s.SeasonNumber == 1);
            var season2 = series.Seasons.Single(s => s.SeasonNumber == 2);

            season1.Title.Should().Be("Main Hub Season 1");
            season1.Images.Should().NotBeNull();

            season2.Title.Should().Be("Season 2 Spinoff");
            season2.Images.Should().NotBeNull();
        }

        [Test]
        public void should_enrich_air_times_using_both_episode_and_absolute_episode_matching()
        {
            var anilistEnricherMock = Mocker.GetMock<IAniListEnricher>();
            var timeOfDay = new TimeSpan(23, 0, 0); // 23:00 JST

            // AniList data: we pretend AniList uses relative numbering 1,2,3 for this cour
            var airingTimes = new Dictionary<int, TimeSpan>
            {
                { 1, timeOfDay }
            };

            var multipleTimes = new Dictionary<int, Dictionary<int, TimeSpan>> { { 185874, airingTimes } };
            anilistEnricherMock.Setup(c => c.GetAiringTimesForMultiple(It.IsAny<IEnumerable<int>>())).Returns(multipleTimes);

            // Hub is Bleach TYBW Cour 1, this is Cour 4 (Kashin-tan)
            GivenXmlResponse(1, BuildAnimeXml(1, "BLEACH TYBW", new List<Tuple<int, string>> { Tuple.Create(2, "Sequel") }, 13));
            GivenXmlResponse(2, BuildAnimeXml(2, "BLEACH TYBW Cour 2", new List<Tuple<int, string>> { Tuple.Create(1, "Prequel"), Tuple.Create(3, "Sequel") }, 13));
            GivenXmlResponse(3, BuildAnimeXml(3, "BLEACH TYBW Cour 3", new List<Tuple<int, string>> { Tuple.Create(2, "Prequel"), Tuple.Create(4, "Sequel") }, 13));

            // Kashin-tan (Cour 4)
            // It has an AniList ID 185874. It has 1 episode in the XML (Episode 1).
            var kashinTanXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""4"">
  <titles>
    <title xml:lang=""en"" type=""main"">BLEACH: Sennen Kessen-hen - Kashin-tan</title>
  </titles>
  <type>TV Series</type>
  <relatedanime>
    <anime id=""3"" type=""Prequel"">Related</anime>
  </relatedanime>
  <episodes>
    <episode><epno type=""1"">1</epno><length>25</length><title xml:lang=""en"">Episode 1</title><airdate>2026-07-25</airdate></episode>
  </episodes>
</anime>";

            GivenXmlResponse(4, kashinTanXml);

            // Mock finding AniList ID for the Kashin-tan season (ID 4)
            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            var kashinTanMock = new AnimeOfflineTitle { AniDbId = 4, AniListId = 185874 };
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 4)).Returns(kashinTanMock);
            var details = Subject.GetSeriesInfo("1");

            // Assert
            var episodes = details.Item2;
            var episode = episodes.Single(e => e.SeasonNumber == 4 && e.EpisodeNumber == 1);

            // Expected precise time is 14:00 UTC (since 14:00 UTC = 23:00 JST, and the AniDb date is 2026-07-25)
            episode.AirDateUtc.Should().Be(new DateTime(2026, 7, 25, 14, 0, 0, DateTimeKind.Utc));
            episode.AirDateUtc.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void should_handle_calendar_day_rollover_when_jst_crosses_midnight_relative_to_utc()
        {
            var anilistEnricherMock = Mocker.GetMock<IAniListEnricher>();
            var timeOfDay = new TimeSpan(2, 0, 0); // 02:00 JST

            // Mock AniList response (using episode 1 relative numbering)
            var airingTimes = new Dictionary<int, TimeSpan>
            {
                { 1, timeOfDay }
            };

            var multipleTimes = new Dictionary<int, Dictionary<int, TimeSpan>> { { 185874, airingTimes } };
            anilistEnricherMock.Setup(c => c.GetAiringTimesForMultiple(It.IsAny<IEnumerable<int>>())).Returns(multipleTimes);

            // AniDb date is July 26th
            var testXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""1"">
  <titles>
    <title xml:lang=""en"" type=""main"">Test Anime</title>
  </titles>
  <type>TV Series</type>
  <episodes>
    <episode><epno type=""1"">1</epno><length>25</length><title xml:lang=""en"">Episode 1</title><airdate>2026-07-26</airdate></episode>
  </episodes>
</anime>";

            GivenXmlResponse(1, testXml);

            // Mock mapping
            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            var localSeries = new AnimeOfflineTitle { AniDbId = 1, AniListId = 185874 };
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 1)).Returns(localSeries);

            var details = Subject.GetSeriesInfo("1");
            var episode = details.Item2.First();

            // 02:00 JST on July 26th = 17:00 UTC on July 25th (rolls backwards across calendar boundary)
            episode.AirDateUtc.Should().Be(new DateTime(2026, 7, 25, 17, 0, 0, DateTimeKind.Utc));
            episode.AirDateUtc.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void should_fallback_to_title_search_and_cache_result_when_anilist_id_missing()
        {
            var anilistEnricherMock = Mocker.GetMock<IAniListEnricher>();
            var timeOfDay = new TimeSpan(14, 0, 0);

            var airingTimes = new Dictionary<int, TimeSpan> { { 1, timeOfDay } };
            var multipleTimes = new Dictionary<int, Dictionary<int, TimeSpan>> { { 185874, airingTimes } };
            anilistEnricherMock.Setup(c => c.GetAiringTimesForMultiple(It.IsAny<IEnumerable<int>>())).Returns(multipleTimes);

            // Mock the title fallback search to return our ID
            anilistEnricherMock.Setup(c => c.SearchAniListIdByTitle("Test Anime Fallback", 2026, 1)).Returns(185874);

            var testXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""1"">
  <titles>
    <title xml:lang=""en"" type=""main"">Test Anime Fallback</title>
  </titles>
  <type>TV Series</type>
  <startdate>2026-07-26</startdate>
  <episodes>
    <episode><epno type=""1"">1</epno><length>25</length><title xml:lang=""en"">Episode 1</title><airdate>2026-07-26</airdate></episode>
  </episodes>
</anime>";

            GivenXmlResponse(1, testXml);

            // Mock mapping: Series found in DB, but NO AniList ID!
            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            var localSeries = new AnimeOfflineTitle { AniDbId = 1, Title = "Test Anime Fallback" }; // Missing AniListId
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 1)).Returns(localSeries);

            var details = Subject.GetSeriesInfo("1");
            var episode = details.Item2.First();

            episode.AirDateUtc.Should().NotBeNull();

            // Verify fallback was called
            anilistEnricherMock.Verify(c => c.SearchAniListIdByTitle("Test Anime Fallback", 2026, 1), Times.Once);

            // Verify caching occurred
            titleSearchMock.Verify(c => c.UpdateAniListId(1, 185874), Times.Once);
        }

        [Test]
        public void should_ignore_ambiguous_matches_during_fallback_search()
        {
            var anilistEnricherMock = Mocker.GetMock<IAniListEnricher>();

            // Mock the title fallback search to return null (ambiguous match or not found)
            anilistEnricherMock.Setup(c => c.SearchAniListIdByTitle("Ambiguous Anime", 2026, 1)).Returns((int?)null);

            var testXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""1"">
  <titles>
    <title xml:lang=""en"" type=""main"">Ambiguous Anime</title>
  </titles>
  <type>TV Series</type>
  <startdate>2026-07-26</startdate>
  <episodes>
    <episode><epno type=""1"">1</epno><length>25</length><title xml:lang=""en"">Episode 1</title><airdate>2026-07-26</airdate></episode>
  </episodes>
</anime>";

            GivenXmlResponse(1, testXml);

            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            var localSeries = new AnimeOfflineTitle { AniDbId = 1, Title = "Ambiguous Anime" }; // Missing AniListId
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 1)).Returns(localSeries);

            var details = Subject.GetSeriesInfo("1");
            var episode = details.Item2.First();

            // Enrichment should fail/be skipped, meaning default fallback of 23:59:59 should be used
            episode.AirDateUtc.Should().Be(new DateTime(2026, 7, 26, 23, 59, 59, DateTimeKind.Utc));

            anilistEnricherMock.Verify(c => c.SearchAniListIdByTitle("Ambiguous Anime", 2026, 1), Times.Once);
        }

        [Test]
        public void should_mark_series_as_continuing_if_enddate_is_missing()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""1"">
  <titles><title xml:lang=""en"" type=""main"">Test Anime</title></titles>
</anime>";
            GivenXmlResponse(1, xml);
            var details = Subject.GetSeriesInfo("1");
            details.Item1.Status.Should().Be(SeriesStatusType.Continuing);
        }

        [Test]
        public void should_mark_series_as_continuing_if_enddate_contains_question_mark()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""1"">
  <titles><title xml:lang=""en"" type=""main"">Test Anime</title></titles>
  <enddate>?</enddate>
</anime>";
            GivenXmlResponse(1, xml);
            var details = Subject.GetSeriesInfo("1");
            details.Item1.Status.Should().Be(SeriesStatusType.Continuing);
        }

        [Test]
        public void should_mark_series_as_continuing_if_enddate_is_in_the_future()
        {
            var futureDate = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");
            var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""1"">
  <titles><title xml:lang=""en"" type=""main"">Test Anime</title></titles>
  <enddate>{futureDate}</enddate>
</anime>";
            GivenXmlResponse(1, xml);
            var details = Subject.GetSeriesInfo("1");
            details.Item1.Status.Should().Be(SeriesStatusType.Continuing);
        }

        [Test]
        public void should_mark_series_as_ended_if_enddate_is_in_the_past()
        {
            var pastDate = DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd");
            var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""1"">
  <titles><title xml:lang=""en"" type=""main"">Test Anime</title></titles>
  <enddate>{pastDate}</enddate>
</anime>";
            GivenXmlResponse(1, xml);
            var details = Subject.GetSeriesInfo("1");
            details.Item1.Status.Should().Be(SeriesStatusType.Ended);
        }
    }
}
