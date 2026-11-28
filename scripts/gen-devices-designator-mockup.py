#!/usr/bin/env python3
"""Mockup for docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d / §3.13e — the HUD of the LASER TARGET DESIGNATOR (SD-S5c, D-121) operated through
the VIRTUAL PANEL (SD-S5v, D-122…D-129): clickable chips with their hotkeys, the hover frame, POINT and RMB-LOOK. R-4138 / R-4143. Built on
scripts/mockup_kit.py, in the house style of the other device sheets.

    python3 scripts/gen-devices-designator-mockup.py      # -> docs/designs/devices/mockups/devices-hud-designator.svg

SCOPE (R-4151):
  SPECIFIES  what DesignatorHudGizmo draws in a view bound to the designator (the window, reticle, status, code, lase timer, range, the received-target cue,
             the chips through HudPanel) and what the station's VirtualPanelAdapter (the device's panel IO) draws over it (the hover frame, the cursor state, the LOOK hint).
  CONTEXT    dashed / grey, tagged "unchanged": the compass strip, the operator's unit lines, the toolbar's run controls (panel 19). The scene is illustrative.
  PROPOSAL   every position is a proposal where the design is silent.
Panel numbers continue the device sheets (1–6, 7–11, 12–15).
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

PLAY, POINTER, HAND, ARROWS = 0xf04b, 0xf245, 0xf256, 0xf0b2
W, H = 1460, 1290
PW, PH = 640, 360
GOLD, WHITE, LASE = "#FFD700", "#FFFFFF", "#FF3B30"
s = Svg(W, H, "Simulated devices — the LASER TARGET DESIGNATOR through the virtual panel  (SD-S5c + SD-S5v, DESIGN_Simulated_Devices §3.13d / §3.13e)",
        "Full colour = SPECIFIED · grey dashed + 'unchanged' = CONTEXT · the scene is illustrative · the chips are CLICKABLE (D-122) and carry their hotkey (D-124).")


def esc(t): return str(t).replace("&", "&amp;").replace("<", "&lt;")


def ht(x, y, t, c=WHITE, sz=12, anchor="start", weight="normal", op=1):
    s.raw(f"<text x='{x}' y='{y}' fill='{c}' font-size='{sz}' font-family='Roboto, Segoe UI, Arial, sans-serif' text-anchor='{anchor}' font-weight='{weight}' "
          f"stroke='#000' stroke-width='2.2' stroke-opacity='0.6' paint-order='stroke' opacity='{op}' xml:space='preserve'>{esc(t)}</text>")


def hline(x1, y1, x2, y2, c=WHITE, sw=1.2):
    s.line(x1, y1, x2, y2, "#000", sw + 2.2); s.line(x1, y1, x2, y2, c, sw)


_n = [0]


def scene(x, y, k=2.4):
    """The view through the designator (illustrative): a ridge, a field, a tree line, a farm and ONE tank under the reticle."""
    _n[0] += 1; cid = f"z{_n[0]}"
    cx, cy = x + PW / 2, y + PH / 2
    s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}'/></clipPath><g clip-path='url(#{cid})'>")
    s.raw(f"<g transform='translate({cx},{cy}) scale({k}) translate({-cx},{-cy})'>")
    s.raw(f"<rect x='{x - 400}' y='{y - 300}' width='{PW + 800}' height='{PH * 0.55 + 300}' fill='url(#sky)'/>")
    hz = y + PH * 0.55
    s.raw(f"<polygon points='{x - 400},{hz} {x + 120},{hz - 14} {x + 300},{hz - 4} {x + 470},{hz - 18} {x + PW + 400},{hz - 5} {x + PW + 400},{hz}' fill='#4C5E49'/>")
    s.raw(f"<rect x='{x - 400}' y='{hz}' width='{PW + 800}' height='400' fill='url(#grass)'/>")
    for i in range(-14, 30): s.raw(f"<circle cx='{x + i * 13}' cy='{hz - 3}' r='7' fill='#2F4A2E'/>")
    s.rect(x + 410, hz - 13, 26, 14, "#A59A86"); s.raw(f"<polygon points='{x + 406},{hz - 13} {x + 423},{hz - 23} {x + 440},{hz - 13}' fill='#7A3B30'/>")
    s.rect(cx - 14, cy - 2, 28, 8, "#5B6350", rx=1.5); s.rect(cx - 7, cy - 6, 14, 5, "#4B5342", rx=1.5); s.line(cx + 6, cy - 4, cx + 22, cy - 5, "#4B5342", 1.6)
    s.raw("</g></g>")
    s.rect(x, y, PW, PH, "none", stroke=C.BORDER)


def window(x, y):
    """The designator's window: corner brackets, the outside darkened."""
    m = 26
    s.raw(f"<path d='M{x},{y} h{PW} v{PH} h{-PW} z M{x + m},{y + m} v{PH - 2 * m} h{PW - 2 * m} v{-(PH - 2 * m)} z' fill='#000' fill-rule='evenodd' opacity='0.55'/>")
    for sx, sy in ((0, 0), (1, 0), (0, 1), (1, 1)):
        px, py = x + m + sx * (PW - 2 * m), y + m + sy * (PH - 2 * m)
        dx, dy = (1 if sx == 0 else -1) * 22, (1 if sy == 0 else -1) * 22
        hline(px, py, px + dx, py, WHITE, 1.6); hline(px, py, px, py + dy, WHITE, 1.6)


