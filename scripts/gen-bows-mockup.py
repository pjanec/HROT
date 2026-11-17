#!/usr/bin/env python3
"""CE-1976 (R-4143, docs/designs/weapons/DESIGN_Bows_And_Crossbows.md §3 BW-3 / BW-10) — THE BOW LOOKS: the held bow (relaxed / drawn to the cheek), the crossbow, the quiver, the arrow and the bolt in flight,
a stuck arrow, the mounted archer, and the thrown tomahawk (the existing CE-1190 axe — context, unchanged).
Output: docs/designs/weapons/bows-visuals.svg  (a PNG for the chat via --png <file>, never committed).
SCOPE (R-4151): ONLY the bow, the crossbow, the quiver / bolt case, the arrow and bolt, the stuck arrow and the draw pose. The figure under them (legs, torso, head, arms in grey), the horse, the wall and the
ground are the EXISTING looks, drawn for context and UNCHANGED. Boxes are WORLD METRES of the 1.8 m figure: x forward, y left, z up — the numbers BlockFigureBows.cs / ProjectileLayer.cs use.
"""
import os, sys, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, render

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "designs", "weapons", "bows-visuals.svg")
WOOD, DARK, BONE, RED, METAL, LEATHER = "#8B6036", "#2B2A2E", "#E2DAC4", "#BE2824", "#6E7074", "#60422A"
GREY, GREYD, SKIN, HORSE, HORSED = "#6A6D74", "#52555C", "#B9855F", "#8A4E2A", "#5E3418"
V = lambda *a: tuple(float(x) for x in a)
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul(a, k): return (a[0] * k, a[1] * k, a[2] * k)
def norm(a):
    l = math.sqrt(sum(x * x for x in a)); return (a[0] / l, a[1] / l, a[2] / l)
def dot(a, b): return sum(x * y for x, y in zip(a, b))
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])

def knee(hip, ankle, t, s, bend):   # BlockFigure.Knee — two-bone IK
    d = sub(ankle, hip); ln = math.sqrt(dot(d, d)); dr = mul(d, 1 / ln)
    if ln >= t + s - 1e-4: return add(hip, mul(dr, t))
    a = (t * t - s * s + ln * ln) / (2 * ln); h = math.sqrt(max(0, t * t - a * a))
    perp = sub(bend, mul(dr, dot(bend, dr)))
    if dot(perp, perp) < 1e-6: perp = sub((0, 0, 1), mul(dr, dot((0, 0, 1), dr)))
    return add(add(hip, mul(dr, a)), mul(norm(perp), h))

# ── the geometry (mirrors BlockFigureBows.cs) ─────────────────────────────────────────────────────────────────────────────────────────
ARM = 0.62; BOW_HALF, MID_BACK, TIP_BACK, TIP_BACK_DRAWN, NOCKED = 0.58, 0.05, 0.14, 0.20, 0.66
def bow(g, u, drawn, nock=None):
    tip = TIP_BACK_DRAWN if drawn else TIP_BACK; mid = MID_BACK + 0.02 if drawn else MID_BACK
    W = lambda back, up: add(sub(g, mul(u, back)), (0, 0, up))
    segs = []
    for s in (1, -1):
        segs += [(W(0, 0.05 * s), W(mid, 0.30 * s), 0.030, WOOD), (W(mid, 0.30 * s), W(tip, BOW_HALF * s), 0.026, WOOD)]
    if drawn: segs += [(W(tip, BOW_HALF), nock, 0.008, BONE), (nock, W(tip, -BOW_HALF), 0.008, BONE)]
    else: segs += [(W(tip, BOW_HALF), W(tip, -BOW_HALF), 0.008, BONE)]
    segs += [(add(g, (0, 0, -0.07)), add(g, (0, 0, 0.07)), 0.045, DARK)]   # the grip
    return segs

