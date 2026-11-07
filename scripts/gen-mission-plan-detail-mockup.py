# Generates docs/UX/mission-plan-detail-visual.svg — DESIGN_Mission_Plan_Editor.md §5: the Orders tab's task form with the "then" picker and
# give up (D3, D4, §5.6), operation phases as triggers in the Rules list (D5), the Timeline's ONE Tasks lane (E4), the WHO chip (E5).
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 960
s = Svg(W, H, "Mission plan editor — the task form, the 'then' picker, phases, the Tasks timeline (house style; design §5)")

# ── A: the task form in the Orders tab ──
s.separator_text(20, 60, "A · A task in the Orders tab — an order first, then a plain 'then', an optional give-up", 600)
s.window(20, 88, 560, 300, "Details — Alpha 1-1 (Rifle squad, Friend)")
s.tabs(28, 114, 544, [("Unit", FA.USER, C.BLUE), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)], 1)
y = 146
s.text(32, y + 14, "Plan · 3 tasks", C.SUB, 12); y += 24
for n, sent, state, col in [("1", "Move to grid 3412, then when there", "✓ done", C.OK), ("2", "Hold at the crossroads, then when Phase 2 begins", "▶ now", C.AMBER)]:
    s.rect(28, y - 2, 544, 22, "#1E2A3E" if n == "2" else C.BG); s.text(36, y + 13, n, C.DIS); s.text(56, y + 13, sent); s.text(560, y + 13, state, col, 12, "end"); y += 24
y += 6
s.header(28, y, 544, "Task 2 — Hold", FA.SHIELD, C.BLUE); y += 28
LX, CX, CW = 40, 170, 380
s.text(LX, y + 14, "Order"); s.combo(CX, y, CW, "Hold", glyph=FA.SHIELD, gc=C.BLUE); y += 26
s.text(LX, y + 14, "Where"); w = s.chip(CX, y, "the crossroads", FA.LOCATION, C.TEAL); s.glyph(FA.LOCATION, CX + CW - 16, y + 3, C.TEAL, 12); y += 26
s.text(LX, y + 14, "Then"); s.chip(CX, y, "when Phase 2 begins", FA.FLAG, C.VIOLET, border="#8FB7F0"); y += 26
s.text(LX, y + 14, "Give up after"); s.checkbox(CX, y, True); s.field(CX + 28, y, 80, "10 min"); s.text(CX + 118, y + 14, "then the task is Failed and the plan goes on", C.DIS, 11.5)

# ── B: phases as triggers in the Rules list ──
s.separator_text(640, 60, "B · Operation phases — triggers marked as phases, in the Rules window", W - 20)
s.window(640, 88, 580, 196, "Rules")
s.tabs(648, 114, 564, [("List", FA.LIST_CHECK, C.TEAL), ("Timeline", FA.CLOCK, C.AMBER)], 0)
y = 146
s.tree_row(652, y, 0, "Phases", FA.FLAG, C.VIOLET, True, w=556); y += 24
for name, when, state, col, btn in [("1  Approach", "begins at the start", "✓ begun 0:00", C.OK, False),
                                    ("2  Assault", "when OBJ BLUE held · latest H+20", "waiting", C.DIS, True),
                                    ("3  Consolidate", "when all of 1st Platoon finish task 3", "waiting", C.DIS, False)]:
    s.glyph(FA.FLAG, 682, y + 2, C.VIOLET, 12); s.text(702, y + 14, name); s.text(800, y + 14, when, C.SUB, 12)
    s.text(1206, y + 14, state, col, 12, "end")
    if btn: s.button(1040, y - 1, 96, "Begin now", FA.PLAY, C.GREEN)
    y += 26
s.note(648, y + 14, ["A phase IS a trigger (D5): begins = fires; Begin now = Fire (instructor, Live);", "'no later than' = its when OR scenario time ≥ H+20."], C.NOTE, 11.5)

# ── C: the Tasks timeline ──
s.separator_text(20, 600, "C · The Timeline's Tasks group — ONE lane for all units, bars for the selection (E4)", W - 20)
TX, TY, TW, TH = 20, 628, 1200, 260
s.rect(TX, TY, TW, TH, C.BG, stroke=C.BORDER); s.rect(TX, TY, TW, 20, C.TITLE); s.text(TX + 8, TY + 14, "Event Timeline")
rx0, rx1 = TX + 150, TX + TW - 16
def X(t): return rx0 + (rx1 - rx0) * t / 30.0
for m in range(0, 31, 5):
    s.text(X(m), TY + 38, f"{m}:00", C.DIS, 11, "middle"); s.line(X(m), TY + 42, X(m), TY + 46, C.SEP)
