#!/usr/bin/env python3
"""Mockup for docs/designs/devices/DESIGN_Simulated_Devices.md §3.13g - the HUD of the FIRE CONTROL UNIT's AIMING: the lock tracker, the lead, the ranged line,
the guidance switch and the view cycle (SD-S4d, D-173 - D-181; R-4138 / R-4143), panels 27-32. Built on scripts/mockup_hud.py (the helpers shared with the
control-unit sheet, gen-devices-control-mockup.py - one drawing of every element).

    python3 scripts/gen-devices-lock-mockup.py      # -> docs/designs/devices/mockups/devices-hud-lock.svg

SCOPE (R-4151, what this picture specifies and what it does not):
  SPECIFIES  what the unit's projector adds to the control-unit HUD (panels 20-23): the LOCK box (SEARCH / TRACKING / LOST), the LEAD pipper (the aim the
             MUSCLE is using, WeaponTaskStatus.AimPoint), the GUN marker (the barrel's actual pose against the slaved sight's cross), the RANGE readout and the
             RNG switch (AUTO / HOLD), the TRACK and RANGE chips, the GUIDE switch (SEEKER / STICK / LINE), NO LOCK, the missile-view chrome (the seeker ring,
             the stick gauge, VIEW ▸ MISSILE), as HudPanel buttons / switches with hotkey badges (D-122 / D-124).
  CONTEXT    drawn dashed / greyed and tagged "unchanged": the compass strip, the operator's unit lines. The picture, the targets and the wall are world effects.
  PROPOSAL   every layout position is a proposal where the design is silent.
Panel numbers continue the device sheets (1-6, 7-11, 12-15, 16-19, 20-23, 24-26 the link).
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width
import mockup_hud as HUD
from mockup_hud import (PW, PH, GOLD, WHITE, CYAN, ht, hline, scene, brackets, optic, reticle, lamp, status, view_label, zoom_readout, message, chips,
                        mode_switch, switch2, compass_ctx, unit_lines, wrap, para, head, legend, lock_box, lead_pipper, gun_marker, range_row,
                        seeker_ring, stick_gauge)

W, H = 1460, 2060
s = Svg(W, H, "Simulated devices — the FIRE CONTROL UNIT's aiming: lock, lead, ranged line, guidance switch, view  (SD-S4d, DESIGN_Simulated_Devices §3.13g)",
        "Full colour = SPECIFIED by this mockup · grey dashed + 'unchanged' = CONTEXT, not specified · the picture, the targets and the wall are world effects, illustrative.")
HUD.bind(s)
HUD.defs()
HUD.layout([30, 750], [190, 820, 1450])

s.rect(30, 56, 12, 12, "#000", stroke=WHITE, sw=1); s.text(48, 66, "specified HUD element (full colour, dark halo — the station's HudText over the view)", C.SUB, 11)
s.rect(600, 56, 12, 12, "none", stroke="#8A8A96", dash="4 3"); s.text(618, 66, "context — unchanged, not specified here", C.SUB, 11)
s.text(30, 88, "Frames are 640 × 360 (16:9). Panels 27, 31, 32: the sight head's own line of sight (SightMode = Slaved, D-181); 28 – 30: the missile unit's sight, then the round's seeker view. Layout is PROPOSAL.", C.PEND, 11)
s.text(30, 114, "AIM METHODS (D-173)", "#9FB7D8", 11, weight="bold")
xx = 190
for nm, c in (("LAY", GOLD), ("LINE", GOLD), ("STICK", GOLD), ("LOCK", CYAN)):
    w = text_width(nm, 11.5) + 18; s.rect(xx, 102, w, 20, "#000", rx=4, op=0.7, stroke=c, sw=1.4); s.text(xx + w / 2, 116.5, nm, c, 11.5, "middle", "bold"); xx += w + 6
s.text(xx + 6, 116.5, "the type lists which exist · LOCK: ", "#8A8A96", 10); xx += 6 + text_width("the type lists which exist · LOCK: ", 10)
for nm, c in (("SEARCH", WHITE), ("TRACKING", C.GREEN), ("LOST", C.RED)):
    w = text_width(nm, 10.5) + 14; s.rect(xx, 104, w, 17, "none", stroke=c, rx=3, sw=1.3, dash="3 2" if nm != "TRACKING" else None); s.text(xx + w / 2, 116.5, nm, c, 10.5, "middle", "bold"); xx += w + 5
s.text(30, 140, "Every chip is a HudPanel button (D-122) with its hotkey badge (D-124 — proposals); a switch is the latching panel element of D-142 (a click or its hotkey steps it; beside a replica the replica's rotary owns it, D-135).", "#8A8A96", 10)
s.text(30, 156, "The aim chips (TRACK, RANGE) sit in a row above the command chips so the six commands of panels 20-23 keep their place.", "#8A8A96", 10)

AIMROW = -30   # the aim chips' row above the command chips

# ── 27 · RWS tracking ───────────────────────────────────────────────────────────────────────────────────────────
x, y = head(0, "27 · RWS CONSOLE, TRACKING a crossing truck — lock box, LEAD, GUN (dv-rws-lock)",
            "Scope: the lock box on the target, the LEAD pipper (where the muscle is aiming the round, ahead of the truck), the GUN marker (the barrel, still turning), RANGE, the MODE and RNG switches, the TRACK chip set.")
scene(x, y, 2.6, at=(-16, 8)); brackets(x, y); reticle(x, y)
lock_box(x, y, -16, 8, 58, 34, "track", "TGT 1 · 620 m")
lead_pipper(x, y, 42, 6, -16, 8); gun_marker(x, y, 22, 2)
compass_ctx(x, y); unit_lines(x, y)
status(x, y, "RWS CONSOLE", "ARMED", (("WEAPON", "7.62 MM MG", WHITE), ("AMMO", "187", WHITE), ("AIM", "AZ 052°  EL +1.2°", "#C8C8D0")))
range_row(x, y, "620 m", "AUTO · laser")
mode_switch(x, y, 2, dy=22); switch2(x, y, "RNG", ("AUTO", "HOLD"), 0, "RNG", 50)
lamp(x, y, True, "SLEWING", C.AMBER); view_label(x, y, "RWS CONSOLE", "sight head, slaved"); zoom_readout(x, y, "4×")
message(x, y, "TRACKING", C.GREEN, "the gun follows the LEAD · FIRE held fires on target", dy=-6)
chips(x, y, (("TRACK", "on"), ("RANGE", "off")), dy=AIMROW)
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "ready"), ("ZOOM", "ready"), ("VIEW", "off")))
legend(x, y, "the TRACKING box (green corners) with its label `TGT n · range` · the LEAD pipper (cyan diamond, dotted from the target's middle): the aim point the MUSCLE is using now (`WeaponTaskStatus.AimPoint`, D-175) — ahead of the truck by the lead · the GUN marker: the barrel's replicated pose, still turning toward the LEAD · the lamp SLEWING (amber) until the gun is on the LEAD, then READY · RANGE with its mode word · the MODE and RNG switches · the TRACK chip set (lock held)",
       "the compass · the unit lines · the picture and the truck (world)",
       ["The cross is the SLAVED sight head's line of sight (D-181): while locked it rides the target, so the operator needs no stick. The LEAD pipper is the muscle's, read from the weapon's turret part by the console's tick (a declared read) — the device never calculates it. TRACK again breaks the lock (the box ends LOST, then the cross is the stick's again).",
        "PROPOSAL: the cyan of the LEAD, the dotted line, the box label, the SLEWING relabel, the RNG switch's place and the aim-chip row are mine."])

# ── 28 · missile unit, locked before launch ─────────────────────────────────────────────────────────────────────
x, y = head(1, "28 · LOCK-ON MISSILE UNIT, LOCKED before launch — seeker sight, GUIDE ▸ SEEKER (dv-lock-fire)",
            "Scope: the unit's own sight with the lock box on the tank, the lock's range, GUIDE on SEEKER, READY, the TRACK chip set. No MODE switch: a launcher offers SINGLE only (D-157).")
scene(x, y, 3.6, target="tank"); optic(x, y); reticle(x, y)
lock_box(x, y, 0, 3, 54, 30, "track", "TGT 1 · 1 840 m")
compass_ctx(x, y); unit_lines(x, y)
status(x, y, "LOCK-ON MISSILE UNIT", "ARMED", (("MISSILES", "2", WHITE), ("LOCK", "TRACKING", C.GREEN), ("FLIGHT", "—", "#C8C8D0")))
range_row(x, y, "1 840 m", "laser")
switch2(x, y, "GUIDE", ("SEEKER", "STICK"), 0, "GUIDE", 22)
lamp(x, y, True); view_label(x, y, "MISSILE UNIT", "through its own sight"); zoom_readout(x, y, "8×")
message(x, y, "LOCKED — READY TO FIRE", C.GREEN, "FIRE launches · the seeker holds the lock", dy=-6)
chips(x, y, (("TRACK", "on"),), dy=AIMROW)
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "ready"), ("ZOOM", "ready"), ("VIEW", "off")))
legend(x, y, "the TRACKING box (green) · the lock's range · GUIDE on SEEKER (the round flies at the locked target, D-176) · READY only with a lock when the type requires one · TRACK set · no MODE switch (a launcher fires SINGLE) · VIEW greyed: no round is flying",
       "the compass · the unit lines · the picture and the tank (world)",
       ["The lock is the DEVICE's tracker holding `FireControl.Target` (D-174); the missile's own seeker (its cone, `GuidedRound.SeekerCos`) takes over at launch. A type with no lock requirement shows READY without it. GUIDE's positions come from the type: a Javelin-class unit offers SEEKER and STICK, a wire-guided box STICK alone (greyed), a TOW-class unit LINE.",
        "PROPOSAL: the lock label, the range row and the switch's place are mine."])

# ── 29 · in flight, NO LOCK ───────────────────────────────────────────────────────────────────────────────────
x, y = head(2, "29 · IN FLIGHT, NO LOCK — the missile's view, GUIDE ▸ STICK offered (dv-lock-lost)",
            "Scope: the missile's view (VIEW ▸ MISSILE) with the seeker ring, the lost lock, NO LOCK in amber where READY was, GUIDE on SEEKER with STICK pressable, TRACK pressable.")
scene(x, y, 1.7, target="tank", at=(118, -34)); seeker_ring(x, y, 64); reticle(x, y, mil_px=2.4)
lock_box(x, y, 118, -34, 44, 26, "lost", "LOST")
compass_ctx(x, y)
status(x, y, "LOCK-ON MISSILE UNIT", "GUIDING", (("MISSILES", "1", WHITE), ("LOCK", "NO LOCK", C.AMBER), ("FLIGHT", "T+ 7.2 s", WHITE)))
switch2(x, y, "GUIDE", ("SEEKER", "STICK"), 0, "GUIDE", 22, note="no lock")
lamp(x, y, True, "NO LOCK", C.AMBER); view_label(x, y, "MISSILE", "through the round's seeker"); zoom_readout(x, y, "1×")
message(x, y, "NO LOCK", C.AMBER, "GUIDE ▸ STICK flies it · TRACK a new target", dy=-6)
chips(x, y, (("TRACK", "ready"),), dy=AIMROW)
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "off"), ("FIRE", "off"), ("ZOOM", "ready"), ("VIEW", "on")))
legend(x, y, "VIEW set, labelled MISSILE (rocket glyph) · the seeker ring · the LOST box on the target outside it · the GUIDING chip (red) · LOCK / FLIGHT lines · NO LOCK (amber lamp and message) · GUIDE on SEEKER with the STICK position pressable and TRACK pressable (the round has a link, D-176) · FIRE and ARM refused in flight",
       "the compass · the picture (the chase view of the round, world) and the tank",
       ["The unit leaves READY for NO LOCK when `GuidedFlight.State` becomes NoLock (D-177): the seeker no longer sees its target; the round flies on to where it last saw it. A round with no link would show the same lamp and offer nothing: GUIDE and TRACK greyed. When the flight ends the unit sets the view back to its sight by itself (D-178).",
        "PROPOSAL: the ring's radius, the LOST box and the amber lamp are mine; the design names NO LOCK, not its look."])

# ── 30 · retarget ─────────────────────────────────────────────────────────────────────────────────────────────
x, y = head(3, "30 · RETARGET IN FLIGHT — GUIDE ▸ STICK, TRACK on the new target B (dv-retarget-in-flight)",
            "Scope: the missile's view with the stick gauge, GUIDE on STICK, the old target A lost, the new target B locked (TRACKING), the hint to hand the round back to the seeker.")
scene(x, y, 1.7, target="tank", at=(-96, 30), second=("tank", 54, -14)); reticle(x, y, mil_px=2.4)
lock_box(x, y, -96, 30, 44, 26, "lost", "A LOST"); lock_box(x, y, 54, -14, 52, 30, "track", "TGT B · 2 460 m")
compass_ctx(x, y)
status(x, y, "LOCK-ON MISSILE UNIT", "GUIDING", (("MISSILES", "1", WHITE), ("LOCK", "B TRACKING", C.GREEN), ("FLIGHT", "T+ 11.8 s", WHITE)))
switch2(x, y, "GUIDE", ("SEEKER", "STICK"), 1, "GUIDE", 22, note="▸ SEEKER hands over")
stick_gauge(x, y, 0.35, -0.10)
lamp(x, y, True, "STICK", GOLD); view_label(x, y, "MISSILE", "through the round's seeker"); zoom_readout(x, y, "1×")
message(x, y, "STICK — B LOCKED", C.GREEN, "GUIDE ▸ SEEKER sends the round at B", dy=-6)
chips(x, y, (("TRACK", "on"),), dy=AIMROW)
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "off"), ("FIRE", "off"), ("ZOOM", "ready"), ("VIEW", "on")))
legend(x, y, "GUIDE set to STICK with the hand-over hint · the stick gauge (axes 0 / 1) · the lamp STICK (gold: the operator flies it) · A's lost box · B's TRACKING box and range · TRACK set (a new lock, `TargetSeq` moved) · the round is flown by the stick until GUIDE ▸ SEEKER",
       "the compass · the picture and the two tanks (world)",
       ["TRACK works in the missile's view (D-178): the tracker holds B while the stick keeps the round on a course that keeps B in the seeker's picture. GUIDE ▸ SEEKER then re-targets the round (D-176): its `Target` becomes B and the seeker takes over; `dv-retarget-in-flight` judges the hit on B.",
        "PROPOSAL: the hand-over hint, the gauge's place and the gold lamp are mine."])

# ── 31 · RWS, unlocked, AUTO range ──────────────────────────────────────────────────────────────────────────────
x, y = head(4, "31 · RWS CONSOLE, UNLOCKED — the cross on a parked truck, AUTO range (dv-rws-point)",
            "Scope: the slaved sight's cross, the GUN marker lagging behind it, RANGE measured under the cross (AUTO), RNG on AUTO, the TRACK and RANGE chips, the lamp.")
scene(x, y, 2.4, at=(0, 6)); brackets(x, y); reticle(x, y); gun_marker(x, y, -52, 14)
compass_ctx(x, y); unit_lines(x, y)
status(x, y, "RWS CONSOLE", "ARMED", (("WEAPON", "7.62 MM MG", WHITE), ("AMMO", "187", WHITE), ("AIM", "AZ 047°  EL +0.8°", "#C8C8D0")))
range_row(x, y, "600 m", "AUTO · under the cross")
mode_switch(x, y, 1, dy=22); switch2(x, y, "RNG", ("AUTO", "HOLD"), 0, "RNG", 50)
lamp(x, y, True, "SLEWING", C.AMBER); view_label(x, y, "RWS CONSOLE", "sight head, slaved"); zoom_readout(x, y, "4×")
message(x, y, "GUN SLEWING", C.AMBER, "READY when the GUN marker reaches the cross", dy=-6)
chips(x, y, (("TRACK", "ready"), ("RANGE", "off")), dy=AIMROW)
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "ready"), ("ZOOM", "ready"), ("VIEW", "off")))
legend(x, y, "the cross (the sight's own line) · the GUN marker offset from it while the barrel turns · RANGE measured under the cross every tick with its mode word (AUTO) · the RNG switch on AUTO, RANGE chip greyed (nothing to latch) · TRACK pressable (a lock instead of the cross) · the SLEWING lamp and message",
       "the compass · the unit lines · the picture and the truck (world)",
       ["The gun follows the POINT at the measured range under the cross (`AimMode = LINE`, D-181), laid from the barrel's own place so the sight-to-barrel offset is solved; the GUN marker shows the lag the drives add. The round flies straight (no drop term for a direct round today, D-181 ③), so the super-elevation is zero; an arc weapon's comes from the range automatically.",
        "PROPOSAL: the range row's wording and the marker's place are mine."])

# ── 32 · RWS, HOLD range ──────────────────────────────────────────────────────────────────────────────────────────
x, y = head(5, "32 · RWS CONSOLE, RANGE HELD — lased at 600 m, the cross on a nearer wall (dv-rws-range-hold)",
            "Scope: RNG on HOLD, the held range (amber) beside the live range under the cross, RANGE pressable (re-lase), the aim at the held range.")
scene(x, y, 2.4, target="wall", at=(24, 10)); brackets(x, y); reticle(x, y)
compass_ctx(x, y); unit_lines(x, y)
status(x, y, "RWS CONSOLE", "ARMED", (("WEAPON", "7.62 MM MG", WHITE), ("AMMO", "187", WHITE), ("AIM", "AZ 061°  EL +0.8°", "#C8C8D0")))
range_row(x, y, "600 m", "HELD", held=True)
ht(x + 190, y + 102 + 3 * 18, "UNDER CROSS", "#A0A0AA", 10.5); ht(x + 268, y + 102 + 3 * 18, "210 m", "#C8C8D0", 12, weight="bold")
mode_switch(x, y, 1, dy=22); switch2(x, y, "RNG", ("AUTO", "HOLD"), 1, "RNG", 50)
lamp(x, y, True); view_label(x, y, "RWS CONSOLE", "sight head, slaved"); zoom_readout(x, y, "4×")
message(x, y, "RANGE HELD 600 m", C.AMBER, "RANGE re-lases at the cross · RNG ▸ AUTO follows it", dy=-6)
chips(x, y, (("TRACK", "ready"), ("RANGE", "ready")), dy=AIMROW)
chips(x, y, (("POWER", "on"), ("SELECT", "off"), ("ARM", "on"), ("FIRE", "ready"), ("ZOOM", "ready"), ("VIEW", "off")))
legend(x, y, "RNG on HOLD · RANGE with its HELD word in amber (the latched value) · the live range under the cross in grey beside it · RANGE pressable (a new lase replaces the held value) · READY: the aim at the held range is reached",
       "the compass · the unit lines · the picture and the wall (world)",
       ["The weapon aims at the point 600 m along the cross (`RangeM` latched from `WeaponTaskStatus.MeasuredRange`, D-181 ④), not at the wall: the cross moves the aim along the same range, as a stored-range fire-control computer does. A unit never ranged uses the type's zero range. Pause freezes the latched value with the lay (D-83).",
        "PROPOSAL: the amber HELD word and the UNDER CROSS line are mine."])

os.makedirs("docs/designs/devices/mockups", exist_ok=True)
s.save("docs/designs/devices/mockups/devices-hud-lock.svg")
print("wrote docs/designs/devices/mockups/devices-hud-lock.svg")
