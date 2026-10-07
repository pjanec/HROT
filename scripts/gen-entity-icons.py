#!/usr/bin/env python3
"""CE-1017 S5 - generate the Add Entity picker's entity icons (docs/DESIGN_Add_Entity_Picker.md D3c, S5).

Placeholder art until hand-made PNGs replace them: 64x64 RGBA side-view silhouettes for the built-in TKB
IconNames, plus the fallback glyphs the catalog asks for when a type names no icon (by tool: _point/_area/
_route/_zone, by DIS kind: _person/_tank/_afv/_wheeled/_unit). Light fill + dark outline so they read on the
dark editor theme. Drawn at 4x and downsampled for anti-aliasing.

Re-run after editing:  python3 scripts/gen-entity-icons.py
Output: Hrot/Engine/Hrot.Presentation/Assets/EntityIcons/<name>.png (embedded into Hrot.Presentation).
A hand-made PNG with the same name simply replaces the generated one.
"""
import math
import os
from PIL import Image, ImageDraw

S = 256                     # draw size (4x)
OUT = 64                    # shipped size
FILL = (214, 220, 226, 255)
LINE = (30, 34, 40, 255)
ACCENT = (120, 170, 220, 255)
W = 10                      # outline width at 4x
HERE = os.path.dirname(os.path.abspath(__file__))
DEST = os.path.join(HERE, "..", "Hrot", "Engine", "Hrot.Presentation", "Assets", "EntityIcons")


def poly(d, pts, fill=FILL):
    d.polygon(pts, fill=fill, outline=LINE)
    d.line(pts + [pts[0]], fill=LINE, width=W, joint="curve")


def wheels(d, xs, y, r):
    for x in xs:
        d.ellipse([x - r, y - r, x + r, y + r], fill=LINE)
        d.ellipse([x - r * 0.45, y - r * 0.45, x + r * 0.45, y + r * 0.45], fill=FILL)


def tracks(d, x0, x1, y0, y1, n):
    d.rounded_rectangle([x0, y0, x1, y1], radius=(y1 - y0) / 2, fill=LINE)
    r = (y1 - y0) / 2 - 6
    for i in range(n):
        cx = x0 + (y1 - y0) / 2 + i * ((x1 - x0) - (y1 - y0)) / max(1, n - 1)
        d.ellipse([cx - r, (y0 + y1) / 2 - r, cx + r, (y0 + y1) / 2 + r], fill=FILL)


def tank(d, dome=False, scale=1.0, ox=0, oy=0):
    def p(x, y):
        return (ox + x * scale, oy + y * scale)
    x0, y0 = p(28, 158)
    x1, y1 = p(228, 206)
    tracks(d, x0, x1, y0, y1, 6 if scale == 1.0 else 4)
    poly(d, [p(20, 160), p(40, 128), p(220, 128), p(240, 160)])           # hull
    if dome:
        x0, y0 = p(80, 72); x1, y1 = p(176, 136)
        d.chord([x0, y0, x1, y1 + (y1 - y0)], 180, 360, fill=FILL, outline=LINE, width=W)
    else:
        poly(d, [p(74, 128), p(84, 90), p(178, 90), p(186, 128)])          # boxy turret
    d.line([p(150, 100), p(252, 96)], fill=LINE, width=int(W * 1.6 * scale) or 1)   # gun


def afv(d):
    tracks(d, 26, 230, 164, 206, 6)
    poly(d, [(16, 166), (36, 112), (196, 106), (240, 136), (240, 166)])
    poly(d, [(92, 106), (100, 80), (156, 80), (162, 106)])
    d.line([(140, 90), (222, 88)], fill=LINE, width=W)


def wheeled(d, utility=True):
    poly(d, [(18, 176), (18, 128), (70, 120), (96, 84), (188, 84), (210, 120), (240, 128), (240, 176)])
    d.polygon([(104, 96), (140, 96), (140, 120), (88, 120)], fill=ACCENT, outline=LINE)
    d.polygon([(150, 96), (184, 96), (198, 120), (150, 120)], fill=ACCENT, outline=LINE)
    wheels(d, [64, 196], 180, 28)
    if utility:
        d.line([(30, 128), (30, 104)], fill=LINE, width=W)


