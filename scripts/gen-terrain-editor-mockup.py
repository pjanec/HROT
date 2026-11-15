# Generates docs/UX/terrain-editor.svg — the user-friendly terrain editing of DESIGN_Terrain_Editor_UX.md (CE-4594), house style (R-4143):
#   A the Terrain row on the editor's Tools toolbar (TE-1) and a sticky tool (TE-2) · B planting trees: one · a row · a group (TE-3, TE-4)
#   · C a wall along a road: snapping (TE-6), a closed plot and a gate (TE-7) · D an obstacle row and a street-clutter scatter (TE-4, TE-5)
#   · E the Terrain Details tab (TE-8) · F what was placed stays visible and pickable (TE-9) with the ghost while drawing (TE-10).
# Regenerate: python3 scripts/gen-terrain-editor-mockup.py [docs/UX/terrain-editor.svg]
# SCOPE (R-4151): the toolbar's Terrain row and its flyouts, the draw panel's new rows, the snap marks, the gate split, the Terrain Details tab, the feature
# handles. CONTEXT, drawn as measured and UNCHANGED: the toolbar's Select · Place Entity · Edit Shape · Edit Route buttons (EditorToolbarPanel.cs:57-64) ·
# the 2-D plan's colours (gen-ground-shaping-mockup.py, TerrainPlan2D) · the drawing look — white squares, route green, the next segment dashed (GizmoEditStyle) ·
# the draw panel bottom-left (TerrainDrawPanel.cs) · the ghost's two-line hint, green / amber, no glyphs (TerrainGhostDraw) · label-first Details with
# SeparatorText sections (GroundFeatureDetailsView.cs, TerrainBlockDetailsView.cs).
import os, sys, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 1560
GROUND = ["#33452C", "#3B5131", "#4A5C36", "#5E643C"]
ROAD = "#E8D58A"; ROAD_EDGE = "#8A7A40"; HOUSE = "#E1CDA0"; FOREST = "#286E32"; ROUTE = "#5BD06A"; TREE = "#3F8F45"
WALL = "#C9B79A"; HEDGE = "#4E8A3A"; SAND = "#D8B860"; CONC = "#B8B8C0"; SNAP = "#4296FA"
G = dict(TREE=0xf1bb, WALL=0xf7a4, ROAD=0xf018, CAR=0xf1b9, BARRIER=0xe562, MOUND=0xe52d, DIG=0xf85e, SEED=0xf4d8, BRUSH=0xf55d,
         MAGNET=0xf076, DOOR=0xf52b, RULER=0xf545, CUBE=0xf1b2, TRUCK=0xf0d1, CAUTION=0xf071, BOX=0xf466, DICE=0xf522, BAN=0xf05e)


def poly(s, pts, fill, stroke=None, op=1.0, sw=1, dash=None):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    st = f" stroke='{stroke}' stroke-width='{sw}'" if stroke else ""
    d = f" stroke-dasharray='{dash}'" if dash else ""
    s.raw(f"<polygon points='{p}' fill='{fill}' fill-opacity='{op}'{st}{d}/>")


def pline(s, pts, col, w, dash=None, op=1.0, cap="round"):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    d = f" stroke-dasharray='{dash}'" if dash else ""
    s.raw(f"<polyline points='{p}' fill='none' stroke='{col}' stroke-width='{w}' stroke-linecap='{cap}' stroke-linejoin='round' stroke-opacity='{op}'{d}/>")


def circle(s, x, y, r, fill, op=1.0, stroke=None, sw=1):
    st = f" stroke='{stroke}' stroke-width='{sw}'" if stroke else ""
    s.raw(f"<circle cx='{x:.1f}' cy='{y:.1f}' r='{r}' fill='{fill}' fill-opacity='{op}'{st}/>")


