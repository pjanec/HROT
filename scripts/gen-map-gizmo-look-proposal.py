# Generates the B-17 P7 gizmo-look PROPOSAL sheets (DESIGN_Map_Interaction.md §2.6), house style (R-4143), over mockup_kit.
#   sheet 1: placement ghost · drawing points and lines · edit handles     sheet 2: pick cursor + banner · rotate ring · screen text
# Regenerate: python3.13 scripts/gen-map-gizmo-look-proposal.py docs/UX/map-gizmo-look-1.svg docs/UX/map-gizmo-look-2.svg
# Each row: TODAY (as the rails draw it) | PROPOSAL 2-D | PROPOSAL 3-D. Context (terrain, roads) is drawn for orientation only — unchanged.
import sys, math
from mockup_kit import Svg, C, FA, text_width

W = 1240; PW, PH = 392, 236; GAP = 12; X0 = 20
GREYMAP = "#4F4F4F"; MAPBG = "#1E2A22"; GRID = "#25332A"; ROAD = "#3A3A32"; BLD = "#2E3440"
SKY = "#2B3442"; GROUND = "#2A3A2C"; GROUNDLINE = "#344836"
AREA = C.TEAL; ROUTEC = C.GREEN; SIDE = C.BLUE; GRAB = C.AMBER


def row_title(s, y, n, title, lean):
    s.text(X0, y + 14, f"{n}  {title}", "#E8E8EE", 14, weight="bold")
    s.text(X0, y + 32, lean, C.SUB, 11.5)


def cols(s, y, today_note, labels=("TODAY", "PROPOSAL — 2-D map", "PROPOSAL — 3-D view")):
    xs = [X0 + i * (PW + GAP) for i in range(3)]
    for x, lab, c in zip(xs, labels, [C.NOTE, C.OK, C.OK]):
        s.text(x, y - 4, lab, c, 11, weight="bold")
    s.pending_note = (xs[0] + 8, y + 16, today_note)
    return xs


def today_panel(s, x, y):
    s.rect(x, y, PW, PH, GREYMAP, stroke=C.BORDER)
    nx, ny, note = s.pending_note
    s.text(nx, ny, note, "#D8D8DE", 10.5)


def map2d(s, x, y):
    s.rect(x, y, PW, PH, MAPBG, stroke=C.BORDER)
    for gx in range(int(x) + 30, int(x + PW), 50): s.line(gx, y, gx, y + PH, GRID)
    for gy in range(int(y) + 30, int(y + PH), 50): s.line(x, gy, x + PW, gy, GRID)
    s.rect(x, y + PH * 0.70, PW, 12, ROAD); s.rect(x + PW * 0.78, y, 12, PH, ROAD)


class P3:
    """A tiny perspective camera over a flat ground: u (0..1 across), v (0 far .. 1 near), h metres up."""
    def __init__(self, s, x, y):
        self.s, self.x, self.y = s, x, y; self.hy = y + PH * 0.30; self.by = y + PH
        s.rect(x, y, PW, PH, SKY, stroke=C.BORDER)
        pts = [self.p(0, 0), self.p(1, 0), self.p(1, 1), self.p(0, 1)]
        s.raw("<polygon points='" + " ".join(f"{a:.1f},{b:.1f}" for a, b in pts) + f"' fill='{GROUND}'/>")
        s.rect(x, self.hy, PW, PH * 0.70, GROUND, op=0.9)
        for i in range(0, 11): s.line(*self.p(i / 10, 0), *self.p(i / 10, 1), GROUNDLINE)
        for j in range(1, 7): v = (j / 6) ** 1.4; s.line(*self.p(0, v), *self.p(1, v), GROUNDLINE)
        s.rect(x, y, PW, PH, "none", stroke=C.BORDER)

    def k(self, v): return 0.30 + 0.70 * v

    def p(self, u, v, h=0.0):
        k = self.k(v); sx = self.x + PW / 2 + (u - 0.5) * PW * 1.6 * k
        sy = self.hy + (self.by - self.hy) * (0.04 + 0.96 * v ** 1.1) - h * 2.2 * k
        return (max(self.x, min(self.x + PW, sx)), sy)

    def box(self, u, v, h, wu, dv, height, fill, op, stroke):
        """A footprint box (wu across, dv deep) standing at height h, with a top face."""
        s = self.s
        b = [self.p(u - wu, v - dv, h), self.p(u + wu, v - dv, h), self.p(u + wu, v + dv, h), self.p(u - wu, v + dv, h)]
        t = [self.p(a, c, h + height) for a, c in [(u - wu, v - dv), (u + wu, v - dv), (u + wu, v + dv), (u - wu, v + dv)]]
        poly = lambda q, f, o: s.raw("<polygon points='" + " ".join(f"{a:.1f},{c:.1f}" for a, c in q) + f"' fill='{f}' fill-opacity='{o}' stroke='{stroke}' stroke-width='1.2'/>")
        poly([b[3], b[2], t[2], t[3]], fill, op); poly([b[1], b[2], t[2], t[1]], fill, op * 0.8); poly(t, fill, op * 1.2)


