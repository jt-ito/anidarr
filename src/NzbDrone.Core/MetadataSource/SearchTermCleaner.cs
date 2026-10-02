using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Turns release/folder names such as "[Group] Title - 01-12 [1080p][HEVC]" into
    /// search terms providers can actually match. Returns nothing for terms that
    /// are already clean, so ordinary searches are never altered.
    /// </summary>
    public static class SearchTermCleaner
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
        private const int MaxCandidates = 4;
        private const int MinAliasLength = 5;

        private static readonly Regex IdPrefix = new Regex(@"^\s*(?:tvdb|tvdbid|anidb|anilist|mal|tmdb|imdb)\s*:", Options);
        private static readonly Regex BracketGroup = new Regex(@"\[[^\]]*\]", Options);
        private static readonly Regex ParenGroup = new Regex(@"\(([^)]*)\)", Options);
        private static readonly Regex Cjk = new Regex(@"[぀-ヿ㐀-鿿＀-￯～]+", Options);
        private static readonly Regex StrayBrackets = new Regex(@"[\[\]()]", Options);
        private static readonly Regex Whitespace = new Regex(@"\s+", Options);
        private static readonly Regex DashSplit = new Regex(@"\s[-~–]\s", Options);
        private static readonly Regex Separators = new Regex(@"^[\s\-~–:+&,._]+|[\s\-~–:+&,._]+$", Options);
        private static readonly Regex Letters = new Regex(@"\p{L}{2,}", Options);

        // Words that mark a bracket/paren group as release info rather than part of the title
        private static readonly Regex TechWord = new Regex(
            @"\b(?:\d{3,4}p|\d{3,4}x\d{3,4}|bd|bdrip|bluray|blu-ray|dvd|dvdrip|web|web-?dl|webrip|hevc|x26[45]|h\.?26[45]|av1|aac|opus|ac3|flac|10[- ]?bit|8[- ]?bit|multi|dual|subs?|eng|batch|complete|uncensored|enhanced|remux|seasons?|s\d{1,2}|ovas?|oad|specials?|extras?|movies?|v\d|hs|ht|digital|raw|uhd|hdr|\d{4})\b",
            Options);

        // Noise that sits loose in the title text (not inside brackets)
        private static readonly Regex[] Noise =
        {
            new Regex(@"\b(?:ep(?:isodes?)?\.?\s*)?\d{1,4}\s*[-~–]\s*\d{1,4}\b", Options),
            new Regex(@"\s[-~–]\s*\d{1,4}(?:v\d)?\s*$", Options),
            new Regex(@"\bS\d{1,2}(?:\s*[-+&~]\s*S?\d{1,2})*\b", Options),
            new Regex(@"[+&]\s*(?:ovas?|oad|specials?|extras?)\b", Options),
            new Regex(@"\b(?:complete(?:\s+(?:series|collection))?|batch|uncensored|dual[- ]audio|multi[- ]?subs?|eng[- ]?subs?)\b", Options),

            // Codec, optionally followed by the release group ("x265-EMBER")
            new Regex(@"\b(?:x26[45]|h\.?26[45]|av1)(?:-[a-z0-9]+)?\b", Options),
            new Regex(@"\b(?:\d{3,4}p|bd|bdrip|dvdrip|webrip|web-?dl|hevc|aac|opus|ac3|flac|10[- ]?bit|v\d)\b", Options),
        };

        // Everything from the episode marker on is episode title / release info, not the show
        private static readonly Regex[] EpisodeTail =
        {
            new Regex(@"\bS\d{1,2}\s?E\d{1,4}\b.*$", Options),
            new Regex(@"\s[-~–]\s*(?:ep?\.?\s*)?\d{1,4}(?:v\d)?\b.*$", Options),
            new Regex(@"\bep(?:isode)?\.?\s?\d{1,4}\b.*$", Options),
            new Regex(@"\b(?:\d{3,4}p|bd|bdrip|dvdrip|webrip|web-?dl|hevc|x26[45]|av1)\b.*$", Options),
        };

        // Only meaningful (and only applied) when the name looks like a scene release,
        // so a title that happens to contain "DD" is left alone otherwise.
        private static readonly Regex SceneContext = new Regex(@"\b(?:x26[45]|h\.?26[45]|webrip|web-?dl|bdrip|blu-?ray|dvdrip)\b", Options);

        private static readonly Regex[] SceneNoise =
        {
            // " - GROUP" straight after an audio/codec token
            new Regex(@"(?<=\b(?:aac|ac3|flac|opus|x26[45]|dd\+?))\s*-\s*[a-z0-9][\w\-]*\s*$", Options),
            new Regex(@"\bengl?ish\s+(?:dub(?:bed)?|hardsubs?|subs?)\b", Options),
            new Regex(@"\bweb\b(?!-?dl)", Options),
            new Regex(@"\b\d{1,2}\s*bits?\b", Options),
            new Regex(@"\bDDP?\s*\+?(?:\s?\d\.\d)?(?=\s|$)", Options),
            new Regex(@"\b(?:dts|truehd|atmos|amzn|dsnp|hmax|atvp|hdr10?|uhd|repack|proper|remux)\b", Options),
        };

        /// <summary>
        /// Cleaner alternatives to <paramref name="query"/>, best first. Empty when the
        /// query needs no cleaning.
        /// </summary>
        public static List<string> GetCandidates(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || IdPrefix.IsMatch(query))
            {
                return new List<string>();
            }

            var original = Normalize(query);
            var candidates = new List<string>();

            // Folder-style names with no spaces ("Show.Name.2020") use . or _ as separators
            var text = query.Contains(' ') ? query : query.Replace('.', ' ').Replace('_', ' ');

            // Whatever follows the last tag is often a plain-English title
            var tail = ExtractTail(text);

            var alternates = new List<string>();

            text = BracketGroup.Replace(text, " ");
            text = ParenGroup.Replace(text, match =>
            {
                var content = match.Groups[1].Value.Trim();

                if (content.Length >= MinAliasLength && !TechWord.IsMatch(content))
                {
                    alternates.Add(content);
                }

                return " ";
            });

            var primary = CleanText(text);

            candidates.Add(primary);
            candidates.Add(CleanText(tail));
            candidates.AddRange(alternates.Select(CleanText));

            var beforeDash = DashSplit.Split(primary).FirstOrDefault();

            if (beforeDash != null && beforeDash.Contains(' '))
            {
                candidates.Add(CleanText(beforeDash));
            }

            var distinct = candidates
                .Where(c => Letters.IsMatch(c))
                .GroupBy(Normalize)
                .Select(g => g.First())
                .ToList();

            // Nothing differs from what was typed: no cleaning needed. Otherwise keep the
            // primary term even when it equals the original (a clean title with " - " still
            // needs its full form searched before the shortened one).
            return distinct.Any(c => Normalize(c) != original)
                ? distinct.Take(MaxCandidates).ToList()
                : new List<string>();
        }

        // Show title taken from an episode file name, e.g. "[Group] Show - 05 [1080p].mkv" -> "Show"
        public static string CleanFileTitle(string fileName)
        {
            var text = fileName.Contains(' ') ? fileName : fileName.Replace('.', ' ').Replace('_', ' ');

            text = BracketGroup.Replace(text, " ");
            text = ParenGroup.Replace(text, " ");

            foreach (var tail in EpisodeTail)
            {
                text = tail.Replace(text, " ");
            }

            return CleanText(text);
        }

        private static string ExtractTail(string text)
        {
            var close = text.LastIndexOfAny(new[] { ']', ')' });

            return close >= 0 && close < text.Length - 1 ? text.Substring(close + 1) : string.Empty;
        }

        private static string CleanText(string text)
        {
            text = Cjk.Replace(text, " ");

            // Scene tags go first: their release-group suffix is matched against the codec/audio
            // token before the generic pass strips those tokens.
            if (SceneContext.IsMatch(text))
            {
                foreach (var noise in SceneNoise)
                {
                    text = noise.Replace(text, " ");
                }
            }

            foreach (var noise in Noise)
            {
                text = noise.Replace(text, " ");
            }

            text = StrayBrackets.Replace(text, " ");
            text = Whitespace.Replace(text, " ");

            return Separators.Replace(text, string.Empty).Trim();
        }

        private static string Normalize(string text)
        {
            return Whitespace.Replace(text, " ").Trim().ToLowerInvariant();
        }
    }
}
