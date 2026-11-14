#!/usr/bin/env python3
"""access-trace-session.py — the SCRIPTED SESSION for the A-1 access tracer (HROT_ACCESS_TRACE=1; FDP/Engine/Fdp.Core/AccessTrace.cs).

Launches the REAL editor (--mode editor, hidden window under xvfb, Release) on the scale rig, then drives it over the debug HTTP API through the
steps the handoff lists, labelling each step as a tracer PHASE (the tracer reads `phase NAME` / `dump` lines from a control file). Every step is
logged with its outcome: a step that failed or was not reachable is an UNKNOWN in the report, never "reads nothing".

    dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release --no-restore
    python3 scripts/scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10 --write-only
    python3 scripts/access-trace-session.py --out /tmp/a1 [--scenario scale-rig-scale-1-1000-2s] [--play-seconds 60]
    python3 scripts/access-trace-report.py /tmp/a1/trace-*.json --full /tmp/a1/per-reader-full.md > /tmp/a1/tables.md

Outputs in --out: trace-N.json (the tracer's aggregates, one per editor process — an ImGui dock assertion can abort the process and the script then restarts it; the report merges them), session.json (every step and its result), editor-N.log.
"""
import argparse
import json
import os
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.request

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NO_TRACE = False   # --no-trace
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))   # localhost must bypass the sandbox proxy


class Session:
    def __init__(self, out, port, gen=0, tag=""):
        self.out, self.port, self.tag = out, port, tag
        self.base = f"http://localhost:{port}"
        self.gen = gen   # one trace file per editor PROCESS (trace-1.json …): an ImGui assertion can abort the process, the next one continues
        self.ctl = None
        self.steps = []
        self.proc = None
        self.crashes = []

    # ------------------------------------------------------------ http
    def call(self, method, path, body=None, timeout=60):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(self.base + path, data=data, method=method, headers={"Content-Type": "application/json"})
        try:
            with OPENER.open(req, timeout=timeout) as r:
                txt = r.read().decode("utf-8", "replace")
        except urllib.error.HTTPError as e:
            txt = e.read().decode("utf-8", "replace")
        except Exception as e:  # noqa: BLE001 — a driver reports, never throws
            return {"ok": False, "error": f"{type(e).__name__}: {e}"}
        try:
            return json.loads(txt)
        except Exception:
            return {"ok": False, "error": "not json: " + txt[:200]}

    def get(self, path, **kw): return self.call("GET", path, None, **kw)
    def post(self, path, body=None, **kw): return self.call("POST", path, {} if body is None else body, **kw)

    # ------------------------------------------------------------ tracer control
    def phase(self, name):
        self._phase = name
        with open(self.ctl, "w") as f:
            f.write(f"phase {name}\n")
        time.sleep(0.7)   # the tracer polls its control file every 200 ms

    def dump(self):
        if not self.ctl or not os.path.isdir(self.out):
            return
        with open(self.ctl, "w") as f:
            f.write("dump\n")
        time.sleep(1.0)

    def step(self, name, fn, wait=0.0):
        """One scripted step: set the phase, run fn, record the outcome (ok / the error) — and wait so frames are drawn under the phase."""
        self.phase(name)
        t0 = time.time()
        try:
            res = fn()
            ok = True
            if isinstance(res, dict) and res.get("ok") is False:
                ok = False
            note = json.dumps(res)[:300] if res is not None else ""
        except Exception as e:  # noqa: BLE001
            ok, note = False, f"{type(e).__name__}: {e}"
        if wait:
            time.sleep(wait)
        self.steps.append({"step": name, "ok": ok, "note": note, "seconds": round(time.time() - t0, 1)})
        print(f"[{'ok ' if ok else 'FAIL'}] {name}: {note[:140]}", flush=True)
        return ok

    # ------------------------------------------------------------ process
    def alive(self):
        return bool(self.get("/status", timeout=15).get("ok"))

    def start(self, dll):
        self.gen += 1
        self.dll = dll
        trace = os.path.join(self.out, f"trace-{self.gen}.json")
        self.ctl = trace + ".ctl"
        env = dict(os.environ, HROT_ACCESS_TRACE="0" if NO_TRACE else "1", HROT_ACCESS_TRACE_OUT=trace,
                   HROT_HIDDEN_WINDOW="1", HROT_DEBUG_API_PORT=str(self.port))
        log = open(os.path.join(self.out, f"editor-{self.gen}.log"), "w")
        self.proc = subprocess.Popen(
            (["dotnet"] if os.name == "nt" else ["xvfb-run", "-a", "--server-args=-screen 0 1280x800x24", "dotnet"])   # Windows: a hidden real window, no Xvfb
            + [dll, "--mode", "editor", "--no-wait", "--target-fps", "0"],
            stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, env=env, start_new_session=True)
        for _ in range(180):
            r = self.get("/status", timeout=5)
            if r.get("ok"):
                return True
            time.sleep(1)
        return False

    def last_phase(self):
        return getattr(self, "_phase", "(start)")

    def stop(self):
        self.post("/shutdown", timeout=10)
        time.sleep(3)
        if self.proc and self.proc.poll() is None:
            try:
                os.killpg(os.getpgid(self.proc.pid), signal.SIGKILL)
            except Exception:  # noqa: BLE001 — Windows has no process groups: kill the process itself
                try: self.proc.kill()
                except Exception: pass  # noqa: BLE001

    def save_steps(self):
        json.dump(self.steps, open(os.path.join(self.out, f"session{self.tag}.json"), "w"), indent=1)


