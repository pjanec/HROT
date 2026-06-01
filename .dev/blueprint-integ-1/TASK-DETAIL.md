# AI Editor Integration — Task Details

> **Reference:** chapters of [DESIGN.md](./DESIGN.md) are cited as `§n`. Status in [TASK-TRACKER.md](./TASK-TRACKER.md).
> Each task lists **Goal**, **Files**, **Depends on**, and **Success conditions** (the latter usually as xUnit test specs). "DoD" = the task is done when all success conditions hold and the affected solution still builds with `TreatWarningsAsErrors`.

Conventions: new editor host code follows the existing assemblies' patterns; tests use xUnit (matching `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `Hrot.Blueprints.Tests`, `Hrot.ClusterRunner.Integration.Tests`). Prefer headless-constructible windows (no Raylib calls in ctor) so they are unit-testable, mirroring existing window tests.

---

## Phase 0 — Foundations: NodeEdit icon UV + engine adapters

### AIE-001 — NodeEdit `IconHandle`/`IIconProvider` UV-rect support
**Goal (§4.7):** let an icon handle address a sub-rect of a texture atlas so a single engine atlas can back many icons.
**Files:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IIconProvider.cs` (extend `IconHandle` with `Uv0`/`Uv1` `Vector2`, default `(0,0)`–`(1,1)`); the renderer(s) that draw icons (`NodeEditor.UI/Panels/MyBlueprintItemRenderer.cs`, picker/catalog icon draws) to pass the UVs to `ImGui.Image`.
**Depends on:** none.
**Success conditions:**
- `IconHandle` carries `Uv0`/`Uv1`; existing constructions still compile (defaults cover whole-texture).
- A new unit test `IconHandle_DefaultUvs_CoverWholeTexture` asserts the parameterless/whole-texture handle yields `(0,0)`/`(1,1)`.
- Icon draw sites pass `handle.Uv0/Uv1` to `ImGui.Image`; a UI test (or renderer unit test) verifies a non-default UV is forwarded.
- `NodeEditor.Demo`, `NodeEditor.Core.Tests`, `NodeEditor.UI.Tests` all build and pass unchanged.

### AIE-002 — `SilkIconProvider : IIconProvider`
**Goal (§5.1):** map NodeEdit icon keys to famfamfam-silk atlas cells via the engine `IconAtlas`.
**Files:** new `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs`; an icon-key→silk-cell map (e.g. `bt/sequence`→a silk coordinate).
**Depends on:** AIE-001.
**Success conditions:**
- `SilkIconProvider_TryGet_KnownKey_ReturnsHandleWithUv`: a known key returns `true` and a handle whose `TextureId` == the atlas texture and `Uv0/Uv1` match `IconAtlas.GetUvCoordinates(cell)`.
- `SilkIconProvider_TryGet_UnknownKey_ReturnsFalseOrFallback`: unknown key returns `false` (or a documented fallback cell) without throwing.
- Construction takes an `IconAtlas` (no GPU calls) so the test runs headless.

### AIE-003 — `ImGuiInputSource : IInputSource`
**Goal (§5.1):** abstract ImGuiNET input into NodeEdit's per-frame snapshot.
**Files:** new `Hrot/Editor/Hrot.Editor.AiShared/Adapters/ImGuiInputSource.cs` + `EditorKey`/`MouseButton`/`KeyModifiers` mapping tables.
**Depends on:** none.
**Success conditions:**
- Pure-logic mapping helpers are unit-tested: `MapMouseButton`/`MapEditorKey`/`MapModifiers` round-trip the enums used by `CanvasInput` (`ImGuiInputSource_Maps_AllMouseButtons`, `_Maps_CommonEditorKeys`, `_Maps_Modifiers`).
- The frame-snapshot properties (`MousePosition`, `MouseDelta`, `WheelDelta`, `Modifiers`, `TextThisFrame`) compile against the interface; ImGui-touching members are isolated so the mapping tests need no ImGui context.

### AIE-004 — `EngineEditorTheme : IEditorTheme`
**Goal (§5.1):** production theme over NodeEdit `DefaultTheme` + engine fonts.
**Files:** new `Hrot/Editor/Hrot.Editor.AiShared/Adapters/EngineEditorTheme.cs`.
**Depends on:** none.
**Success conditions:**
- `EngineEditorTheme_Implements_IEditorTheme_FullSurface`: every non-defaulted member returns a sane value (colors non-NaN, sizes > 0); attachment/container defaults inherited from `DefaultTheme` unless overridden.
- `EngineEditorTheme_GetFontForSize_ReturnsZeroOrValidPtr`: returns `IntPtr.Zero` (fallback) when no engine font registered, else a non-zero handle; never throws.
- `GetCategoryHeaderColor` returns a distinct color per `NodeCategory`.

### AIE-005 — `ImGuiClipboard : IClipboard`
**Goal (§5.1).** **Files:** new `Hrot/Editor/Hrot.Editor.AiShared/Adapters/ImGuiClipboard.cs`.
**Success conditions:** thin wrapper over `ImGui.GetClipboardText`/`SetClipboardText`; `ImGuiClipboard_Implements_IClipboard` compiles and the methods are non-throwing no-ops/passthroughs (ImGui calls guarded for headless). Behavioural verification deferred to manual run.

### AIE-006 — `NLogDiagnosticsSink : IDiagnosticsSink`
**Goal (§5.1).** **Files:** new `Hrot/Editor/Hrot.Editor.AiShared/Adapters/NLogDiagnosticsSink.cs`.
**Success conditions:** `NLogDiagnosticsSink_Log_RoutesAllSeverities` — each `DiagnosticSeverity` maps to the corresponding engine log level; logging an exception includes it; no throw on null exception.

### AIE-007 — `AiEditorAdapterBundle`
**Goal (§5.1):** single place that builds the five adapters + `PickerRegistry` and calls `SetServices(icons, theme)`; exposed to host-services factories.
**Files:** new `Hrot/Editor/Hrot.Editor.AiShared/Adapters/AiEditorAdapterBundle.cs`.
**Depends on:** AIE-002..006.
**Success conditions:**
- `AiEditorAdapterBundle_Build_PopulatesAllServices`: exposes non-null `Icons`, `Theme`, `Input`, `Clipboard`, `Diagnostics`, `Pickers`.
- `AiEditorAdapterBundle_Pickers_HaveServicesSet`: the `PickerRegistry` received `SetServices` with the bundle's icons + theme (observable via a registered picker that reads them, or an exposed flag).

---

## Phase 1 — Shared backing + document/perspective infrastructure

### AIE-010 — Unified `AssetCatalog` + contributors + `LoadFrom`
**Goal (§4.5, design-talk Step 2):** one shared `AssetCatalog` aggregating BTree/HSM/Blueprint contributors; reflect `Hrot.AI.Behaviors.dll` on init and after every hot reload.
**Files:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (compose `AssetCatalog`, add `BTreeAssetContributor`/`HsmAssetContributor`/Blueprint contributor, call `LoadFrom(asm)` after `_aiCoordinator.TriggerInitialLoad()` and on `OnReloadCompleted`).
**Depends on:** AIE-011.
**Success conditions:**
- `AssetCatalog_AfterLoadFrom_ListsBTreeAndHsmAssets`: given the loaded behaviors assembly (or a fake assembly with `[BTreeDefinition]`/`[HsmDefinition]`), the catalog enumerates the expected BTree + HSM entries.
- `AssetCatalog_OnHotReload_Rebuilds`: firing `OnReloadCompleted` re-invokes `LoadFrom` and the catalog's `Changed` event fires (assert via integration test in `Hrot.ClusterRunner.Integration.Tests`).
- Existing `AssetCatalogTests` still pass.

### AIE-011 — `BlueprintAssetContributor` (retire legacy `FileSystemAssetCatalog`)
**Goal (§3.2-E, design-talk Step 2):** enumerate `.bp.json` files as `IAssetCatalogContributor` (`AssetKind.Blueprint`) into the shared catalog; remove the Blueprint-specific `IAssetCatalog`/`FileSystemAssetCatalog` usage as the catalog.
**Files:** new `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Catalog/BlueprintAssetContributor.cs`; update/remove `Hrot.Blueprints.Editor/FileSystemAssetCatalog.cs` and `IAssetCatalog.cs` usage.
**Depends on:** none.
**Success conditions:**
- `BlueprintAssetContributor_Enumerate_FindsBpJson`: given a temp dir with `*.bp.json` headers, returns one `IEditableAsset` per asset with correct `AssetId`/`Name`, lazily (header-only) until opened.
- `BlueprintAssetContributor_FiresChanged_OnRefresh`.
- Blueprint tests that referenced the old catalog are updated and pass.

### AIE-012 — `AiDocumentManager`
**Goal (§4.3):** track open documents (asset, kind, cached `GraphView`+view-state, dirty) and the active one; `Open`/`Activate`/`Close`; activating switches perspective + focuses canvas + retargets that perspective's selection store.
**Files:** new `Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocumentManager.cs`.
**Depends on:** AIE-014 (perspective switch hook), AIE-007.
**Success conditions:**
- `AiDocumentManager_Open_AddsDocument_AndActivates`.
- `AiDocumentManager_OpenAlreadyOpen_FocusesExisting_NoDuplicate`.
- `AiDocumentManager_Activate_SwitchesPerspectiveToAssetKind`: activating a BTree doc invokes `SwitchPerspective("BTree")` (assert via a fake `WindowManager`/switch callback).
- `AiDocumentManager_Close_RemovesDocument_AndActivatesNextOrNone`.
- `AiDocumentManager_PreservesViewStatePerDocument`: switching away and back returns the same cached `GraphView` instance.

### AIE-013 — Global `AssetBrowserWindow` with Open-docs section
**Goal (§4.4):** make the AiShared `AssetBrowserWindow` `Global` scope; add an "Open" section (open docs across kinds; `●` active, `*` dirty, `[×]` close) above the catalog; double-click catalog → `Open`, click open-row → `Activate`.
**Files:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs` (+ ctor takes `AiDocumentManager`).
**Depends on:** AIE-012.
**Success conditions:**
- `AssetBrowser_OpenSection_ListsOpenDocs_WithActiveMarker` (logic-level test over a fake document manager — no ImGui).
- `AssetBrowser_DoubleClickCatalog_CallsOpen`; `AssetBrowser_ClickOpenRow_CallsActivate`; `AssetBrowser_CloseButton_CallsClose` (verified via injected command callbacks / a testable interaction layer, mirroring existing `AssetBrowserWindowTests`).
- Existing `AssetBrowserWindowTests` updated and pass.

