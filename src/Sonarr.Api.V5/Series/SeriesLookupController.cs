using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.SeriesStats;
using Sonarr.Http;

namespace Sonarr.Api.V5.Series;

public class LookupMatchResource
{
    public string Status { get; set; } = "none";

    // Why the status was chosen (shown as a hint)
    public string Reason { get; set; } = string.Empty;

    // Video files found in the folder
    public int FileCount { get; set; }

    // The folder name without release tags (what was searched for)
    public string SearchTerm { get; set; } = string.Empty;

    public List<SeriesResource> Results { get; set; } = new();
}

public class ImportChoiceResource
{
    public string Term { get; set; } = string.Empty;

    public int TvdbId { get; set; }

    public int AniDbId { get; set; }

    public string? Title { get; set; }
}

[V5ApiController("series/lookup")]
public class SeriesLookupController : Controller
{
    private readonly ISearchForNewSeries _searchProxy;
    private readonly IMetadataDispatcher _metadataDispatcher;
    private readonly IFolderLookupService _folderLookupService;
    private readonly IImportChoiceStore _importChoiceStore;
    private readonly IAniDbPrefetchService _aniDbPrefetchService;
    private readonly IRootFolderService _rootFolderService;
    private readonly IBuildFileNames _fileNameBuilder;
    private readonly IMapCoversToLocal _coverMapper;
    private readonly IImportListExclusionService _importListExclusionService;

    public SeriesLookupController(ISearchForNewSeries searchProxy,
                                  IMetadataDispatcher metadataDispatcher,
                                  IFolderLookupService folderLookupService,
                                  IImportChoiceStore importChoiceStore,
                                  IAniDbPrefetchService aniDbPrefetchService,
                                  IRootFolderService rootFolderService,
                                  IBuildFileNames fileNameBuilder,
                                  IMapCoversToLocal coverMapper,
                                  IImportListExclusionService importListExclusionService)
    {
        _searchProxy = searchProxy;
        _metadataDispatcher = metadataDispatcher;
        _folderLookupService = folderLookupService;
        _importChoiceStore = importChoiceStore;
        _aniDbPrefetchService = aniDbPrefetchService;
        _rootFolderService = rootFolderService;
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

    // Library import: finds the series a folder holds and says how sure it is, so callers
    // can pick it automatically (matched), offer choices (possible), or leave it alone (none).
    // `path` (optional) is the folder itself, so ids, a tvshow.nfo and episode file names can be used.
    [HttpGet("match")]
    public Ok<LookupMatchResource> Match([FromQuery] string term, [FromQuery] string? provider = null, [FromQuery] string? path = null, [FromQuery] bool background = false)
    {
        // Anything the user asked for (typing, refresh) goes ahead of the bulk import queue in the
        // central AniDB limiter. Bulk work (background) keeps the low priority and waits its turn.
        NzbDrone.Core.MetadataSource.AniDb.AniDbRateLimiter.IsManualContext.Value = !background;

        MetadataProviderType? providerType = !string.IsNullOrWhiteSpace(provider) &&
                                             Enum.TryParse<MetadataProviderType>(provider, ignoreCase: true, out var parsed)
            ? parsed
            : null;

        var match = _folderLookupService.Lookup(term, IsInsideRootFolder(path) ? path : null, providerType);

        return TypedResults.Ok(new LookupMatchResource
        {
            Status = match.Status.ToString().ToLowerInvariant(),
            Reason = match.Reason,
            FileCount = match.FileCount,
            SearchTerm = match.SearchTerm,
            Results = MapToResource(match.Results).ToList()
        });
    }

    // Remembers which series the user picked for a folder name
    [HttpPost("choice")]
    public Ok RememberChoice([FromBody] ImportChoiceResource choice)
    {
        if (choice.TvdbId > 0)
        {
            _importChoiceStore.Remember(choice.Term, "tvdb", choice.TvdbId, choice.Title);
        }
        else if (choice.AniDbId > 0)
        {
            _importChoiceStore.Remember(choice.Term, "anidb", choice.AniDbId, choice.Title);

            // They are about to import it: have its hub data cached
            _aniDbPrefetchService.Enqueue(choice.AniDbId);
        }

        return TypedResults.Ok();
    }

    // The server only reads inside folders the user configured as root folders
    private bool IsInsideRootFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return _rootFolderService.All().Any(root => root.Path.IsParentPath(path));
    }

    [HttpGet("prewarm")]
    public IActionResult Prewarm([FromQuery] int anidbId)
    {
        if (anidbId <= 0)
        {
            return BadRequest("Invalid anidbId");
        }

        global::System.Threading.Tasks.Task.Run(() =>
        {
            NzbDrone.Core.MetadataSource.AniDb.AniDbRateLimiter.IsManualContext.Value = true;
            NzbDrone.Core.MetadataSource.AniList.AniListRateLimiter.IsManualContext.Value = true;

            try
            {
                _metadataDispatcher.GetSeriesInfo(new NzbDrone.Core.Tv.Series
                {
                    AniDbId = anidbId,
                    PrimaryMetadataProvider = "anidb"
                });
            }
            catch
            {
                // Best-effort pre-warming
            }
        });

        return Ok(new { prewarmed = true });
    }

    private IEnumerable<SeriesResource> MapToResource(IEnumerable<NzbDrone.Core.Tv.Series> series)
    {
        var exclusions = _importListExclusionService.All();
        var tvdbExclusions = new HashSet<int>(exclusions.Where(e => e.TvdbId > 0).Select(e => e.TvdbId));
        var anidbExclusions = new HashSet<int>(exclusions.Where(e => e.AniDbId.HasValue).Select(e => e.AniDbId!.Value));
        var anilistExclusions = new HashSet<int>(exclusions.Where(e => e.AniListId.HasValue).Select(e => e.AniListId!.Value));
        var malExclusions = new HashSet<int>(exclusions.Where(e => e.MalId.HasValue).Select(e => e.MalId!.Value));

        foreach (var currentSeries in series)
        {
            var resource = currentSeries.ToResource();

            _coverMapper.ConvertToLocalUrls(resource.Id, resource.Images);

            var poster = currentSeries.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster);

            if (poster != null)
            {
                resource.RemotePoster = string.IsNullOrWhiteSpace(poster.RemoteUrl) ? poster.Url : poster.RemoteUrl;
            }

            resource.Folder = _fileNameBuilder.GetSeriesFolder(currentSeries);
            resource.Statistics = new SeriesStatistics().ToResource(resource.Seasons);
            resource.IsExcluded = currentSeries.PrimaryMetadataProvider switch
            {
                "anidb" => currentSeries.AniDbId.HasValue && anidbExclusions.Contains(currentSeries.AniDbId.Value),
                "anilist" => currentSeries.AniListIds != null && currentSeries.AniListIds.Any(id => anilistExclusions.Contains(id)),
                "mal" => currentSeries.MalIds != null && currentSeries.MalIds.Any(id => malExclusions.Contains(id)),
                _ => currentSeries.TvdbId > 0 && tvdbExclusions.Contains(currentSeries.TvdbId)
            };

            yield return resource;
        }
    }
}