s.tree_row(TX + 8, TY + 52, 0, "Tasks", FA.ROUTE, C.GREEN, True, w=120)
# E4 (R-4161): ONE Tasks lane for ALL units — a marker where a task started / ended Done / ended Failed, naming unit and task
yy = TY + 80
s.text(TX + 24, yy + 14, "all units", C.SUB, 12)
s.rect(rx0, yy + 2, rx1 - rx0, 18, C.BG, stroke=C.BORDER)
marks = [(0, C.BLUE, FA.PLAY), (0.4, C.BLUE, FA.PLAY), (1.2, C.BLUE, FA.PLAY), (3, C.BLUE, FA.PLAY), (6, C.OK, FA.CIRCLE_CHECK), (7, C.OK, FA.CIRCLE_CHECK),
         (9, C.OK, FA.CIRCLE_CHECK), (11.5, C.RED, FA.XMARK), (14, C.BLUE, FA.PLAY), (14.2, C.BLUE, FA.PLAY), (14.4, C.BLUE, FA.PLAY), (18, C.OK, FA.CIRCLE_CHECK)]
for t, col, g in marks:
    s.glyph(g, X(t) - 5, yy + 5, col, 10)
s.note(X(11.5) + 10, yy + 34, ["Bravo 1-1 · task 2 Overwatch · Failed (gave up after 10 min)"], C.RED, 11)
# the SELECTED unit additionally shows its own tasks as bars
yy = TY + 128
s.text(TX + 24, yy + 14, "Alpha 1-1 ▸", C.TXT, 12)
for a, b, lab, col in [(0, 6, "Move", C.OK), (6, 14, "Hold", C.AMBER), (14, 22, "Attack OBJ", C.RED)]:
    s.rect(X(a) + 1, yy + 2, X(b) - X(a) - 2, 18, col, rx=3, op=0.35); s.rect(X(a) + 1, yy + 2, 3, 18, col)
    s.text(X(a) + 8, yy + 15, lab, C.TXT, 11.5)
for t, name in [(0, "Phase 1 Approach"), (14, "Phase 2 Assault")]:
    s.line(X(t), TY + 50, X(t), TY + TH - 10, C.VIOLET, sw=1.5, dash="5 3"); s.glyph(FA.FLAG, X(t) + 4, TY + 186, C.VIOLET, 11); s.text(X(t) + 20, TY + 196, name, C.VIOLET, 11.5)
s.line(X(17), TY + 30, X(17), TY + TH - 10, C.RED, sw=1.5)
s.note(20, TY + TH + 22, ["ONE Tasks lane for every unit (E4, R-4161 — thousands of units stay one row): a marker per task start / Done / Failed, hover names unit and task;",
                          "it follows the timeline's entity filter. The SELECTED unit also shows its own tasks as bars. Phase triggers' fire times are the violet columns; the playhead is red."], C.SUB, 12)

# ── D: Advanced condition… in the Mission-task context — WHO defaults to "this unit", never empty (E5, R-4161) ──
s.separator_text(640, 350, "D · Advanced condition… — WHO is 'this unit' by default, never an empty field (E5)", W - 20)
s.window(640, 378, 580, 120, "Condition — Alpha 1-1, task 2 Hold, then …")
s.text(656, 422, "WHO", C.SUB, 12); w1 = s.chip(700, 408, "this unit", FA.USER, C.BLUE)
s.text(700 + w1 + 12, 422, "IS", C.SUB, 12); s.chip(700 + w1 + 34, 408, "under fire", FA.FIRE, C.ORANGE)
s.note(656, 452, ["The chip is a real choice: another unit, a rule or a phase; stored with no OnEntity wrapper.",
                  "A Wait task (E9) has no order: its 'then' is required; an empty 'then' elsewhere means 'when done'."], C.NOTE, 11.5)

# the open "then" menu
s.menu(360, 300, 230, [("#", "then …"), ("when done", None, FA.CIRCLE_CHECK, C.OK), ("when there", None, FA.LOCATION, C.TEAL), ("after … min", None, FA.CLOCK, C.AMBER),
                       ("when under fire", None, FA.FIRE, C.ORANGE), ("when strength below … %", None, FA.HEART, C.RED), "---",
                       ("when a phase begins ▸", None, FA.FLAG, C.VIOLET), ("when a unit finishes a task ▸", None, FA.USERS, C.BLUE),
                       ("when a rule fires ▸", None, FA.BOLT, C.AMBER), "---", ("Advanced condition…", None, FA.FILTER, C.TEAL), ("New rule…", None, FA.PLUS, C.GREEN)])


out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "mission-plan-detail-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
