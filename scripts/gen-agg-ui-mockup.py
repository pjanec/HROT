#!/usr/bin/env python3
"""CE-8600 (exp/agg-ui, R-4143) — the STOCHASTIC UNIT's look: the unit form's Resolution section, the slot table in the unit's details, the 2-D
footprint (line and column, the unit symbol, the strength), losses on the map, the detection-levels debug overlay, the de- / re-aggregate request and
the process's progress. Output: docs/designs/aggregate-units/agg-ui-visuals.svg (a PNG for the chat via mockup_kit.render, never committed).

SCOPE (the caption says it too): this specifies ONLY what a stochastic unit adds — the Resolution section, the Slots tab, the footprint and its label rows,
the loss marks, the detection overlay and its layer row, the Resolution menu and the progress chip. The Details frame, the other tabs, the map, the
Tactical Map Layers panel's other rows and the context menu's other items are drawn for context and are UNCHANGED (R-4151).
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width, render

W, H = 1660, 1500
s = Svg(W, H, "Stochastic unit - the look (exp/agg-ui, CE-8600)",
        "Grey annotations are not drawn by the app. Scope: Resolution section, Slots tab, 2-D footprint + label rows, loss marks, detection overlay, Resolution menu + progress. Everything else is context, unchanged.")

FRIEND = "#0064FF"; HOSTILE = "#FF0000"           # ResolvedStyleConstants (Symbology §3.2)
FRIEND_FILL = "#80E0FF"; HOSTILE_FILL = "#FF8080"  # the 2525 frame fills (Symbology §3.8.11)
MAP = "#3C5A32"; MAP2 = "#46663A"; ROAD = "#B8A77A"; WOOD = "#2C4A26"
GREEN, AMBER, RED = "#5BD06A", "#F2C033", "#E65555"
DICE = 0xf522   # fa-dice
SPLIT = 0xf0e8  # fa-sitemap
MERGE = 0xf247  # fa-object-group
THUMB = 0xf08d  # fa-thumbtack


def panel_title(x, y, n, title):
    s.text(x, y, f"{n}  {title}", "#E8E8EE", 13.5, weight="bold")


# ───────────────────────── ① the Unit tab: the Resolution section ─────────────────────────
X0, Y0 = 20, 70
panel_title(X0, Y0, "①", "Details - Unit tab: the RESOLUTION section (ST-0, authored, saved)")
wx, wy, ww, wh = X0, Y0 + 12, 470, 470
s.window(wx, wy, ww, wh, "Details - 2 Plt / B Coy", chip=("Edit", C.TXT, C.AUTH))
s.tabs(wx + 6, wy + 26, ww - 12, [("Unit", FA.USER, C.BLUE), ("Slots", FA.LIST_CHECK, C.VIOLET), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)], 0)
cy = wy + 60; LW = 120; fx = wx + 12
s.separator_text(fx, cy, "Identity", wx + ww - 12); cy += 24
s.form_row(fx, cy, "Name", LW, "field", 320, "2 Plt / B Coy"); cy += 25
s.form_row(fx, cy, "Side", LW, "combo", 320, "Blue", glyph=FA.FLAG, gc=C.BLUE); cy += 30
# NEW: the Resolution section
s.rect(fx - 6, cy - 4, ww - 12, 196, "none", stroke=C.TEAL, rx=4, dash="4 3")
s.separator_text(fx, cy, "Resolution", wx + ww - 12); cy += 24
s.text(fx, cy + 17, "Resolution")
bx = fx + LW
for i, (g, gc, lab) in enumerate([(FA.USERS, C.BLUE, "Physical"), (FA.OBJECT_GROUP, C.TEAL, "Fusion"), (DICE, C.VIOLET, "Stochastic")]):
    s.glyph_toggle(bx + i * 34, cy, g, gc, i == 2)
s.text(bx + 108, cy + 17, "Stochastic", C.TXT)
s.text(bx + 180, cy + 17, "(type default: Physical)", C.DIS, 11.5)
cy += 32
s.form_row(fx, cy, "Echelon", LW, "field", 120, "Platoon", dis=True); s.text(fx + LW + 130, cy + 14, "from the type", C.DIS, 11.5); cy += 25
s.form_row(fx, cy, "Formation", LW, "combo", 120, "Line"); cy += 25
s.text(fx, cy + 14, "Frontage")
s.field(fx + LW, cy, 70, "200 m"); s.text(fx + LW + 78, cy + 14, "x  depth", C.SUB); s.field(fx + LW + 140, cy, 70, "80 m"); cy += 25
s.form_row(fx, cy, "Heading", LW, "field", 70, "045°"); s.text(fx + LW + 80, cy + 14, "drag the footprint's arrow on the map", C.DIS, 11.5); cy += 25
s.form_row(fx, cy, "Prepared", LW, "combo", 120, "Hasty"); cy += 34
s.separator_text(fx, cy, "State", wx + ww - 12); cy += 24
# Strength reads UnitStrength.Of — the slot table for a stochastic unit
# as built (CE-8601): read-only form rows (FormView draws a derived field disabled) — the SITREP's own words
s.form_row(fx, cy, "Strength", LW, "field", 320, "26 / 30 men  ·  Green", dis=True); cy += 25
s.form_row(fx, cy, "Ammunition", LW, "field", 320, "62 %  ·  Amber", dis=True); cy += 25
s.text(fx, cy + 14, "Vehicles"); s.text(fx + LW, cy + 14, "3 / 4 IFV operational", C.TXT)
s.callout(wx + ww, Y0 + 165, wx + ww + 18, Y0 + 150, ["NEW - a unit with", "AggregateUnit only;", "dashed = new rows"])
s.callout(wx + ww, Y0 + 400, wx + ww + 18, Y0 + 370, ["NEW for EVERY unit", "(physical too): men", "via UnitStrength.Of,", "ammo via AmmoOf"])

# ───────────────────────── ② the Slots tab ─────────────────────────
X1 = 650
panel_title(X1, Y0, "②", "Details - SLOTS tab: the slot table (ST-3, live, read-only)")
wx, wy, ww, wh = X1, Y0 + 12, 560, 470
s.window(wx, wy, ww, wh, "Details - 2 Plt / B Coy", chip=("Live", C.TXT, "#1E4A22"))
s.tabs(wx + 6, wy + 26, ww - 12, [("Unit", FA.USER, C.BLUE), ("Slots", FA.LIST_CHECK, C.VIOLET), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)], 1)
cy = wy + 60; fx = wx + 12
# unit-level gauges
for lab, val, col, txt in [("Suppressed", 0.35, AMBER, "35 % of the men under fire"), ("Morale", 0.78, GREEN, "2 slots broken"), ("Prepared", 0.33, C.INFO, "Hasty - 4 min in position")]:
    s.text(fx, cy + 14, lab); s.rect(fx + 100, cy + 3, 140, 13, C.FRAME); s.rect(fx + 100, cy + 3, 140 * val, 13, col); s.text(fx + 250, cy + 14, txt, C.SUB, 12); cy += 23
cy += 6
# the table header
cols = [("#", 0), ("Slot", 26), ("System", 150), ("Health", 250), ("Ammo", 340), ("Stress", 420), ("", 500)]
s.rect(fx - 4, cy, ww - 16, 22, C.HDR)
for name, ox in cols: s.text(fx + ox, cy + 15, name, C.SUB, 12)
cy += 24
rows = [
    ("1", "Plt leader", FA.CROWN, C.AMBER, "Rifle", 1.0, 0.7, 0.2, None),
    ("2", "1 Sqn / MG", FA.PERSON_RIFLE, C.BLUE, "MG", 0.8, 0.45, 0.55, "suppressed"),
    ("3", "1 Sqn / AT", FA.PERSON_RIFLE, C.BLUE, "ATGM", 1.0, 0.5, 0.3, None),
    ("4", "1 Sqn / rifle", FA.PERSON_RIFLE, C.BLUE, "Rifle", 0.0, 0.0, 0.0, "down"),
    ("5", "1 Sqn / rifle", FA.PERSON_RIFLE, C.BLUE, "Rifle", 0.4, 0.8, 1.0, "broken"),
    ("6", "IFV 1", FA.VAN, C.TEAL, "25 mm", 1.0, 0.6, None, None),
    ("7", "IFV 2", FA.VAN, C.TEAL, "25 mm", 0.0, 0.0, None, "K-kill"),
    ("…", "", None, None, "", None, None, None, None),
]
def bar(x, y, w, v, col):
    s.rect(x, y + 4, w, 11, C.FRAME); s.rect(x, y + 4, w * v, 11, col)
for r in rows:
    n, slot, g, gc, sysname, hp, ammo, stress, flag = r
    dim = flag in ("down", "K-kill")
    if n == "…": s.text(fx, cy + 14, "…  30 men + 4 IFV in 34 slots (chunks of 16)", C.DIS, 12); cy += 22; continue
    s.text(fx, cy + 14, n, C.DIS if dim else C.TXT, 12)
    if g: s.glyph(g, fx + 26, cy + 3, gc, 12, op=0.45 if dim else 1)
    s.text(fx + 44, cy + 14, slot, C.DIS if dim else C.TXT, 12)
    s.text(fx + 150, cy + 14, sysname, C.DIS if dim else C.SUB, 12)
    bar(fx + 250, cy, 76, hp, GREEN if hp >= 0.66 else AMBER if hp >= 0.33 else RED)
    bar(fx + 340, cy, 66, ammo, C.INFO)
    if stress is not None: bar(fx + 420, cy, 66, stress, AMBER if stress < 1 else RED)
    if flag:
        fc = RED if flag in ("down", "K-kill") else AMBER
        fg = FA.SKULL if flag == "down" else FA.FIRE if flag == "K-kill" else FA.WARNING
        s.glyph(fg, fx + 500, cy + 3, fc, 12); s.text(fx + 516, cy + 14, flag, fc, 11.5)
    cy += 22
s.note(fx, cy + 12, ["Rows are the unit's slot table (UnitSlotTable, chunked). A down slot stays listed - its wounds survive a split (C-3).",
                     "Read-only: losses come only from combat; the instructor's tools are the Unit tab's live actions (unchanged)."], C.DIS, 11)

# ───────────────────────── ③ the 2-D map: footprints, symbol, strength, losses, column ─────────────────────────
Y2 = 600
panel_title(X0, Y2, "③", "2-D map - the FOOTPRINT (ST-1), the label rows, losses (ST-3); ⛔ nothing in 3-D (the unit is hidden)")
mx, my, mw, mh = X0, Y2 + 12, 600, 470
s.rect(mx, my, mw, mh, MAP)
s.raw(f"<path d='M{mx} {my + 360} C{mx + 200} {my + 330} {mx + 420} {my + 420} {mx + mw} {my + 380}' stroke='{ROAD}' stroke-width='7' fill='none'/>")
s.raw(f"<ellipse cx='{mx + 470}' cy='{my + 110}' rx='120' ry='70' fill='{WOOD}'/>")


def footprint(cx, cy, w, d, hdg, edge, fill, dash):
    a = math.radians(hdg); ux, uy = math.sin(a), -math.cos(a); rx, ry = math.cos(a), math.sin(a)
    pts = []
    for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        pts.append((cx + rx * sx * w / 2 + ux * sy * d / 2, cy + ry * sx * w / 2 + uy * sy * d / 2))
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    s.raw(f"<polygon points='{p}' fill='{fill}' fill-opacity='0.22' stroke='{edge}' stroke-width='2' stroke-dasharray='{dash}'/>")
    # the heading arrow off the front edge
    fx_, fy_ = cx + ux * d / 2, cy + uy * d / 2
    s.raw(f"<line x1='{fx_:.1f}' y1='{fy_:.1f}' x2='{fx_ + ux * 22:.1f}' y2='{fy_ + uy * 22:.1f}' stroke='{edge}' stroke-width='2'/>")
    s.raw(f"<path d='M{fx_ + ux * 28:.1f} {fy_ + uy * 28:.1f} L{fx_ + ux * 18 + rx * 5:.1f} {fy_ + uy * 18 + ry * 5:.1f} L{fx_ + ux * 18 - rx * 5:.1f} {fy_ + uy * 18 - ry * 5:.1f} Z' fill='{edge}'/>")


def frame_symbol(cx, cy, side, echelon, mech=False):
    """AS BUILT (CE-8603, R-4146): the frame is a plain box in the 2525 side fill (the 2-D layer cannot draw a pixel-sized cross around a world point),
    the echelon marks ABOVE it, starting at the unit's point (the 2-D text has no x offset)."""
    fill = FRIEND_FILL if side == "F" else HOSTILE_FILL
    s.rect(cx - 18, cy - 12, 36, 24, fill, stroke="#000", sw=1.5)
    marks = {"squad": "·", "platoon": "···", "company": "I", "battalion": "II"}[echelon]
    s.text(cx, cy - 18, marks, "#000", 13, weight="bold")


