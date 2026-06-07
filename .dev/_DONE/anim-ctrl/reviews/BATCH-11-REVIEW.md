# BATCH-11 REVIEW — Phase 7 Foundation

**Review Date:** 2026-05-27  
**Batch ID:** BATCH-11  
**Phase:** 7 (Integration tests, networkless stage-1)  
**Tasks Reviewed:** ANC-P7-01, ANC-P7-02, ANC-P7-03, ANC-P7-04  
**Verdict:** ✅ **APPROVED** (high-quality implementation, test suite is rigorous and well-aligned with DESIGN)

---

## Executive Summary

BATCH-11 delivers the foundation for Phase 7 integration testing: shared infrastructure (`PumpUntil`/`IPumpableHarness`), finalized test helpers, integration fixture, and the first end-to-end scenario. All 4 tasks complete. Test suite demonstrates deep understanding of the animation pipeline and proper coverage of critical integration points.

**Key Strength:** Test assertions are rigorous and exercise the full pipeline (dispatcher → executors → backend → reporter), not just checking compilation or string existence.

**Key Finding:** Developer fixed a critical infrastructure gap: `IssuePlayMontage` was missing the `PlayMontageParams` blob write (deferred from BATCH-09), which would have caused all integration tests relying on it to pass vacuously (montage would "complete" instantly without actually playing).

---

## Test Quality Assessment

### Test Coverage

**New Tests (4):**
1. `Fixture_BootstrapsAndTicksWithoutError` — Smoke test; verifies fixture basic hygiene
2. `SpawnHumanoid_EntityHasRequiredComponents` — Component setup verification; checks all required components exist and initial status
3. `FirstBridgeTick_RegistersEntityWithBackend` — Backend registration lifecycle; verifies handle transition from raw `ClassId` to `(generation << 32) | index`
4. `PlayMontage_RunsToCompletionAndReportsSuccess` — Scenario 1 happy-path; exercises full pipeline

### Test Alignment with DESIGN

**DD-Tests §6 S1 (Happy-Path):**
- ✅ Spawn entity
- ✅ Tick once to register with backend (2-tick startup pattern)
- ✅ Issue `PlayMontage("Walk", slot 0)`
- ✅ `PumpUntil` channel status reaches `Success`
- ✅ Assert channel status
- ✅ Assert `MontageEndedEvent` published with correct fields (`EndReason`, `MontageId`, `Target`, `QueueIndex`)

**Assertion Quality:**
- ✅ Checks actual behavior (status transitions), not just compilation
- ✅ Verifies event payload fields (not just event count)
- ✅ Validates event timing (event readable after pump, not before)
- ✅ Checks montage completion within frame budget (0.5s montage → ~30 frames, budget 100)
- ✅ Uses diagnostic dump in `PumpUntil` for clarity on timeout

**NOT Observed:** Fake/oversimplified tests. All assertions exercise meaningful code paths in the animation subsystem.

### Infrastructure Quality

**`AnimationIntegrationFixture` (ANC-P7-03):**
- Correctly instantiates `EntityRepository`, `FakeAnimationBackend`, `BakedAnimationCache`
- Registers all 10 component types + 2 event types per DD-1
- Creates all 8 systems in correct execution order (DD-1 §17)
- `PumpFrame()` runs Simulation (5 systems) → PostSimulation (3 systems) → `SwapBuffers()`
- `SpawnHumanoid()` properly initializes all animation components
- `ResetWorld()` destroys entities and double-drains event bus (clears both buffers)
- `PumpUntil` correctly leverages `IPumpableHarness` for deterministic frame-stepping

**`TestData.CreateCharacterDef()` (ANC-P7-03):**
- Baked with `ClassId = 100L` (distinct from Phase3 tests)
- Two montages: "Walk" (0.5s), "Run" (0.4s) — sufficient for stage-1 scenarios
- Two stances: Standing, Crouched
- Footstep markers present (for future Scenario 7)
- Inline baking via `BakingUtils.BakeDef` (public method, works correctly)

**`AnimationTestHelpers` improvements (ANC-P7-02):**
- `IssuePlayMontage` now writes full `PlayMontageParams` blob (was deferred in BATCH-09)
- Without this fix, play intent staging would have `MontageId = 0`, slot would never activate, channel would reach `Success` immediately (vacuous pass)
- `DumpAnimationDiagnostics` produces human-readable state snapshots for debugging
- `WriteParams` correctly validates overflow (32-byte max)

---

## Verification Testing

**Build Status:**
```
Build succeeded. 0 Error(s), 1 Warning (pre-existing CS8500 unrelated to BATCH-11)
```

