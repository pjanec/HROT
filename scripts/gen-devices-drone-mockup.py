#!/usr/bin/env python3
"""Mockup for docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c — the ON-SCREEN DISPLAY of the drone ground station (SD-S2, D-111;
R-4138 / R-4143). Built on scripts/mockup_kit.py, in the house style of scripts/gen-devices-hud-mockup.py (panels 1–6).

    python3 scripts/gen-devices-drone-mockup.py      # -> docs/designs/devices/mockups/devices-hud-drone.svg

SCOPE (R-4151, what this picture specifies and what it does not):
  SPECIFIES  what the ground station's HUD projector (DroneStationHudGizmo) draws in a view bound to the ground-station device, while the view
             rides the drone's camera (DeviceView, D-109): the mode chip, battery, link bars, altitude, speed, distance home, the heading tape,
             the gimbal pitch scale, the centre mark, the command line, the warnings (battery low, LINK LOST + failsafe) and the VIEW label.
  CONTEXT    drawn dashed / greyed and tagged "unchanged": the operator's own unit lines (Hud.Lines) and the toolbar's run controls (panel 15).
             The camera picture behind is illustrative only.
  PROPOSAL   every layout position is a proposal where the design is silent (the design names WHAT the OSD shows, not WHERE).
Panel numbers continue devices-hud.svg (1–6); 7–11 (the launcher sight unit) are a separate sheet.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

# FontAwesome 6 solid code points this sheet adds (the app's IconsFontAwesome6 carries them)
BATTERY_FULL, BATTERY_QUARTER, SIGNAL, HOUSE, VIDEO, VIDEO_SLASH, PLAY = 0xf240, 0xf243, 0xf012, 0xf015, 0xf03d, 0xf4e2, 0xf04b
W, H = 1460, 1250
PW, PH = 640, 360                     # one view frame (16:9)
GOLD, WHITE, OK, WARN, BAD = "#FFD700", "#FFFFFF", C.GREEN, C.AMBER, C.RED
s = Svg(W, H, "Simulated devices — the drone ground station's on-screen display  (SD-S2, DESIGN_Simulated_Devices §3.13c, D-111)",
        "Full colour = SPECIFIED by this mockup · grey dashed + 'unchanged' = CONTEXT, not specified · the camera picture behind is illustrative.")


def esc(t): return str(t).replace("&", "&amp;").replace("<", "&lt;")


def ht(x, y, t, c=WHITE, sz=12, anchor="start", weight="normal", op=1):
    """HUD text with the station's dark halo (legible on any background)."""
    s.raw(f"<text x='{x}' y='{y}' fill='{c}' font-size='{sz}' font-family='Roboto, Segoe UI, Arial, sans-serif' text-anchor='{anchor}' font-weight='{weight}' "
          f"stroke='#000' stroke-width='2.2' stroke-opacity='0.6' paint-order='stroke' opacity='{op}' xml:space='preserve'>{esc(t)}</text>")


def hline(x1, y1, x2, y2, c=WHITE, sw=1.2):
    s.line(x1, y1, x2, y2, "#000", sw + 2.2); s.line(x1, y1, x2, y2, c, sw)


_n = [0]


