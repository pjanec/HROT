# BATCH-CF7rev REPORT: Auto In-Memory Instrumentation on Demand

**Batch:** BATCH-CF7rev  
**Date:** 2026-06-09  
**Result:** ✅ ALL SUCCESS CRITERIA MET

---

## Build Status

- `dotnet build IOS-IG-SimHost.sln -c Debug` → **0 errors**
- Production/Release build path unchanged → Verified: `DebugProbeInsertion.cs` still hardcodes `CompilerMode.Release` skip

## Test Results

### Hrot.Blueprints.Tests

| Metric | Before | After |
|--------|--------|-------|
| Failed | 7 | 7 |
| Passed | ~1,680 | 1,689 |
| Skipped | 8 | 8 |
| Total | ~1,688 | 1,696 |
| **New Failures** | — | **0** |

### New CF7rev Tests (8/8 pass)

**File 1: `CF7rev_InstrumentationTests.cs`**
1. ✅ `SetBreakpoint_NoDebugMap_InvokesCallback_WithDebugMode`
2. ✅ `SetBreakpoint_HasDebugMap_DoesNotInvokeCallback`
3. ✅ `AddWatch_NoDebugMap_InvokesCallback_WithTraceMode`
4. ✅ `RegisterDebugMap_ReResolves_TentativeProbeNodeId`
5. ✅ `RegisterDebugMap_ReResolves_WhenProbeNodeIdAlreadyCorrect`

**File 2: `CF7rev_EndToEndTests.cs`**
6. ✅ `SetBreakpoint_TriggersAutoInstrument_ThenPauses`
7. ✅ `BreakpointSetBeforeCompile_BecomesActive_AfterMapRegisters`
8. ✅ `ModeSelection_DebugForBreakpoints_TraceForWatches`

### Hrot.Editor.AiShared.Tests

- `EditorSubsystemBoot`: 856 passed, 0 failed ✅

---

## Full Failing Test Set

### Before (pre-existing — baseline)

1. `Hrot.Blueprints.Tests.Compiler.AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(assetName: "MoveToAndFire")`
2. `Hrot.Blueprints.Tests.Compiler.AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(assetName: "HasVisibleTarget")`
3. `Hrot.Blueprints.Tests.Stage8Tests.Stage8_PdbContainsEmbeddedSource`
4. `Hrot.Blueprints.Tests.Stage8Tests.Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`
5. `Hrot.Blueprints.Tests.Runtime.AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`
6. `Hrot.Blueprints.Tests.Demos.MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot`
7. `Hrot.Blueprints.Tests.Benchmarks.WhenNodePerfTests.WhenNode_ZeroAllocOnHotPath`

### After (post CF-7rev changes)

**Same 7 tests** — 0 new failures, 0 regressions.

---

## Tasks Completed

### Task 1: Add instrumentation callback field + setter to BlueprintDebugSession ✅

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`

- Added `private Func<Guid, BPCompilerMode, Task>? _onInstrumentationRequested;` field
- Added `public void SetInstrumentationCallback(Func<Guid, BPCompilerMode, Task>? callback)` setter
- Used `BPCompilerMode` alias to avoid ambiguity with `Fdp.Toolkit.Blueprints.CompilerMode`
- Not added to `IBlueprintDebugSession` (implementation detail)

### Task 2: Invoke callback from SetBreakpoint and AddWatch ✅

- **SetBreakpoint**: Added fire-and-forget call at the top, before breakpoint record creation:
  ```csharp
  if (!_debugMaps.ContainsKey(assetId) && _onInstrumentationRequested != null)
      _ = _onInstrumentationRequested.Invoke(assetId, BPCompilerMode.Debug);
  ```
- **AddWatch**: Added fire-and-forget call requesting `BPCompilerMode.Trace`:
  ```csharp
  if (!_debugMaps.ContainsKey(assetId) && _onInstrumentationRequested != null)
      _ = _onInstrumentationRequested.Invoke(assetId, BPCompilerMode.Trace);
  ```

### Task 3: Re-resolve breakpoints in RegisterDebugMap ✅

- Added `private void ReResolveBreakpointsForAsset(Guid assetId, DebugMapIndex index)` helper
- Called from `RegisterDebugMap` after storing the map and staleness check
- Re-resolves `ProbeNodeId` from `BreakpointTargets` for all tentative breakpoints
- Re-keys `_bpByNodeString` lookup from old probe id → new probe id
- Re-forwards to `_dataBreakpointManager` with correct probe id (atomically: remove old, add new)
- Handles edge cases: no change needed, authored node not in targets, no DBM

### Task 4: Fix SetBreakpoint to pass sourceElementId ✅

Changed `AddBreakpoint` call in `SetBreakpoint`:
```csharp
// Before:
var mgrId = _dataBreakpointManager.AddBreakpoint(
    new ExternalHitTagPredicateDto { Tag = probeIdStr },
    displayName: $"Blueprint node {nodeIdStr}");
