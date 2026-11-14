# BF-BATCH-UX1 Report

**Date:** 2026-06-06  
**Branch:** blueprint-integ-1  
**Executor:** claude-sonnet-4-6 (agent)  
**Status:** ALL FOUR FIXES APPLIED — zero new test failures

---

## Summary

All four fixes from `BF-BATCH-UX1-INSTRUCTIONS.md` are implemented and verified.

| Fix | Status | New tests |
|-----|--------|-----------|
| A — Gate auto-reload-on-edit | Done | — (behaviour gate) |
| B — ChannelCommand pin collapse | Done | 2 added |
| C — Selection→Details bridge | Done | 7 added |
| D — Delete GraphEditorWindow stub | Done | 3 removed |

---

## FIX A — Gate Blueprint auto-reload-on-edit

**Files changed (2 lines in EditorSubsystem.cs, nothing else):**

`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

1. Added field after `_blueprintQuickReloadTrigger` (~line 268):
   ```csharp
   // BF-UX1 FIX A: gate auto-reload on edit; defaults false so node moves/edits do NOT trigger
   // a Roslyn compile. The user compiles via the toolbar Quick Reload / Full Rebuild buttons.
   private bool _blueprintAutoReloadOnEdit = false;
   ```

2. Changed the flushAction Blueprint branch (~line 2462-2468):
   ```csharp
   if (asset.Kind == Hrot.Editor.AiShared.AssetKind.Blueprint)
   {
       // BF-UX1 FIX A: only auto-recompile when the opt-in flag is set (default false).
       if (_blueprintAutoReloadOnEdit)
           _blueprintQuickReloadTrigger?.Invoke(asset);
       return;
   }
   ```

**EditorSubsystem footprint:** exactly 2 sites (field declaration + flushAction branch). Zero reformatting, zero other changes.

---

## FIX B — ChannelCommand pin collapse after node edit

**Files changed:**

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs`
- Added `using Hrot.Blueprints.Core.Compiler.Catalogs;`
- Added field: `private readonly IChannelCommandCatalog? _channelCommands;`
- Updated constructor: added `IChannelCommandCatalog? channelCommands = null` optional param + assignment
- Changed `ApplyPinIds` to pass `channelCommands: _channelCommands` to `NodePinSchema.GetCanonicalPins`
- Added `ChannelCommandNode` case in `ApplyInitialProperties` to restore `ChannelType`/`ActionId` from the command's `props` dictionary

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs`
- Updated `BlueprintCommandSink` construction to pass `channelCommands: channelCommands`

**New tests in `Hrot.Blueprints.Tests/Host/BlueprintCommandSinkTests.cs`:**
- `CommandSink_AddChannelCommandNode_RetainsParamPins_WithChannelCatalog` — creates a CCNode via `AddNode` command with `ChannelType`/`ActionId`/`PinIds` props; asserts `node.Pins.Count > 2`
- `CommandSink_ChannelCommandNode_AfterRebuild_RetainsParamPins` — adds CCNode directly to asset then rebuilds model with `channelCommands:`; asserts `modelNode.Pins.Count > 2`

---

## FIX C — Selection→Details per-frame bridge

**Files changed:**

`Hrot/Editor/Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs`  
(minimal addition to sealed class — the only way without subclassing)
- Added `AfterDraw` property: `public Action<AiCanvasContext>? AfterDraw { get; set; }`
- Added invocation at end of `DrawClientArea`: `AfterDraw?.Invoke(context);`

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintSelectionBridgeHelper.cs` **(NEW)**
- Pure static class in `Hrot.Blueprints.Editor.Host`
- `MapSelection(SelectionState, BlueprintAsset?) -> BlueprintNodeSelection?`  
  — returns `null` for count != 1; searches all asset graphs for a node match;  
  — uses `graph.Id` (asset Guid), not the deterministic canvas GraphId ✓
- `BuildAfterDrawAction(AiSelectionStore) -> Action<AiCanvasContext>`  
  — closure that calls `MapSelection` and sets `selectionStore.ActiveSubSelection`
- Alias `using AiSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;`  
  to disambiguate from the local `Hrot.Blueprints.Editor.EditorSelectionStore`

`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`  
(3 lines, after the `blueprintCanvasWindow` registration — minimal footprint)
```csharp
// BF-UX1 FIX C: wire the per-frame selection→Details bridge.
blueprintCanvasWindow.AfterDraw =
    Hrot.Blueprints.Editor.Host.BlueprintSelectionBridgeHelper.BuildAfterDrawAction(
        _blueprintSelectionStore);
```

