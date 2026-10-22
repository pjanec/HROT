#!/usr/bin/env python3
"""Scenario regression check — the proof of concept of docs/DESIGN_Scenario_Regression.md (CE-4042).

Runs each scenario the way it must repeat (R-3035, R-3038): a FRESH editor process per scenario (CE-3294: a reload still leaks),
the scenario opened from its file, the preview entered PAUSED, then stepped over HTTP (one gated frame per step, CE-105). Every
SAMPLE (0.25 s of sim time) it reads the world and judges the scenario's machine-checkable conditions
(`scenarios/<s>/checks.json`); every `golden.every` seconds it records the named units' full component state. The run is then
compared with the scenario's GOLDEN (`Hrot/Runner/Hrot.SystemTests/Goldens/scenarios/<s>.json`), and the first difference is named.

    python3 scripts/scenario-check.py ua-tank-ammo bt-grenade-posture        # judge + compare
    python3 scripts/scenario-check.py --capture ua-tank-ammo                 # judge + (re)write the golden (SCENARIO_GOLDEN_CAPTURE=1 too)
    python3 scripts/scenario-check.py --all                                  # every scenario that has a checks.json

Exit 0 when every condition holds and every golden matches (or the scenario has none). The checks.json grammar is the design's §3.
"""
import argparse, importlib.util, json, math, os, sys, time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_spec = importlib.util.spec_from_file_location("udc", os.path.join(REPO, "scripts", "utility-demo-check.py"))
u = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(u)

GOLDENS = os.path.join(REPO, "Hrot", "Runner", "Hrot.SystemTests", "Goldens", "scenarios")
STEP_DT = 1.0 / 60.0
SAMPLE_STEPS = 15            # 0.25 s at 60 Hz: the resolution of every condition's time
TOLERANCE = 1e-3             # the same as /diff/compare (ComponentDiffService, epsilon 0.001)


# ── names ───────────────────────────────────────────────────────────────────────────────────────────────────────────

def matches(name, pattern):
    """A unit is named exactly, or `*part` — any whose name contains `part` (the take anchors' rule, AnchorBook)."""
    if name is None or pattern is None:
        return False
    return pattern[1:] in name if pattern.startswith("*") else name == pattern


# ── values ──────────────────────────────────────────────────────────────────────────────────────────────────────────

def at_path(obj, path):
    for part in path.split("."):
        if isinstance(obj, dict):
            obj = obj.get(part)
        elif isinstance(obj, list) and part.lstrip("-").isdigit():
            i = int(part)
            obj = obj[i] if -len(obj) <= i < len(obj) else None
        else:
            return None
    return obj


def compare(value, op, want):
    if value is None:
        return op == "==" and want is None
    try:
        if op == "==":
            return math.isclose(value, want, abs_tol=TOLERANCE) if isinstance(value, (int, float)) and isinstance(want, (int, float)) else str(value) == str(want)
        if op == "!=":
            return not compare(value, "==", want)
        if op == "<": return value < want
        if op == "<=": return value <= want
        if op == ">": return value > want
        if op == ">=": return value >= want
        if op == "in": return value in want or str(value) in [str(w) for w in want]
        if op == "contains": return str(want) in str(value)
    except TypeError:
        return False
    raise ValueError(f"unknown op {op}")


def where_ok(record, where):
    """`where: {"field": [op, value]}` or `{"field": value}` over a shot / blast record."""
    for k, cond in (where or {}).items():
        op, want = (cond[0], cond[1]) if isinstance(cond, list) and len(cond) == 2 and isinstance(cond[0], str) and cond[0] in (
            "==", "!=", "<", "<=", ">", ">=", "in", "contains") else ("==", cond)
        if not compare(at_path(record, k), op, want):
            return False
    return True


# ── one run ─────────────────────────────────────────────────────────────────────────────────────────────────────────

