using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class SearchTermCleanerFixture
    {
        [TestCase("[Abysswalker] Oni Chichi [1080p][WEB-DL]", "Oni Chichi")]
        [TestCase("[Erai-raws] Kingdom 3rd Season - 01 ~ 16 [1080p][Multiple Subtitle]", "Kingdom 3rd Season")]
        [TestCase("[AmateurSubs] Sister Breeder 01-02 [DVDRip 576p HEVC AC3]", "Sister Breeder")]
        [TestCase("[Erai-raws] One Piece - 1089 ~ 1104 [1080p][HEVC][Multiple Subtitle]", "One Piece")]
        [TestCase("[CBM] Citrus 1-12 Complete (Dual Audio) [BDRip 1080p x265 10bit]", "Citrus")]
        [TestCase("[Anime Time] Nisekoi S01+02+OVA [BD][1080p][HEVC 10bit x265][AAC][Eng Sub]", "Nisekoi")]
        [TestCase("[Anime Time] Megami-ryou no Ryoubo-kun (Season 1) [Dual Audio][BD][1080p]", "Megami-ryou no Ryoubo-kun")]
        [TestCase("(CBB) Keppeki Danshi! Aoyama-kun (BD 1080p)(HEVC-x265-10bit)(Multi-Subs)", "Keppeki Danshi! Aoyama-kun")]
        [TestCase("[AV1ARY] Edomae Elf (BD 1080p AV1 Opus) (v2)", "Edomae Elf")]
        [TestCase("[HH] Hatsu Inu 1-2 The Animation (Eng-Subs) [720p] (Complete)", "Hatsu Inu The Animation")]
        [TestCase("( HT ) Imouto Twins", "Imouto Twins")]
        [TestCase("Show.Name.1080p.WEB-DL", "Show Name")]
        [TestCase("Chained Soldier S01 1080p Dual Audio BDRip 10 bits DD+ x265-EMBER", "Chained Soldier")]
        [TestCase("The Eminence in Shadow S01 1080p Dual Audio  BDRip 10 bits DD + x265-EMBER", "The Eminence in Shadow")]
        [TestCase("From Old Country Bumpkin to Master Swordsman S01 1080p Dual Audio WEBRip DD+ x265-EMBER", "From Old Country Bumpkin to Master Swordsman")]
        [TestCase("Kimi ni Todoke - From Me to You S1-2 [1080][Dual-Audio][KiChiDo]", "Kimi ni Todoke - From Me to You")]
        [TestCase("Fire Punch (Digital) (Shizu)", "Fire Punch")]
        [TestCase("Show Time! S01 UNCENSORED English Dub 720p WEB x264 AAC - TMD-Group (OceanVeil)", "Show Time!")]
        [TestCase("Kimi ni Todoke - From Me to You", "Kimi ni Todoke - From Me to You")]
        public void should_clean_release_style_names(string input, string expectedFirst)
        {
            SearchTermCleaner.GetCandidates(input).Should().StartWith(expectedFirst);
        }

        [TestCase("[Judas] Bocchi the Rock! - 05 [1080p][HEVC]", "Bocchi the Rock!")]
        [TestCase("Show.Name.S01E05.1080p.WEB-DL.x265-GRP", "Show Name")]
        [TestCase("Cyberpunk Edgerunners S01E03 Lucy [1080p]", "Cyberpunk Edgerunners")]
        [TestCase("[SakuraCircle] Midareuchi - 01 (DVD 720x480 h264 AAC)", "Midareuchi")]
        [TestCase("Show Name - 12v2 [BD 1080p]", "Show Name")]
        public void should_read_the_show_title_from_an_episode_file_name(string fileName, string expected)
        {
            SearchTermCleaner.CleanFileTitle(fileName).Should().Be(expected);
        }

        [Test]
        public void should_offer_trailing_english_title_and_parenthetical_alias()
        {
            var tail = SearchTermCleaner.GetCandidates("[Anime Time] Yofukashi No Uta (Season 01) [BD] [1080p][Eng Sub] Call Of The Night");
            tail.Should().Contain("Call Of The Night");

            var alias = SearchTermCleaner.GetCandidates("[Anime Time] Uzaki-chan Wants to Hang Out! (Season 01) [BD][1080p] [Batch] (Uzaki-chan wa Asobitai!)");
            alias.Should().Contain("Uzaki-chan wa Asobitai!");
        }

        [TestCase("Naruto")]
        [TestCase("Love 2 Quad")]
        [TestCase("Oni Chichi 2 Harvest")]
        [TestCase("Steins;Gate 0")]
        [TestCase("DD Hokuto no Ken")]
        [TestCase("tvdb:12345")]
        [TestCase("anidb:42")]
        [TestCase("")]
        public void should_leave_clean_terms_alone(string input)
        {
            SearchTermCleaner.GetCandidates(input).Should().BeEmpty();
        }

        [Test]
        public void should_never_return_the_original_or_more_than_four()
        {
            var input = "[G] A - B [1080p] C (D) (E) (F) (G)";
            var result = SearchTermCleaner.GetCandidates(input);

            result.Should().NotContain(input);
            result.Count.Should().BeLessOrEqualTo(4);
        }
    }
}
