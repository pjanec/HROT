#!/usr/bin/env bash
# Every fine-grained node in ONE process: orchestrator, simhost, ig (player), cgf, traffic.
# Usage: run_All.sh [DOMAIN] [runner options...]     (DDS domain, default 0)
#   e.g. ./run_All.sh --scenario showcase-crossfire --play     (load Crossfire LIVE and run it; docs/MANUAL_TEST_Scenario_Evaluation.md §1)
#   Any argument after the domain (or the first that starts with "-") is passed to the runner as is.
set -euo pipefail

# Resolve the directory this script lives in, so it works from any CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOMAIN=0
if [[ $# -gt 0 && "$1" != -* ]]; then DOMAIN="$1"; shift; fi
RUNNER_DIR="$SCRIPT_DIR/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0"
RUNNER_DLL="Hrot.ClusterRunner.dll"

cd "$RUNNER_DIR"

# Foreground: Ctrl+C reaches the runner directly.
exec dotnet "$RUNNER_DLL" -d "$DOMAIN" -m all "$@"
