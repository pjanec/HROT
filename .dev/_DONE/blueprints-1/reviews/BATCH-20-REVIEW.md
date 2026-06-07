# BATCH-20 Review

**Batch:** BATCH-20
**Reviewer:** Development Lead
**Date:** 2026-05-22
**Status:** APPROVED

---

## Summary

TASK-DBG-006 (Debug Protocol Test Suite) completed. 18 new tests added, total suite 424 pass / 0 fail / 5 skip (429 total). Test results verified independently. Build is clean (0 errors). All required files created.

---

## Files Verified

| File | Tests | Scope |
|------|-------|-------|
| `Debug/NodeHistoryTests.cs` | 4 | Ring buffer, entity isolation, wrap-at-256, maxCount |
| `Debug/StateInspectorTests.cs` | 5 | Snapshot null/non-null, MarshalFromBytes int/float/unknown |
| `Debug/HotReloadInteractionTests.cs` | 4 | Hot reload edge cases (not-paused, all-stale, asset-selective, BP clear) |
| `Debug/ProbeDispatchTests.cs` | 4 | Null-sink no-op, forwarding, zero-alloc paths |
| `Benchmarks/ProbeOverheadBenchmarks.cs` | - | BenchmarkDotNet standalone (not run in test mode) |
| `Benchmarks/ProbeOverheadTests.cs` | 1 | xUnit CI gate for zero-alloc probe path |

---

## Issues Found

### Issue 1: NodeHistoryTests uses cast to BlueprintDebugSession (P3 -- by design)

**File:** `Debug/NodeHistoryTests.cs`
**Problem:** `GetNodeHistory(Entity, int)` is a non-interface method. Tests cast `session` to `BlueprintDebugSession` directly. This is acceptable but brittle if the method moves to the interface later. Mark for DBG-006 follow-up when interface is finalized.
**Action:** Log as DEBT-022 -- add `GetNodeHistory(Entity, int)` to `IBlueprintDebugSession` in a future batch if editor needs it.

### Issue 2: ProbeDispatchTests.SC2 uses CapturingDebugSession -- verify NodeEnterCalls tracking (P4)

**File:** `Debug/ProbeDispatchTests.cs`
**Problem:** SC2 asserts `CapturingDebugSession.NodeEnterCalls` contains `(E1, "some-id")`. Need to verify `CapturingDebugSession` actually accumulates `NodeEnterCalls`. Since all tests passed (406 -> 424 and 0 failures), this is confirmed working.
**Action:** None needed.

---

## Test Quality Assessment

- **NodeHistoryTests SC3 (wrap-at-256)**: Records 260 entries, verifies `GetRecent(500)` returns exactly 256, and first entry is from node #5 (not #1). Verifies both the capacity limit and the chronological ordering. ✅
- **StateInspectorTests SC1 + SC2**: Tests both paused (non-null snapshot) and not-paused (null). Complete coverage. ✅
- **MarshalFromBytes SC3-SC5**: Covers int, float, and unknown-type fallback. ✅
- **HotReloadInteractionTests SC3**: Tests asset-selective stale clearing -- asserts AssetIdA watch clears but AssetIdB watch remains stale. ✅
- **HotReloadInteractionTests SC4**: Tests breakpoint clear only for same-asset (2 AssetA BPs cleared, 1 AssetB BP remains). ✅
- **ProbeOverheadTests**: xUnit CI gate measuring zero-alloc for null-sink probe path. ✅

---

## Verdict

**Status: APPROVED**

Phase 5 Debug Protocol is COMPLETE. All 6 tasks (DBG-000 through DBG-005) implemented, DBG-006 test suite complete.

---

**Next Batch:** BATCH-21 -- Phase 6 Editor (TASK-ED-001: Infrastructure, Window Lifecycle, IWindowRegistrar)
