import { Season, SeriesType } from 'Series/Series';

export interface EpisodeDisplayOptions {
  seasonNumber: number;
  episodeNumber: number;
  absoluteEpisodeNumber?: number;
  seriesType?: SeriesType;
  showAbsoluteEpisodeNumbers?: boolean;
  seasons?: Season[];
}

/**
 * Computes the episode number text for display.
 *
 * When showAbsoluteEpisodeNumbers is enabled on a series with SeriesType == Anime (for regular season > 0):
 * - Uses the stored authoritative absoluteEpisodeNumber if available (e.g. AniDB-sourced series).
 * - Fallback: Calculates a display-only absolute number by cumulatively summing prior regular seasons'
 *   total episode counts (seasons 1..N-1) + the current season-relative episode number.
 *   (Known limitation: For airing or incomplete seasons, this client-side fallback is best-effort
 *   and will dynamically adapt as prior season episode counts are populated in metadata).
 * - Formats as: `{AbsoluteEpisodeNumber} ({SeasonEpisodeNumber})` (e.g. `21 (1)`).
 *
 * When the setting is disabled, or for Specials (seasonNumber === 0), or for non-anime series,
 * returns null so the caller can render standard formatting.
 */
export function getEpisodeDisplayNumber(
  options: EpisodeDisplayOptions
): string | null {
  const {
    seasonNumber,
    episodeNumber,
    absoluteEpisodeNumber,
    seriesType,
    showAbsoluteEpisodeNumbers = false,
    seasons = [],
  } = options;

  if (
    seriesType !== 'anime' ||
    seasonNumber === 0 ||
    !showAbsoluteEpisodeNumbers
  ) {
    return null;
  }

  let absoluteNum = absoluteEpisodeNumber;

  // Client-computed fallback for anime series/episodes lacking stored absolute numbers (e.g. TVDB-primary marked as Anime)
  if (absoluteNum == null || absoluteNum <= 0) {
    let priorSeasonEpisodeCount = 0;

    for (const season of seasons) {
      if (season.seasonNumber > 0 && season.seasonNumber < seasonNumber) {
        priorSeasonEpisodeCount += season.statistics?.totalEpisodeCount ?? 0;
      }
    }

    absoluteNum = priorSeasonEpisodeCount + episodeNumber;
  }

  // If first season or absolute episode number matches season episode number, exclude the (seasonEpisode) suffix
  if (seasonNumber === 1 || absoluteNum === episodeNumber) {
    return null;
  }

  return `${absoluteNum} (${episodeNumber})`;
}
