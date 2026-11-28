# Generates docs/designs/devices/mockups/device-looks-visual.svg — SD-S5d: a device DRAWN as itself in its forms (DESIGN_Simulated_Devices.md §3.10a,
# D-163–D-167), house style (R-4143). Regenerate: python3 scripts/gen-device-looks-mockup.py docs/designs/devices/mockups/device-looks-visual.svg
# Scope: the six device kinds' block shapes and the forms (raised in his hands · at a vehicle station · stacked). The man, the HMMWV and the ground are
# CONTEXT drawn as the 3-D map's block figures already are (unchanged, R-4151). Grey annotations are not drawn by the app.
import sys
from mockup_kit import Svg, C

W, H = 1400, 900
s = Svg(W, H, "Devices drawn as themselves — the six kinds and their forms (Simulated Devices §3.10a, SD-S5d)",
        "Scope: the device shapes and where they sit. The man, the HMMWV, the ground are context, as the 3-D map draws them. Grey annotations are not drawn by the app.")

PX = 350   # pixels per metre for the kind sheet (one scale for all six, so their sizes compare)
OLIVE, DARK, TAN, GREY, LENS, BLACK = "#5E6646", "#2A2C2A", "#B79C70", "#4A4E55", "#7FB2D6", "#1A1A1A"


def box(x, y, w, h, fill, stroke="#111", rx=2):
    s.rect(x, y, w, h, fill, stroke=stroke, rx=rx, sw=1)


# each kind: (name, size L×W×H m, side-view drawer(x0, ybase), front-view drawer(x0, ybase), note)
def binoculars_side(x, yb):
    L, Hh = 0.14 * PX, 0.07 * PX
    box(x, yb - Hh, L, Hh, DARK); box(x + L - 6, yb - Hh * 0.8, 8, Hh * 0.6, LENS, rx=1)            # barrel + objective
    box(x - 8, yb - Hh * 0.7, 10, Hh * 0.4, BLACK)                                                   # eyepiece


def binoculars_front(x, yb):
    Wd, Hh = 0.17 * PX, 0.07 * PX
    for dx in (0, Wd * 0.58):
        s.raw(f"<circle cx='{x + dx + Wd * 0.21}' cy='{yb - Hh / 2}' r='{Hh / 2}' fill='{DARK}' stroke='#111'/>")
        s.raw(f"<circle cx='{x + dx + Wd * 0.21}' cy='{yb - Hh / 2}' r='{Hh / 3.2}' fill='{LENS}'/>")
    box(x + Wd * 0.36, yb - Hh * 0.65, Wd * 0.28, Hh * 0.3, DARK)                                  # the bridge


def rangefinder_side(x, yb):
    L, Hh = 0.20 * PX, 0.08 * PX
    box(x, yb - Hh, L, Hh, OLIVE); box(x + L - 6, yb - Hh * 0.85, 8, Hh * 0.35, LENS, rx=1)         # body + receiver
    box(x + L * 0.55, yb - Hh - 0.025 * PX, L * 0.45, 0.025 * PX, OLIVE)                             # the emitter housing on top
    box(x + L - 5, yb - Hh - 0.022 * PX, 6, 0.018 * PX, "#C0392B", rx=1)                            # the emitter aperture
    box(x - 10, yb - Hh * 0.7, 12, Hh * 0.4, BLACK)


def rangefinder_front(x, yb):
    Wd, Hh = 0.18 * PX, 0.08 * PX
    box(x, yb - Hh, Wd, Hh, OLIVE)
    for dx in (0.25, 0.75): s.raw(f"<circle cx='{x + Wd * dx}' cy='{yb - Hh / 2}' r='{Hh / 3}' fill='{LENS}' stroke='#111'/>")
    box(x + Wd * 0.4, yb - Hh - 0.025 * PX, Wd * 0.2, 0.025 * PX, OLIVE)
    s.raw(f"<circle cx='{x + Wd * 0.5}' cy='{yb - Hh - 0.0125 * PX}' r='5' fill='#C0392B'/>")


def designator_side(x, yb):
    L, Hh = 0.40 * PX, 0.18 * PX
    box(x, yb - Hh, L, Hh, TAN); box(x + L - 8, yb - Hh * 0.85, 10, Hh * 0.6, LENS, rx=2)          # the big front aperture
    box(x - 12, yb - Hh * 0.65, 14, Hh * 0.3, BLACK); box(x + L * 0.3, yb - Hh - 10, L * 0.25, 10, DARK)   # eyepiece, handle


