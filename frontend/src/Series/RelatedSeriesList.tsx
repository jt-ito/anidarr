import React from 'react';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import Series from 'Series/Series';
import translate from 'Utilities/String/translate';

interface RelatedSeriesListProps {
  series: Series;
  className?: string;
}

function RelatedSeriesList({ series, className }: RelatedSeriesListProps) {
  if (!series.aniDbRelatedSeries || series.aniDbRelatedSeries.length === 0) {
    return null;
  }

  return (
    <div className={className} style={{ display: 'flex', flexWrap: 'wrap', gap: '5px', marginTop: '10px' }}>
      <strong style={{ alignSelf: 'center', marginRight: '5px' }}>{translate('Related')}:</strong>
      {series.aniDbRelatedSeries.map((related) => {
        const title = related.title || `AniDB ${related.relatedAniDbId}`;
        const searchUrl = `/add/new?term=${encodeURIComponent(title)}`;

        return (
          <Link key={related.relatedAniDbId} to={searchUrl}>
            <Label size={sizes.SMALL} kind={kinds.INFO} title={related.relationType}>
              {title}
            </Label>
          </Link>
        );
      })}
    </div>
  );
}

export default RelatedSeriesList;
