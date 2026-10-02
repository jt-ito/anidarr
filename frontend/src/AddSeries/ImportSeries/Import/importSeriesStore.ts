import { useEffect } from 'react';
import { create } from 'zustand';
import { useShallow } from 'zustand/react/shallow';
import { useAddSeriesOptions } from 'AddSeries/addSeriesOptionsStore';
import { UnmappedFolder } from 'RootFolder/useRootFolders';
import Series, { SeriesMonitor, SeriesType } from 'Series/Series';

export interface UnamppedFolderItem extends UnmappedFolder {
  id: string;
}

export interface ImportSeriesItem {
  id: string;
  monitor: SeriesMonitor;
  path: string;
  qualityProfileId: number;
  relativePath: string;
  seasonFolder: boolean;
  selectedSeries?: Series;
  seriesType: SeriesType;
  name: string;
  hasSearched: boolean;
  // Manual search overrides (otherwise the folder name is searched on all providers)
  searchTerm?: string;
  searchProvider?: string;
  searchResults?: Series[];
  searchError?: string;
  // How sure the lookup was: matched (auto-selected), possible (user must choose), none
  matchStatus?: 'matched' | 'possible' | 'none';
  // The server's explanation of that status, and how many video files the folder holds
  matchReason?: string;
  fileCount?: number;
  // A search for this row is running right now (queued or typed by the user)
  isSearching?: boolean;
  // The folder name without release tags, as the server searched for it
  displayTerm?: string;
  // The import ran but this folder was not added (skipped: nothing to add; failed: error) and why
  importState?: 'skipped' | 'failed';
  importMessage?: string;
}

interface ImportSeriesState {
  items: Record<string, ImportSeriesItem>;
  lookupQueue: string[];
  isProcessing: boolean;
}

const defaultState: ImportSeriesState = {
  items: {},
  lookupQueue: [],
  isProcessing: false,
};

const importSeriesStore = create<ImportSeriesState>()(() => defaultState);

export const useEnsureImportSeriesItems = (
  unmappedFolders: UnamppedFolderItem[]
) => {
  const { monitor, qualityProfileId, seriesType, seasonFolder } =
    useAddSeriesOptions();

  useEffect(() => {
    unmappedFolders.forEach((unmappedFolder) => {
      const existingItem =
        importSeriesStore.getState().items[unmappedFolder.id];

      if (existingItem) {
        return;
      }

      const newItem: ImportSeriesItem = {
        ...unmappedFolder,
        monitor,
        qualityProfileId,
        seriesType,
        seasonFolder,
        hasSearched: false,
      };

      importSeriesStore.setState((state) => ({
        items: {
          ...state.items,
          [unmappedFolder.id]: newItem,
        },
      }));
    });
  }, [unmappedFolders, monitor, qualityProfileId, seriesType, seasonFolder]);
};

export const updateImportSeriesItem = (
  itemData: Partial<ImportSeriesItem> & Pick<ImportSeriesItem, 'id'>
) => {
  importSeriesStore.setState((state) => {
    const existingItem = state.items[itemData.id];

    if (existingItem) {
      return {
        items: {
          ...state.items,
          [itemData.id]: {
            ...existingItem,
            ...itemData,
          },
        },
      };
    }

    return state;
  });
};

export const updateImportSeriesItemByPath = (
  path: string,
  itemData: Partial<ImportSeriesItem>
) => {
  const item = Object.values(importSeriesStore.getState().items).find(
    (i) => i.path === path
  );

  if (item) {
    updateImportSeriesItem({ ...itemData, id: item.id });
  }
};

export const removeImportSeriesItemByPath = (path: string) => {
  importSeriesStore.setState((state) => {
    const item = Object.values(state.items).find((i) => i.path === path);

    if (!item) {
      return state;
    }

    const { [item.id]: removed, ...items } = state.items;

    return { items };
  });
};

export const clearImportSeries = () => {
  importSeriesStore.setState(defaultState);
};

export const startProcessing = () => {
  importSeriesStore.setState((state) => {
    const items = Object.values(state.items).reduce<string[]>((acc, item) => {
      if (!item.hasSearched) {
        acc.push(item.id);
      }

      return acc;
    }, []);

    return { isProcessing: true, lookupQueue: items };
  });
};

export const stopProcessing = () => {
  importSeriesStore.setState({ isProcessing: false, lookupQueue: [] });
};

export const addToLookupQueue = (id: string) => {
  importSeriesStore.setState((state) =>
    state.lookupQueue.includes(id)
      ? state
      : { lookupQueue: [...state.lookupQueue, id] }
  );
};

export const removeFromLookupQueue = (id: string) => {
  importSeriesStore.setState((state) => ({
    lookupQueue: state.lookupQueue.filter((queuedId) => queuedId !== id),
  }));
};

export const useLookupQueue = () => {
  return importSeriesStore((state) => state.lookupQueue);
};

export const isInLookupQueue = (id: string) => {
  return importSeriesStore.getState().lookupQueue.includes(id);
};

export const getImportSeriesItem = (id: string) => {
  return importSeriesStore.getState().items[id];
};

export const useIsCurrentedItemQueued = (id: string) => {
  return importSeriesStore((state) => state.lookupQueue.includes(id));
};

export const useLookupQueueHasItems = () => {
  return importSeriesStore((state) => state.lookupQueue.length > 0);
};

export const useImportSeriesItem = (id: string) => {
  return importSeriesStore((state) => state.items[id]);
};

export const useImportSeriesItems = () => {
  return importSeriesStore(useShallow((state) => Object.values(state.items)));
};

export const getImportSeriesItems = (ids: string[]) => {
  const state = importSeriesStore.getState();

  return ids.reduce<ImportSeriesItem[]>((acc, id) => {
    const item = state.items[id];

    if (item != null) {
      acc.push(item);
    }

    return acc;
  }, []);
};
