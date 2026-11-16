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

W, H = 1660, 3300
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
# ⭐ the GAUGE rows (UI-10, user 2026-11-16: "more colored and visualized ... not so ugly as basic imgui text field"): a bar in the status colour
#   with the numbers ON it, the SITREP word as a coloured chip with its glyph (never colour alone, Details forms §6.3), the thresholds as ticks
def gauge(x, y, w, frac, fill, text, chip, chip_col, chip_glyph, ticks=()):
    s.rect(x, y, w, 19, C.FRAME, rx=2)
    s.rect(x, y, w * frac, 19, fill, rx=2, op=0.9)
    for t in ticks: s.line(x + w * t, y - 2, x + w * t, y + 21, "#E8E8EE", 1)
    s.text(x + 6, y + 14, text, "#0F0F0F" if frac > 0.35 else C.TXT, 12.5, weight="bold")
    cw = text_width(chip, 11) + 26
    s.rect(x + w + 8, y + 1, cw, 17, "#1C1C22", stroke=chip_col, rx=8, sw=1.2)
    s.glyph(chip_glyph, x + w + 14, y + 3.5, chip_col, 11)
    s.text(x + w + 29, y + 14, chip, chip_col, 11, weight="bold")
s.text(fx, cy + 14, "Strength")
gauge(fx + LW, cy, 210, 26 / 30, AMBER, "26 / 30 men", "AMBER", AMBER, FA.WARNING, ticks=(0.7, 0.9)); cy += 27
s.text(fx, cy + 14, "Ammunition")
gauge(fx + LW, cy, 210, 0.62, GREEN, "62 %  ·  1 860 rds", "GREEN", GREEN, FA.CIRCLE_CHECK, ticks=(0.25, 0.5)); cy += 27
s.text(fx, cy + 14, "Vehicles")
for i in range(4):
    s.glyph(FA.VAN, fx + LW + i * 22, cy + 2, C.TEAL if i < 3 else RED, 14, op=1 if i < 3 else 0.8)
s.glyph(FA.XMARK, fx + LW + 3 * 22 + 4, cy + 3, RED, 9)
s.text(fx + LW + 96, cy + 14, "3 / 4 operational", C.TXT)
s.callout(wx + ww, Y0 + 165, wx + ww + 18, Y0 + 150, ["NEW - a unit with", "AggregateUnit only;", "dashed = new rows"])
s.callout(wx + ww - 120, Y0 + 452, wx + ww + 18, Y0 + 370, ["GAUGES (UI-10):", "SITREP-colour bar,", "numbers on it,", "threshold ticks,", "word + glyph chip"])

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


UNKNOWN_FILL = "#FFFF80"; NEUTRAL_FILL = "#AAFFAA"


def nato(cx, cy, aff, arm, ech, w=34, h=22, sw=1.4):
    """⭐ UI-12 — the composed APP-6 / 2525 land-unit symbol, centre (cx, cy): the FRAME by affiliation (friend rectangle, hostile diamond, neutral square,
    unknown quatrefoil) in the standard's pale fill, the ARM icon inside, the ECHELON marks above. Screen pixels — the same geometry in the card, alone on the
    map and in ImGui panels."""
    fill = {"F": FRIEND_FILL, "H": HOSTILE_FILL, "N": NEUTRAL_FILL, "U": UNKNOWN_FILL}[aff]
    if aff == "F":
        s.rect(cx - w / 2, cy - h / 2, w, h, fill, stroke="#000", sw=sw); bx, by = w / 2, h / 2
    elif aff == "H":
        r = h * 0.8
        s.raw(f"<polygon points='{cx},{cy - r} {cx + r},{cy} {cx},{cy + r} {cx - r},{cy}' fill='{fill}' stroke='#000' stroke-width='{sw}'/>"); bx, by = r * 0.5, r * 0.5
    elif aff == "N":
        s.rect(cx - h / 2 - 2, cy - h / 2 - 2, h + 4, h + 4, fill, stroke="#000", sw=sw); bx, by = h / 2, h / 2
    else:
        r = h * 0.36
        s.raw("".join(f"<circle cx='{cx + dx}' cy='{cy + dy}' r='{r}' fill='{fill}' stroke='#000' stroke-width='{sw}'/>" for dx, dy in ((0, -r), (r, 0), (0, r), (-r, 0))))
        s.raw(f"<circle cx='{cx}' cy='{cy}' r='{r * 1.3}' fill='{fill}'/>"); bx, by = r, r
    k = "#000"
    if arm in ("inf", "mech"):
        s.line(cx - bx, cy - by, cx + bx, cy + by, k, sw); s.line(cx - bx, cy + by, cx + bx, cy - by, k, sw)
    if arm in ("armour", "mech"):
        s.raw(f"<ellipse cx='{cx}' cy='{cy}' rx='{bx * 0.62}' ry='{by * 0.55}' fill='none' stroke='{k}' stroke-width='{sw}'/>")
    if arm == "recon": s.line(cx - bx, cy + by, cx + bx, cy - by, k, sw)
    if arm == "arty": s.raw(f"<circle cx='{cx}' cy='{cy}' r='{by * 0.38}' fill='{k}'/>")
    top = cy - (h * 0.8 if aff == "H" else h / 2 + (2 if aff == "N" else 0) if aff != "U" else h * 0.72)
    if h < 15: return   # a row-sized symbol (the ORBAT): no echelon marks - the tree's depth says it
    marks = {"team": "Ø", "squad": "•", "section": "••", "platoon": "•••", "company": "|", "battalion": "||"}[ech]
    s.text(cx, top - 2, marks, "#000" if aff != "H" else "#000", 11 if ech not in ("company", "battalion") else 12, "middle", "bold")


