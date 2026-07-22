using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Configuration
{
    [TestFixture]
    public class InteractiveImportFoldersConfigFixture : TestBase<ConfigService>
    {
        private List<Config> _storedConfigs;

        [SetUp]
        public void SetUp()
        {
            _storedConfigs = new List<Config>();

            // Wire up Upsert to capture stored values so All() can return them later,
            // simulating how the real SQLite repository round-trips data.
            Mocker.GetMock<IConfigRepository>()
                  .Setup(c => c.Upsert(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((key, value) =>
                  {
                      _storedConfigs.RemoveAll(c => c.Key == key);
                      _storedConfigs.Add(new Config { Key = key, Value = value });
                  });

            Mocker.GetMock<IConfigRepository>()
                  .Setup(c => c.All())
                  .Returns(_storedConfigs);
        }

        // ===================================================================
        //  1. Backend Persistence: round-trip of pinned-path JSON through the
        //     ConfigService, proving data survives save→fetch with no browser.
        // ===================================================================

        [Test]
        public void should_persist_interactive_import_folders_json()
        {
            var json = "{\"pinnedPaths\":[{\"id\":\"1\",\"label\":\"Downloads\",\"path\":\"/mnt/downloads\"}],\"activePinnedPathId\":\"1\",\"recentFolders\":[],\"favoriteFolders\":[]}";

            Subject.InteractiveImportFolders = json;

            // Verify the write actually hit the repository
            Mocker.GetMock<IConfigRepository>()
                  .Verify(c => c.Upsert("interactiveimportfolders", json), Times.Once());
        }

        [Test]
        public void should_round_trip_interactive_import_folders_through_config_service()
        {
            var json = "{\"pinnedPaths\":[{\"id\":\"42\",\"label\":\"Anime\",\"path\":\"/data/anime\"}],\"activePinnedPathId\":\"42\",\"recentFolders\":[],\"favoriteFolders\":[]}";

            // Save
            Subject.InteractiveImportFolders = json;

            // Fetch — this reads from the cache which was populated by All(),
            // simulating a completely fresh client session fetching from the DB.
            var retrieved = Subject.InteractiveImportFolders;

            retrieved.Should().Be(json, "pinned-path JSON must survive a save→fetch round-trip through the database");
        }

        [Test]
        public void should_return_empty_string_when_no_interactive_import_folders_stored()
        {
            // No prior write — should return the default empty string
            var result = Subject.InteractiveImportFolders;

            result.Should().BeEmpty("the default when nothing is stored should be an empty string");
        }

        [Test]
        public void should_persist_active_pin_designation_in_json()
        {
            var json = "{\"pinnedPaths\":[{\"id\":\"a\",\"label\":\"Pin A\",\"path\":\"/a\"},{\"id\":\"b\",\"label\":\"Pin B\",\"path\":\"/b\"}],\"activePinnedPathId\":\"b\",\"recentFolders\":[],\"favoriteFolders\":[]}";

            Subject.InteractiveImportFolders = json;
            var retrieved = Subject.InteractiveImportFolders;

            retrieved.Should().Contain("\"activePinnedPathId\":\"b\"",
                "the active pin designation must be correctly persisted and retrievable");
        }

        // ===================================================================
        //  2. Migration path: simulates what the frontend migration does —
        //     push data via SaveConfigDictionary (which is what the API's
        //     PUT endpoint calls internally) and verify it round-trips.
        // ===================================================================

        [Test]
        public void should_save_migrated_data_via_config_dictionary()
        {
            // This is the exact shape the frontend migration code produces
            // when it reads old localStorage data and pushes it to the backend.
            var migratedJson = "{\"pinnedPaths\":[{\"id\":\"legacy1\",\"label\":\"Old Pin\",\"path\":\"/old/path\"}],\"activePinnedPathId\":\"legacy1\",\"recentFolders\":[],\"favoriteFolders\":[]}";

            var dict = new Dictionary<string, object>
            {
                { "InteractiveImportFolders", migratedJson }
            };

            Subject.SaveConfigDictionary(dict);

            // Verify the data was written
            var retrieved = Subject.InteractiveImportFolders;
            retrieved.Should().Be(migratedJson,
                "migrated localStorage data pushed via SaveConfigDictionary must be retrievable");
        }

        [Test]
        public void should_be_idempotent_when_migration_runs_twice()
        {
            // First migration push
            var migratedJson = "{\"pinnedPaths\":[{\"id\":\"legacy1\",\"label\":\"Old Pin\",\"path\":\"/old/path\"}],\"activePinnedPathId\":\"legacy1\",\"recentFolders\":[],\"favoriteFolders\":[]}";

            var dict = new Dictionary<string, object>
            {
                { "InteractiveImportFolders", migratedJson }
            };

            Subject.SaveConfigDictionary(dict);

            // Second migration push with the same data — should not create duplicates
            // SaveConfigDictionary skips Upsert when the value hasn't changed
            Subject.SaveConfigDictionary(dict);

            // If the value is the same on the second call, it should NOT call Upsert again
            Mocker.GetMock<IConfigRepository>()
                  .Verify(
                      c => c.Upsert("interactiveimportfolders", migratedJson),
                      Times.Once(),
                      "Upsert should only be called once when the same migration data is pushed twice (idempotency)");
        }

        // ===================================================================
        //  3. Deletion fallback: verify that when the active pin is removed
        //     from the JSON, saving and fetching correctly reflects null/no
        //     active pin — no stale ID left behind at the data layer.
        // ===================================================================

        [Test]
        public void should_clear_active_pin_when_pin_deleted_from_json()
        {
            // Initial state: pin "x" is active
            var initialJson = "{\"pinnedPaths\":[{\"id\":\"x\",\"label\":\"Pin X\",\"path\":\"/x\"}],\"activePinnedPathId\":\"x\",\"recentFolders\":[],\"favoriteFolders\":[]}";
            Subject.InteractiveImportFolders = initialJson;

            // Frontend deletes pin "x": pinnedPaths is now empty, activePinnedPathId is null
            var afterDeleteJson = "{\"pinnedPaths\":[],\"activePinnedPathId\":null,\"recentFolders\":[],\"favoriteFolders\":[]}";
            Subject.InteractiveImportFolders = afterDeleteJson;

            var retrieved = Subject.InteractiveImportFolders;

            retrieved.Should().Contain("\"activePinnedPathId\":null",
                "deleting the active pin must result in null active pin, not a stale ID");
            retrieved.Should().Contain("\"pinnedPaths\":[]",
                "the deleted pin must not appear in the retrieved data");
        }

        [Test]
        public void should_preserve_remaining_pins_when_active_pin_deleted()
        {
            // Two pins, "a" is active
            var initialJson = "{\"pinnedPaths\":[{\"id\":\"a\",\"label\":\"Pin A\",\"path\":\"/a\"},{\"id\":\"b\",\"label\":\"Pin B\",\"path\":\"/b\"}],\"activePinnedPathId\":\"a\",\"recentFolders\":[],\"favoriteFolders\":[]}";
            Subject.InteractiveImportFolders = initialJson;

            // Delete pin "a", leave "b", clear active
            var afterDeleteJson = "{\"pinnedPaths\":[{\"id\":\"b\",\"label\":\"Pin B\",\"path\":\"/b\"}],\"activePinnedPathId\":null,\"recentFolders\":[],\"favoriteFolders\":[]}";
            Subject.InteractiveImportFolders = afterDeleteJson;

            var retrieved = Subject.InteractiveImportFolders;

            retrieved.Should().Contain("\"activePinnedPathId\":null",
                "active pin must be cleared when the active pin is deleted");
            retrieved.Should().Contain("Pin B",
                "remaining pins must be preserved after deleting the active pin");
            retrieved.Should().NotContain("Pin A",
                "the deleted pin must not appear");
        }
    }
}