**EditorSubsystem footprint for FIX C:** 3 lines (comment + 2-line assignment). All logic is in `BlueprintSelectionBridgeHelper`.

**New test file: `Hrot.Blueprints.Tests/Host/BlueprintSelectionBridgeHelperTests.cs` (7 tests):**
- `MapSelection_NullAsset_ReturnsNull`
- `MapSelection_EmptySelection_ReturnsNull`
- `MapSelection_MultiSelect_ReturnsNull`
- `MapSelection_LinkOnlySelection_ReturnsNull`
- `MapSelection_SingleNode_ReturnsCorrectSelection` (happy path; asserts `GraphId == graph.Id` from asset)
- `MapSelection_NodeNotInAsset_ReturnsNull`
- `MapSelection_NodeInSecondGraph_ReturnsCorrectGraphId`

---

## FIX D — Delete GraphEditorWindow stub

**Verification:** Confirmed `GraphEditorWindow.cs` was a pure stub (`DrawUI` body = `ImGui.TextDisabled($"Graph: {CurrentAsset!.Name}")`). The real canvas is `AiGraphCanvasWindow` (sealed, kind = `"Blueprint"`). `BlueprintWindowRegistrar` was already retired from `EditorSubsystem` per `// AIE-015` comment — the canvas window is wired directly there. Safe to delete.

**Files changed:**

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs` — **DELETED**

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintWindowRegistrar.cs`
- Removed `using Hrot.Blueprints.Editor.Reload;`
- Removed `_quickReloadService`, `_fullRebuildService` fields and ctor params
- Removed `"Graph Editor"` registration
- 6 windows remain: Asset Browser, Inspector, Debug Panel, Watch Panel, Callstack, Hot Reload Log

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorWindowTests.cs`
- Removed all three `GraphEditorWindow_*` tests + `MakeQuickReload()` helper + `StubCatalog`
- Removed unused usings (`Fdp.Toolkit.Behavior`, `Fdp.Toolkit.Blueprints`, `Hrot.Blueprints.Core.Compiler`, `Hrot.Blueprints.Editor.Reload`)
- Left: `InspectorWindow_Constructor_SetsTitle`, `PreferencesWindow_*` tests + FIX D tombstone comment

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintWindowRegistrarTests.cs`
- Removed `qrs`, `frs` from `MakeRegistrar()` (7-param → 7-param with different set)
- Updated `expected` window arrays to 6 entries (no "Graph Editor")
- Renamed engine-path test to `BlueprintWindowRegistrar_RegistersAllWindows_ViaEngineInterface`

---

## Test Results (real, non-regen, `BLUEPRINT_REGENERATE_SNAPSHOTS` NOT set)

```
Total:   1462
Passed:  1450
Failed:      4
Skipped:     8
```

**Failing tests (all pre-existing, confirmed by stash-and-rerun on base):**

| Test | Pre-existing? |
|------|---------------|
| `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | Yes — logic bug unrelated to this batch |
| `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` | Yes — allocator sensitivity pre-existing |
| `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` | Yes — snapshot divergence; this batch makes no emit changes |
| `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` | Yes — snapshot divergence; this batch makes no emit changes |

**Zero new failures introduced.**

The two snapshot failures (`Library_EmitMatchesGoldenSource`, `LibraryMath_GeneratedSource_Snapshot`) are pre-existing; verified by stashing this batch's changes and re-running — both failed identically on the base commit.

---

## Deviations

None. All four fixes were implemented exactly as specified. No STOPs were necessary:
- FIX C: `AiGraphCanvasWindow` is sealed, so the bridge was wired via the new `AfterDraw` delegate property (minimal addition to the shared assembly) — consistent with the instructions' guidance.
- FIX D: `BlueprintWindowRegistrar` is not called by `EditorSubsystem` (per AIE-015 comment), so the perspective is unaffected by its cleanup.

---

## Live ImGui Behavior — Requires Running-Editor Verification

The following behaviors cannot be confirmed by unit tests alone and require the running editor:

1. **FIX A:** Moving/editing a node in the Blueprint canvas must NOT trigger a Roslyn recompile. The toolbar Quick Reload / Full Rebuild buttons should still work normally.
2. **FIX B:** Editing a `ChannelCommandNode`'s properties (ChannelType / ActionId) and then undoing/redoing must retain the parameter data-IN pins — no collapse to exec-only pins.
3. **FIX C:** Clicking a single node in the Blueprint canvas must update the Details panel (BlueprintDetailsWindow) to show the corresponding node drawer. Clicking empty space / multiple nodes must clear the Details view.
