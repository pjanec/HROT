#!/usr/bin/env bash
# scenario-regression.sh — the NIGHTLY scenario regression (CE-4042, docs/DESIGN_Scenario_Regression.md).
#
#   scripts/scenario-regression.sh                    # every scenario with a checks.json
#   scripts/scenario-regression.sh ua-tank-ammo       # one (or a,b,c)
#   OUT=artifacts/regression NO_BUILD=1 scripts/scenario-regression.sh
#
# Each scenario runs in a FRESH editor process (headless, no window, no Xvfb) — a reload still leaks state (CE-3294) — judged
# by its checks.json and compared with its golden.json. Writes to $OUT: <scenario>.log, <scenario>.json, summary.json, junit.xml.
# Exit code 0 = every scenario passes. A CI job runs exactly this (template: docs/ci/scenario-regression.workflow.yml).
set -euo pipefail
cd "$(dirname "$0")/.."

WHAT="${1:-all}"
OUT="${OUT:-artifacts/scenario-regression/$(date -u +%Y%m%d-%H%M%S)}"
RUNNER=Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll

if [[ "${NO_BUILD:-0}" != "1" ]]; then
  # ⚠ restore first: --no-restore after a merge that adds a package runs without it (CLAUDE.md, the third stale-binary trap)
  dotnet restore Hrot/Runner/Hrot.ClusterRunner >/dev/null
  dotnet build Hrot/Runner/Hrot.ClusterRunner --no-restore -v q -nologo
fi

mkdir -p "$OUT"
echo "scenario regression: $WHAT → $OUT"
set +e
dotnet "$RUNNER" --mode editor --headless --verify "$WHAT" --verify-out "$OUT"
code=$?
set -e
echo "junit: $OUT/junit.xml · summary: $OUT/summary.json · exit $code"
exit $code
