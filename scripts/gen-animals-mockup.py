#!/usr/bin/env python3
"""CE-4623 AU-1 (R-4143, AR-20) — the ANIMALS mockup: the nine kits (side elevations), the gait strip, the jump tuck, the flock, the dead animal, a rider in the saddle,
the 2-D icons, the animal-area tool and its form.
Output: docs/designs/animals/animals-visuals.svg (a PNG for the chat via mockup_kit.render, never committed).

SCOPE (the caption says it too): this specifies the animals' bodies, gaits, the flock's drawing, the dead pose, the rider's pose, the icons and the animal-area tool / form. It specifies
NOTHING else — the HUD, the picker row layout, the area tool's hint line and the Details frame are drawn for context from the real UI and are unchanged.

THE NUMBERS ARE THE KIT'S: SPECIES below is the same table as Hrot.Presentation/Map3D/AnimalKits.cs (metres; x forward, z up) and `quad_boxes` is the same builder algorithm,
so the picture is drawn from what the code draws (a rail checks the kit against the C# table; the fidelity side-by-side is the real frame next to this).
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

W, H = 1500, 1260
s = Svg(W, H, "Animals - the look (CE-4623 AU-1)",
        "Grey annotations are not drawn by the app. Scope: the kits, gaits, flock, dead pose, rider, icons, area tool + form. Context (HUD, picker layout, Details frame) is unchanged.")

DARK = "#2E2E2A"; BONE = "#E2DAC4"; SKIN = "#CEA884"; RED = "#BE2824"; AMBER = "#E1A528"


def lighten(c, f=1.3, add=12):
    r, g, b = int(c[1:3], 16), int(c[3:5], 16), int(c[5:7], 16)
    return "#%02X%02X%02X" % tuple(min(255, int(v * f + add)) for v in (r, g, b))


ROLE = lambda role, body: {"Body": body, "Dark": DARK, "Light": lighten(body), "Bone": BONE, "Skin": SKIN, "Red": RED, "Amber": AMBER}[role]

# (X0, X1, BodyW, Z0, BodyH, NeckUp, NeckL, NeckW, NeckT, HeadDown, HeadL, HeadW, HeadH, SnoutL, SnoutW, SnoutCol, LegT, FrontX, HindX, LegY, HoofH, TailL, TailDeg, TailCol, TorsoCol, HeadCol, LegCol, horns, udder, mane, beard, floppy, upright, curly)
SPECIES = {
    "Cattle": dict(x0=-1.15, x1=0.55, bw=0.70, z0=0.60, bh=0.72, nup=5, nl=0.55, nw=0.40, nt=0.42, hd=40, hl=0.52, hw=0.30, hh=0.30, sl=0.12, sw=0.26, sc="Skin", lt=0.15, fx=0.35, hx=-0.85, ly=0.24, hoof=0.08, tl=0.80, td=0, tc="Dark", horns="short", udder=1, floppy=1, body="#8C5C38"),
    "Horse": dict(x0=-0.85, x1=0.55, bw=0.55, z0=0.85, bh=0.62, nup=50, nl=0.65, nw=0.22, nt=0.30, hd=50, hl=0.55, hw=0.18, hh=0.22, sl=0.10, sw=0.16, sc="Dark", lt=0.10, fx=0.38, hx=-0.65, ly=0.18, hoof=0.07, tl=0.70, td=12, tc="Dark", mane=1, upright=1, body="#82522F"),
    "Sheep": dict(x0=-0.45, x1=0.30, bw=0.42, z0=0.32, bh=0.42, nup=30, nl=0.20, nw=0.16, nt=0.18, hd=45, hl=0.22, hw=0.14, hh=0.16, sl=0.05, sw=0.11, sc="Dark", lt=0.06, fx=0.18, hx=-0.32, ly=0.14, hoof=0.05, tl=0.12, td=20, tc="Light", torso="Light", head="Dark", leg="Dark", floppy=1, body="#E4E0D4"),
    "Dog": dict(x0=-0.40, x1=0.25, bw=0.20, z0=0.28, bh=0.24, nup=40, nl=0.16, nw=0.12, nt=0.14, hd=15, hl=0.20, hw=0.11, hh=0.12, sl=0.10, sw=0.08, sc="Dark", lt=0.06, fx=0.18, hx=-0.30, ly=0.08, hoof=0.04, tl=0.35, td=140, tc="Body", floppy=1, body="#B0804C"),
    "Cat": dict(x0=-0.22, x1=0.14, bw=0.14, z0=0.14, bh=0.15, nup=45, nl=0.08, nw=0.09, nt=0.09, hd=15, hl=0.12, hw=0.10, hh=0.09, sl=0.03, sw=0.05, sc="Skin", lt=0.035, fx=0.10, hx=-0.17, ly=0.045, hoof=0.02, tl=0.34, td=160, tc="Body", upright=1, body="#C8823C"),
    "Pig": dict(x0=-0.55, x1=0.45, bw=0.50, z0=0.22, bh=0.45, nup=0, nl=0.15, nw=0.30, nt=0.30, hd=15, hl=0.35, hw=0.25, hh=0.26, sl=0.10, sw=0.14, sc="Skin", lt=0.09, fx=0.28, hx=-0.35, ly=0.17, hoof=0.05, tl=0.12, td=120, tc="Body", floppy=1, curly=1, body="#E2AAA0"),
    "Goat": dict(x0=-0.45, x1=0.32, bw=0.32, z0=0.45, bh=0.38, nup=45, nl=0.30, nw=0.14, nt=0.16, hd=35, hl=0.24, hw=0.12, hh=0.16, sl=0.06, sw=0.09, sc="Dark", lt=0.06, fx=0.22, hx=-0.30, ly=0.12, hoof=0.05, tl=0.12, td=150, tc="Body", horns="back", beard=1, leg="Dark", upright=1, body="#E2DED4"),
}


def quad_boxes(sp, lying=False):
    """The kit's side-view boxes: (cx, cz, lx, lz, pitch, colour-role, leg-index or -1, head?)."""
    g = lambda k, d=0: sp.get(k, d)
    z0 = g("lt") * 0.7 if lying else g("z0"); nup = g("nup") + (25 if lying else 0)
    out = []
    tcol = g("torso", "Body"); hcol = g("head", "Body"); lcol = g("leg", "Body")
    out.append(((g("x0") + g("x1")) / 2, z0 + g("bh") / 2, g("x1") - g("x0"), g("bh"), 0, tcol, -1, False))
    w = (g("x1") - 0.08, z0 + g("bh") * 0.85)
    up = math.radians(nup); d = (math.cos(up), math.sin(up))
    nc = (w[0] + d[0] * g("nl") / 2, w[1] + d[1] * g("nl") / 2)
    out.append((nc[0], nc[1], g("nl"), g("nt"), -nup, "Light" if hcol == "Dark" else "Body", -1, True))
    if g("mane"): out.append((nc[0], nc[1] + g("nt") / 2 + 0.03, g("nl"), 0.09, -nup, "Dark", -1, True))
    n = (w[0] + d[0] * g("nl"), w[1] + d[1] * g("nl")); hd = math.radians(g("hd")); h = (math.cos(hd), -math.sin(hd))
    hc = (n[0] + h[0] * g("hl") / 2, n[1] + h[1] * g("hl") / 2 - g("hh") * 0.15)
    out.append((hc[0], hc[1], g("hl"), g("hh"), g("hd"), hcol, -1, True))
    sc = (n[0] + h[0] * (g("hl") + g("sl") / 2 - 0.01), n[1] + h[1] * (g("hl") + g("sl") / 2 - 0.01) - g("hh") * 0.2)
    out.append((sc[0], sc[1], g("sl"), g("hh") * 0.7, g("hd"), g("sc"), -1, True))
    ears = (n[0] + h[0] * g("hl") * 0.12, n[1] + h[1] * g("hl") * 0.12 + g("hh") * 0.35)
    if g("upright"): out.append((ears[0], ears[1] + 0.05, 0.035, 0.10, 0, hcol, -1, True))
    elif g("floppy"): out.append((ears[0] - 0.02, ears[1], 0.10, 0.035, 25, "Dark" if hcol == "Dark" else "Body", -1, True))
    if g("horns") == "short": out.append((ears[0] + 0.02, ears[1] + 0.08, 0.04, 0.14, 0, "Bone", -1, True))
    if g("horns") == "back": out.append((ears[0] - 0.04, ears[1] + 0.09, 0.035, 0.20, -40, "Bone", -1, True))
    if g("beard"): out.append((n[0] + h[0] * g("hl") * 0.95, n[1] + h[1] * g("hl") * 0.95 - 0.10, 0.04, 0.12, 0, "Light", -1, True))
    hip = z0 + 0.03
    for i, (x, y) in enumerate([(g("fx"), 1), (g("fx"), -1), (g("hx"), 1), (g("hx"), -1)]):
        if lying:
            out.append((x + (0.12 if i < 2 else -0.02), g("lt") * 0.5, 0.34, g("lt"), 0, lcol, -1, False)); continue
        ln = hip - g("hoof")
        out.append((x, g("hoof") + ln / 2, g("lt"), ln, 0, lcol, i, False, (x, hip)))
        out.append((x, g("hoof") / 2, g("lt") * 1.1, g("hoof"), 0, "Dark", i, False, (x, hip)))
    root = (g("x0"), z0 + g("bh") * 0.85); th = math.radians(g("td")); td = (-math.sin(th), -math.cos(th))
    out.append((root[0] + td[0] * g("tl") / 2, root[1] + td[1] * g("tl") / 2, 0.05, g("tl"), g("td"), g("tc"), -1, False))
    if g("curly"): out.append((root[0] - 0.07, root[1] + 0.08, 0.07, 0.05, 0, g("tc"), -1, False))
    if g("udder") and not lying: out.append((g("x0") + 0.55, z0 - 0.03, 0.30, 0.12, 0, "Skin", -1, False))
    return out, w


