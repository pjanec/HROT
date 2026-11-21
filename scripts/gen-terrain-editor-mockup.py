# Generates docs/UX/terrain-editor.svg — the user-friendly terrain editing of DESIGN_Terrain_Editor_UX.md (CE-4594), house style (R-4143):
#   REVISED 2026-11-15 to the critical review (§8) and the rulings R-5032 (TE-1: the Editor Toolbar window) and R-4264 (TE-5: obstacles are entities):
#   A the toolbars (TE-19, R-5038): one shared dockable strip, a row on top / a column at the side, the main toolbar's Toolbars split icon + Layers · B trees: one · a row · a group (TE-3, TE-4)
#   · C a wall along a road: snapping (TE-6), a closed plot and its gate (TE-7) · D an obstacle row of ENTITIES (TE-5) and baked street clutter (TE-13)
#   · E the Terrain Details tab (TE-8) · F handles (TE-9) and the ghost that waits for the bake (TE-10).
# Regenerate: python3 scripts/gen-terrain-editor-mockup.py [docs/UX/terrain-editor.svg]
# SCOPE (R-4151): the toolbar's Terrain row and its flyouts, the draw panel's new rows, the snap marks, the gate split, the Terrain Details tab, the feature
# handles. CONTEXT, drawn as measured and UNCHANGED: the Editor Toolbar window (title, "Tools", the text buttons Select · Place Entity · Edit Shape ·
# Edit Route, the orange Reload BTrees, the teal ArmedColour 0.20/0.55/0.50 — EditorToolbarPanel.cs:36-66, EditorWindows.cs:42) · the draw panel's 260 px
# width (TerrainDrawPanel.cs:205) · the clutter / obstacle types (ClutterTypes.cs:20-27) ·
# the 2-D plan's colours (gen-ground-shaping-mockup.py, TerrainPlan2D) · the drawing look — white squares, route green, the next segment dashed (GizmoEditStyle) ·
# the draw panel bottom-left (TerrainDrawPanel.cs) · the ghost's two-line hint, green / amber, no glyphs (TerrainGhostDraw) · label-first Details with
# SeparatorText sections (GroundFeatureDetailsView.cs, TerrainBlockDetailsView.cs).
import os, sys, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 1730
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



ARMED = "#338C80"   # EditorToolbarPanel.ArmedColour (0.20, 0.55, 0.50)


def txt_btn(s, x, y, label, armed=False, fill=None):
    w = text_width(label, 12.5) + 16
    s.rect(x, y, w, 21, fill or (ARMED if armed else C.BTN), rx=2); s.text(x + 8, y + 15, label, C.TXT, 12.5)
    return x + w + 8


def fam_btn(s, x, y, glyph, gc, label, armed=False):
    w = 24 + text_width(label, 12.5) + 20
    s.rect(x, y, w, 21, ARMED if armed else C.BTN, rx=2); s.glyph(glyph, x + 6, y + 4, gc, 12); s.text(x + 23, y + 15, label, C.TXT, 12.5)
    s.raw(f"<path d='M{x + w - 12} {y + 9} l3.5 4 l3.5 -4 z' fill='{C.SUB}'/>")
    return x + w + 8


def dpanel(s, x, y, h, title, scatter=False):
    """The draw panel as built: 260 px, bottom-left of the work area, popup background, accent border (amber for a scatter)."""
    s.rect(x, y, 260, h, C.POPUP, stroke=C.AMBER if scatter else C.ACC, rx=4, sw=1)
    s.text(x + 8, y + 17, title, C.TXT, 12)


s = Svg(W, H, "Terrain editor — the user-friendly way (CE-4594) · revised",
        "DESIGN_Terrain_Editor_UX.md §5 (R-5032, R-4264, the review §8) · A toolbar · B trees · C walls, snap, gate · D obstacles · E Details · F handles. Grey = annotation.")

