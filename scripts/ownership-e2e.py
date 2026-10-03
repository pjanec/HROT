#!/usr/bin/env python3
"""Ownership E2E driver — docs/DESIGN_Ownership_Groups_And_Grants.md §5.7, over the cluster debug HTTP API.

Launch the cluster first (one process, all nodes; the debug API on port 8111):
    HROT_DEBUG_API_PORT=8111 xvfb-run -a dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode all
then
    python3 scripts/ownership-e2e.py matrix [E1 E2 ...]   # create per §5.7 and check one ownership truth per descriptor
    python3 scripts/ownership-e2e.py own <networkId>      # dump + check one entity's ownership on every node
    python3 scripts/ownership-e2e.py move <networkId>...  # MoveToLocation task, then position on every node
    python3 scripts/ownership-e2e.py edit <networkId> <perspective> '<patchJson>'   # E4/E6: patch from a NON-owner,
                                                          # then show the patched state on every node (CE-3003)
Multi-process launch (one node per process, RUNBOOK §1.2): set HROT_E2E_PORTS, e.g.
    HROT_E2E_PORTS="SimHost=8102,Scenario=8101,IG=8103" python3 scripts/ownership-e2e.py own <networkId>
and every call for a perspective goes to that node's own port (E8, the crash run).
Node ids (measured on --mode all): SimHost 1 · IG 100 · CGF 400 (perspective "Scenario").
⚠ Use the hostname localhost: 127.0.0.1 404s every route. The cluster boots PAUSED (POST /sim/play).
"""
import json, os, sys, time, urllib.request

B = "http://localhost:8111"
PORTS = dict(kv.split("=") for kv in os.environ.get("HROT_E2E_PORTS", "").split(",") if kv)   # perspective -> port
_base = [B]
NODES = {"SimHost": 1, "Scenario": 400, "IG": 100}          # perspective -> node id (measured)
NAME = {1: "SimHost", 400: "CGF", 100: "IG", -1: "?"}
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def call(method, path, body=None):
    data = json.dumps(body if body is not None else {}).encode() if method in ("POST", "DELETE", "PUT") else None
    req = urllib.request.Request(_base[0] + path, data=data, method=method, headers={"Content-Type": "application/json"})
    try:
        with opener.open(req, timeout=60) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        return json.loads(e.read() or b"{}")
    except (urllib.error.URLError, ConnectionError) as e:      # a killed node (E8) answers nothing
        return {"ok": False, "error": f"unreachable: {e}"}


def persp(name):
    if PORTS:                                   # one node per process: the perspective IS the port
        _base[0] = f"http://localhost:{PORTS[name]}"
        return {"ok": True}
    r = call("POST", "/perspective", {"name": name})
    return r


def entity_ids(p):
    persp(p)
    d = call("GET", "/entities")["data"]
    lst = d if isinstance(d, list) else d.get("entities", [])
    return {e.get("networkId", e.get("NetworkId")) for e in lst}


def sim_time():
    persp("SimHost")
    return call("GET", "/status")["data"]["simTime"]


def wait_sim(seconds):
    t0 = sim_time()
    while sim_time() - t0 < seconds:
        time.sleep(0.2)


def create(creator_persp, tkb, x=600.0, y=320.0):
    before = entity_ids(creator_persp)
    r = call("POST", "/entities/create-request",
             {"tkbType": tkb, "ownerNodeId": NODES[creator_persp],
              "transform": {"position": {"x": x, "y": y, "z": 0.0}, "rotation": {"x": 0, "y": 0, "z": 0, "w": 1}}})
    if not r.get("ok"):
        print("create failed:", r); return None
    deadline = time.time() + 20
    while time.time() < deadline:
        new = entity_ids(creator_persp) - before
        if new:
            return max(new)
        time.sleep(0.3)
    print("create: no new entity on", creator_persp); return None


def ownership(nid):
    out = {}
    for p, node in NODES.items():
        persp(p)
        r = call("GET", f"/entities/{nid}/ownership")
        out[node] = r.get("data") if r.get("ok") else {"error": r.get("error")}
    return out


