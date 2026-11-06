#!/usr/bin/env python3
"""The FULL multiplayer player cycle as ONE scripted acceptance — HANDOFF_Coordinator_To_Backend_Multiplayer_Server_And_Full_Cycle.md item (3).

    server up -> P1 joins, deploys into a placed unit, drives it -> P2 joins LATE, sees it moving, is refused P1's unit, takes the Rifleman role
    -> P2 dies, respawns -> P1 leaves by Esc -> P1 quits (slot disposed, S1-d) -> P1 rejoins and deploys -> P2 quits -> the server keeps running
    -> final check: no orphan slot / ghost instance on the wire (the CE-4000 / CE-4007 late-joiner check, via ddsmonitor when it is installed).

Plain Python 3, standard library only; runs UNCHANGED on Windows and Linux (subprocess.Popen, terminate/kill — no killpg, no xvfb-run).
Over the cluster's debug HTTP API (docs/RUNBOOK_Cluster_Debugging_Over_Http.md): the host name is `localhost`, never 127.0.0.1.

    python scripts/multiplayer-cycle.py                       # starts server + p1 + p2 itself (ports 8140/8141/8142, DDS domain 73)
    python scripts/multiplayer-cycle.py --attach --server-port 8140 --p1-port 8141 --p2-port 8142 --staging <FDP_STAGING_ROOT of the server>
                                                              # processes already running (the .bat files): only the HTTP steps run; the
                                                              # steps that quit/rejoin a player ask YOU to close / reopen it (Enter), or are skipped without a terminal

Prints PASS / FAIL per step with the reason and a summary; the exit code is 0 only if every step passed.
"""
import argparse, glob, json, math, os, shutil, subprocess, sys, tempfile, threading, time, urllib.error, urllib.request

OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))
RESULTS = []          # (step, ok, text)
PROCS = {}            # name -> Popen
LOGS = {}


# ───────────────────────────── HTTP ─────────────────────────────

def http(port, method, path, body=None, timeout=20):
    """-> parsed JSON (an HTTP error body is parsed too); {"ok": False, "error": ...} when the node does not answer."""
    data = json.dumps(body if body is not None else {}).encode() if method in ("POST", "DELETE", "PUT") else None
    req = urllib.request.Request(f"http://localhost:{port}{path}", data=data, method=method, headers={"Content-Type": "application/json"})
    try:
        with OPENER.open(req, timeout=timeout) as r:
            return json.loads(r.read() or b"{}")
    except urllib.error.HTTPError as e:
        try: return json.loads(e.read() or b"{}")
        except ValueError: return {"ok": False, "error": f"HTTP {e.code}"}
    except (urllib.error.URLError, ConnectionError, OSError, ValueError) as e:
        return {"ok": False, "error": f"unreachable: {e}"}


def data_of(r):
    d = r.get("data") if isinstance(r, dict) else None
    return d if d is not None else {}


def wait_for(fn, timeout, interval=0.5):
    """Polls fn() until it returns a truthy value (returned) or the bound passes (None)."""
    end = time.time() + timeout
    while time.time() < end:
        try:
            v = fn()
            if v: return v
        except Exception:
            pass
        time.sleep(interval)
    return None


def can_operate(port):
    d = data_of(http(port, "GET", "/status"))
    return bool(d.get("canOperate")) if isinstance(d, dict) else False


def deploy(port): return data_of(http(port, "GET", "/deploy"))
def state_of(port): return deploy(port).get("state")


def position(port, unit):
    d = data_of(http(port, "GET", f"/entities/{unit}/state"))
    p = d.get("position")
    if isinstance(p, dict) and "x" in p and "y" in p: return (float(p["x"]), float(p["y"]), float(p.get("z", 0)))
    return tuple(float(x) for x in p) if isinstance(p, (list, tuple)) and len(p) >= 2 else None


def dist(a, b): return math.dist(a, b) if a and b else 0.0


