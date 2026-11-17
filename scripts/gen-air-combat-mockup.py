#!/usr/bin/env python3
"""CE-4647 AC-15 (R-4143, DESIGN_Air_Combat.md §4d) — the AIR COMBAT looks, four mockups in one generator:
  air-combat-body.svg    the MiG-29 twin-fin body beside the F-16 (side, top, rear) — the F-16 drawn from ShapeKits.Jet's REAL numbers
  air-combat-stores.svg  both jets with their four missiles on rails — full, and after two shots (IR outboard / radar inboard)
  air-combat-hud.svg     the fighter HUD (gun pipper at the led point, missile in-zone / lock cue, radar warning, g) over the existing flyer HUD
  air-combat-arena.svg   the arena in 2-D and 3-D (faint ring, the floor) + its authoring gizmo handles + its Details form
Output: docs/designs/air-attack/*.svg (PNGs for the chat via --png <dir>, never committed).

SCOPE (every caption says it too, R-4151): the MiG body, the missiles on the jets' rails, the fighter HUD's NEW elements and the arena's drawing + gizmo + form.
Drawn for CONTEXT from the real UI and UNCHANGED: the compass strip, the flyer's SPD/ALT/HDG line, the health / weapon lines, the 2-D map symbols, the Details window frame.
The kit numbers below are the SHAPE KITS' numbers (fractions of the type's box, x forward, y left, z up from the bottom) — Map3D/ShapeKit.cs is built from the same table.
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "designs", "air-attack")
BODY, DARK, GLASS, METAL, LIGHT = "#808892", "#3B4048", "#6FA0C8", "#9AA3AE", "#B4BAC2"
COL = {"Body": BODY, "Dark": DARK, "Glass": GLASS, "Metal": METAL, "Light": LIGHT}

# ── the kits: (shape, axis, centre, extent, colour, pitch, yaw, roll) — shape B box · C cylinder · N cone ─────────────────────────────
def B(c, e, col, pitch=0, yaw=0, roll=0): return ("B", "Z", c, e, col, pitch, yaw, roll)
def Cy(c, e, col): return ("C", "X", c, e, col, 0, 0, 0)
def Cn(c, e, col): return ("N", "X", c, e, col, 0, 0, 0)

F16_SIZE = (15.0, 10.0, 4.9)   # ShapeKits.DefaultSize(Jet)
F16 = [   # ShapeKit.cs :401 — ShapeKits.Jet, verbatim
    Cy((-0.03, 0, 0.40), (0.80, 0, 0.28), "Body"), Cn((0.45, 0, 0.40), (0.10, 0, 0.28), "Body"), B((0.24, 0, 0.56), (0.18, 0.08, 0.10), "Glass"),
    B((-0.08, 0.24, 0.38), (0.26, 0.46, 0.025), "Body", yaw=25), B((-0.08, -0.24, 0.38), (0.26, 0.46, 0.025), "Body", yaw=-25),
    B((-0.41, 0.15, 0.40), (0.12, 0.28, 0.02), "Body", yaw=30), B((-0.41, -0.15, 0.40), (0.12, 0.28, 0.02), "Body", yaw=-30),
    B((-0.36, 0, 0.66), (0.18, 0.012, 0.38), "Body", pitch=-35), Cy((-0.46, 0, 0.40), (0.06, 0, 0.22), "Dark"),
]
MIG_SIZE = (17.3, 11.4, 4.7)   # the MiG-29's real length / span / height
MIG = [    # the NEW VisualFamily.TwinEngineJet kit — ShapeKit.cs TwinEngineJet is this table
    Cy((-0.02, 0, 0.44), (0.62, 0, 0.27), "Body"),                                  # the blended centre body
    Cn((0.38, 0, 0.45), (0.18, 0, 0.24), "Body"),                                    # nose
    B((0.17, 0, 0.60), (0.16, 0.05, 0.09), "Glass"),                                 # canopy
    B((0.00, 0, 0.36), (0.46, 0.20, 0.025), "Body"),                                 # the flat blended underbody (between the engine bays)
    B((-0.10, 0.25, 0.37), (0.30, 0.46, 0.025), "Body", yaw=28), B((-0.10, -0.25, 0.37), (0.30, 0.46, 0.025), "Body", yaw=-28),   # wings
    Cy((-0.20, 0.075, 0.38), (0.46, 0, 0.22), "Body"), Cy((-0.20, -0.075, 0.38), (0.46, 0, 0.22), "Body"),                        # the two engine bays
    Cy((-0.445, 0.075, 0.38), (0.07, 0, 0.19), "Dark"), Cy((-0.445, -0.075, 0.38), (0.07, 0, 0.19), "Dark"),                      # two nozzles
    B((0.06, 0.075, 0.25), (0.22, 0.07, 0.13), "Dark"), B((0.06, -0.075, 0.25), (0.22, 0.07, 0.13), "Dark"),                      # the intakes, under the wing roots
    B((-0.43, 0.15, 0.36), (0.12, 0.26, 0.02), "Body", yaw=30), B((-0.43, -0.15, 0.36), (0.12, 0.26, 0.02), "Body", yaw=-30),     # tailplanes
    B((-0.37, 0.115, 0.68), (0.17, 0.012, 0.40), "Body", pitch=-28, roll=-7),                                                     # TWO fins, canted outward 7 deg
    B((-0.37, -0.115, 0.68), (0.17, 0.012, 0.40), "Body", pitch=-28, roll=7),
]
# stores (fractions of each jet's own box): (kind, y, z) — IR outboard, radar inboard; left side first (the side that goes LAST)
# an IR missile: 3.0 m x 0.17 m (rail + body + nose); a radar missile: 3.7 m x 0.22 m. KIND_ROUNDS: the mount (IR 0 / radar 1) and its round index
def store_parts(size, kind, x, y, z):
    L, W, H = size
    if kind == "IR":   ln, dm, col, nose = 3.0, 0.17, "Metal", "Dark"
    else:              ln, dm, col, nose = 3.7, 0.22, "Light", "Dark"
    parts = [Cy((x, y, z), (ln / L, 0, dm / H), col), Cn((x + ln / L / 2 + 0.2 / L / 2, y, z), (0.2 / L, 0, dm / H), nose),
             B((x - ln / L * 0.4, y, z), (0.35 / L, dm * 2.4 / W, 0.02), "Dark")]     # the tail fins, a cross seen from above
    return parts
F16_STORES = [("IR", 0.487, 0.38, -0.12), ("IR", -0.487, 0.38, -0.12), ("RADAR", 0.30, 0.31, -0.04), ("RADAR", -0.30, 0.31, -0.04)]   # (kind, y, z, x)
MIG_STORES = [("IR", 0.36, 0.30, -0.08), ("IR", -0.36, 0.30, -0.08), ("RADAR", 0.215, 0.27, -0.04), ("RADAR", -0.215, 0.27, -0.04)]
PYLON = lambda size, y, z, x: B((x, y, z + 0.045), (0.06, 0.008, 0.05), "Dark")


# ── projection: a part → hull polygon in a 2-D view ──────────────────────────────────────────────────────────────────────────────
def _corners(p, size):
    shape, axis, c, e, col, pitch, yaw, roll = p
    L, W, H = size
    pts = []
    if shape == "B":
        hx, hy, hz = e[0] * L / 2, e[1] * W / 2, e[2] * H / 2
        pts = [(sx * hx, sy * hy, sz * hz) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
    else:
        ln, dm = e[0] * L, e[2] * H
        for end in (-0.5, 0.5):
            r = dm / 2 if (shape == "C" or end < 0) else 0.01
            for k in range(12):
                a = k * math.pi / 6
                pts.append((end * ln, r * math.cos(a), r * math.sin(a)))
    out = []
    for x, y, z in pts:
        if roll: r = math.radians(roll); y, z = y * math.cos(r) - z * math.sin(r), y * math.sin(r) + z * math.cos(r)
        if pitch: r = math.radians(pitch); x, z = x * math.cos(r) + z * math.sin(r), -x * math.sin(r) + z * math.cos(r)
        if yaw: r = math.radians(yaw); x, y = x * math.cos(r) - y * math.sin(r), x * math.sin(r) + y * math.cos(r)
        out.append((x + c[0] * L, y + c[1] * W, z + c[2] * H))
    return out


def _hull(pts):
    pts = sorted(set((round(x, 3), round(y, 3)) for x, y in pts))
    if len(pts) < 3: return pts
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0: lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0: up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]


def draw_kit(s, parts, size, view, ox, oy, k, stroke="#1B2027"):
    """view 'side' (nose right, from the left), 'top' (nose right, left wing up the page), 'rear' (looking forward; left wing at the left of the page).
    (ox, oy): the screen point of the body's centre-bottom (side/rear) or centre (top); k px per metre."""
    L, W, H = size
    def P(x, y, z):
        if view == "side": return (ox + x * k, oy - (z - 0) * k)
        if view == "top":  return (ox + x * k, oy - y * k)
        return (ox - y * k, oy - z * k)        # rear: the left wing (+y) at the left
    cs = [(p, _corners(p, size)) for p in parts]
    def depth(item):
        _, pts = item
        if view == "side": return sum(q[1] for q in pts) / len(pts)             # nearer = larger y (the left side)
        if view == "top":  return sum(q[2] for q in pts) / len(pts)
        return -sum(q[0] for q in pts) / len(pts)                              # rear: nearer = smaller x
    for p, pts in sorted(cs, key=depth):
        pts = [(x - L * 0.0, y, z - H * 0.0) for x, y, z in pts]
        # the kit's centre is the box centre: x ∈ [-L/2, L/2], y ∈ [-W/2, W/2], z ∈ [0, H] (the box bottom at 0)
        pts2 = [(x, y, z) for x, y, z in pts]
        poly = _hull([P(x, y, z) for x, y, z in pts2])
        col = COL[p[4]]
        s.raw("<polygon points='" + " ".join(f"{a:.1f},{b:.1f}" for a, b in poly) + f"' fill='{col}' stroke='{stroke}' stroke-width='1' stroke-linejoin='round'/>")


