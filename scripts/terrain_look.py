"""terrain_look.py — the LOOK inputs for Unreal, generated from OUR terrain package (CE-4651, UL-10 … UL-13; used by terrain-to-unreal.py --look).

Everything here is derived from HROT's own data, so the picture follows the simulation's map:
  roads_mask    the SUMO lanes rasterised (UL-12) — the ground material paints them
  buildings     each house built from ITS template: walls by storey, windows / doors at the template's openings, a pitched roof from the
                imported roof volume (gable on a near-rectangle, tent otherwise), wall / roof material picked per house (UL-13)
  grass         open-ground grass and forest ferns scattered around the close views — never on roads, water, forest edges or under houses

Unreal frame: X east, Y SOUTH, cm. HROT local: x east, y north, m.
"""
import json, math, os, re, struct, zlib

import numpy as np

# material slots of the building meshes (look.py / hrot_look.py bind them in this order)
SLOT_PLASTER, SLOT_OLD_PLASTER, SLOT_BRICK, SLOT_ROOF_RED, SLOT_ROOF_DARK, SLOT_GLASS, SLOT_FRAME, SLOT_DOOR = range(8)
SLOT_NAMES = ["plaster", "oldPlaster", "brick", "roofRed", "roofDark", "glass", "frame", "door"]
WINDOW_SILL, WINDOW_HEIGHT, DOOR_HEIGHT = 0.9, 1.3, 2.1


def _hash01(s):
    return (zlib.crc32(str(s).encode()) & 0xFFFFFF) / float(0xFFFFFF)


# ── roads (UL-12) ────────────────────────────────────────────────────────────────────────────────────────────────────────────
def sumo_lanes(net_path, de, dn, frame_utm):
    """The non-internal lanes of a SUMO net as HROT-local polylines + widths. Georeferenced UTM nets: local = net − netOffset − (E0, N0)
    (Hrot.Traffic SumoNetPlacement, the same zone ⇒ a translation); a plain net: local = net − anchor (handled by the caller)."""
    text = open(net_path, encoding="utf-8").read()
    m = re.search(r'<location netOffset="([-\d.]+),([-\d.]+)"[^>]*projParameter="([^"]*)"', text)
    if not m:
        return []
    ox, oy, proj = float(m.group(1)), float(m.group(2)), m.group(3)
    if "utm" not in proj or not frame_utm:
        return []
    lanes, edge_class = [], {}
    for em in re.finditer(r'<edge id="([^"]+)"([^>]*)>(.*?)</edge>', text, re.S):
        eid, attrs, body = em.group(1), em.group(2), em.group(3)
        if eid.startswith(":") or 'function="internal"' in attrs:   # junction-internal: the junction AREA below covers them
            continue
        tm = re.search(r'type="([^"]*)"', attrs)
        cls = road_class(tm.group(1) if tm else "")
        edge_class[eid] = cls
        for lm in re.finditer(r'<lane [^>]*?shape="([^"]+)"[^>]*>|<lane [^>]*?shape="([^"]+)"[^>]*/>', body):
            tag = lm.group(0)
            wm = re.search(r'width="([\d.]+)"', tag)
            w = float(wm.group(1)) if wm else (3.2 if cls == "asphalt" else 2.2)
            shape = lm.group(1) or lm.group(2)
            pts = [(float(p.split(",")[0]) - ox - de, float(p.split(",")[1]) - oy - dn) for p in shape.split()]
            if len(pts) >= 2:
                lanes.append((pts, w, cls))
    # the JUNCTION areas (SUMO's own polygon of each crossing; the user: "without them roads look ugly segmented"): painted in the
    # surface of the roads that meet there — asphalt when any street enters it, else dirt. Internal junctions are skipped (their
    # area is inside their parent's polygon).
    for jm in re.finditer(r'<junction id="([^"]+)" type="([^"]+)"([^>]*)/?>', text):
        jid, jtype, attrs = jm.group(1), jm.group(2), jm.group(3)
        if jtype == "internal" or jid.startswith(":"): continue
        sm = re.search(r'shape="([^"]+)"', attrs); im = re.search(r'incLanes="([^"]*)"', attrs)
        if not sm: continue
        pts = [(float(p.split(",")[0]) - ox - de, float(p.split(",")[1]) - oy - dn) for p in sm.group(1).split()]
        if len(pts) < 3: continue
        inc = [edge_class.get(l.rsplit("_", 1)[0]) for l in (im.group(1).split() if im else [])]
        cls = "asphalt" if "asphalt" in inc else "dirt"
        lanes.append((pts, 0.0, cls + "-area"))
    return lanes