def strength_row(cx, y, frac, label):
    """AS BUILT: one label row on a backdrop, starting at the unit's point, coloured by the SITREP strength (no bar)."""
    col = GREEN if frac >= 0.9 else AMBER if frac >= 0.7 else RED
    w = text_width(label, 11.5) + 10
    s.rect(cx, y + 2, w, 17, "#000", op=0.6, rx=3)
    s.text(cx + 5, y + 15, label, col, 11.5)


# a friend platoon in LINE (deployed): footprint + symbol + strength + losses
footprint(mx + 230, my + 190, 200, 80, 35, FRIEND, FRIEND, "6 4")
frame_symbol(mx + 230, my + 190, "F", "platoon", mech=True)
strength_row(mx + 230, my + 207, 26 / 30, "2 Plt · 26/30 · ~")
s.text(mx + 330, my + 150, "−2", RED, 13, weight="bold"); s.glyph(FA.SKULL, mx + 344, my + 139, RED, 11)
# a hostile company in line, in the wood
footprint(mx + 470, my + 110, 260, 90, 215, HOSTILE, HOSTILE, "6 4")
frame_symbol(mx + 470, my + 110, "H", "company")
strength_row(mx + 470, my + 132, 0.55, "3 Coy · 55/100 · ~")
s.text(mx + 380, my + 70, "−5", RED, 13, weight="bold"); s.glyph(FA.SKULL, mx + 396, my + 59, RED, 11)
# a friend company in COLUMN along the road: a segment of its path
pts = [(mx + 120, my + 352), (mx + 230, my + 343), (mx + 330, my + 360), (mx + 400, my + 378)]
path = "M" + " L".join(f"{x} {y}" for x, y in pts)
s.raw(f"<path d='{path}' stroke='{FRIEND}' stroke-width='14' stroke-opacity='0.35' fill='none' stroke-linecap='round'/>")
s.raw(f"<path d='{path}' stroke='{FRIEND}' stroke-width='2' stroke-dasharray='6 4' fill='none'/>")
frame_symbol(mx + 400, my + 378 - 30, "F", "company", mech=True)
s.line(mx + 400, my + 378 - 18, mx + 400, my + 378, FRIEND, 1.5)
strength_row(mx + 400, my + 378 - 50 + 60, 1.0, "A Coy · 100/100 · ~")
s.text(mx + 108, my + 340, "tail", C.SUB, 11); s.text(mx + 412, my + 395, "head", C.SUB, 11)
# a PHYSICAL squad for contrast (its men, the existing look)
for i, (dx, dy) in enumerate([(0, 0), (12, 6), (-12, 7), (24, 12), (-24, 13)]):
    s.raw(f"<circle cx='{mx + 120 + dx}' cy='{my + 100 + dy}' r='4.5' fill='{FRIEND}' stroke='#000' stroke-width='1'/>")
