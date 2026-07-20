using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles
{
    public interface IHardlinkSeriesFiles
    {
        HardlinkResult HardlinkSeries(Series series, string oldSeriesPath);
    }

    public class HardlinkResult
    {
        public int Succeeded { get; set; }
        public List<string> Failed { get; set; } = new();
    }

    /// <summary>
    /// Creates hardlinks of all episode files into a new root folder without touching originals.
    /// Fails gracefully: returns a result object with succeeded/failed counts rather than throwing,
    /// unless zero files succeeded (in which case the exception is rethrown).
    /// </summary>
    public class HardlinkSeriesService : IHardlinkSeriesFiles
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IBuildFileNames _buildFileNames;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IRootFolderService _rootFolderService;
        private readonly Logger _logger;

        public HardlinkSeriesService(IDiskProvider diskProvider,
                                     IMediaFileService mediaFileService,
                                     IBuildFileNames buildFileNames,
                                     IDiskTransferService diskTransferService,
                                     IRootFolderService rootFolderService,
                                     Logger logger)
        {
            _diskProvider = diskProvider;
            _mediaFileService = mediaFileService;
            _buildFileNames = buildFileNames;
            _diskTransferService = diskTransferService;
            _rootFolderService = rootFolderService;
            _logger = logger;
        }

        public HardlinkResult HardlinkSeries(Series series, string oldSeriesPath)
        {
            var episodeFiles = _mediaFileService.GetFilesBySeries(series.Id);
            var result = new HardlinkResult();

            foreach (var episodeFile in episodeFiles)
            {
                try
                {
                    var sourcePath = Path.Combine(oldSeriesPath, episodeFile.RelativePath);

                    if (!_diskProvider.FileExists(sourcePath))
                    {
                        _logger.Warn("Source file not found, skipping hardlink: {0}", sourcePath);
                        result.Failed.Add(sourcePath);
                        continue;
                    }

                    // Destination is directly constructed using the fresh series Path
                    var destinationPath = Path.Combine(series.Path, episodeFile.RelativePath);

                    // Ensure destination directory exists
                    var destDir = Path.GetDirectoryName(destinationPath);
                    if (!_diskProvider.FolderExists(destDir))
                    {
                        _diskProvider.CreateFolder(destDir);
                    }

                    if (_diskProvider.FileExists(destinationPath))
                    {
                        _logger.Debug("Hardlink destination already exists, skipping: {0}", destinationPath);
                        result.Succeeded++;
                        continue;
                    }

                    _logger.Debug("Creating hardlink: {0} -> {1}", sourcePath, destinationPath);

                    try
                    {
                        var transferResult = _diskTransferService.TransferFile(sourcePath, destinationPath, TransferMode.HardLink);

                        if (transferResult.HasFlag(TransferMode.HardLink))
                        {
                            result.Succeeded++;
                            _logger.Debug("Hardlink created successfully: {0}", destinationPath);

                            try
                            {
                                // Remove the old link, mimicking a move
                                _diskProvider.DeleteFile(sourcePath);
                                _logger.Debug("Removed old hardlink source: {0}", sourcePath);
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn(ex, "Failed to remove old hardlink source after successfully hardlinking to destination: {0}", sourcePath);
                            }
                        }
                        else
                        {
                            // TransferFile might fallback or fail silently if not hardlink
                            var msg = $"Hardlink failed (possibly cross-device): {sourcePath} -> {destinationPath}";
                            _logger.Warn(msg);
                            result.Failed.Add(sourcePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, $"Hardlink failed (possibly cross-device): {sourcePath} -> {destinationPath}");
                        result.Failed.Add(sourcePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unexpected error hardlinking file: {0}", episodeFile.RelativePath);
                    result.Failed.Add(episodeFile.RelativePath);
                }
            }

            _logger.Info("Hardlink complete for '{0}': {1} succeeded, {2} failed", series.Title, result.Succeeded, result.Failed.Count);

            if (result.Failed.Count == 0 && _diskProvider.FolderExists(oldSeriesPath))
            {
                DeleteOldSeriesFolder(oldSeriesPath, result.Succeeded);
            }

            return result;
        }

        private void DeleteOldSeriesFolder(string oldSeriesPath, int succeededCount)
        {
            try
            {
                var cleanOldPath = oldSeriesPath.CleanFilePath();
                var osPath = new OsPath(cleanOldPath);

                if (osPath.IsEmpty || osPath.Directory == OsPath.Null)
                {
                    _logger.Error("Safety Check Failed: Cannot delete old series folder because path is empty or a filesystem root. Path: {0}", cleanOldPath);
                    return;
                }

                var rootFolders = _rootFolderService.All();
                foreach (var rootFolder in rootFolders)
                {
                    var cleanRootFolder = rootFolder.Path.CleanFilePath();
                    if (cleanRootFolder.PathEquals(cleanOldPath) || cleanOldPath.IsParentPath(cleanRootFolder))
                    {
                        _logger.Error("Safety Check Failed: Cannot delete old series folder because path is equal to or shorter than a configured root folder. Path: {0}, Root Folder: {1}", cleanOldPath, cleanRootFolder);
                        return;
                    }
                }

                var allFiles = _diskProvider.GetFiles(cleanOldPath, true).ToList();
                if (allFiles.Count > 0)
                {
                    _logger.Warn("Deleting old series folder with {0} untracked files remaining: {1}", allFiles.Count, cleanOldPath);
                    foreach (var file in allFiles)
                    {
                        _logger.Debug("Untracked file will be deleted: {0}", file);
                    }
                }

                _logger.Info("Deleting old series folder '{0}' — {1} episode files verified successfully relinked to new location.", cleanOldPath, succeededCount);
                _diskProvider.DeleteFolder(cleanOldPath, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to cleanly delete old series folder: {0}", oldSeriesPath);
            }
        }
    }
}
