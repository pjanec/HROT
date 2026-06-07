# BATCH-09 Report

## Implementation Summary

### AIE-033 — Canvas runtime overlays + breakpoint toggles

**What was built:**

1. **`IsActive` property on `BTreeRuntimeOverlayRenderer` and `HsmRuntimeOverlayRenderer`:** Both renderers now override the `ICustomCanvasRenderer.IsActive` default (was `true`) to return `_session != null`. When no debug session is attached (authoring mode), the canvas skips the renderer entirely — zero per-frame cost.

2. **`BTreeDocumentFactory` updated:**
   - New `btreeDebugSession: IBTreeDebugSession?` parameter (separate from the existing `IDebugSession?` NodeEdit parameter).
   - New `breakpointManager: IDataBreakpointManager?` parameter.
   - `BuildRenderers` now constructs and wires all 6 BTree renderers in documented pass/registration order:
     - `BeforeContent`: `HeatmapOverlayRenderer`, `SubtreeBoundaryRenderer`
     - `AfterWires`: `ObserverGuardBadgeRenderer`, `VariableBindingBadgeRenderer`
     - `AfterNodes`: `BTreeBreakpointGutterRenderer` (session + manager wired), `BTreeRuntimeOverlayRenderer` (session wired, last = most ephemeral)

3. **`HsmDocumentFactory` updated:**
   - New `hsmDebugSession: IHsmDebugSession?` and `breakpointManager: IDataBreakpointManager?` parameters.
   - `BuildRenderers` now constructs and wires all 6 HSM renderers in strict pass/registration order per design-talk §9:
     - `AfterWires`: `HsmTransitionLabelRenderer`
     - `AfterNodes` (strict z-order): `HsmInitialArrowRenderer` → `HsmRegionConflictsRenderer` → `HsmHistoryGlyphsRenderer` → `HsmBreakpointGutterRenderer` → `HsmRuntimeOverlayRenderer`

4. **`BTreeEditorHostServices.ToggleNodeBreakpoint(NodeId, bool)`:** New public method that dispatches `GraphCommand.SetNodeProperty(nodeId, "isBreakpoint", value)` through `_commandSink`. This is the documented command-sink path for breakpoint toggles.

5. **`HsmEditorHostServices.ToggleNodeBreakpoint(NodeId, bool)`:** Same as above for HSM.

6. **`HsmCommandSink.ApplySetNodeProperty`:** Implemented (was a `/* TODO */` stub). Handles `"isBreakpoint"` key by looking up the state or transition by NodeId and setting `IsBreakpoint`.

7. **`EditorSubsystem.RegisterWindows`:** Updated `DocumentOpened` handler to pass `_btreeDebugSession`/`_hsmDebugSession` and `_bpManager` to the respective factories.

**Renderer IDs and registration order (confirmed against code):**

| Renderer | ID | Pass |
|---|---|---|
| `HeatmapOverlayRenderer` | `btree.heatmap_overlay` | `BeforeContent` |
| `SubtreeBoundaryRenderer` | `btree.subtree_boundaries` | `BeforeContent` |
| `ObserverGuardBadgeRenderer` | `btree.observer_guard_badges` | `AfterWires` |
| `VariableBindingBadgeRenderer` | `btree.variable_binding_badges` | `AfterWires` |
| `BTreeBreakpointGutterRenderer` | `btree.breakpoint_gutter` | `AfterNodes` |
| `BTreeRuntimeOverlayRenderer` | `btree.runtime_overlay` | `AfterNodes` |
| `HsmTransitionLabelRenderer` | `hsm.transition_labels` | `AfterWires` |
| `HsmInitialArrowRenderer` | `hsm.initial_state_arrows` | `AfterNodes` |
| `HsmRegionConflictsRenderer` | `hsm.region_conflicts` | `AfterNodes` |
| `HsmHistoryGlyphsRenderer` | `hsm.history_glyphs` | `AfterNodes` |
| `HsmBreakpointGutterRenderer` | `hsm.breakpoint_gutter` | `AfterNodes` |
| `HsmRuntimeOverlayRenderer` | `hsm.runtime_overlay` | `AfterNodes` |

