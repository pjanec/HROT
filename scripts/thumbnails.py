#!/usr/bin/env python3
"""Generate the pickers' thumbnails by driving an EDITOR over its existing HTTP routes — nothing new renders (CE-4268, docs/DESIGN_Add_Entity_Picker.md §2i).

  scripts/thumbnails.py --scenarios                      every scenario of the repo that has no still yet (writes scenarios/<name>/preview.jpg IN THE REPOSITORY)
  scripts/thumbnails.py --scenarios ua-tank-ammo hill-attack
  scripts/thumbnails.py --terrains                       ⭐ B-9 N6 — every terrain folder under assets/terrain that has no still yet (writes assets/terrain/<name>/preview.jpg IN THE REPOSITORY)
  scripts/thumbnails.py --terrains berlin test-town --force
  scripts/thumbnails.py --types 1001 1101                these TKB type ids (GET /tkb/types lists them); --types with no ids = every type
  scripts/thumbnails.py --scenarios a b --types 1001 --port 5195
  scripts/thumbnails.py --attach --port 5195 ...         use an editor already listening there (do not launch or stop one)

A scenario's still = its START as the editor shows it: POST /scenario/load/edit -> the entities' bounds (GET /entities, GET /entities/{id}) -> POST /film/shot
{eye, target} looking down ~45 degrees over them -> POST /film/capture {path, 512x288} -> DELETE /film/shot.
A type's still = one entity spawned (POST /entities/spawn) in a bare studio scenario (--studio, default test-move: no terrain), framed by distance, captured, removed (DELETE /entities/{id}).

⭐ CE-4271 (§2j) — THE ENTITY ATLAS (the only picture files allowed in git, R-4117): the type pictures are packed into JPEG pages embedded in Hrot.Presentation.
  scripts/thumbnails.py --pack                                  fold every loose <nas>/assets/thumbnails/generated/*.png into the atlas (a name already in it is replaced in its slot)
  scripts/thumbnails.py --types <id> --pack                     capture the type(s) (needs a running editor, below) AND pack them
  scripts/thumbnails.py --add-image <file> --name <TypeName>    a hand-made / downloaded picture: centre-crop to 16:9, scale to 320x180, pack — no editor needed
  scripts/thumbnails.py --atlas-list                            stems with page/col/row
  scripts/thumbnails.py --atlas-remove <TypeName>               free a slot (its cell goes black; the next add reuses it)
  Pages are 2048x2048 JPEG q85 of 320x180 cells (6 x 11 = 66 per page); a name keeps its slot; only a page whose pixels changed is re-encoded; the index (entities.atlas.json) has sorted keys.
  Needs Pillow (`py -3 -m pip install pillow` on Windows). Then commit the changed page(s) + the index and rebuild: they are embedded (step 4 of ThumbnailMap).
  Scenario stills are NOT in the atlas: --scenarios writes the REPOSITORY scenarios/<name>/preview.jpg (320x180 q85); a scenario with a preview.jpg/.png is skipped unless --force.

⛔ Types go into the NAS root (--nas, default /tmp/FDP_Temp/shared): assets/thumbnails/generated/<typeName>.png, then --pack folds them into the atlas.
⭐ Scenario stills go into the REPOSITORY (scenarios/<name>/preview.jpg); one that has a preview.jpg/.png is skipped unless --force. Never an edit of map.json.
The file stem of a type is its TKB Name sanitized exactly as ThumbnailMap.GeneratedFileName does: letters, digits . _ - kept, anything else _ (a name holding ".." is refused).
"""
import argparse, importlib.util, json, math, os, re, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
spec = importlib.util.spec_from_file_location("udc", os.path.join(HERE, "utility-demo-check.py"))
udc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(udc)

DEFAULT_NAS = "/tmp/FDP_Temp/shared"
W, H = 512, 288
FOV_DEG = 45.0
SETTLE_CAPTURES = 4


def generated_name(type_name):
    """ThumbnailMap.GeneratedFileName — the SAME rule (the engine reads what this writes)."""
    if not type_name or not type_name.strip() or ".." in type_name:
        return None
    return re.sub(r"[^A-Za-z0-9._-]", "_", type_name.strip())


def ok(resp):
    return isinstance(resp, dict) and resp.get("http") in (None, 200) and resp.get("ok") is not False


def body_of(resp):
    return udc.data(resp)


def say(resp):
    return json.dumps(resp)[:200]


def wait_loaded(timeout=120):
    """The editor reports a settled world: canOperate and a steady entity count."""
    end, prev, steady = time.time() + timeout, None, 0
    while time.time() < end and steady < 3:
        st = body_of(udc.call("GET", "/status")) or {}
        n = st.get("entityCount")
        steady = steady + 1 if st.get("canOperate") and n == prev else 0
        prev = n
        time.sleep(0.5)
    return steady >= 3


def load_edit(name):
    end = time.time() + 60
    while time.time() < end and not (body_of(udc.call("GET", "/status")) or {}).get("canOperate"):
        time.sleep(0.5)
    r = udc.call("POST", "/scenario/load/edit", {"name": name, "waitForReady": True, "discardUnsaved": True})
    if not ok(r):
        return False, "load: " + say(r)
    if not wait_loaded():
        return False, "the world did not settle"
    return True, ""