CARD_W = 156


def card(x, y, side, name, frac, ammo, info=(), alarm=None, sym=None):
    """THE CARD (UI-11): one per unit, the SAME card as a soldier's — row 10 is the STRENGTH bar (in place of health), row 13 a thin ammo bar, row 20 the
    name (unchanged), row 64 ONE info line: present/authorized in the strength colour · what it does · one alarm. ≤ 3 lines. (x, y) = the card's top-left."""
    edge = FRIEND if side == "F" else HOSTILE
    col = GREEN if frac >= 0.9 else AMBER if frac >= 0.7 else RED
    acol = GREEN if ammo >= 0.5 else AMBER if ammo >= 0.25 else RED
    h = 46 if not info and not alarm else 60
    sx = 0
    if sym:   # ⭐ UI-12 — the symbol CELL, left of the rows, spanning them; the rows shift right
        sx = 46
        s.rect(x, y, CARD_W + sx, h, "#000", op=0.6, rx=2); s.rect(x, y, CARD_W + sx, h, "none", stroke=edge, rx=2, sw=1)
        s.rect(x + 3, y + 3, sx - 4, h - 6, "#FFFFFF", op=0.10, rx=2)
        nato(x + sx / 2 + 1, y + h / 2 + 5, *sym, w=32, h=20)
        x += sx
    else:
        s.rect(x, y, CARD_W, h, "#000", op=0.6, rx=2); s.rect(x, y, CARD_W, h, "none", stroke=edge, rx=2, sw=1)
    s.rect(x + 5, y + 5, CARD_W - 10, 6, "#3A3A3A"); s.rect(x + 5, y + 5, (CARD_W - 10) * frac, 6, col)
    for t in (0.7, 0.9): s.line(x + 5 + (CARD_W - 10) * t, y + 4, x + 5 + (CARD_W - 10) * t, y + 12, "#DDD", 1)
    s.rect(x + 5, y + 13, CARD_W - 10, 3, "#3A3A3A"); s.rect(x + 5, y + 13, (CARD_W - 10) * ammo, 3, acol)
    s.text(x + 6, y + 31, name, "#FFFFFF", 12.5, weight="bold")
    s.text(x + CARD_W - 6, y + 31, "~", C.VIOLET, 13, "end", weight="bold")
    if info or alarm:
        tx = x + 6
        for txt, c in info:
            s.text(tx, y + 50, txt, c, 11.5); tx += text_width(txt, 11.5) + 4
        if alarm:
            g, c, txt = alarm
            s.glyph(g, tx + 2, y + 41, c, 10.5); s.text(tx + 16, y + 50, txt, c, 11.5, weight="bold")
    return h


