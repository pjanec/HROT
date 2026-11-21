#!/usr/bin/env bash
# The replay browser: an offline host that browses recorded exercises (no cluster, no DDS peers).
set -euo pipefail

# Resolve the directory this script lives in, so it works from any CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

RUNNER_DIR="$SCRIPT_DIR/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0"
RUNNER_DLL="Hrot.ClusterRunner.dll"

cd "$RUNNER_DIR"

nohup dotnet "$RUNNER_DLL" -m replaybrowser &
disown