def positions(limit=160):
    """The entities' ground positions (a sample of at most `limit`): GET /entities then the SimTransform of each."""
    rows = body_of(udc.call("GET", "/entities", None)) or []
    placed = [r for r in rows if "SimTransform" in (r.get("components") or [])]
    # the CAST of the scenario, not its scenery or its camera: units (EntityInfo) when there are any, else what the file authored (EntityGuid), else everything placed
    for need in ("EntityInfo", "EntityGuid"):
        cast = [r for r in placed if need in (r.get("components") or [])]
        if cast:
            placed = cast
            break
    ids = [r["networkId"] for r in placed]
    if len(ids) > limit:
        step = len(ids) / limit
        ids = [ids[int(i * step)] for i in range(limit)]
    pts = []
    for nid in ids:
        comps = (body_of(udc.call("GET", f"/entities/{nid}")) or {}).get("Components") or {}
        p = (comps.get("SimTransform") or {}).get("Position")
        if p and len(p) == 3 and all(math.isfinite(v) for v in p):
            pts.append(tuple(p))
            SIZES[tuple(p)] = entity_size(comps)
    return pts


SIZES = {}   # a cast member's (height, length, width), by its position — filled by positions(), read by frame_scene()


def frame(points, pitch_deg=-50.0, yaw_deg=225.0, margin=1.15, min_radius=10.0):
    """eye/target looking down at pitch over the points' bounds (a bounding sphere, fitted to the vertical field of view)."""
    xs, ys, zs = zip(*points)
    cx, cy, cz = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, (min(zs) + max(zs)) / 2
    radius = max(min_radius, math.hypot(max(xs) - min(xs), max(ys) - min(ys)) / 2)
    dist = radius * margin / (math.tan(math.radians(FOV_DEG / 2)) * W / H)   # fitted to the tile's WIDTH (16:9): the bounding circle's radius fills the half-width / margin
    pitch, yaw = math.radians(pitch_deg), math.radians(yaw_deg)
    eye = (cx + dist * math.cos(pitch) * math.cos(yaw), cy + dist * math.cos(pitch) * math.sin(yaw), cz - dist * math.sin(pitch))
    return list(eye), [cx, cy, cz]


PERSON_M = 1.8          # a person's height (his PhysicsCollider.Height is 0 and his VehicleParams Length is the 0.6 m footprint)
PERSON_FOOT_M = 0.6     # a person's footprint
FILL = 0.92             # the projected box fills this share of the tile's limiting dimension — a small margin, never a speck (user, 2026-11-05)


def entity_size(comps):
    """(height, length, width) of a spawned entity in metres, from what it carries: PhysicsCollider.Height and VehicleParams.Length/Width (a tank: 2.23 / 6.95 x 3.59);
    a Pedestrian (Class) is a person: 1.8 m tall on a 0.6 m footprint. Falls back to the collider radius, else a 2 m box."""
    veh = comps.get("VehicleParams") or {}
    col = comps.get("PhysicsCollider") or {}
    if veh.get("Class") == "Pedestrian":
        return PERSON_M, PERSON_FOOT_M, PERSON_FOOT_M
    height = float(col.get("Height") or 0.0)
    length, width = float(veh.get("Length") or 0.0), float(veh.get("Width") or 0.0)
    if length <= 0 and col.get("Radius"):
        length = width = 2.0 * float(col["Radius"])
    if length <= 0:
        length = width = 2.0
    if width <= 0:
        width = length
    if height <= 0:
        height = max(1.0, 0.45 * length)
    return height, length, width


CLUSTER_M = 30.0        # a scenario is framed around its FIRST cast entity and the cast within this distance of it ...
CLUSTER_FALLBACK_M = 60.0   # ... unless that is a lone entity and the rest of the cast is within this: then all of it


def cluster_of(points):
    """The cast to frame: the first entity's neighbours within CLUSTER_M; if that is the first entity alone while everyone is within CLUSTER_FALLBACK_M of it, all of them."""
    first = points[0]
    near = [p for p in points if math.hypot(p[0] - first[0], p[1] - first[1]) <= CLUSTER_M]
    if len(near) == 1 and all(math.hypot(p[0] - first[0], p[1] - first[1]) <= CLUSTER_FALLBACK_M for p in points):
        return points
    return near


def heading_deg(comps):
    """A spawned entity's heading about Z, degrees from +X (counter-clockwise) — the engine's convention (SpawnPoints: yaw 0 = +X); from SimTransform.Rotation (x,y,z,w or {X,Y,Z,W}). Measured on the
    Rifleman: identity rotation faces +X (a camera behind him, at -X, sees his back)."""
    rot = (comps.get("SimTransform") or {}).get("Rotation")
    try:
        if isinstance(rot, dict):
            x, y, z, w = (float(rot.get(k, rot.get(k.upper(), 0.0))) for k in ("x", "y", "z", "w"))
        else:
            x, y, z, w = (float(v) for v in rot)
        return math.degrees(math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z)))
    except (TypeError, ValueError):
        return 0.0


def _basis(yaw, pitch):
    """The camera's forward / right / up for a camera looking from yaw (the eye's direction from the target) down at pitch."""
    fwd = (-math.cos(pitch) * math.cos(yaw), -math.cos(pitch) * math.sin(yaw), math.sin(pitch))      # eye -> target
    right = (fwd[1], -fwd[0], 0.0)
    n = math.hypot(right[0], right[1]) or 1.0
    right = (right[0] / n, right[1] / n, 0.0)
    up = (right[1] * fwd[2] - right[2] * fwd[1], right[2] * fwd[0] - right[0] * fwd[2], right[0] * fwd[1] - right[1] * fwd[0])
    return fwd, right, up