s.text(mx + 120, my + 135, "1 Sqn (physical - its men, unchanged)", "#FFFFFF", 11, "middle")
# annotations
s.callout(mx + 330, my + 200, mx + mw + 12, my + 175, ["FOOTPRINT: width x depth x heading", "(AggregateUnit), dashed = stochastic", "side colour fill 22 %, arrow = heading"])
s.callout(mx + 260, my + 230, mx + mw + 12, my + 225, ["label rows: strength bar (UnitStrength),", "name · present/authorized · ~ = stochastic"])
s.callout(mx + 340, my + 150, mx + mw + 12, my + 270, ["LOSS MARK: −n + skull, at the footprint's", "edge, fades in 4 s (ST-3)"])
s.callout(mx + 330, my + 362, mx + mw + 12, my + 320, ["COLUMN: a band along its path,", "head to tail = Depth (M-3)"])
s.note(mx + mw + 12, my + 380, ["⛔ 3-D: nothing - no bodies (the unit stays", "hidden; the 3-D map draws no footprint)", "", "zoomed out (ST-4): footprints + frames only,", "label rows drop below 40 px per unit"], C.SUB, 11.5)

# ───────────────────────── ④ detection overlay ─────────────────────────
Y3 = 1110
panel_title(X0, Y3, "④", "Debug overlay - DETECTION LEVELS (ST-2): Tactical Map Layers ▸ AI ▸ Detection levels")
mx, my, mw, mh = X0, Y3 + 12, 560, 330
s.rect(mx, my, mw, mh, MAP)
footprint(mx + 140, my + 220, 140, 60, 20, FRIEND, FRIEND, "6 4"); frame_symbol(mx + 140, my + 220, "F", "platoon")
footprint(mx + 430, my + 90, 160, 60, 200, HOSTILE, HOSTILE, "6 4"); frame_symbol(mx + 430, my + 90, "H", "platoon")
footprint(mx + 450, my + 260, 120, 50, 250, HOSTILE, HOSTILE, "6 4"); frame_symbol(mx + 450, my + 260, "H", "squad")


