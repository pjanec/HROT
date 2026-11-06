# Generates docs/UX/replay-forms-visual.svg — the entity forms in the REPLAY BROWSER, read-only (DESIGN_Details_Forms.md §2.4, R-4148):
# grey disabled rows (no banner, no hover tooltip — R-4150), "not recorded", what a seek does, the Brain tab's Character, Copy value.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 740
s = Svg(W, H, "Entity forms in the Replay Browser — read-only (house style; DESIGN_Details_Forms.md §2.4, R-4148)")
REC = "#3A2E4A"; RECFG = "#C9A6F5"     # the 'Recording' chip: violet, the replay family (not the editor's green Edit chip)

def details(x, y, w, t_label, rows, tab=0, chip=True, seek_mark=()):
    h = 64 + sum(24 if r[0] == '#' else 25 for r in rows)
    s.window(x, y, w, h, "Details — Alpha 1-1 (Rifleman, Friend)")
    if chip:
        cw = text_width("Recording", 11) + 34
        s.rect(x + w - cw - 8, y + 2, cw, 16, REC, rx=8); s.glyph(FA.VIDEO, x + w - cw, y + 4.5, RECFG, 10); s.text(x + w - cw + 16, y + 14, "Recording", RECFG, 11)
    s.tabs(x + 8, y + 26, w - 16, [("Unit", FA.USER, C.BLUE), ("Crew & cargo", FA.VAN, C.ORANGE), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)], tab)
    LX = x + 14; LW = 130; CX = LX + LW; CW = w - 28 - LW - 22; yy = y + 58
    for r in rows:
        if r[0] == "#":
            s.separator_text(LX, yy, r[1], x + w - 14); yy += 24; continue
        label, kind, val = r[:3]
        if label in seek_mark: s.rect(x + 6, yy - 3, w - 12, 25, "#1E2A3E")
        s.text(LX, yy + 14, label, C.DIS)
        if kind == "field": s.field(CX, yy, CW, val, dis=True)
        elif kind == "combo": s.combo(CX, yy, CW, val, dis=True)
        elif kind == "glyphcombo": s.combo(CX, yy, CW, val[0], dis=True, glyph=val[1], gc=val[2])
        elif kind == "slider": s.slider(CX, yy, CW, val[0], val[1], dis=True)
        elif kind == "check": s.checkbox(CX, yy, val, dis=True)
        elif kind == "none":
            s.text(CX + 4, yy + 14, "not recorded", "#6E6E78", 12); s.glyph(FA.CIRCLE_XMARK, CX + 84, yy + 2.5, "#55555E", 11)
        if label in seek_mark: s.glyph(FA.CLOCK_BACK, x + w - 30, yy + 2, C.INFO, 11)
        yy += 25
    return yy

# ───────────── A: the Replay Browser's Details, read-only ─────────────
s.separator_text(20, 64, "A · The Replay Browser's Details panel — the SAME forms, read-only", W - 20)
# a slim replay timeline above it (the browser's own; drawn only for context)
TX, TY = 20, 92
s.rect(TX, TY, 560, 30, "#16161B", stroke=C.BORDER)
s.glyph(FA.PLAY, TX + 10, TY + 8, C.GREEN, 12); s.text(TX + 32, TY + 20, "00:12:34", C.TXT, 12)
s.rect(TX + 100, TY + 13, 440, 4, "#2A2A33", rx=2); s.rect(TX + 100, TY + 13, 190, 4, C.ACC, rx=2); s.rect(TX + 287, TY + 8, 6, 14, C.TXT, rx=1)
rowsA = [("#", "Identity"), ("Name", "field", "Alpha 1-1"), ("Side", "glyphcombo", ("Friend", FA.SHIELD, C.BLUE)), ("Type", "field", "Rifleman (US)"),
         ("#", "State"), ("Posture", "combo", "Crouched"), ("Strength", "slider", (0.75, "75 %")), ("Rounds, M4", "none", None),
         ("Weapon", "combo", "Aimed"), ("Aboard", "field", "Truck 2 · seat 3 (passenger)"), ("Playable", "check", True)]
end = details(20, 134, 560, "00:12:34", rowsA)
s.note(20, end + 26, ["Grey says it: no banner, no tooltip (R-4150) — the Recording chip, the time axis and",
                      "the main bar's clock already say where and when. No Apply / Revert / Undo, no pickers.",
                      "'not recorded': the row's source is not in this recording (DataPolicy) —",
                      "never a blank or a default that looks like a value."], C.SUB, 12)

# ───────────── B: a seek ─────────────
s.separator_text(640, 64, "B · Seek — the values follow the playhead", W - 20)
rowsB = [("#", "State"), ("Posture", "combo", "Prone"), ("Strength", "slider", (0.40, "40 %")), ("Weapon", "combo", "At ease"),
         ("Aboard", "field", "—")]
end = details(640, 92, 580, "00:14:02", rowsB, seek_mark=("Posture", "Strength", "Weapon", "Aboard"))
s.note(640, end + 26, ["After a seek, each row whose value changed is tinted for ~1 s and shows",
                       "the clock glyph (Q-R1) — the eye finds what the jump changed.",
                       "During normal play nothing flashes: values just update (0 B per frame)."], C.SUB, 12)

# ───────────── C: Brain tab + copy ─────────────
s.separator_text(640, 372, "C · The Brain tab's Character, and copying a value", W - 20)
s.window(640, 400, 580, 175, "Details — Alpha 1-1 (Rifleman, Friend)")
s.tabs(648, 426, 564, [("Unit", FA.USER, C.BLUE), ("Crew & cargo", FA.VAN, C.ORANGE), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)], 3)
s.separator_text(654, 458, "Character", 1206)
LX, CX = 654, 784
for i, (lab, kind, val) in enumerate([("Skill", "slider", (0.55, "Regular · from the unit")), ("Aggressiveness", "slider", (0.7, "Bold")), ("Doing", "field", "Move to OBJ Bridge · step 2 of 4")]):
    yy = 484 + i * 25
    s.text(LX, yy + 14, lab, C.DIS)
    if kind == "slider": s.slider(CX, yy, 410, val[0], val[1], dis=True)
    else: s.field(CX, yy, 410, val, dis=True)
s.menu(1010, 520, 180, [("Copy value", "Ctrl+C", FA.COPY, C.GREY), ("Copy the whole form", None, FA.PASTE, C.GREY), "---",
                       ("Edit…", None, FA.PEN, C.GREY, "a recording")])
s.note(640, 616, ["Right-click a row: Copy value / Copy the whole form (text, for a report) — the only",
                  "actions in read-only (Q-R3). Edit is listed greyed with its reason, like every menu (R-4132)."], C.SUB, 12)

# ───────────── D: what is NOT replay-specific ─────────────
s.separator_text(20, 680, "D · One rule, every host", W - 20)
s.note(20, 706, ["Editable = ONE function of (run state, edit mode, authority): Replay ⇒ read-only, the same as the editor's Review mode.",
                 "The chip names the reason: Edit mode (green) · Live (amber) · Review (blue) · Recording (violet). The form has no 'if replay'."], C.SUB, 12)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "replay-forms-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
