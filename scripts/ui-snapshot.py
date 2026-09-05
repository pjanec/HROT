#!/usr/bin/env python3
"""
Capture the editor's UI *as data* — every perspective's panels and each panel's
view-model SHAPE — so a refactor can be proven not to have lost a panel.

    python3 scripts/ui-snapshot.py --port 8131 --out /tmp/ui-before.json
    python3 scripts/ui-snapshot.py --port 8131 --out /tmp/ui-after.json
    python3 scripts/ui-snapshot.py --diff /tmp/ui-before.json /tmp/ui-after.json

⭐⭐⭐ WHY SHAPE AND NOT VALUES. A panel's model carries live data — frame counters,
positions, sim time — so comparing values would flag every run as different and the
check would be switched off within a day. What must NOT change across a composition
refactor is:

  ① which windows are REGISTERED per perspective   (the panel exists at all)
  ② which of them CAPTURED a view-model            (the panel actually drew)
  ③ the KINDS map                                  (the logical panel identities)
  ④ each model's FIELD SHAPE                       (it still shows the same things)

⛔ THE TRAP THIS TOOL EXISTS TO AVOID, and it is documented in the API's own guide:
a LAZILY registered window — one created on first activation of its perspective — is
absent from /panels until that perspective has been visited. A capture that does not
switch to every perspective first UNDER-REPORTS, and then a genuinely lost panel looks
identical to a panel that was simply never woken. So this walks every perspective.

📄 tools/ai-debug-mcp/SKILL.md, Group T.
"""
import argparse
import json
import sys
import time
import urllib.request


def call(port, path, method="GET", body=None, timeout=20):
    url = f"http://localhost:{port}{path}"
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    # ⚠ localhost, never 127.0.0.1 — the listener binds the HOSTNAME and matches on it,
    #   so 127.0.0.1 returns 404 on every route (RUNBOOK §2.1).
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read().decode())


def unwrap(resp):
    """The API wraps payloads in an ok/data envelope."""
    if isinstance(resp, dict) and "data" in resp and "ok" in resp:
        return resp["data"]
    return resp


def shape(node, path="", out=None):
    """The set of field PATHS in a model, with leaf TYPES — never leaf values."""
    if out is None:
        out = set()
    if isinstance(node, dict):
        for k in sorted(node.keys()):
            shape(node[k], f"{path}.{k}" if path else k, out)
    elif isinstance(node, list):
        # A list's length is data; its ELEMENT shape is structure. Union the elements so
        # an empty list and a populated one differ only by the "[]" marker, not by noise.
        if not node:
            out.add(f"{path}[] (empty)")
        else:
            for item in node:
                shape(item, f"{path}[]", out)
    else:
        out.add(f"{path}: {type(node).__name__}")
    return out


