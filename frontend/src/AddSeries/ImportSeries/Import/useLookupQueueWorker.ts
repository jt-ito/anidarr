import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useLookupQueue } from './importSeriesStore';
import lookupFolder from './lookupFolder';

// How many folders are searched at once. Each search is mostly waiting on the network,
// so a few in parallel is much faster than one at a time. The server still rate limits
// the providers (AniDB, TVDB) centrally, whatever this is set to.
const CONCURRENCY = 3;

// Works through the lookup queue. This lives at page level (not in the rows) because the
// table is virtualized: a row that scrolls out of view unmounts, and a queue driven by
// rows would stall on it.
function useLookupQueueWorker() {
  const queryClient = useQueryClient();
  const queue = useLookupQueue();
  const [pass, setPass] = useState(0);
  const inFlight = useRef(new Set<string>());
  const isMounted = useRef(true);

  useEffect(() => {
    isMounted.current = true;

    return () => {
      isMounted.current = false;
    };
  }, []);

  useEffect(() => {
    // Start the next waiting folders, in queue order, up to the limit
    const waiting = queue.filter((id) => !inFlight.current.has(id));

    waiting.slice(0, CONCURRENCY - inFlight.current.size).forEach((id) => {
      inFlight.current.add(id);

      lookupFolder(queryClient, id, true).finally(() => {
        inFlight.current.delete(id);

        if (isMounted.current) {
          setPass((p) => p + 1);
        }
      });
    });
  }, [queue, pass, queryClient]);
}

export default useLookupQueueWorker;
