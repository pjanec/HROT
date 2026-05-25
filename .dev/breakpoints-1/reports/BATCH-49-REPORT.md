# BATCH-49 Report

**Batch:** BATCH-49  
**Phase:** P10 — Universal Breakpoints wiring  
**Tasks:** UBP-P10T3 through UBP-P10T11  
**Status:** COMPLETE

---

## Summary of Changes

### Production Code

| File | Task | Change |
|------|------|--------|
| `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs` | P10T10 | Added `public event Action? OnReloadBegin;`; fired in `DrainPendingCallbacks` (step 5.5) and `ApplyQuickReload` (step 3.5) |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | P10T3–P10T11 | All wiring tasks applied (see detail below); also added internal test hooks `BpMutationInterceptor` and `AiCoordinator` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeEditorHostServices.cs` | P10T7 | Added `SetBreakpointManager`, `BpGutterRenderer` property, and explicit interface impl for `CustomElementContextMenu` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeBreakpointContextMenuProvider.cs` | P10T7 | **NEW** — `BTreeBreakpointContextMenuProvider` + `ContextMenuItemCollector` |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmEditorHostServices.cs` | P10T8 | Same pattern as BTree: `SetBreakpointManager`, `BpGutterRenderer`, explicit interface impl |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmBreakpointContextMenuProvider.cs` | P10T8 | **NEW** — `HsmBreakpointContextMenuProvider` + `HsmContextMenuItemCollector` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs` | P10T9 | Added `_bpManager` field and `SetBreakpointManager(IDataBreakpointManager?)` method |

#### EditorSubsystem changes in detail

- **P10T4** — `_bpManager` construction block moved to before the gizmo systems; `DataDrivenGizmoSystem` and `GlobalGizmoManager` constructors updated with `breakpointManager: _bpManager`.
- **P10T3** — `DataBreakpointManagerWindow` registration added to `RegisterWindows()` before the `if (_headless) return;` guard.
- **P10T5** — `_fdpEntityInspector.Reflector.MutationInterceptor = _bpManager` set in `RegisterWindows()` before the headless guard.
- **P10T6** — `BlueprintDebugSession` wired to `_bpManager` and `DebugProbe.Sink` in `Initialize()`; `DebugProbe.Sink = null` and `_blueprintDebugSession = null` added to `Shutdown()`.
- **P10T10** — `_aiCoordinator.OnReloadBegin += ...` and `_aiCoordinator.OnReloadCompleted += ...` subscriptions added after the BP block in `Initialize()`.
- **P10T11** — `_bpManager.LoadWatches(watchesPath)` added to `Initialize()` (conditional on file existence); `_bpManager.SaveWatches(watchesPath)` added to `Shutdown()`.

### Test Files

| File | Tests Added |
|------|-------------|
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BreakpointSubsystemWiringTests.cs` | 13 new tests (Tests 6–18) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Host/BTreeBreakpointWiringTests.cs` | **NEW** — 4 tests |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmBreakpointWiringTests.cs` | **NEW** — 4 tests |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/BlueprintContextMenuTests.cs` | 1 new test + `HasItem()` helper added to `BlueprintRecordingContextMenuBuilder` |

---

## New Test Methods

| # | Test Method | Location | Result |
|---|------------|----------|--------|
| 1 | `Gizmo_System_UsesManagerActiveView_WhenPaused` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 2 | `Gizmo_System_FallsBackWhenNoManager` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 3 | `ManagerWindow_RegisteredInEditorPerspective` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 4 | `ManagerWindow_NotShownInUnrelatedPerspective` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 5 | `ManagerWindow_OpensOnMenuCommand` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 6 | `Inspector_EditWhilePaused_RoutesToStageMutation` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 7 | `Inspector_EditWhileRunning_StillDirectWrites` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 8 | `Blueprint_NodeBP_RoutesThroughManager_TripleBufferApplied` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 9 | `BTree_ContextMenu_ShowsBreakpointItems_WhenManagerWired` | `BTreeBreakpointWiringTests` | ✅ Pass |
| 10 | `BTree_GutterRenderer_ManagerWired_IsReady` | `BTreeBreakpointWiringTests` | ✅ Pass |
| 11 | `Hsm_ContextMenu_ShowsBreakpointItems_WhenManagerWired` | `HsmBreakpointWiringTests` | ✅ Pass |
| 12 | `Hsm_GutterRenderer_ManagerWired_IsReady` | `HsmBreakpointWiringTests` | ✅ Pass |
| 13 | `Blueprint_ContextMenu_ShowsConditionalBreakpointItem` | `BlueprintContextMenuTests` | ✅ Pass |
| 14 | `HotReload_WhilePaused_FlushesPendingAndContinues` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 15 | `HotReload_RebindsCompiledDelegates` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 16 | `HotReload_StructuralBreak_MarksBPIsBroken_NoCrash` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 17 | `Watches_RoundTripAcrossEditorRestart` | `BreakpointSubsystemWiringTests` | ✅ Pass |
| 18 | `Watches_Restore_FailsGracefullyOnDriftedSchema` | `BreakpointSubsystemWiringTests` | ✅ Pass |

**All 18 required test methods pass.**

Extra tests added (not required, free coverage):
- `BTree_ContextMenu_RendererIdMatchesGutterRenderer`
- `BTree_GutterRenderer_ClearedWhenManagerNull`
- `Hsm_ContextMenu_RendererIdMatchesGutterRenderer`
- `Hsm_GutterRenderer_ClearedWhenManagerNull`

---

## Build Output

```
dotnet build IOS-IG-SimHost.sln -v quiet

  0 Error(s)
  4 Warning(s) (all pre-existing CS0618 obsolete IBlueprintTimeController warnings)
