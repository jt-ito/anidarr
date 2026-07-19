using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles
{
    public interface IHardlinkSeriesFiles
    {
        HardlinkResult HardlinkSeries(Series series, string destinationRoot);
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
        private readonly Logger _logger;

        public HardlinkSeriesService(IDiskProvider diskProvider,
                                     IMediaFileService mediaFileService,
                                     IBuildFileNames buildFileNames,
                                     IDiskTransferService diskTransferService,
                                     Logger logger)
        {
            _diskProvider = diskProvider;
            _mediaFileService = mediaFileService;
            _buildFileNames = buildFileNames;
            _diskTransferService = diskTransferService;
            _logger = logger;
        }

        public HardlinkResult HardlinkSeries(Series series, string destinationRoot)
        {
            var episodeFiles = _mediaFileService.GetFilesBySeries(series.Id);
            var result = new HardlinkResult();

            foreach (var episodeFile in episodeFiles)
            {
                try
                {
                    var sourcePath = Path.Combine(series.Path, episodeFile.RelativePath);

                    if (!_diskProvider.FileExists(sourcePath))
                    {
                        _logger.Warn("Source file not found, skipping hardlink: {0}", sourcePath);
                        result.Failed.Add(sourcePath);
                        continue;
                    }

                    // Build destination using same folder structure as original
                    var relativeDestination = episodeFile.RelativePath;
                    var seriesFolderName = new DirectoryInfo(series.Path).Name;
                    var destinationPath = Path.Combine(destinationRoot, seriesFolderName, relativeDestination);

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

            return result;
        }
    }
}
