<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md — ⭐ THE DESIGN (approved R-4293, READY-TO-BUILD): TD-8, TD-9, TD-12 are yours
  - PROTOCOL_Autonomous_Lanes.md — HOW you work
  - HANDOFF_Coordinator_To_Backend_Thermal_Drivers.md — backend builds the components and the calculator that read your data
-->

# HANDOFF — coordinator → `terrain`: THERMAL DRIVERS, the environment half (CE-4662)

**For:** terrain. **From:** coordinator. **Dispatched at the commit that adds this file.**
🔒 User, `2026-11-18`: *"approved"* (the design, R-4293) · *"write the handoffs for backend and terrain, maybe with a bit higher priority than non
critical bugs"*.

## ⭐ Priority
Your P3 / P4 rows are done ⇒ take this **NEXT, before your remaining second-tier (T2) rows** (CE-4401, CE-3428, CE-3426, CE-3440). Finish the row in
progress first.

## Read first
[`DESIGN_Thermal_Drivers.md`](../../designs/thermal/DESIGN_Thermal_Drivers.md) — TD-1 (drivers, never temperatures), TD-8, TD-9, TD-12, §4 "changed, not new".
[`PROTOCOL_Autonomous_Lanes.md`](PROTOCOL_Autonomous_Lanes.md) as before (status `STATUS_Terrain_Queue.md` §3 "taken", decisions `DECISIONS_Terrain.md`).

## The row — CE-4662 (TD-S6), three parts

| part | builds | done when |
|---|---|---|
| ① materials (TD-9) | `materials.json` gains `thermalClass` from the FIXED list: asphalt, concrete, brick, metal, glass, wood, soil-dry, soil-wet, sand, grass, dense-vegetation, water, snow — every material of every shipped terrain gets one; `TerrainMaterial` carries it; the terrain export writes it (the image generator's ground material map) | a rail: every material of every terrain in `assets/terrain/` has a class; the export round-trips it |
| ② smoke (TD-12) | a munition's TKB `SmokeBlocksInfrared` (visual-only vs IR-blocking); the area cloud (`EntityAreaEffectCloud`) carries it by its type | a rail: an IR-blocking smoke cloud says so on every node |
| ③ weather (TD-8) | `WeatherConditions.Since` — when the current weather began (APPENDED to `EntityWeather`, R-169), set where the weather changes | a late joiner reads how long it has rained (rail) |

⚠ Backend builds the calculator that reads ① ② ③ (CE-4657). Agree the field names by a NOTE if you choose differently from the design; ⛔ do not build
their side.

## Gates (CLAUDE.md rule 8)
The touched classes + the terrain export suite + the weather replication rails, every new rail red-proved; a wire append ⇒ name the replication
suite you ran (row 8). Fold any deviation back into the design (obligation ⑤).