def backdrop_text(s, x, y, lines, sz=11.5, fg="#E8E8EE"):
    w = max(text_width(l, sz) for l in lines) + 12
    s.rect(x, y, w, 6 + 15 * len(lines), "#000000", rx=3, op=0.62)
    for i, l in enumerate(lines): s.text(x + 6, y + 15 + 15 * i, l, fg, sz)


def pixel_text(s, x, y, t, c="#FFFFFF"):
    """Today's look: no backdrop, the raylib default pixel font."""
    s.raw(f"<text x='{x}' y='{y}' fill='{c}' font-size='10.5' font-family='monospace' letter-spacing='0.3'>{t}</text>")


def square(s, x, y, sz, fill, stroke, sw=1.4):
    s.rect(x - sz / 2, y - sz / 2, sz, sz, fill, stroke=stroke, sw=sw)


def diamond(s, x, y, r, fill, stroke):
    s.raw(f"<polygon points='{x},{y - r} {x + r},{y} {x},{y + r} {x - r},{y}' fill='{fill}' stroke='{stroke}' stroke-width='1.4'/>")


def crosshair(s, x, y, r=9, c="#FFFFFF"):
    for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
        s.line(x + dx * 3, y + dy * 3, x + dx * r, y + dy * r, "#000000", 3.2)
        s.line(x + dx * 3, y + dy * 3, x + dx * r, y + dy * r, c, 1.4)


def poly(s, pts, stroke, sw=2, fill=None, fop=0.12, dash=None, closed=False):
    d = f" stroke-dasharray='{dash}'" if dash else ""
    tag = "polygon" if closed else "polyline"
    s.raw(f"<{tag} points='" + " ".join(f"{a:.1f},{b:.1f}" for a, b in pts) + f"' fill='{fill or 'none'}' fill-opacity='{fop}' stroke='{stroke}' stroke-width='{sw}'{d}/>")


# ═════════════════════════════════════ SHEET 1 ═════════════════════════════════════
H1 = 60 + 3 * (PH + 70) + 10
s = Svg(W, H1, "B-17 P7 — the gizmo look, proposal 1/2 (design §2.6) · placement ghost · drawing · edit handles")
y = 60

# ── 1 placement ghost ───────────────────────────────────────────────
row_title(s, y, "①", "Placement ghost",
          "lean: the unit's own footprint (the renderer's L × W rule, EntityFootprint) drawn translucent with a heading notch — never a ball; an aircraft at its altitude with a drop line")
