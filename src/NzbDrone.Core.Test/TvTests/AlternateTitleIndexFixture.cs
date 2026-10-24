using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class AlternateTitleIndexFixture : CoreTest
    {
        // The lookups the index replaced: scan every alternate title of every series
        private static List<int> ReferenceExact(Dictionary<int, List<string>> titles, string cleanTitle)
        {
            return titles.Where(kvp => kvp.Value.Contains(cleanTitle)).Select(kvp => kvp.Key).OrderBy(id => id).ToList();
        }

        private static List<int> ReferenceSimilar(Dictionary<int, List<string>> titles, string cleanTitle)
        {
            return titles.Where(kvp => kvp.Value.Any(at =>
                    at == cleanTitle ||
                    ((cleanTitle.Contains(at) || at.Contains(cleanTitle)) &&
                     Math.Min(cleanTitle.Length, at.Length) >= 5 &&
                     (double)Math.Min(cleanTitle.Length, at.Length) / Math.Max(cleanTitle.Length, at.Length) >= 0.4)))
                .Select(kvp => kvp.Key)
                .OrderBy(id => id)
                .ToList();
        }

        private static string RandomTitle(Random random, int minLength, int maxLength)
        {
            // A small alphabet so titles overlap and contain each other often
            var chars = Enumerable.Range(0, random.Next(minLength, maxLength + 1)).Select(_ => "abcde"[random.Next(5)]);

            return new string(chars.ToArray());
        }

        [Test]
        public void should_find_the_same_series_as_scanning_every_title()
        {
            var random = new Random(42);
            var titles = new Dictionary<int, List<string>>();
            var index = new AlternateTitleIndex();

            for (var id = 1; id <= 300; id++)
            {
                var seriesTitles = Enumerable.Range(0, random.Next(0, 12)).Select(_ => RandomTitle(random, 0, 40)).Distinct().ToList();

                if (seriesTitles.Any())
                {
                    titles[id] = seriesTitles;
                }

                index.Set(id, seriesTitles);
            }

            for (var i = 0; i < 2000; i++)
            {
                var query = RandomTitle(random, 0, 60);

                index.FindExact(query).Should().Equal(ReferenceExact(titles, query), $"exact '{query}'");
                index.FindSimilar(query).Should().Equal(ReferenceSimilar(titles, query), $"similar '{query}'");
            }
        }

        [Test]
        public void should_find_titles_that_contain_or_are_contained_in_the_query_at_the_length_limits()
        {
            var index = new AlternateTitleIndex();

            index.Set(1, new[] { "abcde" });                    // 5 characters: the shortest that counts
            index.Set(2, new[] { "abcd" });                     // 4 characters: too short even though it is contained
            index.Set(3, new[] { "xxabcdexxxx" });              // contains the query, 11 vs 5: ratio 0.45
            index.Set(4, new[] { "xxabcdexxxxxx" });            // contains the query, 13 vs 5: ratio 0.38 (too long)
            index.Set(5, new[] { "abcde" });

            index.FindSimilar("abcde").Should().Equal(1, 3, 5);

            // A 20 character query contains titles 3 and 4 (55% and 65% of its length), but "abcde" is only 25% of it
            index.FindSimilar("xxabcdexxxxxxabcdexx").Should().Equal(3, 4);
        }

        [Test]
        public void should_match_an_empty_title_only_exactly()
        {
            var index = new AlternateTitleIndex();

            index.Set(1, new[] { string.Empty, "abcdef" });

            index.FindExact(string.Empty).Should().Equal(1);
            index.FindSimilar(string.Empty).Should().Equal(1);
            index.FindSimilar("abcdef").Should().Equal(1);
            index.FindExact("zzzzzz").Should().BeEmpty();
        }

        [Test]
        public void should_ignore_missing_titles()
        {
            var index = new AlternateTitleIndex();

            index.Set(1, new[] { null, "real title" });
            index.Set(2, new string[] { null });

            index.FindExact("real title").Should().Equal(1);
            index.FindSimilar("real title and more").Should().Equal(1);
            index.FindSimilar("anything else here").Should().BeEmpty();
        }

        [Test]
        public void should_replace_the_titles_of_a_series_that_is_set_again()
        {
            var index = new AlternateTitleIndex();

            index.Set(1, new[] { "old title" });
            index.Set(1, new[] { "new title" });

            index.FindExact("old title").Should().BeEmpty();
            index.FindExact("new title").Should().Equal(1);
        }

        [Test]
        public void should_forget_a_series_that_is_removed_and_keep_the_others_that_share_a_title()
        {
            var index = new AlternateTitleIndex();

            index.Set(1, new[] { "shared title" });
            index.Set(2, new[] { "shared title" });

            index.Remove(1);

            index.FindExact("shared title").Should().Equal(2);
            index.FindSimilar("shared title").Should().Equal(2);

            index.Remove(2);

            index.FindExact("shared title").Should().BeEmpty();
        }

        [Test]
        public void should_stay_correct_after_series_are_replaced_and_removed()
        {
            var random = new Random(7);
            var titles = new Dictionary<int, List<string>>();
            var index = new AlternateTitleIndex();

            for (var step = 0; step < 1500; step++)
            {
                var id = random.Next(1, 60);

                if (random.Next(4) == 0)
                {
                    titles.Remove(id);
                    index.Remove(id);
                }
                else
                {
                    var seriesTitles = Enumerable.Range(0, random.Next(1, 8)).Select(_ => RandomTitle(random, 0, 30)).Distinct().ToList();
                    titles[id] = seriesTitles;
                    index.Set(id, seriesTitles);
                }

                var query = RandomTitle(random, 0, 45);

                index.FindExact(query).Should().Equal(ReferenceExact(titles, query), $"exact '{query}' at step {step}");
                index.FindSimilar(query).Should().Equal(ReferenceSimilar(titles, query), $"similar '{query}' at step {step}");
            }
        }
    }
}
