import React, { useMemo } from 'react';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import Series, { AniDbRelatedSeries } from 'Series/Series';
import useSeries from 'Series/useSeries';

interface RelatedSeriesListProps {
  series: Series;
  className?: string;
}

type RelationCategory = 'main' | 'side' | 'alt';

function getRelationCategory(relationType?: string): RelationCategory {
  const type = (relationType || '').toLowerCase();

  if (type.includes('sequel') || type.includes('prequel')) {
    return 'main';
  }

  if (
    type.includes('side') ||
    type.includes('summary') ||
    type.includes('parent')
  ) {
    return 'side';
  }

  return 'alt';
}

type RelationBadgeKind = 'success' | 'purple' | 'warning' | 'info';

function getRelationBadgeKind(relationType?: string): RelationBadgeKind {
  const type = (relationType || '').toLowerCase();

  if (type.includes('sequel')) {
    return kinds.SUCCESS;
  }

  if (type.includes('prequel')) {
    return kinds.PURPLE;
  }

  if (
    type.includes('side') ||
    type.includes('summary') ||
    type.includes('parent')
  ) {
    return kinds.WARNING;
  }

  return kinds.INFO;
}

function getRelationLabel(relationType?: string): string {
  if (!relationType) {
    return 'Related';
  }

  const type = relationType.toLowerCase();

  if (type.includes('sequel')) {
    return 'Sequel';
  }

  if (type.includes('prequel')) {
    return 'Prequel';
  }

  if (type.includes('side')) {
    return 'Side Story';
  }

  if (type.includes('summary')) {
    return 'Summary';
  }

  if (type.includes('parent')) {
    return 'Parent Story';
  }

  if (type.includes('alternative setting')) {
    return 'Alt. Setting';
  }

  if (type.includes('alternative version')) {
    return 'Alt. Version';
  }

  if (type.includes('spin-off') || type.includes('spinoff')) {
    return 'Spin-Off';
  }

  return relationType;
}

function RelatedSeriesList({ series, className }: RelatedSeriesListProps) {
  const { data: allSeries = [] } = useSeries();

  const groups = useMemo(() => {
    if (!series.aniDbRelatedSeries || series.aniDbRelatedSeries.length === 0) {
      return [];
    }

    const mainStory: AniDbRelatedSeries[] = [];
    const sideStories: AniDbRelatedSeries[] = [];
    const alternatives: AniDbRelatedSeries[] = [];

    for (const rel of series.aniDbRelatedSeries) {
      const cat = getRelationCategory(rel.relationType);

      if (cat === 'main') {
        mainStory.push(rel);
      } else if (cat === 'side') {
        sideStories.push(rel);
      } else {
        alternatives.push(rel);
      }
    }

    const result = [];

    if (mainStory.length > 0) {
      result.push({
        key: 'main',
        label: 'Main Story',
        items: mainStory,
      });
    }

    if (sideStories.length > 0) {
      result.push({
        key: 'side',
        label: 'Side Stories & OVAs',
        items: sideStories,
      });
    }

    if (alternatives.length > 0) {
      result.push({
        key: 'alt',
        label: 'Alternatives & Spin-Offs',
        items: alternatives,
      });
    }

    return result;
  }, [series.aniDbRelatedSeries]);

  if (groups.length === 0) {
    return null;
  }

  return (
    <div
      className={className}
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: '6px',
        marginTop: '12px',
      }}
    >
      {groups.map((group) => (
        <div
          key={group.key}
          style={{
            display: 'flex',
            alignItems: 'center',
            flexWrap: 'wrap',
            gap: '6px',
          }}
        >
          <span
            style={{
              fontSize: '12px',
              fontWeight: 600,
              color: 'var(--disabledColor)',
              minWidth: '130px',
            }}
          >
            {group.label}:
          </span>

          <div
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              gap: '6px',
            }}
          >
            {group.items.map((related) => {
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
                    (related.title &&
                      s.title?.toLowerCase() === related.title.toLowerCase())
                );

                if (existing?.titleSlug) {
                  targetUrl = `/series/${existing.titleSlug}`;
                  isInLibrary = true;
                }
              }

              const returnParams = new URLSearchParams();

              if (series.titleSlug) {
                returnParams.set('returnToSeries', series.titleSlug);
              }

              if (series.title) {
                returnParams.set('returnToSeriesTitle', series.title);
              }

              const returnQuery = returnParams.toString();

              if (isInLibrary) {
                targetUrl = `${targetUrl}${
                  returnQuery ? `?${returnQuery}` : ''
                }`;
              } else {
                targetUrl = `/add/new?term=${encodeURIComponent(title)}${
                  returnQuery ? `&${returnQuery}` : ''
                }`;
              }

              const badgeKind = getRelationBadgeKind(related.relationType);
              const relationLabel = getRelationLabel(related.relationType);

              const tooltipText = isInLibrary
                ? `${
                    related.relationType || 'Related'
                  } (In Library - Click to View)`
                : `${
                    related.relationType || 'Related'
                  } (Not Added - Click to Add)`;

              return (
                <Link key={related.relatedAniDbId} to={targetUrl}>
                  <Label
                    size={sizes.SMALL}
                    kind={badgeKind}
                    outline={!isInLibrary}
                    title={tooltipText}
                  >
                    <strong style={{ marginRight: '4px' }}>
                      [{relationLabel}]
                    </strong>
                    {title}
                    {isInLibrary ? ' ✓' : ''}
                  </Label>
                </Link>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}

export default RelatedSeriesList;
