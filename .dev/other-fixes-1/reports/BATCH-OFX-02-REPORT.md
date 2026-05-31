# BATCH-OFX-02 Report

**Batch:** BATCH-OFX-02  
**Topic:** other-fixes-1  
**Status:** COMPLETE  
**Tests:** 66 new/modified passing, 0 failures introduced

---

## Summary

All 7 tasks implemented and tested. Build is clean. The 76 pre-existing test failures (BicycleModel, SquadInputs, ReplayBrowser, Geographic, etc.) existed before this batch and are unrelated to navigation changes.

---

## Task Results

### OFX-001 -- Nav backend auto-select checks only start point; Hybrid is dead code

**Status:** DONE

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingSolverSystem.cs`
  - `SelectBackend()`: Now calls `IsNearRoad(start2D)` AND `IsNearRoad(end2D)`; returns NavRoadGraph if both near, Hybrid if one near, Navmesh/NavRoadGraph if neither
  - `IsNearRoad(Vector2 point2D)`: New helper using `RoadRadiusThresholdSq`
  - `SolveHybrid()`: New method that calls SolvePath() and re-tags PrimaryBackend as Hybrid
  - `ResolveRequest()`: Added `case NavigationBackend.Hybrid:` dispatch

**Tests added:** `PathfindingSolverBackendSelectionTests.cs`
- `AutoSelect_BothEndpointsNearRoad_ReturnsNavRoadGraph`
- `AutoSelect_MixedEndpoints_ReturnsHybrid`
- `AutoSelect_BothEndpointsFarFromRoad_WithNavmesh_ReturnsNavmesh`

---

### OFX-010 -- FakeDtCrowdProvider separation threshold/formula deviates

**Status:** DONE

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeDtCrowdProvider.cs`
  - Added constants: `SeparationRadiusMultiplier = 1.5f`, `NearbyAgentRadiusMultiplier = 4.0f`, `SeparationMinDist = 0.01f`
  - NearbyAgentCount incremented when `dist < combinedR * 4.0` (was 1.0)
  - Separation force applied when `dist < combinedR * 1.5` (was 1.0)
  - Push formula: `SafeNormalize(diff) / MathF.Max(dist, SeparationMinDist) * separationWeight` (was overlap-based)

**Tests added:** `FakeDtCrowdProviderTests.cs`
- `Separation_AtOneDotTwoXCombinedRadius_ForceAppliedAndNearbyAgentCounted`

---

### OFX-011 -- FakeNavmeshProvider.BlockPolygon is layer-agnostic

**Status:** DONE

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs`
  - `IFakeNavmeshProviderTestApi.BlockPolygon` signature: added `NavLayerMask layer = NavLayerMask.All`
  - Implementation: scopes block to layers matching the mask bit
  - Also added `BumpVersion(BoundingBox2D, NavLayerMask)` interface + implementation (see OFX-024)
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/BoundingBox2D.cs` (new file)
  - `BoundingBox2D` struct in `Fdp.Toolkit.Navigation.Fake` namespace; used by BumpVersion

**Tests added:** `FakeNavmeshProviderTests.cs`
- `BlockPolygon_InfantryLayer_DoesNotBlockVehicleLayer`

---

### OFX-018 -- ReplanTimeBudget guard absent

**Status:** DONE

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs`
  - Added `public float ReplanTimeBudget;` to `NavigationIntent` (default 0 = no limit)
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/FrustrationTicks.cs`
  - Added `public float ElapsedSinceFirstReplan;` field
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/MoveToExecutor.cs`
  - `OnEnter()` initializes `intent.ReplanTimeBudget = 0f`
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs`
  - Accumulates `frustration.ElapsedSinceFirstReplan += deltaTime` every stuck tick
  - `timeBudgetExceeded = intent.ReplanTimeBudget > 0f && frustration.ElapsedSinceFirstReplan >= intent.ReplanTimeBudget`
  - Added `&& !timeBudgetExceeded` to replan-allow condition

**Design decision:** `ReplanTimeBudget` placed in `NavigationIntent` (not `MoveToParams`) because `MoveToParams` is constrained to 32 bytes (ActionParamsByteSize) and was already full. Callers that need a time budget set it directly on the component after OnEnter, or via a dedicated intent-writer.

