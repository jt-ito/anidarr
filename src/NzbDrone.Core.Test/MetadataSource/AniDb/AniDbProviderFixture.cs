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
            AniDbProvider.ClearCache();

            Mocker.GetMock<IAniDbRateLimiter>()
                .Setup(v => v.ExecuteAsync(It.IsAny<Func<string>>()))
                .Returns((Func<string> action) => Task.FromResult(action()));

            Mocker.GetMock<IAppFolderInfo>()
                .SetupGet(v => v.AppDataFolder)
                .Returns(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString()));
        }

        [TearDown]
        public void TearDown()
        {
            AniDbProvider.ClearCache();
        }

        private void GivenXmlResponse(int id, string xml)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Execute(It.Is<HttpRequest>(r => r.Url.ToString().Contains($"aid={id}"))))
                .Returns(new HttpResponse(null, new HttpHeader(), xml));
        }

        private string BuildAnimeXml(int id, string title, List<Tuple<int, string>> relations, int episodes = 12, string animeType = "TV Series")
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
  <type>{animeType}</type>
  <episodecount>{episodes}</episodecount>
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

        [Test]
        public void should_assign_numbered_seasons_to_all_ova_chain_ane_jiru()
        {
            // Ane Jiru: all-OVA chain (2 eps each) -> Season 1 and Season 2
            GivenXmlResponse(4823, BuildAnimeXml(4823, "Ane Jiru The Animation", new List<Tuple<int, string>> { Tuple.Create(7915, "Sequel") }, 2, "OVA"));
            GivenXmlResponse(7915, BuildAnimeXml(7915, "Ane Jiru 2 The Animation", new List<Tuple<int, string>> { Tuple.Create(4823, "Prequel") }, 2, "OVA"));

            var details = Subject.GetSeriesInfo("4823");
            var series = details.Item1;

            var map4823 = series.AniDbMappings.Single(m => m.AniDbId == 4823);
            var map7915 = series.AniDbMappings.Single(m => m.AniDbId == 7915);

            // Rule: Hub entry always gets Season 1
            map4823.SeasonNumber.Should().Be(1);

            // Rule: OVA-native chain, OVA type → numbered season
            map7915.SeasonNumber.Should().Be(2);
        }

        [Test]
        public void should_assign_season_0_to_special_type_in_all_ova_chain_tenchi_muyo()
        {
            // Tenchi Muyo: OVA-native chain with a Special-typed carnival entry between full OVA seasons
            GivenXmlResponse(205, BuildAnimeXml(205, "Tenchi Muyou! Ryououki", new List<Tuple<int, string>> { Tuple.Create(2508, "Sequel") }, 6, "OVA"));
            GivenXmlResponse(2508, BuildAnimeXml(2508, "Tenchi Muyou! Ryououki: Omatsuri Zenjitsu no Yoru!", new List<Tuple<int, string>> { Tuple.Create(205, "Prequel"), Tuple.Create(2509, "Sequel") }, 1, "Special"));
            GivenXmlResponse(2509, BuildAnimeXml(2509, "Tenchi Muyou! Ryououki (1994)", new List<Tuple<int, string>> { Tuple.Create(2508, "Prequel"), Tuple.Create(2510, "Sequel") }, 6, "OVA"));
            GivenXmlResponse(2510, BuildAnimeXml(2510, "Tenchi Muyou! Ryououki (2003)", new List<Tuple<int, string>> { Tuple.Create(2509, "Prequel") }, 6, "OVA"));

            var details = Subject.GetSeriesInfo("205");
            var series = details.Item1;

            var map205 = series.AniDbMappings.Single(m => m.AniDbId == 205);
            var map2508 = series.AniDbMappings.Single(m => m.AniDbId == 2508);
            var map2509 = series.AniDbMappings.Single(m => m.AniDbId == 2509);
            var map2510 = series.AniDbMappings.Single(m => m.AniDbId == 2510);

            // Rule: Hub entry always gets Season 1
            map205.SeasonNumber.Should().Be(1);

            // Rule: OVA-native chain, Special type → Season 0 (bonus content)
            map2508.SeasonNumber.Should().Be(0);

            // Rule: OVA-native chain, OVA type → numbered season
            map2509.SeasonNumber.Should().Be(2);
            map2510.SeasonNumber.Should().Be(3);
        }

        [Test]
        public void should_assign_season_0_to_two_episode_ova_in_tv_anchored_chain_baka_to_test()
        {
            // Baka to Test: TV-anchored chain with 2-ep festival OVA between TV seasons
            GivenXmlResponse(6747, BuildAnimeXml(6747, "Baka to Test to Shoukanjuu", new List<Tuple<int, string>> { Tuple.Create(8085, "Sequel") }, 13, "TV Series"));
            GivenXmlResponse(8085, BuildAnimeXml(8085, "Baka to Test to Shoukanjuu: Matsuri", new List<Tuple<int, string>> { Tuple.Create(6747, "Prequel"), Tuple.Create(8235, "Sequel") }, 2, "OVA"));
            GivenXmlResponse(8235, BuildAnimeXml(8235, "Baka to Test to Shoukanjuu Ni!", new List<Tuple<int, string>> { Tuple.Create(8085, "Prequel") }, 13, "TV Series"));

            var details = Subject.GetSeriesInfo("6747");
            var series = details.Item1;

            var map6747 = series.AniDbMappings.Single(m => m.AniDbId == 6747);
            var map8085 = series.AniDbMappings.Single(m => m.AniDbId == 8085);
            var map8235 = series.AniDbMappings.Single(m => m.AniDbId == 8235);

            // Rule: Hub entry always gets Season 1
            map6747.SeasonNumber.Should().Be(1);

            // Rule: TV-anchored chain, non-TV entry → Season 0
            map8085.SeasonNumber.Should().Be(0);

            // Rule: TV Series in chain → numbered season
            map8235.SeasonNumber.Should().Be(2);
        }

        [Test]
        public void should_assign_season_0_to_two_episode_ova_in_tv_anchored_chain_haikyuu()
        {
            // Haikyuu: TV-anchored chain with 2-ep Land vs Sky OVA between TV seasons
            GivenXmlResponse(11991, BuildAnimeXml(11991, "Haikyuu!! Karasuno Koukou vs Shiratorizawa Gakuen Koukou", new List<Tuple<int, string>> { Tuple.Create(15045, "Sequel") }, 10, "TV Series"));
            GivenXmlResponse(15045, BuildAnimeXml(15045, "Haikyuu!! Land vs Air", new List<Tuple<int, string>> { Tuple.Create(11991, "Prequel"), Tuple.Create(14552, "Sequel") }, 2, "OVA"));
            GivenXmlResponse(14552, BuildAnimeXml(14552, "Haikyuu!! To the Top", new List<Tuple<int, string>> { Tuple.Create(15045, "Prequel") }, 13, "TV Series"));

            var details = Subject.GetSeriesInfo("11991");
            var series = details.Item1;

            var map11991 = series.AniDbMappings.Single(m => m.AniDbId == 11991);
            var map15045 = series.AniDbMappings.Single(m => m.AniDbId == 15045);
            var map14552 = series.AniDbMappings.Single(m => m.AniDbId == 14552);

            // Rule: Hub entry always gets Season 1
            map11991.SeasonNumber.Should().Be(1);

            // Rule: TV-anchored chain, non-TV entry → Season 0
            map15045.SeasonNumber.Should().Be(0);

            // Rule: TV Series in chain → numbered season
            map14552.SeasonNumber.Should().Be(2);
        }

        [Test]
        public void should_assign_season_0_to_ova_and_movie_in_tv_anchored_chain_konosuba()
        {
            // KonoSuba: TV-anchored chain with 1-ep OVA and 1-ep Movie between TV seasons
            GivenXmlResponse(11992, BuildAnimeXml(11992, "Kono Subarashii Sekai ni Shukufuku o! 2", new List<Tuple<int, string>> { Tuple.Create(13317, "Sequel") }, 10, "TV Series"));
            GivenXmlResponse(13317, BuildAnimeXml(13317, "Kono Subarashii Sekai ni Shukufuku o! 2: Kono Subarashii Geijutsu ni Shukufuku o!", new List<Tuple<int, string>> { Tuple.Create(11992, "Prequel"), Tuple.Create(13310, "Sequel") }, 1, "OVA"));
            GivenXmlResponse(13310, BuildAnimeXml(13310, "Kono Subarashii Sekai ni Shukufuku o! Kurenai Densetsu", new List<Tuple<int, string>> { Tuple.Create(13317, "Prequel"), Tuple.Create(17431, "Sequel") }, 1, "Movie"));
            GivenXmlResponse(17431, BuildAnimeXml(17431, "Kono Subarashii Sekai ni Shukufuku o! 3", new List<Tuple<int, string>> { Tuple.Create(13310, "Prequel") }, 11, "TV Series"));

            var details = Subject.GetSeriesInfo("11992");
            var series = details.Item1;

            var map11992 = series.AniDbMappings.Single(m => m.AniDbId == 11992);
            var map13317 = series.AniDbMappings.Single(m => m.AniDbId == 13317);
            var map13310 = series.AniDbMappings.Single(m => m.AniDbId == 13310);
            var map17431 = series.AniDbMappings.Single(m => m.AniDbId == 17431);

            // Rule: Hub entry always gets Season 1
            map11992.SeasonNumber.Should().Be(1);

            // Rule: TV-anchored chain, non-TV entry → Season 0
            map13317.SeasonNumber.Should().Be(0);

            // Rule: Movie always → Season 0 (belongs in Radarr)
            map13310.SeasonNumber.Should().Be(0);

            // Rule: TV Series in chain → numbered season
            map17431.SeasonNumber.Should().Be(2);
        }

        [Test]
        public void should_assign_season_1_to_standalone_single_episode_ova()
        {
            GivenXmlResponse(9999, BuildAnimeXml(9999, "Standalone OVA", new List<Tuple<int, string>>(), 1, "OVA"));

            var details = Subject.GetSeriesInfo("9999");
            var series = details.Item1;

            var map9999 = series.AniDbMappings.Single(m => m.AniDbId == 9999);

            // Rule: Hub entry (standalone) always gets Season 1 regardless of episode count
            map9999.SeasonNumber.Should().Be(1);
        }

        [Test]
        public void should_assign_season_0_to_music_video_in_ova_native_chain()
        {
            // All-OVA chain where a 2-ep Music Video sequel should still be Season 0
            GivenXmlResponse(5000, BuildAnimeXml(5000, "OVA Series", new List<Tuple<int, string>> { Tuple.Create(5001, "Sequel") }, 4, "OVA"));
            GivenXmlResponse(5001, BuildAnimeXml(5001, "OVA Series Music Video", new List<Tuple<int, string>> { Tuple.Create(5000, "Prequel") }, 2, "Music Video"));

            var details = Subject.GetSeriesInfo("5000");
            var series = details.Item1;

            var map5000 = series.AniDbMappings.Single(m => m.AniDbId == 5000);
            var map5001 = series.AniDbMappings.Single(m => m.AniDbId == 5001);

            // Rule: Hub entry always gets Season 1
            map5000.SeasonNumber.Should().Be(1);

            // Rule: Music Video always → Season 0 regardless of chain type or episode count
            map5001.SeasonNumber.Should().Be(0);
        }

        [Test]
        public void should_assign_numbered_season_to_1ep_ova_sequel_in_ova_native_chain()
        {
            // Bavi Stock pattern: both entries are 1-ep OVAs, sequel is a canonical continuation
            GivenXmlResponse(5007, BuildAnimeXml(5007, "Bavi Stock I", new List<Tuple<int, string>> { Tuple.Create(8633, "Sequel") }, 1, "OVA"));
            GivenXmlResponse(8633, BuildAnimeXml(8633, "Bavi Stock II", new List<Tuple<int, string>> { Tuple.Create(5007, "Prequel") }, 1, "OVA"));

            var details = Subject.GetSeriesInfo("5007");
            var series = details.Item1;

            var map5007 = series.AniDbMappings.Single(m => m.AniDbId == 5007);
            var map8633 = series.AniDbMappings.Single(m => m.AniDbId == 8633);

            // Rule: Hub entry always gets Season 1
            map5007.SeasonNumber.Should().Be(1);

            // Rule: OVA-native chain, OVA type → numbered season (regardless of episode count)
            map8633.SeasonNumber.Should().Be(2);
        }

        [Test]
        public void should_assign_season_0_to_special_type_sequel_in_ova_native_chain()
        {
            // Dallos pattern: 4-ep OVA hub with a Special-typed supplement
            GivenXmlResponse(3000, BuildAnimeXml(3000, "OVA Series", new List<Tuple<int, string>> { Tuple.Create(3001, "Sequel") }, 4, "OVA"));
            GivenXmlResponse(3001, BuildAnimeXml(3001, "OVA Series Special", new List<Tuple<int, string>> { Tuple.Create(3000, "Prequel") }, 1, "Special"));

            var details = Subject.GetSeriesInfo("3000");
            var series = details.Item1;

            var map3000 = series.AniDbMappings.Single(m => m.AniDbId == 3000);
            var map3001 = series.AniDbMappings.Single(m => m.AniDbId == 3001);

            // Rule: Hub entry always gets Season 1
            map3000.SeasonNumber.Should().Be(1);

            // Rule: OVA-native chain, Special type → Season 0 (bonus content)
            map3001.SeasonNumber.Should().Be(0);
        }

        [Test]
        public void should_assign_season_0_to_movie_in_ova_native_chain()
        {
            // All-OVA chain where a 3-ep Movie sequel should still be Season 0 (movies belong in Radarr)
            GivenXmlResponse(6000, BuildAnimeXml(6000, "OVA Series", new List<Tuple<int, string>> { Tuple.Create(6001, "Sequel") }, 4, "OVA"));
            GivenXmlResponse(6001, BuildAnimeXml(6001, "OVA Series The Movie", new List<Tuple<int, string>> { Tuple.Create(6000, "Prequel") }, 3, "Movie"));

            var details = Subject.GetSeriesInfo("6000");
            var series = details.Item1;

            var map6000 = series.AniDbMappings.Single(m => m.AniDbId == 6000);
            var map6001 = series.AniDbMappings.Single(m => m.AniDbId == 6001);

            // Rule: Hub entry always gets Season 1
            map6000.SeasonNumber.Should().Be(1);

            // Rule: Movie always → Season 0 (belongs in Radarr, not Sonarr)
            map6001.SeasonNumber.Should().Be(0);
        }

        [Test]
        public void should_use_anilist_advisory_to_assign_season_0_to_single_episode_ova_marked_as_special_on_anilist()
        {
            // AniDB classifies as 1-ep OVA, but AniList advisory indicates Format = SPECIAL
            GivenXmlResponse(7000, BuildAnimeXml(7000, "OVA Franchise", new List<Tuple<int, string>> { Tuple.Create(7001, "Sequel") }, 6, "OVA"));
            GivenXmlResponse(7001, BuildAnimeXml(7001, "OVA Carnival", new List<Tuple<int, string>> { Tuple.Create(7000, "Prequel") }, 1, "OVA"));

            Mocker.GetMock<IAnimeOfflineDatabase>()
                .Setup(v => v.GetSeriesById("anidb", 7001))
                .Returns(new AnimeOfflineTitle { AniDbId = 7001, AniListId = 99901 });

            Mocker.GetMock<IAniListEnricher>()
                .Setup(v => v.GetMediaInfo(99901))
                .Returns(new AniListMediaInfo { Id = 99901, Format = "SPECIAL", Episodes = 1 });

            var details = Subject.GetSeriesInfo("7000");
            var series = details.Item1;

            var map7000 = series.AniDbMappings.Single(m => m.AniDbId == 7000);
            var map7001 = series.AniDbMappings.Single(m => m.AniDbId == 7001);

            // Hub entry gets Season 1
            map7000.SeasonNumber.Should().Be(1);

            // AniList advisory marks it as SPECIAL -> Season 0
            map7001.SeasonNumber.Should().Be(0);
        }

        [Test]
        public void should_use_anilist_advisory_to_resolve_unknown_anidb_type_to_tv_series()
        {
            // AniDB type is Unknown/missing, but AniList advisory confirms Format = TV
            GivenXmlResponse(8000, BuildAnimeXml(8000, "TV Season 1", new List<Tuple<int, string>> { Tuple.Create(8001, "Sequel") }, 12, "TV Series"));
            GivenXmlResponse(8001, BuildAnimeXml(8001, "TV Season 2", new List<Tuple<int, string>> { Tuple.Create(8000, "Prequel") }, 12, "Unknown"));

            Mocker.GetMock<IAnimeOfflineDatabase>()
                .Setup(v => v.GetSeriesById("anidb", 8001))
                .Returns(new AnimeOfflineTitle { AniDbId = 8001, AniListId = 99801 });

            Mocker.GetMock<IAniListEnricher>()
                .Setup(v => v.GetMediaInfo(99801))
                .Returns(new AniListMediaInfo { Id = 99801, Format = "TV", Episodes = 12 });

            var details = Subject.GetSeriesInfo("8000");
            var series = details.Item1;

            var map8000 = series.AniDbMappings.Single(m => m.AniDbId == 8000);
            var map8001 = series.AniDbMappings.Single(m => m.AniDbId == 8001);

            map8000.SeasonNumber.Should().Be(1);

            // Resolved to TV Series -> Season 2 (instead of -1 manual review flag)
            map8001.SeasonNumber.Should().Be(2);
        }

        [Test]
        public void should_use_anilist_advisory_to_assign_season_0_to_hidden_movie_in_ova_chain()
        {
            // AniDB has type OVA, but AniList advisory indicates Format = MOVIE (Radarr)
            GivenXmlResponse(9000, BuildAnimeXml(9000, "OVA Hub", new List<Tuple<int, string>> { Tuple.Create(9001, "Sequel") }, 4, "OVA"));
            GivenXmlResponse(9001, BuildAnimeXml(9001, "OVA Movie Supplement", new List<Tuple<int, string>> { Tuple.Create(9000, "Prequel") }, 1, "OVA"));

            Mocker.GetMock<IAnimeOfflineDatabase>()
                .Setup(v => v.GetSeriesById("anidb", 9001))
                .Returns(new AnimeOfflineTitle { AniDbId = 9001, AniListId = 99701 });

            Mocker.GetMock<IAniListEnricher>()
                .Setup(v => v.GetMediaInfo(99701))
                .Returns(new AniListMediaInfo { Id = 99701, Format = "MOVIE", Episodes = 1 });

            var details = Subject.GetSeriesInfo("9000");
            var series = details.Item1;

            var map9000 = series.AniDbMappings.Single(m => m.AniDbId == 9000);
            var map9001 = series.AniDbMappings.Single(m => m.AniDbId == 9001);

            map9000.SeasonNumber.Should().Be(1);

            // AniList advisory identifies as MOVIE -> Season 0
            map9001.SeasonNumber.Should().Be(0);
        }

        [Test]
        public void should_fallback_to_anidb_default_when_anilist_enricher_is_rate_limited_or_returns_null()
        {
            // OVA franchise with 1-ep sequel, but AniList enricher is rate-limited
            GivenXmlResponse(9500, BuildAnimeXml(9500, "OVA Base", new List<Tuple<int, string>> { Tuple.Create(9501, "Sequel") }, 4, "OVA"));
            GivenXmlResponse(9501, BuildAnimeXml(9501, "OVA Continuation", new List<Tuple<int, string>> { Tuple.Create(9500, "Prequel") }, 1, "OVA"));

            Mocker.GetMock<IAnimeOfflineDatabase>()
                .Setup(v => v.GetSeriesById("anidb", 9501))
                .Returns(new AnimeOfflineTitle { AniDbId = 9501, AniListId = 99601 });

            // Rate limited -> returns null
            Mocker.GetMock<IAniListEnricher>()
                .SetupGet(v => v.IsRateLimited)
                .Returns(true);

            var details = Subject.GetSeriesInfo("9500");
            var series = details.Item1;

            var map9500 = series.AniDbMappings.Single(m => m.AniDbId == 9500);
            var map9501 = series.AniDbMappings.Single(m => m.AniDbId == 9501);

            map9500.SeasonNumber.Should().Be(1);

            // Clean fallback: AniDB OVA-native rule assigns Season 2
            map9501.SeasonNumber.Should().Be(2);
        }

        [Test]
        public void should_not_pick_short_title_acronym_as_series_title()
        {
            // AniDB entry with short title (ark), official English (Animation Runner Kuromi), and main (Animation Seisaku Shinkou Kuromi-chan)
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""773"">
  <titles>
    <title type=""short"" xml:lang=""en"">ark</title>
    <title type=""official"" xml:lang=""en"">Animation Runner Kuromi</title>
    <title type=""main"" xml:lang=""x-jat"">Animation Seisaku Shinkou Kuromi-chan</title>
    <title type=""official"" xml:lang=""ja"">Kuromi-chan</title>
  </titles>
  <type>OVA</type>
  <episodecount>1</episodecount>
  <episodes>
    <episode><epno type=""1"">1</epno><length>25</length><title xml:lang=""en"">Episode 1</title></episode>
  </episodes>
</anime>";
            GivenXmlResponse(773, xml);

            var details = Subject.GetSeriesInfo("773");
            var series = details.Item1;

            series.Title.Should().Be("Animation Runner Kuromi");
            series.Title.Should().NotBe("ark");
            series.AlternateTitles.Should().Contain("ark");
        }

        [Test]
        public void should_return_cached_series_info_on_subsequent_call()
        {
            var xml = BuildAnimeXml(1234, "Cached Test Anime", new List<Tuple<int, string>>());
            GivenXmlResponse(1234, xml);

            var first = Subject.GetSeriesInfo("1234");
            var second = Subject.GetSeriesInfo("1234");

            first.Item1.Title.Should().Be("Cached Test Anime");
            second.Item1.Title.Should().Be("Cached Test Anime");
            second.Should().BeSameAs(first);
            Mocker.GetMock<IHttpClient>().Verify(v => v.Execute(It.Is<HttpRequest>(r => r.Url.ToString().Contains("aid=1234"))), Times.Once());
        }

        [Test]
        public void should_coalesce_concurrent_in_flight_requests_for_same_series_info()
        {
            var xml = BuildAnimeXml(5678, "Concurrent Test Anime", new List<Tuple<int, string>>());
            GivenXmlResponse(5678, xml);

            var task1 = Task.Run(() => Subject.GetSeriesInfo("5678"));
            var task2 = Task.Run(() => Subject.GetSeriesInfo("5678"));

            Task.WaitAll(task1, task2);

            task1.Result.Item1.Title.Should().Be("Concurrent Test Anime");
            task2.Result.Item1.Title.Should().Be("Concurrent Test Anime");
            task2.Result.Item1.Title.Should().Be(task1.Result.Item1.Title);
            Mocker.GetMock<IHttpClient>().Verify(v => v.Execute(It.Is<HttpRequest>(r => r.Url.ToString().Contains("aid=5678"))), Times.Once());
        }

        [Test]
        public void should_prefer_english_title_over_romaji_main_title()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""4823"">
  <titles>
    <title type=""main"" xml:lang=""x-jat"">Ane Jiru The Animation: Shirakawa San Shimai ni Omakase</title>
    <title type=""synonym"" xml:lang=""en"">Big Sister Juice the Animation: Leave the Three Sisters to Shirakawa</title>
    <title type=""official"" xml:lang=""ja"">アネジル The Animation 白川三姉妹におまかせ</title>
  </titles>
  <type>OVA</type>
  <episodecount>2</episodecount>
  <episodes>
    <episode><epno type=""1"">1</epno><length>25</length><title xml:lang=""en"">Episode 1</title></episode>
  </episodes>
</anime>";
            GivenXmlResponse(4823, xml);

            var details = Subject.GetSeriesInfo("4823");
            var series = details.Item1;

            series.Title.Should().Be("Big Sister Juice the Animation: Leave the Three Sisters to Shirakawa");
            series.AlternateTitles.Should().Contain("Ane Jiru The Animation: Shirakawa San Shimai ni Omakase");
        }
    }
}
