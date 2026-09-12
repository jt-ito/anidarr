using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    [TestFixture]
    public class AniListEnricherFixture : CoreTest<AniListEnricher>
    {
        [SetUp]
        public void Setup()
        {
            AniListEnricher.ClearCache();

            Mocker.GetMock<IAniListRateLimiter>()
                .Setup(v => v.ExecuteAsync(It.IsAny<Func<int?>>()))
                .Returns((Func<int?> action) => Task.FromResult(action()));
        }

        [TearDown]
        public void TearDown()
        {
            AniListEnricher.ClearCache();
        }

        private void GivenJsonResponse(string json)
        {
            var httpResponse = new HttpResponse(null, new HttpHeader(), json);
            var typedResponse = new HttpResponse<AniListSearchResponse>(httpResponse);

            Mocker.GetMock<IHttpClient>()
                .Setup(v => v.Post<AniListSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns(typedResponse);
        }

        [Test]
        public void should_match_exact_year()
        {
            var json = @"{
  ""data"": {
    ""page"": {
      ""media"": [
        { ""id"": 100, ""title"": { ""romaji"": ""Some Anime"" }, ""startDate"": { ""year"": 2015 }, ""episodes"": 12, ""format"": ""TV"" }
      ]
    }
  }
}";
            GivenJsonResponse(json);

            var result = Subject.SearchAniListIdByTitle("Some Anime", 2015, 12);

            result.Should().Be(100);
        }

        [Test]
        public void should_match_year_within_tolerance()
        {
            var json = @"{
  ""data"": {
    ""page"": {
      ""media"": [
        { ""id"": 101, ""title"": { ""romaji"": ""Some Anime"" }, ""startDate"": { ""year"": 2016 }, ""episodes"": 12, ""format"": ""TV"" }
      ]
    }
  }
}";
            GivenJsonResponse(json);

            // Expected 2015, found 2016 (within +/- 1)
            var result = Subject.SearchAniListIdByTitle("Some Anime", 2015, 12);

            result.Should().Be(101);
        }

        [Test]
        public void should_use_episode_tiebreaker_when_ambiguous()
        {
            var json = @"{
  ""data"": {
    ""page"": {
      ""media"": [
        { ""id"": 102, ""title"": { ""romaji"": ""Some Anime"" }, ""startDate"": { ""year"": 2015 }, ""episodes"": 12, ""format"": ""TV"" },
        { ""id"": 103, ""title"": { ""romaji"": ""Some Anime"" }, ""startDate"": { ""year"": 2015 }, ""episodes"": 24, ""format"": ""TV"" }
      ]
    }
  }
}";
            GivenJsonResponse(json);

            // Matches year 2015, but there are two candidates.
            // Expected episodes: 24, so it should pick 103.
            var result = Subject.SearchAniListIdByTitle("Some Anime", 2015, 24);

            result.Should().Be(103);
        }

        [Test]
        public void should_return_null_when_ambiguous_and_no_episode_tiebreaker_matches()
        {
            var json = @"{
  ""data"": {
    ""page"": {
      ""media"": [
        { ""id"": 102, ""title"": { ""romaji"": ""Some Anime"" }, ""startDate"": { ""year"": 2015 }, ""episodes"": 12, ""format"": ""TV"" },
        { ""id"": 103, ""title"": { ""romaji"": ""Some Anime"" }, ""startDate"": { ""year"": 2015 }, ""episodes"": 24, ""format"": ""TV"" }
      ]
    }
  }
}";
            GivenJsonResponse(json);

            // Matches year 2015, but there are two candidates.
            // Expected episodes: 50, neither candidate has 50.
            var result = Subject.SearchAniListIdByTitle("Some Anime", 2015, 50);

            result.Should().BeNull();
        }

        [Test]
        public void should_evaluate_short_titles_by_relative_levenshtein_distance()
        {
            var json = @"{
  ""data"": {
    ""page"": {
      ""media"": [
        { ""id"": 100, ""title"": { ""romaji"": ""Bleach"" }, ""startDate"": { ""year"": 2015 }, ""episodes"": 12, ""format"": ""TV"" }
      ]
    }
  }
}";
            GivenJsonResponse(json);

            // "Bleach" vs "Breach" -> clean length 6, 20% = 1 allowed edit.
            // distance = 1.
            var result1 = Subject.SearchAniListIdByTitle("Breach", 2015, 12);
            result1.Should().Be(100);

            // "Bleach" vs "B" -> clean length 6, 20% = 1 allowed edit.
            // distance = 5.
            var result2 = Subject.SearchAniListIdByTitle("B", 2015, 12);
            result2.Should().BeNull();
        }

        [Test]
        public void should_reject_different_series_titles_of_similar_length_by_relative_levenshtein_distance()
        {
            var json = @"{
  ""data"": {
    ""page"": {
      ""media"": [
        { ""id"": 100, ""title"": { ""romaji"": ""Naruto"" }, ""startDate"": { ""year"": 2015 }, ""episodes"": 12, ""format"": ""TV"" }
      ]
    }
  }
}";
            GivenJsonResponse(json);

            // "Naruto" vs "Bleach" -> clean length 6, 20% = 1 allowed edit.
            // distance = 6 (completely different).
            var result = Subject.SearchAniListIdByTitle("Bleach", 2015, 12);
            result.Should().BeNull();
        }

        [Test]
        public void should_allow_small_typos_in_long_titles_by_relative_levenshtein_distance()
        {
            var json = @"{
  ""data"": {
    ""page"": {
      ""media"": [
        { ""id"": 100, ""title"": { ""romaji"": ""Maou Gakuin no Futekigousha: Shijou Saikyou no Maou no Shiso"" }, ""startDate"": { ""year"": 2015 }, ""episodes"": 12, ""format"": ""TV"" }
      ]
    }
  }
}";
            GivenJsonResponse(json);

            // Clean length is 50. 20% allowed = 10 edits.
            // Typo here drops three 'u' characters (distance 3).
            var result = Subject.SearchAniListIdByTitle("Maou Gakuin no Futekigosha: Shijo Saikyo no Mao no Shiso", 2015, 12);
            result.Should().Be(100);
        }

        [Test]
        public void should_return_null_when_circuit_breaker_is_active()
        {
            Mocker.GetMock<IAniListRateLimiter>()
                .SetupGet(r => r.IsRateLimited)
                .Returns(true);

            var result = Subject.SearchAniListIdByTitle("Some Anime", 2015, 12);

            result.Should().BeNull();
            Mocker.GetMock<IHttpClient>().Verify(c => c.Post<AniListSearchResponse>(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Test]
        public void should_handle_429_gracefully_and_record_failure()
        {
            var req = new HttpRequest("https://graphql.anilist.co");
            var res = new HttpResponse(req, new HttpHeader(), "Too Many Requests", System.Net.HttpStatusCode.TooManyRequests);
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<AniListSearchResponse>(It.IsAny<HttpRequest>()))
                .Throws(new TooManyRequestsException(req, res));

            var result = Subject.SearchAniListIdByTitle("Some Anime", 2015, 12);

            result.Should().BeNull();
            Mocker.GetMock<IAniListRateLimiter>()
                .Verify(r => r.RecordFailure(It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
        }

        [Test]
        public void should_handle_403_cloudflare_protection_gracefully()
        {
            var req = new HttpRequest("https://graphql.anilist.co");
            var res = new HttpResponse(req, new HttpHeader(), "Forbidden Cloudflare Challenge", System.Net.HttpStatusCode.Forbidden);
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<AniListSearchResponse>(It.IsAny<HttpRequest>()))
                .Throws(new HttpException(req, res));

            var result = Subject.SearchAniListIdByTitle("Some Anime", 2015, 12);

            result.Should().BeNull();
            Mocker.GetMock<IAniListRateLimiter>()
                .Verify(r => r.RecordFailure(It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
        }

        [Test]
        public void should_handle_500_gracefully()
        {
            var req = new HttpRequest("https://graphql.anilist.co");
            var res = new HttpResponse(req, new HttpHeader(), "Internal Server Error", System.Net.HttpStatusCode.InternalServerError);
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<AniListSearchResponse>(It.IsAny<HttpRequest>()))
                .Throws(new HttpException(req, res));

            var result = Subject.SearchAniListIdByTitle("Some Anime", 2015, 12);

            result.Should().BeNull();
            Mocker.GetMock<IAniListRateLimiter>()
                .Verify(r => r.RecordFailure(It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
        }

        [Test]
        public void should_return_empty_airing_times_when_circuit_breaker_active()
        {
            Mocker.GetMock<IAniListRateLimiter>()
                .SetupGet(r => r.IsRateLimited)
                .Returns(true);

            var result = Subject.GetAiringTimes(12345);

            result.Should().BeEmpty();
            Mocker.GetMock<IHttpClient>().Verify(c => c.Post<AniListMediaResponse>(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Test]
        public void should_return_empty_titles_when_circuit_breaker_active()
        {
            Mocker.GetMock<IAniListRateLimiter>()
                .SetupGet(r => r.IsRateLimited)
                .Returns(true);

            var result = Subject.GetTitles(12345);

            result.Should().BeEmpty();
            Mocker.GetMock<IHttpClient>().Verify(c => c.Post<AniListMediaResponse>(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Test]
        public void should_batch_fetch_enrichment_data_for_multiple_ids()
        {
            Mocker.GetMock<IAniListRateLimiter>()
                .Setup(v => v.ExecuteAsync(It.IsAny<Func<AniListEnrichmentData>>()))
                .Returns((Func<AniListEnrichmentData> action) => Task.FromResult(action()));

            var json = @"{
  ""data"": {
    ""page"": {
      ""media"": [
        {
          ""id"": 100,
          ""title"": { ""romaji"": ""Romaji 100"", ""english"": ""English 100"" },
          ""synonyms"": [""Synonym 100""],
          ""airingSchedule"": {
            ""nodes"": [
              { ""episode"": 1, ""airingAt"": 1753448400 }
            ]
          }
        },
        {
          ""id"": 200,
          ""title"": { ""romaji"": ""Romaji 200"" },
          ""synonyms"": [],
          ""airingSchedule"": {
            ""nodes"": []
          }
        }
      ]
    }
  }
}";
            GivenJsonResponse(json);

            var result = Subject.GetEnrichmentForMultiple(new[] { 100, 200 });

            result.Should().NotBeNull();
            result.Titles[100].Should().Contain("Romaji 100");
            result.Titles[100].Should().Contain("English 100");
            result.Titles[100].Should().Contain("Synonym 100");
            result.AiringTimes[100].Should().ContainKey(1);

            result.Titles[200].Should().Contain("Romaji 200");
        }
    }
}
