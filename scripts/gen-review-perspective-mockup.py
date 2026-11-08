# Generates docs/UX/review-perspective-visual.svg — DESIGN_Review_Perspective.md: the editor in AAR, laid out as the Review perspective —
# the Replay Browser's analysis windows over the editor's ONE world (CE-3419, first iteration). Scope: the perspective's layout and the four
# windows it adds; the run controls are AQ108 §0's (drawn as context, unchanged); the map content is context.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 860
s = Svg(W, H, "Review perspective — the editor in AAR with the Replay Browser's analysis windows (DESIGN_Review_Perspective.md)")
X0, Y0, SW, SH = 20, 56, 1200, 640
s.rect(X0, Y0, SW, SH, "#0B0B0D", stroke=C.BORDER)

# ── the main bar (context: AQ108 §0's AAR state) + the perspective switch ──
s.rect(X0, Y0, SW, 30, "#1E1E22")
mx = X0 + 10
for m in ["File", "Edit", "View", "Scenario"]:
    s.text(mx, Y0 + 20, m, C.TXT, 12.5); mx += text_width(m, 12.5) + 18
bx = mx + 30
s.rect(bx, Y0 + 5, 34, 21, C.BTN, rx=3); s.glyph(FA.PLAY, bx + 6, Y0 + 9, C.BLUE, 13); s.text(bx + 25, Y0 + 20, "▾", C.TXT, 11)
bx += 44
for cp, col in [(FA.PLAY, C.BLUE), (0xf04d, C.GREY)]:
    s.rect(bx, Y0 + 5, 24, 21, C.BTN, rx=3); s.glyph(cp, bx + 5, Y0 + 9, col, 13); bx += 30
s.text(bx + 6, Y0 + 20, "12:31 / 18:40  AAR · Night attack · exercise 3", C.TXT, 12)
# ★ the perspective switch: Scenario · Review
px = X0 + SW - 230
s.text(px - 8, Y0 + 20, "", C.DIS, 11)
for lab, g, col, sel in [("Scenario", FA.EARTH, C.GREY, False), ("Review", FA.MAGNIFYING if hasattr(FA, "MAGNIFYING") else 0xf002, C.TEAL, True)]:
    w = text_width(lab, 12) + 34
    s.rect(px, Y0 + 5, w, 21, "#2C3A52" if sel else C.BTN, rx=3, stroke="#8FB7F0" if sel else None)
    s.glyph(g, px + 7, Y0 + 9, col, 12); s.text(px + 25, Y0 + 20, lab, C.TXT, 12); px += w + 6

# ── the map (centre, context) ──
MX, MY, MW, MH = X0 + 230, Y0 + 30, 600, 360
for gx in range(int(MX), int(MX + MW), 50): s.line(gx, MY, gx, MY + MH, "#1A1A1F")
for gy in range(int(MY), int(MY + MH), 50): s.line(MX, gy, MX + MW, gy, "#1A1A1F")
for (ex, ey, lab, col) in [(MX + 250, MY + 200, "Alpha 1-1", C.BLUE), (MX + 270, MY + 182, "Alpha 1-2", C.BLUE), (MX + 470, MY + 80, "Bravo 2-3", C.RED)]:
    s.rect(ex, ey, 10, 10, col); s.text(ex + 14, ey + 10, lab, C.SUB, 11)
s.text(MX + 10, MY + MH - 10, "the map — the editor's ONE world, replaying (context)", C.NOTE, 11)

# ── left: Mission summary (the rules bundle, unchanged) ──
LX, LY, LW, LH = X0, Y0 + 200, 230, 190
s.rect(LX, LY, LW, LH, C.BG, stroke=C.BORDER); s.rect(LX, LY, LW, 20, C.TITLE); s.text(LX + 8, LY + 14, "Mission summary")
yy = LY + 34
for lab, st, col in [("Hold the bridge", "GO", C.OK), ("No civilian harm", "GO", C.OK), ("Report contact < 30 s", "NO-GO", C.RED)]:
    s.text(LX + 10, yy + 12, lab, C.TXT, 11.5); s.text(LX + LW - 10, yy + 12, st, col, 11.5, "end"); yy += 22
s.note(LX + 10, yy + 14, ["unchanged — the rules bundle", "(read-only on a recording)"], C.NOTE, 11)

