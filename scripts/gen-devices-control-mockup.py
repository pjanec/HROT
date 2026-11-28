#!/usr/bin/env python3
"""Mockup for docs/designs/devices/DESIGN_Simulated_Devices.md §3.13f - the HUD of the CONTROL UNIT that operates weapons it is not mounted on: the
remote weapon station (RWS) console and the 9K11 launcher box (SD-S4c, D-147..D-156; R-4138 / R-4143), panels 20-23. Built on scripts/mockup_kit.py, in
the house style of the other device sheets (gen-devices-drone-mockup.py, gen-devices-designator-mockup.py).

    python3 scripts/gen-devices-control-mockup.py      # -> docs/designs/devices/mockups/devices-hud-control.svg

SCOPE (R-4151, what this picture specifies and what it does not):
  SPECIFIES  what ControlUnitHudGizmo draws in a view bound to the console while the camera rides the selected weapon's sight (DeviceView WeaponSight,
             D-152): the name and phase chip, the selected weapon's lines (ammo, fire mode, azimuth / elevation of the ACTUAL aim), the reticle, the
             LAY marker (where the command points, relative to the actual aim - the drives' lag made visible), the READY / FIRING lamp, the zoom step,
             the message line, the launcher lamps of the box, and the six command chips (POWER SELECT ARM FIRE ZOOM VIEW, D-154) as HudPanel buttons
             with hotkey badges (D-122 / D-124).
  CONTEXT    drawn dashed / greyed and tagged "unchanged": the station's compass strip, the operator's unit lines, the toolbar's run controls (panel 23).
             The picture, the tracers, the impacts and the target are world effects, illustrative.
  PROPOSAL   every layout position is a proposal where the design is silent (the design names WHAT the HUD shows, not WHERE).
Panel numbers continue the device sheets (1-6, 7-11, 12-15, 16-19).
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

PLAY = 0xf04b
W, H = 1460, 1410
PW, PH = 640, 360                     # one view frame (16:9)
GOLD, WHITE = "#FFD700", "#FFFFFF"
PHASE = {"OFF": "#A0A0AA", "STANDBY": C.BLUE, "ARMED": C.GREEN}      # the console's phases (D-154); the colours of the sight unit's sheet
KEYS = {"POWER": "P", "SELECT": "S", "ARM": "A", "FIRE": "F", "ZOOM": "Z", "VIEW": "V"}   # PROPOSAL: the type names its hotkeys (D-124)
s = Svg(W, H, "Simulated devices — the CONTROL UNIT: the remote weapon station and the 9K11 launcher box  (SD-S4c, DESIGN_Simulated_Devices §3.13f)",
        "Full colour = SPECIFIED by this mockup · grey dashed + 'unchanged' = CONTEXT, not specified · the picture, the tracers and the target are world effects, illustrative.")


def esc(t): return str(t).replace("&", "&amp;").replace("<", "&lt;")


def ht(x, y, t, c=WHITE, sz=12, anchor="start", weight="normal", op=1):
    """HUD text with the station's dark halo (legible on any background)."""
    s.raw(f"<text x='{x}' y='{y}' fill='{c}' font-size='{sz}' font-family='Roboto, Segoe UI, Arial, sans-serif' text-anchor='{anchor}' font-weight='{weight}' "
          f"stroke='#000' stroke-width='2.2' stroke-opacity='0.6' paint-order='stroke' opacity='{op}' xml:space='preserve'>{esc(t)}</text>")


def hline(x1, y1, x2, y2, c=WHITE, sw=1.2):
    s.line(x1, y1, x2, y2, "#000", sw + 2.2); s.line(x1, y1, x2, y2, c, sw)


_n = [0]


