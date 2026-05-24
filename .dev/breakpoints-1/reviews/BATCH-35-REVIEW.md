# BATCH-35 Review

**Batch:** BATCH-35
**Reviewer:** Development Lead
**Date:** 2026-05-24
**Status:** ⚠️ NEEDS FIXES (test quality gaps — see below)

---

## Summary

Implementation is structurally correct and the solution builds clean. All 16 tests pass. However, several tests verify only events or gate flags without asserting the actual repository state, missing the core correctness contracts specified in TASK-DETAIL.

---

## Issues Found

### Issue 1: Missing UBP-P0T1 tests (P1)

**Problem:** The two required tests for the interface rename were not written:
- `IEngineDebugTimeController_Implements_PauseResumeStepContract`
- `IBlueprintTimeController_Still_Resolves_Through_Inheritance`

The task detail (TASK-DETAIL.md UBP-P0T1) explicitly names these and the batch instructions repeat them. They are not present in any test file.

**Fix:** Add both tests. `IEngineDebugTimeController_Implements_PauseResumeStepContract` must use `MockDebugTimeController` and verify `IsPausedByDebugger` toggles correctly on pause/resume/step calls. `IBlueprintTimeController_Still_Resolves_Through_Inheritance` must assert `typeof(IBlueprintTimeController).IsAssignableTo(typeof(IEngineDebugTimeController))` and confirm the adapter can be assigned to both interface types.

---

### Issue 2: `GateOn_ExecuteRuns_WithoutException` — shallow test (P1)

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs`

**Problem:** This test only verifies that `Execute` does not throw. It does NOT verify that the snapshot was actually populated with the live repo's data — the core behavior of this system. A completely broken `SyncFrom` call would still pass this test.

**Fix:** The test must:
1. Register a component on `live`.
2. Call `provider.Execute(live, 0f)` with gate on.
3. Assert that `_preTickSnapshot` has that component value.

This is the same pattern used in `EntityRepositorySyncTests.SyncFrom_Default_ExcludesTransient` — create entities/components and assert the other repo receives them.

---

### Issue 3: `OnHit_PerformsTripleBufferRewind_AndFiresEvents` — does not verify repo state (P1)

**File:** `DataBreakpointManagerTests.cs` — `TripleBufferPauseTests`

**Problem:** The TASK-DETAIL success condition for `Manager_OnHit_PerformsTripleBufferRewind` specifies three explicit assertions:
- (a) `_postTickSnapshot` matches `_liveRepo` at fire time (captures post-execution state)
- (b) `_liveRepo` after the call equals `_preTickSnapshot` (rewound to pre-tick)
- (c) `_timeController.IsPausedByDebugger == true`

The current test only verifies events fired and `tc.PauseRequestCount`. The actual repository state changes — the fundamental correctness of the triple-buffer protocol — are not verified. A broken `SyncFrom` in `OnHit` would still pass.

**Fix:** Add concrete repository state assertions:
- Set `_preTickSnapshot` to have `Health.Current = 100` (by calling `SyncFrom` on a staging repo, or by registering/adding directly after the `DebugSnapshotProvider` fills it).
- Set `_liveRepo` to have `Health.Current = 50` before calling `OnHit`.
- After `OnHit`: assert `_postTickSnapshot.GetComponent<Health>(entity).Current == 50`.
- After `OnHit`: assert `_liveRepo.GetComponent<Health>(entity).Current == 100`.

The `DataBreakpointManager` exposes `preTickSnapshot` and `postTickSnapshot` internally — expose them via `internal` test seam properties if needed.

---

### Issue 4: `RequestStep`/`RequestContinue` tests do not verify repo state (P1)

**File:** `DataBreakpointManagerTests.cs` — `TripleBufferPauseTests`

**Problem:** `RequestContinue_ResumesClockAndClearsPause` and `RequestStep_ResumesWithOneTick_AndClearsPause` verify the clock call and `IsPaused` flag but do NOT verify that `_liveRepo.SyncFrom(_postTickSnapshot)` was actually called. A missing `SyncFrom` call would still pass.

**Fix:** After `RequestStep()` or `RequestContinue()`, assert that `_liveRepo` has the same component values as what was in `_postTickSnapshot` at pause time (i.e., the value that was in live before the rewind).

---

### Issue 5: `DebugSnapshotProvider_ZeroAllocationsHotPath` test missing (P1)

**Problem:** TASK-DETAIL UBP-P1T1 explicitly lists this success condition. The instructions say to write a non-BDN test that calls `Execute` 10000 times with gate off and asserts no allocations via `GC.GetTotalMemory`. This test is absent.

**Fix:** Add:
```csharp
[Fact]
public void GateOff_Execute_ZeroAllocations()
{
    var snapshot = new EntityRepository();
    var provider = new DebugSnapshotProvider(snapshot);
    var live = new EntityRepository();

    // Warm up.
    provider.Execute(live, 0f);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long before = GC.GetTotalMemory(false);

    for (int i = 0; i < 10_000; i++)
        provider.Execute(live, 0f);

    long after = GC.GetTotalMemory(false);
    Assert.Equal(0L, after - before);
}
```
(Gate stays off; Execute hot path allocates nothing.)

---

## Verdict

**Status: NEEDS FIXES**

**Required Actions (Corrective Task 0 in BATCH-36):**
1. Add `IEngineDebugTimeController_Implements_PauseResumeStepContract` and `IBlueprintTimeController_Still_Resolves_Through_Inheritance` tests.
2. Strengthen `GateOn_ExecuteRuns_WithoutException` to assert actual snapshot state.
3. Strengthen `OnHit_PerformsTripleBufferRewind_AndFiresEvents` to assert repository contents after rewind (pre-tick and post-tick).
4. Strengthen `RequestStep`/`RequestContinue` tests to verify `_liveRepo` matches `_postTickSnapshot` after restore.
5. Add `GateOff_Execute_ZeroAllocations` test.

All fixes are test-only (no production code changes required). Complete these before any P2 work.

---

**Next Batch:** BATCH-36 (Corrective Task 0 above + P2 tasks UBP-P2T1, UBP-P2T2, UBP-P2T3)