# a friend platoon in LINE (deployed): footprint + symbol + strength + losses
footprint(mx + 230, my + 190, 200, 80, 35, FRIEND, FRIEND, "6 4")
s.raw(f"<circle cx='{mx + 230}' cy='{my + 190}' r='3.5' fill='{FRIEND}' stroke='#000'/>"); s.line(mx + 268, my + 210, mx + 230, my + 190, FRIEND, 1.2)
card(mx + 245, my + 150, "F", "2 Plt", 26 / 30, 0.62, [("26/30", AMBER), ("· Defend", C.TEAL)], sym=("F", "mech", "platoon"))
s.text(mx + 330, my + 128, "−2", RED, 13, weight="bold"); s.glyph(FA.SKULL, mx + 344, my + 117, RED, 11)
# a hostile company in line, in the wood
footprint(mx + 470, my + 110, 260, 90, 215, HOSTILE, HOSTILE, "6 4")
s.raw(f"<circle cx='{mx + 470}' cy='{my + 110}' r='3.5' fill='{HOSTILE}' stroke='#000'/>"); s.line(mx + 443, my + 90, mx + 470, my + 110, HOSTILE, 1.2)
card(mx + 420, my + 30, "H", "3 Coy", 0.55, 0.3, [("55/100", RED)], alarm=(FA.WARNING, AMBER, "supp 60%"), sym=("H", "inf", "company"))
s.text(mx + 380, my + 70, "−5", RED, 13, weight="bold"); s.glyph(FA.SKULL, mx + 396, my + 59, RED, 11)
# a friend company in COLUMN along the road: a segment of its path
pts = [(mx + 120, my + 352), (mx + 230, my + 343), (mx + 330, my + 360), (mx + 400, my + 378)]
path = "M" + " L".join(f"{x} {y}" for x, y in pts)
s.raw(f"<path d='{path}' stroke='{FRIEND}' stroke-width='14' stroke-opacity='0.35' fill='none' stroke-linecap='round'/>")
s.raw(f"<path d='{path}' stroke='{FRIEND}' stroke-width='2' stroke-dasharray='6 4' fill='none'/>")
s.raw(f"<circle cx='{mx + 400}' cy='{my + 378}' r='3.5' fill='{FRIEND}' stroke='#000'/>"); s.line(mx + 433, my + 350, mx + 400, my + 378, FRIEND, 1.2)
card(mx + 410, my + 290, "F", "A Coy", 1.0, 0.95, [("100/100", GREEN), ("· March", C.TEAL)], sym=("F", "armour", "company"))
s.text(mx + 108, my + 340, "tail", C.SUB, 11); s.text(mx + 412, my + 395, "head", C.SUB, 11)
# a PHYSICAL squad for contrast (its men, the existing look)
for i, (dx, dy) in enumerate([(0, 0), (12, 6), (-12, 7), (24, 12), (-24, 13)]):
    s.raw(f"<circle cx='{mx + 120 + dx}' cy='{my + 100 + dy}' r='4.5' fill='{FRIEND}' stroke='#000' stroke-width='1'/>")
s.text(mx + 120, my + 135, "1 Sqn (physical - its men, unchanged)", "#FFFFFF", 11, "middle")
# annotations
s.callout(mx + 330, my + 200, mx + mw + 12, my + 175, ["FOOTPRINT: width x depth x heading", "(AggregateUnit), dashed = stochastic", "side colour fill 22 %, arrow = heading"])
s.callout(mx + 260, my + 230, mx + mw + 12, my + 225, ["the unit's CARD (⑥): strength bar in place", "of health, ammo, name, one info line"])
s.callout(mx + 340, my + 128, mx + mw + 12, my + 270, ["LOSS MARK: −n + skull, at the footprint's", "edge, fades in 4 s (ST-3)"])
s.callout(mx + 330, my + 362, mx + mw + 12, my + 320, ["COLUMN: a band along its path,", "head to tail = Depth (M-3)"])
s.note(mx + mw + 12, my + 380, ["⛔ 3-D: nothing - no bodies (the unit stays", "hidden; the 3-D map draws no footprint)", "", "zoomed out (ST-4): footprints + symbols only (⑦),", "cards drop below 40 px per unit"], C.SUB, 11.5)

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
card(mx + 60, my + 160, "F", "2 Plt", 26 / 30, 0.62)
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

