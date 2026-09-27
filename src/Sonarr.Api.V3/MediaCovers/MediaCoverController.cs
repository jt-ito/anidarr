using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaCover;
using Sonarr.Http;

namespace Sonarr.Api.V3.MediaCovers
{
    [V3ApiController]
    public class MediaCoverController : Controller
    {
        private static readonly Regex RegexResizedImage = new Regex(@"-\d+\.jpg$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RegexFilename = new(@"^(?<coverType>[a-zA-Z]+)(?:-(?<height>\d+))?\.(?<ext>[a-zA-Z0-9]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly IContentTypeProvider _mimeTypeProvider;

        public MediaCoverController(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, IMapCoversToLocal mediaCoverService = null)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _mediaCoverService = mediaCoverService;
            _mimeTypeProvider = new FileExtensionContentTypeProvider();
        }

        [HttpGet(@"{seriesId:int}/{filename:regex((.+)\.(jpg|png|gif))}")]
        public IActionResult GetMediaCover(int seriesId, string filename)
        {
            var filePath = Path.Combine(_appFolderInfo.GetAppDataPath(), "MediaCover", seriesId.ToString(), filename);

            if (!_diskProvider.FileExists(filePath) || _diskProvider.GetFileSize(filePath) == 0)
            {
                // Return the full sized image if someone requests a non-existing resized one.
                var basefilePath = RegexResizedImage.Replace(filePath, ".jpg");
                if (basefilePath != filePath && _diskProvider.FileExists(basefilePath) && _diskProvider.GetFileSize(basefilePath) > 0)
                {
                    return PhysicalFile(basefilePath, GetContentType(basefilePath));
                }

                if (_mediaCoverService != null)
                {
                    var match = RegexFilename.Match(filename);
                    if (match.Success)
                    {
                        var coverTypeStr = match.Groups["coverType"].Value;
                        var heightStr = match.Groups["height"].Value;
                        int? height = string.IsNullOrEmpty(heightStr) ? null : int.Parse(heightStr);

                        if (Enum.TryParse<MediaCoverTypes>(coverTypeStr, true, out var coverType))
                        {
                            var resolvedPath = _mediaCoverService.EnsureCover(seriesId, coverType, height);
                            if (resolvedPath != null && _diskProvider.FileExists(resolvedPath) && _diskProvider.GetFileSize(resolvedPath) > 0)
                            {
                                return PhysicalFile(resolvedPath, GetContentType(resolvedPath));
                            }
                        }
                    }
                }

                if (basefilePath == filePath || !_diskProvider.FileExists(basefilePath))
                {
                    return NotFound();
                }

                filePath = basefilePath;
            }

            return PhysicalFile(filePath, GetContentType(filePath));
        }

        private string GetContentType(string filePath)
        {
            if (!_mimeTypeProvider.TryGetContentType(filePath, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            return contentType;
        }
    }
}