# ── A — the toolbars (TE-19, R-5038): ONE shared dockable strip; the main toolbar's Toolbars split icon + the Layers toggle ─────────
label(s, 20, 62, "A  Toolbars (TE-19, R-5038): one shared strip — a row docked on top, a column docked at the side; the main toolbar's split icon")
# the main toolbar (context: menus + the existing entries, drawn plain) and the two NEW icons at its right
s.rect(20, 72, 1200, 26, "#232328", stroke=C.BORDER)
x = 28
for t in ["File", "Edit", "View", "Scenario", "AI"]: s.text(x, 90, t, C.TXT, 12.5); x += text_width(t, 12.5) + 16
s.rect(x + 6, 76, 140, 18, "#2C2C34", rx=2); s.text(x + 14, 89, "… the existing entries", C.NOTE, 10.5)
ix = x + 160
s.rect(ix, 75, 30, 20, ARMED, rx=2); s.glyph(0xf552, ix + 8, 78, C.TXT, 13)          # the split icon's face (toolbox), lit: a toolbar is open
s.rect(ix + 31, 75, 14, 20, ARMED, rx=2); s.raw(f"<path d='M{ix + 34} 83 l4 5 l4 -5 z' fill='{C.TXT}'/>")
s.rect(ix + 52, 75, 30, 20, C.BTN, rx=2); s.glyph(0xf5fd, ix + 60, 78, C.BLUE, 13)    # Tactical Map Layers — its own toggle
s.menu(ix - 40, 100, 210, [("#", "Toolbars"), ("Scenario tools", "✓", FA.CROSSHAIRS, C.GREY), ("Terrain", "✓", G["TREE"], TREE), "---", ("(more toolbars later)", None, None, None, "")])
s.note(ix + 180, 108, ["the split icon: the face toggles the last toolbar,", "▾ lists every toolbar, ✓ = open — ONE shared widget,", "extracted from the ▶ / ▾ preset picker (CE-3393)", "", "Layers: its own toggle (a panel, not a toolbar)"], C.NOTE, 11)
# the Scenario tools toolbar, docked on top: a row of colour toggle icons
s.window(20, 120, 260, 52, "Scenario tools")
tx = 28
for g, c, arm in [(FA.CROSSHAIRS, C.GREY, True), (FA.PLUS, C.GREEN, False), (FA.DRAW, C.BLUE, False), (FA.ROUTE, C.TEAL, False)]:
    s.glyph_toggle(tx, 143, g, c, arm); tx += 34
s.tooltip(120, 176, 150, ["Edit Shape   E"])
s.note(20, 214, ["docked on top ⇒ a ROW;", "it locks nothing (units move", "in Live — R-4162)"], C.NOTE, 11)
# the Terrain toolbar, docked at the side: a column; Walls lit, its family dropdown open
s.window(300, 120, 44, 262, "")
s.rect(300, 120, 44, 20, C.TITLE); s.text(305, 134, "Terr", C.TXT, 11)
ty = 146
for g, c, arm in [(G["TREE"], TREE, False), (G["SEED"], C.GREEN, False), (G["WALL"], WALL, True), (G["CUBE"], CONC, False), (G["DIG"], "#C8965A", False), (0xf773, C.BLUE, False), (G["CAR"], C.GREY, False)]:
    s.glyph_toggle(307, ty, g, c, arm); ty += 32
s.menu(350, 206, 180, [("Wall", None, G["WALL"], WALL), ("Fence", None, G["WALL"], "#A07850"), ("Hedge   (last used)", None, G["WALL"], HEDGE), "---", ("Wall around a plot", None, FA.POLYGON, WALL)])
s.note(540, 214, ["docked at the side ⇒ a COLUMN;", "a family is ONE icon showing its", "last variant; its menu opens beside it", "", "⭐ the Terrain toolbar OPEN = placed", "terrain is selectable (the key, R-5038)"], C.NOTE, 11)
s.rect(800, 330, 420, 70, "#0F0F12", op=0.92, rx=3, stroke=C.BORDER)
s.text(810, 350, "Hedge — 3 drawn · click the first point of the next", C.TXT, 12)
s.text(810, 368, "Esc drops this shape · Esc again stops the tool", C.SUB, 11.5)
s.text(810, 386, "each repeat gets a new seed", C.NOTE, 11)
s.note(800, 418, ["TE-2: a terrain line / area tool stays armed after Finish,", "like point placement (always-multi, D5)"], C.NOTE, 11)

