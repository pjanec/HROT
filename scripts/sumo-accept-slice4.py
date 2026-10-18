#!/usr/bin/env python3
# Live acceptance, AQ93 §14 (slice 4, R-4023), against a running editor (embedded SUMO).
"""Hand one of OUR vehicles to SUMO and take it back: spawn a BoxTruck on a lane (where a SUMO car just was, facing its way), order a
move 'in traffic' (roadUse InTraffic) to where another SUMO car is, and watch it follow its shadow there; its owner never changes.
Usage: sumo-accept-slice4.py <port> <out-dir>"""
import json, math, os, sys, time, urllib.request
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
def check(name, ok, detail): results.append((name, bool(ok), detail)); print(("PASS " if ok else "FAIL ") + name + " — " + str(detail)[:500], flush=True)
def rows():
    r = data(call("GET", "/entities")) or []
    return r if isinstance(r, list) else r.get("entities", [])
def comps(nid): return (data(call("GET", f"/entities/{nid}")) or {}).get("Components", {})
def tf(nid):
    t = comps(nid).get("SimTransform") or {}
    return t.get("Position"), t.get("Rotation")
def yaw(q): x, y, z, w = q; return math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z))

r = call("POST", "/scenario/load/live", {"name": "Recipes/demo-city", "waitForReady": True, "startPaused": False})
check("load Recipes/demo-city live", (r or {}).get("ok"), (r or {}).get("error") or "")
call("POST", "/sim/play", {}); time.sleep(15)

cars = [r["networkId"] for r in rows() if "TrafficCar" in r.get("components", [])]
start = None
for nid in cars:
    p0, _ = tf(nid); time.sleep(1.0); p1, q1 = tf(nid)
    if p0 and p1 and q1 and math.dist(p0[:2], p1[:2]) > 3.0: start = (p0, yaw(q1)); break
dest = None
for nid in cars:
    p, _ = tf(nid)
    if start and p and 150 < math.dist(p[:2], start[0][:2]) < 600: dest = p; break
check("a start lane and a destination from SUMO's own cars", start and dest, {"start": start, "dest": dest})

h = start[1]
s = call("POST", "/entities/spawn", {"tkbType": 1053, "transform": {"position": {"x": start[0][0], "y": start[0][1], "z": 0},
         "rotation": {"x": 0, "y": 0, "z": math.sin(h / 2), "w": math.cos(h / 2)}}})
time.sleep(3)
truck = max((r["networkId"] for r in rows() if "TrafficCar" not in r.get("components", []) and r.get("networkId", 0) not in cars
             and "VehicleParams" in r.get("components", [])), default=None)
owner0 = (comps(truck).get("NetworkAuthority") or {}).get("PrimaryOwnerId") if truck else None
check("our BoxTruck spawned", truck, {"spawn": s, "truck": truck, "owner": owner0})

o = call("POST", f"/entities/{truck}/order", {"kind": "move", "x": dest[0], "y": dest[1], "roadUse": "InTraffic"})
check("the order 'move in traffic' is accepted", (o or {}).get("ok") and (data(o) or {}).get("accepted"), o)

# ⚠ a standstill is NOT the end: the shadow waits at red lights and in queues — wait for the ORDER to finish (measured: 82 s)
track, t0 = [], time.time()
while time.time() - t0 < 240:
    p, _ = tf(truck)
    if p: track.append((round(time.time() - t0, 1), round(p[0], 1), round(p[1], 1)))
    if (data(call("GET", f"/entities/{truck}/order")) or {}).get("state") not in ("running", None): break
    time.sleep(2)
moved = math.dist(track[-1][1:], track[0][1:]) if track else 0
end_gap = math.dist(track[-1][1:], dest[:2]) if track else 1e9
json.dump(track, open(os.path.join(OUT, "slice4-track.json"), "w"))
check("the truck drove on SUMO's roads", moved > 50, f"moved {moved:.0f} m in {track[-1][0] if track else 0} s")
check("it stopped at the destination (within 25 m)", end_gap < 25, f"{end_gap:.1f} m from the destination")
st = data(call("GET", f"/entities/{truck}/order")) or {}
check("the order ended (taken back)", json.dumps(st).lower().count("succe") > 0 or json.dumps(st).lower().count("arrived") > 0, st)
owner1 = (comps(truck).get("NetworkAuthority") or {}).get("PrimaryOwnerId")
check("ownership never moved", owner1 == owner0, {"before": owner0, "after": owner1})
json.dump([{"check": n, "pass": ok, "detail": str(d)} for n, ok, d in results], open(os.path.join(OUT, "slice4-results.json"), "w"), indent=1)
print(f"\n{sum(ok for _, ok, _ in results)}/{len(results)} passed")
