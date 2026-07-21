# Hrot.Blueprints.Editor

> Manually maintained; last verified 2026-07-21 against the implemented code.

| Field      | Value                                                                              |
|------------|--------------------------------------------------------------------------------------|
| Project    | `Hrot.Blueprints.Editor`                                                             |
| Path       | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/`                                |
| Framework  | net8.0                                                                                |
| Nullable   | enabled                                                                               |
| File count | ~148 `.cs` files                                                                      |

---

## README Validation

**Status: Missing.** No `README.md` exists in the project folder. `BlueprintsEditor.cs` is an
explicit placeholder stub (`// Placeholder for Hrot.Blueprints.Editor assembly.`). All
architectural knowledge lives in source comments and this document.

---

## Executive Overview

`Hrot.Blueprints.Editor` is the ImGui-based authoring, debugging, and hot-reload host for the
Blueprints visual-scripting stack. It has been substantially rebuilt since the original design:
the graph canvas is no longer a bespoke `GraphEditorWindow` — it is the shared **NodeEdit**
canvas library (`ExtDeps`), driven through a `Host/` adaptation layer, and the window/asset
plumbing is now a Blueprint-specific plug-in into the shared **`Hrot.Editor.AiShared`**
perspective/window/catalog infrastructure rather than a self-contained editor.

Four pillars:

- **Canvas (`Host/`)** — `BlueprintGraphModel` projects one `Graph` of a `BlueprintAsset` onto
  NodeEdit's `IGraphModel`; `BlueprintCommandSink` turns NodeEdit `GraphCommand`s (add/remove
  node, wire-drop link, move, set pin default, reroute, comment) into asset mutations, routed
  through `CommandHistory` for undo/redo. `BlueprintDocumentFactory.Build` assembles the whole
  per-document NodeEdit stack (`GraphView`, palette, pickers, find bar, bookmarks, debug overlay)
  and returns an `AiCanvasContext` for `Hrot.Editor.AiShared`'s document manager to host.
- **Windows** — two coexisting mechanisms. Legacy `IBlueprintEditorWindow` panels (Inspector,
  Debug Panel, Watch Panel, Callstack, Hot Reload Log) are registered by `BlueprintWindowRegistrar`
  and adapted into the engine `WindowManager` via `BlueprintManagedWindowAdapter`. Newer panels
  are authored directly as `Fdp.Presentation.WindowManager.ManagedWindow`s
  (`BlueprintMyBlueprintWindow`, `BlueprintDetailsWindow`, `BlueprintVariablesManagedWindow`,
  `GraphSignatureWindow`, `BlueprintBookmarksWindow`, `EntityBlueprintsManagedWindow`) and are
  registered by the host composition root, retargeted per active document.
- **Palette / node authoring** — `BlueprintEditorBootstrap` composes the full node-drawer
  registry and palette registry: built-in vocabulary, channel-command and non-channel behavior
  actions, math presets, reflection-discovered `[BlueprintCallable]` helpers, custom-event
  publish/subscribe entries, and Make/Break/SetMembers struct triples.
- **Debug / hot reload** — `BlueprintDebugSession` (full `IBlueprintDebugSession` +
  `IAiDebugSession` implementation) drives breakpoints, watches, step/rewind, and call-stack
  tracking; `BlueprintDebugToNodeEditAdapter` bridges it to NodeEdit's native `IDebugSession` so
  the canvas draws breakpoint markers and execution overlays without Blueprint-specific renderer
  code. `QuickReloadService` (in-process ALC swap) and `FullRebuildService` (`dotnet build`)
  provide the two reload pathways.

Asset browsing/reference-tracking is **not** owned by this project any more: it plugs into the
shared `Hrot.Editor.AiShared.Catalog` system via `Catalog/BlueprintAssetContributor` (an
`IAssetCatalogContributor`) and `Catalog/BlueprintReferenceContributor`. The old
`IAssetCatalog`/`FileSystemAssetCatalog`/`AssetBrowserWindow` types are retired; the only
Blueprint-local scanner left is `BlueprintPeerSource`, a thin `(Guid, Path)` enumerator used
purely for peer-signature resolution (`CallPeerBlueprintNode` pin typing, sibling-signature
compilation).

Authoring workflow:

```
Asset Browser (shared Hrot.Editor.AiShared catalog, fed by BlueprintAssetContributor)
    |  double-click opens a BlueprintFileAsset
    v
BlueprintDocumentFactory.Build(...)
    |-- loads BlueprintAsset from disk (LoadAsset)
    |-- BlueprintGraphModel projects the Event graph (or first graph) onto NodeEdit
    |-- BlueprintCommandSink + CommandHistory wired for undo/redo
    |-- GraphView (NodeEdit) + FindBar + Bookmarks + debug adapter assembled
    v
AiCanvasContext hosted by Hrot.Editor.AiShared's document manager / canvas window
    |-- wire-drop node/link editing, pin-default edits, comments, reroutes
    |-- My Blueprint / Details / Variables / Graph Signature panels retarget to the doc
    v
Save (BlueprintFileAsset.MarkClean) --> QuickReloadService (in-process ALC swap)
                                   \--> FullRebuildService (dotnet build, then file-watcher drain)
```

---

## Architecture

### Layer overview

```
+----------------------------------------------------------------------------------+
|                      Hrot.Editor.AiShared (host perspective/window/catalog system)|
|  AiDocumentManager · WindowManager · IAssetCatalogContributor · AiCanvasContext   |
+----------------------------------------------------------------------------------+
        ^                          ^                              ^
        |  contributes             |  hosts documents via         |  ManagedWindows
        |                          |  BlueprintDocumentFactory     |  register directly
+-------+-------+     +------------+-------------+      +---------+----------------+
| Catalog/      |     | Host/ (NodeEdit adapters)|      | Windows/, Debug/,        |
| Asset+Ref     |     | BlueprintGraphModel      |      | Variables/,              |
| Contributors  |     | BlueprintCommandSink     |      | EntityBlueprints/        |
+---------------+     | NodePinSchema            |      +--------------------------+
                      | BlueprintDocumentFactory |
                      +--------------------------+
                                |
                +---------------+----------------+
                |               |                |
                v               v                v
      NodeDrawers/     Reload/            Debug (Core.Debug)
      (drawer+palette  QuickReloadService  BlueprintDebugSession
       registries)     FullRebuildService  DebugMap / probes
```

### Two window mechanisms

| Mechanism | Registrar | Panels |
|-----------|-----------|--------|
| Legacy `IBlueprintEditorWindow` | `BlueprintWindowRegistrar` (implements `Fdp.Toolkit.Runner.IWindowRegistrar`), adapted via `BlueprintManagedWindowAdapter : ManagedWindow` | `InspectorWindow`, `Debug/DebugPanelWindow`, `Debug/WatchPanelWindow`, `Debug/CallstackWindow`, `Debug/HotReloadLogWindow` |
| Direct `Fdp.Presentation.WindowManager.ManagedWindow` | Registered by the host composition root (outside this project); retargeted per active document | `Windows/BlueprintMyBlueprintWindow`, `Windows/BlueprintDetailsWindow`, `Windows/BlueprintVariablesManagedWindow`, `Windows/GraphSignatureWindow`, `Windows/BlueprintBookmarksWindow`, `EntityBlueprints/EntityBlueprintsManagedWindow` |

`PreferencesWindow` (legacy `IBlueprintEditorWindow`) still exists but is not wired through
`BlueprintWindowRegistrar.RegisterWindows` — it is registered ad hoc by whichever host needs it.

### The NodeEdit canvas (Host/)

The canvas is the `NodeEditor.Core`/`NodeEditor.UI` (`ExtDeps`) `GraphView`, not a
Blueprint-specific renderer. `Host/` supplies every adapter NodeEdit needs:

| Type | NodeEdit contract | Role |
|------|--------------------|------|
| `BlueprintNodeModel` | `INodeModel` | Projects one `Node`; `Position` reads live from `NodeMetadata`; `BuildTitle` derives the on-canvas title (operator symbols, literal values, short event/variable names); flags unresolved CLR `FunctionCallNode`s as `NodeState.Error`. |
| `BlueprintGraphModel` | `IGraphModel` | Projects one `Graph`; two-pass GUID-binding algorithm resolves pin GUIDs for pin-less (`"Pins": []`) JSON-loaded assets from incident `Link.FromPinId`/`ToPinId`, in parity with the compiler's `Stage0_Rehydrate`. |
| `NodePinSchema` (internal) | — | Canonical per-kind pin projection (`GetCanonicalPins`); resolution order is asset-authored pins → `NodeKindRegistry` descriptor → built-in fallback table. Every dynamic kind (FunctionCall, EventEntry, Return, GetVariable, ChannelCommand, PublishEvent, CallPeerBlueprint, GetShared/SetShared, Make/Break/SetMembers struct nodes, …) is documented in-line with the exact compiler stage it must stay in parity with. |
| `BlueprintCommandSink` | `IGraphCommandSink` | Applies every `GraphCommand` (AddNode, RemoveNodes, AddLink, RemoveLinks, MoveNodes, ChangeParentMultiple, SetNodeProperty, SetPinDefault, InsertReroute/MoveReroute/RemoveReroute, AddComment/UpdateComment/RemoveComment, Batch). Structural ops go through `CommandHistory`; continuous drags bypass it; property/pin edits go through `EditService.RecordPropertyEdit`. |
| `BlueprintDocumentFactory` (static) | — | One-shot per-document factory: loads the asset, builds `BlueprintGraphModel`/`BlueprintCommandSink`/`BlueprintNodeCatalog`/`BlueprintTypeSystem`/`BlueprintLinkValidator`, wires the enum-sentinel pin-default-editor registry, registers the F9 Toggle-Breakpoint command, the My-Blueprint "+ Variable" command, and per-document bookmarks, then returns an `AiCanvasContext`. |
| `BlueprintEditorHostServices` | `IEditorHostServices` | Bundles the above plus the engine `AiEditorAdapterBundle` (pickers, clipboard, icons, diagnostics, input, theme) into one `IEditorHostServices` for `GraphView`. |
| `BlueprintNodeCatalog` | `INodeCatalog` | Wraps the static `NodeKindRegistry` palette and layers on dynamic entries derived from the open asset's `CallablePeers` and `CustomEvents`; fires `CatalogChanged` on `Refresh`. |
| `BlueprintTypeSystem` | (type-compat surface) | Pin type-compatibility rules + `SelectableTypeIds` for variable-type pickers. |
| `BlueprintLinkValidator` | `ILinkValidator` | Output→Input only; Exec↔Exec / Data↔Data only; type compatibility via `BlueprintTypeSystem`; single-data-input rule; rejects self-loops. |
| `BlueprintDebugToNodeEditAdapter` | `IDebugSession` | Bridges `IBlueprintDebugSession` so NodeEdit's native `NodeRenderer` draws breakpoints/execution overlays/pause pulses with zero Blueprint-specific paint code. |

