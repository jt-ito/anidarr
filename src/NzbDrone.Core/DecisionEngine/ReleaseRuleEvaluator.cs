using System;
using System.Linq;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Rules;

namespace NzbDrone.Core.DecisionEngine
{
    public interface IReleaseRuleEvaluator
    {
        bool IsMatch(ReleaseRule rule, RemoteEpisode remoteEpisode);
    }

    public class ReleaseRuleEvaluator : IReleaseRuleEvaluator
    {
        public bool IsMatch(ReleaseRule rule, RemoteEpisode remoteEpisode)
        {
            if (rule.Conditions == null || !rule.Conditions.Any())
            {
                return false;
            }

            foreach (var condition in rule.Conditions)
            {
                if (!EvaluateCondition(condition, remoteEpisode))
                {
                    return false;
                }
            }

            return true;
        }

        private bool EvaluateCondition(ReleaseRuleCondition condition, RemoteEpisode remoteEpisode)
        {
            var value = condition.Value ?? string.Empty;

            switch (condition.ConditionType)
            {
                case ReleaseRuleConditionType.ReleaseGroup:
                    return MatchString(remoteEpisode.ParsedEpisodeInfo.ReleaseGroup, value, condition.Operator);

                case ReleaseRuleConditionType.AudioType:
                    return MatchLanguage(remoteEpisode, value);

                case ReleaseRuleConditionType.CustomFormat:
                    // value is either the format ID or Name. Usually we'll store Name or ID. Let's assume ID as string for exact match.
                    // Wait, we can match by Name.
                    if (int.TryParse(value, out var formatId))
                    {
                        return remoteEpisode.CustomFormats.Any(c => c.Id == formatId);
                    }

                    return remoteEpisode.CustomFormats.Any(c => MatchString(c.Name, value, condition.Operator));

                case ReleaseRuleConditionType.Quality:
                    if (int.TryParse(value, out var qualityId))
                    {
                        return remoteEpisode.ParsedEpisodeInfo.Quality.Quality.Id == qualityId;
                    }

                    return MatchString(remoteEpisode.ParsedEpisodeInfo.Quality.Quality.Name, value, condition.Operator);

                case ReleaseRuleConditionType.ReleaseTitle:
                    return MatchString(remoteEpisode.Release.Title, value, condition.Operator);

                default:
                    return false;
            }
        }

        private bool MatchString(string actual, string expected, ReleaseRuleConditionOperator op)
        {
            if (string.IsNullOrWhiteSpace(actual))
            {
                return string.IsNullOrWhiteSpace(expected);
            }

            if (string.IsNullOrWhiteSpace(expected))
            {
                return false;
            }

            if (op == ReleaseRuleConditionOperator.Exact)
            {
                return actual.Equals(expected, StringComparison.InvariantCultureIgnoreCase);
            }
            else
            {
                // Contains
                return actual.IndexOf(expected, StringComparison.InvariantCultureIgnoreCase) >= 0;
            }
        }

        private bool MatchLanguage(RemoteEpisode remoteEpisode, string expectedAudioType)
        {
            // Expected values from frontend could be mapped to known combos.
            // E.g. "Dual Audio", "English", "Japanese", "Any"
            var languages = remoteEpisode.Languages;
            if (languages == null || !languages.Any())
            {
                return string.Equals(expectedAudioType, "Any", StringComparison.InvariantCultureIgnoreCase) ||
                       string.Equals(expectedAudioType, "Unknown", StringComparison.InvariantCultureIgnoreCase);
            }

            var hasEnglish = languages.Any(l => l.Name.Equals("English", StringComparison.InvariantCultureIgnoreCase));
            var hasJapanese = languages.Any(l => l.Name.Equals("Japanese", StringComparison.InvariantCultureIgnoreCase));

            var isDual = string.Equals(expectedAudioType, "Dual Audio", StringComparison.InvariantCultureIgnoreCase) ||
                          string.Equals(expectedAudioType, "Dual-Audio", StringComparison.InvariantCultureIgnoreCase) ||
                          string.Equals(expectedAudioType, "DUAL", StringComparison.InvariantCulture);

            if (isDual)
            {
                return languages.Count == 2;
            }

            var isMulti = string.Equals(expectedAudioType, "Multi Audio", StringComparison.InvariantCultureIgnoreCase) ||
                           string.Equals(expectedAudioType, "Multi-Audio", StringComparison.InvariantCultureIgnoreCase) ||
                           string.Equals(expectedAudioType, "MULTI", StringComparison.InvariantCulture);

            if (isMulti)
            {
                return languages.Count > 2;
            }

            if (string.Equals(expectedAudioType, "English", StringComparison.InvariantCultureIgnoreCase))
            {
                return hasEnglish;
            }

            if (string.Equals(expectedAudioType, "Japanese", StringComparison.InvariantCultureIgnoreCase))
            {
                return hasJapanese;
            }

            if (string.Equals(expectedAudioType, "Any", StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }

            // Fallback: Check if ANY language matches the string
            return languages.Any(l => MatchString(l.Name, expectedAudioType, ReleaseRuleConditionOperator.Exact));
        }
    }
}
