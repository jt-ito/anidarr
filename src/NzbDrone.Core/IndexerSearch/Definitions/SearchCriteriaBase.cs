using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public abstract class SearchCriteriaBase
    {
        private static readonly Regex SpecialCharacter = new Regex(@"['.\u0060\u00B4\u2018\u2019]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NonWord = new Regex(@"[\W]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BeginningThe = new Regex(@"^the\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Series Series { get; set; }
        public List<string> SceneTitles { get; set; }
        public List<Episode> Episodes { get; set; }
        public SearchMode SearchMode { get; set; }
        public virtual bool MonitoredEpisodesOnly { get; set; }
        public virtual bool UserInvokedSearch { get; set; }
        public virtual bool InteractiveSearch { get; set; }

        private static readonly Regex TrailingPunctuation = new Regex(@"[.!?:;。！？：；]+$", RegexOptions.Compiled);
        private static readonly Regex SmartQuotes = new Regex(@"[\u0060\u00B4\u2018\u2019]", RegexOptions.Compiled);
        private static readonly Regex DelimiterRegex = new Regex(@"(?:\s*[:：]\s*|\s+[-—–]\s+|\s*[～~]\s*)", RegexOptions.Compiled);
        private static readonly Regex GenericDescriptorRegex = new Regex(
            @"(?:\s+|\s*[\(\[])(?:the\s+animation|the\s+series|animation|anime|ova|oav|movie|project|extra)[\)\]]?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> Stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "animation", "anime", "the animation", "ova", "the series", "movie", "project", "extra"
        };

        public List<string> AllSceneTitles => SceneTitles.Concat(CleanSceneTitles).Distinct().ToList();
        public List<string> CleanSceneTitles => SceneTitles.Select(GetCleanSceneTitle).Distinct().ToList();

        public static bool IsStopword(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            return Stopwords.Contains(candidate.Trim());
        }

        public static bool IsNativeJapaneseTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            return title.Any(c => (c >= 0x3040 && c <= 0x30ff) || (c >= 0x4e00 && c <= 0x9faf));
        }

        public static bool IsSafeTitle(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            var trimmed = candidate.Trim();

            if (IsNativeJapaneseTitle(trimmed))
            {
                return trimmed.Length >= 2;
            }

            var alphanumericCount = trimmed.Count(char.IsLetterOrDigit);
            if (alphanumericCount < 4)
            {
                return false;
            }

            if (Stopwords.Contains(trimmed))
            {
                return false;
            }

            var words = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1 && words[0].Length <= 3)
            {
                return false;
            }

            return true;
        }

        public static bool TryGetBaseTitle(string title, out string baseTitle)
        {
            baseTitle = null;
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var match = DelimiterRegex.Match(title);
            if (match.Success && match.Index > 0)
            {
                var candidate = title.Substring(0, match.Index).Trim();
                if (IsSafeTitle(candidate) && !candidate.Equals(title.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    baseTitle = candidate;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetCoreTitle(string title, out string coreTitle)
        {
            coreTitle = null;
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var baseCandidate = TryGetBaseTitle(title, out var derivedBase) ? derivedBase : title.Trim();
            var match = GenericDescriptorRegex.Match(baseCandidate);
            if (match.Success && match.Index > 0)
            {
                var candidate = baseCandidate.Substring(0, match.Index).Trim();
                if (IsSafeTitle(candidate) && !candidate.Equals(baseCandidate, StringComparison.OrdinalIgnoreCase))
                {
                    coreTitle = candidate;
                    return true;
                }
            }

            return false;
        }

        public List<string> AnimeSearchTitles
        {
            get
            {
                var isAniDbSourced = Series?.PrimaryMetadataProvider?.Equals("anidb", StringComparison.OrdinalIgnoreCase) == true ||
                                     (Series?.AniDbId ?? 0) > 0;

                if (!isAniDbSourced)
                {
                    var standardTitles = new List<string>();

                    if (Series?.Title != null)
                    {
                        standardTitles.Add(Series.Title);
                    }

                    if (Series?.AlternateTitles != null)
                    {
                        standardTitles.AddRange(Series.AlternateTitles);
                    }

                    if (SceneTitles != null)
                    {
                        standardTitles.AddRange(SceneTitles);
                    }

                    return standardTitles.Select(NormalizeAnimeTitle).Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
                }

                // AniDB-sourced Policy B candidate title ordering
                var titles = new List<string>();

                var romajiFull = Series.AlternateTitles?.FirstOrDefault(t => !IsNativeJapaneseTitle(t)) ?? Series.Title;
                var nativeJapanese = Series.AlternateTitles?.FirstOrDefault(IsNativeJapaneseTitle)
                                     ?? (IsNativeJapaneseTitle(Series.Title) ? Series.Title : null);
                var englishFull = !IsNativeJapaneseTitle(Series.Title) && !Series.Title.Equals(romajiFull, StringComparison.OrdinalIgnoreCase)
                    ? Series.Title
                    : Series.AlternateTitles?.FirstOrDefault(t => !IsNativeJapaneseTitle(t) && !t.Equals(romajiFull, StringComparison.OrdinalIgnoreCase));

                var hasDelimiter = (romajiFull != null && TryGetBaseTitle(romajiFull, out _)) ||
                                   (romajiFull == null && Series.Title != null && TryGetBaseTitle(Series.Title, out _));

                if (!hasDelimiter)
                {
                    // No delimiter: Romaji Full, Native Japanese, English Full, then Synonyms fill remaining slots
                    if (romajiFull != null)
                    {
                        titles.Add(romajiFull);
                    }

                    if (nativeJapanese != null)
                    {
                        titles.Add(nativeJapanese);
                    }

                    if (englishFull != null)
                    {
                        titles.Add(englishFull);
                    }

                    if (Series.AlternateTitles != null)
                    {
                        titles.AddRange(Series.AlternateTitles);
                    }

                    if (SceneTitles != null)
                    {
                        titles.AddRange(SceneTitles);
                    }
                }
                else
                {
                    // With delimiter: Policy B ordering
                    // 1. Romaji Full
                    if (romajiFull != null)
                    {
                        titles.Add(romajiFull);
                    }

                    // 2. Native Japanese (guaranteed slot 2)
                    if (nativeJapanese != null)
                    {
                        titles.Add(nativeJapanese);
                    }

                    // 3. English Full
                    if (englishFull != null)
                    {
                        titles.Add(englishFull);
                    }

                    // 4. Romaji Base (stripped)
                    string romajiBase = null;
                    if (romajiFull != null && TryGetBaseTitle(romajiFull, out romajiBase))
                    {
                        titles.Add(romajiBase);
                    }

                    // 5. Primary Synonym if one exists, else English Base
                    string primarySynonym = null;
                    if (Series.AlternateTitles != null)
                    {
                        primarySynonym = Series.AlternateTitles.FirstOrDefault(t =>
                            !IsNativeJapaneseTitle(t) &&
                            !t.Equals(romajiFull, StringComparison.OrdinalIgnoreCase) &&
                            (englishFull == null || !t.Equals(englishFull, StringComparison.OrdinalIgnoreCase)) &&
                            (romajiBase == null || !t.Equals(romajiBase, StringComparison.OrdinalIgnoreCase)));
                    }

                    if (primarySynonym != null)
                    {
                        titles.Add(primarySynonym);
                    }

                    // Fallback to English Base if synonym didn't fill slot, or as next candidate
                    if (englishFull != null && TryGetBaseTitle(englishFull, out var englishBase))
                    {
                        titles.Add(englishBase);
                    }

                    // Fallback to Core Keyword (generic descriptor stripped)
                    if (romajiFull != null && TryGetCoreTitle(romajiFull, out var romajiCore))
                    {
                        titles.Add(romajiCore);
                    }

                    if (englishFull != null && TryGetCoreTitle(englishFull, out var englishCore))
                    {
                        titles.Add(englishCore);
                    }

                    // Any remaining alternate titles
                    if (Series.AlternateTitles != null)
                    {
                        titles.AddRange(Series.AlternateTitles);
                    }

                    if (SceneTitles != null)
                    {
                        titles.AddRange(SceneTitles);
                    }
                }

                return titles.Select(NormalizeAnimeTitle).Distinct(StringComparer.InvariantCultureIgnoreCase).Take(5).ToList();
            }
        }

        public static string NormalizeAnimeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            var normalized = SmartQuotes.Replace(title, "'");
            return TrailingPunctuation.Replace(normalized, "").Trim();
        }

        public static string GetCleanSceneTitle(string title)
        {
            Ensure.That(title, () => title).IsNotNullOrWhiteSpace();

            var cleanTitle = BeginningThe.Replace(title, string.Empty);

            cleanTitle = cleanTitle.Replace("&", "and");
            cleanTitle = SpecialCharacter.Replace(cleanTitle, "");
            cleanTitle = NonWord.Replace(cleanTitle, "+");

            // remove any repeating +s
            cleanTitle = Regex.Replace(cleanTitle, @"\+{2,}", "+");
            cleanTitle = cleanTitle.RemoveDiacritics();
            return cleanTitle.Trim('+', ' ');
        }
    }
}
