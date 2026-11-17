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
WHITE, CREAM, BLACK, RED, DKRED = "#F4F3EE", "#E6DCC3", "#1E1E22", "#C23B32", "#8C2A24"
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
def headband():   # the feathers rise from the BACK of the band, 0.055 m wide, stepped backwards and fanned past the head, white with a black tip
    b = [Bx(0, 0, 1.665, 0.225, 0.215, 0.03, DKRED)]
    for s in (-1, 0, 1):
        for i in range(3):
            h = 0.075 if i == 2 else 0.10; z = 1.705 + (0.05, 0.15, 0.2375)[i]
            b.append(Bx(-0.105 - 0.02 * i, 0.07 * s * (1 + 0.75 * i), z, 0.02, 0.055, h, BLACK if i == 2 else "#EEEADE"))
    return b
def braids():   # black hair: the crown, a fall down the back, and on EACH side a braid that grows out of the hair beside the face (over the ear), over the front of the shoulder, down the chest to a red tie
    b = [Bx(0, 0, 1.745, 0.23, 0.22, 0.05, BLACK), Bx(-0.13, 0, 1.52, 0.05, 0.24, 0.40, BLACK)]
    for s in (1, -1):
        b += [Bx(0.03, 0.115 * s, 1.585, 0.10, 0.04, 0.22, BLACK), Bx(0.06, 0.12 * s, 1.42, 0.09, 0.05, 0.10, BLACK), Bx(0.145, 0.12 * s, 1.28, 0.05, 0.05, 0.30, BLACK), Bx(0.145, 0.12 * s, 1.10, 0.055, 0.055, 0.03, DKRED)]
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
 ("headband", "Headband + feathers", "a red band; three broad feathers rising from its back, fanned past the head, white, black tips", headband, SKIN, "bare"),
 ("braids", "Braids", "black hair: the crown, a fall down the back and two braids over the chest, each tied with red", braids, SKIN, "bare"),
 ("facepaint", "Face paint", "a black band across the eyes, a red stripe on one cheek and a white one on the other", facepaint, SKIN, "bare"),
]

# ══ CE-4648 — the NATIVE AMERICAN WARRIOR's look and the AFGHAN dress (user, 2026-11-17) ═════════════════════════════════════════════
BEADS = ("#2F5FB0", "#C23B32", "#F1EEE4", "#E3B831")   # blue, red, white, yellow
HAIRB = "#161416"; PIPE = "#EFE9D8"; LEATHER = "#7A5230"

def beads_row(x, z, n=9, y0=-0.16, step=0.04, sz=0.03, face="front"):
    out = []
    for i in range(n):
        c = BEADS[(i + (1 if z > 1.37 else 0)) % 4]
        out.append(Bx(x, y0 + i * step, z, 0.012, sz, sz, c))
    return out

def yoke():          # a band of small coloured squares across the chest and over the shoulders, front and back
    b = beads_row(0.134, 1.405) + beads_row(0.134, 1.365) + beads_row(-0.134, 1.405)
    b += [Bx(0, 0.15, 1.468, 0.20, 0.03, 0.012, BEADS[0]), Bx(0, -0.15, 1.468, 0.20, 0.03, 0.012, BEADS[1])]
    return b
def hairpipe():      # a bone hair-pipe breastplate: vertical rows of white tubes between two leather bars
    b = [Bx(0.142, -0.15 + 0.06 * i, 1.25, 0.025, 0.032, 0.30, PIPE) for i in range(6)]
    b += [Bx(0.142, 0, 1.405, 0.03, 0.34, 0.025, LEATHER), Bx(0.142, 0, 1.10, 0.03, 0.34, 0.025, LEATHER)]
    return b
