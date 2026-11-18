<!--STATUS
state: LIVE — the terrain lane's decisions (PROTOCOL_Autonomous_Lanes §3), newest at the top
updated: 2026-11-18
current-answer: the whole file
-->
# DECISIONS — terrain

## R-5066 · CE-4662 ② — the smoke cloud carries "blocks infrared" as a FIELD, not by its type *(the lane's own ruling, for review)*
**DECIDED (lane): `AreaEffectCloud.BlocksInfrared` (byte, appended to `dtAreaEffectCloud`), copied from the munition's `WarheadDto.SmokeBlocksInfrared` when
the burst asks for the cloud.** Why: every cloud is the one bare TKB type 7202, so its type cannot tell a phosphorus screen from a signal smoke. Starter
values: the 60 / 81 / 120 mm mortar smoke bombs block infrared; the hand smoke grenade does not. Revisit if a cloud should get its own TKB type per munition.
📄 DESIGN_Thermal_Drivers §4c.

## R-5065 · CE-4662 ① — the ground SURFACES get thermal classes too *(the lane's own ruling, for review)*
**DECIDED (lane): `materials.json` gains a `surfaceThermalClasses` map beside the materials** — ground soil-dry · open grass · forest dense-vegetation ·
water water · scrub grass; a terrain folder may override it. Why: TD-9 named materials only, but most of what an image generator paints (fields, forest,
water) is a surface with no material. Revisit: wet soil after rain (soil-dry → soil-wet) belongs to the generator's solve from the weather, not to us.
📄 DESIGN_Thermal_Drivers §4c.

## R-5064 · CE-5019 — a burst that breaches a wall within 0.5 m makes no crater *(the lane's own ruling, for review)*
**DECIDED `2026-11-18` (user, via coordinator): KEPT as built. Revisit only if a big bomb landing at a wall should still crater (then scale the skip by kg).**
