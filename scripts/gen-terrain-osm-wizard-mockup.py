# Generates docs/UX/terrain-osm-wizard-visual.svg (steps 1 Area, 2 Options) and docs/UX/terrain-osm-preview-visual.svg (steps 3 Preview,
# 4 Import) — DESIGN_Terrain_From_OSM.md §6 (the editor wizard). Scope: only the wizard window; nothing else of the editor is drawn.
# The map is the SAME canvas in every step: OSM map tiles to pick the place, the fetched data drawn over dimmed tiles to preview it.
# Preview colours are HROT's own 2-D map colours (TerrainPlan2D: HeightColor, ForestFill, WaterFill, WallFill) — one colour language.
import os, sys, random, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX")
W = 1240
STEPS = ["Area", "Options", "Preview", "Import"]
# TerrainPlan2D.HeightColor buckets (< 5 m, < 15 m, < 30 m, taller) at 3 m per storey
H_COL = ["#E1CDA0", "#E1A05A", "#B45F3C", "#782828"]
SOLID = "#8C8C96"; FOREST = "#286E32"; WATER = "#326EC8"; WALL = "#96968F"; SCRUB = "#6E8C3C"
ROAD = {"motorway": ("#E07A5F", 5), "primary": ("#F2C033", 4), "secondary": ("#E8D58A", 3.2), "residential": ("#D8D8DC", 2.2),
        "track": ("#A08868", 1.2), "rail": ("#B0B0B8", 1.4)}

PX, PY, PW, PH = 20, 56, 1200, 600
CX, CY, CW, CH = PX + 10, PY + 64, 830, 476            # the map canvas
RX, RW = CX + CW + 12, PW - CW - 32                    # the right panel


def poly(s, pts, fill, stroke=None, op=1.0, sw=1):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    st = f" stroke='{stroke}' stroke-width='{sw}'" if stroke else ""
    s.raw(f"<polygon points='{p}' fill='{fill}' fill-opacity='{op}'{st}/>")


def pline(s, pts, col, w, dash=None, op=1.0):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    d = f" stroke-dasharray='{dash}'" if dash else ""
    s.raw(f"<polyline points='{p}' fill='none' stroke='{col}' stroke-width='{w}' stroke-linecap='round' stroke-linejoin='round' stroke-opacity='{op}'{d}/>")


def world(seed=3):
    """A deterministic fake place: a village street grid, a main road, a river, woods, terraces."""
    r = random.Random(seed)
    roads = [("primary", [(CX - 5, CY + 300), (CX + 260, CY + 285), (CX + 520, CY + 250), (CX + CW + 5, CY + 210)]),
             ("motorway", [(CX + 600, CY - 5), (CX + 640, CY + 200), (CX + 700, CY + CH + 5)]),
             ("secondary", [(CX + 330, CY - 5), (CX + 350, CY + 270), (CX + 380, CY + CH + 5)]),
             ("track", [(CX + 40, CY + 60), (CX + 200, CY + 120), (CX + 300, CY + 110)]),
             ("rail", [(CX - 5, CY + 440), (CX + 400, CY + 420), (CX + CW + 5, CY + 380)])]
    for i in range(5):
        y = CY + 180 + i * 34
        roads.append(("residential", [(CX + 180, y), (CX + 520, y - 18)]))
    for i in range(4):
        x = CX + 200 + i * 90
        roads.append(("residential", [(x, CY + 170), (x + 10, CY + 340)]))
    blds = []
    for gx in range(4):
        for gy in range(4):
            x0, y0 = CX + 208 + gx * 90, CY + 186 + gy * 34
            if gy == 1 and gx == 1:   # a terrace row sharing walls
                for k in range(5):
                    blds.append(([(x0 + k * 13, y0), (x0 + k * 13 + 13, y0), (x0 + k * 13 + 13, y0 + 11), (x0 + k * 13, y0 + 11)], 2, True))
                continue
            for k in range(3):
                w, h = r.uniform(10, 18), r.uniform(8, 13)
                bx = x0 + k * 25 + r.uniform(0, 4)
                st = r.choice([1, 2, 2, 2, 3, 3, 4]) if (gx, gy) != (2, 2) else r.choice([4, 5, 6])
                blds.append(([(bx, y0), (bx + w, y0), (bx + w, y0 + h), (bx, y0 + h)], st, True))
                if r.random() < 0.45:   # a garage behind it — solid
                    gxp, gyp = bx + 2, y0 + h + 3
                    blds.append(([(gxp, gyp), (gxp + 6, gyp), (gxp + 6, gyp + 5), (gxp, gyp + 5)], 1, False))
    woods = [[(CX + 20, CY + 20), (CX + 170, CY + 10), (CX + 250, CY + 70), (CX + 210, CY + 150), (CX + 60, CY + 160), (CX + 10, CY + 100)],
             [(CX + 660, CY + 280), (CX + 810, CY + 300), (CX + 820, CY + 470), (CX + 700, CY + 490), (CX + 640, CY + 400)]]
    river = [(CX + 520, CY + CH + 5), (CX + 560, CY + 420), (CX + 590, CY + 330), (CX + 570, CY + 200), (CX + 620, CY + 90), (CX + 700, CY - 5)]
    scrub = [[(CX + 460, CY + 30), (CX + 540, CY + 40), (CX + 520, CY + 110), (CX + 450, CY + 90)]]
    walls = [[(CX + 190, CY + 172), (CX + 540, CY + 158)], [(CX + 200, CY + 345), (CX + 540, CY + 330)]]
    return roads, blds, woods, river, scrub, walls


