#!/usr/bin/env python3
"""Mockup for docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 — the HUD of the first two simulated devices, the BINOCULARS and the LASER
RANGEFINDER (SD-S1, R-4138 / R-4143). Built on scripts/mockup_kit.py.

    python3 scripts/gen-devices-hud-mockup.py      # -> docs/designs/devices/mockups/devices-hud.svg

SCOPE (R-4151, what this picture specifies and what it does not):
  SPECIFIES  the device HUD drawn in the 3-D view BOUND to a device: the view frame, the reticle, the zoom step, the device name, the mode, the
             menu, the lamp, the last range, the view-source label and the cycle strip — i.e. what the type's HUD projector emits (Screen
             primitives scoped by HudScope, §3.6).
  CONTEXT    drawn dashed / greyed and tagged "unchanged": the station's own HUD that stays — the compass strip (DrawCompass), the unit's
             health / weapon / seat lines (Hud.Lines) — and the toolbar run controls (panel 5). The 3-D scene behind is illustrative only.
  PROPOSAL   every layout position is a proposal where the design is silent (the design names WHAT the HUD shows, not WHERE): see the panel notes.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

PLAY, PAUSE_G = 0xf04b, 0xf04c
W, H = 1500, 1150
PW, PH = 460, 259                     # one view frame (16:9)
GOLD, WHITE, LASER = "#FFD700", "#FFFFFF", "#FF3B30"   # the station's gold readouts (DrawBinoculars), white reticle, red laser lamp
s = Svg(W, H, "Simulated devices — the HUD of the binoculars and the laser rangefinder  (SD-S1, DESIGN_Simulated_Devices §3.6)",
        "Full colour = SPECIFIED by this mockup · grey dashed + 'unchanged' = CONTEXT drawn from the real station HUD, not specified · the scene behind is illustrative.")


# ── small drawing helpers ───────────────────────────────────────────────────────────────────────────────
def esc(t): return str(t).replace("&", "&amp;").replace("<", "&lt;")


def ht(x, y, t, c=WHITE, sz=12, anchor="start", weight="normal", op=1):
    """HUD text: the station's HudText draws over the scene, so it carries a dark halo (legible on any background)."""
    s.raw(f"<text x='{x}' y='{y}' fill='{c}' font-size='{sz}' font-family='Roboto, Segoe UI, Arial, sans-serif' text-anchor='{anchor}' font-weight='{weight}' "
          f"stroke='#000' stroke-width='2.2' stroke-opacity='0.6' paint-order='stroke' opacity='{op}' xml:space='preserve'>{esc(t)}</text>")


def hline(x1, y1, x2, y2, c=WHITE, sw=1.2):
    s.line(x1, y1, x2, y2, "#000", sw + 2.2); s.line(x1, y1, x2, y2, c, sw)


def ctx_box(x, y, w, h, tag):
    s.rect(x, y, w, h, "none", stroke="#8A8A96", dash="4 3", sw=1)
    s.text(x + w - 3, y - 3, tag, "#8A8A96", 9.5, "end")


_n = [0]


