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

        public List<string> AllSceneTitles => SceneTitles.Concat(CleanSceneTitles).Distinct().ToList();
        public List<string> CleanSceneTitles => SceneTitles.Select(GetCleanSceneTitle).Distinct().ToList();

        public List<string> AnimeSearchTitles
        {
            get
            {
                var titles = new List<string>();
                if (Series?.Title != null)
                {
                    titles.Add(Series.Title);
                }

                if (Series?.AlternateTitles != null)
                {
                    titles.AddRange(Series.AlternateTitles);
                }

                if (SceneTitles != null)
                {
                    titles.AddRange(SceneTitles);
                }

                return titles.Select(NormalizeAnimeTitle).Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
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
