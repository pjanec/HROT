#!/usr/bin/env python3
"""Storyboard: a live training session from the Start screen to the debrief and back (AQ108 §0, user 2026-11-08).

Scope: the Start screen (Idle), the toolbar's run controls (indicator · ▶ · ■ · Save) and the two confirmations. The map, the panels and the
menus are drawn as context only — unchanged.

    python3 scripts/gen-live-session-storyboard.py   # writes docs/UX/live-session-storyboard.svg
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA

STOP, PAUSE, CARET, FLOPPY, CLOSE, CHECKERED, CIRCLE = 0xf04d, 0xf04c, 0xf0d7, 0xf0c7, 0xf2f5, 0xf11e, 0xf111
YELLOW, RED, AMB, VIO, BLU = "#FFE14D", "#E63838", "#FFB333", C.VIOLET, C.BLUE

PW, PH = 620, 300          # one panel
GX, GY = 30, 64            # grid origin
COLS = 2
W = GX * 2 + PW * COLS + 40
H = 64 + 4 * (PH + 76) + 40
s = Svg(W, H, "A live training session — from the Start screen to the debrief and back",
        "Scope: the Start screen, the run controls (indicator · ▶ · ■ · Save) and the two questions. The map and panels are context — unchanged.")


def panel(i, step, caption):
    col, row = i % COLS, i // COLS
    x = GX + col * (PW + 40)
    y = GY + row * (PH + 76)
    s.text(x, y + 14, step, C.TXT, 13.5, weight="bold")
    s.rect(x, y + 22, PW, PH, C.BG, stroke=C.BORDER, rx=4)
    words, lines, cur = caption.split(" "), [], ""
    for w_ in words:
        if len(cur) + len(w_) + 1 > 92: lines.append(cur); cur = w_
        else: cur = (cur + " " + w_).strip()
    lines.append(cur)
    for k, l in enumerate(lines): s.text(x, y + PH + 40 + k * 15, l, C.SUB, 11.5)
    return x, y + 22


def toolbar(x, y, ind, ind_col, play, play_col, play_dis, stop_glyph, stop_col, stop_dis, save, save_col, save_dis, label=None, blink=False):
    """The editor's main toolbar (context: File/Edit/View menus) with the run controls."""
    s.rect(x, y, PW, 30, "#16161B")
    s.text(x + 10, y + 19, "File   Edit   View   Scenario", C.NOTE, 11.5)
    bx = x + 250
    # the indicator — a glyph BUTTON with ▾ (D29)
    s.rect(bx, y + 4, 34, 22, C.NEUTRAL, rx=3)
    s.glyph(ind, bx + 4, y + 7, ind_col, 15)
    s.glyph(CARET, bx + 23, y + 10, C.SUB, 8)
    if blink:
        s.text(bx + 17, y + 42, "blinks", C.NOTE, 9.5, anchor="middle")
    # ▶ — the glyph wears the indicator's colour
    s.rect(bx + 40, y + 4, 26, 22, C.BTN, rx=3, op=0.55 if play_dis else 1)
    s.glyph(play, bx + 47, y + 8, play_col, 13, op=0.4 if play_dis else 1)
    # ■ — End the exercise / Close the session
    s.rect(bx + 70, y + 4, 26, 22, C.BTN, rx=3, op=0.55 if stop_dis else 1)
    s.glyph(stop_glyph, bx + 76.5, y + 8, stop_col, 13, op=0.4 if stop_dis else 1)
    s.text(bx + 104, y + 19, "00:00", C.SUB, 11) if label is None else s.text(bx + 104, y + 19, label, C.SUB, 11)
    # Save
    s.rect(x + PW - 40, y + 4, 28, 22, C.BTN, rx=3, op=0.55 if save_dis else 1)
    s.glyph(save, x + PW - 33, y + 8, save_col, 13, op=0.4 if save_dis else 1)


def mapview(x, y, w=PW, h=PH - 30, dim=False):
    s.rect(x, y, w, h, "#20262B")
    for (ux, uy, c) in ((120, 90, C.BLUE), (160, 120, C.BLUE), (420, 150, C.RED), (460, 110, C.RED), (300, 200, C.GREEN)):
        s.rect(x + ux * w / PW, y + uy * h / (PH - 30), 14, 10, c, rx=2, op=0.35 if dim else 0.9)
    s.text(x + 10, y + h - 10, "the map (context)", C.NOTE, 10)


