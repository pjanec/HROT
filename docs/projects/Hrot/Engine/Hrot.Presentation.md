# Hrot.Presentation

**Project file:** `Hrot/Engine/Hrot.Presentation/Hrot.Presentation.csproj`
**Target framework:** net8.0
**Date:** 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the authoritative
architectural reference for the assembly.

---

## Executive Overview

`Hrot.Presentation` is the engine-level presentation library for the HROT simulation
system. It is a single assembly that exports three distinct namespace hierarchies serving
complementary roles:

| Root namespace | Role |
|---|---|
| `Hrot.UI.Common.*` | Reusable, framework-agnostic UI facades, models, panels, menus, and adapters shared by every HROT subsystem (Editor, CGF, SimHost, IG, ExCon). |
| `Hrot.Presentation.*` | HROT-specific ImGui renderers, gizmos, window wrappers, ECS systems, and behavior-parameter UI infrastructure. |
| `Hrot.ScenarioEditor.*` | Scenario editor module, gizmos, render layers, selection system, file service, and cluster-load handler. |

The assembly sits at the Engine layer of the HROT stack, directly above `Hrot.Core` (domain
types), and delegates rendering plumbing to `Fdp.Presentation` (ImGui/Raylib window manager).
Every consumer subsystem (Editor, CGF, SimHost, IG) depends on this assembly and plugs its
own concrete implementations of the facade interfaces into the shared panels and menu helpers.

Design goals:
- **Decoupled panels.** Every panel depends only on facade interfaces, never on ECS or
  network infrastructure. This makes every panel logic path unit-testable without an ImGui
  frame.
- **Expression-tree-compiled UI.** `BehaviorUiCompiler` performs all reflection once at
  startup, caches the result, and emits a delegate that drives ImGui without further
  reflection at frame rate.
- **Gizmo-based map interaction.** `SelectionInteractionSystem`, `EntityDragGizmo`,
  `EntityPlacementGizmo`, and related gizmos replace legacy tool classes from Phase 5 of
  the HROT gizmo migration.
- **Multi-namespace, single assembly.** Code that logically belongs to a "shared UI common"
  layer lives under `Hrot.UI.Common.*` inside the same `.csproj` to avoid a separate NuGet
  project or circular-reference problem.

---

## Architecture

### Layer Diagram

```
+--------------------------------------------------------------------+
|          Subsystem Applications (Editor / CGF / SimHost / IG)      |
|   - Composition root wires concrete adapters to facade interfaces  |
+-----------------------------+--------------------------------------+
                              |
+-----------------------------v--------------------------------------+
|               Hrot.Presentation  (this assembly)                   |
|                                                                    |
|  +------------------+  +------------------+  +------------------+ |
|  | Hrot.UI.Common.* |  | Hrot.Presentation|  | Hrot.ScenarioEd. | |
|  | Facades / Models |  |  Renderers       |  |  Module / Gizmos | |
|  | Panels / Menus   |  |  Windows         |  |  Systems / Svc   | |
|  | Adapters         |  |  Gizmos          |  |  Handlers        | |
|  +------------------+  |  Behavior        |  +------------------+ |
|                         |  Systems         |                       |
|                         +------------------+                       |
+-----------------------------+--------------------------------------+
                              |
         +--------------------+--------------------+
         |                    |                    |
+--------v------+  +----------v------+  +----------v------+
| Hrot.Core     |  | Fdp.Presentation|  | Fdp.Toolkits    |
| (domain types)|  | (ImGui/Raylib)  |  | (ECS infra)     |
+---------------+  +-----------------+  +-----------------+
```

### Facade / Panel Dependency Diagram

```
+--------------------+       uses       +------------------------+
|   MissionPanel     +---------------->|  IMissionEditorService  |
|  (Hrot.UI.Common)  +---------------->|  IMapPickService        |
|                    |   implements     +------------------------+
|                    +---------------->|  IPickInteractionContext|
+--------------------+                 +------------------------+

+--------------------+       uses       +------------------------+
|  SharedOrbatPanel  +---------------->|  IOrbatDataProvider     |
+--------------------+       uses       |  IOrbatController       |
                                        +------------------------+

+--------------------+       uses       +------------------------+
|   SpawnerPanel     +---------------->|  ISpawnController       |
+--------------------+                 +------------------------+

+--------------------+       uses       +------------------------+
|   ConfigPanel      +---------------->|  IMapConfigController   |
+--------------------+                 +------------------------+

+--------------------+       uses       +------------------------+
|   PreviewPanel     +---------------->|  IPreviewController     |
+--------------------+                 +------------------------+

+--------------------+       uses       +------------------------+
|  ZoneEditorPanel   +---------------->| IZoneAuthoringController|
+--------------------+                 +------------------------+

+----------------------------------+   uses   +--------------------+
| ClusterTimeControlStatusBarSection+-------->| ITimeTransportFacade|
+----------------------------------+          +--------------------+
```

### Behavior UI Compilation Pipeline

```
Startup (once per DTO type)
+------------------+    Compile<TDto>()    +----------------------+
| BehaviorUiSetup  |--------------------->| BehaviorUiCompiler   |
|  CreateRegistry()|                       | BuildDelegate<TDto>()|
+------------------+                       |  - GetProperties     |
         |                                 |  - GetCustomAttribs  |
         v                                 |  - Build lambdas     |
+------------------+                       +----------+-----------+
| BehaviorUiRegistry|                                 |
|  _registry[id]=  |<--------------------------------+
|   delegate       |        cached BehaviorUiDrawDelegate
+------------------+

Per frame
+------------------+  TryGet(behaviorId)  +------------------+
|  MissionPanel    |--------------------->| BehaviorUiRegistry|
|  DrawContent()   |<--------- delegate   +------------------+
+------------------+
       |
       v
delegate(currentJson, taskIndex, context)
  -> Deserialize DTO
  -> foreach compiled property renderer
  -> Serialize DTO if changed
  -> return new JSON
```

### Scenario Editor Gizmo Architecture

```
+----------------------+    registers    +----------------------+
| Composition Root     +--------------->| GlobalGizmoManager   |
| (e.g. EditorSubsystem|    IEntityStat  +----------+-----------+
+----------------------+    efulGizmo               |
                                                     | routes bus events
+--------------------+   GizmoInteraction  +---------v---------+
| SelectionInteract. |<---StartedEvent-----| FdpEventBus        |
| System             |   GizmoDragUpdate   | (interaction bus)  |
|                    |   CommitEvent       +-------------------+
+--------------------+   CancelEvent
         |
         | mutates
         v
+--------------------+   reads    +--------------------+
| SelectionState     |<-----------| SelectionRender    |
| (ECS component)    |            | System (IMapLayer) |
+--------------------+            +--------------------+
```

---

## Source Structure

All source files are under `Hrot/Engine/Hrot.Presentation/`.

### Root

| File | Namespace | Type | Description |
|---|---|---|---|
| `AssemblyInfo.cs` | (assembly-level) | Attributes | Grants `InternalsVisibleTo` for `Hrot.ExCon.Tests`. |

### Adapters/

