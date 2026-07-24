using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class RefreshSeriesServiceAniDbIntegrationFixture : CoreTest<RefreshSeriesService>
    {
        private Series _series;

        [SetUp]
        public void Setup()
        {
            var season1 = Builder<Season>.CreateNew()
                                         .With(s => s.SeasonNumber = 1)
                                         .Build();

            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Status = SeriesStatusType.Continuing)
                                     .With(s => s.PrimaryMetadataProvider = "anidb")
                                     .With(s => s.AniDbId = 1)
                                     .With(s => s.TvdbId = -1)
                                     .With(s => s.Seasons = new List<Season> { season1 })
                                     .Build();

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(_series.Id))
                  .Returns(_series);

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<Series> { _series });

            Mocker.GetMock<IAniDbRateLimiter>()
                .Setup(v => v.ExecuteAsync(It.IsAny<Func<string>>()))
                .Returns((Func<string> action) => Task.FromResult(action()));

            Mocker.GetMock<IAppFolderInfo>()
                .SetupGet(v => v.AppDataFolder)
                .Returns(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString()));

            var anidbProvider = Mocker.Resolve<AniDbProvider>();

            var tvdbMock = new Mock<IMetadataProvider>();
            tvdbMock.SetupGet(p => p.ProviderType).Returns(MetadataProviderType.Tvdb);

            var dispatcher = new MetadataDispatcher(new List<IMetadataProvider> { tvdbMock.Object, anidbProvider }, Mocker.GetMock<IAnimeOfflineDatabase>().Object, Mocker.Resolve<Logger>());

            Mocker.SetConstant<IMetadataDispatcher>(dispatcher);
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
  <titles><title xml:lang=""en"" type=""main"">{title}</title></titles>
  <type>TV Series</type>
  <relatedanime>{relatedAnimeXml}</relatedanime>
  <episodes>{episodesXml}</episodes>
</anime>";
        }

        [Test]
        public void should_merge_new_season_from_anidb_during_refresh()
        {
            _series.MonitorNewItems = NewItemMonitorTypes.All;

            GivenXmlResponse(1, BuildAnimeXml(1, "Season 1", new List<Tuple<int, string>> { Tuple.Create(2, "Sequel") }));
            GivenXmlResponse(2, BuildAnimeXml(2, "Season 2", new List<Tuple<int, string>> { Tuple.Create(1, "Prequel") }));

            Subject.Execute(new RefreshSeriesCommand(new List<int> { _series.Id }));

            Mocker.GetMock<ISeriesService>()
                .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.Seasons.Count == 2 && s.Seasons.Single(season => season.SeasonNumber == 2).Monitored == true), It.IsAny<bool>(), It.IsAny<bool>()));

            Mocker.GetMock<IRefreshEpisodeService>()
                .Verify(v => v.RefreshEpisodeInfo(It.IsAny<Series>(), It.Is<List<Episode>>(eps => eps.Count(e => e.SeasonNumber == 2) == 12)));
        }
    }
}
