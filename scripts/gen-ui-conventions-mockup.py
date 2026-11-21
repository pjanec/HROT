# Generates docs/UX/ui-conventions-visual.svg — the three house-wide conventions (DESIGN_UI_Conventions.md, R-4144):
# trees draw CHEVRON · ICON · LABEL with one chevron, forms are label-first, collapsing headers are slate with a coloured icon.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render

W, H = 1240, 960
s = Svg(W, H, "UI conventions — trees, label-first forms, collapsing headers (house style; DESIGN_UI_Conventions.md)")

def tag(x, y, txt, col):
    s.text(x, y, txt, col, 12)

# ───────────── ① trees ─────────────
Y = 64
s.separator_text(20, Y, "① Trees — one chevron, drawn BEFORE the icon", W - 20)
Y += 26
# today — node picker (TreeLayout / SaveAsBrowserDialog): icon, then chevron
tag(20, Y + 12, "TODAY · pickers (icon before chevron)", C.ERR)
s.window(20, Y + 20, 290, 150, "Add Node")
ly = Y + 50
for d, lab, op, g, gc in [(0, "Combat", True, FA.FOLDER_OPEN, C.AMBER), (1, "Fire at target", None, FA.CROSSHAIRS, C.RED),
                          (1, "Suppress", None, FA.FIRE, C.ORANGE), (0, "Movement", False, FA.FOLDER_OPEN, C.AMBER), (0, "Sensors", False, FA.FOLDER_OPEN, C.AMBER)]:
    x = 32 + d * 18
    if op is None:
        s.glyph(g, x + 15, ly + 2, gc, 12.5); s.text(x + 34, ly + 14, lab)
    else:
        s.glyph(g, x, ly + 2, gc, 12.5); s.chevron(x + 20, ly + 3, op); s.text(x + 36, ly + 14, lab)
    ly += 22
# today — ORBAT: ArrowButton (a framed square), no icon
tag(330, Y + 12, "TODAY · ORBAT (a framed arrow button)", C.ERR)
s.window(330, Y + 20, 290, 150, "ORBAT")
ly = Y + 50
for d, lab, op in [(0, "Blue force", True), (1, "1st Platoon", True), (2, "Alpha 1-1", None), (2, "Alpha 1-2", None), (1, "2nd Platoon", False)]:
    x = 342 + d * 12
    if op is not None:
        s.rect(x, ly - 1, 19, 19, C.BTN, rx=2)
        s.o.append(f"<path d='M{x + 4.5} {ly + 6} l10 0 -5 6 z' fill='{C.TXT}'/>" if op else f"<path d='M{x + 7} {ly + 3} l6 5 -6 5 z' fill='{C.TXT}'/>")
        s.text(x + 25, ly + 14, lab)
    else:
        s.text(x, ly + 14, lab)
    ly += 22
# after — every tree
tag(660, Y + 12, "AFTER · every tree: CHEVRON · ICON · LABEL", C.OK)
s.window(660, Y + 20, 270, 150, "Add Node")
ly = Y + 50
for d, lab, op, g, gc in [(0, "Combat", True, FA.FOLDER_OPEN, C.AMBER), (1, "Fire at target", None, FA.CROSSHAIRS, C.RED),
                          (1, "Suppress", None, FA.FIRE, C.ORANGE), (0, "Movement", False, FA.FOLDER_OPEN, C.AMBER), (0, "Sensors", False, FA.FOLDER_OPEN, C.AMBER)]:
    ly += s.tree_row(672, ly, d, lab, g, gc, op, w=246)
s.window(945, Y + 20, 275, 150, "ORBAT")
ly = Y + 50
for d, lab, op, g, gc, sel in [(0, "Blue force", True, FA.FLAG, C.BLUE, False), (1, "1st Platoon", True, FA.USERS, C.BLUE, False),
                               (2, "Alpha 1-1", None, FA.USER, C.BLUE, True), (2, "Alpha 1-2", None, FA.USER, C.BLUE, False),
                               (1, "2nd Platoon", False, FA.USERS, C.BLUE, False)]:
    ly += s.tree_row(957, ly, d, lab, g, gc, op, sel=sel, w=251)
s.note(660, Y + 190, ["One shape: ImGui's own tree arrow (what TreeNodeEx draws) — the ORBAT's framed ArrowButton goes.",
                      "A leaf keeps the chevron's slot, so every label at one depth starts at one x. The icon is painted",
                      "after the arrow, the same way the icon tabs paint theirs (Colour Icons §3.1)."])