def warm_up(port, scenario, steps):
    """Put the world in a DETERMINISTIC state before reading panels.

    ⭐⭐⭐ Why this makes VALUE comparison possible at all. Panels show live data, so two runs
    only agree if the simulation reached the identical state. Two things make that true:
    CE-202 made the behaviour RNG reproducible, and stepping advances a FIXED dt instead of
    the wall clock. ⇒ measured: two separate --mode editor processes, 1000 steps each, gave
    8/8 byte-identical entities.

    ⚠ SINGLE-WORLD ONLY. On --mode all the same measurement gave 5/8: DDS delivery order
    between kernels is not reproducible and CE-202 does not claim to fix it. So run the value
    comparison on the editor, and keep the shape comparison for the cluster.
    """
    call(port, "/scenario/load/live", "POST", {"name": scenario, "waitForReady": True}, timeout=60)

    before = unwrap(call(port, "/status")).get("simTime") or 0.0
    remaining = steps
    while remaining > 0:
        batch = min(25, remaining)
        try:
            call(port, "/sim/step", "POST", {"count": batch}, timeout=30)
        except Exception as exc:  # noqa: BLE001
            raise SystemExit(
                f"REFUSING TO CAPTURE: /sim/step failed ({exc}). A value comparison against a world "
                "that never advanced compares two frozen worlds and reports them identical.") from exc
        remaining -= batch
    after = unwrap(call(port, "/status")).get("simTime") or 0.0

    # ⛔⛔⛔ THE VACUITY GUARD, and it is here because I shipped exactly this mistake.
    # CE-202 first claimed "--mode editor: 8 of 8 entities byte-identical" across two runs. The
    # steps had never landed — /sim/step answers "the master never entered the step barrier and the
    # clock never advanced" on that host — so simTime was 0.0 in BOTH runs and the comparison was
    # between two frozen worlds. Identical, and worthless.
    # ⇒ a warm-up that cannot prove the clock moved must REFUSE, not proceed quietly.
    if after <= before:
        raise SystemExit(
            f"REFUSING TO CAPTURE: simTime did not advance ({before} -> {after}) after {steps} steps. "
            "Every value would compare equal for the wrong reason. Check that this host can actually "
            "step — the editor's clock starts in Deterministic mode and its step barrier may need "
            "play/preview first.")

    print(f"  warmed up: {scenario}, {steps} fixed steps, simTime {before} -> {after}")


def capture(port, settle, values=False):
    perspectives = unwrap(call(port, "/perspectives"))
    names = perspectives.get("perspectives") or []
    snap = {"perspectives": {}, "union": {"registered": [], "captured": [], "kinds": {}}}

    reg_all, cap_all, kinds_all = set(), set(), {}

    for name in names:
        got = unwrap(call(port, "/perspective", "POST", {"name": name}))
        current = got.get("current")
        if current != name:
            print(f"  ! switch to {name!r} reported current={current!r} — recording as unreachable",
                  file=sys.stderr)
            snap["perspectives"][name] = {"switched": False, "current": current}
            continue

        # The new perspective publishes its panels on the NEXT frame.
        time.sleep(settle)

        panels = unwrap(call(port, "/panels"))
        registered = sorted(panels.get("registered") or [])
        captured = sorted(panels.get("captured") or [])
        kinds = {k: sorted(v) for k, v in sorted((panels.get("kinds") or {}).items())}

        models = {}
        for pid in captured:
            try:
                # ⚠ The map's gizmo buffer is NOT served by the generic /panels/{id} route — it has
                #   its own /panels/_gizmo. Measured: the generic route 404s for it on every
                #   perspective, which reads exactly like a missing panel if you do not special-case it.
                if pid.endswith("_gizmo"):
                    got = unwrap(call(port, "/panels/_gizmo"))
                    # ⭐ Its primitive COUNTS are live data (and mostly grid). What is structural is
                    #   WHICH SHAPES the map can draw, so reduce to the distinct shape names.
                    prims = got.get("primitives") or []
                    models[pid] = {
                        "panelKind": "gizmoFrame",
                        "shape": sorted({f"shape:{q.get('shape')}" for q in prims}),
                    }
                    continue

                got = unwrap(call(port, f"/panels/{pid}"))
                entry = {
                    "panelKind": got.get("panelKind"),
                    "shape": sorted(shape(got.get("model"))),
                }
                if values:
                    # ⭐ The whole model, verbatim. A shape check proves the panel still shows the
                    #   same FIELDS; this proves it shows the same THINGS.
                    entry["model"] = got.get("model")
                models[pid] = entry
            except Exception as exc:  # noqa: BLE001 - a panel that refuses is a finding, not a crash
                models[pid] = {"error": str(exc)}

        snap["perspectives"][name] = {
            "switched": True,
            "registered": registered,
            "captured": captured,
            "kinds": kinds,
            "models": models,
        }
        reg_all |= set(registered)
        cap_all |= set(captured)
        for k, v in kinds.items():
            kinds_all.setdefault(k, set()).update(v)
        print(f"  {name:12} registered={len(registered):3} captured={len(captured):3} kinds={len(kinds):3}")

    snap["union"] = {
        "registered": sorted(reg_all),
        "captured": sorted(cap_all),
        "kinds": {k: sorted(v) for k, v in sorted(kinds_all.items())},
    }
    return snap


