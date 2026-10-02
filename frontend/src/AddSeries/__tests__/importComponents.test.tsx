import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  act,
  cleanup,
  fireEvent,
  render,
  renderHook,
  waitFor,
} from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ImportSeries from 'AddSeries/ImportSeries/Import/ImportSeries';
import {
  addToLookupQueue,
  clearImportSeries,
  getImportSeriesItems,
  isInLookupQueue,
  stopProcessing,
  updateImportSeriesItem,
  useEnsureImportSeriesItems,
  useLookupQueueHasItems,
} from 'AddSeries/ImportSeries/Import/importSeriesStore';
import ImportSeriesSearchResult from 'AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesSearchResult';
import ImportSeriesSelectSeries from 'AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesSelectSeries';
import useLookupQueueWorker from 'AddSeries/ImportSeries/Import/useLookupQueueWorker';
import useSyncImportSelection from 'AddSeries/ImportSeries/Import/useSyncImportSelection';
import { SelectProvider, useSelect } from 'App/Select/SelectContext';
import useExistingSeries from 'Series/useExistingSeries';

const fetchJson = vi.fn();
vi.mock('Utilities/Fetch/fetchJson', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  default: (o: { path: string }) => fetchJson(o),
}));

let librarySeries: object[] = [];
vi.mock('Series/useSeries', () => ({
  default: () => ({ data: librarySeries }),
}));

vi.mock('Utilities/String/translate', () => ({
  default: (k: string) => k,
  translate: (k: string) => k,
}));

vi.mock('AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesTitle', () => ({
  default: (p: { title: string; isExistingSeries: boolean }) => (
    <span data-testid="title" data-existing={String(p.isExistingSeries)}>
      {p.title}
    </span>
  ),
}));

vi.mock('react-router', () => ({
  useParams: () => ({ rootFolderId: '1' }),
}));
vi.mock('RootFolder/useRootFolders', () => ({
  default: () => ({
    isFetching: false,
    isFetched: true,
    error: null,
    data: [],
  }),
  useRootFolder: () => undefined,
}));
vi.mock('Settings/Profiles/Quality/useQualityProfiles', () => ({
  useQualityProfilesData: () => [],
}));
vi.mock('Components/Page/PageContent', () => ({
  default: ({ children }: { children: React.ReactNode }) => (
    <div>{children}</div>
  ),
}));
vi.mock('Components/Page/PageContentBody', () => ({
  default: React.forwardRef<HTMLDivElement, { children: React.ReactNode }>(
    ({ children }, ref) => <div ref={ref}>{children}</div>
  ),
}));

const wrapper = ({ children }: { children: React.ReactNode }) => (
  <QueryClientProvider
    client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
  >
    {children}
  </QueryClientProvider>
);

const folder = (id: string, name = id) => ({
  id,
  name,
  path: `/lib/${id}`,
  relativePath: id,
});

function seed(...folders: ReturnType<typeof folder>[]) {
  renderHook(() => useEnsureImportSeriesItems(folders as never), { wrapper });
}

const anidbOnly = {
  title: 'AniDB Only',
  tvdbId: 0,
  aniDbId: 42,
  seriesType: 'standard',
};

const matched = (results: object[]) => ({ status: 'matched', results });

const searched = (id: string) =>
  getImportSeriesItems([id])[0]?.hasSearched === true;

afterEach(cleanup);

beforeEach(() => {
  clearImportSeries();
  librarySeries = [];
  fetchJson.mockReset();
  fetchJson.mockImplementation(async () => matched([anidbOnly]));
});