def evaluate(nid, own, expect=None):
    """expect: {group: owner node} (group names as reported; 'creator' and others)."""
    problems = []
    desc_rows = {}
    for node, d in own.items():
        if not d or "error" in d:
            problems.append(f"{NAME[node]}: {d.get('error') if d else 'no data'}"); continue
        prim = d.get("primaryOwnerId")
        for de in d.get("descriptors", []):
            key = (de["descriptorTypeId"], de["group"])
            rec = de["ownerNodeId"] if de["ownerNodeId"] is not None else prim
            row = desc_rows.setdefault(key, {"records": {}, "claims": {}, "cmr": {}})
            row["records"][node] = rec
            row["cmr"][node] = de.get("claimMatchesRecord")
            for c in de.get("claims", []):
                row.setdefault("present", {}).setdefault(c["componentId"], set()).add(node)
                s_ = row["claims"].setdefault(c["componentId"], set())
                if c["claimedByThisNode"]: s_.add(node)
    groups = {}
    lines = []
    for (dt, g), row in sorted(desc_rows.items()):
        recs = set(v for v in row["records"].values() if v is not None)   # absent on a node = not listed there
        owner = next(iter(recs)) if len(recs) == 1 else None
        claimers = set().union(*row["claims"].values()) if row["claims"] else set()
        # one truth: nobody but the recorded owner claims; the owner claims every component it HAS (a component
        # present only on replicas — built locally from the descriptor — is claimed by nobody, correctly).
        bad_claim = [cid for cid, s in row["claims"].items()
                     if (owner is not None and (s - {owner} or (owner in row.get("present", {}).get(cid, set()) and owner not in s)))]
        flag = ""
        if owner is None: flag += " RECORDS-DISAGREE"
        if bad_claim: flag += f" CLAIM!={sorted(bad_claim)[:6]}"
        if False in row["cmr"].values(): flag += " claimMatchesRecord=false@" + ",".join(NAME[n] for n, v in row["cmr"].items() if v is False)
        lines.append(f"   d{dt:<4} {g:<13} record={'/'.join(NAME.get(r, str(r)) for r in [row['records'].get(n) for n in own])}"
                     f"  claimers={','.join(sorted(NAME[c] for c in claimers)) or '-'}{flag}")
        groups.setdefault(g, set()).add(owner)
        if flag: problems.append(f"d{dt} {g}:{flag}")
    print(f" entity {nid}: primary per node = " + ", ".join(f"{NAME[n]}->{NAME.get(d.get('primaryOwnerId'), d.get('primaryOwnerId'))}" for n, d in own.items() if d and 'error' not in d))
    for l in lines: print(l)
    summary = {g: sorted(NAME.get(o, str(o)) for o in s) for g, s in groups.items()}
    print(" groups:", summary)
    if expect:
        for g, want in expect.items():
            got = summary.get(g)
            if got != [want]:
                problems.append(f"group {g}: expected {want}, got {got}")
    print(" RESULT:", "PASS" if not problems else "FAIL")
    for p in problems: print("   -", p)
    return not problems


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "own":
        nid = int(sys.argv[2]); evaluate(nid, ownership(nid))
    elif cmd == "eval":
        exp = {"E1": {"Brain": "CGF", "MuscleGround": "SimHost", "Perception": "SimHost", "creator": "IG"},
               "E2": {"Brain": "CGF", "MuscleGround": "SimHost", "Perception": "SimHost", "creator": "SimHost"},
               "E3": {"Brain": "CGF", "MuscleGround": "SimHost", "Perception": "SimHost", "creator": "CGF"}}
        for kv in sys.argv[2:]:
            k, nid = kv.split("="); nid = int(nid)
            print(f"== {k} entity {nid}"); evaluate(nid, ownership(nid), exp.get(k))
    elif cmd == "matrix":
        rows = {
          "E1": ("IG", 100, {"Brain": "CGF", "MuscleGround": "SimHost", "Perception": "SimHost", "creator": "IG"}),
          "E2": ("SimHost", 100, {"Brain": "CGF", "MuscleGround": "SimHost", "Perception": "SimHost", "creator": "SimHost"}),
          "E3": ("Scenario", 100, {"Brain": "CGF", "MuscleGround": "SimHost", "Perception": "SimHost", "creator": "CGF"}),
          "E4": ("Scenario", 8803, None),
          "E5": ("Scenario", 301, None),
          "E6": ("IG", 8803, None),
        }
        made = {}
        for k in (sys.argv[2:] or rows):
            p, tkb, exp = rows[k]
            nid = create(p, tkb, 600.0 + 20 * len(made), 320.0)
            made[k] = nid
            print(f"== {k}: {p} creates tkb {tkb} -> entity {nid}")
        wait_sim(4)
        for k, nid in made.items():
            p, tkb, exp = rows[k]
            print(f"== {k} ({p} creates {tkb}) after 4 s sim")
            if nid: evaluate(nid, ownership(nid), exp)
        print('made:', json.dumps(made))
    elif cmd == "create":
        p, tkb = sys.argv[2], int(sys.argv[3])
        nid = create(p, tkb); print("created", nid)


