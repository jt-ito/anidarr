using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Qualities;
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

        [Test]
        public void should_parse_adult_anime_liquid_volume_and_strip_1024x576_dimension()
        {
            const string release = "(18禁アニメ) (ピンクパイナップル) 姉汁2 THE ANIMATION～白川三姉妹におまかせ～ Liquid.1「オナニーのお手伝いしてあげる♪」(DVD 1024x576 H.264 AAC)";

            var parsed = Parser.Parser.ParseTitle(release);

            parsed.Should().NotBeNull();
            parsed.SeasonNumber.Should().NotBe(1024);
            parsed.AbsoluteEpisodeNumbers.Should().Equal(1);
            parsed.Quality.Quality.Should().Be(Quality.DVD);
        }

        [Test]
        public void should_parse_liquid_multi_episode_pack()
        {
            const string release = "[ピンクパイナップル]姉汁2 THE ANIMATION～白川三姉妹におまかせ～ Liquid.1 + Liquid.2 / CHI SUBS";

            var parsed = Parser.Parser.ParseTitle(release);

            parsed.Should().NotBeNull();
            parsed.AbsoluteEpisodeNumbers.Should().Equal(1, 2);
        }

        [Test]
        public void should_match_sequel_release_when_searching_for_season_2()
        {
            var s2Episodes = new List<Episode>
            {
                new Episode
                {
                    Id = 201,
                    SeriesId = _series.Id,
                    SeasonNumber = 2,
                    EpisodeNumber = 1,
                    AbsoluteEpisodeNumber = 3,
                    Title = "Season 2 Episode 1"
                },
                new Episode
                {
                    Id = 202,
                    SeriesId = _series.Id,
                    SeasonNumber = 2,
                    EpisodeNumber = 2,
                    AbsoluteEpisodeNumber = 4,
                    Title = "Season 2 Episode 2"
                }
            };

            var s2SearchCriteria = new SeasonSearchCriteria
            {
                Series = _series,
                SeasonNumber = 2,
                TargetSeasonNumber = 2,
                SeasonTitle = "Ane Jiru 2 The Animation: Shirakawa San Shimai ni Omakase",
                SeasonAlternateTitles = new List<string>
                {
                    "姉汁2 THE ANIMATION～白川三姉妹におまかせ～",
                    "Anejiru 2 The Animation",
                    "Anejiru 2: Shirakawa-san Shimai ni Omakase",
                    "Anejiru 2"
                },
                Episodes = s2Episodes
            };

            const string sequelRelease = "[Eng-Sub] [SubDesu-H] Anejiru 2 : Shirakawa San Shimai ni Omakase - (姉汁2 The Animation ～白川三姉妹におまかせ ～) Ep.01-02";

            var parsed = Parser.Parser.ParseTitle(sequelRelease);

            var remoteEpisode = Subject.Map(parsed, 0, 0, null, s2SearchCriteria);

            remoteEpisode.Should().NotBeNull();
            remoteEpisode.Series.Should().NotBeNull();
            remoteEpisode.Series.Id.Should().Be(_series.Id);
            remoteEpisode.EpisodeRequested.Should().BeTrue();
            remoteEpisode.Episodes.Should().HaveCount(2);
            remoteEpisode.Episodes[0].SeasonNumber.Should().Be(2);
            remoteEpisode.Episodes[0].EpisodeNumber.Should().Be(1);
            remoteEpisode.Episodes[1].SeasonNumber.Should().Be(2);
            remoteEpisode.Episodes[1].EpisodeNumber.Should().Be(2);
        }

        [Test]
        public void should_match_subdesu_japanese_lead_sequel_release_for_season_2()
        {
            var s2Episodes = new List<Episode>
            {
                new Episode
                {
                    Id = 201,
                    SeriesId = _series.Id,
                    SeasonNumber = 2,
                    EpisodeNumber = 1,
                    AbsoluteEpisodeNumber = 3,
                    Title = "Season 2 Episode 1"
                }
            };

            var s2SearchCriteria = new SingleEpisodeSearchCriteria
            {
                Series = _series,
                SeasonNumber = 2,
                TargetSeasonNumber = 2,
                SeasonTitle = "Ane Jiru 2 The Animation: Shirakawa San Shimai ni Omakase",
                SeasonAlternateTitles = new List<string>
                {
                    "姉汁2 THE ANIMATION～白川三姉妹におまかせ～",
                    "Anejiru 2 The Animation",
                    "Anejiru 2"
                },
                EpisodeNumber = 1,
                Episodes = s2Episodes
            };

            const string release = "姉汁2 THE ANIMATION～白川三姉妹におまかせ～ [SubDESU-H] Anejiru 2 Shirakawa Shimai ni Omakase - ep 01";

            var parsed = Parser.Parser.ParseTitle(release);

            var remoteEpisode = Subject.Map(parsed, 0, 0, null, s2SearchCriteria);

            remoteEpisode.Should().NotBeNull();
            remoteEpisode.Series.Should().NotBeNull();
            remoteEpisode.Series.Id.Should().Be(_series.Id);
            remoteEpisode.EpisodeRequested.Should().BeTrue();
            remoteEpisode.Episodes.Should().HaveCount(1);
            remoteEpisode.Episodes[0].SeasonNumber.Should().Be(2);
            remoteEpisode.Episodes[0].EpisodeNumber.Should().Be(1);
        }

        [Test]
        public void should_not_match_season_1_release_as_season_2_when_searching_for_season_2()
        {
            var s2Episodes = new List<Episode>
            {
                new Episode
                {
                    Id = 201,
                    SeriesId = _series.Id,
                    SeasonNumber = 2,
                    EpisodeNumber = 1,
                    AbsoluteEpisodeNumber = 3,
                    Title = "Season 2 Episode 1"
                },
                new Episode
                {
                    Id = 202,
                    SeriesId = _series.Id,
                    SeasonNumber = 2,
                    EpisodeNumber = 2,
                    AbsoluteEpisodeNumber = 4,
                    Title = "Season 2 Episode 2"
                }
            };

            var s2SearchCriteria = new SeasonSearchCriteria
            {
                Series = _series,
                SeasonNumber = 2,
                TargetSeasonNumber = 2,
                SeasonTitle = "Ane Jiru 2 The Animation: Shirakawa San Shimai ni Omakase",
                SeasonAlternateTitles = new List<string>
                {
                    "姉汁2 THE ANIMATION～白川三姉妹におまかせ～",
                    "Anejiru 2 The Animation",
                    "Anejiru 2: Shirakawa-san Shimai ni Omakase",
                    "Anejiru 2"
                },
                Episodes = s2Episodes
            };

            const string s1Release = "[Eng-Sub] [BD&IrrOn] [X-SASG] Anejiru : Shirakawa San Shimai ni Omakase - (姉汁 THE ANIMATION～白川三姉妹におまかせ ～) Ep.01-02";

            var parsed = Parser.Parser.ParseTitle(s1Release);

            var remoteEpisode = Subject.Map(parsed, 0, 0, null, s2SearchCriteria);

            remoteEpisode.Should().NotBeNull();
            remoteEpisode.Series.Should().NotBeNull();
            remoteEpisode.Series.Id.Should().Be(_series.Id);
            remoteEpisode.EpisodeRequested.Should().BeFalse();
            remoteEpisode.Episodes.Should().HaveCount(2);
            remoteEpisode.Episodes[0].SeasonNumber.Should().Be(1);
            remoteEpisode.Episodes[0].EpisodeNumber.Should().Be(1);
            remoteEpisode.Episodes[1].SeasonNumber.Should().Be(1);
            remoteEpisode.Episodes[1].EpisodeNumber.Should().Be(2);
        }

        [Test]
        public void should_not_match_s2_release_as_s1_episodes_when_searching_for_season_1_even_with_combined_alternate_titles()
        {
            var seriesWithAllAlts = Builder<Series>.CreateNew()
                .With(s => s.Id = 4363)
                .With(s => s.TvdbId = 99999)
                .With(s => s.AniDbId = 4363)
                .With(s => s.PrimaryMetadataProvider = "anidb")
                .With(s => s.SeriesType = SeriesTypes.Anime)
                .With(s => s.Title = "Big Sister Juice the Animation: Leave the Three Sisters to Shirakawa")
                .With(s => s.CleanTitle = "bigsisterjuicetheanimationleavethethreesisterstoshirakawa")
                .With(s => s.AlternateTitles = new List<string>
                {
                    "Anejiru The Animation: Shirakawa Sanshimai ni Omakase",
                    "姉汁 THE ANIMATION 白川三姉妹におまかせ",
                    "Ane Jiru The Animation - Shirakawa San Shimai ni Omakase",
                    "Anejiru : Shirakawa San Shimai ni Omakase",
                    "Ane Jiru 2",
                    "Anejiru 2 The Animation",
                    "姉汁2 THE ANIMATION～白川三姉妹におまかせ～"
                })
                .With(s => s.Seasons = new List<Season>
                {
                    new Season { SeasonNumber = 1, Title = "Anejiru : Shirakawa San Shimai ni Omakase" },
                    new Season { SeasonNumber = 2, Title = "Ane Jiru 2 The Animation: Shirakawa San Shimai ni Omakase" }
                })
                .Build();

            var s1SearchCriteria = new SeasonSearchCriteria
            {
                Series = seriesWithAllAlts,
                SeasonNumber = 1,
                TargetSeasonNumber = 1,
                SeasonTitle = "Anejiru : Shirakawa San Shimai ni Omakase",
                SeasonAlternateTitles = new List<string>
                {
                    "Anejiru The Animation: Shirakawa Sanshimai ni Omakase",
                    "姉汁 THE ANIMATION 白川三姉妹におまかせ",
                    "Ane Jiru The Animation - Shirakawa San Shimai ni Omakase",
                    "Anejiru : Shirakawa San Shimai ni Omakase"
                },
                Episodes = _episodes
            };

            var allEpisodes = new List<Episode>
            {
                new Episode { Id = 101, SeriesId = 4363, SeasonNumber = 1, EpisodeNumber = 1, AbsoluteEpisodeNumber = 1, Title = "S1E1" },
                new Episode { Id = 102, SeriesId = 4363, SeasonNumber = 1, EpisodeNumber = 2, AbsoluteEpisodeNumber = 2, Title = "S1E2" },
                new Episode { Id = 201, SeriesId = 4363, SeasonNumber = 2, EpisodeNumber = 1, AbsoluteEpisodeNumber = 3, Title = "S2E1" },
                new Episode { Id = 202, SeriesId = 4363, SeasonNumber = 2, EpisodeNumber = 2, AbsoluteEpisodeNumber = 4, Title = "S2E2" }
            };

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisode(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                  .Returns<int, int, int>((seriesId, season, ep) => allEpisodes.Find(e => e.SeasonNumber == season && e.EpisodeNumber == ep));

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisode(It.IsAny<int>(), It.IsAny<int>()))
                  .Returns<int, int>((seriesId, abs) => allEpisodes.Find(e => e.AbsoluteEpisodeNumber == abs));

            const string s2Release = "[Eng-Sub] [SubDesu-H] Anejiru 2 : Shirakawa San Shimai ni Omakase - (姉汁2 The Animation ～白川三姉妹におまかせ ～) Ep.01-02";

            var parsed = Parser.Parser.ParseTitle(s2Release);

            var remoteEpisode = Subject.Map(parsed, 0, 0, null, s1SearchCriteria);

            remoteEpisode.Should().NotBeNull();
            remoteEpisode.Series.Should().NotBeNull();
            remoteEpisode.Series.Id.Should().Be(seriesWithAllAlts.Id);
            remoteEpisode.MappedSeasonNumber.Should().Be(2);
            remoteEpisode.EpisodeRequested.Should().BeFalse();
        }

        [Test]
        public void should_not_match_different_series_with_partial_word_overlap_like_marikas_love_meter_to_love_me()
        {
            var p1 = Parser.Parser.ParseTitle("[SakuraCircle] Love Me: Kaede to Suzu The Animation - 01-03 (らぶみー『楓と鈴』 THE ANIMATION 第１-３巻) - English Softsubs");
            p1.Should().NotBeNull();
            p1.SeriesTitle.Should().Be("Love Me: Kaede to Suzu The Animation");
            p1.AbsoluteEpisodeNumbers.Should().Equal(1, 2, 3);

            var p2 = Parser.Parser.ParseTitle("[ToonsHub] Marikas Love Meter Malfunction S01E03 1080p UNCENSORED AMZN WEB-DL DDP2.0 H.264 (Marika-chan no Koukando wa Bukkowareteiru, Multi-Subs)");
            p2.Should().NotBeNull();
            p2.SeriesTitle.Should().Be("Marikas Love Meter Malfunction");

            var p3 = Parser.Parser.ParseTitle("[TokekHutan] Marika's Love Meter Malfunction - S01E01v2 (茉莉花ちゃんの好感度はぶっ壊れている; Marika-chan no Koukando wa Bukkowareteiru) [UNCENSORED, AMZN.WEB-DL 1080P AVC, EAC3 D-AUD, M-SUB][0410B7E9]");
            p3.Should().NotBeNull();
            p3.SeriesTitle.Should().Be("Marika's Love Meter Malfunction");

            var loveMeSeries = Builder<Series>.CreateNew()
                .With(s => s.Id = 8888)
                .With(s => s.Title = "Love Me: Kaede and Suzu The Animation")
                .With(s => s.CleanTitle = "lovemekaedeandsuzutheanimation")
                .With(s => s.PrimaryMetadataProvider = "anidb")
                .With(s => s.SeriesType = SeriesTypes.Anime)
                .With(s => s.AlternateTitles = new List<string>
                {
                    "Love Me: Kaede and Suzu The Animation",
                    "らぶみー『楓と鈴』 THE ANIMATION",
                    "Love Me: Kaede to Suzu The Animation"
                })
                .Build();

            var lmEpisodes = new List<Episode>
            {
                new Episode { Id = 1, SeriesId = 8888, SeasonNumber = 1, EpisodeNumber = 1, AbsoluteEpisodeNumber = 1 },
                new Episode { Id = 2, SeriesId = 8888, SeasonNumber = 1, EpisodeNumber = 2, AbsoluteEpisodeNumber = 2 },
                new Episode { Id = 3, SeriesId = 8888, SeasonNumber = 1, EpisodeNumber = 3, AbsoluteEpisodeNumber = 3 }
            };

            var criteria = new SeasonSearchCriteria
            {
                Series = loveMeSeries,
                SeasonNumber = 1,
                TargetSeasonNumber = 1,
                Episodes = lmEpisodes
            };

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisode(8888, It.IsAny<int>()))
                  .Returns<int, int>((id, abs) => lmEpisodes.Find(e => e.AbsoluteEpisodeNumber == abs));

            var rem1 = Subject.Map(p1, 0, 0, null, criteria);
            var rem2 = Subject.Map(p2, 0, 0, null, criteria);
            var rem3 = Subject.Map(p3, 0, 0, null, criteria);

            rem2.Series.Should().BeNull("Marikas Love Meter Malfunction must NOT match Love Me");
            rem3.Series.Should().BeNull("Marika's Love Meter Malfunction must NOT match Love Me");
            rem1.Series.Should().NotBeNull();
            rem1.Episodes.Should().HaveCount(3);
            rem1.Episodes[0].EpisodeNumber.Should().Be(1);
            rem1.Episodes[1].EpisodeNumber.Should().Be(2);
            rem1.Episodes[2].EpisodeNumber.Should().Be(3);
        }

        [Test]
        public void LustyLadiesOfMayohiga_SingleEpisode_OVA_Release_Without_Episode_Number_Matches_S01E01()
        {
            var mayohigaSeries = Builder<Series>.CreateNew()
                .With(s => s.Id = 12690)
                .With(s => s.TvdbId = 0)
                .With(s => s.AniDbId = 12690)
                .With(s => s.PrimaryMetadataProvider = "anidb")
                .With(s => s.SeriesType = SeriesTypes.Anime)
                .With(s => s.Title = "Lusty Ladies of Mayohiga")
                .With(s => s.CleanTitle = "lustyladiesofmayohiga")
                .With(s => s.AlternateTitles = new List<string>
                {
                    "Mayoiga no Onee-san The Animation",
                    "Mayohiga no Onee-san The Animation",
                    "マヨヒガのお姉さん THE ANIMATION",
                    "Lusty Ladies of Mayohiga",
                    "Entremets au café Mayohiga",
                    "The Lusty Ladies of Mayohiga"
                })
                .Build();

            var mayohigaEpisodes = new List<Episode>
            {
                new Episode
                {
                    Id = 17539,
                    SeriesId = 12690,
                    SeasonNumber = 1,
                    EpisodeNumber = 1,
                    AbsoluteEpisodeNumber = 1,
                    Title = "OVA"
                }
            };

            var criteria = new SeasonSearchCriteria
            {
                Series = mayohigaSeries,
                SeasonNumber = 1,
                TargetSeasonNumber = 1,
                Episodes = mayohigaEpisodes
            };

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodeBySeries(12690))
                  .Returns(mayohigaEpisodes);

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodesBySeason(12690, 1))
                  .Returns(mayohigaEpisodes);

            var releaseTitles = new[]
            {
                "Mayoiga no Onee-san The Animation / マヨヒガのお姉さん THE ANIMATION / Lusty Ladies of Mayohiga [UNCEN][BD 1920x1080 AVC 10bit] - English softsubs",
                "[Deadmau- RAWS] マヨヒガのお姉さん THE ANIMATION / Mayoiga no Onee-san The Animation (2017) [UNCEN]  (BDRip 1080p x264 DTS) [RAW]",
                "[Deadmau- RAWS] マヨヒガのお姉さん THE ANIMATION / Mayoiga no Onee-san The Animation (2017) [UNCEN]  (BDRip 720p x264 DTS)",
                "[KDSxGSF] Mayoiga no Onee-san The Animation [BD 1080p] (sub. español)",
                "Mayohiga no Onee-san The Animation [HH][UNCEN][BD 1920x1080 AVC 10bit][31EC31BE]"
            };

            foreach (var releaseTitle in releaseTitles)
            {
                var specialInfo = Subject.ParseSpecialEpisodeTitle(null, releaseTitle, 0, 0, null, criteria);
                specialInfo.Should().NotBeNull($"Special episode parser should identify single-episode anime for: {releaseTitle}");
                specialInfo.SeriesTitle.Should().Be("Lusty Ladies of Mayohiga");
                specialInfo.SeasonNumber.Should().Be(1);
                specialInfo.EpisodeNumbers.Should().Equal(new[] { 1 });

                var remoteEpisode = Subject.Map(specialInfo, 0, 0, null, criteria);
                remoteEpisode.Should().NotBeNull();
                remoteEpisode.Series.Should().NotBeNull($"Series should map correctly for: {releaseTitle}");
                remoteEpisode.Series.Id.Should().Be(12690);
                remoteEpisode.Episodes.Should().HaveCount(1);
                remoteEpisode.Episodes[0].EpisodeNumber.Should().Be(1);
                remoteEpisode.Episodes[0].SeasonNumber.Should().Be(1);
            }
        }
    }
}