def draw_quad(ox, oy, k, name, body=None, angles=None, lying=False, dead=False, head=0.0, tuck=0.0):
    """Draw a species side view at (ox, oy = ground), k px per metre; angles = 4 leg angles (rad, + = foot forward)."""
    sp = SPECIES[name]; body = body or sp["body"]
    boxes, wth = quad_boxes(sp, lying)
    xs = [b[0] for b in boxes]; x_shift = (min(b[0] - b[2] / 2 for b in boxes) + max(b[0] + b[2] / 2 for b in boxes)) / 2
    def P(x, z): return (ox + (x - x_shift) * k, oy - z * k)
    def poly(pts, fill):
        d = " ".join(f"{a:.1f},{b:.1f}" for a, b in pts)
        s.raw(f"<polygon points='{d}' fill='{fill}' stroke='#14141A' stroke-width='1' stroke-linejoin='round'/>")
    order = sorted(boxes, key=lambda b: 0 if (len(b) > 8 or b[6] in (1, 3)) else 1)   # far legs first
    for b in order:
        cx, cz, lx, lz, pitch, role, leg, ishead = b[:8]
        pivot = b[8] if len(b) > 8 else None
        corners = []
        for dx, dz in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            x, z = dx * lx / 2, dz * lz / 2; p = math.radians(pitch)
            xr, zr = x * math.cos(p) + z * math.sin(p), -x * math.sin(p) + z * math.cos(p)
            corners.append((cx + xr, cz + zr))
        if ishead and head:   # the neck + head pitch about the withers (+ = nose down); the head counter-tilts
            a = head
            corners = [(wth[0] + (x - wth[0]) * math.cos(a) + (z - wth[1]) * math.sin(a), wth[1] - (x - wth[0]) * math.sin(a) + (z - wth[1]) * math.cos(a)) for x, z in corners]
        if pivot is not None and leg >= 0:
            a = (angles[leg] if angles else 0.0)
            a = a + (tuck * ((1.05 if leg < 2 else -0.75) - a))
            px, pz = pivot
            corners = [(px + (x - px) * math.cos(a) - (z - pz) * math.sin(a), pz + (x - px) * math.sin(a) + (z - pz) * math.cos(a)) for x, z in corners]
        poly([P(x, z) for x, z in corners], ROLE(role, body))
    return P


