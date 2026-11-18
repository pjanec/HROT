#!/usr/bin/env python3
"""Mockup for docs/DESIGN_Glass_Windows.md (CE-5026): glass panes in a house's windows on the 3-D map — intact, cracked (one and two hits, drawn
from the replicated state), shattered (an open hole + the shard burst), and a man inside firing out. Built on scripts/mockup_kit.py (R-4143).

    python3 scripts/gen-glass-windows-mockup.py      # -> docs/UX/glass-windows.svg
"""
import math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C

SKY = "#2A3442"; WALL = "#C9B48A"; WALL_EDGE = "#7A6646"; GLASS = "#9FD7FF"; FRAME = "#5A4A32"; DARK = "#1A1A20"; GROUND = "#3B5131"
s = Svg(1240, 760, "Glass windows on the 3-D map — intact · cracked · shattered  (CE-5026)",
        "Annotations in grey are not drawn by the app. Every pane and crack below is drawn FROM THE REPLICATED STATE — the same on every map node, a late joiner included.")


def crack(cx, cy, r, seed):
    """A crack star: a white impact dot, 7 radial lines, two partial rings."""
    rnd = random.Random(seed)
    for k in range(7):
        a = k * 2 * math.pi / 7 + rnd.uniform(-0.25, 0.25); l = r * rnd.uniform(0.7, 1.0)
        s.line(cx, cy, cx + math.cos(a) * l, cy + math.sin(a) * l, "#FFFFFF", 1.1)
    for rr in (r * 0.35, r * 0.6):
        s.raw(f"<circle cx='{cx}' cy='{cy}' r='{rr:.1f}' fill='none' stroke='#FFFFFF' stroke-width='0.8' stroke-dasharray='5 4' stroke-opacity='0.8'/>")
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='2.6' fill='#FFFFFF'/>")


def window(x, y, w, h, state, cracks=(), seed=1):
    s.rect(x - 4, y - 4, w + 8, h + 8, FRAME)                              # the opening's reveal
    if state == "broken":
        s.rect(x, y, w, h, DARK)                                           # an open hole: the dark room behind
        for (px, py) in ((x + 3, y + 3), (x + w - 9, y + 2), (x + 2, y + h - 10)):
            s.raw(f"<polygon points='{px},{py} {px + 6},{py + 2} {px + 2},{py + 8}' fill='{GLASS}' fill-opacity='0.55'/>")   # a few shards left in the frame
        return
    s.rect(x, y, w, h, GLASS, op=0.45)
    s.raw(f"<polygon points='{x + 6},{y + h - 6} {x + 22},{y + 6} {x + 30},{y + 6} {x + 14},{y + h - 6}' fill='#FFFFFF' fill-opacity='0.18'/>")   # a sheen
    for i, (u, v) in enumerate(cracks): crack(x + u * w, y + v * h, min(w, h) * 0.32, seed + i)


# ── the facade ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
s.rect(20, 60, 800, 600, SKY)
s.rect(20, 560, 800, 100, GROUND)
s.rect(90, 140, 660, 420, WALL, stroke=WALL_EDGE, sw=2)
s.rect(90, 128, 660, 14, "#8A6A4A")                                         # the eaves
W, H = 86, 104
cols = [140, 300, 460, 620]
for row, wy in enumerate((180, 360)):
    for ci, wx in enumerate(cols):
        key = (row, ci)
        if key == (0, 1): window(wx, wy, W, H, "intact", cracks=[(0.34, 0.42)], seed=3)
        elif key == (0, 2): window(wx, wy, W, H, "intact", cracks=[(0.3, 0.3), (0.66, 0.66)], seed=7)
        elif key == (1, 2): window(wx, wy, W, H, "broken")
        else: window(wx, wy, W, H, "intact")
# the door
s.rect(394, 470, 58, 90, "#6E5236", stroke=FRAME, sw=3)

# ── the shatter, live: shards bursting outward from the lower window ──────────────────────────────────────────────────────────────────────
rnd = random.Random(11)
for k in range(26):
    a = rnd.uniform(-2.6, -0.5); d = rnd.uniform(30, 120); sx, sy = 503 + math.cos(a) * d * 1.3, 470 + math.sin(a) * -d * 0.4 + 40
    s.raw(f"<polygon points='{sx:.0f},{sy:.0f} {sx + 5:.0f},{sy + 2:.0f} {sx + 1:.0f},{sy + 6:.0f}' fill='{GLASS}' fill-opacity='0.7'/>")
s.raw("<circle cx='503' cy='412' r='5' fill='#FFE08A'/>")                  # the muzzle of the man inside, firing out
s.line(503, 412, 600, 520, "#FFE08A", 1.6, dash="6 4")

# ── callouts ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
s.callout(140 + W / 2, 180, 880, 120, ["intact pane — sight, fire and sound pass as today",
                                       "(a pane changes NO visibility, cracked or not)"])
s.callout(300 + W / 2, 180, 880, 200, ["1 hit — one crack where the round crossed (u, v kept",
                                       "in the replicated WindowDamage)"])
s.callout(460 + W / 2, 180, 880, 280, ["2 hits — two cracks; the 3rd hit SHATTERS it"])
s.callout(460 + W / 2, 360 + H, 880, 380, ["SHATTERED — an open hole (as windows are today); its",
                                           "cracks are gone with it; a few shards stay in the frame;",
                                           "a shard burst (a LOCAL effect) and a loud sound (AI hearing)"])
s.callout(503, 412, 880, 500, ["a man inside fires OUT: the pane he fires through",
                               "shatters at once (he is within 1.5 m of it)"])

# ── the close-up ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
s.window(880, 560, 340, 180, "close-up — one crack (drawn from u, v)")
s.rect(900, 592, 140, 136, GLASS, op=0.45)
crack(970, 660, 48, 21)
s.note(1056, 610, ["a white impact dot,", "radial lines, two", "broken rings — one", "decal per kept hit;", "≤ 2 per pane"], C.SUB, 11)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "glass-windows.svg")
s.save(out)
print("wrote", os.path.normpath(out))
