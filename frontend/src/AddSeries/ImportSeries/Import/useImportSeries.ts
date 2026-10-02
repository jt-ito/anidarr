import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';
import { addOrUpdateQueryClientItem } from 'Helpers/Hooks/useApiMutation';
import Series from 'Series/Series';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import getQueryString from 'Utilities/Fetch/getQueryString';
import {
  getImportSeriesItems,
  removeImportSeriesItemByPath,
  updateImportSeriesItemByPath,
} from './importSeriesStore';

export const IMPORT_POLL_INTERVAL_MS = 1000;

// A poll that fails this many times in a row stops; the import itself keeps running on the server
const MAX_POLL_FAILURES = 5;

interface ImportJobItem {
  index: number;
  path?: string;
  title?: string;
  state: 'added' | 'skipped' | 'failed';
  message?: string;
  series?: Series;
}

interface ImportJobStatus {
  generation: number;
  total: number;
  done: number;
  isRunning: boolean;
  next: number;
  items: ImportJobItem[];
}

export interface ImportProgress {
  done: number;
  total: number;
}

// tvdbId is 0 for AniDB-only results, so identify a series by whichever id it has.
// Series with no id at all are never treated as duplicates.
const getSeriesKey = (series: Series) => {
  if (series.tvdbId > 0) return `tvdb:${series.tvdbId}`;
  if (series.aniDbId) return `anidb:${series.aniDbId}`;
  if (series.aniListIds?.length) return `anilist:${series.aniListIds[0]}`;
  if (series.malIds?.length) return `mal:${series.malIds[0]}`;

  return undefined;
};

const requestHeaders = () => ({
  'X-Api-Key': window.Sonarr.apiKey,
  'X-Sonarr-Client': 'Sonarr',
});

const fetchStatus = (after: number, generation: number) =>
  fetchJson<ImportJobStatus, unknown>({
    path:
      getQueryPath('/series/import/job') +
      getQueryString({ after, generation }),
    headers: requestHeaders(),
  });

// Adds the selected series in the background on the server and follows its progress, so a big
// import is not one request held open for as long as AniDB takes.
export const useImportSeries = () => {
  const queryClient = useQueryClient();
  const [progress, setProgress] = useState<ImportProgress | null>(null);
  const [error, setError] = useState<ApiError | undefined>(undefined);

  const cursor = useRef({ after: 0, generation: 0 });
  const timer = useRef<ReturnType<typeof setTimeout>>();
  const failures = useRef(0);
  const isMounted = useRef(true);

  const applyItems = useCallback(
    (items: ImportJobItem[]) => {
      items.forEach((item) => {
        if (item.state === 'added' && item.series) {
          const added = item.series;

          queryClient.setQueryData<Series[]>(['/series'], (oldSeries) =>
            oldSeries
              ? addOrUpdateQueryClientItem(oldSeries, added, 'id')
              : oldSeries
          );

          if (item.path) {
            removeImportSeriesItemByPath(item.path);
          }

          return;
        }

        // Not added: keep the row and say why
        if (item.path) {
          updateImportSeriesItemByPath(item.path, {
            importState: item.state === 'failed' ? 'failed' : 'skipped',
            importMessage: item.message,
          });
        }
      });
    },
    [queryClient]
  );

  const poll = useCallback(async () => {
    try {
      const status = await fetchStatus(
        cursor.current.after,
        cursor.current.generation
      );

      if (!isMounted.current) {
        return;
      }

      failures.current = 0;
      cursor.current = { after: status.next, generation: status.generation };
      applyItems(status.items);

      if (status.isRunning) {
        setProgress({ done: status.done, total: status.total });
        timer.current = setTimeout(poll, IMPORT_POLL_INTERVAL_MS);

        return;
      }

      setProgress(null);
      queryClient.invalidateQueries({ queryKey: ['/rootFolder'] });
    } catch (e) {
      if (!isMounted.current) {
        return;
      }

      failures.current += 1;

      if (failures.current >= MAX_POLL_FAILURES) {
        setError(e as ApiError);
        setProgress(null);

        return;
      }

      timer.current = setTimeout(poll, IMPORT_POLL_INTERVAL_MS * 3);
    }
  }, [applyItems, queryClient]);

  const startPolling = useCallback(
    (after: number, generation: number) => {
      cursor.current = { after, generation };
      failures.current = 0;
      clearTimeout(timer.current);
      poll();
    },
    [poll]
  );

  // Coming back to the page while an import is still running: pick up its progress
  useEffect(() => {
    isMounted.current = true;

    fetchStatus(0, 0)
      .then((status) => {
        if (isMounted.current && status.isRunning) {
          setProgress({ done: status.done, total: status.total });
          startPolling(status.next, status.generation);
        }
      })
      .catch(() => undefined);

    return () => {
      isMounted.current = false;
      clearTimeout(timer.current);
    };
  }, [startPolling]);

  const start = useCallback(
    async (series: Series[], deferMetadata: boolean) => {
      setError(undefined);
      setProgress({ done: 0, total: series.length });

      try {
        // Results of earlier imports are not replayed: follow from where the server is now
        const current = await fetchStatus(0, 0);

        await fetchJson<unknown, unknown>({
          path: getQueryPath('/series/import/job'),
          method: 'POST',
          headers: requestHeaders(),
          body: { series, deferMetadata },
        });

        startPolling(current.next, current.generation);
      } catch (e) {
        setError(e as ApiError);
        setProgress(null);
      }
    },
    [startPolling]
  );

  const importSeries = useCallback(
    (ids: string[], deferMetadata = false) => {
      const items = getImportSeriesItems(ids);
      const seenKeys = new Set<string>();

      const allNewSeries = ids.reduce<Series[]>((acc, id) => {
        const item = items.find((i) => i.id === id);
        const selectedSeries = item?.selectedSeries;

        // Make sure we have a selected series and the same series hasn't been added yet.
        const key = selectedSeries && getSeriesKey(selectedSeries);

        if (selectedSeries && !(key && seenKeys.has(key))) {
          if (key) {
            seenKeys.add(key);
          }

          const newSeries: Series = {
            ...selectedSeries,
            monitored: true,
            monitorNewItems: 'all',
            qualityProfileId: item.qualityProfileId,
            path: item.path,
            seriesType: item.seriesType,
            seasonFolder: item.seasonFolder,
            addOptions: {
              monitor: item.monitor,
              searchForMissingEpisodes: false,
              searchForCutoffUnmetEpisodes: false,
            },
            tags: [],
          };

          newSeries.path = item.path;

          acc.push(newSeries);
        }

        return acc;
      }, []);

      if (allNewSeries.length > 0) {
        start(allNewSeries, deferMetadata);
      }
    },
    [start]
  );

  return {
    isImporting: progress !== null,
    importProgress: progress,
    importError: error,
    importSeries,
  };
};
