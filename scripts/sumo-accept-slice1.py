#!/usr/bin/env python3
# Live acceptance, AQ93 docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md (slice 1 §12.4). Editor: HROT_DEBUG_API_PORT=<port> xvfb-run ... ClusterRunner --mode editor; ACCEPT_DISPLAY/ACCEPT_XAUTH for screenshots.
"""AQ93 slice 1 live acceptance (PLAN_Sumo_Slice1_Embedded_Editor.md §3) — the editor, headless, over its debug API.
Usage: accept_slice1.py <port> <out-dir>   (the editor must already be up on <port>)"""
import json, math, os, subprocess, sys, time, urllib.request

PORT = int(sys.argv[1]); OUT = sys.argv[2]
op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
def call(method, path, body=None, timeout=180):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"http://localhost:{PORT}{path}", data=data, method=method, headers={"Content-Type": "application/json"})
    try: return json.loads(op.open(req, timeout=timeout).read())
    except urllib.error.HTTPError as e: return json.loads(e.read() or b"{}")
    except Exception as e: return {"ok": False, "error": str(e)}
def data(r): return (r or {}).get("data")
results = []
def check(name, ok, detail): results.append((name, bool(ok), detail)); print(("PASS " if ok else "FAIL ") + name + " — " + str(detail), flush=True)

def entities():
    rows = data(call("GET", "/entities")) or []
    return rows if isinstance(rows, list) else rows.get("entities", [])
def comps(nid): return (data(call("GET", f"/entities/{nid}")) or {}).get("Components", {})
def pos(nid):
    p = (comps(nid).get("SimTransform") or {}).get("Position"); return p[:2] if p else None
def cars(): return [r["networkId"] for r in entities() if "TrafficCar" in r.get("components", [])]
def bodies():   # an HROT body: SimTransform + PhysicsCollider, not a SUMO car
    return [r["networkId"] for r in entities() if "TrafficCar" not in r.get("components", []) and "PhysicsCollider" in r.get("components", []) and "SimTransform" in r.get("components", [])]
def screenshot(tag):
    disp = os.environ.get("ACCEPT_DISPLAY")
    if not disp: return None
    f = os.path.join(OUT, f"slice1-{tag}.png")
    env = dict(os.environ); xa = os.environ.get("ACCEPT_XAUTH")
    if xa: env["XAUTHORITY"] = xa
    subprocess.run(["import", "-display", disp, "-window", "root", f], capture_output=True, timeout=30, env=env)
    return f if os.path.exists(f) else None

# ── load + run ────────────────────────────────────────────────────────────────────────────
r = call("POST", "/scenario/load/live", {"name": "Recipes/demo-city", "waitForReady": True, "startPaused": False})
check("load Recipes/demo-city live", (r or {}).get("ok"), (r or {}).get("error") or data(r))
call("POST", "/sim/play", {})
time.sleep(20)

# ① roads and lights render — the screenshot (judged by eye) + the road layer's presence
shot = screenshot("roads")
check("① map screenshot taken", shot, shot or "no ACCEPT_DISPLAY / import failed")

# ①b the map gizmo buffer keeps everything drawn (the road layer once starved it — AQ93 §12.4)
g = data(call("GET", "/panels/_gizmo")) or {}
json.dump(g, open(os.path.join(OUT, "gizmo.json"), "w"), indent=1)
check("①b map gizmo buffer drops nothing", g.get("dropped", -1) == 0, {k: g.get(k) for k in list(g)[:8]})

# ② cars exist and move
c0 = cars()
check("② SUMO car replicas exist near our bodies", len(c0) > 0, f"{len(c0)} replicas")
p0 = {n: pos(n) for n in c0[:10]}
time.sleep(3)
p1 = {n: pos(n) for n in c0[:10]}
moved = [n for n in p0 if p0[n] and p1.get(n) and math.dist(p0[n], p1[n]) > 0.5]
stopped = [n for n in p0 if p0[n] and p1.get(n) and math.dist(p0[n], p1[n]) <= 0.05]
check("② cars move", len(moved) > 0, f"{len(moved)}/{len(p0)} moved > 0.5 m in 3 s; {len(stopped)} stood still (lights/queues)")

# ③ pause holds every car still
call("POST", "/sim/pause", {})
time.sleep(1.0)
a = {n: pos(n) for n in cars()[:15]}
time.sleep(3.0)
b = {n: pos(n) for n in a}
drift = max([math.dist(a[n], b[n]) for n in a if a[n] and b.get(n)] or [0])
check("③ pause holds every car still", drift < 0.01, f"max drift {drift:.4f} m over 3 s paused ({len(a)} cars)")
call("POST", "/sim/play", {})
time.sleep(2)

# ④ replicas only within 300 m (+50 hysteresis) of an HROT body
anchors = [pos(n) for n in bodies()]; anchors = [x for x in anchors if x]
far = []
for n in cars():
    p = pos(n)
    if p and anchors and min(math.dist(p, q) for q in anchors) > 350.0: far.append((n, round(min(math.dist(p, q) for q in anchors))))
check("④ no replica beyond 300 m + 50 m of every HROT body", anchors and not far, f"{len(anchors)} anchors; beyond: {far[:5]}")

# ⑤ a raycast from a soldier at a passing car reports the car
hit = None
cs = cars()
for s in bodies()[:3]:
    sp = pos(s)
    for n in sorted(cs, key=lambda n: math.dist(sp, pos(n) or [1e9, 1e9]))[:3]:
        los = data(call("GET", f"/perception/los?observer={s}&target={n}")) or {}
        blockers = [pt.get("blockingEntity") for pt in los.get("points", []) if pt.get("blockingEntity")]
        hit = (s, n, los.get("visible"), los.get("verdict"), blockers[:3])
        if los.get("visible") or blockers: break
    if hit and (hit[2] or hit[4]): break
check("⑤ LOS from a soldier at a nearby car resolves the car", hit and (hit[2] or hit[4]), hit)

# ⑥ removing the traffic entity: roads stay, SUMO idles (replicas go)
traffic = [r["networkId"] for r in entities() if "TrafficDemand" in r.get("components", [])]
r = call("DELETE", f"/entities/{traffic[0]}") if traffic else {"ok": False, "error": "no traffic entity"}
check("⑥a delete the traffic entity", (r or {}).get("ok"), traffic[:1] or r)
time.sleep(5)
left = cars()
check("⑥b SUMO idle: no car replicas left", len(left) == 0, f"{len(left)} replicas")
shot2 = screenshot("after-delete")
check("⑥c map screenshot after delete (roads still drawn — judge by eye)", shot2, shot2)

json.dump([{"check": n, "pass": ok, "detail": str(d)} for n, ok, d in results], open(os.path.join(OUT, "slice1-results.json"), "w"), indent=1)
print(f"\n{sum(ok for _, ok, _ in results)}/{len(results)} passed")