def archer(lsh, rsh, posture):
    """-> (segments, arm segments, arrow-or-None). posture 'aimed' | 'ease'."""
    c = mul(add(lsh, rsh), 0.5)
    if posture == "aimed":
        g = add(c, (0.57, 0.12, 0.12)); n = add(c, (0.06, -0.12, 0.12))
        u = norm((g[0] - n[0], g[1] - n[1], 0))
        segs = bow(g, u, True, n)
        arrow = [(n, add(n, mul(u, NOCKED - 0.06)), 0.012, WOOD), (add(n, mul(u, NOCKED - 0.06)), add(n, mul(u, NOCKED)), 0.024, METAL),
                 (add(n, mul(u, 0.02)), add(n, mul(u, 0.13)), 0.036, RED)]
        el = knee(lsh, g, ARM / 2, ARM / 2, (0, 0.4, -0.3)); er = knee(rsh, n, ARM / 2, ARM / 2, (-0.35, -1, 0.2))
        arms = [(lsh, el, 0.11, GREY), (el, g, 0.10, GREY), (rsh, er, 0.11, GREYD), (er, n, 0.10, GREYD)]
        return segs, arms, arrow
    g = add(lsh, (0.10, 0.06, -0.55)); u = (0, 1, 0)
    el = knee(lsh, g, ARM / 2, ARM / 2, (0, 0.3, -0.1))
    arms = [(lsh, el, 0.11, GREY), (el, g, 0.10, GREY), (rsh, add(rsh, (0.0, -0.02, -0.30)), 0.11, GREYD), (add(rsh, (0.0, -0.02, -0.30)), add(rsh, (0.02, -0.02, -0.60)), 0.10, GREYD)]
    return bow(g, u, False), arms, []

QUIVER = [((-0.17, -0.11, 1.02), (0.08, 0.10, 0.30), LEATHER), ((-0.17, -0.06, 1.27), (0.08, 0.10, 0.30), LEATHER), ((-0.17, -0.01, 1.50), (0.08, 0.10, 0.26), LEATHER),
          ((-0.17, -0.04, 1.70), (0.03, 0.03, 0.10), RED), ((-0.17, 0.01, 1.72), (0.03, 0.03, 0.12), BONE), ((-0.17, 0.05, 1.69), (0.03, 0.03, 0.09), RED)]
BOLTCASE = [((-0.02, -0.215, 0.88), (0.09, 0.07, 0.20), LEATHER), ((-0.02, -0.215, 1.01), (0.03, 0.03, 0.06), BONE)]

# ── drawing ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
class View:
    def __init__(s, svg, kind, ox, oy, k): s.svg, s.kind, s.ox, s.oy, s.k = svg, kind, ox, oy, k
    def p(s, a):
        if s.kind == "side": return (s.ox + a[0] * s.k, s.oy - a[2] * s.k)
        if s.kind == "back": return (s.ox - a[1] * s.k, s.oy - a[2] * s.k)
        if s.kind == "front": return (s.ox + a[1] * s.k, s.oy - a[2] * s.k)
        return (s.ox + a[0] * s.k, s.oy - a[1] * s.k)   # top
    def seg(s, a, b, w, col, op=1):
        (x1, y1), (x2, y2) = s.p(a), s.p(b); dx, dy = x2 - x1, y2 - y1; ln = math.hypot(dx, dy) or 1e-6
        wx, wy = -dy / ln * max(1.3, w * s.k) / 2, dx / ln * max(1.3, w * s.k) / 2
        s.svg.raw(f"<polygon points='{x1+wx:.1f},{y1+wy:.1f} {x2+wx:.1f},{y2+wy:.1f} {x2-wx:.1f},{y2-wy:.1f} {x1-wx:.1f},{y1-wy:.1f}' fill='{col}' stroke='#14141A' stroke-width='0.5' opacity='{op}'/>")
    def box(s, c, e, col):
        x, y = s.p(c)
        w, h = {"side": (e[0], e[2]), "back": (e[1], e[2]), "front": (e[1], e[2]), "top": (e[0], e[1])}[s.kind]
        s.svg.rect(round(x - w * s.k / 2, 1), round(y - h * s.k / 2, 1), max(1.3, round(w * s.k, 1)), max(1.3, round(h * s.k, 1)), col, stroke="#14141A", sw=0.5)

