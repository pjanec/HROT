# Generates docs/UX/map-interaction-visual.svg — the map interaction states (DESIGN_Map_Interaction.md §2.5 / §2.6), house style (R-4143).
# Regenerate: python3 scripts/gen-map-interaction-mockup.py docs/UX/map-interaction-visual.svg
import sys
from mockup_kit import Svg, C, FA, text_width
W, H = 1240, 850
s = Svg(W, H, "Map interaction — drawing, picking, placing (house style; real FontAwesome glyphs; design §2.5 / §2.6)")
MAPBG = "#1E2A22"; ROAD = "#3A3A32"; BLD = "#2E3440"; GRID = "#25332A"

def map_panel(x, y, w, h, title):
    s.rect(x, y, w, h, MAPBG, stroke=C.BORDER)
    for gx in range(int(x) + 40, int(x + w), 60): s.line(gx, y, gx, y + h, GRID)
    for gy in range(int(y) + 40, int(y + h), 60): s.line(x, gy, x + w, gy, GRID)
    s.rect(x, y + h * 0.62, w, 14, ROAD); s.rect(x + w * 0.7, y, 14, h, ROAD)
    s.rect(x + w * 0.74, y + h * 0.2, 70, 50, BLD, stroke="#4A5260"); s.rect(x + w * 0.15, y + h * 0.7, 60, 40, BLD, stroke="#4A5260")
    s.text(x + 8, y + 16, title, C.SUB, 12)

def status_line(x, y, w, glyph, gc, tool, hint):
    s.rect(x, y, w, 22, "#16161B", stroke=C.BORDER)
    s.glyph(glyph, x + 8, y + 4, gc, 12.5); s.text(x + 26, y + 15.5, tool, C.TXT, 12.5)
    s.text(x + 34 + text_width(tool, 12.5), y + 15.5, hint, C.SUB, 12)

# ── A: drawing an area ───────────────────────────────────────────────
AX, AY, AW, AH = 20, 60, 590, 380
map_panel(AX, AY, AW, AH, "A — drawing an area")
# toolbar (armed tool marked)
tx = AX + 8; ty = AY + 26
for g, gc, sel in [(FA.MOVE, C.GREY, False), (FA.POLYGON, C.TEAL, True), (FA.ROUTE, C.GREEN, False), (FA.LOCATION, C.BLUE, False), (FA.CROSSHAIRS, C.GREEN, False)]:
    s.glyph_toggle(tx, ty, g, gc, sel); tx += 34
pts = [(AX + 120, AY + 120), (AX + 260, AY + 100), (AX + 330, AY + 210), (AX + 210, AY + 270)]
cur = (AX + 140, AY + 250)
s.raw("<polygon points='" + " ".join(f"{px},{py}" for px, py in pts) + f"' fill='{C.TEAL}' fill-opacity='0.12' stroke='none'/>")
for i in range(len(pts) - 1): s.line(*pts[i], *pts[i + 1], C.TEAL, 2)
s.line(*pts[-1], *cur, C.TEAL, 1.6, dash="5 4"); s.line(*cur, *pts[0], C.TEAL, 1.2, dash="2 4")
for i, (px, py) in enumerate(pts): s.rect(px - 4, py - 4, 8, 8, C.TEAL if i else "#FFFFFF", stroke="#0B2A26")
s.glyph(FA.CROSSHAIRS, cur[0] - 7, cur[1] - 7, "#FFFFFF", 14)
# on-map hint by the cursor
s.rect(cur[0] + 14, cur[1] + 8, 248, 20, "#000000", rx=3, op=0.6)
s.text(cur[0] + 20, cur[1] + 22, "4 points · Enter / ✓ finish · Esc cancel", "#E8E8EE", 11.5)
# on-map finish button near the last point
s.button(pts[2][0] + 14, pts[2][1] + 4, 70, "Finish", glyph=FA.CHECK, gc=C.OK)
# right-click drawing menu
s.menu(AX + 360, AY + 80, 222, [("Finish area", "Enter", FA.CHECK, C.OK), ("Remove last point", "Backspace", FA.BACKSPACE, C.AMBER), "---", ("Cancel drawing", "Esc", FA.XMARK, C.RED)])
s.text(AX + 360, AY + 74, "right-click while drawing:", C.NOTE, 11)
status_line(AX, AY + AH - 22, AW, FA.POLYGON, C.TEAL, "Area", "click: add point · Backspace: remove last · Enter or ✓: finish · Esc: cancel")

