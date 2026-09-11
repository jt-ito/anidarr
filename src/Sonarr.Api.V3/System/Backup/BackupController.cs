using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
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

            var cachedFilePath = Path.Combine(_backupService.GetBackupFolder(), "sonarr-compatible", $"sonarr-compatible_{backup.Name}");
            if (_diskProvider.FileExists(cachedFilePath))
            {
                _diskProvider.DeleteFile(cachedFilePath);
            }

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
        public object RestoreUpload()
        {
            var files = Request.Form.Files;

            if (files.Count == 0)
            {
                throw new BadRequestException("file must be provided");
            }

            var file = files[0];
            var extension = Path.GetExtension(file.FileName);

            if (!ValidExtensions.Contains(extension))
            {
                throw new BadRequestException($"Invalid extension, must be one of: {string.Join(", ", ValidExtensions)}");
            }

            var path = Path.Combine(_appFolderInfo.TempFolder, $"sonarr_backup_restore{extension}");

            _diskProvider.SaveStream(file.OpenReadStream(), path);
            _backupService.Restore(path);
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

        private static int GetBackupId(NzbDrone.Core.Backup.Backup backup)
        {
            return HashConverter.GetHashInt31($"backup-{backup.Type}-{backup.Name}");
        }

        private NzbDrone.Core.Backup.Backup GetBackup(int id)
        {
            return _backupService.GetBackups().SingleOrDefault(b => GetBackupId(b) == id);
        }

        [HttpGet("download/sonarr-compatible/{id:int}")]
        [Microsoft.AspNetCore.Authorization.Authorize(Policy = "UI")]
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

            var cacheFolder = Path.Combine(_backupService.GetBackupFolder(), "sonarr-compatible");
            _diskProvider.EnsureFolder(cacheFolder);
            var cachedFilePath = Path.Combine(cacheFolder, $"sonarr-compatible_{backup.Name}");

            if (_diskProvider.FileExists(cachedFilePath))
            {
                var sourceTime = _diskProvider.FileGetLastWrite(path);
                var cachedTime = _diskProvider.FileGetLastWrite(cachedFilePath);

                if (cachedTime >= sourceTime && _diskProvider.GetFileSize(cachedFilePath) > 0)
                {
                    var cachedStream = new FileStream(cachedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return File(cachedStream, "application/zip", backup.Name.Replace("sonarr_backup_", "sonarr-compatible_backup_"));
                }
            }

            var guidStr = global::System.Guid.NewGuid().ToString("N");
            var tempWorkDir = Path.Combine(_appFolderInfo.TempFolder, $"sonarr_compatible_backup_{guidStr}");
            var tempZipOutput = Path.Combine(_appFolderInfo.TempFolder, $"temp_sonarr_compatible_{guidStr}.zip");
            _diskProvider.EnsureFolder(tempWorkDir);

            try
            {
                global::System.IO.Compression.ZipFile.ExtractToDirectory(path, tempWorkDir, true);

                var dbPath = Path.Combine(tempWorkDir, "sonarr.db");
                if (_diskProvider.FileExists(dbPath))
                {
                    scrubber.ScrubDatabase(dbPath);
                }

                if (_diskProvider.FileExists(tempZipOutput))
                {
                    _diskProvider.DeleteFile(tempZipOutput);
                }

                global::System.IO.Compression.ZipFile.CreateFromDirectory(tempWorkDir, tempZipOutput, global::System.IO.Compression.CompressionLevel.Fastest, false);

                if (_diskProvider.FileExists(cachedFilePath))
                {
                    _diskProvider.DeleteFile(cachedFilePath);
                }

                _diskProvider.MoveFile(tempZipOutput, cachedFilePath);

                var fileStream = new FileStream(cachedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(fileStream, "application/zip", backup.Name.Replace("sonarr_backup_", "sonarr-compatible_backup_"));
            }
            finally
            {
                if (_diskProvider.FolderExists(tempWorkDir))
                {
                    _diskProvider.DeleteFolder(tempWorkDir, true);
                }

                if (_diskProvider.FileExists(tempZipOutput))
                {
                    _diskProvider.DeleteFile(tempZipOutput);
                }
            }
        }
    }
}