def level_line(x1, y1, x2, y2, lvl, state):
    col = {"contact": GREEN, "building": AMBER, "fading": C.GREY}[state]
    s.raw(f"<line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='{col}' stroke-width='2' stroke-dasharray='{'none' if state == 'contact' else '5 4'}'/>")
    mxm, mym = (x1 + x2) / 2, (y1 + y2) / 2
    s.rect(mxm - 34, mym - 10, 68, 20, "#000000", op=0.75, rx=3)
    for i in range(5):
        s.rect(mxm - 30 + i * 12, mym - 5, 10, 10, col if i < lvl else "#333")
    return mxm, mym


m1 = level_line(mx + 160, my + 205, mx + 415, my + 105, 5, "contact")
m2 = level_line(mx + 165, my + 225, mx + 440, my + 255, 2, "building")
m3 = level_line(mx + 420, my + 110, mx + 155, my + 215, 3, "fading")
s.callout(m1[0] + 34, m1[1], mx + mw + 12, my + 40, ["observer → target, the pair's LEVEL (N-5):", "5 boxes = the threshold - green = CONTACT", "reported (in the squad contact pool, N-2)"])
s.callout(m2[0] + 34, m2[1], mx + mw + 12, my + 110, ["amber = accumulating (this step's Pd added)"])
s.callout(m3[0] - 30, m3[1] + 10, mx + mw + 12, my + 150, ["grey = fading (out of sight)"])
s.note(mx + mw + 12, my + 190, ["hover a line: Pd this step, range, LOS samples", "x / 4, woods, posture, signature", "",
                               "a layer row (AI group), 2-D only, off by default;", "\"selected only\" shows just the selected", "unit's pairs (the gizmo family switch)"], C.SUB, 11.5)
