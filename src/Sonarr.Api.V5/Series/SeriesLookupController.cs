using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.SeriesStats;
using Sonarr.Http;

namespace Sonarr.Api.V5.Series;

[V5ApiController("series/lookup")]
public class SeriesLookupController : Controller
{
    private readonly ISearchForNewSeries _searchProxy;
    private readonly IMetadataDispatcher _metadataDispatcher;
    private readonly IBuildFileNames _fileNameBuilder;
    private readonly IMapCoversToLocal _coverMapper;
    private readonly IImportListExclusionService _importListExclusionService;

    public SeriesLookupController(ISearchForNewSeries searchProxy,
                                  IMetadataDispatcher metadataDispatcher,
                                  IBuildFileNames fileNameBuilder,
                                  IMapCoversToLocal coverMapper,
                                  IImportListExclusionService importListExclusionService)
    {
        _searchProxy = searchProxy;
        _metadataDispatcher = metadataDispatcher;
        _fileNameBuilder = fileNameBuilder;
        _coverMapper = coverMapper;
        _importListExclusionService = importListExclusionService;
    }

    [HttpGet]
    public Ok<IEnumerable<SeriesResource>> Search([FromQuery] string term, [FromQuery] string? provider = null)
    {
        IEnumerable<NzbDrone.Core.Tv.Series> results;

        if (!string.IsNullOrWhiteSpace(provider) &&
            Enum.TryParse<MetadataProviderType>(provider, ignoreCase: true, out var providerType))
        {
            if (providerType == MetadataProviderType.AniDb)
            {
                NzbDrone.Core.MetadataSource.AniDb.AniDbRateLimiter.IsManualContext.Value = true;
            }

            results = _metadataDispatcher.Search(term, providerType);
        }
        else
        {
            results = _searchProxy.SearchForNewSeries(term);
        }

        return TypedResults.Ok(MapToResource(results));
    }

    private IEnumerable<SeriesResource> MapToResource(IEnumerable<NzbDrone.Core.Tv.Series> series)
    {
        foreach (var currentSeries in series)
        {
            var resource = currentSeries.ToResource();

            _coverMapper.ConvertToLocalUrls(resource.Id, resource.Images);

            var poster = currentSeries.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster);

            if (poster != null)
            {
                resource.RemotePoster = poster.RemoteUrl;
            }

            resource.Folder = _fileNameBuilder.GetSeriesFolder(currentSeries);
            resource.Statistics = new SeriesStatistics().ToResource(resource.Seasons);
            resource.IsExcluded = currentSeries.PrimaryMetadataProvider switch
            {
                "anidb" => currentSeries.AniDbId.HasValue && _importListExclusionService.FindByAniDbId(currentSeries.AniDbId.Value) is not null,
                "anilist" => currentSeries.AniListIds != null && currentSeries.AniListIds.Any(id => _importListExclusionService.FindByAniListId(id) is not null),
                "mal" => currentSeries.MalIds != null && currentSeries.MalIds.Any(id => _importListExclusionService.FindByMalId(id) is not null),
                _ => currentSeries.TvdbId > 0 && _importListExclusionService.FindByTvdbId(currentSeries.TvdbId) is not null
            };

            yield return resource;
        }
    }
}
