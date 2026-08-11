using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormats
{
    public class CustomFormatsTestHelpers : CoreTest
    {
        private static List<CustomFormat> _customFormats { get; set; }

        public static void GivenCustomFormats(params CustomFormat[] formats)
        {
            _customFormats = formats.ToList();
        }

        public static List<ProfileFormatItem> GetSampleFormatItems(params string[] allowed)
        {
            var allowedItems = _customFormats.Where(x => allowed.Contains(x.Name)).Select((f, index) => new ProfileFormatItem { Format = f, Score = (int)Math.Pow(2, index) }).ToList();
            var disallowedItems = _customFormats.Where(x => !allowed.Contains(x.Name)).Select(f => new ProfileFormatItem { Format = f, Score = -1 * (int)Math.Pow(2, allowedItems.Count) });

            return disallowedItems.Concat(allowedItems).ToList();
        }

        public static List<ProfileFormatItem> GetDefaultFormatItems()
        {
            return new List<ProfileFormatItem>();
        }
    }

    [NUnit.Framework.TestFixture]
    public class ResolutionSpecificationFixture
    {
        [NUnit.Framework.Test]
        public void should_allow_unknown_resolution()
        {
            var spec = new NzbDrone.Core.CustomFormats.ResolutionSpecification { Value = 0 };
            var result = spec.Validate();
            FluentAssertions.AssertionExtensions.Should(result.IsValid).BeTrue();
        }

        [NUnit.Framework.Test]
        public void should_reject_null_resolution()
        {
            var spec = new NzbDrone.Core.CustomFormats.ResolutionSpecification { Value = null };
            var result = spec.Validate();
            FluentAssertions.AssertionExtensions.Should(result.IsValid).BeFalse();
            FluentAssertions.AssertionExtensions.Should(result.Errors).Contain(e => e.PropertyName == "Value");
        }

        [NUnit.Framework.Test]
        public void should_match_unknown_release()
        {
            var spec = new NzbDrone.Core.CustomFormats.ResolutionSpecification { Value = 0 };
            var input = new CustomFormatInput
            {
                EpisodeInfo = new NzbDrone.Core.Parser.Model.ParsedEpisodeInfo
                {
                    Quality = new NzbDrone.Core.Qualities.QualityModel(NzbDrone.Core.Qualities.Quality.Unknown, new NzbDrone.Core.Qualities.Revision())
                }
            };
            FluentAssertions.AssertionExtensions.Should(spec.IsSatisfiedBy(input)).BeTrue();
        }
    }
}
