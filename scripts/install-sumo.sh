#!/usr/bin/env bash
# Installs SUMO (netconvert) at the version HROT follows — tools/sumo/version.txt (the vendored SumoSharp's, 1.20.0) — into the repo's
# gitignored .local/sumo/<version>/, from the official Python package (eclipse-sumo on PyPI). Terrain From OSM §9.1; found by the shared
# locator (Hrot.Terrain.InputSet.Netconvert). Idempotent. Nothing SUMO-made is committed.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(tr -d '[:space:]' < "$ROOT/tools/sumo/version.txt")"
DEST="$ROOT/.local/sumo/$VERSION"
NC="$DEST/venv/bin/netconvert"
if [ -x "$NC" ] && "$NC" --version 2>/dev/null | grep -q "Version $VERSION"; then
  echo "SUMO $VERSION already installed: $NC"; exit 0
fi
command -v python3 >/dev/null || { echo "python3 is needed" >&2; exit 1; }
mkdir -p "$DEST"
python3 -m venv "$DEST/venv"
"$DEST/venv/bin/pip" install --quiet --disable-pip-version-check "eclipse-sumo==$VERSION"
"$NC" --version | head -1
echo "installed: $NC"