# ───────────── ② label-first forms ─────────────
Y = 340
s.separator_text(20, Y, "② Forms — the label first, every control in the second column", W - 20)
Y += 26
tag(20, Y + 12, "TODAY · ImGui's default (Director window): control first, label after", C.ERR)
s.window(20, Y + 20, 440, 172, "Director")
fy = Y + 50
s.combo(32, fy, 250, "convoy-ambush"); s.text(290, fy + 14, "Scenario"); fy += 25
s.combo(32, fy, 250, "take 3"); s.text(290, fy + 14, "Take"); fy += 25
s.slider(32, fy, 250, 0.4, "1.00x"); s.text(290, fy + 14, "Speed"); fy += 25
s.checkbox(32, fy, True); s.text(58, fy + 14, "Loop shot"); fy += 25
s.checkbox(32, fy, False); s.text(58, fy + 14, "Pilot view"); fy += 25
s.slider(32, fy, 250, 0.2, "0.50x"); s.text(290, fy + 14, "Segment speed")
s.note(20, Y + 208, ["The eye scans ragged edges: controls at the left, labels trailing at varying x,", "checkbox labels at a third x."])

tag(640, Y + 12, "AFTER · label-first (the Details forms' row, house-wide)", C.OK)
s.window(640, Y + 20, 440, 172, "Director")
fy = Y + 50; LW = 112
for lab, ctrl, args, kw in [("Scenario", "combo", (300, "convoy-ambush"), {}), ("Take", "combo", (300, "take 3"), {}),
                            ("Speed", "slider", (300, 0.4, "1.00x"), {}), ("Loop shot", "checkbox", (True,), {}),
                            ("Pilot view", "checkbox", (False,), {}), ("Segment speed", "slider", (300, 0.2, "0.50x"), {})]:
    s.form_row(652, fy, lab, LW, ctrl, *args, **kw); fy += 25
s.line(652 + LW - 6, Y + 44, 652 + LW - 6, Y + 196, "#5A5A66", dash="2 3")
s.note(640, Y + 208, ["Column 1 = the widest label (measured once, cached), column 2 = the controls at ONE x and ONE width.",
                      "A checkbox in a form sits in column 2 with no text after it. A checkbox that is a toolbar option",
                      "(the asset browser's kind chips) keeps ImGui's text-after-box — it is not a form row (Q3)."])

# ───────────── ③ collapsing headers ─────────────
Y = 640
s.separator_text(20, Y, "③ Collapsing headers — slate with a coloured icon; blue stays for SELECTION", W - 20)
Y += 26
tag(20, Y + 12, "TODAY · headers, buttons, frames, selection — all the same blue", C.ERR)
s.window(20, Y + 20, 440, 214, "Cluster scenario")
hy = Y + 48
s.rect(28, hy, 424, 21, C.HDR, rx=2); s.chevron(34, hy + 5, True); s.text(50, hy + 15, "Exercise"); hy += 25
s.form_row(40, hy, "Name", 90, "field", 300, "convoy-ambush"); hy += 24
s.rect(40, hy - 1, 400, 22, "#2C4A73", rx=2); s.text(48, hy + 14, "Alpha 1-1   (selected row)"); hy += 25
s.rect(28, hy, 424, 21, C.HDR, rx=2); s.chevron(34, hy + 5, True); s.text(50, hy + 15, "Nodes"); hy += 25
s.button(40, hy, 120, "Start node"); s.button(166, hy, 120, "Stop node"); hy += 26
s.rect(28, hy, 424, 21, C.HDR, rx=2); s.chevron(34, hy + 5, False); s.text(50, hy + 15, "Diagnostics"); hy += 25
s.rect(28, hy, 424, 21, C.HDR, rx=2); s.chevron(34, hy + 5, False); s.text(50, hy + 15, "Recording")

tag(640, Y + 12, "AFTER · slate header, CHEVRON · coloured ICON · LABEL", C.OK)
s.window(640, Y + 20, 440, 214, "Cluster scenario")
hy = Y + 48
hy += s.header(648, hy, 424, "Exercise", FA.FLAG_CHECKERED, C.TEAL) + 4
s.form_row(660, hy, "Name", 90, "field", 300, "convoy-ambush"); hy += 24
s.rect(660, hy - 1, 400, 22, "#2C4A73", rx=2); s.glyph(FA.USER, 668, hy + 2, C.BLUE, 12.5); s.text(687, hy + 14, "Alpha 1-1   (selected row — still blue)"); hy += 25
hy += s.header(648, hy, 424, "Nodes", FA.SITEMAP, C.VIOLET) + 4
s.button(660, hy, 120, "Start node", FA.PLAY, C.GREEN); s.button(786, hy, 120, "Stop node", FA.XMARK, C.RED); hy += 26
hy += s.header(648, hy, 424, "Diagnostics", FA.WARNING, C.AMBER, open_=False) + 4
s.header(648, hy, 424, "Recording", FA.VIDEO, C.ORANGE, open_=False)
s.note(640, Y + 248, ["Slate base #2B2F38, hover #353A45, active #3F4552 — pushed by the one header helper, never globally:",
                      "ImGuiCol.Header is ALSO the selection colour of every Selectable and tree row, so a global change",
                      "would grey out selection. The raw editor's authority-green component header keeps its meaning (Q5)."])
s.note(20, Y + 248, ["Measured: no theme is set anywhere (no StyleColors* call in Hrot/ or FDP/ engine code) — this is",
                     "ImGui's default dark, where Header, Button, FrameBg, Tab and TitleBg are all the same hue."])

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "ui-conventions-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
