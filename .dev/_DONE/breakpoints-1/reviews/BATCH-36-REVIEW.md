# BATCH-36 Review

**Batch:** BATCH-36
**Reviewer:** Development Lead
**Date:** 2026-05-24
**Status:** NEEDS FIXES (one P1 test quality gap)

---

## Summary

Implementation is structurally sound and complete. All 5 corrective task issues from BATCH-35 are fixed.
`DataBreakpointSystem` is correct, the triple-buffer rewind tests pass with actual repo-state assertions,
and the event path is wired up properly using `FdpEventBus.SwapBuffers()`. One test quality gap was found: the
`NoBreakpoints_DoesNoWork` test does not include the zero-allocation assertion required by TASK-DETAIL.

---

## Corrective Task 0 — Verified PASS

All 5 BATCH-35 issues are resolved:

1. **Fix 1 — UBP-P0T1 tests**: `EngineDebugTimeControllerTests` class added with both required tests.
   - `IEngineDebugTimeController_Implements_PauseResumeStepContract` asserts pause/resume/step state transitions. ✅
   - `IBlueprintTimeController_Still_Resolves_Through_Inheritance` asserts `IsAssignableFrom`. ✅

2. **Fix 2 — GateOn_SyncsSnapshotFromLiveRepo**: Registers `TestHealth`, creates entity with `Current=42`,
   calls Execute, asserts both `HasComponent` and `GetComponent` value. Strong test. ✅

3. **Fix 3 — OnHit_PerformsTripleBufferRewind_AndStateIsCorrect**: All three post-conditions verified:
   - `postTickSnapshot.GetComponent<TestHealth>(entity).Current == 50` (captured live at hit time) ✅
   - `liveRepo.GetComponent<TestHealth>(entity).Current == 100` (rewound to pre-tick) ✅
   - `tc.IsPausedByDebugger == true` + `manager.IsPaused == true` ✅
   - The `liveRepo.Tick()` workaround for the SyncDirtyChunks chunk-version race is correctly explained and justified.

4. **Fix 4 — Step/Continue repo state assertions**: Both `RequestStep_RestoresLiveRepoToPostTickState`
   and `RequestContinue_RestoresLiveRepoToPostTickState` assert `liveRepo == 50` after restore. ✅

5. **Fix 5 — GateOff_Execute_ZeroAllocations**: Uses `GC.GetAllocatedBytesForCurrentThread()` (correct
   choice for parallel xUnit runner — avoids cross-thread skew). Asserts `after - before == 0L`. ✅

---

## UBP-P2T1 (Component-Data Path) — Implementation PASS, Test Quality GAP

### Code review

**`DataBreakpointSystem`:**
- `[UpdateInPhase(SystemPhase.PostSimulation)]` correct. ✅
- Early-out gate `if (!_manager.HasMountedDelegates) return;` correct. ✅
- Mandatory-component query filter via `ComponentTypeRegistry.GetId(t)` → `WithComponentId` correct. ✅
- Collect-then-fire pattern (`pendingHits` list) prevents the rewind-mid-iteration
  `IndexOutOfRangeException` explained in the root-cause notes. Correct fix. ✅
- `sinceVersion = 0` with TODO note accepted for P2. ✅

**`DataBreakpointManager` extensions:**
- `CompiledComponentPredicate` and `CompiledEventScanner` records defined at the correct scope. ✅
- `TryMountDelegate` switch on condition type (PropertyMatch / Compound / BehaviorParam / TransientEvent) is correct. ✅
- `MountedComponentPredicates` and `MountedEventScanners` properties rebuild on every access (allocate per-call).
  This is a P2/P3 optimization note — acceptable for this phase since DESIGN only requires zero-allocation for
  the no-breakpoints case (covered by `HasMountedDelegates` early-out). ✅ (acceptable for now)

### Tests — P2T1

| Test | Assessment |
|------|-----------|
| `NoBreakpoints_DoesNoWork` | Checks `IsPaused == false` only — **MISSING allocation assertion** (see Issue 1 below) |
| `PropertyMatch_FiresWhenConditionMet` | Uses real `PredicateCompiler`, concrete DTO, asserts pause + event. ✅ |
| `FilterEntity_ScopesPredicateToOneEntity` | Creates e1+e2 both matching, scopes to e1, asserts `hitCount == 1`. ✅ |
| `OccurrenceThreshold_PausesOnNthHit` | threshold=3, 3 Executes, checks IsPaused==false x2 then true x1. ✅ |

---

## UBP-P2T2 (Event Path) — PASS

### Code review