ASPHALT = ("motorway", "trunk", "primary", "secondary", "tertiary", "residential", "unclassified", "service", "living_street", "road")


def road_class(sumo_type):
    """The surface a road is drawn with, from its OSM highway class (SUMO's edge type "highway.<class>"): asphalt for streets and
    roads, dirt / gravel for tracks, paths, footways and steps."""
    k = sumo_type.split(".", 1)[-1].replace("_link", "")
    return "asphalt" if k in ASPHALT else "dirt"


def roads_mask(lanes, x0, y0, W, H, cell=1.0):
    """An RGB mask (the NORTH row first) of the lanes at `cell` m: R = asphalt, G = dirt / gravel (each lane its width + 0.5 m)."""
    from PIL import Image, ImageDraw
    mw, mh = int(round(W / cell)), int(round(H / cell))
    chans = {"asphalt": Image.new("L", (mw, mh), 0), "dirt": Image.new("L", (mw, mh), 0)}
    for cls in ("dirt", "asphalt"):
        dr = ImageDraw.Draw(chans[cls])
        for pts, w, c in lanes:
            if c == cls + "-area":   # a junction polygon, filled (and its rim, so it meets the lanes' edges without a seam)
                px = [((x - x0) / cell, (y0 + H - y) / cell) for x, y in pts]
                dr.polygon(px, fill=255)
                dr.line(px + [px[0]], fill=255, width=max(1, int(round(0.6 / cell))))
                continue
            if c != cls: continue
            px = [((x - x0) / cell, (y0 + H - y) / cell) for x, y in pts]
            dr.line(px, fill=255, width=max(1, int(round((w + 0.5) / cell))), joint="curve")
            r = (w + 0.5) / cell / 2
            for cx, cy in (px[0], px[-1]):
                dr.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    return Image.merge("RGB", (chans["asphalt"], chans["dirt"], Image.new("L", (mw, mh), 0)))


# ── buildings (UL-13) ────────────────────────────────────────────────────────────────────────────────────────────────────────
class Mesh:
    def __init__(self, ox, oy, oz):
        self.ox, self.oy, self.oz = ox, oy, oz
        self.V, self.T, self.M = [], [], []
        self.U, self.id = [], (0.0, 0.0)   # U-20: per vertex (building index in the tile, part = panel*256 + opening + 1 on its glass / frame / door)

    def v(self, x, y, h):
        self.V.append(((x - self.ox) * 100.0, -(y - self.oy) * 100.0, (h - self.oz) * 100.0))
        self.U.append(self.id)
        return len(self.V) - 1

    def quad(self, a, b, c, d, slot):   # a, b, c, d counter-clockwise seen from the FRONT (HROT frame)
        self.T += [(a, b, c), (a, c, d)]; self.M += [slot, slot]

    def wall_quad(self, p, q, z0, z1, slot, out=0.0, nrm=(0.0, 0.0)):
        """A vertical rectangle from p to q (HROT xy), z0..z1, pushed `out` m along the outward normal nrm."""
        px, py = p[0] + nrm[0] * out, p[1] + nrm[1] * out; qx, qy = q[0] + nrm[0] * out, q[1] + nrm[1] * out
        self.quad(self.v(px, py, z0), self.v(qx, qy, z0), self.v(qx, qy, z1), self.v(px, py, z1), slot)

    def write(self, path):
        with open(path, "wb") as f:
            f.write(struct.pack("<ii", len(self.V), len(self.T)))
            f.write(np.array(self.V, dtype="<f4").tobytes()); f.write(np.array(self.T, dtype="<i4").tobytes())
            f.write(np.array(self.M, dtype="u1").tobytes())
            f.write(np.array(self.U, dtype="<f4").tobytes())   # U-20: the ids trailer (BuildMesh: UV channel 1)


