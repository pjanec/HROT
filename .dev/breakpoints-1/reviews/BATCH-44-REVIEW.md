# BATCH-44 Review — UBP-P7T1 + P7T2 + P7T3 + P7T4

**Date:** 2025  
**Status:** APPROVED  
**Prior test count:** 57  
**New test count:** 72 (+15)

---

## Summary

All four P7 tasks implemented and tested correctly. 72/72 tests pass, zero compiler warnings.

---

## Files Changed

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs` | Added `ExternalHitTagPredicateDto`, `ReadOnlyChildIndices` to `CompoundPredicateDto`, `[JsonDerivedType]` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs` | Added `SourceElementId: Guid?` to `Breakpoint` record |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs` | Added `Guid? sourceElementId = null` parameter to `AddBreakpoint` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` | `_externalHitPredicates` field, `TryMountDelegate` new cases, `UnmountDelegate` cleanup, full `OnExternalHit`, updated `AddBreakpoint`, `HasMountedDelegates` updated |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs` | Added `case ExternalHitTagPredicateDto _: return static (_, _) => false;` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Hrot.BTree.Editor.csproj` | Added `Hrot.Diagnostics.Breakpoints` + `Fdp.Presentation` references |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj` | Same |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj` | Added `Hrot.Diagnostics.Breakpoints` reference |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj` | Added BTree.Editor + Hsm.Editor + Fdp.Presentation refs |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/BTreeBreakpointMenuPopulator.cs` | NEW |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmBreakpointMenuPopulator.cs` | NEW |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintBreakpointMenuPopulator.cs` | NEW |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/BTreeBreakpointGutterRenderer.cs` | Added `SetManager`, `CountManagerBreakpoints()`, extended `Render()` |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmBreakpointGutterRenderer.cs` | Added `SetManager`, extended `CountBreakpoints()` for manager BPs |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` | Added `SetDataBreakpointManager`, routed `HandleBreakpointHit` through manager |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/ExternalHitTagTests.cs` | NEW — 7 tests |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/BTreeContextMenuTests.cs` | NEW — 3 tests |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/HsmContextMenuTests.cs` | NEW — 3 tests |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/BlueprintContextMenuTests.cs` | NEW — 2 tests |

---

## Test Quality Assessment

### P7T4 — ExternalHitTagTests (7 tests) ✓
- **`ExternalHitTag_Standalone_TriggersOnTagMatch`**: Registers standalone tag predicate, calls `OnExternalHit` with matching tag, asserts `OnBreakpointHit` fires. **Correct behavior tested.**
- **`ExternalHitTag_WrongTag_DoesNotFire`**: Negative test — different tag does not fire. **Boundary condition covered.**
- **`ExternalHitTag_InCompoundAnd_ValueZero_Fires`** + **`..._ValueNonZero_DoesNotFire`**: Compound `[ExternalHitTag, PropertyMatch(Health==0)]`. Uses fresh manager instances to avoid paused-state interference. Verifies remaining delegate is evaluated. **Critical path tested independently.**
- **`ExternalHitTag_DisabledBreakpoint_DoesNotFire`**: Disabled BP path. **Good negative test.**
- **`ExternalHitTag_NoMatchingBreakpoint_StillPausesViaFallback`**: No registered BP → still pauses. **Fallback path verified.**
- **`ExternalHitTag_Compiler_ReturnsAlwaysFalse`**: Compiler stub returns always-false delegate. **Correct per design.**

### P7T1 — BTreeContextMenuTests (3 tests) ✓
- **`BTreeContextMenu_AddBreakOnActivation_RegistersWithManager`**: Menu callback verified to synthesise `TraceBufferScanPredicateDto` with correct OpCode/IndexField/StatusField and `SourceElementId == node.VisualId`. Precisely aligned with design.
- **`BTreeContextMenu_AddConditional_OpensDetailsInspectorWithEditReadOnlyA`**: Compound has `ReadOnlyChildIndices=[0]`, Branch A = TraceBufferScan, Branch B = BehaviorParamPredicateDto. **Exactly matches design §13.3.**
- **`BTreeGutterRenderer_ReadsManagerForBreakpoints`**: Registers BP via menu, calls `CountManagerBreakpoints()`, asserts 1. Avoids need for a fake `ICanvasRenderContext`. **Pragmatic but correct.**

### P7T2 — HsmContextMenuTests (3 tests) ✓
- Mirror of P7T1 using `HsmTraceWorkingMemory1024`, `TraceOpCode.StateEnter`, `StateNode.FlatIndex` and `state.StableId`. All structural predicates verified. **Correct HSM-specific values.**
- Gutter renderer test uses `CountBreakpoints()` return value (tuple with state/transition counts), asserts stateDots == 1.

### P7T3 — BlueprintContextMenuTests (2 tests) ✓
- **`Blueprint_NodeBP_RoutesToManager_TripleBufferRewindApplied`**: Registers Slice 1 node BP, calls `_session.OnNodeEnter(entity, nodeId)`, asserts `_manager.IsPaused`. Correctly validates the routing change.
- **`Blueprint_AddConditional_SynthesizesCompoundWithReadOnlyA`**: Branch A = `ExternalHitTagPredicateDto{Tag=nodeId}`, Branch B = `BlueprintVariablePredicateDto{TargetBlueprintAssetId=assetId}`, `ReadOnlyChildIndices=[0]`. **All critical structural fields asserted.**

---

## Key Implementation Observations

1. **`TryMountDelegate` ordering**: The `CompoundPredicateDto when HasExternalHitTag` guard case correctly precedes the generic `CompoundPredicateDto _:` case — C# evaluates guards in source order.

2. **Fallback rewind in `OnExternalHit`**: When no external-hit predicate matches (e.g., no universal BP registered but Blueprint session fires), the triple-buffer rewind still executes. This ensures Slice 1 node breakpoints continue to work even without a universal BP companion.

3. **`sourceElementId` param**: Added as optional `Guid? sourceElementId = null` to `AddBreakpoint` — zero breaking changes to existing callers.

4. **Gutter renderer avoids full render in tests**: `CountManagerBreakpoints()` / `CountBreakpoints()` internal methods avoid needing a fake `ICanvasRenderContext`. Practical and sufficient.

---

## APPROVED — proceed to BATCH-45
