# BATCH-02 Review

**Batch:** BATCH-02
**Reviewer:** Dev Lead
**Date:** 2025-07-24
**Decision:** ✅ APPROVED

---

## Summary

BATCH-02 completes Phase 0 (Foundations). All three work items were delivered:
the P1 corrective (test rename + assertion fix), NAV-P0-T4 (action command layer),
and NAV-P0-T5 (NavWaypoint, enums, corridor components, ComponentIds 69-73).

Build: **0 errors**. Nav unit tests: **79/79**. DDS translator tests: **7/7**.

---

## Per-Task Assessment

### Corrective T0 — NoneIntent fix
- ✅ Test renamed to `NoneIntent_HaltsNavigation_NavStateSetToNone`
- ✅ Assertions corrected: `KinematicsMode.None`, `TargetSpeed = 0`
- Comment updated. Clean.

### NAV-P0-T4 — Action Command Layer
- ✅ ActionIds 6-9 added to `NavigationConstants.cs`; `ActionIdFollowRoadGraph` marked `[Obsolete]`
- ✅ `MoveToParams` extended with `RouteHandle`, `LayerMask`, `BackendForce`; ≤32 bytes confirmed by test
- ✅ `PlanRouteParams`, `FollowPathParams`, `FetchPathDetailsParams`, `ReleasePathParams` — all 32 bytes, verified by layout tests
- ✅ `NavigationIntent.RouteHandle` added; `NavigationStatus` extended with Phase/LastFailureReason/ReplanCount/RouteHandle/EstimatedTimeRemaining/NavmeshVersionObserved
- ✅ `NavigationPhase` enum added alongside the other nav enums
- ✅ `NavigationResult` extended to include PathFound=4 .. FailedInvalidHandle=7
- ✅ DDS `SimDescriptors.cs` updated for both `NavigationIntent` and `NavigationStatus`
- ✅ `NavigationIntentEgressTranslator` — `RouteHandle` forwarded; Mode=None guard prevents spurious publish (also fixed 2 pre-existing translator-test failures)
- ✅ `NavigationStatusEgressTranslator` and `NavigationStatusIngressTranslator` updated; change-detection key extended to include `Phase`
- ✅ `MoveToExecutor` updated: copies `RouteHandle`; handles 3 new failure result cases
- ✅ 8+ tests covering struct sizes, result value non-collision, translator round-trip

**Noted improvement (non-blocking):** `NavigationStatusEgressTranslator` does a full entity scan per tick. A delta-query approach would be more efficient at scale. Logged as P3 debt below.

### NAV-P0-T5 — NavWaypoint, Enums, Components, ComponentIds
- ✅ `NavWaypoint` — 24 bytes (Vector3 12 + TraversalKind 1 + SurfaceType 1 + 2 pad + float 4 + float 4)
- ✅ `TraversalKind` enum (Walk/Jump/Climb/Door/Fly) and `SurfaceType` enum added
- ✅ **DSC-4 (noted by developer):** `SurfaceType.Generic` replaces `SurfaceType.Default` to avoid IDL reserved-keyword collision with `default`. Decision accepted — the name is semantically appropriate and avoids a real code-gen issue.
- ✅ `NavAgentProfile` (ComponentId=69), `NavigationCorridorMuscle` (70), `NavigationCorridorPreview` (71), `NavigationPathDetailsBuffer` (72), `CrowdAgent` (73)
- ✅ `NavigationCorridorPreview` — 144 bytes (16 header + 8×16 PreviewWaypoint), verified by test
- ✅ `PreviewWaypoint` — 16 bytes, verified by test
- ✅ `NavigationContractsComponentIds` — 69-73 allocated; range comment added
- ✅ 6+ tests: sizes, ComponentId uniqueness (reflection-based), range 69-79, default-value contracts

---

## Deviations from Instructions

