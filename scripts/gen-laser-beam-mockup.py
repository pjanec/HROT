# Generates docs/designs/devices/mockups/laser-beam-visual.svg — SD-S6a: the laser beam and its spots on the 3-D map, per band and per sensor view
# (DESIGN_Simulated_Devices.md §3.8a, D-158–D-162), house style (R-4143).
# Regenerate: python3 scripts/gen-laser-beam-mockup.py docs/designs/devices/mockups/laser-beam-visual.svg
# Scope: the BEAM (a thin additive line, its scatter off haze) and the SPOT glow(s) only. The scene (ground, wall, man, sky) is CONTEXT drawn as the
# three existing views already draw it (unchanged, R-4151). Grey annotations are not drawn by the app.
import sys
from mockup_kit import Svg, C

W, H = 1460, 940
s = Svg(W, H, "Laser beam and spot on the 3-D map — who sees which band (Simulated Devices §3.8a, SD-S6a)",
        "Scope: the beam line and the spot glow. The scene is context, as each view already draws it. Grey annotations are not drawn by the app.")

COLS = [("Naked eye", "eye"), ("Night vision", "nv"), ("Thermal", "th")]
ROWS = [("Band 0 — visible (532 nm pointer)", 0), ("Band 2 — designator (1 064 nm)", 2), ("Band 3 — near-IR pointer / illuminator (850 nm)", 3)]
CW, CH, X0, Y0, GX, GY = 380, 200, 240, 96, 24, 40

# the scene per view (context): sky, ground, the wall the spot lands on, the man holding the device
SCENE = {
    "eye": dict(sky="#1A2236", ground="#20261C", wall="#3A3A40", man="#2C3326"),
    "nv":  dict(sky="#0C1A0C", ground="#16301A", wall="#2E5A34", man="#1E4222"),
    "th":  dict(sky="#202020", ground="#3C3C3C", wall="#555555", man="#F2F2F2"),
}
BEAM = {0: "#3CFF6A"}       # band 0 drawn in its own colour to the eye; every NV draw is white (SceneSensors.Emissive)
# intensity per band × sensor (D-159): eye / nv / thermal
TABLE = {0: (1.0, 1.0, 0.0), 1: (0.0, 0.0, 0.0), 2: (0.0, 0.25, 0.0), 3: (0.0, 1.0, 0.0)}


def cell(x, y, view, band, haze=0.45):
    sc = SCENE[view]
    s.rect(x, y, CW, CH, sc["sky"], stroke=C.BORDER)
    s.rect(x + 1, y + CH * 0.58, CW - 2, CH * 0.42 - 1, sc["ground"])
    s.rect(x + CW - 90, y + 30, 50, CH * 0.58 - 30 + 18, sc["wall"])                                  # the wall
    mx, my = x + 46, y + CH * 0.58 + 10
    s.rect(mx - 6, my - 34, 12, 34, sc["man"], rx=3); s.raw(f"<circle cx='{mx}' cy='{my - 40}' r='6' fill='{sc['man']}'/>")   # the man
    ox, oy = mx + 8, my - 26                                                                              # the device's muzzle
    sx, sy = x + CW - 90, y + 74                                                                          # the spot, on the wall
    i = {"eye": 0, "nv": 1, "th": 2}[view]
    k = TABLE[band][i]
    if k > 0:
        col = BEAM.get(band, "#FFFFFF") if view == "eye" else "#FFFFFF"
        # the beam: its scatter, fading along the line (exp(-d / visibility)) — drawn as a gradient on an additive line
        gid = f"g{view}{band}{x}{y}"
        a0, a1 = min(1.0, k * haze * 1.6), min(1.0, k * haze * 0.9)
        s.raw(f"<defs><linearGradient id='{gid}' x1='{ox}' y1='{oy}' x2='{sx}' y2='{sy}' gradientUnits='userSpaceOnUse'>"
              f"<stop offset='0' stop-color='{col}' stop-opacity='{a0:.2f}'/><stop offset='1' stop-color='{col}' stop-opacity='{a1:.2f}'/></linearGradient>"
              f"<radialGradient id='r{gid}'><stop offset='0' stop-color='{col}' stop-opacity='{min(1, k):.2f}'/>"
              f"<stop offset='0.35' stop-color='{col}' stop-opacity='{min(1, k) * 0.55:.2f}'/><stop offset='1' stop-color='{col}' stop-opacity='0'/></radialGradient></defs>")
        s.raw(f"<line x1='{ox}' y1='{oy}' x2='{sx}' y2='{sy}' stroke='url(#{gid})' stroke-width='{1.6 if view == 'eye' else 2.2}'/>")
        r = 9 + 9 * k
        s.raw(f"<circle cx='{sx}' cy='{sy}' r='{r:.1f}' fill='url(#r{gid})'/>")
        if view == "nv": s.raw(f"<circle cx='{sx}' cy='{sy}' r='{r * 2.2:.1f}' fill='url(#r{gid})' opacity='0.35'/>")   # the tube's bloom
    else:
        s.text(x + CW / 2, y + CH - 14, "no beam, no spot", C.NOTE, 11, "middle")
    if view == "nv": s.rect(x, y, CW, CH, "none", stroke="#3B6E3F")


for c, (name, view) in enumerate(COLS):
    s.text(X0 + c * (CW + GX) + CW / 2, Y0 - 12, name, C.TXT, 14, "middle", "bold")
for r, (label, band) in enumerate(ROWS):
    y = Y0 + r * (CH + GY)
    s.text(X0 - 12, y + CH / 2 - 8, label.split(" — ")[0], C.TXT, 13, "end", "bold")
    s.text(X0 - 12, y + CH / 2 + 10, label.split(" — ")[1], C.SUB, 11, "end")
    for c, (_, view) in enumerate(COLS):
        cell(X0 + c * (CW + GX), y, view, band)

# under the grid: band 1, and the haze
y = Y0 + 3 * (CH + GY) - 10
s.note(X0, y, ["Band 1 — eye-safe rangefinder (1 540 nm): drawn in NO view (D-159). Only the device's own HUD shows the range it measured."], C.SUB)
s.note(X0, y + 22, ["Thermal (8–12 µm) sees none of these bands: the thermal column draws nothing — the man is hot, the beam is not.",
                    "Band 2 under night vision is FAINT (× 0.25: the tube's tail at 1 064 nm); bands 0 and 3 are bright. Every night-vision draw is white (SceneSensors.Emissive) and blooms."], C.SUB)
s.note(X0, y + 70, ["THE BEAM is its scatter off haze: alpha = table × power × aerosol, fading along the line with exp(−d / visibility).",
                    "THE SPOT is a surface return: alpha = table × the spot's strength — it needs no haze. A Preview spot (paused) is drawn the same way."], C.SUB)
s.save(sys.argv[1])
