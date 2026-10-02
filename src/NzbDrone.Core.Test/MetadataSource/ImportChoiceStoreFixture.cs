using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class ImportChoiceStoreFixture
    {
        private Dictionary<string, string> _files;
        private Mock<IDiskProvider> _disk;
        private Mock<IAppFolderInfo> _appFolder;

        [SetUp]
        public void Setup()
        {
            _files = new Dictionary<string, string>();
            _disk = new Mock<IDiskProvider>();
            _appFolder = new Mock<IAppFolderInfo>();

            _appFolder.SetupGet(a => a.AppDataFolder).Returns("/config");
            _disk.Setup(d => d.FileExists(It.IsAny<string>())).Returns<string>(p => _files.ContainsKey(p));
            _disk.Setup(d => d.ReadAllText(It.IsAny<string>())).Returns<string>(p => _files[p]);
            _disk.Setup(d => d.WriteAllText(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((p, c) => _files[p] = c);
        }

        private ImportChoiceStore NewStore()
        {
            return new ImportChoiceStore(_appFolder.Object, _disk.Object, LogManager.GetCurrentClassLogger());
        }

        [Test]
        public void should_remember_a_choice_across_restarts()
        {
            NewStore().Remember("[Group] One Piece [1080p]", "tvdb", 392276, "ONE PIECE (2023)");

            var found = NewStore().Find("[Group] One Piece [1080p]");

            found.Provider.Should().Be("tvdb");
            found.Id.Should().Be(392276);
        }

        [Test]
        public void should_ignore_case_and_spacing_in_the_folder_name()
        {
            var store = NewStore();

            store.Remember("One  Piece", "anidb", 69, "One Piece");

            store.Find("  one piece ").Should().NotBeNull();
        }

        [Test]
        public void should_not_find_a_folder_it_has_not_seen()
        {
            NewStore().Find("Unknown").Should().BeNull();
        }

        [TestCase("tvdb", 0)]
        [TestCase("imdb", 5)]
        [TestCase("", 5)]
        public void should_refuse_choices_it_cannot_use_later(string provider, int id)
        {
            var store = NewStore();

            store.Remember("Show", provider, id, "Show");

            store.Find("Show").Should().BeNull();
            _files.Should().BeEmpty();
        }

        [Test]
        public void should_start_empty_when_the_saved_file_is_corrupt()
        {
            _files["/config/import-choices.json"] = "{ not json";

            var store = NewStore();

            store.Find("Show").Should().BeNull();
            store.Remember("Show", "tvdb", 1, "Show");
            store.Find("Show").Should().NotBeNull();
        }
    }
}