**Breakpoint-toggle command path:**
`hostServices.ToggleNodeBreakpoint(nodeId, true)` → `_commandSink.Apply(new GraphCommand.SetNodeProperty(nodeId, "isBreakpoint", true))` → `BTreeCommandSink.ApplySetNodeProperty` → `node.IsBreakpoint = true`

---

### AIE-034 — Watch / Breakpoints / Diagnostics windows per perspective

**What was built:**

1. **`Hrot.Diagnostics.Breakpoints` reference added to `Hrot.Editor.AiShared.csproj`:** Allows AiShared windows to reference `IDataBreakpointManager`.

2. **`AiBreakpointsWindow`** (`Hrot.Editor.AiShared/Windows/AiBreakpointsWindow.cs`): Per-perspective `ManagedWindow` (`PerspectiveBound`) wrapping `IDataBreakpointManager`. Shows active breakpoint count; exposes `Manager` property for test verification of shared-instance identity.

3. **`AiWatchWindow`** (`Hrot.Editor.AiShared/Windows/AiWatchWindow.cs`): Per-perspective `ManagedWindow` (`PerspectiveBound`) showing `IBreakpoint.IsWatch == true` entries from the same shared manager. Exposes `Manager` for identity verification.

4. **`PerspectiveWorkspaceRegistrar` updated:**
   - New optional `breakpointManager: IDataBreakpointManager?` constructor parameter.
   - When non-null, creates `AiBreakpointsWindow` (`ai_breakpoints_{suffix}`) and `AiWatchWindow` (`ai_watch_{suffix}`) at construction.
   - `RegisterWindows` registers them after the 6 core windows (total = 8 per perspective when manager is supplied).
   - `Breakpoints` and `Watch` properties expose the windows for test verification.

5. **`EditorSubsystem.RegisterWindows`**: Updated to pass `_bpManager` to all three registrars (`_btreeRegistrar`, `_hsmRegistrar`, `_blueprintRegistrar`), wiring the shared `DataBreakpointManager` into per-perspective Watch and Breakpoints windows. Single manager instance, zero duplication.

**How Watch/Breakpoints reuse the shared manager per perspective:**
- The single `DataBreakpointManager` instance (created in `EditorSubsystem.Initialize`) is passed by reference to all three `PerspectiveWorkspaceRegistrar` constructors.
- Each registrar creates one `AiWatchWindow` and one `AiBreakpointsWindow` holding a reference to the same object.
- The global `DataBreakpointManagerWindow` (`"editor_bp_manager"` in `EditorSubsystem.RegisterWindows`) is preserved unchanged.

---

## Design Decisions

1. **Separate `IBTreeDebugSession?` parameter in `BTreeDocumentFactory.Build`:** The existing `IDebugSession?` parameter is the NodeEdit canvas interface; `IBTreeDebugSession` is the richer BTree-specific session. These are two different interface hierarchies that cannot be unified. Adding a separate parameter avoids reflection/casting and preserves type safety.

2. **`ToggleNodeBreakpoint` method on host services (not a context menu):** The batch requires the command-sink path to be testable. The most direct approach is a named method that dispatches `SetNodeProperty`. The canvas can call this from a context menu entry; unit tests call it directly. Avoids any ImGui dependency in the test.

3. **`HsmCommandSink.ApplySetNodeProperty` implemented now:** The TODO stub was blocking the breakpoint toggle. Implemented minimally: handles `"isBreakpoint"`, silently ignores unknown keys (forward-compatible).

4. **`AiBreakpointsWindow`/`AiWatchWindow` in `Hrot.Editor.AiShared`:** They must be in AiShared (not Hrot.Presentation) because `PerspectiveWorkspaceRegistrar` lives there. Added `Hrot.Diagnostics.Breakpoints` project reference to enable `IDataBreakpointManager` usage. No circular dependency introduced.

