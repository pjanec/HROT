# BATCH-13 Report

## Implementation Summary

### Task 1 — AIE-047: `BlueprintMyBlueprintModel` + `MyBlueprintPanel` registration

**New files:**
- `Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintModel.cs` — Implements `IMyBlueprintModel` projecting the active `BlueprintAsset`.
- `Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintWindow.cs` — `ManagedWindow` wrapper hosting NodeEdit's `MyBlueprintPanel`.

**`BlueprintMyBlueprintModel` details:**
- Fixed 6-section order: Graphs(0), Functions(1), Macros(2), Custom Events(3), Variables(4), Event Dispatchers(5).
- **Real sections**: `Graphs` (from `BlueprintAsset.Graphs`), `CustomEvents`, `Variables` (with name/type/category/accent color from `BlueprintTypeSystem` palette), `EventDispatchers`.
- **Faked/empty v1**: Functions + Macros always return `Array.Empty<MyBlueprintItem>()`. The section descriptors are still listed (per D.6.2 spec) so the panel renders them as empty.
- `Retarget(IEditableAsset?, BlueprintAsset?)` subscribes/unsubscribes from `IEditableAsset.Changed` and fires `Changed` on retarget.
- Variable accent color uses a new `BlueprintTypeSystem.GetAccentColorForTypeId(string)` static helper (reuses the existing `_types` palette — one place to update, no duplication).

**Registration:** `EditorSubsystem.RegisterWindows` creates `BlueprintMyBlueprintWindow` and registers it via `_blueprintRegistrar.RegisterExtraWindow` → id `"ai_my_blueprint_blueprint"`, perspective `"Blueprint"`, scope `PerspectiveBound`.

**Retarget wiring:** In `_aiDocumentManager.ActiveChanged`, when `active.Kind == Blueprint`, the code extracts `BlueprintAsset` from `AiCanvasContext.AssetRef` (a new nullable property added to `AiCanvasContext`), then calls `_blueprintMyBlueprintWindow.Retarget(asset, bpAsset, context.View.Host, new EditorCommandsImpl())`. On non-Blueprint activation, the window is retargeted with nulls.

### Task 2 — AIE-048: Blueprint Details + Variables windows

**New files:**
- `Hrot.Blueprints.Editor/Windows/BlueprintDetailsWindow.cs` — `ManagedWindow` that resolves a `BlueprintNodeDrawerRegistry` drawer for the selected node.
- `Hrot.Blueprints.Editor/Windows/BlueprintVariablesManagedWindow.cs` — `ManagedWindow` wrapper around the existing `BlueprintVariablesWindow`.

**`BlueprintDetailsWindow` details:**
- Takes the AiShared `EditorSelectionStore` and `BlueprintNodeDrawerRegistry`.
- `ResolveSession()` — headless-testable projection method:
  1. Reads `ActiveSubSelection` as `BlueprintNodeSelection`.
  2. Finds the matching `Graph` and `Node` in the asset.
  3. Calls `BlueprintNodeDrawerRegistry.GetDrawerFor(node)` — first exact-type match, then `Handles()` scan.
  4. Creates (or returns cached) `INodeEditSession`. Caches by `(GraphId, NodeId)` pair.
  5. Exposes `ResolvedDrawerKind` (the `Type` of the matched drawer) for test assertions.
- `DrawClientArea()` calls `ResolveSession()` then `session.Draw()`.
- `Retarget(BlueprintAsset?)` clears the cached session and disposes it.

**`BlueprintVariablesManagedWindow`:** Wraps `BlueprintVariablesWindow` (which uses the legacy `Hrot.Blueprints.Editor.EditorSelectionStore`) in a new `ManagedWindow`. The composition root drives the legacy store via `_blueprintLegacySelectionStore.SelectAsset(bpAsset)` in `ActiveChanged`.

**Registration:** Both windows registered via `_blueprintRegistrar.RegisterExtraWindow` →
- `"ai_details_blueprint"` / `"Blueprint"` / `PerspectiveBound`
- `"ai_variables_blueprint"` / `"Blueprint"` / `PerspectiveBound`

