# Generates docs/UX/vehicle-openings-lights.svg — CE-4670: a vehicle's openings and lights on the 3-D map, and the station's H / U keys
# (DESIGN_Vehicle_Appearance_State.md §7d), house style (R-4143). Regenerate: python3 scripts/gen-vehicle-openings-lights-mockup.py <out.svg>
import sys
from mockup_kit import Svg, C, FA, text_width
W, H = 1240, 720
s = Svg(W, H, "Vehicle openings and lights on the 3-D map; the station's head-out and light keys (CE-4670, design §7d)")
HULL, TURRET, DARK, SKIN, CREW = "#5E6B4A", "#56623F", "#2E3426", "#D9A982", "#3F4A2E"

def poly(pts, fill, stroke=None, op=1):
    p = " ".join(f"{x},{y}" for x, y in pts)
    s.raw(f"<polygon points='{p}' fill='{fill}' opacity='{op}'" + (f" stroke='{stroke}' stroke-width='1'" if stroke else "") + "/>")

def glow(x, y, col, r=5):
    s.raw(f"<circle cx='{x}' cy='{y}' r='{r * 2.6}' fill='{col}' opacity='0.35'/>")
    s.raw(f"<circle cx='{x}' cy='{y}' r='{r}' fill='{col}'/>")

# ── A: an APC dismounting — the ramp half open, the commander's head out of his hatch ─────────────────
AX, AY = 20, 70
s.text(AX, AY - 8, "A — an APC dismounts (uc-convoy-hit): the ramp half open; the commander's head out of his hatch", C.SUB, 12)
s.rect(AX, AY, 580, 300, "#8FA9C4")
s.rect(AX, AY + 220, 580, 80, "#6E8A4E")
# hull (side view, front to the right)
poly([(AX + 140, AY + 210), (AX + 460, AY + 210), (AX + 500, AY + 170), (AX + 470, AY + 140), (AX + 140, AY + 140)], HULL, DARK)
for wx in range(0, 4): s.raw(f"<circle cx='{AX + 180 + wx * 85}' cy='{AY + 215}' r='20' fill='{DARK}'/>")
poly([(AX + 260, AY + 140), (AX + 380, AY + 140), (AX + 370, AY + 115), (AX + 270, AY + 115)], TURRET, DARK)
s.raw(f"<rect x='{AX + 380}' y='{AY + 122}' width='90' height='7' fill='{DARK}'/>")
# the commander's hatch lid, up ~100° about its rear edge, and his head and shoulders
poly([(AX + 296, AY + 115), (AX + 300, AY + 115), (AX + 292, AY + 85), (AX + 288, AY + 86)], TURRET, DARK)
s.rect(AX + 300, AY + 98, 30, 17, CREW, rx=3)
s.raw(f"<circle cx='{AX + 315}' cy='{AY + 88}' r='9' fill='{SKIN}'/>")
s.raw(f"<rect x='{AX + 305}' y='{AY + 77}' width='20' height='6' rx='3' fill='{CREW}'/>")
# the driver's hatch, closed (flush)
s.rect(AX + 430, AY + 138, 26, 4, DARK)
# the ramp: hinged at the rear bottom, half way down (~48°)
poly([(AX + 140, AY + 208), (AX + 140, AY + 196), (AX + 78, AY + 150), (AX + 72, AY + 158)], HULL, DARK)
s.rect(AX + 140, AY + 145, 6, 62, "#1A1D16")
s.callout(AX + 105, AY + 160, AX + 20, AY + 40, ["the ramp, hinged at the bottom: drawn at", "VehicleLook.OpeningProgress × 95°"])
s.callout(AX + 315, AY + 80, AX + 380, AY + 30, ["CrewExposed: head and shoulders,", "the lid up 100° about its rear edge"])
s.callout(AX + 443, AY + 140, AX + 400, AY + 262, ["the driver's hatch, closed: flush"])

# ── B: a T-72 at night — head and tail lit; a blacked-out one beside it ───────────────────────────────
BX, BY = 640, 70
s.text(BX, BY - 8, "B — night (va-lights-night): a Normal T-72 lit, a Blackout one dark", C.SUB, 12)
s.rect(BX, BY, 580, 300, "#0E1424")
s.rect(BX, BY + 220, 580, 80, "#1A2416")
def tank(x, lit):
    poly([(x, BY + 205), (x + 210, BY + 205), (x + 235, BY + 180), (x + 215, BY + 160), (x, BY + 160)], "#2C3324", "#11140D")
    poly([(x + 70, BY + 160), (x + 150, BY + 160), (x + 140, BY + 138), (x + 80, BY + 138)], "#262C1F", "#11140D")
    s.rect(x + 150, BY + 146, 90, 6, "#11140D")
    if lit:
        glow(x + 228, BY + 176, "#FFF4D6", 5)       # head (the near one of the pair)
        s.raw(f"<polygon points='{x + 232},{BY + 170} {x + 300},{BY + 150} {x + 300},{BY + 205}' fill='#FFF4D6' opacity='0.08'/>")
        glow(x + 3, BY + 178, "#C81414", 4)          # tail