| # | Deviation | Classification | Disposition |
|---|-----------|----------------|-------------|
| D1 | `SurfaceType.Generic` instead of `SurfaceType.Default` | Architecture fix (IDL reserved keyword) | **Accepted** — registered as DSC-4 below |
| D2 | Pre-existing translator test failures fixed proactively | Scope expansion (no harm) | **Accepted** — improvements, not regressions |

---

## Issues Found in Review

### P1 — None (all blockers resolved by developer)

### P3 Debt Items (non-blocking)
- `NavigationStatusEgressTranslator` full-scan per tick — should be delta-query for large entity counts. Log to DEBT-TRACKER.

---

## Design Clarifications Surfaced (DSC)

**DSC-4** (surfaced during T5):
- **Issue:** `SurfaceType.Default = 0` collides with CycloneDDS IDL code-gen namespace; also, `default` is an IDL reserved keyword.
- **Decision:** Rename zero value to `SurfaceType.Generic`. Semantically equivalent; avoids IDL namespace collision. No downstream impact in this batch.
- **Status:** Resolved. Document in TASK-DETAILS.md as an accepted constraint.

---

## Test Health Summary

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| Navigation (Fdp.Toolkits.Tests --filter Navigation) | 64 (63 pass) | 79 (79 pass) | +15 tests, -1 failure |
| NavigationIntent translator (Hrot.Map.Common.Tests) | 7 (5 pass) | 7 (7 pass) | +0 tests, -2 failures (pre-existing) |

---

## Phase 0 Completion Status

| Task | Status |
|------|--------|
| NAV-P0-T1: Assembly placement policy | ✅ DONE (BATCH-01) |
| NAV-P0-T2: KinematicsMode enum extension | ✅ DONE (BATCH-01) |
| NAV-P0-T3: INavmeshProvider 3D, EQS migration | ✅ DONE (BATCH-01) |
| NAV-P0-T4: Action command layer | ✅ DONE (BATCH-02) |
| NAV-P0-T5: NavWaypoint, components, ComponentIds | ✅ DONE (BATCH-02) |

**Phase 0 is complete.** BATCH-03 will begin Phase 1 (PathfindingRequestEvent, IPathRegistry, NavmeshLayerRegistry).

---

## Pre-existing Failures (unrelated, unchanged)

55 failures in test suites unrelated to Navigation subsystem work (GizmoSettings, IdAllocation, SimTransformBridge, ReplayModule, RecordingExportService). These predate this workstream and are excluded from batch scope.

---

## Commit Message

```
feat(nav-p0): BATCH-02 — action layer, NavWaypoint, components, ComponentIds 69-73 [NAV-P0-T4, NAV-P0-T5]

- Fix: NoneIntent_HaltsNavigation_NavStateSetToNone (corrective T0)
- Add: ActionIds 6-9 (PlanRoute/FollowPath/FetchPathDetails/ReleasePath)
- Add: 32B param structs (PlanRouteParams, FollowPathParams, FetchPathDetailsParams, ReleasePathParams)
- Extend: MoveToParams with RouteHandle/LayerMask/BackendForce (≤32B)
- Extend: NavigationIntent.RouteHandle; NavigationStatus Phase/LastFailureReason/ReplanCount/RouteHandle/ETR/NavmeshVersionObserved
- Add: NavigationPhase enum; NavigationResult values 4-7
- Update: DDS descriptors (SimDescriptors.cs) to include new fields
- Fix: NavigationIntentEgressTranslator Mode=None guard (fixed 2 pre-existing test failures)
- Update: NavigationStatusEgressTranslator/IngressTranslator for new fields
- Add: NavWaypoint 24B (full, replaces BATCH-01 stub)
- Add: TraversalKind, SurfaceType enums (DSC-4: SurfaceType.Generic replaces .Default)
- Add: NavAgentProfile (69), NavigationCorridorMuscle (70), NavigationCorridorPreview (71), NavigationPathDetailsBuffer (72), CrowdAgent (73)
- Tests: 79/79 nav tests pass; 7/7 translator tests pass; 0 build errors
```