**Supporting changes:**
- `AiCanvasContext.AssetRef` (new nullable `object?` property) — set by `BlueprintDocumentFactory.Build` to the `BlueprintAsset`. Lets the composition root retrieve the asset without introducing a Blueprint dependency into AiShared.
- `BlueprintTypeSystem.GetAccentColorForTypeId(string)` — new static helper for accent color lookup.

## Design Decisions

1. **`AiCanvasContext.AssetRef: object?`** — an opaque slot avoids adding `Hrot.Blueprints.Core` as a dependency to `Hrot.Editor.AiShared`. The composition root (`EditorSubsystem`) already references both, so it can safely cast `context.AssetRef as BlueprintAsset`.

2. **Functions/Macros faked via empty `Array.Empty<>`, sections still listed** — per D.6.2 and the BATCH-13 spec: the panel must show these section headers in fixed order even when empty, so users see the complete outline structure.

3. **Legacy `EditorSelectionStore` bridge** — `BlueprintVariablesWindow` was built against the legacy store (holds `BlueprintAsset?`) and was designed before the AiShared perspective infrastructure. Rather than rewriting it, a thin bridge field `_blueprintLegacySelectionStore` is driven from `ActiveChanged`. This avoids modifying a working, tested class.

4. **`ResolveSession()` as testable extraction** — the session-resolution logic (selection → node lookup → drawer dispatch → session creation) is extracted from `DrawClientArea()` into a public method so tests can call it without an ImGui context.

5. **`EditorCommandsImpl()` per retarget** — the My Blueprint panel's `IEditorCommands` is used for create-item commands (not yet wired in v1). A fresh `EditorCommandsImpl()` is passed; it's cheap to create and correct for the lifecycle.

## Deviations

1. **Added `AiCanvasContext.AssetRef`** — not in the original spec. Alternative was casting `context.View.Model` to `BlueprintGraphModel` and calling a `GetAsset()` accessor; the opaque-ref approach is simpler and avoids adding a new method to `BlueprintGraphModel`. Risk: low (additive, backward-compatible, clearly documented).

2. **`BlueprintVariablesManagedWindow` wrapper instead of modifying `BlueprintVariablesWindow`** — spec says "reuse existing `BlueprintVariablesWindow`". The wrapper satisfies the reuse requirement without altering the existing class. Risk: none.

3. **`BlueprintTypeSystem.GetAccentColorForTypeId` static helper** — the model needs per-type colors but shouldn't construct a `BlueprintTypeSystem` instance (which requires `IPinDefaultValueEditorRegistry`). A static helper reading the same `_types` dictionary was the cleanest solution. Benefit: single source of truth for colors. Risk: none.

## Test Results

### Hrot.Blueprints.Tests
```
Failed:   10  (all pre-existing DEBT-006: golden emit tests × 7, allocation-free, condition-summary, library snapshot × 2)
Passed: 1027
Skipped:   8
Total:  1045
```

**New tests added: 19**
- `BlueprintMyBlueprintModelTests` (8 tests):
  - `Sections_FixedOrder` — asserts 6 sections in exact order (Graphs/Functions/Macros/CustomEvents/Variables/Dispatchers), SortOrder matches index.
  - `Variables_ProjectAssetVariables` — asserts name="Health"/type accent matches `BlueprintTypeSystem.Single` palette, CategoryPath="Info" for second var.
  - `Graphs_ProjectAssetGraphs` — asserts count=2, names, IsHostDefined=true, IsDeletable=false.
  - `CustomEvents_AndDispatchers_Projected` — asserts evt names + dispatcher name.
  - `FakedSections_ReturnEmpty_NoThrow` — Functions+Macros always empty.
  - `FiresChanged_OnAssetMutation` — Changed fires when `FakeEditableAsset.FireChanged()` called.
  - `Retarget_ToNull_FiresChangedAndReturnsEmpty` — clearing returns empty lists.
  - `Retarget_UnsubscribesOldAsset` / `Sections_SameInstanceAlways` — unsubscription and stable sections ref.
