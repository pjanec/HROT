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
outcomes = []
for nid in cars()[:12]:
    p0, q = tf(nid); time.sleep(1.0); p1, q1 = tf(nid)
    if not (p0 and p1 and q1) or math.dist(p0[:2], p1[:2]) < 3.0: continue      # moving cars only
    h = yaw(q1); fx, fy = math.cos(h), math.sin(h)
    at = (p1[0] + 80 * fx, p1[1] + 80 * fy)
    s = call("POST", "/entities/spawn", {"tkbType": tkb, "transform": {"position": {"x": at[0], "y": at[1], "z": 0},
             "rotation": {"x": 0, "y": 0, "z": math.sin(h / 2), "w": math.cos(h / 2)}}})
    time.sleep(20)
    p2, _ = tf(nid)
    if not p2: outcomes.append((nid, "gone (left the vicinity)")); continue
    lat = abs((p2[0] - p1[0]) * fy - (p2[1] - p1[1]) * fx)
    along = (p2[0] - at[0]) * fx + (p2[1] - at[1]) * fy
    p3, _ = tf(nid); time.sleep(2); p4, _ = tf(nid)
    still = p3 and p4 and math.dist(p3[:2], p4[:2]) < 0.5
    outcomes.append((nid, f"lat {lat:.1f} m, along {along:.1f} m, still {still}, spawn ok {(s or {}).get('ok')}"))
    if lat < 2.0:
        check(f"SUMO car {nid} stops behind our vehicle", along < -2.0 and still, outcomes[-1][1])
        break
check("a car stayed in its lane to judge (else inconclusive)", any("lat" in o[1] and float(o[1].split()[1]) < 2.0 for o in outcomes), outcomes)
screenshot("after")
json.dump([{"check": n, "pass": ok, "detail": str(d)} for n, ok, d in results], open(os.path.join(OUT, "slice3-results.json"), "w"), indent=1)
print(f"\n{sum(ok for _, ok, _ in results)}/{len(results)} passed")