xs = cols(s, y + 54, "a 3 m translucent sphere (EntityPlacementGizmo.cs:165): a dot / a ball")
gy = y + 54
today_panel(s, xs[0], gy)
s.raw(f"<circle cx='{xs[0] + 160}' cy='{gy + 110}' r='7' fill='#5AA9FF' fill-opacity='0.45'/>")
s.raw(f"<circle cx='{xs[0] + 300}' cy='{gy + 70}' r='7' fill='#5AA9FF' fill-opacity='0.45'/>")
pixel_text(s, xs[0] + 172, gy + 136, "level 0 of 3 - PgUp / PgDn")
pixel_text(s, xs[0] + 150, gy + 96, "airborne 300 m (Shift: on the ground)")
s.text(xs[0] + 8, gy + PH - 10, "rifleman on a roof (left) · A-10C (right): both just a dot", C.NOTE, 10.5)
# 2-D proposal
map2d(s, xs[1], gy); bx, by = xs[1] + 60, gy + 50
s.rect(bx, by, 90, 64, BLD, stroke="#4A5260")
cx, cy = bx + 45, by + 32
s.raw(f"<g transform='rotate(-30 {cx} {cy})'><rect x='{cx - 9}' y='{cy - 5}' width='18' height='10' fill='{SIDE}' fill-opacity='0.4' stroke='{SIDE}' stroke-width='1.5' stroke-dasharray='4 2'/>"
      f"<polygon points='{cx + 9},{cy - 4} {cx + 14},{cy} {cx + 9},{cy + 4}' fill='{SIDE}'/></g>")
crosshair(s, cx, cy, 6)
backdrop_text(s, bx - 40, by + 74, ["roof · level 2 of 3 · PgUp / PgDn", "drag: heading"])
ax, ay = xs[1] + 290, gy + 70
s.raw(f"<g transform='rotate(20 {ax} {ay})'><rect x='{ax - 20}' y='{ay - 14}' width='40' height='28' fill='{SIDE}' fill-opacity='0.4' stroke='{SIDE}' stroke-width='1.5' stroke-dasharray='4 2'/>"
      f"<polygon points='{ax + 20},{ay - 6} {ax + 28},{ay} {ax + 20},{ay + 6}' fill='{SIDE}'/></g>")
s.glyph(FA.PLANE, ax - 8, ay - 8, "#FFFFFF", 16, op=0.85)
backdrop_text(s, ax - 170, ay + 30, ["airborne 300 m · Shift: on the ground"])
s.text(xs[1] + 8, gy + PH - 10, "footprint at its real size (scales with zoom, like the symbol)", C.NOTE, 10.5)
# 3-D proposal
cam = P3(s, xs[2], gy)
cam.box(0.30, 0.62, 0, 0.11, 0.10, 18, BLD, 0.95, "#4A5260")
cam.box(0.30, 0.62, 18, 0.025, 0.02, 2.2, SIDE, 0.45, SIDE)
gx, gy3 = cam.p(0.30, 0.62, 19)
crosshair(s, gx, gy3 - 2, 6)
backdrop_text(s, gx - 70, gy3 - 46, ["roof · level 2 of 3"])
air = cam.p(0.70, 0.55, 30); gnd = cam.p(0.70, 0.55, 0)
s.line(air[0], air[1] + 8, gnd[0], gnd[1], C.SUB, 1.2, dash="4 3")
s.raw(f"<ellipse cx='{gnd[0]:.1f}' cy='{gnd[1]:.1f}' rx='14' ry='4' fill='#000' fill-opacity='0.45'/>")
cam.box(0.70, 0.55, 30, 0.05, 0.035, 2.5, SIDE, 0.45, SIDE)
backdrop_text(s, air[0] + 20, air[1] - 24, ["300 m"])
s.text(xs[2] + 8, gy + PH - 10, "footprint on the surface; aircraft at altitude, drop line", C.NOTE, 10.5)

# ── 2 drawing points and lines ──────────────────────────────────────
y += PH + 70
row_title(s, y, "②", "Drawing points and lines — one style for all four drawing gizmos",
          "lean: the shape's kind colour (area teal · route green · the toolbar's own icon colours); small squares; placed segments solid; next segment dashed; closing segment dotted")
