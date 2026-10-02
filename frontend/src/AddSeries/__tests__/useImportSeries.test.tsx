import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, renderHook, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  clearImportSeries,
  getImportSeriesItems,
  updateImportSeriesItem,
  useEnsureImportSeriesItems,
} from 'AddSeries/ImportSeries/Import/importSeriesStore';
import { useImportSeries } from 'AddSeries/ImportSeries/Import/useImportSeries';

const fetchJson = vi.fn();
vi.mock('Utilities/Fetch/fetchJson', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  default: (o: { path: string; method?: string }) => fetchJson(o),
}));

const idle = {
  generation: 1,
  total: 0,
  done: 0,
  isRunning: false,
  next: 0,
  items: [],
};

let queryClient = new QueryClient();

const wrapper = ({ children }: { children: React.ReactNode }) => (
  <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
);

const folders = ['a', 'b', 'c'].map((n) => ({
  id: n,
  name: n,
  path: `/lib/${n}`,
  relativePath: n,
}));

function setup(selected: Record<string, object>) {
  renderHook(() => useEnsureImportSeriesItems(folders as never), { wrapper });
  Object.entries(selected).forEach(([id, selectedSeries]) =>
    updateImportSeriesItem({ id, selectedSeries: selectedSeries as never })
  );

  return renderHook(() => useImportSeries(), { wrapper });
}

// GET status answers with `statuses` in turn (the last one repeats); POST just accepts
function givenServer(...statuses: object[]) {
  let calls = 0;

  fetchJson.mockImplementation(async (o: { path: string; method?: string }) => {
    if (o.method === 'POST') {
      return undefined;
    }

    const status = statuses[Math.min(calls, statuses.length - 1)];

    calls += 1;

    return status;
  });
}

const postCall = () =>
  fetchJson.mock.calls.find((c) => c[0].method === 'POST')?.[0];

afterEach(cleanup);

beforeEach(() => {
  clearImportSeries();
  queryClient = new QueryClient();
  fetchJson.mockReset();
});

describe('useImportSeries', () => {
  it('sends every selected AniDB-only series (tvdbId 0) as one background job', async () => {
    givenServer(idle);
    const { result } = setup({
      a: { title: 'A', tvdbId: 0, aniDbId: 1 },
      b: { title: 'B', tvdbId: 0, aniDbId: 2 },
      c: { title: 'C', tvdbId: 0, aniDbId: 3 },
    });

    act(() => result.current.importSeries(['a', 'b', 'c']));

    await waitFor(() => expect(postCall()).toBeDefined());
    expect(postCall().path).toContain('/series/import/job');
    expect(postCall().body.series).toHaveLength(3);
    expect(postCall().body.deferMetadata).toBe(false);
  });

  it('passes the add-now option through', async () => {
    givenServer(idle);
    const { result } = setup({ a: { title: 'A', tvdbId: 1 } });

    act(() => result.current.importSeries(['a'], true));

    await waitFor(() => expect(postCall()).toBeDefined());
    expect(postCall().body.deferMetadata).toBe(true);
  });

  it('removes the rows that were added and adds them to the series list', async () => {
    queryClient.setQueryData(['/series'], []);
    givenServer(idle, {
      ...idle,
      total: 1,
      done: 1,
      next: 1,
      items: [
        {
          index: 0,
          path: '/lib/a',
          state: 'added',
          series: { id: 10, title: 'A', path: '/lib/a' },
        },
      ],
    });
    const { result } = setup({
      a: { title: 'A', tvdbId: 1 },
      b: { title: 'B', tvdbId: 2 },
    });

    act(() => result.current.importSeries(['a', 'b']));

    await waitFor(() =>
      expect(getImportSeriesItems(['a', 'b']).map((i) => i.id)).toEqual(['b'])
    );
    expect(queryClient.getQueryData<{ id: number }[]>(['/series'])).toEqual([
      { id: 10, title: 'A', path: '/lib/a' },
    ]);
  });

  it('keeps rows the server skipped or failed, with the reason', async () => {
    givenServer(idle, {
      ...idle,
      total: 2,
      done: 2,
      next: 2,
      items: [
        {
          index: 0,
          path: '/lib/b',
          state: 'skipped',
          message: 'Already in your library',
        },
        { index: 1, path: '/lib/c', state: 'failed', message: 'boom' },
      ],
    });
    const { result } = setup({
      b: { title: 'B', tvdbId: 2 },
      c: { title: 'C', tvdbId: 3 },
    });

    act(() => result.current.importSeries(['b', 'c']));

    await waitFor(() =>
      expect(getImportSeriesItems(['b'])[0].importState).toBe('skipped')
    );
    const [b, c] = getImportSeriesItems(['b', 'c']);

    expect(b.importMessage).toBe('Already in your library');
    expect(c.importState).toBe('failed');
    expect(c.importMessage).toBe('boom');
  });

  it('reports progress while the import runs and clears it when done', async () => {
    // the hook asks once when it mounts, once before starting, then polls
    givenServer(
      idle,
      idle,
      { ...idle, total: 3, done: 1, isRunning: true, next: 1 },
      { ...idle, total: 3, done: 3, isRunning: false, next: 3 }
    );
    const { result } = setup({ a: { title: 'A', tvdbId: 1 } });

    act(() => result.current.importSeries(['a']));

    await waitFor(() =>
      expect(result.current.importProgress).toEqual({ done: 1, total: 3 })
    );
    expect(result.current.isImporting).toBe(true);

    await waitFor(() => expect(result.current.isImporting).toBe(false), {
      timeout: 4000,
    });
  });

  it('picks up an import that is already running when the page opens', async () => {
    // on mount it sees the running import (2/5), then keeps polling (3/5) until it finishes
    givenServer(
      { ...idle, total: 5, done: 2, isRunning: true, next: 2 },
      { ...idle, total: 5, done: 3, isRunning: true, next: 3 },
      { ...idle, total: 5, done: 5, isRunning: false, next: 5 }
    );

    const { result } = setup({});

    await waitFor(() =>
      expect(result.current.importProgress).toEqual({ done: 3, total: 5 })
    );
    await waitFor(() => expect(result.current.isImporting).toBe(false), {
      timeout: 4000,
    });
  });

  it('reports an error when the import cannot be started', async () => {
    fetchJson.mockImplementation(async (o: { method?: string }) => {
      if (o.method === 'POST') {
        throw new Error('nope');
      }

      return idle;
    });
    const { result } = setup({ a: { title: 'A', tvdbId: 1 } });

    act(() => result.current.importSeries(['a']));

    await waitFor(() => expect(result.current.importError).toBeDefined());
    expect(result.current.isImporting).toBe(false);
  });
});
