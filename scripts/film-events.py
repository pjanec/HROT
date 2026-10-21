#!/usr/bin/env python3
"""When does something happen in a scenario? — the sim times of every shot fired (and what it hit), to plan a film's cuts.

⭐ CE-3289 (docs/RUNBOOK_Filming.md). Launches a ClusterRunner --mode all under Xvfb, loads the scenario, plays it in real time and
prints each new shot as `sim <t>: <shooter> -> <target> <outcome>`, plus the start positions of the named entities. ⚠ Real-time play is
not frame-identical between runs: treat the times as ±0.5 s and put a cut that much before the event.

  scripts/film-events.py ua-tank-ammo --seconds 40
"""
import argparse, importlib.util, os, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("udc", os.path.join(HERE, "utility-demo-check.py"))
udc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(udc)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("scenario")
    ap.add_argument("--seconds", type=float, default=40.0, help="sim seconds to watch")
    ap.add_argument("--port", type=int, default=8111)
    args = ap.parse_args()
    udc.BASE = f"http://localhost:{args.port}"
    proc = udc.launch(args.port)
    try:
        udc.load(args.scenario)
        ids = udc.ids_by_name()
        name = {v: k for k, v in ids.items()}
        for n, nid in sorted(ids.items(), key=lambda kv: kv[1]):
            c = (udc.data(udc.call("GET", f"/entities/{nid}")) or {}).get("Components") or {}
            p = (c.get("SimTransform") or {}).get("Position")
            print(f"   {nid:>6}  {n:<24} {[round(float(v), 1) for v in p] if isinstance(p, list) else p}")
        udc.call("POST", "/sim/play", {})
        seen = set()
        while (udc.sim_time() or 0.0) < args.seconds:
            t = udc.sim_time() or 0.0
            for x in udc._if_shots_all():
                if x.get("seq") in seen:
                    continue
                seen.add(x.get("seq"))
                print(f"   sim {t:6.1f}: {name.get(x.get('shooter'), x.get('shooter'))} -> {name.get(x.get('target'), x.get('target'))} "
                      f"{x.get('outcome')}" + (f" (hit {name.get(x.get('hit'), x.get('hit'))})" if x.get("hit") else ""), flush=True)
            time.sleep(0.5)
    finally:
        udc.stop(proc)


if __name__ == "__main__":
    main()