def reticle(x, y, lasing=False):
    cx, cy = x + PW / 2, y + PH / 2
    c = LASE if lasing else WHITE
    for sx, sy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        hline(cx + sx * 10, cy + sy * 10, cx + sx * 46, cy + sy * 46, c, 1.4)
    s.rect(cx - 5, cy - 5, 10, 10, "none", stroke=c, sw=1.4)


def status(x, y, phase, pc, code, power_on=True):
    ht(x + 40, y + 50, "LASER DESIGNATOR", "#C8C8D0", 12, weight="bold")
    ht(x + 40, y + 70, "STATE", "#A0A0AA", 11); ht(x + 92, y + 70, phase, pc, 13, weight="bold")
    ht(x + 40, y + 89, "CODE", "#A0A0AA", 11); ht(x + 92, y + 89, code, GOLD, 13, weight="bold")


def lamp(x, y, on):
    lx, ly = x + PW - 50, y + 52
    ht(lx - 12, ly + 5, "LASE", LASE if on else "#A0A0AA", 12, "end", "bold")
    if on: s.raw(f"<circle cx='{lx}' cy='{ly}' r='11' fill='#000' opacity='0.45'/><circle cx='{lx}' cy='{ly}' r='10' fill='{LASE}' opacity='0.28'/>")
    s.raw(f"<circle cx='{lx}' cy='{ly}' r='6' fill='{LASE if on else '#3A3A44'}' stroke='#000' stroke-width='1.5'/>")


def timer(x, y, label, frac, c, text):
    bx, by, bw = x + 40, y + 104, 150
    ht(bx, by + 10, label, "#A0A0AA", 10.5)
    s.rect(bx + 52, by + 2, bw, 9, "#000", rx=2, op=0.6, stroke="#8A8A96", sw=0.8); s.rect(bx + 53, by + 3, (bw - 2) * frac, 7, c, rx=2)
    ht(bx + 58 + bw, by + 11, text, c, 11, weight="bold")


def rng(x, y, text):
    ht(x + PW / 2, y + PH - 116, "RANGE", "#C8C8D0", 11, "middle")
    ht(x + PW / 2, y + PH - 90, text, GOLD, 24, "middle", "bold")


def chips(x, y, active=None, hover=None, disabled=(), queued=None):
    """The chip row through HudPanel.Button: label + its hotkey; hover = the station-local frame (the adapter); disabled greyed."""
    items = (("POWER", "P"), ("LASE", "L"), ("CODE", "C"), ("RANGE", "R"))
    xx = x + PW - 34
    rects = {}
    for nm, key in reversed(items):
        w = text_width(nm, 11.5) + 34; xx -= w
        on, dis = nm == active, nm in disabled
        s.rect(xx, y + PH - 66, w, 26, "#3369AD" if on else "#000", rx=3, op=0.85 if on else 0.62, stroke=None if on else "#8A8A96", sw=1)
        ht(xx + 8, y + PH - 48.5, nm, "#6A6A74" if dis else WHITE, 11.5, weight="bold" if on else "normal")
        s.rect(xx + w - 20, y + PH - 60, 14, 14, "none", stroke="#6A6A74" if dis else "#C8C8D0", rx=2, sw=1)
        ht(xx + w - 13, y + PH - 49.5, key, "#6A6A74" if dis else "#C8C8D0", 9.5, "middle", "bold")
        if nm == hover:
            s.rect(xx - 3, y + PH - 69, w + 6, 32, "none", stroke=GOLD, rx=4, sw=2)
        if nm == queued:
            ht(xx + w / 2, y + PH - 74, "queued", GOLD, 9.5, "middle", "bold")
        rects[nm] = (xx, y + PH - 66, w)
        xx -= 6
    return rects


def cursor(cx, cy, kind="pointer"):
    g = HAND if kind == "hand" else POINTER
    s.glyph(g, cx, cy, WHITE, 18)