def _ccw(ring_):
    a = np.sum(ring_[:, 0] * np.roll(ring_[:, 1], -1) - np.roll(ring_[:, 0], -1) * ring_[:, 1])
    return ring_ if a > 0 else ring_[::-1]


def _min_rect(ring_):
    """The minimum-area rectangle around a polygon (rotating calipers over its edges): centre, the long axis unit vector, half sizes."""
    best = None
    for i in range(len(ring_)):
        e = ring_[(i + 1) % len(ring_)] - ring_[i]
        n = np.hypot(*e)
        if n < 1e-6: continue
        u = e / n; w = np.array([-u[1], u[0]])
        pu, pw = ring_ @ u, ring_ @ w
        area = (pu.max() - pu.min()) * (pw.max() - pw.min())
        if best is None or area < best[0]:
            best = (area, u, w, (pu.min(), pu.max()), (pw.min(), pw.max()))
    _, u, w, (u0, u1), (w0, w1) = best
    if (u1 - u0) < (w1 - w0):   # the long axis first
        u, w, (u0, u1), (w0, w1) = w, -u, (w0, w1), (-u1, -u0)
    c = u * (u0 + u1) / 2 + w * (w0 + w1) / 2
    return c, u, w, (u1 - u0) / 2, (w1 - w0) / 2, best[0]


def _polygon_area(r):
    return abs(np.sum(r[:, 0] * np.roll(r[:, 1], -1) - np.roll(r[:, 0], -1) * r[:, 1])) / 2


def build_roof(mesh, ring_, eaves, height, slot, wall_slot, overhang=0.35):
    """A pitched roof over a footprint: a GABLE along the long axis of its minimum rectangle when the footprint fills ≥ 82 % of it,
    else a TENT to the centroid. Overhang on every side; gable ends closed in the wall material."""
    import mapbox_earcut as earcut
    ring_ = _ccw(np.asarray(ring_, dtype=float))
    c, u, w, hu, hw, rect_area = _min_rect(ring_)
    if rect_area > 0 and _polygon_area(ring_) / rect_area >= 0.82 and height > 0.2:
        hu2, hw2 = hu + overhang, hw + overhang
        A = c - u * hu2 - w * hw2; B = c + u * hu2 - w * hw2; C = c + u * hu2 + w * hw2; D = c - u * hu2 + w * hw2
        R0 = c - u * hu2; R1 = c + u * hu2
        z0 = eaves - overhang * height / max(hw, 0.1); z1 = eaves + height
        a, b, r1, r0 = mesh.v(*A, z0), mesh.v(*B, z0), mesh.v(*R1, z1), mesh.v(*R0, z1)
        mesh.quad(a, b, r1, r0, slot)                                    # the slope facing −w
        cc, dd, r0b, r1b = mesh.v(*C, z0), mesh.v(*D, z0), mesh.v(*R0, z1), mesh.v(*R1, z1)
        mesh.quad(cc, dd, r0b, r1b, slot)                                # the slope facing +w
        G0 = c - u * hu; G1 = c + u * hu                                 # the gable triangles, at the wall line
        for g, s in ((G0, -1), (G1, 1)):
            p = g - w * hw; q = g + w * hw
            i0, i1, i2 = mesh.v(*p, eaves), mesh.v(*q, eaves), mesh.v(*g, eaves + height)
            mesh.T.append((i0, i1, i2) if s > 0 else (i1, i0, i2)); mesh.M.append(wall_slot)
        return "gable"
    cx, cy = ring_.mean(0)
    n = len(ring_)
    grown = [(x + (x - cx) / max(np.hypot(x - cx, y - cy), 0.1) * overhang, y + (y - cy) / max(np.hypot(x - cx, y - cy), 0.1) * overhang) for x, y in ring_]
    apex = mesh.v(cx, cy, eaves + max(height, 0.3))
    for i in range(n):
        a = mesh.v(*grown[i], eaves - 0.15); b = mesh.v(*grown[(i + 1) % n], eaves - 0.15)
        mesh.T.append((a, b, apex)); mesh.M.append(slot)
    return "tent"


