# Generates docs/UX/recording-storage-visual.svg — DESIGN_Recording_Storage.md §7.3 / §8: what every station SHOWS of the recordings'
# storage jobs (CE-4330): the status bar's transfer segment, the Transfers popover, the ⏏ Close the session question, the import dialog,
# the recording picker's location chips, the close guard. Scope: ONLY these; the rest of the editor (menus, the map, the status bar's
# source / seat text) is context, drawn unchanged.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

UP, DOWN, NAS, DISK, WARN, OK, CLOCK, CLOSE, ARCH = 0xf093, 0xf019, 0xf233, 0xf0a0, 0xf071, 0xf058, 0xf017, 0xf2f5, 0xf187
W, H = 1240, 1220
s = Svg(W, H, "Recording storage — what every station shows of archives and imports (DESIGN_Recording_Storage.md §7.3, §8)")


def bar(x, y, w, frac, col=C.ACC, h=6):
    s.rect(x, y, w, h, "#2A2A33", rx=3); s.rect(x, y, max(h, w * frac), h, col, rx=3)


def radio(x, y, on):
    s.o.append(f"<circle cx='{x + 8}' cy='{y + 8}' r='7' fill='{C.FRAME}'/>")
    if on: s.o.append(f"<circle cx='{x + 8}' cy='{y + 8}' r='3.5' fill='{C.ACC}'/>")


def dialog(x, y, w, h, title):
    s.rect(x + 4, y + 4, w, h, "#000000", op=0.35, rx=3)
    s.rect(x, y, w, h, C.POPUP, stroke=C.SEP, rx=3); s.rect(x, y, w, 20, C.TITLE); s.text(x + 8, y + 14, title)


# ── ① the STATUS BAR, every station ──────────────────────────────────────────────────────────────────────────────
Y = 62
s.separator_text(20, Y, "① The status bar — every station shows the SAME job (the replicated StorageJobs). Its transfer segment sits left of the seat · hat.", W - 20)
rows = [
    ("nothing running", None),
    ("archiving — this station asked", ("arch", "Archiving exercise 3 — 2 of 4 files · 1.2 / 3.1 GB", 0.39, "(yours)", "+1 queued")),
    ("archiving while a new Live runs", ("live", "Archiving exercise 3 — 3 of 4 files · slowed while Live runs", 0.7, None, None)),
    ("importing — another station asked", ("imp", "Importing exercise 5 — 1 of 2 files · 0.4 / 1.4 GB", 0.28, None, None)),
    ("an import finished", ("ready", "Exercise 5 ready", None, None, None)),
    ("an archive stopped part-way", ("inc", "Archive of exercise 2 incomplete — 3 of 4 files (SimHost-1 offline)", None, None, None)),
]
ry = Y + 26
for lab, spec in rows:
    s.text(20, ry + 15, lab, C.NOTE, 11.5)
    bx0, bw = 250, W - 270
    s.rect(bx0, ry, bw, 22, "#16161B", stroke=C.BORDER)
    s.text(bx0 + 8, ry + 15, "Live · Night attack · exercise 4" if spec and spec[0] == "live" else "Idle", C.SUB, 11.5)   # context
    s.text(bx0 + bw - 10, ry + 15, "Instructor · Observe", C.SUB, 11.5, "end")                                             # context (CE-4329)
    if spec:
        kind, txt, frac, yours, queued = spec
        tx = bx0 + 250
        g, col = {"arch": (UP, C.BLUE), "live": (UP, C.BLUE), "imp": (DOWN, C.TEAL), "ready": (OK, C.OK), "inc": (WARN, C.AMBER)}[kind]
        s.glyph(g, tx, ry + 5, col, 12); tx += 18
        s.text(tx, ry + 15, txt, C.TXT if kind != "inc" else C.AMBER, 11.5); tx += text_width(txt, 11.5) + 10
        if frac is not None:
            bar(tx, ry + 8, 90, frac, C.BLUE if kind != "imp" else C.TEAL); tx += 100
        if kind == "live": s.glyph(CLOCK, tx - 2, ry + 5, C.AMBER, 11); tx += 16
        if yours: s.text(tx, ry + 15, yours, C.ACC, 11.5); tx += text_width(yours, 11.5) + 10
        if queued: s.text(tx, ry + 15, queued, C.SUB, 11.5); tx += text_width(queued, 11.5) + 10
        if kind == "ready": s.button(tx, ry + 1, 58, "Open", FA.PLAY, C.BLUE)
        if kind == "inc": s.button(tx, ry + 1, 76, "Resume", 0xf2f1, C.AMBER)
    ry += 30
s.note(20, ry + 6, ["the segment is ONE line; a click on it opens ② · ⛔ it never switches mode by itself · a Live never waits for it and is never slowed by it (R-4173):",
                    "the transfer gives way while Live runs (the clock mark) and speeds up again after"], C.SUB, 11.5)

