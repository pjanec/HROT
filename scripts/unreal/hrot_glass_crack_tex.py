"""hrot_glass_crack_tex.py - CE-4651 (U-20): a BULLET HOLE in window glass as an RGBA texture (the user, 2026-11-24: "Have you seen any photograph
of a bullet hole inside a glass? ... What it is like now is not too realistic").

What a photograph of a round through a pane shows, and what is drawn here:
  * the hole itself - small (~1.5 % of the texture), clear (alpha 0)
  * the crushed cone around it - a frosted white ring, irregular, densest at the hole
  * radial cracks - 6-11, jagged (a random walk around their heading), thin and bright, some BRANCH; uneven lengths, the longest reach the edge
  * concentric cracks - short ARC segments between neighbouring radials at 2-4 radii (the spider web), each slightly bowed and jittered
  * lines catch light unevenly - alpha varies along a crack

    python scripts/unreal/hrot_glass_crack_tex.py <out folder> [count 4] [size 1024]     -> T_HrotGlassCrack_<n>.png (RGBA, white, alpha = crack)
Deterministic per variant (seeded). The texture is linear-alpha; the material multiplies its alpha into opacity.
"""
import math, os, sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SS = 3   # supersampling for anti-aliased thin lines


def jagged(rng, x0, y0, ang, length, step, wobble):
    pts = [(x0, y0)]; x, y = x0, y0; a = ang; d = 0.0
    while d < length:
        a += rng.normal(0, wobble); a = ang + max(-0.35, min(0.35, a - ang))   # stays near its heading
        s = step * rng.uniform(0.6, 1.4); x += math.cos(a) * s; y += math.sin(a) * s; d += s
        pts.append((x, y))
    return pts


def variant(seed, size):
    rng = np.random.default_rng(seed)
    N = size * SS; c = N / 2
    alpha = Image.new("L", (N, N), 0); dr = ImageDraw.Draw(alpha)
    # radial cracks
    k = int(rng.integers(6, 12))
    base = rng.uniform(0, 2 * math.pi)
    angs = sorted(base + i * 2 * math.pi / k + rng.normal(0, 0.25) for i in range(k))
    lens = [N * 0.5 * rng.uniform(0.45, 1.05) for _ in angs]
    spokes = []
    for a, L in zip(angs, lens):
        r0 = N * rng.uniform(0.012, 0.03)
        pts = jagged(rng, c + math.cos(a) * r0, c + math.sin(a) * r0, a, L, N * 0.012, 0.18)
        spokes.append(pts)
        w0 = N * 0.008   # measured: 0.0032 vanished in the mips at a pane's distance
        for i in range(len(pts) - 1):   # thinner and fainter outwards, uneven brightness
            f = i / max(len(pts) - 1, 1)
            dr.line([pts[i], pts[i + 1]], fill=int(255 * (0.95 - 0.55 * f) * rng.uniform(0.7, 1.0)), width=max(1, int(w0 * (1.0 - 0.6 * f))))
        for _ in range(int(rng.integers(0, 3))):   # branches
            j = int(rng.integers(len(pts) // 4, max(len(pts) // 4 + 1, len(pts) - 2)))
            ba = a + rng.choice([-1, 1]) * rng.uniform(0.35, 0.8)
            bp = jagged(rng, pts[j][0], pts[j][1], ba, L * rng.uniform(0.15, 0.35), N * 0.01, 0.2)
            for i in range(len(bp) - 1):
                dr.line([bp[i], bp[i + 1]], fill=int(200 * rng.uniform(0.6, 1.0) * (1 - i / len(bp) * 0.6)), width=max(1, int(N * 0.0055)))
    # concentric arc cracks between neighbouring radials (the spider web)
    for ring in range(int(rng.integers(2, 5))):
        R = N * 0.5 * rng.uniform(0.07, 0.38)
        for i in range(k):
            if rng.random() < 0.5: continue   # not every gap
            a0, a1 = angs[i], angs[(i + 1) % k] + (2 * math.pi if i == k - 1 else 0)
            Ri = R * rng.uniform(0.8, 1.2); bow = rng.uniform(-0.18, 0.18)
            seg = []
            for t in np.linspace(0, 1, 10):
                a = a0 + (a1 - a0) * t; rr = Ri * (1 + bow * math.sin(math.pi * t)) + rng.normal(0, N * 0.003)
                seg.append((c + math.cos(a) * rr, c + math.sin(a) * rr))
            dr.line(seg, fill=int(210 * rng.uniform(0.6, 1.0)), width=max(1, int(N * 0.006)))
    # the crushed cone: a frosted, irregular white ring around the hole
    cone = Image.new("L", (N, N), 0); dc = ImageDraw.Draw(cone)
    Rc = N * rng.uniform(0.03, 0.045)
    poly = [(c + math.cos(t) * Rc * rng.uniform(0.75, 1.2), c + math.sin(t) * Rc * rng.uniform(0.75, 1.2)) for t in np.linspace(0, 2 * math.pi, 28, endpoint=False)]
    dc.polygon(poly, fill=200)
    cone = cone.filter(ImageFilter.GaussianBlur(N * 0.006))
    a = np.maximum(np.asarray(alpha, np.float32), np.asarray(cone, np.float32))
    # chips: speckle inside the cone
    yy, xx = np.mgrid[0:N, 0:N]; rr = np.hypot(xx - c, yy - c)
    speck = (rng.random((N, N)) < 0.02) & (rr < Rc * 1.4)
    a = np.where(speck, 255, a)
    # the hole: clear
    Rh = N * rng.uniform(0.007, 0.011)
    a = np.where(rr < Rh, 0, a)
    a = np.where((rr >= Rh) & (rr < Rh * 1.35), 255, a)   # its bright broken rim
    img = Image.fromarray(a.clip(0, 255).astype(np.uint8), "L").resize((size, size), Image.LANCZOS)
    rgba = Image.merge("RGBA", (Image.new("L", (size, size), 245), Image.new("L", (size, size), 248), Image.new("L", (size, size), 250), img))
    return rgba


if __name__ == "__main__":
    out = sys.argv[1]; n = int(sys.argv[2]) if len(sys.argv) > 2 else 4; size = int(sys.argv[3]) if len(sys.argv) > 3 else 1024
    os.makedirs(out, exist_ok=True)
    for i in range(n):
        p = os.path.join(out, f"T_HrotGlassCrack_{i}.png"); variant(1000 + i * 17, size).save(p); print(p)