describe('manual import search result poster', () => {
  const poster = {
    coverType: 'poster',
    url: '/MediaCoverProxy/abc123/poster.jpg',
    remoteUrl: 'https://cdn.anidb.net/images/main/1234.jpg',
  };

  const renderResult = (images: object[]) =>
    render(
      <ImportSeriesSearchResult
        tvdbId={0}
        aniDbId={4337}
        primaryMetadataProvider="anidb"
        title="Sex Exchange"
        year={2006}
        images={images as never}
        index={0}
        onPress={vi.fn()}
      />,
      { wrapper }
    );

  it('loads the poster through the server proxy, not straight from the image host', () => {
    const { container } = renderResult([poster]);

    expect(container.querySelector('img')?.getAttribute('src')).toBe(
      poster.url
    );
  });

  it('falls back to the original address when there is no proxied one', () => {
    const { container } = renderResult([{ ...poster, url: '' }]);

    expect(container.querySelector('img')?.getAttribute('src')).toBe(
      poster.remoteUrl
    );
  });

  it('shows no empty box when the poster fails to load', () => {
    const { container } = renderResult([poster]);

    fireEvent.error(container.querySelector('img') as Element);

    expect(container.querySelector('img')).toBeNull();
  });
});

describe('manual import row', () => {
  it('#2 flags an AniDB-only result already in the library as existing', async () => {
    librarySeries = [{ tvdbId: -42, aniDbId: 42 }];
    seed(folder('a'));
    updateImportSeriesItem({
      id: 'a',
      hasSearched: true,
      selectedSeries: anidbOnly as never,
      searchResults: [anidbOnly as never],
    });

    const { findByTestId } = render(
      <ImportSeriesSelectSeries id="a" onInputChange={vi.fn()} />,
      { wrapper }
    );

    expect((await findByTestId('title')).dataset.existing).toBe('true');
  });

  it('shows possible matches instead of "no match" when the server was unsure', async () => {
    seed(folder('a'));
    updateImportSeriesItem({
      id: 'a',
      hasSearched: true,
      matchStatus: 'possible',
      searchResults: [anidbOnly as never],
    });

    const { findByText } = render(
      <ImportSeriesSelectSeries id="a" onInputChange={vi.fn()} />,
      { wrapper }
    );

    await findByText('PossibleMatchesFound');
  });

  it('shows the cleaned folder name in the search box, not the release name', async () => {
    seed(folder('a', '( HT ) Imouto Twins'));
    updateImportSeriesItem({
      id: 'a',
      hasSearched: true,
      matchStatus: 'possible',
      displayTerm: 'Imouto Twins',
      searchResults: [anidbOnly as never],
    });

    const { findByText, findByRole } = render(
      <ImportSeriesSelectSeries id="a" onInputChange={vi.fn()} />,
      { wrapper }
    );
    fireEvent.click(await findByText('PossibleMatchesFound'));

    expect(((await findByRole('textbox')) as HTMLInputElement).value).toBe(
      'Imouto Twins'
    );
  });

  it('remembers the series the user picks for the folder', async () => {
    const results = [
      { title: 'One Piece', year: 1999, tvdbId: 1, seriesType: 'standard' },
      { title: 'Second Choice', year: 2023, tvdbId: 2, seriesType: 'standard' },
    ];
    seed(folder('a', 'One Piece [1080p]'));
    updateImportSeriesItem({
      id: 'a',
      hasSearched: true,
      matchStatus: 'possible',
      searchResults: results as never,
    });

    const { findByText } = render(
      <ImportSeriesSelectSeries id="a" onInputChange={vi.fn()} />,
      { wrapper }
    );
    fireEvent.click(await findByText('PossibleMatchesFound'));
    fireEvent.click(await findByText('Second Choice'));

    await waitFor(() =>
      expect(
        fetchJson.mock.calls.some((c) => c[0].path.endsWith('/lookup/choice'))
      ).toBe(true)
    );
    const call = fetchJson.mock.calls.find((c) =>
      c[0].path.endsWith('/lookup/choice')
    );
    expect(call?.[0].method).toBe('POST');
    expect(call?.[0].body).toMatchObject({
      term: 'One Piece [1080p]',
      tvdbId: 2,
    });
    expect(getImportSeriesItems(['a'])[0].selectedSeries?.title).toBe(
      'Second Choice'
    );
  });

  it('runs a typed search straight away even while the background queue is busy', async () => {
    // background lookups never finish; anything else answers at once
    fetchJson.mockImplementation(async ({ path }: { path: string }) => {
      if (path.includes('background=true')) {
        return new Promise(() => undefined);
      }

      return matched([
        { title: 'Typed Result', tvdbId: 9, seriesType: 'standard' },
      ]);
    });
    const ids = ['a', 'b', 'c', 'd', 'e', 'f'];
    seed(...ids.map((id) => folder(id)));
    updateImportSeriesItem({
      id: 'f',
      hasSearched: true,
      matchStatus: 'possible',
      searchResults: [anidbOnly as never],
    });
    ids.forEach(addToLookupQueue);
    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(fetchJson).toHaveBeenCalledTimes(3));

    const { findByText, findByRole } = render(
      <ImportSeriesSelectSeries id="f" onInputChange={vi.fn()} />,
      { wrapper }
    );
    fireEvent.click(await findByText('PossibleMatchesFound'));
    fireEvent.change(await findByRole('textbox'), {
      target: { value: 'my own search' },
    });

    await waitFor(
      () =>
        expect(getImportSeriesItems(['f'])[0].selectedSeries?.title).toBe(
          'Typed Result'
        ),
      { timeout: 3000 }
    );
    const item = getImportSeriesItems(['f'])[0];
    expect(item.searchTerm).toBe('my own search');
    expect(item.selectedSeries?.title).toBe('Typed Result');
    // it jumped the queue: still waiting folders are untouched, f left the queue
    expect(searched('d')).toBe(false);
    expect(isInLookupQueue('f')).toBe(false);
    expect(isInLookupQueue('d')).toBe(true);
  });

  it('#8 result list has unique React keys', async () => {
    const results = [
      { title: 'A', tvdbId: 123, seriesType: 'standard' },
      { title: 'B', tvdbId: 0, aniDbId: 123, seriesType: 'standard' },
    ];
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    seed(folder('a'));
    updateImportSeriesItem({
      id: 'a',
      hasSearched: true,
      selectedSeries: results[0] as never,
      searchResults: results as never,
    });

    const { findByTestId } = render(
      <ImportSeriesSelectSeries id="a" onInputChange={vi.fn()} />,
      { wrapper }
    );
    fireEvent.click(await findByTestId('title'));
    await waitFor(() => expect(document.body.textContent).toContain('B'));

    const dupKey = error.mock.calls.some((c) =>
      String(c[0]).includes('same key')
    );
    error.mockRestore();
    expect(dupKey).toBe(false);
  });

  it('#9 useExistingSeries does not rescan the library on every render', () => {
    const find = vi.fn(() => undefined);
    librarySeries = { find } as unknown as object[];

    const { rerender } = renderHook(() => useExistingSeries({ tvdbId: 1 }));
    rerender();
    rerender();

    expect(find).toHaveBeenCalledTimes(1);
  });
});

