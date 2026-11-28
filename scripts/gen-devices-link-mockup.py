#!/usr/bin/env python3
"""Mockup for docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d — the DEVICE LINK on the HUD (SD-S5b, D-119): the rangefinder's SEND and the binoculars'
received-target CUE (R-4138 / R-4143). Built on scripts/mockup_kit.py and the device sheet's own helpers (scripts/gen-devices-hud-mockup.py — imported, so the
two sheets cannot draw the rangefinder differently).

    python3 scripts/gen-devices-link-mockup.py      # -> docs/designs/devices/mockups/devices-hud-link.svg

SCOPE (R-4151):
  SPECIFIES  24 — the rangefinder's SEND chip (lit) and its green SENT tag after a send; 25 — the binoculars' received-target cue OFF the view (an amber arrow
             on the eyepieces' edge toward the target, TARGET + the side + the bearing offset, the range from the binoculars, the sender and "(linked)");
             26 — the same cue IN the view (an amber diamond on the point). The cue is ONE helper (HudCue.Target) the designator (panel 16) draws too.
  CONTEXT    dashed / grey, tagged "unchanged": the compass strip, the unit lines, the rest of each device's HUD (panels 1–6). The scene is illustrative.
  PROPOSAL   every position is a proposal where the design is silent.
Panel numbers continue the device sheets (1–6, 7–11, 12–15, 16–19, 20–23).
"""
import importlib.util, io, os, sys, contextlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

_spec = importlib.util.spec_from_file_location("devhud", os.path.join(os.path.dirname(os.path.abspath(__file__)), "gen-devices-hud-mockup.py"))
H = importlib.util.module_from_spec(_spec)
with contextlib.redirect_stdout(io.StringIO()): _spec.loader.exec_module(H)   # draws (and rewrites, identically) devices-hud.svg; we borrow its helpers

PW, PH, GOLD, WHITE = H.PW, H.PH, H.GOLD, H.WHITE
W_, H_ = 1500, 560
s = Svg(W_, H_, "Simulated devices — the DEVICE LINK on the HUD: SEND and the received-target cue  (SD-S5b, DESIGN_Simulated_Devices §3.13d D-119)",
        "Full colour = SPECIFIED · grey dashed + 'unchanged' = CONTEXT · the scene is illustrative · the cue is the one helper the designator's panel 16 draws too.")
H.s = s   # the helpers draw on THIS sheet
s.raw("<defs><linearGradient id='sky' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#5B87B8'/><stop offset='1' stop-color='#BFD3E3'/></linearGradient>"
      "<linearGradient id='grass' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#6B7E4A'/><stop offset='1' stop-color='#4A5A33'/></linearGradient></defs>")
COLX, ROWY = [30, 520, 1010], [100]
H.COLX, H.ROWY = COLX, ROWY
ht, para = H.ht, H.para


def binoculars(x, y, fov=50 / 7):
    """The binoculars' own HUD (panel 1's — context here): the eyepiece frame, the mil reticle, the name, the zoom steps."""
    cx, cy = x + PW / 2, y + PH / 2; r = PH * 0.42
    s.raw(f"<mask id='bm{x}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='#fff'/><circle cx='{cx - r * 0.55}' cy='{cy}' r='{r}' fill='#000'/>"
          f"<circle cx='{cx + r * 0.55}' cy='{cy}' r='{r}' fill='#000'/></mask><rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='#000' opacity='0.93' mask='url(#bm{x})'/>")
    px = PH / (fov * 17.4533)
    for sgn in (-1, 1):
        H.hline(cx + sgn * 6, cy, cx + sgn * 58 * px, cy); H.hline(cx, cy + sgn * 6, cx, cy + sgn * min(58 * px, r * (1 - 0.55 ** 2) ** 0.5 - 4))
    for m in range(5, 55, 5):
        d = m * px; big = m % 10 == 0
        for sgn in (-1, 1): H.hline(cx + sgn * d, cy - (4 if big else 2.5), cx + sgn * d, cy + (4 if big else 2.5), WHITE, 1)
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='1.5' fill='{WHITE}'/>")
    ht(cx, y + PH - 32, "BINOCULARS", "#C8C8D0", 11.5, "middle", "bold")
    for i, (z, on) in enumerate((("5×", False), ("7×", True), ("10×", False))):
        ht(x + PW - 14, y + 132 + i * 18, z, GOLD if on else "#808088", 14 if on else 11, "end", "bold" if on else "normal")
    H.ctx_box(x + 4, y + 4, PW - 8, PH - 8, "unchanged — the binoculars' HUD (panel 1)")


def tri(pts, c, stroke=None):
    s.raw("<polygon points='" + " ".join(f"{a},{b}" for a, b in pts) + f"' fill='{c}'" + (f" stroke='{stroke}' stroke-width='1.2'" if stroke else "") + "/>")


def cue_lines(tx, ty, offset, rng, sender, left=True):
    """HudCue.Target's three lines: TARGET + the side's triangle + the offset · the range · the sender (amber, amber, grey)."""
    ht(tx, ty, "TARGET", C.AMBER, 11, weight="bold")
    mx, my = tx + 50, ty - 4
    tri([(mx + 7, my - 4), (mx + 7, my + 4), (mx, my)] if left else [(mx, my - 4), (mx + 7, my), (mx, my + 4)], C.AMBER)
    ht(mx + 11, ty, offset, C.AMBER, 11, weight="bold")
    ht(tx, ty + 14, rng, C.AMBER, 11, weight="bold")
    ht(tx, ty + 28, sender, "#A0A0AA", 9.5)


