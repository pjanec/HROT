#!/usr/bin/env python3
"""Mockup for docs/designs/sensor-view/DESIGN_Sensor_View.md — the 3-D map's two SENSOR VIEWS on one shared post-process: night vision (an image
intensifier) and thermal (a heat picture), plus the View menu, the legend strip and the hover readout. Built on scripts/mockup_kit.py (R-4143).

    python3 scripts/gen-sensor-view-mockup.py      # -> docs/UX/sensor-view.svg

The SAME scene is drawn in every panel (a road, a field, a pond, a house, a tree line, an idling tank, a parked cold tank, a car with its lights on,
a man, a burning wreck, a warm patch where a vehicle stood); only the light / the time of day changes. Brightness values are the design's §4 / §5
model evaluated by hand — the picture is a promise about RELATIVE brightness (what is brighter than what), not about exact numbers.
"""
import math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA

W, H = 1500, 1420
s = Svg(W, H, "Sensor views on the 3-D map — night vision and thermal, one shared post-process  (CE-1984 / CE-1985)",
        "Annotations in grey are not drawn by the app. Every panel is the SAME scene; only the light (night vision) or the time of day (thermal) changes.")
PW, PH = 350, 230   # one view panel


def nv(v):
    """Night vision: P43 green phosphor."""
    v = max(0.0, min(1.0, v))
    return f"#{int(20 + 150 * v ** 1.1):02X}{int(30 + 225 * v):02X}{int(18 + 120 * v ** 1.3):02X}"


def wh(v):
    """Thermal white-hot."""
    g = int(18 + 237 * max(0.0, min(1.0, v)))
    return f"#{g:02X}{g:02X}{g:02X}"


def iron(v):
    """Thermal ironbow (black → purple → orange → white)."""
    v = max(0.0, min(1.0, v))
    stops = [(0, (10, 8, 30)), (0.3, (110, 20, 140)), (0.6, (230, 90, 40)), (0.85, (250, 200, 60)), (1, (255, 255, 235))]
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        if v <= b:
            t = (v - a) / (b - a)
            c = [int(ca[i] + (cb[i] - ca[i]) * t) for i in range(3)]
            return f"#{c[0]:02X}{c[1]:02X}{c[2]:02X}"
    return "#FFFFEB"


def scene(x, y, val, pal, glow, grain, title, sub, vignette=False, seed=1):
    """val(name) -> 0..1 brightness of a scene element; glow: list of (cx, cy, r, v) blooms; grain: 0..1 noise strength."""
    clip = f"clip{seed}"
    s.raw(f"<clipPath id='{clip}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}'/></clipPath><g clip-path='url(#{clip})'>")
    s.rect(x, y, PW, PH, pal(val("sky")))
    s.rect(x, y + 90, PW, PH - 90, pal(val("field")))                                                     # the field
    s.raw(f"<polygon points='{x},{y + 170} {x + PW},{y + 140} {x + PW},{y + 158} {x},{y + 192}' fill='{pal(val('road'))}'/>")   # the road
    s.raw(f"<ellipse cx='{x + 290}' cy='{y + 205}' rx='70' ry='18' fill='{pal(val('water'))}'/>")          # the pond
    for i in range(7):                                                                                       # the tree line
        s.raw(f"<circle cx='{x + 10 + i * 26}' cy='{y + 88}' r='16' fill='{pal(val('trees'))}'/>")
    s.rect(x + 230, y + 50, 70, 45, pal(val("wall")))                                                        # the house
    s.raw(f"<polygon points='{x + 224},{y + 52} {x + 265},{y + 26} {x + 306},{y + 52}' fill='{pal(val('roof'))}'/>")
    s.rect(x + 250, y + 62, 14, 14, pal(val("glass")))
    s.raw(f"<ellipse cx='{x + 70}' cy='{y + 140}' rx='34' ry='9' fill='{pal(val('mark'))}'/>")             # the warm patch
    s.rect(x + 120, y + 112, 52, 22, pal(val("tank_idle")), rx=3)                                            # idling tank, hot rear
    s.rect(x + 160, y + 114, 12, 18, pal(val("tank_idle_rear")), rx=2)
    s.rect(x + 40, y + 105, 50, 20, pal(val("tank_cold")), rx=3)                                             # parked cold tank
    s.rect(x + 195, y + 150, 34, 14, pal(val("car")), rx=3)                                                  # the car
    s.rect(x + 210, y + 106, 5, 14, pal(val("man")))                                                         # the man
    s.raw(f"<circle cx='{x + 212}' cy='{y + 103}' r='3' fill='{pal(val('man'))}'/>")
    s.rect(x + 300, y + 115, 30, 14, pal(val("wreck")), rx=2)                                                # the burning wreck
    for (cx, cy, r, v) in glow:
        for k in range(4):
            s.raw(f"<circle cx='{x + cx}' cy='{y + cy}' r='{r * (1 + k * 0.9):.1f}' fill='{pal(v)}' fill-opacity='{0.75 / (1 + k * 1.6):.2f}'/>")
    rnd = random.Random(seed)
    for _ in range(int(900 * grain)):
        gx, gy = rnd.uniform(x, x + PW), rnd.uniform(y, y + PH)
        s.raw(f"<rect x='{gx:.1f}' y='{gy:.1f}' width='1.6' height='1.6' fill='{pal(rnd.uniform(0.5, 1.0))}' fill-opacity='{0.55 * grain + 0.2:.2f}'/>")
    if vignette:
        s.raw(f"<rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='url(#vig)'/>")
    s.raw("</g>")
    s.rect(x, y, PW, PH, "none", stroke=C.BORDER)
    s.text(x, y - 22, title, C.TXT, 13, weight="bold")
    s.text(x, y - 7, sub, C.NOTE, 11)


