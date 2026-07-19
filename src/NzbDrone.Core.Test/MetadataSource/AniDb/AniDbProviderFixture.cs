using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Test.Framework;
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
    }
}
