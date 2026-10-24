import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import ModelBase from 'App/ModelBase';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Language from 'Language/Language';
import { QualityModel } from 'Quality/Quality';
import clientSideFilterAndSort from 'Utilities/Filter/clientSideFilterAndSort';
import InteractiveImport from './InteractiveImport';
import { useInteractiveImportOptions } from './interactiveImportOptionsStore';
import { useInteractiveImportViewStore } from './interactiveImportViewStore';
import ReleaseType from './ReleaseType';

const DEFAULT_ITEMS: InteractiveImport[] = [];

// Rows rendered before "show all": each row mounts a dozen cells and eight dialogs
export const INTERACTIVE_IMPORT_ROW_LIMIT = 100;

const normalize = (text: string) =>
  text.toLowerCase().replace(/[._\-[\]()]+/g, ' ');

interface InteractiveImportParams {
  downloadIds?: string[];
  seriesId?: number;
  seasonNumber?: number;
  folder?: string;
  filterExistingFiles?: boolean;
}

const useInteractiveImport = (params: InteractiveImportParams) => {
  const { sortKey, sortDirection } = useInteractiveImportOptions();

  const { data, isFetching, isFetched, error, refetch } = useApiQuery<
    InteractiveImport[]
  >({
    path: '/manualimport',
    queryParams: { ...params },
    queryOptions: {
      // Set to 0 so we don't persist the data after the modal is closed and the query becomes inactive
      gcTime: 0,
      // Disable refetch on window focus to prevent refetching when the user switch tabs
      refetchOnWindowFocus: false,
    },
  });

  const items = data ?? DEFAULT_ITEMS;
  const originalItems = [...items];

  const search = useInteractiveImportViewStore((state) => state.search);
  const showAll = useInteractiveImportViewStore((state) => state.showAll);
  const pinnedIds = useInteractiveImportViewStore((state) => state.pinnedIds);

  const { sortedItems, shownCount, totalCount } = useMemo(() => {
    const sortPredicates = {
      series: (item: InteractiveImport) => item.series?.title || '',
      quality: (item: InteractiveImport) => item.quality?.quality?.name || '',
      languages: (item: InteractiveImport) =>
        item.languages?.map((l) => l.name).join(', ') || '',
    };

    let { data: sorted } = clientSideFilterAndSort(items, {
      sortKey,
      sortDirection,
      sortPredicates,
    });

    // Files matched to a series come first, unless another column was sorted on purpose
    if (sortKey === 'relativePath') {
      sorted = [
        ...sorted.filter((item) => item.series),
        ...sorted.filter((item) => !item.series),
      ];
    }

    const terms = normalize(search).split(' ').filter(Boolean);

    if (terms.length) {
      sorted = sorted.filter((item) => {
        const text = normalize(
          `${item.relativePath} ${item.series?.title ?? ''}`
        );

        return terms.every((term) => text.includes(term));
      });
    }

    const total = sorted.length;

    if (!showAll && total > INTERACTIVE_IMPORT_ROW_LIMIT) {
      const pinned = new Set(pinnedIds);

      sorted = sorted.filter(
        (item, index) =>
          index < INTERACTIVE_IMPORT_ROW_LIMIT || pinned.has(item.id)
      );
    }

    return {
      sortedItems: sorted,
      shownCount: sorted.length,
      totalCount: total,
    };
  }, [items, sortKey, sortDirection, search, showAll, pinnedIds]);

  return {
    data: sortedItems,
    shownCount,
    totalCount,
    originalItems,
    isFetching,
    isFetched,
    error,
    refetch,
  };
};

export default useInteractiveImport;

export const useUpdateInteractiveImportItem = () => {
  const queryClient = useQueryClient();

  const updateInteractiveImportItem = useCallback(
    (id: number, updates: Partial<InteractiveImport>) => {
      queryClient.setQueriesData(
        { queryKey: ['/manualimport'] },
        (oldData: InteractiveImport[] | undefined) => {
          if (!oldData) {
            return oldData;
          }

          return oldData.map((item) => {
            return item.id === id ? { ...item, ...updates } : item;
          });
        }
      );
    },
    [queryClient]
  );

  return { updateInteractiveImportItem };
};

export const useUpdateInteractiveImportItems = () => {
  const queryClient = useQueryClient();

  const updateInteractiveImportItems = useCallback(
    (ids: number[], updates: Partial<InteractiveImport>) => {
      queryClient.setQueriesData(
        { queryKey: ['/manualimport'] },
        (oldData: InteractiveImport[] | undefined) => {
          if (!oldData) {
            return oldData;
          }

          return oldData.map((item) => {
            return ids.includes(item.id) ? { ...item, ...updates } : item;
          });
        }
      );
    },
    [queryClient]
  );

  return { updateInteractiveImportItems };
};

interface ReprocessInteractiveImportItem extends ModelBase {
  path: string;
  relativePath: string;
  seriesId: number | undefined;
  seasonNumber: number | undefined;
  episodeIds: number[] | undefined;
  quality: QualityModel | undefined;
  languages: Language[];
  releaseGroup: string | undefined;
  downloadId: string | undefined;
  indexerFlags: number;
  releaseType: ReleaseType;
}

export const useReprocessInteractiveImportItems = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<
    InteractiveImport[],
    ReprocessInteractiveImportItem[]
  >({
    path: '/manualimport',
    method: 'POST',
    mutationOptions: {
      onSuccess: (updatedItems) => {
        queryClient.setQueriesData(
          { queryKey: ['/manualimport'] },
          (oldData: InteractiveImport[] | undefined) => {
            if (!oldData) {
              return oldData;
            }

            return oldData.map((oldItem: InteractiveImport) => {
              const reprocessedItem = updatedItems.find(
                (updatedItem) => updatedItem.id === oldItem.id
              );

              return reprocessedItem ? reprocessedItem : oldItem;
            });
          }
        );
      },
    },
  });

  const reprocessInteractiveImportItems = useCallback(
    (ids: number[]) => {
      const queries = queryClient.getQueriesData<InteractiveImport[]>({
        queryKey: ['/manualimport'],
      });
      const currentData = queries.flatMap(([, data]) => data || []);

      if (currentData.length === 0) {
        return;
      }

      const requestPayload = ids.reduce<ReprocessInteractiveImportItem[]>(
        (acc, id) => {
          const item = currentData.find((i) => i.id === id);

          if (!item) {
            return acc;
          }

          acc.push({
            id,
            path: item.path,
            relativePath: item.relativePath,
            seriesId: item.series ? item.series.id : undefined,
            seasonNumber: item.seasonNumber,
            episodeIds: (item.episodes || []).map((e) => e.id),
            quality: item.quality,
            languages: item.languages,
            releaseGroup: item.releaseGroup,
            indexerFlags: item.indexerFlags,
            releaseType: item.releaseType,
            downloadId: item.downloadId,
          });

          return acc;
        },
        []
      );

      mutate(requestPayload);
    },
    [queryClient, mutate]
  );

  return {
    reprocessInteractiveImportItems,
    isReprocessing: isPending,
    error,
  };
};
