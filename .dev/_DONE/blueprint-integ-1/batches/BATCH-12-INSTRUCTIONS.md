# BATCH-12: Blueprint command sink + host services + canvas binding + real IEditService
**Tasks:** AIE-044, AIE-045, AIE-046, AIE-049   **Phase:** 4   **Est:** ~12h
**Dependencies:** BATCH-11 (Blueprint host adapters); BATCH-05 (canvas + document-factory pattern); BATCH-06 (real picker registry).

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/blueprint-integ-1/DESIGN.md` §2 (canvas contract), §5.5; `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-044, AIE-045, AIE-046, AIE-049.
3. `.dev/blueprint-integ-1/reviews/BATCH-11-REVIEW.md`.
4. **Templates:** NodeEdit `FakeBlueprint` (`FakeCommandSink`, `FakeHostServices`) + the existing `BTreeDocumentFactory`/`HsmDocumentFactory` (BATCH-05) and `BTreeEditorHostServices`/`HsmEditorHostServices` ctors.

Use **codebase-memory MCP** first; not `search_code`. **Keep `GizmoMap.Contracts` on CycloneDDS 0.2.2** (user decision — do not change package versions). Headless tests must not call ImGui without a context.

## Ground truth (verify before coding)
- BATCH-11 adapters in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/`: `BlueprintGraphModel`, `BlueprintTypeSystem`, `BlueprintLinkValidator`, `BlueprintNodeCatalog` (+ node/pin/link models). Reuse these.
- Existing Blueprint editor mutation pieces: `Hrot.Blueprints.Editor/GraphEditor/GraphCommands.cs` (`AddNodeCommand`/`DeleteNodeCommand`), `CommandHistory.cs`, `GraphEditor/SelectionState.cs`; node drawers (`WhenNodeDrawer`, `PlayMontageChainNodeDrawer`, `ReadEqsResultNodeDrawer`, `SpawnEqsSensorNodeDrawer`), attachment providers, custom renderers (`WhenFiringPulseRenderer`), `BlueprintEditorTheme`. `IEditService` is currently `EditorSubsystem.NoOpEditService` (Blueprint property-edit dispatcher).
- `AiEditorAdapterBundle` (BATCH-01) supplies `Pickers/Clipboard/Icons/Diagnostics/Input/Theme`.
- NodeEdit `IGraphCommandSink` command set (`GraphCommand.*`), `IEditorHostServices` (verify the full member list incl. `AttachmentContextMenu`/`CustomCanvasRenderers`). `AiGraphCanvasWindow` + `PerspectiveWorkspaceRegistrar.RegisterExtraWindow` seam (BATCH-03/05), `AiDocumentManager.DocumentOpened` (BATCH-05/07).

## Tasks (in order)

### Task 1: Real IEditService (AIE-049) — file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/EditService.cs` (NEW) + remove NoOp usage
Replace `NoOpEditService` with a real `IEditService` that records property edits as **undoable commands** on the Blueprint `CommandHistory` and marks the asset dirty. Wire it in `EditorSubsystem` in place of `NoOpEditService`.
**Tests (`Hrot.Blueprints.Tests`):** `EditService_MarkDirty_FlagsAsset`; `EditService_PropertyEdit_PushesUndoableCommand`; `EditService_Undo_RevertsPropertyEdit`.

