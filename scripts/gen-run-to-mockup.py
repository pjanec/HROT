# Generates docs/UX/run-to-visual.svg — LC-10 "Run to …" in the mode menu (DESIGN_Scenario_Lifecycle_Review.md §2.8, W-4), house style (R-4143).
# Regenerate: python3 scripts/gen-run-to-mockup.py docs/UX/run-to-visual.svg
# Scope: the mode menu's Run to item and its popup ONLY. The toolbar, ▶ ■ and the rest of the mode menu are context — unchanged.
import sys
from mockup_kit import Svg, C, FA
W, H = 1240, 520
s = Svg(W, H, "Run to … — skip the start of the scenario (Lifecycle LC-10; scope: the mode menu item + its popup; the rest is unchanged context)")
# ── A: the mode menu, Edit ───────────────────────────────────────────
AX, AY = 20, 78
s.text(AX, AY - 8, "A — click the mode indicator · Edit (Preview armed)", C.SUB, 12)
s.menu(AX, AY, 300, [("#", "▶ will start: Preview"), ("Preview", None, None, None), ("Live", None, None, None), ("AAR", None, None, None),
                     "---", ("Run to …", None, FA.HOURGLASS, C.VIOLET)])
s.callout(AX + 300, AY + 125, AX + 320, AY + 125, ["NEW: Run to … — under the moves; offered in", "Edit and in Preview (from Edit it starts the", "Preview, paused, then runs). Never in Live,", "DAR or AAR (R-4166: a Preview is the dry run)"])
# ── B: the popup ─────────────────────────────────────────────────────
BX, BY = 640, 78
s.text(BX, BY - 8, "B — the Run to popup", C.SUB, 12)
s.window(BX, BY, 360, 262, "Run to")
s.note(BX + 12, BY + 40, ["Run the Preview as fast as it steps, then pause."], C.DIS, 12)
s.text(BX + 12, BY + 72, "Time", C.TXT, 12.5); s.field(BX + 70, BY + 58, 120, "10:00"); s.button(BX + 200, BY + 58, 70, "Run", glyph=FA.PLAY, gc=C.VIOLET)
s.line(BX + 10, BY + 92, BX + 350, BY + 92)
s.text(BX + 12, BY + 112, "When a phase begins", C.NOTE, 11.5)
s.glyph(FA.FLAG, BX + 14, BY + 120, C.AMBER, 12); s.text(BX + 34, BY + 132, "Phase 1 · Approach", C.TXT, 12.5)
s.glyph(FA.FLAG, BX + 14, BY + 142, C.AMBER, 12); s.text(BX + 34, BY + 154, "Phase 2 · Assault", C.TXT, 12.5)
s.line(BX + 10, BY + 166, BX + 350, BY + 166)
s.glyph(FA.BOLT, BX + 14, BY + 174, C.ORANGE, 12); s.text(BX + 34, BY + 186, "When a rule fires", C.TXT, 12.5); s.text(BX + 340, BY + 186, "▸", C.TXT, 12.5, "end")
s.menu(BX + 360, BY + 168, 230, [("Blue reaches the bridge", None, FA.BOLT, C.ORANGE), ("Red loses the house", None, FA.BOLT, C.ORANGE), ("Brief Blue", None, FA.BOLT, C.ORANGE)])
s.line(BX + 10, BY + 200, BX + 350, BY + 200)
s.note(BX + 12, BY + 220, ["It pauses there. Stop Run to (this menu) stops it sooner."], C.DIS, 12)
s.callout(BX + 270, BY + 68, BX + 300, BY + 30, ["m:ss or h:mm:ss; a bare number", "is minutes; greyed with the reason", "when not ahead of the clock"])
# ── C: running ───────────────────────────────────────────────────────
CX0, CY0 = 20, 330
s.text(CX0, CY0 - 8, "C — while it runs (the same menu)", C.SUB, 12)
s.menu(CX0, CY0, 300, [("#", "Preview · 3:12"), ("#", "running to 10:00 …"), ("Stop Run to", None, FA.XMARK, C.RED)])
s.callout(CX0 + 300, CY0 + 40, CX0 + 320, CY0 + 40, ["the status line, then Stop Run to — it pauses where", "it is. When it ends, the line says why (\"reached 10:00\",", "\"the condition held at 8:34\") until the next run"])
s.save(sys.argv[1])