s.raw("<defs><radialGradient id='vig' cx='50%' cy='50%' r='62%'><stop offset='72%' stop-color='#000' stop-opacity='0'/>"
      "<stop offset='100%' stop-color='#000' stop-opacity='0.92'/></radialGradient></defs>")

# ── Row A: night vision ───────────────────────────────────────────────────────────────────────────────────────────
s.text(20, 78, "A · NIGHT VISION — the scene lit by the REAL light (sun · moon · stars), the tube's gain brings it up, grain grows as the light falls",
       C.SUB, 13, weight="bold")
base = dict(sky=0.25, field=0.45, road=0.38, water=0.15, trees=0.72, wall=0.55, roof=0.42, glass=0.2, mark=0.45, tank_idle=0.33, tank_idle_rear=0.33,
            tank_cold=0.33, car=0.4, man=0.5, wreck=0.3)
lights = [(198, 156, 3, 1.0), (198, 160, 3, 1.0), (229, 158, 2.5, 0.9)]
fire = [(315, 112, 9, 1.0)]
scene(20, 120, lambda k: base[k], nv, lights + fire, 0.12, "A1 · full moon (~0.2 lux)", "clean picture, the trees bright (leaves reflect near-infrared)",
      vignette=True, seed=11)
scene(390, 120, lambda k: base[k] * 0.85, nv, lights + fire, 0.75, "A2 · starlight, overcast (~0.001 lux)", "the same gain, far more grain (noise ∝ 1/√light)",
      vignette=True, seed=12)
scene(760, 120, lambda k: base[k] * 0.45, nv, lights + fire + [(150, 118, 26, 1.0)], 0.35, "A3 · a muzzle flash in view",
      "the bright source blooms, the gain pulls everything else down", vignette=True, seed=13)
s.callout(20 + 198, 120 + 158, 1150, 150, ["a lamp that is ON (VehicleLights, brake lights derived):", "drawn bright and blooming — it lights nothing around it"])
s.callout(20 + 315, 120 + 112, 1150, 205, ["a burning spot, a flame jet, a flash, a blast:", "the effect layer's own list — bright, blooming"])
s.callout(760 + 150, 120 + 118, 1150, 260, ["the automatic gain dims the scene while a", "bright source is on screen (A3 vs A1)"])

# ── Row B: thermal ───────────────────────────────────────────────────────────────────────────────────────────────
PW = 350
s.text(20, 418, "B · THERMAL (white-hot) — the same scene through the day: surfaces by α (absorption) · τ (inertia) · ε (emissivity), units by their heat",
       C.SUB, 13, weight="bold")