def fit_view(corners, target, yaw_deg, pitch_deg, fill=None):
    """THE fit, for an entity and a scenario alike: the camera looks from yaw / pitch at `target`; its distance is scaled and its target slid
    sideways / up until the projected corners' box is CENTRED and its farthest edge sits at `fill` of the half-width or half-height (whichever
    limits) — the 45 degree vertical field, the tile's 16:9. Returns (eye, target)."""
    fill = fill or FILL
    pitch, yaw = math.radians(pitch_deg), math.radians(yaw_deg)
    fwd, right, up = _basis(yaw, pitch)
    tv = math.tan(math.radians(FOV_DEG / 2))
    th = tv * W / H
    span = max(math.dist(c, target) for c in corners) or 1.0
    dist, tgt = span * 2.5, list(target)
    for _ in range(16):
        eye = (tgt[0] - fwd[0] * dist, tgt[1] - fwd[1] * dist, tgt[2] - fwd[2] * dist)
        xs, ys = [], []
        for c in corners:
            v = (c[0] - eye[0], c[1] - eye[1], c[2] - eye[2])
            depth = max(v[0] * fwd[0] + v[1] * fwd[1] + v[2] * fwd[2], 0.05)
            xs.append((v[0] * right[0] + v[1] * right[1] + v[2] * right[2]) / (depth * th))
            ys.append((v[0] * up[0] + v[1] * up[1] + v[2] * up[2]) / (depth * tv))
        mx, my = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
        half = max((max(xs) - min(xs)) / 2, (max(ys) - min(ys)) / 2)
        # slide the target so the projected box is centred (at the target's own depth), then scale the distance to the fill
        for k in range(3):
            tgt[k] += right[k] * mx * dist * th + up[k] * my * dist * tv
        dist = max(dist * half / fill, 0.8)
    eye = (tgt[0] - fwd[0] * dist, tgt[1] - fwd[1] * dist, tgt[2] - fwd[2] * dist)
    return list(eye), tgt


def box_corners(center, height, length, width, heading_deg=0.0):
    """The 8 corners of an oriented box: length along the heading, width across, from the ground (center z) up to height."""
    h = math.radians(heading_deg)
    ax, ay = (math.cos(h), math.sin(h)), (-math.sin(h), math.cos(h))
    out = []
    for sl in (-0.5, 0.5):
        for sw in (-0.5, 0.5):
            for z in (0.0, height):
                out.append((center[0] + sl * length * ax[0] + sw * width * ay[0], center[1] + sl * length * ax[1] + sw * width * ay[1], center[2] + z))
    return out


def frame_entity(center, height, length, width, facing_deg=0.0, off_front_deg=30.0, pitch_deg=-15.0, fill=None):
    """A low three-quarter view IN FRONT of the entity (~30 degrees off its heading), its oriented box fitted to FILL of the tile (fit_view)."""
    corners = box_corners(center, height, length, width, facing_deg)
    return fit_view(corners, (center[0], center[1], center[2] + height / 2), facing_deg + off_front_deg, pitch_deg, fill)


SCENE_UNIT_M = (4.0, 10.0, 10.0)  # a cast member's box for the scenario fit (height, length, width): a tank with its barrel in any heading fits inside


def frame_scene(points, pitch_deg=-50.0, yaw_deg=225.0):
    """A scenario's cast fitted like an entity: every member's box, the view centred on their projected extent and filling FILL of the tile."""
    corners = []
    for p in points:
        hgt, ln, wd = SIZES.get(tuple(p), SCENE_UNIT_M)
        ln = max(ln * 1.3, 1.5); wd = max(wd * 1.3, 1.5)          # a barrel, a little air around a man
        corners += box_corners(p, hgt, max(ln, wd), max(ln, wd))   # heading-free: a square of the longer side
    hgt = max(SIZES.get(tuple(p), SCENE_UNIT_M)[0] for p in points)
    cx = sum(p[0] for p in points) / len(points)
    cy = sum(p[1] for p in points) / len(points)
    cz = sum(p[2] for p in points) / len(points)
    return fit_view(corners, (cx, cy, cz + hgt / 2), yaw_deg, pitch_deg)


def shoot(path, eye, target, width=None, height=None):
    """shot -> a throwaway settle capture (a new shot's first frame reads the weather before the camera moves) -> the capture; the shot released after."""
    try:
        # ⭐ the shot's own layers: `layers: {}` is a take's clean start — the scene rows only, so no unit cards over the picture (a transient override through the
        #   director's layer override; never the user's settings; DELETE /film/shot drops it). Measured: {} alone is enough — a spec's override keeps only the scene rows.
        r = udc.call("POST", "/film/shot", {"spec": {"eye": eye, "target": target, "layers": {}}, "t": 0})
        if not ok(r):
            return False, "shot: " + say(r)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        for _ in range(SETTLE_CAPTURES):           # let the camera arrive (it eases to a new shot) and the weather settle
            udc.call("POST", "/film/capture", {"path": path + ".settle.png", "width": 64, "height": 64})
            time.sleep(0.4)
        try:
            os.remove(path + ".settle.png")
        except OSError:
            pass
        r = udc.call("POST", "/film/capture", {"path": path, "width": width or W, "height": height or H})
        if not ok(r):
            return False, "capture: " + say(r)
        return (True, "") if os.path.isfile(path) and os.path.getsize(path) > 0 else (False, "no file written")
    finally:
        udc.call("DELETE", "/film/shot")


BIG_W, BIG_H = 2048, 1152     # a type is captured large, then cropped to what it drew (sharp after the downscale)
PLACEHOLDER_RGB = (166, 111, 185)   # the 3-D view's box for a type with no drawing (a voice cue, a deploy role): no thumbnail for it