| File | Namespace | Type | Description |
|---|---|---|---|
| `ClusterTimeTransportAdapter.cs` | `Hrot.UI.Common.Adapters` | `sealed class` | Implements `ITimeTransportFacade` for distributed cluster nodes. Drains `ClusterStateUpdateEvent` and `SwitchTimeModeEvent` from the event bus each frame to track pause/play/time-scale state. Dispatches intents (`ResumeTimeIntent`, `PauseTimeIntent`, etc.) back onto the same bus. |

### Behavior/

| File | Namespace | Type | Description |
|---|---|---|---|
| `BehaviorSchemaDiscovery.cs` | `Hrot.Presentation.Behavior` | `static class` | Scans `Hrot.Core` assembly for `[BehaviorContractAttribute]`-decorated types and auto-registers them with `BehaviorUiRegistry` and `ScenarioBehaviorRemapper` via reflection. |
| `BehaviorUiCompiler.cs` | `Hrot.Presentation.Behavior` | `static class` + `sealed class` | Expression-tree-based compilation of ImGui draw delegates for behavior-parameter DTOs. `BehaviorUiRegistry` stores the per-ID cache; `BehaviorUiCompiler.Compile<TDto>()` performs one-time reflection and returns a cached `BehaviorUiDrawDelegate`. |
| `BehaviorUiSetup.cs` | `Hrot.Presentation.Behavior` | `static class` | Composition-root helper. `CreateRegistry()` returns a `BehaviorUiRegistry` pre-populated via `BehaviorSchemaDiscovery.AutoRegister`. |
| `IPickInteractionContext.cs` | `Hrot.Presentation.Behavior` | `interface` | Per-field pick coordination for behavior parameter editors. Implemented by `MissionPanel`. |

### Facades/

| File | Namespace | Type | Description |
|---|---|---|---|
| `CanvasMapPickAdapter.cs` | `Hrot.UI.Common.Facades` | `sealed class` | `IMapPickService` implementation using a `MapCanvas` and `GlobalGizmoManager`. Registers `FdpLocationPickerGizmo` and `EntityPickerGizmo` on demand, resolves `NetworkIdentity` IDs, and returns `Task<T>` via `TaskCompletionSource`. |
| `IEntityActionController.cs` | `Hrot.UI.Common.Facades` | `interface` | Commands: `CenterOnEntity`, `DeleteEntity`, `EditOverlay`, `EditRoute`, `Rename`, `ActivateMeasureTool`, `ActivateRotateTool`. |
| `IMapConfigController.cs` | `Hrot.UI.Common.Facades` | `interface` | `GetCurrentConfig()` / `ApplyConfig(MapLayerState)`. |
| `IMapPickService.cs` | `Hrot.UI.Common.Facades` | `interface` | Async map-pick: `PickLocationAsync`, `PickEntityAsync`, `PickAreaEntitiesAsync`. |
| `IMissionEditorService.cs` | `Hrot.UI.Common.Facades` | `interface` | Mission read/write: `GetAvailableBehaviors`, `GetMissionSnapshot`, `CommitMissionAsync`, `SendControlCommandAsync`. |
| `IOrbatController.cs` | `Hrot.UI.Common.Facades` | `interface` | ORBAT commands: `SelectEntity`, `CreateUnit`, `ToggleExpanded`, `RequestEmbark`, `RequestDisembark`, `RequestAssignSubordinate`, `RequestRemoveSubordinate`. |
| `IOrbatDataProvider.cs` | `Hrot.UI.Common.Facades` | `interface` | `GetVisibleNodes(filterText, expandedNodes)` returns flat `OrbatNodeViewModel` list. |
| `IPreviewController.cs` | `Hrot.UI.Common.Facades` | `interface` | `IsInPreviewMode`, `EnterPreviewMode(startPaused)`, `ExitPreviewMode`. |
| `ISpawnController.cs` | `Hrot.UI.Common.Facades` | `interface` | `StartPlacementMode`, `StartAreaAuthoringMode`, `StartRouteAuthoringMode`. |
| `ITimeTransportFacade.cs` | `Hrot.UI.Common.Facades` | `interface` | Time transport state + actions: `IsPaused`, `TotalTime`, `TimeScale`, `TogglePlayPause`, `Step`, `Stop`, `SetTimeScale`. |
| `IZoneAuthoringController.cs` | `Hrot.UI.Common.Facades` | `interface` | `SetRoadNetworkPath`, `StartObstaclePlacementMode`. |
| `MapPickServiceBridge.cs` | `Hrot.Presentation.Facades` | `sealed class` | Adapts async `IMapPickService` to the synchronous `IComponentPickerContext` polling contract used by `ComponentReflector`. Bridges async tasks to per-frame `TryConsume*` calls. |

### Gizmos/

| File | Namespace | Type | Description |
|---|---|---|---|
| `CanvasContextMenuGizmo.cs` | `Hrot.Presentation.Gizmos` | `sealed class` | `[GizmoProjector]`-decorated `IGlobalStatelessGizmo`. Reads `CanvasContextMenuState` singleton and emits a `ContextMenuBinding` meta-primitive keyed by anchor `−1L` so right-clicking empty canvas space opens the global context menu. |

### Menus/

| File | Namespace | Type | Description |
|---|---|---|---|
| `MapContextActionController.cs` | `Hrot.UI.Common.Menus` | `sealed class` | Minimal `IEntityActionController` for map right-click context menus. Delegates center, delete, and rotate to injected callbacks; other actions are intentional no-ops. |
| `SharedContextMenuPopulator.cs` | `Hrot.UI.Common.Menus` | `static class` | Pure helper with no state. `PopulateEntityMenu` and `PopulateEmptyMapMenu` add items to `IContextMenuBuilder`. Fully testable without an ImGui frame. |

### Models/

| File | Namespace | Type | Description |
|---|---|---|---|
| `MapLayerState.cs` | `Hrot.UI.Common.Models` | `record` | Visibility flags for seven map layers: `Satellite`, `GroundUnits`, `AirUnits`, `Vehicles`, `TacticalGraphics`, `RoadGraphs`, `Grid`. |
| `MissionCommitResult.cs` | `Hrot.UI.Common.Models` | `record` | `Success`, `NewVersion`, `ErrorMessage`. Returned by `IMissionEditorService.CommitMissionAsync`. |
| `OrbatNodeViewModel.cs` | `Hrot.UI.Common.Models` | `sealed record` | Flat tree node: `EntityId`, `Name`, `Depth`, `HasChildren`, `IsPendingDelete`, `CanAcceptSubordinates`. |

### Panels/

