# BATCH-03 Review

**Batch:** BATCH-03
**Reviewer:** Dev Lead
**Date:** 2025-07-25
**Decision:** APPROVED

---

## Summary

BATCH-03 completes Phase 1 (Muscle Solver Path-Query Pipeline). All four tasks were
delivered: PathfindingRequestEvent/ResultEvent extension (T1), NavigationIntentBridgeSystem
action routing (T2), multi-modal backend selection in PathfindingSolverSystem (T3), and
PathfindingResultMaterializationSystem corridor + status writes with capacity resize (T4).

Build: **0 errors, 0 warnings**. Navigation tests: **96/96**. Translator tests: **44/44**.

---

## Per-Task Assessment

### T1 — PathfindingRequestEvent / PathfindingResultEvent Extension
- PASS `PathfindingRequestEvent` has all 5 new fields (`RouteHandle`, `NavLayerMask`,
  `BackendForce`, `MaxCost`, `NavmeshVersionAtRequest`).
- PASS `PathfindingResultEvent` has all 4 new fields (`RouteHandle`, `NavmeshVersionAtPlan`,
  `FailureReason`, `PrimaryBackend`).
- PASS `NavigationBackend` enum: Auto=0, NavRoadGraph=1, Navmesh=2, Hybrid=3, Volumetric=4.
- PASS `NavigationFailureReason` enum: NoFailure=0, Unreachable=1, Timeout=2, InvalidHandle=3, ProviderError=4.
  Note: The developer used `NoFailure` instead of `None` to avoid IDL namespace collision — accepted, sensible choice.
- PASS `MoveStartedEvent` (EventId 2034) defined in `PathfindingEvents.cs`.
- PASS `MobilityProfile.Naval=3` and `MobilityProfile.Flying=4` added.
- PASS DDS translators updated for structural forward-compat.
- PASS Tests: 4 tests covering zero-defaults, enum values.
  Minor note: tests assert enum values by integral cast — robust, will catch accidental renumbering.

### T2 — NavigationIntentBridgeSystem Action Routing
- PASS `MoveTo` publishes exactly one `PathfindingRequestEvent` with correct destination.
- PASS `PlanRoute` publishes request carrying the Brain-allocated `RouteHandle`.
- PASS `FollowPath` with unknown handle immediately sets `NavigationResult.FailedInvalidHandle`.
- PASS Idempotency: same `ActionInstanceId` on second tick → no new request.
- PASS 4 tests in `NavigationIntentBridgePipelineTests`; assertions verify exact event counts
  and field values (not just "event exists").
- Note: `FetchPathDetails` and `ReleasePath` are stubbed; documented in report as coverage gap.
  Deferred correctly to NAV-P4-T3/NAV-P3 as specified.

### T3 — Multi-Modal Backend Selection in PathfindingSolverSystem
- PASS `BackendForce.NavRoadGraph` → road-graph Dijkstra, `PrimaryBackend == NavRoadGraph`.
- PASS `MobilityProfile.Flying` → `IVolumetricPathProvider.PlanPath` called; `INavmeshProvider`
  NOT called (verified via `StubVolumetricProvider.WasCalled` and `StubNavmeshProvider.PlanPathWasCalled`).
- PASS Anonymous handle (RouteHandle=0) → result handle `>= MuscleHandleBase (0x40000000)`.
- PASS Brain-allocated handle (RouteHandle=99) → echoed as 99.
- PASS `NavigationHandleAllocator` implemented as `Interlocked.Increment` from `0x3FFFFFFF`
  base counter. Thread-safe. First call returns exactly `0x40000000`. ✓
- PASS `Moq` limitation for `Span<T>` parameters handled correctly with concrete stub classes.
- Note: Hybrid backend falls back to RoadGraph when navmesh null — acceptable per spec.
  Documented as gap pending Phase 2 fake providers.

### T4 — PathfindingResultMaterializationSystem + Corridor + Capacity
- PASS Entity lookup from high-32 bits of `RequestId`.
- PASS `LocomotionChannel.ActiveAction` branching.
- PASS `MoveTo` reachable: `NavigationCorridorMuscle` written, `MoveStartedEvent` fired once,
  `NavigationStatus.Phase == Following`.