def designator_front(x, yb):
    Wd, Hh = 0.16 * PX, 0.18 * PX
    box(x, yb - Hh, Wd, Hh, TAN); s.raw(f"<circle cx='{x + Wd / 2}' cy='{yb - Hh / 2}' r='{Wd * 0.33}' fill='{LENS}' stroke='#111'/>")


def gcs_side(x, yb):
    L, Hh = 0.05 * PX, 0.20 * PX
    box(x, yb - Hh, L, Hh, GREY); box(x + L - 4, yb - Hh * 0.92, 4, Hh * 0.7, LENS, rx=1)
    box(x + L * 0.4, yb - Hh - 0.10 * PX, 4, 0.10 * PX, BLACK)                                      # the antenna


def gcs_front(x, yb):
    Wd, Hh = 0.30 * PX, 0.20 * PX
    box(x, yb - Hh, Wd, Hh, GREY); box(x + Wd * 0.25, yb - Hh * 0.9, Wd * 0.5, Hh * 0.6, LENS, rx=2)   # the screen
    for dx in (0.12, 0.88): s.raw(f"<circle cx='{x + Wd * dx}' cy='{yb - Hh * 0.35}' r='8' fill='{BLACK}'/>")   # the two sticks
    box(x + Wd * 0.85, yb - Hh - 0.10 * PX, 4, 0.10 * PX, BLACK)


def console_side(x, yb):
    L, Hh = 0.12 * PX, 0.25 * PX
    box(x, yb - Hh, L, Hh, DARK); box(x + L - 4, yb - Hh * 0.95, 4, Hh * 0.55, LENS, rx=1)


def console_front(x, yb):
    Wd, Hh = 0.35 * PX, 0.25 * PX
    box(x, yb - Hh, Wd, Hh, DARK); box(x + Wd * 0.08, yb - Hh * 0.92, Wd * 0.62, Hh * 0.55, LENS, rx=2)
    for r in range(3):
        for c in range(2): box(x + Wd * 0.76 + c * 14, yb - Hh * 0.9 + r * 16, 10, 10, "#888", rx=1)   # the keys
    box(x + Wd * 0.1, yb - Hh * 0.28, Wd * 0.8, Hh * 0.16, "#3A3D40")


def box_side(x, yb):
    box(x, yb - 0.15 * PX, 0.30 * PX, 0.15 * PX, OLIVE); box(x + 0.30 * PX - 4, yb - 0.12 * PX, 4, 0.04 * PX, "#888")


def box_front(x, yb):
    Wd, Hh = 0.20 * PX, 0.15 * PX
    box(x, yb - Hh, Wd, Hh, OLIVE)
    for i in range(3): s.raw(f"<circle cx='{x + Wd * (0.25 + 0.25 * i)}' cy='{yb - Hh * 0.6}' r='5' fill='#222' stroke='#888'/>")   # the switches


KINDS = [
    ("Binoculars", "0.14 × 0.17 × 0.07 m", binoculars_side, binoculars_front, "type 641 · twin barrels, dark"),
    ("Rangefinder", "0.20 × 0.18 × 0.10 m", rangefinder_side, rangefinder_front, "type 640 · olive body, the emitter on top"),
    ("Designator", "0.40 × 0.16 × 0.18 m", designator_side, designator_front, "no type yet · tan, the big aperture"),
    ("DroneController", "0.05 × 0.30 × 0.20 m", gcs_side, gcs_front, "no type yet · tablet, sticks, antenna"),
    ("Console", "0.12 × 0.35 × 0.25 m", console_side, console_front, "no type yet · screen + keys (RWS)"),
    ("Box", "0.30 × 0.20 × 0.15 m", box_side, box_front, "the default · switches (9K11 box)"),
]
CW, X0, Y0 = 220, 30, 90
for i, (name, size, side, front, note) in enumerate(KINDS):
    x = X0 + i * (CW + 8)
    s.rect(x, Y0, CW, 300, "#202530", stroke=C.BORDER)
    s.text(x + CW / 2, Y0 + 22, name, C.TXT, 13.5, "middle", "bold")
    s.text(x + CW / 2, Y0 + 40, size, C.SUB, 11, "middle")
    s.text(x + 12, Y0 + 66, "side →", C.NOTE, 10.5); side(x + 36, Y0 + 170)
    s.text(x + 12, Y0 + 196, "front", C.NOTE, 10.5); front(x + 36, Y0 + 280)
    s.note(x, Y0 + 316, [note], C.SUB, 10.5)

# ── the forms ────────────────────────────────────────────────────────────────────────────────────────────────────────────
FY = 470