def pick_units(s, n):
    """Network ids of n soldiers of the rig ("Blue 1-1" …) and of a vehicle, from GET /entities (it ignores `limit`: ~12 000 rows)."""
    r = s.get("/entities", timeout=120)
    items = r.get("data") if isinstance(r, dict) and isinstance(r.get("data"), list) else []
    import re
    men = [int(x["networkId"]) for x in items if isinstance(x, dict) and re.match(r"^(Blue|Red) \d+-\d+$", str(x.get("name") or ""))]
    veh = [int(x["networkId"]) for x in items if isinstance(x, dict) and str(x.get("name") or "").startswith("Vehicle")]
    return men[:n], veh[:3], items


# ⭐ The editor's [FrameStats] lines — ONE parser for every measuring script (threaded-scale-measure, threaded-poc2-measure).
#   ⛔ It reads a decimal POINT only (R-4221): the processes run culture-invariant, so a comma is a defect to fix at the output, never to teach a parser.
_NUM = r"([0-9]+(?:\.[0-9]+)?)"


def frame_stats(text):
    """Every [FrameStats] line in <text> as (mean ms, p95 ms or None), in order."""
    import re as _re
    out = []
    for m in _re.finditer(r"\[FrameStats\] \d+ frames\s+mean " + _NUM + r" ms(?:\s+p50 " + _NUM + r"\s+p95 " + _NUM + r")?", text):
        mean = float(m.group(1))
        p95 = float(m.group(3)) if m.group(3) else None
        out.append((mean, p95))
    return out


def load_scenario(s, a):
    for _ in range(60):   # the editor answers /status before the cluster master has booted: retry until the load is accepted
        r = s.post("/scenario/load/edit", {"name": a.scenario, "waitForReady": False}, timeout=120)
        if r.get("ok") or "not operable" not in str(r.get("error", "")):
            return r
        time.sleep(3)
    return {"ok": False, "error": "the cluster never became operable"}


def wait_state(s, want, seconds):
    """Poll GET /status until clusterState == want (and entities exist) — the load with the tracer ON takes minutes."""
    t0 = time.time()
    while time.time() - t0 < seconds:
        d = (s.get("/status", timeout=30).get("data") or {})
        if d.get("clusterState") == want and (want != "OperatingEdit" or d.get("entityCount", 0) > 0):
            return {"ok": True, "state": want, "entityCount": d.get("entityCount"), "seconds": round(time.time() - t0)}
        time.sleep(5)
    return {"ok": False, "error": f"state {want} not reached in {seconds}s"}


def open_asset(s, kind):
    r = s.get("/assets", timeout=60)
    items = (r.get("data") or {}).get("assets", []) if isinstance(r.get("data"), dict) else []
    ids = [x.get("assetId") or x.get("id") for x in items if (x.get("kind") or x.get("type")) == kind]
    if not ids:
        return {"ok": False, "error": f"no {kind} asset listed"}
    return s.post(f"/assets/{ids[0]}/open", {}, timeout=120)


STAGES = ["load", "idle", "select1", "panels", "select10", "perspectives", "play", "stepping", "view3d", "edits", "orders", "hotreload", "edit2", "save", "n15"]


def persp_of(pid):
    """Which perspective a window belongs to — focusing it from another perspective PINS it there, and with ~40 pinned dock windows ImGui aborts."""
    if pid.endswith("_blueprint") or pid == "editor_blueprint_tools":
        return "Blueprint"
    if pid.endswith("_btree") or pid.startswith("ai_asset_browser"):
        return "BTree"
    if pid.endswith("_hsm"):
        return "HSM"
    if pid.startswith(("editor_review", "rb_")) or pid == "editor_trainee_debrief":
        return "Review"
    return "Scenario"


