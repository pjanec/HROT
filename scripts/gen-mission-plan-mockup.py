# Generates docs/UX/mission-plan-editor.svg — the mission plan on the map and in the Orders tab (DESIGN_Mission_Plan_Editor.md §2.1),
# house style (R-4143; replaces the early sketch). Regenerate: python3 scripts/gen-mission-plan-mockup.py <out.svg>
import sys
from mockup_kit import Svg, C, FA, text_width
W, H = 1240, 760
s = Svg(W, H, "Mission plan — drawn on the map, listed as sentences in the Orders tab (house style; design §2.1)")
TABS_ENTITY = [("Unit", FA.USER, C.BLUE), ("Crew & cargo", FA.VAN, C.ORANGE), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)]
# ── the map ──────────────────────────────────────────────────────────
MX, MY, MW, MH = 20, 60, 600, 680
s.rect(MX, MY, MW, MH, "#1E2A22", stroke=C.BORDER)
for gx in range(MX + 40, MX + MW, 60): s.line(gx, MY, gx, MY + MH, "#25332A")
for gy in range(MY + 40, MY + MH, 60): s.line(MX, gy, MX + MW, gy, "#25332A")
s.rect(MX, MY + 420, MW, 14, "#3A3A32"); s.rect(MX + 380, MY, 14, MH, "#3A3A32")
s.text(MX + 8, MY + 16, "Map — the selected unit's plan drawn on it", C.SUB, 12)
# objective + phase line
s.raw(f"<polygon points='{MX+400},{MY+110} {MX+540},{MY+90} {MX+560},{MY+200} {MX+430},{MY+220}' fill='{C.GREEN}' fill-opacity='0.12' stroke='{C.GREEN}' stroke-width='1.5'/>")
s.text(MX + 470, MY + 200, "OBJ BLUE", C.GREEN, 12.5)
s.line(MX + 20, MY + 330, MX + MW - 20, MY + 300, C.RED, 2, dash="10 6"); s.text(MX + 30, MY + 322, "PL RED — Phase 2", C.RED, 12)
# the unit and its route with numbered markers
ux, uy = MX + 110, MY + 600
s.raw(f"<circle cx='{ux}' cy='{uy}' r='11' fill='{C.BLUE}' fill-opacity='0.35' stroke='{C.BLUE}' stroke-width='1.6'/>"); s.glyph(FA.USERS, ux - 8, uy - 7, "#FFFFFF", 13)
s.text(ux + 16, uy + 4, "Alpha 1-1", C.TXT, 12.5)
pts = [(ux, uy), (MX + 230, MY + 470), (MX + 300, MY + 330), (MX + 470, MY + 160)]
for i in range(len(pts) - 1): s.line(*pts[i], *pts[i + 1], C.BLUE, 2, dash=None if i == 0 else "7 5")
for n, (px, py) in enumerate(pts[1:], 1):
    done = n == 1; now = n == 2
    s.raw(f"<circle cx='{px}' cy='{py}' r='10' fill='{C.OK if done else ('#F2C033' if now else C.NEUTRAL)}' stroke='#FFFFFF' stroke-width='1.2'/>")
    s.text(px, py + 4.5, str(n), "#000000" if (done or now) else C.TXT, 12, "middle")
s.rect(MX + 312, MY + 318, 170, 20, "#000000", rx=3, op=0.6); s.text(MX + 318, MY + 332, "2  Hold until Phase 2", "#E8E8EE", 11.5)
s.menu(MX + 330, MY + 470, 250, [("#", "right-click the ground"), ("Add task ▸ Move here", None, FA.ROUTE, C.GREEN), ("Add task ▸ Hold here", None, FA.SHIELD, C.BLUE),
                                ("Add task ▸ Attack…", None, FA.CROSSHAIRS, C.RED), "---", ("Move here — replaces the plan", "M", FA.ROUTE, C.GREEN)])
