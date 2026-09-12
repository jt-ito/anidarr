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
using NzbDrone.Core.Tv;

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

        [Test]
        public void should_update_metadata_with_anidb_poster_and_rich_info_overwriting_minami_placeholder()
        {
            var existing = new AnimeOfflineTitle
            {
                Id = 1,
                AniDbId = 16067,
                Title = "Old Title",
                PictureUrl = "https://cdn.myanimelist.net/images/anime/10/12345.jpg",
                Overview = null,
                Year = null,
                Status = SeriesStatusType.Continuing
            };

            _titleRepositoryMock.Setup(c => c.FindByAniDbId(16067)).Returns(existing);

            var series = new NzbDrone.Core.Tv.Series
            {
                AniDbId = 16067,
                Title = "My Little Brother Is Huge as Hell",
                Overview = "Rich AniDB description",
                Year = 2021,
                Status = SeriesStatusType.Ended,
                Genres = new List<string> { "Comedy", "Ecchi" },
                Images = new List<NzbDrone.Core.MediaCover.MediaCover>
                {
                    new NzbDrone.Core.MediaCover.MediaCover(NzbDrone.Core.MediaCover.MediaCoverTypes.Poster, "https://cdn.anidb.net/images/main/257324.jpg")
                }
            };

            Subject.UpdateMetadata(series);

            _titleRepositoryMock.Verify(
                c => c.Update(It.Is<AnimeOfflineTitle>(t =>
                    t.PictureUrl == "https://cdn.anidb.net/images/main/257324.jpg" &&
                    t.Overview == "Rich AniDB description" &&
                    t.Year == 2021 &&
                    t.Status == SeriesStatusType.Ended &&
                    t.Genres.Contains("Comedy"))),
                Times.Once);

            _titleRepositoryMock.Verify(c => c.ClearFuzzyCache(), Times.Once);
        }

        [Test]
        public void should_not_let_manami_dump_overwrite_authoritative_anidb_poster()
        {
            var existing = new AnimeOfflineTitle
            {
                Id = 1,
                AniDbId = 16067,
                Title = "Uchi no Otouto",
                PictureUrl = "https://cdn.anidb.net/images/main/257324.jpg",
                Overview = "Rich AniDB description"
            };

            _titleRepositoryMock.Setup(c => c.All()).Returns(new List<AnimeOfflineTitle> { existing });

            var jsonPath = Path.GetTempFileName();
            File.WriteAllText(jsonPath, "{\"data\":[{\"title\":\"Uchi no Otouto\",\"sources\":[\"https://anidb.net/anime/16067\"],\"picture\":\"https://cdn.myanimelist.net/images/anime/1/999.jpg\",\"status\":\"FINISHED\"}]}");

            var gzPath = Path.GetTempFileName();
            using (var fileStream = new FileStream(gzPath, FileMode.Create))
            using (var gzipStream = new GZipStream(fileStream, CompressionMode.Compress, true))
            using (var writer = new StreamWriter(gzipStream, Encoding.UTF8))
            {
                writer.Write("16067|1|x-jat|Uchi no Otouto\n");
            }

            AnimeOfflineTitle updatedTitle = null;
            _titleRepositoryMock.Setup(c => c.UpdateMany(It.IsAny<IList<AnimeOfflineTitle>>()))
                .Callback<IList<AnimeOfflineTitle>>(t => updatedTitle = t[0]);

            Subject.ParseAndSyncDumps(jsonPath, gzPath);

            // PictureUrl should remain the AniDB poster, NOT overwritten by MAL
            existing.PictureUrl.Should().Be("https://cdn.anidb.net/images/main/257324.jpg");
            existing.Overview.Should().Be("Rich AniDB description");

            File.Delete(gzPath);
            File.Delete(jsonPath);
        }

        [Test]
        public void should_backfill_from_cached_anidb_xml()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var cacheDir = Path.Combine(tempDir, "AniDbCache");
            Directory.CreateDirectory(cacheDir);

            _appFolderInfoMock.SetupGet(c => c.AppDataFolder).Returns(tempDir);

            var xmlContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<anime id=""16067"">
    <picture>257324.jpg</picture>
    <startdate>2021-04-28</startdate>
    <enddate>2021-04-28</enddate>
    <description>Clean description here</description>
    <tags>
        <tag><name>Comedy</name></tag>
    </tags>
    <titles>
        <title type=""main"" xml:lang=""x-jat"">Uchi no Otouto</title>
    </titles>
</anime>";

            File.WriteAllText(Path.Combine(cacheDir, "anime_aid16067.xml"), xmlContent);

            var existing = new AnimeOfflineTitle
            {
                Id = 1,
                AniDbId = 16067,
                Title = "Uchi no Otouto",
                PictureUrl = null,
                Overview = null
            };

            _titleRepositoryMock.Setup(c => c.FindByAniDbId(16067)).Returns(existing);

            Subject.BackfillFromCachedAniDbXml();

            _titleRepositoryMock.Verify(
                c => c.Update(It.Is<AnimeOfflineTitle>(t =>
                    t.PictureUrl == "https://cdn.anidb.net/images/main/257324.jpg" &&
                    t.Overview == "Clean description here" &&
                    t.Year == 2021 &&
                    t.Status == SeriesStatusType.Ended &&
                    t.Genres.Contains("Comedy"))),
                Times.Once);

            Directory.Delete(tempDir, true);
        }
    }
}
