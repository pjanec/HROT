#!/usr/bin/env python3
"""CE-4621 M-1v / U-1 (R-4143) — the MOTORCYCLE mockup: the bike in 2-D and 3-D, rider and pillion seated, the pillion's fire arc when selected.
Output: docs/designs/vehicle-tactics/motorcycle-visuals.svg (a PNG for the chat via mockup_kit.render, never committed).

SCOPE (the caption says it too): this specifies the bike's silhouette, its two seated riders and the pillion's firing pose + arc. It specifies
NOTHING else — the HUD, the picker row layout, the 2-D symbol set are drawn for context from the real UI and are unchanged.
Metres: x forward, y left, z up. Bike 2.1 x 0.8 x 1.15 (to the bars); wheels 0.62 m; wheelbase 1.4 m.
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

W, H = 1240, 800
s = Svg(W, H, "Motorcycle - the look (CE-4621 M-1v)",
        "Grey annotations are not drawn by the app. Scope: the bike, two seated riders, the pillion's pose and arc.")

BIKE = "#7A8466"; DARK = "#2A3139"; METAL = "#9AA3AE"; SKIN = "#D9A88A"; CLOTH = "#5C6B4A"; LEG = "#3B4A5C"; GLASS = "#6FA0C8"


def panel(x, y, w, h, title):
    s.rect(x, y, w, h, "#15151A", stroke=C.BORDER)
    s.rect(x, y, w, 20, C.TITLE); s.text(x + 8, y + 14, title)


# ── the side elevation ──────────────────────────────────────────────────────────────────────────────────────────────────────────
def elevation(ox, oy, k, mode):
    """mode: 'ride' (driver + pillion, arms on the bars / the rider) or 'fire' (the pillion turned back, rifle shouldered)."""
    def P(x, z): return (ox + x * k, oy - z * k)
    def poly(pts, fill, stroke="#1B2027", sw=1.6):
        d = " ".join(f"{px:.1f},{py:.1f}" for px, py in (P(*p) for p in pts))
        s.raw(f"<polygon points='{d}' fill='{fill}' stroke='{stroke}' stroke-width='{sw}' stroke-linejoin='round'/>")
    def seg(a, b, c, w):
        (x1, y1), (x2, y2) = P(*a), P(*b)
        s.raw(f"<line x1='{x1:.1f}' y1='{y1:.1f}' x2='{x2:.1f}' y2='{y2:.1f}' stroke='{c}' stroke-width='{w * k:.1f}' stroke-linecap='round'/>")
    def circ(x, z, r, fill, stroke="#1B2027", sw=1.6):
        cx, cy = P(x, z); s.raw(f"<circle cx='{cx:.1f}' cy='{cy:.1f}' r='{r * k:.1f}' fill='{fill}' stroke='{stroke}' stroke-width='{sw}'/>")
    s.line(ox - 1.6 * k, oy, ox + 1.6 * k, oy, "#4A4A55", 1)
    # wheels (rear wider), spokes at an angle so the roll shows
    for x, wr in ((0.70, 0.31), (-0.70, 0.31)):
        circ(x, wr, wr, "#1B2027"); circ(x, wr, wr * 0.62, "#6E7A87", sw=1.2); circ(x, wr, 0.05, "#3A434E", sw=1)
        for a in (20, 110):
            ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
            seg((x - ca * wr * 0.9, wr - sa * wr * 0.9), (x + ca * wr * 0.9, wr + sa * wr * 0.9), "#C9D1DA", 0.018)
    # rear: swing arm, shock, tail, seat, rack
    seg((-0.35, 0.45), (-0.70, 0.31), DARK, 0.05)
    poly([(-0.95, 0.82), (-0.80, 0.90), (-0.55, 0.88), (-0.55, 0.80)], DARK)          # the rack behind the pillion
    poly([(-0.92, 0.78), (-0.60, 0.84), (-0.20, 0.86), (0.20, 0.86), (0.22, 0.78), (-0.20, 0.70), (-0.80, 0.66)], BIKE)   # tail + dual seat body
    poly([(-0.88, 0.86), (0.22, 0.89), (0.22, 0.84), (-0.88, 0.82)], DARK, sw=1.2)    # the dual seat
    # frame, engine, tank, exhaust
    poly([(0.58, 0.98), (0.12, 0.84), (-0.30, 0.72), (-0.35, 0.45), (0.02, 0.36), (0.36, 0.50)], "#46505C")
    poly([(0.00, 0.30), (0.34, 0.30), (0.34, 0.60), (0.00, 0.60)], METAL)             # engine block
    poly([(-0.55, 0.30), (0.05, 0.22), (0.05, 0.28), (-0.55, 0.36)], "#59606A")       # exhaust
    poly([(0.04, 0.84), (0.48, 0.86), (0.54, 0.96), (0.50, 1.03), (0.10, 0.98)], BIKE)   # fuel tank
    # fork, bars, headlight, mudguard
    seg((0.60, 1.00), (0.70, 0.31), "#C9D1DA", 0.035)
    poly([(0.52, 0.52), (0.82, 0.52), (0.86, 0.60), (0.56, 0.62)], BIKE)               # front mudguard
    circ(0.69, 0.93, 0.085, GLASS); seg((0.56, 1.05), (0.50, 1.10), DARK, 0.03)
    # the riders
    def rider(hx, hz, lean_deg, arm_to, torso_len=0.55):
        a = math.radians(90 - lean_deg)   # torso angle from the horizontal: leaning forward = smaller
        sx, sz = hx + math.cos(a) * torso_len, hz + math.sin(a) * torso_len
        foot = (hx + 0.16, hz - 0.46)
        knee = (hx + 0.34, hz - 0.14)
        seg((hx, hz), knee, LEG, 0.14); seg(knee, foot, LEG, 0.12)                       # thigh, shin on the peg
        seg((hx, hz), (sx, sz), CLOTH, 0.26)                                              # torso
        circ(sx + 0.05, sz + 0.17, 0.115, SKIN); poly([(sx - 0.07, sz + 0.20), (sx + 0.16, sz + 0.22), (sx + 0.15, sz + 0.28), (sx - 0.05, sz + 0.30)], "#3C4636", sw=1.2)   # head + helmet
        seg((sx, sz - 0.05), arm_to, CLOTH, 0.09)
        return sx, sz
    rider(0.02, 0.88, 22, (0.56, 1.08))                                                   # the driver: hands on the bars
    if mode == "ride":
        rider(-0.52, 0.88, 8, (0.22, 1.00))                                               # the pillion: hands on the driver's waist
    else:
        # the pillion TURNED to his arc: seen from the left, his torso faces us / the rear — a broader torso, the rifle shouldered, pointing back-left
        hx, hz = -0.52, 0.88
        seg((hx, hz), (hx + 0.34, hz - 0.14), LEG, 0.14); seg((hx + 0.34, hz - 0.14), (hx + 0.16, hz - 0.46), LEG, 0.12)
        seg((hx, hz), (hx - 0.04, hz + 0.55), CLOTH, 0.34)
        circ(hx - 0.08, hz + 0.72, 0.115, SKIN); poly([(hx - 0.19, hz + 0.75), (hx + 0.03, hz + 0.76), (hx + 0.02, hz + 0.83), (hx - 0.17, hz + 0.84)], "#3C4636", sw=1.2)
        seg((hx - 0.06, hz + 0.42), (hx - 0.62, hz + 0.50), DARK, 0.045)                  # the rifle, level, pointing back
        seg((hx - 0.02, hz + 0.40), (hx - 0.34, hz + 0.47), CLOTH, 0.08)                  # his arms on it
    return P


# ── panel A: 3-D, riding ───────────────────────────────────────────────────────────────────────────────────────────────────────────────
panel(20, 60, 600, 330, "3-D map — the motorcycle, two up (side view)")
P = elevation(330, 362, 112, "ride")
s.callout(P(0.62, 1.15)[0], P(0.62, 1.15)[1], 440, 100, ["driver, seat 0 (Ride pose):", "straddles the tank, leans 17°,", "hands on the bars"])
s.callout(P(-0.52, 1.50)[0], P(-0.52, 1.50)[1], 36, 100, ["pillion, seat 1 (Ride pose):", "sits upright behind, hands on", "the driver's waist; helmet, rifle slung"])
s.callout(P(0.70, 0.12)[0], P(0.70, 0.12)[1], 440, 340, ["two wheels (not the 4-wheel car kit);", "they roll, a turn leans the bike"])
s.note(36, 380, ["bike 2.1 × 0.8 × 1.15 m · wheels 0.62 m · wheelbase 1.4 m — metres are the TKB's metres"], C.NOTE, 11)

# ── panel B: 3-D, pillion firing ───────────────────────────────────────────────────────────────────────────────────────────────────────────
panel(640, 60, 580, 330, "3-D map — the pillion FIRES: torso turned to his arc")
P = elevation(940, 362, 112, "fire")
s.callout(P(-0.62, 1.50)[0], P(-0.62, 1.50)[1], 656, 100, ["pillion: torso twisted to the aim", "(up to 160°; the seat arc limits),", "legs stay astride; eased"])
s.callout(P(0.62, 1.15)[0], P(0.62, 1.15)[1], 1030, 100, ["rider: never fires moving"])
s.note(656, 380, ["drawn from the weapon brain's replicated posture + aim point: every host shows the same"], C.NOTE, 11)

# ── panel C: plan view with the fire arc ──────────────────────────────────────────────────────────────────────────────────────────────────
panel(20, 410, 600, 380, "Plan — the pillion's fire arc when the bike is selected")
ox, oy, k = 300, 610, 80
def PX(x, y): return ox - y * k, oy - x * k * -1 * -1  # placeholder, replaced below
def Q(x, y): return (ox + x * k, oy - y * k)          # x forward = right on this plan, y left = up
# the arc: the pillion's hips at (-0.52, 0); both sides and the rear, never forward across the rider  → 70° .. 290° (from the forward axis, ccw)
hx, hy = -0.52, 0.0
R = 1.9
def pt(angle_deg, r): a = math.radians(angle_deg); return Q(hx + math.cos(a) * r, hy + math.sin(a) * r)
pts = [Q(hx, hy)] + [pt(a, R) for a in range(70, 291, 5)]
s.raw("<polygon points='" + " ".join(f"{px:.1f},{py:.1f}" for px, py in pts) + f"' fill='{C.AMBER}' fill-opacity='0.18' stroke='{C.AMBER}' stroke-width='1.4' stroke-dasharray='5 4'/>")
for a in (70, 290):
    p2 = pt(a, R); q0 = Q(hx, hy); s.line(q0[0], q0[1], p2[0], p2[1], C.AMBER, 1.6)
# the bike: wheels, body, the two riders
def box(x0, y0, x1, y1, fill, rx=3, stroke="#1B2027"):
    a, b = Q(x0, y1), Q(x1, y0); s.rect(a[0], a[1], b[0] - a[0], b[1] - a[1], fill, stroke=stroke, rx=rx)
box(0.39, -0.055, 1.01, 0.055, "#1B2027"); box(-1.01, -0.08, -0.39, 0.08, "#1B2027")      # wheels from above
box(-0.80, -0.11, 0.55, 0.11, BIKE, 6)                                                     # the body: tank + seat
box(0.45, -0.36, 0.62, 0.36, DARK, 2)                                                      # the bars
# driver
q = Q(0.02, 0); s.raw(f"<ellipse cx='{q[0]:.1f}' cy='{q[1]:.1f}' rx='{0.11 * k:.1f}' ry='{0.21 * k:.1f}' fill='{CLOTH}' stroke='#1B2027'/>")
q = Q(0.12, 0); s.raw(f"<circle cx='{q[0]:.1f}' cy='{q[1]:.1f}' r='{0.115 * k:.1f}' fill='#3C4636' stroke='#1B2027'/>")
# pillion, twisted toward the rear-left (135°): the shoulders turned, the rifle out along the aim
tw = math.radians(135)
q = Q(hx, hy)
s.raw(f"<g transform='translate({q[0]:.1f},{q[1]:.1f}) rotate({-135})'><ellipse cx='0' cy='0' rx='{0.11 * k:.1f}' ry='{0.21 * k:.1f}' fill='{CLOTH}' stroke='#1B2027'/>"
      f"<circle cx='{0.10 * k:.1f}' cy='0' r='{0.115 * k:.1f}' fill='#3C4636' stroke='#1B2027'/>"
      f"<line x1='{0.12 * k:.1f}' y1='0' x2='{0.95 * k:.1f}' y2='0' stroke='{DARK}' stroke-width='5' stroke-linecap='round'/></g>")
# the aim line + target
tgt = pt(135, 1.7)
s.line(q[0], q[1], tgt[0], tgt[1], C.RED, 1.4, "4 3"); s.glyph(FA.CROSSHAIRS, tgt[0] - 8, tgt[1] - 8, C.RED, 16)
s.callout(pt(180, 1.5)[0], pt(180, 1.5)[1] + 6, 36, 770, ["target inside the arc ⇒ he fires (PF-3: his own weapon brain decides)"])
q2 = pt(20, 1.6); s.text(q2[0] - 20, q2[1] + 4, "no fire ahead", C.RED, 11.5)
s.line(Q(0.9, 0)[0] + 12, Q(0.9, 0)[1], Q(0.9, 0)[0] + 62, Q(0.9, 0)[1], C.RED, 1.2, "3 3")
s.text(Q(1.0, 0.0)[0] + 20, Q(1.0, 0.0)[1] - 14, "forward = across the rider", C.NOTE, 11)
s.text(pt(290, 1.0)[0] + 10, pt(290, 1.0)[1] + 8, "arc edge ≈ 70° / 290°", C.AMBER, 11.5)
s.text(pt(180, 1.9)[0] - 4, pt(180, 1.9)[1] - 6, "rear: full", C.AMBER, 11.5)

# ── panel D: 2-D map + picker ──────────────────────────────────────────────────────────────────────────────────────────────────────────────
panel(640, 410, 580, 380, "2-D map and the Add Entity picker (only the glyph is new)")
# the 2-D map patch with the selected bike, its pillion wedge and a hostile
mx, my, mw, mh = 656, 440, 300, 330
s.rect(mx, my, mw, mh, "#20261B", stroke=C.BORDER)
for gx in range(0, mw, 30): s.line(mx + gx, my, mx + gx, my + mh, "#2A3123", 1)
for gy in range(0, mh, 30): s.line(mx, my + gy, mx + mw, my + gy, "#2A3123", 1)
bx, by = mx + 150, my + 190                                    # the bike, heading up the page
R2 = 110
def p2(a, r): aa = math.radians(a); return (bx + math.sin(aa) * r, by - math.cos(aa) * r)    # 0° = forward = up, ccw positive = left
pts = [(bx, by)] + [p2(-a, R2) for a in range(70, 291, 5)]
s.raw("<polygon points='" + " ".join(f"{px:.1f},{py:.1f}" for px, py in pts) + f"' fill='{C.AMBER}' fill-opacity='0.18' stroke='{C.AMBER}' stroke-width='1.2' stroke-dasharray='4 3'/>")
# the 2-D body outline: the ground-vehicle profile, slim — plus the 2525 symbol frame
s.raw(f"<rect x='{bx - 5}' y='{by - 17}' width='10' height='34' rx='5' fill='#3E6FB0' stroke='#9BC0F0' stroke-width='1.6'/>")
s.raw(f"<polygon points='{bx},{by - 24} {bx - 5},{by - 15} {bx + 5},{by - 15}' fill='#9BC0F0'/>")
s.text(bx + 16, by - 4, "Bike 1 (2 up)", "#E8E8EE", 11.5)
hxp, hyp = mx + 60, my + 300
s.raw(f"<rect x='{hxp - 9}' y='{hyp - 9}' width='18' height='18' rx='2' fill='#B04040' stroke='#F0A0A0' stroke-width='1.4'/>")
s.line(bx, by, hxp, hyp, C.RED, 1, "4 3")
s.text(hxp + 14, hyp + 4, "inside the arc", C.RED, 11.5)
s.callout(bx + 6, by - 60, mx + 40, my + 22, ["wedge shown when the bike is SELECTED"])
# the picker row (real layout: 16 px glyph + name + DIS text)
px, py = 976, 440
s.rect(px, py, 230, 148, "#141418", stroke=C.SEP, rx=3)
s.text(px + 10, py + 17, "Platform/Land/Motorcycle", C.NOTE, 11.5)
rows = [("Motorcycle (2 up)", "1.1.0.80.1.0.0", True), ("Pickup (Toyota)", "1.1.0.6.0.0.0", False), ("Civilian car", "1.1.0.81.0.0.0", False)]
for i, (n, d, sel) in enumerate(rows):
    ry = py + 28 + i * 26
    if sel: s.rect(px + 4, ry - 2, 222, 24, "#2C3A52", rx=2)
    s.raw(f"<g transform='translate({px + 10},{ry + 1}) scale(0.125)'>"
          + ("<circle cx='30' cy='92' r='26' fill='none' stroke='#9AA3AE' stroke-width='9'/><circle cx='98' cy='92' r='26' fill='none' stroke='#9AA3AE' stroke-width='9'/>"
             "<path d='M30,92 L52,52 L74,52 L98,92 M52,52 L44,34 L62,34 M74,52 L84,34 L100,34' fill='none' stroke='#C9D1DA' stroke-width='8' stroke-linejoin='round' stroke-linecap='round'/>"
             if i == 0 else
             "<rect x='6' y='56' width='116' height='30' rx='8' fill='#9AA3AE'/><circle cx='34' cy='90' r='14' fill='#55606C'/><circle cx='96' cy='90' r='14' fill='#55606C'/>") + "</g>")
    s.text(px + 36, ry + 14, n, C.TXT if sel else C.SUB, 12); s.text(px + 220, ry + 14, d, C.DIS, 10.5, "end")
s.note(px, py + 168, ["new glyph: a motorcycle profile (_motorcycle),", "style of _wheeled; thumbnail = the 3-D kit."], C.SUB, 11.5)
s.note(px, py + 214, ["out of scope (unchanged): possession HUD,", "picker layout, the 2-D symbol set."], C.NOTE, 11)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "designs", "vehicle-tactics", "motorcycle-visuals.svg")
s.save(out)
print(out)
if "--png" in sys.argv:
    from mockup_kit import render
    render(out, sys.argv[sys.argv.index("--png") + 1], W, H)
