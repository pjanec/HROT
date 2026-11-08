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
    ("EDIT, ▶ armed PREVIEW — ▶ runs a copy of exactly what you edit", None),
    ("Edit · saveable · Preview armed", (FA.PEN, YELLOW, 1.0, ""), (FA.PLAY, VIO, False), (FLOPPY, C.TXT, False),
     "Editing — your changes save to Night attack. ▶ previews them (click to change)", "Save scenario (Ctrl+S)"),
    ("EDIT, ▶ armed LIVE / AAR — ▶ LEAVES editing (it asks to save first)", None),
    ("Edit · Live armed (recorded)", (0xf111, RED, DIM, ""), (FA.PLAY, RED, False), (FLOPPY, C.TXT, False),
     "▶ will start: Live, recorded — from the SAVED scenario; unsaved edits: Save and go live / Cancel", "Save scenario (Ctrl+S)"),
    ("Edit · Live armed (not recorded)", (0xf111, GRN, DIM, ""), (FA.PLAY, GRN, False), (FLOPPY, C.TXT, False),
     "▶ will start: Live, not recorded — from the SAVED scenario; unsaved edits: Save and go live / Cancel", "Save scenario (Ctrl+S)"),
    ("Edit · AAR armed", (FA.PLAY, BLU, DIM, ""), (FA.PLAY, BLU, False), (FLOPPY, C.TXT, False),
     "▶ will open the last exercise's recording — unsaved edits: Save / Discard / Cancel", "Save scenario (Ctrl+S)"),
    ("EDIT — NOT saveable (a variant preset is loaded: some entities may be missing)", None),
    ("Edit · variant world · ▶ armed Preview", (FA.EYE, VIO, DIM, ""), (FA.PLAY, VIO, False), (FLOPPY, C.TXT, True),
     "▶ will start: Preview (click to change) — not saveable: built with variant 3", "Not saved: this world was built with variant 3 — load the scenario as authored to save"),
    ("Edit · variant world · ▶ armed Live (recorded)", (0xf111, RED, DIM, ""), (FA.PLAY, RED, False), (FLOPPY, C.TXT, True),
     "▶ will start: Live (click to change) — not saveable: built with variant 3", "Not saved: this world was built with variant 3 — load the scenario as authored to save"),
    ("RUNNING — the world is a run, never the scenario", None),
    ("Preview running (indicator blinks)", (FA.EYE, VIO, 1.0, ""), (PAUSE, VIO, False), (FLOPPY, C.TXT, True),
     "Preview — a copy of the edit world; ■ returns to it", "Not saved: a preview runs a copy — ■ to return to editing"),
    ("Live, recorded (blinks while running)", (0xf111, RED, 1.0, ""), (FA.PLAY, RED, False), (FA.FLAG, AMB, False),
     "Live — recorded", "Take a checkpoint (Ctrl+S) — a live run never saves the scenario"),
    ("Live, not recorded (blinks while running)", (0xf111, GRN, 1.0, ""), (FA.PLAY, GRN, False), (FA.FLAG, AMB, False),
     "Live — not recorded", "Take a checkpoint (Ctrl+S) — a live run never saves the scenario"),
    ("DAR (review)", (FA.CLOCK_BACK, AMB, 1.0, ""), (FA.PLAY, AMB, False), (FA.FLAG, AMB, False),
     "DAR — 2 min behind live", "Take a checkpoint (Ctrl+S)"),
    ("AAR (replay)", (FA.PLAY, BLU, 1.0, ""), (FA.PLAY, BLU, False), (FA.FLAG, AMB, False),
     "AAR", "Take a checkpoint (Ctrl+S)"),
]

W = 1330
H = 96 + sum(30 if r[1] is None else 40 for r in ROWS) + 180
s = Svg(W, H, "The run indicator, ▶ and Save — every state",
        "Scope: the mode indicator, ▶ (+ ▾) and Save. The rest of the toolbar is context — unchanged. Grey text is annotation.")

X_STATE, X_TB, X_TIP, X_SAVETIP = 20, 300, 490, 900
y = 64
s.text(X_STATE, y, "state", C.NOTE, 11.5); s.text(X_TB, y, "indicator · ▶ ▾ · Save", C.NOTE, 11.5)
s.text(X_TIP, y, "indicator tooltip", C.NOTE, 11.5); s.text(X_SAVETIP, y, "Save tooltip", C.NOTE, 11.5)
y += 14

def toolbar(x, y, ind, play, save):
    s.rect(x, y, 170, 30, C.BG, stroke=C.BORDER, rx=3)
    g, col, a, mark = ind                                 # the indicator
    s.glyph(g, x + 10, y + 7, col, 16, op=a)
    pg, pc, pd = play                                      # ▶ — the GLYPH takes the colour, never the background
    s.rect(x + 40, y + 4, 26, 22, C.BTN, rx=3)
    s.glyph(pg, x + 47, y + 8, pc, 14, op=0.55 if pd else 1)
    s.rect(x + 66, y + 4, 12, 22, C.BTN, rx=3)             # ▾ — the preset picker
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
    "THE RULES",
    "• ARMING never leaves Edit and asks nothing (AQ108 §3) — ▶ does both: it asks about unsaved edits, then leaves.",
    "• The indicator shows the ARMED mode (dim) in Edit, the RUNNING mode (bright; blinking while time runs) in a run — as today.",
    "  ONE exception: Edit + PREVIEW armed + saveable (variant 0 · cluster in Edit · not in error) ⇒ the YELLOW PEN: ▶ runs what you edit.",
    "  With Live / AAR armed ▶ leaves editing — the dim armed glyph says so, and ▶ asks to save first (AQ108 §3). Click opens the mode menu.",
    "• ▶'s GLYPH is always in the indicator's colour (violet Preview · Live RED when recorded / GREEN when not · blue AAR · amber DAR) — never the background.",
    "  ⚠ Fix: today ▶ uses plain Live green even when runs are recorded; it will read the indicator's colour (one source).",
    "  With the pen showing (Preview armed), ▶ is violet. (LC-14 PlayColour, built.)",
    "• Save: the floppy saves the scenario in Edit whatever is armed; greyed with its reason in Edit-not-saveable and Preview;",
    "  in Live / DAR / AAR it becomes the AMBER FLAG — the checkpoint glyph — because Ctrl+S there takes a checkpoint.",
    "• Save on / off is ONE answer (IScenarioSession.SaveDisabledReason): the pen, the Save button and File ▸ Save read the same reason.",
], C.SUB, 11.5)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "run-indicator-visual.svg")
s.save(out)
print("wrote", os.path.normpath(out))
