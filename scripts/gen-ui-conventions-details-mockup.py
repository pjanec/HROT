# Generates docs/UX/ui-conventions-details-visual.svg — DESIGN_UI_Conventions.md §3.1-§3.3:
# what stays out of the label column (Q-C3), a Blueprint node's Details editor before / after, the label-width rule (Q-C6).
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 830
s = Svg(W, H, "UI conventions — what is not a form row, a node's Details editor, the label width (DESIGN_UI_Conventions.md §3)")

def tag(x, y, txt, col): s.text(x, y, txt, col, 12)

# ───────────── A: not form rows (Q-C3) ─────────────
Y = 64
s.separator_text(20, Y, "A · Not form rows — they keep ImGui's own shape (Q-C3)", W - 20)
Y += 26
tag(20, Y + 12, "KEPT · a filter bar: each checkbox is an option, its text after the box", C.OK)
s.window(20, Y + 20, 560, 120, "Asset Browser")
s.field(32, Y + 50, 536, "      search assets", hint=True); s.glyph(FA.SEARCH, 38, Y + 53, C.DIS, 12)
fx = 32
for lab, on in [("BTree", True), ("HSM", True), ("Blueprint", True), ("Scenario", False), ("Mission", False)]:
    s.checkbox(fx, Y + 78, on); s.text(fx + 25, Y + 92, lab); fx += 34 + text_width(lab) + 14
s.button(32, Y + 106, 110, "Create...", FA.PLUS, C.GREEN, fill=C.BTNSEL); s.button(148, Y + 106, 90, "Cancel")
s.note(20, Y + 156, ["Three things stay as they are: a search box (the hint inside it is its label),",
                     "a row of filter checkboxes (each one an option, not a field), a row of buttons."])

tag(640, Y + 12, "REJECTED · the same bar forced label-first", C.ERR)
s.window(640, Y + 20, 580, 168, "Asset Browser")
s.form_row(652, Y + 48, "Search", 100, "field", 456, "", hint=True)
ry = Y + 72
for lab, on in [("BTree", True), ("HSM", True), ("Blueprint", True), ("Scenario", False)]:
    s.form_row(652, ry, lab, 100, "checkbox", on); ry += 23
s.text(652, ry + 12, "… a column of rows for what was one line", C.DIS, 12)

# ───────────── B: a Blueprint node's Details editor ─────────────
Y = 300
s.separator_text(20, Y, "B · A node's editor in the Blueprint's Details panel (the 'node drawers' — NOT on the canvas)", W - 20)
Y += 26
tag(20, Y + 12, "TODAY · WhenNodeDrawer: label after the control", C.ERR)
s.window(20, Y + 20, 560, 176, "Details — When")
y = Y + 50
s.combo(32, y, 330, "Event Fired"); s.text(372, y + 14, "Mode"); y += 25
s.text(32, y + 14, "Edges:"); s.checkbox(80, y, True); s.text(104, y + 14, "Rising"); s.checkbox(160, y, False); s.text(184, y + 14, "Falling"); y += 25
s.combo(32, y, 330, "EntityDamaged"); s.text(372, y + 14, "Event"); y += 25
s.checkbox(32, y, True); s.text(56, y + 14, "Only when it targets me"); s.text(220, y + 14, "(via Target)", C.DIS); y += 25
s.field(32, y, 330, "0.50"); s.text(372, y + 14, "Score threshold"); y += 25
s.field(32, y, 330, "2.0"); s.text(372, y + 14, "Max age (s)")

tag(640, Y + 12, "AFTER · label-first; a group of flags stays in column 2", C.OK)
s.window(640, Y + 20, 580, 176, "Details — When")
y = Y + 50; LW = 128; X2 = 652 + LW
s.form_row(652, y, "Mode", LW, "combo", 420, "Event Fired"); y += 25
s.text(652, y + 14, "Edges"); s.checkbox(X2, y, True); s.text(X2 + 24, y + 14, "Rising"); s.checkbox(X2 + 90, y, False); s.text(X2 + 114, y + 14, "Falling"); y += 25
s.form_row(652, y, "Event", LW, "combo", 420, "EntityDamaged"); y += 25
s.text(652, y + 14, "Targets me only"); s.checkbox(X2, y, True); s.text(X2 + 26, y + 14, "via Target", C.DIS); y += 25
s.form_row(652, y, "Score threshold", LW, "field", 420, "0.50"); y += 25
s.form_row(652, y, "Max age (s)", LW, "field", 420, "2.0")
s.note(640, Y + 210, ["A field made of several flags (Edges) keeps each flag's text after its box — INSIDE column 2.",
                      "The same applies to every node editor (20 drawers) and to popups like 'promote to variable'."])

# ───────────── C: the label width (Q-C6) ─────────────
Y = 570
s.separator_text(20, Y, "C · The label column: the widest label, but never more than 25 % of the form (Q-C6)", W - 20)
Y += 26
def form(x, y, w, title, labels_vals, note):
    s.window(x, y, w, 30 + 25 * len(labels_vals), title)
    widest = max(text_width(l) for l, _ in labels_vals) + 14
    lw = min(widest, max(0.25 * (w - 24), 90))
    yy = y + 30
    for lab, val in labels_vals:
        shown = lab
        if text_width(lab) + 14 > lw:
            while text_width(shown + "…") + 14 > lw: shown = shown[:-1]
            shown = shown.rstrip() + "…"
        s.text(x + 12, yy + 14, shown); s.field(x + 12 + lw, yy, w - 24 - lw, val); yy += 25
    s.line(x + 12 + lw - 6, y + 26, x + 12 + lw - 6, yy, "#5A5A66", dash="2 3")
    s.note(x, yy + 18, note)
    return yy
rows = [("Name", "Alpha 1-1"), ("Side", "Blue"), ("Starts with weapon raised and ready", "yes"), ("Skill", "Veteran")]
yy = form(20, Y + 20, 560, "Unit — wide panel", rows,
          ["560 px: 25 % = 134 px. The long label is cut with … and its full text is",
           "the tooltip; the short ones keep the column narrow (widest first, cap second)."])
s.tooltip(150, Y + 112, 250, ["Starts with weapon raised and ready"])
yy = form(640, Y + 20, 320, "Unit — narrow panel", rows,
          ["320 px: 25 % = 74 px, below the 90 px floor —", "so the column is 90 px."])
s.note(980, Y + 50, ["The rule, recomputed when the form's", "width changes (no allocation: the widest",
                     "label is measured once per form):", "", "  col1 = min(widest + 14,", "             max(25 % of the form, 90 px))",
                     "", "⭐ Labels are written short — a label", "that needs the cut is a finding."], C.SUB, 12)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "ui-conventions-details-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