# ───────────────────────── ⑥ the CARD ─────────────────────────
Y6 = 1500
panel_title(X0, Y6, "⑥", "The unit's CARD (2-D and 3-D, the Labels layer): the NATO SYMBOL on the left (UI-12), STRENGTH in place of health - 3 lines max")
mx, my = X0, Y6 + 12
s.rect(mx, my, 1600, 330, MAP)
# context: a soldier's card as it is today (health bar + name)
s.rect(mx + 30, my + 40, 110, 32, "#000", op=0.6, rx=2); s.rect(mx + 30, my + 40, 110, 32, "none", stroke=FRIEND, rx=2)
s.rect(mx + 35, my + 45, 100, 5, "#3A3A3A"); s.rect(mx + 35, my + 45, 80, 5, GREEN)
s.text(mx + 36, my + 66, "Pvt Novak", "#FFF", 12.5, weight="bold")
s.raw(f"<circle cx='{mx + 85}' cy='{my + 95}' r='5' fill='{FRIEND}' stroke='#000'/>")
s.note(mx + 30, my + 130, ["context, unchanged:", "a soldier's card -", "health bar + name"], C.SUB)
states = [
    ("healthy, defending", "F", "2 Plt", 0.97, 0.9, [("29/30", GREEN), ("· Defend", C.TEAL), ("· Fortified", C.INFO)], None, ("F", "mech", "platoon")),
    ("losses, suppressed", "F", "2 Plt", 26 / 30, 0.62, [("26/30", AMBER), ("· Attack", C.TEAL)], (FA.WARNING, AMBER, "supp 35%"), ("F", "mech", "platoon")),
    ("breaking, low ammo", "H", "3 Coy", 0.55, 0.18, [("55/100", RED)], (FA.SKULL, RED, "BROKEN 12"), ("H", "inf", "company")),
    ("marching, quiet", "F", "A Coy", 1.0, 1.0, [], None, ("F", "armour", "company")),
    ("splitting (ST-5)", "F", "2 Plt", 26 / 30, 0.62, [("26/30", AMBER)], (SPLIT, C.BLUE, "split 2/3"), ("F", "mech", "platoon")),
]
x = mx + 200
for title, side, name, f, a, info, alarm, sym in states:
    s.text(x, my + 28, title, C.SUB, 11.5)
    h_ = card(x, my + 40, side, name, f, a, info, alarm, sym)
    ax = x + 23
    s.line(ax, my + 40 + h_, ax, my + 125, FRIEND if side == "F" else HOSTILE, 1.2)   # the card's leader line to the unit's point
    s.raw(f"<circle cx='{ax}' cy='{my + 127}' r='3' fill='{FRIEND if side == 'F' else HOSTILE}'/>")
    x += CARD_W + 46 + 34
s.callout(mx + 300, my + 102, mx + 200, my + 190, ["line 1 - STRENGTH bar (row 10, where a man's health is): present / authorized in the SITREP colour",
                                                     "(green >= 90 %, amber >= 70 %, red); ticks at 70 / 90 %; a thin AMMO bar under it (green >= 50 %, amber >= 25 %, red)"])
s.callout(mx + 330, my + 102, mx + 200, my + 230, ["line 2 - the NAME (row 20, unchanged) + ~ in violet = not physical"])
s.callout(mx + 360, my + 102, mx + 200, my + 258, ["line 3 - ONE info line (row 64): present/authorized in the strength colour · what it does (teal) · the prepared level",
                                                     "(blue) or ONE alarm, the worst first: BROKEN n (red, skull) > supp nn% (amber) > out of ammo > split n/m; nothing when quiet"])
s.note(mx + 200, my + 300, ["Same card machinery and Labels layer as every entity (one card per unit) - in 3-D too, the only place a stochastic unit shows there. Hidden behind terrain like every card."], C.SUB)

s.callout(mx + 1023, my + 102, mx + 1160, my + 190, ["the SYMBOL cell (UI-12): APP-6 / 2525 frame by side,", "the arm icon, echelon marks above - spans the", "3 lines; the leader line drops from its centre", "to the unit's point"])

