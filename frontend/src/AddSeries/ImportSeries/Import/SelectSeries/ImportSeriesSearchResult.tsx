import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import useExistingSeries from 'Series/useExistingSeries';
import ImportSeriesTitle from './ImportSeriesTitle';
import styles from './ImportSeriesSearchResult.css';

interface ImportSeriesSearchResultProps {
  tvdbId: number;
  aniDbId?: number;
  primaryMetadataProvider?: string;
  title: string;
  year: number;
  network?: string;
  index: number;
  onPress: (index: number) => void;
}

function ImportSeriesSearchResult({
  tvdbId,
  aniDbId,
  primaryMetadataProvider,
  title,
  year,
  network,
  index,
  onPress,
}: ImportSeriesSearchResultProps) {
  const isExistingSeries = !!useExistingSeries({ tvdbId });

  const handlePress = useCallback(() => {
    onPress(index);
  }, [index, onPress]);

  const isAniDb =
    primaryMetadataProvider === 'anidb' || (tvdbId === 0 && !!aniDbId);
  const linkUrl = isAniDb
    ? `https://anidb.net/anime/${aniDbId}`
    : `https://www.thetvdb.com/?tab=series&id=${tvdbId}`;

  const linkTitle = isAniDb ? 'AniDB' : 'TheTVDB';

  return (
    <div className={styles.container}>
      <Link className={styles.series} onPress={handlePress}>
        <ImportSeriesTitle
          title={title}
          year={year}
          network={network}
          isExistingSeries={isExistingSeries}
        />
        {isAniDb && (
          <span className={styles.aniDbBadge} title="Sourced from AniDB">
            AniDB
          </span>
        )}
      </Link>

      <Link className={styles.tvdbLink} to={linkUrl} title={linkTitle}>
        <Icon
          className={styles.tvdbLinkIcon}
          name={icons.EXTERNAL_LINK}
          size={16}
        />
      </Link>
    </div>
  );
}

export default ImportSeriesSearchResult;