def crop_to_drawn(big, bg, out, accept_small=False):
    """The pixels that differ between the capture with the entity and the same view without it are what it DREW: crop a 16:9 window
    around them so they fill FILL of it, downscale to W x H. Nothing drawn, or only the placeholder box ⇒ no picture (the picker's
    default stays). Needs Pillow; without it the large capture is only downscaled."""
    try:
        from PIL import Image, ImageChops
    except ImportError:
        os.replace(big, out); return ("ok", out + " (no Pillow: not cropped)")
    a, b = Image.open(big).convert("RGB"), Image.open(bg).convert("RGB")
    diff = ImageChops.difference(a, b).convert("L").point(lambda v: 255 if v > 24 else 0)
    box = diff.getbbox()
    if not box or (box[2] - box[0]) * (box[3] - box[1]) < 12:
        return ("skipped", "nothing drawn")
    mask = diff.crop(box)
    px = [p for p, m in zip(list(a.crop(box).getdata()), list(mask.getdata())) if m]
    purple = sum(1 for r, g, bl in px if abs(r - PLACEHOLDER_RGB[0]) < 30 and abs(g - PLACEHOLDER_RGB[1]) < 30 and abs(bl - PLACEHOLDER_RGB[2]) < 30)
    if px and purple > 0.5 * len(px):
        return ("skipped", "only the placeholder box is drawn")
    x0, y0, x1, y1 = box
    if not accept_small and (x0 <= 2 or y0 <= 2 or x1 >= a.width - 2 or y1 >= a.height - 2):
        return ("clipped", "the drawing runs off the view: zoom out", box)
    if not accept_small and max(x1 - x0, (y1 - y0) * W / H) < 0.25 * W * BIG_W / W * 1.0 and max(x1 - x0, y1 - y0) < 300:
        return ("small", "a tiny drawing: zoom in", box)
    cw = max((x1 - x0) / FILL, (y1 - y0) / FILL * W / H, 64)
    ch = cw * H / W
    if cw > a.width or ch > a.height:
        cw, ch = a.width, a.height
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    left = min(max(cx - cw / 2, 0), a.width - cw)
    top = min(max(cy - ch / 2, 0), a.height - ch)
    a.crop((int(left), int(top), int(left + cw), int(top + ch))).resize((W, H), Image.LANCZOS).save(out)
    return ("ok", out)


def scenario_names(requested):
    root = os.path.join(REPO, "scenarios")
    have = sorted(d for d in os.listdir(root) if os.path.isfile(os.path.join(root, d, "scenario.json")))
    if requested:
        missing = [n for n in requested if n not in have]
        if missing:
            sys.exit(f"no such scenario in the repository: {missing}")
        return requested
    return have


def do_scenarios(names, rows, force=False):
    for name in names:
        hand = next((f for f in ("preview.jpg", "preview.png") if os.path.isfile(os.path.join(REPO, "scenarios", name, f))), None)
        if hand and not force:
            rows.append((name, "skipped (has a still)", f"{hand} in the repository; --force re-shoots")); continue
        good, why = load_edit(name)
        if not good:
            rows.append((name, "failed", why)); continue
        pts = positions()
        if not pts:
            rows.append((name, "failed", "no entity with a position")); continue
        cast = cluster_of(pts)
        # a lone figure (or a pair) is shot lower than a spread cast: from -50 degrees a man beside / inside a house is framed through its roof
        yaw = 225.0
        rest = [p for p in pts if p not in cast]
        if rest and len(cast) <= 2:   # the camera on the side of the rest of the cast: the line between them is open (a camera behind a house frames its wall)
            c = (sum(p[0] for p in cast) / len(cast), sum(p[1] for p in cast) / len(cast))
            near = min(rest, key=lambda p: math.hypot(p[0] - c[0], p[1] - c[1]))
            yaw = math.degrees(math.atan2(near[1] - c[1], near[0] - c[0]))
        eye, target = frame_scene(cast, pitch_deg=-28.0, yaw_deg=yaw) if len(cast) <= 2 else frame_scene(cast)
        folder = os.path.join(REPO, "scenarios", name)
        out = os.path.join(folder, "preview.jpg")
        shot = os.path.join(folder, "preview.capture.png")            # the capture route writes PNG; the still is a small JPEG (§2j): 320x180 q85
        good, why = shoot(shot, eye, target)
        if good:
            try:
                cell_image(shot).save(out, "JPEG", quality=JPEG_Q, optimize=True)
            except OSError as e:
                good, why = False, f"jpeg: {e}"
            finally:
                try:
                    os.remove(shot)
                except OSError:
                    pass
        rows.append((name, "ok" if good else "failed", out if good else why))


TERRAIN_STILL_PREFIX = "_terrain-still/"   # the throwaway blank scenario the still is shot in (written to the NAS the editor runs against, deleted after)
TERRAIN_EYE_HEIGHT_M = 600.0               # a terrain's still: straight down over its origin from this height (no API reports a terrain's extent; --terrain-height overrides)


def terrain_names(requested):
    """Every terrain folder under the repository's assets/terrain (a folder at any depth holding terrain.json), or the requested ones."""
    root = os.path.join(REPO, "assets", "terrain")
    have = sorted(os.path.relpath(d, root).replace(os.sep, "/") for d, _, files in os.walk(root) if "terrain.json" in files and d != root)
    if requested:
        missing = [n for n in requested if n not in have]
        if missing:
            sys.exit(f"no such terrain in the repository: {missing}")
        return requested
    return have