def panel(x, y, w, h, title):
    s.rect(x, y, w, h, "#15151A", stroke=C.BORDER)
    s.rect(x, y, w, 20, C.TITLE); s.text(x + 8, y + 14, title)


# ── A: the nine kits ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
panel(20, 60, 1460, 340, "3-D map - the animal kits, side elevation, drawn from the kit's own metres (a 1 m scale bar under each)")
slots = [("Cattle", 100, 0, 70), ("Horse", 330, 0, 70), ("Sheep", 560, 0, 100), ("Pig", 730, 0, 100), ("Goat", 890, 0, 100), ("Dog", 1030, 0, 110), ("Cat", 1150, 0, 160)]
for name, cx, _, k in slots:
    draw_quad(cx + 70 if name in ("Cattle", "Horse") else cx + 40, 330, k, name)
    s.line(cx, 345, cx + k, 345, C.NOTE, 1.5); s.text(cx, 360, f"{name} - 1 m", C.SUB, 11.5)
# the hen and the bird
def hen(ox, oy, k):
    def R(x, z, w, h, fill, rot=0):
        a = ox + (x - w / 2) * k; b = oy - (z + h / 2) * k
        s.raw(f"<rect x='{a:.1f}' y='{b:.1f}' width='{w * k:.1f}' height='{h * k:.1f}' fill='{fill}' stroke='#14141A' stroke-width='1' transform='rotate({rot} {a + w * k / 2:.1f} {b + h * k / 2:.1f})'/>")
    R(0, 0.075, 0.025, 0.15, AMBER); R(0, 0.24, 0.30, 0.19, "#965A34"); R(0.07, 0.20, 0.14, 0.09, lighten("#965A34")); R(-0.17, 0.33, 0.05, 0.20, DARK, 25)
    R(0.13, 0.36, 0.07, 0.14, "#965A34"); R(0.16, 0.44, 0.09, 0.07, "#965A34"); R(0.215, 0.43, 0.04, 0.025, AMBER); R(0.16, 0.485, 0.07, 0.04, RED); R(0.20, 0.395, 0.015, 0.04, RED)
    R(0.0, 0.26, 0.20, 0.12, DARK)