def build_house(mesh, ring_, base, storeys, openings_by_edge, wall_slot):
    """Walls by storey along the footprint (counter-clockwise), the template's windows and doors at their positions on each edge: a
    frame and a glass pane (windows), a door leaf — pushed slightly out of the wall so they read from any distance."""
    ring_ = np.asarray(ring_, dtype=float)
    n = len(ring_)
    z = base
    for si, sh in enumerate(storeys):
        for i in range(n):
            p, q = ring_[i], ring_[(i + 1) % n]
            e = q - p; L = float(np.hypot(*e))
            if L < 1e-3: continue
            nrm = (e[1] / L, -e[0] / L)   # outward for a counter-clockwise ring
            mesh.wall_quad(p, q, z, z + sh, wall_slot)
            for op in openings_by_edge.get((si, i), []):
                a = float(op.get("at", 0)); wd = float(op.get("width", 1.0))
                if a + wd > L + 1e-3: continue
                s0 = p + e / L * a; s1 = p + e / L * (a + wd)
                if op.get("kind") == "door":
                    if si != 0: continue
                    mesh.wall_quad(s0, s1, z, z + min(DOOR_HEIGHT, sh - 0.2), SLOT_DOOR, out=0.03, nrm=nrm)
                else:
                    zs = z + WINDOW_SILL; ze = min(z + WINDOW_SILL + WINDOW_HEIGHT, z + sh - 0.25)
                    if ze <= zs: continue
                    f0 = s0 - e / L * 0.07; f1 = s1 + e / L * 0.07
                    mesh.wall_quad(f0, f1, zs - 0.07, ze + 0.07, SLOT_FRAME, out=0.02, nrm=nrm)
                    mesh.wall_quad(s0, s1, zs, ze, SLOT_GLASS, out=0.035, nrm=nrm)
        z += sh
    return z


def _free_rects(L, z0, z1, holes):
    """The panel rectangle [0, L] x [z0, z1] minus the hole rectangles (u0, u1, h0, h1): vertical strips at every hole edge, each strip's
    free height intervals. Returns [(u0, u1, h0, h1)]."""
    xs = sorted({0.0, L, *[min(max(h[0], 0.0), L) for h in holes], *[min(max(h[1], 0.0), L) for h in holes]})
    out = []
    for i in range(len(xs) - 1):
        a, b = xs[i], xs[i + 1]
        if b - a < 1e-4: continue
        m = (a + b) / 2
        z = z0
        for h0, h1 in sorted((h[2], h[3]) for h in holes if h[0] < m < h[1]):
            if h0 > z + 1e-4: out.append((a, b, z, min(h0, z1)))
            z = max(z, h1)
        if z < z1 - 1e-4: out.append((a, b, z, z1))
    return out