def person(d, rifle=True, cx=128, scale=1.0, oy=0):
    def p(x, y):
        return (cx + (x - 128) * scale, oy + y * scale)
    r = 24 * scale
    hx, hy = p(128, 52)
    d.ellipse([hx - r, hy - r, hx + r, hy + r], fill=FILL, outline=LINE, width=max(2, int(W * scale)))
    poly(d, [p(98, 86), p(158, 86), p(166, 156), p(90, 156)])               # torso
    poly(d, [p(96, 156), p(124, 156), p(120, 236), p(98, 236)])              # legs
    poly(d, [p(132, 156), p(160, 156), p(158, 236), p(136, 236)])
    if rifle:
        d.line([p(70, 150), p(206, 96)], fill=LINE, width=max(3, int(W * 1.3 * scale)))


def frame(d):
    d.rounded_rectangle([14, 50, 242, 206], radius=18, fill=(214, 220, 226, 70), outline=LINE, width=W)


def unit_tanks(d):
    frame(d)
    for i, (x, y) in enumerate([(30, 64), (118, 64), (74, 128)]):
        tank(d, scale=0.42, ox=x, oy=y)


def unit_infantry(d):
    frame(d)
    for cx in (70, 128, 186):
        person(d, rifle=True, cx=cx, scale=0.55, oy=62)


def unit_generic(d):
    frame(d)
    d.line([(14, 50), (242, 206)], fill=LINE, width=W)
    d.line([(14, 206), (242, 50)], fill=LINE, width=W)


def pin(d):
    d.ellipse([78, 30, 178, 130], fill=FILL, outline=LINE, width=W)
    d.polygon([(90, 110), (166, 110), (128, 226)], fill=FILL, outline=LINE)
    d.line([(90, 110), (128, 226), (166, 110)], fill=LINE, width=W)
    d.ellipse([110, 62, 146, 98], fill=ACCENT, outline=LINE, width=6)


def area(d):
    pts = [(30, 70), (150, 30), (230, 110), (190, 220), (60, 200)]
    d.polygon(pts, fill=(120, 170, 220, 140))
    d.line(pts + [pts[0]], fill=LINE, width=W, joint="curve")


def route(d):
    pts = [(28, 210), (90, 150), (150, 170), (220, 60)]
    d.line(pts, fill=LINE, width=W * 2, joint="curve")
    d.line(pts, fill=ACCENT, width=W, joint="curve")
    for x, y in pts[:-1]:
        d.ellipse([x - 16, y - 16, x + 16, y + 16], fill=FILL, outline=LINE, width=6)
    a = math.atan2(60 - 170, 220 - 150)
    tip = (232, 42)
    l = 50
    d.polygon([tip, (tip[0] - l * math.cos(a - 0.45), tip[1] - l * math.sin(a - 0.45)),
               (tip[0] - l * math.cos(a + 0.45), tip[1] - l * math.sin(a + 0.45))], fill=LINE)


def zone(d):
    pts = [(36, 52), (220, 40), (232, 210), (28, 220)]
    d.polygon(pts, fill=(214, 220, 226, 60))
    for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
        n = 7
        for i in range(n):
            t0, t1 = i / n, (i + 0.55) / n
            d.line([(x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0), (x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1)],
                   fill=LINE, width=W)
    d.rectangle([96, 96, 160, 160], outline=ACCENT, width=W)


def fireline(d):
    d.line([(20, 196), (236, 60)], fill=LINE, width=W * 3)
    d.line([(20, 196), (236, 60)], fill=(220, 80, 70, 255), width=W + 4)
    for t in (0.2, 0.45, 0.7):
        x, y = 20 + 216 * t, 196 - 136 * t
        d.line([(x, y), (x + 34, y + 54)], fill=(220, 80, 70, 255), width=W)


ICONS = {
    # built-in TKB IconNames (CE-1017 S0)
    "m1_abrams":      lambda d: tank(d, dome=False),
    "t72":            lambda d: tank(d, dome=True),
    "m2_bradley":     afv,
    "hmmwv":          wheeled,
    "rifleman":       person,
    "tank_platoon":   unit_tanks,
    "infantry_squad": unit_infantry,
    # fallbacks by tool
    "_point": pin, "_area": area, "_route": route, "_zone": zone, "_fireline": fireline,
    # fallbacks by DIS kind/category
    "_person":  lambda d: person(d, rifle=False),
    "_tank":    lambda d: tank(d, dome=False),
    "_afv":     afv,
    "_wheeled": lambda d: wheeled(d, utility=False),
    "_unit":    unit_generic,
}


def main():
    os.makedirs(DEST, exist_ok=True)
    for name, draw in ICONS.items():
        img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        draw(ImageDraw.Draw(img))
        img.resize((OUT, OUT), Image.LANCZOS).save(os.path.join(DEST, name + ".png"), optimize=True)
    print(f"{len(ICONS)} icons -> {os.path.normpath(DEST)}")


if __name__ == "__main__":
    main()