def do_terrains(names, rows, nas, force=False, height=TERRAIN_EYE_HEIGHT_M):
    """⭐ B-9 N6 — a terrain's still: a header-only scenario on it (the file POST /scenario/new writes — the master is not needed, and an editor-mode host, the one with the film
    director, does not have one) written under the NAS, opened for edit, the camera straight down over the origin, 320x180 q85 JPEG into assets/terrain/<name>/preview.jpg
    (the one picture exception for terrains, R-4135). A terrain with a preview.jpg/.png is skipped unless --force. The throwaway scenario is removed after."""
    import shutil
    for name in names:
        folder = os.path.join(REPO, "assets", "terrain", *name.split("/"))
        hand = next((f for f in ("preview.jpg", "preview.png") if os.path.isfile(os.path.join(folder, f))), None)
        if hand and not force:
            rows.append((name, "skipped (has a still)", f"{hand} in the repository; --force re-shoots")); continue
        scen = TERRAIN_STILL_PREFIX + name.replace("/", "_") + f"-{os.getpid()}"
        scen_dir = os.path.join(nas, "scenarios", *scen.split("/"))
        os.makedirs(scen_dir, exist_ok=True)
        with open(os.path.join(scen_dir, "scenario.json"), "w", encoding="utf-8") as f:
            json.dump({"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
                       "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": name}, "entities": {}}, f)
        try:
            good, why = load_edit(scen)
            if not good:
                rows.append((name, "failed", why)); continue
            out = os.path.join(folder, "preview.jpg")
            shot = os.path.join(folder, "preview.capture.png")
            # straight down over the origin (the target is the origin at ground level; the eye is a metre off it so the look vector is not degenerate)
            good, why = shoot(shot, [0.0, -1.0, height], [0.0, 0.0, 0.0])
            if good:
                try:
                    cell_image(shot).save(out, "JPEG", quality=JPEG_Q, optimize=True)
                except OSError as e:
                    good, why = False, f"jpeg: {e}"
                finally:
                    try:
                        os.remove(shot)
                    except OSError:
                        pass
            rows.append((name, "ok" if good else "failed", out if good else why))
        finally:
            shutil.rmtree(scen_dir, ignore_errors=True)


def type_table():
    rows = body_of(udc.call("GET", "/tkb/types")) or []
    return {int(r["tkbType"]): r["name"] for r in rows}


def spawn_one(tkb, origin):
    """Spawn one entity of this type at origin and step until it exists; returns its net ids ([] when the request was never served)."""
    before = {r["networkId"] for r in (body_of(udc.call("GET", "/entities")) or [])}
    r = udc.call("POST", "/entities/spawn", {"tkbType": tkb, "transform": {"position": {"x": origin[0], "y": origin[1], "z": origin[2]},
                                                                          "rotation": {"x": 0, "y": 0, "z": 0, "w": 1}}})
    if not ok(r):
        return None, "spawn: " + say(r)
    for _ in range(10):                                   # step until the request is served and the entity exists
        udc.call("POST", "/sim/step", {"count": 6})
        nids = [x["networkId"] for x in (body_of(udc.call("GET", "/entities")) or []) if x["networkId"] not in before]
        if nids:
            udc.call("POST", "/sim/step", {"count": 6})   # let it realise (its body, its size)
            return nids, ""
    return None, "the spawn request was never served"


def hold_still(nid, origin):
    """⭐ U-12 / CE-4273 — pin the subject: any order it was given cancelled, its velocity zero and its pose back at where it was spawned (x, y of `origin`; its own settled z). A vehicle that starts driving the moment the sim is stepped
    (the `Car`, 8806) otherwise moves between the spawn's steps and the two captures, and the crop (the pixels that differ between the shot with it and the shot without it) then holds the car TWICE or off the frame. A fix in the script, never in the type.
    Best effort: a route that refuses (a type with no such component) is not an error — the captures run all the same."""
    udc.call("DELETE", f"/entities/{nid}/order", {})
    comps = (body_of(udc.call("GET", f"/entities/{nid}")) or {}).get("Components") or {}
    z = ((comps.get("SimTransform") or {}).get("Position") or {}).get("Z", origin[2])
    udc.call("POST", f"/entities/{nid}/component", {"componentType": "SimVelocity", "patch": {"Linear": {"X": 0.0, "Y": 0.0, "Z": 0.0}}})
    udc.call("POST", f"/entities/{nid}/component", {"componentType": "SimTransform", "patch": {"Position": {"X": origin[0], "Y": origin[1], "Z": z}}})


def wait_gone(nids, before_steps=3, tries=8):
    """⭐ U-12 / CE-4273 — step until none of `nids` is in GET /entities any more: a removal is a request the sim serves, and a vehicle still driving through its last ticks put a second car in the empty-view shot."""
    for _ in range(tries):
        udc.call("POST", "/sim/step", {"count": before_steps})
        live = {r["networkId"] for r in (body_of(udc.call("GET", "/entities")) or [])}
        if not any(n in live for n in nids):
            return True
    return False