def build_panels(mesh, bid, panels, wall_slot):
    """U-20: a template house from HROT's OWN panels (hrot-building-panels, BuildingDamage's numbering): every panel a wall of its
    thickness with its openings CUT through (reveals at the cut), a window's frame and glass and a door's leaf in the opening's middle
    plane tagged with (panel, opening) in UV1, so the client can break exactly the window HROT names. Returns the top of the walls."""
    top = max(p["top"] for p in panels)
    for p in panels:
        A = np.array(p["a"], dtype=float); B = np.array(p["b"], dtype=float)
        e = B - A; L = float(np.hypot(*e))
        if L < 1e-3: continue
        e /= L; n = np.array([e[1], -e[0]]); h = max(float(p.get("thickness") or 0.2), 0.05) / 2
        z0, z1 = float(p["base"]), float(p["top"])
        P = lambda u, s: A + e * u + n * (h * s)
        mesh.id = (float(bid), 0.0)
        holes = []
        for o in p["openings"]:
            u0 = max(float(o["at"]), 0.0); u1 = min(float(o["at"]) + float(o["width"]), L)
            h0 = max(float(o["sill"]), z0); h1 = min(float(o["head"]), z1)
            if u1 - u0 > 0.05 and h1 - h0 > 0.05: holes.append((u0, u1, h0, h1, o))
        for (u0, u1, a0, a1) in _free_rects(L, z0, z1, holes):   # both faces
            for s in (1, -1):
                q = [mesh.v(*P(u0, s), a0), mesh.v(*P(u1, s), a0), mesh.v(*P(u1, s), a1), mesh.v(*P(u0, s), a1)]
                mesh.quad(*(q if s > 0 else q[::-1]), wall_slot)
        def cap(pa, pb, za, zb):   # a quad joining the two faces (an end, the top, a reveal)
            mesh.quad(mesh.v(*P(pa, 1), za), mesh.v(*P(pb, 1), zb), mesh.v(*P(pb, -1), zb), mesh.v(*P(pa, -1), za), wall_slot)
        def cap_h(u0, u1, z):
            mesh.quad(mesh.v(*P(u0, 1), z), mesh.v(*P(u1, 1), z), mesh.v(*P(u1, -1), z), mesh.v(*P(u0, -1), z), wall_slot)
        def cap_v(u, za, zb):
            mesh.quad(mesh.v(*P(u, 1), za), mesh.v(*P(u, -1), za), mesh.v(*P(u, -1), zb), mesh.v(*P(u, 1), zb), wall_slot)
        cap_v(0.0, z0, z1); cap_v(L, z0, z1); cap_h(0.0, L, z1)
        for (u0, u1, h0, h1, o) in holes:
            cap_v(u0, h0, h1); cap_v(u1, h0, h1); cap_h(u0, u1, h1)
            if h0 > z0 + 1e-3: cap_h(u0, u1, h0)
            mesh.id = (float(bid), float(int(p["panel"]) * 256 + int(o["opening"]) + 1))
            C = lambda u, z: (mesh.v(*P(u, 0), z))
            if o["kind"] == "door":
                mesh.quad(C(u0, h0), C(u1, h0), C(u1, h1), C(u0, h1), SLOT_DOOR)
            elif o["kind"] == "window":
                f = 0.06
                for (ua, ub, za, zb) in ((u0, u1, h0, h0 + f), (u0, u1, h1 - f, h1), (u0, u0 + f, h0 + f, h1 - f), (u1 - f, u1, h0 + f, h1 - f)):
                    mesh.quad(C(ua, za), C(ub, za), C(ub, zb), C(ua, zb), SLOT_FRAME)
                if o.get("glazed", True):
                    mesh.quad(C(u0 + f, h0 + f), C(u1 - f, h0 + f), C(u1 - f, h1 - f), C(u0 + f, h1 - f), SLOT_GLASS)
            mesh.id = (float(bid), 0.0)
    return top


def build_floors(mesh, ring_, levels, slot):
    """U-20: a floor slab per storey (its base), so a hole shows a room, not the ground."""
    import mapbox_earcut as earcut
    ring_ = _ccw(np.asarray(ring_, dtype=float))
    tri = earcut.triangulate_float64(ring_, np.array([len(ring_)], dtype=np.uint32)).reshape(-1, 3)
    for z in levels:
        top = [mesh.v(x, y, z + 0.02) for x, y in ring_]; bot = [mesh.v(x, y, z - 0.18) for x, y in ring_]
        for a_, b_, c_ in tri:
            mesh.T.append((top[a_], top[b_], top[c_])); mesh.M.append(slot)
            mesh.T.append((bot[c_], bot[b_], bot[a_])); mesh.M.append(slot)