# ───────────────────────── ⑦ the symbols ─────────────────────────
Y7 = 1860
panel_title(X0, Y7, "⑦", "NATO symbols (UI-12): ONE composed renderer - in the card, ALONE on the map when there is no card, in ImGui panels")
mx, my = X0, Y7 + 12
s.rect(mx, my, 760, 420, "#1C1C22", stroke=C.BORDER)
s.text(mx + 14, my + 26, "the vocabulary this programme needs (land units, 2525C function ids, derived - never hand-authored):", C.SUB, 12)
rows = [("Infantry", "inf", "UCI"), ("Mech. infantry", "mech", "UCIZ"), ("Armour", "armour", "UCA"), ("Reconnaissance", "recon", "UCR"), ("Artillery", "arty", "UCF")]
cols = [("F", "friend"), ("H", "hostile"), ("N", "neutral"), ("U", "unknown")]
for j, (aff, lab) in enumerate(cols): s.text(mx + 220 + j * 90, my + 56, lab, C.SUB, 11.5, "middle")
for i, (lab, arm, code) in enumerate(rows):
    y = my + 92 + i * 52
    s.text(mx + 14, y + 5, lab, C.TXT, 12.5); s.text(mx + 128, y + 5, code, C.DIS, 11.5)
    for j, (aff, _) in enumerate(cols): nato(mx + 220 + j * 90, y, aff, arm, "platoon")
s.text(mx + 650, my + 56, "echelon (above the frame)", C.SUB, 11.5, "middle")
for i, (e, n) in enumerate([("team", "team"), ("squad", "squad"), ("section", "section"), ("platoon", "platoon"), ("company", "company / battery"), ("battalion", "battalion")]):
    y = my + 92 + i * 46
    nato(mx + 600, y, "F", "inf", e, w=30, h=19); s.text(mx + 632, y + 5, n, C.TXT, 12)
s.note(mx + 14, my + 380, ["The SIDC is DERIVED: side (EntityInfo) + arm and echelon (the unit's DIS type, UnitDis) + mounted (AggregateUnit.Posture); a TKB-authored",
                          "SIDC (Symbology §3.8.3c, TextureName) overrides it. Not shown: the stochastic marker - the standard has none; the card's ~ carries it."], C.SUB, 11.5)

# the zoomed-out picture: symbols alone (no cards)
zx, zy = mx + 790, my
s.rect(zx, zy, 810, 420, MAP)
s.text(zx + 14, zy + 24, "zoomed out or Labels off: the SAME symbol ALONE at the unit's point - ONE echelon level per zoom (here companies)", "#FFF", 12)
for (px, py, w_, d_, hd, aff, arm, ech) in [(160, 150, 120, 50, 35, "F", "mech", "company"), (300, 220, 110, 40, 60, "F", "inf", "company"), (190, 300, 90, 40, 20, "F", "armour", "company"),
                                            (560, 140, 150, 55, 215, "H", "inf", "company"), (620, 260, 140, 50, 230, "H", "inf", "company"), (690, 350, 90, 40, 200, "H", "arty", "company")]:
    footprint(zx + px, zy + py, w_, d_, hd, FRIEND if aff == "F" else HOSTILE, FRIEND if aff == "F" else HOSTILE, "6 4")
    nato(zx + px, zy + py, aff, arm, ech, w=30, h=19)
s.note(zx + 14, zy + 395, ["a battalion against a battalion: the frames and the footprints carry the picture; a card returns on hover / selection or when zoomed in"], C.SUB, 11.5)

# ───────────────────────── ⑧ derived from the content ─────────────────────────
Y8 = 2330
panel_title(X0, Y8, "⑧", "PROCEDURAL: the symbol is DERIVED from what the unit is made of (UI-12) - vectors, no images, nothing hand-drawn per unit")
mx, my = X0, Y8 + 12
s.rect(mx, my, 1600, 420, "#1C1C22", stroke=C.BORDER)


def mobility(cx, cy, kind, w=34, h=22):
    """2525 mobility indicator under the frame: tracked = an oval band, wheeled = circles."""
    y = cy + h / 2 + 5
    if kind == "tracked": s.raw(f"<rect x='{cx - w * 0.42}' y='{y - 2}' width='{w * 0.84}' height='6' rx='3' fill='none' stroke='#000' stroke-width='1.4'/>")
    if kind == "wheeled": s.raw("".join(f"<circle cx='{cx + dx}' cy='{y + 1}' r='2.6' fill='none' stroke='#000' stroke-width='1.3'/>" for dx in (-9, 0, 9)))


