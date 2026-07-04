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

The project supports **four** metadata providers only:
1. **TVDB** — for live-action / non-anime
2. **AniDB** — primary anime source (AniDB XML + offline DB)
3. **Simkl** — secondary anime source
4. *(No AniList UI)*
5. *(No MyAnimeList UI)*

> AniList and MAL IDs may still be stored internally on `Series` objects for cross-referencing, but they must **not** be exposed as search provider filters or add-series options in the UI.

---

## Build & Dev

- Backend: `dotnet run --project src/NzbDrone.Console/Sonarr.Console.csproj` from repo root
- Frontend: `yarn start` from repo root (webpack watch mode, outputs to `_output/UI`)
- After making frontend changes, the browser must **hard-refresh** (Ctrl+Shift+R) to pick up the new compiled output