# ── B: a pending pick ────────────────────────────────────────────────
BX, BY, BW, BH = 630, 60, 590, 380
map_panel(BX, BY, BW, BH, "B — a form asked for a point")
# the banner at the top of the map
s.rect(BX + 120, BY + 28, 350, 30, "#22283A", stroke=C.INFO, rx=4)
s.glyph(FA.CROSSHAIRS, BX + 132, BY + 36, C.GREEN, 14); s.text(BX + 154, BY + 48, "Pick a point for  Move to  (Alpha 1-1)", C.TXT, 12.5)
s.button(BX + 400, BY + 33, 62, "Cancel")
# crosshair cursor + a rejected-click flash on a building
s.glyph(FA.CROSSHAIRS, BX + 240, BY + 180, "#FFFFFF", 22)
s.rect(BX + BW * 0.74 - 6, BY + BH * 0.2 - 6, 82, 62, "none", stroke=C.ERR, rx=3, sw=1.6)
s.rect(BX + BW * 0.74 - 40, BY + BH * 0.2 + 62, 170, 20, C.POPUP, stroke=C.ERR, rx=3)
s.text(BX + BW * 0.74 - 32, BY + BH * 0.2 + 76, "a point, not a building", "#F08080", 11.5)
status_line(BX, BY + BH - 22, BW, FA.CROSSHAIRS, C.GREEN, "Pick a point", "click the map · Esc or Cancel: stop picking — the field stays as it was")
s.note(BX + 10, BY + BH + 18, ["the requesting field in the form is highlighted; a second request replaces this one with a notice"], C.NOTE)

# ── C: placing on a building — level choice ──────────────────────────
CX, CY, CW, CH = 20, 470, 590, 360
map_panel(CX, CY, CW, CH, "C — placing a rifleman on a 3-storey building")
gx, gy = CX + CW * 0.74 + 30, CY + CH * 0.2 + 22
s.raw(f"<circle cx='{gx}' cy='{gy}' r='9' fill='{C.BLUE}' fill-opacity='0.35' stroke='{C.BLUE}' stroke-width='1.5'/>")
s.glyph(FA.PERSON_RIFLE if False else FA.USER, gx - 6, gy - 7, "#FFFFFF", 12)
s.line(gx, gy, gx + 34, gy - 26, C.BLUE, 1.2, dash="3 3")  # heading drag
s.rect(gx - 250, gy + 16, 236, 42, "#000000", rx=3, op=0.65)
s.text(gx - 244, gy + 31, "roof · level 2 of 3   PgUp / PgDn", "#E8E8EE", 11.5)
s.text(gx - 244, gy + 48, "drag: heading · right-click / Esc: done", "#E8E8EE", 11.5)
s.callout(gx - 12, gy + 4, CX + 20, CY + 60, ["the ghost is the unit's own symbol at its real footprint —", "no big sphere (§2.6); the level shows only when a", "building offers more than one"])
status_line(CX, CY + CH - 22, CW, FA.USER, C.BLUE, "Place Rifleman", "click: place · PgUp / PgDn: level · drag: heading · Esc: done")

# ── D: placing an aircraft — airborne by default ─────────────────────
DX, DY, DW, DH = 630, 470, 590, 360
map_panel(DX, DY, DW, DH, "D — placing an A-10C")
px, py = DX + 260, DY + 170
s.line(px, py + 14, px, py + 96, C.SUB, 1.2, dash="4 3")
s.raw(f"<ellipse cx='{px}' cy='{py + 100}' rx='14' ry='5' fill='#000' fill-opacity='0.45'/>")
s.raw(f"<circle cx='{px}' cy='{py}' r='13' fill='{C.BLUE}' fill-opacity='0.3' stroke='{C.BLUE}' stroke-width='1.5'/>")
s.glyph(FA.PLANE, px - 8, py - 8, "#FFFFFF", 16)
s.rect(px + 22, py - 18, 232, 42, "#000000", rx=3, op=0.65)
s.text(px + 28, py - 3, "airborne · 300 m over the ground · 150 m/s", "#E8E8EE", 11.5)
s.text(px + 28, py + 14, "Shift: parked on the ground", "#E8E8EE", 11.5)
s.callout(px - 16, py + 60, DX + 20, DY + 290, ["a Mobility = Air type is placed in the air (SpawnHeight.AboveLevel);", "the drop line shows where it is over the ground"])
status_line(DX, DY + DH - 22, DW, FA.PLANE, C.BLUE, "Place A-10C", "click: place airborne · Shift-click: on the ground · Esc: done")
s.save(sys.argv[1])