hen(1330, 330, 130); s.line(1300, 345, 1300 + 130, 345, C.NOTE, 1.5); s.text(1300, 360, "Hen - 1 m", C.SUB, 11.5)
def bird(ox, oy, k, flying=True):
    def R(x, z, w, h, fill, rot=0):
        a = ox + (x - w / 2) * k; b = oy - (z + h / 2) * k
        s.raw(f"<rect x='{a:.1f}' y='{b:.1f}' width='{w * k:.1f}' height='{h * k:.1f}' fill='{fill}' stroke='#14141A' stroke-width='1' transform='rotate({rot} {a + w * k / 2:.1f} {b + h * k / 2:.1f})'/>")
    R(0, 0.085, 0.22, 0.08, "#6E543C"); R(0.13, 0.105, 0.07, 0.06, "#6E543C"); R(0.18, 0.10, 0.045, 0.02, AMBER); R(-0.15, 0.09, 0.11, 0.015, DARK, 8)
    if flying: R(0.02, 0.15, 0.10, 0.012, "#6E543C", -35)
    else: R(-0.01, 0.09, 0.17, 0.055, DARK)
bird(1190, 110, 220, True); s.text(1150, 135, "Bird, wings beating", C.SUB, 11.5)
bird(1390, 110, 220, False); s.text(1360, 135, "perched, folded", C.SUB, 11.5)
s.note(36, 388, ["coat: seeded per animal from a species palette (the TKB colour wins) - the same animal on every host; belly / wool = a lighter shade of the coat; horn, comb, beak = fixed bone / red / amber."], C.NOTE, 11)

