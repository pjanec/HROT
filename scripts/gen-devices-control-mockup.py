#!/usr/bin/env python3
"""Mockup for docs/designs/devices/DESIGN_Simulated_Devices.md §3.13f - the HUD of the CONTROL UNIT that operates weapons it is not mounted on: the
remote weapon station (RWS) console and the 9K11 launcher box (SD-S4c, D-147..D-157; R-4138 / R-4143), panels 20-23. Built on scripts/mockup_kit.py, in
the house style of the other device sheets (gen-devices-drone-mockup.py, gen-devices-designator-mockup.py).

    python3 scripts/gen-devices-control-mockup.py      # -> docs/designs/devices/mockups/devices-hud-control.svg

SCOPE (R-4151, what this picture specifies and what it does not):
  SPECIFIES  what ControlUnitHudGizmo draws in a view bound to the console while the camera rides the selected weapon's sight (DeviceView WeaponSight,
             D-152): the name and phase chip, the selected weapon's lines (ammo, azimuth / elevation of the ACTUAL aim), the reticle, the LAY marker
             (where the command points, relative to the actual aim - the drives' lag made visible), the READY / FIRING lamp, the zoom step, the message
             line, the launcher lamps of the box, the fire MODE switch (SINGLE / BURST / AUTO, D-157) and the six command chips (POWER SELECT ARM FIRE
             ZOOM VIEW, D-154) as HudPanel buttons with hotkey badges (D-122 / D-124).
  CONTEXT    drawn dashed / greyed and tagged "unchanged": the station's compass strip, the operator's unit lines, the toolbar's run controls (panel 23).
             The picture, the tracers, the impacts and the target are world effects, illustrative.
  PROPOSAL   every layout position is a proposal where the design is silent (the design names WHAT the HUD shows, not WHERE).
Panel numbers continue the device sheets (1-6, 7-11, 12-15, 16-19).
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width
import mockup_hud as HUD
from mockup_hud import (PW, PH, GOLD, WHITE, PHASE, KEYS, PLAY, ht, hline, scene, brackets, optic, reticle, gun_marker, range_row, switch2, tracers, lamp, status,
                        view_label, zoom_readout, message, chips, mode_switch, launchers, compass_ctx, unit_lines, wrap, para, head, legend)

AIMROW = -30                          # the aim chips' row above the command chips (shared with the lock sheet)
W, H = 1460, 1500
s = Svg(W, H, "Simulated devices — the CONTROL UNIT: the remote weapon station and the 9K11 launcher box  (SD-S4c, DESIGN_Simulated_Devices §3.13f)",
        "Full colour = SPECIFIED by this mockup · grey dashed + 'unchanged' = CONTEXT, not specified · the picture, the tracers and the target are world effects, illustrative.")
HUD.bind(s)


# ── page ───────────────────────────────────────────────────────────────────────────────────────────────
HUD.defs()
HUD.layout([30, 750], [180, 850])


s.rect(30, 56, 12, 12, "#000", stroke=WHITE, sw=1); s.text(48, 66, "specified HUD element (full colour, dark halo — the station's HudText over the view)", C.SUB, 11)
s.rect(600, 56, 12, 12, "none", stroke="#8A8A96", dash="4 3"); s.text(618, 66, "context — unchanged, not specified here", C.SUB, 11)
s.text(30, 88, "Frames are 640 × 360 (16:9), the view riding the selected weapon's sight: the RWS's sight is SLAVED (SightMode = Slaved, D-181 — the cross is the commanded line, the GUN marker the barrel). Layout positions are PROPOSALS where the design is silent.", C.PEND, 11)
lx = 30                                                                                          # the console's procedure ladder (D-154)
s.text(lx, 114, "THE CONSOLE PROCEDURE (D-154)", "#9FB7D8", 11, weight="bold"); lx += 196
for i, ph in enumerate(("OFF", "STANDBY", "ARMED")):
    w = text_width(ph, 11.5) + 18
    s.rect(lx, 102, w, 20, "#000", rx=4, op=0.7, stroke=PHASE[ph], sw=1.4); s.text(lx + w / 2, 116.5, ph, PHASE[ph], 11.5, "middle", "bold"); lx += w
    if i < 2:
        s.text(lx + 4, 116.5, ["POWER ▸", "ARM ▸"][i], "#8A8A96", 10); lx += text_width(["POWER ▸", "ARM ▸"][i], 10) + 10
s.text(lx + 8, 116.5, "SELECT steps the weapon in STANDBY or ARMED · ARM needs the selected weapon loaded (D-112) · FIRE per press or held, by the weapon's kind", "#8A8A96", 10)
s.text(30, 136, "Every chip is a HudPanel button (D-122): clickable, with its hotkey badge (D-124 — the keys are proposals). The hover frame, the cursor and RMB-LOOK are the virtual adapter's (panels 16–19). For a console the look IS the lay (D-151).", C.NOTE, 11)
s.text(30, 152, "The RWS type lists TRACK, RANGE and the RNG switch beside the base chips (D-172, D-174, D-181) — which controls exist is type data; the 9K11 box lists none of them.", C.NOTE, 11)

# ── 20 · RWS armed, ready ──────────────────────────────────────────────────────────────────────────────
x, y = head(0, "20 · RWS CONSOLE, ARMED — the slaved sight, the cross and the GUN marker (dv-rws-mg)",
            "Scope: the slaved sight's view with the cross, the GUN marker, ammo, the aim readout, RANGE, the MODE and RNG switches, ZOOM, the lamp, the TRACK / RANGE chips and the six base chips. The compass and the unit lines are context.")
scene(x, y, 2.4); brackets(x, y); reticle(x, y); gun_marker(x, y, -58, 24); compass_ctx(x, y); unit_lines(x, y)
status(x, y, "RWS CONSOLE", "ARMED", (("WEAPON", "7.62 MM MG", WHITE), ("AMMO", "187", WHITE), ("AIM", "AZ 047°  EL +2.5°", "#C8C8D0")))
range_row(x, y, "600 m", "AUTO · under the cross")
mode_switch(x, y, 1, dy=22); switch2(x, y, "RNG", ("AUTO", "HOLD"), 0, "RNG", 50)
lamp(x, y, True, "SLEWING", C.AMBER); view_label(x, y, "RWS CONSOLE", "sight head, slaved"); zoom_readout(x, y, "4×")
message(x, y, "GUN SLEWING", C.AMBER, "READY when the GUN marker reaches the cross")
chips(x, y, (("TRACK", "ready"), ("RANGE", "off")), dy=AIMROW)
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "ready"), ("ZOOM", "ready"), ("VIEW", "off")))
legend(x, y, "the ARMED chip (green) · the selected weapon's lines: name, AMMO, and AZ / EL of the ACTUAL aim (the replicated `TurretPose`) · the cross: the slaved sight head's own line, the COMMANDED direction (`FireControl.LayDir`) · the GUN marker: where the barrel actually points (the replicated `TurretPose`) against the cross — the gun is still turning toward it · RANGE under the cross with its mode word, the RNG switch on AUTO (RANGE greyed, nothing to latch), TRACK pressable · "
             "the MODE switch (D-157): its three positions SINGLE · BURST · AUTO with the set one lit, BURST here, and its hotkey badge · the lamp relabelled SLEWING (amber) until the gun is on the cross, then READY · ZOOM 4× · SELECT greyed (one weapon), VIEW greyed (a gun has no flight to watch)",
       "the compass · the unit lines · the picture and the truck (the weapon's camera, world)",
       ["The RWS's sight is SLAVED (`SightMode = Slaved`, D-181): the picture is the sight head's own line, so the operator sees the cross move at once and the GUN marker follow with the drives' lag, converging on the cross as the turret arrives. (The simple box / TV launcher keeps `SightMode = OnWeapon`, D-152, panel 22: its reticle is the barrel.) The stick commands a slew RATE, never a position (D-151).",
        "MODE is a latching switch (D-157): a click or its hotkey steps it, and beside a replica the replica's rotary owns it and this switch is display-only (D-135). SINGLE = one round per FIRE press, BURST = one burst per press at the weapon's own burst length (CE-11033), AUTO = while FIRE is held; the message line follows the mode.",
        "PROPOSAL: the weapon lines, the AZ / EL readout, the switch's place and look, the markers' look, the RNG switch's place, the aim-chip row, the corner brackets and the layout are mine; the design names what the HUD shows, not where."])

# ── 21 · RWS firing, held ──────────────────────────────────────────────────────────────────────────────
x, y = head(1, "21 · RWS CONSOLE, FIRING — AUTO, FIRE held, the ammo counting down (dv-rws-mg, dv-rws-modes)",
            "Scope: the MODE switch on AUTO, the FIRING lamp, FIRE set, the ammo falling, the GUN marker converged on the cross, the message. Tracers and impacts are world effects.")
scene(x, y, 2.4); tracers(x, y); brackets(x, y); reticle(x, y); gun_marker(x, y, -8, 3); compass_ctx(x, y); unit_lines(x, y)
status(x, y, "RWS CONSOLE", "ARMED", (("WEAPON", "7.62 MM MG", WHITE), ("AMMO", "142", C.AMBER), ("AIM", "AZ 047°  EL +2.5°", "#C8C8D0")))
range_row(x, y, "600 m", "AUTO · under the cross")
mode_switch(x, y, 2, dy=22); switch2(x, y, "RNG", ("AUTO", "HOLD"), 0, "RNG", 50)
lamp(x, y, True, "FIRING", C.RED); view_label(x, y, "RWS CONSOLE", "sight head, slaved"); zoom_readout(x, y, "4×")
message(x, y, "FIRING", C.RED, "AUTO — FIRE held, release to stop")
chips(x, y, (("TRACK", "ready"), ("RANGE", "off")), dy=AIMROW)
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "on"), ("ZOOM", "ready"), ("VIEW", "off")))
legend(x, y, "the MODE switch on AUTO (the only position that fires while FIRE is held) · the lamp turned red and relabelled FIRING (`FireControl.Trigger` held) · the FIRE chip set (blue) while the key or the button is down · AMMO falling (amber below a quarter) · the GUN marker nearly on the cross: the gun has arrived, RANGE and the RNG switch as panel 20 · 'FIRING' with the release hint",
       "the compass · the unit lines · the tracers and the impacts on the truck (the rounds, world)",
       ["On BURST the same FIRE press would fire ONE burst of the weapon's own burst length and stop by itself, on SINGLE one round, with no FIRING state held (D-157). The weapon's OWN brain fires — the console only raises `Trigger` or moves `LaunchSeq` (D-149, D-154).",
        "PROPOSAL: the amber ammo threshold (a quarter of the belt) and the FIRING relabel are mine; the design names the held trigger, not its look."])

# ── 22 · the 9K11 box ──────────────────────────────────────────────────────────────────────────────────
x, y = head(2, "22 · THE 9K11 BOX, ARMED — four launcher lamps, SELECT (dv-remote-launcher, dv-box-two-launchers)",
            "Scope: the box's own sight (GuideFrom = Self), the launcher lamps with the selected one framed, ARMED, READY, the six chips. The flight itself is panel 11 — the same box, GUIDING.")
scene(x, y, 3.4, target="tank"); optic(x, y); reticle(x, y); compass_ctx(x, y); unit_lines(x, y)
status(x, y, "9S415 · 9K11 CONTROL BOX", "ARMED")
launchers(x, y, (("spent", False), ("loaded", True), ("loaded", False), ("none", False)))
mode_switch(x, y, 0, refused=True)
lamp(x, y, True); view_label(x, y, "9K11 BOX", "through its own sight"); zoom_readout(x, y, "8×")
message(x, y, "LAUNCHER 2 READY", GOLD, "SELECT steps the launcher · FIRE launches")
chips(x, y, (("POWER", "on"), ("SELECT", "ready"), ("ARM", "on"), ("FIRE", "ready"), ("ZOOM", "ready"), ("VIEW", "off")))
legend(x, y, "the box's name and the ARMED chip · the LAUNCHER lamps: 1 spent (greyed, crossed), 2 loaded and SELECTED (gold frame), 3 loaded, 4 not connected (dashed) · the READY lamp for the selected launcher · 'LAUNCHER 2 READY' · the MODE switch GREYED on SINGLE (a launcher offers SINGLE only, D-157) · SELECT pressable (steps 2 → 3), FIRE pressable, VIEW greyed until a round flies",
       "the compass · the unit lines · the picture and the tank",
       ["The box commands up to four uncrewed launchers on cables (D-155); each `Selected` index is a launcher of `ControlledEntities` (D-147). ARM needs the SELECTED launcher loaded (D-154, D-112): selecting the spent launcher 1 would show NO MISSILE and ARM refused. A press of MODE is refused the same way (D-157): a per-press weapon has one mode.",
        "The view is the box's own sight, not the launcher's camera (`GuideFrom = Self`, D-153): the round flies the line of the sight he looks through, so the reticle is the guide line, as for the TOW sight unit (panel 8).",
        "D-156 lists this panel as 'ARMED, a guided flight'; the flight half is drawn as panel 11 (the same chrome: lamp 1 in flight, SELECT refused, the stick gauge) so the two do not repeat each other.",
        "PROPOSAL: the lamp strip's place and look, the 8× field and the hotkeys are mine."])

# ── 23 · paused ────────────────────────────────────────────────────────────────────────────────────────
x, y = head(3, "23 · RWS CONSOLE, PAUSED — FIRE and the lay held, the view still answers (dv-rws-paused)",
            "Scope: the same HUD in pause: FIRE greyed, the cross and the GUN marker held where they were (the stick is ignored), MODE and ZOOM still acting. The toolbar's paused state is context.")
scene(x, y, 3.6); brackets(x, y); reticle(x, y); gun_marker(x, y, -70, 28, tag="HELD"); compass_ctx(x, y); unit_lines(x, y)
status(x, y, "RWS CONSOLE", "ARMED", (("WEAPON", "7.62 MM MG", WHITE), ("AMMO", "187", WHITE), ("AIM", "AZ 047°  EL +2.5°", "#C8C8D0")))
range_row(x, y, "600 m", "AUTO · under the cross")
mode_switch(x, y, 0, note="set in pause", dy=22); switch2(x, y, "RNG", ("AUTO", "HOLD"), 1, "RNG", 50)
lamp(x, y, False); view_label(x, y, "RWS CONSOLE", "sight head, slaved"); zoom_readout(x, y, "8×")
message(x, y, "FIRE HELD IN PAUSE", C.AMBER, "MODE · RNG · ZOOM · SELECT · VIEW still act; TRACK waits")
chips(x, y, (("TRACK", "off"), ("RANGE", "ready")), dy=AIMROW)
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "off"), ("ZOOM", "ready"), ("VIEW", "off")))
ty = y + PH + 8
s.rect(x, ty, 300, 24, C.PAGE, stroke="#8A8A96", dash="4 3", rx=3)
s.glyph(0xf111, x + 8, ty + 5, "#E63838", 13); s.glyph(PLAY, x + 40, ty + 5, "#E63838", 13)
s.text(x + 64, ty + 16, "toolbar, paused: ▶ shown (context)", C.NOTE, 10.5)
legend(x, y, "the READY lamp off and FIRE greyed (a world effect is refused in pause, never queued — D-78, D-154) · TRACK greyed (D-180: a lock is a write, it waits for a live frame) while RANGE stays pressable (a latch of the measured range is device state, shown, nothing flies) and the RNG switch stepped to HOLD in pause · the GUN marker held, tagged HELD, while the stick is swung (the gun does not turn, the cross does not move) — the lay is integrated on live frames only (D-151) · the MODE switch stepped to SINGLE in pause, tagged 'set in pause' (MODE is device state, nothing fires — D-157) · ZOOM 8× changed in pause and the picture magnified (observation) · the message 'FIRE HELD IN PAUSE'",
       "the toolbar's run controls (paused) · the compass · the unit lines · the picture",
       ["Judged by dv-rws-paused: FIRE held through a pause and the stick swung hard — ⛔ `never` a shot during the pause, the turret pose unchanged. Nothing turns, nothing fires; on resume the lay continues from where it was held.",
        "No in-view PAUSED banner (as panels 5 and 15): the toolbar is the pause cue. PROPOSAL: the HELD tag and the message's words are mine; the design says the lay, the lock and the trigger are frozen, not how the HUD shows it."], dy=34)

os.makedirs("docs/designs/devices/mockups", exist_ok=True)
s.save("docs/designs/devices/mockups/devices-hud-control.svg")
print("wrote docs/designs/devices/mockups/devices-hud-control.svg")
