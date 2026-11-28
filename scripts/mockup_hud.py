#!/usr/bin/env python3
"""Shared HUD drawing helpers for the device mockup sheets (docs/designs/devices/mockups): the view frame, the reticle, the LAY marker,
the command chips, the phase chip, the MODE switch ... Extracted from gen-devices-control-mockup.py so the control-unit sheet (panels 20-23) and the
lock / ranging sheet (panels 27-32) draw the SAME elements by the same code (ruling 9: shared, never duplicated). Call bind(svg) once per sheet."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

PLAY = 0xf04b
PW, PH = 640, 360                     # one view frame (16:9)
GOLD, WHITE = "#FFD700", "#FFFFFF"
PHASE = {"OFF": "#A0A0AA", "TEST": C.AMBER, "STANDBY": C.BLUE, "ARMED": C.GREEN, "GUIDING": C.RED}      # the console's phases (D-154); the colours of the sight unit's sheet
KEYS = {"POWER": "P", "SELECT": "S", "ARM": "A", "FIRE": "F", "MODE": "M", "ZOOM": "Z", "VIEW": "V", "TRACK": "T", "RANGE": "R", "GUIDE": "G", "RNG": "N"}   # PROPOSAL: the type names its hotkeys (D-124)
s = None                              # the sheet being drawn (bind)
_n = [0]

def bind(svg):
    global s
    s = svg

def defs():
    """The sky and grass gradients the scene() backdrop uses - once per sheet."""
    s.raw("<defs><linearGradient id='sky' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#5B87B8'/><stop offset='1' stop-color='#BFD3E3'/></linearGradient>"
          "<linearGradient id='grass' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#6B7E4A'/><stop offset='1' stop-color='#4A5A33'/></linearGradient></defs>")


def esc(t): return str(t).replace("&", "&amp;").replace("<", "&lt;")


def ht(x, y, t, c=WHITE, sz=12, anchor="start", weight="normal", op=1):
    """HUD text with the station's dark halo (legible on any background)."""
    s.raw(f"<text x='{x}' y='{y}' fill='{c}' font-size='{sz}' font-family='Roboto, Segoe UI, Arial, sans-serif' text-anchor='{anchor}' font-weight='{weight}' "
          f"stroke='#000' stroke-width='2.2' stroke-opacity='0.6' paint-order='stroke' opacity='{op}' xml:space='preserve'>{esc(t)}</text>")


def hline(x1, y1, x2, y2, c=WHITE, sw=1.2):
    s.line(x1, y1, x2, y2, "#000", sw + 2.2); s.line(x1, y1, x2, y2, c, sw)




def scene(x, y, k, target="truck", at=(0, 0), second=None):
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
    def _target(kind, cx, cy):
        if kind == "wall":                                                                      # a masonry wall (panel 32: nearer than the held range)
            s.rect(cx - 30, cy - 14, 60, 22, "#8C8A80", rx=1); s.rect(cx - 30, cy - 14, 60, 4, "#6E6C63")
        elif kind == "tank":
            s.rect(cx - 14, cy - 2, 28, 8, "#5B6350", rx=1.5); s.rect(cx - 7, cy - 6, 14, 5, "#4B5342", rx=1.5); s.line(cx + 6, cy - 4, cx + 22, cy - 5, "#4B5342", 1.6)
        else:                                                                                   # a truck: box, cab, wheels
            s.rect(cx - 17, cy - 8, 22, 13, "#6A6E58", rx=1); s.rect(cx + 5, cy - 5, 11, 10, "#56604A", rx=1.5); s.rect(cx + 11, cy - 3.5, 4, 3.5, "#9FB4C4")
            s.raw(f"<circle cx='{cx - 10}' cy='{cy + 5}' r='2.8' fill='#23262A'/><circle cx='{cx + 10}' cy='{cy + 5}' r='2.8' fill='#23262A'/>")
    _target(target, cx + at[0], cy + at[1])
    if second: _target(second[0], cx + second[1], cy + second[2])
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


def message(x, y, big, bigc, sub=None, dy=0):
    cx = x + PW / 2
    ht(cx, y + PH - 100 + dy, big, bigc, 25, "middle", "bold")
    if sub: ht(cx, y + PH - 80 + dy, sub, "#C8C8D0", 11.5, "middle")


def chips(x, y, states, hover=None, dy=0):
    """The command chips, bottom-right (POWER SELECT ARM FIRE ZOOM VIEW; MODE is the switch of mode_switch): HudPanel buttons (D-122) with their hotkey badges (D-124).
    on = set (blue), ready = pressable, off = refused (greyed)."""
    xx = x + PW - 14
    for nm, st in reversed(states):
        w = text_width(nm, 11) + 27; xx -= w
        if st == "on": s.rect(xx, y + PH - 50 + dy, w, 24, "#3369AD", rx=3, op=0.95)
        else: s.rect(xx, y + PH - 50 + dy, w, 24, "#000", rx=3, op=0.6, stroke="#8A8A96" if st == "ready" else None, sw=1)
        s.text(xx + 7, y + PH - 33 + dy, nm, WHITE if st == "on" else ("#E0E0E8" if st == "ready" else "#6A6A74"), 11, weight="bold" if st == "on" else "normal")
        kc = "#6A6A74" if st == "off" else "#C8C8D0"
        s.rect(xx + w - 16, y + PH - 44 + dy, 11, 13, "none", stroke=kc, rx=2, sw=0.9); s.text(xx + w - 10.5, y + PH - 34 + dy, KEYS[nm], kc, 9, "middle", "bold")
        xx -= 3


