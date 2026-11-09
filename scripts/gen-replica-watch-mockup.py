# Generates docs/UX/replica-watch-visual.svg — DESIGN_Review_Perspective.md §6.13 (the replica watch, CE-4354 follow-up): the "Replica differs" lane
# rebuilt as a WATCH the user points at a suspicion — ① a component is watched from Details ▸ By node, ② the lane draws only PERSISTENT
# disagreement of the watched components as spans, ③ the lane's settings: what is watched, each one's tolerance, the persistence threshold.
# Scope: only these; the rest of Details, the By node table and the other timeline lanes are context, drawn as built.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

SITEMAP, CROWN, EYE, GEAR, XM = FA.SITEMAP, FA.CROWN, FA.EYE, FA.GEAR, FA.XMARK
W, H = 1240, 760
s = Svg(W, H, "Replica watch — what each node believed, for the components you pick (DESIGN_Review_Perspective.md §6.13)")

# ── ① Details · By node with the watch toggle ───────────────────────────────────────────────────────────
Y = 62
s.separator_text(20, Y, "① Details — watch a component on the timeline (the eye in its header)", 600)
DX, DY, DW, DH = 20, Y + 24, 580, 330
s.window(DX, DY, DW, DH, "Details — Bravo 2-3")
s.tabs(DX + 8, DY + 26, DW - 16, [("Unit", FA.USER, C.BLUE), ("Components", FA.CODE, C.GREY)], 1)


def comp(y, name, glyph, gc, watched, rows, unit):
    s.header(DX + 8, y, DW - 16, name, glyph, gc)
    # the watch toggle, right end of the component header
    s.glyph_toggle(DX + DW - 42, y + 2, EYE, C.TEAL if watched else C.DIS, watched)
    s.header(DX + 18, y + 28, DW - 36, "By node", SITEMAP, C.TEAL)
    ry = y + 56
    for node, val, delta, owner, warn in rows:
        if owner: s.glyph(CROWN, DX + 30, ry + 2, C.AMBER, 12)
        s.text(DX + 50, ry + 14, node, C.TXT, 12)
        s.text(DX + 260, ry + 14, val, C.AMBER if warn else C.SUB, 12)
        s.text(DX + 400, ry + 14, ("owner" if owner else delta), C.AMBER if warn else C.NOTE, 11.5)
        ry += 22
    return ry


ry = comp(DY + 56, "Health", FA.HEART, C.RED, True,
          [("CGF-1 (Brain)", "0 / 300", "", True, False), ("SimHost-1 (Muscle)", "300 / 300", "300", False, True),
           ("IG-1 (Map2D)", "0 / 300", "—", False, False)], "")
ry = comp(ry + 6, "Position", FA.LOCATION, C.TEAL, False,
          [("SimHost-1 (Muscle)", "668.2 · 427.9", "", True, False), ("CGF-1 (Brain)", "667.9 · 427.8", "0.3 m", False, False)], "m")
s.note(DX, DY + DH + 16, ["the eye (right end of a component header): watch it on the timeline · teal = watched, grey = not",
                          "on every component header of a networked unit, in the merged view only",
                          "amber here is the instant view (as built); the lane below shows only what PERSISTS"], C.NOTE, 11)

# ── ③ the lane's settings popup ─────────────────────────────────────────────────────────────────────────
PX, PY, PW = 660, Y + 24, 540
s.separator_text(PX, Y, "③ the lane's settings (the gear on the lane)", W - 20)
rows = [("Health", FA.HEART, C.RED, "exact"), ("WeaponAmmo", FA.CROSSHAIRS, C.ORANGE, "exact"), ("Position", FA.LOCATION, C.TEAL, "0.5 m")]
PH = 36 + len(rows) * 28 + 100
s.rect(PX, PY, PW, PH, C.POPUP, stroke=C.SEP, rx=3)
s.text(PX + 12, PY + 22, "Watched on Bravo 2-3", C.SUB, 12)
s.text(PX + 300, PY + 22, "differs when beyond", C.NOTE, 11)
yy = PY + 36
for name, g, gc, tol in rows:
    s.glyph(EYE, PX + 12, yy + 4, C.TEAL, 12)
    s.glyph(g, PX + 34, yy + 4, gc, 12)
    s.text(PX + 56, yy + 16, name, C.TXT, 12)
    s.field(PX + 300, yy, 120, tol, hint=(tol == "exact"))
    s.glyph(XM, PX + PW - 28, yy + 4, C.DIS, 12)
    yy += 28
