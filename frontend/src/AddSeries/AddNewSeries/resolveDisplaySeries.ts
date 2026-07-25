import AddSeries from 'AddSeries/AddSeries';
import Series from 'Series/Series';

function resolveSeasonImages(
  existingSeason: Series['seasons'][number] | undefined,
  series: AddSeries,
  existingSeries: Series
) {
  if (existingSeason?.images?.length) {
    return existingSeason.images;
  }

  if (series.images?.length) {
    return series.images;
  }

  return existingSeries.images;
}

export default function resolveDisplaySeries(
  series: AddSeries,
  existingSeries?: Series
) {
  let seasonNumber = series.seasons?.[0]?.seasonNumber;

  if (
    seasonNumber === undefined &&
    series.primaryMetadataProvider === 'anidb' &&
    series.aniDbId &&
    existingSeries?.aniDbMappings
  ) {
    const mapping = existingSeries.aniDbMappings.find(
      (m) => m.aniDbId === series.aniDbId
    );

    if (mapping) {
      seasonNumber = mapping.seasonNumber;
    }
  }

  const existingSeason =
    seasonNumber === undefined
      ? undefined
      : existingSeries?.seasons?.find((s) => s.seasonNumber === seasonNumber);

  if (!existingSeries) {
    return series;
  }

  return {
    ...existingSeries,
    title: existingSeason?.title || series.title || existingSeries.title,
    images: resolveSeasonImages(existingSeason, series, existingSeries),
  };
}
