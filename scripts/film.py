#!/usr/bin/env python3
"""Film a scenario's 3-D map — the scene alone, no panels — into an MP4.

⭐ CE-3289 (docs/DESIGN_Map_3D_Mode.md §6ab, R-3032). Launches a ClusterRunner --mode all under Xvfb (the demo checker's launch), loads the
scenario, lets it run for --warmup seconds, PAUSES it, then for every film frame: holds the camera on the shot (POST /film/shot), writes the
scene (POST /film/capture) and steps the sim by one film frame (POST /sim/step). The frames are a moment of SIM time each, so the film is
smooth however slow the software renderer is. ffmpeg joins them.

  scripts/film.py ua-tank-ammo --follow Tank --distance 30 --pitch -15 --yaw 250 --orbit 6 --seconds 10 --out /tmp/tank.mp4
  scripts/film.py ua-tank-ammo --over Tank --toward "Enemy Tank" --back 22 --height 7 --seconds 10

Shots (one per run):
  --follow NAME   the camera looks at NAME (a name or a network id) from --distance at --yaw / --pitch; --orbit turns it (deg / s)
  --over NAME --toward NAME2   over NAME's shoulder at NAME2: --back metres behind, --height up, looking --ahead of the way to NAME2
  --look-at X,Y,Z  a fixed point, from --distance at --yaw / --pitch (--orbit turns it)

A PLAN (--plan plan.json) cuts between shots: a list of segments, each {"at": sim seconds, "seconds": n, <the shot's options>}, e.g.
  [{"at": 9.3, "seconds": 2, "over": "Tank", "toward": "Enemy Tank", "back": 16, "height": 5},
   {"at": 18.9, "seconds": 2.4, "follow": "Enemy Tank", "distance": 24, "yaw": 180, "pitch": -10}]
Between segments the sim runs in real time to the next "at" (a cut). Option names are the flags' without the dashes.
"""
import argparse, importlib.util, json, math, os, shutil, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("udc", os.path.join(HERE, "utility-demo-check.py"))
udc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(udc)


def ids():
    return udc.ids_by_name()


def resolve(who, names):
    """A name or a network id → the network id."""
    if who is None:
        return None
    if str(who).isdigit():
        return int(who)
    if who not in names:
        sys.exit(f"no entity named {who!r} — the scenario has: {sorted(names)}")
    return names[who]


def position(nid):
    c = (udc.data(udc.call("GET", f"/entities/{nid}")) or {}).get("Components") or {}
    p = (c.get("SimTransform") or {}).get("Position")
    if isinstance(p, list):
        return tuple(float(v) for v in p[:3])
    if isinstance(p, dict):
        return tuple(float(p.get(k, 0) or 0) for k in ("X", "Y", "Z"))
    return None


def must(resp, what):
    if not isinstance(resp, dict) or resp.get("http") not in (None, 200) or resp.get("ok") is False:
        sys.exit(f"{what} failed: {json.dumps(resp)[:400]}")
    return udc.data(resp)


def shot_for(args, t, follow, over, toward):
    """The shot at film time t (s)."""
    shot = _shot_for(args, t, follow, over, toward)
    if shot is not None and getattr(args, "fov", None):
        shot["fovDeg"] = args.fov
    return shot


def _shot_for(args, t, follow, over, toward):
    yaw = args.yaw + args.orbit * t
    if follow is not None:
        return {"follow": follow, "distance": args.distance, "yawDeg": yaw, "pitchDeg": args.pitch, "lift": args.lift}
    if over is not None:
        a, b = position(over), position(toward)
        if a is None or b is None:
            return None
        dx, dy = b[0] - a[0], b[1] - a[1]
        d = math.hypot(dx, dy) or 1.0
        ux, uy = dx / d, dy / d
        eye = [a[0] - ux * args.back, a[1] - uy * args.back, a[2] + args.height]
        target = [a[0] + ux * d * args.ahead, a[1] + uy * d * args.ahead, a[2] + args.lift]
        return {"eye": eye, "target": target}
    x, y, z = (float(v) for v in args.look_at.split(","))
    return {"lookAt": [x, y, z], "distance": args.distance, "yawDeg": yaw, "pitchDeg": args.pitch}


SHOT_KEYS = ("follow", "over", "toward", "look_at", "distance", "yaw", "pitch", "orbit", "lift", "back", "height", "ahead", "fov")


def run_to(t, step_fps):
    """Lets the sim run in real time to sim time t, then pauses (a cut). Already past t: nothing."""
    now = udc.sim_time() or 0.0
    if now >= t - 0.5 / step_fps:
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
    ap.add_argument("--keep-frames", action="store_true")
    ap.add_argument("--attach", action="store_true", help="use a cluster already listening on --port (do not launch or load)")
    ap.add_argument("--plan", help="a JSON list of segments (see above) instead of one shot")
    args = ap.parse_args()
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
            proc = udc.launch(args.port)
            udc.load(args.scenario)
        names = ids()
        steps = max(1, round(60 / args.fps))     # the sim runs 60 frames a second; one film frame = this many
        must(udc.call("POST", "/sim/pause", {}), "pause")
        started, i = time.time(), 0
        for seg in segments:
            run_to(seg.at, args.fps)
            follow = resolve(seg.follow, names)
            over, toward = resolve(seg.over, names), resolve(seg.toward, names)
            count = int(float(seg.seconds) * args.fps)
            for k in range(count + 1):
                shot = shot_for(seg, k / args.fps, follow, over, toward)
                if shot is not None:
                    must(udc.call("POST", "/film/shot", shot), f"shot {i}")
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


if __name__ == "__main__":
    main()