def armbands():      # a thin band round the MIDDLE of each upper arm (a third of the way from shoulder to elbow), slightly thicker than the arm, red with a blue stripe
    return [Bx(0, 0.26, 1.27, 0.122, 0.122, 0.04, BEADS[1]), Bx(0, -0.26, 1.27, 0.122, 0.122, 0.04, BEADS[1]),
            Bx(0, 0.26, 1.27, 0.126, 0.126, 0.012, BEADS[0]), Bx(0, -0.26, 1.27, 0.126, 0.126, 0.012, BEADS[0])]
def necklace():      # a bead necklace: a collar round the neck, a strand hanging on the chest with a pendant
    b = [Bx(0.0, 0, 1.46, 0.19, 0.23, 0.035, BEADS[2])]
    b += [Bx(0.138, 0.06 * s, 1.40 - 0.02 * abs(s) , 0.02, 0.025, 0.07, BEADS[(1 + abs(s)) % 4]) for s in (-2, -1, 0, 1, 2)]
    b += [Bx(0.145, 0, 1.30, 0.03, 0.04, 0.05, BEADS[3])]
    return b
def beltbeads():     # the breechcloth's belt: a row of beads
    return [Bx(0.142, -0.15 + 0.05 * i, 0.93, 0.012, 0.03, 0.03, BEADS[i % 4]) for i in range(7)]

def warrior():       # the whole look: long black hair + a braid each side, headband + feathers, face paint, buckskin + yoke, breechcloth + leggings, ornaments
    return (braids() + headband() + facepaint() + buckskin() + yoke() + hairpipe() + armbands() + necklace() + breechcloth() + beltbeads())
def warrior_yoke_only():
    return braids() + headband() + facepaint() + buckskin() + yoke() + armbands() + necklace() + breechcloth() + beltbeads()

# ── Afghan ──
PERAHAN, TUNBAN_C, VEST, PAKOL_C, LUNGEE, PATU_C, CHADARI_C, GRILLE = "#E8E4D6", "#8A8576", "#262629", "#B9A57F", "#F0EEE8", "#6B5B45", "#6FA0DA", "#33415A"
def perahan(c=None):       # a knee-length tunic: collar, front and back panels (the SIDES left open: the slits), long sleeves = the arm colour
    c = c or PERAHAN
    return [Bx(0, 0, 1.20, 0.27, 0.43, 0.50, c), Bx(0, 0, 1.455, 0.22, 0.30, 0.045, shade(c, 0.8)),
            Bx(0.145, 0, 0.72, 0.03, 0.42, 0.46, c), Bx(-0.145, 0, 0.72, 0.03, 0.42, 0.46, c), Bx(0.15, 0, 0.93, 0.03, 0.43, 0.025, shade(c, 0.85))]
def tunban(tc=None):        # very loose trousers: a baggy sleeve round each leg, gathered at the ankle
    tc = tc or TUNBAN_C
    b = []
    for s in (1, -1): b += [Bx(0, 0.125 * s, 0.52, 0.29, 0.25, 0.80, tc), Bx(0, 0.125 * s, 0.07, 0.17, 0.17, 0.05, shade(tc, 0.6))]   # +50 % wide, deeper, full to the shin; a narrow cuff at the ankle
    return b
def waistcoat():     # a dark vest over the tunic: two front panels (open between them), a back panel, two straps
    return [Bx(0.145, 0.115, 1.22, 0.03, 0.17, 0.42, VEST), Bx(0.145, -0.115, 1.22, 0.03, 0.17, 0.42, VEST), Bx(-0.145, 0, 1.22, 0.03, 0.42, 0.42, VEST),
            Bx(0, 0.15, 1.45, 0.25, 0.05, 0.05, VEST), Bx(0, -0.15, 1.45, 0.25, 0.05, 0.05, VEST)]
def pakol():         # the flat round rolled-brim wool cap: a thick rolled rim and a flat top disc a little wider
    return [Bx(0, 0, 1.69, 0.235, 0.225, 0.085, PAKOL_C), Bx(0, 0, 1.745, 0.25, 0.24, 0.04, shade(PAKOL_C, 0.9)), Bx(0, 0, 1.665, 0.24, 0.23, 0.02, shade(PAKOL_C, 0.7))]
