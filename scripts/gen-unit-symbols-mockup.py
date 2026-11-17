#!/usr/bin/env python3
"""DESIGN_Unit_Symbols (R-4143) — NATO symbols for PHYSICAL units: ONE friend platoon (3 squads × 9 men + the platoon leader) drawn under each combination of
the layer panel's three Units switches (Entities · Labels · NATO unit symbols), then the zoom levels (one echelon at a time) and 3-D.
Output: docs/UX/unit-symbols-visual.svg (a PNG for the chat via mockup_kit.render, never committed).

SCOPE (the caption says it too): this specifies ONLY the NATO symbol of a UNIT entity (squad / platoon / company — its card cell or alone at its point),
the unit card's strength bar, and the one new layer row. The men's silhouettes and the men's cards are drawn for CONTEXT, as they are today (R-4151).
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width, render, nato

W, H = 1660, 1330
s = Svg(W, H, "Unit symbols for PHYSICAL units - what each layer switch shows (DESIGN_Unit_Symbols)",
        "Grey annotations are not drawn by the app. Scope: the units' NATO symbol, the unit card, the new layer row. The men's silhouettes and cards are context (today's look).")

FRIEND = "#0064FF"; MAP = "#3C5A32"; GREEN, AMBER, RED = "#5BD06A", "#F2C033", "#E65555"
PW, PH = 500, 330

# one platoon: three squads in a line, each 9 men in a wedge; positions in panel coordinates (px), heading north-east
SQUADS = [("1 Sqn", 120, 210), ("2 Sqn", 250, 170), ("3 Sqn", 380, 210)]
WEDGE = [(0, -14), (-12, -4), (12, -4), (-24, 6), (24, 6), (-36, 16), (36, 16), (-6, 20), (6, 20)]


def man(x, y, hdg=40, leader=False):
    """today's 2-D vector symbol of a man: a small rotated arrowhead in the side colour (context)."""
    a = math.radians(hdg); f = (math.sin(a), -math.cos(a)); r = (math.cos(a), math.sin(a))
    pts = [(x + f[0] * 6, y + f[1] * 6), (x - f[0] * 4 + r[0] * 4, y - f[1] * 4 + r[1] * 4), (x - f[0] * 2, y - f[1] * 2), (x - f[0] * 4 - r[0] * 4, y - f[1] * 4 - r[1] * 4)]
    s.raw(f"<polygon points='{' '.join(f'{px:.1f},{py:.1f}' for px, py in pts)}' fill='{FRIEND}' stroke='#000' stroke-width='0.8'/>")
    if leader: s.raw(f"<circle cx='{x}' cy='{y}' r='8' fill='none' stroke='#F2C033' stroke-width='1'/>")


def man_card(x, y):
    """today's card of a man (context): a health bar + the name, small."""
    s.rect(x - 15, y - 30, 30, 13, "#000", op=0.55, rx=1); s.rect(x - 13, y - 28, 26, 2.5, GREEN); s.rect(x - 13, y - 24, 18, 4, "#DDD", op=0.7)


def unit_card(x, y, name, frac, info, sym, with_sym=True):
    """the UNIT's card (DESIGN_Stochastic_Unit_UI UI-11, for a physical unit too): [symbol cell] strength bar · name · one info line. (x, y) = top-left."""
    cw = 118; sx = 40 if with_sym else 0
    col = GREEN if frac >= 0.9 else AMBER if frac >= 0.7 else RED
    s.rect(x, y, cw + sx, 48, "#000", op=0.62, rx=2); s.rect(x, y, cw + sx, 48, "none", stroke=FRIEND, rx=2)
    if with_sym:
        s.rect(x + 3, y + 3, sx - 4, 42, "#FFF", op=0.1, rx=2); nato(s, x + sx / 2 + 1, y + 28, "F", *sym, w=28, h=17, sw=1.2)
    x += sx
    s.rect(x + 5, y + 5, cw - 10, 5, "#3A3A3A"); s.rect(x + 5, y + 5, (cw - 10) * frac, 5, col)
    s.text(x + 6, y + 26, name, "#FFF", 12, weight="bold")
    s.text(x + 6, y + 42, info, col, 11)
    return cw + sx


def centroid(sq):
    return sq[1], sq[2] + 3


def scene(px, py, title, entities, labels, natos, note):
    s.text(px, py - 8, title, "#E8E8EE", 13, weight="bold")
    # the three switches as chips
    cx = px + PW
    for lab, on in reversed([("Entities", entities), ("Labels", labels), ("NATO", natos)]):
        w = text_width(lab, 11) + 26; cx -= w + 6
        s.rect(cx, py - 22, w, 17, "#1C1C22", stroke=C.OK if on else "#555", rx=8)
        s.glyph(FA.CHECK if on else FA.XMARK, cx + 6, py - 19.5, C.OK if on else "#888", 10.5)
        s.text(cx + 20, py - 9, lab, C.TXT if on else C.DIS, 11)
    s.rect(px, py, PW, PH, MAP)
    for name, x, y in SQUADS:
        for i, (dx, dy) in enumerate(WEDGE):
            if entities: man(px + x + dx, py + y + dy, leader=i == 0)
            if entities and labels: man_card(px + x + dx, py + y + dy)
    if entities: man(px + 250, py + 260, leader=True)   # the platoon leader
    for name, x, y in SQUADS:
        ux, uy = px + x, py + y + 3
        if labels:
            cx0, top = {"1 Sqn": (ux - 100, uy - 100), "2 Sqn": (ux - 70, uy - 140), "3 Sqn": (ux - 40, uy - 100)}[name]
            unit_card(cx0, top, name, 1.0 if name != "3 Sqn" else 7 / 9, "9/9 · Defend" if name != "3 Sqn" else "7/9 · Defend", ("inf", "squad"), with_sym=natos)
            s.line(cx0 + (20 if natos else 30), top + 48, ux, uy, FRIEND, 1.1)
        elif natos:
            nato(s, ux, uy, "F", "inf", "squad", w=30, h=19)
        if not (labels or natos) and not entities:
            pass
    # the platoon's own card (a unit too), lower right
    if labels:
        unit_card(px + PW - 175, py + PH - 58, "2 Plt", 25 / 28, "25/28 · Defend", ("inf", "platoon"), with_sym=natos)
        s.line(px + PW - 175 + 10, py + PH - 34, px + 250, py + 262, FRIEND, 1.1)
    s.note(px, py + PH + 16, note, C.SUB, 11.5)


