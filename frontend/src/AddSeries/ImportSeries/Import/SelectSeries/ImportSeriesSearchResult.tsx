import React, { useCallback, useState } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import { Image, Season } from 'Series/Series';
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
  images?: Image[];
  seasons?: Season[];
  overview?: string;
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
  images,
  seasons,
  overview,
  index,
  onPress,
}: ImportSeriesSearchResultProps) {
  const isExistingSeries = !!useExistingSeries({ tvdbId, aniDbId });

  const handlePress = useCallback(() => {
    onPress(index);
  }, [index, onPress]);

  const isAniDb =
    primaryMetadataProvider === 'anidb' || (tvdbId === 0 && !!aniDbId);
  const linkUrl = isAniDb
    ? `https://anidb.net/anime/${aniDbId}`
    : `https://www.thetvdb.com/?tab=series&id=${tvdbId}`;

  const linkTitle = isAniDb ? 'AniDB' : 'TheTVDB';

  // A thumbnail and a few facts make same-named series easy to tell apart
  const poster = images?.find((image) => image.coverType === 'poster');

  // For a series that is not in the library yet the server hands out a proxied `url` (so the image is
  // fetched server side). `remoteUrl` is the original address, which some hosts (AniDB's) refuse to
  // serve to a browser, so only fall back to it when there is no proxied one.
  const posterUrl = poster?.url || poster?.remoteUrl;
  const [hasPosterFailed, setHasPosterFailed] = useState(false);

  const handlePosterError = useCallback(() => {
    setHasPosterFailed(true);
  }, []);

  const seasonCount = seasons?.filter((s) => s.seasonNumber > 0).length ?? 0;

  return (
    <div className={styles.container}>
      <Link className={styles.series} title={overview} onPress={handlePress}>
        {posterUrl && !hasPosterFailed ? (
          <img
            className={styles.poster}
            src={posterUrl}
            alt=""
            loading="lazy"
            onError={handlePosterError}
          />
        ) : null}

        <div className={styles.details}>
          <ImportSeriesTitle
            title={title}
            year={year}
            network={network}
            isExistingSeries={isExistingSeries}
          />

          <div className={styles.meta}>
            <span className={isAniDb ? styles.aniDbBadge : styles.tvdbBadge}>
              {linkTitle}
            </span>

            {seasonCount > 0 ? (
              <span>
                {seasonCount === 1 ? '1 season' : `${seasonCount} seasons`}
              </span>
            ) : null}
          </div>
        </div>
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
