#!/usr/bin/env python3
"""CE-3029 live check (docs/designs/navig-2/Navigation_Design_v2_0.md §5.2b): the Bradley (1000) of tt-nav-los drives its
planned path past test-town's Block A (the square (40,230)-(90,280)); this samples its position over the cluster debug HTTP
API and reports the closest its CENTRE came to the building. The Vehicle navmesh is baked at 1.8 m (§8.2), so a path that
holds its plan keeps the centre >= ~1.8 m from the wall; before CE-3029 the corners were cut to 0.16-0.7 m.

Launch ClusterRunner --mode all with HROT_DEBUG_API_PORT=8111 first, then:
    python3 scripts/corner-clearance-check.py [seconds=90] [port=8111]
Prints the closest approach, where it happened, and PASS when it is >= 1.5 m (the hull's half-width is 1.8 m: the remainder
is the follower's lag, Navigation v2 §5.2b C7).
"""
import json, sys, time, urllib.request

seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 90.0
port = int(sys.argv[2]) if len(sys.argv) > 2 else 8111
B = f"http://localhost:{port}"
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
BLOCK_A = (40.0, 230.0, 90.0, 280.0)
BRADLEY = 1000
PASS_AT = 1.5


def call(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(B + path, data=data, method=method, headers={"Content-Type": "application/json"})
    try:
        with opener.open(req, timeout=30) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        return {"http": e.code, "body": e.read().decode()[:400]}


def distance_to_box(x, y, box):
    x0, y0, x1, y1 = box
    dx = max(x0 - x, 0.0, x - x1)
    dy = max(y0 - y, 0.0, y - y1)
    return (dx * dx + dy * dy) ** 0.5


print("load:", (call("POST", "/scenario/load/live", {"name": "tt-nav-los", "waitForReady": True}).get("data") or {}).get("loaded"))
call("POST", "/sim/play", {})
call("POST", "/perspective", {"name": "SimHost"})
closest, where, samples, last = 1e9, None, 0, None
deadline = time.time() + seconds
while time.time() < deadline:
    e = (call("GET", f"/entities/{BRADLEY}").get("data") or {}).get("Components", {})
    pos = (e.get("SimTransform") or {}).get("Position")
    if pos:
        samples += 1
        d = distance_to_box(pos[0], pos[1], BLOCK_A)
        if d < closest:
            closest, where = d, pos
        last = pos
    time.sleep(0.2)
print(f"samples {samples}, last position {last}")
print(f"closest to Block A: {closest:.2f} m at {where}")
print("PASS" if closest >= PASS_AT else "FAIL")