# ───────────────────────────── reporting ─────────────────────────────

def step(n, title):
    print(f"\n== step {n}: {title}", flush=True)
    return n


def report(n, ok, text):
    RESULTS.append((n, ok, text))
    print(f"{'PASS' if ok else 'FAIL'}  step {n}: {text}", flush=True)
    return ok


def skip(n, text):
    RESULTS.append((n, None, text))
    print(f"SKIP  step {n}: {text}", flush=True)


# ───────────────────────────── processes ─────────────────────────────

def find_runner(arg):
    if arg: return arg
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    for cfg in ("Debug", "Release"):
        p = os.path.join(root, "Hrot", "Runner", "Hrot.ClusterRunner", "bin", cfg, "net8.0", "Hrot.ClusterRunner.dll")
        if os.path.exists(p): return p
    raise SystemExit("Hrot.ClusterRunner.dll not found — build the runner or pass --runner-dll")


XVFB = []   # a private Xvfb started once when a Linux box has no DISPLAY (never on Windows)


def display_env(env):
    if os.name != "nt" and not env.get("DISPLAY") and not XVFB and shutil.which("Xvfb"):
        num = ":97"
        XVFB.append(subprocess.Popen(["Xvfb", num, "-screen", "0", "1280x800x24"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
        time.sleep(1.5)
        os.environ["DISPLAY"] = num
    if XVFB: env["DISPLAY"] = os.environ["DISPLAY"]
    return env


def start(name, args, port, a):
    env = display_env(dict(os.environ))
    env["HROT_DEBUG_API_PORT"] = str(port)
    env["FDP_STAGING_ROOT"] = a.staging
    dll = find_runner(a.runner_dll)
    log = open(os.path.join(a.logdir, f"cycle-{name}.log"), "ab")
    LOGS[name] = log.name
    PROCS[name] = subprocess.Popen([shutil.which("dotnet") or "dotnet", dll] + args + ["-d", str(a.domain)], cwd=os.path.dirname(dll), env=env,
                                   stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)


def stop(name):
    p = PROCS.pop(name, None)
    if p is None: return
    p.terminate()
    try: p.wait(15)
    except subprocess.TimeoutExpired:
        p.kill(); p.wait(10)


def stop_all():
    for n in list(PROCS): stop(n)
    for x in XVFB:
        x.terminate()


def ask_human(msg):
    """--attach: the run cannot kill/start a process it did not start — ask, when there is a terminal."""
    if not sys.stdin or not sys.stdin.isatty(): return False
    input(f"   >>> {msg}  (then press Enter) ")
    return True


# ───────────────────────────── the scenario (the S2 recipe) ─────────────────────────────

def write_scenario(staging, root):
    """s2-respawn = scenarios/bc-house-defended + a blue SpawnArea, a 'Rifleman' DeployRole (max 1), a PLACED Playable unit (Blue Rifleman 1) and GameplaySettings.RespawnDelaySeconds 5."""
    d = json.load(open(os.path.join(root, "scenarios", "bc-house-defended", "scenario.json"), encoding="utf-8"))
    d["header"].setdefault("World", {})["GameplaySettings"] = {"RespawnDelaySeconds": 5}
    d["entities"]["5a000000-0000-4000-8000-000000000001"] = {
        "EditablePolyline": {"Points": [{"X": -8, "Y": -8}, {"X": -8, "Y": 8}, {"X": 8, "Y": 8}, {"X": 8, "Y": -8}], "Version": 0},
        "SimTransform": {"Position": [10, 70, 0], "Rotation": [0, 0, 0, 1]}, "TkbIdentity": {"TkbType": 8814},
        "EntityInfo": {"Name": "Blue spawn", "ForceId": "Friend"}, "SpawnArea": {"Side": "Friend", "FacingDeg": 90}}
    d["entities"]["5a000000-0000-4000-8000-000000000002"] = {
        "SimTransform": {"Position": [0, 60, 0], "Rotation": [0, 0, 0, 1]}, "TkbIdentity": {"TkbType": 8815},
        "EntityInfo": {"Name": "Rifleman role", "ForceId": "Friend"},
        "DeployRole": {"Side": "Friend", "PersonTkbType": 2002, "Name": "Rifleman", "MaxCount": 1}}
    # a PLACED unit a player may deploy into (Player Join D-9: the Playable component on a scenario unit)
    blue1 = d["entities"]["bc000005-0000-4000-8000-000000000003"]
    blue1["Playable"] = {"Side": "Friend", "SlotName": "Blue 1"}
    blue1["SimTransform"]["Position"] = [20, 75, 0]   # in the open, next to the spawn area (the authored spot is inside the house: a man there cannot walk)
    out = os.path.join(staging, "shared", "scenarios", "s2-respawn")
    os.makedirs(out, exist_ok=True)
    json.dump(d, open(os.path.join(out, "scenario.json"), "w", encoding="utf-8"), indent=1)


# ───────────────────────────── a player driving a unit ─────────────────────────────

class Driver(threading.Thread):
    """Posts rising control frames to one unit on one node until stopped (what a held key does)."""
    def __init__(self, port, unit):
        # seq from 1 000 000: the runner's own 3-D station sends frames too, and newest seq wins (RUNBOOK 6.1)
        super().__init__(daemon=True); self.port, self.unit, self.seq, self.go, self.last = port, unit, 1_000_000, True, None
    def run(self):
        while self.go:
            self.seq += 1
            self.last = http(self.port, "POST", f"/entities/{self.unit}/control", {"seq": self.seq, "forward": 1.0, "gait": 1, "yaw": 0.0}, timeout=10)
            time.sleep(0.1)
    def halt(self): self.go = False; self.join(3)


# ───────────────────────────── the late-joiner wire check (CE-4000 / CE-4007) ─────────────────────────────

def ghost_check(a, server_port, left_stations):
    """ddsmonitor as a LATE joiner: any durable instance still ALIVE for an entity the server no longer has, or a PlayerSlot of a station that left."""
    tools = os.path.join(os.path.expanduser("~"), ".dotnet", "tools")
    env = dict(os.environ); env["PATH"] = os.environ.get("PATH", "") + os.pathsep + tools
    exe = shutil.which("ddsmonitor", path=env["PATH"])
    if not exe: return None, "ddsmonitor is not on PATH (dotnet tool install -g cyclonedds.net.ddsmonitor) — wire check skipped"
    if "DOTNET_ROOT" not in env and shutil.which("dotnet"): env["DOTNET_ROOT"] = os.path.dirname(os.path.realpath(shutil.which("dotnet")))
    out = os.path.join(a.logdir, "cycle-wire.jsonl"); summ = os.path.join(a.logdir, "cycle-wire.summary.json")
    for f in (out, summ):
        if os.path.exists(f): os.remove(f)
    runner_dir = os.path.dirname(find_runner(a.runner_dll))
    cmd = [exe, "--DdsSettings:HeadlessMode=Record", f"--DdsSettings:DomainId={a.domain}", f"--AppSettings:TopicSources:0={runner_dir}",
           "--AppSettings:ExcludeTopics:0=*DebugPrimitives*", "--AppSettings:ExcludeTopics:1=*Heartbeat*", "--AppSettings:ExcludeTopics:2=*TimeSync*",
           f"--DdsSettings:HeadlessFilePath={out}", f"--DdsSettings:RecordSummaryFile={summ}", "--DdsSettings:RecordDurationSeconds=12"]
    subprocess.run(cmd, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=90)
    if not os.path.exists(out): return None, "ddsmonitor wrote no capture"
    live = set()
    ents = data_of(http(server_port, "GET", "/entities"))
    for e in (ents if isinstance(ents, list) else ents.get("entities", [])):
        v = e.get("networkId", e.get("NetworkId"))
        if isinstance(v, int): live.add(v)
    last = {}   # (topic, key) -> (state, payload)
    payload_has_station = {}
    for line in open(out, encoding="utf-8", errors="replace"):
        try: r = json.loads(line)
        except ValueError: continue
        p = r.get("Payload") or {}
        if "EntityId" in p: key = (r["TopicName"], p["EntityId"])
        elif "StationNodeId" in p: key = (r["TopicName"], p["StationNodeId"]); payload_has_station[key] = True
        elif "ParentNetworkId" in p: key = (r["TopicName"], (p["ParentNetworkId"], p.get("LocalChildIndex", 0)))
        else: continue
        last[key] = (r.get("InstanceState", ""), p)
    ghosts = []
    for (topic, key), (state, _p) in last.items():
        if state != "Alive": continue
        if payload_has_station.get((topic, key)):
            if key in left_stations: ghosts.append(f"{topic.split('_')[-1]} of station {key} (which left) is still ALIVE")
            continue
        if isinstance(_p.get("StationNodeId"), int) and _p["StationNodeId"] in left_stations and _p.get("Held", 0):
            ghosts.append(f"{topic.split('_')[-1]} of entity {key} is still HELD by station {_p['StationNodeId']}, which left")
        ent = key[0] if isinstance(key, tuple) else key
        if isinstance(ent, int) and ent not in live and ent != 0:
            ghosts.append(f"{topic.split('_')[-1]} entity {ent} is ALIVE on the wire but the server has no such entity")
    return ghosts, f"{len(last)} durable instances read, server has {len(live)} entities"


# ───────────────────────────── the cycle ─────────────────────────────

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--attach", action="store_true", help="use already running processes (ports below); do not start or kill them")
    ap.add_argument("--server-port", type=int, default=8140); ap.add_argument("--p1-port", type=int, default=8141); ap.add_argument("--p2-port", type=int, default=8142)
    ap.add_argument("--domain", type=int, default=73, help="DDS domain (-d)")
    ap.add_argument("--runner-dll", help="Hrot.ClusterRunner.dll (default: the repo's bin/Debug|Release)")
    ap.add_argument("--staging", help="FDP_STAGING_ROOT shared by the processes (default: a fresh temp dir; REQUIRED with --attach for the scenario to be found)")
    ap.add_argument("--logdir", default=tempfile.gettempdir())
    a = ap.parse_args()
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    own_staging = not a.staging
    if own_staging:
        a.staging = tempfile.mkdtemp(prefix="hrot-cycle-")
    os.makedirs(a.logdir, exist_ok=True)   # a fresh --logdir is created, not refused
    S, P1, P2 = a.server_port, a.p1_port, a.p2_port
    t0 = time.time()
    try:
        run(a, root, S, P1, P2)
    except KeyboardInterrupt:
        report("abort", False, "interrupted")
    finally:
        stop_all()
        if own_staging: shutil.rmtree(a.staging, ignore_errors=True)
    print("\n== summary")
    for n, ok, text in RESULTS:
        print(f"  {'PASS' if ok else 'SKIP' if ok is None else 'FAIL'}  step {n}: {text}")
    failed = [r for r in RESULTS if r[1] is False]
    print(f"{len(RESULTS) - len(failed)} of {len(RESULTS)} steps passed/skipped, {len(failed)} FAILED  ({time.time() - t0:.0f} s)")
    if failed: print("logs:", ", ".join(LOGS.values()))
    sys.exit(1 if failed else 0)


def run(a, root, S, P1, P2):
    attach = a.attach
    p1_station2 = None
    # 1 ── server up, scenario loaded live
    n = step(1, "server up (-m server), scenario s2-respawn loaded live")
    if not attach:
        write_scenario(a.staging, root)
        start("server", ["-m", "server"], S, a)
    if not wait_for(lambda: can_operate(S), 240, 2):
        return report(n, False, "the server never reported canOperate within 240 s (see the server log)")
    r = http(S, "POST", "/scenario/load/live", {"name": "s2-respawn", "waitForReady": True, "startPaused": False}, timeout=240)
    if not r.get("ok", True) or r.get("error"):
        return report(n, False, f"POST /scenario/load/live refused: {r.get('error') or r}")
    report(n, True, "canOperate; s2-respawn loaded live (Rifleman role present, respawn delay 5 s)")

    # 2 ── P1 joins, deploys into a placed unit
    n = step(2, "P1 joins: Choosing -> deploys into a PLACED unit -> Playing")
    if not attach: start("p1", ["-m", "player", "--node-name", "p1", "--no-wait"], P1, a)
    if not wait_for(lambda: state_of(P1) == "Choosing", 150, 1):
        return report(n, False, f"P1 never reached Choosing (state={state_of(P1)!r}): {json.dumps(deploy(P1))[:300]}")
    offers = deploy(P1).get("offers", [])
    placed = [o for o in offers if o.get("kind") == "Placed" and o.get("side") == "Friend" and o.get("holderStation", 0) == 0]
    if not placed:
        return report(n, False, f"no free PLACED Friend offer for P1; offers: {json.dumps(offers)[:400]}")
    unit = placed[0]["unitNetId"]
    http(P1, "POST", "/deploy", {"side": "Friend", "unitNetId": unit})
    if not wait_for(lambda: state_of(P1) == "Playing", 30, 0.5):
        d = deploy(P1); return report(n, False, f"P1 did not reach Playing on unit {unit}: state={d.get('state')} lastRefusal={d.get('lastRefusal')!r}")
    d = deploy(P1); p1_station = d.get("station"); slot_unit = (d.get("slot") or {}).get("unit")
    if slot_unit != unit:
        return report(n, False, f"P1 is Playing but on unit {slot_unit}, not the placed unit {unit}")
    report(n, True, f"P1 (station {p1_station}) Playing on placed unit {unit} ({placed[0].get('slotName')})")

    # 3 ── P1 drives it
    n = step(3, "P1 drives the unit: control frames move it on the server")
    before = position(S, unit)
    drv = Driver(P1, unit); drv.start(); time.sleep(5)
    after = position(S, unit); moved = dist(before, after)
    last = drv.last or {}
    if before is None or after is None:
        drv.halt(); return report(n, False, f"cannot read the unit's position on the server (before={before}, after={after})")
    if moved < 0.5:
        drv.halt(); return report(n, False, f"the unit did not move on the server ({before} -> {after}); last control answer: {json.dumps(last)[:200]}")
    report(n, True, f"unit {unit} moved {moved:.1f} m on the server ({before} -> {after})")

    # 4 ── P2 joins late
    n = step(4, "P2 joins LATE: sees the unit moving, is refused P1's unit, takes the Rifleman role")
    if not attach: start("p2", ["-m", "player", "--node-name", "p2", "--no-wait"], P2, a)
    if not wait_for(lambda: state_of(P2) == "Choosing", 150, 1):
        drv.halt(); return report(n, False, f"P2 never reached Choosing (state={state_of(P2)!r})")
    # the replica of P1's unit exists on P2 and moves: two reads apart differ
    seen = wait_for(lambda: position(P2, unit), 40, 1)
    if seen is None:
        drv.halt(); return report(n, False, f"P2 has no state for P1's unit {unit} (the late joiner never received it)")
    time.sleep(1.5)
    seen2 = position(P2, unit)
    if dist(seen, seen2) < 0.1:
        drv.halt(); return report(n, False, f"P2 sees P1's unit standing still ({seen} -> {seen2}) while P1 drives it")
    http(P2, "POST", "/deploy", {"side": "Friend", "unitNetId": unit})
    refusal = wait_for(lambda: deploy(P2).get("lastRefusal"), 15, 0.5)
    holder_named = bool(refusal) and (str(p1_station) in refusal or "station" in refusal.lower() or "taken" in refusal.lower() or "holder" in refusal.lower() or "p1" in refusal.lower())
    if not refusal or state_of(P2) == "Playing":
        drv.halt(); return report(n, False, f"P2 was NOT refused P1's unit (state={state_of(P2)}, lastRefusal={refusal!r})")
    role = [o for o in deploy(P2).get("offers", []) if o.get("slotName") == "Rifleman" and o.get("kind") == "Role"]
    if not role:
        drv.halt(); return report(n, False, f"P2 sees no Rifleman role offer: {json.dumps(deploy(P2).get('offers'))[:300]}")
    http(P2, "POST", "/deploy", {"side": "Friend", "unitNetId": role[0]["unitNetId"]})
    if not wait_for(lambda: state_of(P2) == "Playing", 30, 0.5):
        d = deploy(P2); drv.halt(); return report(n, False, f"P2 did not reach Playing on the Rifleman role: state={d.get('state')} lastRefusal={d.get('lastRefusal')!r}")
    p2_station = deploy(P2).get("station"); man = (deploy(P2).get("slot") or {}).get("unit")
    report(n, True, f"P2 saw the unit move ({dist(seen, seen2):.1f} m), was refused it ('{refusal}'{'' if holder_named else ' — WARN: refusal does not name the holder'}), then Playing as the Rifleman (unit {man}, station {p2_station})")

    # 5 ── P2 dies
    n = step(5, "P2 dies (Health 0 on the server): Dead, then Choosing after the respawn delay")
    killed = None
    for pv in ("SimHost", "Scenario", "CGF"):
        http(S, "POST", "/perspective", {"name": pv})
        rr = http(S, "POST", f"/entities/{man}/component", {"componentType": "Health", "patch": {"Current": 0}})
        if rr.get("ok") is True or (rr.get("ok") is None and not rr.get("error")): killed = pv; break
    if not killed:
        drv.halt(); return report(n, False, f"could not set Health 0 on unit {man} from any perspective; last answer: {json.dumps(rr)[:250]}")
    if not wait_for(lambda: state_of(P2) == "Dead", 20, 0.3):
        d = deploy(P2); drv.halt(); return report(n, False, f"P2 never went Dead after Health 0 (via {killed}): state={d.get('state')} reason={d.get('reason')!r}")
    d = deploy(P2); dead_reason = d.get("reason"); t1 = time.time()
    if not wait_for(lambda: state_of(P2) == "Choosing", 30, 0.3):
        drv.halt(); return report(n, False, f"P2 stayed {state_of(P2)} 30 s after dying (respawn delay is 5 s)")
    report(n, True, f"P2 Dead ('{dead_reason}') then Choosing after {time.time() - t1:.1f} s")

    # 6 ── P1 leaves by Esc
    n = step(6, "P1 leaves by Esc (DELETE /entities/{unit}/possess): back to Choosing, the unit is free again")
    drv.halt()
    r = http(P1, "DELETE", f"/entities/{unit}/possess")
    if r.get("ok") is False or r.get("error"):
        return report(n, False, f"Esc (unpossess) refused: {json.dumps(r)[:250]}")
    if not wait_for(lambda: state_of(P1) == "Choosing", 20, 0.5):
        d = deploy(P1); return report(n, False, f"P1 stayed {d.get('state')} after Esc (reason={d.get('reason')!r}, lastRefusal={d.get('lastRefusal')!r}); the unit's reservation is kept")
    def unit_free():
        o = [x for x in deploy(P2).get("offers", []) if x.get("unitNetId") == unit]
        return o and o[0].get("holderStation", 0) == 0
    if not wait_for(unit_free, 20, 0.5):
        o = [x for x in deploy(P2).get("offers", []) if x.get("unitNetId") == unit]
        return report(n, False, f"P1 is Choosing but P2 still sees the unit held: {json.dumps(o)[:250]}")
    report(n, True, "P1 Choosing; the unit shows holderStation 0 on P2")

    # 7 ── P1 quits
    n = step(7, "P1 quits (process ends): its slot is disposed on the server (S1-d)")
    # P1 takes the unit again first, so the quit has something to release
    http(P1, "POST", "/deploy", {"side": "Friend", "unitNetId": unit})
    wait_for(lambda: state_of(P1) == "Playing", 30, 0.5)
    wait_for(lambda: [x for x in deploy(P2).get("offers", []) if x.get("unitNetId") == unit and x.get("holderStation", 0) != 0], 20, 0.5)   # P2 sees it held first
    if attach:
        if not ask_human("close player P1 now"):
            skip(n, "--attach without a terminal: cannot quit P1"); skip(8, "needs step 7"); p1_gone = False
        else: p1_gone = True
    else:
        stop("p1"); p1_gone = True
    if p1_gone:
        def released():
            o = [x for x in deploy(P2).get("offers", []) if x.get("unitNetId") == unit]
            return o and o[0].get("holderStation", 0) == 0
        if not wait_for(released, 40, 1):
            o = [x for x in deploy(P2).get("offers", []) if x.get("unitNetId") == unit]
            return report(n, False, f"P1 quit while Playing but its unit is still held after 40 s (a leaked slot/reservation): {json.dumps(o)[:250]}")
        report(n, True, f"P1 quit while Playing; the unit's reservation was released within the bound (station {p1_station} no longer holds {unit})")

        # 8 ── P1 rejoins
        n = step(8, "P1 rejoins (started again) and deploys again: Playing")
        if attach:
            ask_human("start player P1 again (same port)")
        else:
            start("p1", ["-m", "player", "--node-name", "p1", "--no-wait"], P1, a)
        if not wait_for(lambda: state_of(P1) == "Choosing", 150, 1):
            return report(n, False, f"the rejoined P1 never reached Choosing (state={state_of(P1)!r})")
        free = [o for o in deploy(P1).get("offers", []) if o.get("kind") == "Placed" and o.get("side") == "Friend" and o.get("holderStation", 0) == 0]
        if not free:
            return report(n, False, f"the rejoined P1 sees no free placed unit: {json.dumps(deploy(P1).get('offers'))[:300]}")
        http(P1, "POST", "/deploy", {"side": "Friend", "unitNetId": free[0]["unitNetId"]})
        if not wait_for(lambda: state_of(P1) == "Playing", 30, 0.5):
            d = deploy(P1); return report(n, False, f"the rejoined P1 did not reach Playing: state={d.get('state')} lastRefusal={d.get('lastRefusal')!r}")
        p1_station2 = deploy(P1).get("station")
        report(n, True, f"P1 Playing again on unit {free[0]['unitNetId']} (station {p1_station2})")

    # 9 ── P2 quits
    n = step(9, "P2 quits: the server keeps running")
    if attach:
        if not ask_human("close player P2 now"): skip(n, "--attach without a terminal: cannot quit P2")
        else: pass
    else:
        stop("p2")
    time.sleep(3)
    if not can_operate(S):
        return report(n, False, f"the server does not answer / cannot operate after P2 quit: {json.dumps(http(S, 'GET', '/status'))[:250]}")
    report(n, True, "GET /status still answers, canOperate")

    # 10 ── the wire
    n = step(10, "final check: no orphan slot / ghost instance on the wire (late-joiner capture)")
    if not attach: stop("p1")   # P1's second life ends too: nothing of either player may outlive them
    time.sleep(3)
    left = {x for x in (p1_station, p1_station2, p2_station) if x is not None}
    ghosts, note = ghost_check(a, S, left)
    if ghosts is None:
        skip(n, "WARN " + note)
    elif ghosts:
        report(n, False, f"{len(ghosts)} ghost/orphan instance(s): " + "; ".join(ghosts[:8]) + f"  ({note})")
    else:
        report(n, True, "no durable instance ALIVE for a vanished entity or a departed station  (" + note + ")")


if __name__ == "__main__":
    main()