describe('manual import lookup queue worker', () => {
  it('#5 processes queued folders even when no rows are mounted', async () => {
    seed(folder('a'), folder('b'), folder('c'));
    ['a', 'b', 'c'].forEach(addToLookupQueue);

    renderHook(() => useLookupQueueWorker(), { wrapper });

    await waitFor(() =>
      expect(['a', 'b', 'c'].every((id) => searched(id))).toBe(true)
    );
    expect(getImportSeriesItems(['a'])[0].selectedSeries?.title).toBe(
      'AniDB Only'
    );
    const { result } = renderHook(() => useLookupQueueHasItems());
    expect(result.current).toBe(false);
  });

  it('#6 an empty term does not block the rest of the queue', async () => {
    seed(folder('a', ''), folder('b'));
    addToLookupQueue('a');
    addToLookupQueue('b');

    renderHook(() => useLookupQueueWorker(), { wrapper });

    await waitFor(() => expect(searched('b')).toBe(true));
    expect(searched('a')).toBe(true);
  });

  it('lets the server look inside the folder when searching by folder name', async () => {
    seed(folder('a'));
    addToLookupQueue('a');

    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(searched('a')).toBe(true));

    expect(decodeURIComponent(fetchJson.mock.calls[0][0].path)).toContain(
      'path=/lib/a'
    );
  });

  it('does not send the folder when the user typed their own search', async () => {
    seed(folder('a'));
    updateImportSeriesItem({ id: 'a', searchTerm: 'something else' });
    addToLookupQueue('a');

    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(searched('a')).toBe(true));

    expect(fetchJson.mock.calls[0][0].path).not.toContain('path=');
    expect(fetchJson.mock.calls[0][0].path).toContain('term=something');
  });

  it('stores the cleaned search term the server used for the folder', async () => {
    fetchJson.mockImplementation(async () => ({
      status: 'none',
      searchTerm: 'Imouto Twins',
      results: [],
    }));
    seed(folder('a', '( HT ) Imouto Twins'));
    addToLookupQueue('a');

    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(searched('a')).toBe(true));

    expect(getImportSeriesItems(['a'])[0].displayTerm).toBe('Imouto Twins');
  });

  it('keeps the explanation and file count from the server', async () => {
    fetchJson.mockImplementation(async () => ({
      status: 'possible',
      reason: '2 different series share this title',
      fileCount: 14,
      results: [{ title: 'One Piece', tvdbId: 1, seriesType: 'standard' }],
    }));
    seed(folder('a'));
    addToLookupQueue('a');

    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(searched('a')).toBe(true));

    const item = getImportSeriesItems(['a'])[0];
    expect(item.matchReason).toContain('share this title');
    expect(item.fileCount).toBe(14);
  });

  it('searches a few folders at once, not one at a time', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((r) => {
      release = r;
    });
    fetchJson.mockImplementation(async () => {
      await gate;
      return matched([anidbOnly]);
    });
    const ids = ['a', 'b', 'c', 'd', 'e'];
    seed(...ids.map((id) => folder(id)));
    ids.forEach(addToLookupQueue);

    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(fetchJson).toHaveBeenCalledTimes(3));
    await new Promise((r) => setTimeout(r, 50));

    // capped, not unlimited
    expect(fetchJson).toHaveBeenCalledTimes(3);

    release();
    await waitFor(() => expect(ids.every((id) => searched(id))).toBe(true));
    expect(fetchJson).toHaveBeenCalledTimes(5);
  });

  it('marks queue searches as background so the server serves the user first', async () => {
    seed(folder('a'));
    addToLookupQueue('a');

    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(searched('a')).toBe(true));

    expect(fetchJson.mock.calls[0][0].path).toContain('background=true');
  });

  it('drops the result when processing is cancelled mid-flight', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((r) => {
      release = r;
    });
    fetchJson.mockImplementation(async () => {
      await gate;
      return matched([anidbOnly]);
    });
    seed(folder('a'));
    addToLookupQueue('a');

    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(fetchJson).toHaveBeenCalled());
    act(() => stopProcessing());
    release();
    await new Promise((r) => setTimeout(r, 50));

    expect(searched('a')).toBe(false);
  });

  it('does not select anything when the server is unsure (possible)', async () => {
    const candidates = [
      { title: 'One Piece', tvdbId: 1, seriesType: 'standard' },
      { title: 'ONE PIECE (2023)', tvdbId: 2, seriesType: 'standard' },
    ];
    fetchJson.mockImplementation(async () => ({
      status: 'possible',
      results: candidates,
    }));
    seed(folder('a'));
    addToLookupQueue('a');

    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(searched('a')).toBe(true));

    const item = getImportSeriesItems(['a'])[0];
    expect(item.selectedSeries).toBeUndefined();
    expect(item.matchStatus).toBe('possible');
    expect(item.searchResults).toHaveLength(2);
  });

  it('does not select anything when nothing matched', async () => {
    fetchJson.mockImplementation(async () => ({ status: 'none', results: [] }));
    seed(folder('a'));
    addToLookupQueue('a');

    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(searched('a')).toBe(true));

    expect(getImportSeriesItems(['a'])[0].selectedSeries).toBeUndefined();
  });

  it('searches the new term when the search changes mid-flight', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((r) => {
      release = r;
    });
    fetchJson.mockImplementation(async ({ path }: { path: string }) => {
      if (/term=a(&|$)/.test(path)) {
        await gate;
        return matched([{ title: 'Old', tvdbId: 1, seriesType: 'standard' }]);
      }

      return matched([{ title: 'New', tvdbId: 2, seriesType: 'standard' }]);
    });
    seed(folder('a'));
    addToLookupQueue('a');

    renderHook(() => useLookupQueueWorker(), { wrapper });
    await waitFor(() => expect(fetchJson).toHaveBeenCalled());
    act(() => updateImportSeriesItem({ id: 'a', searchTerm: 'new' }));
    release();

    await waitFor(() => expect(searched('a')).toBe(true));
    expect(getImportSeriesItems(['a'])[0].selectedSeries?.title).toBe('New');
  });
});

