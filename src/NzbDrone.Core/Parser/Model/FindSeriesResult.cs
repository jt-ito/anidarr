using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Parser.Model
{
    public class FindSeriesResult
    {
        public Series Series { get; set; }
        public SeriesMatchType MatchType { get; set; }
        public int? MatchedSeasonNumber { get; set; }

        public FindSeriesResult(Series series, SeriesMatchType matchType, int? matchedSeasonNumber = null)
        {
            Series = series;
            MatchType = matchType;
            MatchedSeasonNumber = matchedSeasonNumber;
        }
    }

    public enum SeriesMatchType
    {
        Unknown = 0,
        Title = 1,
        Alias = 2,
        Id = 3
    }
}