class World:
    """What one sample can read: names, components (fetched once per sample), the shot and blast logs since the load."""

    def __init__(self):
        self.t = 0.0
        self.ids = {}
        self.names = {}
        self.shots = {}          # seq -> record (names resolved)
        self.blasts = []         # records in the order seen, each with the sample time it was seen at
        self._blast_keys = set()
        self._comps = {}
        self._extra = {}

    def refresh(self, t):
        self.t = t
        self._comps.clear()
        self._extra.clear()
        rows = u.data(u.call("GET", "/entities")) or []
        self.ids = {r.get("name"): r.get("networkId") for r in rows if r.get("name")}
        self.names = {v: k for k, v in self.ids.items()}
        for x in (u.data(u.call("GET", "/combat/shots?last=256")) or {}).get("shots") or []:
            if x.get("seq") in self.shots:
                # a round is logged when FIRED and ends later in place (AnchorBook): keep its latest outcome
                if x.get("outcome") == self.shots[x["seq"]].get("outcome"):
                    continue
            r = dict(x)
            for k in ("shooter", "target", "hit"):
                r[k] = self.names.get(x.get(k), x.get(k))
            self.shots[x.get("seq")] = r
        for d in (u.data(u.call("GET", "/combat/detonations?last=128")) or {}).get("detonations") or []:
            key = json.dumps({k: d.get(k) for k in ("tick", "seq", "shooter", "burst")}, sort_keys=True)
            if key in self._blast_keys:
                continue
            self._blast_keys.add(key)
            r = dict(d)
            r["shooter"] = self.names.get(d.get("shooter"), d.get("shooter"))
            r["seenAt"] = round(t, 4)
            self.blasts.append(r)

    def units(self, pattern):
        return [n for n in self.ids if matches(n, pattern)]

    def comps(self, name):
        if name not in self._comps:
            self._comps[name] = (u.data(u.call("GET", f"/entities/{self.ids[name]}")) or {}).get("Components") or {}
        return self._comps[name]

    def route(self, name, what):
        key = (name, what)
        if key not in self._extra:
            self._extra[key] = u.data(u.call("GET", f"/entities/{self.ids[name]}/{what}")) or {}
        return self._extra[key]


def cue_time(cue, w):
    """The time the cue first holds in this run, judged at this sample — or None. Event cues (shot, blast) answer the event's own
    time; state cues answer the sample's."""
    on = cue.get("on")
    if on == "shot":
        xs = sorted((s for s in w.shots.values()
                     if (not cue.get("by") or matches(s.get("shooter"), cue["by"]))
                     and (not cue.get("at") or matches(s.get("target"), cue["at"]))
                     and (not cue.get("hit") or matches(s.get("hit"), cue["hit"]))
                     and where_ok(s, cue.get("where"))), key=lambda s: s.get("seq") or 0)
        n = cue.get("n", 1)
        return xs[n - 1].get("firedAt", w.t) if len(xs) >= n else None
    if on == "blast":
        xs = [b for b in w.blasts if (not cue.get("by") or matches(b.get("shooter"), cue["by"])) and where_ok(b, cue.get("where"))]
        n = cue.get("n", 1)
        return xs[n - 1]["seenAt"] if len(xs) >= n else None
    if on == "field":
        units = w.units(cue["who"])
        want = w.t if cue.get("value") == "$now" else cue.get("value")       # "$now": the sim time of the sample (KnockedDownUntil > now)
        held = [n for n in units if compare(at_path(w.comps(n), cue["path"]), cue.get("op", "=="), want)]
        q = cue.get("quantifier", "any")
        ok = (len(held) > 0 if q == "any" else
              len(units) > 0 and len(held) == len(units) if q == "all" else
              len(held) >= int(q.split(":")[1]) if q.startswith("atLeast:") else False)
        return w.t if ok else None
    if on == "task":
        want = [str(x).lower() for x in (cue["is"] if isinstance(cue["is"], list) else [cue["is"]])]
        for n in w.units(cue["who"]):
            b = (w.route(n, "state").get("behavior") or {}).get("name")
            if b and b.lower() in want:
                return w.t
        return None
    if on == "decision":
        for n in w.units(cue["who"]):
            for d in w.route(n, "utility").get("decisions") or []:
                if d.get("decision") == cue["decision"] and d.get("winner") in cue["winner"]:
                    return w.t
        return None
    raise ValueError(f"unknown cue 'on': {on}")