def frame(x, y, k, view="plain", target=True, tank_dx=0):
    """The 3-D view behind the HUD (illustrative): sky, a ridge, a field, a tree line, a house and ONE tank under the reticle, magnified by k."""
    _n[0] += 1; cid = f"c{_n[0]}"
    cx, cy = x + PW / 2, y + PH / 2
    s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}'/></clipPath><g clip-path='url(#{cid})'>")
    s.raw(f"<g transform='translate({cx},{cy}) scale({k}) translate({-cx},{-cy})'>")
    s.raw(f"<rect x='{x - 400}' y='{y - 300}' width='{PW + 800}' height='{PH * 0.56 + 300}' fill='url(#sky)'/>")
    hz = y + PH * 0.56
    s.raw(f"<polygon points='{x - 400},{hz} {x - 60},{hz - 16} {x + 120},{hz - 5} {x + 300},{hz - 22} {x + PW + 400},{hz - 6} {x + PW + 400},{hz} ' fill='#4C5E49'/>")
    s.raw(f"<rect x='{x - 400}' y='{hz}' width='{PW + 800}' height='400' fill='url(#grass)'/>")
    for i in range(-14, 22):                                                                    # the tree line
        s.raw(f"<circle cx='{x + i * 13}' cy='{hz - 3}' r='7' fill='#2F4A2E'/>")
    s.rect(x + 70, hz - 14, 30, 16, "#A59A86"); s.raw(f"<polygon points='{x + 66},{hz - 14} {x + 85},{hz - 26} {x + 104},{hz - 14}' fill='#7A3B30'/>")
    if target:                                                                                  # the tank the reticle rests on
        tx, ty = cx + tank_dx, cy + 2
        s.rect(tx - 14, ty - 4, 28, 8, "#5B6350", rx=1.5); s.rect(tx - 7, ty - 8, 14, 5, "#4B5342", rx=1.5)
        s.line(tx + 6, ty - 6, tx + 22, ty - 7, "#4B5342", 1.6)
    s.raw("</g></g>")
    s.rect(x, y, PW, PH, "none", stroke=C.BORDER)


def compass(x, y, tag=True):
    """CONTEXT — PossessionStation.DrawCompass: the ticks strip at the top and the heading under it."""
    cx = x + PW / 2; half = 138
    s.rect(cx - half - 8, y + 8, 2 * half + 16, 38, "#000", op=0.43)
    for i in range(-6, 7):
        tx = cx + i * half / 6.5; maj = i % 3 == 0
        s.line(tx, y + 12, tx, y + 12 + (10 if maj else 6), "#CCCCCC", 1)
        if maj and i % 6 == 0 or i == 0: s.text(tx, y + 33, {-6: "NW", 0: "N", 6: "NE"}[i], "#CCCCCC", 9, "middle")
    s.rect(cx - 15, y + 48, 30, 14, "#000", op=0.6); s.text(cx, y + 59, "012", "#8F7F10", 10.5, "middle")
    ctx_box(cx - half - 8, y + 8, 2 * half + 16, 54, "unchanged — compass" if tag else "unchanged")


def unit_lines(x, y, w=112):
    """CONTEXT — PossessionStation.Hud.Lines: health / weapon / seat lines, bottom-left of the visible map."""
    s.rect(x + 8, y + PH - 46, w, 38, "#000", op=0.6)
    for i, t in enumerate(("health · seat", "weapon · round", "keys hint")):
        s.rect(x + 14, y + PH - 40 + i * 11, [70, 84, 56][i], 4, "#8A8A96", op=0.7)
    ctx_box(x + 8, y + PH - 46, w, 38, "unchanged — unit lines")


def lamp(x, y, on, label="LASER"):
    if on:
        s.raw(f"<circle cx='{x}' cy='{y}' r='9' fill='{LASER}' fill-opacity='0.28'/>")
        s.raw(f"<circle cx='{x}' cy='{y}' r='5' fill='{LASER}' stroke='#000' stroke-width='1'/>")
    else:
        s.raw(f"<circle cx='{x}' cy='{y}' r='5' fill='#2A2A2E' stroke='#8A8A96' stroke-width='1.2'/>")
    ht(x - 12, y + 4, label, LASER if on else "#A0A0AA", 11, "end", "bold")


def rf_view(x, y, spot=False):
    """The rangefinder's frame: a darkened rectangular window with corner brackets (its monocular), no eyepiece circles."""
    s.raw(f"<mask id='rfm{x}{y}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='#fff'/>"
          f"<rect x='{x + 10}' y='{y + 10}' width='{PW - 20}' height='{PH - 20}' rx='10' fill='#000'/></mask>")
    s.raw(f"<rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='#000' opacity='0.78' mask='url(#rfm{x}{y})'/>")
    for (a, b, dx, dy) in ((x + 14, y + 14, 1, 1), (x + PW - 14, y + 14, -1, 1), (x + 14, y + PH - 14, 1, -1), (x + PW - 14, y + PH - 14, -1, -1)):
        hline(a, b, a + 16 * dx, b, WHITE, 1.4); hline(a, b, a, b + 16 * dy, WHITE, 1.4)


def rf_reticle(x, y):
    """A gapped cross, a centre dot and short mil ticks (1 mil = 1.5 px here, ticks every 5 mil)."""
    cx, cy = x + PW / 2, y + PH / 2
    for sgn in (-1, 1):
        hline(cx + sgn * 6, cy, cx + sgn * 44, cy); hline(cx, cy + sgn * 6, cx, cy + sgn * 44)
    for m in range(1, 6):
        d = 6 + m * 7.5
        for sgn in (-1, 1):
            hline(cx + sgn * d, cy - 2.5, cx + sgn * d, cy + 2.5, WHITE, 1); hline(cx - 2.5, cy + sgn * d, cx + 2.5, cy + sgn * d, WHITE, 1)
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='1.6' fill='{WHITE}' stroke='#000' stroke-width='0.8'/>")


def rf_status(x, y, mode="SINGLE", power="LOW", lamp_on=False):
    ht(x + 24, y + 82, "LASER RANGEFINDER", "#C8C8D0", 11, weight="bold")
    ht(x + 24, y + 98, "MODE", "#A0A0AA", 11); ht(x + 62, y + 98, mode, WHITE, 12, weight="bold")
    ht(x + 24, y + 114, "POWER", "#A0A0AA", 11); ht(x + 70, y + 114, power, WHITE, 12, weight="bold")
    lamp(x + PW - 24, y + 82, lamp_on)


def rf_range(x, y, text, preview=False):
    cx = x + PW / 2
    ht(cx, y + PH - 56, "LAST RANGE", "#A0A0AA", 10.5, "middle")
    ht(cx, y + PH - 28, text, GOLD, 24, "middle", "bold")
    if preview:
        w = text_width("PREVIEW", 10) + 12
        s.rect(cx + 50, y + PH - 52, w, 15, "#000", rx=3, op=0.7, stroke=C.PEND, sw=1)
        s.text(cx + 50 + w / 2, y + PH - 41, "PREVIEW", C.PEND, 10, "middle", "bold")


def rf_menu_strip(x, y, cur=0, opened=False):
    """The menu line: the items, the cursor on one (highlight). Proposal: bottom-right, one row."""
    items = ["MODE", "POWER", "EXIT"]
    xx = x + PW - 14
    for i in reversed(range(len(items))):
        w = text_width(items[i], 11) + 14; xx -= w
        sel = i == cur
        s.rect(xx, y + PH - 36, w, 18, "#3369AD" if sel else "#000", rx=3, op=0.95 if sel else 0.55, stroke=GOLD if (sel and opened) else None, sw=1.2)
        s.text(xx + w / 2, y + PH - 23.5, items[i], WHITE if sel else "#B8B8C0", 11, "middle", "bold" if sel else "normal")
        xx -= 5


def spot(x, y, preview=False):
    """CONTEXT — the laser spot on the target: a WORLD effect of LaserLayer3D (SD-S6), not part of the device HUD."""
    cx, cy = x + PW / 2, y + PH / 2 + 2
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='9' fill='{LASER}' fill-opacity='0.30'/><circle cx='{cx}' cy='{cy}' r='3.2' fill='{LASER}'"
          + (" fill-opacity='0.6'" if preview else "") + "/>")
    if preview: s.raw(f"<circle cx='{cx}' cy='{cy}' r='8' fill='none' stroke='{LASER}' stroke-width='1' stroke-dasharray='3 2'/>")
    s.text(cx + 16, cy - 14, "spot — world effect, SD-S6", "#8A8A96", 9.5)
    s.line(cx + 4, cy - 4, cx + 15, cy - 12, "#8A8A96", 0.8, "2 2")