# ── ② the TRANSFERS popover ──────────────────────────────────────────────────────────────────────────────────────
Y2 = ry + 46
s.separator_text(20, Y2, "② Transfers — a click on the status bar's segment (= the orchestrator panel's list)", 600)
PX, PY, PW = 20, Y2 + 24, 580
s.rect(PX, PY, PW, 236, C.POPUP, stroke=C.SEP, rx=3)
s.text(PX + 10, PY + 18, "Transfers — one at a time, the rest wait in order", C.NOTE, 11.5)
jy = PY + 30
for g, col, title, sub, frac, btn, dis in [
        (UP, C.BLUE, "Archive · Night attack · exercise 3", "running · 2 of 4 files · 1.2 / 3.1 GB · asked by this station", 0.39, "Cancel", False),
        (DOWN, C.TEAL, "Import · Recon · exercise 5", "queued — after exercise 3 · asked by Debrief-1 (2 stations waiting)", None, "Cancel", False),
        (WARN, C.AMBER, "Archive · Crossfire · exercise 2", "incomplete — 3 of 4 files · SimHost-1 offline; retries when it is back", None, "Resume", False)]:
    s.rect(PX + 6, jy, PW - 12, 54, "#1A1A20", rx=3)
    s.glyph(g, PX + 16, jy + 9, col, 13); s.text(PX + 38, jy + 20, title, C.TXT, 12.5); s.text(PX + 38, jy + 38, sub, C.SUB, 11.5)
    if frac is not None: bar(PX + 38, jy + 44, 300, frac, col, 4)
    s.button(PX + PW - 92, jy + 16, 78, btn, 0xf00d if btn == "Cancel" else 0xf2f1, C.RED if btn == "Cancel" else C.AMBER, dis=dis)
    jy += 60
s.note(PX + 10, jy + 2, ["Cancel / Resume: the Author and Instructor seats (R-4173) —", "greyed on another seat: \"needs the Author or Instructor seat\""], C.NOTE, 11)

# ── ③ ⏏ CLOSE THE SESSION ────────────────────────────────────────────────────────────────────────────────────────
s.separator_text(620, Y2, "③ ⏏ Close the session — a recorded Live only (AQ108 R2″)", W - 20)
DX, DY, DW, DH = 620, Y2 + 24, 600, 214
dialog(DX, DY, DW, DH, "Close the live session")
s.text(DX + 14, DY + 42, "Night attack · exercise 3 — 18:40, recorded on 4 nodes (3.1 GB)", C.TXT, 12.5)
oy = DY + 56
for on, g, col, lab, sub in [(True, UP, C.BLUE, "Archive to NAS", "copies in the background — the status bar shows it; the nodes' copies become a cache once verified"),
                             (False, DISK, C.GREY, "Keep on the nodes", "not archived — archive later from Open Exercise Recording"),
                             (False, 0xf1f8, C.RED, "Discard", "deletes the recording on every node — cannot be undone")]:
    radio(DX + 14, oy + 2, on); s.glyph(g, DX + 36, oy + 2, col, 12.5)
    s.text(DX + 56, oy + 14, lab, C.TXT, 12.5); s.text(DX + 56, oy + 30, sub, C.SUB, 11); oy += 40
s.button(DX + DW - 230, DY + DH - 30, 120, "Close session", CLOSE, C.GREY, fill=C.BTNSEL); s.button(DX + DW - 100, DY + DH - 30, 86, "Cancel")
s.note(DX, DY + DH + 18, ["Archive is the default (R-4171) · an unrecorded Live closes with no question", "the all-in-one editor asks the same"], C.NOTE, 11)

# ── ④ the IMPORT dialog ──────────────────────────────────────────────────────────────────────────────────────────
Y3 = DY + DH + 58
s.separator_text(20, Y3, "④ Opening a run that is only on the NAS — the import (§5a)", 600)
IX, IY, IW, IH = 20, Y3 + 24, 580, 150
dialog(IX, IY, IW, IH, "Open Exercise Recording")
s.glyph(DOWN, IX + 14, IY + 34, C.TEAL, 14); s.text(IX + 36, IY + 46, "Importing Recon · exercise 5 from the NAS", C.TXT, 12.5)
s.text(IX + 36, IY + 64, "joined the import Debrief-1 started — the same job, the same numbers", C.SUB, 11.5)
bar(IX + 36, IY + 76, IW - 72, 0.42, C.TEAL, 8)
s.text(IX + 36, IY + 102, "2 of 4 files · 0.6 / 1.4 GB · about 40 s", C.SUB, 11.5)
s.button(IX + IW - 290, IY + IH - 30, 180, "Continue in background", 0xf2d2, C.GREY); s.button(IX + IW - 100, IY + IH - 30, 86, "Cancel")
s.note(IX, IY + IH + 18, ["waiting: the load starts BY ITSELF when the files are in (the existing loading screen)",
                          "in the background: the status bar then offers Open — never by itself",
                          "Cancel stops the job for every station that joined it (Author / Instructor seats)"], C.NOTE, 11)

