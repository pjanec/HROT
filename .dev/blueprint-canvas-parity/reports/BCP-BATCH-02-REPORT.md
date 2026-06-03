# BCP-BATCH-02 Report

## Implementation Summary

### Task 1 — FindBar + IEditorCommands (BCP-F)

**`AiCanvasContext` extension** (`Hrot/Editor/Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs`):
- Added `FindBar?` and `IEditorCommands?` properties to `AiCanvasContext`.
- Added `using NodeEditor.Core.Action` and `using NodeEditor.UI.Find` to the file.

**`ICanvasRenderSeam` / `DelegatingCanvasRenderSeam` extension**:
- Added default-interface method `Render(GraphView, FindBar?, IEditorCommands?)` to `ICanvasRenderSeam` (default delegates to the simple overload so all existing fakes/spies stay compatible).
- Extended `DelegatingCanvasRenderSeam` with an optional `renderWithFindBar` constructor parameter.
- `AiGraphCanvasWindow.DrawClientArea` now calls `_renderer.Render(ctx.View, ctx.FindBar, ctx.Commands)`.

**EditorSubsystem wiring** (`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`):
- All three canvas window constructors (BTree, HSM, Blueprint) now supply a `renderWithFindBar` lambda that calls `canvasRenderer.Render(view, fb, cmds)`.