def legend(x, y, specs, ctx, notes=()):
    return H.legend(x, y, specs, ctx, notes)


# ── 24 · rangefinder, SENT ─────────────────────────────────────────────────────────────────────────────
x, y = H.head(0, "24 · RANGEFINDER — SEND pressed after a range: the target sent to the link",
              ["Scope: the SEND chip (lit) left of RANGE and the green SENT tag under it (2 s of sim time). Everything else is panel 3."])
H.frame(x, y, 2.2); H.rf_view(x, y); H.spot(x, y); H.rf_reticle(x, y); H.rf_status(x, y, lamp_on=False); H.rf_range(x, y, "452 m"); H.rf_menu_strip(x, y, cur=0)
H.rf_chips(x, y, send="sent")
s.rect(x + PW - 14 - 78 - 6 - 78 - 3, y + 121, 84, 26, "none", stroke=GOLD, rx=4, sw=2); s.glyph(0xf256, x + PW - 14 - 78 - 6 - 40, y + 133, WHITE, 15)
H.compass(x, y); H.unit_lines(x, y)
legend(x, y, "the SEND Y chip lit (a click or Y sends the last range's point to the link, Peer0) · the SENT tag (green) for 2 s of sim time — it stays while paused (sim time stands) · the hover frame + hand cursor on SEND (POINT)",
       "the rest of the rangefinder's HUD (panel 3) · compass · unit lines · the spot",
       ["SEND is greyed with nothing measured or no link (panels 2, 4, 6); it sends one TargetLocation per press — a device message, one tick late, applied by the receiver.",
        "PROPOSAL: SEND left of RANGE (the column's fifth row would meet the menu line), the tag under it."])

# ── 25 · binoculars, a target received OFF the view ─────────────────────────────────────────────────────
x, y = H.head(1, "25 · BINOCULARS — a target received from the linked rangefinder, OFF the view (20° left)",
              ["Scope: the received-target cue — the amber arrow on the eyepieces' edge toward the target and its three lines. The binoculars' own HUD is context."])
H.frame(x, y, 3.0, target=False); binoculars(x, y)
cx, cy = x + PW / 2, y + PH / 2
tip = (cx - 140, cy); base = tip[0] + 16
tri([(tip[0] - 1.2, tip[1]), (base + 0.6, tip[1] - 10.2), (base + 0.6, tip[1] + 10.2)], "#000")
tri([tip, (base, tip[1] - 9), (base, tip[1] + 9)], C.AMBER)
cue_lines(base + 6, cy + 22, "20°", "1 240 m", "from LASER RANGEFINDER (linked)")
H.compass(x, y); H.unit_lines(x, y)
legend(x, y, "the arrow on an ellipse inside the eyepieces (half-axes 140 × 76 px) pointing toward the target · TARGET ◂ 20° (the side's triangle — the gizmo font has no ◂) · 1 240 m (from the BINOCULARS) · from LASER RANGEFINDER (linked) — the sender's TKB name, linked when the binoculars' own link names it",
       "the binoculars' HUD (panel 1) · compass · unit lines · the scene",
       ["The cue turns with the look: slewing toward it, the arrow slides along the edge, and the target enters the view (panel 26).",
        "PROPOSAL: the text inside the arrow (to its right on the left edge, to its left on the right edge, above it at the bottom)."])

# ── 26 · binoculars, the target IN the view ─────────────────────────────────────────────────────────────
x, y = H.head(2, "26 · BINOCULARS — slewed toward it: the target IN the view (2° left)",
              ["Scope: the cue in the view — an amber diamond on the received point and the same three lines beside it."])
H.frame(x, y, 3.0, target=True, tank_dx=-72 / 3.0); binoculars(x, y)
cx, cy = x + PW / 2, y + PH / 2
dx, dy = cx - 72, cy                                                                   # 2° left at 7× (36 px / °)
tri([(dx, dy - 7), (dx + 7, dy), (dx, dy + 7)], C.AMBER); tri([(dx, dy - 7), (dx, dy + 7), (dx - 7, dy)], C.AMBER)
s.raw(f"<circle cx='{dx}' cy='{dy}' r='1.6' fill='#000'/>")
cue_lines(dx + 12, dy - 4, "2°", "1 238 m", "from LASER RANGEFINDER (linked)")
H.compass(x, y); H.unit_lines(x, y)
legend(x, y, "the amber diamond ON the received point (where the sender's laser met) · the same three lines beside it",
       "the binoculars' HUD (panel 1) · compass · unit lines · the scene (the tank)",
       ["Judged by dv-linked-target: the binoculars' memory holds the point (within 2 m of the tank) and the hud row reads the sender line.",
        "PROPOSAL: the text right of the diamond, 12 px out."])

os.makedirs("docs/designs/devices/mockups", exist_ok=True)
s.save("docs/designs/devices/mockups/devices-hud-link.svg")
print("wrote docs/designs/devices/mockups/devices-hud-link.svg")
