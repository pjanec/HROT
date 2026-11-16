<!--STATUS
state: LIVE — in progress
updated: 2026-11-16
current-answer: §1 the items, §2 lane decisions, §3 gates
related-designs:
  - ../../DESIGN_Vehicle_Breaching.md — B-D4, B-D11, §7.6, §7.8 (as built)
  - ../Architect_Question_112_Terrain_Versions_On_Reimport.md — terrain versions (R-4280)
  - HANDOFF_Coordinator_To_Terrain_Breaching_Fixes_And_Versions.md — the dispatch (scope frozen at b324236a6)
-->

# STATUS — `terrain`: breaching fixes + terrain versions (R-4280; CE-4629, CE-4630, CE-5004) — started at `88e5520ff`

## 1. The items

| # | item | state | evidence |
|---|---|---|---|
| 1 | R-5000 changed — a vehicle crosses a kerb | ✅ | `HullTrace.MinPanelHeightM` 0.25 m, skipped in the hull trace; `CarKinematicsSystemTests.R4280_*` (a tank and a possessed car over 10 cm; the car still held by 1 m) — red-proved (skip removed ⇒ 2 red); DESIGN §7.8 row |
| 2 | CE-4630 — a possessed soldier cannot enter a bumped hole | ⏳ | |
| 3 | CE-4629 — a bulldozer's hole is blade-wide | ⏳ | |
| 4 | feature scenario `tt-dozer-breach` | ⏳ | |
| 5 | AQ112 / CE-5004 — terrain versions | ⏳ | |

## 2. Lane decisions (for the user's review)

| id | decision | what would change it |
|---|---|---|
| R-5056 | the kerb height is a constant `HullTrace.MinPanelHeightM` = 0.25 m beside `HullBandM`, not a row of `contact-breach.json`: the hull trace lives in the terrain world, below the breach table, and every hull (AI, possessed, any class) uses one rule | a per-class kerb (a car vs a tank) ⇒ move it to the table |

## 3. Gates
*(per item)*