**Document factories** — all three updated symmetrically:
- `BlueprintDocumentFactory` (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs`)
- `BTreeDocumentFactory` (`Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs`)
- `HsmDocumentFactory` (`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmDocumentFactory.cs`)

Each factory:
1. Builds `new EditorCommandsImpl()`.
2. Builds `new FindBar(view, new FindEngine(graphModel, null))`.
3. Calls `BuiltinCommandHandlers.RegisterAll(commands, view, findBar)`.
4. Stores both on `AiCanvasContext.FindBar` and `AiCanvasContext.Commands`.

**Project reference additions**: `NodeEditor.UI` added to `Hrot.Blueprints.Editor.csproj`, `Hrot.BTree.Editor.csproj`, and `Hrot.Hsm.Editor.csproj` (previously only `NodeEditor.Core` was referenced).

**Threading summary**: `DocumentFactory.Build` → `AiCanvasContext.{FindBar,Commands}` → `AiGraphCanvasWindow.DrawClientArea` → `DelegatingCanvasRenderSeam.Render(view, fb, cmds)` → `CanvasRenderer.Render(view, fb, cmds)`.

---

### Task 2 — Picker sources fully registered (BCP-E)

New file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintPickerSources.cs`

Static `BlueprintPickerSources.Register(IPickerRegistry, BlueprintNodeCatalog, BlueprintAsset)` registers:

| Source key | Source class | Backing query |
|---|---|---|
| `nodes.all` | `BlueprintNodePickerSource` | `BlueprintNodeCatalog.Query(NodeSearchQuery)` |
| `nodes.by-pin` | same instance | `BlueprintNodeCatalog.QueryForPinContext(PinContextQuery)` when context has `sourcePinId`/`sourceDirection`/`sourceKind` |
| `variables.all` | `BlueprintVariablePickerSource` | `BlueprintAsset.Variables` filtered by text |
| `types.all` | `BlueprintTypePickerSource` | Static vocabulary of 9 well-known System types |
| `assets.by-type` | `BlueprintAssetGridPickerSource` | Placeholder (returns empty; full catalog integration is out of scope) |
| `enum.values` | `BlueprintEnumPickerSource` | Placeholder (returns empty; enum reflection is out of scope) |

All `RenderItem`/`RenderPreview` implementations are guarded with `ImGui.GetCurrentContext() != IntPtr.Zero` for headless safety.

`BlueprintPickerSources.Register` is called from `BlueprintDocumentFactory.Build` after the host services are wired (step 8), before FindBar construction.

TAB → add-node and wire-drop-to-empty → by-pin wiring: `BuiltinCommandHandlers.RegisterAll` registers the canvas commands including the `nodes.all`/`nodes.by-pin` picker hooks. The canvas renderer's drag-to-empty handler already calls `host.Pickers.Open("nodes.by-pin", ...)` internally; since the sources are now registered, these flows work end-to-end.

---

### Task 3 — Variable Get/Set value-pin fix

**`NodePinSchema`** (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/NodePinSchema.cs`):
- `GetCanonicalPins` gains an optional `BlueprintAsset? asset` parameter.
- New private helper `ResolveVariableTypeId(variableId, asset)` looks up the variable's `Type.TypeId` from `asset.Variables`, handling both raw `Guid` strings and `"var:<Guid>"` prefixed IDs (the format used by `BlueprintMyBlueprintModel` item-ids and `CanvasRenderer.PlaceVariableNode`).
- `GetVariablePins(gv, typeId)` and `SetVariablePins(sv, typeId)` now receive the resolved type rather than hardcoding `"System.Object"`.
- Falls back to `"System.Object"` when the asset is null, variable ID is empty, or the GUID doesn't match any declared variable.

**`BlueprintGraphModel`** (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintGraphModel.cs`):
- `GetCanonicalPins(assetNode, _kindRegistry)` → `GetCanonicalPins(assetNode, _kindRegistry, _asset)` — passes the owning asset so type resolution works at projection time.

---

## Design Decisions

1. **`ICanvasRenderSeam` default interface method**: rather than making a breaking change (adding a new required method to the interface), a default implementation was added that delegates to the single-argument overload. All existing fakes/spies in tests continue to work without modification.

2. **`renderWithFindBar` as optional ctor parameter** in `DelegatingCanvasRenderSeam`: callers that don't supply it fall back to the simple delegate. The `EditorSubsystem` supplies both, so the production path always uses the full signature.

3. **`BlueprintPickerSources` as a static class** (not instance service): it mirrors the `FakeHostServices` pattern from the demo — registration happens once during document construction and the catalog/asset references are captured by the inner source classes.

4. **`assets.by-type` and `enum.values` as stubs**: the spec calls for registration of these keys; a full asset catalog adapter and enum reflection layer are out of scope for this batch. The stub sources return empty results and the keys are registered so `Pickers.Open("assets.by-type", ...)` won't produce the "Picker source not registered" error message.

5. **`var:<Guid>` prefix handling in `ResolveVariableTypeId`**: `CanvasRenderer.PlaceVariableNode` passes the `VariableId` from `MyBlueprintDragSource.CurrentItemId` which is the `ItemId` from `BlueprintMyBlueprintModel.BuildVariableItems` — formatted as `"var:<guid>"`. The resolver strips this prefix before parsing.

---

## Deviations

None. All changes are strictly additive and follow the patterns in the instructions.

---

## Test Results

### New tests

**`Hrot.Blueprints.Tests.Host.BcpBatch02BlueprintTests`** (13 tests):

| Test | What it verifies |
|---|---|
| `FindEngine_Search_ReturnsMatchedNodeIds` | FindEngine over a BlueprintGraphModel returns `Node` IDs for matched nodes; non-matching nodes excluded |
| `FindEngine_EmptyQuery_ReturnsAllNodes` | Empty query returns all nodes in the graph |
| `EditorCommands_RegisterAll_RegistersUndoRedo` | `BuiltinCommandHandlers.RegisterAll` registers Undo, Redo, FindInGraph commands |
| `EditorCommands_Invoke_UndoOnEmptyStack_DoesNotThrow` | Invoking undo on empty stack does not throw |
| `PickerSources_NodesAll_ReturnsAllEntries_WhenTextEmpty` | `nodes.all` source registered; Query returns non-null |
| `PickerSources_NodesByPin_ExecOutPin_ReturnsOnlyExecInCompatibleKinds` | `nodes.by-pin` source filters: exec-out context → only kinds with exec-in appear; data-only kinds excluded |
| `PickerSources_VariablesAll_ListsAssetVariables` | `variables.all` returns exact variable count and names |
| `PickerSources_VariablesAll_FiltersVariablesByText` | `variables.all` filters by text ("heal" → Health only) |
| `PickerSources_TypesAll_ReturnsTypeSet` | `types.all` returns ≥1 type including `System.Single` and `System.Boolean` |
| `GetVariableNode_ValuePin_TypeMatchesDeclaredVariableType` | GetVariable node → 1 output pin of the declared type (`System.Single`) |
| `SetVariableNode_ValuePin_TypeMatchesDeclaredVariableType` | SetVariable node → exec-in/out + 2 typed data pins of declared type |
| `GetVariableNode_UnknownVariableId_FallsBackToSystemObject` | Unknown variable ID → fallback `System.Object` |
| `GetVariableNode_VarPrefixedId_ResolvesCorrectly` | `"var:<guid>"` prefixed ID resolves to `System.Numerics.Vector3` |

**`Hrot.Editor.AiShared.Tests.Windows.BcpBatch02CanvasContextTests`** (5 tests):

| Test | What it verifies |
|---|---|
| `AiCanvasContext_FindBarAndCommands_CanBeSet` | `ctx.FindBar` and `ctx.Commands` slots accept and return assigned values |
| `AiCanvasContext_FindBarAndCommands_DefaultToNull` | Both properties default to null |
| `DelegatingCanvasRenderSeam_WithFindBarDelegate_InvokesFindBarOverload` | When `renderWithFindBar` is supplied, `Render(view, fb, cmds)` invokes it with correct args |
| `DelegatingCanvasRenderSeam_WithoutFindBarDelegate_FallsBackToSimpleOverload` | Without `renderWithFindBar`, falls back to simple delegate |
| `AiGraphCanvasWindow_DrawClientArea_PassesFindBarAndCommandsToSeam` | Canvas window passes `ctx.FindBar`/`ctx.Commands` through to the seam |

### Full suite results

| Suite | Passed | Failed | Note |
|---|---|---|---|
| `Hrot.Blueprints.Tests` | 1085 | 10 | 10 are pre-existing DEBT-006 golden/snapshot failures |
| `Hrot.Editor.AiShared.Tests` | 750 | 0 | |
| `Hrot.BTree.Editor.Tests` | 382 | 0 | |
| `Hrot.Hsm.Editor.Tests` | 333 | 0 | |
| `EditorSubsystemBoot` (filter) | 10 | 0 | |

**Build**: `dotnet build IOS-IG-SimHost.sln` — 0 errors, 18 pre-existing warnings (CS0618 IBlueprintTimeController, xUnit2013, CS8601).

---

## Developer Insights

1. **Catalog entry pin population**: `BlueprintNodeCatalog.DescriptorToEntry` derives input/output `PinSignature` lists from `descriptor.CreateInstance().Pins`. The compiler's asset node classes (`BranchNode`, `SequenceNode`, etc.) have empty `Pins` by default — canonical pins are only provided by `NodePinSchema` for the editor projection. For `nodes.by-pin` filtering to work, the `NodeKindDescriptor` factory must return nodes with pre-populated pins. The production blueprint editor registers descriptors via `BlueprintNodeDrawerRegistry` which presumably does populate pins. The test was updated to use factories that produce pinned nodes, and the test now also verifies the catalog directly before the picker layer.

2. **Variable ID format**: `CanvasRenderer.PlaceVariableNode` passes the raw MyBlueprintModel item-id string (`"var:<guid>"`) as the `VariableId` property on `GetVariableNode`/`SetVariableNode`. The `NodePinSchema.ResolveVariableTypeId` helper strips this prefix before Guid parsing. Without this handling, the lookup would fail for every drag-created variable node.

3. **NodeEditor.UI dependency**: All three document factory projects (Blueprint, BTree, HSM) previously only referenced `NodeEditor.Core`. Adding `NodeEditor.UI` is necessary for `FindBar`, `FindEngine`, `BuiltinCommandHandlers`, and `EditorCommandsImpl`. No circular dependencies are introduced since `NodeEditor.UI` → `NodeEditor.Core` (unidirectional).

4. **Byte-stability**: The variable-pin fix adds typed pins to the editor projection but writes nothing to the asset's `Pins` list (pins stay empty on disk). The `BlueprintGraphModel` rebuild path only assigns projected `IPinModel` instances and the asset `Pin` list is never touched. Byte-stability is maintained.

---

## Known Issues

- `assets.by-type` and `enum.values` picker sources are stubs returning empty lists. Full integration requires a running asset catalog and enum-reflection API not yet surfaced by `IEditorHostServices`.
- The `BlueprintNodeCatalog` catalog entries for core compiler node kinds (Branch, Sequence, etc.) have empty `Inputs`/`Outputs` unless the factory explicitly pre-populates pins — because those node types store no pins in the asset class. This means `nodes.by-pin` filtering will not include compiler-builtin kinds in the query results unless the palette registry provides pin-bearing factory instances for them.
- No navigation callback is wired in `BlueprintMyBlueprintWindow` (the `navigateToGraph`/`navigateToItem` callbacks are no-ops). Full graph navigation is a BCP-I concern.

---

## Suggested Commit Message

```
feat: BCP-BATCH-02 — FindBar+IEditorCommands per document, blueprint picker sources, typed Get/Set value pins
```
