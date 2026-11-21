# Generates docs/UX/scenario-presets-visual.svg — scenario PRESETS: the preset editor in Details ▸ Scenario ▸ Parameters, the preset under the
# thumbnail in the load dialog, the split ▶ with its preset list, and the four toolbar states (DESIGN_Scenario_Presets.md §5, house style R-4143).
# Regenerate: python3 scripts/gen-scenario-presets-mockup.py docs/UX/scenario-presets-visual.svg
import sys
from mockup_kit import Svg, C, FA, text_width

W, H = 1280, 1150
s = Svg(W, H, "Scenario presets — the preset editor, the load dialog, the split ▶, the toolbar states (design §5)",
        "Scope: the Parameters section, the load dialog's side pane, ▶ and the mode glyph. Everything drawn grey-dashed is context — unchanged.")

# ════════════════ A — Details ▸ Scenario ▸ Parameters: the preset editor ════════════════
X, Y, WW, HH = 20, 60, 610, 540
s.window(X, Y, WW, HH, "Details — Scenario")
s.tabs(X + 8, Y + 30, WW - 16, [("Scenario", FA.FILE, C.AMBER), ("Rules", FA.LIST_CHECK, C.GREEN), ("World", FA.EARTH, C.BLUE)], 0)
s.header(X + 8, Y + 62, WW - 16, "Description", FA.INFO, C.INFO, open_=False)
s.header(X + 8, Y + 90, WW - 16, "Parameters", FA.GEAR, C.GREY, open_=True)
yy = Y + 122
# the world line: what the MAP holds now (not what the table shows)
s.glyph(FA.PEN, X + 18, yy + 3, C.AMBER, 12)
s.text(X + 38, yy + 13, "The map holds:", C.DIS, 12.5); s.text(X + 136, yy + 13, "Default", C.TXT, 12.5)
s.text(X + 196, yy + 13, "— editable, Save on", C.DIS, 12.5)
yy += 30
# the preset bar: combo · + · ⋮ · Preview
s.text(X + 18, yy + 15, "Preset", C.SUB, 13)
s.combo(X + 76, yy, 250, "Night attack", glyph=FA.EYE, gc=C.VIOLET)
s.button(X + 334, yy, 28, "", glyph=FA.PLUS, gc=C.OK)
s.button(X + 368, yy, 28, "", glyph=FA.ELLIPSIS_V, gc=C.GREY)
s.button(X + 412, yy, 182, "Preview this preset", glyph=FA.EYE, gc=C.VIOLET, fill=C.BTNSEL)
yy += 34
# the variant row — only when the scenario uses variants
s.text(X + 18, yy + 15, "Variant", C.SUB, 13)
for i, (lab, on) in enumerate([("As authored", False), ("Random each run", False), ("Number", True)]):
    cx = X + 76 + i * 150
    s.raw(f"<circle cx='{cx + 7}' cy='{yy + 11}' r='6' fill='{C.FRAME}' stroke='{C.BORDER}'/>")
    if on: s.raw(f"<circle cx='{cx + 7}' cy='{yy + 11}' r='3' fill='{C.ACC}'/>")
    s.text(cx + 18, yy + 15, lab, C.TXT, 12.5)
s.field(X + 470, yy, 54, "3")
yy += 26
s.glyph(FA.EYE, X + 76, yy + 3, C.VIOLET, 11)
s.text(X + 94, yy + 13, "Preview only — a world built with a variant can't be saved (some entities may be missing).", C.VIOLET, 11.5)
yy += 26
# the one table: Name | Type | Value (of the shown preset) | Used
cols = [(X + 18, "Name"), (X + 168, "Type"), (X + 268, "Value  ·  Night attack"), (X + 540, "Used")]
s.rect(X + 12, yy, WW - 24, 24, C.HDR)
for cx, lab in cols: s.text(cx, yy + 16, lab, C.SUB, 12)
yy += 24
rows = [("enemyStrength", "int", "3", True, "4"), ("startTime", "time", "02:30", True, "1"),
        ("weather", "enum", "Clear", False, "2"), ("reinforce", "bool", "true", False, "1"),
        ("assembly", "area", "@Village", False, "3")]