| File | Namespace | Type | Description |
|---|---|---|---|
| `ClusterTimeControlStatusBarSection.cs` | `Hrot.UI.Common.Panels` | `sealed class` | Status-bar section: play/pause, step, stop buttons and sim-time / time-rate display. Backed by `ITimeTransportFacade`. Renders `HH:MM:SS.SSS` and a time-rate popup. |
| `ConfigPanel.cs` | `Hrot.UI.Common.Panels` | `sealed class` | Seven layer-visibility toggle checkboxes and an icon-scale slider. `HandleSendConfigPatch` builds a `MapLayerState` and calls `IMapConfigController.ApplyConfig`. |
| `MissionPanel.cs` | `Hrot.UI.Common.Panels` | `sealed class` | Implements `IPickInteractionContext`. Displays selected entity mission plan, handles JUMP/ABORT control commands, task editing, and async map picks for `MoveToLocation` / `FollowRoute` tasks. Uses `BehaviorUiRegistry` to render behavior-parameter DTOs. |
| `PanelConstants.cs` | `Hrot.UI.Common.Panels` | `static class` | Shared constants: icon scale range, filter text max length, behavior params buffer size, move-to defaults, error messages. |
| `PreviewPanel.cs` | `Hrot.UI.Common.Panels` | `sealed class` | "Enter Preview" / "Stop Preview" toggle backed by `IPreviewController`. No internal state; color-coded status label. |
| `SharedOrbatPanel.cs` | `Hrot.UI.Common.Panels` | `sealed class` | ORBAT tree with text filter, depth-based indentation, arrow toggles, click-to-select, ImGui drag-and-drop embarkation, and "Disembark" context menu. |
| `SpawnerPanel.cs` | `Hrot.UI.Common.Panels` | `sealed class` | TKB entity catalog browser with case-insensitive filter, force-affiliation radio buttons, and "Place" / "Draw Area" / "Draw Route" activation buttons. Pre-computed filtered list avoids LINQ allocations in `Draw`. |
| `ZoneEditorPanel.cs` | `Hrot.UI.Common.Panels` | `sealed class` | Zone name, road-network JSON path, and obstacle-radius inputs. Calls `IZoneAuthoringController.SetRoadNetworkPath` and `StartObstaclePlacementMode`. |

### Renderers/

| File | Namespace | Type | Description |
|---|---|---|---|
| `ActivePerspectiveRenderer.cs` | `Hrot.Presentation.Renderers` | `sealed class` | `[ImGuiRenderer(typeof(ActivePerspective))]`. Summary shows perspective name; `RenderValue` returns `false` to let `ImGuiPropertyTree` render `Name` as an editable leaf. |
| `BehaviorStateRenderer.cs` | `Hrot.Presentation.Renderers` | `sealed class` | `[ImGuiRenderer(typeof(BehaviorState))]`. Summary: behavior name + brain tier. Renders `ActiveBehaviorHash`, `InstanceId`, `BrainTier`. Uses static `BehaviorRegistryAccessor`. |
| `Blackboard1024Renderer.cs` | `Hrot.Presentation.Renderers` | `sealed class` | `[ImGuiRenderer(typeof(Blackboard1024))]`. Entity-aware. Deserializes 1024-byte heavy blackboard as `HeavyDtoType` when registered; falls back to raw-bytes label. |
| `Blackboard1024ViewProvider.cs` | `Hrot.Presentation.Renderers` | `sealed class` | StructEdit `IBufferViewProvider`. Projects `$.Memory` of `Blackboard1024` as the active behavior's `HeavyDtoType`. Zero-allocation writes via `NativeFieldBinding`. |
| `BrainBlackboardRenderer.cs` | `Hrot.Presentation.Renderers` | `sealed class` | `[ImGuiRenderer(typeof(BrainBlackboard))]`. Entity-aware. Interprets `BehaviorParameters` fixed buffer as `ParamsDtoType`; renders typed or raw-hex fallback. Also shows `ExpectedThreatLevel` and interrupt flags. |
| `BrainBlackboardViewProvider.cs` | `Hrot.Presentation.Renderers` | `sealed class` | StructEdit `IBufferViewProvider`. Projects `$.BehaviorParameters` of `BrainBlackboard` as the active behavior's `ParamsDtoType`. |
| `BTreeTraceWorkingMemoryRenderer.cs` | `Hrot.Presentation.Renderers` | `sealed class` | `[ImGuiRenderer(typeof(BTreeTraceWorkingMemory1024))]`. Entity-aware ring-buffer renderer. Decodes 16-byte trace records into a 4-column ImGui table. Node-index symbolication via `BehaviorTreeBlob.DebugMetadata`. |
| `BTreeVisualizerRenderer.cs` | `Hrot.Presentation.Renderers` | `sealed class` | `[ImGuiRenderer(typeof(BrainBTreeState))]`. Entity-aware. Renders the full behavior-tree hierarchy with color-coded active-path highlighting (green = running, yellow = ancestral, gray = inactive). Source-location tooltips from `NodeDebugMetadata`. |
| `HrotSingletonRenderers.cs` | `Hrot.Presentation.Renderers` | Contains `ActivePerspectiveRenderer` | (see above) |
| `HsmTraceWorkingMemoryRenderer.cs` | `Hrot.Presentation.Renderers` | `sealed class` | `[ImGuiRenderer(typeof(HsmTraceWorkingMemory1024))]`. Entity-aware ring-buffer renderer for HSM execution traces. Decodes records using `MachineMetadata` to symbolicate state/event/action IDs. |
| `MissionPlanQueueRenderer.cs` | `Hrot.Presentation.Renderers` | `sealed class` | `[ImGuiRenderer(typeof(MissionPlanQueue))]`. Renders `CurrentPhase`, `PhaseCount`, `PhaseElapsedSeconds`, and per-phase `BehaviorId` / `Trigger` / `TriggerParam` in a two-column ImGui table. |

### Systems/

| File | Namespace | Type | Description |
|---|---|---|---|
| `CanvasMenuUpdateSystem.cs` | `Hrot.Presentation.Systems` | `class` | `[UpdateInPhase(SystemPhase.PostSimulation)]`. Writes a pre-serialized `CanvasMenuJson` singleton each frame so `CanvasContextMenuGizmo` can emit the context-menu binding without dynamic string allocation. |

### Windows/

| File | Namespace | Type | Description |
|---|---|---|---|
| `ArchitectureDiagnosticsWindow.cs` | `Hrot.Presentation.Windows` | `sealed class` | `ManagedWindow` wrapping `ArchitectureDiagnosticsPanel`. Scope: `PerspectiveBound`. Starts closed. Optional title-bar color. |
| `FdpEntityInspectorHelper.cs` | `Hrot.Presentation.Windows` | `static class` | Shared helper: wires `ComponentReflector` settings on an `EntityInspectorPanel` and registers an "Inspect..." context-menu handler that spawns volatile `FdpEntityWatchWindow` instances via the window manager. |
| `FdpPanelWindows.cs` | `Hrot.Presentation.Windows` | Three `sealed class`es | `FdpEntityInspectorWindow`, `FdpEventBrowserWindow`, `FdpEntityWatchWindow`. All are `ManagedWindow` subclasses; `FdpEntityWatchWindow` is `IsVolatile=true` and `ShowInMenu=false` (spawned on demand). |

### ScenarioEditor/

#### Root files

| File | Namespace | Type | Description |
|---|---|---|---|
| `IScenarioStateProvider.cs` | `Hrot.ScenarioEditor` | `interface` | Port: `ScenarioEditorState CurrentState { get; }` |
| `ScenarioEditorModule.cs` | `Hrot.ScenarioEditor` | `class` | `IEcsModule` entry point. Exposes `FileService`. System registration placeholders for PACK2-E002/E003. |
| `ScenarioEditorState.cs` | `Hrot.ScenarioEditor` | `enum` | States: `Idle`, `LoadingEdit`, `OperatingEdit`, `LoadingPreview`, `OperatingPreview`, `SavingEdit`. |

#### Gizmos/

