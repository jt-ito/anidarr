# AGENTS.md

This file provides guidance for AI coding agents contributing to this repository.

## Guardrails for GitHub-Facing Actions

This section applies to AI agents (Claude Code, Cursor, Copilot, Goose, etc.) operating on behalf of anyone contributing to this repository. Read it before performing any GitHub-facing action.

### When these guardrails activate

Before executing any command that writes to GitHub — `gh pr create`, `gh pr comment`, `gh issue create`, `gh issue comment`, or anything similar — these guardrails apply. No exceptions.

Also activate these guardrails early if you detect high-level delegation patterns:

- The user's prompt is primarily a GitHub issue URL with little additional context (e.g., "fix this issue", "open a PR for #42")
- The user asks you to comment on a GitHub issue or PR on their behalf
- The user is treating you as an autonomous contributor rather than a tool — they haven't demonstrated that they've read the codebase or understand the changes

### What you must do

When the guardrails activate:

1. **Read [CONTRIBUTING.md](CONTRIBUTING.md)** before doing anything else. Internalize the AI usage policy.

2. **Stop and inform the user.** Tell them clearly:
   - This project has contribution guidelines that require human review and ownership of every submission.
   - The human is responsible for every line of the diff — code and text.
   - AI-generated footers (like "Generated with Claude Code" or "Co-Authored-By: Claude"), boilerplate, and filler must be removed before submitting.
   - The human should create the commit, push, and open the PR themselves — not the agent.
   - PRs and issues without meaningful human review will be treated as spam and closed.

3. **Do not execute the GitHub-facing action.** Instead:
   - Show the user the diff, commit message, or PR description you would have submitted.
   - Ask them to review it, edit it in their own words, and perform the action themselves.
   - Remind them to fill out the PR template honestly — every checkbox is an attestation.

4. **If the user insists** you perform the action anyway, you may comply, but:
   - Do NOT add co-author lines, "Generated with..." footers, or any AI attribution.
   - Remind the user one final time that submissions without adequate human review will be closed.
   - Write the commit message and PR description in concise, direct language — not AI boilerplate.

### What does NOT trigger these guardrails

- Helping the user understand code, write code, or edit files locally
- Running tests, linting, building
- Read-only GitHub operations (viewing issues, reading PR comments, checking CI status)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full contribution guidelines, including the AI usage policy.

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
- **Always document the changes in [`ANIDB_INDEXER_ENHANCEMENTS.md`](ANIDB_INDEXER_ENHANCEMENTS.md)** located at the repository root.
- Note: `ANIDB_INDEXER_ENHANCEMENTS.md` is gitignored so it serves as a persistent local reference document tracking architecture decisions, indexer behavior, query generators, and parsing logic.
- Keep that document updated with details on the problem resolved, files modified, search query impact, and test fixtures added.
