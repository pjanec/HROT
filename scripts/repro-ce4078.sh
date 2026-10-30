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
  ( while :; do echo "$(date +%T.%N | cut -c1-12) $(curl -s --noproxy '*' --max-time 5 localhost:8600/cluster/roster | tr -d '\n')" >> $ROSTER; sleep 1; done ) &
  SAMPLER=$!
fi
echo "== up $(date +%T)"
curl -s --noproxy '*' --max-time 180 -X POST localhost:8600/scenario/load/live -H 'Content-Type: application/json' -d '{"name":"recipes/scenarios/demo-city","waitForReady":true,"startPaused":false}' | cut -c1-200; echo
[ -n "${SAMPLER:-}" ] && kill $SAMPLER 2>/dev/null
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
if [ "$WITH_IG" = 1 ]; then
  echo "== PLAYER variant: the IG (node 100) in the master's roster during the load; /status rtts of the IG"
  for i in 1 2 3 4 5; do t0=$(date +%s.%N); curl -s --noproxy '*' --max-time 60 localhost:8603/status >/dev/null; echo "  ig status rtt $(echo "$(date +%s.%N) - $t0" | bc | cut -c1-5) s at $(date +%T)"; sleep 2; done
  python3 - $ROSTER /tmp/n-orchestrator-8600.log /tmp/n-ig-8603.log <<'PY'
import re,sys,json
roster,mlog,iglog=sys.argv[1:4]
last=None; rows=[]
def walk(o,out):
    if isinstance(o,dict):
        if o.get("nodeId") in (100,) or o.get("id") in (100,): out.append(o)
        for v in o.values(): walk(v,out)
    elif isinstance(o,list):
        for v in o: walk(v,out)
for l in open(roster,errors='ignore'):
    t,_,body=l.partition(' ')
    try: d=json.loads(body)
    except Exception: continue
    found=[]; walk(d,found)
    for f in found:
        key={k:v for k,v in f.items() if re.search('step|phase|state|ready|load|status',k,re.I)}
        if key!=last: rows.append((t,key)); last=key
print("  IG roster entry, changes (time, step/phase/state fields):")
for t,k in rows[:40]: print("   ",t,json.dumps(k)[:200])
print("== ig log: terrain / map / ready lines")
for l in open(iglog,errors='ignore'):
    if re.search(r"Terrain .*committed|3-D map|PlayReady|map build|Map3D|MapBuild",l): print("  ",l.strip()[:200])
print("== master log: IG phase lines")
for l in open(mlog,errors='ignore'):
    if re.search(r"Slave#100|node 100|IG .*(Loading|PlayReady)|3-D map",l): print("  ",l.strip()[:200])
PY
fi
for p in $PORTS; do $N kill $p >/dev/null 2>&1; done