| File | Namespace | Type | Description |
|---|---|---|---|
| `EntityDragGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `IEntityStatefulGizmo`. Handles drag interaction for map entities: records position on press, writes live `SimTransform` position during drag, fires `OnDragCommitted` callback on release. |
| `EntityEditorLabelGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `IStatelessGizmo` (manual registration). Emits three world-space text labels per entity: network ID, active behavior name (truncated to 20 chars), HP current/max (color-coded). |
| `EntityEditorPolylineGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `[GizmoProjector(SimTransform, NetworkIdentity)]`. Draws perspective-exaggerated rectangular silhouette aligned to entity heading. |
| `EntityPlacementGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `IEntityStatefulGizmo`. Left-click builds and fires a `SpawnEntityCommand`. Optional `autoPopOnPlace` for single vs. multi-placement. Ghost preview circle at cursor. |
| `EntityPresentationGizmoShared.cs` | `Hrot.ScenarioEditor.Gizmos` | `static class` | Shared helpers: `DrawSpatialAnchorFromRotation`, `EmitPickBox`, `TryGetVehicleDimensions`, `ResolveProfileId`, `DrawSemanticShape`. |
| `IgEntityPresentationGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `[GizmoProjector(SimTransform, NetworkIdentity, CullingState)]`. Emits `SpatialAnchor` + `SemanticShape` for IG entities, gated by `CullingState.IsVisible`. Computes condition mask from `IgHealthState`. |
| `IRouteWaypointEditorState.cs` | `Hrot.ScenarioEditor.Gizmos` | `interface` | `SelectedVertexIndex`, `GetSelectedWaypointRef()`. Implemented by `RouteWaypointGizmo`. |
| `MapOverlayGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `[GizmoProjector(SimTransform, MapOverlayStyle)]`. Emits line segments for `EditablePolyline` entities, using `MapOverlayStyle` for colour and thickness. |
| `MeasureGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `IEntityStatefulGizmo`. Two-click distance measurement with live preview line. Exposes `LastMeasuredDistanceMeters` for tests. Supports meters / kilometers display. |
| `MissionPresentationGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `IStatelessGizmo` (manual registration). Draws orange-to-blue gradient lines from selected entity through its `ActiveMissionPlan` task targets. Uses `IGeographicTransform` for lat/lon-to-cartesian conversion. |
| `RouteGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `[GizmoProjector(TkbIdentity)]`. Emits line segments for `TkbEntityTypes.TacGraphic_Route` entities from `RoutePlan.Waypoints`. |
| `RouteWaypointGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `IEntityStatefulGizmo + IRouteWaypointEditorState`. Exclusive-focus gizmo for dragging route waypoints. Context menu: insert/delete waypoint. Exposes static `Current` for `WaypointEditorPanel` binding. |
| `RubberBandGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `IGlobalStatelessGizmo`. Draws blue semi-transparent rubber-band selection rectangle while `RubberBandState.IsActive`. |
| `RubberBandState.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | Shared mutable state: `IsActive`, `Start`, `Current`. Shared between `RubberBandGizmo` and `SelectionInteractionSystem`. |
| `TacticalAreaGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `[GizmoProjector(TkbIdentity)]`. Draws closed olive-yellow polygon outline for `TkbEntityTypes.TacGraphic_Area` entities from `EditablePolyline.Points`. |
| `VertexEditGizmo.cs` | `Hrot.ScenarioEditor.Gizmos` | `sealed class` | `IEntityStatefulGizmo`. Exclusive-focus gizmo for dragging individual vertices of `EditablePolyline`. Context menu: insert/delete point. Writes back via `UpdateEntityCommand`. |

#### Handlers/

| File | Namespace | Type | Description |
|---|---|---|---|
| `HrotEditLoadHandler.cs` | `Hrot.ScenarioEditor.Handlers` | `sealed class` | `ITickableClusterStateHandler`. Intercepts `PrepareState(OperatingEdit)`, extracts entity creation requests from scenario JSON via the staging pipeline, enqueues them into `ScenarioEntityCreationRequestSource` for the genesis pipeline. Applies zone data synchronously. Holds cluster in `LoadingEdit` until all ECS entities leave the `Constructing` lifecycle phase. |

#### Rendering/

| File | Namespace | Type | Description |
|---|---|---|---|
| `SelectionRenderConstants.cs` | `Hrot.ScenarioEditor.Rendering` | `static class` | Layer name, always-visible bit index (`-1`), primary fill RGBA channels, selection ring radius (`20 px`). |
| `SelectionRenderSystem.cs` | `Hrot.ScenarioEditor.Rendering` | `class` | `IMapLayer`. Draws selection rings for all entities with `SelectionState.IsSelected = true`. Primary selection: green filled circle + outline. Secondary: yellow outline only. `LayerBitIndex = -1` (always visible). |

#### Services/

| File | Namespace | Type | Description |
|---|---|---|---|
| `ScenarioFileService.cs` | `Hrot.ScenarioEditor.Services` | `sealed class` | Local scenario file operations: `NewScenario` (clears repo, fires `WorldResetEvent`), `SaveScenario` (serializes to `HrotScenarioEnvelopeDto` JSON), `LoadScenario`. Publishes `WorldResetEvent` before `repo.SoftClear()` to let consumers flush cached entity handles. Supports `RegisterWorldResetObserver`. |

#### Systems/

| File | Namespace | Type | Description |
|---|---|---|---|
| `SelectionInteractionSystem.cs` | `Hrot.ScenarioEditor.Systems` | `sealed class` | Translates gizmo interaction events into `SelectionState` ECS mutations. Handles click-to-select, rubber-band box selection, Delete key to destroy selected entities. Fires `OnSelectionChanged` callback. |

#### Tools/

| File | Namespace | Type | Description |
|---|---|---|---|
| `MeasureToolConstants.cs` | `Hrot.ScenarioEditor.Tools` | `static class` | `ToolName`, `LineThickness`, `LabelFontSize`, `LabelOffsetY` for the measure gizmo. |

---

## Public API Reference

### Hrot.UI.Common.Adapters

#### `ClusterTimeTransportAdapter`

```csharp
public sealed class ClusterTimeTransportAdapter : ITimeTransportFacade
{
    public ClusterTimeTransportAdapter(FdpEventBus bus, Func<double>? localSimTimeGetter = null);
    public void Update();           // drain bus events; call before SwapBuffers
    // ITimeTransportFacade:
    public bool   IsPlayPauseEnabled { get; }
    public bool   IsStepEnabled      { get; }
    public bool   IsStopEnabled      { get; }
    public bool   IsPaused           { get; }
    public double TotalTime          { get; }
    public float  TimeScale          { get; }
    public void   TogglePlayPause();
    public void   Step();
    public void   Stop();
    public void   SetTimeScale(float scale);
}
```

### Hrot.UI.Common.Facades

#### `ITimeTransportFacade`

```csharp
public interface ITimeTransportFacade
{
    bool   IsPlayPauseEnabled { get; }
    bool   IsStepEnabled      { get; }
    bool   IsStopEnabled      { get; }
    bool   IsPaused           { get; }
    double TotalTime          { get; }
    float  TimeScale          { get; }
    void   TogglePlayPause();
    void   Step();
    void   Stop();
    void   SetTimeScale(float scale);
}
```

#### `IMapPickService`

```csharp
public interface IMapPickService
{
    Task<GeoPoint> PickLocationAsync(CancellationToken ct = default);
    Task<int>      PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default);
    Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default);
}
```

#### `IMissionEditorService`

```csharp
public interface IMissionEditorService
{
    IReadOnlyList<string>          GetAvailableBehaviors(long entityId);
    (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId);
    Task<MissionCommitResult>      CommitMissionAsync(long entityId, MissionPlan plan, long baseVersion);
    Task<MissionCommitResult>      SendControlCommandAsync(long entityId, eMissionCommandType type, Guid taskId);
}
```

#### `IOrbatController`

```csharp
public interface IOrbatController
{
    void SelectEntity(int entityId);
    void CreateUnit(long tkbType);
    void ToggleExpanded(int entityId);
    void RequestEmbark(int passengerEntityId, int vehicleEntityId);
    void RequestDisembark(int passengerEntityId);
    void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId);
    void RequestRemoveSubordinate(int subordinateEntityId);
}
```

#### `IOrbatDataProvider`

```csharp
public interface IOrbatDataProvider
{
    IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(string filterText, HashSet<int> expandedNodes);
}
```

#### `IEntityActionController`

```csharp
public interface IEntityActionController
{
    void CenterOnEntity(long entityId);
    void DeleteEntity(long entityId);
    void EditOverlay(long entityId);
    void EditRoute(long entityId);
    void Rename(long entityId);
    void ActivateMeasureTool();
    void ActivateRotateTool(long entityId);
}
```

#### `IMapConfigController`

```csharp
public interface IMapConfigController
{
    MapLayerState GetCurrentConfig();
    void ApplyConfig(MapLayerState config);
}
```

#### `IPreviewController`

```csharp
public interface IPreviewController
{
    bool IsInPreviewMode { get; }
    void EnterPreviewMode(bool startPaused = false);
    void ExitPreviewMode();
}
```

#### `ISpawnController`

```csharp
public interface ISpawnController
{
    void StartPlacementMode(long tkbType, string? initialPropertiesJson = null);
    void StartAreaAuthoringMode(string styleOverrideJson = "");
    void StartRouteAuthoringMode();
}
```

#### `IZoneAuthoringController`

```csharp
public interface IZoneAuthoringController
{
    void SetRoadNetworkPath(string activeZoneName, string assetPath);
    void StartObstaclePlacementMode(string activeZoneName, float radius);
}
```

#### `CanvasMapPickAdapter`

```csharp
public sealed class CanvasMapPickAdapter : IMapPickService
{
    public CanvasMapPickAdapter(
        MapCanvas canvas,
        EntityRepository? repo = null,
        IEntityFilterFactory? filterFactory = null,
        GlobalGizmoManager? globalGizmoManager = null);
    // IMapPickService members implemented
}
```

### Hrot.UI.Common.Models

#### `OrbatNodeViewModel`

```csharp
public sealed record OrbatNodeViewModel(
    int  EntityId,
    string Name,
    int  Depth,
    bool HasChildren,
    bool IsPendingDelete,
    bool CanAcceptSubordinates);
