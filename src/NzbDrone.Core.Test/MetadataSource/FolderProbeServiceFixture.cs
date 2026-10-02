using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class FolderProbeServiceFixture
    {
        private const string Folder = "/library/Some Show";

        private Mock<IDiskProvider> _disk;
        private FolderProbeService _subject;

        [SetUp]
        public void Setup()
        {
            _disk = new Mock<IDiskProvider>();
            _disk.Setup(d => d.FolderExists(Folder)).Returns(true);
            _disk.Setup(d => d.GetFileSize(It.IsAny<string>())).Returns(1024);

            _subject = new FolderProbeService(_disk.Object, LogManager.GetCurrentClassLogger());
        }

        private void GivenFiles(params string[] names)
        {
            var files = new List<string>();

            foreach (var name in names)
            {
                files.Add(Folder + "/" + name);
            }

            _disk.Setup(d => d.GetFiles(Folder, true)).Returns(files);
        }

        [TestCase("Some Show (2020) [tvdbid-81797]", "tvdb", 81797)]
        [TestCase("Some Show {tvdb-81797}", "tvdb", 81797)]
        [TestCase("Some Show [anidb-69]", "anidb", 69)]
        [TestCase("Some Show (anidbid=69)", "anidb", 69)]
        public void should_read_ids_written_in_the_folder_name(string folderName, string provider, int id)
        {
            var probe = _subject.Probe("/library/missing", folderName);

            probe.IdHints.Should().ContainSingle().Which.Should().Be((provider, id));
        }

        [Test]
        public void should_read_ids_from_a_tvshow_nfo()
        {
            GivenFiles("tvshow.nfo", "ep1.mkv");
            _disk.Setup(d => d.ReadAllText(Folder + "/tvshow.nfo"))
                 .Returns("<tvshow><title>X</title><uniqueid type=\"tvdb\" default=\"true\">81797</uniqueid><uniqueid type=\"imdb\">tt0388629</uniqueid></tvshow>");

            var probe = _subject.Probe(Folder, "Some Show");

            probe.IdHints.Should().ContainSingle().Which.Should().Be(("tvdb", 81797));
        }

        [Test]
        public void should_read_legacy_id_elements_from_a_tvshow_nfo()
        {
            GivenFiles("tvshow.nfo");
            _disk.Setup(d => d.ReadAllText(Folder + "/tvshow.nfo")).Returns("<tvshow><anidbid>69</anidbid></tvshow>");

            _subject.Probe(Folder, "Some Show").IdHints.Should().ContainSingle().Which.Should().Be(("anidb", 69));
        }

        [Test]
        public void should_ignore_episode_level_nfo_files()
        {
            GivenFiles("ep1.nfo", "Season 1/tvshow.nfo");
            _disk.Setup(d => d.ReadAllText(It.IsAny<string>())).Returns("<episodedetails><uniqueid type=\"tvdb\">999</uniqueid></episodedetails>");

            _subject.Probe(Folder, "Some Show").IdHints.Should().BeEmpty();
            _disk.Verify(d => d.ReadAllText(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_ignore_a_tvshow_nfo_that_is_not_a_show_file()
        {
            GivenFiles("tvshow.nfo");
            _disk.Setup(d => d.ReadAllText(It.IsAny<string>())).Returns("<movie><uniqueid type=\"tvdb\">999</uniqueid></movie>");

            _subject.Probe(Folder, "Some Show").IdHints.Should().BeEmpty();
        }

        [Test]
        public void should_take_the_show_title_most_files_agree_on()
        {
            GivenFiles(
                "[Judas] Bocchi the Rock! - 01 [1080p].mkv",
                "[Judas] Bocchi the Rock! - 02 [1080p].mkv",
                "[Judas] Bocchi the Rock! - 03 [1080p].mkv",
                "readme.txt");

            var probe = _subject.Probe(Folder, "Some Show");

            probe.VideoFileCount.Should().Be(3);
            probe.FileTitles.Should().Equal("Bocchi the Rock!");
        }

        [Test]
        public void should_find_the_title_when_only_a_trailing_episode_number_differs()
        {
            GivenFiles("Midareuchi 01.mkv", "Midareuchi 02.mkv", "Midareuchi 03.mkv");

            _subject.Probe(Folder, "Some Show").FileTitles.Should().Equal("Midareuchi");
        }

        [Test]
        public void should_use_the_title_of_a_single_file()
        {
            GivenFiles("Show.Name.S01E05.1080p.WEB-DL.x265-GRP.mkv");

            _subject.Probe(Folder, "Some Show").FileTitles.Should().Equal("Show Name");
        }

        [Test]
        public void should_not_guess_a_title_when_files_disagree()
        {
            GivenFiles("Alpha - 01.mkv", "Beta - 01.mkv", "Gamma - 01.mkv");

            _subject.Probe(Folder, "Some Show").FileTitles.Should().BeEmpty();
        }

        [Test]
        public void should_survive_a_folder_that_cannot_be_read()
        {
            _disk.Setup(d => d.GetFiles(Folder, true)).Throws(new IOException("denied"));

            var probe = _subject.Probe(Folder, "Some Show [tvdbid-1]");

            probe.IdHints.Should().ContainSingle();
            probe.VideoFileCount.Should().Be(0);
        }
    }
}