xs = cols(s, y + 54, "round dots, solid lines, no closing segment")
gy = y + 54
today_panel(s, xs[0], gy)
P = [(70, 170), (160, 70), (270, 130)]; cur = (130, 180)
poly(s, [(xs[0] + a, gy + b) for a, b in P], "#0A7BFF", 1.6)
s.line(xs[0] + P[-1][0], gy + P[-1][1], xs[0] + cur[0], gy + cur[1], "#66BFFF", 1)
for a, b in P: s.raw(f"<circle cx='{xs[0] + a}' cy='{gy + b}' r='6' fill='#0A7BFF'/>")
s.raw(f"<circle cx='{xs[0] + cur[0]}' cy='{gy + cur[1]}' r='7' fill='#0A7BFF'/>")
s.rect(xs[0] + 330, gy + 14, 34, 34, "none", stroke=C.OK, sw=1.5); s.raw(f"<path d='M{xs[0] + 337} {gy + 31} l7 8 l14 -18' stroke='{C.OK}' fill='none' stroke-width='1.6'/>")
for i, (c, lab) in enumerate([("#0A7BFF", "drawn area / route"), ("#66BFFF", "circle / rectangle"), ("#00D278", "edit shape"), ("#00A0FF", "edit route")]):
    s.rect(xs[0] + 10, gy + 196 - i * 0 + 0, 0, 0, c)
s.text(xs[0] + 8, gy + PH - 10, "4 gizmos · 4 blues/greens · spheres or squares", C.NOTE, 10.5)
# 2-D proposal
map2d(s, xs[1], gy)
Q = [(xs[1] + a, gy + b) for a, b in P]; cur2 = (xs[1] + cur[0], gy + cur[1])
poly(s, Q + [cur2], AREA, 0, fill=AREA, fop=0.12, closed=True)
poly(s, Q, AREA, 2)
s.line(*Q[-1], *cur2, AREA, 1.6, dash="6 4"); s.line(*cur2, *Q[0], AREA, 1.2, dash="2 4")
square(s, *Q[0], 10, "#FFFFFF", AREA, 2)
for q in Q[1:]: square(s, *q, 8, "#10302B", AREA)
crosshair(s, *cur2, 8)
backdrop_text(s, cur2[0] + 14, cur2[1] + 8, ["3 points · Enter / Done: finish · Esc: cancel"])
s.button(Q[-1][0] + 12, Q[-1][1] - 26, 62, "Done", glyph=FA.CHECK, gc=C.OK)
s.text(xs[1] + 8, gy + PH - 10, "first point white = click it to close · Done is a button with a label", C.NOTE, 10.5)
# 3-D proposal
cam = P3(s, xs[2], gy)
UV = [(0.25, 0.80), (0.42, 0.35), (0.70, 0.55)]; curuv = (0.40, 0.92)
q3 = [cam.p(u, v) for u, v in UV]; c3 = cam.p(*curuv)
poly(s, q3 + [c3], AREA, 0, fill=AREA, fop=0.14, closed=True)
poly(s, q3, AREA, 2)
s.line(*q3[-1], *c3, AREA, 1.6, dash="6 4"); s.line(*c3, *q3[0], AREA, 1.2, dash="2 4")
square(s, *q3[0], 10, "#FFFFFF", AREA, 2)
for q in q3[1:]: square(s, *q, 8, "#10302B", AREA)
crosshair(s, *c3, 8)
backdrop_text(s, c3[0] + 14, c3[1] - 28, ["3 points · Enter / Done: finish"])
s.text(xs[2] + 8, gy + PH - 10, "draped on the surface; squares stay screen-sized", C.NOTE, 10.5)

# ── 3 edit handles ──────────────────────────────────────────────────
y += PH + 70
row_title(s, y, "③", "Edit handles — vertices, waypoints, the centre, segment insert; and the same in 3-D",
          "lean: one handle look for every editor — white squares outlined in the kind colour; hovered = filled; grabbed = amber (red means an error); centre = a diamond with the move glyph")
