using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Backup;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.System.Backup
{
    [V3ApiController("system/backup")]
    public class BackupController : Controller
    {
        private readonly IBackupService _backupService;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;

        private static readonly List<string> ValidExtensions = new() { ".zip", ".db", ".xml" };

        public BackupController(IBackupService backupService,
                            IAppFolderInfo appFolderInfo,
                            IDiskProvider diskProvider)
        {
            _backupService = backupService;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
        }

        [HttpGet]
        public List<BackupResource> GetBackupFiles()
        {
            var backups = _backupService.GetBackups();

            return backups.Select(b => new BackupResource
            {
                Id = GetBackupId(b),
                Name = b.Name,
                Path = $"/backup/{b.Type.ToString().ToLower()}/{b.Name}",
                Size = b.Size,
                Type = b.Type,
                Time = b.Time
            })
                .OrderByDescending(b => b.Time)
                .ToList();
        }

        [RestDeleteById]
        public object DeleteBackup(int id)
        {
            var backup = GetBackup(id);

            if (backup == null)
            {
                throw new NotFoundException();
            }

            var path = GetBackupPath(backup);

            if (!_diskProvider.FileExists(path))
            {
                throw new NotFoundException();
            }

            _diskProvider.DeleteFile(path);

            return new { };
        }

        [HttpPost("restore/{id:int}")]
        public object Restore([FromRoute] int id)
        {
            var backup = GetBackup(id);

            if (backup == null)
            {
                throw new NotFoundException();
            }

            var path = GetBackupPath(backup);

            _backupService.Restore(path);

            return new
            {
                RestartRequired = true
            };
        }

        [HttpPost("restore/upload")]
        [RequestFormLimits(MultipartBodyLengthLimit = 5000000000)]
        public object UploadAndRestore()
        {
            var files = Request.Form.Files;

            if (files.Empty())
            {
                throw new BadRequestException("file must be provided");
            }

            var file = files[0];
            var extension = Path.GetExtension(file.FileName);

            if (!ValidExtensions.Contains(extension))
            {
                throw new UnsupportedMediaTypeException($"Invalid extension, must be one of: {ValidExtensions.Join(", ")}");
            }

            var path = Path.Combine(_appFolderInfo.TempFolder, $"sonarr_backup_restore{extension}");

            _diskProvider.SaveStream(file.OpenReadStream(), path);
            _backupService.Restore(path);

            // Cleanup restored file
            _diskProvider.DeleteFile(path);

            return new
            {
                RestartRequired = true
            };
        }

        private string GetBackupPath(NzbDrone.Core.Backup.Backup backup)
        {
            return Path.Combine(_backupService.GetBackupFolder(backup.Type), backup.Name);
        }

        private int GetBackupId(NzbDrone.Core.Backup.Backup backup)
        {
            return HashConverter.GetHashInt31($"backup-{backup.Type}-{backup.Name}");
        }

        private NzbDrone.Core.Backup.Backup GetBackup(int id)
        {
            return _backupService.GetBackups().SingleOrDefault(b => GetBackupId(b) == id);
        }

        [HttpGet("download/sonarr-compatible/{id:int}")]
        public IActionResult DownloadSonarrCompatible([FromRoute] int id, [FromServices] ISonarrCompatibleBackupScrubber scrubber, [FromServices] NzbDrone.Common.IArchiveService archiveService)
        {
            var backup = GetBackup(id);
            if (backup == null)
            {
                return NotFound();
            }

            var path = GetBackupPath(backup);
            if (!_diskProvider.FileExists(path))
            {
                return NotFound();
            }

            var guidStr = global::System.Guid.NewGuid().ToString("N");
            var tempWorkDir = Path.Combine(_appFolderInfo.TempFolder, $"sonarr_compatible_backup_{guidStr}");
            _diskProvider.EnsureFolder(tempWorkDir);

            try
            {
                archiveService.Extract(path, tempWorkDir);

                var dbPath = Path.Combine(tempWorkDir, "sonarr.db");
                if (_diskProvider.FileExists(dbPath))
                {
                    scrubber.ScrubDatabase(dbPath);
                }

                var outputPath = Path.Combine(_appFolderInfo.TempFolder, $"sonarr-compatible_{backup.Name}");
                if (_diskProvider.FileExists(outputPath))
                {
                    _diskProvider.DeleteFile(outputPath);
                }

                archiveService.CreateZip(outputPath, _diskProvider.GetFiles(tempWorkDir, true));

                var fileStream = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                return File(fileStream, "application/zip", backup.Name.Replace("sonarr_backup_", "sonarr-compatible_backup_"));
            }
            finally
            {
                if (_diskProvider.FolderExists(tempWorkDir))
                {
                    _diskProvider.DeleteFolder(tempWorkDir, true);
                }
            }
        }
    }
}