def panel(s, x, y, w, h, title):
    s.rect(x, y, w, h, "#15151A", stroke=C.BORDER); s.rect(x, y, w, 20, C.TITLE); s.text(x + 8, y + 14, title)


def ground(s, x, y, w, ox, oy, k):
    s.line(x, oy, x + w, oy, "#4A4A55", 1)


def scale_bar(s, x, y, k, label="5 m"):
    s.line(x, y, x + 5 * k, y, C.NOTE, 1.4); s.line(x, y - 3, x, y + 3, C.NOTE, 1.4); s.line(x + 5 * k, y - 3, x + 5 * k, y + 3, C.NOTE, 1.4)
    s.text(x + 5 * k / 2, y - 6, label, C.NOTE, 10.5, "middle")


# ══ 1. the body ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
def body_svg():
    W, H = 1240, 800
    s = Svg(W, H, "Air combat - the MiG-29 body beside the F-16 (CE-4647 AC-15, step 2)",
            "Grey annotations are not drawn by the app. Scope: the NEW MiG-29 body (class TwinEngineFighter, type 409). The F-16 (ShapeKits.Jet) is drawn from its real numbers and is UNCHANGED.")
    for col, (name, kit, size, tag, cx0) in enumerate((("F-16C (type 401) - existing, unchanged", F16, F16_SIZE, "ShapeKits.Jet", 20),
                                                       ("MiG-29 (type 409) - NEW, VisualFamily.TwinEngineJet", MIG, MIG_SIZE, "TwinEngineFighter", 640))):
        L, Wd, Ht = size
        k = 27 if col == 0 else 27
        panel(s, cx0, 60, 580, 230, f"{name} - side"); draw_kit(s, kit, size, "side", cx0 + 290, 262, k)
        s.line(cx0 + 20, 262, cx0 + 560, 262, "#33333B", 1)
        panel(s, cx0, 300, 580, 250, f"{name} - top"); draw_kit(s, kit, size, "top", cx0 + 290, 430, 18)
        panel(s, cx0, 560, 580, 230, f"{name} - rear (what the cant shows)"); draw_kit(s, kit, size, "rear", cx0 + 290, 760, 25 if col == 0 else 25)
        s.line(cx0 + 20, 760, cx0 + 560, 760, "#33333B", 1)
        s.text(cx0 + 12, 282, f"{L:g} x {Wd:g} x {Ht:g} m - the family's box", C.NOTE, 11)
        scale_bar(s, cx0 + 440, 280, 15 if False else 27 / 5 * 1.0 * 1)
    # callouts
    s.callout(640 + 290 + (-0.37 * 17.3) * 27, 262 - 0.68 * 4.7 * 27, 1000, 84, ["TWO fins, swept 28 deg,", "canted outward 7 deg"])
    s.callout(640 + 290 + (-0.445 * 17.3) * 27, 262 - 0.38 * 4.7 * 27, 660, 118, ["TWO nozzles, side by side"])
    s.callout(640 + 290 + (0.06 * 17.3) * 27, 262 - 0.25 * 4.7 * 27 + 8, 1000, 250, ["intakes UNDER the wing roots"])
    s.callout(20 + 290 + (-0.36 * 15) * 27, 262 - 0.66 * 4.9 * 27, 330, 84, ["ONE swept fin, ONE nozzle"])
    s.callout(640 + 290 + (0.17 * 17.3) * 27, 262 - 0.60 * 4.7 * 27, 870, 84, ["canopy"])
    s.note(36, 540, ["F-16: drawn from ShapeKit.cs:401 as built (the nose cone sits a hair ahead of the fuselage, as built)"], C.NOTE, 10.5)
    s.note(656, 540, ["MiG: a wider blended body between two engine bays, flat underbody, wings swept 28 deg, tailplanes on the booms"], C.NOTE, 10.5)
    s.note(36, 782, ["F-16 rear: one fin, one nozzle"], C.NOTE, 10.5)
    s.note(656, 782, ["MiG rear: fins canted outward, two nozzles"], C.NOTE, 10.5)
    s.note(20, 797, ["Out of scope (unchanged): gear, colours (the jet family tint), the picker row, the 2-D symbol."], C.NOTE, 10.5)
    return s


