#!/usr/bin/env python3
"""Film a scenario's 3-D map — the scene alone, no panels — into an MP4.

⭐ CE-3289 (docs/DESIGN_Map_3D_Mode.md §6ab, R-3032). Launches a ClusterRunner --mode all under Xvfb (the demo checker's launch), loads the
scenario, lets it run for --warmup seconds, PAUSES it, then for every film frame: holds the camera on the shot (POST /film/shot), writes the
scene (POST /film/capture) and steps the sim by one film frame (POST /sim/step). The frames are a moment of SIM time each, so the film is
smooth however slow the software renderer is. ffmpeg joins them.
⭐ CE-4038 (docs/DESIGN_Film_Director.md D-9): the shot math is the ENGINE's (ShotEvaluator, through POST /film/shot {spec, t}) — this
script keeps no copy. ⭐ --take NAME films a scenario's saved take with the editor's film director instead (one movie, captions, sound):
  scripts/film.py ua-tank-ammo --take main --out /tmp/tank.mp4

  scripts/film.py ua-tank-ammo --follow Tank --distance 30 --pitch -15 --yaw 250 --orbit 6 --seconds 10 --out /tmp/tank.mp4
  scripts/film.py ua-tank-ammo --over Tank --toward "Enemy Tank" --back 22 --height 7 --seconds 10

Shots (one per run):
  --follow NAME   the camera looks at NAME (a name or a network id) from --distance at --yaw / --pitch; --orbit turns it (deg / s)
  --over NAME --toward NAME2   over NAME's shoulder at NAME2: --back metres behind, --height up, looking --ahead of the way to NAME2
  --look-at X,Y,Z  a fixed point, from --distance at --yaw / --pitch (--orbit turns it)

A PLAN (--plan plan.json) cuts between shots: a list of segments, each {"at": sim seconds, "seconds": n, <the shot's options>}, e.g.
  [{"at": 9.3, "seconds": 2, "over": "Tank", "toward": "Enemy Tank", "back": 16, "height": 5},
   {"at": 18.9, "seconds": 2.4, "follow": "Enemy Tank", "distance": 24, "yaw": 180, "pitch": -10}]
Between segments the sim gets to the next "at" (a cut): --mode editor STEPS there (frame-exact, the same every run); --mode all
plays in real time (overshoots up to ~0.1 s). Option names are the flags' without the dashes.
"""
import argparse, importlib.util, json, os, shutil, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("udc", os.path.join(HERE, "utility-demo-check.py"))
udc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(udc)


def check_names(seg, names):
    """Every unit a segment names is in the scenario (a name or a network id)."""
    for key in ("follow", "over", "toward"):
        who = getattr(seg, key, None)
        if who is not None and not str(who).isdigit() and who not in names:
            sys.exit(f"no entity named {who!r} — the scenario has: {sorted(names)}")


def must(resp, what):
    if not isinstance(resp, dict) or resp.get("http") not in (None, 200) or resp.get("ok") is False:
        sys.exit(f"{what} failed: {json.dumps(resp)[:400]}")
    return udc.data(resp)


def spec_of(seg):
    """⭐ CE-4038 D-9 — the segment as a take's shot (docs/DESIGN_Film_Director.md §4): the ENGINE evaluates it (ShotEvaluator, through
    POST /film/shot {spec, t}) — the over-the-shoulder and orbit math live there once, not here. Units go by name."""
    spec = {"at": 0, "seconds": max(float(seg.seconds), 0.001)}
    for key in ("follow", "over", "toward"):
        if getattr(seg, key, None) is not None:
            spec[key] = str(getattr(seg, key))
    if getattr(seg, "look_at", None) is not None:
        spec["lookAt"] = [float(v) for v in str(seg.look_at).split(",")]
    for key in ("distance", "yaw", "pitch", "orbit", "lift", "back", "height", "ahead"):
        spec[key] = float(getattr(seg, key))
    if getattr(seg, "fov", None):
        spec["fov"] = float(seg.fov)
    return spec


SHOT_KEYS = ("follow", "over", "toward", "look_at", "distance", "yaw", "pitch", "orbit", "lift", "back", "height", "ahead", "fov")


