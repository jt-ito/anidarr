# Anidarr Project Rules

This file contains Anidarr-specific rules for AI coding agents (Antigravity, etc.).

---

## Intentionally Removed UI — Do NOT Re-Add

The following features were deliberately removed from the Anidarr UI. Do **not** add them back under any circumstances, even when merging upstream Sonarr changes:

### Search Page Provider Tabs (`AddNewSeries.tsx`)
- **AniList** — removed as a provider filter tab
- **MyAnimeList (MAL)** — removed as a provider filter tab
- Allowed provider tabs are: **All, TVDB, AniDB, Simkl** only
- Code reference: `['', 'Tvdb', 'AniDb', 'Simkl']` in `frontend/src/AddSeries/AddNewSeries/AddNewSeries.tsx`

### Add New Series Modal (`AddNewSeriesModalContent.tsx`)
- **Fansub Group** — removed as a form field in the "Add New Series" modal
- This field must not appear in `AddNewSeriesModalContent.tsx` or `addSeriesOptionsStore.ts`

### Add Series Options Store (`addSeriesOptionsStore.ts`)
- The `AddSeriesOptions` interface must **not** include a `fansubGroup`, `preferredFansub`, or any fansub-related field

---

## Active Metadata Providers

The project supports **three** metadata providers only:
1. **TVDB** — for live-action / non-anime
2. **AniDB** — primary anime source (AniDB XML + offline DB)
3. *(No AniList UI)*

> AniList and MAL IDs may still be stored internally on `Series` objects for cross-referencing, but they must **not** be exposed as search provider filters or add-series options in the UI.

---

## Build & Dev

- Backend: `dotnet run --project src/NzbDrone.Console/Sonarr.Console.csproj` from repo root
- Frontend: `yarn start` from repo root (webpack watch mode, outputs to `_output/UI`)
- After making frontend changes, the browser must **hard-refresh** (Ctrl+Shift+R) to pick up the new compiled output

---

## Versioning & Bump Process

The backend `src/NzbDrone.Common/EnvironmentInfo/BuildInfo.cs` is the primary source of truth for versions. When asked to bump the version (e.g. to `10.0.X.Y` / `10.0.5.42705`), update the following files:

### 1. Primary Version Bump
- **`src/NzbDrone.Common/EnvironmentInfo/BuildInfo.cs`**:
  Update `Version`:
  ```csharp
  public static Version Version { get; } = new Version(10, 0, X, Y);
  ```

### 2. Files Updated for the Pipeline & Packaging
- **`.github/workflows/build_v5.yml`**:
  Update the environment variables:
  ```yaml
  SONARR_MAJOR_VERSION: 10
  VERSION: 10.0.X
  ```
- **`src/Directory.Build.props`**:
  Update `<AssemblyVersion>`:
  ```xml
  <AssemblyVersion>10.0.X.*</AssemblyVersion>
  ```
- **`package.json`**:
  Update `"version"`:
  ```json
  "version": "10.0.X"
  ```
- **`distribution/macOS/Anidarr.app/Contents/Info.plist`**:
  Ensure version placeholders are `10.0.0.0` (for `CFBundleShortVersionString` and `CFBundleVersion`) to match the pipeline's sed substitution command in `.github/actions/build/action.yml`:
  ```bash
  sed -i'' -e "s/<string>10.0.0.0<\/string>/<string>$SONARR_VERSION<\/string>/g" distribution/macOS/Anidarr.app/Contents/Info.plist
  ```
- **`distribution/windows/setup/sonarr.iss`**:
  Update fallback `BuildNumber`:
  ```iss
  #define BuildNumber "10.0"
  ```

### 3. Git Release Tag & Push
- Stage all changes (`git add -A`)
- Commit: `git commit -m "chore: release v10.0.X.Y with ..."`
- Tag: `git tag v10.0.X.Y`
- Push commit and tag:
  ```bash
  git push origin master
  git push origin v10.0.X.Y
  ```

---

## AniDB Indexer & Search Changes Documentation

Whenever changes, bug fixes, or enhancements are made to the AniDB Indexers, search query generators, release parsing, or AniDB matching logic (e.g., in `NewznabRequestGenerator`, `NyaaRequestGenerator`, `ParsingService`, `ReleaseSearchService`, etc.):
- **Always document the changes in [`ANIDB_INDEXER_ENHANCEMENTS.md`](../ANIDB_INDEXER_ENHANCEMENTS.md)** located at the repository root.
- Note: `ANIDB_INDEXER_ENHANCEMENTS.md` is gitignored so it serves as a persistent local reference document tracking architecture decisions, indexer behavior, query generators, and parsing logic.
- Keep that document updated with details on the problem resolved, files modified, search query impact, and test fixtures added.