describe('manual import selection', () => {
  const selectableFolders = ['a', 'b', 'c'].map((id) => ({ id }));

  const selectionWrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={new QueryClient()}>
      <SelectProvider items={selectableFolders}>{children}</SelectProvider>
    </QueryClientProvider>
  );

  it('selects every folder that has a series, including rows that were never rendered', async () => {
    librarySeries = [{ tvdbId: 0, aniDbId: 77 }];
    seed(folder('a'), folder('b'), folder('c'));
    updateImportSeriesItem({
      id: 'a',
      selectedSeries: { title: 'A', tvdbId: 1 } as never,
    });
    // already in the library: must not be selectable
    updateImportSeriesItem({
      id: 'b',
      selectedSeries: { title: 'B', tvdbId: 0, aniDbId: 77 } as never,
    });

    // no rows are rendered at all, like folders far down a virtualized list
    const { result } = renderHook(
      () => {
        useSyncImportSelection();

        return useSelect();
      },
      { wrapper: selectionWrapper }
    );

    await waitFor(() => expect(result.current.selectedCount).toBe(1));
    expect(result.current.getSelectedIds()).toEqual(['a']);

    act(() =>
      updateImportSeriesItem({
        id: 'c',
        selectedSeries: { title: 'C', tvdbId: 3 } as never,
      })
    );

    await waitFor(() => expect(result.current.selectedCount).toBe(2));
    expect(result.current.getSelectedIds().sort()).toEqual(['a', 'c']);
  });
});