T = {  # hand-evaluated °C-ish brightness 0..1 after the auto gain, per time of day (design §5 table)
    "noon":  dict(sky=0.05, field=0.55, road=0.95, water=0.25, trees=0.45, wall=0.7, roof=0.85, glass=0.3, mark=0.62, tank_idle=0.8, tank_idle_rear=1.0,
                  tank_cold=0.75, car=0.75, man=0.68, wreck=1.0),
    "dusk":  dict(sky=0.05, field=0.42, road=0.6, water=0.42, trees=0.4, wall=0.5, roof=0.4, glass=0.25, mark=0.6, tank_idle=0.6, tank_idle_rear=1.0,
                  tank_cold=0.45, car=0.45, man=0.8, wreck=1.0),
    "night": dict(sky=0.0, field=0.18, road=0.42, water=0.5, trees=0.28, wall=0.38, roof=0.12, glass=0.08, mark=0.62, tank_idle=0.55, tank_idle_rear=1.0,
                  tank_cold=0.1, car=0.15, man=0.92, wreck=1.0),
    "cloud": dict(sky=0.3, field=0.42, road=0.5, water=0.5, trees=0.44, wall=0.48, roof=0.42, glass=0.42, mark=0.6, tank_idle=0.6, tank_idle_rear=1.0,
                  tank_cold=0.42, car=0.42, man=0.85, wreck=1.0),
}
PW = 340
for i, (key, title, sub) in enumerate((("noon", "B1 · noon, sunny", "the road hottest; a parked tank sun-warm; low contrast on the man"),
                                       ("dusk", "B2 · dusk", "the crossover: road and field close; the pond ≈ land"),
                                       ("night", "B3 · 3 am, clear", "road, pond above the field; cold tank, roof, glass below"),
                                       ("cloud", "B4 · 3 am, overcast", "flat: clouds stop the cooling; units stand out"))):
    scene(20 + i * 368, 460, lambda k, key=key: T[key][k], wh, [(315, 112, 8, 1.0)], 0.08, title, sub, seed=20 + i)
s.callout(20 + 368 * 2 + 70, 460 + 140, 640, 728, ["a ground heat mark (TD-14): where a vehicle idled —", "warm for its cooling time, never drawn in the normal view"])
s.callout(20 + 368 * 2 + 165, 460 + 122, 1030, 728, ["the idling tank: hull at the metal surface temperature +", "the engine's heat at its hot end (the AI's calculator, in °C)"])
s.callout(20 + 368 * 2 + 255, 460 + 68, 250, 728, ["glass and metal reflect the sky:", "cold under a clear night sky"])

# ── Row C: the controls ──────────────────────────────────────────────────────────────────────────────────────────
s.text(20, 770, "C · THE CONTROLS — View menu, the legend strip on the map, the hover readout", C.SUB, 13, weight="bold")
s.window(20, 790, 300, 230, "View")
y0 = 828
for i, (label, sel, key) in enumerate((("2-D / 3-D Map", False, ""), ("First Person: Over the Shoulder", False, ""), ("Director", False, ""),
                                       (None, False, ""), ("Sensor View ▸ Off", False, ""), ("Sensor View ▸ Night Vision", False, "F4"),
                                       ("Sensor View ▸ Thermal", True, "F4"))):
    if label is None:
        s.line(32, y0 + i * 26 - 8, 308, y0 + i * 26 - 8, C.SEP); continue
    if sel: s.rect(28, y0 + i * 26 - 16, 284, 24, C.BTNSEL)
    if label.startswith("Sensor"):
        s.glyph(FA.CIRCLE if sel else FA.CIRCLE, 40, y0 + i * 26, C.ACC if sel else C.DIS, 8, op=1 if sel else 0.5)
    s.text(56, y0 + i * 26, label, C.TXT, 13)
    if key: s.text(300, y0 + i * 26, key, C.NOTE, 11, anchor="end")

# the legend strip (thermal), drawn on the map's top edge — after the post-process, so it stays sharp
lx, ly, lw = 350, 795, 780
s.rect(lx, ly, lw, 92, "#101014", stroke=C.BORDER, rx=4, op=0.92)
s.glyph(FA.FIRE, lx + 16, ly + 24, C.ORANGE, 14)
s.text(lx + 34, ly + 24, "THERMAL", C.TXT, 13, weight="bold")
s.combo(lx + 110, ly + 10, 120, "White-hot", glyph=FA.SLIDERS, gc=C.GREY)
s.combo(lx + 240, ly + 10, 90, "Auto gain")
for i, (lab, on) in enumerate((("Halo", True), ("Sensor resolution", True), ("Noise", True))):
    s.checkbox(lx + 345 + i * 140, ly + 13, on); s.text(lx + 367 + i * 140, ly + 25, lab, C.TXT, 12)