def figure(v, lsh, rsh, head_z=1.60, hip_z=0.88, legs="stand"):
    # the EXISTING block figure, grey, for context: legs, torso, head
    for yy in (0.11, -0.11): v.seg((0.02 if yy > 0 else -0.10, yy, hip_z), (0.20 if yy > 0 else -0.16, yy, 0.03), 0.14, "#4E4A40") if legs == "stand" else None
    v.box((0, 0, hip_z + 0.30), (0.24, 0.40, 0.56), GREY)
    v.box((0, 0, head_z), (0.21, 0.20, 0.24), SKIN)

def draw_archer(svg, kind, ox, oy, k, posture, quiver=True):
    lsh, rsh = V(0, 0.26, 1.34 if posture == "aimed" else 1.43), V(0, -0.26, 1.34 if posture == "aimed" else 1.43)
    v = View(svg, kind, ox, oy, k)
    segs, arms, arrow = archer(lsh, rsh, posture)
    order = lambda items: sorted(items, key=lambda it: (it[0][1] if kind == "side" else -it[0][0]) if kind in ("side",) else it[0][0])
    # far (his right) arm and legs first, then body, near arm, bow (the bow is in his left hand: nearest to a viewer on his left)
    figure(v, lsh, rsh, head_z=1.54 if posture == "aimed" else 1.60)
    if quiver:
        for c, e, col in QUIVER: v.box(c, e, col)
    if kind == "side":
        for a, b, w, col in arms[2:]: v.seg(a, b, w, col)
        for a, b, w, col in arms[:2]: v.seg(a, b, w, col)
    else:
        for a, b, w, col in arms: v.seg(a, b, w, col)
    for a, b, w, col in segs + arrow: v.seg(a, b, w, col)
    return v

def crossbow(stock, d, side):
    segs = [(stock, add(stock, mul(d, 0.72)), 0.05, WOOD)]
    f = add(stock, mul(d, 0.60)); catch = add(stock, mul(d, 0.34))
    for s in (1, -1):
        p1 = sub(add(f, mul(side, 0.15 * s)), mul(d, 0.01)); p2 = sub(add(f, mul(side, 0.30 * s)), mul(d, 0.07))
        segs += [(f, p1, 0.03, WOOD), (p1, p2, 0.026, WOOD), (p2, catch, 0.008, BONE)]
    rail = (0, 0, 0.05)
    segs += [(add(catch, rail), add(add(stock, mul(d, 0.80)), rail), 0.016, WOOD), (add(add(stock, mul(d, 0.80)), rail), add(add(stock, mul(d, 0.85)), rail), 0.024, METAL)]
    return segs

def tile(svg, x, y, w, h, title, note):
    svg.rect(x, y, w, h, C.BG, stroke=C.BORDER); svg.text(x + 10, y + 18, title, "#E8E8EE", 13, weight="bold")
    ly = y + 34; cols = int((w - 20) / 5.9)
    for ln in note:
        line = ""
        for wd in ln.split():
            if len(line) + len(wd) + 1 > cols: svg.text(x + 10, ly, line, C.NOTE, 10.5); ly += 13; line = wd
            else: line = (line + " " + wd).strip()
        svg.text(x + 10, ly, line, C.NOTE, 10.5); ly += 13

def label(svg, x, y, s, anchor="middle"): svg.text(x, y, s, C.NOTE, 10.5, anchor)

