using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Sonarr.Api.V5.Series;

namespace NzbDrone.Api.Test.v5.Series
{
    [TestFixture]
    public class SeriesResourceMapperFixture
    {
        [Test]
        public void ToModel_should_not_map_spaceless_slug_alternate_titles()
        {
            var resource = new SeriesResource
            {
                AlternateTitles = new List<AlternateTitleResource>
                {
                    new AlternateTitleResource { Title = "anejirutheanimationshirakawasanshimainiomakase" },
                    new AlternateTitleResource { Title = "Anejiru 2" }
                }
            };

            var model = resource.ToModel();

            model.AlternateTitles.Should().Contain("Anejiru 2");
            model.AlternateTitles.Should().NotContain("anejirutheanimationshirakawasanshimainiomakase");
        }
    }
}
