#!/usr/bin/env python3
"""Mockup for docs/designs/devices/DESIGN_Simulated_Devices.md §3.13b - the HUD of the LAUNCHER SIGHT UNIT (SD-S4 / SD-S4b, D-92..D-100;
R-4138 / R-4143), panels 7-11. Built on scripts/mockup_kit.py, in the house style of scripts/gen-devices-hud-mockup.py (panels 1-6) and
scripts/gen-devices-drone-mockup.py (panels 12-15).

    python3 scripts/gen-devices-sight-mockup.py      # -> docs/designs/devices/mockups/devices-hud-sight.svg

SCOPE (R-4151, what this picture specifies and what it does not):
  SPECIFIES  what the AtgmSight HUD projector draws in a view bound to the sight unit: the name, the procedure phase chip, the missiles left,
             the READY lamp, the reticle, the message line (self-test progress, READY, IN FLIGHT with the time of flight), the command chips
             POWER / ARM / FIRE / VIEW, the VIEW label, the missile's-view aim offset (crosshair guidance), the MCLOS stick gauge.
  CONTEXT    drawn dashed / greyed and tagged "unchanged": the station's compass strip and the operator's unit lines. The 3-D scene, the
             missile's flare and the missile in the chase picture are WORLD effects (illustrative), not the HUD.
  PROPOSAL   every layout position is a proposal where the design is silent (the design names WHAT the HUD shows, not WHERE).
Panel numbers continue devices-hud.svg (1-6); 12-15 are devices-hud-drone.svg. Panel 7 is drawn four times, one per phase before ARMED
(7a OFF, 7b TEST, 7c STANDBY, 7d STANDBY with no missile).
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

ROCKET = 0xf135
W, H = 1500, 1840
PW, PH = 460, 259                     # one view frame (16:9)
GOLD, WHITE = "#FFD700", "#FFFFFF"
PHASE = {"OFF": "#A0A0AA", "TEST": C.AMBER, "STANDBY": C.BLUE, "ARMED": C.GREEN, "GUIDING": C.RED}   # one colour per procedure phase (D-93)
s = Svg(W, H, "Simulated devices — the HUD of the launcher sight unit  (SD-S4 / SD-S4b, DESIGN_Simulated_Devices §3.13b, D-100)",
        "Full colour = SPECIFIED by this mockup · grey dashed + 'unchanged' = CONTEXT drawn from the real station HUD, not specified · the scene behind is illustrative.")


def esc(t): return str(t).replace("&", "&amp;").replace("<", "&lt;")


def ht(x, y, t, c=WHITE, sz=12, anchor="start", weight="normal", op=1):
    """HUD text with the station's dark halo (legible on any background)."""
    s.raw(f"<text x='{x}' y='{y}' fill='{c}' font-size='{sz}' font-family='Roboto, Segoe UI, Arial, sans-serif' text-anchor='{anchor}' font-weight='{weight}' "
          f"stroke='#000' stroke-width='2.2' stroke-opacity='0.6' paint-order='stroke' opacity='{op}' xml:space='preserve'>{esc(t)}</text>")


def hline(x1, y1, x2, y2, c=WHITE, sw=1.2):
    s.line(x1, y1, x2, y2, "#000", sw + 2.2); s.line(x1, y1, x2, y2, c, sw)


def ctx_box(x, y, w, h, tag):
    s.rect(x, y, w, h, "none", stroke="#8A8A96", dash="4 3", sw=1)
    s.text(x + w - 3, y - 3, tag, "#8A8A96", 9.5, "end")


_n = [0]