def modal(x, y, w, title, body, buttons):
    s.rect(x, y, w, 26 + 18 * len(body) + 40, C.POPUP, stroke=C.SEP, rx=4)
    s.rect(x, y, w, 22, C.TITLE, rx=4)
    s.text(x + 10, y + 15, title, C.TXT, 12)
    for i, l in enumerate(body):
        s.text(x + 12, y + 42 + i * 18, l, C.SUB, 11.5)
    bx = x + 12
    by = y + 26 + 18 * len(body) + 10
    for i, b in enumerate(buttons):
        bw = 14 + len(b) * 7
        s.rect(bx, by, bw, 22, C.BTNSEL if i == 0 else C.BTN, rx=3)
        s.text(bx + bw / 2, by + 15, b, C.TXT, 11.5, anchor="middle")
        bx += bw + 8


# ① Idle — the Start screen
x, y = panel(0, "① IDLE — the Start screen (nothing loaded)",
             "The editor UI waits behind the Start screen; run controls hidden. Recent = one click back into a scenario, already in the mode you pick.")
s.rect(x, y, PW, PH, "#14181D")
s.text(x + PW / 2, y + 40, "HROT", C.TXT, 22, anchor="middle", weight="bold")
for i, (lbl, g) in enumerate((("New scenario…", FA.PLUS), ("Open scenario…", FA.FOLDER_OPEN), ("Open recording…", FA.CLOCK_BACK))):
    s.button(x + 40, y + 70 + i * 30, 180, lbl, glyph=g, gc=C.SUB)
s.text(x + 260, y + 66, "Recent", C.NOTE, 11.5)
recent = (("Night attack", "Live · 10 min ago · exercise 3"), ("Bridgehead", "edited yesterday"), ("Crossfire", "Live · 2 days ago"))
for i, (n, sub) in enumerate(recent):
    ry = y + 74 + i * 52
    s.rect(x + 250, ry, 350, 46, C.NEUTRAL, rx=4, stroke=C.BORDER)
    s.text(x + 272, ry + 18, n, C.TXT, 12.5)
    s.text(x + 272, ry + 35, sub, C.NOTE, 10.5)
    s.rect(x + 440, ry + 12, 66, 22, C.BTN, rx=3); s.glyph(CIRCLE, x + 447, ry + 17, RED, 11); s.text(x + 462, ry + 27, "Live", C.TXT, 11)
    s.rect(x + 512, ry + 12, 66, 22, C.BTN, rx=3); s.glyph(FA.PEN, x + 519, ry + 17, YELLOW, 11); s.text(x + 535, ry + 27, "Edit", C.TXT, 11)

# ② Edit
x, y = panel(1, "② EDIT — Recent ▸ Edit (or Open scenario)",
             "The full editor appears; the pen says your edits are saved. Indicator ▾ → Live: asks Save / Discard / Cancel, then loads Live.")
toolbar(x, y, FA.PEN, YELLOW, FA.PLAY, VIO, False, STOP, C.SUB, True, FLOPPY, C.TXT, False)
mapview(x, y + 30)
s.menu(x + 250, y + 30, 230, [("Edit", "✓", FA.PEN, YELLOW), ("Live", None, CIRCLE, RED), ("AAR — open a recording…", None, FA.PLAY, BLU), "---", ("Close", None, CLOSE, C.SUB)])

# ③ Live — briefing
x, y = panel(2, "③ LIVE · BRIEFING — loaded, paused at 0:00 (the exercise exists, recording armed)",
             "Trainees mount and listen. ▶ starts the exercise (or the run setting 'Start immediately' skips this pause).")
toolbar(x, y, CIRCLE, RED, FA.PLAY, RED, False, STOP, C.SUB, True, FA.FLAG, AMB, False, label="00:00  Live · recorded")
mapview(x, y + 30)

# ④ Live running
x, y = panel(3, "④ LIVE · RUNNING",
             "■ = END THE EXERCISE (a square). DAR looks back inside the run; Save = take a checkpoint (the flag).")
toolbar(x, y, CIRCLE, RED, PAUSE, RED, False, STOP, C.TXT, False, FA.FLAG, AMB, False, label="12:31", blink=True)
mapview(x, y + 30)
s.text(x + 324, y + 62, "■ End the exercise", C.NOTE, 10.5)
s.line(x + 336, y + 50, x + 333, y + 28, C.NOTE)

# ⑤ the end question
x, y = panel(4, "⑤ ■ — the first question",
             "Results are computed: objectives close, each side's outcome is written (SR-116). Nothing unloads.")
