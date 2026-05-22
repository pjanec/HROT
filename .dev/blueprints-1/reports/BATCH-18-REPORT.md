# BATCH-18 Report

**Batch:** BATCH-18
**Developer:** GitHub Copilot (Claude Sonnet 4.6)
**Date:** 2026-05-22
**Status:** COMPLETED

---

## Work Completed

### CT0-A: GcReclaimRetries Increase

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixtureOptions.cs`

Changed `GcReclaimRetries` from 20 to 30 with an explanatory comment. Addresses the BATCH-17 GC pressure regression where 16 new tests added extra heap objects causing HotReload ALC reclaim tests to fail 2-5 times per run.

### CT0-B: NodeHistoryEntry struct change

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`

Changed `NodeHistoryEntry` from `sealed record` (heap class) to `readonly record struct`. This eliminates one heap allocation per `OnNodeEnter` call -- the struct is now stack-allocated and written inline into the `ExecutionHistory._buffer` array. The `string NodeId` reference inside is still heap, but it was already allocated before the probe call.

Also updated the `ExecutionHistory_Record_ZeroAllocation` test in `DebugMapTests.cs` to include `new NodeHistoryEntry(...)` construction inside the measured region via a `[NoInlining]` `RecordEntry` helper, preventing future regression.

### StepMode enum

Added `public enum StepMode { None, Over, Into, Out }` to `IBlueprintDebugSession.cs` near the other identifier types.

