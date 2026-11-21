# Generates docs/UX/condition-expert-visual.svg — the condition editor's EXPERT view redesigned (DESIGN_Condition_Editor.md §8):
# the same struct editor, readable — names not classes, the kind on its own row, no empty Bind rows, numbers with units,
# things by name, a collapsed node shows its sentence. Same condition as the measured frame of today's editor.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 680
s = Svg(W, H, "Condition editor — the Expert view, redesigned (house style; DESIGN_Condition_Editor.md §8)")

X, Y, WW, HH = 20, 60, 760, 600
s.window(X, Y, WW, HH, "Rule — Counter-attack")
s.tabs(X + 8, Y + 28, WW - 16, [("When", FA.FILTER, C.TEAL), ("Do", FA.PLAY, C.GREEN), ("Settings", FA.GEAR, C.GREY), ("Results", FA.FLAG_CHECKERED, C.AMBER)])
# the view switch: friendly / expert
s.text(X + 14, Y + 72, "View", C.TXT); s.glyph_toggle(X + 60, Y + 56, FA.LIST_CHECK, C.TEAL, False); s.glyph_toggle(X + 94, Y + 56, FA.CODE, C.VIOLET, True)
s.text(X + 134, Y + 72, "Expert — every field of the condition", C.SUB, 12)

LX = X + 12; LW = 200; CX = LX + LW; CW = WW - 24 - LW - 26
ty = Y + 96
def node(depth, label, g, gc, open_, kind=None, summary=None, trash=False):
    """A condition node: chevron · coloured kind glyph · kind name; the kind combo in column 2; a collapsed node shows its sentence."""
    global ty
    s.tree_row(LX, ty, depth, label, g, gc, open_, w=WW - 24)
    if kind: s.combo(CX, ty - 1, CW, kind, glyph=g, gc=gc)
    if summary: s.text(CX + 6, ty + 14, summary, C.DIS, 12)
    if trash: s.glyph(FA.TRASH, X + WW - 30, ty + 2, "#8A8A96", 12)
    ty += 24
def field(depth, label, ctrl, *a, **k):
    global ty
    s.text(LX + depth * 18 + 15, ty + 14, label)
    getattr(s, ctrl)(CX, ty - 1, CW, *a, **k); ty += 24

node(0, "When", FA.FILTER, C.TEAL, True, kind="All of these")
node(1, "On one thing", FA.CROSSHAIRS, C.BLUE, True, kind="On one thing", trash=True)
s.text(LX + 2 * 18 + 15, ty + 14, "Thing")
s.chip(CX, ty - 1, "Objective: Hold the bridge", FA.FLAG, C.AMBER); s.glyph(FA.LOCATION, CX + CW - 16, ty + 2, C.TEAL, 12); ty += 24
node(2, "Is", FA.LIST_CHECK, C.GREEN, True, kind="A property")
field(3, "Of", "combo", "Objective status")
field(3, "Property", "combo", "State")
field(3, "Test", "combo", "equals")
field(3, "Value", "combo", "Achieved", glyph=FA.CIRCLE_CHECK, gc=C.OK)
node(1, "Time", FA.CLOCK, C.AMBER, False, summary="after 1 min of scenario time", trash=True)
node(1, "Count", FA.USERS, C.ORANGE, True, kind="At least N of", trash=True)
s.text(LX + 2 * 18 + 15, ty + 14, "At least"); s.field(CX, ty - 1, 70, "3"); s.text(CX + 80, ty + 14, "at most"); s.field(CX + 140, ty - 1, 90, "no limit", hint=True); ty += 24
node(2, "Each", FA.BOLT, C.RED, False, summary="is under fire (above 0.5)")
ty += 4
s.button(LX + 18, ty, 150, "Add condition", FA.PLUS, C.GREEN); ty += 30
s.separator_text(LX, ty, "Timing", X + WW - 12); ty += 24
field(0, "Stays true for", "field", "30 s")
field(0, "Then wait", "field", "0 s")
field(0, "Fire again after", "field", "10 s")
s.button(X + 12, Y + HH - 30, 100, "Apply", FA.CHECK, C.GREEN, fill=C.BTNSEL); s.button(X + 118, Y + HH - 30, 80, "Revert"); s.button(X + 204, Y + HH - 30, 34, "", FA.UNDO, C.GREY); s.button(X + 242, Y + HH - 30, 34, "", FA.REDO, C.GREY)

# annotations
NX = 810
s.note(NX, 80, ["What changed against today's measured frame:"], C.TXT, 13)
items = [
    ("① names, not classes", ["'All of these', 'On one thing' — never", "CompoundPredicateDto / OnEntityPredicateDto"]),
    ("② the kind is the node's own row", ["chevron · coloured glyph · name, its kind", "picker in column 2 — no [0] [1] [2] rows"]),
    ("③ no empty 'Bind' rows", ["a binding shows only where one exists,", "through the field's bind affordance"]),
    ("④ things by name", ["'Objective: Hold the bridge', picked on the", "map or from a list — not 'Exact · Key #10'"]),
    ("⑤ numbers as people write them", ["30 s, 3, 0.5 — not 60.000000;", "'no limit' / 'no end' for the open bound"]),
    ("⑥ a collapsed node says what it means", ["its sentence, from the ONE sentence renderer", "the friendly view uses (§2)"]),
    ("⑦ add and delete", ["'Add condition' at the list's end; a trash", "glyph per node — not +Add / X"]),
    ("⑧ the When tab holds the condition", ["and its timing — the rule's title, scoring", "and visibility live under Settings"]),
]
ny = 108
for head, lines in items:
    s.text(NX, ny, head, C.SUB, 12.5); s.note(NX + 14, ny + 16, lines, C.NOTE, 11.5); ny += 54
s.note(NX, ny + 6, ["①②③⑤⑥⑦ are fixes to the ONE struct editor — every raw", "inspector, params form, replay search and breakpoint gets",
                    "them. ④ and the names are data on the condition types."], C.SUB, 12)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "condition-expert-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
