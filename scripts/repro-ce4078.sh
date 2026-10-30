#!/bin/bash
# CE-4078 repro (AQ91 §4-B3 S0): orchestrator + cgf + simhost + traffic as separate processes under Xvfb, recipes/scenarios/demo-city live,
# then the 3-D map opened on CGF (POST /film/shot); prints /status round-trips, the master's ejections/catch-ups and each node's longest log silences.
# Uses ports 8600-8604 and DDS domain 61; kills only its own nodes. Set HROT_RUNNER_DLL to test another build.
# WITH_IG=1 adds the PLAYER variant (R-1046 / CE-4078): an `ig` node on port 8603 (same domain) loads with the rest; the master's /cluster/roster is sampled every second during the load, and the
# report adds the IG's load-step texts (the 3-D map's "3-D map: terrain" while its map builds), when it reached PlayReady, its /status round-trips and the ejections.
set -u
export DOTNET_ROOT=${DOTNET_ROOT:-/root/.dotnet}; export HROT_RUNNER_DLL=${HROT_RUNNER_DLL:-$(cd "$(dirname "$0")/.." && pwd)/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll}
N=$(cd "$(dirname "$0")" && pwd)/cluster-node.sh; D=61
WITH_IG=${WITH_IG:-0}; PORTS="8600 8601 8602 8604"; [ "$WITH_IG" = 1 ] && PORTS="$PORTS 8603"
for p in $PORTS; do $N kill $p >/dev/null 2>&1; done
# fail fast: a node that does not come up makes the whole run meaningless (and each wait is 120 s)
$N start orchestrator 8600 0 $D || exit 1; $N start cgf 8601 0 $D || exit 1; $N start simhost 8602 0 $D || exit 1; $N start traffic 8604 0 $D || exit 1
[ "$WITH_IG" = 1 ] && { $N start ig 8603 0 $D || exit 1; }
ROSTER=/tmp/ce4078-roster.log; rm -f $ROSTER
if [ "$WITH_IG" = 1 ]; then
  # every ~200 ms: the master's view of the IG (roster state), the IG's /status gate text and every step/loading/3-D-map string in the IG's /diagnostics/architecture (its heartbeat's load step)
  ( while :; do
      r=$(curl -s --noproxy '*' --max-time 5 localhost:8600/cluster/roster | python3 -c 'import sys,json;d=json.load(sys.stdin)["data"]["nodes"];print([n["reportedState"] for n in d if n["nodeId"]==100][0])' 2>/dev/null)
      g=$(curl -s --noproxy '*' --max-time 5 localhost:8603/status | grep -oE '"(canOperate|localPhase|localStep|open|reason|visible|text)":("[^"]*"|null|[a-z]+)' | tr '\n' ' ')   # CE-1451: load.localStep, runGate.reason, loadingScreen.text
      d=$(curl -s --noproxy '*' --max-time 5 localhost:8603/view | grep -oE '"(pending3D|built|step)":("[^"]*"|null|[a-z]+)' | tr '\n' ' ')   # CE-1451: the IG's map build
      echo "$(date +%T.%N | cut -c1-12) igState=${r:-?} $g view: $d" >> $ROSTER; sleep 0.2; done ) &
  SAMPLER=$!
  # the VRAM upload lines carry no timestamp of their own: stamp them as they appear (= when the IG's map meshes were built)
  rm -f /tmp/ce4078-vao.log; ( tail -n0 -F /tmp/n-ig-8603.log 2>/dev/null | grep --line-buffered "Mesh uploaded" | while read -r l; do echo "$(date +%T.%N | cut -c1-12) $l" >> /tmp/ce4078-vao.log; done ) &
  TAILER=$!