def shoot_type(tkb, origin, path, view):
    """One pass: spawn, capture large from `view` (or a view framed from the entity's own data), remove it, capture the same view empty,
    crop to the difference. A drawing too small to crop sharply returns ("small", ...) with the view zoomed onto it, for a second pass."""
    nids, why = spawn_one(tkb, origin)
    if not nids:
        return ("failed", why), view, None
    try:
        hold_still(nids[0], origin)                        # ⭐ CE-4273 — held still BEFORE it is framed and shot
        if view is None:
            comps = (body_of(udc.call("GET", f"/entities/{nids[0]}")) or {}).get("Components") or {}
            pos = (comps.get("SimTransform") or {}).get("Position") or origin
            height, length, width = entity_size(comps)
            face = heading_deg(comps)
            eye, target = frame_entity(pos, height, length, width, facing_deg=face, fill=0.45)   # generous: the whole of it in view
            view = (eye, target, face + 30.0, -15.0)
        eye, target, yaw, pitch = view
        good, why = shoot(path + ".big.png", eye, target, BIG_W, BIG_H)
        if not good:
            return ("failed", why), view, None
    finally:
        for nid in nids:                                   # the same view with nothing in it: the difference is what the entity DREW
            udc.call("DELETE", f"/entities/{nid}")
        wait_gone(nids)                                    # ⭐ CE-4273 — until it is really gone (not a fixed 3 steps)
    good, why = shoot(path + ".bg.png", eye, target, BIG_W, BIG_H)
    if not good:
        return ("failed", why), view, None
    verdict = crop_to_drawn(path + ".big.png", path + ".bg.png", path)
    if verdict[0] == "clipped":                            # bigger than its data said (a wingspan, a rotor): step back and look again
        dist = math.dist(eye, target)
        fwd, right, up = _basis(math.radians(yaw), math.radians(pitch))
        nd = dist * 2.2
        view = ([target[k] - fwd[k] * nd for k in range(3)], list(target), yaw, pitch)
        return verdict[:2], view, verdict[2]
    if verdict[0] == "small":
        x0, y0, x1, y1 = verdict[2]
        fwd, right, up = _basis(math.radians(yaw), math.radians(pitch))
        tv = math.tan(math.radians(FOV_DEG / 2)); th = tv * W / H
        dist = math.dist(eye, target)
        mx = ((x0 + x1) / 2 - BIG_W / 2) / (BIG_W / 2)
        my = -((y0 + y1) / 2 - BIG_H / 2) / (BIG_H / 2)
        half = max((x1 - x0) / BIG_W, (y1 - y0) / BIG_H, 0.002)
        tgt = [target[k] + right[k] * mx * dist * th + up[k] * my * dist * tv for k in range(3)]
        nd = max(dist * half / 0.45, 0.3)
        view = ([tgt[k] - fwd[k] * nd for k in range(3)], tgt, yaw, pitch)
        return verdict[:2], view, verdict[2]
    return verdict, view, None


def do_types(ids, nas, studio, rows):
    table = type_table()
    if not ids:
        ids = sorted(table)
    good, why = load_edit(studio)
    if not good:
        for i in ids:
            rows.append((f"type {i}", "failed", f"studio {studio}: {why}"))
        return
    out_dir = os.path.join(nas, "assets", "thumbnails", "generated")
    origin = [4000.0, 4000.0, 0.0]                      # far from the studio scenario's own entity
    # a spawn is a REQUEST the sim serves: enter preview PAUSED (the clock does not run in edit) and step it
    udc.call("POST", "/preview/enter", {"startPaused": True})
    try:
        for tkb in ids:
            name = table.get(tkb)
            if name is None:
                rows.append((f"type {tkb}", "failed", "not in GET /tkb/types")); continue
            stem = generated_name(name)
            if stem is None:
                rows.append((f"type {tkb} {name}", "failed", "unusable name")); continue
            path = os.path.join(out_dir, stem + ".png")
            view = None                                            # pass 1: framed from the entity's data; pass 2 (a tiny drawing): zoomed onto it
            for attempt in range(4):
                verdict, view, drawn = shoot_type(tkb, origin, path, view)
                if verdict[0] not in ("small", "clipped"):
                    break
            if verdict[0] in ("small", "clipped"):
                verdict = crop_to_drawn(path + ".big.png", path + ".bg.png", path, accept_small=True)
            for f in (path + ".big.png", path + ".bg.png"):
                try: os.remove(f)
                except OSError: pass
            rows.append((f"type {tkb} {name}",) + verdict)
    finally:
        udc.call("POST", "/preview/exit", {})


def positions_near(origin, radius=60.0):
    return [p for p in positions() if math.hypot(p[0] - origin[0], p[1] - origin[1]) <= radius]


# ── ⭐ CE-4271 — the entity ATLAS packer (Pillow only; no C# encoder) ─────────────────────────────────────────────────────────────────
ATLAS_DIR = os.path.join(REPO, "Hrot", "Engine", "Hrot.Presentation", "Thumbnails")
INDEX_NAME = "entities.atlas.json"
PAGE_W = PAGE_H = 2048
CELL_W, CELL_H = 320, 180
COLS, ROWS = PAGE_W // CELL_W, PAGE_H // CELL_H            # 6 x 11 = 66 cells a page
JPEG_Q = 85
SAME_MAE = 6.0          # a cell whose mean absolute pixel difference from the page's is below this is "unchanged" (a JPEG round trip is never bit-exact)


def need_pillow():
    try:
        from PIL import Image  # noqa: F401
        return Image
    except ImportError:
        sys.exit("the atlas packer needs Pillow:  py -3 -m pip install pillow   (Linux: python3 -m pip install pillow)")


def cell_image(path):
    """A picture as a 320x180 RGB cell: centre-cropped to 16:9, scaled with Lanczos."""
    Image = need_pillow()
    with Image.open(path) as src:
        im = src.convert("RGB")
    w, h = im.size
    want = CELL_W / CELL_H
    if w / h > want:                      # too wide: crop the sides
        nw = round(h * want); x0 = (w - nw) // 2; im = im.crop((x0, 0, x0 + nw, h))
    elif w / h < want:                    # too tall: crop top and bottom
        nh = round(w / want); y0 = (h - nh) // 2; im = im.crop((0, y0, w, y0 + nh))
    return im.resize((CELL_W, CELL_H), Image.LANCZOS)


