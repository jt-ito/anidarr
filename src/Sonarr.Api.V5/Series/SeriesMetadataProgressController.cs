using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V5.Series;

public class SeriesMetadataProgressResource
{
    // AniDB series still waiting for their hub, seasons and episodes
    public int Pending { get; set; }

    public int Done { get; set; }

    public int Total { get; set; }

    // Set while AniDB is rate limiting this client: nothing can be updated until then
    public DateTime? BlockedUntil { get; set; }

    // False when the AniDB client name/version are not set: nothing can be updated until they are
    public bool ClientConfigured { get; set; } = true;
}

[V5ApiController("series/metadata-progress")]
public class SeriesMetadataProgressController : Controller
{
    private readonly ISeriesService _seriesService;
    private readonly IPendingMetadataTracker _pendingMetadataTracker;
    private readonly IConfigFileProvider _configFileProvider;

    public SeriesMetadataProgressController(ISeriesService seriesService,
                                            IPendingMetadataTracker pendingMetadataTracker,
                                            IConfigFileProvider configFileProvider)
    {
        _seriesService = seriesService;
        _pendingMetadataTracker = pendingMetadataTracker;
        _configFileProvider = configFileProvider;
    }

    [HttpGet]
    public Ok<SeriesMetadataProgressResource> GetProgress()
    {
        var progress = _pendingMetadataTracker.GetProgress(_seriesService.CountPendingMetadata());
        var blockedUntil = _configFileProvider.AniDbBanExpiration;

        return TypedResults.Ok(new SeriesMetadataProgressResource
        {
            Pending = progress.Pending,
            Done = progress.Done,
            Total = progress.Total,
            BlockedUntil = blockedUntil.HasValue && blockedUntil.Value > DateTime.UtcNow ? blockedUntil : null,
            ClientConfigured = _configFileProvider.IsAniDbClientConfigured
        });
    }
}