def pos(nid):
    out = {}
    for p, node in NODES.items():
        persp(p)
        r = call("GET", f"/entities/{nid}")
        if not r.get("ok"): out[NAME[node]] = "ERR " + str(r.get("error"))[:80]; continue
        t = r["data"]["Components"].get("SimTransform") or {}
        out[NAME[node]] = [round(v, 1) for v in (t.get("Position") or [])[:2]]
    return out


def move_check(ids, persp_name="Scenario", dx=80.0, dy=60.0, secs=10):
    import math
    start = {}
    for nid in ids:
        p0 = pos(nid); start[nid] = p0
        persp(persp_name)
        x, y = next(v for v in p0.values() if isinstance(v, list) and v)
        r = call("POST", f"/missions/{nid}/task", {"behavior": "MoveToLocation",
                 "params": {"X": x + dx, "Y": y + dy, "Speed": 8, "ArrivalRadius": 3}})
        print(f" task {nid}: ok={r.get('ok')} err={r.get('error')} data={str(r.get('data'))[:120]}")
        r = call("POST", f"/missions/{nid}/run", {})
        print(f" run  {nid}: ok={r.get('ok')} err={r.get('error')}")
    t0 = sim_time(); wait_sim(secs); t1 = sim_time()
    for nid in ids:
        p1 = pos(nid)
        print(f" entity {nid}: dSimTime={t1 - t0:.1f}s")
        for n in p1:
            a, b = start[nid].get(n), p1[n]
            if isinstance(a, list) and isinstance(b, list) and a and b:
                print(f"   {n:<8} {a} -> {b}  moved {math.dist(a, b):.1f} m")
            else:
                print(f"   {n:<8} {a} -> {b}")


def state(nid):
    """Position, heading-bearing rotation and name of one entity, from every node."""
    out = {}
    for p, node in NODES.items():
        persp(p)
        r = call("GET", f"/entities/{nid}")
        if not r.get("ok"): out[NAME[node]] = "ERR " + str(r.get("error"))[:80]; continue
        c = r["data"]["Components"]
        t = c.get("SimTransform") or {}
        info = c.get("EntityInfo") or {}
        out[NAME[node]] = {"pos": [round(v, 1) for v in (t.get("Position") or [])[:2]],
                           "rot": [round(v, 3) for v in (t.get("Rotation") or [])],
                           "name": info.get("Name")}
    return out


def edit_check(nid, editor_persp, patch_json, secs=3):
    """CE-3003 / design §5.7 E4, E6: a NON-owner patches; the OWNER applies; every node shows it."""
    before = state(nid)
    persp(editor_persp)
    r = call("POST", f"/entities/{nid}/attribute", {"patchJson": json.loads(patch_json)})
    print(f" patch from {editor_persp}: ok={r.get('ok')} err={r.get('error')} "
          f"write={json.dumps((r.get('data') or {}).get('write'))}")
    wait_sim(secs)
    after = state(nid)
    for n in after:
        print(f"   {n:<8} {before.get(n)} -> {after[n]}")
    return r.get("ok"), after


if __name__ == "__main__" and sys.argv[1] == "move":
    move_check([int(x) for x in sys.argv[2:]])

if __name__ == "__main__" and sys.argv[1] == "edit":
    edit_check(int(sys.argv[2]), sys.argv[3], sys.argv[4])