s.raw("<g transform='translate(0,170)'>")
# ── B — trees ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
label(s, 20, 302, "B  Trees — one, a row, a group (TE-3); a Row and a Group are ONE feature each (TE-4: kinds 14, 15)")
bx, by, bw, bh = 20, 312, 900, 330
clip(s, "cb", bx, by, bw, bh); ground(s, bx, by, bw, bh, 11)
road(s, [(bx - 10, by + 210), (bx + 300, by + 200), (bx + 910, by + 230)])
for hx, hy in [(120, 120), (190, 110), (260, 125), (560, 120)]: house(s, bx + hx, by + hy, 40, 30)
circle(s, bx + 70, by + 60, 9, TREE, 0.95, "#1E4A22"); circle(s, bx + 70, by + 60, 3, "#FFFFFF", 0.9)
s.text(bx + 84, by + 64, "① One — a click", C.SUB, 11)
row = [(bx + 330, by + 178), (bx + 520, by + 186), (bx + 700, by + 205)]
drawpts(s, row[:2], next_dash=row[2])
for i in range(16):
    t = i / 15.0
    if t <= 0.5: px, py = row[0][0] + (row[1][0] - row[0][0]) * t * 2, row[0][1] + (row[1][1] - row[0][1]) * t * 2
    else: px, py = row[1][0] + (row[2][0] - row[1][0]) * (t - .5) * 2, row[1][1] + (row[2][1] - row[1][1]) * (t - .5) * 2
    circle(s, px, py - 12, 7, TREE, 0.55, "#7FD08A", 1)
hint(s, bx + 470, by + 232, [("Row of 16 trees · 8 m apart · 124 m", C.GREEN), ("Ctrl: no snap · Enter ends", C.SUB)])
s.text(bx + 330, by + 160, "② Row — a line", C.SUB, 11)
grp = [(bx + 70, by + 250), (bx + 200, by + 250), (bx + 230, by + 315), (bx + 60, by + 320)]
poly(s, grp, "#5BD06A", "#5BD06A", op=0.08, sw=1.2, dash="5 4")
r = random.Random(4)
for _ in range(11): circle(s, bx + r.uniform(80, 210), by + r.uniform(262, 310), 6.5, TREE, 0.9, "#1E4A22")
s.text(bx + 70, by + 244, "③ Group — an area: 11 lone trees", C.SUB, 11)
dpanel(s, bx + 620, by + 20, 160, "Trees — click to add a point")
s.text(bx + 628, by + 60, "Plant", C.TXT, 12); seg(s, bx + 690, by + 46, ["One", "Row", "Group"], 1, 11.5)
s.text(bx + 628, by + 88, "Spacing", C.TXT, 12); s.field(bx + 690, by + 74, 70, "8 m")
s.text(bx + 628, by + 114, "Seed", C.TXT, 12); s.field(bx + 690, by + 100, 70, "7712"); s.glyph(G["DICE"], bx + 768, by + 103, C.SUB, 13)
s.text(bx + 628, by + 140, "16 trees · 124 m · Enter ends", C.SUB, 11.5)
unclip(s)
s.note(940, 320, ["the draw panel as built: 260 px,", "bottom-left of the work area", "", "a Row / Group = single trunks:", "they fell, burn and block like",
                  "any lone tree (B-6) — not a WOOD", "", "no Species / Size in v1:", "no field carries them (§5 TE-4)", "", "the placement is the recipe",
                  "`group` element's, shared (R-3)"], C.NOTE, 11)

