#!/usr/bin/env bash
# quick-check.sh — the SMALL-FIX loop. ~16s instead of ~80s.
#
#   scripts/quick-check.sh <test-project.csproj> [xunit-filter] [--isolated]
#
# Measured on this repo, 2026-08-20:
#   dotnet build            (restore + dependency graph)   79 s   <-- the real bottleneck
#   dotnet build --no-restore                              16 s
#   dotnet build --no-restore --no-dependencies            13 s
#   dotnet test  --no-build --filter <one class>            3 s
#   dotnet test  --no-build  (Blueprints, 3870 tests)     179 s
#
# ⇒ For a small fix the cost was never the tests. It was RESTORE.
#
# ⛔ THE TRAP THIS SCRIPT EXISTS TO CLOSE: `dotnet test --no-build` happily runs a STALE
#    binary when the build failed, and reports PASSED. That bit twice in one session.
#    This script refuses to run tests unless the build actually succeeded.
#
# --isolated adds --no-dependencies: only legal when the edit is confined to this project
# (e.g. a test-only change). If you touched a referenced project, leave it off.
set -uo pipefail

PROJ="${1:-}"
FILTER="${2:-}"
ISOLATED="${3:-}"

if [[ -z "$PROJ" ]]; then
  echo "usage: scripts/quick-check.sh <test-project.csproj> [filter] [--isolated]" >&2
  exit 2
fi

BUILD_ARGS=(--no-restore -v q --nologo)
[[ "$ISOLATED" == "--isolated" ]] && BUILD_ARGS+=(--no-dependencies)

echo "── build ─────────────────────────────────────────────"
if ! dotnet build "$PROJ" "${BUILD_ARGS[@]}"; then
  echo
  echo "⛔ BUILD FAILED — not running tests." >&2
  echo "   (a --no-build run here would have tested a stale binary and printed PASSED)" >&2
  exit 1
fi

echo "── test ──────────────────────────────────────────────"
TEST_ARGS=(--no-build --nologo -v q)
[[ -n "$FILTER" ]] && TEST_ARGS+=(--filter "$FILTER")

# Frame rails need a display; harmless for everything else.
if command -v xvfb-run >/dev/null 2>&1; then
  xvfb-run -a -s "-screen 0 1280x800x24" dotnet test "$PROJ" "${TEST_ARGS[@]}"
else
  dotnet test "$PROJ" "${TEST_ARGS[@]}"
fi
