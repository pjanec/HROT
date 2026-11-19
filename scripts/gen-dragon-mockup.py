#!/usr/bin/env python3
"""Mockup for docs/designs/fun/DESIGN_Dragon.md §8 (DR-11, ui D-4) — the DRAGON's look on the 3-D map: a box-part kit (body, neck, head, tail, four legs,
two wings) posed by the simulation — wings beating / gliding / folded, legs planted / tucked, the neck on turret 0 with the FL-1 flame jet from the mouth,
the rider and the pillion astride — and its 2-D symbol and sounds. Built on scripts/mockup_kit.py (R-4143).

    python3 scripts/gen-dragon-mockup.py      # -> docs/UX/dragon-look.svg

Every panel draws the SAME kit (metres from the design's §8 part table, 1 m = 11 px) in one of the states a frame rail renders.
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA

W, H = 1500, 1000
s = Svg(W, H, "The dragon on the 3-D map — a box-part kit posed by the simulation  (CE-4634 D-4, DESIGN_Dragon DR-11)",
        "Annotations in grey are not drawn by the app. One kit (20 m nose to tail, 26 m wingspan, ~6 m standing) in the states the frame rails render.")
K = 11.0                      # px per metre
PW, PH = 460, 300             # a panel
SKY, GROUND, BODY, BELLY, WING, WINGEDGE, DARK, HORN = "#26313F", "#3A3A2E", "#7A1F1F", "#A24A42", "#C07A6E", "#8A5446", "#3E1414", "#D8D0B8"   # as built (CE-4634): the TKB's own #7A1F1F body; the wing membrane the body LIGHTENED (ColourRole.Light)
MAN, RIFLE, FLAME, FLAMEHOT, SADDLE = "#7A6A50", "#2B2B2B", "#F29B38", "#FFE28A", "#5A3A22"


def panel(x, y, title, sub):
    s.rect(x, y, PW, PH, SKY, stroke=C.BORDER, rx=3)
    s.rect(x, y + PH - 46, PW, 46, GROUND)
    s.text(x + 10, y + 20, title, C.TXT, 13, weight="bold")
    s.text(x + 10, y + 36, sub, C.SUB, 11)


def poly(pts, fill, stroke=None, op=1.0):
    p = " ".join(f"{a:.1f},{b:.1f}" for a, b in pts)
    st = f" stroke='{stroke}' stroke-width='1'" if stroke else ""
    s.raw(f"<polygon points='{p}' fill='{fill}' fill-opacity='{op}'{st}/>")


def box(ox, oy, cx, cz, l, h, pitch_deg, fill, stroke=DARK, op=1.0):
    """A side-view box: centre (cx, cz) m, length l, height h, pitched (nose up +) — drawn about the panel origin (ox, oy = ground under x 0)."""
    a = math.radians(pitch_deg); ca, sa = math.cos(a), math.sin(a)
    pts = []
    for dx, dz in ((-l / 2, -h / 2), (l / 2, -h / 2), (l / 2, h / 2), (-l / 2, h / 2)):
        x = cx + dx * ca - dz * sa; z = cz + dx * sa + dz * ca
        pts.append((ox + x * K, oy - z * K))
    poly(pts, fill, stroke, op)


def side(ox, oy, legs="planted", neck_el=0, wings="folded", lift=0.0):
    """The kit from the left: x forward (right on the page). lift raises the whole body (in flight)."""
    z0 = lift
    # tail: two tapering boxes
    box(ox, oy, -6.5, 3.4 + z0, 5.5, 1.1, -6, BODY); box(ox, oy, -10.8, 2.9 + z0, 4.2, 0.6, -8, BODY)
    poly([(ox - 13.2 * K, oy - (2.5 + z0) * K), (ox - 12.4 * K, oy - (3.3 + z0) * K), (ox - 12.0 * K, oy - (2.4 + z0) * K)], DARK)   # the tail's spade
    # legs (hind, then front), behind the body
    for lx in (-2.4, 2.6):
        if legs == "planted":
            box(ox, oy, lx, 1.15 + z0, 0.9, 2.3, 0, BODY); box(ox, oy, lx + 0.3, 0.15 + z0, 1.2, 0.3, 0, DARK)
        else:   # tucked back under the belly
            box(ox, oy, lx - 0.9, 2.1 + z0, 2.2, 0.8, 18, BODY)
    # body
    box(ox, oy, 0, 3.4 + z0, 8.4, 2.6, 0, BODY); box(ox, oy, 0, 2.35 + z0, 7.6, 0.5, 0, BELLY)
    # wing (the near one), seen from the side
    if wings == "folded":
        poly([(ox + 1.8 * K, oy - (4.7 + z0) * K), (ox - 5.5 * K, oy - (4.2 + z0) * K), (ox - 4.0 * K, oy - (2.8 + z0) * K), (ox + 1.2 * K, oy - (3.6 + z0) * K)], WING, WINGEDGE)
    elif wings == "up":
        poly([(ox + 1.8 * K, oy - (4.7 + z0) * K), (ox + 0.5 * K, oy - (11.5 + z0) * K), (ox - 4.5 * K, oy - (9.5 + z0) * K), (ox - 2.5 * K, oy - (4.6 + z0) * K)], WING, WINGEDGE, 0.9)
    # neck (as built, CE-4634: LEVEL — the TKB puts the neck pivot and the head trunnion at one height) and head
    a = math.radians(neck_el)
    nx, nz = 3.6 + 2.2 * math.cos(a), 4.1 + z0 + 2.2 * math.sin(a)
    box(ox, oy, nx, nz, 4.6, 1.2, neck_el, BODY)
    hx, hz = 3.6 + 4.6 * math.cos(a) + 0.9 * math.cos(math.radians(neck_el)), 4.1 + z0 + 4.6 * math.sin(a) + 0.9 * math.sin(math.radians(neck_el))
    box(ox, oy, hx, hz, 2.4, 1.1, neck_el, BODY); box(ox, oy, hx + 0.4, hz - 0.45, 1.6, 0.3, neck_el, DARK)   # head, jaw
    box(ox, oy, hx - 0.9, hz + 0.75, 1.0, 0.18, neck_el + 35, HORN)                                            # a horn
    return hx + 1.2 * math.cos(math.radians(neck_el)), hz + 1.2 * math.sin(math.radians(neck_el)) - 0.2   # the mouth


def rider(ox, oy, x, z, aiming=False, pillion=False):
    """A man astride (the horse's Mounted pose): hips at (x, z) m, legs down the flanks, torso up."""
    box(ox, oy, x, z - 0.55, 0.45, 1.1, -15, MAN)                    # the near leg, down the flank
    box(ox, oy, x + 0.05, z + 0.6, 0.55, 1.15, -8 if not pillion else 0, MAN)   # torso
    s.raw(f"<circle cx='{ox + (x + 0.1) * K:.1f}' cy='{oy - (z + 1.45) * K:.1f}' r='{0.32 * K:.1f}' fill='{MAN}' stroke='{DARK}'/>")
    if aiming:
        box(ox, oy, x + 0.9, z + 0.95, 1.6, 0.14, 0, RIFLE)          # the rifle shouldered, across his arc
    else:
        box(ox, oy, x + 0.55, z + 0.55, 0.9, 0.18, -25, MAN)         # arms to the reins / the rider ahead


def front(ox, oy, wing_deg, op=1.0, z=8.0):
    """The kit from the front: the body, and both wings at wing_deg (+ = tips up) about roots 1.3 m out."""
    box(ox, oy, 0, z, 2.8, 2.6, 0, BODY, op=op)
    for sgn in (1, -1):
        a = math.radians(wing_deg)
        rx, rz = sgn * 1.3, z + 0.9
        tx, tz = rx + sgn * 11.7 * math.cos(a), rz + 11.7 * math.sin(a)
        mx, mz = rx + sgn * 6.0 * math.cos(a), rz + 6.0 * math.sin(a) - 1.4
        poly([(ox + rx * K, oy - rz * K), (ox + tx * K, oy - tz * K), (ox + mx * K, oy - mz * K), (ox + rx * K, oy - (rz - 1.2) * K)], WING, WINGEDGE, op)
    box(ox, oy, 0, z + 1.9, 1.1, 1.3, 0, BODY, op=op)   # the neck toward us


# ── Row A: the three flight / ground states ─────────────────────────────────────────────────────────────────────────────────────────
x0, y0 = 20, 70
panel(x0, y0, "A1 · FLYING — the wings BEAT", "≈ 1 Hz while climbing or slow (< 10 m/s); legs tucked")
ox, oy = x0 + PW / 2, y0 + PH - 46
for deg, op in ((38, 0.30), (5, 0.55), (-28, 1.0)):
    front(ox, oy, deg, op)
s.callout(ox + 11.5 * K, oy - 15.5 * K, x0 + PW - 150, y0 + 70, ["one beat: up → level → down,", "a slow heavy stroke"])
s.note(x0 + 10, y0 + PH - 22, ["front view · three phases of one beat overlaid (the app draws one per frame)"], C.NOTE, 10.5)

x1 = x0 + PW + 20
panel(x1, y0, "A2 · GLIDING — wings out, still", "≥ 10 m/s and not climbing: no beat, a small dihedral")
front(x1 + PW / 2, y0 + PH - 46, 6)
s.note(x1 + 10, y0 + PH - 22, ["front view"], C.NOTE, 10.5)

x2 = x1 + PW + 20
panel(x2, y0, "A3 · LANDED — wings folded, legs planted", "the flight state's Airborne = 0: on its four legs")
side(x2 + PW / 2 + 20, y0 + PH - 46, legs="planted", wings="folded", neck_el=0)
s.callout(x2 + PW / 2 + 20 - 2 * K, y0 + PH - 46 - 4.4 * K, x2 + 20, y0 + 70, ["the wings fold back along", "the flanks (tips down)"])

# ── Row B: breathing, the riders, dead ─────────────────────────────────────────────────────────────────────────────────────────────
y1 = y0 + PH + 30
panel(x0, y1, "B1 · BREATHING — the neck on turret 0", "azimuth ±70°, elevation −60…+15° (the AH-64 chin gun's pose)")
# top view: body, wings folded, neck turned 40° to the left of the nose, the FL-1 jet from the mouth
ox, oy = x0 + 150, y1 + 190
def top_box(cx, cy, l, w, yaw, fill, op=1.0):
    a = math.radians(yaw); ca, sa = math.cos(a), math.sin(a)
    pts = [(ox + (cx + dx * ca - dy * sa) * K * 0.8, oy - (cy + dx * sa + dy * ca) * K * 0.8) for dx, dy in ((-l/2, -w/2), (l/2, -w/2), (l/2, w/2), (-l/2, w/2))]
    poly(pts, fill, DARK, op)
top_box(-8, 0, 9.5, 1.0, 0, BODY); top_box(0, 0, 8.4, 3.0, 0, BODY)
top_box(-1.5, 2.2, 7.0, 1.2, 8, WING); top_box(-1.5, -2.2, 7.0, 1.2, -8, WING)
yaw = 40; a = math.radians(yaw)
top_box(4.2 + 2.3 * math.cos(a), 2.3 * math.sin(a), 4.6, 1.2, yaw, BODY)
mx, my = 4.2 + 4.6 * math.cos(a) + 1.2 * math.cos(a), 4.6 * math.sin(a) + 1.2 * math.sin(a)
top_box(mx - 0.4 * math.cos(a), my - 0.4 * math.sin(a), 2.4, 1.1, yaw, BODY)
L, half = 13, math.radians(9)
tip1 = (mx + L * math.cos(a + half), my + L * math.sin(a + half)); tip2 = (mx + L * math.cos(a - half), my + L * math.sin(a - half))
poly([(ox + (mx + 0.6 * math.cos(a)) * K * 0.8, oy - (my + 0.6 * math.sin(a)) * K * 0.8), (ox + tip1[0] * K * 0.8, oy - tip1[1] * K * 0.8), (ox + tip2[0] * K * 0.8, oy - tip2[1] * K * 0.8)], FLAME, None, 0.85)
poly([(ox + (mx + 0.6 * math.cos(a)) * K * 0.8, oy - (my + 0.6 * math.sin(a)) * K * 0.8), (ox + (mx + 7 * math.cos(a + 0.06)) * K * 0.8, oy - (my + 7 * math.sin(a + 0.06)) * K * 0.8), (ox + (mx + 7 * math.cos(a - 0.06)) * K * 0.8, oy - (my + 7 * math.sin(a - 0.06)) * K * 0.8)], FLAMEHOT, None, 0.9)
s.callout(ox + tip2[0] * K * 0.8, oy - tip2[1] * K * 0.8, x0 + 290, y1 + 210, ["the FL-1 flame jet, unchanged —", "from the MOUTH (backend's DR-5", "muzzle), along the neck's aim"])
s.note(x0 + 10, y1 + PH - 22, ["top view, nose to the right"], C.NOTE, 10.5)

panel(x1, y1, "B2 · RIDER + PILLION — astride (2× zoom)", "seat 0 the rider (Driver), seat 1 the pillion: a firing seat, 100–260°")
K = 22.0
s.raw(f"<clipPath id='b2'><rect x='{x1}' y='{y1 + 44}' width='{PW}' height='{PH - 44}'/></clipPath><g clip-path='url(#b2)'>")
ox, oy = x1 + PW / 2 - 60, y1 + PH - 46 + 2.2 * K
s.rect(x1, y1 + PH - 46, PW, 46, GROUND)
side(ox, oy, legs="planted", wings="folded", neck_el=0)
box(ox, oy, 2.6, 4.75, 2.6, 0.3, 0, SADDLE)                       # the saddle at the neck's base
rider(ox, oy, 3.0, 5.0); rider(ox, oy, 1.6, 5.0, aiming=True, pillion=True)
s.callout(ox + 3.1 * K, oy - 6.6 * K, x1 + 20, y1 + 66, ["the rider: hands to the neck (the", "horse's Mounted pose, any mount)"])
s.callout(ox + 2.4 * K, oy - 6.0 * K, x1 + PW - 190, y1 + 66, ["the pillion: turns and aims his", "own rifle in his arc"])
s.raw("</g>")
K = 11.0
s.note(x1 + 10, y1 + PH - 22, ["side view; both drawn while the dragon is possessed (no cab hides them)"], C.NOTE, 10.5)

panel(x2, y1, "B3 · DEAD — fallen, wings spread", "a dead body falls (the rotorcraft model); it lies wings out — NO fire (fireproof)")
ox, oy = x2 + PW / 2, y1 + PH - 46
box(ox, oy, 0, 1.3, 8.4, 2.4, 0, BODY); box(ox, oy, -6.5, 1.0, 5.5, 1.0, 0, BODY); box(ox, oy, -10.8, 0.7, 4.2, 0.6, 0, BODY)
box(ox, oy, 6.0, 0.9, 4.6, 1.1, -8, BODY); box(ox, oy, 9.0, 0.6, 2.4, 1.0, -5, BODY)
poly([(ox - 3 * K, oy - 1.6 * K), (ox + 2 * K, oy - 2.6 * K), (ox + 1 * K, oy - 0.1 * K), (ox - 5 * K, oy - 0.1 * K)], WING, WINGEDGE, 0.85)
s.note(x2 + 10, y1 + PH - 22, ["side view; legs folded under, the head on the ground"], C.NOTE, 10.5)

# ── Row C: the 2-D symbol, the parts, the sounds ────────────────────────────────────────────────────────────────────────────────────
y2 = y1 + PH + 30
s.text(20, y2, "C · THE 2-D MAP, THE PARTS, THE SOUNDS", C.SUB, 13, weight="bold")
s.rect(20, y2 + 12, 300, 150, C.POPUP, stroke=C.BORDER, rx=3)
s.text(34, y2 + 34, "2-D: the fun pack's dragon symbol", C.TXT, 12)
s.rect(60, y2 + 52, 70, 60, "#1E2A1A", stroke=C.SEP, rx=4); s.glyph(0xf6d5, 74, y2 + 96, C.GREEN, 34)   # FontAwesome 'dragon'
s.rect(170, y2 + 52, 70, 60, "#1E2A1A", stroke=C.SEP, rx=4); s.glyph(FA.HELICOPTER, 186, y2 + 96, C.GREY, 30, op=0.5)
s.note(150, y2 + 132, ["⛔ not the helicopter's (grey)"], C.NOTE, 10.5)
s.note(34, y2 + 150, ["the dragon glyph in the unit's colour, in its card cell"], C.NOTE, 10.5)

rows = [("part", "box (m)", "moves by", "from"),
        ("body · belly", "8.4 × 3.0 × 2.6", "—", "the TKB box"),
        ("neck + head", "4.6 · 2.4", "turret 0 azimuth / elevation", "TurretParts.PoseOf"),
        ("tail (2 boxes)", "5.5 · 4.2", "a slow sway in flight", "speed"),
        ("wings ×2", "11.7 span each", "beat 1 Hz · glide · fold", "speed, climb, Airborne"),
        ("legs ×4", "0.9 × 2.3", "planted · tucked", "Airborne"),
        ("riders", "seats 0, 1", "astride; the pillion aims", "CarrierSeats")]
tx, ty = 345, y2 + 30
for i, r in enumerate(rows):
    for j, (cell, w) in enumerate(zip(r, (110, 120, 200, 160))):
        s.text(tx + sum((110, 120, 200, 160)[:j]), ty + i * 20, cell, C.TXT if i else C.SUB, 11.5, weight="bold" if i == 0 else "normal")
snd = [("sound", "when (sim state the look already reads)"),
       ("wingbeat whoosh", "each down-stroke while beating; wind only gliding; silent landed"),
       ("ROAR", "the breath starts (its burst record) · takes off (Airborne 0 → 1)"),
       ("", "· hit (a Health drop) · now and then in flight (seeded, sim time)"),
       ("dying roar", "a long roar falling away when Health reaches 0")]
sx, sy = 960, y2 + 30
for i, (a, b) in enumerate(snd):
    s.text(sx, sy + i * 20, a, C.SUB if i == 0 else C.ORANGE, 11.5, weight="bold" if i == 0 else "normal")
    s.text(sx + 130, sy + i * 20, b, C.SUB if i == 0 else C.TXT, 11.5)
s.note(20, y2 + 190, ["Scope: the dragon's own look and sounds only. The helicopter's look, the flame look and the horse rider are UNCHANGED, except that the seated",
                      "rider's mounted pose is now given to ANY mount (a horse, a dragon) — the motorcycle keeps its riding pose. Drawn from the measured kits: box parts,",
                      "the bird's wing roles (WingLeft / WingRight) with the dragon's own beat, the quadruped's legs, the turret / gun roles for the neck and head."], C.NOTE, 11)
s.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "dragon-look.svg"))
