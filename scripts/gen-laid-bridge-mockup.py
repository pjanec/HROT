# Generates docs/UX/laid-bridge-visual.svg — what a LAID bridge looks like (DESIGN_Bridge_Laying.md §13.4), house style (R-4143).
# Regenerate: python3 scripts/gen-laid-bridge-mockup.py docs/UX/laid-bridge-visual.svg
# Scope: the bridge entity's DETAIL parts (girders, hinge, treadways; pontoons, bay joints, curbs). The deck slab and its ramps are the TERRAIN's (unchanged).
import sys
from mockup_kit import Svg, C
W, H = 1320, 560
s = Svg(W, H, "Laid bridges — the deck stays the terrain's; the entity draws the bridge on it (house style; Bridge Laying §13.4)",
        "Scope: the detail parts (coloured). The grey deck slab and ramps are drawn by the terrain, unchanged (context). Grey annotations are not drawn by the app.")
WATER, GROUND, DECK, STEEL, DARK, PONT = "#2C5C8A", "#3E5A33", "#8C8C8C", "#6F7466", "#3A3D34", "#5E6B3A"

def side(x0, y0, w, label, sub):
    s.rect(x0, y0, w, 200, "#202530", stroke=C.BORDER)
    s.text(x0 + 12, y0 + 22, label, "#E8E8EE", 12.5)
    s.text(x0 + 12, y0 + 190, sub, C.NOTE, 11)

# ── ① assault bridge, side view ────────────────────────────────────────────────
X, Y = 20, 70
side(X, Y, 630, "① Assault bridge (8825) — side view, 26 m deck, 1.2 m above the banks", "girders + mid-span hinge + end plates on the terrain's deck; ramps stay the terrain's")
gy = Y + 140
s.rect(X + 10, gy, 120, 40, GROUND); s.rect(X + 500, gy, 120, 40, GROUND); s.rect(X + 130, gy + 14, 370, 26, WATER)
s.raw(f"<path d='M{X+40} {gy} L{X+110} {gy-24} L{X+520} {gy-24} L{X+590} {gy}' stroke='{DECK}' stroke-width='5' fill='none'/>")   # the terrain's deck + ramps
s.rect(X + 110, gy - 40, 410, 16, STEEL, stroke=DARK)                                       # the side girder
s.rect(X + 309, gy - 44, 12, 34, DARK)                                                       # the hinge
s.rect(X + 106, gy - 44, 8, 30, DARK); s.rect(X + 516, gy - 44, 8, 30, DARK)              # end plates
s.text(X + 315, gy - 52, "scissor hinge", C.SUB, 11, "middle")

# ── ② floating bridge, side view ───────────────────────────────────────────────
X2 = 670
side(X2, Y, 630, "② Floating bridge (8826) — side view, 0.6 m above the water", "pontoon bays in the water under the deck, a joint between bays, low curbs")
gy2 = Y + 140
s.rect(X2 + 10, gy2, 70, 40, GROUND); s.rect(X2 + 550, gy2, 70, 40, GROUND); s.rect(X2 + 80, gy2 + 4, 470, 36, WATER)
s.raw(f"<path d='M{X2+20} {gy2} L{X2+70} {gy2-12} L{X2+560} {gy2-12} L{X2+610} {gy2}' stroke='{DECK}' stroke-width='5' fill='none'/>")
for k in range(10):
    bx = X2 + 85 + k * 46
    s.rect(bx, gy2 - 8, 42, 18, PONT, stroke=DARK)                                          # a pontoon bay, half in the water
    s.rect(bx + 43, gy2 - 16, 3, 8, DARK)                                                    # the joint
s.rect(X2 + 70, gy2 - 19, 490, 5, STEEL)                                                     # the curb

# ── ③ both from above ──────────────────────────────────────────────────────────
X3, Y3 = 20, 300
s.rect(X3, Y3, 1280, 220, "#202530", stroke=C.BORDER)
s.text(X3 + 12, Y3 + 22, "③ from above — the treadways (assault) and the bays (floating) read as a bridge, not a slab", "#E8E8EE", 12.5)
s.rect(X3 + 20, Y3 + 40, 600, 160, GROUND); s.rect(X3 + 20, Y3 + 95, 600, 50, WATER)
s.rect(X3 + 150, Y3 + 105, 340, 30, DECK)
s.rect(X3 + 150, Y3 + 105, 340, 4, STEEL); s.rect(X3 + 150, Y3 + 131, 340, 4, STEEL)          # girders
s.rect(X3 + 150, Y3 + 117, 340, 6, DARK)                                                       # the gap between the two treadways
s.rect(X3 + 318, Y3 + 103, 4, 34, DARK)
s.rect(X3 + 660, Y3 + 40, 600, 160, GROUND); s.rect(X3 + 700, Y3 + 60, 520, 120, WATER)
s.rect(X3 + 690, Y3 + 105, 540, 30, DECK)
for k in range(10):
    s.rect(X3 + 712 + k * 50, Y3 + 101, 46, 38, PONT, op=0.55)
    s.rect(X3 + 758 + k * 50, Y3 + 105, 3, 30, DARK)
s.rect(X3 + 690, Y3 + 105, 540, 3, STEEL); s.rect(X3 + 690, Y3 + 132, 540, 3, STEEL)
s.save(sys.argv[1])