def man(x, yb, raised=None):
    # block figure (context): legs, torso, head; the arms to the face when a device is raised
    s.rect(x - 9, yb - 90, 18, 90, "#4B5233"); s.rect(x - 13, yb - 160, 26, 72, "#566040", rx=3)
    s.raw(f"<circle cx='{x}' cy='{yb - 174}' r='12' fill='#C9A27E'/>")
    if raised:
        s.line(x + 4, yb - 150, x + 26, yb - 168, "#566040", 7); s.line(x - 4, yb - 150, x + 22, yb - 172, "#4B5233", 7)
        raised(x + 18, yb - 160)
    else:
        s.line(x + 10, yb - 152, x + 14, yb - 100, "#566040", 7)


s.rect(30, FY, 430, 300, "#202530", stroke=C.BORDER)
s.text(42, FY + 22, "① RAISED in his hands — while his view shows the device", C.TXT, 12.5, "start", "bold")
s.rect(31, FY + 240, 428, 59, "#33402A")
man(130, FY + 240, lambda x, y: (box(x, y - 12, 0.20 * 260, 0.08 * 260, OLIVE), box(x + 0.20 * 260 * 0.55, y - 12 - 7, 0.09 * 260, 7, OLIVE)))
s.text(130, FY + 262, "rangefinder (640)", C.SUB, 11, "middle")
man(330, FY + 240, lambda x, y: (box(x, y - 10, 0.14 * 260, 0.07 * 260, DARK),))
s.text(330, FY + 262, "binoculars (641)", C.SUB, 11, "middle")
s.note(42, FY + 280, ["not raised ⇒ not drawn (in his kit, D-64)"], C.NOTE, 10.5)

s.rect(480, FY, 440, 300, "#202530", stroke=C.BORDER)
s.text(492, FY + 22, "② MOUNTED at a station — observer HMMWV (120), station 7", C.TXT, 12.5, "start", "bold")
s.rect(481, FY + 240, 438, 59, "#33402A")
hx, hy = 560, FY + 240
s.rect(hx, hy - 70, 300, 52, "#5E6646", rx=6); s.rect(hx + 170, hy - 104, 90, 36, "#5E6646", rx=4); s.rect(hx + 180, hy - 98, 40, 18, LENS)   # body, cab, glass (context)
for wx in (40, 250): s.raw(f"<circle cx='{hx + wx}' cy='{hy - 16}' r='16' fill='#222'/>")
# the station's ring and the rangefinder on it, turned toward where the trainee looks
s.rect(hx + 110, hy - 112, 36, 8, "#3D4230")
s.raw(f"<g transform='rotate(-14 {hx + 128} {hy - 118})'>")
box(hx + 112, hy - 130, 0.20 * 180, 0.08 * 180, OLIVE); box(hx + 112 + 0.20 * 180 * 0.55, hy - 130 - 5, 0.09 * 180, 5, OLIVE)
s.raw("</g>")
s.callout(hx + 150, hy - 128, hx + 170, hy - 175, ["at the station place, turned", "by its DevicePose (his look)"])

s.rect(940, FY, 430, 300, "#202530", stroke=C.BORDER)
s.text(952, FY + 22, "③ STACKED on another device — waits for SD-S5b", C.TXT, 12.5, "start", "bold")
s.rect(941, FY + 240, 428, 59, "#33402A")
tx, ty = 1150, FY + 240
s.line(tx, ty - 110, tx - 50, ty, "#444", 4); s.line(tx, ty - 110, tx + 50, ty, "#444", 4); s.line(tx, ty - 110, tx, ty, "#444", 4)   # a stand (context)
box(tx - 0.40 * 200 / 2, ty - 110 - 0.18 * 200, 0.40 * 200, 0.18 * 200, TAN)                      # a designator on it
box(tx - 0.20 * 200 / 2, ty - 110 - 0.18 * 200 - 0.08 * 200, 0.20 * 200, 0.08 * 200, OLIVE)       # a rangefinder stacked on top
s.note(952, FY + 262, ["at its host's stack place (StackOffset), turning with the host"], C.NOTE, 10.5)

s.note(30, FY + 316, ["Every form reads the place from DevicePoses.TryWorld (D-166); the kind from DeviceLooks.KindOf — TKB data, DeviceDefDto.Look, defaulted by the behaviour (D-163).",
                      "Later, the same kits: ④ on its own tripod (D-60), ⑤ lying where it was dropped (SD-S5) — not built (D-165)."], C.SUB)
s.save(sys.argv[1])