# ── C — walls: snapping, a closed plot, a gate ───────────────────────────────────────────────────────────────────────────────────
label(s, 20, 670, "C  Walls — along a road and onto a hedge's end (snap, TE-6); round a plot with a gate (TE-7)")
cx0, cy0, cw, ch = 20, 682, 900, 330
clip(s, "cc", cx0, cy0, cw, ch); ground(s, cx0, cy0, cw, ch, 21)
road(s, [(cx0 - 10, cy0 + 90), (cx0 + 910, cy0 + 90)], 18)
house(s, cx0 + 120, cy0 + 170, 70, 46); house(s, cx0 + 470, cy0 + 175, 60, 50)
pline(s, [(cx0 + 40, cy0 + 107), (cx0 + 330, cy0 + 107)], WALL, 4)
drawpts(s, [(cx0 + 40, cy0 + 107), (cx0 + 330, cy0 + 107)], next_dash=(cx0 + 360, cy0 + 107))
pline(s, [(cx0 + 40, cy0 + 99.5), (cx0 + 360, cy0 + 99.5)], SNAP, 1.5, dash="3 3")
circle(s, cx0 + 360, cy0 + 107, 6, "none", 1, SNAP, 2)
s.chip(cx0 + 300, cy0 + 116, "road edge +1 m", G["MAGNET"], SNAP, arrow=False)
hint(s, cx0 + 40, cy0 + 130, [("Wall 320 m · brick 2 m · snapped: road edge +1 m", C.GREEN), ("Ctrl: no snap · Shift: 15° · Enter ends", C.SUB)])
# a closed plot with ONE gate: the line's start rotated into the gap (one feature)
pline(s, [(cx0 + 512, cy0 + 145), (cx0 + 580, cy0 + 145), (cx0 + 580, cy0 + 260), (cx0 + 430, cy0 + 260), (cx0 + 430, cy0 + 145), (cx0 + 490, cy0 + 145)], WALL, 4)
s.rect(cx0 + 490, cy0 + 138, 22, 14, "none", stroke=C.AMBER, sw=1.5, dash="3 2"); s.glyph(G["DOOR"], cx0 + 494, cy0 + 118, C.AMBER, 12)
s.menu(cx0 + 600, cy0 + 8, 170, [("Add gate here (3 m)", None, G["DOOR"], C.AMBER), ("Insert point", None, FA.PLUS, C.GREEN), "---", ("Finish", "Enter", FA.CHECK, C.GREEN)])
s.note(cx0 + 430, cy0 + 280, ["still ONE wall: the gate moves", "the line's start into the gap", "(the recipe enclosure's way) —", "one undo, its breaches kept"], C.SUB, 11)
pline(s, [(cx0 + 40, cy0 + 250), (cx0 + 190, cy0 + 300)], HEDGE, 5)
drawpts(s, [(cx0 + 300, cy0 + 300)], next_dash=(cx0 + 194, cy0 + 300))
circle(s, cx0 + 190, cy0 + 300, 7, "none", 1, SNAP, 2)
s.chip(cx0 + 205, cy0 + 268, "joins Hedge 2", G["MAGNET"], SNAP, arrow=False)
dpanel(s, cx0 + 620, cy0 + 180, 146, "Wall — click to add a point")
s.text(cx0 + 628, cy0 + 220, "Kind", C.TXT, 12); seg(s, cx0 + 680, cy0 + 206, ["Hedge", "Wall", "Fence"], 1, 11.5)
s.text(cx0 + 628, cy0 + 246, "Height", C.TXT, 12); s.field(cx0 + 680, cy0 + 232, 60, "2 m")
s.text(cx0 + 628, cy0 + 272, "Snap", C.TXT, 12); s.checkbox(cx0 + 680, cy0 + 258, True); s.text(cx0 + 704, cy0 + 272, "roads · walls · ends", C.SUB, 11.5)
s.text(cx0 + 628, cy0 + 302, "212 m · Enter ends", C.SUB, 11.5)
unclip(s)
s.note(940, 690, ["snap is ON for LINE tools only", "(walls, rows, ditches), off for", "areas — ⚠ a deviation from", "Map Interaction's off-default,",
                  "for the user to rule (TE-6)", "", "Ctrl (a keymap row) frees one", "point; Alt-click stays", "\"select the next overlapped\"", "",
                  "Kind offers the wall family only:", "a line never becomes a point"], C.NOTE, 11)

# ── D — obstacles are ENTITIES (R-4264) ──────────────────────────────────────────────────────────────────────────────────────────
label(s, 20, 1040, "D  Obstacles are ENTITIES (TE-5, R-4264): one, or a row = one undo, one group · street clutter stays baked (TE-13)")
dx, dy, dw, dh = 20, 1052, 900, 250
clip(s, "cd", dx, dy, dw, dh); ground(s, dx, dy, dw, dh, 31)
road(s, [(dx - 10, dy + 120), (dx + 910, dy + 120)], 22)
for i in range(12): s.rect(dx + 60 + i * 15, dy + 40 + i * 3, 14, 8, SAND, stroke="#7A6630", sw=0.8, rx=2)
s.text(dx + 60, dy + 30, "Sandbag wall × 12 · a row, end to end — one group", C.SUB, 11)
blk = [(dx + 380, dy + 95), (dx + 380, dy + 150)]
for i in range(4): s.rect(dx + 372, dy + 96 + i * 15, 16, 9, CONC, stroke="#606068", sw=0.8)
s.rect(dx + 366, dy + 90, 28, 70, "none", stroke=SNAP, sw=1.4, rx=3)
drawpts(s, blk)
hint(s, dx + 120, dy + 175, [("4 concrete blocks · 1.5 m gaps · one undo · the road CLOSES", C.AMBER), ("SUMO re-routes · a tank crushes · the bulldozer clears", C.SUB)])
sc = [(dx + 500, dy + 168), (dx + 610, dy + 168), (dx + 610, dy + 242), (dx + 500, dy + 242)]
poly(s, sc, "#A0A0AA", "#A0A0AA", op=0.07, sw=1.2, dash="5 4")
r = random.Random(9)
for _ in range(14):
    xx, yy = dx + r.uniform(505, 596), dy + r.uniform(174, 232); k = r.choice([CONC, "#7A7A88", SAND, "#B06A3A"])
    s.rect(xx, yy, 10, 6, k, rx=1)
