import moment from 'moment';
import { create } from 'zustand';
import sortByProp from 'Utilities/Array/sortByProp';

const MAXIMUM_RECENT_FOLDERS = 10;

interface RecentFolder {
  folder: string;
  lastUsed: string;
}

interface FavoriteFolder {
  folder: string;
}

export interface PinnedPath {
  id: string;
  label: string;
  path: string;
}

interface InteractiveImportFoldersState {
  recentFolders: RecentFolder[];
  favoriteFolders: FavoriteFolder[];
  pinnedPaths: PinnedPath[];
  activePinnedPathId: string | null;
}

interface InteractiveImportFoldersActions {
  hydrate: (state: Partial<InteractiveImportFoldersState>) => void;
}

export type InteractiveImportFoldersStore = InteractiveImportFoldersState &
  InteractiveImportFoldersActions;

const store = create<InteractiveImportFoldersStore>((set) => ({
  recentFolders: [],
  favoriteFolders: [],
  pinnedPaths: [],
  activePinnedPathId: null,
  hydrate: (state) => set(state),
}));

export const useInteractiveImportFolders = store;

export const useRecentFolders = () => {
  return store((state) => state.recentFolders);
};

export const useFavoriteFolders = () => {
  return store((state) => state.favoriteFolders);
};

export const usePinnedPaths = () => {
  return store((state) => state.pinnedPaths);
};

export const useActivePinnedPathId = () => {
  return store((state) => state.activePinnedPathId);
};

export const addRecentFolder = (folder: string) => {
  store.setState((state) => {
    const recentFolder: RecentFolder = {
      folder,
      lastUsed: moment().toISOString(),
    };
    const recentFolders = [...state.recentFolders];
    const index = recentFolders.findIndex((r) => r.folder === folder);

    if (index > -1) {
      recentFolders.splice(index, 1);
    }

    recentFolders.push(recentFolder);

    const sliceIndex = Math.max(
      recentFolders.length - MAXIMUM_RECENT_FOLDERS,
      0
    );

    return {
      ...state,
      recentFolders: recentFolders.slice(sliceIndex),
    };
  });
};

export const removeRecentFolder = (folder: string) => {
  store.setState((state) => {
    const recentFolders = [...state.recentFolders];
    const index = recentFolders.findIndex((r) => r.folder === folder);

    if (index > -1) {
      recentFolders.splice(index, 1);
    }

    return {
      ...state,
      recentFolders,
    };
  });
};

export const addFavoriteFolder = (folder: string) => {
  store.setState((state) => {
    const favoriteFolder: FavoriteFolder = { folder };
    const favoriteFolders = [...state.favoriteFolders, favoriteFolder].sort(
      sortByProp('folder')
    );

    return {
      ...state,
      favoriteFolders,
    };
  });
};

export const removeFavoriteFolder = (folder: string) => {
  store.setState((state) => {
    const favoriteFolders = state.favoriteFolders.filter(
      (item) => item.folder !== folder
    );

    return {
      ...state,
      favoriteFolders,
    };
  });
};

export const getInteractiveImportFolders = () => {
  return store.getState();
};

export const getRecentFolders = () => {
  return store.getState().recentFolders;
};

export const getFavoriteFolders = () => {
  return store.getState().favoriteFolders;
};

export const addPinnedPath = (label: string, path: string) => {
  store.setState((state) => {
    const id = Date.now().toString(); // simple ID generation
    const pinnedPath: PinnedPath = { id, label, path };
    const pinnedPaths = [...state.pinnedPaths, pinnedPath].sort(
      sortByProp('label')
    );

    return {
      ...state,
      pinnedPaths,
      activePinnedPathId: id, // Automatically make it active when pinned
    };
  });
};

export const removePinnedPath = (id: string) => {
  store.setState((state) => {
    const pinnedPaths = state.pinnedPaths.filter((item) => item.id !== id);
    const activePinnedPathId =
      state.activePinnedPathId === id ? null : state.activePinnedPathId;

    return {
      ...state,
      pinnedPaths,
      activePinnedPathId,
    };
  });
};

export const setActivePinnedPath = (id: string) => {
  store.setState((state) => ({
    ...state,
    activePinnedPathId: id,
  }));
};

export const clearActivePinnedPath = () => {
  store.setState((state) => ({
    ...state,
    activePinnedPathId: null,
  }));
};
