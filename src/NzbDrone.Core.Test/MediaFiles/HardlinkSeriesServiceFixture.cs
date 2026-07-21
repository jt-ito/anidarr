using System;
using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class HardlinkSeriesServiceFixture : CoreTest<HardlinkSeriesService>
    {
        private Series _series;
        private string _oldSeriesPath;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Id = 1)
                                     .With(s => s.Path = @"C:\Test\NewPath\Series")
                                     .Build();

            _oldSeriesPath = @"C:\Test\OldPath\Series";

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_skip_if_source_file_does_not_exist()
        {
            var episodeFile = Builder<EpisodeFile>.CreateNew()
                                                  .With(f => f.RelativePath = "Episode1.mkv")
                                                  .Build();

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesBySeries(_series.Id))
                  .Returns(new List<EpisodeFile> { episodeFile });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(Path.Combine(_oldSeriesPath, "Episode1.mkv")))
                  .Returns(false);

            var result = Subject.HardlinkSeries(_series, _oldSeriesPath);

            result.Succeeded.Should().Be(0);
            result.Failed.Should().HaveCount(1);
            result.Failed[0].Should().Be(Path.Combine(_oldSeriesPath, "Episode1.mkv"));
        }

        [Test]
        public void should_hardlink_and_delete_old_source_when_successful()
        {
            var episodeFile = Builder<EpisodeFile>.CreateNew()
                                                  .With(f => f.RelativePath = "Season 1\\Episode1.mkv")
                                                  .Build();

            var expectedSource = Path.Combine(_oldSeriesPath, "Season 1", "Episode1.mkv");
            var expectedDest = Path.Combine(_series.Path, "Season 1", "Episode1.mkv");

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesBySeries(_series.Id))
                  .Returns(new List<EpisodeFile> { episodeFile });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(expectedSource))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(expectedDest))
                  .Returns(false);

            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFile(expectedSource, expectedDest, TransferMode.HardLink))
                  .Returns(TransferMode.HardLink);

            var result = Subject.HardlinkSeries(_series, _oldSeriesPath);

            result.Succeeded.Should().Be(1);
            result.Failed.Should().BeEmpty();

            // Verify DiskTransferService was called with correct paths
            Mocker.GetMock<IDiskTransferService>()
                  .Verify(v => v.TransferFile(expectedSource, expectedDest, TransferMode.HardLink), Times.Once());

            // Verify the old source was deleted
            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFile(expectedSource), Times.Once());
        }

        [Test]
        public void should_preserve_old_source_if_hardlink_fails()
        {
            var episodeFile = Builder<EpisodeFile>.CreateNew()
                                                  .With(f => f.RelativePath = "Episode1.mkv")
                                                  .Build();

            var expectedSource = Path.Combine(_oldSeriesPath, "Episode1.mkv");
            var expectedDest = Path.Combine(_series.Path, "Episode1.mkv");

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesBySeries(_series.Id))
                  .Returns(new List<EpisodeFile> { episodeFile });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(expectedSource))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(expectedDest))
                  .Returns(false);

            // Simulate failure
            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFile(expectedSource, expectedDest, TransferMode.HardLink))
                  .Throws(new Exception("Cross-device link"));

            var result = Subject.HardlinkSeries(_series, _oldSeriesPath);

            result.Succeeded.Should().Be(0);
            result.Failed.Should().HaveCount(1);
            result.Failed[0].Should().Be(expectedSource);

            // Verify the old source was NOT deleted
            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_process_multiple_episodes_sequentially_and_handle_partial_failures()
        {
            var episode1 = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Ep1.mkv").Build();
            var episode2 = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Ep2.mkv").Build();
            var episode3 = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Ep3.mkv").Build();

            var source1 = Path.Combine(_oldSeriesPath, "Ep1.mkv");
            var source2 = Path.Combine(_oldSeriesPath, "Ep2.mkv");
            var source3 = Path.Combine(_oldSeriesPath, "Ep3.mkv");

            var dest1 = Path.Combine(_series.Path, "Ep1.mkv");
            var dest2 = Path.Combine(_series.Path, "Ep2.mkv");
            var dest3 = Path.Combine(_series.Path, "Ep3.mkv");

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesBySeries(_series.Id))
                  .Returns(new List<EpisodeFile> { episode1, episode2, episode3 });

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsRegex("NewPath"))).Returns(false);

            // Ep1 succeeds
            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFile(source1, dest1, TransferMode.HardLink))
                  .Returns(TransferMode.HardLink);

            // Ep2 fails
            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFile(source2, dest2, TransferMode.HardLink))
                  .Throws(new Exception("Fail"));

            // Ep3 succeeds
            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFile(source3, dest3, TransferMode.HardLink))
                  .Returns(TransferMode.HardLink);

            var result = Subject.HardlinkSeries(_series, _oldSeriesPath);

            result.Succeeded.Should().Be(2);
            result.Failed.Should().HaveCount(1);
            result.Failed[0].Should().Be(source2);

            // Verify sequential deletions
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(source1), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(source2), Times.Never()); // Preserved
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(source3), Times.Once());
        }

        [Test]
        public void should_delete_old_folder_when_all_episodes_successfully_hardlinked()
        {
            var episodeFile = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Episode1.mkv").Build();
            var expectedSource = Path.Combine(_oldSeriesPath, "Episode1.mkv");
            var expectedDest = Path.Combine(_series.Path, "Episode1.mkv");

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesBySeries(_series.Id)).Returns(new List<EpisodeFile> { episodeFile });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(expectedSource)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(_oldSeriesPath)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(It.IsAny<string>(), true)).Returns(new string[0]);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder>());

            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFile(expectedSource, expectedDest, TransferMode.HardLink))
                  .Returns(TransferMode.HardLink);

            Subject.HardlinkSeries(_series, _oldSeriesPath);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(_oldSeriesPath, true), Times.Once());
        }

        [Test]
        public void should_not_delete_old_folder_when_partial_failures_occur()
        {
            var episodeFile = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Episode1.mkv").Build();
            var expectedSource = Path.Combine(_oldSeriesPath, "Episode1.mkv");
            var expectedDest = Path.Combine(_series.Path, "Episode1.mkv");

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesBySeries(_series.Id)).Returns(new List<EpisodeFile> { episodeFile });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(expectedSource)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(_oldSeriesPath)).Returns(true);
            Mocker.GetMock<IDiskTransferService>().Setup(s => s.TransferFile(expectedSource, expectedDest, TransferMode.HardLink)).Throws(new Exception("Fail"));

            Subject.HardlinkSeries(_series, _oldSeriesPath);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_delete_old_folder_when_it_matches_root_folder()
        {
            var episodeFile = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Episode1.mkv").Build();
            var expectedSource = Path.Combine(_oldSeriesPath, "Episode1.mkv");
            var expectedDest = Path.Combine(_series.Path, "Episode1.mkv");

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesBySeries(_series.Id)).Returns(new List<EpisodeFile> { episodeFile });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(expectedSource)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(_oldSeriesPath)).Returns(true);
            Mocker.GetMock<IDiskTransferService>().Setup(s => s.TransferFile(expectedSource, expectedDest, TransferMode.HardLink)).Returns(TransferMode.HardLink);

            // Setup RootFolderService to return the old path as a Root Folder
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder> { new RootFolder { Path = _oldSeriesPath } });

            Subject.HardlinkSeries(_series, _oldSeriesPath);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_delete_old_folder_when_it_is_parent_of_root_folder()
        {
            var episodeFile = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Episode1.mkv").Build();

            // Explicitly test the case where old path is a parent of a root folder
            var explicitOldPath = @"/data/Choows"; // Parent directory
            var explicitRootFolder = @"/data/Choows/My Classmate's a Sexy Actress, and Now We Live Together!!"; // Child root folder

            var expectedSource = Path.Combine(explicitOldPath, "Episode1.mkv");
            var expectedDest = Path.Combine(_series.Path, "Episode1.mkv");

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesBySeries(_series.Id)).Returns(new List<EpisodeFile> { episodeFile });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(expectedSource)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(explicitOldPath)).Returns(true);
            Mocker.GetMock<IDiskTransferService>().Setup(s => s.TransferFile(expectedSource, expectedDest, TransferMode.HardLink)).Returns(TransferMode.HardLink);

            // Setup RootFolderService to return a root folder that is INSIDE the old path
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder> { new RootFolder { Path = explicitRootFolder } });

            Subject.HardlinkSeries(_series, explicitOldPath);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_delete_old_folder_even_if_it_shares_prefix_with_root_folder()
        {
            var episodeFile = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Episode1.mkv").Build();

            // Explicitly test the false-prefix match scenario
            var explicitOldPath = @"/data/ChoowsExtra"; // A sibling directory that shares a prefix
            var explicitRootFolder = @"/data/Choows"; // The root folder

            var expectedSource = Path.Combine(explicitOldPath, "Episode1.mkv");
            var expectedDest = Path.Combine(_series.Path, "Episode1.mkv");

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesBySeries(_series.Id)).Returns(new List<EpisodeFile> { episodeFile });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(expectedSource)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(explicitOldPath)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(It.IsAny<string>(), true)).Returns(new string[0]);
            Mocker.GetMock<IDiskTransferService>().Setup(s => s.TransferFile(expectedSource, expectedDest, TransferMode.HardLink)).Returns(TransferMode.HardLink);

            // Setup RootFolderService to return a root folder that shares a prefix but is not a parent/child
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder> { new RootFolder { Path = explicitRootFolder } });

            Subject.HardlinkSeries(_series, explicitOldPath);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(explicitOldPath.CleanFilePath(), true), Times.Once());
        }

        [Test]
        public void should_abort_deletion_when_untracked_files_exist()
        {
            var episodeFile = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Episode1.mkv").Build();
            var expectedSource = Path.Combine(_oldSeriesPath, "Episode1.mkv");
            var expectedDest = Path.Combine(_series.Path, "Episode1.mkv");

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesBySeries(_series.Id)).Returns(new List<EpisodeFile> { episodeFile });
            var oldPath = @"C:\Test\OldPath\Series";
            Mocker.GetMock<IDiskProvider>().Setup(v => v.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.GetFiles(It.IsAny<string>(), true)).Returns(new[] { @"C:\Test\OldPath\Series\untracked.nfo" });

            // Fix: Mock Hardlink dependencies so it gets past them and reaches deletion logic
            expectedSource = Path.Combine(oldPath, "Episode1.mkv");
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(expectedSource)).Returns(true);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder>());
            Mocker.GetMock<IDiskTransferService>().Setup(s => s.TransferFile(expectedSource, expectedDest, TransferMode.HardLink)).Returns(TransferMode.HardLink);

            var result = Subject.HardlinkSeries(_series, oldPath);

            Assert.AreEqual(1, result.Succeeded, "Succeeded count");
            Assert.AreEqual(0, result.Failed.Count, "Failed count");

            // We expect DeleteFolder to NEVER be called
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_delete_exact_series_folder_without_touching_parent_root_folder()
        {
            var originalOldPath = @"/data/Choows/My Classmate's a Sexy Actress, and Now We Live Together!!";
            var rootFolderPath = @"/data/Choows";

            var episodeFile = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Episode1.mkv").Build();
            var expectedSource = Path.Combine(originalOldPath, "Episode1.mkv");
            var expectedDest = Path.Combine(_series.Path, "Episode1.mkv");

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesBySeries(_series.Id)).Returns(new List<EpisodeFile> { episodeFile });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(expectedSource)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(originalOldPath)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(It.IsAny<string>(), true)).Returns(new string[0]);

            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder> { new RootFolder { Path = rootFolderPath } });

            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFile(expectedSource, expectedDest, TransferMode.HardLink))
                  .Returns(TransferMode.HardLink);

            Subject.HardlinkSeries(_series, originalOldPath);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(originalOldPath.CleanFilePath(), true), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(rootFolderPath.CleanFilePath(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(It.Is<string>(p => p != originalOldPath.CleanFilePath()), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_delete_old_folder_when_it_shares_string_prefix_but_is_not_child_of_root_folder()
        {
            var originalOldPath = @"/data/ChoowsExtra";
            var rootFolderPath = @"/data/Choows";

            var episodeFile = Builder<EpisodeFile>.CreateNew().With(f => f.RelativePath = "Episode1.mkv").Build();
            var expectedSource = Path.Combine(originalOldPath, "Episode1.mkv");
            var expectedDest = Path.Combine(_series.Path, "Episode1.mkv");

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesBySeries(_series.Id)).Returns(new List<EpisodeFile> { episodeFile });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(expectedSource)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(originalOldPath)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(It.IsAny<string>(), true)).Returns(new string[0]);

            Mocker.GetMock<IRootFolderService>().Setup(s => s.All()).Returns(new List<RootFolder> { new RootFolder { Path = rootFolderPath } });

            Mocker.GetMock<IDiskTransferService>()
                  .Setup(s => s.TransferFile(expectedSource, expectedDest, TransferMode.HardLink))
                  .Returns(TransferMode.HardLink);

            Subject.HardlinkSeries(_series, originalOldPath);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(originalOldPath.CleanFilePath(), true), Times.Once());
        }
    }
}
