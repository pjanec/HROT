#!/usr/bin/env bash
set -euo pipefail

# Resolve the directory this script lives in, so it works from any CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOMAIN=0
RUNNER_DIR="$SCRIPT_DIR/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0"
RUNNER_DLL="Hrot.ClusterRunner.dll"

cd "$RUNNER_DIR"

# start "SimHost" %RUNNER% -d %DOMAIN% -m simhost --no-wait
nohup dotnet "$RUNNER_DLL" -d "$DOMAIN" -m simhost --no-wait &
disown
#nohup dotnet "$RUNNER_DLL" -d "$DOMAIN" -m ig      --no-wait &
#nohup dotnet "$RUNNER_DLL" -d "$DOMAIN" -m ios     --no-wait &

#nohup dotnet "$RUNNER_DLL" -d "$DOMAIN" -m simhost --wait-for ig,ios &
#nohup dotnet "$RUNNER_DLL" -d "$DOMAIN" -m ig      --wait-for simhost,ios &
#nohup dotnet "$RUNNER_DLL" -d "$DOMAIN" -m ios     --wait-for simhost,ig &
