using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Tv.Commands;

namespace NzbDrone.Core.Tv
{
    public class FetchAniDbRelatedSeriesService : IExecute<FetchAniDbRelatedSeriesCommand>
    {
        private readonly ISeriesService _seriesService;
        private readonly IAniDbSeriesMappingService _mappingService;
        private readonly IAniDbRelatedSeriesService _relatedSeriesService;
        private readonly IAniDbRelatedMetadataCacheRepository _cacheRepository;
        private readonly IConfigFileProvider _configService;
        private readonly IAniDbXmlClient _xmlClient;
        private readonly Logger _logger;

        public FetchAniDbRelatedSeriesService(
            ISeriesService seriesService,
            IAniDbSeriesMappingService mappingService,
            IAniDbRelatedSeriesService relatedSeriesService,
            IAniDbRelatedMetadataCacheRepository cacheRepository,
            IConfigFileProvider configService,
            IAniDbXmlClient xmlClient,
            Logger logger)
        {
            _seriesService = seriesService;
            _mappingService = mappingService;
            _relatedSeriesService = relatedSeriesService;
            _cacheRepository = cacheRepository;
            _configService = configService;
            _xmlClient = xmlClient;
            _logger = logger;
        }

        public void Execute(FetchAniDbRelatedSeriesCommand message)
        {
            if (!_configService.IsRelatedSeriesEnabled)
            {
                return;
            }

            if (!_seriesService.TryGetSeries(message.SeriesId, out var series))
            {
                return;
            }

            var mappings = _mappingService.GetMappingsForSeries(series.Id);
            var hubIds = new HashSet<int>(mappings.Select(m => m.AniDbId));

            var related = _relatedSeriesService.GetRelatedSeries(series.Id);
            var queue = new Queue<(int Id, int Depth)>();

            foreach (var r in related)
            {
                queue.Enqueue((r.RelatedAniDbId, 1));
            }

            var visited = new HashSet<int>(hubIds);
            var newRelationsFound = false;

            while (queue.Count > 0)
            {
                if (!_configService.IsRelatedSeriesEnabled)
                {
                    _logger.Info("Related series fetching disabled mid-flight. Stopping.");
                    break;
                }

                var current = queue.Dequeue();

                if (current.Depth > 10)
                {
                    _logger.Debug("Hit related series depth cap of 10 hops for AniDB ID {0}. Stopping traversal on this branch.", current.Id);
                    continue;
                }

                if (!visited.Add(current.Id))
                {
                    continue;
                }

                XDocument doc;
                try
                {
                    doc = _xmlClient.GetAnimeXml(current.Id);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to fetch AniDB XML for related series ID {0}. Skipping.", current.Id);
                    continue;
                }

                ParseAndCacheMetadata(doc, current.Id);

                var allRelations = _xmlClient.GetAllRelations(doc.Root);
                foreach (var relation in allRelations)
                {
                    if (!hubIds.Contains(relation.Id))
                    {
                        if (!related.Any(r => r.RelatedAniDbId == relation.Id))
                        {
                            related.Add(new AniDbRelatedSeries
                            {
                                SeriesId = series.Id,
                                RelatedAniDbId = relation.Id,
                                RelationType = relation.RelationType
                            });
                            newRelationsFound = true;
                        }

                        if (!visited.Contains(relation.Id))
                        {
                            queue.Enqueue((relation.Id, current.Depth + 1));
                        }
                    }
                }
            }

            if (newRelationsFound)
            {
                _relatedSeriesService.UpdateRelatedSeries(series.Id, related);
            }
        }

        private void ParseAndCacheMetadata(XDocument doc, int aniDbId)
        {
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var titleElements = doc.Root?.Elements(ns + "titles").Elements(ns + "title");
            var title = _xmlClient.GetBestTitle(titleElements, $"AniDB {aniDbId}");

            var description = doc.Root?.Element(ns + "description")?.Value;
            if (!string.IsNullOrWhiteSpace(description))
            {
                // Basic cleanup
                description = System.Text.RegularExpressions.Regex.Replace(description, @"https?://anidb\.net/[^\s\[]+\s*\[(.*?)\]", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            var posterUrl = doc.Root?.Element(ns + "picture")?.Value;
            if (!string.IsNullOrWhiteSpace(posterUrl))
            {
                posterUrl = $"https://cdn.anidb.net/images/main/{posterUrl}";
            }

            var existing = _cacheRepository.GetByAniDbId(aniDbId);
            if (existing != null)
            {
                existing.Title = title;
                existing.PosterUrl = posterUrl;
                existing.Overview = description;
                _cacheRepository.Update(existing);
            }
            else
            {
                _cacheRepository.Insert(new AniDbRelatedMetadataCache
                {
                    AniDbId = aniDbId,
                    Title = title,
                    PosterUrl = posterUrl,
                    Overview = description
                });
            }
        }
    }
}
