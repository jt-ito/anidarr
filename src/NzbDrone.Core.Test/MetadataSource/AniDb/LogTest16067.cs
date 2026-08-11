using System;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.AniDb
{
    [TestFixture]
    public class LogTest16067 : CoreTest<AniDbProvider>
    {
        [Test]
        public void generate_production_log_for_16067()
        {
            var realRateLimiter = new AniListRateLimiter(TestLogger);
            typeof(AniListRateLimiter).GetField("_staticLogger", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, TestLogger);
            var realEnricher = new AniListEnricher(
                Mocker.GetMock<IHttpClient>().Object,
                realRateLimiter,
                TestLogger);
            Mocker.SetConstant<IAniListEnricher>(realEnricher);

            Mocker.GetMock<IHttpClient>()
                .Setup(s => s.Post<AniListSearchResponse>(It.Is<HttpRequest>(c => c.Url.ToString().Contains("graphql.anilist.co"))))
                .Returns(new HttpResponse<AniListSearchResponse>(new HttpResponse(new HttpRequest("https://graphql.anilist.co"), new HttpHeader(), "{\"data\": {\"Page\": {\"media\": []}}}")));

            Mocker.GetMock<IAniDbRateLimiter>()
                .Setup(v => v.ExecuteAsync(It.IsAny<Func<string>>()))
                .Returns((Func<string> action) => System.Threading.Tasks.Task.FromResult(action()));

            var appFolderInfoMock = Mocker.GetMock<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
            appFolderInfoMock.SetupGet(c => c.AppDataFolder).Returns("c:\\test\\appdata");

            var testXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""16067"">
  <titles>
    <title xml:lang=""en"" type=""main"">Test Anime</title>
  </titles>
  <type>TV Series</type>
  <startdate>2026-07-26</startdate>
  <episodes>
    <episode><epno type=""1"">1</epno><length>25</length><title xml:lang=""en"">Episode 1</title><airdate>2026-07-26</airdate></episode>
  </episodes>
</anime>";

            GivenXmlResponse(16067, testXml);

            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            var localSeries = new AnimeOfflineTitle
            {
                AniDbId = 16067,
                RomajiTitle = "Uchi no Otouto Maji de Dekain Dakedo Mi ni Konai?",
                NativeTitle = "ウチの弟マジでデカイんだけど見にこない",
                EnglishTitle = "My Little Brother Is Huge as Hell. Wanna Come over and See?",
                SearchSynonyms = new System.Collections.Generic.List<string> { "우리동생진짜큰데보러안올래" }
            };
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 16067)).Returns(localSeries);

            Subject.GetSeriesInfo("16067");

            Console.WriteLine("\n\n--- PRODUCTION LOG OUTPUT ---");
            Console.WriteLine("\n\n--- END PRODUCTION LOG OUTPUT ---");
        }

        protected void GivenXmlResponse(int id, string xml)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Execute(It.Is<HttpRequest>(c => c.Url.ToString().Contains($"aid={id}"))))
                  .Returns(new HttpResponse(null, new HttpHeader(), xml));
        }
    }
}