### Task 2: BlueprintCommandSink (AIE-044) — file: `.../Host/BlueprintCommandSink.cs` (NEW)
`IGraphCommandSink` applying NodeEdit `GraphCommand`s to the active `BlueprintAsset` graph: `AddNode` (resolve `NodeKindKey` via `NodeKindRegistry`/`BlueprintNodeCatalog`, mint node + pins), `RemoveNodes`, `AddLink`/`RemoveLinks` (data-flow links on `Graph.Links`, respecting `BlueprintLinkValidator` single-data-input replacement), `MoveNodes` (EditorMetadata X/Y), `SetNodeProperty`. Route structural ops through the existing `GraphCommands`/`CommandHistory` where they exist (reuse, don't duplicate); property edits via the real `IEditService`. Mark asset dirty + raise `BlueprintGraphModel.Changed`/rebuild after each. Template: `FakeCommandSink`.
**Tests:** `CommandSink_AddNode_AddsToAssetGraph` (+pins); `_RemoveNodes_Removes`; `_AddLink_ConnectsPins_OnGraphLinks`; `_AddLink_SingleDataInput_ReplacesExisting`; `_MoveNodes_UpdatesPositions`; `_SetProperty_UpdatesNode`; `_MarksDirty_AfterMutation`; `_Batch_AppliesAllOrStopsOnFailure`. Assert real model state after each command.

### Task 3: BlueprintEditorHostServices (AIE-045) — file: `.../Host/BlueprintEditorHostServices.cs` (NEW)
`IEditorHostServices` bundling the BATCH-11 catalog/type-system/validator + the new command sink + the `AiEditorAdapterBundle` adapters (pickers/clipboard/icons/diagnostics/input/theme) + the existing Blueprint custom renderers (e.g. `WhenFiringPulseRenderer`) via `CustomCanvasRenderers`, and the attachment context-menu/providers where the interface supports them. Mirror `BTreeEditorHostServices`/`HsmEditorHostServices` ctor shape + `FakeHostServices`.
**Tests:** `BlueprintEditorHostServices_FullSurface_NonNull`; `_GraphView_Constructs` (`new GraphView(model, host.CommandSink, host.Validator, host.TypeSystem, host.NodeCatalog, host)` succeeds and exposes the projected nodes/links); `_CustomRenderers_IncludeBlueprintRenderers`.

### Task 4: Blueprint canvas binding (AIE-046) — file: `.../Host/BlueprintDocumentFactory.cs` (NEW) + EditorSubsystem wire-up
Mirror `BTreeDocumentFactory`/`HsmDocumentFactory`: given a Blueprint `IEditableAsset`, build `BlueprintGraphModel` + `BlueprintEditorHostServices` + `GraphView`, return the `AiCanvasContext`. In `EditorSubsystem.RegisterWindows`, register an `AiGraphCanvasWindow` into the **Blueprint** `PerspectiveWorkspaceRegistrar` via the extension seam, and route `AiDocumentManager.DocumentOpened` for `AssetKind.Blueprint` to this factory. Keep `EditorSubsystemBoot` green.
**Tests:** `BlueprintDocumentFactory_Build_ProducesHostServices_AndGraphView`; an integration-style test that opening a Blueprint asset (e.g. via the contributor + factory) yields a renderable `AiCanvasContext` with projected nodes.

## Success Criteria
- [ ] AIE-044/045/046/049 per success conditions.
- [ ] `dotnet build IOS-IG-SimHost.sln` 0 errors (GizmoMap.Contracts stays on 0.2.2).
- [ ] Green: `Hrot.Blueprints.Tests` (no new failures beyond DEBT-006's 10), `Hrot.Editor.AiShared.Tests`, `EditorSubsystemBoot` filter.
- [ ] No warnings; docs; no leftover TODO/debug.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-12-REPORT.md`.

## Execution rules
- Tasks in sequence (IEditService → CommandSink → HostServices → canvas binding). Run suites yourself; fix root causes; never fake a pass; assert real model state (nodes/links/positions/dirty/undo), not non-null.
- Reuse existing `GraphCommands`/`CommandHistory`/node-drawers/renderers — do NOT duplicate. Verify the `IEditorHostServices`/`IGraphCommandSink` member sets against the code.
- Do NOT change CycloneDDS package versions or touch Hrot.IG/DDS.

## Report Requirements
In `reports/BATCH-12-REPORT.md`: how the command sink reuses GraphCommands/CommandHistory; the IEditService undo design; how host services expose the Blueprint renderers/attachments; the canvas-binding wire-up; actual test counts; full-solution build 0 errors + Blueprints no new failures; suggested commit message. No comprehension questions.