`GraphEditor/` still exists as the older command-history/selection substrate
(`CommandHistory`, `IGraphCommand`, `GraphCommands.AddNodeCommand`/`DeleteNodeCommand`/
`LinkEditCommand`, `SelectionState`) — `BlueprintCommandSink` builds on top of it rather than
replacing it.

### Two-pass pin/GUID resolution (why `"Pins": []` still works)

Persisted `.bp.json` nodes store `Pins: []` (a deliberate `SaveActiveBlueprintCommand`
invariant — pins are an editor projection, never serialized). `BlueprintGraphModel.Rebuild()`:

1. **Pass 1** — for each node, get the canonical pin list from `NodePinSchema`, then bind each
   pin's `Guid` from the incident `Link.FromPinId`/`ToPinId` (deterministic-GUID-first, legacy
   positional fallback, exactly mirroring the compiler's `Stage0_Rehydrate.AssignDirection`).
2. **Pass 2** — build `BlueprintNodeModel`/`BlueprintLinkModel` instances from the resolved pins.

Nothing is written back to the asset or disk — the projection is pure and rebuilt on every
mutation via `RebuildAndNotify()` (or the lightweight `NotifyMoved` during a drag, which skips
the rebuild to preserve node-model identity across drag frames).

---

## Diagrams

### 1 — Opening a document

```
BlueprintFileAsset (Catalog/BlueprintAssetContributor, header-only)
    |
    | double-click / AiDocumentManager.Open
    v
BlueprintDocumentFactory.Build(asset, bundle, editService, paletteRegistry, ...)
    |-- LoadAsset(bpFile)                         [File.ReadAllText + BlueprintJsonServices.Deserialize]
    |-- resolve Event graph (fallback: first graph)
    |-- BlueprintGraphModel(asset, graph, kindRegistry, channelCommands, peerLookup, ...)
    |-- BlueprintNodeCatalog / BlueprintTypeSystem / BlueprintLinkValidator / CommandHistory
    |-- BlueprintCommandSink(asset, graph, model, catalog, validator, history, editService, markDirty, ...)
    |-- BlueprintEditorHostServices(...)  <-- bundles engine AiEditorAdapterBundle
    |-- [debugSession != null] BlueprintDebugToNodeEditAdapter -> hostServices.SetDebugSession
    |-- GraphView(model, commandSink, validator, typeSystem, catalog, hostServices)
    |-- BlueprintPickerSources.Register(...) / FindBar / EditorCommandsImpl / BookmarkStore
    v
AiCanvasContext { View, AssetRef=bpAsset, FindBar, Commands, Bookmarks }
    |
    v
Hosted by the shared AiGraphCanvasWindow; My Blueprint / Details / Variables / Graph
Signature / Bookmarks ManagedWindows retarget to the new AssetRef.
```

### 2 — Wire-drop edit -> undo/redo

```
NodeEdit canvas (user drags a wire / drops a palette node)
    |
    v
GraphCommand (AddNode | AddLink | MoveNodes | SetPinDefault | ...)
    |
    v
BlueprintCommandSink.Apply(command)
    |-- structural (AddNode/RemoveNodes/AddLink/RemoveLinks/reroute/comment)
    |       --> CommandHistory.Execute(IGraphCommand)         [undoable]
    |-- continuous drag (MoveNodes/ChangeParentMultiple)
    |       --> mutate NodeMetadata.X/Y directly; NotifyMoved  [NOT pushed to history]
    |-- property/pin edit (SetNodeProperty/SetPinDefault)
    |       --> EditService.RecordPropertyEdit(apply, undo)   [undoable]
    v
_markDirty(asset)  -->  BlueprintFileAsset.MarkDirty()
    v
BlueprintGraphModel.RebuildAndNotify()  -->  GraphView redraws
```

### 3 — Quick Reload pipeline

```
QuickReloadService.TriggerAsync(asset)
    |-- BuildSiblingSignatures(asset)
    |       catalog: BlueprintPeerSource.EnumerateAll()
    |       per sibling: EditorState.GetInMemoryAsset() override, else BlueprintSignatureParser.Parse(disk)
    |       edited asset: BlueprintSignatureBuilder.FromInMemoryAsset(asset)
    |-- IBlueprintCompiler.Compile(asset, CompileOptions{ EmitPdbWithEmbeddedSource: true, ... })
    |       [[AST compile -> GeneratedSource + DebugMap]]
    v
TriggerFromSourcesAsync(sources, assemblyName, debugMap, assetId)
    |-- InMemoryRoslynCompiler.Compile(sources) -> (peBytes, pdbBytes)
    |-- new AssemblyLoadContext(assemblyName, isCollectible: true).LoadFromStream(pe, pdb)
    |-- HsmActionDispatcher.ClearAll()
    |-- BlueprintRegistrarScanner.Scan(assembly, blueprintStaging, behaviorStaging)
    |-- IBlueprintDebugSession.RegisterDebugMap(debugMap)      [before coordinator handoff]
    |-- AiHotReloadCoordinator.ApplyQuickReload(alc, behaviorStaging, blueprintStaging)  [atomic RCU swap]
    v
QuickReloadResult(Succeeded, ErrorMessage, DurationMs)
```

`FullRebuildService.TriggerAsync()` is the out-of-process alternative: spawns
`dotnet build [buildTarget]`, streams stdout/stderr to `IOutputConsole`, and sets
`PendingDrainAfterBuild = true` on success so the caller drains the file watcher afterward.

### 4 — Debug probe -> canvas overlay

```
Generated Blueprint code            DebugProbe.Sink.OnNodeEnter(entity, nodeId)
    |                                        |
    v                                        v
BlueprintDebugSession  <-------------------- (also OnPinValueChanged<T>, OnPeerCallEnter/Exit)
    |
    +-- ExecutionHistory ring buffer (per entity)
    +-- Breakpoint dict lookup (O(1) by node-id string) --> HandleBreakpointHit()
    |                                                            |
    |                                                            +-- _isPaused = true
    |                                                            +-- _timeController.RequestPause()  [soft-pause]
    |                                                            +-- OnBreakpointHit event
    +-- StepMode / virtual-pointer bookkeeping (StepBack/Into/Over/Out via ExecSuccessors)
    |
    v
BlueprintDebugToNodeEditAdapter (IDebugSession)
    |-- CurrentlyExecutingNode / RecentlyExecutedNodes / Breakpoints / WatchedPins
    v
NodeEdit NodeRenderer draws breakpoint markers + execution overlay + pause pulse natively.

DebugPanelWindow / WatchPanelWindow / CallstackWindow read the same session for their tables;
DebugStepControls.Draw() renders the shared Continue/Step-Back/Step-Over/Step-Into/Step-Out row.
```

---

## Source Structure

