#!/bin/bash
# CE-4078 repro (AQ91 §4-B3 S0): orchestrator + cgf + simhost + traffic as separate processes under Xvfb, Recipes/demo-city live,
# then the 3-D map opened on CGF (POST /film/shot); prints /status round-trips, the master's ejections/catch-ups and each node's longest log silences.
# Uses ports 8600-8604 and DDS domain 61; kills only its own nodes. Set HROT_RUNNER_DLL to test another build.
set -u
export DOTNET_ROOT=${DOTNET_ROOT:-/root/.dotnet}; export HROT_RUNNER_DLL=${HROT_RUNNER_DLL:-$(cd "$(dirname "$0")/.." && pwd)/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll}
N=$(cd "$(dirname "$0")" && pwd)/cluster-node.sh; D=61
for p in 8600 8601 8602 8604; do $N kill $p >/dev/null 2>&1; done
$N start orchestrator 8600 0 $D; $N start cgf 8601 0 $D; $N start simhost 8602 0 $D; $N start traffic 8604 0 $D
echo "== up $(date +%T)"
curl -s --noproxy '*' --max-time 180 -X POST localhost:8600/scenario/load/live -H 'Content-Type: application/json' -d '{"name":"Recipes/demo-city","waitForReady":true,"startPaused":false}' | cut -c1-200; echo
sleep 5
echo "== open the 3-D map on CGF (film shot) $(date +%T.%N | cut -c1-12)"
t0=$(date +%s.%N); r=$(curl -s --noproxy '*' --max-time 170 -X POST localhost:8601/film/shot -H 'Content-Type: application/json' -d '{"lookAt":[400,400,0],"distance":350,"yawDeg":45,"pitchDeg":-35}'); echo "film/shot took $(echo "$(date +%s.%N) - $t0" | bc | cut -c1-6) s: ${r:0:200}"
curl -s --noproxy '*' --max-time 30 localhost:8601/view | cut -c1-300; echo
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
for p in 8600 8601 8602 8604; do $N kill $p >/dev/null 2>&1; done