s.note(MX + 12, MY + MH - 14, ["drag a numbered marker to move that task's place — the list and the map are one model"], C.NOTE)
# ── the Orders tab ───────────────────────────────────────────────────
OX, OY, OW, OH = 640, 60, 580, 680
s.window(OX, OY, OW, OH, "Details — Alpha 1-1 (Rifle squad, Friend)", chip=("Live", "#7FD69A", "#163A24"))
s.tabs(OX + 8, OY + 26, OW - 16, TABS_ENTITY, 2)
y = OY + 60
s.text(OX + 14, y + 14, "Plan · 3 tasks", C.TXT); s.glyph(FA.PLAY, OX + 140, y + 3, "#F2C033", 12); s.text(OX + 158, y + 14, "running — task 2", "#F2C033", 12.5)
s.button(OX + OW - 196, y - 2, 90, "Jump to…", glyph=FA.ROUTE, gc=C.BLUE); s.button(OX + OW - 100, y - 2, 86, "Stop plan", glyph=FA.XMARK, gc=C.RED)
y += 32
def task(n, state, order_g, order_c, order, place, then, sel=False):
    global y
    if sel: s.rect(OX + 6, y - 4, OW - 12, 58, "#22283A", stroke="#3A4A6A", rx=3)
    col = C.OK if state == "done" else ("#F2C033" if state == "now" else C.DIS)
    s.raw(f"<circle cx='{OX + 22}' cy='{y + 9}' r='9' fill='{col if state != 'planned' else C.NEUTRAL}' stroke='#FFFFFF' stroke-width='1'/>")
    s.text(OX + 22, y + 13.5, str(n), "#000000" if state != "planned" else C.TXT, 11.5, "middle")
    cx = OX + 40
    cx += s.chip(cx, y, order, glyph=order_g, gc=order_c) + 6
    cx += s.chip(cx, y, place, glyph=FA.CROSSHAIRS, gc=C.GREEN) + 6
    y += 26
    s.text(OX + 40, y + 14, "then", "#9FB7D8"); s.chip(OX + 78, y, then)
    s.text(OX + OW - 14, y - 12, {"done": "✓ done", "now": "▶ now", "planned": "planned"}[state], col, 11.5, "end")
    y += 34
task(1, "done", FA.ROUTE, C.GREEN, "Move", "grid 3412", "when there")
task(2, "now", FA.SHIELD, C.BLUE, "Hold", "the crossroads", "when Phase 2 begins", sel=True)
task(3, "planned", FA.CROSSHAIRS, C.RED, "Attack", "OBJ BLUE", "when done")
s.button(OX + 40, y, 112, "Add task ▾", glyph=FA.PLUS, gc=C.OK); y += 34
s.separator_text(OX + 14, y, "Task 2 — Hold", OX + OW - 14); y += 26
for lab, ctl in [("Where", lambda yy: s.combo(OX + 130, yy, OW - 176, "the crossroads", glyph=FA.CROSSHAIRS, gc=C.GREEN)),
                 ("Facing", lambda yy: s.field(OX + 130, yy, OW - 176, "045°")),
                 ("Then", lambda yy: s.combo(OX + 130, yy, OW - 176, "when Phase 2 begins")),
                 ("Give up after", lambda yy: s.field(OX + 130, yy, OW - 176, "— never —", hint=True))]:
    s.text(OX + 14, y + 14, lab); ctl(y); y += 26
s.button(OX + 130, y + 4, 180, "Advanced condition…", glyph=FA.FILTER, gc=C.BLUE)
s.button(OX + 316, y + 4, 160, "When a rule fires…", glyph=FA.BOLT, gc=C.AMBER); y += 40
s.note(OX + 14, y, ["Advanced condition… opens the condition editor (E5); a rule can also", "start the task — one evaluator for both (R-4129 Q3)."], C.NOTE)
s.save(sys.argv[1])
