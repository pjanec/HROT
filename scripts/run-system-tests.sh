#!/usr/bin/env bash
#
# Runs the MCP-driven system-test harness (Hrot.SystemTests) — the suite that boots the REAL editor
# headless and drives it over the AI-debug HTTP API.
#
# This is the slow lane on purpose (design D10): it is NOT the per-edit gate. Use scripts/quick-check.sh
# for that. Expect ~20-30 s plus the build.
#
# Usage:
#   scripts/run-system-tests.sh                     # the whole suite (SystemSmoke + SystemModes)
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
# HN-009: the filter covers BOTH categories, and it did not.
#
# Measured 2026-08-23: the project holds 52 cases; this script ran 44. ModeStartupRails.cs carries only
# [Trait("Category","SystemModes")], so its 8 rails -- including the --mode ig TRIPWIRE that is meant to
# fail the day ST-020 is fixed -- were never run by the project's own runner. A rail the standard gate
# does not execute is a rail nobody will see fire, which is the same disease as HN-007: green because
# nothing looked.
#
# Widened here rather than by editing ModeStartupRails.cs: that file belongs to the runner batch, and a
# trait added to it would be a cross-lane edit for a problem that is the SCRIPT's.
# Cost, measured: SystemSmoke 44 cases ~29 s; SystemModes 8 cases ~62 s (each boots a runner in a
# different mode). ~90 s total. This is the slow lane on purpose (D10) -- scripts/quick-check.sh is the
# per-edit gate -- so covering both is the right trade.
FILTER='Category=SystemSmoke|Category=SystemModes'
if [[ -n "$NAME_FILTER" ]]; then
  # A name filter applies to both categories, so it must distribute over the OR rather than bind to the
  # last clause: "A|B&Name~x" would filter only B.
  FILTER="(Category=SystemSmoke|Category=SystemModes)&FullyQualifiedName~$NAME_FILTER"
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