def lungee(c=None):        # a large wrapped turban, wider and taller than the head, with a loose tail at the back
    c = c or LUNGEE
    return [Bx(0, 0, 1.71, 0.27, 0.27, 0.09, c), Bx(0, 0, 1.79, 0.26, 0.28, 0.08, shade(c, 0.93)), Bx(0, 0, 1.865, 0.235, 0.25, 0.07, c),
            Bx(0.136, 0, 1.75, 0.012, 0.26, 0.03, "#9A9AA2" if sum(int(c.lstrip('#')[i:i+2],16) for i in (0,2,4)) < 200 else "#2B2B30"), Bx(-0.165, 0.05, 1.45, 0.05, 0.11, 0.50, c), Bx(-0.168, 0.05, 1.22, 0.052, 0.112, 0.03, "#9A9AA2" if sum(int(c.lstrip('#')[i:i+2],16) for i in (0,2,4)) < 200 else "#2B2B30")]
def patu():          # a wool shawl over the shoulders, down the back and over one shoulder to the front
    return [Bx(0, 0, 1.40, 0.31, 0.47, 0.10, PATU_C), Bx(-0.15, 0, 1.12, 0.04, 0.42, 0.62, PATU_C), Bx(0.15, 0.13, 1.18, 0.04, 0.17, 0.52, PATU_C),
            Bx(-0.153, 0, 0.84, 0.042, 0.43, 0.04, shade(PATU_C, 0.6)), Bx(0.153, 0.13, 0.95, 0.042, 0.17, 0.04, shade(PATU_C, 0.6))]
def chadari():       # the women's full-body pleated cover: crown to ankle, a dark mesh grille over the eyes, the face fully covered
    b = [Bx(0, 0, 1.715, 0.27, 0.265, 0.13, CHADARI_C), Bx(0, 0, 1.54, 0.27, 0.265, 0.17, CHADARI_C), Bx(0, 0.135, 1.625, 0.21, 0.04, 0.06, CHADARI_C), Bx(0, -0.135, 1.625, 0.21, 0.04, 0.06, CHADARI_C),
         Bx(-0.135, 0, 1.625, 0.05, 0.265, 0.06, CHADARI_C), Bx(0.137, 0, 1.625, 0.012, 0.21, 0.06, GRILLE),
         Bx(0, 0, 1.38, 0.33, 0.50, 0.14, CHADARI_C), Bx(0, 0, 0.62, 0.34, 0.52, 0.62, CHADARI_C), Bx(0, 0, 0.17, 0.38, 0.58, 0.32, CHADARI_C)]
    b += [Bx(0.19, -0.18 + 0.06 * i, 0.60, 0.012, 0.012, 1.05, shade(CHADARI_C, 0.78)) for i in range(7)]
    return b
def chadari_fig(): return chadari()
PALEBLUE, OLIVEP, BLACKT = "#A9B8C8", "#6B6E4C", "#232326"
def afghan_man():    return perahan(PALEBLUE) + tunban() + waistcoat() + pakol()
def afghan_elder():  return perahan() + tunban() + patu() + lungee()
def afghan_fighter():return perahan(OLIVEP) + tunban("#5A5C42") + chestrig() + patu() + lungee(BLACKT)
def afghan_boy():    return perahan() + tunban() + pakol()
def afghan_girl():   return [Bx(0, 0, 1.20, 0.27, 0.43, 0.50, "#E36A8C"), Bx(0.145, 0, 0.72, 0.03, 0.42, 0.46, "#E36A8C"), Bx(-0.145, 0, 0.72, 0.03, 0.42, 0.46, "#E36A8C")] + tunban() + hijab_color("#3E9A8F")
def hijab_color(c):
    return [Bx(0, 0, 1.715, 0.25, 0.24, 0.10, c), Bx(0.125, 0, 1.685, 0.03, 0.22, 0.05, c), Bx(0, 0.125, 1.58, 0.22, 0.04, 0.30, c), Bx(0, -0.125, 1.58, 0.22, 0.04, 0.30, c),
            Bx(-0.13, 0, 1.55, 0.05, 0.26, 0.34, c), Bx(0, 0, 1.38, 0.30, 0.46, 0.12, c), Bx(-0.135, 0, 1.28, 0.04, 0.34, 0.34, c)]

