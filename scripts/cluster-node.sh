#!/bin/bash
# Launch / probe / stop ONE cluster node process each — the shape the membership use cases need
# (docs/DESIGN_Cluster_Membership_Use_Cases.md). Wraps RUNBOOK_Cluster_Debugging_Over_Http.md §1.2/§1.3.
#
#   scripts/cluster-node.sh start <mode> <port> [base-node-id|auto] [domain]   # mode: orchestrator|cgf|simhost|traffic|player (= ig)|server|all; auto = ask the master (C1)
#   scripts/cluster-node.sh status <port>                                  # GET /status of that node
#   scripts/cluster-node.sh get <port> <route>                             # GET any debug route
#   scripts/cluster-node.sh post <port> <route> [json]                     # POST (body defaults to {})
#   scripts/cluster-node.sh kill <port>                                    # SIGKILL one node (a crash)
#   scripts/cluster-node.sh stop-all                                       # every node + Xvfb
#
# A node's id is <base-node-id> + the subsystem offset (Program.cs ResolveAppNodeId: simhost 1, ig 100,
# orchestrator 300, cgf 400, excon 200, traffic 800; "player" is the IG node). Logs: /tmp/n-<mode>-<port>.log. PIDs: /tmp/n-<port>.pid.
set -u
DLL=${HROT_RUNNER_DLL:-/home/user/HROT/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll}
curl_() { curl -s --noproxy '*' --max-time 20 "$@"; }

case "${1:-}" in
  start)
    mode=$2; port=$3; base=${4:-0}; domain=${5:-0}
    log=/tmp/n-$mode-$port.log
    # ⛔ setsid + nohup: a plain & dies with the calling shell (runbook §1.3).
    HROT_DEBUG_API_PORT=$port setsid nohup xvfb-run -a --server-args="-screen 0 1280x800x24" \
      dotnet "$DLL" --mode "$mode" --no-wait $( [ "$base" = auto ] || echo "-n $base" ) -d "$domain" > "$log" 2>&1 < /dev/null &
    echo $! > /tmp/n-$port.pid
    # wait for the debug API (no fixed sleep): poll /status until it answers or 120 s pass
    for _ in $(seq 1 120); do
      if curl_ "http://localhost:$port/status" | grep -q '"ok"'; then echo "up: $mode port=$port base=$base domain=$domain"; exit 0; fi
      sleep 1
    done
    echo "NOT UP after 120 s: $mode port=$port — see $log"; exit 1 ;;
  status) curl_ "http://localhost:$2/status"; echo ;;
  get)    curl_ "http://localhost:$2$3"; echo ;;
  post)   curl_ -X POST "http://localhost:$2$3" -H 'Content-Type: application/json' -d "${4:-{\}}"; echo ;;
  kill)
    # the xvfb-run wrapper's child is the dotnet process; kill the whole session (setsid made it a group).
    # ⭐ CE-4029 — find EVERY node serving this port by its own environment (HROT_DEBUG_API_PORT), not only the pid file: a later
    #   `start` of the same port overwrites the file and orphaned the earlier node (measured: ten processes survived a full kill).
    sids=""
    pid=$(cat /tmp/n-$2.pid 2>/dev/null) && sids="$(ps -o sid= -p "$pid" 2>/dev/null | tr -d ' ')"
    for p in $(pgrep -f 'ClusterRunner[.]dll'); do
      tr '\0' '\n' < /proc/$p/environ 2>/dev/null | grep -qx "HROT_DEBUG_API_PORT=$2" && sids="$sids $(ps -o sid= -p "$p" | tr -d ' ')"
    done
    for sid in $sids; do [ -n "$sid" ] && pkill -KILL -s "$sid" 2>/dev/null; done
    rm -f /tmp/n-$2.pid
    for _ in $(seq 1 20); do
      left=$(for p in $(pgrep -f 'ClusterRunner[.]dll'); do tr '\0' '\n' < /proc/$p/environ 2>/dev/null | grep -qx "HROT_DEBUG_API_PORT=$2" && echo $p; done)
      [ -z "$left" ] && break; sleep 0.5
    done
    if [ -n "$left" ]; then echo "STILL RUNNING on port $2: $left"; exit 1; fi
    echo "killed node on port $2" ;;
  stop-all)
    pkill -f 'ClusterRunner[.]dll'; sleep 1; pkill -9 -f 'ClusterRunner[.]dll'; pkill -f '[X]vfb'
    rm -f /tmp/n-*.pid; echo "stopped" ;;
  *) sed -n 2,13p "$0"; exit 2 ;;
esac