def scene(x, y, k, tank_dx=0):
    """The 3-D view behind the HUD (illustrative), as panels 1-6: sky, a ridge, a field, a tree line, a house, ONE tank under the reticle, magnified by k."""
    _n[0] += 1; cid = f"c{_n[0]}"
    cx, cy = x + PW / 2, y + PH / 2
    s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}'/></clipPath><g clip-path='url(#{cid})'>")
    s.raw(f"<g transform='translate({cx},{cy}) scale({k}) translate({-cx},{-cy})'>")
    s.raw(f"<rect x='{x - 400}' y='{y - 300}' width='{PW + 800}' height='{PH * 0.56 + 300}' fill='url(#sky)'/>")
    hz = y + PH * 0.56
    s.raw(f"<polygon points='{x - 400},{hz} {x - 60},{hz - 16} {x + 120},{hz - 5} {x + 300},{hz - 22} {x + PW + 400},{hz - 6} {x + PW + 400},{hz} ' fill='#4C5E49'/>")
    s.raw(f"<rect x='{x - 400}' y='{hz}' width='{PW + 800}' height='400' fill='url(#grass)'/>")
    for i in range(-14, 22):
        s.raw(f"<circle cx='{x + i * 13}' cy='{hz - 3}' r='7' fill='#2F4A2E'/>")
    s.rect(x + 70, hz - 14, 30, 16, "#A59A86"); s.raw(f"<polygon points='{x + 66},{hz - 14} {x + 85},{hz - 26} {x + 104},{hz - 14}' fill='#7A3B30'/>")
    tx, ty = cx + tank_dx, cy + 2
    s.rect(tx - 14, ty - 4, 28, 8, "#5B6350", rx=1.5); s.rect(tx - 7, ty - 8, 14, 5, "#4B5342", rx=1.5)
    s.line(tx + 6, ty - 6, tx + 22, ty - 7, "#4B5342", 1.6)
    s.raw("</g></g>")
    s.rect(x, y, PW, PH, "none", stroke=C.BORDER)


def chase(x, y):
    """The missile's picture (illustrative): a chase view from behind and above the round, flying down a valley toward a tank, the horizon high."""
    _n[0] += 1; cid = f"c{_n[0]}"
    cx, hz = x + PW / 2, y + PH * 0.40
    s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}'/></clipPath><g clip-path='url(#{cid})'>")
    s.raw(f"<rect x='{x}' y='{y}' width='{PW}' height='{hz - y}' fill='url(#sky)'/>")
    s.raw(f"<polygon points='{x},{hz} {x + 90},{hz - 14} {x + 200},{hz - 4} {x + 330},{hz - 18} {x + PW},{hz - 6} {x + PW},{hz}' fill='#4C5E49'/>")
    s.raw(f"<rect x='{x}' y='{hz}' width='{PW}' height='{PH}' fill='url(#grass)'/>")
    for i in range(-7, 8):                                                                      # field strips converging on the vanishing point
        if i % 2: continue
        s.raw(f"<polygon points='{cx + i * 70 - 24},{y + PH} {cx + i * 70 + 24},{y + PH} {cx + i * 5 + 3},{hz} {cx + i * 5 - 3},{hz}' fill='#74874F' opacity='0.35'/>")
    s.raw(f"<polygon points='{cx - 40},{y + PH} {cx + 54},{y + PH} {cx + 6},{hz} {cx + 1},{hz}' fill='#8C8A84'/>")   # the track
    for i in range(0, 41):
        s.raw(f"<circle cx='{x + 4 + i * 12}' cy='{hz + 1}' r='3.4' fill='#2F4A2E'/>")
    tx, ty = cx + 42, hz + 12                                                                   # the tank, far ahead and a little right of the heading
    s.rect(tx - 9, ty - 2.5, 18, 5, "#5B6350", rx=1); s.rect(tx - 4.5, ty - 5.5, 9, 3.5, "#4B5342", rx=1)
    s.line(tx + 4, ty - 4, tx + 14, ty - 4.8, "#4B5342", 1.2)
    s.raw(f"<radialGradient id='fl{cid}'><stop offset='0' stop-color='#FFF1B0' stop-opacity='0.95'/><stop offset='0.4' stop-color='#FF9A2E' stop-opacity='0.6'/>"
          f"<stop offset='1' stop-color='#FF6A00' stop-opacity='0'/></radialGradient>")
    ty0 = y + 166                                                                               # the round, seen from behind: fins, body, nose, the motor's flame
    s.raw(f"<ellipse cx='{cx}' cy='{ty0 + 4}' rx='22' ry='15' fill='url(#fl{cid})'/>")
    s.raw(f"<polygon points='{cx - 8},{ty0 - 2} {cx - 24},{ty0 + 6} {cx - 8},{ty0 - 16}' fill='#52565D'/><polygon points='{cx + 8},{ty0 - 2} {cx + 24},{ty0 + 6} {cx + 8},{ty0 - 16}' fill='#52565D'/>")
    s.raw(f"<polygon points='{cx - 8},{ty0} {cx + 8},{ty0} {cx + 5.5},{ty0 - 36} {cx - 5.5},{ty0 - 36}' fill='#767B83'/>")
    s.raw(f"<polygon points='{cx - 5.5},{ty0 - 36} {cx + 5.5},{ty0 - 36} {cx},{ty0 - 48}' fill='#9AA0A8'/>")
    s.raw("</g>")
    s.rect(x, y, PW, PH, "none", stroke=C.BORDER)
    ht(cx, y + 16, "the round and its picture — world, illustrative", "#C8C8D0", 9.5, "middle")


def flare(x, y, dx, dy, trail=1, caption=True):
    """CONTEXT - the missile's flare and smoke trail seen from the sight: a WORLD effect (the round entity), not part of the HUD."""
    cx, cy = x + PW / 2 + dx, y + PH / 2 + dy
    s.raw(f"<polyline points='{cx - 46 * trail},{cy + 3 * trail} {cx - 22 * trail},{cy + 1.5 * trail} {cx},{cy}' fill='none' stroke='#E8E8EE' stroke-width='1.6' stroke-opacity='0.55'/>")
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='6' fill='#FF8A1E' fill-opacity='0.35'/><circle cx='{cx}' cy='{cy}' r='2.4' fill='#FFF1B0'/>")
    if caption: s.text(cx + 10, cy + 20, "missile — world effect", "#8A8A96", 9.5, "end")


def compass(x, y):
    """CONTEXT - PossessionStation.DrawCompass: the ticks strip at the top and the heading under it."""
    cx = x + PW / 2; half = 138
    s.rect(cx - half - 8, y + 8, 2 * half + 16, 38, "#000", op=0.43)
    for i in range(-6, 7):
        tx = cx + i * half / 6.5; maj = i % 3 == 0
        s.line(tx, y + 12, tx, y + 12 + (10 if maj else 6), "#CCCCCC", 1)
        if maj and i % 6 == 0 or i == 0: s.text(tx, y + 33, {-6: "NW", 0: "N", 6: "NE"}[i], "#CCCCCC", 9, "middle")
    s.rect(cx - 15, y + 48, 30, 14, "#000", op=0.6); s.text(cx, y + 59, "012", "#8F7F10", 10.5, "middle")
    ctx_box(cx - half - 8, y + 8, 2 * half + 16, 54, "unchanged — compass")


def unit_lines(x, y, w=112):
    """CONTEXT - PossessionStation.Hud.Lines: health / weapon / seat lines, bottom-left of the visible map."""
    s.rect(x + 8, y + PH - 46, w, 38, "#000", op=0.6)
    for i in range(3): s.rect(x + 14, y + PH - 40 + i * 11, [70, 84, 56][i], 4, "#8A8A96", op=0.7)
    ctx_box(x + 8, y + PH - 46, w, 38, "unchanged — unit lines")


# ── the sight unit's HUD parts ─────────────────────────────────────────────────────────────────────────────
def optic(x, y, dim=0.0):
    """The sight's field: one round eyepiece, the view darkened outside it. dim darkens the whole field (OFF, TEST)."""
    cx, cy, r = x + PW / 2, y + PH / 2, PH * 0.47
    _n[0] += 1; mid = f"om{_n[0]}"
    s.raw(f"<mask id='{mid}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='#fff'/><circle cx='{cx}' cy='{cy}' r='{r}' fill='#000'/></mask>"
          f"<rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='#000' opacity='0.93' mask='url(#{mid})'/>")
    if dim > 0: s.rect(x, y, PW, PH, "#000", op=dim)


def reticle(x, y, c=WHITE, mil_px=3.0):
    """A gapped cross, a centre dot and mil ticks every 5 mil (1 mil = mil_px here: 10x, 5 deg field over the frame height)."""
    cx, cy = x + PW / 2, y + PH / 2
    for sgn in (-1, 1):                                                                         # the lower arm stops above the message line
        hline(cx + sgn * 6, cy, cx + sgn * 78, cy, c); hline(cx, cy + sgn * 6, cx, cy + (48 if sgn > 0 else -78), c)
    for m in range(5, 30, 5):
        d = m * mil_px; big = m % 10 == 0
        for sgn in (-1, 1):
            hline(cx + sgn * d, cy - (4 if big else 2.5), cx + sgn * d, cy + (4 if big else 2.5), c, 1)
            if sgn < 0 or d <= 48: hline(cx - (4 if big else 2.5), cy + sgn * d, cx + (4 if big else 2.5), cy + sgn * d, c, 1)
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='1.6' fill='{c}' stroke='#000' stroke-width='0.8'/>")


def lamp(x, y, on, label="READY", c=C.GREEN):
    if on:
        s.raw(f"<circle cx='{x}' cy='{y}' r='9' fill='#000' fill-opacity='0.9'/><circle cx='{x}' cy='{y}' r='9' fill='{c}' fill-opacity='0.28'/>")
        s.raw(f"<circle cx='{x}' cy='{y}' r='5' fill='{c}' stroke='#000' stroke-width='1'/>")
    else:
        s.raw(f"<circle cx='{x}' cy='{y}' r='5' fill='#2A2A2E' stroke='#8A8A96' stroke-width='1.2'/>")
    ht(x - 12, y + 4, label, c if on else "#A0A0AA", 11, "end", "bold")


def status(x, y, name, phase, missiles=None, lamp_on=False):
    """Top-left: the unit's name, the phase chip (one colour per phase), the missiles left; top-right: the READY lamp."""
    ht(x + 24, y + 82, name, "#C8C8D0", 11, weight="bold")
    c = PHASE[phase]; w = text_width(phase, 11.5) + 18
    s.rect(x + 24, y + 89, w, 20, "#000", rx=4, op=0.7, stroke=c, sw=1.4); ht(x + 24 + w / 2, y + 103, phase, c, 11.5, "middle", "bold")
    if missiles is not None:
        ht(x + 24, y + 126, "MISSILES", "#A0A0AA", 10.5); ht(x + 80, y + 126, str(missiles), WHITE if missiles else C.AMBER, 12, weight="bold")
    lamp(x + PW - 24, y + 82, lamp_on)


def view_label(x, y, glyph, gc, name):
    """The VIEW label of the bound source - always on while a view shows a device (panel 6), right column under the lamp."""
    lw = text_width("VIEW  " + name, 11) + 34
    lx = x + PW - 14 - lw
    s.rect(lx, y + 94, lw, 20, "#000", rx=3, op=0.7); s.glyph(glyph, lx + 6, y + 97, gc, 13)
    s.text(lx + 24, y + 108.5, "VIEW", "#A0A0AA", 10.5); s.text(lx + 56, y + 108.5, name, WHITE, 11, weight="bold")


def message(x, y, big, bigc, sub=None, subc="#C8C8D0"):
    """Bottom centre: the one line that says what to do or what is happening, and a smaller line under it."""
    cx = x + PW / 2
    ht(cx, y + PH - 62, big, bigc, 21, "middle", "bold")
    if sub: ht(cx, y + PH - 46, sub, subc, 11, "middle")


KEYS = {"POWER": "P", "SELECT": "S", "ARM": "A", "FIRE": "F", "MODE": "M", "ZOOM": "Z", "VIEW": "V"}       # PROPOSAL: the type names its hotkeys (D-124)


def chips(x, y, states, sz=10.5):
    """The command chips, bottom-right: HudPanel buttons (D-122), each with its hotkey badge (D-124). Channels POWER / ARM / FIRE / VIEW (D-92), the control
    box adds SELECT / ZOOM (D-154). state: on = the switch is set (blue), ready = pressable, off = refused (greyed)."""
    xx = x + PW - 14
    for nm, st in reversed(states):
        w = text_width(nm, sz) + 23; xx -= w
        if st == "on": s.rect(xx, y + PH - 36, w, 18, "#3369AD", rx=3, op=0.95)
        else: s.rect(xx, y + PH - 36, w, 18, "#000", rx=3, op=0.55, stroke="#8A8A96" if st == "ready" else None, sw=1)
        s.text(xx + 6, y + PH - 23.5, nm, WHITE if st == "on" else ("#E0E0E8" if st == "ready" else "#6A6A74"), sz, weight="bold" if st == "on" else "normal")
        kc = "#6A6A74" if st == "off" else "#C8C8D0"
        s.rect(xx + w - 14, y + PH - 33, 10, 12, "none", stroke=kc, rx=2, sw=0.9); s.text(xx + w - 9, y + PH - 23.5, KEYS[nm], kc, 8.5, "middle", "bold")
        xx -= 3


def mode_switch_refused(x, y):
    """The control box's fire MODE (D-157): a latching 3-position switch (SINGLE / BURST / AUTO); a launcher offers SINGLE only, so the whole switch is greyed
    and a press is refused. Under the launcher lamps (panel 22 draws it live on the RWS console, the greyed one on the box)."""
    bx, by = x + 24, y + 160
    ht(bx, by + 13, "MODE", "#A0A0AA", 9.5)
    xx = bx + 32
    for i, nm in enumerate(("SINGLE", "BURST", "AUTO")):
        w = text_width(nm, 9.5) + 12
        s.rect(xx, by, w, 18, "#2A2A32" if i == 0 else "#000", rx=3, op=0.7 if i == 0 else 0.5, stroke="#4A4A55", sw=1)
        s.text(xx + w / 2, by + 13, nm, "#808088" if i == 0 else "#5A5A64", 9.5, "middle", "bold" if i == 0 else "normal")
        xx += w + 2
    s.rect(xx + 2, by + 3, 10, 12, "#000", rx=2, op=0.6, stroke="#6A6A74", sw=0.9); s.text(xx + 7, by + 12.5, KEYS["MODE"], "#6A6A74", 8.5, "middle", "bold")


def launchers(x, y, states):
    """The control box's launcher lamps (D-155, up to four): (state, selected). loaded green / in flight red / spent greyed with a cross / not connected dashed;
    the selected one framed gold."""
    ht(x + 24, y + 124, "LAUNCHERS", "#A0A0AA", 10)
    for i, (st, sel) in enumerate(states):
        lx, ly = x + 24 + i * 30, y + 130
        col = {"loaded": C.GREEN, "flight": C.RED, "spent": "#6A6A74", "none": "#6A6A74"}[st]
        s.rect(lx, ly, 24, 22, col if st == "flight" else "#000", rx=3, op=0.55 if st == "flight" else 0.65)
        s.rect(lx, ly, 24, 22, "none", stroke=GOLD if sel else col, rx=3, sw=2 if sel else 1.2, dash="3 2" if st == "none" else None)
        ht(lx + 12, ly + 15, str(i + 1), WHITE if st in ("loaded", "flight") else "#808088", 11, "middle", "bold")
        if st == "spent": hline(lx + 4, ly + 18, lx + 20, ly + 4, "#808088", 1)


# ── page: defs, key, the procedure ladder ──────────────────────────────────────────────────────────────────
s.raw("<defs><linearGradient id='sky' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#5B87B8'/><stop offset='1' stop-color='#BFD3E3'/></linearGradient>"
      "<linearGradient id='grass' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#6B7E4A'/><stop offset='1' stop-color='#4A5A33'/></linearGradient></defs>")
COLX = [30, 520, 1010]
ROWY = [204, 724, 1244]


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
    para(x, y + 16, scope, C.NOTE, 11, 455)
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
s.text(30, 86, "Frames are 460 × 259 (16:9). The scene is illustrative; the tank is only there so the reticle has something to rest on. The missile and its flare are world effects.", C.NOTE, 11)
s.text(30, 102, "Layout positions are PROPOSALS where the design is silent (it names what the HUD shows, not where) — each panel lists its own. Panels 1–6: devices-hud.svg · 12–15: devices-hud-drone.svg · 16–19: devices-hud-designator.svg · 20–23: devices-hud-control.svg.", C.PEND, 11)

lx = 30                                                                                          # the procedure ladder (D-93): one colour per phase, used by every panel
s.text(lx, 130, "THE PROCEDURE (D-93)", "#9FB7D8", 11, weight="bold"); lx += 142
for i, ph in enumerate(("OFF", "TEST", "STANDBY", "ARMED", "GUIDING")):
    w = text_width(ph, 11.5) + 18
    s.rect(lx, 118, w, 20, "#000", rx=4, op=0.7, stroke=PHASE[ph], sw=1.4); s.text(lx + w / 2, 132.5, ph, PHASE[ph], 11.5, "middle", "bold"); lx += w
    if i < 4:
        s.text(lx + 4, 132.5, ["POWER ▸", "test ends ▸", "ARM ▸", "FIRE ▸"][i], "#8A8A96", 10); lx += text_width(["POWER ▸", "test ends ▸", "ARM ▸", "FIRE ▸"][i], 10) + 10
s.text(lx + 6, 132.5, "the flight ends ▸ ARMED (or STANDBY, NO MISSILE) · POWER from any phase ▸ OFF", "#8A8A96", 10)
s.text(30, 156, "Phase colours: OFF grey · TEST amber · STANDBY blue · ARMED green · GUIDING red — the chip, and nothing else, carries the phase. Commands: on = set · plain = pressable · greyed = refused.", C.NOTE, 11)
s.text(30, 172, "Every chip is a HudPanel button (D-122, D-128): clickable, with its hotkey badge (D-124 — the keys are proposals). The hover frame, the cursor and RMB-LOOK are the virtual adapter's, drawn in panels 16–19.", C.NOTE, 11)

SIGHT = "ATGM SIGHT · TOW"

# ── 7a · OFF ───────────────────────────────────────────────────────────────────────────────────────────────
x, y = head(0, "7a · SIGHT UNIT, OFF — powered down (dv-sight-procedure)",
            "Scope: the dark optic, the unit's name, the OFF chip, the hint line, the commands with only POWER pressable. The compass and the unit lines are context.")
scene(x, y, 3.4); optic(x, y, dim=0.55); compass(x, y); unit_lines(x, y)
status(x, y, SIGHT, "OFF", missiles=1)
view_label(x, y, FA.TARGET, C.ORANGE, "ATGM SIGHT")
message(x, y, "SIGHT OFF", "#A0A0AA", "press POWER to switch on")
chips(x, y, (("POWER", "ready"), ("ARM", "off"), ("FIRE", "off"), ("VIEW", "off")))
legend(x, y, "the dimmed optic · name · OFF chip · MISSILES · the hint line · POWER pressable, ARM / FIRE / VIEW refused (greyed) · the READY lamp off",
       "compass · unit lines · the scene",
       ["A FIRE press before POWER does nothing — the scenario's first row (`never` a launch before ARMED); the HUD may show the refusal as a flash of the greyed chip.",
        "PROPOSAL: powered off the optic stays dim, not black, so the trainee still sees where the launcher points; the name and the phase chip stay so he knows which unit he is on."])

# ── 7b · TEST ──────────────────────────────────────────────────────────────────────────────────────────────
x, y = head(1, "7b · SIGHT UNIT, SELF-TEST — TestSeconds counting down",
            "Scope: the TEST chip, the test message with its progress bar and the seconds left, ARM refused until the test ends. The test timer is SIM time.")
scene(x, y, 3.4); optic(x, y, dim=0.35); compass(x, y); unit_lines(x, y)
status(x, y, SIGHT, "TEST", missiles=1)
view_label(x, y, FA.TARGET, C.ORANGE, "ATGM SIGHT")
cx = x + PW / 2
ht(cx, y + PH - 78, "SELF-TEST", C.AMBER, 21, "middle", "bold")
s.rect(cx - 90, y + PH - 66, 180, 8, "#000", rx=3, op=0.75, stroke="#8A8A96", sw=1); s.rect(cx - 90, y + PH - 66, 180 * 0.58, 8, C.AMBER, rx=3)
ht(cx, y + PH - 46, "2.1 s", "#C8C8D0", 11, "middle")
chips(x, y, (("POWER", "on"), ("ARM", "off"), ("FIRE", "off"), ("VIEW", "off")))
legend(x, y, "the TEST chip (amber) · SELF-TEST with a progress bar and the seconds left · POWER set · ARM / FIRE / VIEW refused until the test ends · the optic dimmed lighter than OFF",
       "compass · unit lines · the scene",
       ["TestSeconds is type data (a TOW tripod's unit tests, the Malyutka's box does not — TestSeconds 0, panel 11); this panel shows 2.1 s left of 5.0 s. The timer freezes in pause (D-93).",
        "When the test ends the unit goes to STANDBY (panel 7c).",
        "PROPOSAL: the bar and the countdown are mine; the design says the test lasts TestSeconds, not how it is shown."])

# ── 7c · STANDBY ───────────────────────────────────────────────────────────────────────────────────────────
x, y = head(2, "7c · SIGHT UNIT, STANDBY — tested, not armed (dv-sight-procedure)",
            "Scope: the STANDBY chip, the live view with the reticle, the 'ARM to fire' message, ARM pressable, FIRE refused, READY off. The compass and the unit lines are context.")
scene(x, y, 3.4); optic(x, y); reticle(x, y); compass(x, y); unit_lines(x, y)
status(x, y, SIGHT, "STANDBY", missiles=1, lamp_on=False)
view_label(x, y, FA.TARGET, C.ORANGE, "ATGM SIGHT")
message(x, y, "ARM TO FIRE", C.BLUE, "tested · FIRE is refused until armed")
chips(x, y, (("POWER", "on"), ("ARM", "ready"), ("FIRE", "off"), ("VIEW", "off")))
legend(x, y, "the STANDBY chip (blue) · the live optic with the mil reticle (he can aim already) · MISSILES 1 · 'ARM TO FIRE' (blue, the phase's colour) · POWER set · ARM pressable · FIRE and VIEW refused · the READY lamp off",
       "compass · unit lines · the scene",
       ["The unit's safe state: it is on and tested, and a FIRE press does nothing — the HUD says why (the greyed chip and the message). ARM is the one step to ARMED (panel 8) and acts in pause too (D-93) — with a round in the weapon (D-112, panel 7d).",
        "Reached by: the self-test ending (panel 7b), or POWER on the Malyutka's box, which has no self-test (TestSeconds 0, panel 11). A flight ending with a round left returns to ARMED (panel 8), not here.",
        "PROPOSAL: 'ARM TO FIRE' and the hint line are mine; the design names the phase and what is pressable."])

# ── 7d · STANDBY, no missile ───────────────────────────────────────────────────────────────────────────────
x, y = head(3, "7d · SIGHT UNIT, STANDBY with NO MISSILE — the launcher is empty",
            "Scope: the STANDBY chip, MISSILES 0 in amber, the 'NO MISSILE' message, ARM refused. The state the unit drops to when a flight ends and the weapon holds no round.")
scene(x, y, 3.4); optic(x, y); reticle(x, y); compass(x, y); unit_lines(x, y)
status(x, y, SIGHT, "STANDBY", missiles=0, lamp_on=False)
view_label(x, y, FA.TARGET, C.ORANGE, "ATGM SIGHT")
message(x, y, "NO MISSILE", C.AMBER, "the weapon holds no round")
chips(x, y, (("POWER", "on"), ("ARM", "off"), ("FIRE", "off"), ("VIEW", "off")))
legend(x, y, "the STANDBY chip (blue) · MISSILES 0 and 'NO MISSILE' in amber · the weapon holds no round · POWER set · ARM, FIRE and VIEW refused · the READY lamp off",
       "compass · unit lines · the scene",
       ["D-93: when the flight ends the unit returns to ARMED if a round is left, else to STANDBY with 'NO MISSILE' — this is that screen, so the trainee sees an empty launcher, not a broken sight unit.",
        "D-112: ARM is refused while the weapon is empty — the chip greyed, nothing queued (in pause too); once a round is loaded this reads as panel 7c again.",
        "Judged by dv-sight-procedure (the screen after its one round's flight) and dv-sight-empty (a launcher authored empty: ARM never takes). PROPOSAL: the amber is the HUD's warning colour (as MISSILES 0 in panel 9)."])

# ── 8 · ARMED ──────────────────────────────────────────────────────────────────────────────────────────────
x, y = head(4, "8 · ARMED on a tank — READY to fire (dv-sight-procedure)",
            "Scope: the ARMED chip, the READY lamp, the mil reticle, MISSILES, the message line, FIRE pressable. The compass and the unit lines are context.")
scene(x, y, 3.4); optic(x, y); reticle(x, y); compass(x, y); unit_lines(x, y)
status(x, y, SIGHT, "ARMED", missiles=1, lamp_on=True)
view_label(x, y, FA.TARGET, C.ORANGE, "ATGM SIGHT")
message(x, y, "MISSILE 1 READY", GOLD, "FIRE launches · look to aim")
chips(x, y, (("POWER", "on"), ("ARM", "on"), ("FIRE", "ready"), ("VIEW", "off")))
legend(x, y, "the ARMED chip (green) · READY lamp (green: armed, a round in the weapon, the weapon deployed, not paused) · mil reticle (10×, 1 mil = 3 px) · MISSILES 1 · 'MISSILE 1 READY' (gold, the station's readout colour) · FIRE pressable",
       "compass · unit lines · the scene",
       ["READY is the device's `FireControl.Ready` (D-93): in pause, or with the weapon stowed, the lamp is off and FIRE is greyed — the press is refused, never queued (D-78). Judged: `hud` text contains ARMED (dv-sight-procedure).",
        "The reticle IS the guide line: the missile will fly eye → the crosshair the trainee holds (D-96) — the trainee aims by looking, the sight's frame carries the look.",
        "PROPOSAL: the lamp top-right, the message bottom-centre and the chips bottom-right follow the rangefinder's layout (panel 2) so the two units read alike."])

# ── 9 · GUIDING ────────────────────────────────────────────────────────────────────────────────────────────
x, y = head(5, "9 · GUIDING — the missile in flight, the trainee steers (dv-steer-in-flight)",
            "Scope: the GUIDING chip, the gold reticle (the line the missile flies), IN FLIGHT with the time of flight, MISSILES, VIEW pressable, FIRE refused.")
scene(x, y, 3.4); optic(x, y); flare(x, y, -62, 5); reticle(x, y, c=GOLD); compass(x, y); unit_lines(x, y)
status(x, y, SIGHT, "GUIDING", missiles=0, lamp_on=False)
view_label(x, y, FA.TARGET, C.ORANGE, "ATGM SIGHT")
message(x, y, "IN FLIGHT  T+ 6.4 s", C.RED, "hold the crosshair on the target")
chips(x, y, (("POWER", "on"), ("ARM", "on"), ("FIRE", "off"), ("VIEW", "ready")))
legend(x, y, "the GUIDING chip (red) · the reticle turned gold (it is live: the round follows it) · 'IN FLIGHT' with the time of flight (counts only while time advances) · MISSILES 0 (amber) · READY off · FIRE refused · VIEW pressable",
       "compass · unit lines · the scene · the flare and its trail (the round, a world effect)",
       ["In pause the time of flight stands still and the gold reticle still follows the look (observation) — the command the round reads does not move (D-83, D-96). Judged: `hud` text contains IN FLIGHT.",
        "The flight ends ▸ ARMED (a missile left, panel 8) or STANDBY with 'NO MISSILE' (panel 7d).",
        "PROPOSAL: the gold reticle, the time-of-flight counter and the hint line are mine; the design names 'IN FLIGHT', the time of flight and the missiles left (D-100)."])

# ── 10 · the missile's view ────────────────────────────────────────────────────────────────────────────────
x, y = head(6, "10 · THE MISSILE'S VIEW — VIEW pressed in flight (dv-steer-view-toggle)",
            "Scope: the same HUD over the chase picture: the VIEW ▸ MISSILE label, the aim offset of the shooter's crosshair, IN FLIGHT, VIEW pressable to go back.")
chase(x, y)
unit_lines(x, y)
status(x, y, SIGHT, "GUIDING", missiles=0, lamp_on=False)
view_label(x, y, ROCKET, C.RED, "MISSILE")
rx, ry = x + PW / 2 + 38, y + PH / 2 - 14                                                         # the shooter's crosshair, as the missile sees it: a ring beside the tank
s.raw(f"<circle cx='{rx}' cy='{ry}' r='9' fill='none' stroke='#000' stroke-width='3.4' stroke-opacity='0.6'/><circle cx='{rx}' cy='{ry}' r='9' fill='none' stroke='{GOLD}' stroke-width='1.6' stroke-dasharray='4 2.5'/>")
s.raw(f"<circle cx='{rx}' cy='{ry}' r='1.5' fill='{GOLD}'/>")
ht(rx + 13, ry - 6, "AIM", GOLD, 10.5, weight="bold")
message(x, y, "IN FLIGHT  T+ 6.4 s", C.RED, "AIM  1.8° R · 0.6° U")
chips(x, y, (("POWER", "on"), ("ARM", "on"), ("FIRE", "off"), ("VIEW", "on")))
legend(x, y, "the VIEW ▸ MISSILE label (rocket glyph) · the AIM ring: where the shooter's crosshair is, relative to the missile's heading · its offset in degrees · IN FLIGHT with the time of flight · VIEW set (press it to go back to the shooter)",
       "the unit lines · the picture and the round in it (world effects)",
       ["Crosshair mode (D-90): the missile's view is a WATCH — the stick still moves the shooter's crosshair; the AIM ring shows the offset and the round turns toward it. In MCLOS the stick turns the round itself (panel 11).",
        "The view stays bound to the sight (D-109): the HUD above is the sight's, only the camera is on the round; the round's end (`Lost`) gives the camera back to the shooter. Works in pause (D-91): the camera can jump to the frozen round and back.",
        "PROPOSAL: the sight's optic mask and the mil reticle are not drawn here (the picture is the missile's, not the sight's); the station's compass strip is not drawn either (it would show the man's heading, not the round's); the AIM ring and its degrees line are mine."])

# ── 11 · the MCLOS box ─────────────────────────────────────────────────────────────────────────────────────
x, y = head(7, "11 · THE 9K11 BOX, MCLOS flight — the stick steers the round",
            "Scope: the 9S415 control box (D-155: a separate cable box commanding up to four launchers; dv-steer-manual, dv-remote-launcher), GUIDING, the launcher lamps, the stick gauge and its numbers, IN FLIGHT. The reticle is the boresight.")
scene(x, y, 3.8, tank_dx=2); optic(x, y); flare(x, y, -50, 17, caption=False); reticle(x, y, mil_px=3.4); compass(x, y); unit_lines(x, y)
status(x, y, "9S415 · 9K11 CONTROL BOX", "GUIDING", lamp_on=False)
launchers(x, y, (("flight", True), ("loaded", False), ("loaded", False), ("none", False)))
mode_switch_refused(x, y)
view_label(x, y, FA.TARGET, C.ORANGE, "9S415")
gx, gy, gs = x + PW - 96, y + PH - 128, 80                                                        # the stick gauge: the deflection the device reads (axes 0 / 1)
s.rect(gx, gy, gs, gs, "#000", rx=3, op=0.6, stroke="#8A8A96", sw=1)
hline(gx + gs / 2, gy + 6, gx + gs / 2, gy + gs - 6, "#8A8A96", 0.9); hline(gx + 6, gy + gs / 2, gx + gs - 6, gy + gs / 2, "#8A8A96", 0.9)
s.raw(f"<circle cx='{gx + gs / 2}' cy='{gy + gs / 2}' r='{gs / 2 - 8}' fill='none' stroke='#8A8A96' stroke-width='0.8' stroke-dasharray='3 3'/>")
sx_, sy_ = gx + gs / 2 + 0.35 * (gs / 2 - 8), gy + gs / 2 - 0.20 * (gs / 2 - 8)
hline(gx + gs / 2, gy + gs / 2, sx_, sy_, GOLD, 1.4)
s.raw(f"<circle cx='{sx_}' cy='{sy_}' r='5' fill='{GOLD}' stroke='#000' stroke-width='1'/>")
ht(gx + gs / 2, gy - 5, "STICK", "#C8C8D0", 10.5, "middle", "bold")
message(x, y, "IN FLIGHT  T+ 9.8 s", C.RED, "LAUNCHER 1 · turn  X +0.35 · Y +0.20")
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "off"), ("ZOOM", "ready"), ("VIEW", "ready")), sz=9.5)
legend(x, y, "the box's name · GUIDING (red) · the LAUNCHER lamps (1 in flight and selected, 2 and 3 loaded, 4 not connected) · the STICK gauge: the deflection the device reads (axes 0 / 1) and the same two numbers on the message line · IN FLIGHT with the time of flight · the MODE switch GREYED on SINGLE (a launcher offers SINGLE only, D-157) · the six chips of D-154 as HudPanel buttons: SELECT refused while a round flies",
       "compass · unit lines · the scene · the flare and its trail",
       ["D-155 refines D-97: the 9S415 is a CONTROL UNIT (panel 22 shows it ARMED, selecting a launcher), not a sight mounted on the launcher; the Malyutka launchers are uncrewed and fire from its console. The box has no TEST phase: POWER goes straight to STANDBY (D-154).",
        "MCLOS (D-97): the round turns at stick × its turn rate and never looks at the target; the trainee watches the flare against the target and corrects. Here the flare is low-left of the target and the stick is pushed up-right. In pause the gauge still shows the stick (observation) but the command the round reads stays frozen (D-83).",
        "PROPOSAL: the gauge's place and look, the lamp strip, the signed numbers, the narrower field (a Malyutka's 8×) and the hotkeys (D-124: the type names them) are mine. The VIEW press shows the missile's picture as in panel 10, the gauge stays."])

os.makedirs("docs/designs/devices/mockups", exist_ok=True)
s.save("docs/designs/devices/mockups/devices-hud-sight.svg")
print("wrote docs/designs/devices/mockups/devices-hud-sight.svg")
