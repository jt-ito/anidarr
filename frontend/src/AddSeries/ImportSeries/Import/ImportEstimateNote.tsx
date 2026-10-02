import React from 'react';
import translate from 'Utilities/String/translate';
import { ImportEstimate } from './useImportEstimate';
import styles from './ImportEstimateNote.css';

const toMinutes = (seconds: number) => Math.max(1, Math.round(seconds / 60));

function formatMinutes(minutes: number) {
  if (minutes < 60) {
    return `${minutes} min`;
  }

  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;

  return rest ? `${hours} h ${rest} min` : `${hours} h`;
}

// "5–15 min", "1 h 10 min – 2 h", or "under a minute"
export function formatEtaRange(lowSeconds: number, highSeconds: number) {
  if (highSeconds < 60) {
    return translate('ImportEstimateUnderAMinute');
  }

  const low = toMinutes(lowSeconds);
  const high = toMinutes(highSeconds);

  if (high < 60) {
    return low === high ? `${high} min` : `${low}–${high} min`;
  }

  return `${formatMinutes(low)} – ${formatMinutes(high)}`;
}

interface ImportEstimateNoteProps {
  estimate: ImportEstimate;
}

// Shown once every folder is processed and before importing: how long importing the AniDB series will
// take, and whether "add now, fetch details later" is worth ticking.
function ImportEstimateNote({ estimate }: ImportEstimateNoteProps) {
  if (estimate.aniDbSeries === 0) {
    return null;
  }

  if (estimate.uncached === 0) {
    return (
      <div className={styles.estimate}>
        {translate('ImportEstimateAllCached', { total: estimate.aniDbSeries })}
      </div>
    );
  }

  return (
    <div className={styles.estimate}>
      <div>
        {translate('ImportEstimateAniDb', {
          time: formatEtaRange(estimate.secondsLow, estimate.secondsHigh),
          uncached: estimate.uncached,
          total: estimate.aniDbSeries,
        })}
      </div>

      <div className={styles.disclaimer}>
        {translate('ImportEstimateAddNowNote')}
      </div>
    </div>
  );
}

export default ImportEstimateNote;
