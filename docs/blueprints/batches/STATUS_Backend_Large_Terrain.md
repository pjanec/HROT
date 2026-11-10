# STATUS — backend: the large terrain ([handoff](HANDOFF_Coordinator_To_Backend_Large_Terrain.md), started at `171e46e0c`)

Machine for every number below: this cloud VM (4 cores, 15 GB), Debug build unless marked Release.

| # | id | state | commit | note |
|---|---|---|---|---|
| L-1 | CE-4363 | DONE | `f36e43f9e` | see below; scale-1 now carries the 1 m height store (L-3) |
| L-2 | CE-4364 | DONE — rig + baselines on demo-city; ⛔ scale-1 does not load on the cluster (→ L-4) | this commit | [DESIGN_Scale_Rig](../../DESIGN_Scale_Rig.md): `scripts/scale-rig.py` (imports perf-stress's cluster machinery), garbage per system in the scheduler's profiler, `process` numbers on `/diagnostics/architecture`, opt-in rail `ScaleRigRail` (`HROT_SCALE_RIG=1`) |
| L-3 | CE-4365 | DONE | this commit | [Terrain Height §9](../../DESIGN_Terrain_Height.md): tiles + index, binary `.heights`, `hrot-heights` (one C# resampler). 1 m scale-1: load **0.31 s / 127 MB** (the same as a 1 m `.asc`: 5.9 s / 813 MB). Float, not 16-bit — numbers in §9.2 |
| L-4 | CE-4366 | IN PROGRESS — the load fixed in one process (≈ 19.5 s warm); the cluster re-measure next, then the frame / garbage table | this commit | [Scale Rig §5](../../DESIGN_Scale_Rig.md): the per-tile bake (peak 7.0 → 1.46 GB, warm 47 → 16 s, cache keys unchanged), the cover build 52 → 0.4 s |
| L-5 | CE-4367 | DONE | this commit | [World Query Seam §7](../../DESIGN_World_Query_Seam.md): a 3 km eye-level trace **38.7 → 3.8 µs**, answers identical (rail, 12 000 lines) |
| L-6 | CE-4368 | WAITING — the coordinator's CE-4371 (input-set reader) is not on `origin/coordinator` yet ⇒ L-7 first (the handoff's swap) | | |
| L-7 | CE-4369 | — | | |
| L-8 | CE-4370 | DONE (design only) | `a71aa6c81` | UTM §8.1: sequence, module graph, P3-a…e |

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

## L-4 — measured so far

| stage (scale-1, 1 m heights, one process, Release) | time | memory |
|---|---|---|
| parse: 1 651 buildings → 84 607 prisms, 6 911 doors (+ the 127 MB height store) | 2.2 s | 0.32 GB |
| ⛔ the ground + building mesh (`TerrainWorldMesh.Build`: 4 vertices per 1 m cell) | 9.4 s | **61.4 M triangles, 2.2 GB; peak 7.0 GB** |
| (demo-city for scale: the mesh 0.1 s, the navmesh 6.7 s cold, 2 312 tiles) | | |

⇒ fixed in order of size — see Scale Rig §5.1 for the before / after table: the per-tile navmesh bake (no global soup), the Recast input only
on a cache miss, the tile key hashed once, the cover build through the spatial index.

### Other lanes' reds seen at the merge (not touched)

- `Fdp.Toolkits.Tests`: `S0IdTypesRailTests.S0_EveryNodeIdMember_IsANodeId` — 7 bare-int node ids in Orchestration placement / Replay Browser
  federation / search types; `FdpOrchestrationCqrsStructTests.TakeCheckpointIntent_HasRequestIdAndLabel` — 3 fields, the rail expects 2.
- ⚠ flaky in the full run, green alone 3/3: `MissionDirectorSystemTests.MissionDirector_DoesNotAdvance_WhenHealthAboveThreshold` (order-dependent).
- Fixed at the merge (stated in its commit): ui's `ManagerWindowTests` still called the removed `AddBreakpoint()` — the solution did not build.

---
_backend lane, 2026-11-09 … 2026-11-10_
