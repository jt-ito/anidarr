import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { cleanup, render, renderHook, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ImportEstimateNote, {
  formatEtaRange,
} from 'AddSeries/ImportSeries/Import/ImportEstimateNote';
import useImportEstimate, {
  ImportEstimate,
} from 'AddSeries/ImportSeries/Import/useImportEstimate';

const fetchJson = vi.fn();
vi.mock('Utilities/Fetch/fetchJson', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  default: (o: { path: string }) => fetchJson(o),
}));

vi.mock('Utilities/String/translate', () => ({
  default: (key: string, tokens?: object) =>
    tokens ? `${key} ${JSON.stringify(tokens)}` : key,
}));

const estimate = (overrides: Partial<ImportEstimate> = {}): ImportEstimate => ({
  aniDbSeries: 300,
  cached: 0,
  uncached: 300,
  secondsLow: 600,
  secondsHigh: 1800,
  ...overrides,
});

afterEach(cleanup);

beforeEach(() => {
  fetchJson.mockReset();
});

describe('formatEtaRange', () => {
  it('shows a range of minutes', () => {
    expect(formatEtaRange(600, 1800)).toBe('10–30 min');
  });

  it('never rounds a short wait down to zero minutes', () => {
    expect(formatEtaRange(30, 90)).toBe('1–2 min');
  });

  it('collapses a range that rounds to the same number', () => {
    expect(formatEtaRange(120, 140)).toBe('2 min');
  });

  it('says under a minute for very short waits', () => {
    expect(formatEtaRange(0, 50)).toBe('ImportEstimateUnderAMinute');
  });

  it('switches to hours for long waits', () => {
    expect(formatEtaRange(3600, 7200)).toBe('1 h – 2 h');
    expect(formatEtaRange(4200, 7500)).toBe('1 h 10 min – 2 h 5 min');
  });
});

describe('ImportEstimateNote', () => {
  it('gives the time and tells the user "add now" does not make the total faster', () => {
    const { container } = render(
      <ImportEstimateNote estimate={estimate({ cached: 100, uncached: 200 })} />
    );

    expect(container.textContent).toContain('ImportEstimateAniDb');
    expect(container.textContent).toContain('"time":"10–30 min"');
    expect(container.textContent).toContain('"uncached":200');
    expect(container.textContent).toContain('"total":300');
    expect(container.textContent).toContain('ImportEstimateAddNowNote');
  });

  it('says it is quick when everything is already cached', () => {
    const { container } = render(
      <ImportEstimateNote
        estimate={estimate({
          cached: 300,
          uncached: 0,
          secondsLow: 0,
          secondsHigh: 0,
        })}
      />
    );

    expect(container.textContent).toContain('ImportEstimateAllCached');
    expect(container.textContent).not.toContain('ImportEstimateAddNowNote');
  });

  it('shows nothing when no AniDB series are selected', () => {
    const { container } = render(
      <ImportEstimateNote
        estimate={estimate({ aniDbSeries: 0, uncached: 0 })}
      />
    );

    expect(container.textContent).toBe('');
  });
});

describe('useImportEstimate', () => {
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={new QueryClient()}>
      {children}
    </QueryClientProvider>
  );

  it('asks the server about the AniDB ids once it is enabled', async () => {
    fetchJson.mockResolvedValue(estimate());

    const { result } = renderHook(() => useImportEstimate([3, 1, 2], true), {
      wrapper,
    });

    await waitFor(() => expect(result.current).toBeDefined());

    const call = fetchJson.mock.calls[0][0];

    expect(call.path).toContain('/series/import/estimate');
    expect(call.method).toBe('POST');
    expect(call.body).toEqual({ aniDbIds: [3, 1, 2] });
  });

  it('does not ask while folders are still being processed', async () => {
    fetchJson.mockResolvedValue(estimate());

    const { result } = renderHook(() => useImportEstimate([1, 2], false), {
      wrapper,
    });

    await new Promise((r) => setTimeout(r, 50));

    expect(fetchJson).not.toHaveBeenCalled();
    expect(result.current).toBeUndefined();
  });

  it('does not ask when there are no AniDB series', async () => {
    fetchJson.mockResolvedValue(estimate());

    renderHook(() => useImportEstimate([], true), { wrapper });
    await new Promise((r) => setTimeout(r, 50));

    expect(fetchJson).not.toHaveBeenCalled();
  });
});
