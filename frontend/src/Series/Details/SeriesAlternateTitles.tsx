import React from 'react';
import { AlternateTitle } from 'Series/Series';
import isSpacelessSlug from 'Utilities/Series/isSpacelessSlug';
import styles from './SeriesAlternateTitles.css';

interface SeriesAlternateTitlesProps {
  alternateTitles: AlternateTitle[];
}

function SeriesAlternateTitles({
  alternateTitles,
}: SeriesAlternateTitlesProps) {
  const filteredTitles = alternateTitles.filter(
    (alternateTitle) => !isSpacelessSlug(alternateTitle.title)
  );

  return (
    <ul>
      {filteredTitles.map((alternateTitle) => {
        return (
          <li key={alternateTitle.title} className={styles.alternateTitle}>
            {alternateTitle.title}
            {alternateTitle.comment ? (
              <span className={styles.comment}> {alternateTitle.comment}</span>
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}

export default SeriesAlternateTitles;
