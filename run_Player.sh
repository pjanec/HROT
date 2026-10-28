#!/usr/bin/env bash
# A player: a client joining a networked game (run_Server.sh, or run_All.sh on the same domain).
# No -n: the cluster master assigns the node id (scripts/cluster-node.sh calls this "auto").
# Usage: run_Player.sh [DOMAIN]     (DDS domain, default 0)
set -euo pipefail

# Resolve the directory this script lives in, so it works from any CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOMAIN="${1:-0}"
RUNNER_DIR="$SCRIPT_DIR/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0"
RUNNER_DLL="Hrot.ClusterRunner.dll"

cd "$RUNNER_DIR"

nohup dotnet "$RUNNER_DLL" -d "$DOMAIN" -m player --no-wait &
disown
