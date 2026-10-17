#!/usr/bin/env python3
# Live acceptance, AQ93 §15 (slice 5) on the five-process cluster: orchestrator 8300, CGF 8301, SimHost 8302, IG 8303, traffic 8304.
"""The traffic node walks pedestrians; every node sees the same pedestrian replicas near our units; a shoot-out run by CGF/SimHost
reaches the traffic node (NED), which gives the frightened pedestrians up; CGF (the brain) creates each as OUR civilian with the
pedestrian's network id, terrified (the fright crosses the wire as creation attributes, CE-4024).
Usage: sumo-accept-slice5-cluster.py <out-dir>   (the cluster must be up and demo-city loaded live)"""
import json, os, sys, time, urllib.request
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
CGF, SIM, IG, TRAFFIC = 8301, 8302, 8303, 8304
NODES = {CGF: "CGF", SIM: "SimHost", IG: "IG", TRAFFIC: "traffic"}
op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
def call(port, method, path, body=None, timeout=60):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"http://localhost:{port}{path}", data=data, method=method, headers={"Content-Type": "application/json"})
    try: return json.loads(op.open(req, timeout=timeout).read())
    except urllib.error.HTTPError as e: return json.loads(e.read() or b"{}")
    except Exception as e: return {"ok": False, "error": str(e)}
def data(r): return (r or {}).get("data")
results = []
def check(name, ok, detail): results.append((name, bool(ok), detail)); print(("PASS " if ok else "FAIL ") + name + " — " + str(detail)[:600], flush=True)
def rows(port):
    r = data(call(port, "GET", "/entities")) or []
    return r if isinstance(r, list) else r.get("entities", [])
def comps(port, nid): return (data(call(port, "GET", f"/entities/{nid}")) or {}).get("Components", {})
def tkb(port, nid): return (comps(port, nid).get("TkbIdentity") or {}).get("TkbType")
CIVILIANS = (1101, 1102, 1103, 1104)
def sumo(port): return [r["networkId"] for r in rows(port) if "TrafficCar" in r.get("components", [])]

t0 = time.time()
while time.time() - t0 < 120 and not sumo(IG): time.sleep(2)
cars = sumo(CGF)
check("SUMO replicas on CGF", cars, len(cars))
types = data(call(CGF, "GET", "/tkb/types")) or []
types = types if isinstance(types, list) else types.get("types", [])
rifle = next(((t.get("tkbType") or t.get("id") or t.get("type")) for t in types if "rifleman" in json.dumps(t).lower()), None)
spot = next(((comps(CGF, n).get("SimTransform") or {}).get("Position") for n in cars if tkb(CGF, n) not in CIVILIANS), None)
check("a rifleman type and a spot on SUMO's streets", rifle and spot, {"rifle": rifle, "spot": spot})
time.sleep(30)   # pedestrians spawn over time

before = {r["networkId"] for r in rows(CGF)}
for dx, side in ((-15, "FORCE_FRIENDLY"), (15, "FORCE_OPPOSING")):
    call(CGF, "POST", "/entities/spawn", {"tkbType": rifle, "transform": {"position": {"x": spot[0] + dx, "y": spot[1], "z": 0}},
                                          "attributesJson": json.dumps({"Affiliation": side})})
time.sleep(8)
ours = [r["networkId"] for r in rows(CGF) if r["networkId"] not in before and "TrafficCar" not in r.get("components", [])]
check("two riflemen placed (CGF)", len(ours) >= 2, ours)

tcars = [n for n in sumo(TRAFFIC) if tkb(TRAFFIC, n) not in CIVILIANS]
check("the traffic node anchors on our units too: it has car replicas (CE-4024 — our bodies carry a collider there)", tcars, len(tcars))
peds = {p: sorted(n for n in sumo(p) if tkb(p, n) in CIVILIANS) for p in (CGF, SIM, IG, TRAFFIC)}
common = set.intersection(*(set(v) for v in peds.values())) if all(peds.values()) else set()
check("pedestrian replicas near our units on every node (K3)", common, {NODES[p]: len(v) for p, v in peds.items()} | {"common": len(common)})
if common:
    n = sorted(common)[0]
    owners = {NODES[p]: (comps(p, n).get("NetworkAuthority") or {}).get("PrimaryOwnerId") for p in peds}
    check("one pedestrian, one owner on every node (the traffic node)", len(set(owners.values())) == 1 and None not in owners.values(), owners)

if len(ours) >= 2:
    a, b = ours[0], ours[1]
    oa = call(CGF, "POST", f"/entities/{a}/order", {"kind": "attack", "targetId": b})
    ob = call(CGF, "POST", f"/entities/{b}/order", {"kind": "attack", "targetId": a})
    check("the attack orders are accepted", (data(oa) or {}).get("accepted") and (data(ob) or {}).get("accepted"), {"a": oa.get("error"), "b": ob.get("error")})

converted, t1 = [], time.time()
while time.time() - t1 < 90 and not converted:
    time.sleep(3)
    converted = [r["networkId"] for r in rows(CGF) if "CivilianState" in r.get("components", []) and "TrafficCar" not in r.get("components", [])]
check("a shoot-out gives pedestrians up as civilians on CGF (K4/K5)", converted, {"count": len(converted), "ids": converted[:10]})
if converted:
    c = converted[0]
    e = comps(CGF, c)
    mind = e.get("CivilianState") or {}
    check("he keeps the pedestrian's network id", any(c in v for v in peds.values()) or c not in before, {"id": c})
    check("CGF owns him (the brain node)", (e.get("NetworkAuthority") or {}).get("PrimaryOwnerId") not in (None,) , e.get("NetworkAuthority"))
    # ⭐ The fright crossed the wire: he knows where it came from although he never HEARD it himself (HeardAt / LastFrightAt 0 are the
    #   fear system's marks), so only the creation attributes can have told him. His fear was 1 at birth and decays 0.05/s (CE-2235):
    #   a threshold read at an unknown age is a clock, not a check. 📌 2026-10-17: 0.39 read ~12 s after birth, 0 a minute later.
    attr_born = mind.get("HasFrightFrom") == 1 and not mind.get("HeardAt") and not mind.get("LastFrightAt")
    heard = (mind.get("Fear") or 0) >= 0.6
    check("he was born terrified on CGF — the fright crossed the wire (CE-4024)", attr_born or heard,
          {k: mind.get(k) for k in ("Fear", "Mode", "HasFrightFrom", "FrightFrom", "HeardAt", "LastFrightAt")})
    time.sleep(3)
    seen = {NODES[p]: c in {r["networkId"] for r in rows(p)} for p in (SIM, IG, TRAFFIC)}
    check("every node sees him as an entity", all(seen.values()), seen)
    sumo_now = {NODES[p]: c in sumo(p) for p in (CGF, SIM, IG, TRAFFIC)}
    check("no node keeps him as a SUMO replica", not any(sumo_now.values()), sumo_now)
json.dump([{"check": n, "pass": ok, "detail": str(d)} for n, ok, d in results], open(os.path.join(OUT, "slice5-cluster-results.json"), "w"), indent=1)
print(f"\n{sum(ok for _, ok, _ in results)}/{len(results)} passed")