def resolve(expr, times):
    """A time: a number, or `<name>` / `<name>+<s>` / `<name>-<s>` of an anchor or expectation already seen."""
    if expr is None or isinstance(expr, (int, float)):
        return expr
    for sign in ("+", "-"):
        if sign in expr[1:]:
            name, off = expr.rsplit(sign, 1)
            base = times.get(name.strip())
            return None if base is None else base + (float(off) if sign == "+" else -float(off))
    return times.get(expr.strip())


def run(scenario, checks, port):
    rec = {"scenario": scenario, "steps": 0, "beats": {}, "verdicts": [], "snapshots": [], "notes": []}
    run_for = float(checks.get("runFor", 60))
    golden_every = (checks.get("golden") or {}).get("every") if checks.get("golden") is not False else None
    anchors = checks.get("anchors") or {}
    expects = checks.get("expect") or []
    actions = [dict(a) for a in checks.get("do") or []]
    times = {}                  # anchor / expectation name -> first time it held
    never_broken = {}           # never-expectation name -> time it was broken
    wall = time.time()
    proc = u.launch(port, "editor")
    try:
        r = u.call("POST", "/scenario/load/edit", {"name": scenario, "waitForReady": True})
        if "http" in r:
            raise RuntimeError(f"load: {str(r)[:300]}")
        r = u.call("POST", "/preview/enter", {"startPaused": True})
        if "http" in r:
            raise RuntimeError(f"preview: {str(r)[:300]}")
        t0 = u.sim_time() or 0.0
        w = World()
        w.refresh(0.0)
        rec["cast"] = sorted(w.ids)
        missing = sorted({p for c in [*anchors.values(), *[e.get("never") or e for e in expects]]
                          for p in [c.get("who"), c.get("by"), c.get("at") if isinstance(c.get("at"), str) else None]
                          if p and not w.units(p)})
        if missing:
            rec["notes"].append(f"names not in the scenario: {missing}")
        next_golden = 0.0 if golden_every else None
        step = 0
        while True:
            t = step * STEP_DT
            # what happens at this step: actions due, then a sample
            for a in actions:
                if a.get("done"):
                    continue
                at = resolve(a.get("at"), times)
                if at is not None and t + 1e-9 >= at:
                    s = a["set"]
                    for n in w.units(s["who"]):
                        res = u.call("POST", f"/entities/{w.ids[n]}/component",
                                     {"networkId": w.ids[n], "componentType": s["component"], "patch": s["patch"]})
                        if "http" in res:
                            rec["notes"].append(f"do at {t:.2f}: {n}.{s['component']} refused: {str(res)[:200]}")
                    a["done"] = round(t, 4)
                    if a.get("name"):
                        times[a["name"]] = a["done"]           # an action's time is a name too ("after": "hurt")
            if step % SAMPLE_STEPS == 0:
                w.refresh(t)
                for name, cue in anchors.items():
                    if name not in times and (ct := cue_time(cue, w)) is not None:
                        times[name] = round(ct, 4)
                for e in expects:
                    name = e["name"]
                    lo, hi = resolve(e.get("after"), times), resolve(e.get("before"), times)
                    if e.get("never"):
                        if name in never_broken or (lo is not None and t < lo) or (hi is not None and t > hi):
                            continue
                        if (ct := cue_time(e["never"], w)) is not None and (lo is None or ct >= lo) and (hi is None or ct <= hi):
                            never_broken[name] = round(ct, 4)
                    elif not e.get("atEnd") and name not in times:
                        if isinstance(e.get("after"), str) and lo is None:
                            continue                       # its window opens on a beat not seen yet
                        ct = cue_time(e, w)
                        if ct is not None and (lo is None or ct >= lo - 1e-9) and (hi is None or ct <= hi + 1e-9):
                            times[name] = round(ct, 4)
                if next_golden is not None and t + 1e-9 >= next_golden:
                    rec["snapshots"].append(snapshot(w, t))
                    next_golden += golden_every
            if t + 1e-9 >= run_for:
                break
            # an action due before the next sample is landed on its exact step: single-step up to it
            due = [at for a in actions if not a.get("done") and (at := resolve(a.get("at"), times)) is not None]
            near = any(at < t + (SAMPLE_STEPS - step % SAMPLE_STEPS) * STEP_DT - 1e-9 for at in due)
            res = u.call("POST", "/sim/step", {"count": 1 if near else SAMPLE_STEPS - step % SAMPLE_STEPS})
            if "http" in res:
                raise RuntimeError(f"step at {t:.2f}: {str(res)[:300]}")
            step = round(((u.sim_time() or 0.0) - t0) / STEP_DT)
        # the end state
        for e in expects:
            if e.get("atEnd"):
                if cue_time(e, w) is not None:
                    times[e["name"]] = round(w.t, 4)
        if golden_every and (not rec["snapshots"] or rec["snapshots"][-1]["t"] < w.t - 1e-9):
            rec["snapshots"].append(snapshot(w, w.t))
        rec["steps"] = step
        rec["simEnd"] = round(w.t, 4)
        rec["clockDrift"] = round((u.sim_time() or 0.0) - t0 - step * STEP_DT, 6)
        rec["shots"] = [{k: s.get(k) for k in ("seq", "firedAt", "shooter", "target", "hit", "outcome", "penetration", "weaponIndex")}
                        for _, s in sorted(w.shots.items())]
        rec["blasts"] = [{k: b.get(k) for k in ("seenAt", "shooter", "burst", "warheadSource")} for b in w.blasts]
    except Exception as ex:
        rec["notes"].append(f"error: {type(ex).__name__}: {ex}")
    finally:
        u.stop(proc)
    rec["wall"] = round(time.time() - wall, 1)
    rec["beats"] = {k: v for k, v in times.items() if k in anchors or any(a.get("name") == k for a in actions)}
    for e in expects:
        name = e["name"]
        if e.get("never"):
            ok, seen = name not in never_broken, never_broken.get(name)
        else:
            ok, seen = name in times, times.get(name)
        rec["verdicts"].append({"name": name, "ok": ok and not any(n.startswith("error") for n in rec["notes"]), "t": seen})
    return rec