for i in range(240):
    s.rect(lx + 16 + i * 3, ly + 46, 3, 14, wh(i / 239))
s.text(lx + 16, ly + 78, "−4 °C", C.SUB, 11); s.text(lx + 16 + 720, ly + 78, "38 °C", C.SUB, 11, anchor="end")
s.text(lx + 90, ly + 78, "outlined units: above the SELECTED observer's threshold (Infantry_ThermalObserver, signature 0.3)", C.PEND, 11)

# the night-vision legend
ny = 900
s.rect(lx, ny, lw, 48, "#101014", stroke=C.BORDER, rx=4, op=0.92)
s.glyph(FA.EYE, lx + 16, ny + 28, C.GREEN, 14)
s.text(lx + 34, ny + 28, "NIGHT VISION", C.TXT, 13, weight="bold")
s.text(lx + 150, ny + 28, "light 0.002 lux · moon 12 % lit, below the horizon · gain ×4 000", C.SUB, 12)
s.combo(lx + 560, ny + 12, 110, "Green (P43)")
s.checkbox(lx + 685, ny + 15, True); s.text(lx + 707, ny + 28, "Noise", C.TXT, 12)

# the hover readout
s.tooltip(1160, 800, 320, ["T-72  ·  41.3 °C  (rear 74 °C)", "engine Idle for 4 min · barrel 12 °C · no fire", "signature 0.52 ≥ observer 0.30: SEEN (outlined)"])
s.note(1160, 880, ["hover any unit in thermal: its temperature and WHY", "(the drivers the AI's sensor reads); in night", "vision: its light level and lamps"], C.NOTE, 11)

s.callout(lx + 300, ly + 92, 700, 975, ["drawn after the post-process: sharp, never grainy or green"])
s.note(20, 1048, [
    "Scope: the two sensor views, their menu items, the legend strips and the hover readout. Everything else on the map (panels, HUD, the 2-D map,",
    "the normal 3-D view) is unchanged except for row D. Lamps, flames, flashes and embers are SEEN in every view and light their surroundings (no shadows) except in thermal (R-1057).",
    "⭐ The labels and cards a layer draws in screen space stay on top, sharp; the 3-D gizmos are part of the scene and take the sensor's look.",
], C.NOTE, 11.5)

# ── Row D: light sources — seen in every view, and (cheaply) lighting their surroundings except in thermal (R-1057) ────────────
s.text(20, 1108, "D · LIGHT SOURCES at night — headlamps, a blast, a muzzle flash, a burning cigarette: SEEN in every view, LIGHTING their surroundings "
       "(no shadows) except in thermal", C.SUB, 13, weight="bold")
DPW = 460