for name, typ, val, over, used in rows:
    s.rect(X + 12, yy, WW - 24, 28, "#151519" if rows.index((name, typ, val, over, used)) % 2 else C.BG)
    s.text(X + 18, yy + 18, name, C.TXT, 12.5); s.text(X + 168, yy + 18, typ, C.DIS, 12.5)
    if over:
        s.rect(X + 262, yy + 3, 4, 22, C.AMBER)                                  # overridden: amber bar + bright value + ↺
        s.field(X + 270, yy + 3, 220, val)
        s.glyph(FA.ROTATE_LEFT, X + 500, yy + 7, C.AMBER, 12)
    else:
        s.field(X + 270, yy + 3, 220, val, c=C.DIS)                              # = the default: greyed, still editable
        s.text(X + 498, yy + 18, "default", C.DIS, 10.5)
    s.text(X + 548, yy + 18, used, C.DIS, 12.5)
    yy += 28
s.button(X + 18, yy + 8, 130, "Add parameter", glyph=FA.PLUS, gc=C.OK)
s.text(X + 160, yy + 24, "Add / rename / remove act on EVERY preset (one undo step).", C.DIS, 11.5)
# the combo's popup, open, to the right
PX, PY = X + 76, Y + 176
s.callout(PX + 250, PY + 4, X + WW + 14, PY + 30, [])
s.rect(X + WW + 14, Y + 150, 240, 196, C.POPUP, stroke=C.BORDER)
items = [(FA.PEN, C.AMBER, "Default", "declared defaults", False), (FA.PEN, C.AMBER, "Platoon only", "2 changed", False),
         (FA.EYE, C.VIOLET, "Night attack", "variant 3 · 2 changed", True), (FA.EYE, C.VIOLET, "Random", "random variant", False)]
iy = Y + 158
for g, gc, lab, sub, sel in items:
    if sel: s.rect(X + WW + 18, iy - 2, 232, 38, C.BTNSEL)
    s.glyph(g, X + WW + 26, iy + 4, gc, 12); s.text(X + WW + 46, iy + 13, lab, C.TXT, 12.5); s.text(X + WW + 46, iy + 29, sub, C.DIS, 11)
    iy += 40
s.line(X + WW + 20, iy, X + WW + 248, iy, C.SEP)
s.glyph(FA.PLUS, X + WW + 26, iy + 9, C.OK, 12); s.text(X + WW + 46, iy + 19, "New preset (copy of the shown)", C.TXT, 12)
s.note(X + WW + 14, Y + 362, ["The combo picks what the TABLE shows and edits —", "it never reloads the map.", "",
                              "✎ amber = loads editable (Save on)", "👁 violet = preview only (a variant)",
                              "", "⋮ : Rename · Duplicate · Delete ·", "Move up / down · Reset all to default", "(Default: no rename / delete)"], C.SUB, 11.5)

# ════════════════ B — the load dialog: the preset right under the thumbnail ════════════════
BX, BY, BW, BH = 20, 624, 610, 500
s.window(BX, BY, BW, BH, "Open Scenario")
s.rect(BX + 10, BY + 32, 220, BH - 80, "#151519", stroke=C.SEP, dash="4,3")    # the tree: context, unchanged
s.text(BX + 120, BY + 60, "scenario tree", C.DIS, 12, "middle"); s.text(BX + 120, BY + 76, "(unchanged)", C.DIS, 11, "middle")
PXX = BX + 244
s.rect(PXX, BY + 32, 352, 170, "#22303A", stroke=C.BORDER)
s.text(PXX + 176, BY + 196, "preview.jpg", C.DIS, 11, "middle")
s.text(PXX, BY + 224, "hill-attack", C.TXT, 15)
s.text(PXX, BY + 252, "Preset", C.SUB, 13)
s.combo(PXX + 58, BY + 236, 294, "Night attack", glyph=FA.EYE, gc=C.VIOLET)
s.text(PXX + 58, BY + 274, "variant 3 · enemyStrength 3 · startTime 02:30", C.DIS, 11.5)
s.rect(PXX, BY + 286, 352, 120, "#151519", stroke=C.SEP, dash="4,3")
s.text(PXX + 8, BY + 304, "the description — scrolls in its own box", C.DIS, 12)
s.text(PXX + 8, BY + 322, "(was above the Run setup expander, which it pushed", C.DIS, 11.5)
s.text(PXX + 8, BY + 338, "out of sight in a short window)", C.DIS, 11.5)
s.text(PXX, BY + 426, "Terrain: test-town   ·   Entities: 46", C.DIS, 12)
s.button(BX + BW - 290, BY + BH - 36, 120, "Open", fill=C.BTNSEL)
s.button(BX + BW - 162, BY + BH - 36, 150, "Open and go live")
s.note(BX + 10, BY + BH - 30, ["Open = load for editing with the preset", "(a 👁 preset opens preview-only)."], C.SUB, 11)

