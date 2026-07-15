using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FizzWare.NBuilder;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests.SeriesRepositoryTests
{
    [TestFixture]
    public class PerformanceFixture : DbTest<SeriesRepository, Series>
    {
        [Test]
        public void Benchmark_FindByTitleInexact()
        {
            var sw = Stopwatch.StartNew();
            var currentTvdbId = 1;

            // Generate 3000 TVDB series
            var tvdbSeries = Builder<Series>.CreateListOfSize(3000)
                .All()
                .With(s => s.Id = 0)
                .With(s => s.TvdbId = currentTvdbId++)
                .With(s => s.CleanTitle = Guid.NewGuid().ToString())
                .With(s => s.TitleSlug = Guid.NewGuid().ToString())
                .With(s => s.AlternateTitles = new List<string>())
                .BuildList();

            // Generate 2000 AniDB series with AlternateTitles
            var anidbSeries = Builder<Series>.CreateListOfSize(2000)
                .All()
                .With(s => s.Id = 0)
                .With(s => s.TvdbId = currentTvdbId++)
                .With(s => s.CleanTitle = Guid.NewGuid().ToString())
                .With(s => s.TitleSlug = Guid.NewGuid().ToString())
                .With(s => s.AlternateTitles = new List<string> { "alt1_" + Guid.NewGuid(), "alt2_" + Guid.NewGuid(), "english_500" })
                .BuildList();

            var allSeries = tvdbSeries.Concat(anidbSeries).ToList();
            Subject.InsertMany(allSeries);
            sw.Stop();
            Console.WriteLine($"Setup took: {sw.ElapsedMilliseconds} ms");

            // Warmup
            Subject.FindByTitleInexact("warmup");

            // Benchmark 100 queries
            sw.Restart();
            for (var i = 0; i < 100; i++)
            {
                Subject.FindByTitleInexact("english_500");
            }

            sw.Stop();
            Console.WriteLine($"100 queries took: {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"Average: {(double)sw.ElapsedMilliseconds / 100.0} ms");
        }
    }
}
