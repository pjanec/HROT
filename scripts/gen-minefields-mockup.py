#!/usr/bin/env python3
"""CE-4624 MS-U1 (R-4143) — the MINEFIELDS mockup: one field on the 2-D map as three viewers see it (the ground truth, a side that has struck and
reconnoitred it, a side that has marked it), the 3-D look (the outline, the marking, the bursts, the clearer at work), the area tool's minefield
arm and the lane line, the field's details form, and the mine-clearing vehicle's kit + icon.
Output: docs/designs/minefields/minefields-visuals.svg (a PNG for the chat via mockup_kit.render, never committed).

SCOPE (the caption says it too): this specifies the minefield's drawing (2-D + 3-D), the minefield arm of the area tool, the lane line, the
Minefield details form, the clearer's body and icon, and the mine bursts. It specifies NOTHING else — the map's other layers, the toolbar, the
Details frame and its other tabs are context and are unchanged.

THE MINES ARE THE PATTERN'S: `mines()` below is MF-2's algorithm (rows at the spacing along the orientation, every other row offset by half a
spacing, clipped to the outline) — the picture is what the derived pattern gives, not a hand-placed decoration.
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA

W, H = 1500, 1330
s = Svg(W, H, "Minefields - the look and the authoring (CE-4624 MS-U1)",
        "Grey annotations are not drawn by the app. Scope: the field's 2-D/3-D drawing, the area tool's minefield arm + lane line, the Minefield form, the clearer's kit + icon, the bursts. Everything else is context, unchanged.")

FIELD_RED = "#E65555"; SUSPECT = "#F2C033"; MINED = "#E65555"; CLEAR = "#5BD06A"; LANE = "#FFFFFF"; GROUND = "#20261B"; OLIVE = "#56603A"
MINEFIELD = 0xe51b; BURST = 0xe4dc; BAN = 0xf05e; DIG = 0xf85e; ROAD = 0xf018


def panel(x, y, w, h, title):
    s.rect(x, y, w, h, "#15151A", stroke=C.BORDER)
    s.rect(x, y, w, 20, C.TITLE); s.text(x + 8, y + 14, title)


# ── the field, in metres (x east, y north), and MF-2's derived pattern ────────────────────────────────────────────────────────────────────────
OUTLINE = [(0, 0), (230, -20), (270, 90), (150, 140), (20, 120)]
SPACING = 14.0; ORIENT = math.radians(-5)        # rows run ~east (the orientation), staggered by half a spacing
LANE_A, LANE_B, LANE_W = (120, -40), (150, 160), 4.5


def inside(p, poly):
    x, y = p; c = False
    for i in range(len(poly)):
        (x1, y1), (x2, y2) = poly[i], poly[(i + 1) % len(poly)]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1: c = not c
    return c


def mines():
    """MF-2: (row, col, x, y) — rows along ORIENT at SPACING, odd rows offset by half a spacing, clipped to the outline."""
    ux, uy = math.cos(ORIENT), math.sin(ORIENT); vx, vy = -uy, ux
    ox, oy = OUTLINE[0]; out = []
    for r in range(-20, 21):
        for c in range(-5, 40):
            u = c * SPACING + (SPACING / 2 if r % 2 else 0); v = r * SPACING
            p = (ox + u * ux + v * vx, oy + u * uy + v * vy)
            if inside(p, OUTLINE): out.append((r, c, p[0], p[1]))
    return out


def in_lane(p):
    ax, ay = LANE_A; bx, by = LANE_B; dx, dy = bx - ax, by - ay; L = math.hypot(dx, dy)
    t = ((p[0] - ax) * dx + (p[1] - ay) * dy) / (L * L)
    if t < 0 or t > 1: return False
    return abs(((p[0] - ax) * dy - (p[1] - ay) * dx) / L) <= LANE_W / 2 + SPACING * 0.25


MINES = mines()
STRUCK = {(1, 9)}                           # the patrol's strike (row, col)
TYPE = lambda r, c: "AT" if (r + c) % 4 == 0 else "AP"   # mixed field: every 4th lattice point an AT mine


def tile(x, y, w, h, k, title, sub):
    s.rect(x, y, w, h, GROUND, stroke=C.BORDER)
    s.text(x + 8, y + 16, title, C.TXT, 12); s.text(x + 8, y + 32, sub, C.NOTE, 10.5)
    ox, oy = x + (w - 270 * k) / 2, y + 40 + 160 * k   # the field centred in the tile
    return lambda px, py: (ox + px * k, oy - py * k)


def poly(P, pts, **kw):
    d = " ".join("%.1f,%.1f" % P(a, b) for a, b in pts)
    attrs = " ".join(f"{k.replace('_', '-')}='{v}'" for k, v in kw.items())
    s.raw(f"<polygon points='{d}' {attrs}/>")


def mine_dot(P, mx, my, kind, struck=False):
    X, Y = P(mx, my)
    if struck:
        s.glyph(BURST, X - 7, Y - 7, "#FF8A3A", 14); return
    if kind == "AT": s.raw(f"<rect x='{X - 2.6:.1f}' y='{Y - 2.6:.1f}' width='5.2' height='5.2' fill='#C9B07A' stroke='#14141A' stroke-width='0.8'/>")
    else: s.raw(f"<circle cx='{X:.1f}' cy='{Y:.1f}' r='1.9' fill='#D8D0B8' stroke='#14141A' stroke-width='0.6'/>")


def lane(P, marked=True, colour=LANE):
    ax, ay = LANE_A; bx, by = LANE_B; dx, dy = bx - ax, by - ay; L = math.hypot(dx, dy); nx, ny = -dy / L * LANE_W / 2, dx / L * LANE_W / 2
    for sg in (1, -1):
        (x1, y1), (x2, y2) = P(ax + nx * sg * 2.2, ay + ny * sg * 2.2), P(bx + nx * sg * 2.2, by + ny * sg * 2.2)
        s.raw(f"<line x1='{x1:.1f}' y1='{y1:.1f}' x2='{x2:.1f}' y2='{y2:.1f}' stroke='{colour}' stroke-width='1.6' stroke-dasharray='{'7 4' if marked else '2 3'}'/>")
    if marked:   # entry / exit chevrons (the lane's direction of travel)
        for t in (0.08, 0.92):
            cx, cy = P(ax + dx * t, ay + dy * t); ang = math.degrees(math.atan2(-(dy), dx))
            s.raw(f"<path d='M -5 -5 L 2 0 L -5 5' fill='none' stroke='{colour}' stroke-width='2' transform='translate({cx:.1f},{cy:.1f}) rotate({ang:.1f})'/>")


def outline_graphic(P, colour, hatch=True):
    """The tactical graphic of a known / marked field: the outline, a light hatch, a mine symbol every ~60 m along the edge."""
    if hatch:
        s.raw(f"<defs><pattern id='mfh{colour[1:]}' width='9' height='9' patternUnits='userSpaceOnUse' patternTransform='rotate(45)'>"
              f"<line x1='0' y1='0' x2='0' y2='9' stroke='{colour}' stroke-width='1' stroke-opacity='0.45'/></pattern></defs>")
        poly(P, OUTLINE, fill=f"url(#mfh{colour[1:]})", stroke="none")
    poly(P, OUTLINE, fill="none", stroke=colour, stroke_width="2")
    for i in range(len(OUTLINE)):
        (x1, y1), (x2, y2) = OUTLINE[i], OUTLINE[(i + 1) % len(OUTLINE)]
        n = max(1, int(math.hypot(x2 - x1, y2 - y1) / 60))
        for j in range(n):
            t = (j + 0.5) / n; X, Y = P(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t)
            s.raw(f"<circle cx='{X:.1f}' cy='{Y:.1f}' r='5' fill='{GROUND}' stroke='{colour}' stroke-width='1.6'/>")
            s.raw(f"<line x1='{X - 3:.1f}' y1='{Y + 3:.1f}' x2='{X + 3:.1f}' y2='{Y - 3:.1f}' stroke='{colour}' stroke-width='1.4'/>")


# ── A: the 2-D map — one field, three viewers ─────────────────────────────────────────────────────────────────────────────────────────────────
panel(20, 60, 1460, 410, "2-D map - ONE field, three viewers (MinefieldGizmo; the generic overlay never draws it; P-3 gate: off = truth for everyone)")
K = 1.45
# 1) the instructor / gate off: ground truth
P = tile(36, 90, 470, 350, K, "Instructor, or the side-knowledge gate OFF: the TRUTH",
         "every mine (AP dot, AT square), the struck one, the cleared lane")
outline_graphic(P, FIELD_RED, hatch=False)
for r, c, mx, my in MINES:
    if in_lane((mx, my)): continue
    mine_dot(P, mx, my, TYPE(r, c), (r, c) in STRUCK)
lane(P)
lx, ly = P(*LANE_B); s.text(lx + 6, ly + 4, "lane 1 - cleared by Blue", C.SUB, 10.5)
# 2) Blue, gate on, after a strike and a partial recon: the knowledge grid
P = tile(516, 90, 470, 350, K, "Blue seat, gate ON: what Blue KNOWS (its knowledge grid, 10 m cells)",
         "amber suspected (50 m) · red known-mined · green known-clear")
strike = next((mx, my) for r, c, mx, my in MINES if (r, c) in STRUCK)
CELL = 10.0
ux, uy = math.cos(ORIENT), math.sin(ORIENT); vx, vy = -uy, ux
trace_cells = set()
for i in range(-8, 9):            # the recon walked west along the struck edge: cells within its sweep are known
    for j in range(-6, 7):
        cx_, cy_ = strike[0] + (i + 0.5) * CELL * ux + (j + 0.5) * CELL * vx, strike[1] + (i + 0.5) * CELL * uy + (j + 0.5) * CELL * vy
        d = math.hypot(cx_ - strike[0], cy_ - strike[1])
        corners = [(cx_ + a * CELL / 2 * ux + b * CELL / 2 * vx, cy_ + a * CELL / 2 * uy + b * CELL / 2 * vy) for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        swept = -3 <= i <= 1 and -4 <= j <= -1
        if swept:
            col = MINED if inside((cx_, cy_), OUTLINE) else CLEAR
            poly(P, corners, fill=col, fill_opacity="0.40", stroke=col, stroke_opacity="0.7", stroke_width="0.6")
        elif d <= 50:
            poly(P, corners, fill=SUSPECT, fill_opacity="0.22", stroke=SUSPECT, stroke_opacity="0.35", stroke_width="0.5")
X, Y = P(*strike); s.glyph(BURST, X - 8, Y - 8, "#FF8A3A", 16)
s.text(X + 12, Y - 30, "the strike: Blue suspects 50 m round it at once", C.SUB, 10.5)
X2, Y2 = P(strike[0] + 30, strike[1] - 30); s.glyph(DIG, X2 - 7, Y2 - 7, C.BLUE, 14); s.text(X2 + 12, Y2 + 4, "engineers tracing the edge", C.SUB, 10.5)
s.note(530, 145, ["no outline, no dots: Blue has not seen the field - only its cells"], C.NOTE, 10.5)
# 3) Blue, gate on, the field MARKED: the tactical graphic + the lane
P = tile(996, 90, 470, 350, K, "Blue seat, the field MARKED (or own, known at start)",
         "outline + hatch + edge symbols + Blue's lane; no single mines")
outline_graphic(P, FIELD_RED)
lane(P)
cx, cy = P(135, 60)
s.rect(cx - 52, cy - 11, 104, 18, "#15151A", stroke=FIELD_RED, rx=3, op=0.9); s.glyph(MINEFIELD, cx - 46, cy - 8, FIELD_RED, 12)
s.text(cx - 28, cy + 3, "Mixed · 14 m", C.TXT, 10.5)

# ── B: the 3-D look ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
panel(20, 480, 900, 360, "3-D map - a field is invisible until known; known = outline on the ground; MARKED = pickets and signs; the bursts; the clearer")
bx, by = 30, 505
s.rect(bx, by, 880, 325, "#7FA6D0")
s.raw(f"<polygon points='{bx},{by + 120} {bx + 880},{by + 105} {bx + 880},{by + 325} {bx},{by + 325}' fill='#5E7D3A'/>")
# the field draped (perspective quad) + pickets with red triangle signs + the lane's white pickets
F = [(bx + 120, by + 270), (bx + 560, by + 285), (bx + 640, by + 175), (bx + 380, by + 150), (bx + 150, by + 165)]
s.raw("<polygon points='" + " ".join(f"{a},{b}" for a, b in F) + f"' fill='none' stroke='{FIELD_RED}' stroke-width='2' stroke-dasharray='10 6'/>")
for i, (a, b) in enumerate(F):
    nx_, ny_ = F[(i + 1) % len(F)]
    for t in (0.0, 0.5):
        px_, py_ = a + (nx_ - a) * t, b + (ny_ - b) * t; hpx = 26 * (py_ - by) / 280 + 8
        s.line(px_, py_, px_, py_ - hpx, "#3A2A1A", 2)
        tri = f"{px_ - hpx * 0.32:.1f},{py_ - hpx:.1f} {px_ + hpx * 0.32:.1f},{py_ - hpx:.1f} {px_:.1f},{py_ - hpx * 0.55:.1f}"
        s.raw(f"<polygon points='{tri}' fill='{FIELD_RED}' stroke='#3A1010' stroke-width='0.8'/>")
L1, L2 = (bx + 330, by + 285), (bx + 420, by + 150)
for sg in (-1, 1):
    for t in (0.1, 0.4, 0.7, 0.95):
        px_ = L1[0] + (L2[0] - L1[0]) * t + sg * (14 - 8 * t); py_ = L1[1] + (L2[1] - L1[1]) * t; hpx = 20 * (py_ - by) / 280 + 6
        s.line(px_, py_, px_, py_ - hpx, "#F0F0F0", 2.2)
s.raw(f"<polygon points='{L1[0] - 14},{L1[1]} {L1[0] + 14},{L1[1]} {L2[0] + 6},{L2[1]} {L2[0] - 6},{L2[1]}' fill='#8C7A4A' fill-opacity='0.55'/>")
s.text(bx + 432, by + 145, "the lane: churned earth + white pickets", "#14141A", 11)
s.text(bx + 140, by + 300, "marked field: red triangle signs on pickets along the edge (a known, unmarked field: the dashed outline only)", "#14141A", 11)
# instructor dots (only the instructor)
for (dx_, dy_) in ((220, 235), (260, 240), (300, 245), (240, 210), (280, 215), (500, 250), (540, 255), (470, 220), (520, 225), (560, 200)):
    s.raw(f"<ellipse cx='{bx + dx_}' cy='{by + dy_}' rx='3.5' ry='1.6' fill='#D8D0B8' stroke='#14141A' stroke-width='0.5'/>")
s.text(bx + 200, by + 195, "instructor only: the mines as discs", "#14141A", 10.5)
# the bursts
def puff(x, y, r, col, op):
    s.raw(f"<circle cx='{x}' cy='{y}' r='{r}' fill='{col}' fill-opacity='{op}'/>")
for dx_, dy_, r in ((0, 0, 9), (-7, -8, 7), (7, -10, 8), (0, -18, 6)): puff(bx + 470 + dx_, by + 262 + dy_, r, "#8A7A60", 0.85)
s.raw(f"<circle cx='{bx + 470}' cy='{by + 262}' r='4' fill='#FFC14A'/>")
s.text(bx + 445, by + 300 - 0, "", C.NOTE, 1)
s.text(bx + 490, by + 245, "AP: a small dust burst at the foot", "#14141A", 10.5)
for dx_, dy_, r in ((0, 0, 14), (-6, -22, 13), (6, -42, 12), (-3, -62, 11), (4, -80, 9)): puff(bx + 700 + dx_, by + 250 + dy_, r, "#4A4038", 0.9)
s.raw(f"<circle cx='{bx + 700}' cy='{by + 250}' r='9' fill='#FF9A3A'/>")
s.text(bx + 718, by + 160, "AT: a tall dark column under the hull", "#14141A", 10.5)
s.text(bx + 718, by + 174, "(the munition's own detonation effect, VE-D)", "#14141A", 10.5)
s.note(40, 838, [""], C.NOTE, 1)


# ── E: the clearer (TKB 116), side elevation ─────────────────────────────────────────────────────────────────────────────────────────────────
def clearer(ox, oy, k, flail_phase=0.0, plough=False):
    def R(x, z, w, h, fill, rot=0):
        a = ox + (x - w / 2) * k; b = oy - (z + h / 2) * k
        s.raw(f"<rect x='{a:.1f}' y='{b:.1f}' width='{w * k:.1f}' height='{h * k:.1f}' fill='{fill}' stroke='#14141A' stroke-width='1' transform='rotate({rot} {a + w * k / 2:.1f} {b + h * k / 2:.1f})'/>")
    R(0, 0.45, 7.0, 0.9, "#2E2E2A")                      # tracks
    for i in range(7): R(-3.0 + i, 0.45, 0.62, 0.62, "#4A4A44")
    R(-0.2, 1.35, 6.6, 1.0, OLIVE)                      # hull
    R(-0.8, 2.2, 3.0, 0.75, OLIVE)                      # superstructure (no turret: an engineer vehicle)
    R(-2.6, 2.75, 1.6, 0.45, "#6B7246")                 # the line-charge launcher box (rear)
    R(-2.0, 3.0, 1.2, 0.08, "#2E2E2A", -18)
    R(3.6, 1.55, 1.8, 0.22, "#2E2E2A", 22)              # the arms
    if plough:
        R(4.7, 0.45, 0.5, 0.9, "#6A6A66", -20)          # the plough blade
    else:
        R(4.6, 0.75, 0.75, 0.75, "#3A3A36")              # the flail drum
        for i in range(8):
            a = flail_phase + i * math.pi / 4
            x1 = 4.6 + math.cos(a) * 0.38; z1 = 0.75 + math.sin(a) * 0.38
            x2 = 4.6 + math.cos(a) * 0.95; z2 = max(0.05, 0.75 + math.sin(a) * 0.95)
            s.line(ox + x1 * k, oy - z1 * k, ox + x2 * k, oy - z2 * k, "#1E1E1A", 1.6)
            s.raw(f"<circle cx='{ox + x2 * k:.1f}' cy='{oy - z2 * k:.1f}' r='{0.08 * k:.1f}' fill='#1E1E1A'/>")
    s.line(ox - 3.8 * k, oy + 4, ox + 3.8 * k, oy + 4, "#14141A", 1)


panel(930, 480, 550, 360, "The mine clearer (TKB 116 Engineer_MineClearer) - flail / plough + line charge")
s.rect(940, 505, 530, 230, "#7FA6D0"); s.rect(940, 690, 530, 45, "#5E7D3A")
clearer(1160, 690, 28, 0.3)
for dx_, dy_, r in ((0, 0, 10), (-12, -6, 8), (10, -8, 9)): puff(1300 + dx_, 682 + dy_, r, "#8A7A60", 0.6)
s.text(950, 525, "flail: the drum spins (PartRole time-driven), chains lash, dust", "#14141A", 11)
s.text(950, 540, "flail loop sound while the flail is ON; mines in its swath burst harmlessly", "#14141A", 11)
s.text(1110, 600, "line-charge launcher (rear)", "#14141A", 10.5); s.line(1150, 604, 1105, 612, "#14141A", 1)
s.text(1260, 725, "1 m", "#14141A", 10.5); s.line(1260, 712, 1288, 712, "#14141A", 2)
# icon + thumbnail + the plough variant
s.rect(940, 745, 86, 86, "#20261B", stroke=C.BORDER); clearer(983, 805, 9, 0.6)
s.text(983, 828, "thumbnail", C.SUB, 10, "middle")
s.rect(1036, 745, 86, 86, "#20261B", stroke=C.BORDER); clearer(1079, 805, 9, 0, plough=True)
s.text(1079, 828, "plough variant", C.SUB, 10, "middle")
s.rect(1132, 745, 60, 60, "#20261B", stroke=C.BORDER); s.glyph(DIG, 1146, 761, C.AMBER, 30)
s.text(1162, 828, "2-D icon", C.SUB, 10, "middle")
s.note(1205, 760, ["family MineClearer (class 1.1.*.9.2), its OWN kit;", "picker glyph assets/icons/_mineclearer;", "2-D: the vehicle box + the engineer glyph", "(as the bulldozer)"], C.NOTE, 10.5)

# ── C: the area tool's minefield arm + the lane line ─────────────────────────────────────────────────────────────────────────────────────────
panel(20, 850, 700, 460, "Authoring - the ONE area tool's minefield arm (a closed polyline) and the lane (the area tool in LINE mode)")
P = lambda px, py: (110 + px * 1.45, 1150 - py * 1.45)
s.rect(36, 878, 668, 330, GROUND, stroke=C.BORDER)
poly(P, OUTLINE, fill=FIELD_RED, fill_opacity="0.08", stroke=FIELD_RED, stroke_width="1.6", stroke_dasharray="6 4")
for a, b in OUTLINE:
    X, Y = P(a, b); s.raw(f"<circle cx='{X:.1f}' cy='{Y:.1f}' r='4' fill='#fff' stroke='{FIELD_RED}'/>")
for r, c, mx, my in MINES:                               # the author sees the derived pattern while drawing (preview, light)
    X, Y = P(mx, my); s.raw(f"<circle cx='{X:.1f}' cy='{Y:.1f}' r='1.5' fill='#D8D0B8' fill-opacity='0.6'/>")
cx0, cy0 = P(135, 60); ax_, ay_ = cx0 + math.cos(ORIENT) * 60, cy0 - math.sin(ORIENT) * 60
s.raw(f"<line x1='{cx0:.1f}' y1='{cy0:.1f}' x2='{ax_:.1f}' y2='{ay_:.1f}' stroke='{C.AMBER}' stroke-width='2.2'/>")
s.raw(f"<circle cx='{ax_:.1f}' cy='{ay_:.1f}' r='5' fill='{C.AMBER}' stroke='#14141A'/>")
s.text(ax_ + 8, ay_ + 4, "row orientation (drag the handle)", C.SUB, 10.5)
la, lb = P(*LANE_A), P(*LANE_B)
s.raw(f"<line x1='{la[0]:.1f}' y1='{la[1]:.1f}' x2='{lb[0]:.1f}' y2='{lb[1]:.1f}' stroke='{LANE}' stroke-width='2' stroke-dasharray='7 4'/>")
for X, Y in (la, lb): s.raw(f"<circle cx='{X:.1f}' cy='{Y:.1f}' r='4' fill='{C.BLUE}' stroke='#fff'/>")
s.text(lb[0] + 8, lb[1] + 18, "Add lane: two clicks, 4.5 m wide", C.SUB, 10.5)
s.text(44, 896, "drawing: click the corners, close on the first point / double-click; the mines preview as you draw (MF-2)", C.SUB, 11)
s.rect(36, 1214, 668, 22, C.POPUP, stroke=C.SEP); s.glyph(FA.INFO, 44, 1218, C.INFO, 13)
s.text(64, 1230, "Minefield: click corners, double-click or the first point to close  ·  Esc cancels", C.SUB, 11)
s.rect(36, 1242, 668, 22, C.ERRROW, stroke=C.ERR); s.glyph(FA.LOCK, 44, 1246, C.ERR, 13)
s.text(64, 1258, "Shape locked: 1 mine has gone off in this field - delete and re-lay it to change the outline", C.TXT, 11)
s.note(44, 1282, ["refused edit (MR-8): a struck field's corners do not move; the hint line says why; type / spacing / lanes still edit"], C.NOTE, 10.5)

# ── D: the details form ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
fx, fy, fw = 730, 850, 750
panel(fx, fy, fw, 460, "Details - the Minefield form (one edit per change, undoable; the same rows as the animal area's form)")
fx += 10; fy += 30; fw -= 20
s.header(fx, fy, fw, "Minefield", glyph=MINEFIELD, gc=FIELD_RED)
LW = 150; yy = fy + 34
def row(label, draw, hint=None):
    global yy
    s.text(fx + 8, yy + 14, label, C.SUB); draw(fx + LW, yy)
    if hint: s.text(fx + LW + 230, yy + 14, hint, C.NOTE, 10.5)
    yy += 30
row("Type", lambda x, y: s.combo(x, y, 210, "Mixed (AP + AT)"), "AP: any person or vehicle · AT: vehicles only")
row("Spacing", lambda x, y: s.field(x, y, 90, "14 m"), "between mines in a row and between rows (staggered)")
row("Row orientation", lambda x, y: s.field(x, y, 90, "85°"), "the rows' bearing; the handle on the map edits it too")
row("Seed", lambda x, y: s.field(x, y, 90, "1"), "jitter only (0 = exact pattern)")
row("Anti-handling", lambda x, y: s.checkbox(x, y, False), "a plough / flail misses more (MR-15)")
def sides(x, y):
    s.checkbox(x, y, True); s.text(x + 24, y + 14, "Blue", C.BLUE)
    s.checkbox(x + 80, y, False); s.text(x + 104, y + 14, "Red", C.RED)
    s.checkbox(x + 160, y, False); s.text(x + 184, y + 14, "Green", C.GREEN)
row("Known at start", sides, "a side that knows it avoids it (MF-15)")
N_AT = sum(1 for r, c, _, _ in MINES if TYPE(r, c) == "AT"); N_LANE = sum(1 for _, _, mx, my in MINES if in_lane((mx, my)))
row("Mines", lambda x, y: s.text(x, y + 14, f"{len(MINES)} ({len(MINES) - N_AT} AP, {N_AT} AT) · 1 gone off · {N_LANE} cleared", C.TXT), None)
yy += 4
s.header(fx, yy, fw, "Lanes", glyph=ROAD, gc=C.GREY); yy += 30
s.text(fx + 8, yy + 14, "1", C.SUB); s.text(fx + 30, yy + 14, "4.5 m · 185 m long", C.TXT); s.text(fx + 200, yy + 14, "cleared by", C.SUB)
s.text(fx + 275, yy + 14, "Blue", C.BLUE); s.text(fx + 330, yy + 14, "known to", C.SUB); s.text(fx + 395, yy + 14, "Blue", C.BLUE)
s.glyph(FA.TRASH, fx + fw - 30, yy + 2, C.GREY, 14); yy += 30
s.button(fx + 8, yy, 120, "Add lane", glyph=FA.PLUS, gc=C.GREEN)
s.text(fx + 140, yy + 14, "arms the area tool in line mode: two clicks (MR-18)", C.NOTE, 10.5); yy += 36
s.note(fx + 8, yy + 8, ["the outline is the field's ONE editable polyline (its vertices on the map); the form holds only the parameters (MR-8)",
                        "the mine counts are derived (MF-2) - nothing per mine is stored"], C.NOTE, 10.5)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "designs", "minefields", "minefields-visuals.svg")
s.save(out)
print(out, len(MINES), "mines")