def write_buildings(out, name, feats, template_of, ground_lowest, x0, y0, tile_m=500.0, panels=None):
    """UL-13 — OUR houses as meshes (tiles of tile_m m): a template placement is built from its template (walls by storey, openings,
    the footprint placed by the placement's point and rotation, exactly as HROT's TerrainBuildingExpander); an inline building as a
    plain prism; every house's roof from its imported roof volume ("<id>-roof") or, without one, a low tent. Returns a summary."""
    roofs = {f["properties"].get("parent"): f for f in feats if f["properties"]["hrot_kind"] == "roof"}
    tiles = {}
    for f in feats:
        pr = f["properties"]
        if pr["hrot_kind"] != "building": continue
        c = np.array(f["geometry"]["coordinates"][0], dtype=float).mean(0)
        tiles.setdefault((int((c[0] - x0) // tile_m), int((c[1] - y0) // tile_m)), []).append(f)
    written, kinds = [], {"gable": 0, "tent": 0, "flat": 0, "windows": 0, "doors": 0}
    terrain_name = (panels or {}).get("terrain") or name
    panel_of = {b_["key"]: b_ for b_ in (panels or {}).get("buildings", []) if b_.get("kind") == "building"}
    index = {}
    for (tx, ty), items in tiles.items():
        bases = []
        for f in items:
            pr = f["properties"]
            ring_ = np.array(f["geometry"]["coordinates"][0], dtype=float)
            bases.append(float(pr["base_z"]) if pr.get("base_z") is not None else ground_lowest(ring_))
        mesh = Mesh(x0 + tx * tile_m, y0 + ty * tile_m, min(bases))
        for bid, (f, base) in enumerate(zip(items, bases), start=1):
            pr = f["properties"]
            mesh.id = (float(bid), 0.0)
            key = f"{terrain_name}/{pr.get('name')}"
            hp = panel_of.get(key)
            r = _hash01(pr.get("name"))
            wall_slot = SLOT_PLASTER if r < 0.55 else (SLOT_OLD_PLASTER if r < 0.85 else SLOT_BRICK)
            roof_slot = SLOT_ROOF_RED if _hash01(str(pr.get("name")) + "r") < 0.7 else SLOT_ROOF_DARK
            t = template_of(pr.get("template")) if pr.get("template") else None
            if hp and hp["panels"]:   # U-20: HROT's own panels - the walls, openings and ids BuildingDamage names
                if t and pr.get("origin") is not None:   # the template's footprint placed as HROT's TerrainBuildingExpander places it
                    ang = math.radians(float(pr.get("rotation", 0))); ca, sa = math.cos(ang), math.sin(ang); ox, oy = pr["origin"]
                    ring_ = _ccw(np.array([(ox + x * ca - y * sa, oy + x * sa + y * ca) for x, y in t["footprint"]], dtype=float))
                else:
                    ring_ = _ccw(np.array(f["geometry"]["coordinates"][0], dtype=float)[:-1])
                top = build_panels(mesh, bid, hp["panels"], wall_slot)
                levels = sorted({round(float(q["base"]), 3) for q in hp["panels"]})
                build_floors(mesh, ring_, levels, SLOT_PLASTER)
                kinds["fromPanels"] = kinds.get("fromPanels", 0) + 1
                kinds["windows"] += sum(1 for q in hp["panels"] for o in q["openings"] if o["kind"] == "window")
                kinds["doors"] += sum(1 for q in hp["panels"] for o in q["openings"] if o["kind"] == "door")
            elif t and not t.get("solid") and pr.get("origin") is not None:
                ang = math.radians(float(pr.get("rotation", 0))); ca, sa = math.cos(ang), math.sin(ang)
                ox, oy = pr["origin"]
                fp = np.array([(ox + x * ca - y * sa, oy + x * sa + y * ca) for x, y in t["footprint"]], dtype=float)
                ccw = np.sum(fp[:, 0] * np.roll(fp[:, 1], -1) - np.roll(fp[:, 0], -1) * fp[:, 1]) > 0
                # the template's walls are its footprint's edges (from = vertex i): map each wall's openings onto the edge index
                openings = {}
                for si, st in enumerate(t["storeys"]):
                    for wl in st.get("walls", []):
                        a = np.array(wl["from"], dtype=float)
                        k = int(np.argmin([np.hypot(*(np.array(v_, dtype=float) - a)) for v_ in t["footprint"]]))
                        ops = wl.get("openings", [])
                        if not ccw:   # the ring is reversed below ⇒ the edge index and the distance along it change
                            L = float(np.hypot(*(np.array(wl["to"], dtype=float) - a)))
                            k = (len(fp) - 1 - k) % len(fp) - 1
                            ops = [dict(o, at=L - float(o.get("at", 0)) - float(o.get("width", 1))) for o in ops]
                        openings.setdefault((si, k % len(fp)), []).extend(ops)
                        kinds["windows"] += sum(1 for o in ops if o.get("kind") != "door"); kinds["doors"] += sum(1 for o in ops if o.get("kind") == "door")
                ring_ = fp if ccw else fp[::-1]
                top = build_house(mesh, ring_, base, [float(s["height"]) for s in t["storeys"]], openings, wall_slot)
            else:
                ring_ = _ccw(np.array(f["geometry"]["coordinates"][0], dtype=float)[:-1])
                top = build_house(mesh, ring_, base, [float(pr.get("height") or 3.0)], {}, wall_slot)
            index[key] = {"tile": [tx, ty], "index": bid, "panels": bool(hp and hp["panels"]), "base": base, "top": top,
                          "footprint": [[round(float(x), 3), round(float(y), 3)] for x, y in ring_]}
            if hp and hp["panels"]:   # by rank (BuildingDamage.Panel): a, b, thickness - where the client cuts a breach
                index[key]["walls"] = [[q["a"][0], q["a"][1], q["b"][0], q["b"][1], q.get("thickness") or 0.2] for q in sorted(hp["panels"], key=lambda q: q["panel"])]
            rv = roofs.get(pr.get("name"))
            if rv is not None:
                rr = np.array(rv["geometry"]["coordinates"][0], dtype=float)[:-1]
                eaves = float(rv["properties"].get("base_z") or top)
                kinds[build_roof(mesh, rr, eaves, float(rv["properties"].get("height") or 1.5), roof_slot, wall_slot)] += 1
            else:
                kinds[build_roof(mesh, ring_, top, 0.0, roof_slot, wall_slot)] += 1
        path = os.path.join(out, f"{name}.bld_{tx}_{ty}.bin")
        mesh.write(path)
        written.append({"file": os.path.basename(path), "locationCm": [mesh.ox * 100.0, -mesh.oy * 100.0, mesh.oz * 100.0],
                        "buildings": len(items), "triangles": len(mesh.T), "tile": [tx, ty]})
    with open(os.path.join(out, f"{name}.buildings-mesh.json"), "w", encoding="utf-8") as f:
        json.dump({"tiles": written, "tileMetres": tile_m, "slots": SLOT_NAMES}, f, indent=1)
    # U-20: key -> tile + index (UV1.x) + footprint, for the client's damage (BuildingDamage keys)
    with open(os.path.join(out, f"{name}.buildings-index.json"), "w", encoding="utf-8") as f:
        json.dump({"format": 1, "terrain": terrain_name, "tileMetres": tile_m, "buildings": index}, f, indent=1)
    return {"tiles": len(written), "buildings": sum(w["buildings"] for w in written), "triangles": sum(w["triangles"] for w in written), **kinds}


# ── grass (UL-13) ────────────────────────────────────────────────────────────────────────────────────────────────────────────
def scatter_near(centres, radius, spacing, allowed, sample_z, rng, mask_xy):
    """Jittered points every `spacing` m within `radius` of any centre, where allowed(mask px) holds; records X, Y, Z (Unreal cm), yaw,
    width scale, height scale."""
    pts = []
    for cx, cy in centres:
        gx = np.arange(cx - radius, cx + radius, spacing); gy = np.arange(cy - radius, cy + radius, spacing)
        X, Y = np.meshgrid(gx, gy)
        X = X + rng.uniform(-0.45, 0.45, X.shape) * spacing; Y = Y + rng.uniform(-0.45, 0.45, Y.shape) * spacing
        keep = (X - cx) ** 2 + (Y - cy) ** 2 <= radius ** 2
        X, Y = X[keep], Y[keep]
        ci, ri = mask_xy(X, Y)
        ok = allowed(ri, ci)
        pts.append(np.stack([X[ok], Y[ok]], 1))
    if not pts: return np.zeros((0, 6), dtype="<f4")
    P = np.unique(np.round(np.concatenate(pts), 2), axis=0)
    Z = sample_z(P[:, 0], P[:, 1])
    n = len(P)
    return np.stack([P[:, 0] * 100, -P[:, 1] * 100, Z * 100 - 5, rng.uniform(0, 360, n), rng.uniform(0.7, 1.3, n), rng.uniform(0.7, 1.4, n)], 1).astype("<f4")