def clip_open(s, cid, x, y, w, h):
    s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{w}' height='{h}'/></clipPath><g clip-path='url(#{cid})'>")


def clip_close(s): s.raw("</g>")


def tiles(s, dim=False):
    """OSM-like map tiles (light raster). Dimmed in the Preview so the fetched data reads on top."""
    roads, blds, woods, river, scrub, walls = world()
    clip_open(s, "canvas" + ("d" if dim else ""), CX, CY, CW, CH)
    s.rect(CX, CY, CW, CH, "#F2EFE9")
    for wd in woods: poly(s, wd, "#ADD19E")
    pline(s, river, "#AAD3DF", 9)
    for cls, pts in roads:
        col = {"motorway": "#E892A2", "primary": "#FCD6A4", "secondary": "#F7FABF", "rail": "#999999"}.get(cls, "#FFFFFF")
        pline(s, pts, "#C8C0B8", ROAD[cls][1] * 1.8 + 1.5)
        pline(s, pts, col, ROAD[cls][1] * 1.8)
    for pts, st, ent in blds: poly(s, pts, "#D9D0C9", "#C4B5AA", sw=0.6)
    for i in range(0, CW, 256): s.line(CX + i, CY, CX + i, CY + CH, "#E2DED6", 0.6)
    for j in range(0, CH, 256): s.line(CX, CY + j, CX + CW, CY + j, "#E2DED6", 0.6)
    if dim: s.rect(CX, CY, CW, CH, "#0F0F0F", op=0.72)
    clip_close(s)
    s.rect(CX + CW - 196, CY + CH - 18, 196, 18, "#FFFFFF", op=0.75 if not dim else 0.15)
    s.text(CX + CW - 190, CY + CH - 5, "© OpenStreetMap contributors", "#333333" if not dim else C.NOTE, 10.5)


def frame(s, title, step):
    s.window(PX, PY, PW, PH, title)
    x = PX + 12
    for i, name in enumerate(STEPS):
        done, cur = i < step, i == step
        w = 26 + text_width(f"{i + 1}  {name}", 12) + 14
        s.rect(x, PY + 30, w, 24, C.BTNSEL if cur else (C.FRAME if done else C.NEUTRAL), rx=12, stroke=C.ACC if cur else C.BORDER)
        if done: s.glyph(FA.CHECK, x + 9, PY + 36, C.OK, 11)
        else: s.text(x + 13, PY + 46, str(i + 1), C.TXT if cur else C.DIS, 12, weight="bold")
        s.text(x + 28, PY + 46, name, C.TXT if (cur or done) else C.DIS, 12)
        x += w
        if i < 3: s.line(x + 4, PY + 42, x + 20, PY + 42, C.SEP, 1.2); x += 24


def footer(s, back, nxt, nxt_glyph=None, nxt_col=None, nxt_dis=False):
    y = PY + PH - 34
    s.line(PX, y - 8, PX + PW, y - 8, C.SEP)
    if back: s.button(PX + PW - 330, y, 90, "Back", 0xf053, C.SUB)
    s.button(PX + PW - 230, y, 110, nxt, nxt_glyph, nxt_col, fill=C.BTNSEL if not nxt_dis else C.BTN, dis=nxt_dis)
    s.button(PX + PW - 110, y, 90, "Cancel")


def zoom_ctrls(s):
    for i, (g, tip) in enumerate([(FA.PLUS, "+"), (0xf068, "-"), (0xf05b, "fit")]):
        s.rect(CX + 10, CY + 10 + i * 28, 24, 24, C.POPUP, stroke=C.BORDER, rx=3, op=0.9)
        s.glyph(g, CX + 16, CY + 15 + i * 28, C.SUB, 12)


# ───────────────────────────── page 1: Area + Options ─────────────────────────────
s = Svg(W, 1250, "Terrain from OpenStreetMap — the wizard: 1 Area, 2 Options (DESIGN_Terrain_From_OSM.md §6)")
frame(s, "New terrain from OpenStreetMap", 0)
tiles(s)
zoom_ctrls(s)
# the search box over the map
s.rect(CX + 46, CY + 10, 330, 24, C.POPUP, stroke=C.ACC, rx=3)
s.glyph(FA.SEARCH, CX + 54, CY + 16, C.SUB, 12); s.text(CX + 74, CY + 27, "Kolín", C.TXT, 12.5)
s.rect(CX + 46, CY + 36, 330, 72, C.POPUP, stroke=C.BORDER, rx=3)
for i, (nm, sub) in enumerate([("Kolín", "town, Central Bohemia, Czechia"), ("Kolín district", "district, Czechia"), ("Nový Kolín", "village")]):
    if i == 0: s.rect(CX + 47, CY + 37, 328, 23, C.NEUTRALSEL)
    s.glyph(FA.LOCATION, CX + 54, CY + 42 + i * 23, C.RED, 11); s.text(CX + 72, CY + 53 + i * 23, nm, C.TXT, 12)
    s.text(CX + 72 + text_width(nm, 12) + 8, CY + 53 + i * 23, sub, C.NOTE, 11)
# the area box
bx0, by0, bx1, by1 = CX + 150, CY + 140, CX + 600, CY + 470
s.rect(bx0, by0, bx1 - bx0, by1 - by0, C.ACC, op=0.10)
s.rect(bx0, by0, bx1 - bx0, by1 - by0, "none", stroke=C.ACC, sw=2)
for hx, hy in [(bx0, by0), (bx1, by0), (bx0, by1), (bx1, by1), ((bx0 + bx1) / 2, by0), ((bx0 + bx1) / 2, by1), (bx0, (by0 + by1) / 2), (bx1, (by0 + by1) / 2)]:
    s.rect(hx - 4, hy - 4, 8, 8, "#FFFFFF", stroke=C.ACC, sw=1.5)
s.rect((bx0 + bx1) / 2 - 62, by0 - 26, 124, 20, C.POPUP, stroke=C.ACC, rx=10)
s.text((bx0 + bx1) / 2, by0 - 12, "4.8 × 3.5 km", C.TXT, 12, anchor="middle", weight="bold")
# right panel
y = CY
s.separator_text(RX, y + 10, "The area", RX + RW)
s.text(RX, y + 40, "Drag a box on the map, or search a place.", C.SUB, 12)
s.form_row(RX, y + 58, "Width", 80, "field", RW - 90, "4.8 km", dis=True)
s.form_row(RX, y + 86, "Height", 80, "field", RW - 90, "3.5 km", dis=True)
s.form_row(RX, y + 114, "Centre", 80, "field", RW - 90, "50.0281 N · 15.2049 E", dis=True)
s.form_row(RX, y + 142, "UTM zone", 80, "field", RW - 90, "33 N", dis=True)
s.rect(RX, y + 178, RW, 26, C.NEUTRAL, stroke=C.BORDER, rx=3)
s.glyph(FA.INFO, RX + 8, y + 185, C.INFO, 12); s.text(RX + 28, y + 196, "Largest box: 6 × 6 km", C.SUB, 12)
s.separator_text(RX, y + 232, "Estimate (from the map, before download)", RX + RW)
s.note(RX, y + 262, ["The content check runs in Preview —", "a town packs ≈ 1 000 buildings per km²;", "HROT is tuned for ≈ 1 800 in all."], C.NOTE, 11.5)
footer(s, False, "Next", 0xf054, C.TXT)
s.note(PX, PY + PH + 18, ["① Area — the map is OSM's standard map tiles (not satellite), with the attribution; pan with the mouse, wheel to zoom, Fit = the box.",
                          "   Search = Nominatim; picking a result centres the map and proposes a box of the place's size (clamped to 6 × 6 km).",
                          "   The box: drag on empty map to draw, drag inside to move, the 8 handles to resize; the size chip turns RED above 6 km and Next greys."],
       C.NOTE, 11.5)

# Options, below
OY = PY + PH + 90
PY2 = OY
s.window(PX, PY2, PW, 330, "New terrain from OpenStreetMap")
x = PX + 12
for i, name in enumerate(STEPS):
    done, cur = i < 1, i == 1
    w = 26 + text_width(f"{i + 1}  {name}", 12) + 14
    s.rect(x, PY2 + 30, w, 24, C.BTNSEL if cur else (C.FRAME if done else C.NEUTRAL), rx=12, stroke=C.ACC if cur else C.BORDER)
    if done: s.glyph(FA.CHECK, x + 9, PY2 + 36, C.OK, 11)
    else: s.text(x + 13, PY2 + 46, str(i + 1), C.TXT if cur else C.DIS, 12, weight="bold")
    s.text(x + 28, PY2 + 46, name, C.TXT if (cur or done) else C.DIS, 12)
    x += w
    if i < 3: s.line(x + 4, PY2 + 42, x + 20, PY2 + 42, C.SEP, 1.2); x += 24
LX, LY = PX + 20, PY2 + 74
s.separator_text(LX, LY, "Terrain", LX + 560)
s.form_row(LX, LY + 20, "Name", 150, "field", 390, "kolin-centre")
s.glyph(FA.WARNING, LX + 552, LY + 25, C.PEND, 13)   # as-built: amber while the name exists (green tick = a new name)
s.text(LX + 150, LY + 64, "Saved as assets/terrain/kolin-centre/", C.NOTE, 11)
s.separator_text(LX, LY + 92, "Include", LX + 560)
for i, (lab, g, gc, on, sub) in enumerate([("Buildings", FA.HOUSE, C.ORANGE, True, "enterable houses; garages and sheds solid"),
                                            ("Trees and woods", 0xf1bb, C.GREEN, True, "woods, scrub, lone trees, tree rows"),
                                            ("Walls and fences", 0xf84c, C.GREY, True, "walls, fences, hedges, retaining walls"),
                                            ("Water", 0xf773, C.BLUE, True, "lakes, ponds, rivers")]):
    yy = LY + 112 + i * 28
    s.checkbox(LX + 4, yy, on); s.glyph(g, LX + 30, yy + 2, gc, 13); s.text(LX + 52, yy + 14, lab, C.TXT, 12.5)
    s.text(LX + 200, yy + 14, sub, C.NOTE, 11.5)
s.separator_text(LX + 600, LY, "Buildings without a height in OSM", PX + PW - 20)
s.form_row(LX + 600, LY + 20, "Storey height", 150, "field", 120, "3.0 m")
s.form_row(LX + 600, LY + 48, "Default storeys", 150, "field", 120, "2")
s.note(LX + 600, LY + 92, ["Used only where OSM has no levels, no height", "and no known building type (≈ 1 in 3 buildings).",
                           "Roads always come in (they cannot be switched off)."], C.NOTE, 11.5)
s.rect(LX + 600, LY + 150, 540, 46, C.PENDROW, stroke=C.PEND, rx=3)
s.glyph(FA.WARNING, LX + 610, LY + 158, C.PEND, 13)
s.text(LX + 632, LY + 168, "A terrain named \"kolin-centre\" exists — tick Replace to overwrite it.", C.TXT, 12)
s.checkbox(LX + 632, LY + 174, False); s.text(LX + 656, LY + 188, "Replace — used by 2 scenarios: kolin-patrol, kolin-cordon", C.SUB, 11.5)
PY_save = PY
PY = PY2; PH_save = PH; PH = 330
footer(s, True, "Preview", FA.EYE, C.TEAL)
PY = PY_save; PH = PH_save
s.note(PX, PY2 + 348, ["② Options — the name is checked as you type (a valid folder name; green tick = new, amber row = exists → Replace with the scenarios that use it).",
                       "   Everything else follows the design's rules; there is no per-building editing (one-shot, Q-O5) — to change anything, import again."], C.NOTE, 11.5)
s.save(os.path.join(OUT, "terrain-osm-wizard-visual.svg"))
if len(sys.argv) > 1: render(os.path.join(OUT, "terrain-osm-wizard-visual.svg"), sys.argv[1] + "-1.png", W, 1250)

# ───────────────────────────── page 2: Preview + Import ─────────────────────────────
s = Svg(W, 1320, "Terrain from OpenStreetMap — the wizard: 3 Preview, 4 Import (DESIGN_Terrain_From_OSM.md §6)")
frame(s, "New terrain from OpenStreetMap", 2)
tiles(s, dim=True)
roads, blds, woods, river, scrub, walls = world()
BX0, BY0, BW_, BH_ = CX + 150, CY + 140, 450, 300
s.rect(BX0, BY0, BW_, BH_, "#0F0F0F", op=0.35)
clip_open(s, "box", BX0, BY0, BW_, BH_)
for wd in woods: poly(s, wd, FOREST, op=0.55)
for sc in scrub: poly(s, sc, SCRUB, op=0.45)
pline(s, river, WATER, 9, op=0.9)
for cls, pts in roads: pline(s, pts, ROAD[cls][0], ROAD[cls][1], dash="6 4" if cls == "rail" else None)
for wl in walls: pline(s, wl, WALL, 1.6)
for pts, st, ent in blds:
    h = st * 3
    if ent: poly(s, pts, H_COL[0 if h < 5 else 1 if h < 15 else 2 if h < 30 else 3], "#141414", sw=0.8)
    else: poly(s, pts, SOLID, "#141414", op=0.9, sw=0.6)
# as-built: no lone-tree dots — the contract has no lone-tree positions (only tree_row lines)
clip_close(s)
s.rect(BX0, BY0, BW_, BH_, "none", stroke=C.ACC, sw=1.5, dash="5 4")
zoom_ctrls(s)
# hover tooltip on a building
s.rect(CX + 300, CY + 223, 14, 11, "none", stroke="#FFFFFF", sw=2)
s.tooltip(CX + 318, CY + 236, 170, ["way 112233445 · enterable", "2 storeys · about 6 m"])   # as-built: what the contract's PreviewBuilding carries
# legend (layer toggles) top-right of the canvas
LGX, LGY = CX + CW - 214, CY + 10
s.rect(LGX, LGY, 204, 238, C.POPUP, stroke=C.BORDER, rx=4, op=0.94)
s.text(LGX + 10, LGY + 18, "Layers", C.TXT, 12, weight="bold")
rows = [(True, "rect", H_COL[0], "Houses · 1 storey"), (True, "rect", H_COL[1], "Houses · 2–4"), (True, "rect", H_COL[2], "Houses · 5–9"),
        (True, "rect", SOLID, "Garages, sheds (solid)"), (True, "rect", FOREST, "Woods"), (True, "rect", SCRUB, "Scrub"),
        (True, "rect", WATER, "Water"), (True, "line", WALL, "Walls, fences"), (True, "dot", FOREST, "Lone trees, tree rows"),
        (True, "line", ROAD["primary"][0], "Roads and rail")]
for i, (on, kind, col, lab) in enumerate(rows):
    yy = LGY + 28 + i * 20.5
    s.checkbox(LGX + 8, yy - 1, on)
    if kind == "rect": s.rect(LGX + 32, yy + 2, 16, 11, col, stroke="#141414")
    elif kind == "line": s.line(LGX + 30, yy + 8, LGX + 50, yy + 8, col, 3)
    else: s.raw(f"<circle cx='{LGX + 40}' cy='{yy + 8}' r='3.5' fill='{col}' stroke='#7FD08A'/>")
    s.text(LGX + 58, yy + 12, lab, C.SUB, 11.5)
# right panel: counts, budget, warnings
y = CY
s.separator_text(RX, y + 10, "What will be built", RX + RW)
counts = [("Enterable buildings", "1 412", FA.HOUSE, C.ORANGE), ("Solid (garages, sheds)", "538", 0xf0c8, C.GREY), ("Doors · windows", "2 980 · 31 400", 0xf52b, C.AMBER),
          ("Trees (woods + lone)", "≈ 96 000", 0xf1bb, C.GREEN), ("Roads", "84 km", FA.ROUTE, C.BLUE), ("Walls · water areas", "212 · 6", 0xf84c, C.GREY)]
for i, (lab, val, g, gc) in enumerate(counts):
    yy = y + 26 + i * 22
    s.glyph(g, RX, yy + 2, gc, 12); s.text(RX + 22, yy + 13, lab, C.SUB, 12); s.text(RX + RW, yy + 13, val, C.TXT, 12, anchor="end", weight="bold")
by = y + 168
s.separator_text(RX, by, "Content budget (vs tuned load)", RX + RW)
for i, (lab, frac) in enumerate([("Buildings", 0.78), ("Openings", 0.49), ("Trees", 0.55)]):
    yy = by + 20 + i * 30
    s.text(RX, yy + 12, lab, C.SUB, 11.5)
    bx, bw = RX + 74, RW - 74
    s.rect(bx, yy, bw, 14, C.NEUTRAL, stroke=C.BORDER, rx=2)
    s.rect(bx, yy, bw * 2 / 3, 14, C.OK, op=0.18); s.rect(bx + bw * 2 / 3, yy, bw / 6, 14, C.PEND, op=0.22); s.rect(bx + bw * 5 / 6, yy, bw / 6, 14, C.ERR, op=0.22)
    s.rect(bx, yy + 3, bw * frac * 2 / 3 / 1.0 if frac <= 1 else bw * 2 / 3, 8, C.OK, rx=1)
    for f, t in [(1 / 3, "1×"), (2 / 3, "2×"), (5 / 6, "")]:
        s.line(bx + bw * f, yy - 2, bx + bw * f, yy + 16, C.SEP, 1)
    if i == 2:
        for f, t in [(1 / 3, "1×"), (2 / 3, "2×"), (5 / 6, "3×")]: s.text(bx + bw * f, yy + 27, t, C.NOTE, 9.5, anchor="middle")
s.rect(RX, by + 122, RW, 28, "#173A1E", stroke=C.OK, rx=3)
s.glyph(FA.CIRCLE_CHECK, RX + 8, by + 129, C.OK, 13); s.text(RX + 30, by + 140, "Within budget — ready to import", C.TXT, 12)
wy = by + 172
s.separator_text(RX, wy, "Notes (4)", RX + RW)   # as-built: the contract's warnings carry no location, so a note cannot zoom the map
for i, (g, gc, t) in enumerate([(FA.INFO, C.INFO, "512 buildings: storeys from their type"), (FA.INFO, C.INFO, "3 courtyards split into parts"),
                                 (FA.WARNING, C.PEND, "9 shapes dropped (< 12 m² or too thin)"), (FA.INFO, C.INFO, "2 rivers widened from lines (10 m)")]):
    yy = wy + 18 + i * 22
    s.glyph(g, RX + 2, yy + 2, gc, 12); s.text(RX + 22, yy + 13, t, C.SUB, 11.5)
s.text(RX, CY + CH - 4, "Data: © OpenStreetMap contributors · Copernicus DEM GLO-30", C.NOTE, 10.5)   # as-built: no data timestamp in the contract
footer(s, True, "Import", FA.CHECK, C.GREEN)
s.note(PX, PY + PH + 18, ["③ Preview — the fetched data drawn over the DIMMED tiles in HROT's own 2-D map colours (buildings by height = TerrainPlan2D.HeightColor; woods,",
                          "   water, walls as on the scenario map). Each layer toggles; hover a building = its id, enterable / solid, storeys (the contract's PreviewBuilding; type, roof, openings later).",
                          "   The budget bars: green to 2×, amber 2–3× (\"heavier than HROT is tuned for\"), red above 3× = Import greyed and \"pick a smaller box\". No heights yet (see §14.3)."],
       C.NOTE, 11.5)

# Import, below
PY2 = PY + PH + 100
s.window(PX, PY2, PW, 470, "New terrain from OpenStreetMap")
x = PX + 12
for i, name in enumerate(STEPS):
    done, cur = i < 3, i == 3
    w = 26 + text_width(f"{i + 1}  {name}", 12) + 14
    s.rect(x, PY2 + 30, w, 24, C.BTNSEL if cur else (C.FRAME if done else C.NEUTRAL), rx=12, stroke=C.ACC if cur else C.BORDER)
    if done: s.glyph(FA.CHECK, x + 9, PY2 + 36, C.OK, 11)
    else: s.text(x + 13, PY2 + 46, str(i + 1), C.TXT, 12, weight="bold")
    s.text(x + 28, PY2 + 46, name, C.TXT, 12)
    x += w
    if i < 3: s.line(x + 4, PY2 + 42, x + 20, PY2 + 42, C.SEP, 1.2); x += 24
LX, LY = PX + 20, PY2 + 76
s.separator_text(LX, LY, "Building the terrain \"kolin-centre\"", LX + 700)
stages = [("Map data (OSM)", 1.0, "cached · 9.8 MB", "ok"), ("Read the map data", 1.0, "41 200 ways", "ok"),
          ("Heights at 1 m", 1.0, "4 800 × 3 500 posts", "ok"), ("Roads (netconvert)", 1.0, "84 km · 612 junctions", "ok"),
          ("Buildings", 0.62, "880 of 1 412", "run"), ("Trees, walls, water", 0.0, "", "wait"), ("Write the input set", 0.0, "", "wait"),
          ("Import into HROT", 0.0, "terrain, navmesh", "wait")]
for i, (lab, f, sub, st) in enumerate(stages):
    yy = LY + 20 + i * 30
    g, gc = {"ok": (FA.CIRCLE_CHECK, C.OK), "run": (FA.HOURGLASS, C.AMBER), "wait": (FA.CIRCLE, C.DIS)}[st]
    s.glyph(g, LX + 2, yy + 3, gc, 13)
    s.text(LX + 26, yy + 15, lab, C.TXT if st != "wait" else C.DIS, 12.5)
    s.rect(LX + 250, yy + 4, 300, 12, C.NEUTRAL, stroke=C.BORDER, rx=2)
    if f > 0: s.rect(LX + 250, yy + 4, 300 * f, 12, C.OK if st == "ok" else C.ACC, rx=2)
    s.text(LX + 562, yy + 15, sub, C.NOTE, 11.5)
s.text(LX, LY + 270, "Elapsed 1:42 · about 2 minutes left", C.SUB, 12)
# the done state, as an inset
DX, DY = PX + 780, PY2 + 76
s.rect(DX, DY, 400, 236, C.POPUP, stroke=C.BORDER, rx=4)
s.text(DX + 12, DY + 20, "When it finishes", C.NOTE, 11.5)
s.glyph(FA.CIRCLE_CHECK, DX + 14, DY + 36, C.OK, 18); s.text(DX + 42, DY + 52, "Terrain \"kolin-centre\" is ready", C.TXT, 14, weight="bold")
s.note(DX + 42, DY + 72, ["1 412 buildings · 84 km of roads · 4.8 × 3.5 km", "© OpenStreetMap contributors · Copernicus DEM GLO-30"], C.SUB, 11.5)
s.button(DX + 42, DY + 112, 190, "Open in New Scenario", FA.FILE, C.BLUE, fill=C.BTNSEL)
s.button(DX + 240, DY + 112, 110, "Show folder", FA.FOLDER_OPEN, C.AMBER)
s.line(DX + 12, DY + 154, DX + 388, DY + 154, C.SEP)
s.text(DX + 12, DY + 174, "Until the terrain importer is built:", C.NOTE, 11.5)
s.glyph(FA.INFO, DX + 14, DY + 184, C.INFO, 12)
s.note(DX + 34, DY + 196, ["Input set written to content/terrain-input/kolin-centre/", "— the terrain importer is not built yet."], C.SUB, 11.5)
y = PY2 + 470 - 34
s.line(PX, y - 8, PX + PW, y - 8, C.SEP)
s.button(PX + PW - 290, y, 170, "Run in background", 0xf2d2, C.SUB)
s.button(PX + PW - 110, y, 90, "Cancel", FA.XMARK, C.RED)
s.note(PX, PY2 + 488, ["④ Import — one row per stage with its own bar and what it produced; Cancel stops at the next safe point and writes nothing (temp folder discarded).",
                       "   \"Run in background\" closes the window and keeps a progress toast; a failure names the service or the stage and offers Retry (cached downloads are kept)."],
       C.NOTE, 11.5)
s.save(os.path.join(OUT, "terrain-osm-preview-visual.svg"))
if len(sys.argv) > 1: render(os.path.join(OUT, "terrain-osm-preview-visual.svg"), sys.argv[1] + "-2.png", W, 1320)