5. **`PerspectiveWorkspaceRegistrar` with optional `breakpointManager`:** Existing tests use `null` (no manager) and still get 6 windows. Tests with manager get 8. Backward-compatible.

---

## Deviations

| Deviation | What | Why | Risk |
|---|---|---|---|
| Two separate debug session parameters in `BTreeDocumentFactory.Build` | Added `btreeDebugSession: IBTreeDebugSession?` separately from existing `debugSession: IDebugSession?` | `IBTreeDebugSession` and `IDebugSession` are different hierarchies; casting at call site is cleaner | Low: new optional params, existing callers unaffected |
| `HsmCommandSink.ApplySetNodeProperty` implemented | Was a TODO stub | Required for breakpoint toggle test; handles `"isBreakpoint"` only | Low: other keys silently ignored |
| `AiBreakpointsWindow.DrawClientArea` is a minimal banner | Full breakpoint grid deferred | Batch spec says "per-perspective view or expose within perspective"; minimal view satisfies this | Low: window is functional, full grid is additive |

---

## Test Results

### `Hrot.BTree.Editor.Tests` — 371/371 ✅ (was 367)
New tests in `Renderers/BTreeHostServicesRuntimeOverlayTests.cs`:
- `BTreeHostServices_IncludeRuntimeOverlayAndBreakpointRenderers` — verifies ids `btree.runtime_overlay`, `btree.breakpoint_gutter`, `btree.heatmap_overlay`, `btree.subtree_boundaries`, `btree.observer_guard_badges` all present
- `RuntimeOverlay_IsActive_FalseWhenSessionDetached` — `IsActive == false` when no session
- `RuntimeOverlay_IsActive_TrueWhenSessionAttached` — `IsActive == true` with fake session
- `BreakpointToggle_OnNode_DispatchesSetNodePropertyCommand` — real value: `assetNode.IsBreakpoint == true` after toggle ON, `false` after toggle OFF

### `Hrot.Hsm.Editor.Tests` — 323/323 ✅ (was 318)
New tests in `Renderers/HsmHostServicesRuntimeOverlayTests.cs`:
- `HsmHostServices_IncludeExpectedRenderers` — 6 renderer ids present
- `RuntimeOverlay_IsActive_FalseWhenSessionDetached`
- `RuntimeOverlay_IsActive_TrueWhenSessionAttached`
- `BreakpointToggle_OnNode_DispatchesSetNodePropertyCommand` — `StateNode.IsBreakpoint == true/false`
- `HsmRenderers_AfterNodesPass_RegisteredInCorrectOrder` — strict `ContainInOrder` assertion

### `Hrot.Editor.AiShared.Tests` — 702/702 ✅ (was 695)
New tests in `Windows/AiWatchBreakpointsWindowTests.cs`:
- `Perspective_RegistersWatchAndBreakpointsAndDiagnostics_WithOwningPerspective` — all 3 perspectives, correct `OwningPerspective` per kind
- `Perspective_WatchAndBreakpoints_ShareSameManagerInstance` — `Assert.Same(manager, reg.Watch.Manager)` and Breakpoints
- `Perspective_NoManager_WatchAndBreakpointsAreNull_NoThrow` — graceful null path, 6 windows
- `Perspective_WithManager_RegistersEightWindows` — count == 8
- `WatchAndBreakpointsWindowIds_AreDistinctAcrossPerspectives` — 3×8=24 distinct ids
- `Diagnostics_WindowId_HasCorrectSuffix` — id and perspective match
- `WatchAndBreakpoints_WindowIds_ContainPerspectiveSuffix` — ids contain "hsm"

### `NodeEditor.UI.Tests` — 40/40 ✅
No changes; verified unchanged.

### `EditorSubsystemBoot` filter — 10/10 ✅
Full EditorHarness boots with new registrar wiring in place.