def clip(s, cid, x, y, w, h): s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{w}' height='{h}'/></clipPath><g clip-path='url(#{cid})'>")
def unclip(s): s.raw("</g>")


def ground(s, x, y, w, h, seed):
    r = random.Random(seed); s.rect(x, y, w, h, GROUND[0])
    for _ in range(4):
        cx, cy = x + r.uniform(0.1, 0.9) * w, y + r.uniform(0.1, 0.9) * h
        rx, ry = r.uniform(60, 120), r.uniform(40, 80)
        pts = [(cx + rx * math.cos(2 * math.pi * i / 14) * (1 + r.uniform(-.15, .15)), cy + ry * math.sin(2 * math.pi * i / 14) * (1 + r.uniform(-.15, .15))) for i in range(14)]
        poly(s, pts, GROUND[1], "#2A3824", op=0.8, sw=0.6)


def road(s, pts, w=16):
    pline(s, pts, ROAD_EDGE, w + 3); pline(s, pts, ROAD, w)


def house(s, x, y, w, h): s.rect(x, y, w, h, HOUSE, stroke="#6E5A3A", sw=1)


def hint(s, x, y, lines):
    w = max(text_width(t, 11.5) for t, _ in lines) + 16
    s.rect(x, y, w, 8 + 16 * len(lines), "#0F0F12", op=0.88, rx=3)
    for i, (t, c) in enumerate(lines): s.text(x + 8, y + 17 + i * 16, t, c, 11.5)


def drawpts(s, pts, col=ROUTE, closed=False, next_dash=None):
    pline(s, pts + ([pts[0]] if closed else []), col, 1.6)
    if next_dash: pline(s, [pts[-1], next_dash], col, 1.6, dash="6 4", op=0.8)
    for i, p in enumerate(pts): s.rect(p[0] - 4, p[1] - 4, 8, 8, "#FFFFFF", stroke=col if i else C.ACC, sw=1.4)


def seg(s, x, y, opts, sel, sz=12):
    for i, o in enumerate(opts):
        bw = text_width(o, sz) + 20
        s.rect(x, y, bw, 21, C.BTNSEL if i == sel else C.BTN, rx=3, stroke=C.ACC if i == sel else None)
        s.text(x + bw / 2, y + 15, o, C.TXT, sz, anchor="middle"); x += bw + 3
    return x


def tool_btn(s, x, y, glyph, gc, label, armed=False, flyout=False):
    w = 30 + text_width(label, 12) + (12 if flyout else 0)
    s.rect(x, y, w, 24, "#5A3C14" if armed else C.BTN, rx=3, stroke="#F29B38" if armed else None, sw=1.4)
    s.glyph(glyph, x + 7, y + 5, gc, 13); s.text(x + 26, y + 16, label, C.TXT, 12)
    if flyout: s.raw(f"<path d='M{x + w - 11} {y + 10} l3.5 4 l3.5 -4 z' fill='{C.SUB}'/>")
    return x + w + 4


def panel(s, x, y, w, h, title): s.rect(x, y, w, h, "#16161A", stroke=C.BORDER, rx=4); s.text(x + 10, y + 18, title, "#9FB7D8", 12.5)


def label(s, x, y, t, c="#9FB7D8", sz=13): s.text(x, y, t, c, sz, weight="bold")


s = Svg(W, H, "Terrain editor — the user-friendly way (CE-4594)",
        "DESIGN_Terrain_Editor_UX.md · A toolbar + sticky tools · B trees · C walls, snapping, gates · D obstacles · E Details · F handles. Annotations in grey are not drawn by the app.")

# ── A — the Terrain row on the Tools toolbar ────────────────────────────────────────────────────────────────────────────────────
label(s, 20, 62, "A  One door for every terrain tool — the Tools toolbar's Terrain row (TE-1), and a tool that stays armed (TE-2)")
s.window(20, 72, 1200, 96, "Tools")
x = 30; y = 100
for g, c, t in [(FA.CROSSHAIRS, C.GREY, "Select"), (FA.PLUS, C.GREEN, "Place Entity"), (FA.DRAW, C.BLUE, "Edit Shape"), (FA.ROUTE, C.TEAL, "Edit Route")]:
    x = tool_btn(s, x, y, g, c, t)
s.text(30, 145, "context — unchanged (EditorToolbarPanel.cs:57-64)", C.NOTE, 11)
s.line(x + 6, y - 2, x + 6, y + 26, C.SEP)
x += 14; s.text(x, y + 16, "Terrain", C.SUB, 12); x += 56
for g, c, t, arm, fly in [(G["TREE"], TREE, "Wood", False, True), (G["SEED"], C.GREEN, "Tree", False, True), (G["WALL"], WALL, "Hedge", True, True),
                          (G["BARRIER"], C.AMBER, "Obstacle", False, True), (G["DIG"], "#C8965A", "Earthworks", False, True), (G["CAR"], C.GREY, "Scatter", False, True)]:
    x = tool_btn(s, x, y, g, c, t, arm, fly)
s.note(20, 186, ["each button shows its LAST variant (Hedge = the wall family); its arrow opens the family —",
                 "the SAME picks Add Entity lists (TerrainPickCatalog), never a second list"], C.NOTE, 11)
# the open flyout of the wall family
fx = 676; fy = 128
s.menu(fx, fy, 210, [("#", "Walls & fences"), ("Wall", "W", G["WALL"], WALL), ("Fence", None, G["WALL"], "#A07850"), ("Hedge", None, G["WALL"], HEDGE),
                     "---", ("Wall around a plot", None, FA.POLYGON, WALL), ("Gate in a wall", None, G["DOOR"], C.SUB)])
# the sticky status
s.rect(900, 176, 320, 52, "#0F0F12", op=0.92, rx=3, stroke=C.BORDER)
s.text(910, 194, "Hedge — 3 drawn · click the first point of the next", C.TXT, 12)
s.text(910, 212, "Esc or Select stops the tool · the toolbar keeps it lit", C.SUB, 11.5)
s.note(900, 244, ["TE-2: a line / area terrain tool re-arms after Finish,", "as point placement already does (always-multi, D5)"], C.NOTE, 11)

# ── B — planting trees ──────────────────────────────────────────────────────────────────────────────────────────────────────────
label(s, 20, 300, "B  Planting trees — one, a row, a group (TE-3); a row and a group are ONE feature each (TE-4)")
bx, by, bw, bh = 20, 312, 760, 330
clip(s, "cb", bx, by, bw, bh); ground(s, bx, by, bw, bh, 11)
road(s, [(bx - 10, by + 210), (bx + 300, by + 200), (bx + 780, by + 230)])
for hx, hy in [(120, 120), (190, 110), (260, 125), (560, 120)]: house(s, bx + hx, by + hy, 40, 30)
# ① a single tree (placed earlier) with its handle
circle(s, bx + 70, by + 60, 9, TREE, 0.95, "#1E4A22"); circle(s, bx + 70, by + 60, 3, "#FFFFFF", 0.9)
s.text(bx + 84, by + 64, "① one tree — click", C.SUB, 11)
# ② a row being drawn along the road: ghost trees every 8 m
row = [(bx + 330, by + 178), (bx + 520, by + 186), (bx + 700, by + 205)]
drawpts(s, row[:2], next_dash=row[2])
for i in range(16):
    t = i / 15.0
    if t <= 0.5: px, py = row[0][0] + (row[1][0] - row[0][0]) * t * 2, row[0][1] + (row[1][1] - row[0][1]) * t * 2
    else: px, py = row[1][0] + (row[2][0] - row[1][0]) * (t - .5) * 2, row[1][1] + (row[2][1] - row[1][1]) * (t - .5) * 2
    circle(s, px, py - 12, 7, TREE, 0.55, "#7FD08A", 1)
hint(s, bx + 470, by + 230, [("Row of 16 trees · 8 m apart · 124 m", C.GREEN), ("kept off the road edge (2 m) · Enter ends", C.SUB)])
s.text(bx + 330, by + 160, "② a row — drag along a line", C.SUB, 11)
# ③ a group in an area
grp = [(bx + 70, by + 250), (bx + 200, by + 250), (bx + 230, by + 315), (bx + 60, by + 320)]
poly(s, grp, "#5BD06A", "#5BD06A", op=0.08, sw=1.2, dash="5 4")
r = random.Random(4)
for _ in range(11): circle(s, bx + r.uniform(80, 210), by + r.uniform(262, 310), 6.5, TREE, 0.9, "#1E4A22")
s.text(bx + 70, by + 244, "③ a group — draw an area: 11 lone trees", C.SUB, 11)
unclip(s)
# the draw panel (bottom-left of the work area, as built) with the Tree rows
panel(s, 800, 312, 420, 214, "Tree — drag a line to plant a row")
s.text(812, 352, "Plant", C.TXT)
seg(s, 902, 338, ["One", "Row", "Group"], 1)
s.text(812, 372, "Species", C.TXT); s.combo(902, 358, 180, "Mixed broadleaf", glyph=G["TREE"], gc=TREE)
s.text(812, 398, "Spacing", C.TXT); s.field(902, 384, 80, "8 m"); s.text(990, 398, "= one tree every 8 m along the line", C.NOTE, 11)
s.text(812, 424, "Size", C.TXT); seg(s, 902, 411, ["Young", "Grown", "Old"], 1)
s.text(812, 450, "Seed", C.TXT); s.field(902, 436, 80, "7712"); s.glyph(G["DICE"], 990, 439, C.SUB, 13)
s.separator_text(812, 462, "What it makes", 1210)
s.text(812, 492, "16 lone trees · they fell, burn and block like any lone tree (B-6)", C.SUB, 11.5)
s.text(812, 510, "Group: count 11 · Row and Group are ONE entity each (one undo, one Details)", C.NOTE, 11)
s.note(800, 548, ["⚠ not a WOOD: a wood is a canopy mass (density per ha, sight blocked inside);",
                  "a row / a group is single trunks — what a user means by \"plant trees here\"",
                  "TE-4: Row and Group are the scatter feature with a LINE or an AREA shape and",
                  "ONE item type (here: tree) — the same kind serves obstacle rows (D)"], C.NOTE, 11)

# ── C — a wall along a road: snapping, a closed plot, a gate ────────────────────────────────────────────────────────────────────
label(s, 20, 670, "C  A wall along a road, a wall round a plot, a gate — snapping (TE-6) and the gate split (TE-7)")
cx0, cy0, cw, ch = 20, 682, 760, 330
clip(s, "cc", cx0, cy0, cw, ch); ground(s, cx0, cy0, cw, ch, 21)
road(s, [(cx0 - 10, cy0 + 90), (cx0 + 780, cy0 + 90)], 18)
house(s, cx0 + 120, cy0 + 170, 70, 46); house(s, cx0 + 470, cy0 + 175, 60, 50)
# the wall along the road, snapped to the road edge + 1 m
pline(s, [(cx0 + 40, cy0 + 107), (cx0 + 330, cy0 + 107)], WALL, 4)
drawpts(s, [(cx0 + 40, cy0 + 107), (cx0 + 330, cy0 + 107)], next_dash=(cx0 + 360, cy0 + 107))
pline(s, [(cx0 + 40, cy0 + 99.5), (cx0 + 360, cy0 + 99.5)], SNAP, 1.5, dash="3 3")
circle(s, cx0 + 360, cy0 + 107, 6, "none", 1, SNAP, 2)
s.chip(cx0 + 300, cy0 + 116, "road edge +1 m", G["MAGNET"], SNAP, arrow=False)
hint(s, cx0 + 40, cy0 + 130, [("Wall 320 m · brick 2 m · snapped to the road edge (+1 m)", C.GREEN), ("Alt: no snap · Shift: 15° steps · Enter ends", C.SUB)])
# a closed plot round the house on the right with a gate
plot = [(cx0 + 430, cy0 + 145), (cx0 + 580, cy0 + 145), (cx0 + 580, cy0 + 260), (cx0 + 430, cy0 + 260)]
pline(s, [plot[0], (cx0 + 490, cy0 + 145)], WALL, 4); pline(s, [(cx0 + 512, cy0 + 145), plot[1], plot[2], plot[3], plot[0]], WALL, 4)
s.rect(cx0 + 490, cy0 + 138, 22, 14, "none", stroke="#F2C033", sw=1.5, dash="3 2")
s.glyph(G["DOOR"], cx0 + 494, cy0 + 118, C.AMBER, 12)
s.menu(cx0 + 600, cy0 + 120, 150, [("Add gate here (3 m)", None, G["DOOR"], C.AMBER), ("Insert point", None, FA.PLUS, C.GREEN), "---", ("Finish", "Enter", FA.CHECK, C.GREEN)])
s.text(cx0 + 430, cy0 + 280, "a wall round a plot: Wall around a plot = a CLOSED line;", C.SUB, 11)
s.text(cx0 + 430, cy0 + 294, "a gate = the segment's menu in Edit Shape — it SPLITS the wall", C.SUB, 11)
s.text(cx0 + 430, cy0 + 308, "into two features with a 3 m gap (one undo step; no new data)", C.SUB, 11)
# a hedge snapping onto the end of an existing hedge
pline(s, [(cx0 + 40, cy0 + 250), (cx0 + 190, cy0 + 300)], HEDGE, 5)
drawpts(s, [(cx0 + 300, cy0 + 300)], next_dash=(cx0 + 194, cy0 + 300))
circle(s, cx0 + 190, cy0 + 300, 7, "none", 1, SNAP, 2)
s.chip(cx0 + 205, cy0 + 268, "joins Hedge 2", G["MAGNET"], SNAP, arrow=False)
unclip(s)
panel(s, 800, 682, 420, 196, "Snapping while drawing a terrain line or area")
rows = [("Road edges", True, "a line runs along the edge at an offset"), ("Building walls", True, "a wall meets a house without a gap"),
        ("Line ends", True, "a new hedge continues an old one"), ("15° steps", False, "held with Shift"), ("Grid", False, "Map Interaction's grid toggle")]
for i, (t, on, why) in enumerate(rows):
    yy = 708 + i * 26; s.checkbox(812, yy, on); s.text(838, yy + 14, t, C.TXT); s.text(960, yy + 14, why, C.NOTE, 11)
s.text(812, 850, "Offset from a road edge", C.TXT); s.field(990, 836, 70, "1.0 m")
s.note(800, 900, ["TE-6 extends Map Interaction's snap row (§2.5, R-4134: ground · grid · 15°)",
                  "with three TERRAIN targets; road edges and line ends are ON for terrain",
                  "lines (the jobs are \"along this road\", \"continue that hedge\") — a deviation",
                  "from the units' off-default, said; Alt suspends every snap for one point"], C.NOTE, 11)

# ── D — obstacles: a row of sandbags, concrete blocks, a clutter scatter ────────────────────────────────────────────────────────
label(s, 20, 1040, "D  Obstacles — one, or a row along a line (TE-5); street clutter in an area (the scatter's first recipes ship)")
dx, dy, dw, dh = 20, 1052, 760, 250
clip(s, "cd", dx, dy, dw, dh); ground(s, dx, dy, dw, dh, 31)
road(s, [(dx - 10, dy + 120), (dx + 780, dy + 120)], 22)
# a sandbag line across the field, contiguous
for i in range(12):
    s.rect(dx + 60 + i * 15, dy + 40 + i * 3, 14, 8, SAND, stroke="#7A6630", sw=0.8, rx=2)
s.text(dx + 60, dy + 30, "Sandbags · a row, end to end (spacing 0)", C.SUB, 11)
# concrete blocks across the road, spaced, being drawn
blk = [(dx + 380, dy + 95), (dx + 380, dy + 150)]
for i in range(4): s.rect(dx + 372, dy + 96 + i * 15, 16, 9, CONC, stroke="#606068", sw=0.8)
drawpts(s, blk)
hint(s, dx + 120, dy + 175, [("4 concrete blocks · 1.5 m gaps · the road is CLOSED to vehicles", C.AMBER), ("infantry passes · SUMO re-routes (P-2) · Enter ends", C.SUB)])
# a street-clutter scatter area
sc = [(dx + 520, dy + 150), (dx + 740, dy + 150), (dx + 740, dy + 240), (dx + 520, dy + 240)]
poly(s, sc, "#A0A0AA", "#A0A0AA", op=0.07, sw=1.2, dash="5 4")
r = random.Random(9)
for _ in range(14):
    xx, yy = dx + r.uniform(530, 725), dy + r.uniform(160, 228); k = r.choice([CONC, "#7A7A88", SAND, "#B06A3A"])
    s.rect(xx, yy, 10, 6, k, rx=1)
s.text(dx + 520, dy + 144, "Street clutter · Normal · 14 items · seed 3434", C.SUB, 11)
unclip(s)
panel(s, 800, 1052, 420, 250, "Obstacle — click one, or drag a line for a row")
s.text(812, 1086, "Item", C.TXT)
for i, (g, c, t, sel) in enumerate([(G["BOX"], SAND, "Sandbags", False), (G["CUBE"], CONC, "Concrete block", True), (G["BARRIER"], C.AMBER, "Barrier", False),
                                    (G["BAN"], C.RED, "Hedgehog", False), (G["CAR"], C.GREY, "Car wreck", False)]):
    yy = 1074 + i * 24
    s.rect(900, yy, 200, 22, C.BTNSEL if sel else "#1C1C22", rx=3); s.glyph(g, 906, yy + 4, c, 12.5); s.text(926, yy + 15, t, C.TXT, 12)
s.text(812, 1210, "Spacing", C.TXT); s.field(900, 1196, 70, "1.5 m"); s.text(978, 1210, "0 = end to end", C.NOTE, 11)
s.text(812, 1236, "Facing", C.TXT); seg(s, 900, 1223, ["Along the line", "Across"], 1)
s.separator_text(812, 1250, "What it does", 1210)
s.text(812, 1282, "a row across a road closes it to vehicles (P-2 road blockage)", C.SUB, 11.5)

# ── E — the Terrain Details tab ─────────────────────────────────────────────────────────────────────────────────────────────────
label(s, 20, 1330, "E  One Details tab for every feature (TE-8)")
s.window(20, 1342, 560, 200, "Details — Wall (3)")
s.tabs(28, 1366, 544, [("Terrain", G["WALL"], WALL), ("Ground", G["DIG"], "#C8965A"), ("Entity", FA.INFO, C.BLUE)], 0)
s.text(36, 1408, "Kind", C.TXT); seg(s, 140, 1394, ["Wall", "Fence", "Hedge"], 0)
s.text(36, 1434, "Material", C.TXT); s.combo(140, 1420, 160, "Brick")
s.text(36, 1460, "Height", C.TXT); s.field(140, 1446, 70, "2.0 m"); s.text(220, 1460, "the kind's default: 2.0 m", C.NOTE, 11)
s.separator_text(36, 1472, "Contents", 570)
s.text(36, 1504, "320 m · 2 gates · stops every vehicle; a tank breaks it (B-3) · blasts breach it (B-2)", C.SUB, 11.5)
s.text(36, 1524, "Edit Shape (E) · Add gate · Split here", C.NOTE, 11)
# F — the handles of placed features
label(s, 600, 1330, "F  What you placed stays visible and pickable (TE-9); the ghost while drawing (TE-10)")
fx0, fy0 = 600, 1342
clip(s, "cf", fx0, fy0, 620, 200); ground(s, fx0, fy0, 620, 200, 41)
circle(s, fx0 + 60, fy0 + 60, 9, TREE, 0.95, "#1E4A22"); circle(s, fx0 + 60, fy0 + 60, 3.2, "#FFFFFF")
s.text(fx0 + 76, fy0 + 64, "a placed tree: a handle dot, picked by a click", C.SUB, 11)
for i in range(6): s.rect(fx0 + 50 + i * 15, fy0 + 110, 14, 8, SAND, stroke="#7A6630", sw=0.8, rx=2)
s.rect(fx0 + 46, fy0 + 104, 98, 20, "none", stroke=SNAP, sw=1.6, rx=3)
s.text(fx0 + 152, fy0 + 118, "a row: selected as ONE (its outline), before the bake lands too", C.SUB, 11)
pline(s, [(fx0 + 60, fy0 + 160), (fx0 + 240, fy0 + 170)], WALL, 4, op=0.5); pline(s, [(fx0 + 60, fy0 + 160), (fx0 + 240, fy0 + 170)], WALL, 1.4, dash="5 4")
s.text(fx0 + 250, fy0 + 172, "the ghost: the wall the bake WILL make, in its own colour", C.SUB, 11)
unclip(s)

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "terrain-editor.svg")
s.save(out)
print("wrote", out)
