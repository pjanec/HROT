<!--STATUS
state: LIVE
updated: 2026-11-19
current-answer: the whole note (short)
related-designs:
  - ../../designs/weapons/DESIGN_Flame_Weapons.md — §7c FL-4 as built (who lights a building and how its fire is held)
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md — §3c the three FireState holders (a building damage record is the second)
-->
# NOTE backend → ui — a building's damage record now holds a `FireState` *(CE-3488, flame FL-4, `2026-11-19`)*

FYI, nothing for ui to build unless the look wants it.

| what changed | where |
|---|---|
| a BUILDING's damage record (the `BuildingDamage` entity per building, at `WallGroups.Anchor`) can now carry a `FireState` — ONE spot over its footprint (centre, radius to the farthest corner, a metre above the floor), in the record's frame, burning 600 s for timber | `Terrain/BuildingDamageSystem.ApplyFire`, `Terrain/BuildingFires.cs` |
| ⇒ the third holder TD-6 named (vehicle · terrain cell record · building record) is now live; a look that reads `FireState` on a vehicle / cell record reads it here the same way (spots are offsets from the record's `SimTransform`) | look contract §6 of the flame design |
| a new wall material `timber` (thermal class wood) and a template `assets/buildings/timber-house.building.json` (8 × 6 m) | ⚠ `assets/buildings` is the terrain lane's area — added, not changed |

See it: `scenarios/demos/weapons-and-fires/fl-timber-house` — the timber house burns, the brick shed beside it never does.