G_NA = [
 ("warrior", "Native American warrior — the whole look", "black hair, a braid each side, headband + feathers, face paint, buckskin + yoke, breastplate, armbands, necklace, beaded belt", warrior, BUCK, "legging"),
 ("na-yoke", "Beaded yoke (close-up)", "a band of small coloured squares — blue, red, white, yellow — across the chest and over the shoulders of the buckskin, front and back", lambda: buckskin() + yoke() + braids(), BUCK, "legging", (250, 482)),
 ("na-breastplate", "Hair-pipe breastplate (close-up)", "a bone breastplate: six vertical rows of white tubes between two leather bars, over the shirt (worn over or instead of the yoke, by the seed)", lambda: buckskin() + hairpipe() + braids(), BUCK, "legging", (250, 482)),
 ("na-armbands", "Armbands (close-up)", "a red band with a blue stripe round each upper arm", lambda: buckskin() + armbands() + braids(), BUCK, "legging", (250, 482)),
 ("na-necklace", "Bead necklace (close-up)", "a white bead collar round the neck, a strand of coloured beads on the chest, a yellow pendant", lambda: buckskin() + necklace() + braids(), BUCK, "legging", (250, 482)),
]
G_AF = [
 ("af-man", "Afghan man — perahan tunban, waistcoat, pakol", "a pale blue-grey knee-length tunic, open sides, over BAGGY trousers (+50 % wide) gathered in a narrow cuff at the ankle; dark waistcoat; pakol", afghan_man, PALEBLUE, "baggy"),
 ("af-perahan", "Perahan tunban", "the long shirt to the knee (side slits left open) over very baggy trousers gathered at the ankle", lambda: perahan() + tunban(), PERAHAN, "baggy"),
 ("af-waistcoat", "Waistcoat", "a dark vest over the tunic: two front panels with the shirt showing between, a back panel, two straps", lambda: perahan() + tunban() + waistcoat(), PERAHAN, "baggy"),
 ("af-pakol", "Pakol", "the flat round wool cap with a thick rolled rim and a flat top, a little wider than the head", lambda: perahan() + tunban() + pakol(), PERAHAN, "baggy"),
 ("af-lungee", "Lungee turban", "a large wrapped turban, wider and taller than the head, layer on layer, a loose tail hanging down the back", lambda: perahan() + tunban() + lungee(), PERAHAN, "baggy"),
 ("af-patu", "Patu", "a wool shawl over the shoulders, down the back and over one shoulder to the front, a dark hem", lambda: perahan() + tunban() + patu(), PERAHAN, "baggy"),
 ("af-chadari", "Chadari (burqa)", "the women's full-body pleated cover from crown to ankle in the iconic light blue, a dark mesh grille over the eyes, the face fully covered", chadari, CHADARI_C, "hidden"),
 ("af-elder", "Afghan elder — turban, patu", "perahan tunban, a large lungee with its tail, a patu round the shoulders (and a long beard, by the face shader)", afghan_elder, PERAHAN, "baggy"),
 ("af-fighter", "Afghan fighter — turban, chest rig, patu", "an olive tunic and baggy trousers, a BLACK lungee turban, an ammunition chest rig, a patu over the shoulders (turbans: black, dark grey or white)", afghan_fighter, OLIVEP, "baggy"),
 ("af-boy", "Afghan boy — small perahan tunban, pakol", "the same dress at a boy's size (the figure's height scales it)", afghan_boy, PERAHAN, "baggy"),
 ("af-girl", "Afghan girl — bright dress, trousers, scarf", "a bright dress over loose trousers and a coloured headscarf", afghan_girl, "#E36A8C", "baggy"),
]

