import { QueryClient, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import AddSeries from 'AddSeries/AddSeries';
import { AddSeriesOptions } from 'AddSeries/addSeriesOptionsStore';
import useApiMutation, {
  addOrUpdateQueryClientItem,
} from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Series from 'Series/Series';
import fetchJson from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import getQueryString from 'Utilities/Fetch/getQueryString';

interface AddSeriesPayload
  extends AddSeries,
    Omit<
      AddSeriesOptions,
      'monitor' | 'searchForMissingEpisodes' | 'searchForCutoffUnmetEpisodes'
    > {}

const DEFAULT_SERIES: AddSeries[] = [];

// Appends AniDB results the primary provider didn't already return.
export const mergeLookupResults = (
  primaryData: readonly AddSeries[],
  anidbData: readonly AddSeries[]
) => {
  if (anidbData.length === 0) {
    return [...primaryData];
  }

  const existingIds = new Set(primaryData.map((s) => s.tvdbId).filter(Boolean));
  const existingTitles = new Set(primaryData.map((s) => s.title.toLowerCase()));

  const uniqueAnidb = anidbData.filter(
    (s) =>
      !existingIds.has(s.tvdbId) && !existingTitles.has(s.title.toLowerCase())
  );

  return [...primaryData, ...uniqueAnidb];
};

export type LookupMatchStatus = 'matched' | 'possible' | 'none';

interface FolderMatch {
  status: LookupMatchStatus;
  // Why the server chose that status
  reason?: string;
  // Video files found in the folder
  fileCount?: number;
  // The folder name without release tags: what was actually searched for
  searchTerm?: string;
  // Best first; only trust results[0] when status is 'matched'
  results: AddSeries[];
}

// Non-hook lookup (for work that must not depend on a component being mounted).
// The server cleans the folder name, searches the providers and scores the results.
// Pass `path` (the folder) so the server can also use ids, a tvshow.nfo and episode file names.
export const fetchFolderMatch = (
  queryClient: QueryClient,
  term: string,
  provider?: string,
  path?: string,
  background = false
) =>
  queryClient.fetchQuery({
    queryKey: ['/series/lookup/match', { term, provider, path, background }],
    queryFn: ({ signal }) =>
      fetchJson<FolderMatch, unknown>({
        path:
          getQueryPath('/series/lookup/match') +
          // background: bulk queue work, which the server serves after anything the user asked for
          getQueryString({
            term,
            provider,
            path,
            background: background || undefined,
          }),
        headers: {
          'X-Api-Key': window.Sonarr.apiKey,
          'X-Sonarr-Client': 'Sonarr',
        },
        signal,
      }),
    // Always hit the server: callers use this for explicit (re)searches
    staleTime: 0,
  });

// Remembers which series the user chose for a folder, so it resolves the same way next time.
// Best-effort: failing to save must never get in the way of importing.
export const rememberImportChoice = (
  folderName: string,
  series: Pick<AddSeries, 'tvdbId' | 'aniDbId' | 'title'>
) =>
  fetchJson<unknown, unknown>({
    path: getQueryPath('/series/lookup/choice'),
    method: 'POST',
    headers: {
      'X-Api-Key': window.Sonarr.apiKey,
      'X-Sonarr-Client': 'Sonarr',
    },
    body: {
      term: folderName,
      tvdbId: series.tvdbId > 0 ? series.tvdbId : 0,
      aniDbId: series.aniDbId ?? 0,
      title: series.title,
    },
  }).catch(() => undefined);

export const useLookupSeries = (
  query: string,
  provider?: string,
  isEnabled = true
) => {
  const isAll = !provider;
  const primaryProvider = isAll ? 'tvdb' : provider;

  const resultPrimary = useApiQuery<AddSeries[]>({
    path: '/series/lookup',
    queryParams: {
      term: query,
      ...(primaryProvider ? { provider: primaryProvider } : {}),
    },
    queryOptions: {
      enabled: isEnabled && !!query,
      // Disable refetch on window focus to prevent refetching when the user switch tabs
      refetchOnWindowFocus: false,
    },
  });

  const resultAnidb = useApiQuery<AddSeries[]>({
    path: '/series/lookup',
    queryParams: {
      term: query,
      provider: 'anidb',
    },
    queryOptions: {
      enabled: isEnabled && !!query && isAll,
      refetchOnWindowFocus: false,
    },
  });

  // Memoized so consumers that depend on `data` don't re-run effects every render
  const mergedData = useMemo(() => {
    const primaryData = resultPrimary.data ?? DEFAULT_SERIES;
    const anidbData = resultAnidb.data ?? DEFAULT_SERIES;

    return isAll ? mergeLookupResults(primaryData, anidbData) : primaryData;
  }, [isAll, resultPrimary.data, resultAnidb.data]);

  const isFetching = isAll
    ? resultPrimary.isFetching || resultAnidb.isFetching
    : resultPrimary.isFetching;

  // In combined mode the lookup isn't done until both providers have answered,
  // otherwise consumers act on partial (TVDB-only) results
  const isFetched = isAll
    ? resultPrimary.isFetched && resultAnidb.isFetched
    : resultPrimary.isFetched;

  const error = resultPrimary.error || resultAnidb.error;

  const { refetch: refetchPrimary } = resultPrimary;
  const { refetch: refetchAnidb } = resultAnidb;

  const refetch = useCallback(
    // refetch ignores `enabled`, so only hit AniDB when it's part of the lookup
    () =>
      Promise.all(
        isAll ? [refetchPrimary(), refetchAnidb()] : [refetchPrimary()]
      ),
    [isAll, refetchPrimary, refetchAnidb]
  );

  return {
    ...resultPrimary,
    isFetching,
    isFetched,
    error,
    refetch,
    data: mergedData.length > 0 ? mergedData : DEFAULT_SERIES,
  };
};

export const useAddSeries = (onSuccess?: () => void) => {
  const queryClient = useQueryClient();

  const { isPending, error, mutate } = useApiMutation<Series, AddSeriesPayload>(
    {
      path: '/series',
      method: 'POST',
      mutationOptions: {
        onSuccess: (newSeries) => {
          queryClient.setQueryData<Series[]>(['/series'], (oldSeries = []) =>
            addOrUpdateQueryClientItem(oldSeries, newSeries, 'id')
          );
          onSuccess?.();
        },
      },
    }
  );

  return {
    isAdding: isPending,
    addError: error,
    addSeries: mutate,
  };
};
