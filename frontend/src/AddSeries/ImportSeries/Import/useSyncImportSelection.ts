import { useEffect, useRef } from 'react';
import { useSelect } from 'App/Select/SelectContext';
import Series from 'Series/Series';
import { findExistingSeries } from 'Series/useExistingSeries';
import useSeries from 'Series/useSeries';
import { ImportSeriesItem, useImportSeriesItems } from './importSeriesStore';

// A stable value for "library not loaded yet", so the cache isn't cleared on every render
const EMPTY_LIBRARY: Series[] = [];

// Keeps "which rows can be selected / are selected" in step with the search results for
// EVERY folder. This lives at page level because the table is virtualized: rows that were
// never scrolled into view are never mounted, so selection driven from the rows left them
// out of the "Import N series" count and out of the import itself.
//
// A row is selectable (and selected) once it has a series and that series isn't already in
// the library.
function useSyncImportSelection() {
  const items = useImportSeriesItems();
  const { data } = useSeries();
  const library = data ?? EMPTY_LIBRARY;
  const { toggleSelected, toggleDisabled } = useSelect<ImportSeriesItem>();

  // id -> what was last applied, so unchanged rows are skipped
  const applied = useRef(
    new Map<string, { series: unknown; importState?: string }>()
  );
  const appliedLibrary = useRef<unknown>(null);

  useEffect(() => {
    // The library changing can change whether any row already exists
    if (appliedLibrary.current !== library) {
      appliedLibrary.current = library;
      applied.current.clear();
    }

    items.forEach((item) => {
      // Unchanged since last time (a new library clears this cache): skip, so a result
      // arriving for one folder doesn't rescan the library for all the others
      const last = applied.current.get(item.id);

      if (
        last &&
        last.series === item.selectedSeries &&
        last.importState === item.importState
      ) {
        return;
      }

      const isExisting = !!findExistingSeries(library, item.selectedSeries);

      applied.current.set(item.id, {
        series: item.selectedSeries,
        importState: item.importState,
      });

      // "Skipped" means the server found nothing to add (already there): not worth selecting again
      const canSelect =
        !!item.selectedSeries && !isExisting && item.importState !== 'skipped';

      // Enable first (a disabled row ignores selection), select, then disable if needed
      toggleDisabled(item.id, false);
      toggleSelected({
        id: item.id,
        isSelected: canSelect,
        shiftKey: false,
      });
      toggleDisabled(item.id, !canSelect);
    });
  }, [items, library, toggleSelected, toggleDisabled]);
}

export default useSyncImportSelection;
