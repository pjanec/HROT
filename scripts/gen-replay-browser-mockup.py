# Generates docs/UX/replay-browser-visual.svg — the Replay Browser redesigned (DESIGN_Replay_Browser_UX.md):
# a seek bar on the main bar, a default layout (map, Details right, the Event Timeline docked below), and what a seek tint (Q-R1) means.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 820
s = Svg(W, H, "Replay Browser — redesigned (house style; DESIGN_Replay_Browser_UX.md)")
X0, Y0, SW, SH = 20, 56, 1200, 600       # the app's screen
s.rect(X0, Y0, SW, SH, "#0B0B0D", stroke=C.BORDER)

# ── the main bar ──
s.rect(X0, Y0, SW, 30, "#1E1E22")
s.text(X0 + 10, Y0 + 20, "File", C.TXT, 12.5); s.text(X0 + 44, Y0 + 20, "View", C.TXT, 12.5)
bx = X0 + 90
for cp, col in [(0xf060, "#3EC6B0"), (0xf061, "#3EC6B0"), (0xf049, C.GREY), (FA.PLAY, C.GREEN)]:
    s.glyph(cp, bx, Y0 + 9, col, 13); bx += 26
s.rect(bx, Y0 + 6, 52, 19, C.FRAME); s.text(bx + 6, Y0 + 20, "1.0x", C.TXT, 12); s.rect(bx + 34, Y0 + 6, 18, 19, C.BTN)
s.o.append(f"<path d='M{bx + 38} {Y0 + 13} l5 5 l5 -5 z' fill='{C.TXT}'/>"); bx += 60
s.rect(bx, Y0 + 6, 20, 19, C.BTN, rx=2); s.text(bx + 10, Y0 + 20, "S", C.TXT, 12, "middle"); bx += 28
for cp in (0xf048, 0xf051): s.glyph(cp, bx, Y0 + 9, C.GREY, 13); bx += 26
# ★ the seek bar
sx = bx + 14; s.text(sx, Y0 + 20, "0:39.9", C.TXT, 12.5); sx += 50
send = X0 + SW - 150
s.rect(sx, Y0 + 12, send - sx, 6, "#2A2A33", rx=3); px = sx + (send - sx) * 0.6
s.rect(sx, Y0 + 12, px - sx, 6, C.ACC, rx=3)
s.o.append(f"<circle cx='{px}' cy='{Y0 + 15}' r='6.5' fill='{C.TXT}'/>")
s.text(send + 8, Y0 + 20, "1:06.8", C.DIS, 12.5)
cw = text_width("Recording", 11) + 34
s.rect(X0 + SW - cw - 10, Y0 + 7, cw, 16, "#3A2E4A", rx=8); s.glyph(FA.VIDEO, X0 + SW - cw - 2, Y0 + 9.5, "#C9A6F5", 10); s.text(X0 + SW - cw + 14, Y0 + 19, "Recording", "#C9A6F5", 11)
# the hover tooltip on the seek bar
hx = sx + (send - sx) * 0.75

# ── the map (centre) ──
MX, MY, MW, MH = X0, Y0 + 30, 820, 380
for gx in range(int(MX), int(MX + MW), 50): s.line(gx, MY, gx, MY + MH, "#1A1A1F")
for gy in range(int(MY), int(MY + MH), 50): s.line(MX, gy, MX + MW, gy, "#1A1A1F")
for (ex, ey, lab, col) in [(520, 250, "Tank Platoon-1", C.BLUE), (540, 232, "Tank Platoon-2", C.BLUE), (760, 110, "M1 Abrams #1006", C.RED)]:
    s.rect(ex, ey, 10, 10, col); s.text(ex + 14, ey + 10, lab, C.SUB, 11)
s.text(MX + 10, MY + MH - 10, "map", C.NOTE, 11)