```

#### `MissionCommitResult`

```csharp
public record MissionCommitResult(bool Success, long NewVersion, string? ErrorMessage = null);
```

#### `MapLayerState`

```csharp
public record MapLayerState(
    bool Satellite,
    bool GroundUnits,
    bool AirUnits,
    bool Vehicles,
    bool TacticalGraphics,
    bool RoadGraphs,
    bool Grid);
```

### Hrot.UI.Common.Panels

#### `MissionPanel`

```csharp
public sealed class MissionPanel : IPickInteractionContext
{
    public MissionPanel(long localNodeId = 0, BehaviorUiRegistry? behaviorUiRegistry = null);
    public int           SelectedEntityId { get; set; }
    public MissionPlan?  DraftPlan        { get; }
    public bool          CommitInFlight   { get; }
    public bool          CommitButtonEnabled { get; }
    public void          HandleJump(IMissionEditorService service);
    public void          HandleAbortAll(IMissionEditorService service);
    public static string GetTaskIcon(MissionTask task, bool isActive);
    public static string GetDefaultTriggerParams(string triggerType);
    public void          DrawContent(IMissionEditorService service, IMapPickService pickService);
    // IPickInteractionContext members
    public bool IsPickPendingFor(int taskIndex, string propertyName);
    public bool TryConsumeEntityPick(int taskIndex, string propertyName, out long entityId);
    public bool TryConsumeLocationPick(int taskIndex, string propertyName, out PickableGeoPoint location);
    public void RequestEntityPick(int taskIndex, string propertyName, string[]? filterPresets);
    public void RequestLocationPick(int taskIndex, string propertyName);
}
```

#### `SharedOrbatPanel`

```csharp
public sealed class SharedOrbatPanel
{
    public string FilterText    { get; set; }
    public IReadOnlySet<int> ExpandedNodes { get; }
    public void DrawContent(IOrbatDataProvider data, IOrbatController ctrl);
}
```

#### `SpawnerPanel`

```csharp
public sealed record TkbCatalogEntry(long TkbId, string Name);

public sealed class SpawnerPanel
{
    public SpawnerPanel(IEnumerable<TkbCatalogEntry> catalog);
    public SpawnerPanel();
    public string SearchFilter  { get; set; }
    public long SelectedType    { get; }
    public eForceIdentifier SelectedAffiliation { get; }
    public IReadOnlyList<TkbCatalogEntry> FilteredEntries { get; }
    public void HandleTypeSelected(long tkbId);
    public void HandleAffiliationChange(eForceIdentifier affiliation);
    public void HandleActivatePlacementTool(ISpawnController ctrl);
    public void Draw(ISpawnController ctrl);
}
```

#### `ConfigPanel`

```csharp
public sealed class ConfigPanel
{
    public ConfigPanel(long localNodeId = 0);
    public bool  SatelliteLayer   { get; set; }
    public bool  GroundUnits      { get; set; }
    public bool  TacticalGraphics { get; set; }
    public bool  AirUnits         { get; set; }
    public bool  Vehicles         { get; set; }
    public bool  RoadGraphs       { get; set; }
    public bool  Grid             { get; set; }
    public float IconScale        { get; set; }  // clamped to [0.5, 2.0]
    public void LoadConfig(IMapConfigController ctrl);
    public void HandleSendConfigPatch(IMapConfigController ctrl);
    public void DrawContent(IMapConfigController ctrl);
}
```

#### `ClusterTimeControlStatusBarSection`

```csharp
public sealed class ClusterTimeControlStatusBarSection
{
    public ClusterTimeControlStatusBarSection(ITimeTransportFacade facade);
    public void Render();   // call each frame inside status-bar window
}
```

#### `PreviewPanel`

```csharp
public sealed class PreviewPanel
{
    public void DrawContent(IPreviewController ctrl);
}
```

#### `ZoneEditorPanel`

```csharp
public sealed class ZoneEditorPanel
{
    public string ZoneName        { get; set; }
    public string RoadNetworkPath { get; set; }
    public float  ObstacleRadius  { get; set; }  // clamped to [1, 50]
    public void DrawContent(IZoneAuthoringController ctrl);
}
```

#### `PanelConstants`

```csharp
public static class PanelConstants
{
    public const float  IconScaleMin                    = 0.5f;
    public const float  IconScaleMax                    = 2.0f;
    public const float  IconScaleDefault                = 1.0f;
    public const int    FilterTextMaxLength             = 256;
    public const int    MissionBehaviorParamsMaxLength  = 2048;
    public const float  MoveToLocationDefaultSpeed      = 15f;
    public const float  MoveToLocationDefaultArrivalRadius = 50f;
    public const int    MissionBehaviorParamsEditorLines = 4;
    public const string VersionConflictErrorMessage     = "ERR_VERSION_CONFLICT";
    public const string FilterPresetRoadGraphs          = "road_graphs";
}
```

### Hrot.UI.Common.Menus

#### `SharedContextMenuPopulator`

```csharp
public static class SharedContextMenuPopulator
{
    public static void PopulateEntityMenu(
        long entityId, long tkbType,
        bool hasEditablePolyline, bool hasRoutePlan,
        IContextMenuBuilder builder, IEntityActionController actions);
    public static void PopulateEmptyMapMenu(
        IContextMenuBuilder builder, IEntityActionController actions);
}
```

#### `MapContextActionController`

```csharp
public sealed class MapContextActionController : IEntityActionController
{
    public MapContextActionController(
        Action<long> centerOnEntity,
        Action<long> deleteEntity,
        Action<long> rotateTool);
    // CenterOnEntity, DeleteEntity, ActivateRotateTool delegate to callbacks.
    // EditOverlay, EditRoute, Rename, ActivateMeasureTool are no-ops.
}
```

### Hrot.Presentation.Behavior

#### `BehaviorUiDrawDelegate`

```csharp
public delegate string BehaviorUiDrawDelegate(
    string currentJson, int taskIndex, IPickInteractionContext context);
