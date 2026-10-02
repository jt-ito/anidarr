using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class ImportEstimateFixture
    {
        [Test]
        public void should_need_no_time_when_everything_is_cached()
        {
            ImportEstimate.Seconds(0).Should().Be((0, 0));
        }

        [Test]
        public void should_scale_with_the_series_that_still_need_anidb()
        {
            // 2s (one request) to 6s (a few requests plus overhead) per series
            ImportEstimate.Seconds(300).Should().Be((600, 1800));
            ImportEstimate.Seconds(1).Should().Be((2, 6));
        }

        [Test]
        public void should_never_report_a_negative_time()
        {
            ImportEstimate.Seconds(-4).Should().Be((0, 0));
        }

        [Test]
        public void should_report_a_low_that_is_below_the_high()
        {
            var (low, high) = ImportEstimate.Seconds(50);

            low.Should().BeLessThan(high);
        }
    }
}
