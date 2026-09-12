using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class EnrichSeriesFromAniListServiceFixture : CoreTest<EnrichSeriesFromAniListService>
    {
        [Test]
        public void should_enrich_episode_air_times_with_anilist_data()
        {
            var series = new Series
            {
                Id = 1,
                Title = "BLEACH: Sennen Kessen-hen",
                AniListIds = new HashSet<int> { 185874 }
            };

            var mapping = new List<AniDbSeriesMapping>
            {
                new AniDbSeriesMapping { SeriesId = 1, SeasonNumber = 4, AniDbId = 4 }
            };

            var episode = new Episode
            {
                Id = 10,
                SeriesId = 1,
                SeasonNumber = 4,
                EpisodeNumber = 1,
                AirDate = "2026-07-25",
                AirDateUtc = new DateTime(2026, 7, 25, 23, 59, 59, DateTimeKind.Utc)
            };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(1)).Returns(series);
            Mocker.GetMock<IAniDbSeriesMappingService>().Setup(m => m.GetMappingsForSeries(1)).Returns(mapping);
            Mocker.GetMock<IEpisodeService>().Setup(e => e.GetEpisodeBySeries(1)).Returns(new List<Episode> { episode });

            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 4))
                .Returns(new AnimeOfflineTitle { AniDbId = 4, AniListId = 185874 });

            var anilistEnricherMock = Mocker.GetMock<IAniListEnricher>();
            var enrichment = new AniListEnrichmentData
            {
                AiringTimes = new Dictionary<int, Dictionary<int, TimeSpan>>
                {
                    { 185874, new Dictionary<int, TimeSpan> { { 1, new TimeSpan(23, 0, 0) } } } // 23:00 JST = 14:00 UTC
                }
            };
            anilistEnricherMock.Setup(c => c.GetEnrichmentForMultiple(It.IsAny<IEnumerable<int>>()))
                .Returns(enrichment);

            Subject.Execute(new EnrichSeriesFromAniListCommand(1));

            // Episode should be enriched to 14:00 UTC on 2026-07-25
            episode.AirDateUtc.Should().Be(new DateTime(2026, 7, 25, 14, 0, 0, DateTimeKind.Utc));
            Mocker.GetMock<IEpisodeService>().Verify(e => e.UpdateEpisodes(It.Is<List<Episode>>(l => l.Contains(episode))), Times.Once);
        }

        [Test]
        public void should_handle_calendar_day_rollover_when_jst_crosses_midnight_relative_to_utc()
        {
            var series = new Series
            {
                Id = 1,
                Title = "Test Anime",
                AniListIds = new HashSet<int> { 185874 }
            };

            var mapping = new List<AniDbSeriesMapping>
            {
                new AniDbSeriesMapping { SeriesId = 1, SeasonNumber = 1, AniDbId = 1 }
            };

            var episode = new Episode
            {
                Id = 10,
                SeriesId = 1,
                SeasonNumber = 1,
                EpisodeNumber = 1,
                AirDate = "2026-07-26",
                AirDateUtc = new DateTime(2026, 7, 26, 23, 59, 59, DateTimeKind.Utc)
            };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(1)).Returns(series);
            Mocker.GetMock<IAniDbSeriesMappingService>().Setup(m => m.GetMappingsForSeries(1)).Returns(mapping);
            Mocker.GetMock<IEpisodeService>().Setup(e => e.GetEpisodeBySeries(1)).Returns(new List<Episode> { episode });

            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 1))
                .Returns(new AnimeOfflineTitle { AniDbId = 1, AniListId = 185874 });

            var anilistEnricherMock = Mocker.GetMock<IAniListEnricher>();
            var enrichment = new AniListEnrichmentData
            {
                AiringTimes = new Dictionary<int, Dictionary<int, TimeSpan>>
                {
                    { 185874, new Dictionary<int, TimeSpan> { { 1, new TimeSpan(2, 0, 0) } } } // 02:00 JST
                }
            };
            anilistEnricherMock.Setup(c => c.GetEnrichmentForMultiple(It.IsAny<IEnumerable<int>>()))
                .Returns(enrichment);

            Subject.Execute(new EnrichSeriesFromAniListCommand(1));

            // 02:00 JST on July 26th = 17:00 UTC on July 25th (rolls backwards across calendar boundary)
            episode.AirDateUtc.Should().Be(new DateTime(2026, 7, 25, 17, 0, 0, DateTimeKind.Utc));
            episode.AirDateUtc.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void should_fallback_to_title_search_and_cache_result_when_anilist_id_missing()
        {
            var series = new Series
            {
                Id = 1,
                Title = "Test Anime Fallback",
                Year = 2026,
                AniListIds = new HashSet<int>()
            };

            var mapping = new List<AniDbSeriesMapping>
            {
                new AniDbSeriesMapping { SeriesId = 1, SeasonNumber = 1, AniDbId = 1 }
            };

            var episode = new Episode
            {
                Id = 10,
                SeriesId = 1,
                SeasonNumber = 1,
                EpisodeNumber = 1,
                AirDate = "2026-07-26",
                AirDateUtc = new DateTime(2026, 7, 26, 23, 59, 59, DateTimeKind.Utc)
            };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(1)).Returns(series);
            Mocker.GetMock<IAniDbSeriesMappingService>().Setup(m => m.GetMappingsForSeries(1)).Returns(mapping);
            Mocker.GetMock<IEpisodeService>().Setup(e => e.GetEpisodeBySeries(1)).Returns(new List<Episode> { episode });

            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            var localSeries = new AnimeOfflineTitle { AniDbId = 1, Title = "Test Anime Fallback" }; // Missing AniListId
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 1)).Returns(localSeries);

            var anilistEnricherMock = Mocker.GetMock<IAniListEnricher>();
            anilistEnricherMock.Setup(c => c.SearchAniListIdByTitle("Test Anime Fallback", 2026, 1)).Returns(185874);

            Subject.Execute(new EnrichSeriesFromAniListCommand(1));

            // Verify fallback search was called
            anilistEnricherMock.Verify(c => c.SearchAniListIdByTitle("Test Anime Fallback", 2026, 1), Times.Once);

            // Verify caching occurred
            titleSearchMock.Verify(c => c.UpdateAniListId(1, 185874), Times.Once);

            // Verify series was updated with new AniListId
            series.AniListIds.Should().Contain(185874);
            Mocker.GetMock<ISeriesService>().Verify(s => s.UpdateSeries(series), Times.Once);
        }

        [Test]
        public void should_ignore_ambiguous_matches_during_fallback_search()
        {
            var series = new Series
            {
                Id = 1,
                Title = "Ambiguous Anime",
                Year = 2026,
                AniListIds = new HashSet<int>()
            };

            var mapping = new List<AniDbSeriesMapping>
            {
                new AniDbSeriesMapping { SeriesId = 1, SeasonNumber = 1, AniDbId = 1 }
            };

            var episode = new Episode
            {
                Id = 10,
                SeriesId = 1,
                SeasonNumber = 1,
                EpisodeNumber = 1,
                AirDate = "2026-07-26",
                AirDateUtc = new DateTime(2026, 7, 26, 23, 59, 59, DateTimeKind.Utc)
            };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(1)).Returns(series);
            Mocker.GetMock<IAniDbSeriesMappingService>().Setup(m => m.GetMappingsForSeries(1)).Returns(mapping);
            Mocker.GetMock<IEpisodeService>().Setup(e => e.GetEpisodeBySeries(1)).Returns(new List<Episode> { episode });

            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            var localSeries = new AnimeOfflineTitle { AniDbId = 1, Title = "Ambiguous Anime" }; // Missing AniListId
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 1)).Returns(localSeries);

            var anilistEnricherMock = Mocker.GetMock<IAniListEnricher>();
            anilistEnricherMock.Setup(c => c.SearchAniListIdByTitle("Ambiguous Anime", 2026, 1)).Returns((int?)null);

            Subject.Execute(new EnrichSeriesFromAniListCommand(1));

            // Episode air date remains untouched fallback
            episode.AirDateUtc.Should().Be(new DateTime(2026, 7, 26, 23, 59, 59, DateTimeKind.Utc));
            anilistEnricherMock.Verify(c => c.SearchAniListIdByTitle("Ambiguous Anime", 2026, 1), Times.Once);
            titleSearchMock.Verify(c => c.UpdateAniListId(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void should_dedup_anilist_titles_ignoring_diacritics()
        {
            var series = new Series
            {
                Id = 1,
                Title = "The Misfit of Demon King Academy",
                AniListIds = new HashSet<int> { 185874 },
                AlternateTitles = new List<string> { "Maou Gakuin no Futekigousha", "Demon King Academy" }
            };

            var mapping = new List<AniDbSeriesMapping>
            {
                new AniDbSeriesMapping { SeriesId = 1, SeasonNumber = 1, AniDbId = 1 }
            };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(1)).Returns(series);
            Mocker.GetMock<IAniDbSeriesMappingService>().Setup(m => m.GetMappingsForSeries(1)).Returns(mapping);
            Mocker.GetMock<IEpisodeService>().Setup(e => e.GetEpisodeBySeries(1)).Returns(new List<Episode>());

            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 1))
                .Returns(new AnimeOfflineTitle { AniDbId = 1, AniListId = 185874 });

            var anilistEnricherMock = Mocker.GetMock<IAniListEnricher>();
            var enrichment = new AniListEnrichmentData
            {
                Titles = new Dictionary<int, List<string>>
                {
                    { 185874, new List<string> { "Maou Gakuin no Futekigousha", "Another Title" } }
                }
            };
            anilistEnricherMock.Setup(c => c.GetEnrichmentForMultiple(It.IsAny<IEnumerable<int>>()))
                .Returns(enrichment);

            Subject.Execute(new EnrichSeriesFromAniListCommand(1));

            series.AlternateTitles.Should().Contain("Maou Gakuin no Futekigousha");
            series.AlternateTitles.Should().Contain("Demon King Academy");
            series.AlternateTitles.Should().Contain("Another Title");
            series.AlternateTitles.Count(t => t == "Maou Gakuin no Futekigousha").Should().Be(1);
            Mocker.GetMock<ISeriesService>().Verify(s => s.UpdateSeries(series), Times.Once);
        }

        [Test]
        public void should_use_exact_fallback_order_for_anidb_16067()
        {
            var sequence = new List<string>();
            var anilistEnricherMock = Mocker.GetMock<IAniListEnricher>();
            anilistEnricherMock.Setup(c => c.SearchAniListIdByTitle(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()))
                .Returns((int?)null)
                .Callback<string, int, int?>((t, y, e) => sequence.Add(t));

            var series = new Series
            {
                Id = 16067,
                Title = "Test Anime",
                Year = 2026,
                AniListIds = new HashSet<int>()
            };

            var mapping = new List<AniDbSeriesMapping>
            {
                new AniDbSeriesMapping { SeriesId = 16067, SeasonNumber = 1, AniDbId = 16067 }
            };

            var episode = new Episode
            {
                Id = 1,
                SeriesId = 16067,
                SeasonNumber = 1,
                EpisodeNumber = 1,
                AirDate = "2026-07-26",
                AirDateUtc = new DateTime(2026, 7, 26, 23, 59, 59, DateTimeKind.Utc)
            };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(16067)).Returns(series);
            Mocker.GetMock<IAniDbSeriesMappingService>().Setup(m => m.GetMappingsForSeries(16067)).Returns(mapping);
            Mocker.GetMock<IEpisodeService>().Setup(e => e.GetEpisodeBySeries(16067)).Returns(new List<Episode> { episode });

            var titleSearchMock = Mocker.GetMock<IAnimeOfflineDatabase>();
            var localSeries = new AnimeOfflineTitle
            {
                AniDbId = 16067,
                RomajiTitle = "Uchi no Otouto Maji de Dekain Dakedo Mi ni Konai?",
                NativeTitle = "ウチの弟マジでデカイんだけど見にこない",
                EnglishTitle = "My Little Brother Is Huge as Hell. Wanna Come over and See?",
                SearchSynonyms = new List<string> { "우리동생진짜큰데보러안올래" }
            };
            titleSearchMock.Setup(x => x.GetSeriesById("anidb", 16067)).Returns(localSeries);

            Subject.Execute(new EnrichSeriesFromAniListCommand(16067));

            sequence.Should().Equal(
                "Uchi no Otouto Maji de Dekain Dakedo Mi ni Konai?",
                "ウチの弟マジでデカイんだけど見にこない",
                "My Little Brother Is Huge as Hell. Wanna Come over and See?",
                "우리동생진짜큰데보러안올래",
                "Test Anime");
        }
    }
}
