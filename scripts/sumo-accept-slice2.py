#!/usr/bin/env python3
# Live acceptance, AQ93 §12.5 (slice 2): five processes via scripts/cluster-node.sh (orchestrator 8300, cgf 8301, simhost 8302, ig 8303, traffic 8304), demo-city loaded from the IG port.
"""AQ93 slice 2 live acceptance — five processes (orchestrator 8300, CGF 8301, SimHost 8302, IG 8303, traffic 8304), demo-city
loaded from the IG port. Usage: accept_slice2.py <out-dir>   (the cluster must already be up, scripts/cluster-node.sh)"""
import json, math, os, statistics, subprocess, sys, time, urllib.request, concurrent.futures
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
MIRRORS = {8301: "CGF", 8302: "SimHost", 8303: "IG"}
TRAFFIC = 8304
op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
def call(port, method, path, body=None, timeout=60):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"http://localhost:{port}{path}", data=data, method=method, headers={"Content-Type": "application/json"})
    try: return json.loads(op.open(req, timeout=timeout).read())
    except urllib.error.HTTPError as e: return json.loads(e.read() or b"{}")
    except Exception as e: return {"ok": False, "error": str(e)}
def data(r): return (r or {}).get("data")
results = []
def check(name, ok, detail): results.append((name, bool(ok), detail)); print(("PASS " if ok else "FAIL ") + name + " — " + str(detail)[:400], flush=True)

def rows(port):
    r = data(call(port, "GET", "/entities")) or []
    return r if isinstance(r, list) else r.get("entities", [])
def cars(port): return sorted(r["networkId"] for r in rows(port) if "TrafficCar" in r.get("components", []))
def comps(port, nid): return (data(call(port, "GET", f"/entities/{nid}")) or {}).get("Components", {})
def pos(port, nid):
    p = (comps(port, nid).get("SimTransform") or {}).get("Position"); return p[:2] if p else None
def bodies(port):
    return [r["networkId"] for r in rows(port) if "TrafficCar" not in r.get("components", []) and "SimTransform" in r.get("components", [])
            and "PhysicsCollider" in r.get("components", [])]
def at_once(nid):
    with concurrent.futures.ThreadPoolExecutor(len(MIRRORS)) as ex:
        return dict(zip(MIRRORS, ex.map(lambda p: pos(p, nid), MIRRORS)))

# ⓪ every node live
st = {p: (data(call(p, "GET", "/status")) or {}) for p in [8300, 8301, 8302, 8303, 8304]}
json.dump(st, open(os.path.join(OUT, "status.json"), "w"), indent=1)
live = {p: json.dumps(s).count("OperatingLive") > 0 for p, s in st.items()}
check("⓪ every node OperatingLive", all(live.values()), live)

# ① the same cars on every mirror, one id, owned by the traffic node (SUMO starts once the grant lands: wait for them)
t_wait = time.time()
while time.time() - t_wait < 120:
    sets = {MIRRORS[p]: cars(p) for p in MIRRORS}
    if all(sets.values()): break
    time.sleep(2)
check("①0 every mirror has cars within 120 s", all(sets.values()), f"after {time.time() - t_wait:.0f} s: " + str({k: len(v) for k, v in sets.items()}))
common = set.intersection(*(set(v) for v in sets.values())) if all(sets.values()) else set()
check("① car replicas on CGF, SimHost and IG", all(len(v) > 0 for v in sets.values()), {k: len(v) for k, v in sets.items()} | {"common": len(common)})
owners = {}
for nid in list(common)[:5]:
    for p in MIRRORS:
        owners.setdefault(nid, {})[MIRRORS[p]] = (comps(p, nid).get("NetworkAuthority") or {}).get("PrimaryOwnerId")
gone = [n for n, v in owners.items() if all(x is None for x in v.values())]   # left the vicinity between the listing and the query
for n in gone: owners.pop(n)
one_owner = all(len(set(v.values())) == 1 and None not in v.values() for v in owners.values()) and owners
check("① each car has ONE primary owner on every node (the traffic node)", one_owner, {"owners": owners, "gone meanwhile": gone})