**Test Execution:**
```
Hrot.Animation.Integration.Tests:     26/27 passed, 1 skipped, 0 failed (~1.66s)
Hrot.MuscleCharacter.Animation.Tests: 169/169 passed, 0 failed (~201ms) — no regressions
```

**All assertions pass; no flakes or timeouts observed.**

---

## Design Alignment Notes

### Event Timing (Developer Insight — Important)

The developer correctly identified a subtle timing detail not fully spelled out in DD-Tests §6:

- `AnimationStateReporterSystem` publishes `MontageEndedEvent` during PostSimulation
- `SwapBuffers()` promotes it to the read buffer
- Test checks `Bus.Read<MontageEndedEvent>()` in same tick
- **Key:** `PumpUntil` returns BEFORE calling another `PumpFrame()`, so event remains readable in the current read buffer

This is correct behavior and the implementation handles it properly. The design doc glosses over this timing window — good catch by the developer, and the implementation is sound.

### Backend Handle Lifecycle (2-Tick Startup Pattern)

Developer notes:
- **Tick 1:** `AnimationRuntimeBridgeSystem` registers entity, replaces `BackendHandle` from raw `ClassId` to `(generation << 32) | index`
- **After Tick 1:** `IssuePlayMontage` must be called (writes params, bumps `ActionInstanceId`)
- **Tick 2:** Dispatcher reads params, stages play, bridge applies via `backend.PlayMontageOnSlot`

The test correctly follows this pattern (spawn → pump once → issue command → pump until success). This is a critical synchronization point that's now well-documented in the test code.

---

## Developer Insights & Weak Points Identified

The developer identified 5 items for future work (all P3 or acceptable P2 deferral):

### 1. **`AnimationRuntimeBridgeSystem._entityClassIds` unbounded growth on reset** (P3)
   - **Issue:** `ResetWorld()` destroys entities but doesn't clean the internal `_entityClassIds` dictionary
   - **Impact:** Over many test runs, dictionary grows unboundedly (minor memory leak in test infrastructure)
   - **Mitigation:** `FakeAnimationBackend` is recreated per fixture instance (GC reclaims it)
   - **Recommendation:** Expose `FlushEntityState(Entity)` or reset method for future phases
   - **Action:** Record in DEBT-TRACKER as D-19 (P3)

### 2. **`SteppingTimeController.Time` property not stepping correctly** (P3)
   - **Issue:** Fixture never calls `Time.Step(dt)` in `PumpFrame`
   - **Impact:** `Time` property is inaccurate; not used by current scenarios
   - **Mitigation:** Intentional for simplicity; sufficient for deterministic frame tests
   - **Recommendation:** Step `Time` if future scenarios need time-correlated assertions
   - **Action:** Acceptable deferral; note for Phase 7 Scenario design

### 3. **Missing `InternalsVisibleTo` in `Hrot.MuscleCharacter.Animation`** (P3)
   - **Issue:** `BakingUtils.BakeForTest` internal method not accessible from integration tests
   - **Impact:** Workaround: use public `BakeDef` instead (which works fine)
   - **Mitigation:** None needed; `BakeDef` is public
   - **Recommendation:** Add `InternalsVisibleTo` attribute if future tests need semantic marker
   - **Action:** Record as D-20 (P3 enhancement)

### 4. **`AnimationBackendCleanupSystem` is a no-op** (P2, Known Deferral)
   - **Issue:** System does not yet clean up backend resources on entity destruction (pending `PendingDestroy` component)
   - **Impact:** Destroyed entities leave orphaned entries in backend (safe for unit tests via GC)
   - **Mitigation:** `ResetWorld()` relies on GC of `FakeAnimationBackend` instance
   - **Recommendation:** Implement when `PendingDestroy` is available in Muscle ECS layer
   - **Action:** Already in DEBT-TRACKER as D-11 (resolved at BATCH-05); mark as still pending implementation

### 5. **`IssuePlayMontage` assertion for non-zero `MontageId`** (P2, Future Safety)
   - **Issue:** Helper could be called with zero `MontageId` without immediate failure; tests would pass vacuously
   - **Impact:** Integration test could mask bugs in montage dispatch
   - **Mitigation:** BATCH-11 fixed the params blob write; params are now fully written
   - **Recommendation:** Add debug assertion in `PlayMontageExecutor.OnEnter` to catch zero `MontageId`
   - **Action:** Record as D-21 (P2 safety enhancement for Phase 7+)

---

## Debt Tracker Updates

**New entries (to be added in next commit):**

