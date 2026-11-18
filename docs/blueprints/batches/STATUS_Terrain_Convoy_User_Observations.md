<!--STATUS
state: LIVE — REPORTED (CE-1959 and CE-1972 done)
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
| **CE-1972** the convoy leader weaves | ✅ live: cv-red-lights, the lead's x 143.7 ↔ 145.9 every ~9 s (±1.5 m/s); alone it holds 145.7 · isolated in SumoSharp: a capped priority shadow with the column fed behind it swerves on 146/180 samples | B6 lateral evasion: the convoy's gap squares carried the follower's velocity and SumoSharp projected the one at the lead's rear INTO its slowed shadow ⇒ a 2 m/s swerve; the followers follow the lead's trail | `AddConvoyGaps` tells the squares AT REST (R-5063). Live after: x 145.65–145.85 | `TrafficBodyFeedTests.CE9002_*` (moving again ⇒ red) · `AVehicleHandedToSumoArrivesTests.CE1972_*` · cv-red-lights "never swerves" rows (red with moving squares) |

## 1a. Lane decisions (for the user's review)

| id | decision | what would change it |
|---|---|---|
| R-5063 | the convoy's gap squares are told to SUMO AT REST (velocity 0), the follower bodies keep theirs | a SumoSharp obstacle model that needs the squares' speed (e.g. a car that must follow a square as a leader at speed) ⇒ give only the squares NOT at the lead's rear their velocity |

## 2. Gates

| gate | result |
|---|---|
| the seven convoy scenarios (`scripts/scenario-regression.sh cv-ambush,cv-blocked-road,cv-mode-1-to-2,cv-no-cut-in,cv-offroad-join-leave,cv-red-lights,cv-traffic-fire`) | **7 / 7** (the coarser grids break nothing) |
| `Fdp.Toolkits.Tests` TerrainWorld / SpatialHash / RaycastSolver | 107 / 107 |
| `Hrot.Traffic.Tests` PedestriansPanicIntoCivilians | 11 / 11 |