def camera(x, y, dark=0.0):
    """The drone's camera picture (illustrative): looking down ~30° over fields, a road, a hamlet and a tree line, the horizon high."""
    _n[0] += 1; cid = f"d{_n[0]}"
    s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{PW}' height='{PH}'/></clipPath><g clip-path='url(#{cid})'>")
    hz = y + 58
    s.raw(f"<rect x='{x}' y='{y}' width='{PW}' height='{hz - y}' fill='url(#sky)'/>")
    s.raw(f"<rect x='{x}' y='{hz}' width='{PW}' height='{PH}' fill='url(#field)'/>")
    for i in range(7):                                                                          # field strips, converging
        fx = x + 40 + i * 95
        s.raw(f"<polygon points='{fx},{y + PH} {fx + 60},{y + PH} {x + PW / 2 + (i - 3) * 22 + 8},{hz} {x + PW / 2 + (i - 3) * 22},{hz}' fill='#74874F' opacity='{0.35 if i % 2 else 0.0}'/>")
    s.raw(f"<polygon points='{x + 250},{y + PH} {x + 330},{y + PH} {x + 352},{hz} {x + 344},{hz}' fill='#8C8A84'/>")   # the road
    for hx, hy, w_, h_ in ((x + 380, y + 150, 34, 22), (x + 430, y + 128, 26, 18), (x + 395, y + 112, 22, 14), (x + 140, y + 200, 40, 26)):
        s.rect(hx, hy, w_, h_, "#B3A893"); s.rect(hx, hy, w_, h_ * 0.45, "#8A4A3A")
    for i in range(30):
        s.raw(f"<circle cx='{x + 8 + i * 22}' cy='{hz + 6 + (i % 3)}' r='8' fill='#2F4A2E'/>")
    s.raw("</g>")
    if dark > 0: s.rect(x, y, PW, PH, "#000", op=dark)
    s.rect(x, y, PW, PH, "none", stroke=C.BORDER)


def unit_lines(x, y):
    """CONTEXT: the operator's own unit lines (Hud.Lines) stay bottom-left."""
    bx, by = x + 10, y + PH - 58
    s.rect(bx, by, 132, 48, "none", stroke="#8A8A96", dash="4 3")
    s.text(bx + 132, by - 3, "unchanged — unit lines", "#8A8A96", 9.5, "end")
    for i in range(3): s.rect(bx + 8, by + 10 + i * 12, 100 - i * 18, 4, "#8A8A96", op=0.7)


def mode_chip(x, y, mode, armed=True, c=OK):
    w = text_width(mode, 13) + 26
    s.rect(x + 14, y + 14, w, 24, "#000", rx=4, op=0.7, stroke=c, sw=1.4)
    ht(x + 14 + w / 2, y + 31, mode, c, 13, "middle", "bold")
    ht(x + 22 + w, y + 31, "ARMED" if armed else "DISARMED", WARN if armed else "#A0A0AA", 11, weight="bold")


def battery(x, y, pct, c):
    g = BATTERY_FULL if pct > 30 else BATTERY_QUARTER
    s.glyph(g, x + PW - 92, y + 18, c, 16); ht(x + PW - 14, y + 32, f"{pct} %", c, 14, "end", "bold")


def link(x, y, bars, c):
    bx = x + PW - 160
    for i in range(4):
        h = 5 + i * 4
        s.rect(bx + i * 7, y + 34 - h, 5, h, c if i < bars else "#5A5A64", rx=1)


def heading_tape(x, y, hdg=24):
    cx = x + PW / 2
    s.rect(cx - 130, y + 12, 260, 26, "#000", rx=3, op=0.45)
    for a in range(hdg - 60 - (hdg % 5), hdg + 61, 5):
        d = a - hdg; px = cx + d * 2.1
        if abs(d) > 60: continue
        big = a % 30 == 0
        hline(px, y + 38, px, y + (28 if big else 33), WHITE, 1)
        if big: ht(px, y + 25, {0: "N", 90: "E", 180: "S", 270: "W"}.get(a % 360, str(a % 360)), "#E0E0E8", 10, "middle")
    s.raw(f"<polygon points='{cx - 5},{y + 44} {cx + 5},{y + 44} {cx},{y + 38}' fill='{GOLD}'/>")
    ht(cx, y + 58, f"{hdg:03d}", GOLD, 11, "middle", "bold")


def gimbal(x, y, pitch=-30):
    """The gimbal pitch scale, left edge: 0° at the top, −90° straight down; the marker at the camera's pitch."""
    gx, gy0, gy1 = x + 22, y + 92, y + 252
    hline(gx, gy0, gx, gy1, "#C8C8D0", 1.2)
    for a in (0, -30, -60, -90):
        py = gy0 + (-a / 90) * (gy1 - gy0)
        hline(gx - 4, py, gx + 4, py, "#C8C8D0", 1); ht(gx + 9, py + 4, f"{a}°", "#C8C8D0", 9.5)
    py = gy0 + (-pitch / 90) * (gy1 - gy0)
    s.raw(f"<polygon points='{gx - 9},{py - 5} {gx - 9},{py + 5} {gx - 2},{py}' fill='{GOLD}'/>")
    ht(gx - 6, gy1 + 18, "CAM", "#A0A0AA", 9.5)


def centre(x, y):
    cx, cy = x + PW / 2, y + PH / 2
    for sx, sy in ((-1, 0), (1, 0), (0, 1)):
        hline(cx + sx * 6, cy + sy * 6, cx + sx * 20, cy + sy * 20, WHITE, 1.4)
    s.raw(f"<circle cx='{cx}' cy='{cy}' r='1.6' fill='{WHITE}'/>")


def readouts(x, y, alt=42, spd=8.4, home=180, c=WHITE):
    yb = y + PH - 20
    cx = x + PW / 2
    ht(cx - 150, yb, "ALT", "#A0A0AA", 10.5, "end"); ht(cx - 145, yb, f"{alt} m", c, 15, weight="bold")
    ht(cx - 20, yb, "SPD", "#A0A0AA", 10.5, "end"); ht(cx - 15, yb, f"{spd:.1f} m/s", c, 15, weight="bold")
    s.glyph(HOUSE, cx + 98, yb - 13, "#A0A0AA", 13); ht(cx + 118, yb, f"{home} m", c, 15, weight="bold")


def commands(x, y, active="", opened=False):
    names = ("TAKEOFF", "LAND", "RTH")
    xx = x + PW - 14
    for nm in reversed(names):
        w = text_width(nm, 11) + 16; xx -= w
        on = nm == active
        s.rect(xx, y + PH - 66, w, 22, "#3369AD" if on else "#000", rx=3, op=0.85 if on else 0.6,
               stroke=GOLD if (opened and on) else ("#8A8A96" if not on else None), sw=1.2)
        ht(xx + w / 2, y + PH - 51, nm, WHITE if on else "#C8C8D0", 11, "middle", "bold" if on else "normal")
        xx -= 6


def view_label(x, y, name="DRONE STATION"):
    lw = text_width("VIEW  " + name, 11) + 34
    lx = x + PW - 14 - lw
    s.rect(lx, y + 48, lw, 20, "#000", rx=3, op=0.7); s.glyph(VIDEO, lx + 6, y + 51, C.BLUE, 13)
    s.text(lx + 24, y + 62.5, "VIEW", "#A0A0AA", 10.5); s.text(lx + 56, y + 62.5, name, WHITE, 11, weight="bold")


# ── page ───────────────────────────────────────────────────────────────────────────────────────────────
s.raw("<defs><linearGradient id='sky' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#7FA4C8'/><stop offset='1' stop-color='#C9D9E6'/></linearGradient>"
      "<linearGradient id='field' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#7C8C58'/><stop offset='1' stop-color='#56683A'/></linearGradient></defs>")
COLX = [30, 750]
ROWY = [112, 668]


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


s.rect(30, 56, 12, 12, "#000", stroke=WHITE, sw=1); s.text(48, 66, "specified OSD element (full colour, dark halo — the station's HudText over the view)", C.SUB, 11)
s.rect(600, 56, 12, 12, "none", stroke="#8A8A96", dash="4 3"); s.text(618, 66, "context — unchanged, not specified here", C.SUB, 11)
s.text(30, 88, "Frames are 640 × 360 (16:9), the view riding the drone's camera (DeviceView, D-109). Layout positions are PROPOSALS where the design is silent.", C.PEND, 11)

# ── 12 · flying ────────────────────────────────────────────────────────────────────────────────────────
x, y = head(0, "12 · DRONE STATION, flying by hand — the OSD over the drone's camera (dv-drone-fly)",
            "Scope: the mode chip, armed state, battery, link bars, the heading tape, the gimbal scale, the centre mark, altitude · speed · distance home, the command line, the VIEW label.")
camera(x, y); heading_tape(x, y); mode_chip(x, y, "MANUAL"); battery(x, y, 78, OK); link(x, y, 4, OK); view_label(x, y)
gimbal(x, y, -30); centre(x, y); readouts(x, y); commands(x, y); unit_lines(x, y)
legend(x, y, "MANUAL chip (green: the link is good) + ARMED · battery 78 % (green > 30 %, amber ≤ 30 %, red ≤ 15 %) · link bars 4/4 · heading tape with the heading under the gold caret · "
             "gimbal pitch −30° on the left scale · centre mark · ALT (above ground) · SPD · ⌂ distance home · TAKEOFF / LAND / RTH, the active one lit · VIEW ▸ DRONE STATION",
       "the operator's unit lines · the camera picture",
       ["The station's compass strip is NOT drawn while the view rides the drone: the OSD's heading tape is the drone's heading (the compass is the man's).",
        "PROPOSAL: the mode chip top-left, battery and link top-right, readouts bottom-centre, commands bottom-right (as the rangefinder's menu line)."])

# ── 13 · battery low ───────────────────────────────────────────────────────────────────────────────────
x, y = head(1, "13 · BATTERY LOW — the warning, and the drone flying home (dv-drone-battery-out)",
            "Scope: the battery in red, the warning banner, the RTH command lit (the operator pressed it, or the type returns on its own — the design leaves that to the GCS's mode).")
camera(x, y); heading_tape(x, y, 204); mode_chip(x, y, "RTH", c=WARN); battery(x, y, 12, BAD); link(x, y, 3, OK); view_label(x, y)
gimbal(x, y, -30); centre(x, y); readouts(x, y, 38, 11.2, 640); commands(x, y, active="RTH"); unit_lines(x, y)
s.rect(x + PW / 2 - 150, y + 86, 300, 30, "#3A1A1A", rx=4, op=0.85, stroke=BAD, sw=1.4)
s.glyph(FA.WARNING, x + PW / 2 - 136, y + 93, BAD, 15); ht(x + PW / 2 + 10, y + 107, "BATTERY LOW — RETURN HOME", BAD, 13, "middle", "bold")
legend(x, y, "battery 12 % in red (blinks by frame count) · the BATTERY LOW banner with the warning glyph · the RTH chip (amber) and the RTH command lit · distance home counting down",
       "the operator's unit lines · the camera picture",
       ["At 0 % the drone comes down where it is (D-107, the existing rule) — the banner then reads BATTERY EMPTY.",
        "PROPOSAL: the warning threshold (15 %) and the banner under the heading tape are mine; the design names a battery warning, not its look."])

# ── 14 · link lost ─────────────────────────────────────────────────────────────────────────────────────
x, y = head(2, "14 · LINK LOST — jammed: the failsafe hovers, then flies home (dv-drone-jammed)",
            "Scope: the darkened last picture, LINK LOST, the reason (JAMMED or out of range), the failsafe's countdown and next step, the link bars at zero.")
camera(x, y, dark=0.62); heading_tape(x, y, 312); mode_chip(x, y, "FAILSAFE", c=BAD); battery(x, y, 64, OK); link(x, y, 0, BAD); view_label(x, y)
readouts(x, y, 51, 0.0, 1240, c="#A0A0AA"); unit_lines(x, y)
cx, cy = x + PW / 2, y + PH / 2
s.glyph(VIDEO_SLASH, cx - 16, cy - 62, BAD, 30)
ht(cx, cy - 4, "LINK LOST", BAD, 26, "middle", "bold")
ht(cx, cy + 20, "JAMMED", WARN, 13, "middle", "bold")
ht(cx, cy + 44, "FAILSAFE: hover 3 s  ▸  return home  ▸  land", "#E0E0E8", 12, "middle")
legend(x, y, "the last picture held and darkened · LINK LOST (red) with the glyph · the reason: JAMMED (DroneLink.Jammed) or OUT OF RANGE · the failsafe's countdown and its next steps · "
             "FAILSAFE chip (red) · link bars 0 (red) · readouts greyed (the last values the link carried)",
       "the operator's unit lines",
       ["Sticks do nothing while the link is lost (D-107); the commands are hidden. The link back ⇒ the GCS's mode again and the live picture.",
        "PROPOSAL: holding the last picture darkened (rather than black or 'snow') is mine — it keeps the operator oriented; the design names LINK LOST and the failsafe, not the look."])

# ── 15 · paused ────────────────────────────────────────────────────────────────────────────────────────
x, y = head(3, "15 · PAUSED — the OSD still answers, nothing moves (dv-drone-paused)",
            "Scope: the same OSD in pause with the command menu opened by a press; the battery and the drone hold still. The toolbar's paused state is context.")
camera(x, y); heading_tape(x, y); mode_chip(x, y, "MANUAL"); battery(x, y, 78, OK); link(x, y, 4, OK); view_label(x, y)
gimbal(x, y, -45); centre(x, y); readouts(x, y, 42, 0.0, 180); commands(x, y, active="LAND", opened=True); unit_lines(x, y)
px0, py0 = x + PW - 254, y + PH - 166
s.rect(px0, py0, 240, 92, "#000", rx=3, op=0.82, stroke="#8A8A96", sw=1)
ht(px0 + 8, py0 + 15, "COMMAND", "#C8C8D0", 10.5, weight="bold"); s.line(px0 + 6, py0 + 21, px0 + 234, py0 + 21, "#8A8A96", 0.8)
for i, (nm, cur, note) in enumerate((("LAND", True, "queued — runs when time does"), ("RTH", False, ""), ("HOVER", False, ""))):
    ry = py0 + 26 + i * 21
    if cur: s.rect(px0 + 4, ry, 232, 20, "#3369AD", rx=3)
    s.text(px0 + 12, ry + 14.5, nm, WHITE, 12, weight="bold" if cur else "normal")
    if note: s.text(px0 + 228, ry + 14.5, note, GOLD, 9.5, "end")
ty = y + PH + 8
s.rect(x, ty, 300, 24, C.PAGE, stroke="#8A8A96", dash="4 3", rx=3)
s.glyph(0xf111, x + 8, ty + 5, "#E63838", 13); s.glyph(PLAY, x + 40, ty + 5, "#E63838", 13)
s.text(x + 64, ty + 16, "toolbar, paused: ▶ shown (context)", C.NOTE, 10.5)
legend(x, y, "the OSD answering in pause: the command menu opens, LAND chosen and shown QUEUED (R-4300: a mode press is a world effect — it runs on the first live frame) · "
             "CAM DOWN works (gimbal −45°, the view tilts — observation) · SPD 0.0 · the battery holds at 78 %",
       "the toolbar's run controls (paused) · the operator's unit lines · the picture",
       ["No in-view PAUSED banner (as panel 5): the toolbar is the pause cue. Judged by dv-drone-paused: position and DroneBattery unchanged at paused frames 10 and 100.",
        "PROPOSAL: the 'queued' note in the menu row is mine; the design says the press waits for the first live frame, not how the OSD shows it."], dy=34)

os.makedirs("docs/designs/devices/mockups", exist_ok=True)
s.save("docs/designs/devices/mockups/devices-hud-drone.svg")
print("wrote docs/designs/devices/mockups/devices-hud-drone.svg")