G = 30
rows = [
    [("A  default: everything on", True, True, True, ["men + their cards (today) · each SQUAD's card gains the symbol cell; the platoon's", "card too. ⚠ busy close up - that is today's men's cards, not the symbols"]),
     ("B  NATO off: today's look + the strength bar", True, True, False, ["exactly today, plus the unit card's strength bar (UI-11) - no symbol", "anywhere"]),
     ("C  Labels off: men + symbols at the squads", True, False, True, ["the men's silhouettes + ONE symbol per squad at its centroid (the", "unit's point, UnitCentroidSystem) - no text"])],
    [("D  symbols only: the commander's map", False, False, True, ["Entities off, Labels off: only the units' symbols - the classic", "overlay; one echelon at a time by zoom (below)"]),
     ("E  cards only (no men)", False, True, True, ["Entities off: the units' cards with their symbols, the men hidden -", "an ORBAT laid on the map"]),
     ("F  men only", True, False, False, ["NATO off, Labels off: today's men only - nothing new is drawn", ""])],
]
y0 = 90
for r, row in enumerate(rows):
    for c, (t, e, l, n, note) in enumerate(row):
        scene(20 + c * (PW + 50), y0 + r * (PH + 90), t, e, l, n, note)

# zoom levels + 3-D
zy = y0 + 2 * (PH + 90) + 10
s.text(20, zy, "G  one ECHELON at a time, by zoom (standalone symbols - cases C and D)", "#E8E8EE", 13, weight="bold")
zooms = [("close (< 1.5 m/px): squads", [("squad", 60, 80), ("squad", 130, 60), ("squad", 200, 80)]),
         ("mid (< 6 m/px): platoons", [("platoon", 70, 70), ("platoon", 140, 70), ("platoon", 210, 70)]),
         ("far (< 25 m/px): companies", [("company", 140, 70)]),
         ("farther: battalions", [("battalion", 140, 70)])]
for i, (lab, units) in enumerate(zooms):
    bx = 20 + i * 265
    s.rect(bx, zy + 12, 250, 130, MAP)
    for ech, ux, uy in units: nato(s, bx + ux - 10, zy + 12 + uy, "F", "inf", ech, w=30, h=19)
    s.text(bx + 8, zy + 135, lab, "#FFF", 11.5)
s.note(20, zy + 162, ["the level whose symbols stand ~40 px apart; a SELECTED unit also shows its children one level down"], C.SUB, 11.5)
# 3-D
tx = 20 + 4 * 265 + 10
s.rect(tx, zy + 12, 570, 130, "#6E8EB8")
s.rect(tx, zy + 92, 570, 50, "#556E3E")
for i in range(9): s.rect(tx + 140 + i * 9, zy + 88 - (i % 3), 3, 9, "#334")
unit_card(tx + 120, zy + 22, "1 Sqn", 1.0, "9/9 · Defend", ("inf", "squad"))
s.line(tx + 140, zy + 70, tx + 175, zy + 86, FRIEND, 1)
s.text(tx + 8, zy + 135, "3-D: the symbol lives in the CARD only (upright, screen-sized)", "#FFF", 11)

# the layer row
ly = zy + 200
s.window(20, ly, 640, 150, "Tactical Map Layers")
s.header(26, ly + 26, 628, "Units", FA.USERS, C.BLUE)
yy = ly + 54
for name, on, doc in [("Entities", True, "the men's and vehicles' silhouettes (unchanged)"), ("Labels", True, "the cards (unchanged)"),
                      ("NATO unit symbols", True, "NEW: every UNIT's symbol - in its card, or alone at its point")]:
    new = name.startswith("NATO")
    if new: s.rect(28, yy - 3, 624, 25, "none", stroke=C.TEAL, rx=3, dash="4 3")
    s.checkbox(34, yy, on); s.text(60, yy + 14, name); s.text(230, yy + 14, doc, C.DIS, 11); yy += 28
s.note(690, ly + 30, ["The rule, by construction ONE symbol per unit:",
                      "   NATO on + Labels on   ⇒ the card's cell (cases A, E)",
                      "   NATO on + Labels off  ⇒ alone at the unit's point (C, D), one echelon by zoom (G)",
                      "   NATO off              ⇒ none (B, F) - today's look",
                      "A unit = a squad / platoon / company entity (UnitRoster); its point = its live men's centroid (UnitCentroidSystem).",
                      "Men and vehicles: their rotated silhouettes always, as today - never a NATO sign (DESIGN_Stochastic_Unit_UI §6 S-8)."], C.SUB, 12)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "unit-symbols-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