def mode_switch(x, y, pos, refused=False, note=None, dy=0):
    """The console's fire MODE (D-157): a latching 3-position switch drawn as its three positions with the set one lit, and its hotkey badge. In the left column
    under the weapon / launcher block. refused = the launchers offer SINGLE only: the whole switch greyed. note = a tag after it (stepped in pause)."""
    bx, by = x + 40, y + 150 + dy
    ht(bx, by + 15, "MODE", "#A0A0AA", 10.5)
    xx = bx + 38
    for i, nm in enumerate(("SINGLE", "BURST", "AUTO")):
        w = text_width(nm, 10.5) + 14
        on = i == pos
        if refused:
            s.rect(xx, by, w, 22, "#2A2A32" if on else "#000", rx=3, op=0.7 if on else 0.5, stroke="#4A4A55", sw=1)
            s.text(xx + w / 2, by + 15, nm, "#808088" if on else "#5A5A64", 10.5, "middle", "bold" if on else "normal")
        else:
            s.rect(xx, by, w, 22, "#3369AD" if on else "#000", rx=3, op=0.95 if on else 0.6, stroke=None if on else "#8A8A96", sw=1)
            s.text(xx + w / 2, by + 15, nm, WHITE if on else "#B8B8C0", 10.5, "middle", "bold" if on else "normal")
        xx += w + 2
    kc = "#6A6A74" if refused else "#C8C8D0"
    s.rect(xx + 2, by + 5, 11, 13, "#000", rx=2, op=0.6, stroke=kc, sw=0.9); s.text(xx + 7.5, by + 15, KEYS["MODE"], kc, 9, "middle", "bold")
    if note: ht(xx + 20, by + 15, note, GOLD, 9.5, weight="bold")


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




COLX, ROWY = [30, 750], [150, 780]


def layout(colx, rowy):
    """The sheet's panel grid: column x's and row y's (two columns)."""
    global COLX, ROWY
    COLX, ROWY = colx, rowy


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


# ── SD-S4d (DESIGN_Simulated_Devices §3.13g, D-173 - D-181): the lock, the lead, the ranged line, the guidance switch ─────────────────────────────────────
CYAN = "#7FE0FF"


def lock_box(x, y, dx, dy, w, h, state, label=None):
    """The LockTracker's box (D-174) on the camera picture: SEARCH white dashed (a candidate) · TRACKING green solid corners · LOST red dashed. dx, dy from the view centre."""
    cx, cy = x + PW / 2 + dx, y + PH / 2 + dy
    col = {"search": WHITE, "track": C.GREEN, "lost": C.RED}[state]
    if state == "track":
        for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            px, py = cx + sx * w / 2, cy + sy * h / 2
            hline(px, py, px - sx * 9, py, col, 1.8); hline(px, py, px, py - sy * 9, col, 1.8)
    else:
        s.raw(f"<rect x='{cx - w / 2}' y='{cy - h / 2}' width='{w}' height='{h}' fill='none' stroke='#000' stroke-width='3.4' stroke-opacity='0.55'/>"
              f"<rect x='{cx - w / 2}' y='{cy - h / 2}' width='{w}' height='{h}' fill='none' stroke='{col}' stroke-width='1.5' stroke-dasharray='4 3'/>")
    if label: ht(cx, cy - h / 2 - 6, label, col, 10.5, "middle", "bold")


def lead_pipper(x, y, dx, dy, from_dx=None, from_dy=None):
    """The LEAD pipper (D-175): where the MUSCLE is aiming the round - the led point (`WeaponTaskStatus.AimPoint`) - a diamond; a dotted line from the target's middle."""
    cx, cy = x + PW / 2, y + PH / 2
    mx, my = cx + dx, cy + dy
    if from_dx is not None:
        s.raw(f"<line x1='{cx + from_dx}' y1='{cy + from_dy}' x2='{mx}' y2='{my}' stroke='#000' stroke-opacity='0.5' stroke-width='3'/>"
              f"<line x1='{cx + from_dx}' y1='{cy + from_dy}' x2='{mx}' y2='{my}' stroke='{CYAN}' stroke-width='1.1' stroke-dasharray='2 3'/>")
    pts = f"{mx},{my - 8} {mx + 8},{my} {mx},{my + 8} {mx - 8},{my}"
    s.raw(f"<polygon points='{pts}' fill='none' stroke='#000' stroke-width='3.6' stroke-opacity='0.6'/><polygon points='{pts}' fill='none' stroke='{CYAN}' stroke-width='1.6'/>"
          f"<circle cx='{mx}' cy='{my}' r='1.5' fill='{CYAN}'/>")
    ht(mx + 12, my - 7, "LEAD", CYAN, 10.5, weight="bold")