def enter_preview_paused():
    """⭐ --mode editor: a loaded editor sits in EDIT, where the clock does not run and every step is dropped — enter preview,
    paused, so stepping advances a fixed dt (scripts/ui-snapshot.py, CE-202: two editor runs stepped alike are byte-identical)."""
    must(udc.call("POST", "/preview/enter", {"startPaused": True}), "preview/enter")


def run_to(t, step_fps, exact):
    """Brings the sim to sim time t (a cut). Already past t: nothing. ⭐ exact (--mode editor): STEPS there — frame-exact, and the
    same frame in every run; otherwise plays in real time and pauses (overshoots up to ~0.1 s)."""
    now = udc.sim_time() or 0.0
    if now >= t - 0.5 / step_fps:
        return
    if exact:
        left = int(round((t - now) * 60))
        while left > 0:
            n = min(60, left)
            must(udc.call("POST", "/sim/step", {"count": n}), "step to the cut")
            left -= n
        return
    must(udc.call("POST", "/sim/play", {}), "play")
    udc.wait_for(lambda: (udc.sim_time() or 0.0) >= t, (t - now) * 20 + 60, every=0.05)
    must(udc.call("POST", "/sim/pause", {}), "pause")


def segments_of(args):
    if args.plan:
        plan = json.load(open(args.plan))
        segs = []
        for raw in plan:
            seg = argparse.Namespace(**vars(args))
            for k, v in raw.items():
                key = k.replace("-", "_")
                if key not in SHOT_KEYS + ("at", "seconds"):
                    sys.exit(f"unknown plan key {k!r}")
                setattr(seg, key, v)
            for k in ("follow", "over", "toward", "look_at"):
                if k not in raw and k.replace("_", "-") not in raw:
                    setattr(seg, k, None)
            seg.at = float(raw.get("at", 0.0))
            segs.append(seg)
        return segs
    args.at = args.warmup
    return [args]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("scenario")
    ap.add_argument("--out", default=None, help="the MP4 (default /tmp/film-<scenario>.mp4)")
    ap.add_argument("--seconds", type=float, default=8.0, help="film length in sim seconds")
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--size", default="1280x720")
    ap.add_argument("--supersample", type=int, default=1)
    ap.add_argument("--warmup", type=float, default=0.0, help="sim seconds to run (in real time) before filming")
    ap.add_argument("--follow")
    ap.add_argument("--over")
    ap.add_argument("--toward")
    ap.add_argument("--look-at", dest="look_at")
    ap.add_argument("--distance", type=float, default=30.0)
    ap.add_argument("--yaw", type=float, default=0.0, help="degrees from east, counter-clockwise")
    ap.add_argument("--pitch", type=float, default=-20.0)
    ap.add_argument("--orbit", type=float, default=0.0, help="degrees per second")
    ap.add_argument("--lift", type=float, default=1.5)
    ap.add_argument("--fov", type=float, default=None, help="vertical field of view, degrees (default 45; narrow = telephoto)")
    ap.add_argument("--back", type=float, default=20.0)
    ap.add_argument("--height", type=float, default=6.0)
    ap.add_argument("--ahead", type=float, default=0.35, help="share of the way to --toward the camera looks at")
    ap.add_argument("--port", type=int, default=8111)
    ap.add_argument("--mode", default="all", help="the ClusterRunner mode: all (the cluster, one process) or editor (the editor's own world, no network)")
    ap.add_argument("--keep-frames", action="store_true")
    ap.add_argument("--attach", action="store_true", help="use a cluster already listening on --port (do not launch or load)")
    ap.add_argument("--plan", help="a JSON list of segments (see above) instead of one shot")
    ap.add_argument("--take", help="⭐ CE-4038 — film the scenario's saved TAKE (scenarios/<s>/takes/<take>.take.json) with the editor's film "
                                   "director: one movie, captions, sound (implies --mode editor; the shot options are ignored)")
    args = ap.parse_args()
    if args.take:
        return film_take(args)
    segments = segments_of(args)
    for seg in segments:
        if sum(x is not None for x in (seg.follow, seg.over, seg.look_at)) != 1:
            sys.exit("each shot needs exactly one of: follow, over (with toward) or look-at")
        if seg.over and not seg.toward:
            sys.exit("over needs toward")
    if not shutil.which("ffmpeg"):
        sys.exit("ffmpeg is not installed")
    w, h = (int(v) for v in args.size.lower().split("x"))
    out = os.path.abspath(args.out or f"/tmp/film-{args.scenario}.mp4")
    frames_dir = os.path.splitext(out)[0] + "-frames"
    shutil.rmtree(frames_dir, ignore_errors=True)
    os.makedirs(frames_dir)

    udc.BASE = f"http://localhost:{args.port}"
    proc = None
    try:
        if not args.attach:
            proc = udc.launch(args.port, args.mode)
            udc.load(args.scenario)
        names = udc.ids_by_name()
        for seg in segments:
            check_names(seg, names)
        steps = max(1, round(60 / args.fps))     # the sim runs 60 frames a second; one film frame = this many
        exact = args.mode == "editor"
        if exact:
            enter_preview_paused()
        must(udc.call("POST", "/sim/pause", {}), "pause")
        started, i = time.time(), 0
        for seg in segments:
            run_to(seg.at, args.fps, exact)
            spec = spec_of(seg)
            count = int(float(seg.seconds) * args.fps)
            for k in range(count + 1):
                r = udc.call("POST", "/film/shot", {"spec": spec, "t": k / args.fps})   # ⭐ the engine's shot math (D-9)
                if isinstance(r, dict) and r.get("http") not in (None, 200, 404):
                    must(r, f"shot {i}")
                if k == 0:
                    # ⚠ a NEW shot's first frame: the weather layer read the atmosphere before the director moved the camera this frame
                    #   (the film layer updates last) — one throwaway capture lets it settle (Map 3D Mode §6ab as built)
                    must(udc.call("POST", "/film/capture", {"path": os.path.join(frames_dir, "settle.png"), "width": 64, "height": 64}), "settle")
                    continue
                path = os.path.join(frames_dir, f"{i:05d}.png")
                must(udc.call("POST", "/film/capture", {"path": path, "width": w, "height": h, "supersample": args.supersample}), f"capture {i}")
                must(udc.call("POST", "/sim/step", {"count": steps}), f"step {i}")
                i += 1
                if i % args.fps == 0:
                    print(f"   frame {i}  sim {udc.sim_time()}  {time.time() - started:.0f} s", flush=True)
        count = i
        if os.path.exists(os.path.join(frames_dir, "settle.png")):
            os.remove(os.path.join(frames_dir, "settle.png"))
        udc.call("DELETE", "/film/shot")

        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(args.fps), "-i", os.path.join(frames_dir, "%05d.png"),
                        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", "-movflags", "+faststart", out], check=True)
        print(f"   {out}  ({count} frames, {count / args.fps:.1f} s, {time.time() - started:.0f} s to film)")
    finally:
        if proc is not None:
            udc.stop(proc)
        if not args.keep_frames:
            shutil.rmtree(frames_dir, ignore_errors=True)