- `BlueprintDetailsWindowTests` (7 tests):
  - `SelectedNode_ResolvesWhenNodeDrawer` — asserts `ResolvedDrawerKind == typeof(StubDrawer<WhenNode>)`.
  - `SelectedNode_ResolvesReadEqsDrawer` — `StubDrawer<ReadEqsResultNode>`.
  - `SelectedNode_ResolvesSpawnEqsDrawer` — `StubDrawer<SpawnEqsSensorNode>`.
  - `UnregisteredNodeType_ReturnsNullSession` — `FunctionCallNode` → null session.
  - `NoSelection_ReturnsNullSession`, `Retarget_ClearsSession`, `SameSelection_ReturnsCachedSession`.
- `EditorSubsystemBlueprintWindowsTests` (3 new tests):
  - `RegistersMyBlueprintWindow_ForBlueprint` — checks id + perspective + scope.
  - `RegistersDetailsWindow_ForBlueprint` — checks id + perspective + scope.
  - `RegistersVariablesWindow_ForBlueprint` — checks id + perspective + scope.

### Hrot.Editor.AiShared.Tests
```
Passed: 702, Failed: 0
```

### EditorSubsystemBoot filter (Hrot.ClusterRunner.Integration.Tests)
```
Passed: 10, Failed: 0
```

### Full solution build
```
dotnet build IOS-IG-SimHost.sln → Build succeeded, 0 errors
```

## Developer Insights

1. **`_blueprintNodeDrawers` null guard** — `EditorSubsystemBlueprintWindowsTests` calls `RegisterWindows` without `Initialize()`, so `_blueprintNodeDrawers` was null. Added `?? new BlueprintNodeDrawerRegistry()` fallback. This is the same defensive pattern used elsewhere in the codebase.

2. **Namespace collision** — `Hrot.Blueprints.Editor.EditorSelectionStore` (legacy) and `Hrot.Editor.AiShared.Selection.EditorSelectionStore` (new) have identical short names. The `BlueprintDetailsWindow.cs` file required an alias (`using AiSelectionStore = ...`) to avoid the ambiguity.

3. **`IEditableAsset` namespace** — the file is under `Identity/` folder but the namespace is `Hrot.Editor.AiShared` (root). This is a known slight mismatch; using `Hrot.Editor.AiShared` (not `Hrot.Editor.AiShared.Identity`) is correct.

4. **`MyBlueprintPanel` needs `IEditorHostServices`** — the panel uses `host.Icons` and `host.Theme` for rendering. Providing these requires the `AiCanvasContext.View.Host` to be available, which means the window can only be fully active after a document is opened. Before that, `DrawClientArea` shows the disabled text "No blueprint open." This is the correct UX.

5. **Weak point**: `BlueprintVariablesWindow` depends on the legacy `DirtyTracker` for marking assets dirty. In the new architecture, dirty tracking goes through `BlueprintFileAsset.MarkDirty()` → `IEditableAsset.Changed` → `RegenerationScheduler`. The `DirtyTracker` in the variables window is a disconnected instance that only affects the legacy path. This could be improved in a future cleanup task, but it doesn't break correctness because the variables window was part of the legacy pipeline.

## Known Issues

None blocking. The `BlueprintVariablesWindow`'s `DirtyTracker` is disconnected from the main dirty-tracking pipeline (see Developer Insights #5) but this is a pre-existing limitation of the legacy window, not a new regression.

## Suggested Commit Message

```
feat(editor): BlueprintMyBlueprintModel + panel + Details + Variables windows (BATCH-13, AIE-047/048)

AIE-047: BlueprintMyBlueprintModel projects Variables/Graphs/CustomEvents/EventDispatchers
(real) + Functions/Macros (faked/empty v1); fixed 6-section order per D.6.2; fires Changed
on asset mutation; registered as BlueprintMyBlueprintWindow in Blueprint perspective.

AIE-048: BlueprintDetailsWindow resolves selected node's IBlueprintNodeDrawer via
BlueprintNodeDrawerRegistry + creates/caches INodeEditSession; BlueprintVariablesManagedWindow
wraps existing BlueprintVariablesWindow. Both registered in Blueprint perspective.

AiCanvasContext.AssetRef (object?) added so composition root retrieves BlueprintAsset without
coupling AiShared to Blueprints.Editor. Phase 4 / M-Blueprint complete.

Build: 0 errors. Tests: Blueprints 1027/10 (DEBT-006), AiShared 702/0, EditorSubsystemBoot 10/10; 19 new.
```