def recover(s, a, why, ids):
    """The editor process died (an ImGui dock assertion): start a new one, reload the scenario, reselect, and carry on under a new trace file."""
    s.crashes.append({"during": s.last_phase(), "why": why})
    print(f"!! editor died during {s.last_phase()} ({why}) — restarting (trace-{s.gen + 1}.json)", flush=True)
    try:
        os.killpg(os.getpgid(s.proc.pid), signal.SIGKILL)
    except Exception:  # noqa: BLE001 — Windows
        try: s.proc.kill()
        except Exception: pass  # noqa: BLE001
    time.sleep(3)
    if not s.start(s.dll):
        raise SystemExit("editor would not restart")
    s.step("(restart) load: " + a.scenario, lambda: load_scenario(s, a))
    s.step("(restart) wait for OperatingEdit", lambda: wait_state(s, "OperatingEdit", 1500))
    if ids:
        s.step("(restart) select: one unit", lambda: s.post("/selection", {"entity": ids[0]}), wait=3)


def focus_panels(s, a, persp, ids):
    pr = s.get("/panels")
    reg = (pr.get("data") or {}).get("registered", []) if isinstance(pr.get("data"), dict) else []
    seen = getattr(s, "_panels_done", set())
    todo = [p for p in reg if p not in seen and persp_of(p) == persp]
    s.steps.append({"step": f"{persp} perspective: windows to open", "ok": bool(reg), "note": f"{len(todo)} of {len(reg)} registered", "seconds": 0})
    for pid in todo:
        ok = s.step(f"panel ({persp}): {pid}", lambda pid=pid: s.post(f"/panels/{pid}/focus", {}), wait=a.panel_seconds)
        seen.add(pid)
        if not s.alive():
            recover(s, a, f"after opening {pid}", ids)
            s.step(f"(restart) perspective: {persp}", lambda: s.post("/perspective", {"name": persp}), wait=3)
    s._panels_done = seen


