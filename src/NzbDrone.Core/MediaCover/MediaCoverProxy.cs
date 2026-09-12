using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaCover
{
    public interface IMediaCoverProxy
    {
        string RegisterUrl(string url);

        string GetUrl(string hash);
        Task<byte[]> GetImage(string hash);
    }

    public class MediaCoverProxy : IMediaCoverProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly ICached<string> _cache;

        public MediaCoverProxy(IHttpClient httpClient, IConfigFileProvider configFileProvider, ICacheManager cacheManager, IAppFolderInfo appFolderInfo)
        {
            _httpClient = httpClient;
            _configFileProvider = configFileProvider;
            _cache = cacheManager.GetCache<string>(GetType());
            _appFolderInfo = appFolderInfo;
        }

        public string RegisterUrl(string url)
        {
            if (url.IsNullOrWhiteSpace())
            {
                return null;
            }

            var hash = url.SHA256Hash();

            _cache.Set(hash, url, TimeSpan.FromHours(24));

            _cache.ClearExpired();

            var fileName = Path.GetFileName(url);
            return _configFileProvider.UrlBase + @"/MediaCoverProxy/" + hash + "/" + fileName;
        }

        public string GetUrl(string hash)
        {
            var result = _cache.Find(hash);

            if (result == null)
            {
                throw new KeyNotFoundException("Url no longer in cache");
            }

            return result;
        }

        public async Task<byte[]> GetImage(string hash)
        {
            var cacheFolder = Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover", "ProxyCache");
            var cacheFile = Path.Combine(cacheFolder, $"{hash}.bin");

            if (File.Exists(cacheFile) && new FileInfo(cacheFile).Length > 0)
            {
                return await File.ReadAllBytesAsync(cacheFile);
            }

            var url = GetUrl(hash);

            var request = new HttpRequest(url);
            var response = await _httpClient.GetAsync(request);

            if (response.ResponseData != null && response.ResponseData.Length > 0)
            {
                try
                {
                    if (!Directory.Exists(cacheFolder))
                    {
                        Directory.CreateDirectory(cacheFolder);
                    }

                    await File.WriteAllBytesAsync(cacheFile, response.ResponseData);
                }
                catch
                {
                    // Non-fatal if cache write fails
                }
            }

            return response.ResponseData;
        }
    }
}
