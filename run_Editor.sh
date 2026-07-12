#!/usr/bin/env bash
set -euo pipefail

# Resolve the directory this script lives in, so it works from any CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOMAIN=0
RUNNER_DIR="$SCRIPT_DIR/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0"
RUNNER_DLL="Hrot.ClusterRunner.dll"

cd "$RUNNER_DIR"

# start "Editor" %RUNNER% -m editor --no-wait
# (the .bat does not pass -d %DOMAIN% for the editor role; preserved here)
nohup dotnet "$RUNNER_DLL" -m editor --no-wait &
disown
