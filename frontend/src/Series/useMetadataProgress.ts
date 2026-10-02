import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface MetadataProgress {
  // AniDB series still waiting for their hub, seasons and episodes (TVDB series are never counted)
  pending: number;
  done: number;
  total: number;
  // Set while AniDB is rate limiting this client
  blockedUntil?: string | null;
  // False when the AniDB client name/version are not set (nothing can be updated until they are)
  clientConfigured?: boolean;
}

const NOTHING_PENDING: MetadataProgress = { pending: 0, done: 0, total: 0 };

// Checks often while series are being updated in the background, rarely otherwise.
export const METADATA_POLL_ACTIVE_MS = 3000;
export const METADATA_POLL_IDLE_MS = 30000;

// How far the background update of series added "now, details later" has got.
function useMetadataProgress(): MetadataProgress {
  const queryClient = useQueryClient();

  const { data } = useApiQuery<MetadataProgress>({
    path: '/series/metadata-progress',
    queryOptions: {
      refetchInterval: (query) =>
        query.state.data?.pending
          ? METADATA_POLL_ACTIVE_MS
          : METADATA_POLL_IDLE_MS,
      refetchOnWindowFocus: false,
    },
  });

  const progress = data ?? NOTHING_PENDING;
  const wasPending = useRef(false);

  // When the last one finishes, reload the library so it shows the final seasons and episodes
  useEffect(() => {
    if (wasPending.current && progress.pending === 0) {
      queryClient.invalidateQueries({ queryKey: ['/series'] });
    }

    wasPending.current = progress.pending > 0;
  }, [progress.pending, queryClient]);

  return progress;
}

export default useMetadataProgress;