### AIE-014 — `PerspectiveWorkspaceRegistrar` infra + active-asset→perspective
**Goal (§4.1, §4.2):** a per-kind registrar that registers that perspective's window instances (`OwningPerspective` = kind, distinct `###Id`s) bound to shared services; manual perspective-switch focuses the most-recent open doc of that kind.
**Files:** new `Hrot/Editor/Hrot.Editor.AiShared/Windows/PerspectiveWorkspaceRegistrar.cs` (or per-kind in subsystem editors); hook `WindowManager.OnPerspectiveChanged`.
**Depends on:** AIE-012.
**Success conditions:**
- `PerspectiveRegistrar_Registers_WindowsWithOwningPerspective`: all windows for kind K have `OwningPerspective == K` and unique ids.
- `PerspectiveSwitch_FocusesMostRecentDocOfKind`: switching to "HSM" with an open HSM doc activates it; with none, no throw and canvas shows empty state.
- Integration: `EditorSubsystemBootTests`-style test asserts the three perspectives appear in the WindowManager's grouped set.

### AIE-015 — `EditorSubsystem` composition rewrite (retire Blueprint parallel infra)
**Goal (§3.2-C/E):** replace `CreateBlueprintWindowRegistrar()` + Blueprint's own `EditorSelectionStore`/`FileSystemAssetCatalog` with: shared `AssetCatalog`, `AiEditorAdapterBundle`, `AiDocumentManager`, per-perspective `EditorSelectionStore`s, three `PerspectiveWorkspaceRegistrar`s, global Asset Browser, `SharedAiWindowRegistrar` usage; register all in `RegisterWindows`.
**Files:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`.
**Depends on:** AIE-010, AIE-012, AIE-013, AIE-014.
**Success conditions:**
- `EditorSubsystem_RegisterWindows_RegistersThreePerspectives_AndGlobalBrowser` (replaces/extends `EditorSubsystemBlueprintWindowsTests`).
- `EditorSubsystem_Boot_Headless_Succeeds` (`EditorSubsystemBootTests` green).
- No remaining references to `BlueprintWindowRegistrar` or `Blueprints.Editor.EditorSelectionStore` from the composition root (grep-clean; old tests removed/migrated).

---

## Phase 2 — BTree + HSM perspectives (authoring)

### AIE-020 — `AiGraphCanvasWindow`
**Goal (§5.2):** per-perspective canvas window hosting `GraphView`+`CanvasRenderer`, rendering the active document's cached view; optional same-kind tab bar.
**Files:** new `Hrot/Editor/Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs`.
**Depends on:** AIE-007, AIE-012.
**Success conditions:**
- `AiGraphCanvasWindow_NoActiveDoc_ShowsEmptyState` (no throw, no GraphView).
- `AiGraphCanvasWindow_RendersActiveDocumentView`: with a fake document holding a `GraphView`, the window calls `CanvasRenderer.Render` with that view (assert via a seam/fake renderer).
- `AiGraphCanvasWindow_OnFocus_ActivatesDocument`: focus → `AiDocumentManager.Activate(self.Doc)`.
- Constructible headless (no Raylib in ctor).

### AIE-021 — BTree host binding
**Goal (§5.3):** factory builds `BTreeGraphModel` + `BTreeEditorHostServices` (adapters from bundle + existing catalog/type-system/validator/command-sink + custom renderers + debug session) per opened BTree; catalog re-queries on hot reload; `BehaviorRegistry` injected for dynamic actions.
**Files:** new `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs` (or in composition root).
**Depends on:** AIE-007, AIE-020.
**Success conditions:**
- `BTreeDocumentFactory_Build_ProducesHostServices_WithAllAdapters` (non-null catalog/type-system/validator/command-sink/pickers/clipboard/icons/input/theme).
- `BTreeDocumentFactory_Build_GraphViewConstructs`: `new GraphView(model, host.CommandSink, host.Validator, host.TypeSystem, host.NodeCatalog, host)` succeeds and exposes the projected nodes.
- `BTreeCatalog_AfterReload_IncludesNewRegistrations` (existing BTree catalog/reload behavior remains green).

### AIE-022 — HSM host binding
**Goal (§5.3):** same as AIE-021 for HSM (`HsmGraphModel` + `HsmEditorHostServices`), including container-node projection (composite/parallel) + custom renderers.
**Files:** new `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmDocumentFactory.cs`.
**Depends on:** AIE-007, AIE-020.
**Success conditions:**
- `HsmDocumentFactory_Build_ProducesHostServices`.
- `HsmDocumentFactory_GraphView_ExposesStatesAndTransitions`; composite/parallel states project as `IContainerNodeModel` with children/regions.
- `HsmEditorHostServicesTests` (existing) still pass.

### AIE-023 — Inspector facet dispatch
**Goal (§5.4, design-talk Step 5):** per-perspective `InspectorWindow` routes `ActiveSubSelection` through `BTreeFacetMapper`/`HsmFacetMapper` to StructEdit facets; commit applies to model + marks dirty.
**Files:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` wiring + composition root dispatch handler.
**Depends on:** AIE-015, AIE-021, AIE-022.
**Success conditions:**
- `Inspector_BTreeNodeSelection_YieldsActionFacet` (and Wait/Sequence/etc.).
- `Inspector_HsmStateSelection_YieldsStateFacet` (+ transition/region/event).
- `Inspector_Commit_AppliesToAsset_AndMarksDirty`: editing a facet field calls the mapper's apply and `asset.IsDirty` becomes true.
- `Inspector_NoSubSelection_FallsBackToAssetProperties`.

