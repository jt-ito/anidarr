import { useMemo } from 'react';
import Series from 'Series/Series';
import useSeries from 'Series/useSeries';

// The library series that is the same show as `seriesToCheck`, if any
export function findExistingSeries(
  library: readonly Series[],
  seriesToCheck?: Partial<Series>
) {
  if (!seriesToCheck) {
    return undefined;
  }

  return library.find((s) => {
    if (
      seriesToCheck.tvdbId &&
      seriesToCheck.tvdbId > 0 &&
      s.tvdbId === seriesToCheck.tvdbId
    )
      return true;
    if (
      seriesToCheck.aniDbId &&
      seriesToCheck.aniDbId > 0 &&
      (s.aniDbId === seriesToCheck.aniDbId ||
        s.mappedAniDbIds?.includes(seriesToCheck.aniDbId))
    )
      return true;
    if (
      seriesToCheck.tmdbId &&
      seriesToCheck.tmdbId > 0 &&
      s.tmdbId === seriesToCheck.tmdbId
    )
      return true;

    if (
      seriesToCheck.malIds &&
      seriesToCheck.malIds.length > 0 &&
      s.malIds &&
      s.malIds.length > 0
    ) {
      if (seriesToCheck.malIds.some((id) => id > 0 && s.malIds!.includes(id)))
        return true;
    }

    if (
      seriesToCheck.aniListIds &&
      seriesToCheck.aniListIds.length > 0 &&
      s.aniListIds &&
      s.aniListIds.length > 0
    ) {
      if (
        seriesToCheck.aniListIds.some(
          (id) => id > 0 && s.aniListIds!.includes(id)
        )
      )
        return true;
    }

    return false;
  });
}

function useExistingSeries(seriesToCheck?: Partial<Series>) {
  const { data: series = [] } = useSeries();

  // Callers often pass a fresh object literal; depend on the ids so the library
  // is only rescanned when they actually change.
  const { tvdbId, aniDbId, tmdbId, malIds, aniListIds } = seriesToCheck ?? {};
  const hasSeriesToCheck = !!seriesToCheck;

  return useMemo(() => {
    if (!hasSeriesToCheck) {
      return undefined;
    }

    return findExistingSeries(series, {
      tvdbId,
      aniDbId,
      tmdbId,
      malIds,
      aniListIds,
    });
  }, [hasSeriesToCheck, tvdbId, aniDbId, tmdbId, malIds, aniListIds, series]);
}

export default useExistingSeries;
