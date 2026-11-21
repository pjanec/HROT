# Generates the terrain-authoring mockups — DESIGN_Terrain_Authoring.md S-0 (R-4198 … R-4201), on the ONE kit (R-4143):
#   docs/UX/terrain-authoring-1-wizard.svg     the workflow strip · the wizard: 1 Preset, 2 Preview (shuffle)
#   docs/UX/terrain-authoring-2-blocks.svg     the editor on a terrain source: Add Entity ▸ Blocks, placing a block, a placed block (menu, Details)
#   docs/UX/terrain-authoring-3-features.svg   single features (wall / fence / hedge line, a bridge over the river) and the area scatter
#   docs/UX/terrain-authoring-4-save.svg       Save terrain as · the overwrite confirmation · the terrain picker's Edit terrain…
# Scope: what is NEW. The editor around it (menu bar, panels, the map's other layers) is drawn only as dimmed context and is UNCHANGED (R-4151).
# Plan colours are HROT's 2-D map colours (TerrainPlan2D: HeightColor buckets, ForestFill, WaterFill, WallFill) — one colour language with the OSM wizard.
import os, sys, random, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX")
W = 1240
H_COL = ["#E1CDA0", "#E1A05A", "#B45F3C", "#782828"]          # building fill by height (< 5, < 15, < 30 m, taller)
FOREST = "#286E32"; WATER = "#326EC8"; WALL = "#96968F"; SCRUB = "#6E8C3C"; HEDGE = "#4E7A3A"
GROUND = ["#33452C", "#3B5131", "#4A5C36", "#5E643C", "#6E6844"]  # relief bands, low → high (the plan's ground shading)
ROADC = {"primary": ("#F2C033", 4.2), "secondary": ("#E8D58A", 3.2), "residential": ("#D8D8DC", 2.2), "track": ("#A08868", 1.3)}
G = dict(DICE=0xf522, SHUFFLE=0xf074, TREE=0xf1bb, WATER=0xf773, BRIDGE=0xe4c8, CAR=0xf1b9, DUMPSTER=0xf793, SEEDLING=0xf4d8,
         CITY=0xf64f, TRACTOR=0xf722, WHEAT=0xe2cd, ROAD=0xf018, UNPACK=0xf49e, SAVE=0xf0c7, EDIT=0xf044, CUBE=0xf1b2,
         HOUSE=0xe3af, TREES=0xe4e5, ROTATE=0xf2f1, LEAF=0xf06c, WALL=0xf7a4, RULER=0xf545, LAYERS=0xf5fd, NEXT=0xf054, BACK=0xf053,
         MOUNT=0xf6fc, CHECKP=0xf3ed, BLANK=0xf0c8)


# ───────────────────────────── drawing helpers ─────────────────────────────
def poly(s, pts, fill, stroke=None, op=1.0, sw=1, dash=None):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    st = f" stroke='{stroke}' stroke-width='{sw}'" if stroke else ""
    d = f" stroke-dasharray='{dash}'" if dash else ""
    s.raw(f"<polygon points='{p}' fill='{fill}' fill-opacity='{op}'{st}{d}/>")


def pline(s, pts, col, w, dash=None, op=1.0):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    d = f" stroke-dasharray='{dash}'" if dash else ""
    s.raw(f"<polyline points='{p}' fill='none' stroke='{col}' stroke-width='{w}' stroke-linecap='round' stroke-linejoin='round' stroke-opacity='{op}'{d}/>")