fi
echo "== up $(date +%T)"
curl -s --noproxy '*' --max-time 180 -X POST localhost:8600/scenario/load/live -H 'Content-Type: application/json' -d '{"name":"recipes/scenarios/demo-city","waitForReady":true,"startPaused":false}' | cut -c1-200; echo
# the load POST returns before the nodes finish: wait for CGF's own terrain commit, so the shot arrives while its 3-D map is still to be built
for i in $(seq 1 120); do grep -q "Terrain 'demo-city' committed" /tmp/n-cgf-8601.log && break; sleep 1; done
echo "== CGF terrain committed: $(grep "Terrain 'demo-city' committed" /tmp/n-cgf-8601.log | cut -c1-12)"
echo "== open the 3-D map on CGF (film shot) $(date +%T.%N | cut -c1-12)"
t0=$(date +%s.%N); r=$(curl -s --noproxy '*' --max-time 170 -X POST localhost:8601/film/shot -H 'Content-Type: application/json' -d '{"lookAt":[400,400,0],"distance":350,"yawDeg":45,"pitchDeg":-35}'); echo "film/shot took $(echo "$(date +%s.%N) - $t0" | bc | cut -c1-6) s: ${r:0:200}"
curl -s --noproxy '*' --max-time 30 localhost:8601/view | cut -c1-300; echo
# how long until the map is really 3-D (the switch waits in 2-D while the background build runs): poll CGF's /view
tv=$t0; for i in $(seq 1 450); do m=$(curl -s --noproxy '*' --max-time 10 localhost:8601/view | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["mode"])' 2>/dev/null); [ -n "$m" ] && [ "$m" != "2D" ] && break; [ $i = 2 ] && { echo "== /diagnostics/render while pending: $(curl -s --noproxy '*' --max-time 10 localhost:8601/diagnostics/render | grep -oiE '.{0,60}(building|pending|3-D map).{0,80}' | head -3)"; }; sleep 0.2; done
echo "== CGF /view mode '$m' (anything but 2D is a 3-D view: 3D / FPS) after $(echo "$(date +%s.%N) - $t0" | bc | cut -c1-5) s from the film/shot request"
for i in 1 2 3 4 5 6 7 8 9 10 11 12; do t0=$(date +%s.%N); curl -s --noproxy '*' --max-time 60 localhost:8601/status >/dev/null; echo "  status rtt $(echo "$(date +%s.%N) - $t0" | bc | cut -c1-5) s at $(date +%T)"; sleep 4; done
echo "== master log: ejections / catch-ups"; grep -iE 'eject|catching up|catch-up|heartbeat timeout' /tmp/n-orchestrator-8600.log | cut -c1-200 | tail -10
for m in cgf-8601 simhost-8602 traffic-8604; do
  echo "== $m: longest silences in its log (s)"
  python3 - /tmp/n-$m.log <<'PY'
import re,sys,datetime
ts=[]
for l in open(sys.argv[1],errors='ignore'):
    m=re.match(r'(\d\d):(\d\d):(\d\d)\.(\d+)',l)
    if m: ts.append((int(m[1])*3600+int(m[2])*60+int(m[3])+float('0.'+m[4]),l.strip()[:110]))
gaps=sorted(((b[0]-a[0],a[1],b[1]) for a,b in zip(ts,ts[1:])),reverse=True)[:3]
for g in gaps: print(f"  {g[0]:.1f}s after: {g[1]}\n        next: {g[2]}")
PY
done
echo "== entity counts"; for p in 8601 8602; do curl -s --noproxy '*' localhost:$p/entities | python3 -c 'import sys,json;d=json.load(sys.stdin).get("data") or [];d=d if isinstance(d,list) else d.get("entities",[]);print(len(d))'; done
if [ "$WITH_IG" = 1 ]; then
  echo "== PLAYER variant: the IG (node 100) in the master's roster during the load; /status rtts of the IG"
  for i in 1 2 3 4 5; do t0=$(date +%s.%N); curl -s --noproxy '*' --max-time 60 localhost:8603/status >/dev/null; echo "  ig status rtt $(echo "$(date +%s.%N) - $t0" | bc | cut -c1-5) s at $(date +%T)"; sleep 2; done
  kill $SAMPLER $TAILER 2>/dev/null; pkill -f 'tail -n0 -F /tmp/n-ig' 2>/dev/null
  python3 - $ROSTER /tmp/n-orchestrator-8600.log /tmp/n-ig-8603.log <<'PY'
import re,sys,json
roster,mlog,iglog=sys.argv[1:4]
last=None
print("  IG state / gate, changes (time, then the fields):")
for l in open(roster,errors='ignore'):
    t,_,rest=l.strip().partition(' ')
    if rest!=last: print("   ",t,rest[:220]); last=rest
print("== IG map meshes uploaded (stamped when seen; first = map built):")
for l in open('/tmp/ce4078-vao.log',errors='ignore'): print("  ",l.strip()[:120])
print("== ig log: terrain / map / ready lines")
for l in open(iglog,errors='ignore'):
    if re.search(r"Terrain .*committed|3-D map|PlayReady|map build|Map3D|MapBuild",l): print("  ",l.strip()[:200])
print("== master log: IG phase lines")
for l in open(mlog,errors='ignore'):
    if re.search(r"Slave#100|node 100|IG .*(Loading|PlayReady)|3-D map",l): print("  ",l.strip()[:200])
PY
fi
for p in $PORTS; do $N kill $p >/dev/null 2>&1; done