def gun_marker(x, y, dx, dy, tag=None):
    """The GUN marker (D-181): where the barrel ACTUALLY points (the replicated TurretPose) against the cross - the drives' lag made visible, as the LAY marker was on panels 20-23."""
    cx, cy = x + PW / 2, y + PH / 2
    mx, my = cx + dx, cy + dy
    s.raw(f"<circle cx='{mx}' cy='{my}' r='10' fill='none' stroke='#000' stroke-width='3.6' stroke-opacity='0.6'/><circle cx='{mx}' cy='{my}' r='10' fill='none' stroke='{WHITE}' stroke-width='1.5' stroke-dasharray='3 2.5'/>"
          f"<line x1='{mx - 5}' y1='{my}' x2='{mx + 5}' y2='{my}' stroke='{WHITE}' stroke-width='1'/><line x1='{mx}' y1='{my - 5}' x2='{mx}' y2='{my + 5}' stroke='{WHITE}' stroke-width='1'/>")
    ht(mx + 13, my + 15, "GUN", WHITE, 10.5, weight="bold")
    if tag:
        w = text_width(tag, 10) + 12
        s.rect(mx + 13, my + 19, w, 15, "#000", rx=3, op=0.7, stroke=C.PEND, sw=1); s.text(mx + 13 + w / 2, my + 30, tag, C.PEND, 10, "middle", "bold")


def range_row(x, y, value, note=None, held=False):
    """The measured range line of the left column (D-181): RANGE <m> with the ranging mode's word; HELD = a latched value (amber)."""
    ht(x + 40, y + 102 + 3 * 18, "RANGE", "#A0A0AA", 10.5)
    ht(x + 108, y + 102 + 3 * 18, value, C.AMBER if held else WHITE, 12, weight="bold")
    if note: ht(x + 108 + text_width(value, 12) + 16, y + 102 + 3 * 18, note, "#A0A0AA", 10)


def switch2(x, y, label, names, pos, key, dy, refused=False, note=None):
    """A latching switch with N positions (D-142) in the left column under the MODE switch: RNG (AUTO · HOLD, D-181) and GUIDE (SEEKER · STICK · LINE, D-176)."""
    bx, by = x + 40, y + 150 + dy
    ht(bx, by + 15, label, "#A0A0AA", 10.5)
    xx = bx + 38
    for i, nm in enumerate(names):
        w = text_width(nm, 10.5) + 14
        on = i == pos
        if refused:
            s.rect(xx, by, w, 22, "#2A2A32" if on else "#000", rx=3, op=0.7 if on else 0.5, stroke="#4A4A55", sw=1)
            s.text(xx + w / 2, by + 15, nm, "#808088" if on else "#5A5A64", 10.5, "middle", "bold" if on else "normal")
        else:
            s.rect(xx, by, w, 22, "#3369AD" if on else "#000", rx=3, op=0.95 if on else 0.6, stroke=None if on else "#8A8A96", sw=1)
            s.text(xx + w / 2, by + 15, nm, WHITE if on else "#B8B8C0", 10.5, "middle", "bold" if on else "normal")
        xx += w + 2
    kc = "#6A6A74" if refused else "#C8C8D0"
    s.rect(xx + 2, by + 5, 11, 13, "#000", rx=2, op=0.6, stroke=kc, sw=0.9); s.text(xx + 7.5, by + 15, KEYS[key], kc, 9, "middle", "bold")
    if note: ht(xx + 20, by + 15, note, GOLD, 9.5, weight="bold")


def seeker_ring(x, y, r=64, label="SEEKER"):
    """The seeker's field (the cone, `GuidedRound.SeekerCos`) in the missile's view: a dashed ring about the centre."""
    cx, cy = x + PW / 2, y + PH / 2
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='{r}' fill='none' stroke='#000' stroke-width='3.2' stroke-opacity='0.5'/><circle cx='{cx}' cy='{cy}' r='{r}' fill='none' stroke='{WHITE}' stroke-width='1.2' stroke-dasharray='5 4'/>")
    ht(cx, cy + r + 14, label, "#C8C8D0", 10, "middle")


def stick_gauge(x, y, ax, ay, label="STICK"):
    """The stick's deflection (D-81, panel 11): a square with the dot at (ax, ay) in -1..1, in the left column."""
    gx, gy = x + 40, y + 206
    ht(gx, gy + 12, label, "#A0A0AA", 10.5)
    bx = gx + 52
    s.rect(bx, gy - 6, 28, 28, "#000", rx=3, op=0.65, stroke="#8A8A96", sw=1)
    s.line(bx + 14, gy - 4, bx + 14, gy + 20, "#4A4A55", 1); s.line(bx + 2, gy + 8, bx + 26, gy + 8, "#4A4A55", 1)
    s.raw(f"<circle cx='{bx + 14 + ax * 11}' cy='{gy + 8 - ay * 11}' r='3.2' fill='{GOLD}' stroke='#000' stroke-width='1'/>")
    ht(bx + 36, gy + 12, f"{ax:+.2f}  {ay:+.2f}", WHITE, 10.5)