```

Build: **0 errors** confirmed.

---

## Deviations from Instructions

1. **`StateNode` constructor**: `HsmBreakpointContextMenuProvider` originally used object-initializer syntax `new StateNode { ... }`, which fails because `StateNode` has a required constructor `StateNode(string name)`. Fixed to `new StateNode(elementKey) { StableId = stableId, FlatIndex = 0 }`.

2. **`IMutationInterceptor` namespace**: The `BpMutationInterceptor` test hook property initially referenced `Fdp.Presentation.Abstractions.IMutationInterceptor`, but the actual type is `Fdp.Toolkit.Diagnostics.Gizmos.IMutationInterceptor`. Fixed.

3. **`IDataBreakpointManager` stub methods**: Stubs in BTree/HSM unit tests initially included non-interface methods (`AdvancePostTickScan`, `FlushPendingMutations`). Fixed to match the actual interface (which includes `EvaluateStatefulBreakpoints`, `MountedComponentPredicates`, `MountedEventScanners`).

4. **`Blueprint_ContextMenu_ShowsConditionalBreakpointItem` location**: Placed in `Hrot.Diagnostics.Breakpoints.Tests/BlueprintContextMenuTests.cs` (same file as existing `BlueprintRecordingContextMenuBuilder`) rather than `Hrot.Blueprints.Tests` as suggested in the instructions, to reuse the existing infrastructure without creating cross-project dependencies.

5. **Test hook properties on `EditorSubsystem`**: Added two internal properties (`BpMutationInterceptor`, `AiCoordinator`) to enable integration tests. These are gated with `internal` visibility and documented as test hooks.

---

## Issues / Questions for Dev Lead

- The `AiCoordinator` internal property exposes `AiHotReloadCoordinator` for tests, but the hot-reload integration tests call `manager.OnHotReloadBegin()` / `manager.OnHotReloadCompleted()` directly on the `IDataBreakpointManager` rather than through the coordinator. This is correct for unit-level testing but does not exercise the coordinator event subscription path end-to-end. A follow-up test exercising `_aiCoordinator.DrainPendingCallbacks()` directly would provide higher confidence in the P10T10 wiring.
