# Generates docs/UX/new-scenario-visual.svg — New Scenario (blank on a terrain / from a recipe) and the Save-As Replace prompt
# (DESIGN_New_Scenario_And_Recipe.md §2, R-4135), house style (R-4143). Regenerate: python3 scripts/gen-new-scenario-mockup.py <out.svg>
import sys
from mockup_kit import Svg, C, FA, text_width
W, H = 1240, 760
s = Svg(W, H, "New scenario on a terrain, and saving over a recipe (house style; design §2, R-4135)")
# ── A: the New Scenario picker (the wide picker of AQ108 D25, one source) ──
X, Y, WW, HH = 20, 60, 900, 520
s.window(X, Y, WW, HH, "New Scenario")
s.field(X + 12, Y + 32, 340, "      search terrains and recipes", hint=True); s.glyph(FA.SEARCH, X + 18, Y + 35, C.DIS, 12)
LX = X + 12; ly = Y + 64
def group(label, g, gc, open_=True):
    global ly
    ly += s.tree_row(LX, ly, 0, label, g, gc, open_, w=340) + 2          # the one tree row (UI conventions §2)
def item(label, sub, g, gc, sel=False):
    global ly
    ly += s.tree_row(LX, ly, 1, label, g, gc, None, sel=sel, sub=sub, w=340)
group("Blank, on a terrain", FA.MOUNTAIN, C.TEAL)
for name, sub, sel in [("basic-desert", "2 recipes", False), ("berlin", "1 recipe", True), ("bt-range", "—", False), ("cqb-village", "—", False),
                       ("demo-city", "1 recipe", False), ("highway-rest", "—", False), ("indirect-fire-range", "—", False), ("test-town", "7 recipes", False)]:
    item(name, sub, FA.EARTH, C.BLUE, sel)
ly += 6
group("From a recipe", FA.FILE, C.AMBER)
for name, sub in [("basic-desert", "basic-desert"), ("demo-city", "demo-city"), ("sop-demo", "berlin"), ("tt-convoy-ambush", "test-town")]:
    item(name, sub, FA.FILE, C.AMBER)
s.text(LX + 30, ly + 12, "… 6 more", C.DIS, 12)
# divider + side pane
PXX = X + 370
s.line(PXX, Y + 28, PXX, Y + HH - 8, C.BORDER)
s.rect(PXX + 16, Y + 32, 500, 280, "#22303A", stroke=C.BORDER)          # the terrain's preview.jpg (R-4135)
for i in range(5): s.line(PXX + 16, Y + 60 + i * 52, PXX + 516, Y + 40 + i * 58, "#2F4250", 9)
s.rect(PXX + 230, Y + 120, 90, 60, "#3A4656", stroke="#56647A"); s.rect(PXX + 120, Y + 200, 60, 44, "#3A4656", stroke="#56647A")
s.text(PXX + 266, Y + 300, "assets/terrain/berlin/preview.jpg", C.DIS, 11, "middle")
s.text(PXX + 16, Y + 334, "berlin", C.TXT, 15)
facts = [("Origin", "52.520° N, 13.405° E"), ("Size", "4.0 × 3.2 km"), ("Roads", "roads.json · 1 network"), ("Recipes on it", "sop-demo")]
for i, (k, v) in enumerate(facts):
    s.text(PXX + 16, Y + 358 + i * 20, k, C.DIS, 12.5); s.text(PXX + 140, Y + 358 + i * 20, v, C.TXT, 12.5)
s.note(PXX + 16, Y + 448, ["A new, empty scenario on this terrain. The terrain is fixed from now on —", "a different terrain means a new scenario."], C.SUB, 12)
s.button(X + WW - 230, Y + HH - 34, 110, "Create…", glyph=FA.PLUS, gc=C.OK, fill=C.BTNSEL)
s.button(X + WW - 112, Y + HH - 34, 100, "Cancel")
s.callout(X + 200, Y + 154, X + 940, Y + 140, ["TWO groups in ONE picker (Q2): a blank", "scenario on any terrain, or a copy of a", "recipe — the recipe's terrain is shown", "beside it (Q1: the terrain is chosen", "only here, R-4124)"])
s.callout(PXX + 500, Y + 200, X + 940, Y + 240, ["the terrain's still (R-4135: assets/terrain/", "<name>/preview.jpg) and its facts from", "terrain.json — the same side pane as the", "Open Scenario picker (D25)"])
s.callout(X + WW - 175, Y + HH - 24, X + 940, Y + 400, ["Create… goes to the ordinary Save-As", "naming, then the ONE load (Q1)"])
# ── B: Save as recipe — the Replace prompt of the ONE Save-As dialog ──
BX, BY = 20, 600
s.window(BX, BY, 560, 150, "Save Scenario as Recipe")
s.text(BX + 14, BY + 46, "Name", C.SUB); s.field(BX + 70, BY + 32, 300, "checkpoint-berlin")
s.text(BX + 380, BY + 46, "recipes/scenarios/", C.DIS, 12)
s.rect(BX + 14, BY + 62, 532, 46, C.PENDROW, stroke=C.PEND, rx=3); s.glyph(FA.WARNING, BX + 24, BY + 70, C.PEND, 13)
s.text(BX + 44, BY + 81, "A recipe named \"checkpoint-berlin\" already exists.", C.PEND, 12.5)
s.text(BX + 44, BY + 99, "Replace it? Scenarios made from it are not changed.", C.SUB, 12)
s.button(BX + 300, BY + 118, 120, "Replace", glyph=FA.ARROWS_ROTATE, gc=C.PEND); s.button(BX + 428, BY + 118, 118, "Cancel", fill=C.BTNSEL)
s.callout(BX + 560, BY + 84, BX + 600, BY + 60, ["the ONE Save-As dialog asks before replacing, for every kind —", "updating a recipe is a plain Save As over its name (R-4135, N3);", "the safe default (Cancel) is the focused button"])
s.save(sys.argv[1])