```

#### `BehaviorUiRegistry`

```csharp
public sealed class BehaviorUiRegistry
{
    public void Register<TDto>(string behaviorId) where TDto : class, new();
    public bool TryGet(string behaviorId, out BehaviorUiDrawDelegate? drawDelegate);
}
```

#### `BehaviorUiCompiler`

```csharp
public static class BehaviorUiCompiler
{
    public static BehaviorUiDrawDelegate Compile<TDto>() where TDto : class, new();
}
```

#### `BehaviorUiSetup`

```csharp
public static class BehaviorUiSetup
{
    public static BehaviorUiRegistry CreateRegistry();
}
```

#### `IPickInteractionContext`

```csharp
public interface IPickInteractionContext
{
    bool IsPickPendingFor(int taskIndex, string propertyName);
    bool TryConsumeEntityPick(int taskIndex, string propertyName, out long entityId);
    bool TryConsumeLocationPick(int taskIndex, string propertyName, out PickableGeoPoint location);
    void RequestEntityPick(int taskIndex, string propertyName, string[]? filterPresets);
    void RequestLocationPick(int taskIndex, string propertyName);
}
```

#### `BehaviorSchemaDiscovery`

```csharp
public static class BehaviorSchemaDiscovery
{
    public static void AutoRegister(BehaviorUiRegistry uiRegistry, ScenarioBehaviorRemapper remapper);
}
```

### Hrot.Presentation.Facades

#### `MapPickServiceBridge`

```csharp
public sealed class MapPickServiceBridge : IComponentPickerContext
{
    public MapPickServiceBridge(IMapPickService pickService, EntityRepository? repo = null);
    public bool IsPickPendingFor(string jsonPath);
    public void RequestEntityPick(string jsonPath, string[]? filterPresets);
    public void RequestLocationPick(string jsonPath);
    public bool TryConsumeEntityPick(string jsonPath, out Entity pickedEntity);
    public bool TryConsumeLocationPick(string jsonPath, out Vector3 location);
}
```

### Hrot.Presentation.Gizmos

#### `CanvasContextMenuGizmo`

```csharp
[GizmoProjector]
public sealed class CanvasContextMenuGizmo : IGlobalStatelessGizmo
{
    public const long CanvasAnchorId = -1L;
    public void Draw(ISimulationView view, IDebugDrawBuilder drawBuilder);
}
```

### Hrot.Presentation.Windows

#### `FdpEntityInspectorHelper`

```csharp
public static class FdpEntityInspectorHelper
{
    public static void WireInspectorWithInspectContextMenu(
        EntityInspectorPanel panel,
        WindowManager windowManager,
        string ownerName,
        Func<IInspectableSession?> sessionGetter,
        MapPickServiceBridge? pickBridge,
        Vector4? titleBarColor);
}
```

### Hrot.Presentation.Systems

#### `CanvasMenuUpdateSystem`

```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public class CanvasMenuUpdateSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime);
}
```

### Hrot.ScenarioEditor

#### `ScenarioEditorState` (enum)

```csharp
public enum ScenarioEditorState
{
    Idle, LoadingEdit, OperatingEdit,
    LoadingPreview, OperatingPreview, SavingEdit
}
```

#### `IScenarioStateProvider`

```csharp
public interface IScenarioStateProvider
{
    ScenarioEditorState CurrentState { get; }
}
```

#### `ScenarioEditorModule`

```csharp
public class ScenarioEditorModule : IEcsModule
{
    public ScenarioEditorModule(ScenarioFileService? fileService = null);
    public string Name => "ScenarioEditor";
    public ScenarioFileService? FileService { get; }
    public void RegisterSystems(ISystemRegistry registry);
}
```

### Hrot.ScenarioEditor.Services

#### `ScenarioFileService`

```csharp
public sealed class ScenarioFileService
{
    public ScenarioFileService(
        ScenarioSerializer serializer,
        FdpEventBus? bus = null,
        IZoneManagerService? zoneService = null,
        ITkbDatabase? tkbDb = null);
    public void RegisterWorldResetObserver(Action callback);
    public void NewScenario(EntityRepository repo);
    public void SaveScenario(EntityRepository repo, string filePath);
    public void LoadScenario(EntityRepository repo, string filePath);
}
```

### Hrot.ScenarioEditor.Gizmos

#### `RubberBandState`

```csharp
public sealed class RubberBandState
{
    public bool    IsActive;
    public Vector2 Start;
    public Vector2 Current;
}
```

#### `IRouteWaypointEditorState`

```csharp
public interface IRouteWaypointEditorState
{
    int SelectedVertexIndex { get; }
    ref RouteWaypoint GetSelectedWaypointRef();
}
```

### Hrot.ScenarioEditor.Systems

#### `SelectionInteractionSystem`

```csharp
public sealed class SelectionInteractionSystem
{
    public Action<Entity, Vector3>? OnSelectionChanged;
    public SelectionInteractionSystem(
        EntityRepository world,
        FdpEventBus interactionBus,
        RubberBandState? rubberBandState = null);
    public void Tick(float dt);
}
```

### Hrot.ScenarioEditor.Rendering

#### `SelectionRenderConstants`

```csharp
public static class SelectionRenderConstants
{
    public const string LayerName                = "SelectionRings";
    public const int    AlwaysVisibleLayerBitIndex = -1;
    public const byte   PrimaryFillR             = 0;
    public const byte   PrimaryFillG             = 255;
    public const byte   PrimaryFillB             = 0;
    public const byte   PrimaryFillAlpha         = 50;
    public const int    SelectionRadiusPx        = 20;
}
```

---

## Dependencies

### Project References

| Assembly | Role |
|---|---|
| `Hrot.Core` | HROT domain types: `GeoPoint`, `MissionPlan`, `MissionTask`, `eMissionCommandType`, `eTaskState`, `BrainBlackboard`, `ActiveMissionPlan`, `IgHealthState`, etc. |
| `Fdp.Core` | ECS engine: `EntityRepository`, `Entity`, `EntityQuery`, `FdpEventBus`, `GlobalTime`, `FixedString32`. |
| `Fdp.Toolkits` | Cross-cutting toolkits: behavior (`BehaviorRegistry`, `BehaviorState`, `BehaviorDefinition`, `MissionPlanQueue`, `BrainBTreeState`, `HsmTraceWorkingMemory1024`, `BTreeTraceWorkingMemory1024`, `Blackboard1024`), time (`MasterSyncController`, `SwitchTimeModeEvent`, `ClusterStateUpdateEvent`), replication (`NetworkIdentity`), spawning (`SpawnEntityCommand`, `CreateEntityRequestSystem`), scenario (`ScenarioSerializer`), vis2D (`MapCanvas`), orchestration (`FdpEventBus`, `ClusterState`). |
| `Fdp.Presentation` | ImGui/Raylib window manager: `ManagedWindow`, `WindowManager`, `WindowScope`, `EntityInspectorPanel`, `EntityWatchPanel`, `ArchitectureDiagnosticsPanel`, `EventBrowserPanel`, `StatusBarManager`, `IContextMenuBuilder`, `IImGuiRenderer`, `IEntityAwareImGuiRenderer`, `ImGuiRenderer` attribute, `ImGuiPropertyTree`, `RepositoryAdapter`, `IInspectableSession`. |
| `Fdp.Toolkits.Analyzers` (Analyzer) | Source generator for `[GizmoProjector]` attribute. Generates `IStatelessGizmo` registration code. Output assembly not referenced at runtime. |

### NuGet Packages

| Package | Version | Usage |
|---|---|---|
| `NLog` | 5.2.8 | Structured logging via `FdpLog<T>` wrappers in panels and services. |
| `Raylib-cs` | 7.0.2 | Underlying rendering API used by `Fdp.Presentation`. Transitively required for types in render systems and map layer implementations. |
| `rlImgui-cs` | 3.2.0 | ImGui integration for Raylib. Provides the bridge between ImGui frame rendering and Raylib draw calls. |

### InternalsVisibleTo

The assembly grants internal access to:
- `Hrot.IG.Tests`
- `Hrot.ScenarioEditor.Tests`
- `Hrot.Presentation.Tests`
- `Hrot.ClusterRunner.Integration.Tests`
- `Hrot.ExCon.Tests` (declared in `AssemblyInfo.cs`)

---

## Usage Examples

### Example 1: Wiring Panels in a Subsystem Composition Root

```csharp
// Composition root for a subsystem (e.g. CGF or SimHost).
// Create facade implementations and wire them to shared panels.

