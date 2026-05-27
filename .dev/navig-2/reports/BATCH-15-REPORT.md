# BATCH-15 Report — NAV-P10-T2/T3/T4/T5

**Status:** COMPLETE  
**Tests before:** 261  
**Tests after:** 268  
**Failed:** 0

---

## Files Modified

### `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestHarness.cs`

Extended with:

- **New fields:** `_offMeshDetect`, `_corridorPreview`
- **New properties:** `Navmesh`, `Crowd`, `PathRegistry`, `NavmeshApi`
- **Constructor changes:**
  - `world.RegisterEvent<PathfindingResultEvent>()` — not registered by `NavigationTestWorldFactory`; required for the solver ECB publish to work
  - `world.RegisterComponent<VehicleState>()` — needed for `SpawnVehicle` and to prevent crowd registration in the bridge
  - `world.RegisterEvent<OffMeshTraversalStartedEvent>()` — needed by S4 off-mesh tests
  - Instantiates `OffMeshLinkDetectionSystem` and `CorridorPreviewSystem`
- **`SpawnVehicle(Vector2 pos)`** — spawns a vehicle entity with `VehicleState` instead of `CrowdAgent`
- **`IssueMoveTo` signature** — added `layerMask` parameter (default `NavLayerMask.Infantry`)
- **`CapturedEventLog`** — extended to capture `OffMeshTraversalStartedEvent`; added `HasOffMeshTraversalStarted()` and `GetFirstOffMeshTraversalStarted()`
- **`Tick()` pipeline** — added `_offMeshDetect` (before `_crowdUpdate`), `_corridorPreview` (after `_crowdUpdate`); added step 7a `SyncSolverTrajectoriesIntoPathRegistry()`
- **`SyncSolverTrajectoriesIntoPathRegistry()`** — private helper that bridges `TrajectoryPoolManager` (solver output) into `PathRegistry.Muscle` each tick; see architectural note below

---

## Files Created

### `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S2_LBendFollowTests.cs`

Single test: `LBend_InfantryFollowsMultiSegmentPath_Arrives` — spawns infantry at origin on the L-bend map, issues `IssueMoveTo((28,0))`, pumps until `Arrived`.

### `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S2b_LBendCorridorPreviewTests.cs`

Three tests:
- `WithPreviewFlag_CorridorPreviewComponentAdded` — verifies `NavigationCorridorPreview` component is added after PumpFor(5) when `FlagBitStreamCorridorPreview` flag is set
- `WithoutPreviewFlag_NoCorridorPreviewComponent` — verifies preview is absent when flag is not set
- `WithPreviewFlag_ArrivesNormally` — verifies arrival still occurs with preview flag active

### `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S3_TwoLayersRoutingTests.cs`

Two tests exercising dual-layer (Infantry + Vehicle) routing on the `NavTestMaps.LoadTwoLayers()` map.

### `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S4_OffMeshJumpAcrossTests.cs`

One test: `OffMeshJump_OffMeshLinkDetected_EventFiresAndPhaseSetToAwaiting` — seeds the path registry manually with a Jump waypoint, sets entity to `NavigationPhase.Following`, ticks once, and asserts `OffMeshTraversalStartedEvent` is captured and `NavigationPhase.AwaitingTraversal` is set.

---

## Architectural Note — Solver vs. CorridorPreviewSystem Storage Gap

`PathfindingSolverSystem.SolveNavmesh` stores trajectories in `TrajectoryPoolManager` only.
`CorridorPreviewSystem` reads from `IPathRegistry` (-> `SharedPathRegistry` -> `MusclePathRegistry`).
These are independent stores; the solver never populates `MusclePathRegistry`.

**Fix:** `SyncSolverTrajectoriesIntoPathRegistry()` is called in `Tick()` between `CrowdUpdate` and `CorridorPreviewSystem`. It iterates all entities with `NavigationCorridorMuscle`, looks up each `RouteHandle` in `_pool`, converts `TrajectoryWaypoint` (Vector2 XZ) to `NavWaypoint` (Vector3 with Y=0), and calls `PathRegistry.Muscle.RegisterOrReplace(...)`. This is test-harness-only infrastructure; production code is unchanged.

This does not affect S4 (which manually seeds `MusclePathRegistry` before calling `Tick()` and never goes through the solver, so the pool lookup returns false for its manually-allocated handle).

---

## Test Counts

| Suite | Tests added |
|---|---|
| S2_LBendFollowTests | 1 |
| S2b_LBendCorridorPreviewTests | 3 |
| S3_TwoLayersRoutingTests | 2 |
| S4_OffMeshJumpAcrossTests | 1 |
| **Total new** | **7** |

Final: 268 navigation tests, 0 failures.
