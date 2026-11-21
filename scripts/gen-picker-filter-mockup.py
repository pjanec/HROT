# Generates docs/UX/picker-filter-visual.svg — CE-4398 (a filter reveals nested hits) + CE-4397 (the scenario picker's folder order).
# Scope: only the "Open Asset" scenario picker (PickerWindow, Tree layout); the editor behind it is context and is not drawn.
# Left: no filter — the tree in R-4185's order. Right: "destr" typed — a FLAT list of the hits, each with its folder path greyed after the name.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 660
s = Svg(W, H, "The scenario picker — folders in R-4185's order; a filter shows a flat list of the hits (CE-4397 · CE-4398)")

# ---- left: no filter ---------------------------------------------------------------------------------------------------------------
PX, PY, PW, PH = 20, 60, 560, 580
s.window(PX, PY, PW, PH, "Open Asset")
s.field(PX + 10, PY + 30, 330, "Search…", hint=True)
s.checkbox(PX + 355, PY + 30, False); s.text(PX + 381, PY + 44, "Show tests (23)", C.SUB, 12)
rows = [
    (0, "Recent", 0xf1da, C.GREY, True, None),
    (1, "showcase-crossfire", FA.FILE, C.TEAL, None, None),
    (1, "bd-destruction", FA.FILE, C.TEAL, None, None),
    (0, "My scenarios", FA.USER, C.BLUE, False, "4"),
    (0, "showcases", FA.STAR, C.AMBER, False, "2"),
    (0, "demos", FA.FOLDER_OPEN, C.AMBER, True, None),
    (1, "air", FA.PLANE, C.BLUE, False, "9"),
    (1, "infantry-and-squads", FA.USERS, C.BLUE, False, "14"),
    (1, "platoons-and-vehicles", FA.VAN, C.BLUE, False, "11"),
    (1, "weapons-and-fires", FA.CROSSHAIRS, C.RED, False, "13"),
    (1, "buildings-and-doors", FA.HOUSE, C.AMBER, False, "10"),
    (1, "civilians-and-crowd", FA.USERS, C.ORANGE, False, "8"),
    (1, "sensing-and-weather", FA.EYE, C.TEAL, False, "7"),
    (1, "rules-objectives-directors", FA.FLAG, C.AMBER, False, "9"),
    (1, "navigation-and-terrain", FA.ROUTE, C.BLUE, False, "6"),
]
ty = PY + 62
for depth, label, g, gc, open_, n in rows:
    s.tree_row(PX + 10, ty, depth if depth else 0, label, g, gc, open_, sub=(n if n else None), w=PW - 30)
    ty += 24
s.note(PX + 12, ty + 12, ["tests/ — fixtures, perf, regression — are behind the tick (off), not in the list"], C.NOTE, 11)

# ---- right: "destr" typed ------------------------------------------------------------------------------------------------------------
QX = 640
s.window(QX, PY, 580, PH, "Open Asset")
s.field(QX + 10, PY + 30, 330, "destr", border=C.ACC)
s.checkbox(QX + 355, PY + 30, False); s.text(QX + 381, PY + 44, "Show tests (23)", C.SUB, 12)
hits = [("bd-destruction", "demos › buildings-and-doors"), ("bd-destruction-heavy", "demos › buildings-and-doors"),
        ("wf-destroyer-duel", "demos › weapons-and-fires"), ("nav-destroyed-bridge", "demos › navigation-and-terrain")]
y = PY + 66
for i, (name, folder) in enumerate(hits):
    sel = i == 0
    if sel: s.rect(QX + 10, y - 2, 560, 22, "#2B4C7A", rx=2)
    s.glyph(FA.FILE, QX + 34, y + 3, C.TEAL, 13)
    # the matched letters highlighted, as the picker already does
    k = name.lower().index("destr"); pre, mid, post = name[:k], name[k:k + 5], name[k + 5:]
    x = QX + 56
    s.text(x, y + 14, pre, C.TXT); x += text_width(pre)
    s.text(x, y + 14, mid, "#FFFF66"); x += text_width(mid)
    s.text(x, y + 14, post, C.TXT); x += text_width(post)
    s.text(x + 14, y + 14, folder, C.DIS, 12)   # the folder path, greyed, after the name
    y += 26
s.note(QX + 12, y + 16, ["★ a non-empty filter lists every hit FLAT — no folders to open, none left collapsed",
                         "★ the folder of each hit stays visible, greyed, after its name",
                         "★ an empty filter brings the tree back as on the left (R-4185's order)"], C.SUB, 12)
s.note(PX + 12, PY + PH - 24, ["unchanged: the search box, the Show tests tick, the Recent section, the side pane and the footer (Open · Open and go live)"], C.NOTE, 11)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "picker-filter-visual.svg")
s.save(out)
if "--png" in sys.argv:
    render(out, sys.argv[sys.argv.index("--png") + 1], W, H)
print("wrote", out)