// 1. Time transport
var timeFacade = new ClusterTimeTransportAdapter(eventBus, () => simEngine.CurrentTime);
// Call timeFacade.Update() each frame before SwapBuffers.

// 2. Time-control status bar
var timeStatusBar = new ClusterTimeControlStatusBarSection(timeFacade);
statusBarManager.Register(() => timeStatusBar.Render());

// 3. ORBAT panel
var orbatPanel = new SharedOrbatPanel();
// Subsystem provides IOrbatDataProvider and IOrbatController implementations.
windowManager.RegisterWindow(new ManagedWindow("orbat", "ORBAT", perspective)
{
    DrawContent = () => orbatPanel.DrawContent(orbatDataProvider, orbatController)
});

// 4. Config panel
var configPanel = new ConfigPanel(localNodeId);
configPanel.LoadConfig(mapConfigController);
windowManager.RegisterWindow(new ManagedWindow("config", "Config", perspective)
{
    DrawContent = () => configPanel.DrawContent(mapConfigController)
});

// 5. Spawner panel
var catalog = tkbDatabase.GetTemplates()
    .Select(t => new TkbCatalogEntry(t.TkbType, t.DisplayName));
var spawnerPanel = new SpawnerPanel(catalog);
windowManager.RegisterWindow(new ManagedWindow("spawner", "Spawner", perspective)
{
    DrawContent = () => spawnerPanel.Draw(spawnController)
});
```

### Example 2: Registering Behavior Parameter UI at Startup

```csharp
// At application startup, create and populate the behavior UI registry.
// BehaviorUiSetup uses BehaviorSchemaDiscovery to scan Hrot.Core for
// [BehaviorContractAttribute]-decorated DTOs and registers each one.
BehaviorUiRegistry behaviorUiRegistry = BehaviorUiSetup.CreateRegistry();

// Pass the registry to MissionPanel so it can render behavior parameters
// generically via the compiled ImGui draw delegates.
var missionPanel = new MissionPanel(localNodeId, behaviorUiRegistry);

// Per frame: when the panel renders a mission task, it calls
//   behaviorUiRegistry.TryGet(task.BehaviorId, out var draw)
//   draw(task.BehaviorParams, taskIndex, missionPanel)
// The compiled delegate deserializes the DTO, renders each property,
// and returns updated JSON if anything changed.
```

### Example 3: Async Map Pick for Mission Task Parameters

```csharp
// CanvasMapPickAdapter provides IMapPickService over a MapCanvas.
var pickService = new CanvasMapPickAdapter(
    canvas: mapCanvas,
    repo: entityRepo,
    filterFactory: domainFilterFactory,
    globalGizmoManager: gizmoManager);

// In a mission panel, when the operator clicks "Pick Location" for a MoveToLocation task:
// MissionPanel calls pickService internally via IPickInteractionContext.
// The panel remains in "pick pending" state and polls TryConsumeLocationPick each frame.

// Example of calling pick directly:
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    GeoPoint pickedPoint = await pickService.PickLocationAsync(cts.Token);
    // pickedPoint.Latitude = world-X, pickedPoint.Longitude = world-Y
    float worldX = (float)pickedPoint.Latitude;
    float worldY = (float)pickedPoint.Longitude;
}
catch (OperationCanceledException)
{
    // operator cancelled or timed out
}
```

### Example 4: Scenario File Operations

```csharp
// Set up scenario file service at composition root.
var fileService = new ScenarioFileService(
    serializer:   scenarioSerializer,
    bus:          eventBus,
    zoneService:  zoneManagerService,
    tkbDb:        tkbDatabase);

// Register any cleanup needed before repo.Clear() (e.g. flush selection state).
fileService.RegisterWorldResetObserver(() => selectionManager.ClearAll());

// Load a scenario.
fileService.LoadScenario(entityRepo, "scenarios/hill-attack/scenario.json");

// Save current state.
fileService.SaveScenario(entityRepo, "scenarios/hill-attack/autosave.json");

// Create a blank scenario.
fileService.NewScenario(entityRepo);
```

### Example 5: Wiring the FDP Entity Inspector with Map-Pick Support

```csharp
// Create the map-pick bridge so in-world component editing works.
var pickBridge = new MapPickServiceBridge(
    pickService: canvasPickAdapter,
    repo: entityRepo);

