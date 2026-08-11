using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class AnimeOfflineDatabaseFixture : CoreTest<AnimeOfflineDatabase>
    {
        private Mock<IAppFolderInfo> _appFolderInfoMock;
        private Mock<IDiskProvider> _diskProviderMock;
        private Mock<IAnimeOfflineTitleRepository> _titleRepositoryMock;

        [SetUp]
        public void SetUp()
        {
            _appFolderInfoMock = Mocker.GetMock<IAppFolderInfo>();
            _diskProviderMock = Mocker.GetMock<IDiskProvider>();
            _titleRepositoryMock = Mocker.GetMock<IAnimeOfflineTitleRepository>();

            _appFolderInfoMock.SetupGet(c => c.AppDataFolder).Returns("c:\\test\\appdata");

            // Allow repository HasItems to be false so EnsureCache doesn't block
            _titleRepositoryMock.Setup(c => c.HasItems()).Returns(false);
        }

        [Test]
        public void should_only_select_en_synonym_as_english_title()
        {
            // Set up a mock gzipped dump file
            var datContent = @"16067|2|fr|Curieuses d'aller voir la tige mastoque de mon frangin ?
16067|2|en|My Little Brother Is Huge as Hell. Wanna Come over and See?
16067|2|ru|У моего брата он чертовски огромен. Не хотите прийти посмотреть?
16067|2|ko|우리 동생 진짜 큰데 보러 안 올래?
16067|4|ja|ウチの弟マジでデカイんだけど見にこない?
16067|1|x-jat|Uchi no Otouto Maji de Dekain Dakedo Mi ni Konai?";

            var gzPath = Path.GetTempFileName();
            using (var fileStream = new FileStream(gzPath, FileMode.Create))
            {
                using (var gzipStream = new GZipStream(fileStream, CompressionMode.Compress, true))
                using (var writer = new StreamWriter(gzipStream, Encoding.UTF8))
                {
                    writer.Write(datContent);
                }
            }

            var jsonPath = Path.GetTempFileName();
            File.WriteAllText(jsonPath, "{\"data\":[]}");

            var insertedTitles = new List<AnimeOfflineTitle>();
            _titleRepositoryMock.Setup(c => c.InsertMany(It.IsAny<IList<AnimeOfflineTitle>>()))
                .Callback<IList<AnimeOfflineTitle>>(t => insertedTitles.AddRange(t));

            Subject.ParseAndSyncDumps(jsonPath, gzPath);

            insertedTitles.Should().HaveCount(1);
            var title = insertedTitles[0];

            title.AniDbId.Should().Be(16067);
            title.RomajiTitle.Should().Be("Uchi no Otouto Maji de Dekain Dakedo Mi ni Konai?");
            title.NativeTitle.Should().Be("ウチの弟マジでデカイんだけど見にこない?");

            // Explicitly asserting that the EN synonym was chosen, ignoring FR/RU/KO synonyms.
            title.EnglishTitle.Should().Be("My Little Brother Is Huge as Hell. Wanna Come over and See?");

            // Ensure all the non-EN synonyms are still safely in SearchSynonyms
            title.SearchSynonyms.Should().Contain("Curieuses d'aller voir la tige mastoque de mon frangin ?");
            title.SearchSynonyms.Should().Contain("У моего брата он чертовски огромен. Не хотите прийти посмотреть?");
            title.SearchSynonyms.Should().Contain("우리 동생 진짜 큰데 보러 안 올래?");

            File.Delete(gzPath);
            File.Delete(jsonPath);
        }
    }
}
