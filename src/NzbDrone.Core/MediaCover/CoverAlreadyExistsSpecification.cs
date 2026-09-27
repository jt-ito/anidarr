using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MediaCover
{
    public interface ICoverExistsSpecification
    {
        bool AlreadyExists(string url, string path);
    }

    public class CoverAlreadyExistsSpecification : ICoverExistsSpecification
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IHttpClient _httpClient;

        public CoverAlreadyExistsSpecification(IDiskProvider diskProvider, IHttpClient httpClient)
        {
            _diskProvider = diskProvider;
            _httpClient = httpClient;
        }

        public bool AlreadyExists(string url, string path)
        {
            if (!_diskProvider.FileExists(path))
            {
                return false;
            }

            var fileSize = _diskProvider.GetFileSize(path);
            if (fileSize == 0)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            try
            {
                var headers = _httpClient.Head(new HttpRequest(url)).Headers;
                return headers.ContentLength.HasValue ? fileSize == headers.ContentLength.Value : false;
            }
            catch
            {
                return fileSize > 0;
            }
        }
    }
}
