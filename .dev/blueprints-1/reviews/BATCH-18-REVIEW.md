# BATCH-18 Review

**Batch:** BATCH-18
**Reviewer:** Development Lead
**Date:** 2026-05-22
**Status:** APPROVED

---

## Summary

CT0-A and CT0-B fixes correct -- HotReload tests stable at 0 failures across multiple runs. TASK-DBG-003 (breakpoints + steps) fully implemented with correct soft-pause semantics per Patch 1. Test suite: 396 pass / 0 fail / 5 skip, confirmed by independent run.

---

## Issues Found

### Issue 1: HandleBreakpointHit fires OnBreakpointListChanged on every hit (P3)

**File:** `Hrot.Blueprints.Core/BlueprintDebugSession.cs`
**Problem:** `HandleBreakpointHit` fires `OnBreakpointListChanged` after incrementing the hit count (to signal the updated `Breakpoint` record). This is fired on EVERY breakpoint hit during normal execution. `OnBreakpointListChanged` was designed for structural changes (map registration / structure hash mismatch), not per-hit updates. The event's name and design doc §5.3 semantics don't match this use. Editor subscribers will receive spurious "breakpoint list changed" notifications every time any breakpoint fires.
**Fix:** Add a separate `event Action<BreakpointId>? OnBreakpointHitCountChanged` for per-hit count updates, OR simply don't fire `OnBreakpointListChanged` on hit-count increments (editor can read the count from the event payload). For BATCH-19, the developer should add this to DEBT-TRACKER as P3 and evaluate if editor needs the count-update notification.

### Issue 2: Hit count not tracked for pseudo-breakpoints (by design, but undocumented) (P3)

**File:** `Hrot.Blueprints.Core/BlueprintDebugSession.cs`
**Problem:** `HandleBreakpointHit` skips hit-count increment for pseudo-breakpoints (step hits) by checking `bp.Id.Value != 0`. This is correct behavior but there is no comment explaining the `default` BreakpointId sentinel check. Add a comment.

---

## Test Quality Assessment

Tests are correct and comprehensive:

- **SC1** (re-entrant guard): Tests two distinct entities hitting the same BP while paused -- asserts `PauseRequestCount == 1` (not 2). Behavioral test that would catch a missing guard. ✅
- **SC2** (Continue): Asserts both `ResumeCount == 1` AND `IsPaused == false` AND `PausedAt == null` -- full state verification. ✅
- **SC3** (hit count): Interleaves `Continue()` between hits to reset pause state -- tests actual counting, not just one hit. ✅
- **SC7** (event payload): Uses `ConfigurableSimulationView(tick: 42, time: 1.5f)` -- asserts actual `Hit.Tick == 42u` and `Hit.SimulationTime == 1.5f`. The only test that verifies event payload correctness. ✅
- **Step SC3** (StepOut): Tests call-depth tracking by calling `OnPeerCallEnter` before hitting BP, then `OnPeerCallExit` before the matching node -- verifies depth math. ✅

---

## Verdict

**Status: APPROVED**

All CT0 fixes verified, TASK-DBG-003 complete. Issues found are P3 and logged for BATCH-19.

---

**Next Batch:** BATCH-19 -- TASK-DBG-004 (Watch Expressions) + TASK-DBG-005 (Multi-Entity)