# ── B: the gait strip ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
panel(20, 410, 1100, 330, "Gaits - one horse at rising speed: each LEG has its own phase of the cycle (offsets = the C# table; amplitude grows)")
OFF = {"walk": (0.25, 0.75, 0.0, 0.5), "trot": (0.0, 0.5, 0.5, 0.0), "canter": (0.66, 0.33, 0.33, 0.0), "gallop": (0.62, 0.50, 0.0, 0.12)}
AMP = {"walk": 0.40, "trot": 0.55, "canter": 0.70, "gallop": 0.95}
SPEED = {"walk": "1.6 m/s", "trot": "3.5 m/s", "canter": "6 m/s", "gallop": "10 m/s"}
for i, g in enumerate(("walk", "trot", "canter", "gallop")):
    ox = 150 + i * 270
    phase = 1.1   # one sampled instant of the cycle
    ang = [AMP[g] * math.sin(phase + 2 * math.pi * o) for o in OFF[g]]
    draw_quad(ox, 545, 56, "Horse", angles=ang, head=(0.0 if g != "gallop" else -0.25))
    s.text(ox - 70, 435 + 14, f"{g.upper()}  {SPEED[g]}", C.TXT, 12.5)
    s.text(ox - 70, 566, f"swing ±{math.degrees(AMP[g]):.0f}°", C.SUB, 11.5)
    s.text(ox - 70, 580, "FL FR HL HR " + " ".join(f"{o:.2f}" for o in OFF[g]), C.NOTE, 10.5)
# the jump
draw_quad(150, 640, 50, "Horse", tuck=1.0)
s.text(30, 650, "JUMP", C.TXT, 12.5)
s.text(30, 730, "jump: fore legs fold forward and up, hind ones trail (height above ground)", C.NOTE, 11)
# grazing
draw_quad(520, 700, 50, "Horse", head=1.7); s.text(440, 732, "grazing: neck down 97°, head counter-tilts", C.NOTE, 11)

# ── C: dead animal ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
panel(1140, 410, 340, 330, "A dead animal LIES - never a burning wreck")
ox, oy = 1215, 560
cow = SPECIES["Cattle"]
def lying_side(ox, oy, k):
    # seen from the side: the body on its side (a long low block), the four legs stiff, straight out toward the viewer's right, the head down - no flames, no smoke
    s.raw(f"<ellipse cx='{ox + 70}' cy='{oy + 66}' rx='{1.2 * k:.0f}' ry='10' fill='#000' opacity='0.3'/>")
    s.raw(f"<rect x='{ox - 0.2 * k:.0f}' y='{oy + 20}' width='{1.7 * k:.0f}' height='{0.72 * k:.0f}' rx='5' fill='#8C5C38' stroke='#14141A'/>")
    for ly in (0.08, 0.30, 0.50, 0.68):
        s.raw(f"<rect x='{ox + 1.5 * k:.0f}' y='{oy + 20 + ly * k:.0f}' width='{0.6 * k:.0f}' height='8' fill='#8C5C38' stroke='#14141A'/>")
    s.raw(f"<rect x='{ox - 0.2 * k - 0.5 * k:.0f}' y='{oy + 30}' width='{0.5 * k:.0f}' height='{0.3 * k:.0f}' fill='#8C5C38' stroke='#14141A'/>")
lying_side(ox + 20, oy - 70, 60)
s.callout(ox + 120, oy - 20, 1150, 665, ["rolled onto its side over 0.6 s of sim time,", "legs stiff, the coat as in life: no black wreck,", "no flames, no smoke (Burns() excludes animals)"])
s.callout(ox + 120, oy + 20, 1150, 705, ["a hit flock drops birds (Count falls),", "the highest indices first"])

