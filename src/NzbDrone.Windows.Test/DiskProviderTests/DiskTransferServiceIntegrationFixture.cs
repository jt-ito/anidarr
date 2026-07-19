using System;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Test.Common;
using NzbDrone.Windows.Disk;

namespace NzbDrone.Windows.Test.DiskProviderTests
{
    [TestFixture]
    [Platform("Win")]
    public class DiskTransferServiceIntegrationFixture : TestBase<DiskTransferService>
    {
        private DiskProvider _diskProvider;

        [SetUp]
        public void SetUp()
        {
            WindowsOnly();
            _diskProvider = new DiskProvider();
            Mocker.SetConstant<IDiskProvider>(_diskProvider);
        }

        private string GetFileId(string path)
        {
            var psi = new ProcessStartInfo("fsutil", $"file queryfileid \"{path}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var proc = Process.Start(psi))
            {
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                return output;
            }
        }

        [Test]
        public void should_hardlink_already_hardlinked_source()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            var source = Path.Combine(tempFolder, "source.txt");
            var torrentHardlink = Path.Combine(tempFolder, "torrent_hardlink.txt");
            var destination = Path.Combine(tempFolder, "destination.txt");

            File.WriteAllText(source, "Data");
            _diskProvider.TryCreateHardLink(source, torrentHardlink).Should().BeTrue();

            // Transfer from the hardlinked file
            Subject.TransferFile(torrentHardlink, destination, TransferMode.HardLink).Should().HaveFlag(TransferMode.HardLink);

            // Verify inodes
            var sourceId = GetFileId(source);
            var torrentId = GetFileId(torrentHardlink);
            var destId = GetFileId(destination);

            sourceId.Should().Be(torrentId);
            torrentId.Should().Be(destId);
        }

        [Test]
        public void should_hardlink_and_delete_source()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            var source = Path.Combine(tempFolder, "source.txt");
            var destination = Path.Combine(tempFolder, "destination.txt");

            File.WriteAllText(source, "Data");

            Subject.TransferFile(source, destination, TransferMode.HardLink).Should().HaveFlag(TransferMode.HardLink);

            var destId = GetFileId(destination);

            // Delete source
            File.Delete(source);

            File.Exists(destination).Should().BeTrue();
            GetFileId(destination).Should().Be(destId);
            File.ReadAllText(destination).Should().Be("Data");
        }

        [Test]
        public void should_resolve_chained_symlinks_before_hardlinking()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            var source = Path.Combine(tempFolder, "source.txt");
            var symlink1 = Path.Combine(tempFolder, "symlink1.txt");
            var symlink2 = Path.Combine(tempFolder, "symlink2.txt");
            var destination = Path.Combine(tempFolder, "destination.txt");

            File.WriteAllText(source, "Data");

            // Require admin rights for Symlinks on Windows, so we might need to skip if unable to create.
            try
            {
                File.CreateSymbolicLink(symlink1, source);
                File.CreateSymbolicLink(symlink2, symlink1);
            }
            catch (IOException)
            {
                Assert.Ignore("Unable to create symlinks, requires elevated privileges on Windows without Developer Mode.");
                return;
            }
            catch (UnauthorizedAccessException)
            {
                Assert.Ignore("Unable to create symlinks, requires elevated privileges on Windows without Developer Mode.");
                return;
            }

            Subject.TransferFile(symlink2, destination, TransferMode.HardLink).Should().HaveFlag(TransferMode.HardLink);

            var sourceId = GetFileId(source);
            var destId = GetFileId(destination);

            destId.Should().Be(sourceId);
        }
    }
}