# ── ⑤ the PICKER — where each run is ─────────────────────────────────────────────────────────────────────────────
s.separator_text(620, Y3, "⑤ Open Exercise Recording — where each run is (the replicated inventory)", W - 20)
KX, KY, KW = 620, Y3 + 24, 600
s.window(KX, KY, KW, 300, "Open Exercise Recording")


def loc(x, y, lab, g, col, fill):
    w = text_width(lab, 11) * 1.12 + 28
    s.rect(x, y, w, 18, fill, rx=9); s.glyph(g, x + 7, y + 3, col, 11); s.text(x + 22, y + 13, lab, col, 11)
    return w + 6


ky = KY + 30
for name, sub, chips, act, sel in [
        ("Night attack · exercise 3 · 18:40", "4 participants — opens merged", [("on the nodes", DISK, C.AMBER, "#3A3112"), ("archiving 2 / 4", UP, C.BLUE, "#1D2F48")], "opens at once", True),
        ("Recon · exercise 5 · 22:10", "2 participants", [("NAS", NAS, C.GREY, "#24242C"), ("importing — job #7", DOWN, C.TEAL, "#173A35")], "joins the import", False),
        ("Bridgehead · exercise 1 · 07:12", "this editor's run", [("NAS", NAS, C.GREY, "#24242C"), ("cached here", DISK, C.OK, "#1E3A22")], "opens at once", False),
        ("Ambush · exercise 0 · 12:48", "3 participants", [("NAS", NAS, C.GREY, "#24242C")], "imports first · 1.4 GB   ⋮", False),
        ("Crossfire · exercise 2 · 31:05", "2 participants", [("NAS incomplete 3 / 4", WARN, C.AMBER, "#3A3112"), ("on the nodes", DISK, C.AMBER, "#3A3112")], "opens at once · Resume archive", False)]:
    if sel: s.rect(KX + 6, ky - 4, KW - 12, 50, "#1E2A3E")
    s.text(KX + 14, ky + 12, name, C.TXT, 12.5); s.text(KX + 14, ky + 30, sub, C.SUB, 11)
    cx = KX + 250
    for c in chips: cx += loc(cx, ky, *c)
    s.text(KX + KW - 14, ky + 32, act, C.NOTE, 11, "end")
    ky += 52
s.note(KX, KY + 318, ["⛔ \"on the nodes\" and not archived = the only copy, never removed automatically (§6)", "\"cached here\" = a disposable local copy of an archived run"], C.NOTE, 11)

# ── ⑥ the CLOSE GUARD ────────────────────────────────────────────────────────────────────────────────────────────
Y4 = KY + 364
s.separator_text(20, Y4, "⑥ Quitting while a transfer runs — asked only where it RUNS (master's host, all-in-one)", 600)
GX, GY, GW, GH = 20, Y4 + 24, 580, 118
dialog(GX, GY, GW, GH, "Quit")
s.glyph(WARN, GX + 14, GY + 34, C.AMBER, 14)
s.text(GX + 36, GY + 46, "Night attack · exercise 3 is still being archived (2 of 4 files).", C.TXT, 12.5)
s.text(GX + 36, GY + 64, "Quit anyway: nothing is lost — the run stays on the nodes and the archive resumes at the next start.", C.SUB, 11)
s.button(GX + GW - 230, GY + GH - 30, 120, "Wait", CLOCK, C.GREY, fill=C.BTNSEL); s.button(GX + GW - 100, GY + GH - 30, 86, "Quit anyway")
s.note(GX, GY + GH + 18, ["another station quits with NO question — the transfer runs on the master, not on it",
                          "an import cancelled by a quit leaves no partial file (.part deleted); the NAS is never touched"], C.NOTE, 11)

# ── ⑦ DELETE FROM THE ARCHIVE (R-4174) ───────────────────────────────────────────────────────────────────────────
s.separator_text(620, Y4, "⑦ Delete from the archive — the picker's ⋮ on a NAS run, or the orchestrator panel", W - 20)
XX, XY, XW, XH = 620, Y4 + 24, 600, 150
dialog(XX, XY, XW, XH, "Delete from the archive")
s.glyph(0xf1f8, XX + 14, XY + 34, C.RED, 14)
s.text(XX + 36, XY + 46, "Delete Ambush · exercise 0 (12:48, 3 participants) from the NAS?", C.TXT, 12.5)
s.text(XX + 36, XY + 64, "3 files · 1.4 GB. The cached copies on this cluster go too.", C.SUB, 11)
s.text(XX + 36, XY + 80, "This cannot be undone — nothing else holds this recording.", C.RED, 11)
s.button(XX + XW - 270, XY + XH - 30, 160, "Delete from archive", 0xf1f8, C.TXT, fill="#8A2A2A"); s.button(XX + XW - 100, XY + XH - 30, 86, "Cancel")
s.note(XX, XY + XH + 18, ["Author / Instructor seats · refused while a transfer of that run is queued or running · Cancel is the focused default",
                          "a storage job like the others (Delete) — the status bar shows it"], C.NOTE, 11)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "recording-storage-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
