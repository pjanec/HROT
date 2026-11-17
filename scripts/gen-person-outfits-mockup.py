#!/usr/bin/env python3
"""CE-4648 S2 (R-4143, DESIGN_Person_Outfits.md §4d) — the NEW GARMENTS on the block figure, front + side, one tile per garment and one sheet of all.
  person-outfits-<garment>.svg   one garment, front and side          person-outfits-sheet.svg   every garment (the picture the design carries)
Output: docs/designs/person-looks/*.svg (PNGs for the chat via --png <dir>, never committed).
SCOPE (R-4151): ONLY the new garment boxes below (what Outfits.Details draws). The figure under them (legs, torso, head, arms) is the EXISTING block figure
(ShapeKit.cs StandingBody: torso 0.24x0.40x0.56 m centred z 1.18; head 0.21x0.20x0.24 at z 1.60; arms hang at y +-0.26) drawn for context, UNCHANGED.
Boxes are WORLD METRES of the 1.8 m standing figure: x forward, y left, z up. Outfits.cs converts each to its part's unit frame (the numbers are the same).
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, render

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "designs", "person-looks")
SKIN = "#B9855F"; BASE = "#7A7F86"; DARKBASE = "#5C6168"
WHITE, CREAM, BLACK, RED, DKRED = "#F2EFE6", "#E6DCC3", "#1E1E22", "#C23B32", "#8C2A24"
GREY, BROWN, OLIVE, KHAKI, NAVY = "#9A9CA0", "#7A5A3C", "#5E6040", "#A59A72", "#23304A"
BUCK, BUCKD, TAN = "#C9A06A", "#9C7646", "#B88C58"

def Bx(x, y, z, dx, dy, dz, col): return (x, y, z, dx, dy, dz, col)

# (title, reference, base-torso, base-legs, hair/skin, boxes, sleeve colour)
def thobe():
    return [Bx(0, 0, 0.62, 0.30, 0.44, 0.60, WHITE), Bx(0, 0, 0.30, 0.34, 0.50, 0.48, WHITE),   # skirt to the ankle, the hem a little wider
            Bx(0, 0, 1.455, 0.22, 0.30, 0.05, WHITE), Bx(0.13, 0, 1.10, 0.012, 0.03, 0.62, "#CFC9B8")]   # band collar, the placket
def abaya():
    return [Bx(0, 0, 0.50, 0.37, 0.54, 0.95, BLACK), Bx(0, 0, 1.38, 0.30, 0.52, 0.12, BLACK), Bx(0.19, 0, 1.0, 0.012, 0.03, 0.90, "#2B2B30")]
def shemagh():   # a LARGE cloth: wider than the head, taller than the crown, framing the face; side flaps to mid-chest, a deep back drape; the agal ON TOP
    b = [Bx(0, 0, 1.74, 0.25, 0.26, 0.12, WHITE)]                                    # crown: 0.03 m over each side (15 %), 0.08 over the head top
    b += [Bx(0, 0, 1.745, 0.252, 0.045, 0.122, RED), Bx(0.0, 0.07, 1.745, 0.252, 0.02, 0.122, RED), Bx(0.0, -0.07, 1.745, 0.252, 0.02, 0.122, RED)]   # check bands over the crown
    for s in (1, -1):
        b += [Bx(0, 0.13 * s, 1.58, 0.21, 0.045, 0.28, WHITE), Bx(0, 0.13 * s, 1.62, 0.212, 0.047, 0.035, RED), Bx(0, 0.13 * s, 1.51, 0.212, 0.047, 0.035, RED)]   # temple flaps past the ears
        b += [Bx(0.14, 0.125 * s, 1.27, 0.04, 0.10, 0.34, WHITE), Bx(0.142, 0.125 * s, 1.33, 0.042, 0.102, 0.035, RED), Bx(0.142, 0.125 * s, 1.20, 0.042, 0.102, 0.035, RED)]   # front panels either side of the neck, to mid-chest
    b += [Bx(-0.135, 0, 1.48, 0.06, 0.32, 0.62, WHITE), Bx(-0.138, 0, 1.58, 0.062, 0.322, 0.04, RED), Bx(-0.138, 0, 1.44, 0.062, 0.322, 0.04, RED), Bx(-0.138, 0, 1.30, 0.062, 0.322, 0.04, RED)]   # the back drape, crown to between the shoulder blades
    b += [Bx(0, 0, 1.815, 0.23, 0.23, 0.02, BLACK), Bx(0, 0, 1.845, 0.23, 0.23, 0.02, BLACK)]   # the agal: a black double ring on the crown, inset
    return b

def skullcap():
    return [Bx(0, 0, 1.705, 0.215, 0.205, 0.05, WHITE), Bx(0, 0, 1.67, 0.225, 0.215, 0.03, WHITE)]
def hijab():
    b = [Bx(0, 0, 1.715, 0.25, 0.24, 0.10, BLACK), Bx(0.125, 0, 1.685, 0.03, 0.22, 0.05, BLACK), Bx(0, 0.125, 1.58, 0.22, 0.04, 0.30, BLACK),
         Bx(0, -0.125, 1.58, 0.22, 0.04, 0.30, BLACK), Bx(-0.13, 0, 1.55, 0.05, 0.26, 0.34, BLACK), Bx(0.02, 0, 1.455, 0.22, 0.27, 0.05, BLACK)]
    b += [Bx(0, 0, 1.38, 0.30, 0.46, 0.12, BLACK),                      # over the shoulders
          Bx(0.135, 0, 1.30, 0.04, 0.30, 0.30, BLACK),                  # the drape down the chest to mid-chest
          Bx(-0.135, 0, 1.28, 0.04, 0.34, 0.34, BLACK)]                 # and down the back
    return b

def niqab():
    return hijab() + [Bx(0.118, 0, 1.54, 0.04, 0.22, 0.12, BLACK)]
def chestrig():
    return [Bx(0, 0, 1.22, 0.27, 0.43, 0.30, OLIVE), Bx(0, 0.10, 1.40, 0.25, 0.05, 0.12, OLIVE), Bx(0, -0.10, 1.40, 0.25, 0.05, 0.12, OLIVE),
            Bx(0.145, 0.13, 1.20, 0.06, 0.08, 0.13, "#4A4C32"), Bx(0.145, 0.0, 1.20, 0.06, 0.08, 0.13, "#4A4C32"), Bx(0.145, -0.13, 1.20, 0.06, 0.08, 0.13, "#4A4C32")]
def facewrap():   # the shemagh WOUND round the head and over the face: only an eye slit of skin; the tail at one shoulder; no agal
    W, D = KHAKI, "#8C8258"
    return [Bx(0, 0, 1.718, 0.25, 0.26, 0.125, W), Bx(0, 0, 1.53, 0.25, 0.26, 0.14, W),                      # above the slit (the crown, the brow) and below it (nose, mouth, chin)
            Bx(0, 0.13, 1.627, 0.21, 0.04, 0.055, W), Bx(0, -0.13, 1.627, 0.21, 0.04, 0.055, W), Bx(-0.13, 0, 1.627, 0.05, 0.26, 0.055, W),   # round the sides and the back at the slit
            Bx(0, 0, 1.76, 0.255, 0.265, 0.03, D), Bx(0, 0, 1.49, 0.255, 0.265, 0.03, D),            # the turn folds
            Bx(0.02, 0.15, 1.33, 0.07, 0.07, 0.30, W), Bx(0.02, 0.15, 1.17, 0.075, 0.075, 0.03, D)]                                          # the loose tail at one shoulder

def buckskin():
    b = [Bx(0, 0, 1.20, 0.27, 0.43, 0.50, BUCK)]
    for k in range(5): b += [Bx(0.135, -0.16 + 0.08 * k, 0.88, 0.02, 0.03, 0.12, BUCKD), Bx(-0.135, -0.16 + 0.08 * k, 0.88, 0.02, 0.03, 0.12, BUCKD)]
    for s in (1, -1): b += [Bx(0, 0.295 * s, 1.20, 0.025, 0.03, 0.40, BUCKD)]
    return b
def breechcloth():
    return [Bx(0, 0, 0.93, 0.27, 0.43, 0.05, BROWN), Bx(0.135, 0, 0.74, 0.02, 0.16, 0.38, BUCK), Bx(-0.135, 0, 0.74, 0.02, 0.16, 0.38, BUCK)]
def headband():   # the feathers rise from the BACK of the band, stepped backwards and fanned outwards, white with a black tip
    b = [Bx(0, 0, 1.665, 0.225, 0.215, 0.03, DKRED)]
    for k, s in enumerate((-1, 0, 1)):
        h = (0.055, 0.065, 0.055)[k]
        for i in range(3):
            y = 0.055 * s * (1 + 0.45 * i)
            x = -0.105 - 0.017 * i
            z = 1.70 + 0.07 * i + h * 0.35
            b.append(Bx(x, y, z, 0.016, 0.034, h + 0.02, BLACK if i == 2 else WHITE))
    return b

def braids():
    b = [Bx(0, 0, 1.745, 0.23, 0.22, 0.05, BLACK), Bx(-0.13, 0, 1.52, 0.05, 0.24, 0.40, BLACK)]
    for s in (1, -1): b += [Bx(0.07, 0.115 * s, 1.30, 0.05, 0.05, 0.36, BLACK), Bx(0.07, 0.115 * s, 1.10, 0.05, 0.05, 0.03, DKRED)]
    return b
def facepaint():
    return [Bx(0.108, 0, 1.63, 0.012, 0.20, 0.04, BLACK), Bx(0.108, 0.07, 1.56, 0.012, 0.05, 0.10, RED), Bx(0.108, -0.07, 1.56, 0.012, 0.05, 0.10, WHITE)]

# name, title, reference line (the dress it is modelled on), boxes, torso colour (what is under it), leg style, sleeve = torso
G = [
 ("thobe", "Robe (thobe / dishdasha)", "ankle-length, long sleeves, white or light cloth, a band collar and a front placket; the hem a little wider", thobe, WHITE, "hidden"),
 ("abaya", "Abaya", "the women's black full-length cloak: wider than the thobe, to the ground, a cape over the shoulders", abaya, BLACK, "hidden"),
 ("shemagh", "Shemagh + agal", "a large red-on-white cloth wider than the head, framing the face; flaps to mid-chest, a deep back drape; black double agal ON the crown (on the thobe)", lambda: thobe() + shemagh(), WHITE, "hidden"),
 ("skullcap", "Skullcap (taqiyah)", "the small white knitted cap worn alone or under the shemagh: a low dome with a band", skullcap, WHITE, "trousers"),
 ("hijab", "Hijab", "a black headscarf: close over the crown, ears and nape, the cloth continuing over the shoulders and down the chest and back to mid-chest, the face open", hijab, BLACK, "hidden"),
 ("niqab", "Niqab", "the hijab plus a black veil over the face below the eyes — the eye band stays open", niqab, BLACK, "hidden"),
 ("chestrig", "Chest rig", "an olive ammunition vest: a panel over the chest, two shoulder straps, three magazine pouches across the front", chestrig, KHAKI, "trousers"),
 ("facewrap", "Face wrap", "the shemagh wound round the head like a turban AND over the face, only an eye slit of skin; the loose tail at one shoulder; no agal. Shown on the fighter with the chest rig", lambda: facewrap() + chestrig(), KHAKI, "trousers"),
 ("buckskin", "Buckskin shirt + fringe", "a tan tunic, the hem cut into a fringe front and back, a fringe down each sleeve seam", buckskin, BUCK, "bare"),
 ("breechcloth", "Breechcloth", "a belt with a flap of tan skin hanging at the front and the back; thighs bare, the shins in leggings", breechcloth, SKIN, "legging"),
 ("headband", "Headband + feathers", "a red band; three feathers rising from its back, leaning backwards, fanned, white with black tips", headband, SKIN, "bare"),
 ("braids", "Braids", "black hair: the crown, a fall down the back and two braids over the chest, each tied with red", braids, SKIN, "bare"),
 ("facepaint", "Face paint", "a black band across the eyes, a red stripe on one cheek and a white one on the other", facepaint, SKIN, "bare"),
]

def figure(torso, legs):
    sleeve = torso if torso != SKIN else SKIN
    lcol = {"hidden": torso, "trousers": DARKBASE, "bare": SKIN, "legging": BUCKD}[legs]
    thigh = SKIN if legs in ("bare", "legging") else lcol
    b = [("legs", Bx(0, 0.11, 0.46, 0.16, 0.16, 0.88, lcol)), ("legs", Bx(0, -0.11, 0.46, 0.16, 0.16, 0.88, lcol)),
         ("t", Bx(0, 0, 1.18, 0.24, 0.40, 0.56, torso)),
         ("h", Bx(0, 0, 1.60, 0.21, 0.20, 0.24, SKIN)),
         ("a", Bx(0, 0.26, 1.12, 0.10, 0.10, 0.62, sleeve)), ("a", Bx(0, -0.26, 1.12, 0.10, 0.10, 0.62, sleeve))]
    if legs in ("bare", "legging"):   # thighs bare in a breechcloth / bare legs
        b[0] = ("legs", Bx(0, 0.11, 0.46, 0.16, 0.16, 0.88, SKIN if legs == "bare" else BUCKD)); b[1] = ("legs", Bx(0, -0.11, 0.46, 0.16, 0.16, 0.88, SKIN if legs == "bare" else BUCKD))
    return [x[1] for x in b]

def shade(col, f):
    c = col.lstrip("#"); r, g, bl = (int(c[i:i + 2], 16) for i in (0, 2, 4))
    return "#%02X%02X%02X" % tuple(max(0, min(255, int(v * f))) for v in (r, g, bl))

def draw_view(svg, ox, oy, boxes, view, k):
    """view 'front' (we look along -x: horizontal = -y so his left is on our right... drawn as seen from the front), 'side' (from his left)."""
    order = sorted(boxes, key=lambda b: b[0] if view == "front" else b[1])
    for (x, y, z, dx, dy, dz, col) in order:
        if view == "front": w, cx = dy, -y
        else: w, cx = dx, x
        px, py = ox + cx * k - w * k / 2, oy - (z + dz / 2) * k
        svg.rect(round(px, 1), round(py, 1), max(1.2, round(w * k, 1)), max(1.2, round(dz * k, 1)), col, stroke=(shade(col, 0.55) if sum(int(col.lstrip('#')[i:i+2],16) for i in (0,2,4)) > 150 else '#5A5A66'), sw=0.8)

def tile(svg, x0, y0, g, k=106):
    name, title, ref, fn, torso, legs = g
    svg.rect(x0, y0, 440, 292, C.BG, stroke=C.BORDER)
    svg.text(x0 + 10, y0 + 18, title, "#E8E8EE", 13, weight="bold")
    base = figure(torso, legs); over = fn()
    gx = y0 + 266
    draw_view(svg, x0 + 110, gx, base + over, "front", k)
    draw_view(svg, x0 + 320, gx, base + over, "side", k)
    svg.text(x0 + 110, y0 + 280, "front", C.NOTE, 10.5, "middle"); svg.text(x0 + 320, y0 + 280, "side", C.NOTE, 10.5, "middle")
    # the reference, wrapped
    words, line, ly = ref.split(), "", y0 + 36
    for w in words:
        if len(line) + len(w) > 62: svg.text(x0 + 10, ly, line, C.NOTE, 10.5); line, ly = "", ly + 13
        line = (line + " " + w).strip()
    svg.text(x0 + 10, ly, line, C.NOTE, 10.5)
    svg.line(x0 + 10, gx, x0 + 430, gx, C.SEP, 1, "3 3")

def single(g):
    s = Svg(440, 292, None); tile(s, 0, 0, g)
    return s

def sheet():
    cols = 3; rows = (len(G) + cols - 1) // cols
    W, H = 20 + cols * 450, 64 + rows * 302 + 40
    s = Svg(W, H, "Person outfits — the new garments on the block figure (front and side)",
            "Scope (R-4151): ONLY the garment boxes. The figure under them is the existing block figure, drawn for context and unchanged. Annotations in grey are not drawn by the app.")
    for i, g in enumerate(G):
        tile(s, 10 + (i % cols) * 450, 60 + (i // cols) * 302, g)
    return s

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    png = sys.argv[sys.argv.index("--png") + 1] if "--png" in sys.argv else None
    outs = [("person-outfits-sheet", sheet())] + [(f"person-outfits-{g[0]}", single(g)) for g in G]
    for name, s in outs:
        p = os.path.join(OUT, name + ".svg"); s.save(p)
        if png: os.makedirs(png, exist_ok=True); render(p, os.path.join(png, name + ".png"), s.w, s.h)