def night_scene(x, y, pal, lit, title, sub, seed, thermal=False):
    """lit(name) -> brightness; pools: (cx, cy, rx, ry, v) light pools on the ground / walls (normal + NV only)."""
    clip = f"dclip{seed}"
    s.raw(f"<clipPath id='{clip}'><rect x='{x}' y='{y}' width='{DPW}' height='{PH}'/></clipPath><g clip-path='url(#{clip})'>")
    s.rect(x, y, DPW, PH, pal(lit("sky")))
    s.rect(x, y + 95, DPW, PH - 95, pal(lit("field")))
    s.raw(f"<polygon points='{x},{y + 175} {x + DPW},{y + 150} {x + DPW},{y + 170} {x},{y + 200}' fill='{pal(lit('road'))}'/>")
    s.rect(x + 300, y + 40, 120, 60, pal(lit("wall")))                                  # a wall
    if not thermal:
        for (cx, cy, rx, ry, v) in ((120, 182, 70, 14, lit("pool_head")), (360, 72, 55, 30, lit("pool_blast")), (215, 120, 16, 10, lit("pool_muzzle"))):
            s.raw(f"<ellipse cx='{x + cx}' cy='{y + cy}' rx='{rx}' ry='{ry}' fill='{pal(v)}' fill-opacity='0.85'/>")
    s.rect(x + 30, y + 160, 40, 16, pal(lit("car")), rx=3)                              # the car, headlamps toward +x
    s.raw(f"<circle cx='{x + 70}' cy='{y + 166}' r='3' fill='{pal(lit('lamp'))}'/><circle cx='{x + 70}' cy='{y + 172}' r='3' fill='{pal(lit('lamp'))}'/>")
    s.raw(f"<circle cx='{x + 365}' cy='{y + 98}' r='14' fill='{pal(lit('blast'))}'/>")  # the blast at the wall's foot
    s.rect(x + 200, y + 108, 5, 16, pal(lit("soldier"))); s.raw(f"<circle cx='{x + 202}' cy='{y + 105}' r='3' fill='{pal(lit('soldier'))}'/>")
    s.raw(f"<circle cx='{x + 211}' cy='{y + 116}' r='3.5' fill='{pal(lit('flash'))}'/>")   # muzzle flash
    s.rect(x + 130, y + 112, 5, 16, pal(lit("smoker"))); s.raw(f"<circle cx='{x + 132}' cy='{y + 109}' r='3' fill='{pal(lit('smoker'))}'/>")
    s.raw(f"<circle cx='{x + 136}' cy='{y + 110}' r='1.6' fill='{pal(lit('ember'))}'/>")   # the cigarette's ember at his lips
    for (cx, cy, r, v) in ((70, 169, 4, lit("lamp")), (365, 98, 14, lit("blast")), (211, 116, 4, lit("flash")), (136, 110, 2, lit("ember"))):
        for k in range(3):
            s.raw(f"<circle cx='{x + cx}' cy='{y + cy}' r='{r * (1.6 + k * 1.2):.1f}' fill='{pal(v)}' fill-opacity='{0.45 / (1 + k * 1.8):.2f}'/>")
    s.raw("</g>")
    s.rect(x, y, DPW, PH, "none", stroke=C.BORDER)
    s.text(x, y - 22, title, C.TXT, 13, weight="bold")
    s.text(x, y - 7, sub, C.NOTE, 11)


def normal_pal(v):
    v = max(0.0, min(1.0, v)); return f"#{int(18 + 200 * v):02X}{int(22 + 180 * v):02X}{int(40 + 150 * v):02X}"


def warm_normal(k):
    return dict(sky=0.08, field=0.12, road=0.14, wall=0.18, pool_head=0.55, pool_blast=0.7, pool_muzzle=0.45, car=0.2, lamp=1.0, blast=1.0,
                soldier=0.22, flash=1.0, smoker=0.24, ember=0.95)[k]


def nv_lit(k):
    return dict(sky=0.25, field=0.42, road=0.38, wall=0.5, pool_head=0.8, pool_blast=0.85, pool_muzzle=0.7, car=0.4, lamp=1.0, blast=1.0,
                soldier=0.5, flash=1.0, smoker=0.5, ember=1.0)[k]


def th_lit(k):
    return dict(sky=0.0, field=0.18, road=0.4, wall=0.35, car=0.55, lamp=0.6, blast=1.0, soldier=0.85, flash=1.0, smoker=0.85, ember=1.0)[k]


night_scene(20, 1150, normal_pal, warm_normal, "D1 · NORMAL view, night", "lamps / blast / flash light pools around them (cheap, no shadows)", 31)
night_scene(500, 1150, nv, nv_lit, "D2 · NIGHT VISION", "the same pools amplified; every source blooms; the ember is a bright point", 32)
night_scene(980, 1150, wh, th_lit, "D3 · THERMAL", "no light pools (light is not heat); blast, flash and ember saturate, the lamp is warm", 33, thermal=True)
s.callout(20 + 136, 1150 + 110, 300, 1395, ["the cigarette's ember: brighter while it is at his lips (the drag, on the", "smoker's own idle clock) — a 0.4 m light, seen in every view"])
s.callout(500 + 211, 1150 + 116, 760, 1395, ["the muzzle flash: a 6 m light for 60 ms, a", "bloom in night vision, saturated in thermal"])
s.callout(980 + 70, 1150 + 169, 1150, 1395, ["F4 cycles: normal → night vision → thermal → normal", "(the legend strip names the view and \"F4: next\")"])

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "sensor-view.svg")
s.save(out)
print("wrote", os.path.normpath(out))
