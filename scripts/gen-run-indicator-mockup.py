#!/usr/bin/env python3
"""Mockup: the run-mode indicator · ▶ · Save — every state (docs/UX/DESIGN_Scenario_Presets.md §5c, D10; user 2026-11-08).

Scope: the toolbar's mode indicator, ▶ (+ its ▾), and the Save button. Everything else on the toolbar is context — unchanged.

    python3 scripts/gen-run-indicator-mockup.py   # writes docs/UX/run-indicator-visual.svg
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA

FLOPPY = 0xf0c7          # stand-in for the silk floppy sprite (shell/save, "m19")
PAUSE = 0xf04c
CARET = 0xf0d7
YELLOW = "#FFE14D"       # the yellow pen: brighter than the DAR amber, never in the same state
VIO, GRN, AMB, BLU, GRY = C.VIOLET, C.GREEN, "#FFB333", C.BLUE, C.GREY
RED = "#E63838"         # Live while runs are RECORDED (CE-4090); green = not recorded
DIM = 0.45               # the indicator's "armed" level (today's RunModeIndicator dim)

STOP, CLOSE, REW, CHECKERED = 0xf04d, 0xf2f5, 0xf04a, 0xf11e
# (state, indicator (glyph, colour, alpha, mark), play (glyph, colour, dis, caret), stop (glyph, colour, dis), review (None hidden / True / False greyed),
#  save (glyph, colour, dis), indicator tooltip, the ■ · ⏪ · Save words)
ROWS = [
    ("EDIT — the indicator shows the mode you ARE in; ▶ previews what you edit", None),
    ("Edit · saveable (variant 0, healthy cluster)", (FA.PEN, YELLOW, 1.0, ""), (FA.PLAY, VIO, False, True), (STOP, C.SUB, True), None, (FLOPPY, C.TXT, False),
     "Editing — your changes save to Night attack. ▶ previews them. Click: switch mode", "■ nothing to stop · Save = save the scenario"),
    ("Edit · HOVERED (button + hand cursor)", (FA.PEN, YELLOW, 1.0, "hover"), (FA.PLAY, VIO, False, True), (STOP, C.SUB, True), None, (FLOPPY, C.TXT, False),
     "Editing — your changes save to Night attack. ▶ previews them. Click: switch mode", "the indicator lights up like every other button"),
    ("Edit · a variant preset is loaded (not saveable)", (FA.EYE, VIO, 1.0, ""), (FA.PLAY, VIO, False, True), (STOP, C.SUB, True), None, (FLOPPY, C.TXT, True),
     "Editing a variant (preview only) — not saveable: built with variant 3", "Save greyed: built with variant 3 — load as authored to save"),
    ("Preview running (blinks)", (FA.EYE, VIO, 1.0, ""), (PAUSE, VIO, False, False), (STOP, C.TXT, False), None, (FLOPPY, C.TXT, True),
     "Preview — a copy of the edit world", "■ = back to Edit (restores) · Save greyed: a preview runs a copy"),
    ("LIVE — briefing · running · ended (R2″)", None),
    ("Live · BRIEFING (paused at 0:00) · recorded", (0xf111, RED, 1.0, ""), (FA.PLAY, RED, False, True), (STOP, C.SUB, True), False, (FA.FLAG, AMB, False),
     "Live — recorded. ▶ starts the exercise. Click: switch mode", "■ greyed (not started) · ⏪ greyed: 'nothing to review yet' · flag = checkpoint"),
    ("Live · RUNNING (blinks) · recorded", (0xf111, RED, 1.0, ""), (PAUSE, RED, False, False), (STOP, C.TXT, False), True, (FA.FLAG, AMB, False),
     "Live — recorded, running", "■ = End the exercise? · ⏪ = DAR (last 30 s / 2 min / from the start) · flag = checkpoint"),
    ("Live · running · NOT recorded", (0xf111, GRN, 1.0, ""), (PAUSE, GRN, False, False), (STOP, C.TXT, False), True, (FA.FLAG, AMB, False),
     "Live — not recorded, running", "⏪ offers only the RAM window ('from the start' greyed: not recorded)"),
    ("Live · ENDED (debrief, frozen at the end)", (CHECKERED, RED, 1.0, ""), (FA.PLAY, RED, True, False), (CLOSE, C.TXT, False), True, (FA.FLAG, AMB, False),
     "Exercise ended — results ready. Click: Run again · checkpoint · DAR · AAR · Close", "▶ greyed (ended) · ■ BECOMES ⏏ Close the session · ⏪ = DAR on this exercise"),
    ("REVIEW — inside a live run (DAR) or a recording (AAR)", None),
    ("DAR (review — the exercise is paused)", (FA.CLOCK_BACK, AMB, 1.0, ""), (FA.PLAY, AMB, False, False), (CLOSE, C.TXT, False), None, (FA.FLAG, AMB, False),
     "DAR — 2 min behind live", "⏏ = back to live (players see 'REVIEW — the exercise is paused')"),
    ("AAR (replay)", (FA.PLAY, BLU, 1.0, ""), (FA.PLAY, BLU, False, False), (STOP, C.TXT, False), None, (FA.FLAG, AMB, False),
     "AAR — exercise 3 of Night attack. Click: Run again from here · Edit · Close", "■ = back to the recording's start (Close is in the indicator's menu)"),
]
W = 1330
H = 96 + sum(30 if r[1] is None else 40 for r in ROWS) + 150
s = Svg(W, H, "The time controls — every state (switch now · Live ends in two steps)",
        "Scope: the mode indicator, ▶ (+ ▾), ■, ⏪ Review and Save. Step, the time and the timeline are context — unchanged. Grey text is annotation.")

X_STATE, X_TB, X_TIP, X_SAVETIP = 20, 300, 545, 935
y = 64
s.text(X_STATE, y, "state", C.NOTE, 11.5); s.text(X_TB, y, "indicator · ▶ ▾ · ■ · ⏪ · Save", C.NOTE, 11.5)
s.text(X_TIP, y, "indicator tooltip", C.NOTE, 11.5); s.text(X_SAVETIP, y, "■ · ⏪ Review · Save", C.NOTE, 11.5)
y += 14

def toolbar(x, y, ind, play, stop, review, save):
    s.rect(x, y, 230, 30, C.BG, stroke=C.BORDER, rx=3)
    g, col, a, mark = ind                                 # the indicator — the kit's glyph button (D29): neutral background, lighter on hover, ▾ = it opens a menu
    hov = mark == "hover"
    s.rect(x + 4, y + 4, 32, 22, C.NEUTRALSEL if hov else C.NEUTRAL, stroke="#C8C8D4" if hov else None, rx=3)
    s.glyph(g, x + 8, y + 7, col, 15, op=a)
    s.glyph(CARET, x + 26, y + 10, C.SUB, 8)
    pg, pc, pd, caret = play                               # ▶ — the GLYPH takes the colour, never the background
    s.rect(x + 40, y + 4, 26, 22, C.BTN, rx=3, op=0.55 if pd else 1)
    s.glyph(pg, x + 47, y + 8, pc, 14, op=0.35 if pd else 1)
    if caret:                                              # ▾ — the preset picker: only where a load with a preset makes sense
        s.rect(x + 66, y + 4, 12, 22, C.BTN, rx=3)
        s.glyph(CARET, x + 68.5, y + 10, C.TXT, 10)
    sg, sc, sd = stop                                      # ■ (or ⏏ Close / back to live)
    s.rect(x + 84, y + 4, 26, 22, C.BTN, rx=3, op=0.55 if sd else 1)
    s.glyph(sg, x + 90.5, y + 8, sc, 13, op=0.35 if sd else 1)
    if review is not None:                                 # ⏪ Review — built (LC-16); greyed at the briefing (nothing to review yet)
        s.rect(x + 114, y + 4, 26, 22, C.BTN, rx=3, op=1 if review else 0.55)
        s.glyph(REW, x + 119.5, y + 8, C.TXT, 13, op=1 if review else 0.35)
    sg, sc, sd = save                                      # Save
    s.rect(x + 190, y + 4, 28, 22, C.BTN, rx=3, op=0.55 if sd else 1)
    s.glyph(sg, x + 197, y + 8, sc, 14, op=0.4 if sd else 1)

def wrap(x, y, t, n=56):
    words, lines, cur = t.split(" "), [], ""
    for w in words:
        if len(cur) + len(w) + 1 > n: lines.append(cur); cur = w
        else: cur = (cur + " " + w).strip()
    lines.append(cur)
    y0 = y + 19 - (len(lines) - 1) * 6.5
    for i, l in enumerate(lines): s.text(x, y0 + i * 13, l, C.SUB, 11)

for r in ROWS:
    if r[1] is None:
        y += 8
        s.text(X_STATE, y + 12, r[0], C.SUB, 12, weight="bold")
        s.line(X_STATE, y + 18, W - 20, y + 18)
        y += 22
        continue
    state, ind, play, stop, review, save, itip, stip = r
    s.text(X_STATE, y + 19, state, C.TXT, 12.5)
    toolbar(X_TB, y, ind, play, stop, review, save)
    wrap(X_TIP, y, itip); wrap(X_SAVETIP, y, stip)
    y += 40

y += 10
s.note(X_STATE, y, [
    "THE RULES (AQ108 §0 — switch now, R2″ Live ends in two steps)",
    "• The indicator shows the mode you ARE in (a button with ▾); choosing a mode switches at once — leaving Edit with unsaved edits asks Save / Discard / Cancel.",
    "• ■ means what it will do: Preview → back to Edit · Live running → 'End the exercise?' (results) · Live ended → ⏏ 'Close the session?' (→ Idle) · DAR → ⏏ back to live · AAR → its start.",
    "• ⏪ Review (built, LC-16): only in Live; the same DAR choices as the indicator's menu (one list). Greyed during the briefing — 'nothing to review yet'.",
    "• ▶'s GLYPH wears the indicator's colour (violet · Live RED recorded / GREEN not · amber DAR · blue AAR); greyed in Live · ended. ▾ presets: Edit and the briefing only.",
    "• Save: the floppy in Edit (greyed with its reason when not saveable, and in Preview); the AMBER FLAG (take a checkpoint) in Live / DAR / AAR.",
    "• Live · ended is derived (OperatingLive + the exercise result Ended) — the checkered flag; nothing restarts by itself.",
], C.SUB, 11.5)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "run-indicator-visual.svg")
s.save(out)
print("wrote", os.path.normpath(out))