### `BreakpointSubsystemWiring` filter — 23/25 ✅
2 failures = pre-existing DEBT-008 (tests 4+5: `CgfSubsystem_Init_RegistersManager`, `CgfSubsystem_HeavyScenario_NoBreakpoints_ZeroOverhead` — require CycloneDDS DDS domain, fail in headless CI with SimHost abort). No new failures introduced.

### `Hrot.Blueprints.Tests` — 889/907, 10 failures ✅
10 pre-existing DEBT-006 snapshot failures. No new failures.

---

## Developer Insights

1. **`IDebugSession` vs `IAiDebugSession` hierarchy split:** NodeEdit's `IDebugSession` (in `NodeEditor.Core.Interfaces`) and AiShared's `IAiDebugSession` (in `Hrot.Editor.AiShared.Debug`) are two completely separate interfaces. BTree/HSM debug sessions implement only `IAiDebugSession`. This is intentional by the original design but means you cannot pass a `BTreeDebugSession` where `IDebugSession?` is expected. The factory signatures now have both params explicitly.

2. **`HsmCommandSink` TODO stubs:** Several command handlers are still stub no-ops. The `ApplySetNodeProperty` stub was the only one blocking AIE-033. The other stubs (`ApplyMoveNodes`, `ApplyAddNode`, etc.) are deferred to AIE-037/038. Nothing was blocked by them in this batch.

3. **`AiBreakpointsWindow.DrawClientArea` calls ImGui:** The spec says "headless-constructible windows". The existing `DiagnosticsWindow.DrawClientArea` pattern also calls ImGui — the constraint is about the constructor, not the draw method. Window constructors have no ImGui calls. Draw methods are only invoked by the `WindowManager` during an active ImGui frame.

4. **`ThreeRegistrars_ShareWindowManager_ProduceDistinctIdSets` test unchanged:** That test uses `MakeRegistrar(perspective)` without a breakpoint manager, so still gets 18 windows (3×6). A new test `WatchAndBreakpointsWindowIds_AreDistinctAcrossPerspectives` covers the 3×8=24 case.

5. **`IDataBreakpointManager.AddBreakpoint` signature discrepancy:** The stub in `StubBreakpointManager` uses the full 5-param `AddBreakpoint` signature. The method existed in the BATCH-08 code as a convenience overload. The interface has two overloads (`Add(Breakpoint)` and `AddBreakpoint(SearchPredicateDto, Entity?, int, string, Guid?)`).

---

## Known Issues

- `AiBreakpointsWindow` and `AiWatchWindow` draw minimal UI (count banner / simple table). A full breakpoint management grid (per-entry enable/disable, remove, condition editing) is deferred — it requires porting `DataBreakpointManagerPanel` into `Hrot.Editor.AiShared` or adding a callback delegate. This is compatible with the batch spec ("per-perspective view or expose within perspective").

- `ToggleNodeBreakpoint` is a push method (caller supplies `value`). The canvas context menu / keyboard shortcut wiring that reads the current `node.IsBreakpoint` and calls `Toggle(!current)` is deferred to the canvas window implementation (out of scope for this batch; the command path is verified).

---

## Suggested Commit Message

```
feat(editor): AIE-033/034 — canvas runtime overlays + per-perspective Watch/Breakpoints windows (BATCH-09)

BTree/HSM document factories now inject all renderer sets (heatmap, subtree,
observer-guard, variable-binding, breakpoint-gutter, runtime-overlay) in the
documented z-order pass sequence; renderers report IsActive==false when session
is detached. ToggleNodeBreakpoint dispatches GraphCommand.SetNodeProperty through
the command sink. HsmCommandSink.ApplySetNodeProperty implemented. Per-perspective
AiWatchWindow + AiBreakpointsWindow registered via PerspectiveWorkspaceRegistrar,
sharing the single DataBreakpointManager instance.

Tests: BTree 371, HSM 323, AiShared 702, NodeEditor.UI 40, EditorSubsystemBoot 10/10,
BreakpointSubsystemWiring 23/25 (2 pre-existing DEBT-008), Blueprints 889/10 (DEBT-006).
```
