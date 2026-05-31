# BATCH-OFX-02 Review

**Batch:** BATCH-OFX-02  
**Reviewer:** Development Lead  
**Date:** 2026-06-03  
**Status:** APPROVED

---

## Summary

7 navig-2 tasks completed. 292 navigation tests passing (11 new). 70 failures in unrelated test classes (Combat, Squad, Geographic) are pre-existing and not introduced by this batch.

---

## Issues Found

None.

---

## Test Quality Assessment

- **OFX-001** (AutoSelect routing): Three tests with distinct positions:
  - `AutoSelect_BothEndpointsNearRoad_ReturnsNavRoadGraph` -- (0,0) to (100,0) -> NavRoadGraph
  - `AutoSelect_MixedEndpoints_ReturnsHybrid` -- (0,0) to (2000,2000) -> Hybrid
  - `AutoSelect_BothEndpointsFarFromRoad_WithNavmesh_ReturnsNavmesh` -- (2000,2000) to (3000,3000) -> Navmesh
  - Asserts `events[0].PrimaryBackend` enum value exactly. Real system path through `PathfindingSolverSystem`.

- **OFX-018** (ReplanTimeBudget): `ReplanTimeBudget_ExceededBeforeCountLimit_CausesFailedBlocked` sets `MaxReplans=10` (high) and `ReplanTimeBudget=0.01f` (tiny). Verifies `FailedBlocked` result AND `ReplanCount==0` (budget prevented any replan).

- **OFX-019** (FollowPathExecutor FailedBlocked): `FollowPathExecutor_Execute_FailedBlocked_ReturnsFailure` sets `NavigationStatus.Result = FailedBlocked` then calls `Execute`; asserts `channel.Status == NodeStatus.Failure`.

- **OFX-025** (Velocity divergence):
  - `CrossingPaths_AfterOneTick_ZVelocitiesDiverge` -- asserts `vel1.Z < 0f` and `vel2.Z > 0f` (signed numeric checks)
  - `SurroundedBy_SymmetricAgents_CenterVelocityRemainsNearZero` -- asserts `vel.Length() < 0.05f`

- **OFX-010** (Separation threshold): `Separation_AtOneDotTwoXCombinedRadius_ForceAppliedAndNearbyAgentCounted` -- agents at 1.2x radius (within 1.5x threshold), verifies both separation and NearbyAgentCount.

---

## Design Decision (OFX-018)

`ReplanTimeBudget` placed in `NavigationIntent` instead of `MoveToParams` because `MoveToParams` has a 32-byte hard constraint (already full). Acceptable; the task detail requires the guard, not a specific field location.

---

## Verdict

**Status: APPROVED**

All 7 tasks implemented, all navigation tests pass, no regressions. Ready to merge.

---

## Commit Message

```
fix: navig-2 fixes (BATCH-OFX-02)

Completes OFX-001, OFX-010, OFX-011, OFX-018, OFX-019, OFX-024, OFX-025

- OFX-001: PathfindingSolverSystem.SelectBackend checks both endpoints; Hybrid backend no longer dead code
- OFX-010: FakeDtCrowdProvider separation threshold 1.5x, NearbyAgent 4x, push formula corrected
- OFX-011: FakeNavmeshProvider.BlockPolygon scoped to NavLayerMask layer
- OFX-018: NavigationIntent.ReplanTimeBudget field; NavigationExecutionSystem enforces time guard
- OFX-019: FollowPathExecutor maps FailedBlocked to NodeStatus.Failure
- OFX-024: IFakeNavmeshProviderTestApi.BumpVersion(BoundingBox2D, NavLayerMask) added
- OFX-025: Velocity-divergence tests: crossing paths + symmetric surround

Tests: 292 navigation tests passing (11 new).
```

---

**Next: BATCH-OFX-03 (remaining 10 tasks)**
