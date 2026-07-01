using NzbDrone.Core.Configuration;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Settings;

public class MetadataSourceSettingsResource : RestResource
{
    public string? TvdbApiKey { get; set; }
    public string? SimklClientId { get; set; }
    public string? MalClientId { get; set; }
    public string? AniDbClientName { get; set; }
    public int AniDbClientVersion { get; set; }
}

public static class MetadataSourceSettingsResourceMapper
{
    public static MetadataSourceSettingsResource ToResource(IConfigFileProvider model, IConfigService configService)
    {
        return new MetadataSourceSettingsResource
        {
            TvdbApiKey = model.TvdbApiKey,
            SimklClientId = model.SimklClientId,
            MalClientId = model.MalClientId,
            AniDbClientName = model.AniDbClientName,
            AniDbClientVersion = model.AniDbClientVersion
        };
    }
}
