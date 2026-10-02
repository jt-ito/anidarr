using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public enum LookupMatchStatus
    {
        None,
        Possible,
        Matched
    }

    public class LookupMatch
    {
        // Best first, one entry per distinct series (same show from several providers is merged)
        public List<Series> Results { get; set; } = new List<Series>();

        public LookupMatchStatus Status { get; set; }

        // Score (0-1) of each entry in Results, same order
        public List<double> Scores { get; set; } = new List<double>();

        // How many results matched the folder name exactly
        public int StrongCount { get; set; }

        // Why the status was chosen, for logs and the UI
        public string Reason { get; set; } = string.Empty;

        // Video files found in the folder (0 when the folder wasn't probed)
        public int FileCount { get; set; }

        // The folder name without release tags: what was actually searched for
        public string SearchTerm { get; set; } = string.Empty;
    }

    /// <summary>
    /// Decides whether a lookup result can be trusted as the series a folder holds.
    /// Prefers "not sure" over a wrong guess: a result is only auto-selected when
    /// exactly one distinct series matches the folder title exactly.
    /// </summary>
    public static class SeriesMatcher
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
        private const double StrongScore = 0.97;
        private const double PossibleScore = 0.6;
        private const double AliasWeight = 0.98;
        private const double PartialTitleWeight = 0.9;

        // Community synonyms are looser than official titles. An exact synonym match still counts (many
        // obscure titles only exist as synonyms) but loses to an official exact match, and two series
        // sharing a synonym is a tie like any other.
        private const double SynonymFactor = 0.975;
        private const double PartialSimilarityCap = 0.85;
        private const int MaxTitlesPerSeries = 40;
        private const int MinSiblingBaseLength = 4;

        private static readonly Regex TrailingYear = new Regex(@"\s*\((\d{4})\)\s*$", Options);
        private static readonly Regex FolderYearPattern = new Regex(@"[\(\[][^\)\]]*?\b((?:19|20)\d{2})\b", Options);
        private static readonly Regex Ordinals = new Regex(@"(\d+)(?:st|nd|rd|th)\s+season", Options);
        private static readonly Regex TrailingSeasonMarker = new Regex(@"\s+(?:season\s+\d{1,2}|\d{1,2}|ii|iii|iv|vi{0,3}|ix)$", Options);
        private static readonly Regex Punctuation = new Regex(@"[^\p{L}\p{N}]+", Options);
        private static readonly Regex Apostrophes = new Regex("[’'`]", Options);

        // fileTitles: show titles read from the episode file names (extra evidence, same rules).
        // aliases: further titles for a result (e.g. English/romaji names from the offline anime
        // database); official ones count in full, community synonyms never reach "exact".
        public static LookupMatch Evaluate(
            string folderName,
            IEnumerable<Series> candidates,
            IEnumerable<string> fileTitles = null,
            Func<Series, IEnumerable<(string Title, bool IsOfficial)>> aliases = null)
        {
            var terms = GetTerms(folderName, fileTitles);
            var folderYear = GetFolderYear(folderName);
            var seen = new HashSet<string>();
            var scored = new List<(Series Series, double Score, double OfficialScore)>();

            foreach (var series in candidates)
            {
                var keys = GetIdentityKeys(series);

                if (keys.Any(seen.Contains))
                {
                    continue;
                }

                seen.UnionWith(keys);

                var (score, officialScore) = Score(terms, folderYear, series, aliases);

                scored.Add((series, score, officialScore));
            }

            var ranked = scored.OrderByDescending(s => s.Score).ToList();
            var strong = ranked.Where(s => s.Score >= StrongScore).ToList();

            // An official exact title beats a synonym that happens to be spelled the same
            var officialStrong = strong.Where(s => s.OfficialScore >= StrongScore).ToList();

            if (officialStrong.Count > 0 && officialStrong.Count < strong.Count)
            {
                strong = officialStrong;
            }

            var match = new LookupMatch { StrongCount = strong.Count };

            if (strong.Count == 1)
            {
                match.Status = LookupMatchStatus.Matched;
                match.Reason = strong[0].OfficialScore >= StrongScore
                    ? "exactly one series has this title"
                    : "exactly one series has this title (as an alternate title)";
            }
            else if (strong.Count > 1)
            {
                // Same title, different shows (e.g. an anime and its live action): only the
                // year in the folder name can break the tie.
                var byYear = folderYear > 0
                    ? strong.Where(s => GetYear(s.Series) == folderYear).ToList()
                    : new List<(Series Series, double Score, double OfficialScore)>();

                if (byYear.Count == 1)
                {
                    match.Status = LookupMatchStatus.Matched;
                    match.Reason = $"{strong.Count} series share this title; the folder year {folderYear} picks one";
                    ranked.Remove(byYear[0]);
                    ranked.Insert(0, byYear[0]);
                }
                else
                {
                    match.Status = LookupMatchStatus.Possible;
                    match.Reason = $"{strong.Count} different series share this title" + (folderYear > 0 ? $" and none is from {folderYear}" : " (no year in the folder name)");
                }
            }
            else if (ranked.Count > 0 && ranked[0].Score >= PossibleScore)
            {
                match.Status = LookupMatchStatus.Possible;
                match.Reason = "no exact title, closest results shown";
            }
            else
            {
                match.Status = LookupMatchStatus.None;
                match.Reason = ranked.Count > 0 ? "nothing close to this title" : "no results";
            }

            // Ordering only, never selection: other seasons of the best result (same title, e.g.
            // "Sex Exchange" / "Sex Exchange (2)") go right after it, not below unrelated hits.
            if (ranked.Count > 1 && ranked[0].Score >= PossibleScore)
            {
                var topBase = BaseTitle(ranked[0].Series);
                var siblings = topBase.Length < MinSiblingBaseLength
                    ? new List<(Series Series, double Score, double OfficialScore)>()
                    : ranked.Skip(1).Where(r => BaseTitle(r.Series) == topBase).ToList();

                if (siblings.Count > 0)
                {
                    ranked = ranked.Take(1).Concat(siblings).Concat(ranked.Skip(1).Except(siblings)).ToList();
                }
            }

            match.Results = ranked.Select(s => s.Series).ToList();
            match.Scores = ranked.Select(s => Math.Round(s.Score, 2)).ToList();

            return match;
        }

        // The title without a trailing season marker: "Sex Exchange (2)", "Sex Exchange 2",
        // "Sex Exchange Season 2" and "Sex Exchange II" all become "sex exchange"
        private static string BaseTitle(Series series)
        {
            var canon = Canon(TrailingYear.Replace(series.Title ?? string.Empty, string.Empty));

            return TrailingSeasonMarker.Replace(canon, string.Empty).Trim();
        }

        private static List<(string Canon, double Weight)> GetTerms(string folderName, IEnumerable<string> fileTitles)
        {
            var texts = SearchTermCleaner.GetCandidates(folderName);

            if (texts.Count == 0)
            {
                texts.Add(folderName.Trim());
            }

            var primary = Canon(texts[0]);

            texts.AddRange(fileTitles ?? Enumerable.Empty<string>());

            return texts
                .Select((text, index) =>
                {
                    var canon = Canon(text);

                    // "Cross Ange" taken from "Cross Ange - Tenshi to Ryuu no Rondo" is only part of the title
                    var isPartial = index > 0 && primary.StartsWith(canon + " ", StringComparison.Ordinal);

                    return (canon, index == 0 ? 1.0 : isPartial ? PartialTitleWeight : AliasWeight);
                })
                .Where(t => t.canon.Length > 0)
                .ToList();
        }

        // Returns the best score over all titles, and the best over official titles only
        private static (double Score, double OfficialScore) Score(
            List<(string Canon, double Weight)> terms,
            int folderYear,
            Series series,
            Func<Series, IEnumerable<(string Title, bool IsOfficial)>> aliases)
        {
            var titles = new[] { series.Title }
                .Concat(series.AlternateTitles ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Take(MaxTitlesPerSeries)
                .Select(t => (Canon: Canon(TrailingYear.Replace(t, string.Empty)), Factor: 1.0, IsOfficial: true))
                .ToList();

            if (aliases != null)
            {
                titles.AddRange(aliases(series)
                    .Where(a => !string.IsNullOrWhiteSpace(a.Title))
                    .Take(MaxTitlesPerSeries)
                    .Select(a => (Canon: Canon(TrailingYear.Replace(a.Title, string.Empty)), Factor: a.IsOfficial ? 1.0 : SynonymFactor, a.IsOfficial)));
            }

            titles = titles.Where(t => t.Canon.Length > 0).ToList();

            var score = 0.0;
            var officialScore = 0.0;

            foreach (var term in terms)
            {
                foreach (var title in titles)
                {
                    var value = Similarity(term.Canon, title.Canon) * term.Weight * title.Factor;

                    score = Math.Max(score, value);

                    if (title.IsOfficial)
                    {
                        officialScore = Math.Max(officialScore, value);
                    }
                }
            }

            var year = GetYear(series);

            if (folderYear > 0)
            {
                if (year == 0)
                {
                    // The folder names a year we can't verify
                    score = Math.Min(score, PossibleScore + 0.2);
                    officialScore = Math.Min(officialScore, PossibleScore + 0.2);
                }
                else if (Math.Abs(year - folderYear) > 1)
                {
                    score *= 0.5;
                    officialScore *= 0.5;
                }
            }

            return (score, officialScore);
        }

        private static double Similarity(string a, string b)
        {
            // Spacing/punctuation differences ("Re-birth" vs "Rebirth", "FateZero") are the same title
            if (a.Replace(" ", string.Empty) == b.Replace(" ", string.Empty))
            {
                return 1.0;
            }

            var tokensA = a.Split(' ').ToHashSet();
            var tokensB = b.Split(' ').ToHashSet();
            var dice = 2.0 * tokensA.Intersect(tokensB).Count() / (tokensA.Count + tokensB.Count);

            return Math.Min(PartialSimilarityCap, dice * 0.9);
        }

        private static string Canon(string text)
        {
            var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();

            foreach (var c in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
            {
                builder.Append(c);
            }

            var s = builder.ToString().Replace("&", " and ");

            s = Apostrophes.Replace(s, string.Empty);
            s = Ordinals.Replace(s, "season $1");

            return Punctuation.Replace(s, " ").Trim();
        }

        private static int GetFolderYear(string folderName)
        {
            var match = FolderYearPattern.Match(folderName);

            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private static int GetYear(Series series)
        {
            if (series.Year > 0)
            {
                return series.Year;
            }

            var match = TrailingYear.Match(series.Title ?? string.Empty);

            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        // Anything two results share means they are the same show seen through different providers
        private static List<string> GetIdentityKeys(Series series)
        {
            var keys = new List<string>();

            keys.AddRange((series.AniListIds ?? new HashSet<int>()).Where(id => id > 0).Select(id => $"anilist:{id}"));
            keys.AddRange((series.MalIds ?? new HashSet<int>()).Where(id => id > 0).Select(id => $"mal:{id}"));

            if (series.TvdbId > 0)
            {
                keys.Add($"tvdb:{series.TvdbId}");
            }

            if (series.AniDbId > 0)
            {
                keys.Add($"anidb:{series.AniDbId}");
            }

            var year = GetYear(series);
            var title = Canon(TrailingYear.Replace(series.Title ?? string.Empty, string.Empty)).Replace(" ", string.Empty);

            if (year > 0 && title.Length > 0)
            {
                keys.Add($"title:{title}|{year}");
            }

            return keys;
        }
    }
}
