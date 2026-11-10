<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the whole note
related-designs:
  - ../../DESIGN_Terrain_Import_UTM.md — §8.5 (S-3 as built: the damage overlay rides the P-7a obstacle bake), ⭐ §8.7 (S-5 measured on demo-city)
  - ../../DESIGN_Scale_Rig.md — the rig the scale-1 numbers belong to
  - HANDOFF_Coordinator_To_Backend_Navmesh_Prefill_And_Utm.md — the same bake code (CE-4393)
-->

# NOTE — coordinator → backend: destruction on scale-1 — two numbers to take, one cost you should know about

Destructible buildings (CE-4391) are built: S-0…S-5, UTM §8.2–§8.7. ⛔ **scale-1 could not be generated on the cloud VM**, because
`scripts/terrain-scale.py` needs `netconvert`. So the rig figures are yours, on the Windows machine, whenever they fit your queue. ⛔ Not urgent;
⛔ do not interrupt CE-3427 or CE-4393/4394.

## ⭐ The cost: every rebake walks EVERY tile

Measured on demo-city (1 156 tiles, Debug, 4 cores; UTM §8.7):

| | |
|---|---|
| one passage, cache warm | 0.44 s, **1 tile** moved |
| a collapse | 0.46 s, 2 tiles |
| ⚠ the same world re-baked with NOTHING changed | **0.42 s** |

So the cost is the fixed walk over every tile (the per-tile triangle gather + hash for the key), not the changed tiles.
- `TerrainResidency.BakeObstacles` goes through `_navmeshFactory.Build(bake.World)`: a full build through the cache.
- If that walk scales with the tile count, a scale-1 rebake is tens of seconds. That is an extrapolation (≈ 19 s at ~52 000 tiles), not a
  measurement. It would also apply to every static obstacle (P-7a), not only to destruction.
- ⇒ a passage would stay unwalkable for that long, and an artillery barrage would keep one bake running back to back.
- ⭐ The lean, yours to judge: a touched-tile path for the obstacle and damage rebake. Only the tiles whose input boxes the change overlaps are
  gathered, hashed and swapped; the rest are carried over from the current snapshot (`RecastNavmeshFactory.Rebake` / `R218P2_Rebake_BakesOnlyTheTilesAChangeTouched`
  bakes only the touched tiles, but the KEY walk still covers all of them).
- ⚠ It overlaps CE-4393's prefill: both need the bake's per-tile input walk to be cheap or skippable. That makes it a natural thing to look at
  in the same pass.

## The numbers to take on scale-1 (the rig, Release)

1. rebake seconds for one building's tiles, cache warm, measured, not extrapolated;
2. the sim frame (ms) and garbage (bytes per frame) with 1 000 men and an artillery barrage of tens of breaches a minute, against R-4192 (≤ 10 ms,
   0 B steady);
3. the sight / fire trace cost with a damage table present: +29 % on a 30 m trace on demo-city in Debug; judge it in Release in the rig's query
   mix.

The measurement rails exist: `FDP/Toolkits/Fdp.Toolkits.Tests/Terrain/BuildingDamageMeasurements.cs` (`HROT_RUN_MEASURE=1`), and they run against
any terrain. Put the results in UTM §8.7, which is the table's home.

## FYI

- **Corridor struct:** `NavigationCorridorMuscle` grew by 4 bytes in S-3 (`TileSequence`, the per-path change stamp). A replay recorded before
  S-3 does not restore its corridors.
- **Flake:** `Collapse_HotPath_AWarmTraceThroughACollapsedBuilding_AllocatesNothing…` failed once in a 1 605-test run. It passed alone and in its
  namespace twice. If you see it again, it is mine: tell me, and it gets a tracker row and `[FlakyFact]`.
