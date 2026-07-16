import { useQueryClient } from '@tanstack/react-query';
import AddSeries from 'AddSeries/AddSeries';
import { AddSeriesOptions } from 'AddSeries/addSeriesOptionsStore';
import useApiMutation, {
  addOrUpdateQueryClientItem,
} from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Series from 'Series/Series';

interface AddSeriesPayload
  extends AddSeries,
    Omit<
      AddSeriesOptions,
      'monitor' | 'searchForMissingEpisodes' | 'searchForCutoffUnmetEpisodes'
    > {}

const DEFAULT_SERIES: AddSeries[] = [];

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

  const primaryData = resultPrimary.data || [];
  const anidbData = resultAnidb.data || [];

  let mergedData = primaryData;

  if (isAll && anidbData.length > 0) {
    const existingIds = new Set(
      primaryData.map((s) => s.tvdbId).filter(Boolean)
    );
    const existingTitles = new Set(
      primaryData.map((s) => s.title.toLowerCase())
    );

    const uniqueAnidb = anidbData.filter(
      (s) =>
        !existingIds.has(s.tvdbId) && !existingTitles.has(s.title.toLowerCase())
    );
    mergedData = [...primaryData, ...uniqueAnidb];
  }

  const isFetching = isAll
    ? resultPrimary.isFetching || resultAnidb.isFetching
    : resultPrimary.isFetching;

  const error = resultPrimary.error || resultAnidb.error;

  return {
    ...resultPrimary,
    isFetching,
    error,
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