describe('manual import selection after an import', () => {
  const selectableFolders = ['a', 'b'].map((id) => ({ id }));

  const selectionWrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={new QueryClient()}>
      <SelectProvider items={selectableFolders}>{children}</SelectProvider>
    </QueryClientProvider>
  );

  it('does not select again a folder the server skipped, but lets a failed one be retried', async () => {
    seed(folder('a'), folder('b'));
    updateImportSeriesItem({
      id: 'a',
      selectedSeries: { title: 'A', tvdbId: 1 } as never,
    });
    updateImportSeriesItem({
      id: 'b',
      selectedSeries: { title: 'B', tvdbId: 2 } as never,
    });

    const { result } = renderHook(
      () => {
        useSyncImportSelection();

        return useSelect();
      },
      { wrapper: selectionWrapper }
    );

    await waitFor(() => expect(result.current.selectedCount).toBe(2));

    act(() => {
      updateImportSeriesItem({ id: 'a', importState: 'skipped' });
      updateImportSeriesItem({ id: 'b', importState: 'failed' });
    });

    await waitFor(() => expect(result.current.getSelectedIds()).toEqual(['b']));
  });
});

describe('manual import page', () => {
  it('#7 does not crash when no quality profiles are loaded yet', () => {
    expect(() => render(<ImportSeries />, { wrapper })).not.toThrow();
  });
});