```
Hrot.Blueprints.Editor/
|-- BlueprintsEditor.cs                     -- assembly placeholder stub
|-- AssemblyInfo.cs
|-- BlueprintEditorModule.cs                -- legacy-window orchestrator (RegisterWindow/DrawAllWindows/OnReloadCompleted)
|-- BlueprintEditorBootstrap.cs             -- composes drawer registry, palette registry, attachment providers, canvas renderers, recipe discovery
|-- BlueprintEditorConfiguration.cs         -- compile-time config record (DebugMapsOutputDirectory, BehaviorsDllDirectory, BehaviorsBuildTarget)
|-- BlueprintEditorPreferences.cs           -- user prefs (camelCase JSON): AutoReloadOnSave, WatchPanelVisible, GraphEditorGridSnap, NodeHistorySize, HotReloadLogMaxEntries
|-- BlueprintEditorServiceCollectionExtensions.cs -- AddBlueprintEditor(services) DI registration
|-- BlueprintEditorWindowBase.cs            -- abstract IBlueprintEditorWindow base
|-- BlueprintWindowRegistrar.cs             -- registers legacy windows into IBlueprintWindowRegistry / engine WindowManager
|-- BlueprintManagedWindowAdapter.cs        -- internal ManagedWindow adapter for legacy windows (lazy factory)
|-- IBlueprintWindowRegistry.cs             -- abstraction over window registration (name -> factory)
|-- IBlueprintEditorWindow.cs               -- legacy window contract (Title/IsVisible/DrawUI/OnActivated/OnDeactivated)
|-- IBlueprintEditorCoordinator.cs          -- hot-reload lifecycle events (OnReloadCompleted/OnReloadFailed)
|-- NullBlueprintEditorCoordinator.cs       -- no-op IBlueprintEditorCoordinator
|-- IWindowRegistrar.cs                     -- menu/toolbar/shortcut registration abstraction
|-- IOutputConsole.cs                       -- logging abstraction (LogInfo/Warning/Error/Debug/Diagnostic)
|-- SystemConsoleOutputConsole.cs           -- Console.WriteLine-backed IOutputConsole
|-- EditorState.cs                          -- in-memory asset overlay keyed by AssetId
|-- EditorSelectionStore.cs                 -- single-selection store + OnSelectionChanged
|-- DirtyTracker.cs                         -- dirty-flag HashSet<Guid>
|-- BlueprintPeerSource.cs                  -- thin (AssetId, Path) scanner over *.bp.json, replaces the retired IAssetCatalog for peer-signature lookups
|-- BlueprintNewAssetService.cs             -- INewAssetService impl: creates new Blueprint assets from recipes / the "Empty" recipe
|-- NewFromRecipeService.cs                 -- clones a recipe BlueprintAsset with a fresh AssetId
|-- RecipeMetadataAdapter.cs                -- maps compiler RecipeMetadata -> shared Hrot.Editor.AiShared.Recipes.RecipeMetadata
|-- SaveActiveBlueprintCommand.cs           -- saves the active asset; strips Pins to [] around serialization (projection-only invariant)
|-- BlueprintBreakpointMenuPopulator.cs     -- Universal-Breakpoints "Add Conditional Data Breakpoint..." node context-menu entry
|-- BlueprintDebugSession.cs                -- IBlueprintDebugSession + IAiDebugSession impl (declared in Hrot.Blueprints.Core.Debug namespace)
|-- InspectorWindow.cs                      -- legacy tabbed inspector (Node/Graph/Asset) -- superseded in practice by Windows/BlueprintDetailsWindow
|-- PreferencesWindow.cs                    -- legacy preferences panel
|-- ReloadInfo.cs                           -- ReloadSource enum + ReloadCompletedInfo record
|
|-- ActionCatalog/
|   |-- IBehaviorActionCatalog.cs           -- BehaviorActionHosts/-Source enums, BehaviorActionEntry record, facade interface
|   |-- BehaviorActionCatalog.cs            -- composes IChannelCommandCatalog + IActionSchemaExporter into one snapshot, rebuilds on Changed
|
|-- Catalog/
|   |-- BlueprintAssetContributor.cs        -- IAssetCatalogContributor for AssetKind.Blueprint; header-only *.bp.json scan; BlueprintFileAsset
|   |-- BlueprintReferenceContributor.cs    -- IReferenceCatalogContributor; exposes asset-id + composed-FQN sub-elements for cross-asset refs
|   |-- BlueprintIconKeys.cs                -- header Dispatch+Intent -> Action/Condition/Function picker icon key
|
|-- Comparison/
|   |-- BlueprintComparisonSanitizer.cs     -- normalizes .bp.json for the Visual Asset Comparison feature
|   |-- BlueprintEditorComparisonServiceCollectionExtensions.cs -- AddBlueprintEditorComparison() DI extension
|
|-- Debug/
|   |-- DebugPanelWindow.cs                 -- pause/step controls + breakpoint table (legacy window)
|   |-- WatchPanelWindow.cs                 -- watch table, subscribes to OnPinValueChangedEvent
|   |-- CallstackWindow.cs                  -- peer-call frame stack table
|   |-- HotReloadLogWindow.cs               -- reload event log window
|   |-- HotReloadLogModel.cs                -- queue ring buffer (max 1000)
|   |-- ReloadLogEntry.cs                   -- log entry record
|   |-- DebugStepControls.cs                -- shared Continue/Step-Back/Step-Over/Step-Into/Step-Out row + node-position text
|   |-- ExecSuccessors.cs                   -- computes immediate exec successor node ids for stepping (mirrors Stage5_Schedule)
|   |-- BlueprintDebugToNodeEditAdapter.cs  -- IBlueprintDebugSession -> NodeEdit IDebugSession bridge
|   |-- AiDebugCommands.cs                  -- registers the polymorphic "AI Debug" toolbar command group (Continue/StepOver/Into/Out/Pause/StepBack)
|   |-- BlueprintDebugToNodeEditAdapter... (see above)
|   |-- MasterSyncTimeControllerAdapter.cs  -- IEngineDebugTimeController over MasterSyncController
|
|-- EntityBlueprints/
|   |-- EntityBlueprintsEditModel.cs        -- headless view-model: Reality scan (3 blackboard tiers) vs staged Adds/Removes, tier projection, CommitPlan
|   |-- EntityBlueprintsPanel.cs            -- ImGui panel bound to the edit model
|   |-- EntityBlueprintsManagedWindow.cs    -- ManagedWindow wrapper (lazy panel factory)
|
|-- GraphEditor/
|   |-- IGraphCommand.cs                    -- command interface (Execute/Undo/Description)
|   |-- GraphCommands.cs                    -- AddNodeCommand, DeleteNodeCommand, LinkEditCommand
|   |-- CommandHistory.cs                   -- ring-buffer undo/redo stack (capacity 64)
|   |-- SelectionState.cs                   -- selected node/link Guid sets
|
|-- Host/
|   |-- BlueprintNodeModel.cs               -- INodeModel adapter; BuildTitle/BuildCategory/HeaderGlyph/NodeState.Error detection
|   |-- BlueprintGraphModel.cs              -- IGraphModel adapter; two-pass pin/GUID projection; Rebuild/NotifyMoved/RebuildAndNotify
|   |-- BlueprintPinModel.cs                -- IPinModel adapter (referenced by BlueprintGraphModel/NodePinSchema)
|   |-- BlueprintLinkModel.cs               -- ILinkModel adapter (stable LinkId derived from FromPinId/ToPinId)
|   |-- BlueprintCommentModel.cs            -- ICommentModel adapter for GraphComment
|   |-- NodePinSchema.cs                    -- internal canonical per-kind pin projection (compiler-parity source of truth)
|   |-- BlueprintCommandSink.cs             -- IGraphCommandSink: applies every GraphCommand to the asset
|   |-- BlueprintDocumentFactory.cs         -- static per-document assembly (asset load -> AiCanvasContext)
|   |-- BlueprintEditorHostServices.cs      -- IEditorHostServices bundling all Host/ services + engine adapters
|   |-- BlueprintNodeCatalog.cs             -- INodeCatalog wrapping NodeKindRegistry + dynamic peer/custom-event entries
|   |-- BlueprintTypeSystem.cs              -- pin type-compatibility rules + SelectableTypeIds
|   |-- BlueprintLinkValidator.cs           -- ILinkValidator (Output->Input, Exec/Data separation, single-data-input rule)
|   |-- BlueprintEnumValueProvider.cs       -- IEnumValueProvider (member-name <-> long) for enum-sentinel pins (ENUM-NAME)
|   |-- EnumSentinelPinEditorRegistry.cs    -- IPinDefaultValueEditorRegistry wrapper: "global::" TypeKeys -> EnumPinEditor
|   |-- NullPinDefaultValueEditorRegistry.cs-- no-op IPinDefaultValueEditorRegistry
|   |-- BlueprintPickerSources.cs           -- registers picker sources (methods/types/peers/events/…) with the engine IPickerRegistry
|   |-- BlueprintSelectionBridgeHelper.cs   -- bridges AiShared selection <-> BlueprintNodeSelection
|   |-- LiteralValueJson.cs                 -- LiteralNode.ValueJson <-> canvas inline-editor value conversions
|   |-- FunctionCallTooltip.cs              -- resolved CLR signature + XML-doc summary for FunctionCallNode hover
|   |-- ClrSourceLocator.cs / ClrXmlDocSource.cs / SourceFileOpener.cs / VisualStudioDteOpener.cs
|   |                                       -- "open in IDE" support for FunctionCallNode's resolved CLR source
|
|-- Inspector/
|   |-- IStructEditDrawer.cs                -- generic typed drawer interface (legacy Inspector)
|   |-- DrawContext.cs                      -- rendering context record (IsReadOnly, IdPrefix, TypeRegistry)
|   |-- DrawerRegistry.cs                   -- type-keyed drawer dictionary
|   |-- PrimitiveDrawers.cs                 -- float/int/bool/string stub drawers
|   |-- BlueprintRuntimeInspectorPane.cs    -- runtime blackboard/state inspector pane
|
|-- Internal/
|   |-- CaptureWindowRegistrar.cs           -- IWindowRegistrar that captures registrations without an ImGui dependency
|
|-- NodeDrawers/
|   |-- IBlueprintNodeDrawer.cs             -- per-node-type drawer interface (Handles/CreateSession)
|   |-- INodeEditSession.cs                 -- transactional node-edit session (Draw/Dispose)
|   |-- IEditService.cs / EditService.cs    -- editor mutation + undo/redo service (RecordPropertyEdit)
|   |-- BlueprintNodeDrawerRegistry.cs      -- registry mapping Node CLR type -> drawer
|   |-- NodeKindDescriptor.cs / NodeKindRegistry.cs -- palette entry metadata + registry
|   |-- ISharedStructTypeProvider.cs / (Reflection impl) -- discovers [BlackboardDtoStruct] types for Get/SetShared + Make/Break/SetMembers
|   |-- WhenNodeDrawer.cs                   -- drawer for WhenNode (4-mode inspector UI)
|   |-- ReadEqsResultNodeDrawer.cs          -- drawer for ReadEqsResultNode
|   |-- SpawnEqsSensorNodeDrawer.cs         -- drawer for SpawnEqsSensorNode
|   |-- FunctionCallNodeDrawer.cs           -- drawer for FunctionCallNode (CLR method picker)
|   |-- LiteralNodeDrawer.cs                -- drawer for LiteralNode's inline value editor
|   |-- ChannelCommandNodeDrawer.cs         -- drawer for ChannelCommandNode (read-only channel/action labels)
|   |-- SharedNodeDrawers.cs                -- GetSharedNodeDrawer / SetSharedNodeDrawer (VariableId + SharedTypeId picker)
|   |-- PlayMontageChainNodeDrawer.cs       -- drawer for BranchNode used as a montage-chain UI (requires animation queries)
|   |-- WhenNodePaletteEntries.cs           -- palette entries: WhenNode, ReadEqsResult, SpawnEqsSensor
|   |-- BlueprintNodePaletteEntries.cs      -- full built-in node-kind vocabulary + ChannelCommandEntries + NonChannelActionEntries
|   |-- BlueprintMathPaletteEntries.cs      -- Math/* function-call presets
|   |-- BlueprintCallablePaletteEntries.cs  -- reflection-discovered [BlueprintCallable] CLR helper entries
|   |-- BlueprintEventCatalog.cs / BlueprintEventDiscovery.cs / BlueprintEventPaletteEntries.cs
|   |                                       -- discovers [BlueprintEvent]/[EventTarget] custom events; "Publish: {Event}" + EventEntry palette entries
|   |-- MakeBreakStructPaletteEntries.cs    -- Make/Break/SetMembers palette triple per [BlackboardDtoStruct]
|   |-- EqsTemplateEntry.cs / EqsTemplateRegistry.cs -- editor-side catalog of EQS template assets
|   |-- NodeKindDescriptor.cs / EditorColors.cs
|
|-- Reload/
|   |-- QuickReloadService.cs               -- in-process hot reload (AST compile -> Roslyn -> collectible ALC -> coordinator)
|   |-- QuickReloadResult.cs                -- result record
|   |-- FullRebuildService.cs               -- out-of-process `dotnet build`
|   |-- FullRebuildResult.cs                -- result record
|   |-- BlueprintSignatureBuilder.cs        -- in-memory BlueprintAsset -> BlueprintSignature (no disk I/O)
|
|-- Runtime/
|   |-- BlueprintAttachService.cs           -- BlueprintAsset -> runtime blueprintId forwarder to BlueprintInstanceService.AttachToEntity
|   |-- BlueprintRuntimeWiring.cs           -- single source of truth wiring the Instance-Blueprint runtime into a ModuleHostKernel (editor + headless harness share it)
|   |-- CounterDemoBlueprint.cs             -- demo/sample blueprint wiring
|   |-- RunBlueprintOnEntityCommand.cs      -- editor command: run a blueprint on a selected entity
|
|-- Variables/
|   |-- BlueprintVariablesWindow.cs         -- legacy variables-list window (wrapped by Windows/BlueprintVariablesManagedWindow)
|   |-- GraphSignatureEditModel.cs          -- headless edit model for a Function graph's Inputs/Outputs list
|
|-- Visuals/
|   |-- BlueprintEditorTheme.cs             -- ImGui color/style constants
|   |-- IAttachmentProvider.cs              -- canvas attachment-decorator provider interface
|   |-- WhenNodeAttachmentProvider.cs / ConditionSummaryAttachment.cs -- inline condition-summary pill for WhenNode
|   |-- ReadEqsResultAttachmentProvider.cs / ReadEqsResultAttachment.cs
|   |-- EqsTemplateAttachmentProvider.cs / EqsTemplateAttachment.cs
|   |-- CrossAssetDependencyAttachmentProvider.cs / CrossAssetDependencyAttachment.cs -- cross-asset peer-reference arrows
|   |-- WhenFiringPulseRenderer.cs          -- ICustomCanvasRenderer: pulsing overlay when a WhenNode fires (Debug builds only)
|   |-- PreviewSynthesizer.cs               -- synthesizes attachment preview label text
|
|-- Windows/
    |-- BlueprintMyBlueprintWindow.cs       -- ManagedWindow hosting NodeEdit's MyBlueprintPanel (variables/functions/events list)
    |-- BlueprintMyBlueprintModel.cs        -- headless projection model for the My-Blueprint panel
    |-- BlueprintDetailsWindow.cs           -- ManagedWindow: resolves selection -> IBlueprintNodeDrawer session, or a reflection-based read-only summary
    |-- BlueprintVariablesManagedWindow.cs  -- ManagedWindow wrapper around Variables/BlueprintVariablesWindow
    |-- GraphSignatureWindow.cs             -- ManagedWindow: edits a Function graph's Inputs/Outputs
    |-- BlueprintBookmarksWindow.cs         -- ManagedWindow hosting NodeEdit's BookmarksPanel
    |-- VariableCreateModal.cs              -- name+type modal for variable creation (duplicate-name guarded)
```