class Atlas:
    """The index and pages of one atlas directory. Slots are FIXED: a name keeps its [page, col, row] forever."""

    def __init__(self, directory):
        self.dir = directory
        self.index_path = os.path.join(directory, INDEX_NAME)
        self.items = {}                   # stem -> [page, col, row]
        self.pages = []                   # page file names
        self._img = {}                    # page number -> PIL image (RGB), loaded lazily
        self._dirty = set()
        if os.path.isfile(self.index_path):
            with open(self.index_path, encoding="utf-8") as fh:
                doc = json.load(fh)
            if doc.get("version") != 1 or doc.get("cell") != [CELL_W, CELL_H] or doc.get("page") != [PAGE_W, PAGE_H]:
                sys.exit(f"{self.index_path}: not a version 1 atlas of {CELL_W}x{CELL_H} cells on {PAGE_W}x{PAGE_H} pages")
            self.items = {k: list(v) for k, v in doc.get("items", {}).items()}
            self.pages = list(doc.get("pages", []))

    def page(self, n):
        Image = need_pillow()
        if n not in self._img:
            path = os.path.join(self.dir, self.pages[n])
            if os.path.isfile(path):
                with Image.open(path) as im:
                    self._img[n] = im.convert("RGB")
            else:
                self._img[n] = Image.new("RGB", (PAGE_W, PAGE_H), (0, 0, 0))
        return self._img[n]

    def free_slot(self):
        taken = {tuple(v) for v in self.items.values()}
        for n in range(len(self.pages)):
            for row in range(ROWS):
                for col in range(COLS):
                    if (n, col, row) not in taken:
                        return [n, col, row]
        n = len(self.pages)                # every cell is taken: open a page
        self.pages.append(f"entities-{n}.jpg")
        return [n, 0, 0]

    @staticmethod
    def box(slot):
        _, col, row = slot
        return (col * CELL_W, row * CELL_H, (col + 1) * CELL_W, (row + 1) * CELL_H)

    def put(self, stem, cell):
        """Place a 320x180 cell under `stem`. Returns 'added' | 'replaced' | 'unchanged'."""
        Image = need_pillow()
        had = stem in self.items
        slot = self.items[stem] if had else self.free_slot()
        page = self.page(slot[0])
        box = self.box(slot)
        if had:
            old = page.crop(box)
            diff = sum(abs(a - b) for a, b in zip(old.tobytes(), cell.tobytes())) / (CELL_W * CELL_H * 3)
            if diff <= SAME_MAE:
                return "unchanged"
        page.paste(cell, box)
        self._dirty.add(slot[0])
        self.items[stem] = slot
        return "replaced" if had else "added"

    def remove(self, stem):
        slot = self.items.pop(stem, None)
        if slot is None:
            return False
        Image = need_pillow()
        self.page(slot[0]).paste(Image.new("RGB", (CELL_W, CELL_H), (0, 0, 0)), self.box(slot))
        self._dirty.add(slot[0])
        return True

    def index_text(self):
        """Sorted keys, ONE item a line: a diff shows exactly the names added."""
        lines = [f'    {json.dumps(k)}: [{v[0]}, {v[1]}, {v[2]}]' for k, v in sorted(self.items.items())]
        return ('{\n  "version": 1,\n  "cell": [%d, %d],\n  "page": [%d, %d],\n  "pages": %s,\n  "items": {\n%s\n  }\n}\n'
                % (CELL_W, CELL_H, PAGE_W, PAGE_H, json.dumps(self.pages), ",\n".join(lines)))

    def save(self):
        """Writes ONLY the pages whose pixels changed, then the index when its text changed. Returns the files written."""
        os.makedirs(self.dir, exist_ok=True)
        written = []
        for n in sorted(self._dirty):
            path = os.path.join(self.dir, self.pages[n])
            self._img[n].save(path, "JPEG", quality=JPEG_Q, optimize=True, subsampling=0)
            written.append(path)
        text = self.index_text()
        old = None
        if os.path.isfile(self.index_path):
            with open(self.index_path, encoding="utf-8", newline="") as fh:
                old = fh.read()
        if old != text:
            with open(self.index_path, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(text)
            written.append(self.index_path)
        self._dirty.clear()
        return written


def pack_files(atlas, files):
    """files: [(stem, path)] -> table rows."""
    rows = []
    for stem, path in files:
        try:
            result = atlas.put(stem, cell_image(path))
        except OSError as e:
            rows.append((stem, "failed", str(e))); continue
        slot = atlas.items[stem]
        rows.append((stem, result, f"page {slot[0]} col {slot[1]} row {slot[2]}"))
    return rows


def loose_generated(nas, only=None):
    """[(stem, path)] of the loose generated/*.png on the NAS (only those stems when given)."""
    folder = os.path.join(nas, "assets", "thumbnails", "generated")
    if not os.path.isdir(folder):
        return []
    out = []
    for f in sorted(os.listdir(folder)):
        if f.lower().endswith(".png") and (only is None or f[:-4] in only):
            out.append((f[:-4], os.path.join(folder, f)))
    return out


def print_table(rows, header="item"):
    width = max((len(r[0]) for r in rows), default=len(header))
    print(f"{header.ljust(width)}  result             detail")
    for item, result, detail in rows:
        print(f"{item.ljust(width)}  {result.ljust(18)} {detail}")


def atlas_report(atlas, written):
    for path in written:
        print(f"   wrote {path}  ({os.path.getsize(path)} bytes)")
    total = sum(os.path.getsize(os.path.join(atlas.dir, f)) for f in atlas.pages + [INDEX_NAME] if os.path.isfile(os.path.join(atlas.dir, f)))
    print(f"   atlas: {len(atlas.items)} pictures on {len(atlas.pages)} page(s), {total} bytes with the index")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--scenarios", nargs="*", default=None, metavar="NAME", help="scenario names (none given = every repository scenario)")
    ap.add_argument("--terrains", nargs="*", default=None, metavar="NAME", help="⭐ B-9 N6 — terrain names under assets/terrain (none given = every terrain folder); needs a running editor like --scenarios")
    ap.add_argument("--terrain-height", type=float, default=TERRAIN_EYE_HEIGHT_M, help="the camera height (m) over a terrain's origin for --terrains")
    ap.add_argument("--types", nargs="*", type=int, default=None, metavar="ID", help="TKB type ids (none given = every type)")
    ap.add_argument("--port", type=int, default=8111)
    ap.add_argument("--force", action="store_true", help="⭐ CE-4271 — with --scenarios: re-shoot a scenario that already has a preview.jpg/.png (the repo file is overwritten; commit it only on purpose)")
    ap.add_argument("--attach", action="store_true", help="use an editor already listening on --port (do not launch or stop one)")
    ap.add_argument("--nas", default=DEFAULT_NAS, help=f"the NAS root the pictures go into (default {DEFAULT_NAS})")
    ap.add_argument("--studio", default="test-move", help="the bare scenario types are spawned in (default test-move: no terrain)")
    ap.add_argument("--pack", action="store_true", help="⭐ CE-4271 — pack the loose generated/*.png on the NAS (with --types: just those just captured) into the entity atlas")
    ap.add_argument("--add-image", metavar="FILE", help="⭐ CE-4271 — pack this picture under --name (centre-cropped to 16:9, 320x180); no editor needed")
    ap.add_argument("--name", metavar="TypeName", help="the TKB type name --add-image / --atlas-remove names")
    ap.add_argument("--atlas-list", action="store_true", help="⭐ CE-4271 — list the atlas: stem, page, col, row")
    ap.add_argument("--atlas-remove", metavar="TypeName", help="⭐ CE-4271 — free a type's slot (its cell goes black, the next add reuses it)")
    ap.add_argument("--atlas-dir", default=ATLAS_DIR, help=f"the atlas directory (default {ATLAS_DIR}); a test passes a temp one")
    args = ap.parse_args()
    atlas_only = args.atlas_list or args.atlas_remove or args.add_image or (args.pack and args.types is None and args.scenarios is None and args.terrains is None)
    if args.scenarios is None and args.types is None and args.terrains is None and not atlas_only:
        sys.exit("say what: --scenarios [names] and/or --terrains [names] and/or --types [ids], or --pack · --add-image F --name T · --atlas-list · --atlas-remove T")
    nas = os.path.abspath(args.nas)
    if os.path.commonpath([nas, REPO]) == REPO:
        sys.exit("⛔ --nas is inside the repository: generated pictures are never written there")

    if atlas_only:
        return atlas_main(args, nas)

    udc.BASE = f"http://localhost:{args.port}"
    proc, rows = None, []
    try:
        if not args.attach:
            proc = udc.launch(args.port, "editor")
        if args.scenarios is not None:
            do_scenarios(scenario_names(args.scenarios), rows, args.force)
        if args.terrains is not None:
            do_terrains(terrain_names(args.terrains), rows, nas, args.force, args.terrain_height)
        if args.types is not None:
            do_types(args.types, nas, args.studio, rows)
    finally:
        if proc is not None:
            udc.stop(proc)
    print_table(rows)
    if args.pack and args.types is not None:        # capture then pack: just the stems this run captured
        stems = {os.path.basename(r[2])[:-4] for r in rows if r[1] == "ok" and r[2].endswith(".png")}
        pack_rows(args, nas, loose_generated(nas, stems))
    sys.exit(1 if any(r[1] == "failed" for r in rows) else 0)


def pack_rows(args, nas, files):
    atlas = Atlas(args.atlas_dir)
    rows = pack_files(atlas, files)
    written = atlas.save()
    print_table(rows, "stem")
    atlas_report(atlas, written)
    return rows


def atlas_main(args, nas):
    """The editor-free atlas commands."""
    atlas = Atlas(args.atlas_dir)
    if args.atlas_list:
        rows = [(stem, "slot", f"page {v[0]} col {v[1]} row {v[2]}") for stem, v in sorted(atlas.items.items())]
        print_table(rows, "stem")
        print(f"   {len(atlas.items)} pictures on {len(atlas.pages)} page(s)")
        return
    if args.atlas_remove:
        stem = generated_name(args.atlas_remove)
        if not stem or not atlas.remove(stem):
            sys.exit(f"{args.atlas_remove!r} is not in the atlas (--atlas-list)")
        atlas_report(atlas, atlas.save())
        return
    if args.add_image:
        stem = generated_name(args.name)
        if not stem:
            sys.exit("--add-image needs --name <TypeName> (a name without '..')")
        if not os.path.isfile(args.add_image):
            sys.exit(f"no such file: {args.add_image}")
        rows = pack_files(atlas, [(stem, args.add_image)])
        written = atlas.save()
        print_table(rows, "stem")
        atlas_report(atlas, written)
        sys.exit(1 if rows[0][1] == "failed" else 0)
    rows = pack_rows(args, nas, loose_generated(nas))
    sys.exit(1 if any(r[1] == "failed" for r in rows) else 0)


if __name__ == "__main__":
    main()
