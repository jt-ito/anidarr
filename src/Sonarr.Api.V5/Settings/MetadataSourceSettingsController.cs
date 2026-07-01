using NzbDrone.Core.Configuration;
using Sonarr.Http;

namespace Sonarr.Api.V5.Settings;

[V5ApiController("settings/metadatasource")]
public class MetadataSourceSettingsController : SettingsController<MetadataSourceSettingsResource>
{
    public MetadataSourceSettingsController(IConfigFileProvider configFileProvider,
                                            IConfigService configService)
        : base(configFileProvider, configService)
    {
        // Add any validators for API keys here if needed
    }

    protected override MetadataSourceSettingsResource ToResource(IConfigFileProvider configFile, IConfigService model)
    {
        return MetadataSourceSettingsResourceMapper.ToResource(configFile, model);
    }
}
