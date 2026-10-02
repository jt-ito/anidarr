import { QueryClient } from '@tanstack/react-query';
import { fetchFolderMatch } from 'AddSeries/AddNewSeries/useAddSeries';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import {
  getImportSeriesItem,
  isInLookupQueue,
  removeFromLookupQueue,
  updateImportSeriesItem,
} from './importSeriesStore';

// Searches for the series a folder holds and stores the outcome on its import row.
//
// Two callers:
// - the background queue (isBackground): bulk work, low priority on the server, and
//   dropped if the user cancels processing while it runs.
// - the user typing, refreshing or scanning AniDB (manual): runs right away, ahead of
//   the queue, with high priority on the server.
//
// Returns true when the result was applied, false when it was dropped as out of date.
async function lookupFolder(
  queryClient: QueryClient,
  id: string,
  isBackground: boolean
): Promise<boolean> {
  const item = getImportSeriesItem(id);

  if (!item) {
    removeFromLookupQueue(id);
    return false;
  }

  // A manual search replaces whatever the queue had planned for this row
  if (!isBackground) {
    removeFromLookupQueue(id);
  }

  const term = item.searchTerm ?? item.name;
  const provider = item.searchProvider;

  // Nothing to search for; settle the row so it doesn't wait forever
  if (!term) {
    updateImportSeriesItem({
      id,
      hasSearched: true,
      selectedSeries: undefined,
      searchResults: [],
      searchError: undefined,
      matchStatus: 'none',
      isSearching: false,
    });
    removeFromLookupQueue(id);
    return true;
  }

  updateImportSeriesItem({ id, isSearching: true });

  let match: Awaited<ReturnType<typeof fetchFolderMatch>> | undefined =
    undefined;
  let error: ApiError | undefined = undefined;

  // Only the default search (the folder name) may look inside the folder; a term the
  // user typed is just a search.
  const isFolderSearch = item.searchTerm === undefined && !provider;

  try {
    match = await fetchFolderMatch(
      queryClient,
      term,
      provider,
      isFolderSearch ? item.path : undefined,
      isBackground
    );
  } catch (e) {
    error = e as ApiError;
  }

  const current = getImportSeriesItem(id);

  // Row removed while searching (imported, or the page was left)
  if (!current) {
    removeFromLookupQueue(id);
    return false;
  }

  // Background work is cancelled by emptying the queue
  if (isBackground && !isInLookupQueue(id)) {
    updateImportSeriesItem({ id, isSearching: false });
    return false;
  }

  // The search changed while this one ran: drop the result. A newer search owns the row.
  if (
    (current.searchTerm ?? current.name) !== term ||
    current.searchProvider !== provider
  ) {
    if (!isBackground) {
      return false;
    }

    // Stay queued so the next pass searches the new term
    updateImportSeriesItem({ id, isSearching: false });
    return false;
  }

  // Only a confident match is selected for the user. Ambiguous or weak results
  // are kept as choices, since a wrong guess is worse than no match.
  updateImportSeriesItem({
    id,
    hasSearched: true,
    isSearching: false,
    selectedSeries: match?.status === 'matched' ? match.results[0] : undefined,
    searchResults: match?.results ?? [],
    matchStatus: match?.status ?? 'none',
    matchReason: match?.reason,
    fileCount: match?.fileCount,
    importState: undefined,
    importMessage: undefined,
    // Show the cleaned folder name in the search box; never overwrite what the user typed
    ...(isFolderSearch && match?.searchTerm
      ? { displayTerm: match.searchTerm }
      : {}),
    searchError: error ? getErrorMessage(error) : undefined,
  });
  removeFromLookupQueue(id);

  return true;
}

export default lookupFolder;
