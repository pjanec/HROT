# BATCH-16 Review

**Batch:** BATCH-16
**Reviewer:** Development Lead
**Date:** 2026-05-22
**Status:** APPROVED

---

## Summary

All corrective tasks (CT0-A DEBT-016, CT0-B DEBT-017) fixed correctly. TASK-DBG-000 and TASK-DBG-001 implemented per spec. Test suite: 369 pass / 5 skip / 0 fail, confirmed by independent run.

---

## Issues Found

### Issue 1: Debug files placed in project root instead of Debug/ subfolder (P3)

**Files:** `Hrot.Blueprints.Core/IBlueprintTimeController.cs`, `IBlueprintProbeSink.cs`, `IBlueprintDebugSession.cs`, `DebugProbe.cs`, `BlueprintDebugSession.cs`

**Problem:** Batch instructions specified `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/` as the target folder. Files were placed in the project root instead (reported reason: `.gitignore` `[Dd]ebug/` pattern). Namespace `Hrot.Blueprints.Core.Debug` is correct, so there is no functional impact.

**Fix:** Acceptable as-is for now. Add to DEBT-TRACKER as P3. DBG-002 developer should continue placing new Debug files in the same root location to remain consistent with what is there now.

### Issue 2: DebugProbe.Sink is a mutable static -- parallel test races (P3)

**Problem:** As noted by the developer, `DebugProbe.Sink` is a process-wide mutable. Parallel xUnit test classes that set/clear `Sink` can interfere. Currently test methods reset `Sink = null` in `finally` blocks but there is no `[Collection]` isolation. This is benign with current test count but will become a concern in DBG-002 through DBG-006 as the test suite grows.

**Fix:** Add to DEBT-TRACKER as P3. DBG-006 batch should add a `[Collection("DebugProbe")]` isolation group or equivalent.

### Issue 3: BlueprintDebugSession.SetBreakpoint uses nodeId.ToString() as map key (P3)

**Problem:** `SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)` stores `nodeId.ToString()` in `_nodeBreakpoints`. `OnNodeEnter` matches against the same string. This is intentionally minimal per the task spec (full matching deferred to DBG-003), but the stub's Guid-to-string key is brittle: the format depends on `Guid.ToString()` default format ("D") matching whatever string is passed to `OnNodeEnter`. DBG-003 must replace this entirely.

**Fix:** Document in DEBT-TRACKER as P3, marked for DBG-003.

---

## Test Quality Assessment

Tests are correct and meet the quality bar:

- SC1/SC2 zero-allocation tests use `GC.GetAllocatedBytesForCurrentThread()` with warm-up + `[NoInlining]` helper -- the correct pattern.
- SC3 breakpoint test wires a real `BlueprintDebugSession` through `MockTimeController`; checks `PauseWasRequested == true` on match and `== false` on non-match -- both cases covered.
- SC4 reflection test explicitly asserts absence of a `Value` property (not just presence of `ValueBytes`) -- catches the prohibited design.
- All five new CT0 `[NoInlining]` fixtures confirmed by zero HotReload failures across two full-suite runs.

No fake or shallow tests found.

---

## Verdict

**Status: APPROVED**

DEBT-016 and DEBT-017 resolved. TASK-DBG-000 and TASK-DBG-001 complete. Code committed at `02f476a6`.

---

## 📝 Commit Message

Already committed (`02f476a6`, `4cbd27ad`). No additional commit needed.

---

**Next Batch:** BATCH-17 -- TASK-DBG-002 (Debug Map Format and Node-ID Resolution)
