import { create } from 'zustand';

interface InteractiveImportViewState {
  search: string;
  showAll: boolean;
  // Rows selected when the view last changed: they stay visible so a search can't silently drop them
  pinnedIds: number[];
}

const initialState: InteractiveImportViewState = {
  search: '',
  showAll: false,
  pinnedIds: [],
};

export const useInteractiveImportViewStore = create<InteractiveImportViewState>(
  () => initialState
);

export const setInteractiveImportSearch = (
  search: string,
  pinnedIds: number[]
) => useInteractiveImportViewStore.setState({ search, pinnedIds });

export const setInteractiveImportShowAll = (pinnedIds: number[]) =>
  useInteractiveImportViewStore.setState({ showAll: true, pinnedIds });

export const resetInteractiveImportView = () =>
  useInteractiveImportViewStore.setState(initialState);