examples = [
    ("2 Plt / B Coy", ["30 men (rifles, 3 MG)", "4 IFV - tracked, 25 mm", "2 ATGM teams"], "IFVs carry the men  ⇒  MECH. INFANTRY", "tracked", ("F", "mech", "platoon"), "AT"),
    ("1 Plt / C Coy", ["28 men (rifles, 2 MG)", "3 trucks - wheeled", ""], "trucks carry the men  ⇒  MOTORISED infantry", "wheeled", ("F", "inf", "platoon"), None),
    ("A Coy / 1 Tk Bn", ["14 tanks - tracked", "1 IFV (HQ)", ""], "tanks dominate the weight  ⇒  ARMOUR", "tracked", ("F", "armour", "company"), None),
    ("3 Coy (enemy)", ["90 men (rifles, MG)", "6 mortars 81 mm", "no vehicles"], "men dominate  ⇒  INFANTRY, mortars as a secondary mark", None, ("H", "inf", "company"), "M"),
]
x = mx + 20
for name, content, rule, mob, sym, mod in examples:
    s.text(x, my + 30, name, C.TXT, 13, weight="bold")
    s.rect(x, my + 40, 360, 70, "#141418", stroke=C.SEP, rx=3)
    s.text(x + 10, my + 58, "UnitHoldings + slot roles (the content):", C.NOTE, 11)
    for i, c in enumerate(content): s.text(x + 10, my + 75 + i * 14, c, C.SUB, 11.5)
    s.text(x + 180, my + 132, "⇩ rule", C.NOTE, 11.5, "middle")
    s.text(x, my + 160, rule, C.TEAL, 12)
    cx, cy = x + 60, my + 225
    nato(cx, cy, *sym, w=46, h=30, sw=1.8)
    if mob: mobility(cx, cy, mob, w=46, h=30)
    if mod:   # a secondary capability: a small mark in the frame's lower right (2525 sector modifier)
        s.rect(cx + 27, cy + 4, text_width(mod, 10.5) + 8, 15, "#FFFFFF", rx=2); s.text(cx + 31, cy + 15, mod, "#000", 10.5, weight="bold")
    s.text(cx + 62, cy - 4, "drawn: frame (side) + icon (role)", C.SUB, 11)
    s.text(cx + 62, cy + 12, "+ mobility (holdings) + echelon", C.SUB, 11)
    s.text(cx + 62, cy + 28, "+ secondary mark (the box)" if mod else "", C.SUB, 11)
    x += 395
s.note(mx + 20, my + 300, [
    "⭐ The TKB gains ONE field per system type: SymbolRole (infantry · infantry-carrier · armour · anti-armour · mortar · artillery · recon · HQ · support) and its",
    "mobility (wheeled / tracked, already in UnitHolding.Mobility). Unset ⇒ derived from the type's existing TKB data (a tank platform ⇒ armour, a rifle ⇒ infantry).",
    "⭐ The rule: weigh the AUTHORED content by role ⇒ the dominant role is the ICON; carriers + men ⇒ mechanised (tracked) / motorised (wheeled); a second role",
    "above a share ⇒ the secondary MARK; echelon from AggregateUnit.Echelon / the DIS type. ⚠ From what the unit WAS MADE OF, not what it has left - losing its",
    "IFVs does not turn a mechanised platoon into infantry (identity is stable; the losses are the card's). The same deriver serves a physical unit from its roster.",
], C.SUB, 12)

# ───────────────────────── ⑨ the ORBAT and the layer panel ─────────────────────────
Y9 = 2790
panel_title(X0, Y9, "⑨", "ORBAT rows by WHAT the entity is (UI-13) · the layer panel's NATO row (UI-14)")
ox, oy = X0, Y9 + 12
s.window(ox, oy, 560, 470, "ORBAT")
for i, (g, c) in enumerate([(FA.SITEMAP, C.TEAL), (FA.CUBES, C.ORANGE), (FA.PUZZLE, C.VIOLET)]): s.glyph_toggle(ox + 10 + i * 34, oy + 28, g, c, i == 0)
s.text(ox + 120, oy + 46, "Command  (context: the relation switch, unchanged)", C.DIS, 11)


def sym_icon(x, y, aff, arm, ech):
    nato(x + 11, y + 10, aff, arm, ech, w=20, h=13, sw=1.1)


