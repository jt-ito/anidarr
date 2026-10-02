#!/usr/bin/env bash
#
# Starts a manual-import test over from scratch.
#
# Removes every series from the Anidarr library and forgets the choices remembered for imports.
# Your media files are NEVER touched: series are removed with "delete files" off, exactly like
# "Delete" in the UI with the "delete files" box unticked.
#
# Kept: settings, root folders, indexers and the offline anime title database. The AniDB cache is
# kept too, unless you pass --clear-anidb-cache (do that to test a cold start of the prefetching).
#
# Needs Anidarr to be running (it removes the series through its API) and either jq or python3.
#
# Usage:  ./reset-test-library.sh [data folder] [--clear-anidb-cache]
#         PORT=8990 ./reset-test-library.sh ~/anidarr-test-config
set -euo pipefail

DATA="$HOME/anidarr-test-config"
CLEAR_CACHE=false

for arg in "$@"; do
  case "$arg" in
    --clear-anidb-cache) CLEAR_CACHE=true ;;
    *) DATA="$arg" ;;
  esac
done

PORT="${PORT:-8990}"
URL="http://localhost:$PORT"

if [ ! -f "$DATA/config.xml" ]; then
  echo "No config.xml in $DATA. Pass your data folder as the first argument." >&2
  exit 1
fi

KEY=$(grep -oP '(?<=<ApiKey>)[^<]+' "$DATA/config.xml" || true)

if [ -z "$KEY" ]; then
  echo "Could not read the API key from $DATA/config.xml" >&2
  exit 1
fi

echo "Reading the library from $URL ..."

if ! SERIES_JSON=$(curl -fsS -H "X-Api-Key: $KEY" "$URL/api/v5/series"); then
  echo "Could not reach Anidarr on port $PORT. Start it first (PORT=$PORT ./Anidarr ...) and try again." >&2
  exit 1
fi

if command -v jq >/dev/null 2>&1; then
  IDS=$(printf '%s' "$SERIES_JSON" | jq -r '[.[].id] | join(",")')
elif command -v python3 >/dev/null 2>&1; then
  IDS=$(printf '%s' "$SERIES_JSON" | python3 -c 'import sys, json; print(",".join(str(s["id"]) for s in json.load(sys.stdin)))')
else
  echo "Install jq or python3 to run this." >&2
  exit 1
fi

if [ -z "$IDS" ]; then
  echo "The library is already empty."
else
  COUNT=$(printf '%s' "$IDS" | tr ',' '\n' | wc -l)
  echo "Removing $COUNT series from the library (files on disk are left alone) ..."

  curl -fsS -X DELETE \
    -H "X-Api-Key: $KEY" \
    -H "Content-Type: application/json" \
    -d "{\"seriesIds\":[$IDS],\"deleteFiles\":false,\"addImportListExclusion\":false}" \
    "$URL/api/v5/series/editor" >/dev/null

  echo "Done."
fi

if [ -f "$DATA/import-choices.json" ]; then
  rm -f "$DATA/import-choices.json"
  echo "Forgot the remembered import choices."
fi

if [ "$CLEAR_CACHE" = true ] && [ -d "$DATA/AniDbCache" ]; then
  rm -rf "$DATA/AniDbCache"
  echo "Cleared the AniDB cache."
fi

echo "Reload the Import page in the browser and start again."
