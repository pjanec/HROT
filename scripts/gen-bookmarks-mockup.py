# Generates docs/UX/recording-marks-visual.svg — DESIGN_Bookmarks_And_Notes.md (E7, CE-4325): the mark popup (B in Live), the timeline's ⚑ with a
# note dot and its tooltip, the Marks window (Live / DAR / AAR). Scope: only these; the main bar, the map and the rest of the timeline are context.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

FLAG, NOTE, SEEK, REST, PENCIL, USERG = 0xf024, 0xf249, 0xf064, 0xf2ea, 0xf304, 0xf007
W, H = 1240, 714
s = Svg(W, H, "Bookmarks and notes — ⚑ mark a moment in Live, find it in the review (DESIGN_Bookmarks_And_Notes.md)")

# ── ① Live: the main bar (context) + the timeline with marks ─────────────────────────────────────────────
Y = 62
s.separator_text(20, Y, "① Live — B marks the moment at once; the ⚑ appears on every station's timeline", W - 20)
X0, Y0, SW = 20, Y + 24, 1200
s.rect(X0, Y0, SW, 30, "#1E1E22")
mx = X0 + 10
for m in ["File", "Edit", "View", "Scenario"]:
    s.text(mx, Y0 + 20, m, C.TXT, 12.5); mx += text_width(m, 12.5) + 18
s.rect(mx + 20, Y0 + 5, 34, 21, C.BTN, rx=3); s.o.append(f"<circle cx='{mx + 33}' cy='{Y0 + 15}' r='5' fill='{C.GREEN}'/>")
s.text(mx + 62, Y0 + 20, "Live · Night attack · 12:31.4", C.SUB, 12)
s.text(X0 + SW - 10, Y0 + 20, "context: AQ108 §0, unchanged", C.NOTE, 11, "end")

TY = Y0 + 40
s.rect(X0, TY, SW, 120, C.BG, stroke=C.BORDER); s.rect(X0, TY, SW, 20, C.TITLE); s.text(X0 + 8, TY + 14, "Event Timeline")
# ★ R-4177 — the corner: ⚑ (mark now · Add note here in AAR) and S / W (the axis toggle, moved from the main toolbar)
cx = X0 + SW - 58
s.rect(cx, TY + 2, 24, 16, C.BTN, rx=3); s.glyph(FLAG, cx + 6, TY + 4, "#60C0F0", 11)
s.rect(cx + 28, TY + 2, 24, 16, C.BTN, rx=3); s.text(cx + 40, TY + 14, "S", C.TXT, 11.5, "middle")
rx0, rx1 = X0 + 110, X0 + SW - 14
s.rect(rx0, TY + 26, rx1 - rx0, 22, "#16161B")
for i in range(7):
    xx = rx0 + (rx1 - rx0) * i / 6.2; s.text(xx, TY + 60, f"{i * 3}:00", C.DIS, 11)
marks = [(0.18, False, False), (0.41, True, True), (0.55, True, False), (0.67, False, False)]
for f, named, noted in marks:
    xx = rx0 + (rx1 - rx0) * f
    s.glyph(FLAG, xx - 5, TY + 29, "#60C0F0", 12)
    if noted: s.o.append(f"<circle cx='{xx + 9}' cy='{TY + 31}' r='3.5' fill='{C.AMBER}'/>")
for i, (name, col, evs) in enumerate([("Shot", C.AMBER, [0.39, 0.45, 0.62]), ("Hit", C.ORANGE, [0.42, 0.63])]):
    yy = TY + 70 + i * 22
    s.text(X0 + 12, yy + 13, name, col, 11.5); s.rect(rx0, yy + 2, rx1 - rx0, 18, "#16161B")
    for f in evs: s.o.append(f"<circle cx='{rx0 + (rx1 - rx0) * f}' cy='{yy + 11}' r='4' fill='{col}'/>")
s.line(rx0 + (rx1 - rx0) * 0.67, TY + 24, rx0 + (rx1 - rx0) * 0.67, TY + 116, C.RED, sw=1.5)
tx = rx0 + (rx1 - rx0) * 0.41
s.tooltip(tx + 12, TY + 40, 380, ["⚑ Bridge crossed · 7:43", "\"Alpha 1-2 crossed without cover — discuss\"",
                                 "Alpha 1-2 · Instructor-1"], edge="#60C0F0")
