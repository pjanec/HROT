#!/usr/bin/env bash
#
# Runs the MCP-driven system-test harness (Hrot.SystemTests) — the suite that boots the REAL editor
# headless and drives it over the AI-debug HTTP API.
#
# This is the slow lane on purpose (design D10): it is NOT the per-edit gate. Use scripts/quick-check.sh
# for that. Expect ~20-30 s plus the build.
#
# Usage:
#   scripts/run-system-tests.sh                     # the whole SystemSmoke suite
#   scripts/run-system-tests.sh Playing_hill_attack # only cases matching a name
#   scripts/run-system-tests.sh --no-build          # skip the build (only when nothing changed)
#
set -uo pipefail

cd "$(dirname "$0")/.."
export PATH="$PATH:$HOME/.dotnet"

PROJECT="Hrot/Runner/Hrot.SystemTests/Hrot.SystemTests.csproj"
BUILD=1
NAME_FILTER=""

for arg in "$@"; do
  case "$arg" in
    --no-build) BUILD=0 ;;
    *)          NAME_FILTER="$arg" ;;
  esac
done

# ── Preflight ────────────────────────────────────────────────────────────────
# The suite SKIPS itself with a stated reason when these are missing, so a bare run would look
# green when it in fact ran nothing. Say so up front instead.
if [[ "$(uname -s)" != "MINGW"* && "$(uname -s)" != "CYGWIN"* ]]; then
  if ! command -v Xvfb >/dev/null 2>&1; then
    echo "!! Xvfb not found — the editor needs a display server on Linux."
    echo "   Install it:  sudo apt-get install -y xvfb libgl1 libglx-mesa0"
    echo "   Without it every case SKIPS (it will not fail, but it proves nothing)."
    exit 2
  fi
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "!! dotnet not on PATH. Try: export PATH=\"\$PATH:\$HOME/.dotnet\"" >&2
  exit 2
fi

# ── Build ────────────────────────────────────────────────────────────────────
# A stale binary reports PASSED against code that no longer exists — the single most expensive
# mistake available here, so building is the default and skipping it is the explicit flag.
if [[ $BUILD -eq 1 ]]; then
  echo "== building $PROJECT =="
  dotnet build "$PROJECT" -v q --nologo || exit 1
fi

# ── Run ──────────────────────────────────────────────────────────────────────
FILTER='Category=SystemSmoke'
if [[ -n "$NAME_FILTER" ]]; then
  FILTER="$FILTER&FullyQualifiedName~$NAME_FILTER"
fi

echo "== running: $FILTER =="
dotnet test "$PROJECT" --no-build --nologo --filter "$FILTER"
STATUS=$?

# The editor's own console output is what explains a failure, and the fixture mirrors it to a file.
if [[ $STATUS -ne 0 ]]; then
  echo
  echo "== editor logs from this run =="
  ls -t /tmp/hrot-systemtests-editor-*.log 2>/dev/null | head -3 | while read -r log; do
    echo "--- $log (last 30 lines) ---"
    tail -30 "$log"
  done
fi

exit $STATUS