def scene(x, y, k, target="truck"):
    """The picture from the weapon's sight (illustrative): a ridge, a field, a tree line, a farm and ONE target under the reticle, magnified by k."""
    _n[0] += 1; cid = f"k{_n[0]}"
    cx, cy = x + PW / 2, y + PH / 2
    s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}'/></clipPath><g clip-path='url(#{cid})'>")
    s.raw(f"<g transform='translate({cx},{cy}) scale({k}) translate({-cx},{-cy})'>")
    s.raw(f"<rect x='{x - 500}' y='{y - 400}' width='{PW + 1000}' height='{PH * 0.55 + 400}' fill='url(#sky)'/>")
    hz = y + PH * 0.55
    s.raw(f"<polygon points='{x - 500},{hz} {x + 120},{hz - 14} {x + 300},{hz - 4} {x + 470},{hz - 18} {x + PW + 500},{hz - 5} {x + PW + 500},{hz}' fill='#4C5E49'/>")
    s.raw(f"<rect x='{x - 500}' y='{hz}' width='{PW + 1000}' height='500' fill='url(#grass)'/>")
    for i in range(-20, 40): s.raw(f"<circle cx='{x + i * 13}' cy='{hz - 3}' r='7' fill='#2F4A2E'/>")
    s.rect(x + 410, hz - 13, 26, 14, "#A59A86"); s.raw(f"<polygon points='{x + 406},{hz - 13} {x + 423},{hz - 23} {x + 440},{hz - 13}' fill='#7A3B30'/>")
    if target == "tank":
        s.rect(cx - 14, cy - 2, 28, 8, "#5B6350", rx=1.5); s.rect(cx - 7, cy - 6, 14, 5, "#4B5342", rx=1.5); s.line(cx + 6, cy - 4, cx + 22, cy - 5, "#4B5342", 1.6)
    else:                                                                                       # a truck: box, cab, wheels
        s.rect(cx - 17, cy - 8, 22, 13, "#6A6E58", rx=1); s.rect(cx + 5, cy - 5, 11, 10, "#56604A", rx=1.5); s.rect(cx + 11, cy - 3.5, 4, 3.5, "#9FB4C4")
        s.raw(f"<circle cx='{cx - 10}' cy='{cy + 5}' r='2.8' fill='#23262A'/><circle cx='{cx + 10}' cy='{cy + 5}' r='2.8' fill='#23262A'/>")
    s.raw("</g></g>")
    s.rect(x, y, PW, PH, "none", stroke=C.BORDER)


def brackets(x, y):
    """The weapon camera's frame: corner brackets only, nothing darkened."""
    m = 14
    for sx, sy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        px, py = x + m + sx * (PW - 2 * m), y + m + sy * (PH - 2 * m)
        dx, dy = (1 if sx == 0 else -1) * 20, (1 if sy == 0 else -1) * 20
        hline(px, py, px + dx, py, WHITE, 1.4); hline(px, py, px, py + dy, WHITE, 1.4)


def optic(x, y):
    """The box's own sight (GuideFrom = Self, D-153): one round eyepiece, the view darkened outside it."""
    cx, cy, r = x + PW / 2, y + PH / 2, PH * 0.47
    _n[0] += 1; mid = f"km{_n[0]}"
    s.raw(f"<mask id='{mid}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='#fff'/><circle cx='{cx}' cy='{cy}' r='{r}' fill='#000'/></mask>"
          f"<rect x='{x}' y='{y}' width='{PW}' height='{PH}' fill='#000' opacity='0.93' mask='url(#{mid})'/>")


def reticle(x, y, mil_px=4.0):
    """A gapped cross, a centre dot and mil ticks every 5 mil (the lower arm stops above the message line)."""
    cx, cy = x + PW / 2, y + PH / 2
    for sgn in (-1, 1):
        hline(cx + sgn * 8, cy, cx + sgn * 110, cy); hline(cx, cy + sgn * 8, cx, cy + (62 if sgn > 0 else -110))
    for m in range(5, 30, 5):
        d = m * mil_px; big = m % 10 == 0
        for sgn in (-1, 1):
            hline(cx + sgn * d, cy - (5 if big else 3), cx + sgn * d, cy + (5 if big else 3), WHITE, 1)
            if sgn < 0 or d <= 62: hline(cx - (5 if big else 3), cy + sgn * d, cx + (5 if big else 3), cy + sgn * d, WHITE, 1)
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='1.8' fill='{WHITE}' stroke='#000' stroke-width='0.8'/>")


def lay_marker(x, y, dx, dy, tag=None):
    """The LAY marker (D-152): where the command (FireControl.LayDir) points, relative to the ACTUAL aim at the centre - the drives lag the command."""
    cx, cy = x + PW / 2, y + PH / 2
    mx, my = cx + dx, cy + dy
    s.raw(f"<line x1='{cx}' y1='{cy}' x2='{mx}' y2='{my}' stroke='#000' stroke-opacity='0.55' stroke-width='3.6'/>"
          f"<line x1='{cx}' y1='{cy}' x2='{mx}' y2='{my}' stroke='{GOLD}' stroke-width='1.2' stroke-dasharray='3 3'/>")
    s.raw(f"<circle cx='{mx}' cy='{my}' r='9' fill='none' stroke='#000' stroke-width='3.6' stroke-opacity='0.6'/><circle cx='{mx}' cy='{my}' r='9' fill='none' stroke='{GOLD}' stroke-width='1.6' stroke-dasharray='4 2.5'/>")
    s.raw(f"<circle cx='{mx}' cy='{my}' r='1.6' fill='{GOLD}'/>")
    ht(mx + 13, my - 7, "LAY", GOLD, 10.5, weight="bold")
    if tag:
        w = text_width(tag, 10) + 12
        s.rect(mx + 13, my - 1, w, 15, "#000", rx=3, op=0.7, stroke=C.PEND, sw=1); s.text(mx + 13 + w / 2, my + 10, tag, C.PEND, 10, "middle", "bold")


def tracers(x, y):
    """CONTEXT - the burst seen from the weapon's sight and the hits on the target: WORLD effects (rounds and impacts), not the HUD."""
    cx, cy = x + PW / 2, y + PH / 2
    for i, (sx, sy) in enumerate(((86, 96), (74, 104), (95, 84), (80, 90), (66, 112))):
        t0, t1 = 0.30 + 0.07 * i, 0.50 + 0.07 * i
        ax, ay = cx + 3 + sx * (1 - t0), cy + 2 + sy * (1 - t0)
        bx, by = cx + 3 + sx * (1 - t1), cy + 2 + sy * (1 - t1)
        s.raw(f"<line x1='{ax}' y1='{ay}' x2='{bx}' y2='{by}' stroke='#FFB23C' stroke-width='2.2' stroke-opacity='0.9'/><line x1='{ax}' y1='{ay}' x2='{bx}' y2='{by}' stroke='#FFF3C0' stroke-width='0.9'/>")
    for ox, oy in ((-6, -2), (5, 1), (-1, 4)):
        s.raw(f"<circle cx='{cx + ox}' cy='{cy + oy}' r='3.4' fill='#FF8A1E' fill-opacity='0.55'/><circle cx='{cx + ox}' cy='{cy + oy}' r='1.4' fill='#FFF1B0'/>")
    s.text(x + PW / 2 + 100, y + PH / 2 + 112, "tracers and impacts — world effects", "#C8C8D0", 9.5)


def lamp(x, y, on, label="READY", c=C.GREEN):
    lx, ly = x + PW - 50, y + 52
    ht(lx - 12, ly + 5, label, c if on else "#A0A0AA", 12, "end", "bold")
    if on: s.raw(f"<circle cx='{lx}' cy='{ly}' r='11' fill='#000' opacity='0.45'/><circle cx='{lx}' cy='{ly}' r='10' fill='{c}' opacity='0.28'/>")
    s.raw(f"<circle cx='{lx}' cy='{ly}' r='6' fill='{c if on else '#3A3A44'}' stroke='#000' stroke-width='1.5'/>")


def status(x, y, name, phase, rows=()):
    """Top-left: the console's name, the phase chip (the sight sheet's colours), then the selected weapon's lines (label, value, value colour)."""
    ht(x + 40, y + 50, name, "#C8C8D0", 12, weight="bold")
    c = PHASE[phase]; w = text_width(phase, 12) + 20
    s.rect(x + 40, y + 58, w, 22, "#000", rx=4, op=0.7, stroke=c, sw=1.4); ht(x + 40 + w / 2, y + 74, phase, c, 12, "middle", "bold")
    for i, (lab, val, vc) in enumerate(rows):
        ht(x + 40, y + 102 + i * 18, lab, "#A0A0AA", 10.5); ht(x + 108, y + 102 + i * 18, val, vc, 12, weight="bold")


def view_label(x, y, name, sub="through the weapon's sight"):
    lw = text_width("VIEW  " + name, 11) + 34
    lx = x + PW - 50 - lw + 38
    s.rect(lx, y + 68, lw, 20, "#000", rx=3, op=0.7); s.glyph(FA.GAMEPAD, lx + 6, y + 71, C.VIOLET, 13)
    s.text(lx + 24, y + 82.5, "VIEW", "#A0A0AA", 10.5); s.text(lx + 56, y + 82.5, name, WHITE, 11, weight="bold")
    ht(lx + lw, y + 102, sub, "#A0A0AA", 9.5, "end")


def zoom_readout(x, y, step):
    ht(x + PW - 14, y + 124, step, WHITE, 13, "end", "bold"); ht(x + PW - 14 - text_width(step, 13) - 6, y + 124, "ZOOM", "#A0A0AA", 10.5, "end")


def message(x, y, big, bigc, sub=None):
    cx = x + PW / 2
    ht(cx, y + PH - 100, big, bigc, 25, "middle", "bold")
    if sub: ht(cx, y + PH - 80, sub, "#C8C8D0", 11.5, "middle")


def chips(x, y, states, hover=None):
    """The six command chips, bottom-right: HudPanel buttons (D-122) with their hotkey badges (D-124). on = set (blue), ready = pressable, off = refused (greyed)."""
    xx = x + PW - 14
    for nm, st in reversed(states):
        w = text_width(nm, 11) + 27; xx -= w
        if st == "on": s.rect(xx, y + PH - 50, w, 24, "#3369AD", rx=3, op=0.95)
        else: s.rect(xx, y + PH - 50, w, 24, "#000", rx=3, op=0.6, stroke="#8A8A96" if st == "ready" else None, sw=1)
        s.text(xx + 7, y + PH - 33, nm, WHITE if st == "on" else ("#E0E0E8" if st == "ready" else "#6A6A74"), 11, weight="bold" if st == "on" else "normal")
        kc = "#6A6A74" if st == "off" else "#C8C8D0"
        s.rect(xx + w - 16, y + PH - 44, 11, 13, "none", stroke=kc, rx=2, sw=0.9); s.text(xx + w - 10.5, y + PH - 34, KEYS[nm], kc, 9, "middle", "bold")
        xx -= 3


def launchers(x, y, states):
    """The 9K11 box's launcher lamps (D-155, up to four): (state, selected). loaded green / in flight red / spent greyed with a cross / not connected dashed; the selected one framed gold."""
    ht(x + 40, y + 102, "LAUNCHERS", "#A0A0AA", 10.5)
    for i, (st, sel) in enumerate(states):
        lx, ly = x + 40 + i * 38, y + 110
        col = {"loaded": C.GREEN, "flight": C.RED, "spent": "#6A6A74", "none": "#6A6A74"}[st]
        s.rect(lx, ly, 32, 28, col if st == "flight" else "#000", rx=3, op=0.55 if st == "flight" else 0.65)
        s.rect(lx, ly, 32, 28, "none", stroke=GOLD if sel else col, rx=3, sw=2.2 if sel else 1.2, dash="3 2" if st == "none" else None)
        ht(lx + 16, ly + 19, str(i + 1), WHITE if st in ("loaded", "flight") else "#808088", 13, "middle", "bold")
        if st == "spent": hline(lx + 5, ly + 23, lx + 27, ly + 5, "#808088", 1.2)


def compass_ctx(x, y):
    s.rect(x + PW / 2 - 110, y + 4, 220, 18, "none", stroke="#8A8A96", dash="4 3"); s.text(x + PW / 2 + 110, y + 1, "unchanged — compass", "#8A8A96", 9, "end")


def unit_lines(x, y):
    bx, by = x + 10, y + PH - 58
    s.rect(bx, by, 132, 48, "none", stroke="#8A8A96", dash="4 3"); s.text(bx + 132, by - 3, "unchanged — unit lines", "#8A8A96", 9.5, "end")
    for i in range(3): s.rect(bx + 8, by + 10 + i * 12, 100 - i * 18, 4, "#8A8A96", op=0.7)


# ── page ───────────────────────────────────────────────────────────────────────────────────────────────
s.raw("<defs><linearGradient id='sky' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#5B87B8'/><stop offset='1' stop-color='#BFD3E3'/></linearGradient>"
      "<linearGradient id='grass' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#6B7E4A'/><stop offset='1' stop-color='#4A5A33'/></linearGradient></defs>")
COLX = [30, 750]
ROWY = [150, 780]


def wrap(t, w, sz):
    out, cur = [], ""
    for word in t.split(" "):
        trial = (cur + " " + word).strip()
        if cur and text_width(trial, sz) > w: out.append(cur); cur = word
        else: cur = trial
    return out + [cur]


def para(x, y, t, c, sz=10.5, w=630, lead=13):
    for l in wrap(t, w, sz): s.text(x, y, l, c, sz); y += lead
    return y


def head(i, title, scope):
    x, y = COLX[i % 2], ROWY[i // 2]
    s.text(x, y, title, "#E8E8EE", 13.5, weight="bold")
    para(x, y + 16, scope, C.NOTE, 11, 630)
    return x, y + 38


def legend(x, y, specs, ctx, notes=(), dy=0):
    yy = y + PH + 16 + dy
    s.text(x, yy, "SPECIFIES", C.GREEN, 10.5, weight="bold"); yy = para(x + 66, yy, specs, C.SUB, 11, 560, 13.5)
    s.text(x, yy + 2, "CONTEXT", "#8A8A96", 10.5, weight="bold"); yy = para(x + 66, yy + 2, ctx, C.NOTE, 11, 560, 13.5)
    yy += 4
    for n in notes: yy = para(x, yy, n, C.PEND if n.startswith("PROPOSAL") else C.NOTE, 10.5, 630) + 2
    return yy


s.rect(30, 56, 12, 12, "#000", stroke=WHITE, sw=1); s.text(48, 66, "specified HUD element (full colour, dark halo — the station's HudText over the view)", C.SUB, 11)
s.rect(600, 56, 12, 12, "none", stroke="#8A8A96", dash="4 3"); s.text(618, 66, "context — unchanged, not specified here", C.SUB, 11)
s.text(30, 88, "Frames are 640 × 360 (16:9), the view riding the selected weapon's sight (DeviceView WeaponSight, D-152). Layout positions are PROPOSALS where the design is silent.", C.PEND, 11)
lx = 30                                                                                          # the console's procedure ladder (D-154)
s.text(lx, 114, "THE CONSOLE PROCEDURE (D-154)", "#9FB7D8", 11, weight="bold"); lx += 196
for i, ph in enumerate(("OFF", "STANDBY", "ARMED")):
    w = text_width(ph, 11.5) + 18
    s.rect(lx, 102, w, 20, "#000", rx=4, op=0.7, stroke=PHASE[ph], sw=1.4); s.text(lx + w / 2, 116.5, ph, PHASE[ph], 11.5, "middle", "bold"); lx += w
    if i < 2:
        s.text(lx + 4, 116.5, ["POWER ▸", "ARM ▸"][i], "#8A8A96", 10); lx += text_width(["POWER ▸", "ARM ▸"][i], 10) + 10
s.text(lx + 8, 116.5, "SELECT steps the weapon in STANDBY or ARMED · ARM needs the selected weapon loaded (D-112) · FIRE per press or held, by the weapon's kind", "#8A8A96", 10)
s.text(30, 136, "Every chip is a HudPanel button (D-122): clickable, with its hotkey badge (D-124 — the keys are proposals). The hover frame, the cursor and RMB-LOOK are the virtual adapter's (panels 16–19). For a console the look IS the lay (D-151).", C.NOTE, 11)

# ── 20 · RWS armed, ready ──────────────────────────────────────────────────────────────────────────────
x, y = head(0, "20 · RWS CONSOLE, ARMED — the view through the gun's camera, the lay marker (dv-rws-mg)",
            "Scope: the weapon-sight view with the reticle, the LAY marker, ammo, the fire mode, the aim readout, ZOOM, the READY lamp, the six chips. The compass and the unit lines are context.")
scene(x, y, 2.4); brackets(x, y); reticle(x, y); lay_marker(x, y, 58, -24); compass_ctx(x, y); unit_lines(x, y)
status(x, y, "RWS CONSOLE", "ARMED", (("WEAPON", "7.62 MM MG", WHITE), ("AMMO", "187", WHITE), ("FIRE MODE", "BURST", WHITE), ("AIM", "AZ 047°  EL +2.5°", "#C8C8D0")))
lamp(x, y, True); view_label(x, y, "RWS CONSOLE"); zoom_readout(x, y, "4×")
message(x, y, "READY TO FIRE", GOLD, "hold FIRE to fire · the stick slews the gun")
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "ready"), ("ZOOM", "ready"), ("VIEW", "off")))
legend(x, y, "the ARMED chip (green) · the selected weapon's lines: name, AMMO, FIRE MODE, and AZ / EL of the ACTUAL aim (the replicated `TurretPose`) · the reticle at the weapon's aim · the LAY marker: the commanded direction (`FireControl.LayDir`) relative to the actual aim, joined to the reticle by a dashed line — the gun is still turning toward it · "
             "READY lamp (armed, a round, not paused) · ZOOM 4× · SELECT greyed (one weapon), VIEW greyed (a gun has no flight to watch)",
       "the compass · the unit lines · the picture and the truck (the weapon's camera, world)",
       ["The camera is on the WEAPON's turret and looks along its actual aim, so the operator sees the real drive lag (D-152): the lay marker converges on the reticle as the turret arrives. The stick commands a slew RATE, never a position (D-151).",
        "D-156 names SINGLE / BURST on this HUD, but D-154 gives the console no fire-mode channel: FIRE MODE here is a read-out of the weapon's mode (a per-press weapon reads SINGLE, an automatic one BURST). A mode switch would need a channel — open.",
        "PROPOSAL: the weapon lines, the AZ / EL readout, the marker's look, the corner brackets and the layout are mine; the design names what the HUD shows, not where."])

