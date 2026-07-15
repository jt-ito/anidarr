using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests.SeriesRepositoryTests
{
    [TestFixture]

    public class SeriesRepositoryFixture : DbTest<SeriesRepository, Series>
    {
        [Test]
        public void should_lazyload_quality_profile()
        {
            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(Quality.Bluray1080p, Quality.DVD, Quality.HDTV720p),

                Cutoff = Quality.Bluray1080p.Id,
                Name = "TestProfile"
            };

            Mocker.Resolve<QualityProfileRepository>().Insert(profile);

            var series = Builder<Series>.CreateNew().BuildNew();
            series.QualityProfileId = profile.Id;

            Subject.Insert(series);

            StoredModel.QualityProfile.Should().NotBeNull();
        }

        private void GivenSeries()
        {
            var series = Builder<Series>.CreateListOfSize(2)
                .All()
                .With(a => a.Id = 0)
                .TheFirst(1)
                .With(x => x.CleanTitle = "crown")
                .TheNext(1)
                .With(x => x.CleanTitle = "crownextralong")
                .BuildList();

            Subject.InsertMany(series);
        }

        [TestCase("crow")]
        [TestCase("rownc")]
        public void should_find_no_inexact_matches(string cleanTitle)
        {
            GivenSeries();

            var found = Subject.FindByTitleInexact(cleanTitle);
            found.Should().BeEmpty();
        }

        [TestCase("crowna")]
        [TestCase("acrown")]
        [TestCase("acrowna")]
        public void should_find_one_inexact_match(string cleanTitle)
        {
            GivenSeries();

            var found = Subject.FindByTitleInexact(cleanTitle);
            found.Should().HaveCount(1);
            found.First().CleanTitle.Should().Be("crown");
        }

        [TestCase("crownextralong")]
        [TestCase("crownextralonga")]
        [TestCase("acrownextralong")]
        [TestCase("acrownextralonga")]
        public void should_find_two_inexact_matches(string cleanTitle)
        {
            GivenSeries();

            var found = Subject.FindByTitleInexact(cleanTitle);
            found.Should().HaveCount(2);
            found.Select(x => x.CleanTitle).Should().BeEquivalentTo(new[] { "crown", "crownextralong" });
        }

        [Test]
        public void should_update_alternate_titles_cache_on_series_updated_event()
        {
            var series = Builder<Series>.CreateNew()
                .With(s => s.Id = 0)
                .With(s => s.CleanTitle = "main")
                .With(s => s.AlternateTitles = new System.Collections.Generic.List<string> { "apple" })
                .BuildNew();

            Subject.Insert(series);

            // 1. Initial lookup -> lazily loads cache
            var found = Subject.FindByTitleInexact("apple");
            found.Should().HaveCount(1);
            found.First().CleanTitle.Should().Be("main");

            // 2. Shrink and update alternate titles
            series.AlternateTitles = new System.Collections.Generic.List<string> { "banana" };
            Subject.Update(series); // persists to DB
            Subject.Handle(new NzbDrone.Core.Tv.Events.SeriesUpdatedEvent(series)); // triggers cache invalidation

            // 3. Old title should no longer match
            found = Subject.FindByTitleInexact("apple");
            found.Should().BeEmpty();

            System.Console.WriteLine("DEBUG: finding banana...");

            // 4. New title should match
            found = Subject.FindByTitleInexact("banana");
            System.Console.WriteLine($"DEBUG: found count = {found.Count}");
            found.Should().HaveCount(1);
            found.First().CleanTitle.Should().Be("main");
        }
    }
}