xs = cols(s, y + 54, "big squares; grabbed = red; centre = orange; no 3-D handles")
gy = y + 54
today_panel(s, xs[0], gy)
R = [(70, 180), (170, 80), (290, 150)]
poly(s, [(xs[0] + a, gy + b) for a, b in R], "#4488FF", 1.2)
for i, (a, b) in enumerate(R): square(s, xs[0] + a, gy + b, 30, "#FF2020" if i == 1 else "#0A99E8", None)
square(s, xs[0] + 177, gy + 136, 30, "#F2A20A", None)
pixel_text(s, xs[0] + 10, gy + 214, "drag a waypoint or the centre (moves all) - click")
# 2-D proposal
map2d(s, xs[1], gy)
R2 = [(xs[1] + a, gy + b) for a, b in R]
poly(s, R2, ROUTEC, 2)
square(s, *R2[0], 9, "#FFFFFF", ROUTEC); square(s, *R2[1], 11, GRAB, "#000000"); square(s, *R2[2], 9, ROUTEC, "#FFFFFF")
mx, my = (R2[1][0] + R2[2][0]) / 2, (R2[1][1] + R2[2][1]) / 2
s.rect(mx - 6, my - 6, 12, 12, "#000000", stroke=ROUTEC, sw=1.2, dash="2 2", op=0.85); s.text(mx, my + 4, "+", ROUTEC, 12, "middle", "bold")
cxm = sum(a for a, _ in R2) / 3; cym = sum(b for _, b in R2) / 3
diamond(s, cxm, cym, 11, "#1A1A1A", "#FFFFFF"); s.glyph(FA.MOVE, cxm - 6, cym - 6, "#FFFFFF", 12)
backdrop_text(s, R2[1][0] + 14, R2[1][1] - 34, ["waypoint 2 · drag · Del: delete"])
s.callout(R2[0][0] + 6, R2[0][1] + 6, xs[1] + 120, gy + 196, ["white: idle · green: hovered · amber: grabbed", "dashed +: hover a segment to insert there"])
# 3-D proposal
cam = P3(s, xs[2], gy)
cam.box(0.70, 0.40, 0, 0.09, 0.08, 14, BLD, 0.95, "#4A5260")
RUV = [(0.25, 0.85, 0), (0.45, 0.50, 0), (0.70, 0.40, 14.2)]
r3 = [cam.p(u, v, h) for u, v, h in RUV]
poly(s, r3, ROUTEC, 2)
g3 = cam.p(0.70, 0.40, 0); s.line(r3[2][0], r3[2][1], g3[0], g3[1], ROUTEC, 1, dash="3 3")
square(s, *r3[0], 9, "#FFFFFF", ROUTEC); square(s, *r3[1], 9, "#FFFFFF", ROUTEC); square(s, *r3[2], 9, "#FFFFFF", ROUTEC)
cc = ((r3[0][0] + r3[1][0] + r3[2][0]) / 3, (r3[0][1] + r3[1][1] + r3[2][1]) / 3)
diamond(s, *cc, 11, "#1A1A1A", "#FFFFFF"); s.glyph(FA.MOVE, cc[0] - 6, cc[1] - 6, "#FFFFFF", 12)
backdrop_text(s, r3[2][0] - 150, r3[2][1] - 30, ["waypoint 3 · roof, level 1"])
s.text(xs[2] + 8, gy + PH - 10, "same handles on the surface, drawn on top; a raised point gets a stem", C.NOTE, 10.5)
s.save(sys.argv[1])

# ═════════════════════════════════════ SHEET 2 ═════════════════════════════════════
H2 = 60 + 3 * (PH + 70) + 10
s = Svg(W, H2, "B-17 P7 — the gizmo look, proposal 2/2 (design §2.6) · pick cursor + banner · rotate ring · screen text")
y = 60

# ── 4 pick cursor + banner ──────────────────────────────────────────
row_title(s, y, "④", "Pick cursor and banner",
          "lean: the OS crosshair cursor over the map while a pick is pending (one call, both views); the banner on a dark strip with a real Cancel button; the rejection on a backdrop")