# ── 21 · RWS firing, held ──────────────────────────────────────────────────────────────────────────────
x, y = head(1, "21 · RWS CONSOLE, FIRING — FIRE held, the burst and the ammo counting down (dv-rws-mg)",
            "Scope: the FIRING lamp, FIRE set, the ammo falling, the lay marker converged on the aim, the message. Tracers and impacts are world effects.")
scene(x, y, 2.4); tracers(x, y); brackets(x, y); reticle(x, y); lay_marker(x, y, 8, -3); compass_ctx(x, y); unit_lines(x, y)
status(x, y, "RWS CONSOLE", "ARMED", (("WEAPON", "7.62 MM MG", WHITE), ("AMMO", "142", C.AMBER), ("FIRE MODE", "BURST", WHITE), ("AIM", "AZ 047°  EL +2.5°", "#C8C8D0")))
lamp(x, y, True, "FIRING", C.RED); view_label(x, y, "RWS CONSOLE"); zoom_readout(x, y, "4×")
message(x, y, "FIRING", C.RED, "FIRE held — release to stop")
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "on"), ("ZOOM", "ready"), ("VIEW", "off")))
legend(x, y, "the lamp turned red and relabelled FIRING (`FireControl.Trigger` held) · the FIRE chip set (blue) while the key or the button is down · AMMO falling (amber below a quarter) · the lay marker nearly on the reticle: the gun has arrived · 'FIRING' with the release hint",
       "the compass · the unit lines · the tracers and the impacts on the truck (the rounds, world)",
       ["An automatic weapon fires while the trigger is HELD; a per-press launcher would fire once per press and the chip would flash instead (D-149, `FiresPerPress`). The weapon's OWN brain fires — the console only raises `Trigger` (D-149, D-154).",
        "PROPOSAL: the amber ammo threshold (a quarter of the belt) and the FIRING relabel are mine; the design names the held trigger, not its look."])

