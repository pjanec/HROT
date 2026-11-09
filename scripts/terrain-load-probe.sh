#!/bin/bash
# CE-4363 (L-1) — load a terrain in ONE headless editor and report the load time and the editor's memory.
# Usage: scripts/terrain-load-probe.sh <terrain> [live]
#   creates (once) a blank scenario 'probe-<terrain>' on it through the debug API, loads it in Edit (and Live with 'live'), prints
#   seconds per load, the editor's VmRSS / VmHWM after each, and the status line. Needs the Debug build of Hrot.ClusterRunner.
T=${1:?terrain name}; MODE=${2:-edit}
S=${TMPDIR:-/tmp}; A=http://localhost:8122; NAME=probe-$T
cd /home/user/HROT/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0 || exit 1
HROT_DEBUG_API_PORT=8122 xvfb-run -a dotnet Hrot.ClusterRunner.dll -m editor --no-wait > $S/terrain-probe.log 2>&1 &
for i in $(seq 1 120); do curl -s -m 2 $A/status | grep -q '"canOperate":true' && break; sleep 2; done
PID=$(pgrep -f "^dotnet Hrot.ClusterRunner.dll -m editor" | head -1); echo "editor pid $PID operable"
mem() { awk '/VmRSS|VmHWM/{printf "%s %.2f GB  ", $1, $2/1048576}' /proc/$PID/status; }
echo "before: $(mem)"
t0=$(date +%s.%N)
R=$(curl -s -m 900 -X POST $A/scenario/new -H 'Content-Type: application/json' -d "{\"name\":\"$NAME\",\"terrain\":\"$T\"}")
echo "new: $(echo $R | head -c 300)"
R=$(curl -s -m 900 -X POST $A/scenario/load/edit -H 'Content-Type: application/json' -d "{\"name\":\"$NAME\",\"waitForReady\":true,\"discardUnsaved\":true}")
# the edit call answers once the cluster is OperatingEdit; the terrain keeps loading on the node — wait for its local phase
for i in $(seq 1 3600); do P=$(curl -s -m 5 $A/status | python3 -c "import json,sys;d=json.load(sys.stdin)['data']['load'];print(d['localPhase'],d.get('localStep',''))" 2>/dev/null); [ "${P%% *}" = Ready ] && break; [ $((i % 20)) = 0 ] && echo "  ... $P $(mem)"; sleep 0.5; done
t1=$(date +%s.%N); echo "edit load: $(python3 -c "print(round($t1-$t0,1))") s  $(mem)  $(echo $R | head -c 200)"
if [ "$MODE" = live ]; then
  R=$(curl -s -m 900 -X POST $A/scenario/load/live -H 'Content-Type: application/json' -d "{\"name\":\"$NAME\",\"waitForReady\":true,\"startPaused\":false,\"discardUnsaved\":true}")
  t2=$(date +%s.%N); echo "live load: $(python3 -c "print(round($t2-$t1,1))") s  $(mem)  $(echo $R | head -c 200)"
  sleep 10; echo "live +10 s: $(mem)"
fi
curl -s $A/status | head -c 600; echo
kill $PID; sleep 2
