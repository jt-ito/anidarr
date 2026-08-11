using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Common.Test.ExtensionTests.StringExtensionTests
{
    [TestFixture]
    public class NormalizeRomajiParticlesFixture
    {
        [TestCase("Kare no Shiranai Himitsu wo Irete. The Animation", "Kare no Shiranai Himitsu o Irete. The Animation")]
        [TestCase("Kare no Shiranai Himitsu o Irete. The Animation", "Kare no Shiranai Himitsu o Irete. The Animation")]
        [TestCase("wo test", "o test")]
        [TestCase("test wo", "test o")]
        [TestCase("test wo test", "test o test")]
        [TestCase("test-wo-test", "test-o-test")]
        [TestCase("test.wo.test", "test.o.test")]
        [TestCase("ha test", "wa test")]
        [TestCase("he test", "e test")]
        [TestCase("Ha test", "wa test")]
        [TestCase("WO test", "o test")]
        [TestCase("He test", "e test")]
        public void should_normalize_standalone_particles(string original, string expected)
        {
            original.NormalizeRomajiParticles().Should().Be(expected);
        }

        [TestCase("wolf")]
        [TestCase("world")]
        [TestCase("know")]
        [TestCase("flower")]
        [TestCase("the")]
        [TestCase("half")]
        [TestCase("what")]
        [TestCase("head")]
        [TestCase("ahead")]
        [TestCase("here")]
        [TestCase("when")]
        [TestCase("wall")]
        [TestCase("water")]
        [TestCase("always")]
        [TestCase("way")]
        public void should_not_modify_words_containing_particles(string original)
        {
            original.NormalizeRomajiParticles().Should().Be(original);
        }

        [TestCase("He is a wolf", "e is a wolf")] // The word 'He' will be modified because it's a standalone particle 'he'
        [TestCase("The wolf is crying ha", "The wolf is crying wa")]
        [TestCase("He-Man and the Masters of the Universe", "e-Man and the Masters of the Universe")]
        [TestCase("E-Man and the Masters of the Universe", "E-Man and the Masters of the Universe")]
        public void should_demonstrate_accepted_collisions_with_english_words(string original, string expected)
        {
            // This test demonstrates the theoretical risk of a collision.
            // "He-Man..." and "E-Man..." will both reduce to the same string when CleanForSearch is applied,
            // since "He" -> "e" and "E" -> "E". This is accepted risk.
            original.NormalizeRomajiParticles().Should().Be(expected);
        }

        [TestCase("A wo B wo C", "A o B o C")]
        [TestCase("A ha B ha C", "A wa B wa C")]
        [TestCase("A he B he C", "A e B e C")]
        public void should_normalize_multiple_occurrences(string original, string expected)
        {
            original.NormalizeRomajiParticles().Should().Be(expected);
        }

        [Test]
        public void should_handle_null_or_empty_gracefully()
        {
            string nullString = null;
            nullString.NormalizeRomajiParticles().Should().BeNull();

            string.Empty.NormalizeRomajiParticles().Should().BeEmpty();
            " ".NormalizeRomajiParticles().Should().Be(" ");
        }

        [TestCase("Kare no Shiranai Himitsu wo Irete", "Kare no Shiranai Himitsu o Irete")]
        public void CleanForSearch_should_benefit_from_particle_normalization(string original, string searchTarget)
        {
            var originalCleaned = original.CleanForSearch();
            var targetCleaned = searchTarget.CleanForSearch();

            originalCleaned.Should().Be(targetCleaned);
            originalCleaned.Should().Be("karenoshiranaihimitsuoirete");
        }

        [Test]
        public void CleanForSearch_should_demonstrate_accepted_collisions()
        {
            var title1 = "He-Man and the Masters of the Universe".CleanForSearch();
            var title2 = "E-Man and the Masters of the Universe".CleanForSearch();

            title1.Should().Be(title2);
            title1.Should().Be("emanandthemastersoftheuniverse");
        }
    }
}
