import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, render, renderHook, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import SeriesIndexRefreshSeriesButton from 'Series/Index/SeriesIndexRefreshSeriesButton';
import { MetadataProgress } from 'Series/useMetadataProgress';

const fetchJson = vi.fn();
vi.mock('Utilities/Fetch/fetchJson', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  default: (o: { path: string }) => fetchJson(o),
}));

let progress: MetadataProgress = { pending: 0, done: 0, total: 0 };
let isRefreshing = false;

vi.mock('Series/useMetadataProgress', async (importOriginal) => {
  const actual = await importOriginal<{ default: () => MetadataProgress }>();

  return {
    ...actual,
    // the button test sets `progress`; the hook test needs the real hook, so it imports it directly
    default: () => progress,
    useRealMetadataProgress: actual.default,
  };
});

vi.mock('Commands/useCommands', () => ({
  useCommandExecuting: () => isRefreshing,
  useExecuteCommand: () => vi.fn(),
}));
vi.mock('App/Select/SelectContext', () => ({
  useSelect: () => ({ anySelected: false, getSelectedIds: () => [] }),
}));
vi.mock('Series/useSeries', () => ({
  useSeriesIndex: () => ({ data: [], totalItems: 3 }),
}));
vi.mock('Utilities/String/translate', () => ({
  default: (key: string, tokens?: object) =>
    tokens ? `${key} ${JSON.stringify(tokens)}` : key,
}));

afterEach(cleanup);

beforeEach(() => {
  progress = { pending: 0, done: 0, total: 0 };
  isRefreshing = false;
  fetchJson.mockReset();
});

const button = () => (
  <SeriesIndexRefreshSeriesButton
    isSelectMode={false}
    selectedFilterKey="all"
  />
);

const isSpinning = (container: HTMLElement) =>
  !!container.querySelector('[class*="spin"]');

describe('Update all button', () => {
  it('spins and says how far the AniDB update has got', () => {
    progress = { pending: 48, done: 12, total: 60 };

    const { container } = render(button());

    expect(isSpinning(container)).toBe(true);

    const tooltip = container.querySelector(
      '[title*="UpdatingSeriesProgress"]'
    );

    expect(tooltip?.getAttribute('title')).toContain('"done":12,"total":60');
  });

  it('stays still with its normal label when nothing is being updated', () => {
    const { container } = render(button());

    expect(isSpinning(container)).toBe(false);
    expect(container.querySelector('[title="UpdateAll"]')).not.toBeNull();
  });

  it('still spins for a normal refresh', () => {
    isRefreshing = true;

    const { container } = render(button());

    expect(isSpinning(container)).toBe(true);
  });

  it('does not spin when the AniDB client is not set up, and says what to do', () => {
    progress = { pending: 5, done: 0, total: 5, clientConfigured: false };

    const { container } = render(button());

    expect(isSpinning(container)).toBe(false);
    expect(
      container.querySelector('[title*="UpdatingSeriesNeedsClient"]')
    ).not.toBeNull();
  });

  it('does not spin while AniDB is blocking us, and says why', () => {
    progress = {
      pending: 5,
      done: 0,
      total: 5,
      blockedUntil: new Date(Date.now() + 3600000).toISOString(),
    };

    const { container } = render(button());

    expect(isSpinning(container)).toBe(false);
    expect(
      container.querySelector('[title*="UpdatingSeriesBlocked"]')
    ).not.toBeNull();
  });
});

describe('useMetadataProgress', () => {
  it('reloads the library when the last series finishes updating', async () => {
    const mod = await import('Series/useMetadataProgress');
    const real = (
      mod as unknown as { useRealMetadataProgress: () => MetadataProgress }
    ).useRealMetadataProgress;

    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    fetchJson
      .mockResolvedValueOnce({ pending: 2, done: 0, total: 2 })
      .mockResolvedValue({ pending: 0, done: 0, total: 0 });

    const { result } = renderHook(() => real(), {
      wrapper: ({ children }: { children: React.ReactNode }) => (
        <QueryClientProvider client={queryClient}>
          {children}
        </QueryClientProvider>
      ),
    });

    await waitFor(() => expect(result.current.pending).toBe(2));

    await queryClient.refetchQueries({
      queryKey: ['/series/metadata-progress'],
    });

    await waitFor(() => expect(result.current.pending).toBe(0));
    await waitFor(() =>
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ['/series'] })
    );
  });
});
