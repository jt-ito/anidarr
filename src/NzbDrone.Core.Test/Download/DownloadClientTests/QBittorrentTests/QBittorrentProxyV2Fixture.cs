using System;
using System.Collections.Generic;
using System.Net;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.QBittorrentTests
{
    [TestFixture]
    public class QBittorrentProxyV2Fixture : CoreTest<QBittorrentProxyV2>
    {
        private QBittorrentSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = new QBittorrentSettings
            {
                Host = "localhost",
                Port = 8080,
                ApiKey = "someapikey"
            };

            var mockCacheManager = Mocker.GetMock<ICacheManager>();
            var mockAuthCache = new Mock<ICached<Dictionary<string, string>>>();
            mockCacheManager.Setup(c => c.GetCache<Dictionary<string, string>>(It.IsAny<Type>(), "authCookies"))
                            .Returns(mockAuthCache.Object);

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/api/v2/app/webapiVersion"))))
                  .Returns(new HttpResponse(new HttpRequest("http://localhost:8080/api/v2/app/webapiVersion"), new HttpHeader(), "2.11.0"));
        }

        [Test]
        public void should_throw_DownloadClientItemExistsException_when_AddTorrentFromUrl_returns_409_Conflict()
        {
            var httpException = new HttpException(new HttpRequest("http://localhost:8080/api/v2/torrents/add"),
                new HttpResponse(new HttpRequest("http://localhost:8080/api/v2/torrents/add"), new HttpHeader(), new byte[0], HttpStatusCode.Conflict));

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/api/v2/torrents/add"))))
                  .Throws(httpException);

            Assert.Throws<DownloadClientItemExistsException>(() =>
                Subject.AddTorrentFromUrl("magnet:?xt=urn:btih:fakehash", null, _settings));
        }

        [Test]
        public void should_throw_DownloadClientItemExistsException_when_AddTorrentFromFile_returns_409_Conflict()
        {
            var httpException = new HttpException(new HttpRequest("http://localhost:8080/api/v2/torrents/add"),
                new HttpResponse(new HttpRequest("http://localhost:8080/api/v2/torrents/add"), new HttpHeader(), new byte[0], HttpStatusCode.Conflict));

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.Is<HttpRequest>(r => r.Url.FullUri.Contains("/api/v2/torrents/add"))))
                  .Throws(httpException);

            Assert.Throws<DownloadClientItemExistsException>(() =>
                Subject.AddTorrentFromFile("test.torrent", new byte[] { 1, 2, 3 }, null, _settings));
        }
    }
}