s.line(PX + 8, yy + 4, PX + PW - 8, yy + 4)
s.text(PX + 12, yy + 28, "Show a disagreement only when it lasts longer than", C.TXT, 12)
s.field(PX + 360, yy + 14, 80, "200 ms")
s.text(PX + 448, yy + 28, "(default)", C.NOTE, 11)
s.text(PX + 12, yy + 52, "≈ 2 × the time-sync tolerance: normal replication catch-up stays hidden", C.SUB, 11.5)
s.line(PX + 8, yy + 64, PX + PW - 8, yy + 64)
s.glyph(FA.PLUS, PX + 12, yy + 74, C.GREEN, 12)
s.text(PX + 34, yy + 86, "Watch another component…  (or the eye in Details)", C.SUB, 12)
s.note(PX, PY + PH + 16, ["per unit: each selected unit keeps its own watch list (empty by default ⇒ no lane)",
                          "tolerance: discrete values default to exact; a continuous one (position, heading) to a size in its unit",
                          "✕ stops watching · the threshold applies to every watched component"], C.NOTE, 11)

# ── ② the lane: persistent disagreement as spans ────────────────────────────────────────────────────────
Y2 = DY + DH + 84
s.separator_text(20, Y2, "② the Event Timeline — one lane per selected unit, spans of PERSISTENT disagreement, one colour per watched component", W - 20)
TX, TY, TW, TH = 20, Y2 + 24, 1200, 150
s.rect(TX, TY, TW, TH, C.BG, stroke=C.BORDER); s.rect(TX, TY, TW, 20, C.TITLE); s.text(TX + 8, TY + 14, "Event Timeline")
rx0, rx1 = TX + 230, TX + TW - 14
X = lambda f: rx0 + (rx1 - rx0) * f
for i, (name, col, evs) in enumerate([("Shot", C.AMBER, [0.2, 0.31, 0.62]), ("Hit", C.ORANGE, [0.33, 0.63])]):
    y = TY + 30 + i * 30
    s.text(TX + 12, y + 13, name, col, 11.5); s.rect(rx0, y + 2, rx1 - rx0, 18, "#16161B")
    for f in evs: s.o.append(f"<circle cx='{X(f)}' cy='{y + 11}' r='4' fill='{col}'/>")
y = TY + 90
s.glyph(EYE, TX + 12, y + 3, C.TEAL, 11); s.text(TX + 30, y + 13, "Replica watch · Bravo 2-3", C.TEAL, 11.5)
s.glyph(GEAR, TX + 196, y + 3, C.SUB, 11)
s.rect(rx0, y + 2, rx1 - rx0, 18, "#16161B")
for a, b, col in [(0.335, 0.39, C.RED), (0.64, 0.655, C.ORANGE), (0.80, 0.97, C.TEAL)]:
    s.rect(X(a), y + 4, X(b) - X(a), 14, col, rx=2, op=0.85)
s.line(X(0.45), TY + 24, X(0.45), TY + TH - 6, C.RED, sw=1.5)
s.tooltip(X(0.335), y + 24, 420, ["Health — SimHost-1 believed 300 while the owner CGF-1 had 0",
                                  "for 2.4 s (12.1 – 14.5 s) · click = seek to its start"], edge=C.RED)
s.note(TX, TY + TH + 54, ["a span = a watched component on which some node disagreed with the owner beyond its tolerance for longer than the threshold",
                          "normal replication delay and clock offset (a few frames at each change) never show · hover = who believed what · click = seek",
                          "the lane exists only in the merged view, for the selected unit, while it has a watched component"], C.NOTE, 11)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "replica-watch-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