# ── Details, docked right ──
DX, DY, DW, DH = X0 + MW, Y0 + 30, SW - MW, 380
s.rect(DX, DY, DW, DH, C.BG, stroke=C.BORDER); s.rect(DX, DY, DW, 20, C.TITLE); s.text(DX + 8, DY + 14, "Details — M1 Abrams #1006")
s.tabs(DX + 8, DY + 26, DW - 16, [("Unit", FA.USER, C.BLUE), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)], 0)
ly = DY + 58
for lab, val in [("Name", "M1 Abrams #1006"), ("Side", "Hostile"), ("Type", "M1 Abrams"), ("Position", "668 · 427 m"), ("Strength", "100 %")]:
    s.text(DX + 12, ly + 14, lab, C.DIS); s.field(DX + 110, ly, DW - 130, val, dis=True)
    if lab == "Position": s.glyph(FA.LOCATION, DX + DW - 40, ly + 2.5, C.TEAL, 12)
    ly += 25
s.note(DX + 12, ly + 12, ["the forms, read-only (forms §2.4);", "the ⌖ shows a point on the map", "(DESIGN_Show_On_Map)"], C.NOTE, 11)

# ── the Event Timeline, docked below, full width ──
TX, TY, TW, TH = X0, Y0 + 410, SW, 190
s.rect(TX, TY, TW, TH, C.BG, stroke=C.BORDER); s.rect(TX, TY, TW, 20, C.TITLE); s.text(TX + 8, TY + 14, "Event Timeline")
rx0, rx1 = TX + 110, TX + TW - 14
for i, t in enumerate(["0:00", "0:10", "0:20", "0:30", "0:40", "0:50", "1:00"]):
    xx = rx0 + (rx1 - rx0) * i / 6.68; s.text(xx, TY + 38, t, C.DIS, 11); s.line(xx, TY + 42, xx, TY + 47, C.SEP)
lanes = [("Spawned", C.GREEN, [0.01, 0.015, 0.02]), ("Side", C.BLUE, [0.02, 0.3]), ("Health", C.PINK, [0.31, 0.44, 0.52]),
         ("Shot", C.AMBER, [0.18, 0.2, 0.31, 0.32, 0.44]), ("Hit", C.ORANGE, [0.31, 0.44]), ("Orders", C.VIOLET, [0.05, 0.5, 0.83])]
for i, (name, col, evs) in enumerate(lanes):
    yy = TY + 52 + i * 21
    s.text(TX + 12, yy + 13, name, col, 11.5); s.rect(rx0, yy + 2, rx1 - rx0, 16, "#16161B")
    for f in evs: s.o.append(f"<circle cx='{rx0 + (rx1 - rx0) * f}' cy='{yy + 10}' r='3.5' fill='{col}'/>")
s.line(rx0 + (rx1 - rx0) * 0.6, TY + 30, rx0 + (rx1 - rx0) * 0.6, TY + TH - 6, C.RED, sw=1.5)

s.tooltip(hx - 70, Y0 + 32, 150, ["0:50.1 · frame 2 640"])
# ── notes ──
NY = Y0 + SH + 18
s.note(20, NY, ["★ The seek bar fills the free width of the main bar: drag = scrub, click = jump, hover = the time and frame. No ticks: the events are in the",
                "   Event Timeline below (R-4155). The Replay Timeline window's slider, frame / tick / path rows move here",
                "   (the frame into the tooltip, the file into File ▸ Recording info) and the window is retired (Q-B2).",
                "★ A default Replay layout: map centre, Details docked right, the Event Timeline docked below full width — today all seven default windows",
                "   open stacked at one corner, four of them about 90 × 50 px (measured). The debug windows (inspectors, event browser, frame diff, federation, search)",
                "   start closed under View ▸ Windows ▸ Debug (Q-B3)."], C.SUB, 12)
s.note(20, NY + 98, ["Q-R1 explained: after you JUMP (seek) from 0:12 to 0:40, the Details rows whose value differs between the two moments flash for a moment —",
                     "e.g. Position and Strength light up, Name does not — so you see what the jump changed. During normal play nothing flashes."], C.TXT, 12)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "replay-browser-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