- PASS `MoveTo` unreachable: corridor NOT written, no `MoveStartedEvent`, `Result == FailedUnreachable`.
- PASS `PlanRoute` reachable: `Phase == Idle`, `Result == PathFound`, `RouteHandle != 0`, no corridor, no event.
- PASS `PlanRoute` unreachable: `Result == NoPath`.
- PASS `PathfindingBatchData.DefaultCapacity == 256`.
- PASS 5 tests with full assertion chains (verifying presence/absence of corridor + event + status fields).

---

## Deviations from Instructions

| # | Deviation | Classification | Disposition |
|---|-----------|----------------|-------------|
| D1 | `NavigationFailureReason.NoFailure` instead of `None` | IDL keyword avoidance | **Accepted** — avoids IDL namespace collision |
| D2 | `NavigationCorridorMuscle.TotalSegmentCount = 1` for Phase 1 | Incomplete data (one-segment road-graph path) | **Accepted** — semantically correct; multi-segment deferred to Phase 2/3 |
| D3 | `NavigationCorridorMuscle.Flags = 0` | Flag semantics deferred | **Accepted** — Phase 2 defines traversal state machine bits |

---

## Issues Found in Review

### P1 — None

### P2 Debt Items
- `NavigationTestWorldFactory` does not register `NavigationCorridorMuscle`. Each test class
  that needs it must register manually. Should be fixed proactively before Phase 9 system tests
  accumulate more registration overhead.

### P3 Debt Items (non-blocking)
- Ring-buffer slot aliasing in `PathfindingBatchData.Results` (same entity, two requests within
  capacity window collide silently). No diagnostic for collision. Log to DEBT-TRACKER.
- §15 budget bands (Critical/Normal/Low) not yet applied — solver processes events in arrival
  order. Acceptable per spec (ring-buffer oldest-evict explicitly OK), but the three tiers are
  documented without a priority sort. Log to DEBT-TRACKER.

---

## Test Health Summary

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| Navigation (`Fdp.Toolkits.Tests --filter Navigation`) | 79 | 96 | +17 tests |
| Hrot.Map.Common.Tests (translators) | 44 | 44 | +0 tests, transient fail on 1st run resolved |

---

## Phase 1 Completion Status

| Task | Status |
|------|--------|
| NAV-P1-T1: PathfindingRequestEvent/ResultEvent extension | DONE (BATCH-03) |
| NAV-P1-T2: NavigationIntentBridgeSystem action routing | DONE (BATCH-03) |
| NAV-P1-T3: Multi-modal backend selection | DONE (BATCH-03) |
| NAV-P1-T4: Response materialization + corridor + capacity | DONE (BATCH-03) |

**Phase 1 is complete.** BATCH-04 will begin Phase 2 (Fake backends: DD-Fake-Nav).

---

## Suggested Git Commit Message

```
feat(nav-p1): BATCH-03 -- Muscle solver path-query pipeline [NAV-P1-T1..T4]

- Extend PathfindingRequestEvent with RouteHandle/NavLayerMask/BackendForce/MaxCost/NavmeshVersionAtRequest
- Extend PathfindingResultEvent with RouteHandle/NavmeshVersionAtPlan/FailureReason/PrimaryBackend
- Add NavigationBackend and NavigationFailureReason enums; MobilityProfile Naval=3/Flying=4
- Add MoveStartedEvent (EventId 2034)
- NavigationIntentBridgeSystem: MoveTo/PlanRoute -> solver; FollowPath invalid-handle guard; idempotency
- PathfindingSolverSystem: BackendForce override, Flying->Volumetric, Auto heuristic, NavigationHandleAllocator
- PathfindingResultMaterializationSystem: corridor write, status branch (MoveTo/PlanRoute/unreachable), MoveStartedEvent
- PathfindingBatchData.DefaultCapacity: 64->256
- DDS translators updated for structural forward-compat
- Tests: 96/96 nav tests pass (was 79); 44/44 translator tests pass
```
