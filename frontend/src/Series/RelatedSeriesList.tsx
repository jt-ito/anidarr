import React from 'react';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import Series from 'Series/Series';
import useSeries from 'Series/useSeries';
import translate from 'Utilities/String/translate';

interface RelatedSeriesListProps {
  series: Series;
  className?: string;
}

function RelatedSeriesList({ series, className }: RelatedSeriesListProps) {
  const { data: allSeries = [] } = useSeries();

  if (!series.aniDbRelatedSeries || series.aniDbRelatedSeries.length === 0) {
    return null;
  }

  return (
    <div
      className={className}
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        gap: '5px',
        marginTop: '10px',
      }}
    >
      <strong style={{ alignSelf: 'center', marginRight: '5px' }}>
        {translate('Related')}:
      </strong>
      {series.aniDbRelatedSeries.map((related) => {
        const title = related.title || `AniDB ${related.relatedAniDbId}`;

        let targetUrl = `/add/new?term=${encodeURIComponent(title)}`;
        let isInLibrary = false;

        if (related.existingTitleSlug) {
          targetUrl = `/series/${related.existingTitleSlug}`;
          isInLibrary = true;
        } else {
          const existing = allSeries.find(
            (s) =>
              s.aniDbId === related.relatedAniDbId ||
              s.mappedAniDbIds?.includes(related.relatedAniDbId) ||
              (related.title && s.title?.toLowerCase() === related.title.toLowerCase())
          );

          if (existing?.titleSlug) {
            targetUrl = `/series/${existing.titleSlug}`;
            isInLibrary = true;
          }
        }

        return (
          <Link key={related.relatedAniDbId} to={targetUrl}>
            <Label
              size={sizes.SMALL}
              kind={isInLibrary ? kinds.SUCCESS : kinds.INFO}
              title={
                isInLibrary
                  ? `${related.relationType || 'Related'} (In Library)`
                  : related.relationType
              }
            >
              {title}
            </Label>
          </Link>
        );
      })}
    </div>
  );
}

export default RelatedSeriesList;
