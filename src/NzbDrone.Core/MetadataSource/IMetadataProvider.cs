using System;
using System.Collections.Generic;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public enum MetadataProviderType
    {
        Tvdb = 0,
        AniDb = 1,
        AniList = 2,
        Simkl = 3,
        Mal = 4
    }

    /// <summary>
    /// Common contract for all metadata providers (TVDB, AniDB, AniList, Simkl, MAL).
    /// Each provider is registered in DI and routed through MetadataDispatcher.
    /// </summary>
    public interface IMetadataProvider
    {
        MetadataProviderType ProviderType { get; }

        /// <summary>Returns true if this provider can resolve the given external ID key (e.g. "anidb", "anilist").</summary>
        bool CanHandleId(string externalIdKey);

        /// <summary>Fetch full series + episode list from this provider using the provider-native external ID.</summary>
        Tuple<Series, List<Episode>> GetSeriesInfo(string externalId);

        /// <summary>Search for series by title (or provider-specific prefix like "tvdb:12345").</summary>
        List<Series> Search(string query);
    }
}