# ── right top: Details (unchanged, read-only) ──
DX, DY, DW, DH = X0 + 830, Y0 + 30, SW - 830, 170
s.rect(DX, DY, DW, DH, C.BG, stroke=C.BORDER); s.rect(DX, DY, DW, 20, C.TITLE); s.text(DX + 8, DY + 14, "Details — Bravo 2-3")
s.tabs(DX + 8, DY + 26, DW - 16, [("Unit", FA.USER, C.BLUE), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)], 0)
ly = DY + 58
for lab, val in [("Side", "Hostile"), ("Position", "668 · 427 m"), ("Strength", "40 %")]:
    s.text(DX + 12, ly + 14, lab, C.DIS); s.field(DX + 100, ly, DW - 120, val, dis=True); ly += 25

# ── right bottom: ★ the analysis windows, tabbed (NEW in the editor) ──
AX, AY, AW, AH = DX, DY + DH, DW, MH - DH
s.rect(AX, AY, AW, AH, C.BG, stroke=C.BORDER)
s.tabs(AX + 6, AY + 4, AW - 12, [("Frame diff", 0xf0ec, C.AMBER), ("Search", 0xf002, C.TEAL), ("Breakpoints", 0xf111, C.RED)], 0)
ry = AY + 36
s.text(AX + 10, ry + 12, "Bravo 2-3 · 12:30.9 → 12:31.0", C.SUB, 11.5); ry += 20
for comp, old, new in [("Health.Current", "62", "40"), ("Weapon.Ammo", "18", "15"), ("Posture", "Standing", "Prone")]:
    s.text(AX + 10, ry + 12, comp, C.TXT, 11.5); s.text(AX + 170, ry + 12, old, C.RED, 11.5); s.text(AX + 230, ry + 12, "→", C.DIS, 11.5)
    s.text(AX + 250, ry + 12, new, C.OK, 11.5); ry += 20
s.text(AX + 10, ry + 16, "◀ previous change    next change ▶", C.ACC, 11.5)

# ── bottom: the Event Timeline (unchanged) + Search results strip ──
TX, TY, TW, TH = X0, Y0 + 390, SW, 250
s.rect(TX, TY, TW, TH, C.BG, stroke=C.BORDER); s.rect(TX, TY, TW, 20, C.TITLE); s.text(TX + 8, TY + 14, "Event Timeline")
rx0, rx1 = TX + 110, TX + TW - 14
for i in range(7):
    t = f"{i * 3}:00"; xx = rx0 + (rx1 - rx0) * i / 6.2; s.text(xx, TY + 38, t, C.DIS, 11); s.line(xx, TY + 42, xx, TY + 47, C.SEP)
lanes = [("Tasks", C.GREEN, [0.02, 0.3, 0.5]), ("Shot", C.AMBER, [0.4, 0.45, 0.62, 0.66]), ("Hit", C.ORANGE, [0.62, 0.66]),
         ("★ Search", C.TEAL, [0.31, 0.62, 0.71]), ("★ Breakpoint", C.RED, [0.66])]
for i, (name, col, evs) in enumerate(lanes):
    yy = TY + 52 + i * 24
    s.text(TX + 12, yy + 13, name, col, 11.5); s.rect(rx0, yy + 2, rx1 - rx0, 18, "#16161B")
    for f in evs: s.o.append(f"<circle cx='{rx0 + (rx1 - rx0) * f}' cy='{yy + 11}' r='4' fill='{col}'/>")
s.line(rx0 + (rx1 - rx0) * 0.67, TY + 30, rx0 + (rx1 - rx0) * 0.67, TY + TH - 6, C.RED, sw=1.5)
s.note(TX + 12, TY + TH - 40, ["★ the search's hits and a data breakpoint's stops become timeline lanes; a click seeks the cluster there"], C.NOTE, 11)

# ── the File menu addition ──
s.menu(X0 + 8, Y0 + 30, 300, [("#", "File"), ("Open Exercise Recording…", None, 0xf1da, C.BLUE), ("Recording info", None, 0xf05a, C.GREY),
                             ("Export to JSON…", None, 0xf56e, C.GREY), "---", ("Open all nodes in the Replay Browser…", None, 0xf0e8, C.TEAL)])

NY = Y0 + SH + 18
s.note(20, NY, ["★ ONE world: everything above draws the editor's own world, which the AAR replays (the cluster seeks every node). The search, the frame diff and",
                "   the change finder read the RECORDING FILE in throwaway background readers — the way the editor's HTTP API already searches (no second visible world).",
                "★ Review is a perspective (a window layout), switched to automatically on entering AAR and back on leaving it; the same layout is the Replay Browser's default (Q-B3).",
                "★ Federation (every node's own view, merged) stays in the standalone Replay Browser — File ▸ Open all nodes in the Replay Browser… opens this exercise there (Q-B6).",
                "   Unchanged, drawn as context: the run controls (AQ108 §0), the rules bundle, Details, the Event Timeline's own lanes."], C.SUB, 12)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "review-perspective-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