rows = [  # depth, chevron, icon-kind, label, sub, dim
    (0, True, ("sym", "F", "mech", "company"), "B Coy", "physical", False),
    (1, True, ("sym", "F", "mech", "platoon"), "2 Plt / B Coy", "~ stochastic · 26/30", False),
    (2, None, ("glyph", FA.CROWN, C.AMBER), "slot 1 · Plt leader", "Rifle", False),
    (2, None, ("glyph", FA.PERSON_RIFLE, C.BLUE), "slot 2 · 1 Sqn / MG", "MG", False),
    (2, None, ("glyph", FA.PERSON_RIFLE, C.BLUE), "slot 3 · 1 Sqn / AT", "ATGM", False),
    (2, None, ("glyph", FA.SKULL, C.RED), "slot 4 · 1 Sqn / rifle", "down", True),
    (2, None, ("glyph", FA.VAN, C.TEAL), "slot 6 · IFV 1", "25 mm", False),
    (2, None, ("glyph", FA.VAN, C.RED), "slot 7 · IFV 2", "K-kill", True),
    (1, True, ("sym", "F", "inf", "platoon"), "1 Plt / B Coy", "physical · 28/30", False),
    (2, True, ("sym", "F", "inf", "squad"), "1 Sqn", "9/9", False),
    (3, None, ("glyph", FA.CROWN, C.AMBER), "Sgt Dvorak", "leader", False),
    (3, None, ("glyph", FA.PERSON_RIFLE, C.BLUE), "Cpl Novak", "MG", False),
    (3, None, ("glyph", FA.PERSON_RIFLE, C.BLUE), "Pvt Svoboda", "rifle", False),
    (2, None, ("glyph", FA.VAN, C.TEAL), "BVP-2 #1", "IFV", False),
]
y = oy + 66
for depth, chev, ic, lab, sub, dim in rows:
    cx = ox + 12 + depth * 18
    if chev is not None: s.chevron(cx, y + 5, chev)
    if ic[0] == "sym": sym_icon(cx + 14, y, ic[1], ic[2], ic[3])
    else: s.glyph(ic[1], cx + 17, y + 3, ic[2], 12.5, op=0.55 if dim else 1)
    s.text(cx + 40, y + 15, lab, C.DIS if dim else C.TXT, 12.5)
    s.text(ox + 548, y + 15, sub, C.DIS, 11.5, "end")
    y += 24
s.callout(ox + 560, oy + 80, ox + 580, oy + 70, ["a UNIT: its NATO symbol (the SAME geometry, the ImGui sink) -", "physical and stochastic alike"])
s.callout(ox + 560, oy + 130, ox + 580, oy + 120, ["a stochastic unit's SLOTS as read-only rows (it has no child", "entities) - the Slots tab's icon per role, a down one dimmed"])
s.callout(ox + 560, oy + 330, ox + 580, oy + 330, ["a MAN: by his role - leader crown (amber), the rest the", "rifleman (blue); a VEHICLE: by its class (van / truck / tank -", "Colour Icons §2); destroyed: dimmed + the red skull / fire"])

lx, ly = ox + 980, oy
s.window(lx, ly, 600, 300, "Tactical Map Layers")
s.header(lx + 6, ly + 26, 588, "Units", FA.USERS, C.BLUE)
items = [("Entities", True, "unit symbols and silhouettes (unchanged)"), ("Labels", True, "the cards"),
         ("NATO unit symbols", True, "NEW - a unit's APP-6 symbol: in its card's cell, or alone at its point"),
         ("Unit details", False, ""), ("Rounds", True, "")]
yy = ly + 54
for name, on, doc in items:
    new = name.startswith("NATO")
    if new: s.rect(lx + 8, yy - 3, 584, 25, "none", stroke=C.TEAL, rx=3, dash="4 3")
    s.checkbox(lx + 14, yy, on)
    if new: s.glyph(FA.FLAG, lx + 40, yy + 3, C.BLUE, 12)
    s.text(lx + (60 if new else 40), yy + 14, name, C.TXT)
    if doc: s.text(lx + 220, yy + 14, doc, C.DIS, 11)
    yy += 28
s.note(lx + 10, ly + 210, ["NATO on + Labels on  ⇒ the symbol in the CARD's cell (no second one at the point)",
                           "NATO on + Labels off ⇒ the symbol ALONE at the unit's point (the zoomed-out picture)",
                           "NATO off            ⇒ none: a unit is its card, as today",
                           "ONE symbol per UNIT (a squad's for its nine men) - never per man; one echelon level per zoom",
                           "Men and vehicles keep their rotated silhouettes ('Entities') - no NATO mode for them (§6 S-8)"], C.SUB, 11.5)

s.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "designs", "aggregate-units", "agg-ui-visuals.svg"))
if len(sys.argv) > 1: render(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "designs", "aggregate-units", "agg-ui-visuals.svg"), sys.argv[1], W, H)
