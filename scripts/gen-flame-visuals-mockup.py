#!/usr/bin/env python3
"""Mockup for docs/designs/weapons/DESIGN_Flame_Weapons.md §7 (ui's flame visuals, HANDOFF_Backend_To_Ui_Flame_Visuals): a flame jet into a window,
a burning truck, a napalm field, a burning man, scorched ground — each drawn FROM THE SIMULATION'S STATE (§6), the same on every node.
Built on scripts/mockup_kit.py (R-4143).

    python3 scripts/gen-flame-visuals-mockup.py      # -> docs/UX/flame-visuals.svg
"""
import math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C

SKY = "#2A3442"; GROUND = "#3B5131"; WALL = "#C9B48A"; WALL_EDGE = "#7A6646"; DARK = "#1A1A20"; TRUCK = "#4E5A3A"; TREE = "#24402A"
SCORCH = "#15120F"; SMOKE = "#5A5652"
FIRE = ("#FFC846", "#F05A14", "#50241A")   # yellow → orange → soot: EffectLayer's fire ramp (W3, tuned on the frame rail)
s = Svg(1400, 960, "Flame visuals on the 3-D map — jet · burning spots · burning man · scorch  (CE-4635 / CE-4637)",
        "Annotations in grey are not drawn by the app. Every flame, smoke and scorch below is drawn FROM THE SIMULATION'S STATE (design §6) — no per-frame "
        "state of its own: a replay, a pause and a second node draw the same.")


def tongues(cx, cy, size, n, seed, intensity=1.0, spread=0.9):
    """One burning spot: EffectLayer's fire tongues — alpha-blended yellow → orange → soot, rising, shrinking (sized by the spot's Radius)."""
    rnd = random.Random(seed)
    for i in range(n):
        t = rnd.random()
        jx = (rnd.random() - 0.5) * size * spread * (1 - 0.6 * t); h = t * size * 1.3
        r = size * 0.22 * (1 - 0.5 * t) * (0.5 + 0.5 * intensity)
        col = FIRE[0] if t < 0.3 else FIRE[1] if t < 0.7 else FIRE[2]
        s.raw(f"<circle cx='{cx + jx:.1f}' cy='{cy - h:.1f}' r='{max(r, 1.5):.1f}' fill='{col}' fill-opacity='{0.85 * (1 - t) ** 0.6 * intensity:.2f}'/>")


def smoke(cx, cy, size, n, seed, op=0.5):
    rnd = random.Random(seed)
    for i in range(n):
        t = (i + rnd.random()) / n
        s.raw(f"<circle cx='{cx + t * size * 1.2 + (rnd.random() - 0.5) * size * 0.4:.1f}' cy='{cy - t * size * 4:.1f}' r='{size * (0.25 + 0.6 * t):.1f}' "
              f"fill='{SMOKE}' fill-opacity='{op * (1 - t):.2f}'/>")


def panel(x, y, w, h, title):
    s.rect(x, y, w, h, SKY, stroke=C.BORDER)
    s.rect(x, y + h * 0.62, w, h * 0.38, GROUND)
    s.text(x + 10, y + 20, title, C.TXT, 13, weight="bold")


# ── A: the flame jet into a window (U-1) + the room catching (U-2, a building record) ────────────────────────────────
ax, ay, aw, ah = 20, 70, 680, 420
panel(ax, ay, aw, ah, "A · a flamethrower's jet into a window — FlameJets.TryGet (U-1)")
s.rect(ax + 400, ay + 120, 250, 190, WALL, stroke=WALL_EDGE, sw=2)          # context: the house (unchanged)
s.rect(ax + 470, ay + 160, 70, 80, DARK)                                     # the open window
gx, gy = ax + 90, ay + 300                                                   # the soldier
s.raw(f"<circle cx='{gx}' cy='{gy - 52}' r='8' fill='#C8A882'/>"); s.rect(gx - 8, gy - 44, 16, 44, "#556B3A")
mx, my = gx + 22, gy - 30                                                    # the mount's muzzle
s.rect(gx + 2, my - 3, 20, 6, "#2A2A2A")
tx, ty = ax + 505, ay + 200                                                   # jet.AimPoint (the window)
rnd = random.Random(5)
for i in range(90):                                                           # tongues streaming muzzle → aim, widening to the half-angle
    t = (i + rnd.random()) / 90
    px, py = mx + (tx - mx) * t, my + (ty - my) * t
    spread = 4 + t * 34
    px += (rnd.random() - 0.5) * spread; py += (rnd.random() - 0.5) * spread - t * 10
    col = FIRE[0] if t < 0.45 else FIRE[1] if t < 0.85 else FIRE[2]
    s.raw(f"<circle cx='{px:.1f}' cy='{py:.1f}' r='{3 + t * 9:.1f}' fill='{col}' fill-opacity='{0.9 - 0.4 * t:.2f}'/>")
tongues(ax + 505, ay + 242, 34, 14, 31)                                       # the room burning: a FireState spot on the building's record
smoke(ax + 505, ay + 160, 14, 7, 32, 0.4)
s.callout(mx, my, ax + 30, ay + 45, ["the jet: from mount jet.Mount's muzzle toward jet.AimPoint (its Target's position when it has one),",
                                    "jet.RangeM long, widening to jet.HalfAngleRad, bright by jet.Intensity, flaring up from jet.BurstStartedAt;",
                                    "lit while TryGet is true (≤ 0.25 s after the last fuel shot). v1 draws the full reach (the sim shortens it)"])
