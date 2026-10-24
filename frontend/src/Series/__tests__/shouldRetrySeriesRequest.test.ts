import { describe, expect, it } from 'vitest';
import { shouldRetrySeriesRequest } from 'Series/useSeries';

describe('shouldRetrySeriesRequest', () => {
  it('does not retry a request that timed out, since the server is still working on it', () => {
    const timeout = new DOMException(
      'The operation was aborted.',
      'AbortError'
    );

    expect(shouldRetrySeriesRequest(0, timeout)).toBe(false);
  });

  it('retries other failures a few times', () => {
    const networkError = new TypeError('Failed to fetch');

    expect(shouldRetrySeriesRequest(0, networkError)).toBe(true);
    expect(shouldRetrySeriesRequest(2, networkError)).toBe(true);
    expect(shouldRetrySeriesRequest(3, networkError)).toBe(false);
  });
});
