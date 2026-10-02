using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.MetadataSource
{
    public class ImportChoice
    {
        public string Provider { get; set; }

        public int Id { get; set; }

        public string Title { get; set; }
    }

    public interface IImportChoiceStore
    {
        ImportChoice Find(string folderName);

        void Remember(string folderName, string provider, int id, string title);
    }

    /// <summary>
    /// Remembers which series the user picked for a folder name, so the same folder
    /// resolves instantly (and identically) the next time it is imported.
    /// </summary>
    public class ImportChoiceStore : IImportChoiceStore
    {
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);

        private readonly string _filePath;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;
        private readonly object _lock = new object();
        private Dictionary<string, ImportChoice> _choices;

        public ImportChoiceStore(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, Logger logger)
        {
            _filePath = Path.Combine(appFolderInfo.AppDataFolder, "import-choices.json");
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public ImportChoice Find(string folderName)
        {
            lock (_lock)
            {
                return Load().TryGetValue(Key(folderName), out var choice) ? choice : null;
            }
        }

        public void Remember(string folderName, string provider, int id, string title)
        {
            if (string.IsNullOrWhiteSpace(folderName) || id <= 0 || (provider != "tvdb" && provider != "anidb"))
            {
                return;
            }

            lock (_lock)
            {
                var choices = Load();

                choices[Key(folderName)] = new ImportChoice { Provider = provider, Id = id, Title = title };

                try
                {
                    _diskProvider.WriteAllText(_filePath, JsonSerializer.Serialize(choices, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not save import choices to {0}", _filePath);
                }
            }
        }

        private static string Key(string folderName)
        {
            return Spaces.Replace(folderName.Trim().ToLowerInvariant(), " ");
        }

        private Dictionary<string, ImportChoice> Load()
        {
            if (_choices != null)
            {
                return _choices;
            }

            try
            {
                _choices = _diskProvider.FileExists(_filePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, ImportChoice>>(_diskProvider.ReadAllText(_filePath))
                    : null;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not read import choices from {0}", _filePath);
            }

            return _choices ??= new Dictionary<string, ImportChoice>();
        }
    }
}
