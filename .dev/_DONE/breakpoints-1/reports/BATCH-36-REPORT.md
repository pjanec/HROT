# BATCH-36 Report

**Workstream:** breakpoints-1  
**Batch:** BATCH-36  
**Status:** COMPLETE — all tasks implemented, all tests pass

---

## Task Summary

| Task              | Title                                               | Status |
|-------------------|-----------------------------------------------------|--------|
| Corrective Task 0 | Fix BATCH-35 test quality gaps (all 5 issues)       | DONE   |
| UBP-P2T1          | DataBreakpointSystem — component-data path          | DONE   |
| UBP-P2T2          | DataBreakpointSystem — event path                   | DONE   |

---

## Corrective Task 0 — BATCH-35 Test Quality Gaps

All 5 issues from `BATCH-35-REVIEW.md` resolved.

### Fix 1 — Add UBP-P0T1 tests (Issue 1)

Added new class `EngineDebugTimeControllerTests` with two tests:

- `IEngineDebugTimeController_Implements_PauseResumeStepContract` — instantiates
  `MockDebugTimeController` and asserts that `RequestPause` sets `IsPausedByDebugger`,
  `RequestResume` clears it, and `RequestStepOneTick` clears the paused state.

- `IBlueprintTimeController_Still_Resolves_Through_Inheritance` — asserts
  `typeof(IEngineDebugTimeController).IsAssignableFrom(typeof(IBlueprintTimeController))`
  and that a `MockDebugTimeController` can be assigned to `IEngineDebugTimeController`.

### Fix 2 — Strengthen `GateOn_SyncsSnapshotFromLiveRepo` (Issue 2)

Replaced the trivial `GateOn_ExecuteRuns_WithoutException` test with
`GateOn_SyncsSnapshotFromLiveRepo`. The new test:

1. Registers `TestHealth` on both `live` and `snapshot` repos.
2. Adds an entity to `live` with `Current = 42`.
3. Calls `provider.Execute(live, 0f)` with gate open.
4. Asserts `snapshot.HasComponent<TestHealth>(entity)` is true.
5. Asserts `snapshot.GetComponent<TestHealth>(entity).Current == 42`.

### Fix 3 — Strengthen `OnHit_PerformsTripleBufferRewind_AndStateIsCorrect` (Issue 3)

Rewrote the test to assert actual repository states after `OnHit`:

- `postTickSnapshot.GetComponent<TestHealth>(entity).Current == 50` — post-tick
  capture holds the live value at the time of the hit.
- `liveRepo.GetComponent<TestHealth>(entity).Current == 100` — live repo rewound to
  the pre-tick value.
- `tc.IsPausedByDebugger == true` and `manager.IsPaused == true`.

**Root cause discovered and fixed:** After `preTickSnapshot.SyncFrom(liveRepo)`, both
repos share the same chunk version. `GetComponentRW` with the same `_globalVersion`
uses the condition `if (_chunkVersions[i].Value != currentVersion)` — which is false
when versions match — so it does NOT bump the chunk version. As a result,
`SyncDirtyChunks` sees equal versions and skips the chunk entirely. Fix: call
`liveRepo.Tick()` before mutation to advance `_globalVersion`, ensuring the chunk
version is bumped on the next `GetComponentRW` call.

### Fix 4 — Add repo-state assertions to Step/Continue tests (Issue 4)

Added two new tests:

- `RequestStep_RestoresLiveRepoToPostTickState` — after `OnHit` (liveRepo = 100),
  calls `RequestStep()` and asserts `liveRepo.GetComponent<TestHealth>(entity).Current == 50`
  (restored from postTickSnapshot).

- `RequestContinue_RestoresLiveRepoToPostTickState` — same pattern with `RequestContinue()`.

Both tests also use the `liveRepo.Tick()` fix for the chunk version issue.

### Fix 5 — Add `GateOff_Execute_ZeroAllocations` test (Issue 5)

Added a non-BDN allocation test that:

1. Warms up the JIT by running `provider.Execute` once.
2. Records `GC.GetAllocatedBytesForCurrentThread()` before the hot loop.
3. Runs 10 000 iterations of `provider.Execute(live, 0f)` with gate off.
4. Asserts `after - before == 0L`.