# ════════════════ C — the toolbar: the split ▶ and the four states ════════════════
CX, CY = 660, 624
s.window(CX, CY, 600, 230, "Main toolbar — time controls")
ty = CY + 40
s.glyph_toggle(CX + 16, ty, FA.PEN, C.AMBER, False)                      # the mode glyph (left of ▶)
s.rect(CX + 52, ty - 2, 40, 30, C.NEUTRAL, stroke=C.BORDER, rx=3); s.glyph(FA.PLAY, CX + 64, ty + 5, C.VIOLET, 16)
s.rect(CX + 92, ty - 2, 16, 30, C.NEUTRALSEL, stroke=C.BORDER, rx=3); s.text(CX + 100, ty + 18, "▾", C.TXT, 12, "middle")
s.rect(CX + 114, ty - 2, 120, 30, "#151519", stroke=C.SEP, dash="4,3"); s.text(CX + 174, ty + 18, "step ■ 0:00 1×", C.DIS, 11, "middle")
# the ▾ menu
s.rect(CX + 52, ty + 32, 280, 160, C.POPUP, stroke=C.BORDER)
my = ty + 40
for g, gc, lab, chk in [(FA.PEN, C.AMBER, "Default", False), (FA.PEN, C.AMBER, "Platoon only", False),
                         (FA.EYE, C.VIOLET, "Night attack", True), (FA.EYE, C.VIOLET, "Random", False)]:
    if chk: s.glyph(FA.CHECK, CX + 60, my + 4, C.OK, 11)
    s.glyph(g, CX + 80, my + 4, gc, 12); s.text(CX + 100, my + 14, lab, C.TXT, 12.5)
    my += 26
s.line(CX + 58, my + 2, CX + 326, my + 2, C.SEP)
s.glyph(FA.GEAR, CX + 80, my + 10, C.GREY, 12); s.text(CX + 100, my + 20, "Edit presets…", C.TXT, 12.5)
s.tooltip(CX + 344, ty + 40, 240, ["▶ Preview — Night attack", "(variant 3: preview only)", "", "▾ arms a preset; never the mode"])

SX, SY = 660, 876
s.window(SX, SY, 600, 260, "The mode glyph and Save, by state")
states = [(FA.PEN, C.AMBER, "steady", "Edit — Default", "Save on"),
          (FA.PEN, C.AMBER, "steady", "Edit — Platoon only", "Save on (bindings win on load)"),
          (FA.EYE, C.VIOLET, "steady", "Night attack, paused at its start", "Save off — tooltip says why"),
          (FA.EYE, C.VIOLET, "blinking", "Preview running (any preset)", "Save off")]
ry = SY + 38
for g, gc, look, what, save in states:
    s.glyph_toggle(SX + 16, ry, g, gc, look == "blinking")
    s.text(SX + 56, ry + 16, what, C.TXT, 12.5); s.text(SX + 300, ry + 16, look, C.DIS, 12); s.text(SX + 380, ry + 16, save, C.SUB, 12)
    ry += 36
s.tooltip(SX + 56, ry + 2, 400, ["Save is off: this world was built with variant 3 —", "entities whose Presence roll failed are not in it.", "Pick an ✎ preset (or Default) to edit and save."])
s.save(sys.argv[1] if len(sys.argv) > 1 else "docs/UX/scenario-presets-visual.svg")