# ── page: defs, key ─────────────────────────────────────────────────────────────────────────────────────
s.raw("<defs><linearGradient id='sky' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#5B87B8'/><stop offset='1' stop-color='#BFD3E3'/></linearGradient>"
      "<linearGradient id='grass' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#6B7E4A'/><stop offset='1' stop-color='#4A5A33'/></linearGradient></defs>")
COLX = [30, 520, 1010]
ROWY = [124, 622]


def wrap(t, w, sz):
    out, cur = [], ""
    for word in t.split(" "):
        trial = (cur + " " + word).strip()
        if cur and text_width(trial, sz) > w: out.append(cur); cur = word
        else: cur = trial
    return out + [cur]


def para(x, y, t, c, sz=10.5, w=450, lead=13):
    for l in wrap(t, w, sz): s.text(x, y, l, c, sz); y += lead
    return y


def head(i, title, scope):
    x, y = COLX[i % 3], ROWY[i // 3]
    s.text(x, y, title, "#E8E8EE", 13.5, weight="bold")
    para(x, y + 16, scope if isinstance(scope, str) else " ".join(scope), C.NOTE, 11, 455)
    return x, y + 58


def legend(x, y, specs, ctx, notes=(), dy=0):
    yy = y + PH + 16 + dy
    s.text(x, yy, "SPECIFIES", C.GREEN, 10.5, weight="bold"); yy = para(x + 66, yy, specs, C.SUB, 11, 390, 13.5)
    s.text(x, yy + 2, "CONTEXT", "#8A8A96", 10.5, weight="bold"); yy = para(x + 66, yy + 2, ctx, C.NOTE, 11, 390, 13.5)
    yy += 4
    for n in notes: yy = para(x, yy, n, C.PEND if n.startswith("PROPOSAL") else C.NOTE, 10.5, 455) + 2
    return yy


s.rect(30, 56, 12, 12, "#000", stroke=WHITE, sw=1); s.text(48, 66, "specified HUD element (full colour, dark halo — as the station's HudText draws over the 3-D view)", C.SUB, 11)
s.rect(640, 56, 12, 12, "none", stroke="#8A8A96", dash="4 3"); s.text(658, 66, "context — unchanged, not specified here", C.SUB, 11)
s.text(30, 102, "Layout positions are PROPOSALS where the design is silent (it names what the HUD shows, not where) — each panel lists its own.", C.PEND, 11)
s.text(30, 86, "Frames are 460 × 259 (16:9). The scene is the same in every panel and illustrative; the tank is only there so the reticle has something to rest on.", C.NOTE, 11)

# ── 1 · binoculars ──────────────────────────────────────────────────────────────────────────────────────
x, y = head(0, "1 · BINOCULARS — the view bound to the binoculars device",
            ["Scope: the two-eyepiece frame, the mil reticle, the zoom step, the device name (D-42, D-75: replaces the station's overlay).", "The compass and the unit lines are context."])
frame(x, y, 3.0)
cx, cy = x + PW / 2, y + PH / 2; r = PH * 0.42
s.raw(f"<mask id='bm'><rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='#fff'/><circle cx='{cx - r * 0.55}' cy='{cy}' r='{r}' fill='#000'/>"
      f"<circle cx='{cx + r * 0.55}' cy='{cy}' r='{r}' fill='#000'/></mask><rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='#000' opacity='0.93' mask='url(#bm)'/>")
px = PH / 174.5   # 1 mil in px, true to today's 10° view (HeldItems.BinocularsFovDegrees) over the frame height
for sgn in (-1, 1):                                                        # the cross with a gap, ticks every 5 mil, a number every 10
    hline(cx + sgn * 6, cy, cx + sgn * 58 * px, cy); hline(cx, cy + sgn * 6, cx, cy + sgn * 58 * px)
for m in range(5, 55, 5):
    d = m * px; big = m % 10 == 0
    for sgn in (-1, 1):
        hline(cx + sgn * d, cy - (4 if big else 2.5), cx + sgn * d, cy + (4 if big else 2.5), WHITE, 1)
        hline(cx - (4 if big else 2.5), cy + sgn * d, cx + (4 if big else 2.5), cy + sgn * d, WHITE, 1)
    if big and m <= 40:
        ht(cx + 8, cy + d + 3, str(m), "#E0E0E8", 8.5)
        if m % 20 == 0: ht(cx + d, cy - 7, str(m), "#E0E0E8", 8.5, "middle")
s.raw(f"<circle cx='{cx}' cy='{cy}' r='1.5' fill='{WHITE}'/>")
ht(cx, y + PH - 12, "BINOCULARS", "#C8C8D0", 11.5, "middle", "bold")                       # the device name, in the dark rim
for i, (z, on) in enumerate((("5×", False), ("7×", True), ("10×", False))):                # the zoom step list, right rim
    ht(x + PW - 14, y + 108 + i * 18, z, GOLD if on else "#808088", 14 if on else 11, "end", "bold" if on else "normal")
s.glyph(FA.BINOCULARS, x + PW - 36, y + 78, C.TEAL, 15)
compass(x, y); unit_lines(x, y)
legend(x, y, "two-eyepiece frame · mil reticle (5-mil ticks, numbers every 10) · zoom step 7× of a list · device name", "compass strip · unit lines · the 3-D scene",
       ["mil scale is true to today's 10° view (HeldItems.BinocularsFovDegrees) over this frame: 1 mil = 1.48 px.",
        "PROPOSAL: step values 5× / 7× / 10× are illustrative (the type's data); the list sits in the right rim.",
        "No range shown: the binoculars carry no laser (the old overlay's RANGE / LMB-designate goes with CE-1219, D-75)."])

# ── 2 · rangefinder idle ────────────────────────────────────────────────────────────────────────────────
x, y = head(1, "2 · RANGEFINDER, idle — laser OFF, nothing measured yet",
            ["Scope: the rectangular window, the gapped reticle, mode / power, the laser lamp, the last range, the menu line.", "The compass and the unit lines are context."])
frame(x, y, 2.2); rf_view(x, y); rf_reticle(x, y); rf_status(x, y, lamp_on=False); rf_range(x, y, "— m"); rf_menu_strip(x, y, cur=0)
compass(x, y); unit_lines(x, y)
legend(x, y, "window + corner brackets · reticle · name · MODE · POWER · LASER lamp (off) · LAST RANGE '— m' · menu line, cursor on MODE", "compass · unit lines · scene",
       ["PROPOSAL: mode names SINGLE / CONTINUOUS and power LOW / HIGH are placeholders (the type's data); status top-left, lamp",
        "top-right, readout bottom-centre, menu line bottom-right (clear of the unit lines, which stay bottom-left)."])

# ── 3 · rangefinder after RANGE ────────────────────────────────────────────────────────────────────────
x, y = head(2, "3 · RANGEFINDER, after RANGE on a target — laser ON, 452 m",
            ["Scope: the lamp on, the range written, the same layout as panel 2.", "The spot on the target is a world effect (context)."])
frame(x, y, 2.2); rf_view(x, y); spot(x, y); rf_reticle(x, y); rf_status(x, y, lamp_on=True); rf_range(x, y, "452 m"); rf_menu_strip(x, y, cur=0)
compass(x, y); unit_lines(x, y)
legend(x, y, "LASER lamp ON (blinks by frame count) · LAST RANGE '452 m' (gold, the station's readout colour)", "the red spot (LaserLayer3D, SD-S6) · compass · unit lines",
       ["The HUD does NOT draw the spot: it is the 3-D map's laser layer (D-32..D-35, slice SD-S6) — SD-S1 shows the lamp and the range only.",
        "Judged by scenario dv-range-targets: `hud` text contains the range, `device range` within accuracy."])

# ── 4 · rangefinder menu open ──────────────────────────────────────────────────────────────────────────
x, y = head(3, "4 · RANGEFINDER, menu open — the mode list (dv-menu-and-modes)",
            ["Scope: the menu popup (title, the modes, the check on the active one, the cursor row), the menu line with MODE open.", "Everything else as panel 2."])
frame(x, y, 2.2); rf_view(x, y); rf_reticle(x, y); rf_status(x, y, lamp_on=False); rf_range(x, y, "— m"); rf_menu_strip(x, y, cur=0, opened=True)
px0, py0, pw0, ph0 = x + 24, y + 124, 150, 74
s.rect(px0, py0, pw0, ph0, "#000", rx=3, op=0.82, stroke="#8A8A96", sw=1)
ht(px0 + 8, py0 + 15, "MENU ▸ MODE", "#C8C8D0", 10.5, weight="bold"); s.line(px0 + 6, py0 + 21, px0 + pw0 - 6, py0 + 21, "#8A8A96", 0.8)
for i, (nm, active, cur) in enumerate((("SINGLE", True, False), ("CONTINUOUS", False, True))):
    ry = py0 + 26 + i * 22
    if cur: s.rect(px0 + 4, ry, pw0 - 8, 20, "#3369AD", rx=3)
    s.text(px0 + 12, ry + 14.5, nm, WHITE, 12, weight="bold" if cur else "normal")
    if active: s.glyph(FA.CHECK, px0 + pw0 - 24, ry + 4, C.GREEN, 12)
    if cur: s.text(px0 + pw0 - 12, ry + 14.5, "▸", GOLD, 12, "end")
compass(x, y); unit_lines(x, y)
legend(x, y, "popup list · ✓ on the active mode · highlighted cursor row · MODE chip framed (menu is open) · reticle stays", "compass · unit lines · scene",
       ["The cursor row (CONTINUOUS) is not yet the mode: the state `Mode` changes on select; `Menu` follows each press (the scenario's rows).",
        "PROPOSAL: popup under the status block, left; the reticle and the range stay visible while the menu is open."])

# ── 5 · paused ──────────────────────────────────────────────────────────────────────────────────────────
x, y = head(4, "5 · RANGEFINDER while the simulation is PAUSED (dv-paused-operation)",
            ["Scope: the same HUD still answering in pause: a range measured in pause is shown marked PREVIEW (R5-10, LS-13).", "The toolbar's paused state is context."])
frame(x, y, 2.2); rf_view(x, y); spot(x, y, preview=True); rf_reticle(x, y); rf_status(x, y, mode="CONTINUOUS", lamp_on=True); rf_range(x, y, "452 m", preview=True)
rf_menu_strip(x, y, cur=0)
compass(x, y); unit_lines(x, y)
ty = y + PH + 8                                                                               # CONTEXT: the toolbar's run controls, paused
s.rect(x, ty, 285, 24, C.PAGE, stroke="#8A8A96", dash="4 3", rx=3)
s.glyph(0xf111, x + 8, ty + 5, "#E63838", 13); s.glyph(PLAY, x + 40, ty + 5, "#E63838", 13); s.glyph(0xf04d, x + 72, ty + 5, C.DIS, 13, op=0.5)
s.text(x + 96, ty + 16, "toolbar, paused: ▶ shown (context)", C.NOTE, 10.5)
legend(x, y, "the HUD still answering in pause: mode (CONTINUOUS, changed by a press), the lamp, the range, with a PREVIEW tag beside it (a spot written in pause acts on nothing, R5-10)",
       "the toolbar's run controls (Live, recorded, paused — the indicator + ▶) · compass · unit lines · the spot", [
       "Measured: no in-view PAUSED banner exists today; the toolbar run indicator is the only pause cue (a DAR review shows players 'REVIEW — the exercise is paused'). The device HUD adds none of its own.",
       "PROPOSAL: the PREVIEW tag's look and place are mine — the design says the HUD shows the preview spot, not how. Judged on the wall-clock axis (dv-replay-hud)."], dy=34)

# ── 6 · switching ───────────────────────────────────────────────────────────────────────────────────────
x, y = head(5, "6 · SWITCHING — the view's bound source (device wheel / cycle)",
            ["Scope: the 'VIEW ▸' label of the bound source and the cycle strip shown while switching. LAYOUT IS A PROPOSAL.", "Design says: a view is bound to ONE source (D-38 / D-66); the wheel's key is open (Q-5)."])
frame(x, y, 2.2); rf_view(x, y); rf_reticle(x, y); rf_status(x, y, lamp_on=False); rf_range(x, y, "— m"); rf_menu_strip(x, y, cur=0)
lw = text_width("VIEW  LASER RANGEFINDER", 11) + 34                                          # the always-on label (right column, under the lamp)
lx = x + PW - 14 - lw
s.rect(lx, y + 94, lw, 20, "#000", rx=3, op=0.7); s.glyph(FA.CROSSHAIRS, lx + 6, y + 97, C.RED, 13)
s.text(lx + 24, y + 108.5, "VIEW", "#A0A0AA", 10.5); s.text(lx + 56, y + 108.5, "LASER RANGEFINDER", WHITE, 11, weight="bold")
sw, sx = 3, x + PW / 2 - 130                                                                   # the cycle strip: eye · binoculars · rangefinder
s.rect(sx - 6, y + 138, 272, 30, "#000", rx=4, op=0.7)
for i, (g, gc, nm, sel) in enumerate(((FA.EYE, C.BLUE, "eye", False), (FA.BINOCULARS, C.TEAL, "binoculars", False), (FA.CROSSHAIRS, C.RED, "rangefinder", True))):
    cxx = sx + i * 90
    if sel: s.rect(cxx, y + 141, 86, 24, "#34343E", rx=4, stroke="#C8C8D4", sw=1.3)
    s.glyph(g, cxx + 6, y + 146, gc, 13, op=1 if sel else 0.6); s.text(cxx + 24, y + 158, nm, WHITE if sel else "#9A9AA4", 10.5, weight="bold" if sel else "normal")
compass(x, y, tag=False); unit_lines(x, y)
yy = legend(x, y, "the VIEW ▸ label of the bound source (its glyph in its colour + the device name, top-right under the lamp) · the cycle strip (eye · binoculars · rangefinder), selection framed",
            "compass · unit lines · the bound device's own HUD (panel 2)", [
            "The label is always on while a view shows a device; the strip is drawn only while switching. Below: the label for each source the strip offers today (more sources — weapon sight, cockpit — join with SD-S7).",
            "PROPOSAL (all of it): the strip, the label's place and how long the strip stays are the ui lane's call; the wheel's key is open (Q-5). The design fixes only: one source per view (D-38 / D-66), the plain eye is the 'none' source."])
for i, (g, gc, nm) in enumerate(((FA.EYE, C.BLUE, "plain eye"), (FA.BINOCULARS, C.TEAL, "binoculars"), (FA.CROSSHAIRS, C.RED, "rangefinder"))):
    bx = x + i * 152
    s.rect(bx, yy + 2, 146, 22, "#000", rx=3, stroke="#C8C8D4", sw=1.2); s.glyph(g, bx + 7, yy + 6, gc, 13)
    s.text(bx + 27, yy + 17, "VIEW", "#A0A0AA", 10); s.text(bx + 55, yy + 17, nm, WHITE, 10.5, weight="bold")

os.makedirs("docs/designs/devices/mockups", exist_ok=True)
s.save("docs/designs/devices/mockups/devices-hud.svg")
print("wrote docs/designs/devices/mockups/devices-hud.svg")
