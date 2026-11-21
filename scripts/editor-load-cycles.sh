#!/bin/bash
# CE-4131 / CE-3421 — one headless editor, N edit->live->10 s->stop cycles. Prints per cycle: the scenario, sim time reached, entities, the editor RSS.
# Usage: scripts/editor-load-cycles.sh <N> "<scenario> [<scenario> ...]"   (rotates the list). Recorder rate per cycle: TotalFrames/Duration of data/nodes/node-1/exercises/*/node_1.fdp.meta.json
S=${TMPDIR:-/tmp}; N=${1:-12}; SC=${2:-ua-weather-regions}
cd /home/user/HROT/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0
HROT_DEBUG_API_PORT=8121 xvfb-run -a dotnet Hrot.ClusterRunner.dll -m editor --no-wait > $S/x6.log 2>&1 &
A=http://localhost:8121
for i in $(seq 1 120); do curl -s -m 2 $A/status | grep -q '"canOperate":true' && break; sleep 2; done
PID=$(pgrep -f "^dotnet Hrot.ClusterRunner.dll -m editor" | head -1); echo "pid $PID operable"
REC=/home/user/HROT/data
LIST=($SC); for c in $(seq 1 $N); do SCN=${LIST[$(( (c-1) % ${#LIST[@]} ))]}
  curl -s -m 300 -X POST $A/scenario/load/edit -H 'Content-Type: application/json' -d "{\"name\":\"$SCN\",\"waitForReady\":true,\"discardUnsaved\":true}" >/dev/null
  curl -s -m 300 -X POST $A/scenario/load/live -H 'Content-Type: application/json' -d "{\"name\":\"$SCN\",\"waitForReady\":true,\"startPaused\":false,\"discardUnsaved\":true}" > $S/x6.live.json
  t0=$(date +%s.%N); s0=$(curl -s $A/status | python3 -c "import json,sys;print(json.load(sys.stdin)['data']['simTime'])")
  sleep 10
  s1=$(curl -s $A/status | python3 -c "import json,sys;d=json.load(sys.stdin)['data'];print(d['simTime'],d['entityCount'])"); t1=$(date +%s.%N)
  curl -s -m 60 -X POST $A/sim/stop -H 'Content-Type: application/json' -d '{"discardUnsaved":true}' >/dev/null; sleep 2
  META=$(ls -t $(find $REC/nodes/node-1/exercises -name 'node_1.fdp.meta.json' -mmin -2 2>/dev/null) 2>/dev/null | head -1)
  RATE=$( [ -n "$META" ] && python3 -c "import json;m=json.load(open('$META'));import re;d=m['Duration'];h,mi,se=d.split(':');print(round(m['TotalFrames']/(int(h)*3600+int(mi)*60+float(se)),1))" || echo "?")
  RSS=$(grep VmRSS /proc/$PID/status | awk '{print $2}')
  echo "cycle $c $SCN  states/s=$RATE  sim0=$s0 sim1/ent=$s1 wall=$(python3 -c "print(round($t1-$t0,1))")  rssKB=$RSS  live=$(head -c 80 $S/x6.live.json)"
done
kill $PID