# ── D: the flock ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
panel(20, 750, 720, 250, "The flock - ONE entity (Count, Seed, Mode) drawn as N birds; each bird a closed-form offset of (seed, index, sim time)")
import random
random.seed(7)
def cloud(x, y, w, h, mode, n):
    s.rect(x, y, w, h, "#10151A", stroke=C.BORDER)
    for i in range(n):
        if mode == "fly":
            bx, by = x + w * (0.15 + 0.7 * random.random()), y + h * (0.2 + 0.6 * random.random())
            s.raw(f"<path d='M{bx - 6:.1f},{by - 3:.1f} L{bx:.1f},{by:.1f} L{bx + 6:.1f},{by - 3:.1f}' stroke='#7A6048' stroke-width='2' fill='none'/>")
        elif mode == "land":
            bx, by = x + w * (0.2 + 0.6 * random.random()), y + h * (0.55 + 0.35 * random.random())
            s.raw(f"<ellipse cx='{bx:.1f}' cy='{by:.1f}' rx='5' ry='3' fill='#6E543C' stroke='#14141A'/>")
        elif mode == "far":
            bx, by = x + w * (0.3 + 0.4 * random.random()), y + h * (0.3 + 0.4 * random.random())
            s.rect(bx, by, 2, 2, "#6E543C")
        else:
            bx, by = x + w * (0.35 + 0.3 * random.random()), y + h * (0.2 + 0.3 * random.random())
            s.raw(f"<ellipse cx='{bx:.1f}' cy='{by:.1f}' rx='5' ry='4' fill='#6E543C' stroke='#14141A'/>")
cloud(36, 782, 170, 130, "fly", 40); s.text(40, 930, "Flying: a loose cloud,", C.SUB, 11.5); s.text(40, 944, "each bird beats its own wings", C.SUB, 11.5)
cloud(216, 782, 170, 130, "land", 14); s.text(220, 930, "Landed: spread on the", C.SUB, 11.5); s.text(220, 944, "ground, wings folded", C.SUB, 11.5)
s.raw("<rect x='396' y='782' width='170' height='130' fill='#10151A' stroke='#3A3A44'/><circle cx='481' cy='830' r='38' fill='#2C4A2A' stroke='#14141A'/><rect x='476' y='860' width='10' height='50' fill='#5C4630'/>")
random.seed(3)
for i in range(9):
    bx, by = 481 + 28 * (random.random() - 0.5) * 2, 822 + 26 * (random.random() - 0.5) * 2
    s.raw(f"<ellipse cx='{bx:.1f}' cy='{by:.1f}' rx='5' ry='4' fill='#6E543C' stroke='#14141A'/>")
s.text(400, 930, "Perched in a lone tree /", C.SUB, 11.5); s.text(400, 944, "orchard / park - never woods", C.SUB, 11.5)
cloud(576, 782, 150, 130, "far", 60); s.text(580, 930, "Far band: one dot each", C.SUB, 11.5); s.text(580, 944, "(under 3 px per bird)", C.SUB, 11.5)
s.note(36, 972, ["seek, replay jump and a late-joining node draw the SAME birds (nothing is integrated); when Count falls the highest indices go, the rest stay put."], C.NOTE, 11)

# ── E: the rider ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
panel(750, 750, 730, 250, "The rider - in the saddle: legs round the barrel, hands on the reins; he FIRES while riding (rifle shouldered)")
def rider(ox, oy, k, firing):
    P = draw_quad(ox, oy, k, "Horse", angles=[0.3, -0.2, 0.2, -0.3] if firing else None, head=-0.2 if firing else 0.0)
    hz = 0.85 + 0.62 + 0.02   # the saddle: the horse's back
    hx = 0.0
    def seg(a, b, col, w):
        (x1, y1), (x2, y2) = P(*a), P(*b)
        s.raw(f"<line x1='{x1:.1f}' y1='{y1:.1f}' x2='{x2:.1f}' y2='{y2:.1f}' stroke='{col}' stroke-width='{w * k:.1f}' stroke-linecap='round'/>")
    seg((hx, hz), (hx + 0.08, hz - 0.40), "#3B4A5C", 0.14); seg((hx + 0.08, hz - 0.40), (hx + 0.08, hz - 0.78), "#3B4A5C", 0.12)   # thigh, shin in the stirrup
    seg((hx, hz), (hx + 0.05, hz + 0.58), "#5C6B4A", 0.26)                                                                             # torso
    cx, cy = P(hx + 0.07, hz + 0.76); s.raw(f"<circle cx='{cx:.1f}' cy='{cy:.1f}' r='{0.115 * k:.1f}' fill='{SKIN}' stroke='#14141A'/>")
    s.raw(f"<path d='M{cx - 0.12 * k:.1f},{cy - 0.01 * k:.1f} a{0.12 * k:.1f},{0.12 * k:.1f} 0 0 1 {0.24 * k:.1f},0 z' fill='#3C4636' stroke='#14141A'/>")
    if firing: seg((hx + 0.10, hz + 0.50), (hx + 0.95, hz + 0.54), DARK, 0.045); seg((hx + 0.10, hz + 0.45), (hx + 0.55, hz + 0.52), "#5C6B4A", 0.08)
    else: seg((hx + 0.05, hz + 0.45), (hx + 0.45, hz + 0.20), "#5C6B4A", 0.08); seg((hx + 0.45, hz + 0.20), (hx + 0.75, hz + 0.34), "#8A7A5A", 0.02)   # arm to the reins, the rein to the bit
    return P