// After:
var mgrId = _dataBreakpointManager.AddBreakpoint(
    new ExternalHitTagPredicateDto { Tag = probeIdStr },
    displayName: $"Blueprint node {nodeIdStr}",
    sourceElementId: nodeId);  // authored node GUID — needed for CF-8 persistence
```

### Task 5: Wire callback in EditorSubsystem ✅

File: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

- Added fields: `_blueprintQuickReloadService` (QuickReloadService?) and `_blueprintAssetCatalog` (IAssetCatalog?)
- Stored catalog and service at creation time in the QuickReload scope block
- Wired callback that:
  1. Finds the asset file by ID in the catalog
  2. Loads the `BlueprintAsset` from disk via JSON
  3. Sets `EditorMetadata.CompilerMode = mode`
  4. Awaits `TriggerAsync` for in-memory QuickReload
  5. Catches and logs errors to Console
- Added usings: `System.Text.Json`, `Hrot.Blueprints.Core.Assets`, `Hrot.Blueprints.Core.Compiler`, `Hrot.Blueprints.Editor.Reload`

### Task 6: Add .debug/ to .gitignore ✅

Added to `.gitignore`:
```
# Blueprint debug session files (user-local, not committed)
.debug/
```

---

## Files Changed

| File | Change Summary |
|------|---------------|
| `BlueprintDebugSession.cs` | Added instrumentation callback + setter, callback invocations, ReResolveBreakpointsForAsset, sourceElementId fix |
| `EditorSubsystem.cs` | Wired auto-instrumentation callback with QuickReloadService, stored catalog/service as fields |
| `.gitignore` | Added `.debug/` entry |
| `CF7rev_InstrumentationTests.cs` | **New** — 5 unit tests for callback + re-resolution |
| `CF7rev_EndToEndTests.cs` | **New** — 3 end-to-end tests with compiler + fixture |

---

## Architect Decisions Honored

- ✅ **Q1**: `Func<Guid, CompilerMode, Task>?` callback on `BlueprintDebugSession` — NOT on the interface
- ✅ **Q2**: `RegisterDebugMap` re-resolves tentative breakpoints via `BreakpointTargets`
- ✅ **Q3**: `.debug/bpsession.json` path designed (file location settled; restore deferred to CF-8)
- ✅ **Q4**: `SetBreakpoint` passes `sourceElementId: nodeId` to `DataBreakpointManager`

## Quality Checklist

- [x] Build: 0 errors
- [x] Hrot.Blueprints.Tests: 7 pre-existing failures, 0 new
- [x] Hrot.Editor.AiShared.Tests: 856 passed, 0 failed
- [x] All 8 new CF7rev tests pass
- [x] Callback invoked from SetBreakpoint/AddWatch when no DebugMap
- [x] RegisterDebugMap re-resolves tentative ProbeNodeId
- [x] SetBreakpoint passes sourceElementId to DataBreakpointManager
- [x] .debug/ added to .gitignore
- [x] Production/Release build path unchanged
- [x] No test assertions deleted, skipped, or weakened
- [x] No golden snapshots regenerated

🤖 Generated with [Claude Code](https://claude.com/claude-code)