def cue(x, y, angle_deg=-14, txt="TARGET ◂ 14°  1 240 m"):
    """The received-target cue (D-119): an arrow at the window's edge toward the received point + its bearing offset and range."""
    ax, ay = x + 70, y + PH / 2
    s.raw(f"<polygon points='{ax - 14},{ay} {ax + 2},{ay - 9} {ax + 2},{ay + 9}' fill='{C.AMBER}' stroke='#000' stroke-width='1.2'/>")
    ht(ax + 8, ay + 26, txt, C.AMBER, 11, weight="bold")
    ht(ax + 8, ay + 41, "from RANGEFINDER (linked)", "#A0A0AA", 9.5)


def compass_ctx(x, y):
    s.rect(x + PW / 2 - 110, y + 4, 220, 18, "none", stroke="#8A8A96", dash="4 3"); s.text(x + PW / 2 + 110, y + 1, "unchanged — compass", "#8A8A96", 9, "end")


def unit_lines(x, y):
    bx, by = x + 34, y + PH - 92
    s.rect(bx, by, 120, 40, "none", stroke="#8A8A96", dash="4 3"); s.text(bx + 120, by - 3, "unchanged — unit lines", "#8A8A96", 9, "end")
    for i in range(3): s.rect(bx + 8, by + 8 + i * 10, 90 - i * 18, 4, "#8A8A96", op=0.7)


def look_hint(x, y, active):
    tx, ty = x + 52, y + PH - 30
    s.glyph(0xf8cc if False else FA.EYE, tx, ty - 12, GOLD if active else "#A0A0AA", 12)
    ht(tx + 18, ty, "LOOK — hold RMB · arrows", GOLD if active else "#A0A0AA", 10.5, weight="bold" if active else "normal")


# ── page ───────────────────────────────────────────────────────────────────────────────────────────────
s.raw("<defs><linearGradient id='sky' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#5B87B8'/><stop offset='1' stop-color='#BFD3E3'/></linearGradient>"
      "<linearGradient id='grass' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#6B7E4A'/><stop offset='1' stop-color='#4A5A33'/></linearGradient></defs>")
COLX = [30, 750]
ROWY = [112, 694]


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


s.rect(30, 56, 12, 12, "#000", stroke=WHITE, sw=1); s.text(48, 66, "specified HUD element (dark halo) — chips carry their hotkey in a key box", C.SUB, 11)
s.rect(560, 56, 12, 12, "none", stroke=GOLD, sw=2); s.text(578, 66, "the station adapter's hover frame (station-local, D-123)", C.SUB, 11)
s.rect(980, 56, 12, 12, "none", stroke="#8A8A96", dash="4 3"); s.text(998, 66, "context — unchanged", C.SUB, 11)
s.text(30, 88, "Frames are 640 × 360, the view bound to the designator. POINT is the default: the cursor is free; hold RMB to LOOK. Layout positions are PROPOSALS.", C.PEND, 11)

# ── 16 · standby, a target received, pointing at CODE ─────────────────────────────────────────────────
x, y = head(0, "16 · STANDBY — a target received from the linked rangefinder; the cursor on CODE (POINT)",
            "Scope: window, reticle, STATE, CODE, the received-target cue, RANGE, the chip row with hotkeys, the hover frame and the cursor. Compass and unit lines are context.")
scene(x, y); window(x, y); reticle(x, y); status(x, y, "STANDBY", C.GREEN, "1688"); lamp(x, y, False); rng(x, y, "— m")
r = chips(x, y, hover="CODE"); cue(x, y); compass_ctx(x, y); unit_lines(x, y); look_hint(x, y, False)
cx_, cy_, w_ = r["CODE"]; cursor(cx_ + w_ - 8, cy_ + 16, "hand")
legend(x, y, "STATE STANDBY (green) · CODE 1688 (gold) · the cue: an amber arrow at the window's edge toward the received point, its bearing offset and range, and who sent it · "
             "RANGE '— m' · the chips POWER P · LASE L · CODE C · RANGE R (each a PanelButton) · the gold hover frame on CODE and the hand cursor (the adapter) · the LOOK hint",
       "compass · unit lines · the scene",
       ["A click on CODE steps the code (STANDBY only, D-113) — exactly the press its hotkey C, a pad button or a replica button makes (D-125).",
        "PROPOSAL: the cue at the window's left edge pointing toward the target; the chip row bottom-right; the LOOK hint bottom-left."])

# ── 17 · lasing, looking with RMB ──────────────────────────────────────────────────────────────────────
x, y = head(1, "17 · LASING — the lase timer running, the view turned with RMB held (LOOK)",
            "Scope: the LASE lamp lit, the reticle red, STATE LASING, the lase timer, the range to the spot, LASE lit; LOOK active — the cursor hidden, the hint gold.")