def film_take(args):
    """⭐ CE-4038 (docs/DESIGN_Film_Director.md §3b) — the director films the take: POST /director/play {scenario, take, capture}, then wait
    for it to report Done; it writes the frames, the sound track and ONE movie."""
    out = os.path.abspath(args.out or f"/tmp/film-{args.scenario}-{args.take}.mp4")
    work = os.path.splitext(out)[0] + "-work"
    udc.BASE = f"http://localhost:{args.port}"
    proc = None if args.attach else udc.launch(args.port, "editor")
    try:
        must(udc.call("POST", "/director/play", {"scenario": args.scenario, "take": args.take, "capture": {
            "dir": work, "fps": args.fps, "size": args.size, "supersample": args.supersample, "movie": out}}), "director/play")
        started = time.time()
        while True:
            st = must(udc.call("GET", "/director/status"), "director/status") or {}
            if st.get("phase") in ("Done", "Stopped"):
                break
            time.sleep(2)
        for r in st.get("reports") or []:
            print(f"   {r.get('scenario')}/{r.get('take')}: {r.get('status')}  frames {r.get('frames')}  missing {r.get('missing')}  {r.get('problems')}")
        if st.get("error"):
            sys.exit(f"the director failed: {st['error']}")
        print(f"   {st.get('movie')}  ({time.time() - started:.0f} s to film)")
    finally:
        if proc is not None:
            udc.stop(proc)
        if not args.keep_frames:
            shutil.rmtree(os.path.join(work, "frames"), ignore_errors=True)


if __name__ == "__main__":
    main()