---

## Public API Reference

### Root namespace — orchestration & DI

#### `BlueprintEditorServiceCollectionExtensions.AddBlueprintEditor(IServiceCollection)`

```csharp
public static IServiceCollection AddBlueprintEditor(this IServiceCollection services);
```

Registers `DirtyTracker`, `EditorSelectionStore`, `EditorState`, `BlueprintWindowRegistrar`
(as itself and as `Fdp.Toolkit.Runner.IWindowRegistrar`), and `BlueprintEditorModule`. **No
longer takes an `assetRootDirectory` parameter** — asset enumeration is the shared catalog's
job now (`Catalog/BlueprintAssetContributor`, registered by the host separately).

#### `BlueprintEditorModule` (sealed)

```csharp
public sealed class BlueprintEditorModule
{
    public BlueprintEditorModule(
        IWindowRegistrar windowRegistrar, DirtyTracker dirtyTracker,
        EditorSelectionStore selectionStore, EditorState editorState,
        IOutputConsole outputConsole, IBlueprintDebugSession? session = null);

    public IReadOnlyList<IBlueprintEditorWindow> Windows { get; }
    public void RegisterWindow(IBlueprintEditorWindow window);
    public void OnEditorActivated();     // attaches session, registers "Blueprint/{Title}" menu entries, calls OnActivated on each window
    public void OnEditorDeactivated();   // detaches session, calls OnDeactivated on each window
    public void DrawAllWindows();        // per-frame: draws every window with IsVisible == true
    public void OnReloadCompleted(ReloadCompletedInfo info);  // logs; on FullRebuild loads *.dbgmap.json files and registers them
}
```

Orchestrates only the **legacy** `IBlueprintEditorWindow` set. The `ManagedWindow`-based panels
under `Windows/`/`EntityBlueprints/` are independent of this class.

#### `BlueprintWindowRegistrar` (sealed) : `Fdp.Toolkit.Runner.IWindowRegistrar`

```csharp
public sealed class BlueprintWindowRegistrar : Fdp.Toolkit.Runner.IWindowRegistrar
{
    public BlueprintWindowRegistrar(
        EditorSelectionStore selectionStore, DirtyTracker dirtyTracker, EditorState editorState,
        IBlueprintDebugSession session, IBlueprintEditorCoordinator coordinator,
        DrawerRegistry drawerRegistry);

    public void RegisterWindows(IBlueprintWindowRegistry registry);
    // engine entry point: void EngineWindowRegistrar.RegisterWindows(WindowManager wm)
}
```

`RegisterWindows` registers factories for **Inspector**, **Debug Panel**, **Watch Panel**,
**Callstack**, and **Hot Reload Log**. Its explicit `Fdp.Toolkit.Runner.IWindowRegistrar`
implementation wraps the engine `WindowManager` in a private `WindowManagerRegistry` and calls
`RegisterWindows(IBlueprintWindowRegistry)`, so each registered window becomes a
`BlueprintManagedWindowAdapter : ManagedWindow` with lazy (on-first-render) instantiation.

#### `BlueprintManagedWindowAdapter` (internal) : `ManagedWindow`

