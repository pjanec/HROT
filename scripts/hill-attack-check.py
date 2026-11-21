#!/usr/bin/env python3
"""Hill-attack live acceptance over the cluster debug HTTP API (hill-attack DESIGN §5.2: hostiles eliminated,
commander finishes). Launch ClusterRunner --mode all with HROT_DEBUG_API_PORT=8111 first, then:
    python3 scripts/hill-attack-check.py hill-attack-close 1000
Pass = "commander finished by t=…", hostiles 1006/1007 at Health 0, tanks 1001-1004 back near the baseline.
Measured 2026-10-02 (after ownership S8): hill-attack / -close / -close-bp all pass.
"""
import json, sys, time
import json, sys, urllib.request
B="http://localhost:8111"
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
def call(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(B+path, data=data, method=method, headers={"Content-Type":"application/json"})
    try:
        with opener.open(req, timeout=30) as r: return json.loads(r.read())
    except urllib.error.HTTPError as e:
        return {"http": e.code, "body": e.read().decode()[:400]}

scenario, cmd = sys.argv[1], int(sys.argv[2])
print("load:", (call("POST","/scenario/load/live",{"name":scenario,"waitForReady":True}).get("data") or {}).get("loaded"))
call("POST","/sim/play",{})
call("POST","/perspective",{"name":"Scenario"})
time.sleep(8)
t = call("GET", f"/entities/{cmd}/trace").get("data") or {}
print("trace:", json.dumps({k: t.get(k) for k in ("tier","dispatch","behavior","activeNode","stackPointer","traceArmed")}))
print("observe:", json.dumps(call("POST","/trace/observe",{"networkId":cmd,"on":True}).get("data")))
actives=[]
for k in range(4):
    time.sleep(3)
    t = call("GET", f"/entities/{cmd}/trace").get("data") or {}
    actives.append(t.get("activeNode")); print(f"  tier={t.get('tier')} armed={t.get('traceArmed')} active={t.get('activeNode')} hist={len(t.get('nodeHistory') or [])}")
print("distinct active nodes:", len(set(a for a in actives if a)))
v = call("GET", f"/entities/{cmd}/variables")
d = v.get("data")
print("variables:", json.dumps([ (x["path"], x["value"]) for x in d["variables"] if x["path"] in ("Phase","Sensor")]) if d else json.dumps(v)[:300])
# wait for the fight to end, then check outcome on SimHost (owner of the hostiles)
for k in range(30):
    time.sleep(5)
    st = call("GET","/status")["data"]
    t = call("GET", f"/entities/{cmd}/trace").get("data") or {}
    if t.get("tier") in (None,"unknown","none"): print(f"commander finished by t={st['simTime']:.1f}"); break
call("POST","/perspective",{"name":"SimHost"})
time.sleep(2)
for n in (1006,1007):
    e = (call("GET", f"/entities/{n}").get("data") or {}).get("Components",{})
    print(n, "Health", (e.get("Health") or {}).get("Current"))
for n in (1001,1002,1003,1004):
    e = (call("GET", f"/entities/{n}").get("data") or {}).get("Components",{})
    tf = e.get("SimTransform") or {}
    print(n, "pos", tf.get("Position"))