s.note(X0, TY + 136, ["⚑ = a checkpoint (built) · the amber dot = it has a note · hover: name, note, unit, who · double-click: seek (in DAR / AAR)",
                    "the title bar's corner: ⚑ = mark now (B), in AAR Add note here · S / W = the sim / wall axis, MOVED here from the main toolbar (R-4177)"], C.NOTE, 11)

# ── ② the mark popup ───────────────────────────────────────────────────────────────────────────────────
Y2 = TY + 184
s.separator_text(20, Y2, "② B — the moment is taken at the key press; this asks only for the optional words", 600)
PX, PY, PW, PH = 20, Y2 + 24, 580, 196
s.rect(PX + 4, PY + 4, PW, PH, "#000000", op=0.35, rx=3)
s.rect(PX, PY, PW, PH, C.POPUP, stroke="#60C0F0", rx=3)
s.glyph(FLAG, PX + 12, PY + 12, "#60C0F0", 14); s.text(PX + 34, PY + 25, "Marked 12:31.4", C.TXT, 13)
s.text(PX + PW - 12, PY + 25, "closes by itself in 4 s — the mark stays", C.NOTE, 11, "end")
s.form_row(PX + 14, PY + 42, "Name", 70, "field", PW - 100, "Bridge crossed")
s.text(PX + 14, PY + 82, "Note", C.TXT)
s.rect(PX + 84, PY + 70, PW - 100, 52, C.FRAME); s.text(PX + 90, PY + 86, "Alpha 1-2 crossed without cover — discuss", C.TXT)
s.form_row(PX + 14, PY + 132, "Unit", 70, "combo", 240, "Alpha 1-2", glyph=USERG, gc=C.BLUE)
s.text(PX + 340, PY + 146, "the selected unit, pre-filled", C.NOTE, 11)
s.button(PX + PW - 100, PY + PH - 30, 86, "Done")
s.note(PX, PY + PH + 18, ["Esc / ignoring it keeps an unnamed mark (\"Mark 3 · 12:31\")", "B is greyed in an unrecorded Live: \"no recording — nothing to mark\""], C.NOTE, 11)

# ── ③ the Marks window ─────────────────────────────────────────────────────────────────────────────────
s.separator_text(620, Y2, "③ Marks — every host with a timeline (Live · DAR · AAR)", W - 20)
MX, MY, MW, MH = 620, Y2 + 24, 600, 320
s.window(MX, MY, MW, MH, "Marks — Night attack · exercise 3")
s.field(MX + 8, MY + 28, 200, "Search marks…", hint=True); s.combo(MX + 216, MY + 28, 150, "Everyone", glyph=USERG, gc=C.GREY)
s.button(MX + MW - 128, MY + 28, 120, "Add note here", NOTE, C.AMBER)
hy = MY + 58
for lab, x in [("time", 14), ("name · note", 80), ("unit", 360), ("who", 470)]:
    s.text(MX + x, hy + 12, lab, C.NOTE, 11)
ry = hy + 20
rows = [(FLAG, "#60C0F0", "3:18", "Mark 1", "", "—", "Instructor-1", False),
        (FLAG, "#60C0F0", "7:43", "Bridge crossed", "Alpha 1-2 crossed without cover — discuss", "Alpha 1-2", "Instructor-1", True),
        (FLAG, "#60C0F0", "10:12", "Contact east", "", "Bravo 2-3", "Observer-2", False),
        (NOTE, C.AMBER, "14:02", "(note)", "good use of smoke here", "Alpha 1", "Instructor-1 · in the AAR", False)]
for g, gc, t, name, note, unit, who, sel in rows:
    if sel: s.rect(MX + 6, ry - 3, MW - 12, 44, "#1E2A3E")
    s.glyph(g, MX + 14, ry + 3, gc, 12); s.text(MX + 32, ry + 14, t, C.TXT, 12)
    s.text(MX + 80, ry + 14, name, C.TXT, 12.5); s.text(MX + 80, ry + 31, note or "—", C.SUB if note else C.DIS, 11)
    s.text(MX + 360, ry + 14, unit, C.TXT if unit != "—" else C.DIS, 12); s.text(MX + 470, ry + 14, who, C.SUB, 11)
    ry += 48
s.note(MX + 10, MY + MH - 30, ["click: seek there (and select the unit) · right-click: Edit note · Restart from here (a ⚑ only) · Delete the note"], C.NOTE, 11)
s.note(MX, MY + MH + 18, ["⚑ = a mark taken in Live (a checkpoint)", "the amber note = added in the AAR: a time only, not restorable (R-4168) — Q2"], C.NOTE, 11)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "recording-marks-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
