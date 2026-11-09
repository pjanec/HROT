# STATUS — backend: the large terrain ([handoff](HANDOFF_Coordinator_To_Backend_Large_Terrain.md), started at `171e46e0c`)

Machine for every number below: this cloud VM (4 cores, 15 GB), Debug build unless marked Release.

| # | id | state | commit | note |
|---|---|---|---|---|
| L-1 | CE-4363 | IN PROGRESS | — | see below |
| L-2 | CE-4364 | — | | |
| L-3 | CE-4365 | — | | |
| L-4 | CE-4366 | — | | |
| L-5 | CE-4367 | — | | |
| L-6 | CE-4368 | — | | |
| L-7 | CE-4369 | — | | |
| L-8 | CE-4370 | — | | |

## L-1 — the scale terrain

`scripts/terrain-scale.py --seed 1` → `assets/terrain/scale-1/` (gitignored) in **11 s** (the synth set itself: 17 s). Two runs give byte-identical
folders (`diff -r`). Placement geometry factored into `scripts/terrain_placement.py`; `demo-city-quarters.py` imports it (its `--check` still holds).

| | scale-1 |
|---|---|
| extent | 5 500 × 5 500 m, heights 2 751² posts at 2 m (z 450…980; the synth set's 125 500 NODATA posts take their row's nearest height) |
| roads | 9 819 lanes, 818 junctions (SUMO 1.20.0 netconvert) |
| houses | **1 651 of 1 800 sites**: 729 house-2f · 322 cqb-house · 188 house-3f · 133 office-4f · 130 shop-2f · 95 courtyard-house · 54 market-hall; 8 shrunk to clear; dropped 67 on a lane, 1 on a junction, **81 on a neighbour** (terraced rows share walls; the templates keep a 1 m gap) |

### Found by loading it — fixed

| finding | fix | rail |
|---|---|---|
| ⚠ a **New Scenario on a terrain never loaded**: the blank file had `"entities": []`, `ScenarioSerializer` needs an object | `StorageProcessManager.WriteBlankScenario` writes `{}` | `ClusterMasterNewScenarioTests.N2_*` (red-proved) |
| ⚠ **every house at a non-right angle km from the origin refused the terrain** ("footprint is degenerate"): `PolygonMath.SignedArea2` summed absolute products (≈ 2.5e7 at 5 km) in float — a wall's 6 m² cancelled to 0 | the sum relative to the first vertex (same value, exact) | `TemplatePlacementTests.CE4363_*` — every template × 360 rotations at 5 km (red before) |
| ⚠ **the ground mesh never finished** (> 10 min, 350 % CPU): `TerrainWorldMesh.AddGround` scanned every prism for each of 7.5 M ground cells (stack sample: `Excluded` ← `AddGround` ← `RecastNavmeshFactory.BakeMeshes`) | `GroundBins`: solids, water, woods filed once in 32 m bins; same predicates | `GroundBinsEqualityTests` — the binned ground = the plain scan's on demo-city, test-town, highway-rest, bt-range (red-proved by two mutations) |

### Other lanes' reds seen at the merge (not touched)

- `Fdp.Toolkits.Tests`: `S0IdTypesRailTests.S0_EveryNodeIdMember_IsANodeId` — 7 bare-int node ids in Orchestration placement / Replay Browser
  federation / search types; `FdpOrchestrationCqrsStructTests.TakeCheckpointIntent_HasRequestIdAndLabel` — 3 fields, the rail expects 2.
- Fixed at the merge (stated in its commit): ui's `ManagerWindowTests` still called the removed `AddBreakpoint()` — the solution did not build.

---
_backend lane, 2026-11-09_
