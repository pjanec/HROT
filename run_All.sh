#!/usr/bin/env bash
# Every fine-grained node in ONE process: orchestrator, simhost, ig (player), cgf, traffic.
# Usage: run_All.sh [DOMAIN]     (DDS domain, default 0)
set -euo pipefail

# Resolve the directory this script lives in, so it works from any CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOMAIN="${1:-0}"
RUNNER_DIR="$SCRIPT_DIR/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0"
RUNNER_DLL="Hrot.ClusterRunner.dll"

cd "$RUNNER_DIR"

# Foreground: Ctrl+C reaches the runner directly.
exec dotnet "$RUNNER_DLL" -d "$DOMAIN" -m all