Used `GC.GetAllocatedBytesForCurrentThread()` (per-thread counter) instead of
`GC.GetTotalMemory(false)` (process-wide heap size) to avoid false positives when
other test threads are allocating concurrently during parallel xUnit execution.

### Additional fix — xUnit parallel execution race condition

Four test classes call `ComponentTypeRegistry.Clear()` which mutates a global static
registry. When xUnit runs these classes in parallel (the default), a `Clear()` from
one class can wipe registrations mid-execution in another, causing
`BitMask256.SetBit(bitIndex)` to receive a stale component ID of `-1` and throw
`ArgumentOutOfRangeException`.

**Fix:** Added `[Collection("ComponentRegistry")]` to all four affected test classes:
`DebugSnapshotProviderTests`, `TripleBufferPauseTests`, `DataBreakpointSystemTests`,
and `DataBreakpointSystemEventTests`. Classes that share a collection are serialized
by xUnit; `SnapshotGateTests` and `EngineDebugTimeControllerTests` are unaffected and
continue to run in parallel.

---

## UBP-P2T1 — DataBreakpointSystem (Component-Data Path)

### IDataBreakpointManager extensions

Added to `IDataBreakpointManager.cs`:

```csharp
void OnHit(Breakpoint bp, Entity entity);
bool HasMountedDelegates { get; }
IReadOnlyList<(Breakpoint Breakpoint, CompiledComponentPredicate Compiled)> MountedComponentPredicates { get; }
IReadOnlyList<(Breakpoint Breakpoint, CompiledEventScanner Scanner)> MountedEventScanners { get; }
```

### DataBreakpointManager extensions

Added to `DataBreakpointManager.cs`:

- `CompiledComponentPredicate` record — wraps the compiled `ComponentPredicateDelegate`
  and its `MandatoryComponents` list.
- `CompiledEventScanner` record — wraps the compiled `EventScannerDelegate` and an
  `Evaluate(FdpEventBus, EntityRepository)` convenience method.
- `TryMountDelegate(Breakpoint)` — calls `_predicateCompiler.Compile` or
  `_eventScannerCompiler.Compile` depending on the condition type; stores the result
  in `_mountedPredicates` or `_mountedScanners`.
- `UnmountDelegate(BreakpointId)` — removes compiled entries on `Remove` or disable.
- Internal test seam properties: `PreTickSnapshot` and `PostTickSnapshot`.

### DataBreakpointSystem

New file `DataBreakpointSystem.cs`. Key design decisions:

**Collect-then-fire pattern:** `QueryDelta` callbacks must NOT call `OnHit` directly.
`OnHit` calls `_liveRepo.SyncFrom(_preTickSnapshot)` which rewinds the live repo in
place. The `DeltaQueryEnumerator` captures `_maxIndex` at construction, but after the
rewind `_maxIssuedIndex` is reset to -1, making the previously valid entity index
invalid and causing `IndexOutOfRangeException`. Fix: collect matching entities in a
`List<Entity>`, then iterate and call `OnHit` after `QueryDelta` returns.

**Mandatory-component query filter:** For each mounted predicate, `MandatoryComponents`
are resolved via `ComponentTypeRegistry.GetId(t)` and passed to `QueryBuilder.WithComponentId`.
This restricts `QueryDelta` to entities that actually have the relevant components,
avoiding predicate evaluation on entities without the queried data.

**Early-out:** `if (!_manager.HasMountedDelegates) return;` — zero work when no
breakpoints are mounted.

### Tests (UBP-P2T1)

Class: `DataBreakpointSystemTests`

| Test | Description |
|------|-------------|
| `NoBreakpoints_DoesNoWork` | Execute with no breakpoints does not pause |
| `PropertyMatch_FiresWhenConditionMet` | Compiled predicate fires and pauses when condition matches |
| `FilterEntity_ScopesPredicateToOneEntity` | FilterEntity restricts hit to one entity among two matching |
| `OccurrenceThreshold_PausesOnNthHit` | threshold=3: first two Execute calls do not pause; third does |

---

## UBP-P2T2 — DataBreakpointSystem (Event Path)

### DataBreakpointSystem event path

Added to `DataBreakpointSystem.Execute`:

