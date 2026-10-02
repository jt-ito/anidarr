import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import fetchJson from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

export interface ImportEstimate {
  // AniDB-only series about to be imported
  aniDbSeries: number;
  // already in the local AniDB cache: importing them needs no AniDB request
  cached: number;
  uncached: number;
  secondsLow: number;
  secondsHigh: number;
}

// Asks the server roughly how long importing these AniDB series will take (it knows which ones are
// already cached). Re-checks while some are not, because the background prefetch keeps filling the cache.
function useImportEstimate(aniDbIds: number[], isEnabled: boolean) {
  const key = useMemo(
    () => [...aniDbIds].sort((a, b) => a - b).join(','),
    [aniDbIds]
  );
  const isActive = isEnabled && aniDbIds.length > 0;

  const { data } = useQuery({
    queryKey: ['/series/import/estimate', key],
    queryFn: ({ signal }) =>
      fetchJson<ImportEstimate, { aniDbIds: number[] }>({
        path: getQueryPath('/series/import/estimate'),
        method: 'POST',
        headers: {
          'X-Api-Key': window.Sonarr.apiKey,
          'X-Sonarr-Client': 'Sonarr',
        },
        body: { aniDbIds },
        signal,
      }),
    enabled: isActive,
    staleTime: 0,
    refetchInterval: (query) => (query.state.data?.uncached ? 10000 : false),
    refetchOnWindowFocus: false,
  });

  return isActive ? data : undefined;
}

export default useImportEstimate;
