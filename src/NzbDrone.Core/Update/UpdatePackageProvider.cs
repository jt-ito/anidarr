using System;
using System.Collections.Generic;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Analytics;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Update
{
    public interface IUpdatePackageProvider
    {
        UpdatePackage GetLatestUpdate(string branch, Version currentVersion);
        List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null);
    }

    public class UpdatePackageProvider : IUpdatePackageProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly IPlatformInfo _platformInfo;
        private readonly IAnalyticsService _analyticsService;
        private readonly IMainDatabase _mainDatabase;

        public UpdatePackageProvider(IHttpClient httpClient, ISonarrCloudRequestBuilder requestBuilder, IAnalyticsService analyticsService, IPlatformInfo platformInfo, IMainDatabase mainDatabase)
        {
            _platformInfo = platformInfo;
            _analyticsService = analyticsService;
            _requestBuilder = requestBuilder.Services;
            _httpClient = httpClient;
            _mainDatabase = mainDatabase;
        }

        public UpdatePackage GetLatestUpdate(string branch, Version currentVersion)
        {
            return null;
        }

        public List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion)
        {
            return new List<UpdatePackage>();
        }
    }
}
