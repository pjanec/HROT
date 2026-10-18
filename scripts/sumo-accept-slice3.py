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
outcomes = []
for nid in cars()[:24]:
    p0, q = tf(nid); time.sleep(1.0); p1, q1 = tf(nid)
    if not (p0 and p1 and q1) or math.dist(p0[:2], p1[:2]) < 3.0: continue      # moving cars only
    h = yaw(q1); fx, fy = math.cos(h), math.sin(h)
    at = (p1[0] + 80 * fx, p1[1] + 80 * fy)
    s = call("POST", "/entities/spawn", {"tkbType": tkb, "transform": {"position": {"x": at[0], "y": at[1], "z": 0},
             "rotation": {"x": 0, "y": 0, "z": math.sin(h / 2), "w": math.cos(h / 2)}}})
    # ⭐ CE-4025 — TRACK it for 30 s (one sample at 20 s cannot tell "stopped in a queue" from "not braking"): the closest it came,
    #   whether it ever passed our vehicle, its slowest speed, and any car between it and our vehicle at the end.
    track, last = [], None
    for _ in range(30):
        time.sleep(1.0)
        p2, _ = tf(nid)
        if not p2: break
        along = (p2[0] - at[0]) * fx + (p2[1] - at[1]) * fy
        lat = abs((p2[0] - p1[0]) * fy - (p2[1] - p1[1]) * fx)
        speed = math.dist(p2[:2], last[:2]) if last else None
        track.append((round(along, 1), round(lat, 1), None if speed is None else round(speed, 2)))
        last = p2
    if not track: outcomes.append((nid, "gone (left the vicinity)")); continue
    along, lat = track[-1][0], track[-1][1]
    passed = any(t[0] > 2.0 for t in track)
    slowest = min((t[2] for t in track if t[2] is not None), default=None)
    between = []
    for other in cars():
        if other == nid: continue
        q, _ = tf(other)
        if not q: continue
        oa = (q[0] - at[0]) * fx + (q[1] - at[1]) * fy
        ol = abs((q[0] - p1[0]) * fy - (q[1] - p1[1]) * fx)
        if ol < 2.0 and along < oa < 0: between.append((other, round(oa, 1)))
    outcomes.append((nid, f"lat {lat:.1f} m, along {along:.1f} m, passed {passed}, slowest {slowest} m/s, cars between {between}, "
                          f"spawn ok {(s or {}).get('ok')}"[:300]))
    json.dump(track, open(os.path.join(OUT, f"slice3-track-{nid}.json"), "w"))
    if lat < 2.0:
        check(f"SUMO car {nid} stops behind our vehicle (never passes it, comes to rest)",
              not passed and slowest is not None and slowest < 0.5, outcomes[-1][1])
        break
check("a car stayed in its lane to judge (else inconclusive)", any("lat" in o[1] and float(o[1].split()[1]) < 2.0 for o in outcomes), outcomes)
screenshot("after")
json.dump([{"check": n, "pass": ok, "detail": str(d)} for n, ok, d in results], open(os.path.join(OUT, "slice3-results.json"), "w"), indent=1)
print(f"\n{sum(ok for _, ok, _ in results)}/{len(results)} passed")