| ID | Priority | Source | Description | Target Batch |
|----|----------|--------|-------------|--------------|
| D-19 | P3 | BATCH-11 REVIEW (Infrastructure) | `AnimationRuntimeBridgeSystem._entityClassIds` unbounded growth on `ResetWorld()`. Recommend exposing `FlushEntityState(Entity)` method or resetting system state. Acceptable for now (GC mitigates). | Phase 7 infrastructure review |
| D-20 | P3 | BATCH-11 REVIEW (Enhancement) | Add `InternalsVisibleTo("Hrot.Animation.Integration.Tests")` to `Hrot.MuscleCharacter.Animation` project file for semantic clarity (not blocking; `BakeDef` is public). | Post-Phase 7 |
| D-21 | P2 | BATCH-11 REVIEW (Safety) | Add debug assertion in `PlayMontageExecutor.OnEnter` to catch zero `MontageId` in params blob. Prevents vacuous test passes. Implement with next integration scenario. | BATCH-12 (Phase 7 Scenario 2+) |

---

## Files Changed

| File | Type | Notes |
|------|------|-------|
| `Harness/AnimationTestHelpers.cs` | Modified | `IssuePlayMontage` now writes full `PlayMontageParams` blob (critical fix) |
| `Data/TestData.cs` | New | Character definition baking for all 8 scenarios (`ClassId=100`, 2 montages, 2 stances) |
| `AnimationIntegrationFixture.cs` | New | Shared test fixture implementing `IPumpableHarness` + 8-system orchestration |
| `AnimationIntegrationScenarios.cs` | New | 4 tests: fixture smoke, component setup, backend registration, Scenario 1 |

---

## Test Suite Status

| Suite | Before BATCH-11 | After BATCH-11 | Δ | Status |
|-------|-----------------|----------------|----|--------|
| `Hrot.MuscleCharacter.Animation.Tests` | 169 | 169 | 0 | ✅ No change (verified no regressions) |
| `Hrot.Animation.Integration.Tests` | 23 | 27 | +4 | ✅ New: Fixture smoke, component setup, backend reg, Scenario 1 |
| **Total Passing** | 192 | 196 | +4 | ✅ All green, 1 pre-existing skip |

---

## Approval & Next Steps

**BATCH-11: APPROVED ✅**

**Rationale:**
- All 4 tasks complete and working correctly
- Test quality is high; assertions exercise real code paths
- Infrastructure (`IPumpableHarness`, fixture, test data) is well-designed and reusable
- Critical bug fix in `IssuePlayMontage` discovered and resolved
- Developer insights are thorough and identify real (but non-blocking) weak points
- No regressions; 4 new tests passing

**Next Batch (BATCH-12):**
- **Scope:** Phase 7 Scenarios 2–8 (remaining 7 end-to-end tests)
- **Tasks:** ANC-P7-05 through ANC-P7-11
- **Estimate:** 12–15 hours
- **Priority:** Scenarios 2–8 exercise critical paths (notify keyframes, interruption, stance transition, queue chaining, enqueue mid-play, footstep cadence, look-at lifecycle)
- **Debts to incorporate:** D-21 (MontageId assertion in Executor)

**Suggested Git Commit Message:**

```
BATCH-11 APPROVED: Phase 7 Foundation Complete

ANC-P7-01 through ANC-P7-04 complete:
- PumpUntil + IPumpableHarness verified in shared infra
- AnimationTestHelpers.IssuePlayMontage fixed: now writes full params blob
- AnimationIntegrationFixture + TestData.cs created for all 8 scenarios
- Scenario 1 (happy-path single montage) integration test passing

Results: 196 tests passing (4 new) | Build clean | No regressions | 1 pre-existing skip

Infrastructure Notes:
- Fixture correctly implements IPumpableHarness for deterministic frame-stepping
- All 8 systems execute in correct order per DD-1 SS17
- Test assertions are rigorous and exercise full pipeline
- Backend registration lifecycle (2-tick startup) properly synchronized

Weak Points Identified for Future Work:
- AnimationRuntimeBridgeSystem._entityClassIds unbounded growth on reset (D-19, P3)
- Missing InternalsVisibleTo attribute (D-20, P3 enhancement)
- MontageId zero-check assertion for safety (D-21, P2)
- AnimationBackendCleanupSystem still no-op pending PendingDestroy (D-11, known)

Phase 7 foundation ready for scenarios 2–8.
```

---

## Review Checklist

- [x] All 4 tasks implemented per TASK-DETAIL.md
- [x] Design alignment verified (DD-Tests §6, DD-1 SS17, DD-Fake)
- [x] Test quality rigorous (full pipeline exercises, meaningful assertions)
- [x] No regressions (169 baseline tests still passing)
- [x] Build clean (0 errors, 1 pre-existing warning)
- [x] Infrastructure reusable and well-documented
- [x] Developer insights recorded and categorized
- [x] Weak points identified for future work (5 items, all P2/P3)