### TASK-DBG-003: Full Breakpoint and Step Semantics

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintDebugSession.cs`

Complete rewrite of the `BlueprintDebugSession` implementation. Key changes:

1. **Replaced `_nodeBreakpoints` HashSet** with proper dual-dictionary storage:
   - `_breakpoints: Dictionary<BreakpointId, Breakpoint>` -- management operations
   - `_bpByNodeString: Dictionary<string, Breakpoint>` -- O(1) probe-path lookup
   - `_nextBpId` auto-incrementing int, starts at 1

2. **`SetBreakpoint`** creates a `Breakpoint` record with `nodeId.ToString("D")` as the key, stores in both dicts, returns the new `BreakpointId`. Resolves DEBT-020.

3. **`ClearBreakpoint`** removes from both dicts by looking up `bp.NodeId` from the id dict.

4. **`ClearAllBreakpoints`** clears both dicts.

5. **`GetBreakpoints`** returns `_breakpoints.Values.ToList().AsReadOnly()`.

6. **`IsAnyBreakpointActive`** returns `_breakpoints.Count > 0`.

7. **`OnNodeEnter`** (reordered vs stub):
   - Records history (unchanged from BATCH-17)
   - Checks `!_isPaused && _bpByNodeString.TryGetValue(nodeId, out bp)` -- re-entrant guard prevents double-pause
   - Checks step mode with additional `!_isPaused` guard to avoid double-pause when a BP is on the step node

8. **`HandleBreakpointHit`** (private): sets `_isPaused`, `_pausedAt`, `_pausedOnEntity`, clears step state, calls `RequestPause()`, increments hit count (if real BP, `bp.Id.Value != 0`), fires `OnBreakpointHit` and `OnSessionStateChanged`. For real BPs, also fires `OnBreakpointListChanged` after hit-count update (since the Breakpoint record changed).

9. **`Continue`**: clears all pause state, calls `RequestResume()`, fires `OnSessionStateChanged`.

10. **`Pause`**: calls `RequestPause()`, sets `_isPaused`, fires `OnSessionStateChanged`.

11. **`StepOver/Into/Out`**: captures `_pausedOnEntity` as `_stepFromEntity`, captures current call depth as `_stepFromDepth`, clears pause state, sets `_stepMode`, calls `RequestStepOneTick()`, fires `OnSessionStateChanged`.

12. **`OnPeerCallEnter/Exit`**: maintain `_currentCallDepth: Dictionary<Entity, int>` per entity. Enter increments, exit decrements with floor at 0.

13. **`IsPaused`, `PausedAt`, `PausedOnEntity`**: now return actual state fields instead of delegating to `_timeController.IsPausedByDebugger`.

14. **`GetCurrentStateSnapshot`**: returns `BlueprintStateSnapshot(_pausedOnEntity.Value, Guid.Empty)` when paused, null otherwise. AssetId stub until DBG-004.

15. **`RegisterDebugMap`**: updated to clear only breakpoints for the affected asset (by matching `bp.AssetId == map.AssetId` on hash mismatch). Removes the "clear all" stub from BATCH-17.

### Test Files Created

- `Hrot.Blueprints.Tests/Debug/BreakpointTests.cs` -- 7 tests (SC1-SC7)
- `Hrot.Blueprints.Tests/Debug/StepTests.cs` -- 4 tests (SC1-SC4)

Both files were force-added to git (`git add -f`) due to the project-wide `[Dd]ebug/` .gitignore pattern (see DEBT-018).

---

## Test Results

### Before CT0 fix (BATCH-17 baseline)

| Run | Pass | Fail | Skip |
|-----|------|------|------|
| 1 | 382 | 3 | 5 |
| 2 | 382 | 3 | 5 |
| 3 | 383 | 2 | 5 |

HotReload tests failed 2-5 times per run under full-suite load.

### After CT0 fix (before DBG-003 tests)

| Run | Pass | Fail | Skip |
|-----|------|------|------|
| 1 | 385 | 0 | 5 |

(390 total; CT0 also caused 5 tests that previously failed due to NodeHistoryEntry being a class to now pass correctly.)

### After DBG-003 (final)

| Run | Pass | Fail | Skip |
|-----|------|------|------|
| 1 | 396 | 0 | 5 |
| 2 | 396 | 0 | 5 |

**Total: 401 tests (396 pass / 5 skip / 0 fail)**. 11 new tests added (7 + 4).

---

## Issues Encountered and Resolution

### 1. Missing usings in new test files

`BreakpointTests.cs` and `StepTests.cs` initially only had `using Fdp.Core` and `using Hrot.Blueprints.Core.Debug`. The types `ISimulationView`, `DebugMap`, `DebugMapEntry`, `IEntityCommandBuffer`, `QueryBuilder` were not found.

**Resolution:** Added the same set of usings as `DebugMapTests.cs`: `Fdp.Interfaces`, `Fdp.ModuleHost.Abstractions`, `Fdp.Toolkit.Blueprints`, `Hrot.Blueprints.Core.Compiler.Emit`.

### 2. New test files silently ignored by .gitignore

`git status` showed no trace of `BreakpointTests.cs` or `StepTests.cs`. The `[Dd]ebug/` gitignore rule (documented in DEBT-018) suppresses them.

**Resolution:** `git add -f` to force-add, consistent with how the existing `DebugMapTests.cs` etc. were staged in prior batches.

### 3. OnBreakpointListChanged firing on every hit

The initial implementation fired `OnBreakpointListChanged` whenever a real breakpoint's hit count changed. While functionally correct (the `Breakpoint` record changed), this could produce noise. However it matches what `SC6` in `BreakpointTests` tests, and the design says to fire it on hit-count update. No change made -- this is intentional behavior.

---

## Design Decisions Beyond Spec

### Re-entrant guard in step-matching

The spec's step-matching code (`_stepMode != StepMode.None && self == _stepFromEntity`) has no explicit `!_isPaused` guard. However, a node that has both a breakpoint AND is the step-target would cause `HandleBreakpointHit` to be called twice in one `OnNodeEnter` (once from BP check, once from step check), resulting in two `RequestPause` calls.

Added `!_isPaused` to the step-match condition:
```csharp
if (_stepMode != StepMode.None && !_isPaused && self == _stepFromEntity)
```

This is consistent with the re-entrant guard applied to the BP check. The step check clears `_stepMode` and calls `HandleBreakpointHit`, which sets `_isPaused`. If the BP check already set `_isPaused`, the step check skips. Net effect: one `RequestPause` per frame, regardless of BP+step overlap.

### Pseudo-breakpoint Id.Value == 0 sentinel

The spec says pseudo-breakpoints (created for step matching) use `new Breakpoint(default, ...)`. Since `_nextBpId` starts at 1, real breakpoints always have `Id.Value >= 1`. The `HandleBreakpointHit` method checks `bp.Id.Value != 0` to determine whether to update hit count. This is a clean sentinel that avoids an explicit `bool isPseudo` parameter on the private method.

### OnBreakpointListChanged on hit count update

The spec says: "Fire `OnBreakpointListChanged?.Invoke(assetId)` after updating hit count." This was implemented as specified. One minor consequence: any listener on `OnBreakpointListChanged` will fire on every hit. In a real editor, this would refresh the breakpoint list UI. Acceptable cost; the alternative (suppress if only hit count changed) requires more complex diffing.

---

## Weak Points

1. **`GetBreakpoints()` allocates on every call** (`_breakpoints.Values.ToList().AsReadOnly()`). Per spec this is the required signature; a `CollectionsMarshal.AsSpan` approach would require changing the interface return type. Deferred to a future DBG cleanup pass.

2. **`RegisterDebugMap` per-asset clear uses LINQ** (`_breakpoints.Values.Where(...).Select(...).ToList()`). This allocates on structure-hash mismatch during hot reload. Negligible for the expected number of breakpoints (<50). Acceptable for Slice 1.

3. **`OnBreakpointListChanged` fires on every hit** (see design decision above). If many entities hit breakpoints frequently, this could flood editor UI listeners. A debounce or "only fire on management operations, not hit-count changes" policy may be needed in DBG-006.

4. **Step matching does not handle entity reuse** -- if `_stepFromEntity` is recycled by the ECS between the step call and the step tick, a different logical entity with the same index could be matched. This is a general ECS staleness issue and is out of scope for Slice 1.

5. **`GetCurrentStateSnapshot` always returns `Guid.Empty` for assetId** -- stub per spec until DBG-004 provides real slot inspection.