# ══ 2. the stores ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
def stores_svg():
    W, H = 1240, 800
    s = Svg(W, H, "Air combat - the missiles on the rails: full, and after two shots (CE-4647 AC-15, step 3)",
            "Grey annotations are not drawn by the app. Scope: the missiles carried + their rails. Rounds-left rule: one missile per round left on its mount, as the A-10's bombs (EntityBodyLayer3D.cs:483).")
    jets = (("F-16C", F16, F16_SIZE, F16_STORES), ("MiG-29", MIG, MIG_SIZE, MIG_STORES))
    states = (("FULL: 2 IR + 2 radar (mount 0: IR, mount 1: radar)", 2, 2), ("AFTER TWO SHOTS: one IR and one radar gone (1 + 1 left)", 1, 1))
    for row, (name, kit, size, stores) in enumerate(jets):
        for col, (title, ir_left, rd_left) in enumerate(states):
            x0, y0 = 20 + col * 610, 60 + row * 370
            panel(s, x0, y0, 600, 360, f"{name} - {title}")
            parts, shown = list(kit), []
            L, Wd, Ht = size
            for kind, y, z, x in stores:
                parts.append(PYLON(size, y, z, x))
            irs = [st for st in stores if st[0] == "IR"]; rds = [st for st in stores if st[0] == "RADAR"]
            for i, st in enumerate(irs):
                if i < ir_left: shown.append(st)
            for i, st in enumerate(rds):
                if i < rd_left: shown.append(st)
            sp = []
            for kind, y, z, x in shown: sp += store_parts(size, kind, x, y, z)
            draw_kit(s, parts, size, "top", x0 + 300, y0 + 175, 18); draw_kit(s, sp, size, "top", x0 + 300, y0 + 175, 18, stroke="#000")   # carried missiles drawn over the wing (they hang BELOW it)
            draw_kit(s, parts + sp, size, "side", x0 + 300, y0 + 340, 12)
            s.text(x0 + 12, y0 + 345, "side (small): the rails under the wing", C.NOTE, 10.5)
            ox = x0 + 300
            for kind, y, z, x in stores:
                on = (kind, y, z, x) in shown
                sx, sy = ox + x * L * 18, y0 + 175 - y * Wd * 18
                if not on: s.raw(f"<circle cx='{sx:.1f}' cy='{sy:.1f}' r='9' fill='none' stroke='{C.AMBER}' stroke-dasharray='3 3'/>")
            s.note(x0 + 12, y0 + 38, [f"IR outboard: {'wingtip rails' if row == 0 else 'outboard underwing rails'} (grey, 3.0 m)",
                                         f"radar inboard: underwing rails (light, 3.7 m)"], C.NOTE, 10.5)
            if col == 1:
                s.note(x0 + 330, y0 + 36, ["dashed ring = the rail is empty (a shot was fired):", "the left rails keep their missile, the right ones are gone"], C.NOTE, 10.5)
    s.note(20, 796, ["The rails (the dark pylons) are drawn whether loaded or not. Out of scope (unchanged): the body (steps 2), the in-flight missile (step 4)."], C.NOTE, 10.5)
    return s


