using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.MetadataSource
{
    public class FolderProbe
    {
        // Provider ("tvdb" / "anidb") ids found in the folder name or its tvshow.nfo
        public List<(string Provider, int Id)> IdHints { get; set; } = new List<(string Provider, int Id)>();

        // The show title most episode files agree on (empty when they don't)
        public List<string> FileTitles { get; set; } = new List<string>();

        public int VideoFileCount { get; set; }
    }

    public interface IFolderProbeService
    {
        FolderProbe Probe(string folderPath, string folderName);
    }

    /// <summary>
    /// Looks inside a library folder for evidence of what it holds: ids written into the
    /// folder name (Plex/Jellyfin style) or a tvshow.nfo, and the show title the episode
    /// file names agree on.
    /// </summary>
    public class FolderProbeService : IFolderProbeService
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
        private const int MaxFiles = 500;
        private const long MaxNfoBytes = 256 * 1024;

        private static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".avi", ".m4v", ".ts", ".webm", ".wmv", ".mov" };

        // "[tvdbid-81797]", "{tvdb-81797}", "(anidb=69)"
        private static readonly Regex FolderIdTag = new Regex(@"[\[\{\(]\s*(tvdb|anidb)(?:id)?\s*[-_=:]\s*(\d{1,8})\s*[\]\}\)]", Options);

        private static readonly Regex NfoUniqueId = new Regex(@"<uniqueid\b([^>]*)>\s*(\d{1,8})\s*</uniqueid>", Options);
        private static readonly Regex NfoIdElement = new Regex(@"<(tvdb_?id|anidb_?id)>\s*(\d{1,8})\s*</\1>", Options);
        private static readonly Regex UniqueIdType = new Regex(@"type\s*=\s*[""'](tvdb|anidb)[""']", Options);
        private static readonly Regex TrailingNumber = new Regex(@"\s\d{1,3}$", Options);

        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public FolderProbeService(IDiskProvider diskProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public FolderProbe Probe(string folderPath, string folderName)
        {
            var probe = new FolderProbe();

            AddFolderNameHints(probe, folderName);

            try
            {
                if (!_diskProvider.FolderExists(folderPath))
                {
                    return probe;
                }

                var files = _diskProvider.GetFiles(folderPath, true).Take(MaxFiles).ToList();
                var videos = files.Where(f => VideoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToList();

                probe.VideoFileCount = videos.Count;

                foreach (var nfo in files.Where(f => IsSeriesNfo(f, folderPath)))
                {
                    AddNfoHints(probe, nfo);
                }

                probe.FileTitles = DominantTitle(videos.Select(v => SearchTermCleaner.CleanFileTitle(Path.GetFileNameWithoutExtension(v))).ToList());
            }
            catch (Exception ex)
            {
                // Probing is best-effort evidence; never fail the lookup because of it
                _logger.Debug(ex, "Could not probe folder '{0}'", folderPath);
            }

            return probe;
        }

        private static void AddFolderNameHints(FolderProbe probe, string folderName)
        {
            foreach (Match match in FolderIdTag.Matches(folderName ?? string.Empty))
            {
                probe.IdHints.Add((match.Groups[1].Value.ToLowerInvariant(), int.Parse(match.Groups[2].Value)));
            }
        }

        private static bool IsSeriesNfo(string file, string folderPath)
        {
            // Only the show-level file: episode .nfo files carry episode ids that mean something else
            return string.Equals(Path.GetFileName(file), "tvshow.nfo", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(NormalizeSlashes(Path.GetDirectoryName(file)), NormalizeSlashes(folderPath), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSlashes(string path)
        {
            return path?.Replace('\\', '/').TrimEnd('/');
        }

        private void AddNfoHints(FolderProbe probe, string nfoPath)
        {
            if (_diskProvider.GetFileSize(nfoPath) > MaxNfoBytes)
            {
                return;
            }

            var content = _diskProvider.ReadAllText(nfoPath);

            if (!content.Contains("<tvshow", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (Match match in NfoUniqueId.Matches(content))
            {
                var type = UniqueIdType.Match(match.Groups[1].Value);

                if (type.Success)
                {
                    probe.IdHints.Add((type.Groups[1].Value.ToLowerInvariant(), int.Parse(match.Groups[2].Value)));
                }
            }

            foreach (Match match in NfoIdElement.Matches(content))
            {
                var provider = match.Groups[1].Value.StartsWith("tvdb", StringComparison.OrdinalIgnoreCase) ? "tvdb" : "anidb";

                probe.IdHints.Add((provider, int.Parse(match.Groups[2].Value)));
            }
        }

        // The title most files share. Needs agreement (two or more files, half of them) unless
        // there's only one file, so a stray file can't steer the search.
        internal static List<string> DominantTitle(List<string> titles)
        {
            var named = titles.Where(t => t.Length > 0).ToList();

            return PickDominant(named, titles.Count) ?? PickDominant(named.Select(t => TrailingNumber.Replace(t, string.Empty)).Where(t => t.Length > 0).ToList(), titles.Count) ?? new List<string>();
        }

        private static List<string> PickDominant(List<string> titles, int totalFiles)
        {
            var top = titles
                .GroupBy(t => t.ToLowerInvariant())
                .Select(g => (Title: g.First(), Count: g.Count()))
                .OrderByDescending(g => g.Count)
                .FirstOrDefault();

            if (top.Title == null)
            {
                return null;
            }

            var agreed = totalFiles == 1 || (top.Count >= 2 && top.Count * 2 >= totalFiles);

            return agreed ? new List<string> { top.Title } : null;
        }
    }
}
