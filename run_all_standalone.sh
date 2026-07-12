#!/usr/bin/env bash
set -euo pipefail

# Resolve the directory this script lives in, so it works from any CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOMAIN=0
RUNNER_DIR="$SCRIPT_DIR/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0"
RUNNER_DLL="Hrot.ClusterRunner.dll"

pids=()

cleanup() {
    echo "Stopping all roles..."
    for pid in "${pids[@]}"; do
        kill "$pid" 2>/dev/null || true
    done
}
trap cleanup INT TERM

# start "SimHost" /d %FOLDER% %RUNNER% -d %DOMAIN% -m simhost --no-wait
(cd "$RUNNER_DIR" && exec dotnet "$RUNNER_DLL" -d "$DOMAIN" -m simhost --no-wait) &
pids+=("$!")

# start "IG"      /d %FOLDER% %RUNNER% -d %DOMAIN% -m ig      --no-wait
(cd "$RUNNER_DIR" && exec dotnet "$RUNNER_DLL" -d "$DOMAIN" -m ig --no-wait) &
pids+=("$!")

# start "ExCon"   /d %FOLDER% %RUNNER% -d %DOMAIN% -m excon   --no-wait
(cd "$RUNNER_DIR" && exec dotnet "$RUNNER_DLL" -d "$DOMAIN" -m excon --no-wait) &
pids+=("$!")

# start "CGF"     /d %FOLDER% %RUNNER% -d %DOMAIN% -m cgf     --no-wait
(cd "$RUNNER_DIR" && exec dotnet "$RUNNER_DLL" -d "$DOMAIN" -m cgf --no-wait) &
pids+=("$!")

#start "SimHost" /d %FOLDER% %RUNNER% -d %DOMAIN% -m simhost --wait-for ig,ios
#start "IG"      /d %FOLDER% %RUNNER% -d %DOMAIN% -m ig      --wait-for simhost,ios
#start "IOS"     /d %FOLDER% %RUNNER% -d %DOMAIN% -m ios     --wait-for simhost,ig

wait "${pids[@]}" || true
