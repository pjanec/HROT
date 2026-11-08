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

# (group, state, indicator (glyph, colour, alpha, mark), play (glyph, colour, dis), save (glyph, colour, dis), indicator tooltip, Save tooltip)
ROWS = [
    ("EDIT — the indicator shows the mode you ARE in; ▶ previews what you edit", None),
    ("Edit · saveable (variant 0, healthy cluster)", (FA.PEN, YELLOW, 1.0, ""), (FA.PLAY, VIO, False, True), (FLOPPY, C.TXT, False),
     "Editing — your changes save to Night attack. ▶ previews them. Click: switch mode", "Save scenario (Ctrl+S)"),
    ("Edit · HOVERED (button + hand cursor)", (FA.PEN, YELLOW, 1.0, "hover"), (FA.PLAY, VIO, False, True), (FLOPPY, C.TXT, False),
     "Editing — your changes save to Night attack. ▶ previews them. Click: switch mode", "Save scenario (Ctrl+S)"),
    ("Edit · a variant preset is loaded (not saveable)", (FA.EYE, VIO, 1.0, ""), (FA.PLAY, VIO, False, True), (FLOPPY, C.TXT, True),
     "Editing a variant (preview only) — not saveable: built with variant 3. Click: switch mode", "Not saved: this world was built with variant 3 — load the scenario as authored to save"),
    ("Preview running (blinks; ■ → Edit)", (FA.EYE, VIO, 1.0, ""), (PAUSE, VIO, False, False), (FLOPPY, C.TXT, True),
     "Preview — a copy of the edit world; ■ returns to it", "Not saved: a preview runs a copy — ■ to return to editing"),
    ("LIVE — switched to at the indicator: loaded at once, paused at the start", None),
    ("Live · paused at the start · recorded", (0xf111, RED, 1.0, ""), (FA.PLAY, RED, False, True), (FA.FLAG, AMB, False),
     "Live — recorded. ▶ starts the exercise. Click: switch mode", "Take a checkpoint (Ctrl+S) — a live run never saves the scenario"),
    ("Live · paused at the start · not recorded", (0xf111, GRN, 1.0, ""), (FA.PLAY, GRN, False, True), (FA.FLAG, AMB, False),
     "Live — not recorded. ▶ starts the exercise. Click: switch mode", "Take a checkpoint (Ctrl+S) — a live run never saves the scenario"),
    ("Live · running (blinks; ■ → start)", (0xf111, RED, 1.0, ""), (PAUSE, RED, False, False), (FA.FLAG, AMB, False),
     "Live — recorded, running", "Take a checkpoint (Ctrl+S)"),
    ("REVIEW — inside a live run (DAR) or a recording (AAR)", None),
    ("DAR (review)", (FA.CLOCK_BACK, AMB, 1.0, ""), (FA.PLAY, AMB, False, False), (FA.FLAG, AMB, False),
     "DAR — 2 min behind live", "Take a checkpoint (Ctrl+S)"),
    ("AAR (replay)", (FA.PLAY, BLU, 1.0, ""), (FA.PLAY, BLU, False, False), (FA.FLAG, AMB, False),
     "AAR — exercise 3 of Night attack", "Take a checkpoint (Ctrl+S)"),
]

W = 1330
H = 96 + sum(30 if r[1] is None else 40 for r in ROWS) + 195
s = Svg(W, H, "The run indicator, ▶ and Save — every state (switch now)",
        "Scope: the mode indicator, ▶ (+ ▾) and Save. The rest of the toolbar is context — unchanged. Grey text is annotation.")

X_STATE, X_TB, X_TIP, X_SAVETIP = 20, 300, 490, 900
y = 64
s.text(X_STATE, y, "state", C.NOTE, 11.5); s.text(X_TB, y, "indicator · ▶ ▾ · Save", C.NOTE, 11.5)
s.text(X_TIP, y, "indicator tooltip", C.NOTE, 11.5); s.text(X_SAVETIP, y, "Save tooltip", C.NOTE, 11.5)
y += 14

def toolbar(x, y, ind, play, save):
    s.rect(x, y, 170, 30, C.BG, stroke=C.BORDER, rx=3)
    g, col, a, mark = ind                                 # the indicator — the kit's glyph button (D29): neutral background, lighter on hover, ▾ = it opens a menu
    hov = mark == "hover"
    s.rect(x + 4, y + 4, 32, 22, C.NEUTRALSEL if hov else C.NEUTRAL, stroke="#C8C8D4" if hov else None, rx=3)
    s.glyph(g, x + 8, y + 7, col, 15, op=a)
    s.glyph(CARET, x + 26, y + 10, C.SUB, 8)
    pg, pc, pd, caret = play                               # ▶ — the GLYPH takes the colour, never the background
    s.rect(x + 40, y + 4, 26, 22, C.BTN, rx=3)
    s.glyph(pg, x + 47, y + 8, pc, 14, op=0.55 if pd else 1)
    if caret:                                              # ▾ — the preset picker: only where a load with a preset makes sense
        s.rect(x + 66, y + 4, 12, 22, C.BTN, rx=3)
        s.glyph(CARET, x + 68.5, y + 10, C.TXT, 10)
    sg, sc, sd = save                                      # Save
    s.rect(x + 130, y + 4, 28, 22, C.BTN, rx=3, op=0.55 if sd else 1)
    s.glyph(sg, x + 137, y + 8, sc, 14, op=0.4 if sd else 1)

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
    state, ind, play, save, itip, stip = r
    s.text(X_STATE, y + 19, state, C.TXT, 12.5)
    toolbar(X_TB, y, ind, play, save)
    wrap(X_TIP, y, itip); wrap(X_SAVETIP, y, stip)
    y += 40

y += 10
s.note(X_STATE, y, [
    "THE RULES (AQ108 §0, user 2026-11-08 — SWITCH NOW)",
    "• The indicator shows the mode you ARE in. Choosing a mode in its menu SWITCHES at once: leaving Edit with unsaved edits asks Save / Discard / Cancel first.",
    "  → Live: loads Live PAUSED at the start (▶ starts the exercise) · → AAR: opens the last exercise (or the picker) at its start · → Edit (Preview): reopens the scenario.",
    "• The indicator is a BUTTON (D29): the kit's glyph button (neutral background, lighter + framed on hover, hand cursor) with a small ▾ — it opens the mode menu.",
    "• The YELLOW PEN = Edit on a saveable world (variant 0 · not in error). A variant world in Edit: the violet eye, steady. ▶ in Edit is always the preview.",
    "• ▶'s GLYPH is in the indicator's colour (violet · Live RED recorded / GREEN not · amber DAR · blue AAR) — never the background.",
    "• ▾ (presets) only where a load with a preset makes sense: Edit (reopens with it) and Live paused at the start (reloads Live with it). Never while running, in DAR or AAR.",
    "• Save: the floppy in Edit (greyed with its reason when not saveable, and in Preview); the AMBER FLAG (= take a checkpoint) in Live / DAR / AAR.",
    "• A Live that is left without its clock ever running leaves NO recording (the master drops the empty exercise).",
    "• Save on / off is ONE answer (IScenarioSession.SaveDisabledReason): the pen, the Save button and File ▸ Save read the same reason.",
], C.SUB, 11.5)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "run-indicator-visual.svg")
s.save(out)
print("wrote", os.path.normpath(out))