# the layers panel row (context: the panel)
lx, ly = mx + mw + 300, my + 190
s.window(lx, ly, 300, 130, "Tactical Map Layers")
s.header(lx + 6, ly + 26, 288, "AI", FA.BRAIN, C.PINK)
s.checkbox(lx + 14, ly + 52, True); s.text(lx + 40, ly + 66, "Brain state", C.TXT)
s.rect(lx + 8, ly + 76, 284, 24, "none", stroke=C.TEAL, rx=3, dash="4 3")
s.checkbox(lx + 14, ly + 79, True); s.glyph(DICE, lx + 40, ly + 82, C.VIOLET, 12); s.text(lx + 58, ly + 93, "Detection levels (stochastic)", C.TXT)
s.checkbox(lx + 14, ly + 104, False); s.text(lx + 40, ly + 118, "Sensors", C.TXT)

# ───────────────────────── ⑤ de- / re-aggregate request + progress ─────────────────────────
X5, Y5 = 900, 600
panel_title(X5, Y5, "⑤", "De- / re-aggregate: the request (right-click, ST-5) and the PROGRESS")
mx, my = X5, Y5 + 12
s.rect(mx, my, 640, 470, MAP)
footprint(mx + 150, my + 120, 200, 80, 35, FRIEND, FRIEND, "6 4"); frame_symbol(mx + 150, my + 120, "F", "platoon", mech=True)
strength_row(mx + 150, my + 137, 26 / 30, "2 Plt · 26/30 · ~")
# context menu (context: its other items unchanged)
menu_items = [("Orders", None, FA.ROUTE, C.GREEN), ("Designate", None, FA.CROSSHAIRS, C.ORANGE), "---",
              ("Resolution", None, DICE, C.VIOLET), "---",
              ("Select", "Space", None, None), ("Properties…", None, None, None), "---",
              ("Copy", "Ctrl+C", FA.COPY, C.GREY), ("Rotate", "R", FA.ARROWS_ROTATE, C.GREY), "---", ("Delete", "Delete", FA.TRASH, C.RED)]