# ══ 3. the HUD ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
HUDG = "#7CF58A"; HUDA = "#FFC83C"; HUDR = "#FF5A50"; HUDC = "#78DCFF"
def hud_svg():
    W, H = 1240, 800
    s = Svg(W, H, "Air combat - the fighter HUD for a possessed F-16 / MiG (CE-4647 AC-15, step 7)",
            "Grey annotations are not drawn by the app. Scope: the NEW elements (pipper, lock cue, in-zone cue, radar warning, g). The compass strip and the SPD/ALT/HDG, HEALTH, weapon lines are the real UI, UNCHANGED.")
    # the view: a banked horizon
    vx, vy, vw, vh = 20, 60, 800, 560
    s.rect(vx, vy, vw, vh, "#2C4F7C", stroke=C.BORDER)
    cx, cy = vx + vw / 2, vy + vh / 2 + 20
    ang = math.radians(-8)
    s.raw(f"<g clip-path='url(#v)'><clipPath id='v'><rect x='{vx}' y='{vy}' width='{vw}' height='{vh}'/></clipPath>"
          f"<polygon points='{vx - 600},{cy + 90} {vx + vw + 600},{cy - 40} {vx + vw + 600},{vy + vh + 600} {vx - 600},{vy + vh + 600}' fill='#47552F'/>"
          f"<polygon points='{vx - 600},{cy + 90} {vx + vw + 600},{cy - 40} {vx + vw + 600},{cy - 70} {vx - 600},{cy + 60}' fill='#6C86A6'/></g>")
    # the real, unchanged context: the compass strip and the HUD text column (colours from PossessionStation.Hud.ColourOf)
    s.rect(vx, vy, vw, 50, "#10131A", op=0.65); s.text(vx + vw / 2, vy + 20, "N          NE          E", "#E6EBF2", 13, "middle")
    s.text(vx + vw / 2, vy + 46, "HDG 045", "#E6EBF2", 12, "middle")
    ctx = [("SPD 152 m/s  ALT 312 m  HDG 045", "#96C8FF"), ("HEALTH 400/400", "#78EB78"), ("> M61 20 mm  500", "#FFDC5A"), ("  IR missile  2", "#D7D7D7"), ("  Radar missile  2", "#D7D7D7"),
           ("  Flares 30   Chaff 30", "#D7D7D7"), ("LMB fire  Tab weapon  Esc release", "#AFAFAF")]
    for i, (t, c) in enumerate(ctx): s.text(vx + 10, vy + 82 + i * 21, t, c, 14)
    s.text(vx + 10, vy + 82 + 7 * 21 + 12, "(unchanged: the real flyer HUD lines)", C.NOTE, 10.5)
    # target and the NEW elements
    tx, ty = vx + 560, vy + 235
    s.raw(f"<polygon points='{tx - 14},{ty} {tx + 14},{ty + 3} {tx + 8},{ty + 6} {tx - 8},{ty + 6}' fill='#202428'/><rect x='{tx - 2}' y='{ty - 7}' width='4' height='13' fill='#202428'/>")
    # the lock diamond + range
    s.raw(f"<polygon points='{tx},{ty - 34} {tx + 34},{ty} {tx},{ty + 34} {tx - 34},{ty}' fill='none' stroke='{HUDG}' stroke-width='2'/>")
    s.text(tx + 40, ty - 14, "LOCK", HUDG, 14); s.text(tx + 40, ty + 4, "3.1 km  -210 m/s", HUDG, 13)
    # the gun pipper at the led point, with the lead line
    px, py = tx - 44, ty + 20
    s.raw(f"<circle cx='{px}' cy='{py}' r='19' fill='none' stroke='{HUDA}' stroke-width='2'/><circle cx='{px}' cy='{py}' r='2.4' fill='{HUDA}'/>")
    s.line(tx - 8, ty + 6, px + 17, py - 5, HUDA, 1.5, "4 3")
    s.text(px - 22, py + 40, "PIPPER (led point)", HUDA, 12, "end")
    # the boresight cross (the existing crosshair is the player's aim)
    s.line(cx - 10, cy, cx - 3, cy, "#FFFFFF", 1.6); s.line(cx + 3, cy, cx + 10, cy, "#FFFFFF", 1.6); s.line(cx, cy - 10, cx, cy - 3, "#FFFFFF", 1.6); s.line(cx, cy + 3, cx, cy + 10, "#FFFFFF", 1.6)
    # in-zone cue (bottom centre): a bracket + SHOOT
    zx, zy = cx, vy + vh - 70
    s.rect(zx - 80, zy - 22, 160, 36, "#10131A", op=0.55, rx=3)
    s.text(zx, zy + 4, "IN ZONE", HUDG, 20, "middle", "bold")
    s.line(zx - 80, zy - 22, zx - 62, zy - 22, HUDG, 2); s.line(zx - 80, zy - 22, zx - 80, zy - 8, HUDG, 2); s.line(zx + 80, zy - 22, zx + 62, zy - 22, HUDG, 2); s.line(zx + 80, zy - 22, zx + 80, zy - 8, HUDG, 2)
    # g readout and the radar warning (left column, under the context)
    s.rect(vx + 10, vy + vh - 120, 90, 28, "#10131A", op=0.55, rx=3); s.text(vx + 55, vy + vh - 100, "G  6.2", HUDA, 18, "middle", "bold")
    # radar warning ring (bottom-left)
    rx, ry, rr = vx + 90, vy + vh - 220, 46
    s.raw(f"<circle cx='{rx}' cy='{ry}' r='{rr}' fill='#10131A' fill-opacity='0.55' stroke='{C.NOTE}'/><circle cx='{rx}' cy='{ry}' r='{rr / 2}' fill='none' stroke='{C.NOTE}' stroke-dasharray='2 3'/>")
    s.line(rx, ry - rr, rx, ry + rr, "#3A3F4A", 1); s.line(rx - rr, ry, rx + rr, ry, "#3A3F4A", 1)
    s.raw(f"<polygon points='{rx - 24},{ry - 39} {rx - 17},{ry - 29} {rx - 31},{ry - 29}' fill='{HUDR}'/>")
    s.text(rx + rr + 12, ry - 8, "LAUNCH  IR  4.2 s", HUDR, 14, "start", "bold"); s.text(rx + rr + 12, ry + 10, "(tracked: amber tick, no text)", C.NOTE, 10.5)
    # callouts (grey, not drawn)
    s.callout(px, py - 20, 600, 120, ["gun pipper: where the burst will be when", "it arrives (the led point, accelerating target)"])
    s.callout(tx + 26, ty - 26, 600, 160, ["lock diamond + range / closure"])
    s.callout(zx + 60, zy - 14, 640, vy + vh - 150, ["IN ZONE: a missile shot now cannot", "be out-run (the employment zone, AC-8)"])
    s.callout(rx - 24, ry - 39, vx + 190, vy + 380, ["radar warning: a tick at the bearing of the emitter /", "the nearest inbound missile; the text on a LAUNCH"])
    # right column: the states, zoomed
    x0 = 840
    panel(s, x0, 60, 380, 130, "pipper + lead line");
    s.raw(f"<polygon points='{x0 + 70},{125} {x0 + 98},{128} {x0 + 92},{131} {x0 + 76},{131}' fill='#D0D4DA'/>")
    s.raw(f"<circle cx='{x0 + 150}' cy='132' r='19' fill='none' stroke='{HUDA}' stroke-width='2'/><circle cx='{x0 + 150}' cy='132' r='2.4' fill='{HUDA}'/>"); s.line(x0 + 90, 130, x0 + 132, 128, HUDA, 1.5, "4 3")
    s.text(x0 + 190, 128, "shown inside 500 m,", C.SUB, 12); s.text(x0 + 190, 144, "gun selected (> M61)", C.SUB, 12); s.text(x0 + 12, 178, "amber; red when the aim is off by < 2 deg", C.NOTE, 10.5)
    panel(s, x0, 200, 380, 150, "missile cue - three states")
    for i, (lab, col, sub) in enumerate((("SEARCH", C.GREY, "no lock, no cue"), ("IN ZONE", HUDG, "target inside the zone; seeker looking"), ("LOCK", HUDA, "seeker has it (diamond), tone on; amber -> green in zone"))):
        s.text(x0 + 14, 240 + i * 36, lab, col, 16, "start", "bold"); s.text(x0 + 110, 240 + i * 36, sub, C.SUB, 11.5)
    panel(s, x0, 360, 380, 130, "radar warning - two states")
    s.raw(f"<polygon points='{x0 + 30},{415} {x0 + 38},{426} {x0 + 22},{426}' fill='{HUDA}'/>"); s.text(x0 + 60, 424, "TRACKED - a radar has you (amber tick)", C.SUB, 12)
    s.raw(f"<polygon points='{x0 + 30},{450} {x0 + 38},{461} {x0 + 22},{461}' fill='{HUDR}'/>"); s.text(x0 + 60, 459, "LAUNCH <seeker> <time to impact> (red, blinks)", C.SUB, 12)
    panel(s, x0, 500, 380, 120, "g read-out - three colours")
    for i, (t, c, sub) in enumerate((("G  3.1", "#D7D7D7", "below 6"), ("G  6.8", HUDA, "6 g and up: vortex trails show"), ("G  8.7", HUDR, "within 1 g of the limit"))):
        s.text(x0 + 14, 540 + i * 26, t, c, 16, "start", "bold"); s.text(x0 + 110, 540 + i * 26, sub, C.SUB, 12)
    s.note(20, 640, ["Layout rule (the measured rail): every new element is at a FIXED anchor - the lock diamond on the target's screen point, the pipper on the led point, the IN ZONE tag",
                     "bottom-centre, the radar ring bottom-left, the g tag under it; ALL under the existing compass strip (PlayerHudLayer.Top = 62 px).",
                     "The player flies the MANUAL mouse-aim pilot (mode 8 is AI only): the aim is the look, the HUD shows the cues; there is no autopilot cue here."], C.SUB, 11.5)
    s.note(20, 700, ["Out of scope (unchanged): the compass strip, SPD/ALT/HDG, HEALTH and weapon lines, the crosshair, the kill feed and mission column."], C.NOTE, 11)
    return s