xs = cols(s, y + 54, "no backdrop, a small orange +, '[Cancel]' as text")
gy = y + 54
today_panel(s, xs[0], gy)
pixel_text(s, xs[0] + 10, gy + 40, "[Cancel]", C.AMBER); pixel_text(s, xs[0] + 90, gy + 40, "Pick a unit for Target")
s.text(xs[0] + 180, gy + 115, "+", C.ORANGE, 16, "middle")
pixel_text(s, xs[0] + 185, gy + 142, "pick a unit, not a graphic", "#FF7070")
for xx, cam_ in [(xs[1], None), (xs[2], "3d")]:
    if cam_: c = P3(s, xx, gy); c.box(0.70, 0.45, 0, 0.09, 0.08, 14, BLD, 0.95, "#4A5260")
    else: map2d(s, xx, gy); s.rect(xx + 260, gy + 90, 70, 50, BLD, stroke="#4A5260")
    s.rect(xx + 46, gy + 10, 300, 30, "#22283A", stroke=C.INFO, rx=4)
    s.glyph(FA.CROSSHAIRS, xx + 56, gy + 18, C.GREEN, 14); s.text(xx + 76, gy + 30, "Pick a unit for  Target  (Alpha 1-1)", C.TXT, 12)
    s.button(xx + 280, gy + 15, 58, "Cancel")
    crosshair(s, xx + 150, gy + 140, 11)
    if cam_: rx0, ry0, rw, rh = xx + 245, gy + 104, 74, 72
    else: rx0, ry0, rw, rh = xx + 254, gy + 84, 82, 62
    s.rect(rx0, ry0, rw, rh, "none", stroke=C.ERR, rx=3, sw=1.6)
    backdrop_text(s, rx0 - 60, ry0 + rh + 8, ["a unit, not a building"], fg="#FF8A8A")
s.text(xs[1] + 8, gy + PH - 10, "banner = the existing ImGui strip style (P4) — same in both views", C.NOTE, 10.5)

# ── 5 rotate ring ───────────────────────────────────────────────────
y += PH + 70
row_title(s, y, "⑤", "Rotate — one unit, a group about its centre, a group each about its own centre",
          "lean: keep the ring and the live label; add the swept wedge from the start angle and a knob; label '+35°' on a backdrop; 'each' mode draws a small arc on every unit")
xs = cols(s, y + 54, "a thin yellow ring + ray; 'group -46deg', no backdrop")
gy = y + 54
today_panel(s, xs[0], gy)
oc = (xs[0] + 196, gy + 120)
s.raw(f"<circle cx='{oc[0]}' cy='{oc[1]}' r='86' fill='none' stroke='#FFFF00' stroke-width='1'/>")
s.line(*oc, oc[0] + 50, oc[1] - 120, "#FFFF00", 1.6)
pixel_text(s, oc[0] + 30, oc[1] - 82, "group -46deg")


def ring(s, cx, cy, r, a0, a1, units, each=False, label=True):
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='{r}' fill='none' stroke='{GRAB}' stroke-width='1.6' stroke-dasharray='6 4'/>")
    p = lambda a, rr: (cx + rr * math.sin(math.radians(a)), cy - rr * math.cos(math.radians(a)))
    large = 1 if abs(a1 - a0) > 180 else 0; sweep = 1 if a1 > a0 else 0
    s0, s1 = p(a0, r), p(a1, r)
    s.raw(f"<path d='M{cx} {cy} L{s0[0]:.1f} {s0[1]:.1f} A{r} {r} 0 {large} {sweep} {s1[0]:.1f} {s1[1]:.1f} Z' fill='{GRAB}' fill-opacity='0.16' stroke='none'/>")
    s.line(cx, cy, *s0, GRAB, 1, dash="3 3"); s.line(cx, cy, *s1, GRAB, 1.8)
    s.raw(f"<circle cx='{s1[0]:.1f}' cy='{s1[1]:.1f}' r='6' fill='{GRAB}' stroke='#000' stroke-width='1.2'/>")
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='3' fill='#FFFFFF'/>")
    for ux, uy, hd in units:
        s.raw(f"<g transform='rotate({hd + (a1 - a0)} {ux} {uy})'><rect x='{ux - 8}' y='{uy - 5}' width='16' height='10' fill='{SIDE}' fill-opacity='0.55' stroke='{SIDE}'/>"
              f"<polygon points='{ux + 8},{uy - 3} {ux + 12},{uy} {ux + 8},{uy + 3}' fill='{SIDE}'/></g>")
        if each: s.raw(f"<path d='M{ux + 14} {uy} A14 14 0 0 1 {ux + 14 * math.cos(math.radians(a1 - a0)):.1f} {uy + 14 * math.sin(math.radians(a1 - a0)):.1f}' fill='none' stroke='{GRAB}' stroke-width='1.4'/>")
    if not label: return
    lp = p(a1, r + 16)
    backdrop_text(s, lp[0] - 4, lp[1] - 10, [f"{'each ' if each else 'group '}{a1 - a0:+.0f}° · Shift: 15° steps"])