def diff(before, after):
    """Report only what a refactor must not do: lose a panel, or change what it shows."""
    problems = []

    b, a = before["union"], after["union"]
    for field in ("registered", "captured"):
        lost = sorted(set(b[field]) - set(a[field]))
        gained = sorted(set(a[field]) - set(b[field]))
        if lost:
            problems.append(f"LOST {field}: {lost}")
        if gained:
            problems.append(f"gained {field} (not a defect, but state it): {gained}")

    lost_kinds = sorted(set(b["kinds"]) - set(a["kinds"]))
    if lost_kinds:
        problems.append(f"LOST panel kinds: {lost_kinds}")

    for name, bp in before["perspectives"].items():
        ap = after["perspectives"].get(name)
        if ap is None:
            problems.append(f"LOST perspective: {name}")
            continue
        if bp.get("switched") and not ap.get("switched"):
            problems.append(f"perspective {name} no longer switchable")
            continue
        if not bp.get("switched"):
            continue
        for pid, bm in (bp.get("models") or {}).items():
            am = (ap.get("models") or {}).get(pid)
            if am is None:
                problems.append(f"{name}/{pid}: panel stopped publishing a model")
                continue
            if bm.get("panelKind") != am.get("panelKind"):
                problems.append(
                    f"{name}/{pid}: kind {bm.get('panelKind')} -> {am.get('panelKind')}")
            missing = sorted(set(bm.get("shape") or []) - set(am.get("shape") or []))
            if missing:
                problems.append(f"{name}/{pid}: model lost fields {missing[:8]}"
                                + (" ..." if len(missing) > 8 else ""))

            # ⭐⭐ VALUE comparison, only when BOTH snapshots carry models. A shape-only baseline
            #    compared against a value capture must not silently report "same".
            if "model" in bm and "model" in am and bm["model"] != am["model"]:
                problems.append(f"{name}/{pid}: VALUES DIFFER")
    return problems


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8131)
    ap.add_argument("--out")
    ap.add_argument("--settle", type=float, default=1.5,
                    help="seconds to wait after a perspective switch before reading panels")
    ap.add_argument("--diff", nargs=2, metavar=("BEFORE", "AFTER"))
    ap.add_argument("--values", action="store_true",
                    help="also record every panel's full model, for an exact value comparison")
    ap.add_argument("--scenario", help="load this scenario and step before capturing")
    ap.add_argument("--steps", type=int, default=1000,
                    help="fixed-dt steps to advance before capturing (with --scenario)")
    args = ap.parse_args()

    if args.diff:
        before = json.load(open(args.diff[0]))
        after = json.load(open(args.diff[1]))
        problems = diff(before, after)
        if not problems:
            print("UI SNAPSHOT IDENTICAL — every panel still registered, still drawing, "
                  "same kinds, same model shape.")
            return 0
        print("UI SNAPSHOT DIFFERENCES:")
        for p in problems:
            print(" ", p)
        return 1 if any(p.startswith(("LOST", "perspective")) or "stopped publishing" in p
                        or "model lost fields" in p for p in problems) else 0

    if args.scenario:
        warm_up(args.port, args.scenario, args.steps)
    snap = capture(args.port, args.settle, values=args.values)
    text = json.dumps(snap, indent=2, sort_keys=True)
    if args.out:
        open(args.out, "w").write(text)
        print(f"wrote {args.out} — {len(snap['union']['registered'])} registered, "
              f"{len(snap['union']['captured'])} captured, {len(snap['union']['kinds'])} kinds")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
