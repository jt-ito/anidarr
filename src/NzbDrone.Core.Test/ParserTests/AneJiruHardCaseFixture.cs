using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class AneJiruHardCaseFixture : TestBase<ParsingService>
    {
        private Series _series;
        private List<Episode> _episodes;
        private SingleEpisodeSearchCriteria _searchCriteria;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                .With(s => s.Id = 4363)
                .With(s => s.TvdbId = 99999)
                .With(s => s.AniDbId = 4363)
                .With(s => s.PrimaryMetadataProvider = "anidb")
                .With(s => s.SeriesType = SeriesTypes.Anime)
                .With(s => s.Title = "Anejiru The Animation: Shirakawa Sanshimai ni Omakase")
                .With(s => s.CleanTitle = "anejiruanimationshirakawasanshimainiomakase")
                .With(s => s.AlternateTitles = new List<string>
                {
                    "Anejiru The Animation: Shirakawa Sanshimai ni Omakase",
                    "姉汁 THE ANIMATION 白川三姉妹におまかせ",
                    "Ane Jiru The Animation - Shirakawa San Shimai ni Omakase",
                    "Anejiru : Shirakawa San Shimai ni Omakase"
                })
                .Build();

            _episodes = new List<Episode>
            {
                new Episode
                {
                    Id = 101,
                    SeriesId = _series.Id,
                    SeasonNumber = 1,
                    EpisodeNumber = 1,
                    AbsoluteEpisodeNumber = 1,
                    Title = "Episode 1"
                },
                new Episode
                {
                    Id = 102,
                    SeriesId = _series.Id,
                    SeasonNumber = 1,
                    EpisodeNumber = 2,
                    AbsoluteEpisodeNumber = 2,
                    Title = "Episode 2"
                }
            };

            _searchCriteria = new SingleEpisodeSearchCriteria
            {
                Series = _series,
                EpisodeNumber = 1,
                SeasonNumber = 1,
                Episodes = _episodes
            };

            Mocker.GetMock<ISceneMappingService>()
                  .Setup(s => s.FindSceneMapping(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                  .Returns((SceneMapping)null);

            Mocker.GetMock<ISceneMappingService>()
                  .Setup(s => s.GetSceneSeasonNumber(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns((int?)null);

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.FindByTitle(It.IsAny<string>()))
                  .Returns((Series)null);

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisodesBySceneNumbering(It.IsAny<int>(), It.IsAny<int>()))
                  .Returns(() => new List<Episode>());

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisodesBySceneNumbering(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                  .Returns(() => new List<Episode>());

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisode(It.IsAny<int>(), It.IsAny<int>()))
                  .Returns<int, int>((seriesId, abs) => _episodes.Find(e => e.AbsoluteEpisodeNumber == abs));

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisode(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                  .Returns<int, int, int>((seriesId, season, ep) => _episodes.Find(e => e.SeasonNumber == season && e.EpisodeNumber == ep));
        }

        [Test]
        public void should_parse_torrent_title_with_absolute_episode_numbers_batch_range()
        {
            const string torrentTitle = "[Eng-Sub] [BD&IrrOn] [X-SASG] Anejiru : Shirakawa San Shimai ni Omakase - (姉汁 THE ANIMATION～白川三姉妹におまかせ ～) Ep.01-02";

            var parsed = Parser.Parser.ParseTitle(torrentTitle);

            parsed.Should().NotBeNull();
            parsed.IsAbsoluteNumbering.Should().BeTrue();
            parsed.AbsoluteEpisodeNumbers.Should().Equal(1, 2);
        }

        [Test]
        public void should_generate_policy_b_candidate_titles_with_native_japanese_guaranteed_in_slot_2()
        {
            var searchTitles = _searchCriteria.AnimeSearchTitles;

            searchTitles.Should().HaveCount(5);
            searchTitles[0].Should().Be("Anejiru The Animation: Shirakawa Sanshimai ni Omakase");
            searchTitles[1].Should().Be("姉汁 THE ANIMATION 白川三姉妹におまかせ");
            searchTitles[2].Should().Be("Ane Jiru The Animation - Shirakawa San Shimai ni Omakase");
            searchTitles[3].Should().Be("Anejiru The Animation");
            searchTitles[4].Should().Be("Anejiru : Shirakawa San Shimai ni Omakase");
        }

        [Test]
        public void should_match_series_and_map_batch_episodes_end_to_end()
        {
            const string torrentTitle = "[Eng-Sub] [BD&IrrOn] [X-SASG] Anejiru : Shirakawa San Shimai ni Omakase - (姉汁 THE ANIMATION～白川三姉妹におまかせ ～) Ep.01-02";

            var parsed = Parser.Parser.ParseTitle(torrentTitle);

            var remoteEpisode = Subject.Map(parsed, 0, 0, null, _searchCriteria);

            remoteEpisode.Should().NotBeNull();
            remoteEpisode.Series.Should().NotBeNull();
            remoteEpisode.Series.Id.Should().Be(_series.Id);
            remoteEpisode.Episodes.Should().HaveCount(2);
            remoteEpisode.Episodes[0].EpisodeNumber.Should().Be(1);
            remoteEpisode.Episodes[1].EpisodeNumber.Should().Be(2);
        }

        [Test]
        public void should_generalize_to_wider_batch_ranges()
        {
            const string torrentTitle = "[UploadGroup] Anejiru The Animation Ep.01-04";

            var parsed = Parser.Parser.ParseTitle(torrentTitle);

            parsed.Should().NotBeNull();
            parsed.IsAbsoluteNumbering.Should().BeTrue();
            parsed.AbsoluteEpisodeNumbers.Should().Equal(1, 2, 3, 4);

            _episodes.Add(new Episode
            {
                Id = 103,
                SeriesId = _series.Id,
                SeasonNumber = 1,
                EpisodeNumber = 3,
                AbsoluteEpisodeNumber = 3,
                Title = "Episode 3"
            });

            _episodes.Add(new Episode
            {
                Id = 104,
                SeriesId = _series.Id,
                SeasonNumber = 1,
                EpisodeNumber = 4,
                AbsoluteEpisodeNumber = 4,
                Title = "Episode 4"
            });

            var remoteEpisode = Subject.Map(parsed, 0, 0, null, _searchCriteria);

            remoteEpisode.Series.Should().NotBeNull();
            remoteEpisode.Series.Id.Should().Be(_series.Id);
            remoteEpisode.Episodes.Should().HaveCount(4);
            remoteEpisode.Episodes[0].EpisodeNumber.Should().Be(1);
            remoteEpisode.Episodes[1].EpisodeNumber.Should().Be(2);
            remoteEpisode.Episodes[2].EpisodeNumber.Should().Be(3);
            remoteEpisode.Episodes[3].EpisodeNumber.Should().Be(4);
        }

        [Test]
        public void should_support_relative_levenshtein_typo_fallback()
        {
            const string typoTitle = "[UploadGroup] Anijiru The Animation - 01";

            var parsed = Parser.Parser.ParseTitle(typoTitle);

            var remoteEpisode = Subject.Map(parsed, 0, 0, null, _searchCriteria);

            remoteEpisode.Series.Should().NotBeNull();
            remoteEpisode.Series.Id.Should().Be(_series.Id);
            remoteEpisode.Episodes.Should().HaveCount(1);
        }

        [Test]
        public void should_match_live_tracker_release_with_variant_casing_and_spacing()
        {
            // Real live release #7 discovered on sukebei.nyaa.si Tier 4
            const string liveRelease = "[Eng-Sub] [BD&IrrOn] [X-SASG] Anejiru: Shirakawa San Shimai Ni Omakase Ep.01-02";

            var parsed = Parser.Parser.ParseTitle(liveRelease);

            var remoteEpisode = Subject.Map(parsed, 0, 0, null, _searchCriteria);

            remoteEpisode.Should().NotBeNull();
            remoteEpisode.Series.Should().NotBeNull();
            remoteEpisode.Series.Id.Should().Be(_series.Id);
            remoteEpisode.Episodes.Should().HaveCount(2);
            remoteEpisode.Episodes[0].EpisodeNumber.Should().Be(1);
            remoteEpisode.Episodes[1].EpisodeNumber.Should().Be(2);
        }

        [Test]
        public void should_not_match_sequel_releases_to_original_series()
        {
            // Real live release #1 discovered on sukebei.nyaa.si Tier 4 (Anejiru 2 sequel)
            const string sequelRelease = "[Eng-Sub] [SubDesu-H] Anejiru 2 The Animation: Shirakawa San Shimai ni Omakase Ep.01-02";

            var parsed = Parser.Parser.ParseTitle(sequelRelease);

            var remoteEpisode = Subject.Map(parsed, 0, 0, null, _searchCriteria);

            remoteEpisode.Series.Should().BeNull();
        }
    }
}
