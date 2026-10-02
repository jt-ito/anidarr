namespace NzbDrone.Core.Tv
{
    /// <summary>
    /// A rough idea of how long an import takes when AniDB has to be asked for each series. AniDB allows
    /// one request every 2 seconds. A series with nothing cached needs 1 request (a single season) to a few
    /// (a franchise with several seasons), and the requests themselves take a moment on top of the 2s wait.
    /// It is an estimate, not a promise: it assumes nothing else is using AniDB at the same time.
    /// </summary>
    public static class ImportEstimate
    {
        public const int MinSecondsPerSeries = 2;
        public const int MaxSecondsPerSeries = 6;

        // Series whose AniDB data is already cached cost (almost) nothing
        public static (int LowSeconds, int HighSeconds) Seconds(int uncachedSeries)
        {
            var count = uncachedSeries < 0 ? 0 : uncachedSeries;

            return (count * MinSecondsPerSeries, count * MaxSecondsPerSeries);
        }
    }
}