def figure(torso, legs):
    sleeve = torso if torso != SKIN else SKIN
    lcol = {"hidden": torso, "trousers": DARKBASE, "bare": SKIN, "legging": BUCKD, "baggy": "#4E4A40"}[legs]
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
    name, title, ref, fn, torso, legs = g[:6]
    zoom = g[6] if len(g) > 6 else None   # (k, baseline offset): a close-up of the upper body, clipped to the tile
    if zoom: k = zoom[0]
    svg.rect(x0, y0, 440, 292, C.BG, stroke=C.BORDER)
    svg.text(x0 + 10, y0 + 18, title, "#E8E8EE", 13, weight="bold")
    base = figure(torso, legs); over = fn()
    gx = y0 + 266
    if zoom:
        gz = zoom[1]
        svg.raw(f"<svg x='{x0}' y='{y0 + 50}' width='440' height='232' overflow='hidden'>")
        draw_view(svg, 110, gz, base + over, "front", k); draw_view(svg, 320, gz, base + over, "side", k)
        svg.raw("</svg>")
        svg.text(x0 + 110, y0 + 284, "front (close-up)", C.NOTE, 10.5, "middle"); svg.text(x0 + 320, y0 + 284, "side (close-up)", C.NOTE, 10.5, "middle")
    else:
        draw_view(svg, x0 + 110, gx, base + over, "front", k)
        draw_view(svg, x0 + 320, gx, base + over, "side", k)
        svg.text(x0 + 110, y0 + 280, "front", C.NOTE, 10.5, "middle"); svg.text(x0 + 320, y0 + 280, "side", C.NOTE, 10.5, "middle")
    # the reference, wrapped
    words, line, ly = ref.split(), "", y0 + 36
    for w in words:
        if len(line) + len(w) > 62: svg.text(x0 + 10, ly, line, C.NOTE, 10.5); line, ly = "", ly + 13
        line = (line + " " + w).strip()
    svg.text(x0 + 10, ly, line, C.NOTE, 10.5)
    if not zoom: svg.line(x0 + 10, gx, x0 + 430, gx, C.SEP, 1, "3 3")

def single(g):
    s = Svg(440, 292, None); tile(s, 0, 0, g)
    return s

def sheet_of(groups, title, sub, cols=3):
    rows = (len(groups) + cols - 1) // cols
    W, H = 20 + cols * 450, 64 + rows * 302 + 40
    s = Svg(W, H, title, sub)
    for i, g in enumerate(groups): tile(s, 10 + (i % cols) * 450, 60 + (i // cols) * 302, g)
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
    SUB = "Scope (R-4151): ONLY the garment boxes. The figure under them is the existing block figure, drawn for context and unchanged. Annotations in grey are not drawn by the app."
    outs = [("person-outfits-sheet", sheet())] + [(f"person-outfits-{g[0]}", single(g)) for g in G]
    outs += [("person-outfits-native-sheet", sheet_of(G_NA, "Native American warrior — the whole look and its ornaments", SUB, 2))] + [(f"person-outfits-{g[0]}", single(g)) for g in G_NA]
    outs += [("person-outfits-afghan-sheet", sheet_of(G_AF, "Afghan dress — civilians and fighters", SUB, 3))] + [(f"person-outfits-{g[0]}", single(g)) for g in G_AF]
    for name, s in outs:
        p = os.path.join(OUT, name + ".svg"); s.save(p)
        if png: os.makedirs(png, exist_ok=True); render(p, os.path.join(png, name + ".png"), s.w, s.h)
