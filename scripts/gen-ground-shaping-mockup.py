# Generates docs/UX/ground-shaping-and-ditches.svg — the Earthworks tools of DESIGN_Ground_Patches_And_Road_Blockage.md, house style (R-4143):
#   §4a the ground-shaping tool (G-14…G-19) · §4c ditches (G-26…G-29) · §4c-1 crater cover and foxholes (G-35…G-38) · §4d one feature family (G-30…G-34)
#   · §9.5 P-5 (V-27, V-28) and P-6 (V-29, V-30) · V-14 (an authored crater is a crater RECORD) · V-25 (SUMO's net follows only after save + reload).
# Regenerate: python3 scripts/gen-ground-shaping-mockup.py [docs/UX/ground-shaping-and-ditches.svg]
# SCOPE (R-4151): the Earthworks group and its tiles, the Ground / Ditch / Foxhole drawing (the draw panel's new rows, the live preview, the hint lines),
# the Ground and Ditch Details tabs, the cover-point markers they add. CONTEXT, drawn as measured and UNCHANGED:
#   Add Entity's sidebar + the six terrain groups (TerrainPicks.cs:60, Icons.cs:131-136, §15a U-1) · the 2-D plan's colours (gen-terrain-authoring-mockups.py,
#   TerrainPlan2D) · the drawing look — white squares, a dashed next segment (GizmoEditStyle, §15a-2 C) · the draw panel (TerrainDrawPanel.cs:174-200, bottom-left) ·
#   the ghost's two-line hint backdrop, green / amber, no glyphs (TerrainGhostDraw, §15a U-2) · label-first Details with SeparatorText sections and a plain
#   Contents line (TerrainBlockDetailsView.cs:60-103) · the cover-point marker: a green dot sized by stance + a tick toward the cover (CoverPointsGizmo.cs:21, :48-56).
import os, sys, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 1530
EARTH = "#C8965A"                                   # the TERRAIN-class patches (heights only)
GROUND = ["#33452C", "#3B5131", "#4A5C36", "#5E643C"]
ROAD = "#E8D58A"; ROAD_EDGE = "#8A7A40"; HOUSE = "#E1CDA0"; FOREST = "#286E32"; ROUTE = "#5BD06A"
COVER = "#00C850"; COVER_DIM = "#6E7A72"            # CoverPointsGizmo.CoverColor (0,200,80); greyed = not yet available
G = dict(DIG=0xf85e, LEVEL=0xf547, RAISE=0xe4c2, LOWER=0xe4b8, BURST=0xe4dc, BARRIER=0xe562, MOUND=0xe52d, ROAD=0xf018, CAR=0xf1b9,
         WALK=0xf554, CITY=0xf64f, HOUSE=0xe3af, TREE=0xf1bb, WALL=0xf7a4, DUMP=0xf793, WATER=0xf773, SHIELD=0xf3ed, CLOCK=0xf017)


def poly(s, pts, fill, stroke=None, op=1.0, sw=1, dash=None, extra=""):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    st = f" stroke='{stroke}' stroke-width='{sw}'" if stroke else ""
    d = f" stroke-dasharray='{dash}'" if dash else ""
    s.raw(f"<polygon points='{p}' fill='{fill}' fill-opacity='{op}'{st}{d}{extra}/>")


def pline(s, pts, col, w, dash=None, op=1.0, cap="round"):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    d = f" stroke-dasharray='{dash}'" if dash else ""
    s.raw(f"<polyline points='{p}' fill='none' stroke='{col}' stroke-width='{w}' stroke-linecap='{cap}' stroke-linejoin='round' stroke-opacity='{op}'{d}/>")