s.menu(mx + 250, my + 40, 200, menu_items)
ry_ = my + 44 + 22 * 2 + 9
s.rect(mx + 252, ry_, 196, 22, "#2C3A52", op=0.8)
s.glyph(DICE, mx + 260, ry_ + 4, C.VIOLET, 12.5); s.text(mx + 280, ry_ + 15, "Resolution"); s.text(mx + 440, ry_ + 15, "›", C.TXT, 13, "end")
s.rect(mx + 248, ry_ - 2, 204, 26, "none", stroke=C.TEAL, rx=3, dash="4 3")
sub = [("#", "now: stochastic · not pinned"),
       ("Make physical now", None, SPLIT, C.BLUE),
       ("Make fusion now", None, FA.OBJECT_GROUP, C.TEAL, "no fusion type"),
       ("Aggregate now", None, MERGE, C.VIOLET, "already stochastic"), "---",
       ("Pin - keep as it is", None, THUMB, C.AMBER),
       ("Unpin (policy decides)", None, FA.XMARK, C.GREY, "not pinned")]
s.menu(mx + 452, ry_, 180, sub)
s.callout(mx + 452, my + 290, mx + 20, my + 300, ["NEW group (dashed): the instructor's request - SV-14: at once, an instructor order",
                                                   "wins over the policy; writes a ResolutionPin. The menu's other items are unchanged."])

# progress: on the map label and in the Unit tab
px, py = mx + 20, my + 340
s.text(px, py, "while it splits - the map label gains a PROGRESS chip:", C.SUB, 12)
py += 12
footprint(px + 130, py + 60, 200, 70, 0, FRIEND, FRIEND, "6 4"); frame_symbol(px + 130, py + 60, "F", "platoon", mech=True)
cx = px + 130; chip_y = py + 78
s.rect(cx - 70, chip_y, 140, 18, "#000", op=0.75, rx=9)
s.glyph(SPLIT, cx - 63, chip_y + 3, C.BLUE, 11)
s.rect(cx - 45, chip_y + 6, 70, 6, "#333"); s.rect(cx - 45, chip_y + 6, 70 * 0.66, 6, C.BLUE)
s.text(cx + 30, chip_y + 13, "2/3", "#FFF", 11)
for i, (dx, dy) in enumerate([(-60, -10), (-48, -6), (-36, -10)]):
    s.raw(f"<circle cx='{cx + dx}' cy='{py + 60 + dy}' r='4' fill='none' stroke='{FRIEND}' stroke-width='1.5' stroke-dasharray='2 2'/>")
s.callout(cx - 60, py + 50, px + 300, py + 30, ["the men already MATERIALISED but HELD", "(dashed rings, D-P4) - not in the world yet"])
s.callout(cx + 70, chip_y + 9, px + 300, py + 70, ["chip: stage glyph · progress (leaves with posts)", "· n/m - gone after the Handover tick"])

# the Unit tab's progress rows
ux, uy = X5, Y5 + 500
s.window(ux, uy, 640, 150, "Details - 2 Plt / B Coy", chip=("Live", C.TXT, "#1E4A22"))
s.separator_text(ux + 12, uy + 28, "Resolution", ux + 628)
s.text(ux + 12, uy + 66, "Resolution"); s.glyph(DICE, ux + 132, uy + 54, C.VIOLET, 12); s.text(ux + 150, uy + 66, "Stochastic  →", C.TXT); s.glyph(FA.USERS, ux + 248, uy + 54, C.BLUE, 12); s.text(ux + 266, uy + 66, "Physical", C.TXT)
s.glyph(THUMB, ux + 340, uy + 54, C.AMBER, 12); s.text(ux + 358, uy + 66, "pinned by the instructor", C.AMBER, 12)
s.text(ux + 12, uy + 92, "Splitting")
stages = [("Planning", True), ("Materialising", True), ("Handover", False)]
sx = ux + 132
for i, (st, done) in enumerate(stages):
    col = C.BLUE if done else "#444"
    s.rect(sx, uy + 80, 120, 18, col, rx=3, op=0.9 if done else 1); s.text(sx + 60, uy + 93, st, "#FFF" if done else C.DIS, 11.5, "middle")
    if i < 2: s.text(sx + 124, uy + 93, "›", C.DIS, 13)
    sx += 132
s.text(ux + 12, uy + 118, "Progress"); s.text(ux + 132, uy + 118, "2 of 3 squads posted  ·  19 of 26 men created  ·  deadline 14 s", C.TXT, 12)
s.button(ux + 132, uy + 124, 90, "Cancel", FA.XMARK, C.RED)

s.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "designs", "aggregate-units", "agg-ui-visuals.svg"))
if len(sys.argv) > 1: render(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "designs", "aggregate-units", "agg-ui-visuals.svg"), sys.argv[1], W, H)