def snapshot(w, t):
    return {"t": round(t, 4), "units": {n: w.comps(n) for n in sorted(w.ids)}}


# ── the golden ──────────────────────────────────────────────────────────────────────────────────────────────────────

def golden_of(rec):
    return {"$doc": "scenario golden — docs/DESIGN_Scenario_Regression.md; rewrite with scripts/scenario-check.py --capture <scenario>",
            "scenario": rec["scenario"], "steps": rec["steps"], "beats": rec["beats"],
            "verdicts": {v["name"]: v["t"] for v in rec["verdicts"]},
            "shots": rec.get("shots", []), "blasts": rec.get("blasts", []), "snapshots": rec["snapshots"]}


def diff(a, b, path=""):
    """Every difference between two JSON values (numbers within TOLERANCE are equal), as (path, a, b)."""
    if isinstance(a, dict) and isinstance(b, dict):
        out = []
        for k in sorted(set(a) | set(b)):
            out += diff(a.get(k), b.get(k), f"{path}.{k}" if path else k) if k in a and k in b else [(f"{path}.{k}", a.get(k, "<absent>"), b.get(k, "<absent>"))]
        return out
    if isinstance(a, list) and isinstance(b, list):
        out = [] if len(a) == len(b) else [(f"{path}.length", len(a), len(b))]
        for i, (x, y) in enumerate(zip(a, b)):
            out += diff(x, y, f"{path}[{i}]")
        return out
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool) and not isinstance(b, bool):
        return [] if math.isclose(a, b, abs_tol=TOLERANCE) else [(path, a, b)]
    return [] if a == b else [(path, a, b)]