def main(png=None):
    W, H = 1500, 960
    svg = Svg(W, H, "Bows and crossbows — the looks (CE-1976, BW-3 / BW-10)",
              "Scope (R-4151): ONLY the bow, crossbow, quiver / bolt case, arrow, bolt, stuck arrow and the draw pose. The grey figure, the horse, the wall and the ground are the existing looks, drawn for context and unchanged.")
    # 1 — the archer aiming (his left side, back)
    x0, y0 = 10, 60; tile(svg, x0, y0, 490, 365, "1 · Archer aiming — the bow drawn to the cheek", ["bow arm straight out to the grip, the string hand at the cheek, elbow raised and out;", "the arrow nocked: shaft, metal point, two crossed red vanes. From his left, and from behind."])
    draw_archer(svg, "side", x0 + 150, y0 + 340, 118, "aimed"); label(svg, x0 + 150, y0 + 352, "side (from his left)")
    draw_archer(svg, "back", x0 + 395, y0 + 340, 118, "aimed"); label(svg, x0 + 395, y0 + 352, "from behind")
    # 2 — carried
    x0 = 510; tile(svg, x0, y0, 490, 365, "2 · Archer carrying — bow in the left hand, quiver on the back", ["at ease / ready: the stave hangs upright at his side, its string toward his body (a plane facing out);", "no arrow in hand. The quiver slants from the right hip to over the left shoulder, three fletchings showing."])
    draw_archer(svg, "front", x0 + 150, y0 + 340, 118, "ease"); label(svg, x0 + 150, y0 + 352, "front")
    draw_archer(svg, "side", x0 + 395, y0 + 340, 118, "ease"); label(svg, x0 + 395, y0 + 352, "side (stave edge-on)")
    # 3 — the crossbow
    x0 = 1010; tile(svg, x0, y0, 480, 360, "3 · Crossbowman — stock, prod, spanned string, bolt", ["a wooden stock along the rifle's line; a prod of two swept limbs across its front; the string spanned back", "to a catch; a short thick bolt on the rail. A short leather bolt case at the right hip (no quiver)."])
    lsh, rsh = V(0, 0.26, 1.34), V(0, -0.26, 1.34); stock = V(0.10, -0.14, 1.40); d = norm((1, 0.0, 0.0)); side = (0, 1, 0)
    for kind, ox, oyy, kk in (("side", x0 + 130, y0 + 340, 118), ("top", x0 + 330, y0 + 250, 118)):
        v = View(svg, kind, ox, oyy, kk)
        if kind == "side":
            figure(v, lsh, rsh, 1.54); [v.box(c, e, col) for c, e, col in BOLTCASE]
            el = (0.25, 0.20, 1.22); v.seg(lsh, el, 0.11, GREY); v.seg(el, (0.50, 0.04, 1.40), 0.10, GREY); v.seg(rsh, (0.0, -0.30, 1.20), 0.11, GREYD); v.seg((0.0, -0.30, 1.20), (0.12, -0.14, 1.38), 0.10, GREYD)
        else:
            v.box((0, 0, 0), (0.24, 0.40, 0.2), GREY); v.box((0, 0, 0), (0.2, 0.2, 0.2), SKIN)
        for a, b, w, col in crossbow(stock, d, side): v.seg(a, b, w, col)
    label(svg, x0 + 130, y0 + 352, "side"); label(svg, x0 + 345, y0 + 352, "from above (prod swept back)")
    # 4 — mounted
    y1 = 435; x0 = 10; tile(svg, x0, y1, 490, 330, "4 · Warrior on his horse — bow in hand, quiver on his back", ["the same type mounts the riding horse (703) and draws the bow from the saddle (BW-8); the hips sit", "on the horse's back, the legs round the barrel. Horse and rider's legs are the existing looks."])
    ox, gz = x0 + 240, y1 + 318; k = 112; v = View(svg, "side", ox, gz, k)
    for hx in (-0.55, 0.40): v.seg((hx, 0, 0.75), (hx, 0, 0.05), 0.12, HORSED)
    v.box((-0.1, 0, 1.05), (1.4, 0.55, 0.62), HORSE); v.seg((0.5, 0, 1.2), (0.85, 0, 1.55), 0.2, HORSE); v.box((0.98, 0, 1.55), (0.3, 0.18, 0.18), HORSED)
    hip = 1.38; shift = hip - 0.88
    lsh, rsh = V(0, 0.26, 1.34 + shift), V(0, -0.26, 1.34 + shift)
    for a, b in (((0.02, 0.11, hip), (0.26, 0.14, hip - 0.34)), ((0.26, 0.14, hip - 0.34), (0.18, 0.14, hip - 0.70))): v.seg(a, b, 0.14, "#4E4A40")
    v.box((0, 0, hip + 0.30), (0.24, 0.40, 0.56), "#B9855F"); v.box((0, 0, 1.54 + shift), (0.21, 0.20, 0.24), SKIN)
    for c, e, col in QUIVER: v.box((c[0], c[1], c[2] + shift), e, col)
    segs, arms, arrow = archer(lsh, rsh, "aimed")
    for a, b, w, col in arms[2:] + arms[:2] + segs + arrow: v.seg(a, b, w, col)
    label(svg, ox, y1 + 322, "side — drawn")
    # 5 — arrow and bolt in flight
    x0 = 510; tile(svg, x0, y1, 490, 330, "5 · The arrow in flight (MunitionShape.Arrow) — and the bolt", ["a slim shaft, a metal point, THREE vanes round the tail; the nose along the velocity, so it pitches over its arc.", "A bolt is the same shape, shorter (0.40 m) and thicker (Ø 10 mm), its vanes shorter and dark. Arrow 0.75 m × Ø 8 mm."])
    def arrow_view(ox, oy, k, length, dia, col, vane_len, vane_col, ang, tag):
        n = (math.cos(ang), 0, math.sin(ang)); v = View(svg, "side", ox, oy, k)
        base = (0, 0, 0); tail = mul(n, -length / 2); nose = mul(n, length / 2)
        v.seg(tail, add(nose, mul(n, -0.07 * length)), dia, col)
        v.seg(add(nose, mul(n, -0.07 * length)), nose, dia * 2.2, METAL)
        perp = (-math.sin(ang), 0, math.cos(ang))
        for a in (0, 120, 240):   # three vanes: seen from the side two are visible (one up, one down) and one points at us
            r = math.radians(a + 90); rad = (perp[0] * math.cos(r), 0, perp[2] * math.cos(r))
            flare = max(dia * 3.2, 0.012)
            v.seg(add(tail, mul(n, vane_len * length)), add(tail, mul(rad, flare)), dia * 1.3, vane_col)
        label(svg, ox, oy + 36, tag)
    arrow_view(x0 + 160, y1 + 110, 230, 0.75, 0.008, WOOD, 0.2, "#DED6C4", 0.35, "arrow — 0.75 m")
    arrow_view(x0 + 160, y1 + 210, 230, 0.40, 0.010, "#5A4A3A", 0.14, "#3C322C", -0.25, "crossbow bolt — 0.40 m")
    # end-on: the three vanes at 120 degrees
    cx, cy = x0 + 400, y1 + 170
    svg.rect(cx - 56, cy - 56, 112, 112, "#1E1E26", stroke=C.BORDER)
    for a in (90, 210, 330):
        r = math.radians(a); svg.line(cx, cy, cx + 40 * math.cos(r), cy - 40 * math.sin(r), "#DED6C4", 5)
    svg.raw(f"<circle cx='{cx}' cy='{cy}' r='4.5' fill='{WOOD}' stroke='#14141A' stroke-width='0.6'/>"); label(svg, cx, cy + 74, "end-on: three vanes at 120°")
    label(svg, x0 + 245, y1 + 300, "flight →   the point leads; no tumble (unlike the thrown axe)", "middle")
    # 6 — stuck
    x0 = 1010; tile(svg, x0, y1, 480, 330, "6 · Arrows stuck in a wall and in the ground", ["lying + Sticks (BW-4): drawn along the round's replicated rotation at its replicated place — NOT flat;", "its point already a third of its length inside the surface (backend sets the position), the shaft sticking out."])
    gy = y1 + 290
    svg.rect(x0 + 40, gy - 190, 95, 190, "#7A7C80", stroke="#14141A"); svg.text(x0 + 87, gy - 100, "wall", "#D0D0D8", 11, "middle")   # the existing wall: a slab
    svg.rect(x0 + 10, gy, 460, 24, "#4A6A38", stroke="#14141A"); svg.text(x0 + 240, gy + 17, "ground", "#D0D0D8", 11, "middle")
    kk = 150
    v = View(svg, "side", 0, 0, kk)
    def stuck(px, py, ang_deg, tag, col=WOOD, going=1):   # px,py pixel point where the shaft ENTERS the surface; the shaft comes out toward the shooter
        L = 0.75 * kk; a = math.radians(ang_deg); inside = L / 3
        # entering at (px,py) travelling in direction (cos a, sin a) in pixel space (a>0 = downward): tail is behind it
        dx, dy = going * math.cos(a), math.sin(a)
        tx, ty = px - dx * (L - inside), py - dy * (L - inside); hx, hy = px + dx * inside, py + dy * inside
        svg.raw(f"<line x1='{tx:.1f}' y1='{ty:.1f}' x2='{hx:.1f}' y2='{hy:.1f}' stroke='{col}' stroke-width='3' />")
        for s in (1, -1): svg.raw(f"<line x1='{tx + dx*0.2*kk*0.5:.1f}' y1='{ty + dy*0.2*kk*0.5:.1f}' x2='{tx - dy*s*7:.1f}' y2='{ty + dx*s*7:.1f}' stroke='#DED6C4' stroke-width='2.4'/>")
        svg.raw(f"<line x1='{px:.1f}' y1='{py:.1f}' x2='{hx:.1f}' y2='{hy:.1f}' stroke='{METAL}' stroke-width='2' stroke-dasharray='2 2' opacity='0.8'/>")
        svg.text(tx, ty - 8, tag, C.NOTE, 10.5, "middle")
    stuck(x0 + 135, gy - 130, 12, "wall, 12° down", WOOD, -1); stuck(x0 + 135, gy - 60, -8, "wall, 8° up", "#5A4A3A", -1)
    stuck(x0 + 300, gy + 4, 30, "ground, 30°"); stuck(x0 + 390, gy + 4, 55, "ground, steep")
    svg.text(x0 + 240, y1 + 322, "dashed = the part inside the surface (a third); drawn in 3-D by the surface hiding it", C.NOTE, 10.5, "middle")
    # 7 — tomahawk (existing)
    y2 = 775; x0 = 10; tile(svg, x0, y2, 490, 150, "7 · The thrown tomahawk — the existing axe look (CE-1190)", ["his hatchet munition (TkbThrowingHatchet) is already MunitionShape.Axe: a haft with a head block and a blade, tumbling end over end", "in flight, flat on the ground when it lies (rails: CE1190_ThrownBlades_…). Nothing to build."])
    ax = x0 + 245; ay = y2 + 100; v = View(svg, "side", ax, ay, 170)
    v.seg((-0.2, 0, 0), (0.15, 0, 0), 0.03, "#7A6A50"); v.seg((0.12, 0, 0), (0.2, 0, 0), 0.06, METAL); v.seg((0.17, 0, -0.08), (0.17, 0, 0.08), 0.025, METAL)
    # 8 — rules / numbers
    tile(svg, 510, y2, 980, 150, "8 · Where each look lives, and how it is chosen", ["• HeldLook.Bow / Crossbow — WeaponCategory.Bow (14); the mount's round says which (an arrow ⇒ Bow, a bolt ⇒ Crossbow); BlockFigure.BowHeld reshapes the arms and appends the stave / string / arrow parts.",
        "• the quiver / bolt case — torso details of a man who holds a bow / crossbow; they follow every stance (crouch, prone, seated, mounted).",
        "• the draw pose — when his weapon posture is Aimed; carried (left hand, string toward him) at ease / ready and when prone. A rider's bow is drawn when he fires (the firing seat).",
        "• the arrow in flight — ProjectileLayer.SolidsOf, MunitionShape.Arrow (shaft + point + 3 vanes); the stuck arrow — the replicated rotation (StuckNose), not lifted onto the ground, not flat.",
        "• a nocked arrow is drawn 0.66 m long (the figure's draw length), not the flying arrow's 0.75 m — one deviation, named in the design's as-built."])
    svg.save(OUT)
    if png: render(OUT, png, W, H)

if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--png") + 1] if "--png" in sys.argv else None)