def clip(s, cid, x, y, w, h):
    s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{w}' height='{h}'/></clipPath><g clip-path='url(#{cid})'>")


def unclip(s): s.raw("</g>")


def blob(cx, cy, rx, ry, r, n=14, j=0.18):
    return [(cx + rx * math.cos(2 * math.pi * k / n) * (1 + r.uniform(-j, j)), cy + ry * math.sin(2 * math.pi * k / n) * (1 + r.uniform(-j, j))) for k in range(n)]


def ground(s, x, y, w, h, seed, relief=2):
    """The relief: nested height bands with a contour line (relief 0 flat … 3 mountainous)."""
    r = random.Random(seed)
    s.rect(x, y, w, h, GROUND[0])
    for _ in range(2 + relief * 2):
        cx, cy = x + r.uniform(0.1, 0.9) * w, y + r.uniform(0.1, 0.9) * h
        base = r.uniform(0.12, 0.28) * min(w, h) * (0.6 + 0.25 * relief)
        for b in range(1, min(len(GROUND), 2 + relief)):
            k = 1 - b * 0.24
            pts = blob(cx, cy, base * k * 1.4, base * k, r)
            poly(s, pts, GROUND[b], "#2A3824", op=0.9, sw=0.6)


def house(s, x, y, w, h, storeys, ang=0):
    col = H_COL[0 if storeys <= 1 else 1 if storeys <= 4 else 2 if storeys <= 9 else 3]
    c, sn = math.cos(ang), math.sin(ang)
    pts = [(x + px * c - py * sn, y + px * sn + py * c) for px, py in [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]]
    poly(s, pts, col, "#2A2A2A", sw=0.5)


def town(s, x, y, w, h, r, dense=True):
    """A block grid of streets with houses along them."""
    for i in range(int(h // 26) + 1):
        pline(s, [(x, y + i * 26), (x + w, y + i * 26 + r.uniform(-3, 3))], ROADC["residential"][0], 2)
    for i in range(int(w // 34) + 1):
        pline(s, [(x + i * 34, y), (x + i * 34 + r.uniform(-3, 3), y + h)], ROADC["residential"][0], 2)
    for gx in range(int(w // 34)):
        for gy in range(int(h // 26)):
            for k in range(3 if dense else 2):
                if not dense and r.random() < 0.4: continue
                house(s, x + gx * 34 + 7 + k * 10, y + gy * 26 + 8 + r.uniform(0, 4), 8, 7, r.choice([2, 3, 4, 5, 6]) if dense else r.choice([1, 2, 2]))


def village(s, pts, r, n=14):
    """Houses along a street (both sides)."""
    pline(s, pts, ROADC["residential"][0], 2.2)
    for k in range(n):
        t = (k + 0.5) / n
        i = min(int(t * (len(pts) - 1)), len(pts) - 2); f = t * (len(pts) - 1) - i
        (x0, y0), (x1, y1) = pts[i], pts[i + 1]
        px, py = x0 + (x1 - x0) * f, y0 + (y1 - y0) * f
        a = math.atan2(y1 - y0, x1 - x0); side = 1 if k % 2 else -1
        house(s, px - math.sin(a) * 9 * side, py + math.cos(a) * 9 * side, 9, 7, r.choice([1, 2, 2]), a)


def wood(s, pts, op=0.9):
    poly(s, pts, FOREST, "#1E5226", op=op, sw=0.8)


def block_outline(s, x, y, w, h, label, glyph=None, gc=None, sel=False, ghost=False):
    col = C.ACC if (sel or ghost) else "#D8D8E0"
    s.rect(x, y, w, h, col, op=0.10 if (sel or ghost) else 0.0)
    s.rect(x, y, w, h, "none", stroke=col, sw=2 if sel else 1.2, dash="6 4" if not sel else None, rx=3)
    lw = text_width(label, 11) + (22 if glyph else 10)
    s.rect(x, y - 17, lw, 16, C.POPUP, stroke=col, rx=8, op=0.92)
    if glyph: s.glyph(glyph, x + 5, y - 15, gc, 11)
    s.text(x + (19 if glyph else 5), y - 5, label, C.TXT, 11)


def plan(s, x, y, w, h, seed=7, relief=2, blocks=True, cid="p", outlines=True):
    """A generated terrain at the plan's scale: relief, the river, the road net, and (blocks=True) the laid-out blocks."""
    r = random.Random(seed)
    clip(s, cid, x, y, w, h)
    ground(s, x, y, w, h, seed, relief)
    river = [(x - 5, y + h * 0.78), (x + w * 0.22, y + h * 0.70), (x + w * 0.42, y + h * 0.80), (x + w * 0.63, y + h * 0.66), (x + w * 0.82, y + h * 0.72), (x + w + 5, y + h * 0.60)]
    pline(s, river, WATER, 9)
    roads = [("primary", [(x - 5, y + h * 0.42), (x + w * 0.35, y + h * 0.46), (x + w * 0.62, y + h * 0.38), (x + w + 5, y + h * 0.34)]),
             ("secondary", [(x + w * 0.48, y - 5), (x + w * 0.50, y + h * 0.42), (x + w * 0.46, y + h * 0.75), (x + w * 0.52, y + h + 5)]),
             ("secondary", [(x + w * 0.80, y + h * 0.35), (x + w * 0.86, y + h * 0.70), (x + w * 0.90, y + h + 5)]),
             ("track", [(x + w * 0.08, y + h * 0.43), (x + w * 0.12, y + h * 0.15), (x + w * 0.30, y + h * 0.05)])]
    for cls, pts in roads: pline(s, pts, ROADC[cls][0], ROADC[cls][1])
    for bx, by in [(x + w * 0.47, y + h * 0.756), (x + w * 0.865, y + h * 0.69)]:    # bridges
        s.rect(bx - 5, by - 9, 10, 18, "#C8C8C8", stroke="#2A2A2A", sw=0.6)
    bl = []
    if blocks:
        town(s, x + w * 0.40, y + h * 0.18, w * 0.18, h * 0.20, r)
        bl.append((x + w * 0.395, y + h * 0.17, w * 0.19, h * 0.22, "Town block ×4", G["CITY"], C.ORANGE))
        village(s, [(x + w * 0.62, y + h * 0.40), (x + w * 0.70, y + h * 0.48), (x + w * 0.78, y + h * 0.50)], r)
        bl.append((x + w * 0.60, y + h * 0.37, w * 0.20, h * 0.16, "Village street", G["HOUSE"], C.ORANGE))
        village(s, [(x + w * 0.14, y + h * 0.44), (x + w * 0.26, y + h * 0.50), (x + w * 0.30, y + h * 0.58)], r, 10)
        bl.append((x + w * 0.12, y + h * 0.41, w * 0.20, h * 0.20, "Hamlet", G["HOUSE"], C.ORANGE))
        for (fx, fy) in [(0.22, 0.18), (0.70, 0.12), (0.30, 0.90)]:
            for k in range(4): house(s, x + w * fx + (k % 2) * 12, y + h * fy + (k // 2) * 10, 10, 7, 1)
            bl.append((x + w * fx - 8, y + h * fy - 8, 36, 30, "Farmstead", G["TRACTOR"], C.ORANGE))
        for pts in [blob(x + w * 0.10, y + h * 0.80, w * 0.08, h * 0.10, r), blob(x + w * 0.88, y + h * 0.15, w * 0.10, h * 0.12, r),
                    blob(x + w * 0.62, y + h * 0.92, w * 0.07, h * 0.06, r)]:
            wood(s, pts)
        bl.append((x + w * 0.80, y + h * 0.06, w * 0.18, h * 0.22, "Copse", G["TREES"], C.GREEN))
        for k in range(5):   # hedgerow fields
            pline(s, [(x + w * 0.05, y + h * (0.55 + k * 0.03)), (x + w * 0.30, y + h * (0.57 + k * 0.03))], HEDGE, 1.6)
    if blocks and outlines:
        for b in bl: block_outline(s, *b)
    unclip(s)
    return bl


def steps(s, x, y, names, cur):
    for i, name in enumerate(names):
        done, c = i < cur, i == cur
        w = 26 + text_width(f"{i + 1}  {name}", 12) + 14
        s.rect(x, y, w, 24, C.BTNSEL if c else (C.FRAME if done else C.NEUTRAL), rx=12, stroke=C.ACC if c else C.BORDER)
        if done: s.glyph(FA.CHECK, x + 9, y + 6, C.OK, 11)
        else: s.text(x + 13, y + 16, str(i + 1), C.TXT if c else C.DIS, 12, weight="bold")
        s.text(x + 28, y + 16, name, C.TXT if (c or done) else C.DIS, 12)
        x += w
        if i < len(names) - 1: s.line(x + 4, y + 12, x + 20, y + 12, C.SEP, 1.2); x += 24


def seg(s, x, y, opts, sel, w=None):
    """A segmented choice (one row of buttons, the selected one filled)."""
    for i, o in enumerate(opts):
        bw = w or (text_width(o, 12) + 22)
        s.rect(x, y, bw, 22, C.BTNSEL if i == sel else C.BTN, rx=3, stroke=C.ACC if i == sel else None)
        s.text(x + bw / 2, y + 15, o, C.TXT, 12, anchor="middle"); x += bw + 3
    return x


def tile(s, x, y, w, h, name, line, seed, kind, sel=False):
    """A gallery tile: a thumbnail (the recipe at its default seed, drawn by the atlas pipeline) + the name + one line."""
    s.rect(x, y, w, h + 38, C.NEUTRALSEL if sel else C.NEUTRAL, stroke=C.ACC if sel else C.BORDER, rx=4, sw=1.5 if sel else 1)
    r = random.Random(seed)
    clip(s, f"t{seed}{int(x)}{int(y)}", x + 4, y + 4, w - 8, h - 6)
    s.rect(x + 4, y + 4, w - 8, h - 6, GROUND[1])
    tx, ty, tw, th = x + 4, y + 4, w - 8, h - 6
    if kind == "town": town(s, tx + 6, ty + 6, tw - 12, th - 12, r)
    elif kind == "village": village(s, [(tx + 6, ty + th * 0.6), (tx + tw * 0.5, ty + th * 0.45), (tx + tw - 6, ty + th * 0.55)], r, 12)
    elif kind == "hamlet": village(s, [(tx + 10, ty + th * 0.5), (tx + tw - 10, ty + th * 0.6)], r, 6)
    elif kind == "farm":
        pline(s, [(tx, ty + th * 0.8), (tx + tw, ty + th * 0.75)], ROADC["track"][0], 2)
        for k in range(4): house(s, tx + tw * 0.35 + (k % 2) * 22, ty + th * 0.30 + (k // 2) * 16, 18, 11, 1)
        for k in range(4): pline(s, [(tx + 4, ty + 6 + k * 9), (tx + tw * 0.28, ty + 8 + k * 9)], HEDGE, 1.4)
    elif kind == "check":
        pline(s, [(tx, ty + th * 0.5), (tx + tw, ty + th * 0.5)], ROADC["primary"][0], 6)
        for k in range(5): s.rect(tx + tw * 0.40 + k * 7, ty + th * 0.30, 5, 4, "#B9A97A")
        s.rect(tx + tw * 0.30, ty + th * 0.62, 14, 10, H_COL[0], stroke="#222", sw=0.5)
        s.rect(tx + tw * 0.55, ty + th * 0.47, 22, 3, "#E65555")
    elif kind == "road":
        pline(s, [(tx, ty + th * 0.5), (tx + tw, ty + th * 0.5)], ROADC["secondary"][0], 6)
        for k in range(4): s.rect(tx + tw * 0.42 + (k % 2) * 6, ty + th * 0.36 + k * 7, 8, 5, "#8C8C96", stroke="#222", sw=0.5)
        s.rect(tx + tw * 0.62, ty + th * 0.40, 16, 8, "#6A6A72", stroke="#222", sw=0.5)
    elif kind == "defence":
        for k in range(6): s.rect(tx + 10 + k * 18, ty + th * 0.40 + (k % 2) * 4, 14, 4, "#B9A97A")
        pline(s, [(tx + 6, ty + th * 0.62), (tx + tw - 6, ty + th * 0.65)], "#7A6A4A", 3, dash="4 3")
    elif kind in ("copse", "orchard"):
        if kind == "copse": wood(s, blob(tx + tw / 2, ty + th / 2, tw * 0.36, th * 0.34, r))
        else:
            for i in range(6):
                for j in range(3): s.raw(f"<circle cx='{tx + 14 + i * 18}' cy='{ty + 14 + j * 18}' r='5' fill='{FOREST}'/>")
    elif kind == "hedge":
        for k in range(4): pline(s, [(tx + 4, ty + 8 + k * 16), (tx + tw - 4, ty + 10 + k * 16)], HEDGE, 2)
        pline(s, [(tx + tw * 0.45, ty), (tx + tw * 0.48, ty + th)], HEDGE, 2)
    else:   # a glyph tile (single features / clutter)
        g, gc = kind
        s.rect(tx, ty, tw, th, "#26262E"); s.glyph(g, tx + tw / 2 - 16, ty + th / 2 - 16, gc, 32)
    unclip(s)
    s.text(x + 8, y + h + 14, name, C.TXT, 12, weight="bold")
    s.text(x + 8, y + h + 30, line, C.NOTE, 10.5)


def groups(s, x, y, w, items, sel):
    for i, (lab, g, gc, n, new) in enumerate(items):
        yy = y + i * 24
        if i == sel: s.rect(x, yy, w, 22, C.NEUTRALSEL, rx=3)
        s.glyph(g, x + 8, yy + 5, gc, 12, op=1 if new else 0.5)
        s.text(x + 28, yy + 15, lab, C.TXT if new else C.DIS, 12)
        if n is not None: s.text(x + w - 8, yy + 15, str(n), C.NOTE, 11, anchor="end")
        if new and i != sel: s.rect(x + w - 34, yy + 5, 4, 12, C.ACC, rx=2)


GROUPS = [("Recent & favourites", FA.STAR, C.AMBER, 8, False), ("Soldiers", FA.PERSON_RIFLE, C.BLUE, 41, False), ("Ground vehicles", 0xf5e4, C.BLUE, 33, False),
          ("Aircraft", FA.PLANE, C.BLUE, 12, False), ("Units", FA.USERS, C.BLUE, 18, False), ("Tactical symbols", FA.FLAG, C.TEAL, 24, False),
          ("Areas & routes", FA.POLYGON, C.TEAL, 6, False), ("Props", FA.CUBES, C.GREY, 4, False),
          ("Blocks", G["CITY"], C.ORANGE, 10, True), ("Buildings", G["HOUSE"], C.ORANGE, 9, True), ("Vegetation", G["TREE"], C.GREEN, 5, True),
          ("Walls & fences", G["WALL"], C.GREY, 6, True), ("Clutter", G["DUMPSTER"], C.GREY, 10, True), ("Water & bridges", G["WATER"], C.BLUE, 3, True)]


# ═════════════════════════ PAGE 1 — the workflow strip and the wizard ═════════════════════════
s = Svg(W, 1500, "Terrain authoring — 1 / 4: the workflow and the random-terrain wizard (DESIGN_Terrain_Authoring.md §12–§13, R-4200)")
# the workflow strip
WF = [("Wizard", "pick a preset", G["WAND"] if "WAND" in G else FA.WAND, C.VIOLET), ("Shuffle", "until it looks right", G["DICE"], C.AMBER),
      ("Edit", "move, re-roll, add", G["EDIT"], C.ACC), ("Save terrain", "baked, mesh pre-built", G["SAVE"], C.OK),
      ("Edit terrain…", "reopens the source any time", G["UNPACK"], C.TEAL)]
x0 = 20
for i, (t, sub, g, gc) in enumerate(WF):
    s.rect(x0, 46, 214, 54, C.NEUTRAL, stroke=gc, rx=6, sw=1.4)
    s.glyph(g, x0 + 12, 58, gc, 22); s.text(x0 + 46, 68, t, C.TXT, 13.5, weight="bold"); s.text(x0 + 46, 87, sub, C.SUB, 11.5)
    if i < len(WF) - 1:
        s.o.append(f"<path d='M{x0 + 218} 73 l18 0 m-6 -6 l6 6 l-6 6' stroke='{C.SEP}' stroke-width='1.8' fill='none'/>")
    x0 += 244
s.o.append(f"<path d='M{x0 - 137} 102 q0 18 -40 18 l-690 0 q-40 0 -40 -18' stroke='{C.TEAL}' stroke-width='1.2' fill='none' stroke-dasharray='4 4'/>")
s.text(560, 134, "the source scenario stays inside the terrain — edit and save again as often as needed (§14)", C.NOTE, 11.5, anchor="middle")

# ── step 1: Preset ──
PX, PY, PW, PH = 20, 160, 1200, 520
s.window(PX, PY, PW, PH, "New random terrain")
steps(s, PX + 12, PY + 30, ["Preset", "Preview", "Open"], 0)
s.separator_text(PX + 16, PY + 70, "What kind of place", PX + 790)
PRE = [("Plain terrain", "ground, roads and water only", "plain"), ("Open countryside", "fields, hedgerows, farms, woods", "farm"),
       ("Villages", "a few villages joined by roads", "village"), ("Small town", "a town of ~300 houses", "town"),
       ("Town with suburbs", "a town, villages around it", "mixed"), ("Mixed", "a town, villages, many woods", "copse")]
for i, (n, ln, kind) in enumerate(PRE):
    tx, ty = PX + 16 + (i % 3) * 258, PY + 96 + (i // 3) * 176
    sel = i == 4
    s.rect(tx, ty, 248, 166, C.NEUTRALSEL if sel else C.NEUTRAL, stroke=C.ACC if sel else C.BORDER, rx=5, sw=1.6 if sel else 1)
    if kind == "plain":
        clip(s, f"pp{i}", tx + 5, ty + 5, 238, 118); ground(s, tx + 5, ty + 5, 238, 118, 3, 2)
        pline(s, [(tx, ty + 95), (tx + 120, ty + 85), (tx + 250, ty + 100)], WATER, 6)
        pline(s, [(tx, ty + 50), (tx + 250, ty + 60)], ROADC["primary"][0], 3); unclip(s)
    elif kind == "mixed":
        clip(s, f"pp{i}", tx + 5, ty + 5, 238, 118); plan(s, tx + 5, ty + 5, 238, 118, 11, 2, True, f"mx{i}", outlines=False); unclip(s)
    else:
        r = random.Random(i)
        clip(s, f"pp{i}", tx + 5, ty + 5, 238, 118); ground(s, tx + 5, ty + 5, 238, 118, i + 20, 1)
        if kind == "farm":
            for k in range(6): pline(s, [(tx + 5, ty + 20 + k * 17), (tx + 240, ty + 24 + k * 17)], HEDGE, 1.6)
            for k in range(4): house(s, tx + 80 + (k % 2) * 16, ty + 50 + (k // 2) * 12, 12, 8, 1)
            wood(s, blob(tx + 200, ty + 40, 34, 26, r))
        elif kind == "village":
            village(s, [(tx + 10, ty + 40), (tx + 110, ty + 50), (tx + 230, ty + 30)], r, 12)
            village(s, [(tx + 40, ty + 105), (tx + 200, ty + 95)], r, 8)
        elif kind == "town": town(s, tx + 50, ty + 20, 150, 90, r)
        elif kind == "copse":
            town(s, tx + 90, ty + 40, 70, 50, r)
            for k in range(4): wood(s, blob(tx + 30 + k * 60, ty + 20 + (k % 2) * 80, 26, 18, r))
        unclip(s)
    s.text(tx + 10, ty + 140, n, C.TXT, 13, weight="bold"); s.text(tx + 10, ty + 157, ln, C.NOTE, 11.5)
    if sel: s.glyph(FA.CIRCLE_CHECK, tx + 222, ty + 133, C.ACC, 16)
RX, RW = PX + 810, 370
s.separator_text(RX, PY + 70, "Terrain", RX + RW)
s.form_row(RX, PY + 94, "Name", 90, "field", RW - 100, "riverside-3")
s.text(RX + 90, PY + 128, "Saved as assets/terrain/riverside-3/ + a scenario", C.NOTE, 11)
s.text(RX, PY + 160, "Size", C.TXT, 12.5); seg(s, RX + 90, PY + 145, ["1 km", "2 km", "3 km", "5.5 km"], 2, 62)
s.text(RX, PY + 196, "Relief", C.TXT, 12.5); seg(s, RX + 90, PY + 181, ["Flat", "Rolling", "Hilly", "Mountains"], 1, 62)
s.text(RX, PY + 232, "Vegetation", C.TXT, 12.5); seg(s, RX + 90, PY + 217, ["Sparse", "Normal", "Dense"], 1, 84)
s.text(RX, PY + 268, "Water", C.TXT, 12.5); seg(s, RX + 90, PY + 253, ["None", "A stream", "A river"], 2, 84)
s.text(RX, PY + 304, "Seed", C.TXT, 12.5); s.field(RX + 90, PY + 290, 150, "48213"); s.button(RX + 246, PY + 290, 34, "", G["DICE"], C.AMBER)
s.rect(RX, PY + 338, RW, 74, C.NEUTRAL, stroke=C.BORDER, rx=3)
s.glyph(FA.INFO, RX + 10, PY + 348, C.INFO, 13)
s.note(RX + 32, PY + 360, ["Everything the generator places — towns, villages,", "farms, woods — is an ordinary block in a scenario:",
                           "move, re-roll or delete any of it later in the editor."], C.SUB, 11.5)
y = PY + PH - 34; s.line(PX, y - 8, PX + PW, y - 8, C.SEP)
s.button(PX + PW - 250, y, 130, "Generate", G["NEXT"], C.TXT, fill=C.BTNSEL); s.button(PX + PW - 110, y, 90, "Cancel")
s.note(PX, PY + PH + 18, ["① Preset — six pictures, one choice. A preset decides the MIX (how much town / village / farm / wood) and the densities; there is no",
                          "   building-density knob (R-4199 B-8: few choices). Plain terrain = the ground, the roads and the water only (R-4198 TA-2).",
                          "   Size, Relief, Vegetation and Water are four short rows; the seed is shown so a result can be reproduced, the dice rolls a new one."], C.NOTE, 11.5)

# ── step 2: Preview ──
PY2 = PY + PH + 90
s.window(PX, PY2, PW, 620, "New random terrain")
steps(s, PX + 12, PY2 + 30, ["Preset", "Preview", "Open"], 1)
CX, CY, CW, CH = PX + 10, PY2 + 64, 820, 500
bl = plan(s, CX, CY, CW, CH, 7, 2, True, "big")
s.rect(CX + 10, CY + CH - 28, 210, 20, C.POPUP, op=0.85, rx=3); s.text(CX + 18, CY + CH - 14, "3.0 × 3.0 km · relief 120 m · 1 river", C.SUB, 11)
# a hovered block
s.tooltip(CX + 250, CY + 205, 200, ["Town block ×4 — 112 houses", "seed 4471 · 3–6 storeys", "click a block to see it larger"])
RX, RW = CX + CW + 14, PW - CW - 34
s.separator_text(RX, CY, "What was generated", RX + RW)
rows = [(G["CITY"], C.ORANGE, "Town blocks", "4", "112 houses"), (G["HOUSE"], C.ORANGE, "Village streets", "3", "41 houses"),
        (G["HOUSE"], C.ORANGE, "Hamlets", "2", "18 houses"), (G["TRACTOR"], C.ORANGE, "Farmsteads", "5", "20 buildings"),
        (G["TREES"], C.GREEN, "Woods and copses", "9", "64 ha"), (G["WALL"], C.GREY, "Hedgerow fields", "6", "4.2 km"),
        (G["DUMPSTER"], C.GREY, "Street clutter", "—", "≈ 380 items"), (G["ROAD"], C.AMBER, "Roads", "", "37 km"),
        (G["WATER"], C.BLUE, "River", "", "3.4 km · 3 bridges")]
for i, (g, gc, lab, n, sub) in enumerate(rows):
    yy = CY + 24 + i * 24
    s.glyph(g, RX + 2, yy + 3, gc, 12.5); s.text(RX + 24, yy + 14, lab, C.TXT, 12)
    s.text(RX + 170, yy + 14, n, C.SUB, 12, anchor="end"); s.text(RX + 180, yy + 14, sub, C.NOTE, 11.5)
s.rect(RX, CY + 250, RW, 26, "#16301A", stroke=C.OK, rx=3); s.glyph(FA.CIRCLE_CHECK, RX + 8, CY + 256, C.OK, 13)
s.text(RX + 28, CY + 267, "Within budget — 191 houses of ~1 800", C.TXT, 12)
s.separator_text(RX, CY + 296, "Not happy?", RX + RW)
s.button(RX, CY + 322, RW, "Shuffle layout", G["DICE"], C.AMBER); s.text(RX + RW - 8, CY + 336, "≈ 2 s", C.NOTE, 11, anchor="end")
s.text(RX, CY + 360, "new places for the blocks, same ground", C.NOTE, 11)
s.button(RX, CY + 374, RW, "Shuffle ground", G["MOUNT"], C.GREEN); s.text(RX + RW - 8, CY + 388, "≈ 40 s", C.NOTE, 11, anchor="end")
s.text(RX, CY + 412, "new hills, roads and river, then a layout", C.NOTE, 11)
s.button(RX, CY + 436, RW, "Back to the preset", G["BACK"], C.SUB)
y = PY2 + 620 - 34; s.line(PX, y - 8, PX + PW, y - 8, C.SEP)
s.button(PX + PW - 330, y, 90, "Back", G["BACK"], C.SUB)
s.button(PX + PW - 230, y, 110, "Open in editor", G["EDIT"], C.TXT, fill=C.BTNSEL); s.button(PX + PW - 110, y, 90, "Cancel")
s.note(PX, PY2 + 620 + 18, ["② Preview — the 2-D plan in HROT's map colours (buildings by height, woods, water, roads); each block is outlined with its recipe's name (R-4200 W-2).",
                            "   Shuffle layout = new seeds for the blocks on the same ground (seconds); Shuffle ground = new relief, road net and river, then a layout (netconvert, slower) (W-1).",
                            "   No moving of blocks here — that is the editor's job. Open in editor writes the ground terrain and the block scenario, and opens the scenario (③)."], C.NOTE, 11.5)
s.save(os.path.join(OUT, "terrain-authoring-1-wizard.svg"))


# ═════════════════════════ PAGE 2 — the editor: blocks through Add Entity ═════════════════════════
s = Svg(W, 1300, "Terrain authoring — 2 / 4: blocks in the scenario editor (DESIGN_Terrain_Authoring.md §11, R-4199)")
# the editor context (dimmed, unchanged)
s.window(20, 46, 1200, 600, "HROT Editor — riverside-3", chip=("TERRAIN SOURCE", "#0F0F0F", C.TEAL))
s.rect(20, 66, 1200, 22, "#1A1A20");
for i, m in enumerate(["File", "Edit", "View", "Scenario", "Run", "Help"]): s.text(32 + i * 62, 81, m, C.DIS, 12)
MX, MY, MW, MH = 20, 88, 1200, 558
plan(s, MX, MY, MW, MH, 7, 2, True, "ed", outlines=False)
s.rect(MX, MY, MW, MH, "#0F0F0F", op=0.35)
# placed blocks (outlined dashed) + the selected one
block_outline(s, MX + MW * 0.395, MY + MH * 0.17, MW * 0.19, MH * 0.22, "Town block · seed 4471", G["CITY"], C.ORANGE, sel=True)
block_outline(s, MX + MW * 0.60, MY + MH * 0.37, MW * 0.20, MH * 0.16, "Village street", G["HOUSE"], C.ORANGE)
block_outline(s, MX + MW * 0.12, MY + MH * 0.41, MW * 0.20, MH * 0.20, "Hamlet", G["HOUSE"], C.ORANGE)
# the ghost of a farmstead being placed, snapping to the track
gx, gy = MX + MW * 0.19, MY + MH * 0.10 - 34
s.rect(gx, gy, 70, 52, C.ACC, op=0.18); s.rect(gx, gy, 70, 52, "none", stroke=C.ACC, sw=1.6, dash="5 3", rx=3)
for k in range(4): house(s, gx + 18 + (k % 2) * 26, gy + 16 + (k // 2) * 18, 18, 11, 1)
s.rect(gx - 2, gy + 56, 196, 36, C.POPUP, stroke=C.ACC, rx=3, op=0.95)
s.glyph(G["ROAD"], gx + 6, gy + 61, C.AMBER, 11); s.text(gx + 24, gy + 71, "aligned to the track (R to rotate)", C.TXT, 11)
s.glyph(FA.CHECK, gx + 6, gy + 77, C.OK, 11); s.text(gx + 24, gy + 87, "clear of water and buildings", C.SUB, 11)
# the block's context menu
s.menu(MX + MW * 0.585 + 8, MY + MH * 0.17 + 4, 250, [("#", "Town block · seed 4471"), ("Re-roll layout", "Ctrl+R", G["DICE"], C.AMBER),
                                                       ("Rotate", "R", G["ROTATE"], C.SUB), ("Move", "drag", FA.MOVE, C.SUB), "---",
                                                       ("Unpack into houses and features", None, G["UNPACK"], C.TEAL),
                                                       ("Change recipe…", None, G["CITY"], C.ORANGE), "---", ("Delete", "Del", FA.TRASH, C.RED)])
# Details for the selected block
DX, DY = 990, 300
s.window(DX, DY, 226, 330, "Details — Town block")
s.tabs(DX + 6, DY + 26, 214, [("Block", G["CITY"], C.ORANGE)], 0)
s.form_row(DX + 10, DY + 62, "Recipe", 60, "combo", 146, "Town block", glyph=G["CITY"], gc=C.ORANGE)
s.text(DX + 10, DY + 102, "Seed", C.TXT, 12.5); s.field(DX + 70, DY + 88, 100, "4471"); s.button(DX + 176, DY + 88, 34, "", G["DICE"], C.AMBER)
s.separator_text(DX + 10, DY + 120, "Fit to the site", DX + 216)
for i, (lab, on, dis) in enumerate([("Align to the nearest road", True, False), ("Keep out of water", True, False),
                                     ("Keep out of buildings", True, False), ("Level houses on pads", True, True)]):
    s.checkbox(DX + 10, DY + 144 + i * 26, on, dis); s.text(DX + 36, DY + 158 + i * 26, lab, C.DIS if dis else C.TXT, 12)
s.text(DX + 36, DY + 266, "flattening comes later (S-5)", C.NOTE, 10.5)
s.separator_text(DX + 10, DY + 278, "Contents", DX + 216)
s.text(DX + 10, DY + 310, "28 houses · 64 clutter items", C.SUB, 11.5)
s.note(20, 666, ["The editor around it is unchanged context (dimmed). NEW: the title chip TERRAIN SOURCE (§14 E-2), block outlines, the placing ghost, the block menu and its Details.",
                 "• Placing: the ghost fits itself to the site as you move it — snaps to the nearest road, refuses water and buildings (B-3); click places ONE scenario entity (B-2).",
                 "• A placed block: Re-roll = a new seed, same place; Unpack = becomes its separate houses and features (frozen, no longer follows the recipe — B-Q1); one undo step each (B-6)."], C.NOTE, 11.5)
# the Add Entity gallery, Blocks group
AX, AY, AW, AH = 20, 740, 1200, 500
s.window(AX, AY, AW, AH, "Add Entity")
s.field(AX + 10, AY + 30, 300, "Search all groups", hint=True); s.glyph(FA.SEARCH, AX + 290, AY + 33, C.DIS, 12)
s.text(AX + 330, AY + 44, "Side", C.DIS, 12); seg(s, AX + 370, AY + 29, ["Friendly", "Hostile", "Neutral"], 0, 70)
s.text(AX + 600, AY + 44, "terrain items have no side — greyed while a terrain group is open", C.NOTE, 11)
s.rect(AX + 370, AY + 29, 216, 22, C.BG, op=0.55)
groups(s, AX + 10, AY + 64, 200, GROUPS, 8)
s.line(AX + 218, AY + 60, AX + 218, AY + AH - 10)
cx = AX + 230
for i, ch in enumerate(["All", "Rural", "Urban", "Military", "Nature"]):
    cx += s.chip(cx, AY + 64, ch, arrow=False, border=C.ACC if i == 0 else None) + 6
TL = [("Farmstead", "house, barns, yard", "farm"), ("Hamlet", "6 houses on a lane", "hamlet"), ("Village street", "12 houses, gardens", "village"),
      ("Town block", "3–6 storeys, a street grid", "town"), ("Checkpoint", "booth, barrier, sandbags", "check"),
      ("Roadblock", "cars, blocks across a road", "road"), ("Defensive position", "sandbag walls, a trench", "defence"),
      ("Copse", "a small wood", "copse"), ("Orchard", "rows of fruit trees", "orchard"), ("Hedgerow fields", "fields lined by hedges", "hedge")]
for i, (n, ln, k) in enumerate(TL):
    tile(s, AX + 230 + (i % 5) * 150, AY + 96 + (i // 5) * 160, 142, 92, n, ln, 30 + i, k, sel=(i == 0))
# side pane
SX = AX + 990
s.line(SX - 8, AY + 60, SX - 8, AY + AH - 10)
s.rect(SX, AY + 64, 200, 130, GROUND[1], rx=3); tile(s, SX, AY + 64, 200, 130, "", "", 30, "farm")
s.text(SX, AY + 226, "Farmstead", C.TXT, 14, weight="bold")
s.note(SX, AY + 246, ["A house, two barns and a yard,", "fitted to the nearest track.", "Every placement is a new layout", "(Re-roll for another)."], C.SUB, 11.5)
s.text(SX, AY + 330, "Footprint ≈ 60 × 45 m", C.NOTE, 11); s.text(SX, AY + 346, "4 buildings · 1 enterable", C.NOTE, 11)
s.button(SX, AY + 440, 200, "Place on map", FA.LOCATION, C.RED, fill=C.BTNSEL)
s.note(AX, AY + AH + 18, ["Add Entity — the SAME gallery (Add Entity Picker §2f); NEW are the six terrain groups at the bottom of the list (B-5), each a curated set (B-8).",
                          "A tile's picture is the recipe at its default seed, rendered by the existing thumbnail pipeline (§2j); the existing groups are unchanged context."], C.NOTE, 11.5)
s.save(os.path.join(OUT, "terrain-authoring-2-blocks.svg"))


# ═════════════════════════ PAGE 3 — single features and the area scatter ═════════════════════════
s = Svg(W, 1210, "Terrain authoring — 3 / 4: single features and the area scatter (DESIGN_Terrain_Authoring.md §2, §7, §8, §11 job ③)")
s.window(20, 46, 1200, 560, "HROT Editor — riverside-3", chip=("TERRAIN SOURCE", "#0F0F0F", C.TEAL))
MX, MY, MW, MH = 20, 66, 1200, 540
r = random.Random(5)
clip(s, "f", MX, MY, MW, MH)
ground(s, MX, MY, MW, MH, 12, 1)
river = [(MX - 5, MY + 420), (MX + 300, MY + 390), (MX + 560, MY + 440), (MX + 820, MY + 400), (MX + MW + 5, MY + 430)]
pline(s, river, WATER, 26)
pline(s, [(MX + 640, MY - 5), (MX + 650, MY + 300), (MX + 655, MY + MH + 5)], ROADC["secondary"][0], 4)
town(s, MX + 700, MY + 40, 260, 180, r)
village(s, [(MX + 80, MY + 120), (MX + 260, MY + 160), (MX + 420, MY + 140)], r, 12)
unclip(s)
s.rect(MX, MY, MW, MH, "#0F0F0F", op=0.30)
# ① a wall / fence / hedge line being drawn
pts = [(MX + 90, MY + 230), (MX + 220, MY + 250), (MX + 330, MY + 238), (MX + 410, MY + 270)]
pline(s, pts[:3], HEDGE, 5); pline(s, pts[2:], HEDGE, 5, dash="6 4", op=0.7)
for p in pts[:3]: s.rect(p[0] - 4, p[1] - 4, 8, 8, "#FFFFFF", stroke=C.ACC, sw=1.5)
s.rect(MX + 60, MY + 290, 250, 92, C.POPUP, stroke=C.ACC, rx=4, op=0.96)
s.text(MX + 72, MY + 308, "Hedge — click to add a point", C.TXT, 12, weight="bold")
s.text(MX + 72, MY + 330, "Kind", C.SUB, 11.5); seg(s, MX + 110, MY + 316, ["Hedge", "Wall", "Fence"], 0, 58)
s.text(MX + 72, MY + 360, "Height", C.SUB, 11.5); s.field(MX + 120, MY + 346, 60, "1.8 m")
s.text(MX + 190, MY + 360, "212 m · Enter ends", C.NOTE, 11)
# ② a bridge over the river
bx, by = MX + 640, MY + 380
s.rect(bx, by, 26, 80, "#C8C8C8", stroke=C.ACC, sw=1.6)
for k in range(4): s.line(bx + 3, by + 10 + k * 20, bx + 23, by + 10 + k * 20, "#8C8C8C")
s.rect(bx - 2, by - 14, 30, 14, "#A8A8A8", op=0.8); s.rect(bx - 2, by + 80, 30, 14, "#A8A8A8", op=0.8)
s.rect(bx + 40, by - 6, 230, 56, C.POPUP, stroke=C.ACC, rx=4, op=0.96)
s.glyph(G["BRIDGE"], bx + 50, by + 2, C.BLUE, 13); s.text(bx + 70, by + 13, "Bridge — spans 46 m", C.TXT, 12, weight="bold")
s.text(bx + 50, by + 31, "deck + ramps, joined to both banks", C.SUB, 11); s.text(bx + 50, by + 45, "carries: foot + vehicles (6 t)", C.NOTE, 11)
# ③ an area scatter over the town's streets
sc = [(MX + 690, MY + 30), (MX + 980, MY + 34), (MX + 990, MY + 230), (MX + 700, MY + 236)]
poly(s, sc, C.AMBER, C.AMBER, op=0.10, sw=1.6, dash="7 4")
for _ in range(70):
    px, py = r.uniform(MX + 700, MX + 980), r.uniform(MY + 40, MY + 228)
    k = r.random()
    s.rect(px, py, 5 if k < 0.6 else 3, 3 if k < 0.6 else 3, "#8C8C96" if k < 0.6 else ("#6A8A5A" if k < 0.8 else "#A0602A"))
s.rect(MX + 1000, MY + 40, 190, 168, C.POPUP, stroke=C.AMBER, rx=4, op=0.97)
s.glyph(G["DUMPSTER"], MX + 1010, MY + 50, C.AMBER, 13); s.text(MX + 1030, MY + 61, "Scatter in this area", C.TXT, 12, weight="bold")
s.text(MX + 1010, MY + 86, "What", C.SUB, 11.5); s.combo(MX + 1050, MY + 72, 130, "Street clutter")
s.text(MX + 1010, MY + 114, "Density", C.SUB, 11.5); seg(s, MX + 1062, MY + 100, ["Low", "Normal", "High"], 1, 38)
s.text(MX + 1010, MY + 142, "≈ 140 items · seed 902", C.SUB, 11.5); s.button(MX + 1150, MY + 128, 30, "", G["DICE"], C.AMBER)
s.button(MX + 1010, MY + 160, 80, "Done", fill=C.BTNSEL); s.button(MX + 1096, MY + 160, 84, "Cancel")
s.note(MX + 1000, MY + 226, ["one entity — an area + a recipe +", "a seed; Unpack makes it single items"], C.NOTE, 10.5)
for (x, y, lab) in [(MX + 72, MY + 220, "①"), (bx - 26, by + 30, "②"), (MX + 680, MY + 22, "③")]:
    s.raw(f"<circle cx='{x}' cy='{y}' r='10' fill='{C.ACC}'/>"); s.text(x, y + 4.5, lab, C.TXT, 12, anchor="middle", weight="bold")
s.note(20, 626, ["① A LINE feature (wall, fence, hedge): the existing point-sequence tool, then its kind and height — becomes WorldWall segments by material (§2).",
                 "② A BRIDGE: placed across water, it measures the span and joins both banks; a deck + ramps (a walkable) and a road edge — a chokepoint, and destructible (§8).",
                 "③ The AREA SCATTER: draw an area, pick Street clutter / Undergrowth / Rubble, a density — ONE entity (an area, a recipe, a seed), re-rollable (§11 job ③)."], C.NOTE, 11.5)
# the single-feature groups in the gallery (tiles only)
AX, AY = 20, 700
s.window(AX, AY, 1200, 440, "Add Entity")
groups(s, AX + 10, AY + 30, 200, GROUPS[8:], 4)
s.line(AX + 218, AY + 26, AX + 218, AY + 430)
s.text(AX + 230, AY + 46, "Clutter", C.TXT, 13, weight="bold"); s.text(AX + 290, AY + 46, "single items — or Scatter in an area… to place many at once", C.NOTE, 11.5)
s.button(AX + 1030, AY + 32, 160, "Scatter in an area…", G["DUMPSTER"], C.AMBER)
CL = [("Parked car", "cover, blocks a lane", (G["CAR"], C.GREY)), ("Skip", "steel, cover", (G["DUMPSTER"], C.GREY)),
      ("Rubbish pile", "hides, does not protect", (FA.TRASH, C.GREY)), ("Bench", "low obstacle", (0xf6c0 if False else G["CUBE"], C.GREY)),
      ("Bus shelter", "glass and steel", (0xf207, C.GREY)), ("Lamp post", "thin steel", (0xf0eb, C.AMBER)),
      ("Construction fence", "stops movement, not sight", (G["WALL"], C.GREY)), ("Market stall", "wood, concealment", (0xf54e, C.ORANGE)),
      ("Burnt-out car", "steel, cover", (FA.FIRE, C.RED)), ("Hay bales", "straw, concealment", (G["WHEAT"], C.AMBER))]
for i, (n, ln, k) in enumerate(CL):
    tile(s, AX + 230 + (i % 5) * 190, AY + 64 + (i // 5) * 170, 180, 96, n, ln, 60 + i, k)
s.note(AX, AY + 460, ["The single-feature groups: Buildings (templates), Vegetation (wood area, lone tree, bush), Walls & fences, Clutter, Water & bridges.",
                      "Each clutter item is a small box of a material (the obstacle bake's own shape, §7) — the material says what it does to bullets and sight.",
                      "The glyph tiles stand in for pictures: each item's thumbnail will be a render of its model, like every other Add Entity tile."], C.NOTE, 11.5)
s.save(os.path.join(OUT, "terrain-authoring-3-features.svg"))


# ═════════════════════════ PAGE 4 — saving the terrain, and editing it again ═════════════════════════
s = Svg(W, 1120, "Terrain authoring — 4 / 4: Save terrain as, saving again, and Edit terrain… (DESIGN_Terrain_Authoring.md §13–§14, R-4200)")
# the File menu
s.rect(20, 46, 300, 22, "#1A1A20"); s.text(30, 61, "File", C.TXT, 12, weight="bold"); s.text(80, 61, "Edit   View   Scenario", C.DIS, 12)
s.menu(20, 68, 300, [("New scenario…", "Ctrl+N", FA.FILE, C.SUB), ("New ▸ Random terrain…", None, FA.WAND, C.VIOLET),
                      ("New ▸ Terrain from OpenStreetMap…", None, FA.MAP, C.GREEN), "---", ("Save scenario", "Ctrl+S", G["SAVE"], C.SUB),
                      ("Save terrain", "Ctrl+Shift+S", FA.MOUNTAIN, C.OK), ("Save terrain as…", None, FA.MOUNTAIN, C.OK), "---",
                      ("Edit terrain…", None, G["EDIT"], C.TEAL)])
s.note(20, 320, ["Save terrain — only in a terrain source; overwrites", "the terrain it produced, after ② (W-4).",
                 "Save terrain as… — a new terrain from any scenario."], C.NOTE, 11.5)
# ① Save terrain as dialog
DX, DY, DW, DH = 350, 46, 560, 470
s.window(DX, DY, DW, DH, "Save terrain as")
s.form_row(DX + 16, DY + 36, "Name", 120, "field", 300, "riverside-3")
s.glyph(FA.CIRCLE_CHECK, DX + 444, DY + 40, C.OK, 13)
s.text(DX + 136, DY + 72, "assets/terrain/riverside-3/  (with its source inside)", C.NOTE, 11)
s.separator_text(DX + 16, DY + 92, "Baked into the terrain", DX + DW - 16)
for i, (g, gc, t) in enumerate([(G["CITY"], C.ORANGE, "23 blocks → 684 houses, 41 farm buildings"), (G["TREES"], C.GREEN, "14 woods, 3 scatters, 220 trees"),
                                 (G["WALL"], C.GREY, "6.1 km of walls, fences and hedges"), (G["BRIDGE"], C.BLUE, "3 bridges")]):
    s.glyph(g, DX + 20, DY + 116 + i * 22, gc, 12); s.text(DX + 42, DY + 127 + i * 22, t, C.TXT, 12)
s.separator_text(DX + 16, DY + 210, "Stays in the source only", DX + DW - 16)
s.glyph(FA.USERS, DX + 20, DY + 234, C.BLUE, 12); s.text(DX + 42, DY + 245, "12 units and 2 areas — not terrain (E-4)", C.SUB, 12)
s.glyph(FA.FIRE, DX + 20, DY + 256, C.RED, 12); s.text(DX + 42, DY + 267, "damage to 3 houses from a test run — not baked (E-6)", C.SUB, 12)
s.separator_text(DX + 16, DY + 296, "Then", DX + DW - 16)
s.checkbox(DX + 18, DY + 318, True); s.text(DX + 44, DY + 332, "Pre-build the walkable mesh", C.TXT, 12); s.text(DX + 250, DY + 332, "≈ 2 min; loads fast after", C.NOTE, 11)
s.checkbox(DX + 18, DY + 346, True); s.text(DX + 44, DY + 360, "Create a scenario on it:", C.TXT, 12); s.field(DX + 210, DY + 346, 200, "riverside-3-mission")
s.text(DX + 44, DY + 384, "empty of the baked blocks; units are not copied (E-5)", C.NOTE, 11)
s.button(DX + DW - 230, DY + DH - 32, 110, "Save terrain", FA.MOUNTAIN, C.OK, fill=C.BTNSEL); s.button(DX + DW - 110, DY + DH - 32, 90, "Cancel")
# ② the overwrite confirmation
OX, OY = 940, 46
s.window(OX, OY, 280, 230, "Save terrain riverside-3?")
s.glyph(FA.WARNING, OX + 14, OY + 34, C.PEND, 16)
s.note(OX + 40, OY + 46, ["It replaces the terrain this", "source made earlier.", "", "Used by 3 scenarios:"], C.TXT, 12)
for i, n in enumerate(["riverside-3-mission", "night-crossing", "bridge-defence"]):
    s.text(OX + 52, OY + 126 + i * 18, "• " + n, C.SUB, 11.5)
s.button(OX + 70, OY + 196, 110, "Replace", fill=C.BTNSEL); s.button(OX + 186, OY + 196, 80, "Cancel")
s.note(OX, OY + 250, ["② Saving again (W-4): names every", "scenario that will see the change."], C.NOTE, 11.5)
# ③ the terrain picker with Edit terrain…
TX, TY, TWW = 20, 560, 1200
s.window(TX, TY, TWW, 380, "New scenario — choose a terrain")
TT = [("riverside-3", "random · 3 km · editable source", 7, True, True), ("kolin-centre", "OpenStreetMap · 1.6 km", 21, False, False),
      ("real-1", "from the other simulator · 5.5 km", 22, False, False), ("demo-city", "hand-made", 23, False, False)]
for i, (n, ln, sd, src, sel) in enumerate(TT):
    x, y = TX + 16 + i * 238, TY + 34
    s.rect(x, y, 228, 200, C.NEUTRALSEL if sel else C.NEUTRAL, stroke=C.ACC if sel else C.BORDER, rx=5)
    if i == 0: plan(s, x + 5, y + 5, 218, 130, 7, 2, True, f"tp{i}", outlines=False)
    else:
        r = random.Random(sd); clip(s, f"tq{i}", x + 5, y + 5, 218, 130); ground(s, x + 5, y + 5, 218, 130, sd, 1 + i % 2)
        if i == 2:
            village(s, [(x + 10, y + 60), (x + 110, y + 70), (x + 210, y + 50)], r, 14); wood(s, blob(x + 160, y + 110, 40, 20, r))
        else: town(s, x + 40 + i * 6, y + 20, 140 - i * 20, 100, r)
        unclip(s)
    s.text(x + 10, y + 154, n, C.TXT, 13, weight="bold"); s.text(x + 10, y + 172, ln, C.NOTE, 11)
    if src:
        s.rect(x + 150, y + 10, 68, 18, C.TEAL, rx=9); s.text(x + 184, y + 23, "SOURCE", "#0F0F0F", 10.5, anchor="middle", weight="bold")
s.menu(TX + 16, TY + 244, 280, [("#", "riverside-3"), ("New scenario on it", "Enter", FA.FILE, C.SUB),
                                  ("Edit terrain…", None, G["EDIT"], C.TEAL), ("Show its source scenario", None, FA.FOLDER_OPEN, C.SUB)])
s.menu(TX + 254, TY + 244, 330, [("#", "kolin-centre (imported — no source yet)"), ("New scenario on it", "Enter", FA.FILE, C.SUB),
                                  ("Edit terrain…  (creates a source)", None, G["EDIT"], C.TEAL)])
s.note(TX, TY + 400, ["③ Edit terrain… (§14) — a terrain's right-click menu (shown for two terrains at once). A terrain with a SOURCE badge reopens its block scenario",
                      "   (the ground → blocks → baked chain is kept in its folder, E-1);",
                      "   an imported terrain gets a new source the first time — only additions, its own houses stay baked (E-3). The editor's title then says TERRAIN SOURCE (E-2).",
                      "   Removing an already-baked house is designed, not built (E-Q)."], C.NOTE, 11.5)
s.save(os.path.join(OUT, "terrain-authoring-4-save.svg"))
print("written: terrain-authoring-1..4 svg")