for i, lit in enumerate([True, False]): tank(BX + 30 + i * 300, lit)
s.callout(BX + 268, BY + 176, BX + 330, BY + 40, ["light_head: warm white, a pair at the front;", "a small glowing sphere + a faint halo"])
s.callout(BX + 43, BY + 178, BX + 20, BY + 255, ["light_tail: red, a pair at the rear"])
s.callout(BX + 450, BY + 160, BX + 300, BY + 255, ["Blackout: nothing lit (OnMask 0)"])
s.note(BX + 10, BY + 290, ["⚠ the faint beam is NOT drawn (no light cones in this slice) — shown only to read the direction"], C.NOTE)

# ── C: the station's HUD block in a vehicle: the two new lines ────────────────────────────────────────
CX, CY = 20, 420
s.text(CX, CY - 8, "C — a possessed crewman (first person): the HUD block, bottom left; H and U", C.SUB, 12)
s.rect(CX, CY, 580, 280, "#2A3140")
lines = [("HEALTH 100/100", "#FFFFFF"), ("> 1 125MM 2A46  22", "#FFFFFF"), ("  2 PKT  2000", "#FFFFFF"), ("AIM SIGHT  (L: switch)", "#FFFFFF"),
         ("HATCH CLOSED  (H: head out)", "#9FD7FF"), ("LIGHTS NORMAL  (U: switch)", "#F2C033"), ("SEAT 125MM 2A46  (G: next seat)", "#FFFFFF")]
bw = max(text_width(t, 13) for t, _ in lines) + 16
s.rect(CX + 20, CY + 280 - 20 - len(lines) * 22 - 8, bw, len(lines) * 22 + 8, "#000000", op=0.6)
for i, (t, c) in enumerate(lines): s.text(CX + 28, CY + 280 - 20 - (len(lines) - i - 1) * 22 - 12, t, c, 13)
s.callout(CX + 28 + text_width(lines[4][0], 13), CY + 280 - 20 - 2 * 22 - 16, CX + 330, CY + 60,
          ["NEW: shown when a hatch serves his seat role;", "H → HEAD OUT  (H: head in) — a request to the", "vehicle's owner; the line follows the state"])
s.callout(CX + 28 + text_width(lines[5][0], 13), CY + 280 - 20 - 1 * 22 - 16, CX + 330, CY + 150,
          ["NEW: shown when the type has light groups;", "U cycles Normal → Convoy → Blackout → Off"])

# ── D: the socket rule (plan view) ───────────────────────────────────────────────────────────────────
DX, DY = 640, 420
s.text(DX, DY - 8, "D — where the sockets sit: one rule over the hull / turret / cab boxes (Hrot.Look VehicleSockets)", C.SUB, 12)
s.rect(DX, DY, 580, 280, C.PAGE, stroke=C.BORDER)
s.rect(DX + 120, DY + 70, 300, 140, HULL, stroke=DARK)          # hull, front to the right
s.rect(DX + 210, DY + 95, 120, 90, TURRET, stroke=DARK)          # turret
for (x, y, lab) in [(DX + 255, DY + 110, "commander"), (DX + 255, DY + 160, "gunner"), (DX + 380, DY + 90, "driver")]:
    s.rect(x - 10, y - 10, 20, 20, DARK, stroke="#C8C8D0"); s.text(x + 14, y + 4, lab, C.SUB, 11)
s.rect(DX + 112, DY + 95, 8, 90, "#1A1D16"); s.text(DX + 20, DY + 145, "ramp_rear", C.SUB, 11)
for y in (DY + 85, DY + 195): glow(DX + 422, y, "#FFF4D6", 4); glow(DX + 118, y, "#C81414", 3)
s.text(DX + 440, DY + 88, "light_head ×2", C.SUB, 11); s.text(DX + 20, DY + 85, "light_tail ×2", C.SUB, 11)
s.note(DX + 10, DY + 250, ["y left is UP in this plan; a turret socket turns with the turret (OnTurret)."], C.NOTE)
s.save(sys.argv[1])
