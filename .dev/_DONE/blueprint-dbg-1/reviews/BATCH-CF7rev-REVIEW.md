# BATCH-CF7rev Review

**Batch:** BATCH-CF7rev  
**Reviewer:** Development Lead  
**Date:** 2026-06-09  
**Status:** ✅ APPROVED

---

## Summary

Auto-instrumentation callback wired on `BlueprintDebugSession`, invoked fire-and-forget from `SetBreakpoint`/`AddWatch` when no DebugMap exists. `RegisterDebugMap` re-resolves tentative breakpoints through `BreakpointTargets`. `sourceElementId` now passed to `DataBreakpointManager`. `EditorSubsystem` wires callback → QuickReload in-memory. `.debug/` added to `.gitignore`. All 4 architect decisions honored.

---

## Verification Results

| Gate | Result |
|------|--------|
| Build (0/0) | ✅ 0 errors, 0 warnings |
| Hrot.Blueprints.Tests | ✅ 7 pre-existing, 0 new (1681 pass, 8 skip) |
| Hrot.Editor.AiShared.Tests | ✅ 856/0 |
| EditorSubsystemBoot | ✅ 10/10 |
| New CF7rev tests | ✅ 8/8 pass |
| Production path unchanged | ✅ `DebugProbeInsertion.cs:9` still gates on `Release` |
| Golden snapshots | ✅ Not regenerated |

---

## Issues Found

### Issue 1: EditorSubsystem indentation (P3 — cosmetic)

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`  
**Problem:** Body of `if (_blueprintDebugSession != null)` block lacks an extra indentation level. The `SetInstrumentationCallback` call aligns with the `if` rather than being indented inside it.  
**Fix:** Indent the block body one level deeper. Cosmetic only — no functional impact.

---

## Test Quality Assessment

**Good:**
- `SetBreakpoint_NoDebugMap_InvokesCallback_WithDebugMode` — verifies callback invoked with `(assetId, Debug)` ✅
- `SetBreakpoint_HasDebugMap_DoesNotInvokeCallback` — verifies callback NOT invoked when map exists ✅
- `AddWatch_NoDebugMap_InvokesCallback_WithTraceMode` — verifies `Trace` for watches ✅
- `RegisterDebugMap_ReResolves_TentativeProbeNodeId` — verifies `ProbeNodeId` changes from authored→block-probe after registration ✅
- `RegisterDebugMap_ReResolves_WhenProbeNodeIdAlreadyCorrect` — verifies no-op case doesn't break ✅
- `SetBreakpoint_TriggersAutoInstrument_ThenPauses` — golden end-to-end: Release compile → breakpoint → Debug recompile → pause ✅
- `BreakpointSetBeforeCompile_BecomesActive_AfterMapRegisters` — verifies `IsNodeBreakpointable` and re-resolution ✅
- `ModeSelection_DebugForBreakpoints_TraceForWatches` — verifies correct mode selection ✅

Tests verify **actual values** (captured parameters, ProbeNodeId changes, PauseRequestCount), not string presence or compilation. No shallow tests.

---

## Verdict

**Status:** ✅ APPROVED

All requirements met. Ready to commit and move to user smoke test.

---

## 📝 Commit Message

```
feat: auto in-memory instrumentation on demand (CF-7-rev)

Completes CF-7-rev

BlueprintDebugSession now accepts an instrumentation callback that fires when
the first breakpoint/watch is set on an un-instrumented asset. RegisterDebugMap
re-resolves tentative breakpoints' ProbeNodeId through BreakpointTargets.
EditorSubsystem wires the callback to trigger a Debug/Trace QuickReload in-memory.

BlueprintDebugSession:
- Add Func<Guid, CompilerMode, Task>? instrumentation callback (not on interface)
- Fire-and-forget from SetBreakpoint (Debug) and AddWatch (Trace)
- ReResolveBreakpointsForAsset: update ProbeNodeId + re-key _bpByNodeString
- Pass sourceElementId to DataBreakpointManager.AddBreakpoint

EditorSubsystem:
- Store QuickReloadService + IAssetCatalog as fields
- Wire callback: find asset by ID → load → set CompilerMode → TriggerAsync
- Error handling: catch and log to Console

.gitignore: add .debug/ (preparation for CF-8)

Tests: 8 tests (5 unit + 3 end-to-end with compiler)
Gates: build 0/0, Blueprints 7/0-new, AiShared 856/0, boot 10/10
Production path untouched: DebugProbeInsertion still gates on Release
```

---

**Next Batch:** CF-8 (Persist & restore debug session)