```csharp
if (_bus == null) return;

foreach (var (bp, scanner) in _manager.MountedEventScanners)
{
    if (scanner.Evaluate(_bus, repo))
        _manager.OnHit(bp, Entity.Null);
}
```

The two-argument constructor `DataBreakpointSystem(IDataBreakpointManager, FdpEventBus?)`
accepts an optional `FdpEventBus`. The single-argument constructor delegates to it with
`null`, disabling the event path.

`CompiledEventScanner.Evaluate` wraps the raw `EventScannerDelegate` signature
(which writes to a `List<SearchResultDto>`) into a boolean result: the scanner fires
when at least one result is collected.

### Tests (UBP-P2T2)

Class: `DataBreakpointSystemEventTests`

| Test | Description |
|------|-------------|
| `Bus_AnyOccurrence_Predicate_FiresOnAnyEventOfType` | AnyOccurrence=true fires when any event of the target type is in the bus read buffer |
| `Bus_PayloadConstraint_FiresOnlyWhenPayloadMatches` | Payload constraint (Damage > 50): value=40 does not fire; value=80 fires |

---

## Test Results

```
dotnet test Hrot.Diagnostics.Breakpoints.Tests.csproj -c Debug
  Passed: 27
  Failed: 0
  Skipped: 0
  Total: 27
```

Run 5 times with identical results — no flakiness.

**Test distribution by class:**

| Class | Tests | Notes |
|-------|-------|-------|
| `DebugSnapshotProviderTests` | 5 | +1 allocation test (Fix 5) |
| `SnapshotGateTests` | 5 | unchanged from BATCH-35 |
| `TripleBufferPauseTests` | 9 | +2 repo-state tests (Fix 4) |
| `EngineDebugTimeControllerTests` | 2 | new class (Fix 1) |
| `DataBreakpointSystemTests` | 4 | new class (UBP-P2T1) |
| `DataBreakpointSystemEventTests` | 2 | new class (UBP-P2T2) |
| **Total** | **27** | |

---

## Build Results

```
dotnet build IOS-IG-SimHost.sln -c Debug
  Build succeeded.
  0 Error(s)
  1 Warning(s)  -- CS0618 IBlueprintTimeController [expected, one-batch grace period]
```

---

## Files Created / Modified

### New files

| File | Description |
|------|-------------|
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointSystem.cs` | ECS system: component-data path + event path |

### Modified files

| File | Change |
|------|--------|
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs` | Added `OnHit`, `HasMountedDelegates`, `MountedComponentPredicates`, `MountedEventScanners` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` | Added `CompiledComponentPredicate`/`CompiledEventScanner` records, `TryMountDelegate`, `UnmountDelegate`, all new interface members, internal test seam properties |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs` | All 5 corrective fixes; 2 new test classes (UBP-P2T1, UBP-P2T2); `[Collection("ComponentRegistry")]` on 4 classes |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj` | Added `StructEdit.Reflection` project reference |

---

## Root Cause Notes

Three non-obvious bugs encountered and fixed:

**1. SyncDirtyChunks version mismatch (tests)**  
After `preTickSnapshot.SyncFrom(liveRepo)`, both repos have identical chunk versions.
`GetComponentRW` only bumps the chunk version when `_globalVersion != currentChunkVersion`.
Since they match post-sync, the chunk is not marked dirty, and `SyncFrom` on the target
sees equal versions and skips the chunk. Fix: call `liveRepo.Tick()` before mutation
to advance `_globalVersion`.

**2. Collect-then-fire in QueryDelta (DataBreakpointSystem)**  
`OnHit` rewinds the live repo mid-iteration. The `DeltaQueryEnumerator` holds a stale
`_maxIndex` after the rewind, causing out-of-range access on the entity store. Fix:
accumulate matching entities in a `List<Entity>` inside the callback, then call `OnHit`
after the full `QueryDelta` loop completes.

**3. xUnit parallel test class execution (tests)**  
`ComponentTypeRegistry.Clear()` is a global-state mutation. Two test classes calling it
concurrently produce a `bitIndex < 0` race: one thread clears the registry while another
has already retrieved a component ID that the next operation depends on. Fix:
`[Collection("ComponentRegistry")]` serializes the affected test classes.