# ══ 4. the arena ══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
def arena_svg():
    W, H = 1240, 800
    s = Svg(W, H, "Air combat - the arena: 2-D, 3-D, gizmo handles and its Details form (CE-4647 AC-15, step 6)",
            "Grey annotations are not drawn by the app. Scope: the arena ring / floor drawing, the gizmo's four handles, the Details form. The map, the window frame and the 2-D symbols are context, UNCHANGED.")
    # 2-D
    panel(s, 20, 60, 440, 360, "2-D map - the arena (selected)")
    mx, my, mw, mh = 28, 84, 424, 330
    s.rect(mx, my, mw, mh, "#20261B", stroke=C.BORDER)
    for g in range(0, mw, 30): s.line(mx + g, my, mx + g, my + mh, "#2A3123", 1)
    for g in range(0, mh, 30): s.line(mx, my + g, mx + mw, my + g, "#2A3123", 1)
    cx, cy, r = mx + 212, my + 165, 120
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='{r}' fill='{C.INFO}' fill-opacity='0.06' stroke='{C.INFO}' stroke-opacity='0.55' stroke-width='1.6' stroke-dasharray='9 6'/>")
    s.text(cx, cy - r - 8, "AIR ARENA  r 6.0 km  floor 150 m  ceiling 4000 m", C.INFO, 11.5, "middle")
    for lab, (hx, hy) in (("centre", (cx, cy)), ("radius", (cx + r, cy)), ("floor", (cx - 60, cy + 60)), ("ceiling", (cx + 60, cy - 60))):
        pass
    # the handles: centre (square), radius (circle on the ring), floor / ceiling (little triangles, in the label strip)
    s.rect(cx - 6, cy - 6, 12, 12, C.AMBER, stroke="#222", sw=1); s.raw(f"<circle cx='{cx + r}' cy='{cy}' r='6' fill='{C.AMBER}' stroke='#222'/>")
    s.raw(f"<polygon points='{cx - 70},{cy + r + 14} {cx - 62},{cy + r + 26} {cx - 78},{cy + r + 26}' fill='{C.AMBER}' stroke='#222'/>")   # floor: down
    s.raw(f"<polygon points='{cx + 70},{cy + r + 26} {cx + 62},{cy + r + 14} {cx + 78},{cy + r + 14}' fill='{C.AMBER}' stroke='#222'/>")   # ceiling: up
    s.text(cx - 54, cy + r + 24, "floor", C.AMBER, 10.5); s.text(cx + 84, cy + r + 24, "ceiling", C.AMBER, 10.5)
    for (jx, jy, c, dr) in ((cx - 50, cy - 35, "#3E6FB0", 35), (cx + 55, cy + 25, "#B04040", 200)):
        a = math.radians(dr); s.raw(f"<polygon points='{jx + 10 * math.cos(a)},{jy - 10 * math.sin(a)} {jx + 7 * math.cos(a + 2.5)},{jy - 7 * math.sin(a + 2.5)} {jx + 7 * math.cos(a - 2.5)},{jy - 7 * math.sin(a - 2.5)}' fill='{c}' stroke='#E8E8EE'/>")
    s.callout(cx + r, cy - 6, 360, 118, ["radius handle (drag along the ring)"]); s.callout(cx, cy, 36, 372, ["centre handle (drag: moves it)"])
    s.note(40, 112, ["faint ring: dashed, 6% fill (a stage, not a wall)"], C.NOTE, 10.5)
    # 3-D
    panel(s, 480, 60, 740, 360, "3-D map - the same arena (an oblique view)")
    ox, oy = 850, 290
    s.rect(482, 82, 736, 336, "#2C4F7C"); s.rect(482, 262, 736, 156, "#47552F")
    def E(a, rr, z): return (ox + math.cos(a) * rr, oy + math.sin(a) * rr * 0.28 - z)
    for z, op, w in ((0, 0.8, 2.0), (95, 0.5, 1.4)):      # the floor ring (the floor drawn as a faint disc + ring) and the ceiling ring, high
        pts = [E(a * math.pi / 36, 300, z) for a in range(73)]
        s.raw("<polyline points='" + " ".join(f"{x:.1f},{y:.1f}" for x, y in pts) + f"' fill='none' stroke='{C.INFO}' stroke-opacity='{op}' stroke-width='{w}' stroke-dasharray='9 6'/>")
    fpts = [E(a * math.pi / 36, 300, 30) for a in range(73)]
    s.raw("<polygon points='" + " ".join(f"{x:.1f},{y:.1f}" for x, y in fpts) + f"' fill='{C.INFO}' fill-opacity='0.10' stroke='none'/>")
    for a in (0, 90, 180, 270):
        x, y = E(math.radians(a), 300, 30); x2, y2 = E(math.radians(a), 300, 95); s.line(x, y, x2, y2, C.INFO, 1.2, "2 4")
    s.text(ox, 112, "ceiling ring - 4000 m", C.INFO, 11.5, "middle"); s.text(ox, 322, "floor disc - 150 m (a missile / jet below it is outside the stage)", C.INFO, 11.5, "middle")
    for x, y in (E(0, 300, 30), E(math.pi / 2, 300, 30)): s.raw(f"<circle cx='{x:.1f}' cy='{y:.1f}' r='7' fill='{C.AMBER}' stroke='#222'/>")
    s.note(498, 400, ["3-D: the ring and floor are drawn only while the arena is selected or the map layer 'Arenas' is on; one line pass, no per-frame allocation"], C.NOTE, 10.5)
    # Details form
    panel(s, 20, 440, 600, 350, "Details - Air Arena (the form; window frame unchanged)")
    s.rect(28, 466, 584, 316, C.BG); s.glyph(FA.PLANE, 40, 474, C.BLUE, 15); s.text(62, 490, "Air Arena 1", C.TXT, 14)
    rows = (("Centre", "x 4200   y 3100", None), ("Radius", "6000 m", None), ("Floor", "150 m", None), ("Ceiling", "4000 m", None), ("Max shot range", "6000 m", None), ("Stalemate", "90 s", None))
    s.separator_text(40, 508, "Geometry", 600)
    for i, (lab, val, _) in enumerate(rows[:4]): s.form_row(48, 530 + i * 26, lab, 140, "field", 200, val)
    s.separator_text(40, 640, "Show rules", 600)
    for i, (lab, val, _) in enumerate(rows[4:]): s.form_row(48, 662 + i * 26, lab, 140, "field", 200, val)
    s.callout(260, 539, 470, 560, ["the same four numbers the gizmo drags"]); s.callout(260, 669, 470, 680, ["authored; read by the air brains"])
    # the gizmo legend
    panel(s, 640, 440, 580, 350, "the authoring gizmo - four handles")
    for i, (sh, lab, sub) in enumerate((("sq", "centre", "drag on the map: moves the arena (the ring, the floor and the ceiling follow)"), ("ci", "radius", "on the ring, east: drag outward / inward (R 500 m ... 30 km)"),
                                        ("dn", "floor", "a triangle on the ring, south-west: drag changes the floor height; shown in the label"), ("up", "ceiling", "a triangle south-east: the ceiling height; floor < ceiling is enforced"))):
        y = 490 + i * 40
        if sh == "sq": s.rect(660, y - 6, 12, 12, C.AMBER, stroke="#222")
        elif sh == "ci": s.raw(f"<circle cx='666' cy='{y}' r='6' fill='{C.AMBER}' stroke='#222'/>")
        elif sh == "dn": s.raw(f"<polygon points='666,{y + 6} 672,{y - 5} 660,{y - 5}' fill='{C.AMBER}' stroke='#222'/>")
        else: s.raw(f"<polygon points='666,{y - 6} 672,{y + 5} 660,{y + 5}' fill='{C.AMBER}' stroke='#222'/>")
        s.text(690, y + 4, lab, C.TXT, 13); s.text(760, y + 4, sub, C.SUB, 11.5)
    s.note(660, 680, ["Edits go through the ONE edit path (undo / redo), the same as the minefield gizmo; the form and the gizmo share the AirArena row."], C.NOTE, 11)
    s.note(660, 720, ["Out of scope (unchanged): the map, the window frame, the 2-D symbols, the picker."], C.NOTE, 11)
    return s


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    png = sys.argv[sys.argv.index("--png") + 1] if "--png" in sys.argv else None
    for name, fn in (("air-combat-body", body_svg), ("air-combat-stores", stores_svg), ("air-combat-hud", hud_svg), ("air-combat-arena", arena_svg)):
        s = fn(); p = os.path.join(OUT, name + ".svg"); s.save(p); print(p)
        if png:
            from mockup_kit import render
            os.makedirs(png, exist_ok=True); render(p, os.path.join(png, name + ".png"), s.w, s.h)
