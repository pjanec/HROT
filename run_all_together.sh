#!/usr/bin/env bash
set -euo pipefail

# Resolve the directory this script lives in, so it works from any CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOMAIN=0
RUNNER_DIR="$SCRIPT_DIR/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0"
RUNNER_DLL="$RUNNER_DIR/Hrot.ClusterRunner.dll"

pids=()

cleanup() {
    echo "Stopping..."
    for pid in "${pids[@]}"; do
        kill "$pid" 2>/dev/null || true
    done
}
trap cleanup INT TERM

# %RUNNER% -m all   (RUNNER already carries "-d %DOMAIN%" in the .bat)
dotnet "$RUNNER_DLL" -d "$DOMAIN" -m all &
pids+=("$!")

wait "${pids[@]}" || true