s.callout(ax + 505, ay + 242, ax + 380, ay + 345, ["the room: a FireState spot on the building's", "damage record — in the record's frame (U-2)"])

# ── B: a burning truck (U-2, a vehicle's spots in ITS frame) ───────────────────────────────────────────────────
bx, by, bw, bh = 720, 70, 660, 420
panel(bx, by, bw, bh, "B · a burning truck — FireState on the vehicle, 3 spots (U-2)")
s.rect(bx + 160, by + 230, 300, 70, TRUCK, rx=6); s.rect(bx + 400, by + 195, 70, 105, TRUCK, rx=6)
for wx in (bx + 200, bx + 300, bx + 430): s.raw(f"<circle cx='{wx}' cy='{by + 305}' r='18' fill='#1E1E1E'/>")
for (sx, sy, size, seed) in ((bx + 430, by + 200, 40, 41), (bx + 260, by + 232, 58, 42), (bx + 300, by + 300, 22, 43)):
    tongues(sx, sy, size, 16, seed)
    smoke(sx, sy - size * 1.2, size * 0.35, 6, seed + 100, 0.45)
s.callout(bx + 260, by + 232, bx + 30, by + 60, ["each spot burning at now (IsBurning(now)): flames sized by Radius,", "bright by Intensity, dying down over its last Seconds;",
                                                 "a small smoke each. Spots are in the TRUCK's frame — they move with it"])
s.callout(bx + 430, by + 200, bx + 440, by + 110, ["⛔ replaces the one-flame-per-", "wreck rule (EffectSystems.cs:422)"])

# ── C: a napalm field (many spots on the cell records) + the strike's cloud (context, unchanged) + scorch ─────────────
cx0, cy0, cw, ch = 20, 510, 680, 430
panel(cx0, cy0, cw, ch, "C · a napalm field — FireState on cell records (U-2) · scorch (U-4)")
for i in range(9): s.raw(f"<circle cx='{cx0 + 60 + i * 70}' cy='{cy0 + 240}' r='34' fill='{TREE}'/>")       # context: the treeline
smoke(cx0 + 340, cy0 + 210, 60, 10, 51, 0.55)                                                                 # the strike's AreaEffectCloud (drawn today)
s.raw(f"<ellipse cx='{cx0 + 340}' cy='{cy0 + 320}' rx='260' ry='46' fill='{SCORCH}' fill-opacity='0.75'/>")  # BurnedArea marks where it went out
rnd = random.Random(52)
for i in range(16):
    a = rnd.uniform(0, 2 * math.pi); r = rnd.random() ** 0.5
    tongues(cx0 + 340 + math.cos(a) * 220 * r, cy0 + 320 + math.sin(a) * 36 * r, rnd.uniform(18, 34), 10, 60 + i, rnd.uniform(0.6, 1.0))
s.callout(cx0 + 340, cy0 + 150, cx0 + 30, cy0 + 70, ["the big smoke: the strike's AreaEffectCloud — drawn today, unchanged (context)"])
s.callout(cx0 + 160, cy0 + 330, cx0 + 30, cy0 + 400, ["burning cells: FireState spots on the ground cells' records (their frame);",
                                                      "where a cell's fire went out: its BurnedArea mark — a dark draped decal (U-4)"])

# ── D: a burning man (U-3) ────────────────────────────────────────────────────────────────────────────────
dx, dy, dw, dh = 720, 510, 320, 430
panel(dx, dy, dw, dh, "D · a burning man (U-3)")
hx, hy = dx + 160, dy + 330
s.raw(f"<circle cx='{hx}' cy='{hy - 90}' r='10' fill='#C8A882'/>"); s.rect(hx - 10, hy - 80, 20, 52, "#7A6A50"); s.rect(hx - 10, hy - 28, 8, 34, "#5A4A38"); s.rect(hx + 2, hy - 28, 8, 34, "#5A4A38")
tongues(hx, hy - 30, 46, 16, 71)
s.callout(hx, hy - 60, dx + 12, dy + 60, ["Effects.IsBurning(view, man, now)", "(EffectState.BurningUntil), through", "PersonLook: one body-sized spot", "on him — it moves with him"])

# ── E: scorched ground, close-up (U-4) ──────────────────────────────────────────────────────────────────────
ex, ey, ew, eh = 1060, 510, 320, 430
panel(ex, ey, ew, eh, "E · scorched ground (U-4)")
s.raw(f"<g transform='rotate(-18 {ex + 160} {ey + 330})'><rect x='{ex + 60}' y='{ey + 300}' width='200' height='60' rx='18' fill='{SCORCH}' fill-opacity='0.8'/>"
      f"<rect x='{ex + 80}' y='{ey + 312}' width='160' height='36' rx='14' fill='#0A0807' fill-opacity='0.6'/></g>")
s.callout(ex + 160, ey + 330, ex + 12, ey + 70, ["a BurnedArea item of GroundHeatMarks:", "a dark decal draped on the ground,", "Length × Width at its Heading — until", "the record drops the mark"])

s.note(20, 948, ["Scope: the five pictures only — the house, the truck's body, the treeline, the man's body and the strike's cloud are context (drawn today, unchanged)."], C.NOTE, 11)
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "flame-visuals.svg")
s.save(out)
print("wrote", os.path.normpath(out))