toolbar(x, y, CIRCLE, RED, PAUSE, RED, False, STOP, C.TXT, False, FA.FLAG, AMB, False, label="12:31")
mapview(x, y + 30, dim=True)
modal(x + 110, y + 70, 400, "End the exercise?",
      ["The objectives are evaluated and the results shown.", "The world stays paused at the end for the debrief.", "Keep running cancels."],
      ["End the exercise", "Keep running"])

# ⑥ Live ended — debrief
x, y = panel(5, "⑥ LIVE · ENDED — debrief on the frozen world (still Live, NOT an AAR)",
             "Indicator = the checkered flag. ▶ greyed (the exercise is over). ■ CHANGED SHAPE: ⏏ Close the session. Indicator ▾ offers what's next.")
toolbar(x, y, CHECKERED, RED, FA.PLAY, RED, True, CLOSE, C.TXT, False, FA.FLAG, AMB, False, label="12:58  ended")
mapview(x, y + 30, dim=True)
s.rect(x + 20, y + 50, 250, 150, C.POPUP, stroke=C.SEP, rx=4)
s.text(x + 30, y + 70, "Exercise result", C.TXT, 12.5, weight="bold")
s.text(x + 30, y + 92, "Blue — mission accomplished", C.GREEN, 11.5)
s.text(x + 30, y + 110, "Score 82  ·  pass 70  ·  GO", C.SUB, 11.5)
s.text(x + 30, y + 132, "Hold the bridge          GO", C.SUB, 11)
s.text(x + 30, y + 150, "No civilian harm          GO", C.SUB, 11)
s.text(x + 30, y + 168, "Report contact < 30 s    NO-GO", C.RED, 11)
s.text(x + 30, y + 190, "(Mission summary / trainee debrief)", C.NOTE, 10)
s.menu(x + 290, y + 30, 312, [("Run again (same scenario → briefing)", None, CIRCLE, RED), ("Restart from a checkpoint ▸", None, FA.FLAG, AMB),
                              ("DAR — look back…", None, FA.CLOCK_BACK, AMB), ("AAR — full replay of this exercise", None, FA.PLAY, BLU), "---",
                              ("Close the session", None, CLOSE, C.SUB)])

# ⑦ Close question
x, y = panel(6, "⑦ ⏏ — the second question (really leaving Live)",
             "The recording is kept. The cluster unloads Live and goes to Idle on every node.")
toolbar(x, y, CHECKERED, RED, FA.PLAY, RED, True, CLOSE, C.TXT, False, FA.FLAG, AMB, False, label="12:58  ended")
mapview(x, y + 30, dim=True)
modal(x + 110, y + 70, 400, "Close the live session?",
      ["Exercise 3 of Night attack is kept (12:58).", "Every station returns to the Start screen."],
      ["Close the session", "Cancel"])

# ⑧ back to Idle
x, y = panel(7, "⑧ IDLE again — repeat in one click",
             "Recent ▸ Night attack ▸ Live = straight into the briefing (③), no scenario picking. Or Edit, or another scenario, or AAR via Open recording.")
s.rect(x, y, PW, PH, "#14181D")
s.text(x + PW / 2, y + 40, "HROT", C.TXT, 22, anchor="middle", weight="bold")
for i, (lbl, g) in enumerate((("New scenario…", FA.PLUS), ("Open scenario…", FA.FOLDER_OPEN), ("Open recording…", FA.CLOCK_BACK))):
    s.button(x + 40, y + 70 + i * 30, 180, lbl, glyph=g, gc=C.SUB)
s.text(x + 260, y + 66, "Recent", C.NOTE, 11.5)
ry = y + 74
s.rect(x + 250, ry, 350, 46, C.NEUTRALSEL, rx=4, stroke="#C8C8D4")
s.text(x + 272, ry + 18, "Night attack", C.TXT, 12.5)
s.text(x + 272, ry + 35, "Live · just now · exercise 3", C.NOTE, 10.5)
s.rect(x + 440, ry + 12, 66, 22, C.BTNSEL, rx=3); s.glyph(CIRCLE, x + 447, ry + 17, RED, 11); s.text(x + 462, ry + 27, "Live", C.TXT, 11)
s.rect(x + 512, ry + 12, 66, 22, C.BTN, rx=3); s.glyph(FA.PEN, x + 519, ry + 17, YELLOW, 11); s.text(x + 535, ry + 27, "Edit", C.TXT, 11)
s.text(x + 272, ry + 70, "+ the AAR of exercise 3:  Open recording… (top of its list)", C.NOTE, 10.5)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "live-session-storyboard.svg")
s.save(out)
print("wrote", os.path.normpath(out))
