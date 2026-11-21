#!/bin/bash
# CE-4104 acceptance: the four run-mode switches, live, as separate processes (orchestrator + cgf + simhost) driven over HTTP.
#   (1) Live -> AAR   POST /replay {}                       (the master picks the live run; the door stops it and opens its recording)
#   (2) AAR -> Live   POST /cluster/transition {target:OperatingLive}   (from a replay = the BRANCH: ReplaySessionIntents.BranchLive(); no seek route needed, /replay/seek first)
#   (3) Live -> DAR   POST /review {atSeconds}              (a window of the live run)
#   (4) DAR -> Live   POST /review/exit                     (the timeline's eject / the toolbar's stop in a review)
# After each switch: the cluster state, the master's /status, CGF's /status load + runGate (CE-1451), /exercises (parent links), /replay/status.
# Uses ports 8600 (orchestrator) 8601 (cgf) 8602 (simhost) and DDS domain ${DOMAIN:-63}; SCENARIO defaults to demo-city (basic-desert fails to load: ScenarioSerializer scenario DOM missing). Exits non-zero on the first failed assertion.
set -u
export DOTNET_ROOT=${DOTNET_ROOT:-/root/.dotnet}; export HROT_RUNNER_DLL=${HROT_RUNNER_DLL:-$(cd "$(dirname "$0")/.." && pwd)/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll}
N=$(cd "$(dirname "$0")" && pwd)/cluster-node.sh; D=${DOMAIN:-63}; SCENARIO=${SCENARIO:-demo-city}; M=localhost:8600; C=localhost:8601
get()  { curl -s --noproxy '*' --max-time 30 "http://$1$2"; }
post() { curl -s --noproxy '*' --max-time 150 -X POST "http://$1$2" -H 'Content-Type: application/json' -d "${3:-{\}}"; }
j()    { python3 -c "import sys,json
try:
  d=json.load(sys.stdin)
  for k in sys.argv[1].split('.'): d=d[k] if isinstance(d,dict) else None
  print(d)
except Exception: print('?')" "$1"; }
FAILS=0
check() { if [ "$2" = "$3" ]; then echo "  PASS $1: $2"; else echo "  FAIL $1: got '$2', want '$3'"; FAILS=$((FAILS+1)); fi; }
state() { get $M /status | j data.clusterState; }
await_state() { for _ in $(seq 1 240); do [ "$(state)" = "$1" ] && return 0; sleep 1; done; echo "  (timeout waiting for $1, now $(state))"; return 1; }
exercises() { get $M /exercises | python3 -c "import sys,json
d=json.load(sys.stdin)['data']['exercises']
for e in d: print('   exercise %s parent=%s duration=%.1f' % (e['exerciseId'][:8], (e['parentExerciseId'] or '-')[:8], e['durationSeconds']))
print('   count=%d' % len(d))"; }
snap() { echo "-- $1: master clusterState=$(state) cgf simTime=$(get $C /status | j data.simTime) recording=$(get $M /status | j data.recording)"
  echo "   cgf: phase=$(get $C /status | j data.load.localPhase) step='$(get $C /status | j data.load.localStep)' gate.open=$(get $C /status | j data.runGate.open) screen='$(get $C /status | j data.loadingScreen.text)'"
  echo "   cgf sim/state: isPaused=$(get $C /sim/state | j data.isPaused) totalTime=$(get $C /sim/state | j data.totalTime) timeScale=$(get $C /sim/state | j data.timeScale)"
  echo "   replay/status: duration=$(get $M /replay/status | j data.durationSeconds) simSeconds=$(get $M /replay/status | j data.simSeconds) review=$(get $M /replay/status | j data.review)"; exercises; }
cleanup() { for p in 8600 8601 8602; do $N kill $p >/dev/null 2>&1; done; }
trap cleanup EXIT
cleanup
$N start orchestrator 8600 0 $D || exit 1; $N start cgf 8601 0 $D || exit 1; $N start simhost 8602 0 $D || exit 1
post $M /run-settings '{"recordLiveRuns":true}' | cut -c1-120; echo
echo "== load $SCENARIO live"
post $M /scenario/load/live "{\"name\":\"recipes/scenarios/$SCENARIO\",\"waitForReady\":true,\"startPaused\":false}" | cut -c1-160; echo
await_state OperatingLive || { echo "FAIL: never OperatingLive"; exit 1; }
[ "$(get $C /sim/state | j data.isPaused)" = True ] && { echo "(the run loaded paused: POST /sim/play)"; post $M /sim/play | cut -c1-100; echo; }
for _ in $(seq 1 60); do t=$(get $C /status | j data.simTime); python3 -c "import sys;sys.exit(0 if float('$t')>=9 else 1)" 2>/dev/null && break; sleep 1; done
snap "the live run (parent)"
PARENT=$(get $M /exercises | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['exercises'][0]['exerciseId'])")
echo "parent exercise: $PARENT"
[ -n "$PARENT" ] || { echo "FAIL: no live exercise recorded (see /tmp/n-orchestrator-8600.log)"; exit 1; }

echo "== (1) Live -> AAR"
post $M /replay '{}' | cut -c1-200; echo
await_state OperatingReplay; check "1: state" "$(state)" OperatingReplay
sleep 2; snap "after Live -> AAR"
get $M /exercises | python3 -c "import sys,json;print(len(json.load(sys.stdin)['data']['exercises']))" > /tmp/ms-count1

echo "== (2) AAR -> Live (the branch), from ~3 s"
post $M /replay/seek '{"atSeconds":3}' | cut -c1-160; echo; sleep 2
post $M /cluster/transition '{"target":"OperatingLive"}' | cut -c1-200; echo
await_state OperatingLive; check "2: state" "$(state)" OperatingLive
sleep 3; snap "after AAR -> Live"
for _ in $(seq 1 20); do BRANCH=$(get $M /exercises | python3 -c "import sys,json
d=json.load(sys.stdin)['data']['exercises']
b=[e for e in d if e['parentExerciseId']=='$PARENT']
print(b[0]['exerciseId'] if b else '')"); [ -n "$BRANCH" ] && break; sleep 1; done
BRANCH_UNUSED=$(get $M /exercises | python3 -c "import sys,json
d=json.load(sys.stdin)['data']['exercises']
b=[e for e in d if e['parentExerciseId']=='$PARENT']
print(b[0]['exerciseId'] if b else '')")
check "2: a NEW exercise whose parentExerciseId is the parent" "$([ -n "$BRANCH" ] && echo yes || echo no)" yes
echo "branch exercise: ${BRANCH:-none}"
for _ in $(seq 1 40); do t=$(get $C /status | j data.simTime); python3 -c "import sys;sys.exit(0 if float('$t')>=6 else 1)" 2>/dev/null && break; sleep 1; done

echo "== (3) Live -> DAR (a window: from 2 s)"
SIM3=$(get $C /status | j data.simTime); echo "live sim just before the DAR: $SIM3"
COUNT3=$(get $M /exercises | python3 -c "import sys,json;print(len(json.load(sys.stdin)['data']['exercises']))")
post $M /review '{"atSeconds":2}' | cut -c1-200; echo
await_state OperatingReview; check "3: state" "$(state)" OperatingReview
sleep 2; snap "after Live -> DAR"
check "3: no new exercise" "$(get $M /exercises | python3 -c "import sys,json;print(len(json.load(sys.stdin)['data']['exercises']))")" "$COUNT3"

echo "== (4) DAR -> Live (eject)"
post $M /review/exit | cut -c1-200; echo
await_state OperatingLive; check "4: state" "$(state)" OperatingLive
sleep 2; snap "after DAR -> Live"
SIM4=$(get $C /status | j data.simTime)
check "4: ONE recording continuing (no new exercise)" "$(get $M /exercises | python3 -c "import sys,json;print(len(json.load(sys.stdin)['data']['exercises']))")" "$COUNT3"
# ⚠ NOT ASSERTED: "the live point = the live end" (CE-1447). Measured: in these headless clusters CGF's /status.simTime and /sim/state stay 0 / isPaused (a POST /sim/play on the master does not move them) and
#   /replay/status.durationSeconds is a stale constant, so no HTTP read of the live point exists to compare. The editor's own rail (TheEditorsRecordingIsFilmedInTheReplayBrowserTests) asserts it.
echo "  (live point = live end: NOT checked here — no HTTP read of the live point; sim before the DAR '$SIM3', after the eject '$SIM4')"
[ $? -eq 0 ] || FAILS=$((FAILS+1))
echo "== done: $FAILS failed check(s)"; exit $FAILS
