import { useMemo } from 'react';
import Series from 'Series/Series';
import useSeries from 'Series/useSeries';

function useExistingSeries(seriesToCheck?: Partial<Series>) {
  const { data: series = [] } = useSeries();

  return useMemo(() => {
    if (!seriesToCheck) {
      return false;
    }

    return series.some((s) => {
      if (
        seriesToCheck.tvdbId &&
        seriesToCheck.tvdbId > 0 &&
        s.tvdbId === seriesToCheck.tvdbId
      )
        return true;
      if (
        seriesToCheck.aniDbId &&
        seriesToCheck.aniDbId > 0 &&
        s.aniDbId === seriesToCheck.aniDbId
      )
        return true;
      if (
        seriesToCheck.tmdbId &&
        seriesToCheck.tmdbId > 0 &&
        s.tmdbId === seriesToCheck.tmdbId
      )
        return true;
      if (
        seriesToCheck.simklId &&
        seriesToCheck.simklId > 0 &&
        s.simklId === seriesToCheck.simklId
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
  }, [seriesToCheck, series]);
}

export default useExistingSeries;
