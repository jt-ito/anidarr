using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test
{
    [TestFixture]
    public class LevenshteinDistanceFixture : TestBase
    {
        [TestCase("", "", 0)]
        [TestCase("abc", "abc", 0)]
        [TestCase("abc", "abcd", 1)]
        [TestCase("abcd", "abc", 1)]
        [TestCase("abc", "abd", 1)]
        [TestCase("abc", "adc", 1)]
        [TestCase("abcdefgh", "abcghdef", 4)]
        [TestCase("a.b.c.", "abc", 3)]
        [TestCase("Agents Of SHIELD", "Marvel's Agents Of S.H.I.E.L.D.", 15)]
        [TestCase("Agents of cracked", "Agents of shield", 6)]
        [TestCase("ABCxxx", "ABC1xx", 1)]
        [TestCase("ABC1xx", "ABCxxx", 1)]
        public void LevenshteinDistance(string text, string other, int expected)
        {
            text.LevenshteinDistance(other).Should().Be(expected);
        }

        [TestCase("", "", 0)]
        [TestCase("abc", "abc", 0)]
        [TestCase("abc", "abcd", 1)]
        [TestCase("abcd", "abc", 3)]
        [TestCase("abc", "abd", 3)]
        [TestCase("abc", "adc", 3)]
        [TestCase("abcdefgh", "abcghdef", 8)]
        [TestCase("a.b.c.", "abc", 0)]
        [TestCase("Agents of shield", "Marvel's Agents Of S.H.I.E.L.D.", 9)]
        [TestCase("Agents of shield", "Agents of cracked", 14)]
        [TestCase("Agents of shield", "the shield", 24)]
        [TestCase("ABCxxx", "ABC1xx", 3)]
        [TestCase("ABC1xx", "ABCxxx", 3)]
        public void LevenshteinDistanceClean(string text, string other, int expected)
        {
            text.ToLower().LevenshteinDistanceClean(other.ToLower()).Should().Be(expected);
        }

        [TestCase("kon", "k-on", 1)] // Length 4 vs 3 (max 4). 4 * 0.2 = 0.8 -> floor 0 -> max(1,0) = 1.
        [TestCase("bleach", "bleech", 1)] // Length 6. 6 * 0.2 = 1.2 -> floor 1 -> max(1,1) = 1.
        [TestCase("naruto shippuden", "naruto shipuden", 3)] // Length 16 vs 15. 16 * 0.2 = 3.2 -> max(1,3) = 3.
        [TestCase("a", "b", 1)]
        [TestCase("long title test", "long title test", 3)] // length 15 * 0.2 = 3
        public void GetAllowedEdits(string text, string other, int expected)
        {
            text.GetAllowedEdits(other).Should().Be(expected);
        }
    }
}
