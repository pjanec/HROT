<!--STATUS
state: LIVE — in progress (CE-1959 done, CE-1972 open)
updated: 2026-11-18
current-answer: §1 the rows, §2 gates
related-designs:
  - HANDOFF_Coordinator_To_Convoy_User_Observations.md — the dispatch (addressed to convoy; taken by terrain at the user's request, 2026-11-18)
  - ../../designs/vehicle-tactics/DESIGN_Road_Convoy.md — §6 as built (the CE-1959 row)
  - ../../DESIGN_Terrain_World.md — §4.4 W9 (the grids cover the ground beyond)
-->

# STATUS — `terrain` (for `convoy`): the user's convoy observations (CE-1959, CE-1972) — started at `92c46700a` (merged coordinator + `convoy@b6b7005e0`; later coordinator, backend at `d6b219114`)

## 1. The rows

| row | reproduced | root cause | fix | rail (red-proved) · scenario |
|---|---|---|---|---|
| **CE-1959** SUMO cars ignore hits | ✅ live: editor, `cv-red-lights` on demo-city (SUMO 1.20 via `scripts/install-sumo.sh`), a possessed IFV and a possessed rifleman fired 90 rounds at SUMO cars — all `StoppedByTerrain` | ① the collider / perception grids were fitted to the terrain's 800 m bounds; the SUMO net (and the convoy) runs to ~4.4 km ⇒ outside the grid, no round meets a body. ② `TrafficPanicSystem` ran only when the traffic had PEDESTRIANS ⇒ in car-only traffic (all convoy scenarios) no hit car was given up, no driver abandoned his | ① `SpatialGridFit.Covered` = bounds ∪ the attached beyond (the net); ② any traffic demand. Live after: drivers abandon, the cars become ours and take the damage | `TerrainWorldTests.CE1959_*` · `PedestriansPanicIntoCiviliansTests.CE1959_*` · scenario `cv-traffic-fire` (red with the grid fix off) |
| **CE-1972** the convoy leader weaves | ⏳ | | | |

## 2. Gates

| gate | result |
|---|---|
| the seven convoy scenarios (`scripts/scenario-regression.sh cv-ambush,cv-blocked-road,cv-mode-1-to-2,cv-no-cut-in,cv-offroad-join-leave,cv-red-lights,cv-traffic-fire`) | **7 / 7** (the coarser grids break nothing) |
| `Fdp.Toolkits.Tests` TerrainWorld / SpatialHash / RaycastSolver | 107 / 107 |
| `Hrot.Traffic.Tests` PedestriansPanicIntoCivilians | 11 / 11 |
