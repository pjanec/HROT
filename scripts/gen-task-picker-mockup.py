# Generates docs/UX/task-picker-visual.svg — DESIGN_Mission_Plan_Editor.md §5.13 (the task picker): "+ Add task" opens THE one picker
# (PickerWindow, Tree layout — Add Entity D1) with the unit's fitting tasks in folders, the generic ones first, a preview pane, tests / demos hidden.
# Scope: only the picker; the Orders tab behind it is context.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 640
s = Svg(W, H, "The task picker — \"+ Add task\" for Rifleman 1 (DESIGN_Mission_Plan_Editor.md §5.13)")
PX, PY, PW, PH = 20, 60, 1100, 560
s.window(PX, PY, PW, PH, "Add a task — Rifleman 1 (Infantry)")
s.field(PX + 10, PY + 30, 420, "Search tasks…  (name, description, keyword)", hint=True)
s.checkbox(PX + 450, PY + 30, False); s.text(PX + 476, PY + 44, "Show tests and demos (43)", C.SUB, 12)

ty = PY + 62
rows = [
    (0, "Recent", 0xf1da, C.GREY, True, None),
    (1, "Move to location", 0xf3c5, C.BLUE, None, None),
    (1, "Fight from cover", FA.SHIELD, C.AMBER, None, None),
    (0, "Orders — the unit's own", FA.FLAG, C.AMBER, True, "4"),
    (1, "Move", 0xf554, C.BLUE, None, None),
    (1, "Hold", 0xf256, C.GREY, None, None),
    (1, "Attack", FA.CROSSHAIRS, C.RED, None, None),
    (1, "Observe and fire", FA.BINOCULARS, C.TEAL, None, None),
    (0, "Movement", 0xf554, C.BLUE, True, "9"),
    (1, "Advance to", None, None, None, None),
    (1, "Danger crossing", None, None, None, None),
    (1, "Fall back", None, None, None, None),
    (1, "Follow route  ·  Join formation  ·  …", None, None, None, None),
    (0, "Combat", FA.CROSSHAIRS, C.RED, False, "12"),
    (0, "Weapons and vehicles", FA.VAN, C.GREY, False, "7"),
    (0, "Casualties and engineering", 0xf0fa, C.RED, False, "6"),
    (0, "Crowd and surrender", FA.USERS, C.ORANGE, False, "2"),
    (0, "Reactions", FA.BOLT, C.AMBER, False, "3"),
    (0, "My behaviours (authored)", FA.BRAIN, C.PINK, False, "0"),
]
for depth, label, g, gc, open_, n in rows:
    sel = label == "Fight from cover"
    s.tree_row(PX + 10, ty, depth, label, g, gc, open_, sel=sel, sub=(f"{n}" if n else None), w=450)
    ty += 24
s.note(PX + 12, ty + 10, ["⛔ hidden unless ticked: Tests/*, Demos/*, Samples (43 for a rifleman) · never offered: OperatorControl, SOPs (not tasks)"], C.NOTE, 11)

# preview pane
VX, VY, VW = PX + 480, PY + 62, 340
s.rect(VX, VY, VW, 360, "#141418", stroke=C.BORDER, rx=3)
s.glyph(FA.SHIELD, VX + 12, VY + 12, C.AMBER, 18); s.text(VX + 40, VY + 28, "Fight from cover", C.TXT, 15)
s.text(VX + 40, VY + 46, "Combat · FightFromCover", C.NOTE, 11)
s.note(VX + 12, VY + 76, ["Moves to the nearest good cover facing the threat,", "fires from it, changes position when suppressed."], C.SUB, 12)
s.separator_text(VX + 12, VY + 110, "Parameters", VX + VW - 12)
py = VY + 136
for lab, val in [("Threat side", "auto (the nearest contact)"), ("Max distance", "40 m"), ("Leave when", "out of ammunition")]:
    s.text(VX + 14, py + 14, lab, C.TXT, 12); s.field(VX + 130, py, VW - 145, val, dis=True); py += 26
s.separator_text(VX + 12, py + 6, "Fits", VX + VW - 12)
s.text(VX + 14, py + 40, "Infantry · Security force", C.SUB, 12)
s.button(VX + VW - 170, VY + 320, 70, "Add", FA.PLUS, C.GREEN, fill=C.BTNSEL); s.button(VX + VW - 92, VY + 320, 80, "Cancel")

s.note(860 + 10, 120, ["★ ONE picker — PickerWindow, Tree layout",
                 "  (the Add Entity / scenario picker, D1):",
                 "  search, folders, preview pane",
                 "",
                 "★ folders in a fixed order, the generic",
                 "  first: Recent → the unit's own Orders",
                 "  → Movement → Combat → Weapons and",
                 "  vehicles → Casualties and engineering",
                 "  → Crowd and surrender → Reactions",
                 "  → My behaviours",
                 "",
                 "★ Orders = the class's generic orders",
                 "  in a fixed order (Move · Hold · Attack",
                 "  · Observe and fire), named without",
                 "  the class (PersonMove → Move)",
                 "",
                 "★ names are words (MoveToLocation →",
                 "  Move to location)",
                 "",
                 "★ only what FITS the unit (CE-4350)",
                 "",
                 "a Rifleman: 90 offered today → 45",
                 "real tasks in 8 folders"], C.SUB, 12)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "task-picker-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
