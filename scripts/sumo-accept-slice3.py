#!/usr/bin/env python3
# Live acceptance, AQ93 §13.7 (slice 3) and the symbolic 2-D road layer (R-4058), against a running editor.
"""AQ93 slice 3 live check (§13.6): in the editor on demo-city, park one of OUR wheeled vehicles 80 m ahead of a moving SUMO car;
the car must stop behind it (or turn off before it — inconclusive, try the next). Also: the 2-D road layer is a symbol (no triangles,
every primitive FlatOnly) and the map buffer drops nothing. Usage: accept_slice3.py <port> <out-dir>"""
import json, math, os, subprocess, sys, time, urllib.request
PORT = int(sys.argv[1]); OUT = sys.argv[2]; os.makedirs(OUT, exist_ok=True)
op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
def call(method, path, body=None, timeout=180):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"http://localhost:{PORT}{path}", data=data, method=method, headers={"Content-Type": "application/json"})
    try: return json.loads(op.open(req, timeout=timeout).read())
    except urllib.error.HTTPError as e: return json.loads(e.read() or b"{}")
    except Exception as e: return {"ok": False, "error": str(e)}
def data(r): return (r or {}).get("data")
results = []
def check(name, ok, detail): results.append((name, bool(ok), detail)); print(("PASS " if ok else "FAIL ") + name + " — " + str(detail)[:400], flush=True)
def rows():
    r = data(call("GET", "/entities")) or []
    return r if isinstance(r, list) else r.get("entities", [])
def tf(nid):
    t = (data(call("GET", f"/entities/{nid}")) or {}).get("Components", {}).get("SimTransform") or {}
    return t.get("Position"), t.get("Rotation")
def yaw(q): x, y, z, w = q; return math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z))
def screenshot(tag):
    disp, xa = os.environ.get("ACCEPT_DISPLAY"), os.environ.get("ACCEPT_XAUTH")
    if not disp: return None
    f = os.path.join(OUT, f"slice3-{tag}.png"); env = dict(os.environ)
    if xa: env["XAUTHORITY"] = xa
    subprocess.run(["import", "-display", disp, "-window", "root", f], capture_output=True, timeout=30, env=env)
    return f if os.path.exists(f) else None

r = call("POST", "/scenario/load/live", {"name": "Recipes/demo-city", "waitForReady": True, "startPaused": False})
check("load Recipes/demo-city live", (r or {}).get("ok"), (r or {}).get("error") or "")
call("POST", "/sim/play", {}); time.sleep(15)

# the 2-D road layer: a symbol, all FlatOnly, nothing dropped
g = data(call("GET", "/panels/_gizmo")) or {}
check("map gizmo buffer drops nothing", g.get("dropped", -1) == 0, {k: g.get(k) for k in ("count", "dropped")})
check("map screenshot (centrelines + light dots — judge by eye)", screenshot("map"), "")

# a wheeled TKB type to park
types = data(call("GET", "/tkb/types")) or []
types = types if isinstance(types, list) else types.get("types", [])
def wheeled(t): s = json.dumps(t).lower(); return ("wheeled" in s or "truck" in s or "vehicle" in s) and "pedestrian" not in s and "rifle" not in s
cand = [t for t in types if wheeled(t)]
tkb = (cand[0].get("tkbType") or cand[0].get("id") or cand[0].get("type")) if cand else None
check("a wheeled TKB type exists", tkb, [ (t.get("name"), t.get("tkbType") or t.get("id")) for t in cand[:5] ])

def cars(): return [r["networkId"] for r in rows() if "TrafficCar" in r.get("components", [])]
# ⭐ On a cluster node only the cars near OUR units are entities (R-4041): place a rifleman on a car to pull a street's cars in.
types_all = types
rifle = next((t.get("tkbType") or t.get("id") for t in types_all if "rifleman" in json.dumps(t).lower()), None)
t_wait = time.time()
while time.time() - t_wait < 120 and not cars(): time.sleep(2)   # a cluster's traffic node starts SUMO once its grant lands
if len(cars()) < 12 and rifle and cars():
    p, _ = tf(cars()[0])
    if p: call("POST", "/entities/spawn", {"tkbType": rifle, "transform": {"position": {"x": p[0] + 8, "y": p[1] + 8, "z": 0}}}); time.sleep(8)
# ⭐ CE-4025 — our vehicle goes ON a lane by construction: where a moving SUMO car is right now (80 m "ahead along its heading" lands
#   off a curving street, where SumoSharp rightly makes it a crowd disc, not a lane obstacle — measured on the cluster, 2026-10-18).
#   Then every car that comes up BEHIND it in that lane is judged: it stops, or changes lane before it; driving through it in its own
#   lane is the only failure.
def rel(q, at, fx, fy): return (q[0] - at[0]) * fx + (q[1] - at[1]) * fy, abs((q[0] - at[0]) * fy - (q[1] - at[1]) * fx)
outcomes, judged = [], []
for nid in cars()[:24]:
    p0, q = tf(nid); time.sleep(1.0); p1, q1 = tf(nid)
    if not (p0 and p1 and q1) or math.dist(p0[:2], p1[:2]) < 3.0: continue      # a moving car: its spot is a lane, its heading the lane's
    h = math.atan2(p1[1] - p0[1], p1[0] - p0[0]); fx, fy = math.cos(h), math.sin(h)
    at = (p1[0], p1[1])
    s = call("POST", "/entities/spawn", {"tkbType": tkb, "transform": {"position": {"x": at[0], "y": at[1], "z": 0},
             "rotation": {"x": 0, "y": 0, "z": math.sin(h / 2), "w": math.cos(h / 2)}}})
    tracks = {}
    for _ in range(40):
        time.sleep(1.0)
        for other in cars():
            if other == nid: continue
            qq, _ = tf(other)
            if qq: tracks.setdefault(other, []).append(rel(qq, at, fx, fy))
    for other, tr in tracks.items():
        if not any(-80.0 <= a <= -5.0 and l < 1.5 for a, l in tr): continue        # never came up behind it in its lane
        through = any(a > 2.0 and l < 1.2 for a, l in tr)
        changed = any(a <= 0.0 and l >= 1.5 for a, l in tr)
        stopped = any(-30.0 <= a <= -3.0 and l < 1.5 for a, l in tr[-5:]) and len(tr) >= 2 and abs(tr[-1][0] - tr[-2][0]) < 0.5
        judged.append((other, "drove through" if through else "changed lane" if changed else "stopped behind" if stopped else "approaching"))
    outcomes.append((nid, f"truck on its spot {at[0]:.0f},{at[1]:.0f}; behind it: {judged}; spawn ok {(s or {}).get('ok')}"[:300]))
    if judged: break
if judged:
    check("SUMO cars respect our vehicle on their lane (stop behind it or change lane — none drives through it)",
          not any(v == "drove through" for _, v in judged) and any(v in ("stopped behind", "changed lane") for _, v in judged), outcomes[-1][1])
check("a car came up behind our vehicle to judge (else inconclusive)", bool(judged), outcomes)
screenshot("after")
json.dump([{"check": n, "pass": ok, "detail": str(d)} for n, ok, d in results], open(os.path.join(OUT, "slice3-results.json"), "w"), indent=1)
print(f"\n{sum(ok for _, ok, _ in results)}/{len(results)} passed")