def run_stages(s, a, stages):
    ids, veh = [], []
    if "load" in stages:
        s.step("load: " + a.scenario, lambda: load_scenario(s, a))
        s.step("load: wait for OperatingEdit", lambda: wait_state(s, "OperatingEdit", 1500))
    ids, veh, _ = pick_units(s, 12)
    print(f"units: {len(ids)} men, {len(veh)} vehicles", flush=True)
    if "idle" in stages:
        s.step("edit-idle (nothing selected)", lambda: None, wait=8)
    if "select1" in stages and ids:
        s.step("select: one unit", lambda: s.post("/selection", {"entity": ids[0]}), wait=4)
        s.step("focus: that unit", lambda: s.post(f"/entities/{ids[0]}/focus", {}), wait=2)
    if "panels" in stages:
        focus_panels(s, a, "Scenario", ids)
    if "select10" in stages and len(ids) >= 10:
        for i in range(10):   # the API selects ONE unit per request — ten at once (box select) needs the mouse: not reachable
            s.step(f"select: unit {i + 1} of 10, one at a time", lambda i=i: s.post("/selection", {"entity": ids[i]}), wait=1.5)
    if "perspectives" in stages:
        for persp, kind in (("BTree", "BTree"), ("HSM", "Hsm"), ("Blueprint", "Blueprint"), ("Review", None)):
            s.step(f"perspective: {persp}", lambda p=persp: s.post("/perspective", {"name": p}), wait=4)
            if kind:
                s.step(f"open a {kind} asset", lambda k=kind: open_asset(s, k), wait=6)
            focus_panels(s, a, persp, ids)
        s.step("perspective: back to Scenario", lambda: s.post("/perspective", {"name": "Scenario"}), wait=4)
    if "play" in stages:
        s.step("preview: play", lambda: s.post("/sim/play", {}, timeout=180), wait=a.play_seconds)
        s.step("preview: pause", lambda: s.post("/sim/pause", {}, timeout=60), wait=6)
    if "stepping" in stages:
        s.step("preview: step x5 (paused)", lambda: s.post("/sim/step", {"count": 5}, timeout=300), wait=4)
    if "view3d" in stages and ids:
        s.step("3-D view on (film_shot follow)", lambda: s.post("/film/shot", {"follow": ids[0], "distance": 40, "pitchDeg": -25}), wait=12)
        s.step("3-D view: preview playing", lambda: s.post("/sim/play", {}, timeout=180), wait=20)
        s.step("3-D view: pause", lambda: s.post("/sim/pause", {}, timeout=60), wait=6)
        s.step("3-D view off", lambda: s.call("DELETE", "/film/shot"), wait=6)
    if "edits" in stages and ids:
        s.step("edit: spawn a soldier (place) — queued until the clock ticks", lambda: s.post("/entities/spawn", {"tkbType": 302}, timeout=60), wait=1)
        s.step("edit: edit a component (SimTransform patch — a move)", lambda: s.post(f"/entities/{ids[1]}/component", {"componentType": "EntityInfo", "patch": {}}, timeout=60), wait=3)
        s.step("edit: delete a soldier", lambda: s.call("DELETE", f"/entities/{ids[2]}"), wait=1)
        s.step("edit: step x3 so the spawn and the teardown land", lambda: s.post("/sim/step", {"count": 3}, timeout=300), wait=4)
        cmds = s.get("/editor/commands?scope=shell")   # N-15 — Undo / Redo are SHELL commands (the document set is the active AI asset's)
        names = [c.get("id") for c in (cmds.get("data") or {}).get("commands", [])] if isinstance(cmds.get("data"), dict) else []
        undo = [c for c in names if c and "undo" in c.lower()]
        s.steps.append({"step": "edit: undo commands offered", "ok": bool(undo), "note": ", ".join(undo[:5]) or f"{len(names)} commands, none named undo", "seconds": 0})
    if "orders" in stages and ids:
        s.step("order: move one soldier", lambda: s.post(f"/entities/{ids[3]}/order", {"kind": "move", "x": 100, "y": 100}), wait=4)
        s.step("order: hold another", lambda: s.post(f"/entities/{ids[4]}/order", {"kind": "hold"}), wait=4)
    if "hotreload" in stages:
        s.step("hot reload: reload_ai_behaviours (builds Hrot.AI.Behaviors)", lambda: s.post("/ai/reload", {}, timeout=600), wait=10)
    def ensure_edit(label):
        # undo / redo / the unsaved check are Edit-only: a preview runs a COPY of the edit world, so stop it first
        if (s.get("/status").get("data") or {}).get("inPreview"):
            s.step(label + ": stop the preview (Edit-only steps follow)", lambda: s.post("/sim/stop", {}, timeout=180), wait=6)
    if "edit2" in stages and ids:
        # in the EDIT state (no preview): a real component change (a move) through the API's edit path, then the editor's own Undo
        ensure_edit("edit (Edit state)")
        def move():
            r = s.get(f"/entities/{ids[0]}")
            tr = ((r.get("data") or {}).get("Components") or {}).get("SimTransform")
            if not isinstance(tr, dict) or not isinstance(tr.get("Position"), (dict, list)):
                return {"ok": False, "error": "SimTransform not readable: " + json.dumps(tr)[:120]}
            pos = tr["Position"]
            if isinstance(pos, dict):
                k = next(k for k in pos if k.lower() == "x")
                pos = dict(pos); pos[k] = float(pos[k]) + 10.0
            else:
                pos = [float(pos[0]) + 10.0] + list(pos[1:])
            return s.post(f"/entities/{ids[0]}/component", {"componentType": "SimTransform", "patch": {"Position": pos}}, timeout=60)
        s.step("edit (Edit state): move a soldier 10 m (edit_component SimTransform)", move, wait=3)
        s.step("edit (Edit state): undo (Edit ▸ Undo)", lambda: s.post("/editor/commands/edit.undo/invoke", {"scope": "shell"}), wait=4)
        s.step("edit (Edit state): redo (Edit ▸ Redo)", lambda: s.post("/editor/commands/edit.redo/invoke", {"scope": "shell"}), wait=4)
        s.step("edit (Edit state): check unsaved", lambda: s.post("/scenario/check-unsaved", {}, timeout=120), wait=2)
    if "n15" in stages and len(ids) >= 8:
        # ⭐ N-15 ① (CE-4477) — WHILE PLAYING: "drags" (a position patch through the attribute route — the drag gizmo's own write rule,
        #   AX-007 / EntityWriteRouter), one REFUSED (a component no node can own on a node-local type — the router must say no), a field edit,
        #   play / pause / step; then a scenario LOAD mid-run, and undo / redo in Edit (they are Edit-only by design)
        def drag(nid, dx):
            r = s.get(f"/entities/{nid}")
            tr = ((r.get("data") or {}).get("Components") or {}).get("SimTransform")
            if not isinstance(tr, dict) or not isinstance(tr.get("Position"), (dict, list)):
                return {"ok": False, "error": "SimTransform not readable"}
            pos = tr["Position"]
            if isinstance(pos, dict):
                k = next(k for k in pos if k.lower() == "x"); pos = dict(pos); pos[k] = float(pos[k]) + dx
            else:
                pos = [float(pos[0]) + dx] + list(pos[1:])
            return s.post(f"/entities/{nid}/component", {"componentType": "SimTransform", "patch": {"Position": pos}}, timeout=60)
        s.step("n15: play", lambda: s.post("/sim/play", {}, timeout=180), wait=8)
        for i in range(3):
            s.step(f"n15: drag unit {i + 1} (playing)", lambda i=i: drag(ids[5 + i], 5.0 + i), wait=2)
        def refused():
            r = s.post(f"/entities/{ids[0]}/attribute", {"patchJson": "{\"NoSuchComponent\": {\"X\": 1}}"}, timeout=60)
            # ok when the write was NOT applied: an error, or the route says nothing matched
            data = r.get("data") or {}
            route = ((data.get("write") or {}).get("route")) if isinstance(data, dict) else None
            return {"ok": (not r.get("ok")) or route == "noMatch", "data": {"answer": r.get("error") or route}}
        s.step("n15: a refused drag (nothing to apply)", refused, wait=2)
        s.step("n15: a field edit (playing)", lambda: s.post(f"/entities/{ids[1]}/component", {"componentType": "EntityInfo", "patch": {}}, timeout=60), wait=3)
        s.step("n15: pause", lambda: s.post("/sim/pause", {}, timeout=60), wait=3)
        s.step("n15: step x5", lambda: s.post("/sim/step", {"count": 5}, timeout=300), wait=3)
        s.step("n15: play again", lambda: s.post("/sim/play", {}, timeout=180), wait=10)
        s.step("n15: load a scenario MID-RUN: " + a.scenario, lambda: load_scenario(s, a), wait=2)
        s.step("n15: wait for OperatingEdit after the mid-run load", lambda: wait_state(s, "OperatingEdit", 1500))
        ids2, _, _ = pick_units(s, 12)
        if ids2:
            ensure_edit("n15 (Edit)")
            s.step("n15 (Edit): drag a unit", lambda: drag(ids2[0], 7.0), wait=3)
            s.step("n15 (Edit): undo (Edit ▸ Undo)", lambda: s.post("/editor/commands/edit.undo/invoke", {"scope": "shell"}), wait=3)
            s.step("n15 (Edit): redo (Edit ▸ Redo)", lambda: s.post("/editor/commands/edit.redo/invoke", {"scope": "shell"}), wait=3)
            s.step("n15 (Edit): play after the load", lambda: s.post("/sim/play", {}, timeout=180), wait=15)
            s.step("n15: pause", lambda: s.post("/sim/pause", {}, timeout=60), wait=3)
    if "save" in stages:
        ensure_edit("save")
        s.step("save: scenario scale-rig-a1-save", lambda: s.post("/scenario/save", {"name": "scale-rig-a1-save"}, timeout=300), wait=5)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--port", type=int, default=8131)
    ap.add_argument("--dll", default=os.path.join(REPO, "Hrot/Runner/Hrot.ClusterRunner/bin/Release/net8.0/Hrot.ClusterRunner.dll"))
    ap.add_argument("--scenario", default="scale-rig-scale-1-1000-2s")
    ap.add_argument("--play-seconds", type=int, default=60)
    ap.add_argument("--panel-seconds", type=float, default=2.5, help="seconds drawn per opened panel")
    ap.add_argument("--stages", default=",".join(STAGES))
    ap.add_argument("--gen-start", type=int, default=0, help="number the trace files from N+1 (a second session into the same --out)")
    ap.add_argument("--tag", default="", help="suffix of session<tag>.json")
    ap.add_argument("--attach", action="store_true", help="the editor is already running (started by an earlier run of this script with --keep)")
    ap.add_argument("--keep", action="store_true", help="leave the editor running at the end")
    ap.add_argument("--no-trace", action="store_true", help="the tracer OFF (A-3: drive the same session for timings, e.g. with HROT_PUBLISH_PROBE=1 set by the caller)")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    global NO_TRACE
    NO_TRACE = a.no_trace
    s = Session(a.out, a.port, a.gen_start, a.tag)
    if not a.attach and not s.start(a.dll):
        print("editor did not come up — see editor.log")
        sys.exit(1)
    try:
        run_stages(s, a, a.stages.split(","))
    finally:
        s.dump()
        s.save_steps()
        if not a.keep:
            s.stop()


if __name__ == "__main__":
    main()
