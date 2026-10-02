using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.AniDb;

namespace NzbDrone.Core.Test.MetadataSource.AniDb
{
    [TestFixture]
    public class AniDbXmlClientFixture
    {
        private const string Anime = "<anime id=\"123\"><type>TV Series</type></anime>";

        private string _dataFolder;
        private Mock<IHttpClient> _httpClient;
        private Mock<IConfigFileProvider> _config;
        private Mock<IAniDbRateLimiter> _rateLimiter;
        private AniDbXmlClient _subject;

        [SetUp]
        public void Setup()
        {
            _dataFolder = Path.Combine(Path.GetTempPath(), "anidarr-xml-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dataFolder);

            var appFolder = new Mock<IAppFolderInfo>();
            appFolder.SetupGet(a => a.AppDataFolder).Returns(_dataFolder);

            _httpClient = new Mock<IHttpClient>();
            _config = new Mock<IConfigFileProvider>();
            _rateLimiter = new Mock<IAniDbRateLimiter>();

            _rateLimiter.Setup(r => r.ExecuteAsync(It.IsAny<Func<string>>())).Returns(Task.FromResult(Anime));

            _subject = new AniDbXmlClient(_httpClient.Object, _config.Object, appFolder.Object, _rateLimiter.Object, LogManager.GetCurrentClassLogger());
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_dataFolder, true);
        }

        private void GivenClientIsConfigured()
        {
            _config.SetupGet(c => c.IsAniDbClientConfigured).Returns(true);
            _config.SetupGet(c => c.AniDbClientName).Returns("anidarr-test");
            _config.SetupGet(c => c.AniDbClientVersion).Returns(1);
        }

        // the same name the client gives its cache file for GetAnimeXml(aid)
        private string CacheFileFor(int aniDbId)
        {
            var extraParams = $"aid={aniDbId}";
            var safeParams = new string(extraParams.Where(char.IsLetterOrDigit).ToArray());
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(extraParams)))[..12];

            return Path.Combine(_dataFolder, "AniDbCache", $"anime_{safeParams}_{hash}.xml");
        }

        [Test]
        public void should_say_the_client_is_not_set_up_instead_of_asking_anidb()
        {
            var act = () => _subject.GetAnimeXml(123);

            act.Should().Throw<InvalidOperationException>().WithMessage("*client name and version*");

            // nothing was queued behind the rate limiter or sent
            _rateLimiter.Verify(r => r.ExecuteAsync(It.IsAny<Func<string>>()), Times.Never());
            _httpClient.Verify(h => h.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_still_serve_a_cached_response_without_a_client()
        {
            Directory.CreateDirectory(Path.Combine(_dataFolder, "AniDbCache"));
            File.WriteAllText(CacheFileFor(123), Anime);

            _subject.GetAnimeXml(123).Root.Attribute("id").Value.Should().Be("123");

            _rateLimiter.Verify(r => r.ExecuteAsync(It.IsAny<Func<string>>()), Times.Never());
        }

        [Test]
        public void should_know_which_entries_are_already_cached()
        {
            _subject.IsCached(123).Should().BeFalse();

            Directory.CreateDirectory(Path.Combine(_dataFolder, "AniDbCache"));
            File.WriteAllText(CacheFileFor(123), Anime);

            _subject.IsCached(123).Should().BeTrue();
            _subject.IsCached(456).Should().BeFalse();
        }

        [Test]
        public void should_not_count_an_empty_cache_file_as_cached()
        {
            Directory.CreateDirectory(Path.Combine(_dataFolder, "AniDbCache"));
            File.WriteAllText(CacheFileFor(123), string.Empty);

            _subject.IsCached(123).Should().BeFalse();
        }

        [Test]
        public void should_go_through_the_rate_limiter_once_the_client_is_set_up()
        {
            GivenClientIsConfigured();

            _subject.GetAnimeXml(123).Root.Attribute("id").Value.Should().Be("123");

            _rateLimiter.Verify(r => r.ExecuteAsync(It.IsAny<Func<string>>()), Times.Once());
        }
    }
}