map2d(s, xs[1], gy)
ring(s, xs[1] + 170, gy + 128, 74, 0, 35, [(xs[1] + 140, gy + 110, 0), (xs[1] + 200, gy + 120, 10), (xs[1] + 165, gy + 160, -15)])
c = P3(s, xs[2], gy)
gc_ = c.p(0.45, 0.62)
s.raw(f"<g transform='translate({gc_[0]} {gc_[1]}) scale(1 0.42) translate({-gc_[0]} {-gc_[1]})'>")
ring(s, gc_[0], gc_[1], 92, 0, 35, [], each=False, label=False)
s.raw("</g>")
for u, v in [(0.38, 0.55), (0.52, 0.60), (0.44, 0.72)]:
    c.box(u, v, 0, 0.03, 0.025, 2.2, SIDE, 0.6, SIDE)
backdrop_text(s, gc_[0] + 40, gc_[1] - 70, ["group +35° · Shift: 15° steps"])
s.text(xs[2] + 8, gy + PH - 10, "the ring lies on the ground under the group", C.NOTE, 10.5)

# ── 6 screen text ───────────────────────────────────────────────────
y += PH + 70
row_title(s, y, "⑥", "Text on the map — hints, labels, the degrees readout",
          "lean: ONE backdrop for every screen-space gizmo text (black 60 %, 3 px radius) in the shared text drawer; load the font with '·', '°', '—', '×'; the tick button says 'Done'")
xs = cols(s, y + 54, "thin text straight on the ground; font loaded ASCII-only", ("TODAY", "PROPOSAL — both views", "PROPOSAL — the finish button"))
gy = y + 54
s.rect(xs[0], gy, PW, PH, MAPBG, stroke=C.BORDER)
for gx in range(int(xs[0]) + 20, int(xs[0] + PW) - 36, 36): s.line(gx, gy + 3, gx + 30, gy + PH - 3, "#3A5040", 5)
s.text(*s.pending_note[:2], s.pending_note[2], "#D8D8DE", 10.5)
pixel_text(s, xs[0] + 14, gy + 60, "click: add point ? Enter / tick: finish ? Esc", "#FFFFFF")
pixel_text(s, xs[0] + 14, gy + 110, "group -46deg", "#FFFFFF")
pixel_text(s, xs[0] + 14, gy + 160, "level 0 of 3 ? PgUp / PgDn", "#FFFFFF")
s.text(xs[0] + 8, gy + PH - 10, "'?' where '·' should be (to confirm on the real app)", C.NOTE, 10.5)
for xx in (xs[1], xs[2]):
    s.rect(xx, gy, PW, PH, MAPBG, stroke=C.BORDER)
    for gx in range(int(xx) + 20, int(xx + PW) - 36, 36): s.line(gx, gy + 3, gx + 30, gy + PH - 3, "#3A5040", 5)
backdrop_text(s, xs[1] + 14, gy + 46, ["click: add point · Enter / Done: finish (3+) · Esc: cancel"])
backdrop_text(s, xs[1] + 14, gy + 96, ["group −46°"])
backdrop_text(s, xs[1] + 14, gy + 146, ["level 0 of 3 · PgUp / PgDn"])
s.text(xs[1] + 8, gy + PH - 10, "Roboto at 13 px, readable on any ground", C.NOTE, 10.5)
s.button(xs[2] + 30, gy + 40, 70, "Done", glyph=FA.CHECK, gc=C.OK)
s.text(xs[2] + 110, gy + 54, "← replaces the bare tick box", C.SUB, 11.5)
s.rect(xs[2] + 30, gy + 80, 34, 34, "none", stroke=C.OK, sw=1.5); s.raw(f"<path d='M{xs[2] + 37} {gy + 97} l7 8 l14 -18' stroke='{C.OK}' fill='none' stroke-width='1.6'/>")
s.text(xs[2] + 76, gy + 102, "today: a tick box with no word", C.NOTE, 11.5)
s.note(xs[2] + 14, gy + 150, ["Roboto has no ✓, so the hint cannot say ✓;", "a button labelled 'Done' says the same in", "words the hint can repeat"], C.SUB)
s.save(sys.argv[2])