### AIE-024 — Custom StructEdit field pickers
**Goal (§5.4, design-talk Step 5.3):** register `IImGuiFieldDrawer`s for `[BehaviorHashPicker]`, `[BlackboardFieldPicker]` (BTree) and `[HsmActionPicker]`, `[HsmGuardPicker]`, `[HsmStateSelector]`, `[HsmEventPicker]`, `[HsmSyncGroupPicker]` (HSM) with the inspector's StructEdit service.
**Files:** existing field-drawer classes in BTree/HSM editor + composition-root registration (composite drawer for shared CLR types).
**Depends on:** AIE-023.
**Success conditions:**
- `FieldPicker_BehaviorHash_ListsRegistryNames`; `FieldPicker_BlackboardField_ListsActiveAssetFields`.
- `FieldPicker_HsmEvent_ListsAssetEvents`; `FieldPicker_HsmState_ListsAssetStates`.
- `CompositeStringDrawer_DispatchesByAttribute`: a string field with no marker falls through to the default drawer.

### AIE-025 — Blackboard Authoring per perspective
**Goal (§4.2):** register `BlackboardAuthoringWindow` in BTree + HSM perspectives, bound to the active asset's blackboard schema (read) and aggregator service (AIE-052 supplies strategies; window tolerates none in v1).
**Files:** composition root registration; `BlackboardAuthoringWindow` retarget on active asset.
**Depends on:** AIE-015.
**Success conditions:**
- `BlackboardWindow_BindsActiveAssetSchema`; `BlackboardWindow_NoAggregator_ShowsExplicitVarsOnly` (no throw).
- Existing `BlackboardAuthoringWindowTests` pass.

