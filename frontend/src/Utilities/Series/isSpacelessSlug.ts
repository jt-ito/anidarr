export function isSpacelessSlug(title?: string | null): boolean {
  if (!title) {
    return false;
  }

  const trimmed = title.trim();

  if (!trimmed.includes(' ')) {
    const lower = trimmed.toLowerCase();

    if (
      lower.includes('theanimation') ||
      lower.includes('theseries') ||
      lower.includes('themovie')
    ) {
      return true;
    }

    if (
      trimmed.length > 20 &&
      /^[a-zA-Z0-9]+$/.test(trimmed) &&
      /[a-zA-Z]/.test(trimmed)
    ) {
      return true;
    }
  }

  return false;
}

export default isSpacelessSlug;
