# Generates docs/UX/bridge-laying-ui-visual.svg — ui's half of bridge laying (DESIGN_Bridge_Laying.md §13), house style (R-4143).
# Regenerate: python3 scripts/gen-bridge-laying-ui-mockup.py docs/UX/bridge-laying-ui-visual.svg
# Scope: ① the two-point pick for Lay bridge (its line and gap label), ② the Bridge row on the Unit form, ③ the two engineers' 3-D silhouettes.
# Everything else (the map, the river, the menu, other form rows) is CONTEXT, drawn unchanged (R-4151).
import sys
from mockup_kit import Svg, C, FA
W, H = 1320, 760
s = Svg(W, H, "Bridge laying — ui's half: the two-point pick, the refusal on the Unit form, the engineers' looks (house style; Bridge Laying §13)",
        "Scope: the pick's line + label, the Bridge row, the two silhouettes. The map, river and other rows are context, unchanged. Grey annotations are not drawn by the app.")


def river(x, y, w, h):
    s.rect(x, y, w, h, "#2B3A2A")   # the 2-D map's ground
    s.raw(f"<path d='M{x} {y + h * 0.42} C {x + w * 0.3} {y + h * 0.30}, {x + w * 0.6} {y + h * 0.55}, {x + w} {y + h * 0.40} L {x + w} {y + h * 0.62} "
          f"C {x + w * 0.6} {y + h * 0.75}, {x + w * 0.3} {y + h * 0.52}, {x} {y + h * 0.64} Z' fill='#2C5C8A'/>")


def engineer(x, y):
    s.rect(x - 11, y - 7, 22, 14, "#3F6DB5", stroke="#FFFFFF", sw=1.2)   # a friendly unit symbol (context)
    s.text(x, y + 4, "E", "#FFFFFF", 10, "middle", "bold")


# ── ① the pick: near bank placed, the line follows the cursor, the gap read off live ─────────────────────────────
for (X, label, col, far, note) in [
        (20, "gap 18.4 m — fits (span 24 m)", C.GREEN, (250, 196), ["① OK: near bank clicked; the line follows the cursor;", "   the label is BridgeLaying.Plan's answer — green, fits"]),
        (450, "the gap is 30.0 m, wider than the 24.0 m this bridge spans", C.RED, (300, 230), ["② refused: the same line in red with the refusal's", "   own words (BridgeLaying.Say); a click still sends it"])]:
    Y = 70
    s.window(X, Y, 410, 300, "Map", chip=None)
    river(X + 1, Y + 21, 408, 278)
    engineer(X + 60, Y + 70)
    nx, ny = X + 150, Y + 110   # the near bank
    fx, fy = X + far[0], Y + far[1]
    s.line(nx, ny, fx, fy, col, 2.5)
    s.raw(f"<circle cx='{nx}' cy='{ny}' r='4' fill='{col}'/>")
    # the cursor crosshair at the far end
    for dx, dy in [(-9, 0), (9, 0), (0, -9), (0, 9)]:
        s.line(fx + dx * 0.35, fy + dy * 0.35, fx + dx, fy + dy, "#66BFFF", 1.5)
    if X == 20: s.text((nx + fx) / 2 + 8, (ny + fy) / 2 - 8, label, col, 11.5, "start")
    else:   # a long refusal sits under the line, inside the map
        s.rect(X + 8, Y + 266, 394, 22, "#1B1B1F", op=0.85, rx=3); s.text(X + 14, Y + 281, label, col, 11.5, "start")
    # the pick banner (context: the existing banner, words for this pick)
    s.rect(X + 8, Y + 28, 394, 22, "#1E2A3A", stroke=C.SEP, rx=3)
    s.text(X + 16, Y + 43, "Pick the far bank for Lay bridge" if X == 20 else "Pick the far bank for Lay bridge", C.TXT, 12)
    s.text(X + 394, Y + 43, "Esc", C.DIS, 11, "end")
    s.note(X, Y + 316, note)

s.note(880, 90, ["THE GESTURE (CE-6500) — Lay bridge from the", "unit's order menu:",
                 "  click 1 = the near bank (a point appears),", "  click 2 = the far bank → the order goes.",
                 "  Esc / right-click = cancel, nothing sent.", "",
                 "Generic: ANY order whose two required fields", "are map points gets this pick (OrderPick.TwoPoints);",
                 "an order may add a live label (Lay bridge: the", "gap and the verdict, from BridgeLaying.Plan — the",
                 "function the engineer plans with, so the preview", "cannot disagree with the order)."], C.SUB)