scene(x, y); window(x, y); reticle(x, y, True); status(x, y, "LASING", LASE, "1688"); lamp(x, y, True); timer(x, y, "LASE", 0.7, LASE, "42 s")
rng(x, y, "1 240 m"); chips(x, y, active="LASE", disabled=("CODE",)); compass_ctx(x, y); unit_lines(x, y); look_hint(x, y, True)
ht(x + PW / 2 + 58, y + PH / 2 - 54, "RMB held — the cursor is captured", "#C8C8D0", 10, "middle")
legend(x, y, "LASE lamp lit (blinks by frame count) · the reticle red while lasing · STATE LASING (red) · the lase timer bar: 42 s left of 60 (D-113) · RANGE 1 240 m (gold) · "
             "LASE chip lit · CODE greyed (steps only in STANDBY) · LOOK active: no cursor, the hint gold",
       "compass · unit lines · the scene (the spot on the tank is the 3-D map's laser layer — SD-S6)",
       ["The bomb's seeker sees this spot only while it is not a Preview (D-115) and only on its own code (D-117).",
        "PROPOSAL: the timer under CODE; the 'captured' note is for this picture only, not drawn by the app."])

# ── 18 · cooling ───────────────────────────────────────────────────────────────────────────────────────
x, y = head(2, "18 · COOLING — the duty limit reached; LASE refused until it cools",
            "Scope: STATE COOLING, the cool-down bar, LASE greyed, the cursor over LASE with the hover frame (a click does nothing).")
scene(x, y); window(x, y); reticle(x, y); status(x, y, "COOLING", C.AMBER, "1688"); lamp(x, y, False); timer(x, y, "COOL", 0.35, C.AMBER, "19 s")
rng(x, y, "1 240 m"); r = chips(x, y, hover="LASE", disabled=("LASE", "CODE")); compass_ctx(x, y); unit_lines(x, y); look_hint(x, y, False)
cx_, cy_, w_ = r["LASE"]; cursor(cx_ + w_ - 8, cy_ + 16, "hand")
legend(x, y, "STATE COOLING (amber) · the cool-down bar (19 s of 30) · LASE and CODE greyed (disabled PanelButtons: hover frames them, a click presses nothing) · the last range kept",
       "compass · unit lines · the scene",
       ["Disabled is the DEVICE's state drawn on the chip (D-122); the adapter does not decide it — a hotkey L is refused the same way (the device ignores it).",
        "PROPOSAL: a disabled chip still takes the hover frame, so the trainee sees why nothing happens."])

# ── 19 · paused, a lase queued ─────────────────────────────────────────────────────────────────────────
x, y = head(3, "19 · PAUSED — LASE clicked in a pause: queued for the first live frame",
            "Scope: the same HUD in pause, STANDBY, the LASE chip lit with 'queued', no spot. The toolbar's paused state is context.")
scene(x, y); window(x, y); reticle(x, y); status(x, y, "STANDBY", C.GREEN, "1688"); lamp(x, y, False); rng(x, y, "— m")
r = chips(x, y, active="LASE", hover="LASE", queued="LASE"); compass_ctx(x, y); unit_lines(x, y); look_hint(x, y, False)
cx_, cy_, w_ = r["LASE"]; cursor(cx_ + w_ - 8, cy_ + 16, "hand")
ty = y + PH + 8
s.rect(x, ty, 300, 24, C.PAGE, stroke="#8A8A96", dash="4 3", rx=3)
s.glyph(0xf111, x + 8, ty + 5, "#E63838", 13); s.glyph(PLAY, x + 40, ty + 5, "#E63838", 13)
s.text(x + 64, ty + 16, "toolbar, paused: ▶ shown (context)", C.NOTE, 10.5)
legend(x, y, "LASE pressed in pause: the chip lit with 'queued' (no designation in pause — R-4300 / D-115) · the lamp off and no spot until time runs · POINT and LOOK still work (D-129)",
       "the toolbar's run controls (paused) · compass · unit lines · the scene",
       ["Judged by dv-ltd-procedure: no non-Preview spot during the pause; the queued lase starts on the first live frame.",
        "PROPOSAL: the 'queued' tag above the chip (the drone OSD's queued command, panel 15, says it the same way)."], dy=34)

os.makedirs("docs/designs/devices/mockups", exist_ok=True)
s.save("docs/designs/devices/mockups/devices-hud-designator.svg")
print("wrote docs/designs/devices/mockups/devices-hud-designator.svg")