def first_difference(golden, run_golden):
    """The EARLIEST snapshot that differs and what in it — the question a red golden must answer first."""
    report = []
    for key in ("steps", "beats", "verdicts"):
        report += [f"{key}: {p} golden {x} run {y}" for p, x, y in diff(golden.get(key), run_golden.get(key), key)]
    shots = diff(golden.get("shots"), run_golden.get("shots"), "shots")
    if shots:
        report.append(f"shots: {len(shots)} differences, first {shots[0][0]}: golden {shots[0][1]} run {shots[0][2]}")
    for sa, sb in zip(golden.get("snapshots", []), run_golden.get("snapshots", [])):
        d = diff(sa, sb, f"t={sa.get('t')}")
        if d:
            units = sorted({p.split(".")[2] for p, _, _ in d if p.count(".") >= 2})
            report.append(f"first differing snapshot at t={sa.get('t')} s: {len(d)} values in {units[:6]}{'…' if len(units) > 6 else ''}")
            report += [f"    {p}: golden {str(x)[:80]} run {str(y)[:80]}" for p, x, y in d[:6]]
            break
    if len(golden.get("snapshots", [])) != len(run_golden.get("snapshots", [])):
        report.append(f"snapshot count: golden {len(golden.get('snapshots', []))} run {len(run_golden.get('snapshots', []))}")
    return report


# ── main ────────────────────────────────────────────────────────────────────────────────────────────────────────────

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("scenarios", nargs="*")
    ap.add_argument("--all", action="store_true", help="every scenario with a checks.json")
    ap.add_argument("--capture", action="store_true", help="write the golden from this run (also SCENARIO_GOLDEN_CAPTURE=1)")
    ap.add_argument("--port", type=int, default=8141)
    ap.add_argument("--out", help="also write each run's full record here (a directory)")
    args = ap.parse_args()
    capture = args.capture or os.environ.get("SCENARIO_GOLDEN_CAPTURE") == "1"
    u.BASE = f"http://localhost:{args.port}"
    names = args.scenarios or []
    if args.all:
        names = sorted(n for n in os.listdir(os.path.join(REPO, "scenarios")) if os.path.exists(os.path.join(REPO, "scenarios", n, "checks.json")))
    if not names:
        ap.error("name a scenario, or --all")
    failed = []
    for s in names:
        checks = json.load(open(os.path.join(REPO, "scenarios", s, "checks.json")))
        print(f"== {s}", flush=True)
        rec = run(s, checks, args.port)
        if args.out:
            os.makedirs(args.out, exist_ok=True)
            json.dump(rec, open(os.path.join(args.out, s + ".run.json"), "w"), indent=1, sort_keys=True)
        for n in rec["notes"]:
            print(f"  NOTE  {n}")
        for v in rec["verdicts"]:
            print(f"  {'PASS' if v['ok'] else 'FAIL'}  {v['name']}" + (f"  (t {v['t']} s)" if v["t"] is not None else ""))
        bad = [v for v in rec["verdicts"] if not v["ok"]] or [n for n in rec["notes"] if n.startswith("error")]
        gpath = os.path.join(GOLDENS, s + ".json")
        if checks.get("golden") is False:
            print("  GOLDEN  none (a demo: conditions only)")
        elif capture:
            if bad:
                print("  GOLDEN  NOT written — a golden of a failing run would bless the failure")
            else:
                os.makedirs(GOLDENS, exist_ok=True)
                with open(gpath, "w") as fh:
                    json.dump(golden_of(rec), fh, indent=1, sort_keys=True)
                    fh.write("\n")
                print(f"  GOLDEN  written {os.path.relpath(gpath, REPO)} ({os.path.getsize(gpath) // 1024} KB) — read its diff before committing")
        elif not os.path.exists(gpath):
            print("  GOLDEN  missing — capture it with --capture")
            bad = bad or ["golden missing"]
        else:
            report = first_difference(json.load(open(gpath)), json.loads(json.dumps(golden_of(rec))))
            if report:
                print("  GOLDEN  DIFFERENT")
                for line in report:
                    print("    " + line)
                bad = bad or ["golden"]
            else:
                print(f"  GOLDEN  SAME ({len(rec['snapshots'])} snapshots, {len(rec.get('shots', []))} rounds)")
        print(f"  {'FAIL' if bad else 'PASS'}  {s} — {rec['steps']} steps, sim {rec.get('simEnd')} s, wall {rec['wall']} s, clock drift {rec.get('clockDrift')}",
              flush=True)
        if bad:
            failed.append(s)
    print(f"\n{len(names) - len(failed)} of {len(names)} scenarios pass" + (f"; failing: {failed}" if failed else ""))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
