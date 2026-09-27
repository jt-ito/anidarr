using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaCover;

namespace Sonarr.Http.Frontend.Mappers
{
    public class MediaCoverMapper : StaticResourceMapperBase
    {
        private static readonly Regex RegexResizedImage = new Regex(@"-\d+\.jpg($|\?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RegexCoverUrl = new(@"^/MediaCover/(?<seriesId>\d+)/(?<coverType>[a-zA-Z]+)(?:-(?<height>\d+))?\.(?<ext>[a-zA-Z0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly IContentTypeProvider _mimeTypeProvider;

        public MediaCoverMapper(IAppFolderInfo appFolderInfo,
                                IDiskProvider diskProvider,
                                Logger logger,
                                IMapCoversToLocal mediaCoverService = null)
            : base(diskProvider, logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _mediaCoverService = mediaCoverService;
            _mimeTypeProvider = new FileExtensionContentTypeProvider();
        }

        protected override string FolderPath => Path.Combine(_appFolderInfo.GetAppDataPath(), "MediaCover");

        protected override string MapPath(string resourceUrl)
        {
            var path = resourceUrl.Replace('/', Path.DirectorySeparatorChar);
            path = path.Trim(Path.DirectorySeparatorChar);

            var resourcePath = Path.Combine(_appFolderInfo.GetAppDataPath(), path);

            if (!_diskProvider.FileExists(resourcePath) || _diskProvider.GetFileSize(resourcePath) == 0)
            {
                var baseResourcePath = RegexResizedImage.Replace(resourcePath, ".jpg$1");
                if (baseResourcePath != resourcePath)
                {
                    return baseResourcePath;
                }
            }

            return resourcePath;
        }

        public override bool CanHandle(string resourceUrl)
        {
            return resourceUrl.StartsWith("/MediaCover/", StringComparison.InvariantCultureIgnoreCase);
        }

        public override async Task<IActionResult> GetResponse(HttpContext context, string resourceUrl)
        {
            var result = await base.GetResponse(context, resourceUrl);
            if (result != null)
            {
                return result;
            }

            if (_mediaCoverService == null)
            {
                return null;
            }

            var match = RegexCoverUrl.Match(resourceUrl);
            if (match.Success)
            {
                var seriesId = int.Parse(match.Groups["seriesId"].Value);
                var coverTypeStr = match.Groups["coverType"].Value;
                var heightStr = match.Groups["height"].Value;
                int? height = string.IsNullOrEmpty(heightStr) ? null : int.Parse(heightStr);

                if (Enum.TryParse<MediaCoverTypes>(coverTypeStr, true, out var coverType))
                {
                    var resolvedPath = _mediaCoverService.EnsureCover(seriesId, coverType, height);
                    if (resolvedPath != null && _diskProvider.FileExists(resolvedPath) && _diskProvider.GetFileSize(resolvedPath) > 0)
                    {
                        if (!_mimeTypeProvider.TryGetContentType(resolvedPath, out var contentType))
                        {
                            contentType = "application/octet-stream";
                        }

                        return new FileStreamResult(GetContentStream(context, resolvedPath), new MediaTypeHeaderValue(contentType)
                        {
                            Encoding = contentType == "text/plain" ? Encoding.UTF8 : null
                        });
                    }
                }
            }

            return null;
        }
    }
}