# ── 22 · the 9K11 box ──────────────────────────────────────────────────────────────────────────────────
x, y = head(2, "22 · THE 9K11 BOX, ARMED — four launcher lamps, SELECT (dv-remote-launcher, dv-box-two-launchers)",
            "Scope: the box's own sight (GuideFrom = Self), the launcher lamps with the selected one framed, ARMED, READY, the six chips. The flight itself is panel 11 — the same box, GUIDING.")
scene(x, y, 3.4, target="tank"); optic(x, y); reticle(x, y); compass_ctx(x, y); unit_lines(x, y)
status(x, y, "9S415 · 9K11 CONTROL BOX", "ARMED")
launchers(x, y, (("spent", False), ("loaded", True), ("loaded", False), ("none", False)))
lamp(x, y, True); view_label(x, y, "9K11 BOX", "through its own sight"); zoom_readout(x, y, "8×")
message(x, y, "LAUNCHER 2 READY", GOLD, "SELECT steps the launcher · FIRE launches")
chips(x, y, (("POWER", "on"), ("SELECT", "ready"), ("ARM", "on"), ("FIRE", "ready"), ("ZOOM", "ready"), ("VIEW", "off")))
legend(x, y, "the box's name and the ARMED chip · the LAUNCHER lamps: 1 spent (greyed, crossed), 2 loaded and SELECTED (gold frame), 3 loaded, 4 not connected (dashed) · the READY lamp for the selected launcher · 'LAUNCHER 2 READY' · SELECT pressable (steps 2 → 3), FIRE pressable, VIEW greyed until a round flies",
       "the compass · the unit lines · the picture and the tank",
       ["The box commands up to four uncrewed launchers on cables (D-155); each `Selected` index is a launcher of `ControlledEntities` (D-147). ARM needs the SELECTED launcher loaded (D-154, D-112): selecting the spent launcher 1 would show NO MISSILE and ARM refused.",
        "The view is the box's own sight, not the launcher's camera (`GuideFrom = Self`, D-153): the round flies the line of the sight he looks through, so the reticle is the guide line, as for the TOW sight unit (panel 8).",
        "D-156 lists this panel as 'ARMED, a guided flight'; the flight half is drawn as panel 11 (the same chrome: lamp 1 in flight, SELECT refused, the stick gauge) so the two do not repeat each other.",
        "PROPOSAL: the lamp strip's place and look, the 8× field and the hotkeys are mine."])

