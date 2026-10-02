import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useLookupSeries } from 'AddSeries/AddNewSeries/useAddSeries';

const fetchJson = vi.fn();
vi.mock('Utilities/Fetch/fetchJson', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  default: (o: { path: string }) => fetchJson(o),
}));

const tvdbResults = [{ title: 'Overlord', tvdbId: 1 }];
const anidbResults = [{ title: 'Only On AniDB', tvdbId: 0, aniDbId: 9 }];

let anidbGate: Promise<unknown> = Promise.resolve();

beforeEach(() => {
  anidbGate = Promise.resolve();
  fetchJson.mockReset();
  fetchJson.mockImplementation(async ({ path }: { path: string }) => {
    if (path.includes('provider=anidb')) {
      await anidbGate;
      return anidbResults;
    }

    return tvdbResults;
  });
});

const wrapper = ({ children }: { children: React.ReactNode }) => (
  <QueryClientProvider
    client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
  >
    {children}
  </QueryClientProvider>
);

describe('useLookupSeries', () => {
  it('returns a stable data reference across renders (effect-loop regression)', async () => {
    const { result, rerender } = renderHook(() => useLookupSeries('overlord'), {
      wrapper,
    });

    await waitFor(() => expect(result.current.data).toHaveLength(2));

    const first = result.current.data;
    rerender();
    rerender();
    rerender();

    expect(result.current.data).toBe(first);
  });

  it('is not fetched until both providers have answered', async () => {
    // eslint-disable-next-line init-declarations
    let release!: () => void;
    anidbGate = new Promise<void>((r) => (release = r));

    const { result } = renderHook(() => useLookupSeries('overlord'), {
      wrapper,
    });

    await waitFor(() => expect(fetchJson).toHaveBeenCalledTimes(2));
    await new Promise((r) => setTimeout(r, 50));
    expect(result.current.isFetched).toBe(false);

    release();
    await waitFor(() => expect(result.current.isFetched).toBe(true));
    expect(result.current.data).toHaveLength(2);
  });

  it('refetch re-runs both providers when searching all', async () => {
    const { result } = renderHook(() => useLookupSeries('overlord'), {
      wrapper,
    });
    await waitFor(() => expect(result.current.isFetched).toBe(true));
    fetchJson.mockClear();

    await result.current.refetch();

    expect(fetchJson).toHaveBeenCalledTimes(2);
  });

  it('refetch only hits the chosen provider otherwise', async () => {
    const { result } = renderHook(() => useLookupSeries('overlord', 'anidb'), {
      wrapper,
    });
    await waitFor(() => expect(result.current.isFetched).toBe(true));
    fetchJson.mockClear();

    await result.current.refetch();

    expect(fetchJson).toHaveBeenCalledTimes(1);
  });
});