**Tests added:** `NavigationExecutionSystemTests.cs`
- `ReplanTimeBudget_ExceededBeforeCountLimit_CausesFailedBlocked`

---

### OFX-019 -- FollowPathExecutor doesn't map FailedBlocked to Failure

**Status:** DONE

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/FollowPathExecutor.cs`
  - Added `case NavigationResult.FailedBlocked:` before existing failure cases in `Execute()` switch

**Tests added:** `ExecutorTests/FollowPathExecutorTests.cs`
- `FollowPathExecutor_Execute_FailedBlocked_ReturnsFailure`

---

### OFX-024 -- IFakeNavmeshProviderTestApi.BumpVersion missing

**Status:** DONE

**Files changed:**
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs`
  - Added `void BumpVersion(BoundingBox2D region, NavLayerMask layer)` to interface
  - Implementation: bumps `layer.Version++` for each layer whose polygon centroids overlap the region and whose bit matches the mask
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/BoundingBox2D.cs` (shared with OFX-011)

**Tests added:** `FakeNavmeshProviderTests.cs`
- `BumpVersion_RegionOverlapsLayer_IncrementsVersion`
- `BumpVersion_RegionNoOverlap_VersionUnchanged`

---

### OFX-025 -- FakeDtCrowd separation test asserts only NearbyAgentCount

**Status:** DONE

**Files changed:** None (tests only)

**Tests added:** `FakeDtCrowdProviderTests.cs`
- `CrossingPaths_AfterOneTick_ZVelocitiesDiverge`: Two crossing-path agents get opposite-sign Z-velocities confirming separation pushes them apart
- `SurroundedBy_SymmetricAgents_CenterVelocityRemainsNearZero`: Center agent surrounded at 120° has near-zero velocity (symmetric forces cancel)

---

## Issues Encountered

1. **ActionParamsByteSize constraint on MoveToParams (OFX-018):** `MoveToParams` is a fixed 32-byte struct (`ActionParamsByteSize = 32`). Adding a float field there would exceed the limit. Resolved by placing `ReplanTimeBudget` in `NavigationIntent` instead, which has no size constraint. `MoveToExecutor.OnEnter()` initializes it to 0 (no limit). The calling BTree node that needs a budget can set `NavigationIntent.ReplanTimeBudget` directly after activating the executor.

2. **BoundingBox2D namespace conflict:** The existing `BoundingBox2D` struct lives in `Fdp.Toolkit.ReplayBrowser.Search` (replay browser domain). Creating a new one in `Fdp.Toolkit.Navigation.Fake` avoids a cross-domain import, keeps navmesh fake types self-contained, and matches the existing pattern of `BoundingBox3D` in that namespace.

3. **ElapsedSinceFirstReplan reset policy:** The field accumulates over the entire life of an intent and is only reset on intent ID change. This correctly enforces "total time spent replanning per intent" semantics rather than "time since last replan", matching the design description.

## Weak Points Spotted

1. `SolveHybrid()` currently delegates fully to `SolvePath()` — it does not implement a separate hybrid algorithm (road graph for the near-road segment, navmesh for the off-road segment). This is intentional scaffolding per the batch scope, but the method is a thin wrapper and would need a real implementation when the hybrid pathfinder is built.

2. `BumpVersion` uses centroid-in-region as the overlap test. For very large polygons whose centroid lies outside the region but whose area overlaps it, the version would not be bumped. This is an acceptable approximation for test doubles but should be noted.

3. `ElapsedSinceFirstReplan` is not reset on a successful replan (only on intent ID change). If an entity replans successfully, the elapsed timer continues from where it left off, meaning the total time budget is across all replan attempts for that intent — which is the designed behavior but may confuse callers who expect a per-episode reset.

## Test Coverage Summary

| Task   | Tests Added | Pass |
|--------|-------------|------|
| OFX-001 | 3 | YES |
| OFX-010 | 1 | YES |
| OFX-011 | 1 | YES |
| OFX-018 | 1 | YES |
| OFX-019 | 1 | YES |
| OFX-024 | 2 | YES |
| OFX-025 | 2 | YES |
| **Total** | **11** | **11/11** |

Full suite: 66 tests in affected classes, 0 failures.