# ② cars move (IG)
sample = list(common)[:10]
p0 = {n: pos(8303, n) for n in sample}; time.sleep(3); p1 = {n: pos(8303, n) for n in sample}
moved = [n for n in sample if p0[n] and p1[n] and math.dist(p0[n], p1[n]) > 0.5]
check("② cars move on IG", moved, f"{len(moved)}/{len(sample)} moved > 0.5 m in 3 s")

# ③ the nodes agree on a car's pose (CE-4017) — simultaneous reads, median spread
spreads = []
for _ in range(8):
    for n in sample[:6]:
        r = [v for v in at_once(n).values() if v]
        if len(r) == len(MIRRORS): spreads.append(max(math.dist(a, b) for a in r for b in r))
    time.sleep(0.3)
med = statistics.median(spreads) if spreads else None
check("③ pose spread across CGF/SimHost/IG: median < 2 m", med is not None and med < 2.0,
      f"{len(spreads)} samples, median {med if med is None else round(med, 2)} m, p90 {round(sorted(spreads)[int(0.9*(len(spreads)-1))], 2) if spreads else None} m")

# ④ pause holds every mirror still
call(8303, "POST", "/sim/pause", {}); time.sleep(1.5)
a = {(p, n): pos(p, n) for p in MIRRORS for n in sample[:5]}; time.sleep(3)
b = {k: pos(*k) for k in a}
drifts = {f"{MIRRORS[k[0]]}:{k[1]}": round(math.dist(a[k], b[k]), 3) for k in a if a[k] and b[k]}
drift = max(drifts.values() or [0])
paused = {MIRRORS[p]: (data(call(p, "GET", "/sim/state")) or {}).get("isPaused") for p in MIRRORS}
check("④ pause (from IG) holds every car still on every mirror", drift < 0.01,
      f"max drift {drift:.4f} m over 3 s; moved: {[k for k, v in drifts.items() if v >= 0.01]}; isPaused: {paused}")
call(8303, "POST", "/sim/play", {}); time.sleep(2)

# ⑤ the map gizmo buffers drop nothing
for p in [8301, 8302, 8303]:
    g = data(call(p, "GET", "/panels/_gizmo")) or {}
    check(f"⑤ {MIRRORS[p]} map gizmo buffer drops nothing", g.get("dropped", -1) == 0, {k: g.get(k) for k in ("count", "dropped")})

# ⑥ SimHost sees a car by LOS
hit = None
cs = cars(8302)
for s in bodies(8302)[:3]:
    sp = pos(8302, s)
    if not sp: continue
    for n in sorted(cs, key=lambda n: math.dist(sp, pos(8302, n) or [1e9, 1e9]))[:3]:
        los = data(call(8302, "GET", f"/perception/los?observer={s}&target={n}")) or {}
        hit = (s, n, los.get("visible"), los.get("verdict"))
        if los.get("visible"): break
    if hit and hit[2]: break
check("⑥ SimHost LOS from a soldier at a nearby car resolves the car", hit and hit[2], hit)

# ⑦ the traffic node crashes: what the mirrors do (E10: traffic simply stops)
subprocess.run(["scripts/cluster-node.sh", "kill", str(TRAFFIC)], cwd=os.path.dirname(os.path.dirname(os.path.abspath(__file__))), capture_output=True)
t0 = time.time(); time.sleep(9)
after = {MIRRORS[p]: cars(p) for p in MIRRORS}
q0 = {n: pos(8303, n) for n in after["IG"][:6]}; time.sleep(3); q1 = {n: pos(8303, n) for n in after["IG"][:6]}
still_moving = [n for n in q0 if q0[n] and q1.get(n) and math.dist(q0[n], q1[n]) > 0.5]
alive = {p: bool(data(call(p, "GET", "/status"))) for p in MIRRORS}
check("⑦a after the traffic node dies every other node stays up", all(alive.values()), alive)
check("⑦b after the traffic node dies no car keeps moving", not still_moving, f"{len(still_moving)} still moving")
check("⑦c after the traffic node dies the replicas are cleared (E10)", all(len(v) == 0 for v in after.values()),
      {k: len(v) for k, v in after.items()} | {"seconds": round(time.time() - t0, 1)})

json.dump([{"check": n, "pass": ok, "detail": str(d)} for n, ok, d in results], open(os.path.join(OUT, "slice2-results.json"), "w"), indent=1)
print(f"\n{sum(ok for _, ok, _ in results)}/{len(results)} passed")
