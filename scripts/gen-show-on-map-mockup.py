# Generates docs/UX/show-on-map-visual.svg — "show on map" for any field of a map-meaning type (DESIGN_Show_On_Map.md):
# the map glyph in the row, hover = a preview on the map, click = pin, the row menu, the same in the raw component inspector.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 700
s = Svg(W, H, "Show on map — any field whose type has a place on the map (house style; DESIGN_Show_On_Map.md)")

# ── A: a form row + the map ──
s.separator_text(20, 60, "A · A form: hover the ⌖ to preview, click to pin", 600)
s.window(20, 88, 440, 136, "Details — M1 Abrams #1006")
LX, CX, CW = 32, 142, 280
rows = [("Position", "668 · 427 m", FA.LOCATION, C.TEAL, True), ("Heading", "242°", FA.LOCATION, C.TEAL, False),
        ("Target", "Tank Platoon-1", FA.CROSSHAIRS, C.RED, False), ("Objective area", "OBJ Bridge", FA.POLYGON, C.AMBER, True)]
y = 118
for lab, val, g, gc, pinned in rows:
    s.text(LX, y + 14, lab, C.TXT); s.field(CX, y, CW, val)
    gx = CX + CW + 8
    s.rect(gx - 3, y - 1, 22, 21, "#2B3A44" if pinned else C.NEUTRAL, stroke="#5FC7B5" if pinned else None, rx=3)
    s.glyph(FA.LOCATION, gx + 1, y + 2.5, C.TEAL, 12.5)
    y += 26
s.note(20, 240, ["⌖ at the end of every row whose TYPE has a place on the map (WorldPoint, a position, Bearing,", "EntityLink, PointLink, AreaLink, a route). Framed = pinned. Read-only rows have it too."], C.SUB, 12)

# the map
MX, MY, MW, MH = 640, 88, 580, 360
s.rect(MX, MY, MW, MH, "#0B0B0D", stroke=C.BORDER)
for gx in range(MX, MX + MW, 40): s.line(gx, MY, gx, MY + MH, "#17171C")
for gy in range(MY, MY + MH, 40): s.line(MX, gy, MX + MW, gy, "#17171C")
s.text(MX + 8, MY + 16, "map", C.NOTE, 11)
# pinned area
s.o.append(f"<polygon points='{MX+60},{MY+250} {MX+190},{MY+220} {MX+230},{MY+310} {MX+90},{MY+330}' fill='#F2C03322' stroke='#F2C033' stroke-width='2' stroke-dasharray='6 4'/>")
s.text(MX + 100, MY + 285, "OBJ Bridge", C.AMBER, 12)
# the unit + pinned point
ux, uy = MX + 380, MY + 140
s.rect(ux - 6, uy - 6, 12, 12, C.RED); s.text(ux + 12, uy + 4, "M1 Abrams #1006", C.SUB, 11)
s.o.append(f"<circle cx='{ux}' cy='{uy}' r='18' fill='none' stroke='#3EC6B0' stroke-width='2.5'/>")
s.o.append(f"<circle cx='{ux}' cy='{uy}' r='26' fill='none' stroke='#3EC6B0' stroke-width='1' opacity='0.5'/>")
s.rect(ux + 24, uy - 34, 150, 20, "#14201E", stroke="#3EC6B0", rx=3); s.text(ux + 30, uy - 20, "Position · 668 · 427 m", "#7FE0CF", 11.5)
# hover preview of the heading (not pinned: thinner)
hx2 = ux + 70; hy2 = uy + 40
s.o.append(f"<line x1='{ux}' y1='{uy}' x2='{hx2}' y2='{hy2}' stroke='#3EC6B0' stroke-width='2' stroke-dasharray='4 3'/>")
s.o.append(f"<path d='M{hx2} {hy2} l-11 -2 l5 -8 z' fill='#3EC6B0'/>")
s.text(hx2 + 6, hy2 + 14, "Heading 242° (hover)", "#7FE0CF", 11)
s.note(MX, MY + MH + 18, ["Hover = a preview while the mouse is there; click = pinned until clicked", "again or the selection changes. Drawn by the map's debug layer, locally",
                          "(never sent to other nodes); teal = 'shown from a form'."], C.SUB, 12)

# ── B: the row menu ──
s.separator_text(20, 340, "B · The row's right-click menu", 600)
s.window(20, 368, 440, 70, "Details — M1 Abrams #1006")
s.text(LX, 398 + 14, "Position", C.TXT); s.field(CX, 398, CW, "668 · 427 m")
s.menu(200, 420, 230, [("Show on map", None, FA.LOCATION, C.TEAL), ("Centre the map here", None, FA.CROSSHAIRS, C.GREY), "---",
                       ("Copy value", "Ctrl+C", FA.COPY, C.GREY)])

# ── C: the raw component inspector ──
s.separator_text(20, 540, "C · The same in the raw component inspector (the ONE struct editor — CE-4288)", W - 20)
s.window(20, 568, 600, 110, "Details — Components")
for i, (lab, val, mapable) in enumerate([("Position", "668.0, 427.0, 0.0", True), ("Velocity", "3.2, -1.1, 0.0", False), ("NavigationTarget", "Tank Platoon-1", True)]):
    y = 598 + i * 24
    s.text(46, y + 14, lab, C.TXT); s.field(220, y, 340, val)
    if mapable: s.glyph(FA.LOCATION, 568, y + 2.5, C.TEAL, 12.5)
s.note(640, 590, ["The struct editor gets the ⌖ from the field's TYPE (a semantic type, or a",
                  "position component's field named in one table) — so every inspector, params",
                  "form, watch row and replay search shows it without per-panel code.",
                  "Velocity is a vector, not a place: no ⌖."], C.SUB, 12)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "show-on-map-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