rider(870, 960, 62, False); s.text(790, 980, "riding: reins in hand, rifle slung", C.SUB, 11.5)
rider(1230, 960, 62, True); s.text(1130, 980, "firing from the saddle (the horse need not stop)", C.SUB, 11.5)
s.note(766, 990, ["seat 0 (rider, a FIRING seat) · seat 1 (a second rider) behind him · the saddle is the horse's back, z 1.49"], C.NOTE, 11)

# ── F: icons + area tool + form ──────────────────────────────────────────────────────────────────────────────────────────────────────────────
panel(20, 1010, 1460, 230, "2-D map icons, the picker glyphs and the animal-area tool + its form (the drawing, the form fields)")
names = ["Cattle", "Sheep", "Horse", "Dog", "Cat", "Bird", "Hen", "Pig", "Goat"]
for i, n in enumerate(names):
    x = 40 + i * 62
    s.rect(x, 1040, 54, 54, "#20261B", stroke=C.BORDER)
    k = 22 if n in ("Cattle", "Horse") else 40 if n in ("Sheep", "Pig", "Goat", "Dog") else 70
    if n in SPECIES: draw_quad(x + 27, 1082, k, n)
    elif n == "Hen": hen(x + 27, 1082, 60)
    else: bird(x + 27, 1070, 120, True)
    s.text(x + 27, 1108, n, C.SUB, 10.5, "middle")
s.note(40, 1126, ["picker glyphs (assets/icons/_cattle ...) in the 3-D look; the 2-D map draws the", "species as a top-down body outline (a cow = a long oval + head, a bird = a cross)", "- never the man's line"], C.NOTE, 11)
# the area on a 2-D map
mx, my, mw, mh = 600, 1038, 330, 190
s.rect(mx, my, mw, mh, "#20261B", stroke=C.BORDER)
pts = [(mx + 40, my + 50), (mx + 170, my + 30), (mx + 260, my + 80), (mx + 220, my + 160), (mx + 70, my + 150)]
s.raw("<polygon points='" + " ".join(f"{a},{b}" for a, b in pts) + f"' fill='{C.GREEN}' fill-opacity='0.14' stroke='{C.GREEN}' stroke-width='1.6' stroke-dasharray='6 4'/>")
for a, b in pts: s.raw(f"<circle cx='{a}' cy='{b}' r='4' fill='#fff' stroke='{C.GREEN}'/>")
random.seed(11)
for i in range(7):
    ax, ay = mx + 90 + random.random() * 130, my + 60 + random.random() * 80
    s.raw(f"<ellipse cx='{ax:.1f}' cy='{ay:.1f}' rx='7' ry='4' fill='#8C5C38' stroke='#14141A'/>")
