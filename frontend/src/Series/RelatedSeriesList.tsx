import React, { useMemo } from 'react';
import Link from 'Components/Link/Link';
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

interface RelationConfig {
  label: string;
  accentColor: string;
}

function getRelationConfig(relationType?: string): RelationConfig {
  const type = (relationType || '').toLowerCase();

  if (type.includes('sequel')) {
    return {
      label: 'Sequel',
      accentColor: '#22c55e', // Vibrant emerald green
    };
  }

  if (type.includes('prequel')) {
    return {
      label: 'Prequel',
      accentColor: '#a855f7', // Vibrant purple
    };
  }

  if (type.includes('side')) {
    return {
      label: 'Side Story',
      accentColor: '#f59e0b', // Warm amber
    };
  }

  if (type.includes('summary')) {
    return {
      label: 'Summary',
      accentColor: '#f59e0b',
    };
  }

  if (type.includes('parent')) {
    return {
      label: 'Parent Story',
      accentColor: '#f59e0b',
    };
  }

  if (type.includes('same setting')) {
    return {
      label: 'Same Setting',
      accentColor: '#06b6d4', // Crisp, high-contrast cyan/teal
    };
  }

  if (type.includes('alternative setting')) {
    return {
      label: 'Alt. Setting',
      accentColor: '#38bdf8', // Sky blue
    };
  }

  if (type.includes('alternative version')) {
    return {
      label: 'Alt. Version',
      accentColor: '#818cf8', // Indigo
    };
  }

  if (type.includes('spin-off') || type.includes('spinoff')) {
    return {
      label: 'Spin-Off',
      accentColor: '#ec4899', // Pink
    };
  }

  return {
    label: relationType || 'Related',
    accentColor: '#06b6d4',
  };
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

              const config = getRelationConfig(related.relationType);

              const tooltipText = isInLibrary
                ? `${
                    related.relationType || 'Related'
                  } (In Library - Click to View)`
                : `${
                    related.relationType || 'Related'
                  } (Not Added - Click to Add)`;

              return (
                <Link
                  key={related.relatedAniDbId}
                  to={targetUrl}
                  style={{ textDecoration: 'none' }}
                >
                  <span
                    title={tooltipText}
                    style={{
                      display: 'inline-flex',
                      alignItems: 'center',
                      padding: '2px 8px',
                      margin: '2px',
                      borderRadius: '4px',
                      fontSize: '11px',
                      lineHeight: '1.4',
                      cursor: 'pointer',
                      whiteSpace: 'nowrap',
                      border: `1px solid ${config.accentColor}`,
                      backgroundColor: isInLibrary
                        ? config.accentColor
                        : 'var(--pageHeaderBackgroundColor, rgba(32, 32, 32, 0.85))',
                      color: isInLibrary
                        ? '#ffffff'
                        : 'var(--defaultLinkHoverColor, var(--textColor, #ffffff))',
                      boxShadow: '0 1px 2px rgba(0, 0, 0, 0.2)',
                    }}
                  >
                    <strong
                      style={{
                        marginRight: '5px',
                        color: isInLibrary ? '#ffffff' : config.accentColor,
                        fontWeight: 700,
                        letterSpacing: '0.2px',
                      }}
                    >
                      [{config.label}]
                    </strong>
                    <span
                      style={{
                        color: isInLibrary
                          ? '#ffffff'
                          : 'var(--defaultLinkHoverColor, var(--textColor, #ffffff))',
                        fontWeight: 500,
                      }}
                    >
                      {title}
                    </span>
                    {isInLibrary ? (
                      <span
                        style={{
                          marginLeft: '5px',
                          fontSize: '10px',
                          opacity: 0.9,
                          fontWeight: 700,
                        }}
                      >
                        ✓
                      </span>
                    ) : null}
                  </span>
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
