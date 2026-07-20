using System;
using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

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
    }
}
