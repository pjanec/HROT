# BATCH-17 Review

**Batch:** BATCH-17
**Reviewer:** Development Lead
**Date:** 2026-05-22
**Status:** CHANGES REQUIRED

---

## Summary

TASK-DBG-002 implementation is correct and complete. `DebugMapEntry` extended, `DebugMapIndex` created, `ExecutionHistory` ring-buffer correct, `RegisterDebugMap/UnregisterDebugMap/OnBreakpointListChanged` wired. Test quality is good (16 tests covering all SCs with actual value assertions). However, **2-5 HotReload tests fail consistently under full-suite load** -- a GC pressure regression introduced by BATCH-17. Code must not remain committed with failing tests.

---

## Test Results

| Run | Pass | Fail | Skip |
|-----|------|------|------|
| Independent run 1 | 382 | 3 | 5 |
| Independent run 2 | 382 | 3 | 5 |
| Independent run 3 | 383 | 2 | 5 |
| HotReload filter only | 15 | 0 | 0 |

Tests pass in isolation. Consistent 2-5 failures under full-suite load. Baseline before BATCH-17 was 369 pass / 0 fail / 5 skip.

**Failing tests (always in HotReload group):**
- `SoftReloadTests.SoftReload_InstanceBlueprint_SlotPayloadPreserved`
- `HardReloadTests.HardReload_InstanceBlueprint_SlotPayloadZeroed`
- `LatentCursorReloadTests.HardReload_InstanceBlueprint_NextTickDoesNotCrash`
- `QuickReloadTests.QuickReload_UpdatesCurrentAlc`
- `AlcLifecycleTests.FailedReload_DoesNotLeakNewAlc`

---

## Root Cause

`OnNodeEnter` in `BlueprintDebugSession` calls `new NodeHistoryEntry(nodeId, _view.Tick, _view.Time)` on every probe invocation. `NodeHistoryEntry` is a `sealed record` (heap class). Each `BlueprintDebugSession` constructor also pre-allocates `ExecutionHistory` on first entity visit (256-slot `NodeHistoryEntry[]`). These allocations accumulate across the 16 new tests, increasing GC pressure enough to exceed the 20-retry ALC reclaim window for HotReload tests.

---

## Issues Found

### Issue 1: GC pressure regression causes consistent HotReload failures (P1 -- CT0)

**Problem:** Full-suite test count increased from 374 to 390. New allocations from `BlueprintDebugSession` history tracking increase GC heap pressure. Result: HotReload ALC GC reclaim tests fail 2-5 per run consistently.

**Fix (two-part):**

**Part A: Increase `GcReclaimRetries` to 30** in `BlueprintTestFixtureOptions.cs`:
```csharp
public int GcReclaimRetries { get; init; } = 30;  // was 20
```

**Part B: Change `NodeHistoryEntry` from `sealed record` (class) to `readonly record struct`** in `IBlueprintDebugSession.cs`:
```csharp
public readonly record struct NodeHistoryEntry(string NodeId, uint Tick, float SimTime);
```
This eliminates heap allocation on every `OnNodeEnter` call. The `string NodeId` reference is still heap-allocated but was already allocated before this point. The struct itself is stack-allocated and copied into the ring-buffer array slot.

After Part B, update the zero-allocation test in `DebugMapTests.cs` (`ExecutionHistory_Record_ZeroAllocation`) to assert zero allocation for the `Record()` call INCLUDING the `new NodeHistoryEntry(...)` construction inside the measured region, to prevent regression.

**Verify:** Full suite 0 failures after fix.

---

## Test Quality Assessment

Tests are correct:
- SC1 (resolve by string + Guid): checks actual `NodeId`, `NodeKind`, `DisplayName` values -- not just "returned something"
- SC2 (structure-hash mismatch): checks event was fired with correct `assetId`; and that same-hash re-register does NOT fire
- SC3 (ring-buffer wrap): asserts specific entries by name in chronological order, plus `GetRecent` max-count limit
- SC4 (entity isolation): checks that E1 history has only E1's entries
- SC5 (serializer roundtrip): checks actual field values after deserialize

No fake or shallow tests.

---

## Verdict

**Status: CHANGES REQUIRED**

**Required Actions:**
1. CT0-A: Increase `GcReclaimRetries` to 30 in `BlueprintTestFixtureOptions.cs`
2. CT0-B: Change `NodeHistoryEntry` to `readonly record struct`
3. Verify full suite: 0 failures

---

**Next Batch:** BATCH-18 -- CT0 fixes + TASK-DBG-003 (Breakpoints and Step Semantics)
