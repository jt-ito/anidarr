using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class FetchAniDbRelatedSeriesServiceFixture : CoreTest<FetchAniDbRelatedSeriesService>
    {
        private Series _series;
        private List<AniDbRelatedSeries> _relatedSeriesCache;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                                     .With(s => s.PrimaryMetadataProvider = "anidb")
                                     .With(s => s.AniDbId = 1) // Base series ID
                                     .Build();

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(_series.Id))
                  .Returns(_series);

            Mocker.GetMock<IAniDbSeriesMappingService>()
                  .Setup(s => s.GetMappingsForSeries(_series.Id))
                  .Returns(new List<AniDbSeriesMapping> { new AniDbSeriesMapping { AniDbId = 1 } });

            _relatedSeriesCache = new List<AniDbRelatedSeries>();
            Mocker.GetMock<IAniDbRelatedSeriesService>()
                  .Setup(s => s.GetRelatedSeries(_series.Id))
                  .Returns(_relatedSeriesCache);
            Mocker.GetMock<IAniDbRelatedSeriesService>()
                  .Setup(s => s.UpdateRelatedSeries(_series.Id, It.IsAny<List<AniDbRelatedSeries>>()))
                  .Callback<int, List<AniDbRelatedSeries>>((id, list) => _relatedSeriesCache = list);

            Mocker.GetMock<IConfigFileProvider>()
                  .Setup(s => s.IsRelatedSeriesEnabled)
                  .Returns(true);
            Mocker.GetMock<IConfigFileProvider>()
                  .Setup(s => s.AniDbClientName)
                  .Returns("testclient");
            Mocker.GetMock<IConfigFileProvider>()
                  .Setup(s => s.AniDbClientVersion)
                  .Returns(1);

            Mocker.GetMock<IAppFolderInfo>()
                  .SetupGet(v => v.AppDataFolder)
                  .Returns(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

            Mocker.GetMock<IAniDbRateLimiter>()
                  .Setup(v => v.ExecuteAsync(It.IsAny<Func<string>>()))
                  .Returns((Func<string> action) => Task.FromResult(action()));

            // Setup cache repository to not throw
            Mocker.GetMock<IAniDbRelatedMetadataCacheRepository>()
                  .Setup(c => c.GetByAniDbId(It.IsAny<int>()))
                  .Returns((AniDbRelatedMetadataCache)null);
        }

        private void GivenXmlResponse(int id, string xml)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Execute(It.Is<HttpRequest>(r => r.Url.ToString().Contains($"aid={id}"))))
                .Returns(new HttpResponse(null, new HttpHeader(), xml));
        }

        private string BuildAnimeXml(int id, string title, List<Tuple<int, string>> relations)
        {
            var relatedAnimeXml = string.Join("\n", relations.Select(r => $"<anime id=\"{r.Item1}\" type=\"{r.Item2}\">Related</anime>"));
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""{id}"">
  <titles><title xml:lang=""en"" type=""main"">{title}</title></titles>
  <type>TV Series</type>
  <relatedanime>{relatedAnimeXml}</relatedanime>
  <episodes></episodes>
</anime>";
        }

        [Test]
        public void should_fetch_imouto_paradise_multi_hop()
        {
            // Item 2: Imouto Paradise! (1) -> Imouto Paradise! 2 (2) -> Imouto Paradise! 3 (3)
            // They are related via "Other" or "Sequel" relation type.
            GivenXmlResponse(1, BuildAnimeXml(1, "Imouto Paradise!", new List<Tuple<int, string>> { Tuple.Create(2, "Other") }));
            GivenXmlResponse(2, BuildAnimeXml(2, "Imouto Paradise! 2", new List<Tuple<int, string>> { Tuple.Create(1, "Other"), Tuple.Create(3, "Other") }));
            GivenXmlResponse(3, BuildAnimeXml(3, "Imouto Paradise! 3", new List<Tuple<int, string>> { Tuple.Create(2, "Other") }));

            // Simulate the existing DB row for the immediate relation (because in real usage, UpdateRelatedSeries triggers this command,
            // meaning the immediate relations are already known)
            _relatedSeriesCache.Add(new AniDbRelatedSeries { SeriesId = _series.Id, RelatedAniDbId = 2, RelationType = "Other" });

            Subject.Execute(new FetchAniDbRelatedSeriesCommand(_series.Id));

            Assert.That(_relatedSeriesCache, Has.Count.EqualTo(2));
            Assert.That(_relatedSeriesCache.Any(r => r.RelatedAniDbId == 2), Is.True);
            Assert.That(_relatedSeriesCache.Any(r => r.RelatedAniDbId == 3), Is.True, "Failed to traverse to 3");
            Console.WriteLine("Test 2 Pass: Found relations for Imouto Paradise!: " + string.Join(", ", _relatedSeriesCache.Select(r => r.RelatedAniDbId)));
        }

        [Test]
        public void should_respect_depth_cap()
        {
            // Item 1: Depth cap enforcement
            // We will create a chain of 13 items. 1 -> 2 -> 3 -> ... -> 13.
            // The depth cap is 10.
            _relatedSeriesCache.Add(new AniDbRelatedSeries { SeriesId = _series.Id, RelatedAniDbId = 2, RelationType = "Sequel" });

            for (var i = 1; i <= 13; i++)
            {
                var relations = new List<Tuple<int, string>>();
                if (i < 13)
                {
                    relations.Add(Tuple.Create(i + 1, "Sequel"));
                }

                if (i > 1)
                {
                    relations.Add(Tuple.Create(i - 1, "Prequel"));
                }

                GivenXmlResponse(i, BuildAnimeXml(i, $"Series {i}", relations));
            }

            Subject.Execute(new FetchAniDbRelatedSeriesCommand(_series.Id));

            // Depth 1: Node 2 (enqueued initially)
            // Depth 2: Node 3
            // ...
            // Depth 10: Node 11
            // Depth 11: Node 12 (enqueued, but skipped during Dequeue)

            // Expected count: nodes 2 through 12 should be in cache = 11 items. Node 12 is discovered, but its relations (13) are never fetched.
            Assert.That(_relatedSeriesCache, Has.Count.EqualTo(11));
            Assert.That(_relatedSeriesCache.Any(r => r.RelatedAniDbId == 13), Is.False, "Exceeded depth cap of 10 hops by discovering depth 12 node");
            Console.WriteLine("Test 1a Pass: Enforced depth cap. Total related series found: " + _relatedSeriesCache.Count);
        }

        [Test]
        public void should_handle_circular_dependencies()
        {
            // Item 1: Circular dependency A -> B -> C -> A
            _relatedSeriesCache.Add(new AniDbRelatedSeries { SeriesId = _series.Id, RelatedAniDbId = 2, RelationType = "Sequel" });

            GivenXmlResponse(1, BuildAnimeXml(1, "A", new List<Tuple<int, string>> { Tuple.Create(2, "Sequel") }));
            GivenXmlResponse(2, BuildAnimeXml(2, "B", new List<Tuple<int, string>> { Tuple.Create(3, "Sequel") }));
            GivenXmlResponse(3, BuildAnimeXml(3, "C", new List<Tuple<int, string>> { Tuple.Create(1, "Sequel") })); // Cycle back to A (1)

            Subject.Execute(new FetchAniDbRelatedSeriesCommand(_series.Id));

            Assert.That(_relatedSeriesCache, Has.Count.EqualTo(2)); // B(2) and C(3)
            Console.WriteLine("Test 1b Pass: Handled circular dependency without infinite loop.");
        }

        [Test]
        public void should_abort_when_toggled_off_mid_flight()
        {
            // Item 3: Test mid-flight setting toggle disable behavior
            _relatedSeriesCache.Add(new AniDbRelatedSeries { SeriesId = _series.Id, RelatedAniDbId = 2, RelationType = "Sequel" });

            GivenXmlResponse(1, BuildAnimeXml(1, "A", new List<Tuple<int, string>> { Tuple.Create(2, "Sequel") }));
            GivenXmlResponse(2, BuildAnimeXml(2, "B", new List<Tuple<int, string>> { Tuple.Create(3, "Sequel") }));
            GivenXmlResponse(3, BuildAnimeXml(3, "C", new List<Tuple<int, string>> { Tuple.Create(4, "Sequel") }));
            GivenXmlResponse(4, BuildAnimeXml(4, "D", new List<Tuple<int, string>> { Tuple.Create(5, "Sequel") }));

            var checkCount = 0;
            Mocker.GetMock<IConfigFileProvider>()
                  .Setup(s => s.IsRelatedSeriesEnabled)
                  .Returns(() =>
                  {
                      checkCount++;

                      // Returns true initially (before while loop, inside while loop for B)
                      // Returns false halfway through traversal
                      return checkCount <= 2;
                  });

            Subject.Execute(new FetchAniDbRelatedSeriesCommand(_series.Id));

            // Since it aborted mid-flight (after fetching B), it shouldn't have fetched C and D.
            // B is in the initial list, and its relation C might be added but traversal stops before fetching C.
            // Actually, B fetches, adds C to queue, next iteration checks setting, it's false, loop breaks.
            // But update is only saved if newRelationsFound = true (which happens when C is added to the list).
            Assert.That(_relatedSeriesCache.Any(r => r.RelatedAniDbId == 4), Is.False);
            Console.WriteLine("Test 3 Pass: Process aborted mid-flight when setting was toggled.");
        }
    }
}