### AIE-026 — Save → emit → hot-reload loop
**Goal (§4.5, design-talk Step 10):** route canvas/inspector edits through a debounced `RegenerationScheduler`; BTree/HSM fluent emit → atomic write → file watcher → reload → projection reconcile by `VisualId`/`StableId`; Blueprint dirty → `QuickReloadService`.
**Files:** command-sink → scheduler wiring; composition root.
**Depends on:** AIE-021, AIE-022.
**Success conditions:**
- `RegenerationScheduler_DebouncesBurst_IntoSingleSave`.
- `Save_BTree_EmitsDeterministicCSharp_ByteIdentical_OnNoChange` (re-emit of unchanged model is a no-op write).
- Integration: editing a node + Quick/Full reload updates the live registry (extend existing `QuickReloadServiceTests`/coordinator tests).
- Post-reload reconciliation keeps positions/comments by stable id.

### AIE-027 — `HsmGlobalsStrip` implementation
**Goal (§3.2-F, design-talk):** finish the stub — render a chip per `GlobalTransitionNode`, click → sub-selection, context menu (edit/change-target/remove via command sink).
**Files:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Windows/HsmGlobalsStrip.cs`; register in HSM perspective.
**Depends on:** AIE-022.
**Success conditions:**
- `HsmGlobalsStrip_RendersChipPerGlobalTransition`; `_ClickChip_SetsGlobalTransitionSubSelection`; `_Remove_DispatchesCommand` (logic-level over a fake selection store/command sink).

---

## Phase 3 — Debug (BTree + HSM)

### AIE-030 — DebugSessionRegistry + AiTracerCoordinator + session factories
**Goal (§5.6, design-talk Step 6):** instantiate `AiTracerCoordinator` + `DebugSessionRegistry`; register `BTreeDebugSession`/`HsmDebugSession` factories bound to the editor's world/kernel/time; wire `NodeDebugMetadata` via contributors.
**Files:** `EditorSubsystem.cs`.
**Depends on:** AIE-015.
**Success conditions:**
- `DebugRegistry_AcquireBTreeSession_ReturnsSession`; `_AcquireHsmSession_ReturnsSession`.
- `Contributor_WiresDebugMetadata_IntoSession` (BTree node-index symbolication resolves to `VisualId`).
- Existing `DebugSessionRegistryTests`/`AiTracerCoordinatorTests` pass.

### AIE-031 — RuntimeInspector panes per perspective
**Goal (§5.6):** register `RuntimeInspectorWindow` per perspective with BTree/HSM runtime panes bound to the active debug session.
**Files:** composition root + existing panes.
**Depends on:** AIE-030.
**Success conditions:** `RuntimeInspector_BTree_ShowsRunningNodeAndStack` (over a fake snapshot); `RuntimeInspector_Hsm_ShowsActiveConfiguration`; existing `RuntimeInspectorWindowTests` pass.

### AIE-032 — TraceTimeline lane providers per perspective
**Goal (§5.6):** register `TraceTimelineWindow` per perspective with `BTreeTraceLaneProvider`/`HsmTraceLaneProvider`.
**Files:** composition root.
**Depends on:** AIE-030.
**Success conditions:** `TraceTimeline_BTree_RegistersFourLanes` (nodes/stack/async/errors); `_Hsm_RegistersExpectedLanes`; existing `TraceTimelineWindowTests` pass.

### AIE-033 — Canvas runtime overlays + breakpoint toggles
**Goal (§5.6):** inject runtime-overlay + breakpoint-gutter custom renderers (already implemented) into the host services with the active session; breakpoint toggle commands route through the command sink.
**Files:** host document factories (AIE-021/022) + canvas context menu.
**Depends on:** AIE-021, AIE-022, AIE-030.
**Success conditions:**
- `HostServices_Include_RuntimeOverlay_And_BreakpointRenderers`.
- `BreakpointToggle_OnNode_DispatchesSetNodePropertyCommand` (`isBreakpoint`).
- Overlay renderer `IsActive==false` when the session is detached (no per-frame overhead) — assert via the renderer's `IsActive`.

### AIE-034 — Watch / Breakpoints / Diagnostics windows per perspective
**Goal (§4.2):** register the universal-breakpoint `DataBreakpointManagerWindow` + AiShared `DiagnosticsWindow` (+ Watch) per perspective bound to shared managers.
**Files:** composition root.
**Depends on:** AIE-030.
**Success conditions:** windows register with correct `OwningPerspective`; `Diagnostics_ShowsValidatorOutput_ForActiveAsset`; existing breakpoint wiring tests (`BreakpointSubsystemWiringTests`) remain green.

---

## Phase 4 — Blueprint perspective (full structural B2 + My Blueprint)

### AIE-040 — `BlueprintGraphModel : IGraphModel`
**Goal (§5.5):** project the active `BlueprintAsset` graph (nodes/pins/links) into NodeEdit; raise `Changed` on mutation. Template: `FakeGraphModel`.
**Files:** new `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintGraphModel.cs`.
**Depends on:** AIE-011.
**Success conditions:**
- `BlueprintGraphModel_ProjectsNodesAndPins_FromAsset` (counts + ids match a built `BlueprintAsset`).
- `BlueprintGraphModel_ProjectsLinks_BetweenPins`.
- `BlueprintGraphModel_FiresChanged_OnAssetMutation`.

### AIE-041 — `BlueprintTypeSystem : ITypeSystem`
**Goal (§5.5):** data-flow typed pins — colors/shapes per type, compatibility, implicit casts, default-value editors. Template: `FakeTypeSystem`.
**Files:** new `…/Host/BlueprintTypeSystem.cs`.
**Depends on:** none.
**Success conditions:**
- `BlueprintTypeSystem_ExecPins_OnlyConnectToExec`; `_DataPins_CompatibleBySameType`.
- `BlueprintTypeSystem_ImplicitCast_AllowedWhereDefined` (e.g. int→float if supported, else false).
- `_GetPinColor/Shape_StablePerType`.

### AIE-042 — `BlueprintLinkValidator : ILinkValidator`
**Goal (§5.5):** reject illegal connections (type-incompatible, exec↔data, illegal cycles), replacing an existing data link on a single-input pin. Template: `FakeLinkValidator`.
**Files:** new `…/Host/BlueprintLinkValidator.cs`.
**Depends on:** AIE-040, AIE-041.
**Success conditions:**
- `LinkValidator_RejectsIncompatibleTypes`; `_RejectsExecToData`; `_AllowsValidDataLink`.
- `LinkValidator_SingleInput_ReplacesExisting`.

### AIE-043 — `BlueprintNodeCatalog : INodeCatalog`
**Goal (§5.5):** wrap the existing `NodeKindRegistry` palette; add dynamic entries for callable peers + custom events; re-query on hot reload.
**Files:** new `…/Host/BlueprintNodeCatalog.cs`.
**Depends on:** AIE-011.
**Success conditions:**
- `BlueprintNodeCatalog_All_IncludesPaletteKinds`.
- `BlueprintNodeCatalog_Query_FiltersByTextAndCategory`.
- `BlueprintNodeCatalog_IncludesCallablePeers_AfterCatalogChanged`.

### AIE-044 — `BlueprintCommandSink : IGraphCommandSink`
**Goal (§5.5):** apply add/move/connect/delete/property to `BlueprintAsset` via the existing `GraphCommands`/`CommandHistory`; property edits via real `IEditService` (AIE-049); mark dirty → regeneration.
**Files:** new `…/Host/BlueprintCommandSink.cs`.
**Depends on:** AIE-040, AIE-049.
**Success conditions:**
- `CommandSink_AddNode_AddsToAssetGraph`; `_RemoveNodes_Removes`; `_AddLink_ConnectsPins`; `_MoveNodes_UpdatesPositions`; `_SetProperty_UpdatesNode`.
- `CommandSink_MarksAssetDirty_AfterMutation`.
- `CommandSink_Batch_AppliesAllOrStopsOnFailure`.

### AIE-045 — `BlueprintEditorHostServices : IEditorHostServices`
**Goal (§5.5):** bundle Blueprint graph model/type system/validator/catalog/command-sink + adapters + existing node drawers/attachment providers/custom renderers/`IEditService`.
**Files:** new `…/Host/BlueprintEditorHostServices.cs`.
**Depends on:** AIE-040..044, AIE-007.
**Success conditions:**
- `BlueprintEditorHostServices_FullSurface_NonNull`.
- `BlueprintEditorHostServices_GraphView_Constructs`.

### AIE-046 — Blueprint host binding into `AiGraphCanvasWindow`
**Goal (§5.2):** Blueprint perspective's canvas renders Blueprint documents via a `BlueprintDocumentFactory`.
**Files:** new `…/Host/BlueprintDocumentFactory.cs`; composition root.
**Depends on:** AIE-045, AIE-020.
**Success conditions:** `BlueprintDocumentFactory_Build_ProducesHostServices_AndGraphView`; opening a `.bp.json` yields a renderable document (integration test in `Hrot.Blueprints.Tests`).

### AIE-047 — `BlueprintMyBlueprintModel` + `MyBlueprintPanel`
**Goal (§5.5, design-talk My Blueprint):** project `Variables`, `Graphs`, `CustomEvents`, `EventDispatchers` (real); `Functions`/`Macros` faked/empty; register NodeEdit `MyBlueprintPanel` in the Blueprint perspective.
**Files:** new `…/Windows/BlueprintMyBlueprintModel.cs` + panel registration. Template: `FakeMyBlueprintModel`.
**Depends on:** AIE-011.
**Success conditions:**
- `MyBlueprintModel_Sections_FixedOrder` (Graphs, Functions, Macros, Custom Events, Variables, Event Dispatchers).
- `MyBlueprintModel_Variables_ProjectAssetVariables` (name/type/category/accent).
- `MyBlueprintModel_FiresChanged_OnAssetMutation`.
- `MyBlueprintModel_FakedSections_ReturnEmpty_NoThrow`.

### AIE-048 — Blueprint Details + Variables windows
**Goal (§4.2):** register the Blueprint node-drawer Details panel + `BlueprintVariablesWindow` in the Blueprint perspective, bound to selection.
**Files:** composition root + existing Blueprint windows.
**Depends on:** AIE-046.
**Success conditions:** `BlueprintDetails_RendersDrawerForSelectedNode` (When/Montage/EQS); `VariablesWindow_ListsAndEditsAssetVariables`; existing Blueprint editor window tests pass.

### AIE-049 — Real `IEditService`
**Goal (§3.2-F):** replace `NoOpEditService` with an implementation that records property edits as undoable commands on the Blueprint `CommandHistory` and marks dirty.
**Files:** new `…/NodeDrawers/EditService.cs`; remove `EditorSubsystem.NoOpEditService` usage.
**Depends on:** none (precedes AIE-044's property routing).
**Success conditions:**
- `EditService_MarkDirty_FlagsAsset`.
- `EditService_PropertyEdit_PushesUndoableCommand`; `Undo_RevertsPropertyEdit`.

---

## Phase 5 — Cross-asset services (P2)

### AIE-050 — Comparison sanitizers + ComparisonExportBuilder
**Goal (§5.7, design-talk Step 11):** register BTree/HSM/Blueprint/Blackboard/Utility sanitizers into `SanitizerRegistry`; wire `ComparisonExportBuilder`.
**Files:** composition root DI (`AddBTreeEditorComparison`/`AddHsmEditorComparison`/`AddBlueprintEditorComparison`).
**Depends on:** AIE-010.
**Success conditions:** `SanitizerRegistry_HasSanitizer_PerAssetKind`; existing sanitizer tests pass; a comparison over two versions of one asset produces deterministic stripped output.

### AIE-051 — Reference catalog contributors + RefactorService + FindResults
**Goal (§5.7, design-talk Step 7):** register `BTreeBlackboardVariableContributor`, HSM + Blueprint reference contributors into `ReferenceCatalog`; wire `RefactorService` + `FindResultsWindow`.
**Files:** composition root.
**Depends on:** AIE-010.
**Success conditions:** `ReferenceCatalog_FindReferences_AcrossAssets`; `RefactorService_Rename_WritesAtomically`; existing `ReferenceCatalogTests`/`RefactorServiceTests` pass.

### AIE-052 — Blackboard aggregator strategies
**Goal (§5.7, design-talk Step 8):** register `BTreeBlackboardAggregatorStrategy`/`HsmBlackboardAggregatorStrategy` into `BlackboardAggregatorService`; feed `BlackboardAuthoringWindow` bin-packing.
**Files:** composition root.
**Depends on:** AIE-025.
**Success conditions:** `Aggregator_BTree_CollectsSubtreeRequirements`; `Aggregator_Hsm_CollectsStateActionRequirements`; bin-packer surfaces budget warnings.

### AIE-053 — SubElementCollision + dangling-reference classification
**Goal (§5.7, design-talk Step 7.1/7.2):** surface action short-name collisions in the inspector diagnostic strip; classify dangling references on delete (auto-resolvable vs critical).
**Files:** `SubElementCollisionDetector` wiring in `InspectorWindow`; `RefactorService.PreviewDelete` classification.
**Depends on:** AIE-051.
**Success conditions:** `CollisionDetector_FlagsDuplicateShortNames`; `PreviewDelete_ClassifiesCriticalVsAutoResolvable`; `ApplyDelete_RefusesCritical_WhenDisallowed`.
