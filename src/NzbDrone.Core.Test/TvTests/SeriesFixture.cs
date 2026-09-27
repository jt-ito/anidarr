using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class SeriesFixture : TestBase
    {
        [Test]
        public void ApplyChanges_should_not_merge_spaceless_slug_alternate_titles()
        {
            var series = new Series
            {
                AlternateTitles = new List<string> { "Anejiru" }
            };

            var otherSeries = new Series
            {
                AlternateTitles = new List<string>
                {
                    "anejirutheanimationshirakawasanshimainiomakase",
                    "Anejiru 2"
                }
            };

            series.ApplyChanges(otherSeries);

            series.AlternateTitles.Should().Contain("Anejiru 2");
            series.AlternateTitles.Should().NotContain("anejirutheanimationshirakawasanshimainiomakase");
        }

        [Test]
        public void ApplyChanges_should_not_merge_spaceless_slug_alternate_titles_into_empty_list()
        {
            var series = new Series
            {
                AlternateTitles = null
            };

            var otherSeries = new Series
            {
                AlternateTitles = new List<string>
                {
                    "anejirutheanimationshirakawasanshimainiomakase",
                    "Anejiru 2"
                }
            };

            series.ApplyChanges(otherSeries);

            series.AlternateTitles.Should().Contain("Anejiru 2");
            series.AlternateTitles.Should().NotContain("anejirutheanimationshirakawasanshimainiomakase");
        }
    }
}