# ── ② the Unit form: the Bridge row ─────────────────────────────────────────────────────────────────────────────
TABS = [("Unit", FA.USER, C.BLUE), ("Crew & cargo", FA.VAN, C.ORANGE), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)]
X, Y, WW = 20, 430, 520
s.window(X, Y, WW, 230, "Details — Wolverine 1 (Assault bridge layer)", chip=("Live", "#F2C033", "#3A3112"))
s.tabs(X + 8, Y + 26, WW - 16, TABS, 0)
LX, CX = X + 14, X + 120
y = Y + 60
s.separator_text(LX, y, "State", X + WW - 14); y += 24
s.text(LX, y + 14, "Strength"); s.rect(CX, y, WW - 134, 19, C.FRAME); s.text(CX + 6, y + 14, "100 / 100"); y += 26
s.text(LX, y + 14, "Bridge"); s.text(CX, y + 14, "Carried — 1 span (24 m)", C.TXT); y += 26
s.text(LX, y + 14, "", C.TXT)
s.glyph(FA.CIRCLE_XMARK, CX, y + 3, C.RED, 13)
s.text(CX + 20, y + 14, "refused: the gap is 30.0 m, wider than the 24.0 m this bridge spans", C.RED, 11.5); y += 26
s.note(X, Y + 246, ["③ the Bridge row (CE-6501): what he carries, laying / recovering / no span (BridgeLayerLook.Of),",
                    "   and the last refusal in its own words under it — shown only while BridgeCarried.Refusal is set. Read-only."])

# ── ③ the two engineers in 3-D (side silhouettes) ───────────────────────────────────────────────────────────────
X, Y = 600, 430
s.rect(X, Y, 700, 300, "#202530", stroke=C.BORDER)
s.text(X + 12, Y + 22, "④ the 3-D looks (CE-6502) — side view, x forward →", "#E8E8EE", 12.5)
# assault bridge layer: tracked hull + the folded scissor bridge on top
bx, by = X + 40, Y + 200
s.rect(bx, by - 30, 260, 30, "#3B3F2E", rx=6)                       # tracks
for i in range(7): s.raw(f"<circle cx='{bx + 22 + i * 36}' cy='{by - 15}' r='11' fill='#6E7262'/>")
s.rect(bx + 10, by - 62, 230, 32, "#626C42")                         # hull
s.rect(bx + 150, by - 82, 50, 20, "#626C42")                         # cab
s.rect(bx - 6, by - 98, 250, 16, "#8A8F7A", stroke="#4A4D40")       # the folded bridge (two halves, stacked)
s.rect(bx - 6, by - 112, 250, 14, "#9BA08A", stroke="#4A4D40")
s.text(bx + 130, by + 26, "Assault bridge layer (118) — the folded span on the roof", C.SUB, 11.5, "middle")
s.text(bx + 130, by + 42, "span hidden while NoSpan (laid)", C.NOTE, 11, "middle")
# floating bridge: wheeled truck + a folded ribbon bay on the bed
fx, fy = X + 380, Y + 200
s.rect(fx + 200, fy - 70, 60, 50, "#626C42", rx=4)                   # cab
s.rect(fx + 212, fy - 62, 30, 18, "#7FA6C8")                         # windscreen
s.rect(fx, fy - 30, 270, 14, "#3B3F2E")                              # chassis
for wx in [30, 80, 140, 232]: s.raw(f"<circle cx='{fx + wx}' cy='{fy - 12}' r='14' fill='#2A2C24'/>")
s.rect(fx + 8, fy - 88, 180, 58, "#8A8F7A", stroke="#4A4D40")       # the folded bay (W profile)
s.raw(f"<path d='M{fx + 8} {fy - 88} l45 22 l45 -22 l45 22 l45 -22' stroke='#4A4D40' fill='none' stroke-width='1.5'/>")
s.text(fx + 135, fy + 26, "Floating bridge engineer (119) — a ribbon bay on the bed", C.SUB, 11.5, "middle")
s.text(fx + 135, fy + 42, "bay hidden while NoSpan", C.NOTE, 11, "middle")
s.save(sys.argv[1])