// Wire the inspector panel with the shared helper.
// This configures the ComponentReflector and registers the "Inspect..." context menu.
FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu(
    panel:          entityInspectorPanel,
    windowManager:  windowManager,
    ownerName:      "CGF",
    sessionGetter:  () => currentSession,
    pickBridge:     pickBridge,
    titleBarColor:  new Vector4(0.3f, 0.1f, 0.5f, 1f));
```

### Example 6: Setting Up the Selection System in the Scenario Editor

```csharp
// Shared mutable state for rubber-band selection gizmo.
var rubberBandState = new RubberBandState();

// Selection interaction system: translates gizmo events -> SelectionState mutations.
var selectionSystem = new SelectionInteractionSystem(
    world:           entityRepo,
    interactionBus:  interactionBus,
    rubberBandState: rubberBandState);

// Wire the selection-changed callback to broadcast network events.
selectionSystem.OnSelectionChanged += (entity, worldPos) =>
{
    if (!entity.IsNull)
        networkBus.PublishManaged(new SelectionChangedEventDto(entity.Index));
};

// Register the rubber-band gizmo with the global manager.
var gizmoId = GlobalGizmoManager.NewId();
gizmoManager.Register(gizmoId, new RubberBandGizmo(rubberBandState));

// Register the selection render layer with the map canvas.
var selectionLayer = new SelectionRenderSystem(
    view: entityRepo,
    query: entityRepo.CreateQuery().With<SelectionState>().With<SimTransform>().Build());
mapCanvas.RegisterLayer(selectionLayer);

// Each frame:
selectionSystem.Tick(deltaTime);
```

---

## Best Practices

### Facade-First Design

Every panel depends only on facade interfaces, never on ECS types, `EntityRepository`, or
network infrastructure directly. This rule ensures:

- All panel state transitions are unit-testable by injecting stub implementations of the
  facades. No ImGui frame is required.
- Subsystems can swap out backend implementations (editor-local controller vs.
  cluster-bus adapter) without modifying any panel code.
- New subsystems only need to provide concrete facade implementations; all shared UI is
  inherited for free.

### Behavior UI: Register Once, Render Many

`BehaviorUiCompiler.Compile<TDto>()` must be called only once per DTO type, at application
startup. The compiled delegate is cached in a `ConcurrentDictionary`; subsequent calls return
the cached delegate immediately. `BehaviorUiSetup.CreateRegistry()` wraps this correctly
via `BehaviorSchemaDiscovery.AutoRegister`.

Do not call `BehaviorUiRegistry.Register<TDto>` per-frame or from panel constructors. The
`InvalidOperationException` thrown on duplicate registration is intentional to surface
misuse.

### Panels: Separate Draw from Logic

All business-logic entry points in panels are `Handle*` methods or property setters that
can be called without an active ImGui render context. The `DrawContent` method calls
`Handle*` methods in response to ImGui widget interactions. This pattern is consistently
applied across `MissionPanel`, `ConfigPanel`, `SpawnerPanel`, `ZoneEditorPanel`,
`PreviewPanel`, and `SharedOrbatPanel`.

### Gizmos: Respect Exclusive Focus

Gizmos that set `RequiresExclusiveFocus = true` (`EntityPlacementGizmo`, `VertexEditGizmo`,
`RouteWaypointGizmo`, `MeasureGizmo`) suppress input routing to all other gizmos while
active. Always call `_onRemove()` when the gizmo task is complete so the `GlobalGizmoManager`
can unregister the gizmo and restore normal input routing.

### SelectionInteractionSystem: One Instance Per Canvas

`SelectionInteractionSystem` reads from the interaction event bus and writes `SelectionState`
ECS components. It must not be instantiated multiple times for the same bus/repo pair, as
each instance would consume the same events and produce duplicate mutations.

### ScenarioFileService: Observer Before Clear

Callers must register their `WorldResetObserver` callbacks before the first
`NewScenario` or `LoadScenario` call. The observers are invoked synchronously
immediately before `repo.SoftClear()`. Any code that caches `Entity` handles must flush
them in this callback to avoid dangling entity references after the repository is wiped.

### Renderer Static Accessors

`BehaviorStateRenderer.BehaviorRegistryAccessor`, `BrainBlackboardRenderer.BehaviorRegistryAccessor`,
`BTreeVisualizerRenderer.BehaviorRegistryAccessor`, `HsmTraceWorkingMemoryRenderer.BehaviorRegistryAccessor`,
and `Blackboard1024Renderer.BehaviorRegistryAccessor` are static properties that must be set
once at the composition root before any ImGui rendering occurs. Set them to the same
`BehaviorRegistry` instance used by the ECS behavior system.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Hrot.Core` | Provides domain types (`MissionPlan`, `GeoPoint`, `BrainBlackboard`, etc.) consumed throughout this assembly. |
| `Hrot.UI.Common` | **Note:** `Hrot.UI.Common.*` namespaces are compiled inside this assembly (`Hrot.Presentation.csproj`), not a separate project. There is no standalone `Hrot.UI.Common.csproj` at this layer. |
| `Hrot.IG` | Image Generator subsystem. Uses `IgEntityPresentationGizmo`, `CullingState`, `IgHealthState`, `SelectionState`, and entity presentation infrastructure from this assembly. |
| `Hrot.ExCon` | Exercise Control subsystem. Depends on panels, facades, and behavior UI infrastructure. Granted `InternalsVisibleTo`. |
| `Hrot.ScenarioEditor` *(namespace)* | Scenario editor logic lives in this assembly under `Hrot.ScenarioEditor.*` namespaces. Consumer subsystems register the `ScenarioEditorModule` in their ECS module hosts. |
| `Fdp.Presentation` | Provides the ImGui/Raylib window manager (`ManagedWindow`, `WindowManager`), panel base types, inspector panels, `IImGuiRenderer` infrastructure, and the `RepositoryAdapter`. This assembly builds directly on top of it. |
| `Fdp.Toolkits` | Provides ECS behavior components, time toolkit, replication, spawning, scenario serialization, and `MapCanvas`/vis2D infrastructure. The facade adapters and scenario editor services depend on these toolkits. |
| `Fdp.Core` | ECS foundation: `EntityRepository`, `Entity`, `FdpEventBus`, `GlobalTime`. Every ECS-touching file in this assembly depends on `Fdp.Core`. |
| `Hrot.Presentation.Tests` | Unit test project for this assembly. Granted internal access. Tests exercise panel `Handle*` methods, `BehaviorUiCompiler` caching, `SelectionInteractionSystem` logic, and gizmo state machines without an active ImGui or Raylib context. |
| `Hrot.IG.Tests` | IG subsystem tests. Granted `InternalsVisibleTo` for integration-level assertions on gizmo and renderer internals. |
| `Hrot.ScenarioEditor.Tests` | Tests for `ScenarioFileService`, `SelectionInteractionSystem`, and gizmo state machines. |
| `Hrot.ClusterRunner.Integration.Tests` | Cluster-level integration tests. Granted `InternalsVisibleTo` for verifying `HrotEditLoadHandler` and `ClusterTimeTransportAdapter` behaviour under simulated cluster state transitions. |