s.note(dx + 500, dy + 150, ["Street clutter (Scatter):", "baked scenery, inert"], C.SUB, 11)
dpanel(s, dx + 620, dy + 6, 240, "Obstacles — drag a line for a row")
for i, (g, c, t, sel) in enumerate([(G["CAR"], C.GREY, "Car", False), (G["BOX"], SAND, "Sandbag wall", False), (G["CUBE"], CONC, "Concrete block", True),
                                    (G["BOX"], "#B07A40", "Crate", False), (0xf613, C.AMBER, "Barrel", False), (G["BARRIER"], C.AMBER, "Jersey barrier", False),
                                    (FA.HOUSE, "#B07A40", "Guard booth", False)]):
    yy = dy + 30 + i * 21
    s.rect(dx + 628, yy, 244, 19, C.BTNSEL if sel else "#1C1C22", rx=3); s.glyph(g, dx + 634, yy + 3, c, 12); s.text(dx + 654, yy + 14, t, C.TXT, 12)
s.text(dx + 628, dy + 196, "Gap", C.TXT, 12); s.field(dx + 680, dy + 182, 60, "1.5 m")
s.text(dx + 628, dy + 222, "Facing", C.TXT, 12); seg(s, dx + 680, dy + 209, ["Along", "Across"], 1, 11.5)
unclip(s)
s.note(940, 1060, ["the types are the ONE ClutterTypes", "table: a baked item and the obstacle", "of the same name are one box", "",
                   "Barrel, Jersey barrier, Guard booth", "become obstacle types (TE-5)", "", "the group's mechanism is measured", "first (candidate: MacroInstance)"], C.NOTE, 11)

# ── E — the Terrain Details tab ───────────────────────────────────────────────────────────────────────────────────────────────────
label(s, 20, 1330, "E  One Details tab for every feature (TE-8)")
s.window(20, 1342, 560, 200, "Details — Wall (3)")
s.tabs(28, 1366, 544, [("Terrain", G["WALL"], WALL), ("Entity", FA.INFO, C.BLUE)], 0)
s.text(36, 1408, "Kind", C.TXT); seg(s, 140, 1394, ["Wall", "Fence", "Hedge"], 0)
s.text(36, 1434, "Material", C.TXT); s.combo(140, 1420, 160, "Brick")
s.text(36, 1460, "Height", C.TXT); s.field(140, 1446, 70, "2.0 m"); s.text(220, 1460, "the kind's default: 2.0 m", C.NOTE, 11)
s.separator_text(36, 1472, "Contents", 570)
s.text(36, 1504, "320 m · 1 gate · stops every vehicle; a tank breaks it (B-3) · blasts breach it (B-2)", C.SUB, 11.5)
s.text(36, 1524, "Edit Shape (E) · Add gate here", C.NOTE, 11)

# ── F — handles and the ghost that waits for the bake ────────────────────────────────────────────────────────────────────────────
label(s, 600, 1330, "F  Handles (TE-9) · the ghost stays until the bake lands (TE-10)")
fx0, fy0 = 600, 1342
clip(s, "cf", fx0, fy0, 620, 200); ground(s, fx0, fy0, 620, 200, 41)
circle(s, fx0 + 60, fy0 + 50, 9, TREE, 0.95, "#1E4A22"); circle(s, fx0 + 60, fy0 + 50, 3.2, "#FFFFFF")
s.text(fx0 + 76, fy0 + 54, "a placed tree: a handle dot, picked by a click", C.SUB, 11)
for i in range(6): circle(s, fx0 + 56 + i * 16, fy0 + 104, 6, TREE, 0.9, "#1E4A22")
s.rect(fx0 + 44, fy0 + 94, 104, 20, "none", stroke=SNAP, sw=1.6, rx=3)
s.text(fx0 + 156, fy0 + 108, "a Row: selected as ONE (its outline)", C.SUB, 11)
pline(s, [(fx0 + 60, fy0 + 160), (fx0 + 240, fy0 + 170)], WALL, 4, op=0.5); pline(s, [(fx0 + 60, fy0 + 160), (fx0 + 240, fy0 + 170)], WALL, 1.4, dash="5 4")
s.text(fx0 + 250, fy0 + 160, "just finished: the ghost holds until the bake", C.SUB, 11)
s.text(fx0 + 250, fy0 + 175, "takes it (≈ 5 s warm) — it never blinks out", C.SUB, 11)
unclip(s)

s.raw("</g>")
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "terrain-editor.svg")
s.save(out)
print("wrote", out)