def clip(s, cid, x, y, w, h):
    s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{w}' height='{h}'/></clipPath><g clip-path='url(#{cid})'>")


def unclip(s): s.raw("</g>")


def ground(s, x, y, w, h, seed):
    r = random.Random(seed)
    s.rect(x, y, w, h, GROUND[0])
    for _ in range(5):
        cx, cy = x + r.uniform(0.1, 0.9) * w, y + r.uniform(0.1, 0.9) * h
        for b in range(1, 3):
            k = 1 - b * 0.3; rx, ry = r.uniform(60, 140) * k, r.uniform(40, 90) * k
            pts = [(cx + rx * math.cos(2 * math.pi * i / 14) * (1 + r.uniform(-.15, .15)), cy + ry * math.sin(2 * math.pi * i / 14) * (1 + r.uniform(-.15, .15))) for i in range(14)]
            poly(s, pts, GROUND[b], "#2A3824", op=0.85, sw=0.6)


def hint(s, x, y, lines):
    """The ghost's shared two-line hint backdrop (TerrainGhostDraw): dark, plain coloured text, no glyphs."""
    w = max(text_width(t, 11.5) for t, _ in lines) + 16
    s.rect(x, y, w, 8 + 16 * len(lines), "#0F0F12", op=0.88, rx=3)
    for i, (t, c) in enumerate(lines): s.text(x + 8, y + 17 + i * 16, t, c, 11.5)


def cover(s, x, y, ang, stance="crouch", dim=False):
    """A cover point as CoverPointsGizmo draws it: a dot by stance (prone .15 · crouch .25 · stand .35 m) and a tick toward the cover."""
    r = {"prone": 2.6, "crouch": 3.6, "stand": 4.6}[stance]; c = COVER_DIM if dim else COVER
    s.raw(f"<circle cx='{x:.1f}' cy='{y:.1f}' r='{r}' fill='{c}' fill-opacity='{0.55 if dim else 0.9}'/>")
    s.line(x, y, x + 11 * math.cos(ang), y + 11 * math.sin(ang), c, 2)


def drawpts(s, pts, closed_dash=True):
    """The existing drawing look (GizmoEditStyle): the line in route green, white squares, the next segment dashed."""
    pline(s, pts, ROUTE, 1.6)
    if closed_dash: pline(s, [pts[-1], pts[0]], ROUTE, 1.6, dash="6 4", op=0.8)
    for i, p in enumerate(pts): s.rect(p[0] - 4, p[1] - 4, 8, 8, "#FFFFFF", stroke=ROUTE if i else C.ACC, sw=1.4)


def seg(s, x, y, opts, sel, sz=12, new=False):
    for i, o in enumerate(opts):
        bw = text_width(o, sz) + 20
        s.rect(x, y, bw, 21, C.BTNSEL if i == sel else C.BTN, rx=3, stroke=C.ACC if i == sel else None)
        s.text(x + bw / 2, y + 15, o, C.TXT, sz, anchor="middle"); x += bw + 3
    return x


def badge(s, x, y, txt="NEW"):
    w = text_width(txt, 9.5) + 10
    s.rect(x, y, w, 13, C.ACC, rx=6); s.text(x + w / 2, y + 10, txt, "#0F0F0F", 9.5, anchor="middle", weight="bold")


def section(s, x, y, label, x_end): s.separator_text(x, y, label, x_end)


s = Svg(W, H, "Earthworks — ground shaping, ditches, foxholes (DESIGN_Ground_Patches_And_Road_Blockage.md §4a, §4c, §4c-1, §4d; house style)",
        "Scope: the Earthworks group, the Ground / Ditch / Foxhole drawing and their Details, the cover points. Context (Add Entity, the map, the draw panel, the hint backdrop) is UNCHANGED. Grey notes are not drawn by the app.")

# ═══════════════════ A — Add Entity ▸ Earthworks ═══════════════════
AX, AY, AW, AH = 20, 76, 1200, 330
s.text(AX, AY - 8, "A — Add Entity ▸ the NEW Earthworks group, beside the six terrain groups (one family, G-30: the same picks and the point / line / area tools)", C.SUB, 12)
s.window(AX, AY, AW, AH, "Add Entity")
s.field(AX + 10, AY + 30, 300, "Search all groups", hint=True); s.glyph(FA.SEARCH, AX + 290, AY + 33, C.DIS, 12)
GR = [("Recent & favourites", FA.STAR, C.AMBER, 8, 0.5), ("…  8 more groups", None, None, None, 0.5),
      ("Blocks", G["CITY"], C.ORANGE, 10, 1), ("Buildings", G["HOUSE"], C.ORANGE, 9, 1), ("Vegetation", G["TREE"], C.GREEN, 3, 1),
      ("Walls & fences", G["WALL"], C.GREY, 3, 1), ("Clutter", G["DUMP"], C.GREY, 7, 1), ("Water & bridges", G["WATER"], C.BLUE, 2, 1),
      ("Earthworks", G["DIG"], EARTH, 7, 1)]
for i, (lab, g, gc, n, op) in enumerate(GR):
    yy = AY + 62 + i * 26
    sel = lab == "Earthworks"
    if sel: s.rect(AX + 10, yy, 200, 23, C.NEUTRALSEL, rx=3)
    if g: s.glyph(g, AX + 18, yy + 5, gc, 12, op=op)
    s.text(AX + 38 if g else AX + 18, yy + 16, lab, C.TXT if op == 1 else C.DIS, 12 if g else 11)
    if n is not None: s.text(AX + 202, yy + 16, str(n), C.NOTE, 11, anchor="end")
    if sel: badge(s, AX + 130, yy + 5)
s.line(AX + 218, AY + 56, AX + 218, AY + AH - 10)
TILES = [("Level ground", "area · level to one height", G["LEVEL"], EARTH, "TERRAIN"), ("Raise ground", "area · raise by dZ", G["RAISE"], EARTH, "TERRAIN"),
         ("Lower ground", "area · lower by dZ", G["LOWER"], EARTH, "TERRAIN"), ("Crater (authored)", "click · a crater record (V-14)", G["BURST"], C.ORANGE, "OBSTACLE"),
         ("Ditch", "line · width, depth, banks", G["DIG"], C.ORANGE, "OBSTACLE"), ("Anti-tank ditch", "line · ≥ 3 m wide, ≥ 1.5 m deep", G["BARRIER"], C.ORANGE, "OBSTACLE"),
         ("Foxhole", "click + heading · 2 × 1.5 × 1 m", FA.PERSON_RIFLE, C.ORANGE, "OBSTACLE")]
for i, (n, ln, g, gc, cls) in enumerate(TILES):
    tx, ty = AX + 232 + (i % 4) * 186, AY + 62 + (i // 4) * 128
    sel = i == 1
    s.rect(tx, ty, 176, 118, C.NEUTRALSEL if sel else C.NEUTRAL, stroke=C.ACC if sel else C.BORDER, rx=4, sw=1.5 if sel else 1)
    s.rect(tx + 4, ty + 4, 168, 72, "#26262E"); s.glyph(g, tx + 72, ty + 22, gc, 32)
    s.text(tx + 168, ty + 16, cls, C.NOTE, 9, anchor="end")
    s.text(tx + 8, ty + 94, n, C.TXT, 12, weight="bold"); s.text(tx + 8, ty + 110, ln, C.NOTE, 10.5)
SX = AX + 990
s.line(SX - 8, AY + 56, SX - 8, AY + AH - 10)
s.rect(SX, AY + 62, 200, 100, "#26262E", rx=3); s.glyph(G["RAISE"], SX + 82, AY + 92, EARTH, 38)
s.text(SX, AY + 186, "Raise ground", C.TXT, 14, weight="bold")
s.note(SX, AY + 206, ["Draw an area; the ground inside", "rises, its edge blends over a skirt.", "Houses and roads in it are kept", "out (islands). Roads follow it;", "SUMO's net at save."], C.SUB, 11.5)
s.button(SX, AY + AH - 34, 200, "Place on map", FA.LOCATION, C.RED, fill=C.BTNSEL)
s.note(AX, AY + AH + 18, ["The SAME gallery and pane (§15a U-1); NEW: the Earthworks group (its icon in the sidebar like the six) and seven tiles, each naming its TOOL — an area, a line or a click — and its class (G-31:",
                          "TERRAIN = heights only, never a blocker G-15 · OBSTACLE = heights + a navmesh area + cover points). Glyph tiles stand in for thumbnails, as on the other terrain groups."], C.NOTE, 11.5)

# ═══════════════════ B — drawing a Ground area on the 2-D map ═══════════════════
BX, BY, BW, BH = 20, 476, 760, 440
s.text(BX, BY - 8, "B — drawing a Ground area (Raise by 2.5 m): the live preview, the islands, the grade hint", C.SUB, 12)
s.rect(BX, BY, BW, BH, C.BG, stroke=C.BORDER)
clip(s, "bmap", BX + 1, BY + 1, BW - 2, BH - 2)
ground(s, BX, BY, BW, BH, 21)
for tx, ty in [(BX + 640, BY + 60), (BX + 680, BY + 90), (BX + 700, BY + 50), (BX + 60, BY + 380), (BX + 95, BY + 400)]:
    s.raw(f"<circle cx='{tx}' cy='{ty}' r='16' fill='{FOREST}' fill-opacity='0.9'/>")
ROADP = [(BX - 10, BY + 300), (BX + 220, BY + 268), (BX + 420, BY + 232), (BX + 620, BY + 170), (BX + BW + 10, BY + 140)]
pline(s, ROADP, ROAD_EDGE, 12); pline(s, ROADP, ROAD, 8)
# the polygon, its skirt, the ΔZ tint with the islands masked out (house + skirt, the road corridor)
P = [(BX + 150, BY + 120), (BX + 400, BY + 80), (BX + 560, BY + 150), (BX + 540, BY + 330), (BX + 300, BY + 380), (BX + 130, BY + 300)]
cxp, cyp = sum(p[0] for p in P) / len(P), sum(p[1] for p in P) / len(P)
def scaled(k): return [(cxp + (x - cxp) * k, cyp + (y - cyp) * k) for x, y in P]
HX, HY, HW, HH = BX + 395, BY + 280, 46, 34                      # a house in the area
s.raw("<mask id='isl'><rect x='0' y='0' width='1240' height='1580' fill='white'/>"
      f"<rect x='{HX - 16}' y='{HY - 16}' width='{HW + 32}' height='{HH + 32}' rx='10' fill='black'/>"
      f"<polyline points='{' '.join(f'{x},{y}' for x, y in ROADP)}' fill='none' stroke='black' stroke-width='30' stroke-linejoin='round'/></mask>")
s.raw("<g mask='url(#isl)'>")
poly(s, scaled(1.12), "#E8A040", op=0.10)
for k, op in [(1.0, 0.18), (0.8, 0.22), (0.55, 0.26), (0.3, 0.30)]: poly(s, scaled(k), "#E8A040", op=op)
s.raw("</g>")
poly(s, scaled(1.12), "none", "#E8C890", sw=1.2, dash="3 4")
s.rect(HX, HY, HW, HH, HOUSE, stroke="#2A2A2A", sw=0.8)
s.rect(HX - 16, HY - 16, HW + 32, HH + 32, "none", stroke="#FFFFFF", sw=1.2, dash="4 3", rx=10)
for off in (-15, 15):
    pts = [(x, y + off) for x, y in [(BX + 128, BY + 288), (BX + 220, BY + 268), (BX + 420, BY + 232), (BX + 575, BY + 183)]]
    pline(s, pts, "#FFFFFF", 1.2, dash="4 3", op=0.9)
drawpts(s, P)
s.rect(P[-1][0] - 4, P[-1][1] - 4, 8, 8, "#FFFFFF", stroke=C.ACC, sw=2)
unclip(s)
# labels on the map
s.callout(BX + 470, BY + 120, BX + 590, BY + 96, ["+2.5 m (the tint: the change,", "strongest inside — NEW)"])
s.callout(BX + 560, BY + 380, BX + 590, BY + 400, ["skirt 8 m (dashed)"])
s.callout(HX + HW + 16, HY + HH, BX + 470, BY + 352, [])
s.text(BX + 452, BY + 368, "island: house + 4 m skirt untouched (G-18)", C.TXT, 11)
s.callout(BX + 280, BY + 248, BX + 160, BY + 214, [])
s.text(BX + 64, BY + 206, "island: road corridor locked —", C.TXT, 11); s.text(BX + 64, BY + 220, "re-levelled along the shape (G-24, V-28)", C.TXT, 11)
hint(s, BX + 300, BY + 20, [("steepest road grade 18 % (over 15 %) — roads follow; SUMO's net after save", C.PEND),
                            ("kept out: 1 house, 1 road corridor", C.OK)])
# the draw panel (TerrainDrawPanel, bottom-left) with the NEW rows
DPX, DPY = BX + 10, BY + BH - 108
s.rect(DPX, DPY, 330, 98, C.POPUP, stroke=C.BORDER, rx=4, op=0.97)
s.glyph(G["MOUND"], DPX + 10, DPY + 8, EARTH, 13); s.text(DPX + 30, DPY + 20, "Ground — click to add a point", C.TXT, 12, weight="bold")
s.text(DPX + 10, DPY + 44, "Mode", C.SUB, 11.5); seg(s, DPX + 58, DPY + 30, ["Level", "Raise", "Lower"], 1, 11.5)
badge(s, DPX + 286, DPY + 34)
s.text(DPX + 10, DPY + 72, "Raise by", C.SUB, 11.5); s.field(DPX + 62, DPY + 58, 62, "2.5 m")
s.text(DPX + 136, DPY + 72, "Skirt", C.SUB, 11.5); s.field(DPX + 168, DPY + 58, 52, "8 m")
s.text(DPX + 10, DPY + 92, "1.2 ha · Enter ends", C.NOTE, 11)
s.note(BX, BY + BH + 18, ["The polygon is the existing area tool (U-7). NEW: the tint of the CHANGE (the 2-D map draws no heights today — P-3),",
                          "the dashed skirt, the two ISLANDS outlined white, the Mode row in the draw panel, and the hint lines on the shared ghost",
                          "backdrop: amber over ~15 % (G-19 — shown, never refused), else green ( steepest road grade 6 % — roads follow; … ).",
                          "Newest wins where shapes overlap (V-28). Our road graph and navmesh follow at once; SUMO's cars and the drawn road",
                          "ribbon keep the old z until save + reload (V-25)."], C.NOTE, 11.5)

# ═══════════════════ C — Details for a Ground feature ═══════════════════
CX, CY, CW, CH = 800, 476, 420, 440
s.text(CX, CY - 8, "C — Details of a Ground feature (a NEW tab, label-first)", C.SUB, 12)
s.window(CX, CY, CW, CH, "Details — Ground 1")
s.tabs(CX + 6, CY + 26, CW - 12, [("Ground", G["MOUND"], EARTH)], 0)
LX, CC = CX + 12, CX + 84
s.text(LX, CY + 76, "Mode", C.TXT, 12.5)
seg(s, CC, CY + 62, ["Level to Z", "Raise by dZ", "Lower by dZ", "Crater"], 1)
s.form_row(LX, CY + 94, "Raise by", 72, "field", 90, "2.5 m"); s.text(CC + 100, CY + 108, "dZ — Level reads  Level to  [212.0 m]", C.NOTE, 10.5)
s.form_row(LX, CY + 122, "Skirt", 72, "field", 90, "8 m"); s.text(CC + 100, CY + 136, "the blend to natural ground", C.NOTE, 10.5)
s.form_row(LX, CY + 150, "Seed", 72, "field", 90, "—", dis=True); s.text(CC + 100, CY + 164, "none: a shape is exact", C.NOTE, 10.5)
section(s, LX, CY + 182, "Effect", CX + CW - 12)
EF = [(FA.WARNING, C.PEND, "steepest road grade 18 % — over 15 %", C.PEND), (G["MOUND"], EARTH, "area 1.2 ha · raised 2.5 m", C.TXT),
      (FA.LOCK, C.SUB, "kept out: 1 house + skirt · 1 road corridor", C.TXT), (FA.LAYERS, C.VIOLET, "drawn over Raise 1 — the newest wins (V-28)", C.TXT),
      (G["ROAD"], C.AMBER, "SUMO net re-elevated at save (our roads follow now)", C.TXT)]
for i, (g, gc, t, tc) in enumerate(EF):
    yy = CY + 210 + i * 24
    s.glyph(g, LX + 2, yy + 1, gc, 12); s.text(LX + 22, yy + 12, t, tc, 12)
section(s, LX, CY + 336, "Crater chosen?", CX + CW - 12)
s.note(LX, CY + 366, ["Crater is not a ground shape: it saves a crater RECORD", "(obstacle class — a navmesh area, rim cover, the road", "closure), V-14 over G-14. The form then shows its", "radius and depth, and the tile Crater (authored) is its click."], C.SUB, 11.5)
s.note(CX, CY + CH + 18, ["Mode, height and skirt are the sibling GroundFeature component (V-27 —", "TerrainFeature's 4 fields are not enough). Every edit = one", "undo step and one live bake, like a placed house (§18e B)."], C.NOTE, 11.5)

# ═══════════════════ D — a Ditch, drawn with the line tool ═══════════════════
DX, DY, DW, DH = 20, 1020, 440, 410
s.text(DX, DY - 8, "D — a Ditch drawn with the line tool, and its Details", C.SUB, 12)
s.rect(DX, DY, DW, DH, C.BG, stroke=C.BORDER)
clip(s, "dmap", DX + 1, DY + 1, DW - 2, DH - 2)
ground(s, DX, DY, DW, DH, 33)
RD = [(DX + 330, DY - 10), (DX + 345, DY + 200), (DX + 360, DY + DH + 10)]
pline(s, RD, ROAD_EDGE, 14); pline(s, RD, ROAD, 10)
for off in (-17, 17): pline(s, [(x + off, y) for x, y in RD], "#FFFFFF", 1.1, dash="4 3", op=0.7)
DITCH = [(DX + 40, DY + 300), (DX + 120, DY + 250), (DX + 220, DY + 230), (DX + 326, DY + 222)]
pline(s, DITCH, "#2A2418", 22, cap="butt"); pline(s, DITCH, "#4A3C26", 14, cap="butt"); pline(s, DITCH, "#1E1810", 5, cap="butt")
for p in DITCH: s.rect(p[0] - 4, p[1] - 4, 8, 8, "#FFFFFF", stroke=ROUTE, sw=1.4)
s.rect(DITCH[0][0] - 4, DITCH[0][1] - 4, 8, 8, "#FFFFFF", stroke=C.ACC, sw=1.6)
# the floor points: a pair facing each bank, every spacing (V-30)
for i in range(len(DITCH) - 1):
    (x0, y0), (x1, y1) = DITCH[i], DITCH[i + 1]
    L = math.hypot(x1 - x0, y1 - y0); ux, uy = (x1 - x0) / L, (y1 - y0) / L; nx, ny = -uy, ux
    k = 0.0
    while k < L - 10:
        px, py = x0 + ux * (k + 16), y0 + uy * (k + 16)
        cover(s, px + nx * 2.5, py + ny * 2.5, math.atan2(ny, nx)); cover(s, px - nx * 2.5, py - ny * 2.5, math.atan2(-ny, -nx))
        k += 34
s.raw(f"<circle cx='{DITCH[-1][0]}' cy='{DITCH[-1][1]}' r='7' fill='none' stroke='{C.RED}' stroke-width='2'/>")
s.line(DITCH[-1][0] - 5, DITCH[-1][1] - 5, DITCH[-1][0] + 5, DITCH[-1][1] + 5, C.RED, 2)
unclip(s)
hint(s, DX + 148, DY + 156, [("stops at the road — G-29", C.PEND), ("64 m · 2.0 × 1.2 m · Enter ends", C.SUB)])
s.callout(DX + 150, DY + 252, DX + 70, DY + 352, ["cover points on the FLOOR,", "a pair facing each bank (V-30)"])
s.note(DX, DY + DH + 18, ["The line is the wall / fence / hedge tool (U-7); NEW: the ditch drawn as a dug band, its floor", "points (the existing cover marker: a dot by stance + a tick toward the cover), and the stop at", "the locked road corridor — a roadside ditch runs beside it; a culvert is later."], C.NOTE, 11.5)
# Details — Ditch
EX, EY, EW, EH = 470, 1020, 360, 410
s.window(EX, EY, EW, EH, "Details — Ditch 1")
s.tabs(EX + 6, EY + 26, EW - 12, [("Ditch", G["DIG"], C.ORANGE)], 0)
LX, CC = EX + 12, EX + 92
s.text(LX, EY + 76, "Preset", C.TXT, 12.5); seg(s, CC, EY + 62, ["Ditch", "Anti-tank ditch", "Foxhole"], 0, 11.5)
s.form_row(LX, EY + 94, "Width", 80, "field", 80, "2.0 m")
s.form_row(LX, EY + 122, "Depth", 80, "field", 80, "1.2 m")
s.form_row(LX, EY + 150, "Bank slope", 80, "field", 80, "45°"); s.text(CC + 90, EY + 164, "walls ≤ 60°: infantry climb", C.NOTE, 10.5)
section(s, LX, EY + 182, "Effect", EX + EW - 12)
DF = [(FA.SHIELD, COVER, "cover: crouch along both banks", "(floor points, a pair facing each bank — V-30)"),
      (G["CAR"], C.RED, "vehicles: wheeled closed · tracked closed", "(every vehicle avoids a ditch area — D-2)"),
      (G["WALK"], C.OK, "infantry passes (a small cost)", None),
      (G["ROAD"], C.AMBER, "stops at the road corridor (G-29)", None),
      (FA.LAYERS, C.VIOLET, "64 m · 3 vertices · newest on top", None)]
yy = EY + 208
for g, gc, t, sub in DF:
    s.glyph(g, LX + 2, yy + 1, gc, 12); s.text(LX + 22, yy + 12, t, C.TXT, 12)
    if sub: s.text(LX + 22, yy + 27, sub, C.NOTE, 10.5); yy += 15
    yy += 24
s.note(LX, EY + EH - 34, ["Anti-tank ditch: ≥ 3 m wide, ≥ 1.5 m deep (G-28); Foxhole: below.", "Stance by depth: ≥ 1.5 stand · ≥ 0.9 crouch · ≥ 0.45 prone (G-27)."], C.NOTE, 10.5)

# ═══════════════════ E — a Foxhole, placed by a click ═══════════════════
FX, FY, FW, FH = 850, 1020, 370, 200
s.text(FX, FY - 8, "E — a Foxhole: a click, then the heading (R turns)", C.SUB, 12)
s.rect(FX, FY, FW, FH, C.BG, stroke=C.BORDER)
clip(s, "fmap", FX + 1, FY + 1, FW - 2, FH - 2)
ground(s, FX, FY, FW, FH, 44)
cx0, cy0 = FX + 120, FY + 104
s.raw(f"<ellipse cx='{cx0}' cy='{cy0}' rx='40' ry='30' fill='#4A3C26' stroke='#2A2418' stroke-width='3' transform='rotate(-30 {cx0} {cy0})'/>")
s.raw(f"<ellipse cx='{cx0}' cy='{cy0}' rx='22' ry='15' fill='#1E1810' transform='rotate(-30 {cx0} {cy0})'/>")
s.rect(cx0 - 44, cy0 - 36, 88, 72, "none", stroke=C.ACC, sw=1.6, dash="5 3", rx=3)
ah = math.radians(-30)
s.o.append(f"<path d='M{cx0} {cy0} L{cx0 + 62 * math.cos(ah):.1f} {cy0 + 62 * math.sin(ah):.1f}' stroke='{C.ACC}' stroke-width='2.5'/>")
hx, hy = cx0 + 62 * math.cos(ah), cy0 + 62 * math.sin(ah)
poly(s, [(hx + 9 * math.cos(ah), hy + 9 * math.sin(ah)), (hx + 7 * math.cos(ah + 2.2), hy + 7 * math.sin(ah + 2.2)), (hx + 7 * math.cos(ah - 2.2), hy + 7 * math.sin(ah - 2.2))], C.ACC)
for k in range(4):                     # all-round points (V-29): the heading's first
    a = ah + k * math.pi / 2
    cover(s, cx0 + 9 * math.cos(a), cy0 + 9 * math.sin(a), a)
unclip(s)
s.text(hx - 4, hy + 18, "heading", C.ACC, 11)
hint(s, FX + 186, FY + 136, [("height grid 2 m:", C.PEND), ("a foxhole will not form here", C.PEND)])
s.note(FX + 222, FY + 24, ["4 cover points all round", "(V-29) — not one facing", "the heading (G-36 is", "superseded) · crouch, 1 m"], C.SUB, 11)
s.note(FX, FY + FH + 16, ["The point tool (U-2: click, R = a quarter turn, drag turns).", "The amber line shows on a grid coarser than ~1.5 m (G-37);", "on a 1 m grid it is green: 1 m grid — a ≈ 3 × 3 m dip."], C.NOTE, 11)

# ═══════════════════ F — a crater's rim cover, greyed until the gases clear ═══════════════════
GX, GY, GW, GH = 850, 1300, 370, 170
s.text(GX, GY - 8, "F — context for the cover rule: a crater's rim, after 60 s (G-35)", C.SUB, 12)
s.rect(GX, GY, GW, GH, C.BG, stroke=C.BORDER)
clip(s, "gmap", GX + 1, GY + 1, GW - 2, GH - 2)
ground(s, GX, GY, GW, GH, 55)
kx, ky = GX + 110, GY + 92
s.raw(f"<circle cx='{kx}' cy='{ky}' r='52' fill='#5A4A30' fill-opacity='0.7'/><circle cx='{kx}' cy='{ky}' r='40' fill='#3A2E1E'/><circle cx='{kx}' cy='{ky}' r='20' fill='#221A10'/>")
for k in range(8):
    a = k * math.pi / 4
    cover(s, kx + 44 * math.cos(a), ky + 44 * math.sin(a), a + math.pi, dim=True)
unclip(s)
s.glyph(G["CLOCK"], GX + 196, GY + 30, C.PEND, 13); s.text(GX + 216, GY + 42, "cover after 60 s — 47 s left", C.PEND, 11.5)
s.note(GX + 196, GY + 66, ["rim points drawn greyed until", "BornAt + coverAfterSeconds;", "the EQS skips them before.", "A man already in it is hidden", "by the ground (TH-D)."], C.SUB, 11)
s.note(GX, GY + GH + 16, ["A crater is a runtime RECORD (P-4) or an authored one (V-14) — not", "an Earthworks shape. Drawn here only for the cover rule."], C.NOTE, 11)

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "ground-shaping-and-ditches.svg")
s.save(out)
print("written:", out)
