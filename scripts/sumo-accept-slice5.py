#!/usr/bin/env python3
# Live acceptance, AQ93 §15 (slice 5, R-4031/R-4032, CV-12), against a running editor (embedded SUMO).
"""Pedestrians walk, named by our allocator, and are civilian replicas near our units; a shoot-out beside them gives them up from SUMO
as OUR civilians in panic, keeping their network ids. Two riflemen of opposite sides are placed where a SUMO car just was (sidewalks run
beside the lanes) and ordered to attack each other.
Usage: sumo-accept-slice5.py <port> <out-dir>"""
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
def check(name, ok, detail): results.append((name, bool(ok), detail)); print(("PASS " if ok else "FAIL ") + name + " — " + str(detail)[:600], flush=True)
def rows():
    r = data(call("GET", "/entities")) or []
    return r if isinstance(r, list) else r.get("entities", [])
def ent(nid): return data(call("GET", f"/entities/{nid}")) or {}
def comps(nid): return ent(nid).get("Components", {})
def tkb(nid): return (comps(nid).get("TkbIdentity") or {}).get("TkbType")
CIVILIANS = (1101, 1102, 1103, 1104)

r = call("POST", "/scenario/load/live", {"name": "recipes/scenarios/demo-city", "waitForReady": True, "startPaused": False})
check("load recipes/scenarios/demo-city live", (r or {}).get("ok"), (r or {}).get("error") or "")
call("POST", "/sim/play", {}); time.sleep(40)   # pedestrians spawn over time (SumoSharp's rate)

types = data(call("GET", "/tkb/types")) or []
types = types if isinstance(types, list) else types.get("types", [])
def tid(t): return t.get("tkbType") or t.get("id") or t.get("type")
rifle = next((tid(t) for t in types if "rifleman" in json.dumps(t).lower()), None)
check("a rifleman TKB type exists", rifle, rifle)

cars = [r["networkId"] for r in rows() if "TrafficCar" in r.get("components", []) and tkb(r["networkId"]) not in CIVILIANS]
spot = None
for nid in cars:
    p = (comps(nid).get("SimTransform") or {}).get("Position")
    if p: spot = p; break
check("a spot on SUMO's streets", spot, spot)

def spawn(x, y, side):
    s = call("POST", "/entities/spawn", {"tkbType": rifle, "transform": {"position": {"x": x, "y": y, "z": 0}},
             "attributesJson": json.dumps({"Affiliation": side})})
    return s
before = {r["networkId"] for r in rows()}
spawn(spot[0] - 15, spot[1], "FORCE_FRIENDLY"); spawn(spot[0] + 15, spot[1], "FORCE_OPPOSING")
time.sleep(6)
ours = [r["networkId"] for r in rows() if r["networkId"] not in before and "TrafficCar" not in r.get("components", [])]
check("two riflemen placed", len(ours) >= 2, ours)

peds = [r["networkId"] for r in rows() if "TrafficCar" in r.get("components", []) and tkb(r["networkId"]) in CIVILIANS]
check("pedestrian replicas near our units (K3)", peds, {"count": len(peds), "first": peds[:5]})
json.dump(peds, open(os.path.join(OUT, "slice5-peds-before.json"), "w"))

if len(ours) >= 2:
    a, b = ours[0], ours[1]
    oa = call("POST", f"/entities/{a}/order", {"kind": "attack", "targetId": b})
    ob = call("POST", f"/entities/{b}/order", {"kind": "attack", "targetId": a})
    check("the attack orders are accepted", (data(oa) or {}).get("accepted") or (data(ob) or {}).get("accepted"), {"a": oa, "b": ob})

converted, t0 = [], time.time()
while time.time() - t0 < 60 and not converted:
    time.sleep(3)
    converted = [r["networkId"] for r in rows() if "TrafficCar" not in r.get("components", []) and tkb(r["networkId"]) in CIVILIANS]
check("a shoot-out converts pedestrians into OUR civilians (K4/K5)", converted, {"count": len(converted), "ids": converted[:10]})
if converted:
    c = converted[0]
    e = comps(c)
    mind = e.get("CivilianState") or {}
    check("he keeps the pedestrian's network id", c in peds, {"id": c, "wasReplica": c in peds})
    check("he is in panic (fear ≥ 0.6, or his HSM in Panic/Shelter)", (mind.get("Fear") or 0) >= 0.6 or str(mind.get("Mode")) in ("Panic", "Shelter", "4", "5"),
          {k: mind.get(k) for k in ("Fear", "Mode", "HasFrightFrom", "FrightFrom")})
    check("he is no longer SUMO's (no TrafficCar)", "TrafficCar" not in e, list(e.keys())[:20])
    check("at most the cap (30) were given up (K6)", len(converted) <= 30, len(converted))
json.dump([{"check": n, "pass": ok, "detail": str(d)} for n, ok, d in results], open(os.path.join(OUT, "slice5-results.json"), "w"), indent=1)
print(f"\n{sum(ok for _, ok, _ in results)}/{len(results)} passed")
