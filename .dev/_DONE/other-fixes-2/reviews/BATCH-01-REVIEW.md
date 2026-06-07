# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Development Lead  
**Date:** 2026-05-31  
**Status:** NEEDS FIXES

---

## Issues Found

### Issue 1: FIX2-003 -- `DebugProbe.NewTick()` still only in test code, not production

**File:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintTickSystem.cs` (Execute method -- the call is ABSENT)  
**Problem:** The task requires calling `DebugProbe.NewTick()` from the production frame loop. The developer found the call inside `BlueprintTestFixture.TickFrame()` (test-only code in `Hrot.Blueprints.Tests`) and declared the wiring "already in place". This is incorrect -- `BlueprintTestFixture` is a test fixture, not production code. `BlueprintTickSystem.Execute()` (the real production ECS system) does not call `DebugProbe.NewTick()` at any point.

**Effect:** In a real running game or simulation, `IBlueprintDebugSession.OnNewTick()` is never called. The per-frame dedup set (`_firedBreakpointsThisTick`) never resets. A breakpoint hit in tick N will be permanently suppressed in ticks N+1, N+2, ... unless the user manually calls `Continue()`.

**Test vacuity:** `Breakpoint_FiresTwice_AcrossTwoTicks_WithNewTickWiring` passes only because the test fixture (`TickFrame`) calls `DebugProbe.NewTick()` before delegating to `TickSystem.Execute()`. If `DebugProbe.NewTick()` were removed from `TickFrame` and NOT added to `Execute()`, the test would fail -- proving the current "fix" is test-fixture-only, not production.

**Required fix:**
1. Move the `DebugProbe.NewTick()` call from `BlueprintTestFixture.TickFrame()` INTO `BlueprintTickSystem.Execute()` (at the top, before blueprint ticking begins).
2. Remove the duplicate call from `BlueprintTestFixture.TickFrame()` (otherwise it fires twice per tick during tests).
3. The existing test `Breakpoint_FiresTwice_AcrossTwoTicks_WithNewTickWiring` should continue to pass because `TickFrame()` delegates to `Execute()`. This proves the production path carries the call.

---

## Test Quality Assessment

FIX2-001 test (`CompiledProbe_EmitsNodeId_InDFormat`) -- **acceptable**. Drives the full compile -> emit -> Roslyn -> load -> tick -> probe chain. The negative assertion (`:N` format absent) is a strong regression guard.

FIX2-003 test (`Breakpoint_FiresTwice_AcrossTwoTicks_WithNewTickWiring`) -- **vacuous** for the reason above. The test proves the test fixture works, not production code.

FIX2-004 tests (`OnEditorActivated_CallsAttach_...`, `OnEditorDeactivated_CallsDetach_...`) -- **acceptable**. Construct the real module, invoke activate/deactivate, assert `DebugProbe.Sink` state changes. No direct `Attach()`/`Detach()` calls from the tests.

---

## Verdict

**Status:** NEEDS FIXES

**Required Action:**
1. Move `DebugProbe.NewTick()` from `BlueprintTestFixture.TickFrame()` into `BlueprintTickSystem.Execute()` (before blueprint ticking begins).
2. Remove the `DebugProbe.NewTick()` call from `BlueprintTestFixture.TickFrame()` to avoid double-call in tests.
3. Confirm the test `Breakpoint_FiresTwice_AcrossTwoTicks_WithNewTickWiring` still passes (it will, because `TickFrame` calls `Execute`).
4. Confirm full test suite is still green.

FIX2-001 and FIX2-004 are approved. Only FIX2-003 needs rework.

---

**Next:** After fixing FIX2-003, proceed to BATCH-02.