s.text(mx + 10, my + 16, "Animal area (closed polyline), populated at run start", C.SUB, 11)
s.text(mx + 10, my + mh - 8, "the one area tool: click points, close by double-click / the first point", C.NOTE, 10.5)
# the form
fx, fy, fw = 960, 1038, 500
s.rect(fx, fy, fw, 190, C.POPUP, stroke=C.SEP, rx=3)
s.header(fx + 8, fy + 8, fw - 16, "Animal area", glyph=FA.EARTH, gc=C.GREEN)
rows = [("Species", "Cattle"), ("Count", "12"), ("Seed", "4711"), ("Enclosed", None)]
yy = fy + 40
s.text(fx + 16, yy + 14, "Species", C.SUB); s.combo(fx + 120, yy, 200, "Cattle"); yy += 30
s.text(fx + 16, yy + 14, "Count", C.SUB); s.field(fx + 120, yy, 90, "12"); s.text(fx + 220, yy + 14, "animals placed at run start (1 .. 200)", C.NOTE, 10.5); yy += 30
s.text(fx + 16, yy + 14, "Seed", C.SUB); s.field(fx + 120, yy, 90, "4711"); s.text(fx + 220, yy + 14, "same placement every run", C.NOTE, 10.5); yy += 30
s.text(fx + 16, yy + 14, "Enclosed", C.SUB); s.checkbox(fx + 120, yy, False); s.text(fx + 146, yy + 14, "never leave (default on for pigs and goats)", C.NOTE, 10.5)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "designs", "animals", "animals-visuals.svg")
s.save(out)
print(out)
if "--png" in sys.argv:
    from mockup_kit import render
    render(out, sys.argv[sys.argv.index("--png") + 1], W, H)


# ── --icons: the picker glyphs (assets/icons/src/_<name>.svg, 128 px, side profile facing right; python3 scripts/render-entity-icons.py renders the PNGs) ──────────────────────────────
if "--icons" in sys.argv:
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "icons", "src")
    def icon(name, polys, key):
        body = "\n".join(f"<polygon points='{pts}' fill='{fill}'/>" for pts, fill in polys)
        open(os.path.join(root, f"_{key}.svg"), "w", encoding="utf-8").write(
            f"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 128 128' width='128' height='128'>\n<g stroke='#1B2027' stroke-width='2.4' stroke-linejoin='round'>\n{body}\n</g>\n</svg>\n")
    for name, key in (("Cattle", "cattle"), ("Sheep", "sheep"), ("Horse", "horse"), ("Dog", "dog"), ("Cat", "cat"), ("Pig", "pig"), ("Goat", "goat")):
        sp = SPECIES[name]; boxes, _ = quad_boxes(sp)
        xs, zs = [], []
        polys = []
        for b in boxes:
            cx, cz, lx, lz, pitch = b[:5]
            pts = []
            for dx, dz in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                x, z = dx * lx / 2, dz * lz / 2; p = math.radians(pitch)
                pts.append((cx + x * math.cos(p) + z * math.sin(p), cz - x * math.sin(p) + z * math.cos(p)))
            polys.append((pts, ROLE(b[5], "#C8D0DA" if b[5] == "Body" else sp["body"]), b))
            xs += [p[0] for p in pts]; zs += [p[1] for p in pts]
        k = 104 / max(max(xs) - min(xs), max(zs) - min(zs)); ox = 64 - (min(xs) + max(xs)) / 2 * k; oy = 64 + (min(zs) + max(zs)) / 2 * k
        out = [(" ".join(f"{ox + x * k:.1f},{oy - z * k:.1f}" for x, z in pts), fill) for pts, fill, _ in polys]
        icon(name, out, key)
    # a hen and a bird, hand-built profiles
    icon("Hen", [("30,70 78,70 86,92 36,98", "#B07040"), ("66,44 80,40 84,56 70,60", "#B07040"), ("84,46 96,50 84,52", "#E1A528"), ("70,34 80,30 82,42 72,42", "#BE2824"), ("26,50 36,46 44,72 30,76", "#2E2E2A"), ("52,98 56,98 56,116 52,116", "#E1A528"), ("62,98 66,98 66,116 62,116", "#E1A528")], "poultry")
    icon("Bird", [("34,62 84,58 92,70 40,74", "#8A6A50"), ("84,56 100,54 100,68 88,70", "#8A6A50"), ("100,58 112,62 100,64", "#E1A528"), ("18,60 36,58 38,68 22,70", "#2E2E2A"), ("46,60 70,22 88,26 66,62", "#A08060")], "bird")
    print("icons ->", root)