Bridges any `IBlueprintEditorWindow` factory into the engine `ManagedWindow` contract; the
underlying window is created on first `DrawClientArea()` call, and `Title` is refreshed from
the wrapped window every frame (so `DebugPanelWindow`'s `"Debug [PAUSED]"` title updates live).

#### `BlueprintEditorBootstrap` (static)

```csharp
public static class BlueprintEditorBootstrap
{
    public static BlueprintNodeDrawerRegistry CreateNodeDrawerRegistry(
        IChannelCommandCatalog channelCatalog, IEngineEventCatalog eventCatalog,
        IEditService editService, IPredicateCompiler predicateCompiler,
        EqsTemplateRegistry eqsTemplates, IAnimationTkbQueries? animationQueries = null,
        Func<string?>? currentClassProvider = null,
        ISharedStructTypeProvider? sharedStructTypeProvider = null);

    public static NodeKindRegistry CreatePaletteRegistry(
        IChannelCommandCatalog? channelCatalog = null,
        IBehaviorActionCatalog? behaviorActionCatalog = null);

    public static List<IAttachmentProvider> CreateAttachmentProviders(
        EqsTemplateRegistry eqsTemplates, Func<Guid, string?> peerNameResolver);

    public static List<ICustomCanvasRenderer> CreateCanvasRenderers();
    public static void DrawBookmarkEdgeMarkers(GraphView view, BookmarkStore store, IEditorTheme theme);
    public static List<BlueprintAsset> DiscoverRecipes();
}
```

- `CreateNodeDrawerRegistry` registers **8 drawers**: `WhenNode` → `WhenNodeDrawer`,
  `ReadEqsResultNode` → `ReadEqsResultNodeDrawer`, `SpawnEqsSensorNode` →
  `SpawnEqsSensorNodeDrawer`, `FunctionCallNode` → `FunctionCallNodeDrawer`, `LiteralNode` →
  `LiteralNodeDrawer`, `ChannelCommandNode` → `ChannelCommandNodeDrawer`, `GetSharedNode`/
  `SetSharedNode` → `GetSharedNodeDrawer`/`SetSharedNodeDrawer`, and — only when
  `animationQueries` and `currentClassProvider` are both supplied — `BranchNode` →
  `PlayMontageChainNodeDrawer` (a montage-chain UI layered onto a plain Branch node).
- `CreatePaletteRegistry` registers the 3 When-vocabulary entries, the full built-in vocabulary
  (`BlueprintNodePaletteEntries.All()`), one entry per channel-command action (when
  `channelCatalog` is supplied), one entry per non-channel behavior action (when
  `behaviorActionCatalog` is supplied), `BlueprintMathPaletteEntries.All()`, reflection-discovered
  `[BlueprintCallable]` entries, one "Publish: {Event}" entry per discovered custom event, and one
  Make/Break struct pair per `[BlackboardDtoStruct]`.
- `CreateAttachmentProviders` returns `WhenNodeAttachmentProvider`,
  `ReadEqsResultAttachmentProvider`, `EqsTemplateAttachmentProvider`,
  `CrossAssetDependencyAttachmentProvider`.
- `CreateCanvasRenderers` returns `WhenFiringPulseRenderer` in Debug builds only
  (`#if DEBUG`, compiled out in Release — not a runtime flag).
- `DrawBookmarkEdgeMarkers` is called directly by the host (not through the
  `ICustomCanvasRenderer` pass) once per frame after the canvas renders.
- `DiscoverRecipes` enumerates `*.bp.json` under `Hrot.AI.Behaviors`'s production recipes
  folder, returning only assets with non-null `EditorMetadata.Recipe`.

#### Core stores

```csharp
public sealed class DirtyTracker
{ void MarkDirty(Guid); void MarkClean(Guid); bool IsDirty(Guid); IReadOnlySet<Guid> DirtyAssets; }

public sealed class EditorSelectionStore
{ BlueprintAsset? SelectedAsset; event Action? OnSelectionChanged; void SelectAsset(BlueprintAsset?); }

public sealed class EditorState
{
    void SetInMemoryAsset(BlueprintAsset); BlueprintAsset? GetInMemoryAsset(Guid);
    void RemoveInMemoryAsset(Guid); IReadOnlyDictionary<Guid, BlueprintAsset> InMemoryAssets;
}
```

Unchanged in shape from earlier revisions of this doc; still the in-memory overlay
`QuickReloadService` reads for sibling signatures.

#### `BlueprintPeerSource` (sealed) — replaces the retired `IAssetCatalog`

```csharp
public sealed class BlueprintPeerSource
{
    public BlueprintPeerSource(string rootDirectory);
    public IEnumerable<(Guid AssetId, string Path)> EnumerateAll();
}
```

Header-only `*.bp.json` scan (case-insensitive extension match, `IgnoreInaccessible: true`).
Used by `QuickReloadService.BuildSiblingSignatures` and
`BlueprintDocumentFactory.BuildPeerSignatureLookup` to resolve peer `BlueprintSignature`s for
`CallPeerBlueprintNode` pin typing — the old `IAssetCatalog`/`AssetCatalogEntry`/
`FileSystemAssetCatalog` types it replaced are gone entirely (retired when the standalone Asset
Browser window moved to the shared catalog).

#### `SaveActiveBlueprintCommand` (sealed)

```csharp
public sealed class SaveActiveBlueprintCommand
{
    public enum SaveStatus { Saved, NoBlueprintOpen, NoSourcePath }
    public sealed class SaveResult { public SaveStatus Status { get; } /* ... */ }
}
```

Implements the **projection-only** invariant (DEBT-BCP-005): before serialization, every node's
`Pins` list is temporarily swapped to an empty sentinel and restored in a `finally` block, so the
live in-memory asset is never left mutated even if serialization throws, and the saved file keeps
`"Pins": []` for byte-stable diffs.

#### `BlueprintNewAssetService` (sealed) : `INewAssetService`

Creates new in-memory `BlueprintAsset`s from a recipe (`NewFromRecipeService.CreateFromRecipe`)
or from a hardcoded "Empty" recipe built in its constructor.

---

### Catalog namespace (`Hrot.Blueprints.Editor.Catalog`)

#### `BlueprintAssetContributor` (sealed) : `IAssetCatalogContributor`

```csharp
public sealed class BlueprintAssetContributor : IAssetCatalogContributor
{
    public BlueprintAssetContributor(string rootDirectory);
    public AssetKind Kind => AssetKind.Blueprint;
    public string? BaseFolder { get; }
    public event Action? ContributorChanged;
    public IReadOnlyList<IEditableAsset> Enumerate();
    public void Refresh();   // rescans *.bp.json, header-only (AssetId, Name, Dispatch, Primitive.Intent)
}
```

Plugs Blueprint assets into the shared `Hrot.Editor.AiShared.Catalog` asset browser. Produces
`BlueprintFileAsset : IEditableAsset, IComposedBlueprintIdentity, IAssetIconKeyProvider` —
lightweight, header-only; the full `BlueprintAsset` is loaded lazily by
`BlueprintDocumentFactory.LoadAsset` when the document is opened.
`BlueprintFileAsset.GeneratedClassName` precomputes the AiPrimitive-composed class name
(`"{SanitizedName}_{BlueprintIdHash:X8}_Bp"`) so BTree-side FQN matching stays Roslyn-free.

#### `BlueprintReferenceContributor` (sealed) : `IReferenceCatalogContributor`

Exposes each Blueprint asset as a referenceable `IAssetSubElement` by asset-id key (matching
`CallPeerBlueprintNode.PeerBlueprintId`'s `"D"`-formatted string) and by its composed
AiPrimitive `TickCore` FQN key (for BTree nodes that host it). Per-node reference enumeration is
deferred (Phase 2) since the header-only asset can't walk the graph without full deserialization.

#### `BlueprintIconKeys` (internal static)

Maps a header's `Dispatch` + `Primitive.Intent` to a picker icon key: `Library` → Function,
`AiPrimitive`+`Condition` → Condition, `AiPrimitive`+`Action` → Action, else `null` (kind default).

---

### Host namespace (`Hrot.Blueprints.Editor.Host`)

#### `BlueprintDocumentFactory.Build(...)` (static)

```csharp
public static AiCanvasContext Build(
    IEditableAsset asset, AiEditorAdapterBundle bundle, EditService? editService = null,
    NodeKindRegistry? paletteRegistry = null,
    IReadOnlyList<ICustomCanvasRenderer>? extraRenderers = null,
    IChannelCommandCatalog? channelCommands = null, BlueprintPeerSource? peerAssetCatalog = null,
    ActionCatalog.IBehaviorActionCatalog? behaviorActions = null,
    IBlueprintDebugSession? debugSession = null);
```

The single entry point for opening a Blueprint document on the shared canvas. See Diagram 1
above for the exact construction order. Also hosts two headless-testable helpers used by the
My-Blueprint "+" and variable-create-modal flows:

```csharp
internal static VariableDecl AddVariable(BlueprintAsset asset, Action? markDirty = null);
internal static VariableDecl? CreateVariable(BlueprintAsset asset, string name, string typeId, Action? markDirty = null);
internal static bool IsDuplicateVariableName(BlueprintAsset asset, string name);
```

`CreateVariable` **rejects** (returns `null`, no rename/suffix) blank or case-insensitively
duplicate names — the caller (the create modal) is expected to warn the user and disable
Confirm, but this method is the authoritative guard.

#### `BlueprintGraphModel` (sealed) : `IGraphModel`

```csharp
public sealed class BlueprintGraphModel : IGraphModel
{
    public BlueprintGraphModel(
        BlueprintAsset asset, Graph graph, NodeKindRegistry? kindRegistry = null,
        IChannelCommandCatalog? channelCommands = null,
        Func<Guid, BlueprintSignature?>? peerSignatureLookup = null,
        IPinDefaultValueEditorRegistry? editorRegistry = null,
        IEnumValueProvider? enumProvider = null,
        ActionCatalog.IBehaviorActionCatalog? behaviorActions = null);

    public GraphId Id { get; }
    public IReadOnlyCollection<INodeModel> Nodes { get; }
    public IReadOnlyCollection<ILinkModel> Links { get; }
    public IReadOnlyCollection<ICommentModel> Comments { get; }
    public event Action<GraphChangeNotification>? Changed;

    public INodeModel? FindNode(NodeId); public IPinModel? FindPin(PinId);
    public ILinkModel? FindLink(LinkId); public ICommentModel? FindComment(CommentId);

    public void Rebuild();                                  // full two-pass re-projection
    public void NotifyChanged();                            // fires Changed(Wholesale)
    public void NotifyMoved(IReadOnlyCollection<NodeId>);    // fires Changed(NodesMoved), no rebuild
    public void RebuildAndNotify();

    public static LinkId MakeLinkId(Guid fromPinId, Guid toPinId);   // deterministic
    internal Link? FindAssetLink(LinkId id);
}
```

#### `NodePinSchema` (internal static)

```csharp
internal static class NodePinSchema
{
    public static IReadOnlyList<Pin> GetCanonicalPins(
        Node node, NodeKindRegistry? registry = null, BlueprintAsset? asset = null,
        IChannelCommandCatalog? channelCommands = null, Graph? containingGraph = null,
        Func<Guid, BlueprintSignature?>? peerSignatureLookup = null,
        IBehaviorActionCatalog? behaviorActions = null);
}
```

The single source of truth for "what pins does this node kind have, right now, given this
asset/graph/catalog context." Resolution order: asset-authored pins (test builders) → literal
inline-editor special case → `NodeKindRegistry` descriptor → dynamic per-kind computation
(`EventEntryNode`, `ReturnNode`, `FunctionCallNode` CLR-vs-graph-call dispatch, `GetVariableNode`/
`SetVariableNode`, `GetParameterNode`, `GetAllParametersNode`, `GetSharedNode`/`SetSharedNode`,
`MakeStructNode`/`BreakStructNode`/`SetMembersNode`, `ChannelCommandNode` channel-vs-non-channel
dispatch, `PublishEventNode`, `CallCustomEventNode`, `CallPeerBlueprintNode`) → static
`BuiltInNodeRegistry` fallback. Every branch's doc comment cites the exact compiler stage
(`Stage0_Rehydrate`, `Stage5_Schedule`) it must stay in parity with — treat this file as load-bearing
whenever a node kind's pin shape changes on the compiler side.

#### `BlueprintCommandSink` (sealed) : `IGraphCommandSink`

```csharp
public sealed class BlueprintCommandSink : IGraphCommandSink
{
    public BlueprintCommandSink(
        BlueprintAsset asset, Graph graph, BlueprintGraphModel model, BlueprintNodeCatalog catalog,
        BlueprintLinkValidator validator, CommandHistory history, EditService editService,
        Action<BlueprintAsset> markDirty, IChannelCommandCatalog? channelCommands = null,
        IEnumValueProvider? enumProvider = null,
        ActionCatalog.IBehaviorActionCatalog? behaviorActions = null);

    public GraphCommandResult Apply(GraphCommand command);
}
```

Dispatches on the `GraphCommand` discriminated union: `AddNode`, `RemoveNodes`, `AddLink`,
`RemoveLinks`, `MoveNodes`, `ChangeParentMultiple`, `SetNodeProperty`, `SetPinDefault`,
`InsertReroute`/`MoveReroute`/`RemoveReroute`, `AddComment`/`UpdateComment`/`RemoveComment`,
`Batch`. Notably: `ApplyAddLink` auto-replaces a conflicting exec-out fan-out or data-input link
as **one** undoable `LinkEditCommand` step; `ApplyPinIds` stamps caller-supplied pin GUIDs
(from the wire-drop auto-connect path) onto a freshly created node's canonical pins so the
paired `AddLink` resolves; Literal pin-default edits write into `LiteralNode.ValueJson`
(formatted as a C# literal) rather than the generic `PinDefaults` map; enum pin defaults persist
as the member-name string (`ENUM-NAME`), not the raw integer.

#### `BlueprintNodeModel` (internal sealed) : `INodeModel`

`BuildTitle` is the canonical node-title formatter — worth knowing when debugging canvas
display: `FunctionCallNode` → method name (or "Function Call"); `GetVariableNode`/
`SetVariableNode` → `"Get {name}"`/`"Set {name}"` resolved from `asset.Variables`;
`LiteralNode` → inline-editable types show `"Literal ({Type})"`, others show the formatted
value directly; `CompareNode`/`BinaryOpNode`/`BooleanOpNode`/`NotNode` → operator symbol
(`==`, `+`, `&&`, `!`); `PublishEventNode`/`CallCustomEventNode`/`WaitForEventNode` → short event
name (last FQN segment); `ChannelCommandNode` → `"Command: {ActionId}"`. A CLR
`FunctionCallNode` whose method can no longer be resolved by reflection sets
`NodeState.Error` with a `StatusTooltip` explaining the mismatch.

#### `BlueprintEditorHostServices`, `BlueprintNodeCatalog`, `BlueprintTypeSystem`, `BlueprintLinkValidator`

Standard NodeEdit host-service quartet (`IEditorHostServices`, `INodeCatalog`, type-compat
surface, `ILinkValidator`) — see the layer table above for their roles; signatures are stable
DI-style constructors taking the sibling Host/ services plus the engine `AiEditorAdapterBundle`.

---

### ActionCatalog namespace

#### `IBehaviorActionCatalog` / `BehaviorActionCatalog`

```csharp
[Flags] public enum BehaviorActionHosts { None = 0, Blueprint = 1, BTree = 2, Hsm = 4 }
public enum BehaviorActionSource { ChannelCommand, Hardcoded, AiPrimitive }

public sealed record BehaviorActionEntry(
    string Id, string DisplayName, string? Category, string? ChannelTypeFqn,
    ushort ActionId, string ParamsTypeFqn, BehaviorActionHosts ValidHosts, BehaviorActionSource Source);

public interface IBehaviorActionCatalog
{
    IReadOnlyList<BehaviorActionEntry> GetActions();
    IReadOnlyList<BehaviorActionEntry> GetActions(BehaviorActionHosts host);
    event Action? Changed;
}
```

`BehaviorActionCatalog` composes `IChannelCommandCatalog` (→ `ChannelCommand` entries, always
`Blueprint`-hosted) with `IActionSchemaExporter` (→ `Hardcoded`/`AiPrimitive` entries, hosted per
`ActionHosting` flags; `ActionHosting.Shared` additionally maps to `BehaviorActionHosts.Blueprint`
— this is the AN7 mechanism that lets `[SharedAiAction]`/compiled-AiPrimitive actions be invoked
from a Blueprint graph's generalized `ChannelCommandNode.ActionFqn` path). The snapshot rebuilds
atomically (volatile reference swap) on `IActionSchemaExporter.Changed`.

---

### Debug namespace (`Hrot.Blueprints.Editor.Debug`)

#### `DebugPanelWindow`, `WatchPanelWindow`, `CallstackWindow` — fully implemented, not stubs

```csharp
public sealed class DebugPanelWindow : BlueprintEditorWindowBase
{
    public override string Title => _session.IsPaused ? "Debug [PAUSED]" : "Debug";
    public bool? LastRenderedPausedState { get; }                       // test-observable
    public IReadOnlyList<Breakpoint>? LastRenderedBreakpoints { get; }  // test-observable
    public string? LastStepActionInvoked { get; }                       // test-observable
}
```

`DebugPanelWindow` renders the shared `DebugStepControls` row plus, while paused, a
Node ID / Asset ID / Hits breakpoint table. `WatchPanelWindow` subscribes/unsubscribes
`OnPinValueChangedEvent` in `OnActivated`/`OnDeactivated` and renders a Name/Type/Value/Tick
table (hex-dumped raw bytes, `[stale]` suffix for `Watch.IsStale`). `CallstackWindow` reads
`IBlueprintDebugSession.GetCurrentCallStack()` and renders a Depth/Asset/Method table, or
`"No call stack."` when empty. All three skip ImGui calls under
`ImGui.GetCurrentContext() == IntPtr.Zero` so they're exercised headlessly in tests via their
`LastRendered*` capture fields.

#### `DebugStepControls` (static)

```csharp
public static class DebugStepControls
{
    public static void Draw(IBlueprintDebugSession session, Action<string>? onStepAction = null);
    public static string FormatNodePosition(IBlueprintDebugSession session);  // "node {p+1} / {count}", testable w/o ImGui
}
```

Renders Continue / **Step Back** / Step Over / Step Into / Step Out. Step Back
(node-granular rewind, NGS-2.4c) is disabled when `session.CurrentNodePointer == 0`.

#### `ExecSuccessors` (static)

```csharp
public static class ExecSuccessors
{ public static IReadOnlyList<Guid> GetSuccessors(Graph graph, Guid nodeId); }
```

Mirrors the compiler's `Stage5_Schedule` successor-following logic (handles both pin-less
projection-only nodes via `BuiltInNodeRegistry` rehydration and pin-carrying nodes); used by
step logic to compute where to plant temporary one-shot breakpoints.

#### `BlueprintDebugToNodeEditAdapter` (sealed) : `IDebugSession`

Bridges `IBlueprintDebugSession` to NodeEdit's native `IDebugSession` so `NodeRenderer` draws
breakpoints/execution overlays without any Blueprint-aware paint code. `CurrentlyExecutingNode`
prioritizes (1) the virtual step-pointer's current node while paused (NGS-2.4b — makes
Step-Back/Into/Over move the highlight live), then (2) `PausedAt`, then (3) the most recent
execution-history entry.

#### `AiDebugCommands` (static)

Registers the polymorphic "AI Debug" toolbar group (`debug.continue`/`stepOver`/`stepInto`/
`stepOut`/`pause`, common to any `IAiDebugSession`) plus the Blueprint-only `debug.stepBack`
(present only when `IDebugSessionRegistry.ActiveSession is IBlueprintDebugSession`). Exposes
`BuildGroupModel`/`NodePositionText` as headless seams for the render path.

#### `BlueprintDebugSession` (sealed, in `Hrot.Blueprints.Core.Debug` namespace)

Full production `IBlueprintDebugSession` + `Hrot.Editor.AiShared.Debug.IAiDebugSession`
implementation, despite living in this Editor project. Two parallel breakpoint/watch
dictionaries (by id, and by node/pin-string for O(1) hot-probe lookup); per-entity
`ExecutionHistory` ring buffers; per-entity call-frame stacks for `GetCurrentCallStack()`;
graph registration (`RegisterGraph`) so `ExecSuccessors` has structure to step through; soft-pause
semantics (`HandleBreakpointHit` sets `_isPaused` and calls `_timeController.RequestPause()`
without blocking the calling thread — the tick completes, the engine pauses at the next frame
boundary).

#### `MasterSyncTimeControllerAdapter` (sealed) : `IEngineDebugTimeController`

Bridges `MasterSyncController` — transitioning to deterministic mode with an empty slave roster
pauses the local sim clock without waiting for network acks; `RequestStepOneTick` steps `1/60s`
while paused.

---

### NodeDrawers namespace

`IBlueprintNodeDrawer` (`Handles(Node)` / `CreateSession(Node, BlueprintAsset)`) +
`INodeEditSession` (`Draw()` / `Dispose()`) is the drawer contract; `BlueprintDetailsWindow`
(Windows/) is the one production consumer that resolves a drawer for the current selection.

| Drawer | Node kind | Notes |
|--------|-----------|-------|
| `WhenNodeDrawer` | `WhenNode` | 4-section inspector: dispatch guard, mode selector (ValueChanged/EventFired/ConditionMet/EqsResult), mode-specific sub-form, RisingEdge/FallingEdge checkboxes, preview pill via `PreviewSynthesizer`. |
| `ReadEqsResultNodeDrawer` | `ReadEqsResultNode` | Dispatch guard + combo over `EqsSensorHandle`-typed variables. |
| `SpawnEqsSensorNodeDrawer` | `SpawnEqsSensorNode` | Dispatch guard + template picker over `EqsTemplateRegistry`. |
| `FunctionCallNodeDrawer` | `FunctionCallNode` | CLR method picker; reads/writes `TargetTypeId`/`MethodName` via `IEditService`. |
| `LiteralNodeDrawer` | `LiteralNode` | Typed inline value editor (delegates formatting to `LiteralValueJson`). |
| `ChannelCommandNodeDrawer` | `ChannelCommandNode` | Read-only `ChannelType`/`ActionId` labels — action is baked at palette-creation time, no in-place mutation path. |
| `GetSharedNodeDrawer` / `SetSharedNodeDrawer` | `GetSharedNode` / `SetSharedNode` | `VariableId` (free text) + `SharedTypeId` (filtered picker over `ISharedStructTypeProvider`), editable post-placement. |
| `PlayMontageChainNodeDrawer` | `BranchNode` | Montage-chain UI over a plain Branch node; only registered when animation queries are supplied. |

Palette-entry factories (`NodeKindDescriptor` producers registered into `NodeKindRegistry`):
`WhenNodePaletteEntries` (When/ReadEqsResult/SpawnEqsSensor), `BlueprintNodePaletteEntries`
(`All()` built-in vocabulary + `ChannelCommandEntries`/`NonChannelActionEntries`),
`BlueprintMathPaletteEntries` (Math/* presets), `BlueprintCallablePaletteEntries` (reflection
over `[BlueprintCallable]`), `BlueprintEventDiscovery`/`BlueprintEventPaletteEntries`
(reflection over `[BlueprintEvent]`/`[EventTarget]` → "Publish: {Event}" + `EventEntry`
Self/Any-filter entries), `MakeBreakStructPaletteEntries` (Make/Break/SetMembers triple per
`[BlackboardDtoStruct]`, via `ISharedStructTypeProvider`).

`EqsTemplateRegistry`/`EqsTemplateEntry` — editor-side catalog of EQS template assets
(`Register`/`EnumerateAll`/`TryGet(Guid)`); distinct from the compiler-facing
`IEqsTemplateCatalog` (which exposes only `Contains(Guid)`).

---

### EntityBlueprints namespace

`EntityBlueprintsEditModel` is the headless view-model behind the "Entity Blueprints" runtime
authoring panel: `RefreshReality()` scans all three blackboard-tier components
(`BlueprintBlackboard1024`/`4096`/`16384`) on a live entity via `EntityRepository`;
`StageAdd`/`StageRemove`/`RevertAll` manage a pending edit set; `ComputeProjection()` returns a
`Projection(Slots, Bytes, Tier, Status)` predicting the post-commit blackboard tier and whether
it needs an upgrade or exceeds the ceiling; `BuildCommitPlan(CommitTiming)` emits either a
paused-mode `CommitPlan` (direct `AttachBlueprintIds`/`DetachBlueprintIds` + optional
`UpgradeToTier`) or a running-mode plan (`AttachInstanceBlueprintEvent`/
`RemoveInstanceBlueprintEvent` records for the event bus). `EntityBlueprintsPanel` renders it;
`EntityBlueprintsManagedWindow` wraps the panel behind a lazy `ManagedWindow` factory.

---

### Reload namespace

#### `QuickReloadService` (sealed)

```csharp
public sealed class QuickReloadService
{
    public IReadOnlyList<BlueprintSignature>? LastSignaturesUsedForTesting { get; }

    public QuickReloadService(
        BlueprintPeerSource catalog, EditorState editorState, IOutputConsole outputConsole,
        IBlueprintCompiler compiler, AiHotReloadCoordinator coordinator,
        IBlueprintDebugSession? session = null);

    public Task<QuickReloadResult> TriggerAsync(BlueprintAsset asset);

    public Task<QuickReloadResult> TriggerFromSourcesAsync(
        IReadOnlyList<(string Source, string VirtualPath)> sources, string assemblyName,
        DebugMap? debugMap = null, Guid? assetIdForDebugMap = null);
}
```

Constructor now takes `BlueprintPeerSource` (not the retired `IAssetCatalog`).
`TriggerFromSourcesAsync` is the **kind-agnostic** half of the pipeline (Roslyn compile → ALC
load → `HsmActionDispatcher.ClearAll()` → `BlueprintRegistrarScanner.Scan` into staging
registries → register debug map → `AiHotReloadCoordinator.ApplyQuickReload` atomic swap) —
`TriggerAsync` is the Blueprint-specific half that AST-compiles first and then delegates to it,
so BTree/HSM hot reload can reuse the same tail pipeline.

Registrar injection rules (unchanged): `BlueprintRegistry` and `HsmActionDispatcher` are
**forbidden** injection types (RCU-contract / static-class violations respectively);
`BehaviorRegistry`, `IPredicateCompiler`, `ISearchPredicateRegistry` are supported.

#### `FullRebuildService` (sealed)

```csharp
public sealed class FullRebuildService
{
    public bool PendingDrainAfterBuild { get; }
    public FullRebuildService(IOutputConsole outputConsole, string buildTarget = "");
    public async Task<FullRebuildResult> TriggerAsync();
}
```

Unchanged in shape: spawns `dotnet build [buildTarget]`, streams stdout/stderr line-by-line to
`IOutputConsole`, sets `PendingDrainAfterBuild = true` on a zero exit code.

#### `BlueprintSignatureBuilder` (static)

```csharp
public static class BlueprintSignatureBuilder
{ public static BlueprintSignature FromInMemoryAsset(BlueprintAsset asset); }
```

---

### Variables / Windows namespaces

`GraphSignatureEditModel` is the headless mutation model behind `Windows/GraphSignatureWindow`
(add/rename/retype/remove a Function graph's `Inputs`/`Outputs` `ParameterDecl` list; each
mutation invokes an `onChanged` callback that marks the asset dirty).

`Windows/BlueprintMyBlueprintWindow` hosts NodeEdit's `MyBlueprintPanel` behind a
`BlueprintMyBlueprintModel` projection, retargeted on active-document change; it also owns a
`VariableCreateModal` wired to the `editor.create-variable` command
(`BlueprintDocumentFactory.RegisterCreateVariableCommand(commands, openModal)` overload) so the
My-Blueprint "+" button opens the name/type modal instead of silently creating `NewVar`.

`Windows/BlueprintDetailsWindow` is the modern replacement for the legacy `InspectorWindow`'s
Node tab: it resolves the active `BlueprintNodeSelection` to an `IBlueprintNodeDrawer` +
`INodeEditSession` via `BlueprintNodeDrawerRegistry`, and falls back to a reflection-driven
read-only property summary for node kinds with no registered drawer (distinguishing "nothing
selected" from "selected but not editable").

`Windows/GraphSignatureWindow`, `Windows/BlueprintVariablesManagedWindow`,
`Windows/BlueprintBookmarksWindow` follow the same `ManagedWindow` + `Retarget(asset)` pattern.

---

## Dependencies

### Project references

| Reference | Purpose |
|-----------|---------|
| `Hrot.Blueprints.Core` | Asset model, compiler pipeline, debug interfaces, `BuiltInNodeRegistry`/catalogs. |
| `Hrot.Diagnostics.Breakpoints` | `IDataBreakpointManager`, `BreakpointId`, `SearchPredicateDto` hierarchy — consumed by `BlueprintBreakpointMenuPopulator`. |
| `Hrot.Editor.AiShared` | Shared perspective/window/document/catalog/selection/refactor infrastructure this project plugs into (`IAssetCatalogContributor`, `AiCanvasContext`, `AiDocumentManager`, `EditorSelectionStore` (AiShared), `IActionSchemaExporter`, `IRefactorService`, `INewAssetService`, recipe metadata). |
| `Fdp.Core` | `Entity`, `ISimulationView`, core ECS primitives. |
| `Fdp.Presentation` | ImGui host (`ImGuiNET`), `WindowManager`/`ManagedWindow`. |
| `Fdp.Toolkits` | `AiHotReloadCoordinator`, `BlueprintRegistry`/`BlueprintRegistryStaging`, `BehaviorRegistry`, `BlueprintIdHash`, `HsmActionDispatcher`, `MasterSyncController`, `BlueprintInstanceService`, blackboard tier components. |
| `NodeEditor.Core` / `NodeEditor.UI` (ExtDeps) | The canvas library itself: `IGraphModel`/`INodeModel`/`IPinModel`/`ILinkModel`/`ICommentModel`, `GraphView`, `IGraphCommandSink`, `ILinkValidator`, `INodeCatalog`, `IEditorHostServices`, `IDebugSession`, bookmarks, find bar. |

### NuGet packages

| Package | Purpose |
|---------|---------|
| `Microsoft.Extensions.DependencyInjection` | DI container for `AddBlueprintEditor`. |
| `Microsoft.CodeAnalysis` (Roslyn) | Transitive via `Hrot.Blueprints.Core`; `IOutputConsole.LogDiagnostic` accepts `Microsoft.CodeAnalysis.Diagnostic` directly. |

---

## Usage Examples

### Example 1 — DI registration + activating the legacy windows

```csharp
var services = new ServiceCollection();
services.AddBlueprintEditor();   // DirtyTracker, EditorSelectionStore, EditorState,
                                  // BlueprintWindowRegistrar, BlueprintEditorModule

services.AddSingleton<IWindowRegistrar, MyWindowRegistrar>();
services.AddSingleton<IOutputConsole, MyOutputConsole>();
services.AddSingleton<IBlueprintDebugSession, BlueprintDebugSession>();
services.AddSingleton<IBlueprintEditorCoordinator, MyCoordinator>();
services.AddSingleton<DrawerRegistry>();

var provider = services.BuildServiceProvider();
var module = provider.GetRequiredService<BlueprintEditorModule>();

// Legacy windows are added directly to the module (Inspector/Debug/Watch/Callstack/HotReloadLog
// are normally supplied via BlueprintWindowRegistrar.RegisterWindows instead — shown here for clarity).
module.RegisterWindow(new InspectorWindow(
    provider.GetRequiredService<EditorSelectionStore>(),
    provider.GetRequiredService<DirtyTracker>(),
    provider.GetRequiredService<DrawerRegistry>()));

module.OnEditorActivated();
// Per-frame: module.DrawAllWindows();
```

### Example 2 — Opening a document on the shared canvas

```csharp
var docFactory = () => Host.BlueprintDocumentFactory.Build(
    asset:            blueprintFileAsset,          // IEditableAsset (BlueprintFileAsset)
    bundle:            aiEditorAdapterBundle,        // engine pickers/clipboard/icons/theme
    editService:       sharedEditService,
    paletteRegistry:   BlueprintEditorBootstrap.CreatePaletteRegistry(channelCatalog, behaviorCatalog),
    channelCommands:   channelCatalog,
    peerAssetCatalog:  new BlueprintPeerSource(blueprintAssetRoot),
    behaviorActions:   behaviorActionCatalog,
    debugSession:      debugSession);

var canvasContext = docFactory();   // AiCanvasContext -- hand to the AiDocumentManager / canvas window
```

### Example 3 — Triggering a Quick Reload

```csharp
var quickReload = new QuickReloadService(
    new BlueprintPeerSource(blueprintAssetRoot), editorState, outputConsole, compiler, coordinator, debugSession);

BlueprintAsset asset = editorState.GetInMemoryAsset(myAssetId)!;
var result = await quickReload.TriggerAsync(asset);

if (result.Succeeded)
    dirtyTracker.MarkClean(asset.AssetId);
else
    outputConsole.LogError($"Reload failed: {result.ErrorMessage}");
```

### Example 4 — Setting a breakpoint and stepping

```csharp
IBlueprintDebugSession session = provider.GetRequiredService<IBlueprintDebugSession>();

Guid assetId = asset.AssetId;
Guid graphId = asset.Graphs[0].Id;
Guid nodeId  = asset.Graphs[0].Nodes[0].Id;
var bpId = session.SetBreakpoint(assetId, graphId, nodeId);

session.OnBreakpointHit += hit =>
    Console.WriteLine($"Breakpoint hit at node {hit.NodeId} by entity {hit.Entity}");

// ... after the probe fires and the session pauses ...
if (session.IsPaused)
{
    session.StepOver();     // or StepInto() / StepOut() / StepBack() / Continue()
}

session.ClearBreakpoint(bpId);
```

### Example 5 — Saving the active asset

```csharp
var save = new SaveActiveBlueprintCommand(/* ... */);
var result = save.Execute();   // strips Pins to [] around serialization, restores in a finally block

switch (result.Status)
{
    case SaveActiveBlueprintCommand.SaveStatus.Saved:           dirtyTracker.MarkClean(asset.AssetId); break;
    case SaveActiveBlueprintCommand.SaveStatus.NoBlueprintOpen: outputConsole.LogWarning("Nothing to save."); break;
    case SaveActiveBlueprintCommand.SaveStatus.NoSourcePath:    outputConsole.LogError("No source path."); break;
}
```

---

## Best Practices

1. **Never persist `Node.Pins`.** Pins are an editor projection (`NodePinSchema`); saved assets
   must keep `"Pins": []`. `SaveActiveBlueprintCommand` enforces this around serialization —
   don't bypass it with a raw `JsonSerializer.Serialize(asset)` call.
2. **Register windows before activating.** `BlueprintEditorModule.OnEditorActivated` iterates
   `_windows` once; anything registered afterward won't get a menu entry or `OnActivated`.
3. **Never inject `BlueprintRegistry` or `HsmActionDispatcher` into a registrar.**
   `QuickReloadService` throws `HotReloadRegistrarException` — the RCU contract requires the
   atomic staging→commit handoff to own the write path exclusively.
4. **Route sibling-signature resolution through `EditorState`/`BlueprintPeerSource`, not raw
   disk reads.** `QuickReloadService.BuildSiblingSignatures` prefers the in-memory override for
   any asset with unsaved edits; call `EditorState.SetInMemoryAsset` after every edit so peers
   see the current signature.
5. **Detach the debug session before disposing the host.** `BlueprintDebugSession.Detach()`
   resumes if paused and clears all breakpoints/watches/history — skipping it can leave the
   simulation permanently paused.
6. **Subscribe/unsubscribe session events in `OnActivated`/`OnDeactivated`**, not the
   constructor — `WatchPanelWindow` is the reference pattern.
7. **Keep `NodePinSchema` and the compiler's `Stage0_Rehydrate`/`Stage5_Schedule` in lockstep.**
   Any pin-shape change to a node kind on the compiler side must be mirrored here, or existing
   wires render "unused" on the canvas even though the compiler still consumes them correctly.
8. **A non-channel `ChannelCommandNode` needs `behaviorActions` threaded everywhere.** Both
   `BlueprintGraphModel` (pin projection) and `BlueprintCommandSink.ApplyPinIds` (re-stamping on
   node creation) must receive the same `IBehaviorActionCatalog`, or the node silently degrades
   to exec-only pins.

---

## Related Projects

| Project | Relationship |
|---------|--------------|
| `Hrot.Blueprints.Core` | Asset model, compiler pipeline, `IBlueprintDebugSession`/`IBlueprintProbeSink` contracts this editor drives and implements against. **Mandatory dependency.** |
| `Hrot.Blueprints.Compiler` | Roslyn-based code-gen backend invoked by `QuickReloadService`/`Stage8_RoslynFinalize`. Compiled into `Hrot.Blueprints.Core`. |
| `Hrot.Editor.AiShared` | Shared perspective/window/document/catalog/selection/refactor host this project plugs into via `IAssetCatalogContributor`/`IReferenceCatalogContributor`/`INewAssetService`/`ManagedWindow`/`AiCanvasContext`. **The Asset Browser, document lifecycle, and perspective/window chrome all live here now, not in this project.** |
| `NodeEditor.Core` / `NodeEditor.UI` (ExtDeps) | The canvas rendering/interaction library itself — `Host/` adapts Blueprint assets onto its `IGraphModel`/`IGraphCommandSink`/`ILinkValidator`/`INodeCatalog`/`IDebugSession` contracts. `GraphEditorWindow.cs` (the old bespoke canvas) no longer exists. |
| `Fdp.Presentation` | ImGui host (`ImGuiNET`) + `WindowManager`/`ManagedWindow` that both window mechanisms build on. |
| `Fdp.Toolkits` | `AiHotReloadCoordinator` (atomic ALC swap), `BlueprintRegistry`/`BehaviorRegistry`, `HsmActionDispatcher`, `MasterSyncController`, `BlueprintInstanceService`. |
| `Fdp.Core` | Core entity system and simulation-view types consumed by `BlueprintDebugSession` and `Runtime/`. |