# ── 23 · paused ────────────────────────────────────────────────────────────────────────────────────────
x, y = head(3, "23 · RWS CONSOLE, PAUSED — FIRE and the lay held, the view still answers (dv-rws-paused)",
            "Scope: the same HUD in pause: FIRE greyed, the lay marker held where it was (the stick is ignored), ZOOM still acting. The toolbar's paused state is context.")
scene(x, y, 3.6); brackets(x, y); reticle(x, y); lay_marker(x, y, 70, -28, tag="LAY HELD"); compass_ctx(x, y); unit_lines(x, y)
status(x, y, "RWS CONSOLE", "ARMED", (("WEAPON", "7.62 MM MG", WHITE), ("AMMO", "187", WHITE), ("FIRE MODE", "BURST", WHITE), ("AIM", "AZ 047°  EL +2.5°", "#C8C8D0")))
lamp(x, y, False); view_label(x, y, "RWS CONSOLE"); zoom_readout(x, y, "8×")
message(x, y, "FIRE HELD IN PAUSE", C.AMBER, "ZOOM · SELECT · VIEW still act")
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "off"), ("ZOOM", "ready"), ("VIEW", "off")))
ty = y + PH + 8
s.rect(x, ty, 300, 24, C.PAGE, stroke="#8A8A96", dash="4 3", rx=3)
s.glyph(0xf111, x + 8, ty + 5, "#E63838", 13); s.glyph(PLAY, x + 40, ty + 5, "#E63838", 13)
s.text(x + 64, ty + 16, "toolbar, paused: ▶ shown (context)", C.NOTE, 10.5)
legend(x, y, "the READY lamp off and FIRE greyed (a world effect is refused in pause, never queued — D-78, D-154) · the LAY marker held, tagged LAY HELD, while the stick is swung — the lay is integrated on live frames only (D-151) · ZOOM 8× changed in pause and the picture magnified (observation) · the message 'FIRE HELD IN PAUSE'",
       "the toolbar's run controls (paused) · the compass · the unit lines · the picture",
       ["Judged by dv-rws-paused: FIRE held through a pause and the stick swung hard — ⛔ `never` a shot during the pause, the turret pose unchanged. Nothing turns, nothing fires; on resume the lay continues from where it was held.",
        "No in-view PAUSED banner (as panels 5 and 15): the toolbar is the pause cue. PROPOSAL: the LAY HELD tag and the message's words are mine; the design says the lay and the trigger are frozen, not how the HUD shows it."], dy=34)

os.makedirs("docs/designs/devices/mockups", exist_ok=True)
s.save("docs/designs/devices/mockups/devices-hud-control.svg")
print("wrote docs/designs/devices/mockups/devices-hud-control.svg")