- `CompiledEventScanner.Evaluate` uses pre-allocated `_buffer = new(4)` inside the record. Zero per-call allocation. ✅
- `if (_bus == null) return;` guard correctly skips event path when no bus injected. ✅
- The event path follows DESIGN §6.3 pseudocode exactly: `if (scanner.Evaluate(_bus, repo))`. ✅

### Tests — P2T2

| Test | Assessment |
|------|-----------|
| `Bus_AnyOccurrence_Predicate_FiresOnAnyEventOfType` | Publishes `HitTestEvent`, calls `SwapBuffers()`, executes, asserts pause. ✅ |
| `Bus_PayloadConstraint_FiresOnlyWhenPayloadMatches` | Tests both negative (40, no pause) and positive (80, pause). ✅ |

Note: The `HitTestEvent` struct is correctly declared as `[EventId(99201)] internal struct` — the developer
fixed the batch instruction's erroneous `[ComponentId(202)] [Flags]` suggestion. ✅

---

## Issue Found

### Issue 1 — `DataBreakpointSystem_NoBreakpoints_DoesNoWork` missing allocation assertion (P1)

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs`

**Problem:** TASK-DETAIL UBP-P2T1 specifies this success condition as `< 200 ns and 0 B/op`.
The current test only asserts `manager.IsPaused == false` after calling Execute with no breakpoints.
A broken early-return implementation that allocates intermediate state (e.g. enumerating an empty collection)
would still pass.

This is the same class of gap fixed in Corrective Task 0 for `DebugSnapshotProvider` (Fix 5). It must
be treated consistently.

**Fix:** Replace `NoBreakpoints_DoesNoWork` with a version that includes a zero-allocation assertion:

```csharp
[Fact]
public void NoBreakpoints_DoesNoWork_ZeroAllocations()
{
    var (manager, system, repo) = Setup();

    // Warmup.
    system.Execute(repo, 0f);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    long before = GC.GetAllocatedBytesForCurrentThread();
    const int Iterations = 10_000;
    for (int i = 0; i < Iterations; i++)
        system.Execute(repo, 0f);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.False(manager.IsPaused);
    Assert.Equal(0L, after - before);
}
```

---

## Non-Blocking Notes

**Note 1 — Per-tick allocation when breakpoints are mounted (future optimization)**

`MountedComponentPredicates` and `MountedEventScanners` each return a freshly allocated `List` on every
property access. The `foreach` in `DataBreakpointSystem.Execute` accesses both properties once per tick,
producing two heap allocations per tick when any breakpoints are mounted.

Similarly, `pendingHits = new System.Collections.Generic.List<Entity>()` is allocated per predicate per tick.

These are acceptable for P2 — the DESIGN only requires zero allocation for the no-breakpoints case. The
full-qualified namespace reference (`System.Collections.Generic.List<Entity>`) in `DataBreakpointSystem`
should be cleaned up (add `using System.Collections.Generic;` at the top of the file).

**Note 2 — `liveRepo.Tick()` workaround documented in tests**

The root-cause note explaining the `SyncDirtyChunks` chunk-version issue is clear and the fix is correct.
However, this reveals a production invariant: the triple-buffer rewind in `DataBreakpointManager.OnHit`
relies on the live repo's global version being at least one step ahead of the pre-tick snapshot's chunk
versions (which it always is during real simulation, since the engine calls `Tick()` every frame). If the
production integration ever bypasses `Tick()`, the rewind would silently fail. Add a note in
`DataBreakpointManager.OnHit`'s summary comment about this dependency.

---

## Test Count Summary

| Class | Before | After |
|-------|--------|-------|
| `DebugSnapshotProviderTests` | 4 | 5 |
| `SnapshotGateTests` | 5 | 5 |
| `TripleBufferPauseTests` | 7 | 9 |
| `EngineDebugTimeControllerTests` | 0 | 2 |
| `DataBreakpointSystemTests` | 0 | 4 |
| `DataBreakpointSystemEventTests` | 0 | 2 |
| **Total** | **16** | **27** |

---

## Required Fix for BATCH-37 Corrective Task

Before UBP-P2T3 work begins, apply one fix:

**Corrective Task 0 (BATCH-37):** In `DataBreakpointSystemTests.NoBreakpoints_DoesNoWork`, add a
`GC.GetAllocatedBytesForCurrentThread` assertion to confirm zero allocations in the no-breakpoints hot path
(as specified by TASK-DETAIL UBP-P2T1). Rename the test to `NoBreakpoints_DoesNoWork_ZeroAllocations`
for clarity.

Also optionally: replace the fully-qualified `System.Collections.Generic.List<Entity>` in
`DataBreakpointSystem.cs` with a proper `using` directive.
