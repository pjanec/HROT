# Hrot.Editor

| Field       | Value                                                                          |
|-------------|--------------------------------------------------------------------------------|
| Project     | `Hrot.Editor`                                                                  |
| Path        | `Hrot/Subsystems/Hrot.Editor/`                                                 |
| Output type | Class library (`net8.0`)                                                       |
| Date        | 2026-05-23                                                                     |

---

## README Validation

**Missing** - no `README.md` exists in the project folder.

---

## Executive Overview

`Hrot.Editor` is the standalone scenario-authoring subsystem for the HROT simulation
system. It implements the `ISubsystem` interface and embeds a fully self-contained
Entity-Component-System (ECS) simulation world that runs offline - no DDS or CycloneDDS
transport is allocated. The editor is used to create, modify, and persist tactical
scenarios (`scenario.json` files) that are subsequently loaded by the live cluster during
exercise execution.

Key responsibilities:

- **Scenario authoring**: create, load, save, and rename scenario files via
  `ScenarioFileService`.
- **Entity placement and editing**: interactive tools for spawning units, drawing
  overlays, editing routes, measuring distances, and rotating entities, all routed
  through the `GlobalGizmoManager` gizmo pipeline.
- **Mission planning**: assign AI behaviour trees and mission orders to individual
  units via the `EditorMissionService`.
- **ORBAT management**: hierarchical unit tree with drag-and-drop subordination via
  `EditorOrbatAdapter`.
- **Zone authoring**: define traversable zones, road networks, and obstacle rings via
  `EditorZoneAdapter` and `EditorZoneAuthoringSystem`.
- **Preview / dry-run**: enter a live simulation preview from the authored state, then
  rewind to the pre-preview snapshot with one click.
- **AI hot-reload**: watch `Hrot.AI.Behaviors.dll` for file-system changes and swap
  behaviour trees at run-time without restarting the editor, via
  `AiHotReloadCoordinator`.
- **Mode switching**: toggle between Internal (local FDP SimHost logic) and External
  (ACL translator packs connected to a remote HROT SimHost) without leaving the editor.

The project targets `.NET 8`, uses Raylib-cs / rlImgui-cs for 2-D rendering and ImGui
panels, and NLog for structured logging.

---

## Architecture

### Layered Design

The project is organized into five distinct layers. Each layer is allowed to reference
only the layers below it, enforcing a strict dependency direction.

```
+---------------------------------------------------------------------+
|                         ISubsystem Host                             |
|   (EditorSubsystem - composition root, lifecycle, IWindowRegistrar) |
+---------------------------------------------------------------------+
          |                  |                    |
          v                  v                    v
+------------------+  +-----------+  +-------------------------+
|  IEditorLogic    |  | MapCanvas |  |  ModuleHostKernel       |
|  (EditorApplica- |  | MapCamera |  |  (ECS tick loop,        |
|   tion facade)   |  | Gizmos    |  |   module registration)  |
+------------------+  +-----------+  +-------------------------+
          |                                       |
          v                                       v
+------------------+                  +----------------------+
|   Adapters       |                  |   ECS Systems /      |
|  (Spawn, Orbat,  |                  |   Modules            |
|   Mission, Map,  |                  |  (EditorSystems-     |
|   Zone, Preview) |                  |   Module, Simula-    |
+------------------+                  |   tion / CGF packs)  |
          |                           +----------------------+
          v
+------------------+
|   UI Panels      |
|  (Toolbar, ORBAT |
|   Browser, Prop  |
|   Inspector,     |
|   Context menu)  |
+------------------+
```

### Key Design Decisions

1. **Offline-first**: `OfflineNetworkFactory` returns null-stub implementations for
   every DDS service, so the editor binary has zero DDS dependencies at run-time.

2. **IEditorLogic facade**: all UI panels hold only an `IEditorLogic` reference. No
   panel code has direct access to `EntityRepository`, `FdpEventBus`, or any DDS type.
   This makes panels independently unit-testable.

3. **Event-driven tool switching**: tool activation is a one-liner -
   `_bus.Publish(new ActivateEditorToolEvent(tool))`. The actual tool switch is drained
   by `EditorSubsystem.DrainToolActivationEvents()` in the game loop.

4. **Mode switching without restart**: `SwitchToExternalAsync()` ejects the local
   logic packs from the kernel and installs ACL translator packs, and
   `SwitchToInternalAsync()` reverses this - both without shutting down or recreating
   the ECS world.

5. **Preview via ECS snapshot**: `EditorPreviewController` wraps
   `PreviewClusterOpHandler` to capture an in-memory ECS snapshot on `EnterPreviewMode`
   and rewind to that snapshot on `ExitPreviewMode`.

6. **Composable gizmo system**: all interactive overlays - entity drag, vertex edit,
   route waypoints, obstacle placement, rubber-band selection, and location picking -
   are stateful `IEntityStatefulGizmo` implementations managed by `GlobalGizmoManager`,
   not imperative tool objects.

---

## ASCII Block Diagrams

### Diagram 1 - EditorSubsystem Lifecycle

```
   SubsystemOrchestrator
           |
           | Initialize(SubsystemConfig)
           v
   +---------------------+
   |  EditorSubsystem    |
   |  Initialize()       |
   |                     |
   |  1. new EntityRepo  |
   |  2. Register comps  |
   |  3. MasterSyncCtrl  |
   |  4. Shared services |
   |     (GeoTransform,  |
   |      NetworkEntityMap,
   |      BehaviorReg.,  |
   |      AiHotReload)   |
   |  5. ClusterSlave    |
   |     + handlers      |
   |  6. Module kernel   |
   |     (RegisterModule)|
   |  7. kernel.Init()   |
   |  8. EditorApplica-  |
   |     tion (facade)   |
   |  9. Canvas+Camera   |
   | 10. Adapters + UI   |
   | 11. Window regist.  |
   +---------------------+
           |
           | Update() each frame
           v
   +---------------------+
   |  EditorSubsystem    |
   |  Update()           |
   |  - timeCtrl.Tick()  |
   |  - kernel.Tick()    |
   |  - AI hot-reload    |
   |    drain            |
   |  - missionService   |
   |    PollAcks()       |
   |  - orchestration    |
   |    bus drain        |
   +---------------------+
           |
           | DrawWorld() / DrawUI()
           v
   +---------------------+
   |  Raylib / ImGui     |
   |  DrawWorld:         |
   |  - canvas.Draw()    |
   |  DrawUI:            |
   |  - ImGui panels     |
   |    (gated by        |
   |    isActiveOwner)   |
   +---------------------+
```

### Diagram 2 - IEditorLogic Call Flow (scenario save)

```
   UI Panel (ScenarioBrowserPanel)
           |
           | logic.SaveCurrentScenario()
           v
   +---------------------+
   |  EditorApplication  |
   |  SaveCurrentScenario|
   |  - derives path from|
   |    ScenariosRoot    |
   |    + scenario name  |
   |  - calls             |
   |    fileService      |
   |    .SaveScenario()  |
   +---------------------+
           |
           v
   +---------------------+
   |  ScenarioFileService|
   |  (Hrot.ScenarioEd.) |
   |  - iterates world   |
   |  - serializes to    |
   |    scenario.json    |
   +---------------------+
           |
           v
   +---------------------+
   | NAS / local disk    |
   | {ScenariosRoot}/    |
   |   {name}/scenario.json
   +---------------------+
```

### Diagram 3 - Gizmo Pipeline (entity placement)

```
   User clicks "Place Entity"
           |
           | EditorToolbarPanel.HandleSpawnClick()
           v
   IEditorLogic.ActivateTool(Spawn)
           |
           | FdpEventBus.Publish(ActivateEditorToolEvent)
           v
   EditorSubsystem (game loop)
     DrainToolActivationEvents()
           |
           | SpawnAdapter.StartPlacementMode(tkbType)
           v
   +-------------------------------+
   |  GlobalGizmoManager           |
   |  Register(id, EntityPlacement-|
   |  Gizmo)                       |
   +-------------------------------+
           |
           | User clicks on map
           v
   +-------------------------------+
   |  EntityPlacementGizmo         |
   |  OnMouseEvent(Left, released) |
   |  - seeds EntityInfo (name,    |
   |    affiliation)               |
   |  - compiles JSON attributes   |
   |  - FdpEventBus.Publish(       |
   |    SpawnEntityCommand)        |
   +-------------------------------+
           |
           v
   +-------------------------------+
   |  NetworkSpawningSystem (ECS)  |
   |  - creates entity in repo     |
   |  - assigns NetworkIdentity    |
   |  - applies TKB translators    |
   +-------------------------------+
```

### Diagram 4 - AI Hot-Reload Thread Model

```
   MSBuild writes Hrot.AI.Behaviors.dll
           |
           | FileSystemWatcher (background)
           v
   +-----------------------------+
   |  AiHotReloadCoordinator     |
   |  Background thread:         |
   |  - Load DLL into fresh ALC  |
   |  - Scan for [Registrar]     |
   |    attributes               |
   |  - Enqueue PendingReload    |
   +-----------------------------+
           |
           | ConcurrentQueue<PendingReload>
           v
   +-----------------------------+
   |  Main thread (game loop)    |
   |  DrainPendingCallbacks():   |
   |  - ClearAll (HSM table)     |
   |  - Invoke each registrar    |
   |  - Apply staging            |
   |  - Hot-reload HSM instances |
   |  - Swap ALC                 |
   |  - Fire OnReloadCompleted   |
   +-----------------------------+
```

### Diagram 5 - Mode Switching (Internal <-> External)

```
   EditorApplication.SwitchToExternalAsync()
           |
           v
   +------------------------------+
   |  ModuleHostKernel            |
   |  EjectModulesAsync(          |
   |    logicPacks)               |
   +------------------------------+
           |
           v
   +------------------------------+
   |  ModuleHostKernel            |
   |  InstallModulesAsync(        |
   |    translatorPacks)          |
   +------------------------------+
           |
           | FdpEventBus now routes
           | to ACL translators
           v
   +------------------------------+
   |  External HROT SimHost       |
   |  (DDS / ACL transport)       |
   +------------------------------+

   Reverse path: SwitchToInternalAsync()
   ejects translatorPacks, reinstalls logicPacks.
```

---

## Source Structure

All source files live under `Hrot/Subsystems/Hrot.Editor/`. Namespace root is
`Hrot.Editor`.

### Root namespace (`Hrot.Editor`)

| File                        | Type(s)                                    | Description                                                                    |
|-----------------------------|--------------------------------------------|--------------------------------------------------------------------------------|
| `EditorSubsystem.cs`        | `EditorSubsystem` (sealed class)           | Main `ISubsystem` implementation; composition root for the entire editor.     |
| `EditorApplication.cs`      | `EditorApplication` (sealed class)         | `IEditorLogic` implementation; delegates to `ScenarioFileService` and buses.  |
| `EditorBootstrap.cs`        | `EditorBootstrap` (static class)           | Static factory helpers: `ScenariosRoot`, `CreateFileService()`.               |
| `IEditorLogic.cs`           | `IEditorLogic` (interface)                 | Application-level facade exposed to all UI panels.                            |
| `EditorTool.cs`             | `EditorTool` (enum)                        | Identifies the active interactive editor tool.                                |
| `SimHostMode.cs`            | `SimHostMode` (enum)                       | Tracks Internal vs External SimHost mode.                                     |
| `OfflineNetworkFactory.cs`  | `OfflineNetworkFactory` (sealed class)     | No-op `INetworkFactory`; all methods return null stubs.                       |
| `AiHotReloadCoordinator.cs` | `AiHotReloadCoordinator` (internal sealed) | Manages ALC lifecycle for AI behavior hot-reload (BTree and HSM).             |
|                             | `AiHotReloadCoordinatorOptions` (record)   | Configuration options (PDB loading, watcher debounce).                        |
|                             | `ReloadSource` (enum)                      | Origin of a completed hot reload.                                             |
|                             | `ReloadCompletedInfo` (record)             | Payload delivered to reload-completed subscribers.                            |
|                             | `RegistrarParameter` (record)              | One parameter of a discovered registrar entry-point method.                   |
|                             | `ResolvedRegistrar` (record)               | Metadata for a registrar class discovered via reflection.                     |

### `Hrot.Editor.Adapters`

| File                        | Type(s)                                    | Description                                                                    |
|-----------------------------|--------------------------------------------|--------------------------------------------------------------------------------|
| `EditorSpawnAdapter.cs`     | `EditorSpawnAdapter` (sealed)              | `ISpawnController` for offline editor; translates spawn requests into gizmos. |
| `EditorOrbatAdapter.cs`     | `EditorOrbatAdapter` (sealed)              | `IOrbatDataProvider` + `IOrbatController`; reads ECS repo directly.           |
| `EditorMissionService.cs`   | `EditorMissionService` (sealed)            | `IMissionEditorService`; TAP-based mission commit via `MissionControlIntent`. |
| `EditorMapConfigAdapter.cs` | `EditorMapConfigAdapter` (sealed)          | `IMapConfigController`; reads/writes `MapViewConfig` and canvas layer mask.   |
| `EditorMapPickAdapter.cs`   | `EditorMapPickAdapter` (sealed)            | `IMapPickService`; bridges modal picker gizmos into `Task<T>`.                |
| `EditorZoneAdapter.cs`      | `EditorZoneAdapter` (sealed)              | `IZoneAuthoringController`; zone config and obstacle placement via gizmos.    |
| `EditorPreviewAdapter.cs`   | `EditorPreviewAdapter` (sealed)            | `IPreviewController` for standalone mode; wraps `PreviewClusterOpHandler`.   |

### `Hrot.Editor.Commands`

| File                         | Type(s)                                   | Description                                                      |
|------------------------------|-------------------------------------------|------------------------------------------------------------------|
| `CenterOnEntityCommand.cs`   | `CenterOnEntityCommand` (struct, EventId 8104) | Requests the canvas to pan/zoom to a specified entity.     |

### `Hrot.Editor.Events`

| File                          | Type(s)                                   | Description                                                     |
|-------------------------------|-------------------------------------------|-----------------------------------------------------------------|
| `ActivateEditorToolEvent.cs`  | `ActivateEditorToolEvent` (struct, EventId 8105) | Published when the user selects a new interactive tool.  |

### `Hrot.Editor.Gizmos`

| File                          | Type(s)                                   | Description                                                              |
|-------------------------------|-------------------------------------------|--------------------------------------------------------------------------|
| `LocationPickerGizmo.cs`      | `LocationPickerGizmo` (sealed)            | Fires geo-point callback on left-click; draws crosshair cursor overlay.  |
| `ModalBoxSelectionGizmo.cs`   | `ModalBoxSelectionGizmo` (sealed)         | Fires entity-index list callback on left-click (rubber-band selection).  |
| `ObstaclePlacementGizmo.cs`   | `ObstaclePlacementGizmo` (sealed)         | Fires world-pos callback on left-click; draws sphere radius preview.     |

### `Hrot.Editor.Modules`

| File                         | Type(s)                                   | Description                                                               |
|------------------------------|-------------------------------------------|---------------------------------------------------------------------------|
| `EditorSystemsModule.cs`     | `EditorSystemsModule` (sealed)            | `IEcsModule` that registers and drives the three editor-only ECS systems. |

### `Hrot.Editor.Rendering`

| File                         | Type(s)                                   | Description                                                               |
|------------------------------|-------------------------------------------|---------------------------------------------------------------------------|
| `PerceptionMapLayer.cs`      | `PerceptionMapLayer` (sealed)             | `IMapLayer`; draws dashed target-memory links from perceivers to targets. |

### `Hrot.Editor.Systems`

| File                              | Type(s)                                    | Description                                                                  |
|-----------------------------------|--------------------------------------------|------------------------------------------------------------------------------|
| `EditorCargoSystem.cs`            | `EditorCargoSystem` (sealed)               | Processes `EmbarkEntityCommand` / `DisembarkEntityCommand`; manages `PassengerBuffer`. |
| `EditorPerceptionSetupSystem.cs`  | `EditorPerceptionSetupSystem` (sealed, unsafe) | Processes `SeedTargetCommand`; injects manual target-memory entries.     |
| `EditorZoneAuthoringSystem.cs`    | `EditorZoneAuthoringSystem` (sealed)       | Processes `SpawnZoneObstacleCommand` and `UpdateZoneConfigCommand`.          |

### `Hrot.Editor.UI`

| File                              | Type(s)                                    | Description                                                                |
|-----------------------------------|--------------------------------------------|----------------------------------------------------------------------------|
| `EditorToolbarPanel.cs`           | `EditorToolbarPanel` (sealed)              | ImGui toolbar: Select, Place Entity, Edit Shape, Edit Route, mode toggle, Reload BTrees. |
| `ScenarioBrowserPanel.cs`         | `ScenarioBrowserPanel` (sealed)            | ImGui file browser: New / Save / Save As / Load with modal dialogs.       |
| `EditorOrbatPanel.cs`             | `EditorOrbatPanel` (sealed)                | ImGui entity list reading from `IEditorLogic.View`.                       |
| `EntityPropertyInspector.cs`      | `EntityPropertyInspector` (sealed)         | ImGui property editor committed via `IEditorLogic.CommitPropertyEdit`.   |
| `JsonEntityContextMenuHandler.cs` | `JsonEntityContextMenuHandler` (sealed)    | `IEntityContextMenuHandler`; populates context menus from `ContextMenuState.MenuJson`. |
| `TimeControlStatusBarSection.cs`  | `TimeControlStatusBarSection` (internal sealed) | Status-bar transport controls (play/pause/step/stop) for preview mode. |
| `EditorTimeTransportAdapter.cs`   | `EditorTimeTransportAdapter` (internal sealed)  | `ITimeTransportFacade`; bridges `IPreviewController` + `MasterSyncController`. |

### `Hrot.Editor.Windows`

| File                 | Type(s)                                                                | Description                                                                   |
|----------------------|------------------------------------------------------------------------|-------------------------------------------------------------------------------|
| `EditorWindows.cs`   | `EditorWindowColor` (static internal)                                  | Editor title-bar colour constant (slate blue).                                |
|                      | `EditorToolbarWindow` (sealed `ManagedWindow`)                         | Perspective-bound window hosting `EditorToolbarPanel`.                        |
|                      | `EditorBrowserWindow` (sealed `ManagedWindow`)                         | Perspective-bound window hosting `ScenarioBrowserPanel`.                      |
|                      | `EditorOrbatWindow` (sealed `ManagedWindow`)                           | Perspective-bound window hosting `EditorOrbatPanel`.                          |
|                      | `EditorSpawnerWindow` (sealed `ManagedWindow`)                         | Perspective-bound window hosting `SpawnerPanel`.                              |
|                      | `EditorMissionWindow` (sealed `ManagedWindow`)                         | Perspective-bound window hosting `MissionPanel`.                              |
|                      | `EditorConfigWindow` (sealed `ManagedWindow`)                          | Perspective-bound window hosting `ConfigPanel`.                               |
|                      | `EditorSharedOrbatWindow` (sealed `ManagedWindow`)                     | Perspective-bound window hosting `SharedOrbatPanel`.                          |
|                      | `EditorPreviewWindow` (sealed `ManagedWindow`)                         | Perspective-bound window hosting `PreviewPanel`.                              |
|                      | `EditorZoneEditorWindow` (sealed `ManagedWindow`)                      | Perspective-bound window hosting `ZoneEditorPanel`.                           |
|                      | `EditorClusterScenarioWindow` (sealed `ManagedWindow`)                 | Perspective-bound window hosting `ClusterScenarioPanel`.                      |
|                      | `EditorClusterDiagnosticsWindow` (sealed `ManagedWindow`)              | Perspective-bound window hosting `ClusterDiagnosticsPanel`.                   |

---

## Public API Reference

### `IEditorLogic` (interface)

Application-level facade. UI panels are permitted to call only these methods.

| Member                                               | Return type                      | Description                                                              |
|------------------------------------------------------|----------------------------------|--------------------------------------------------------------------------|
| `Update()`                                           | `void`                           | Steps the internal state machine; must be called once per frame.         |
| `NewScenario()`                                      | `void`                           | Clears the world and resets simulation time to zero.                     |
| `SaveScenario(string filePath)`                      | `void`                           | Serializes current world state to the specified file path.               |
| `LoadScenario(string filePath)`                      | `void`                           | Clears the world, then deserializes entities from the specified file.    |
| `LoadScenarioByName(string scenarioName)`            | `void`                           | Loads a scenario by name from the scenarios root directory.              |
| `SaveCurrentScenario()`                              | `void`                           | Saves to the most recently loaded scenario name.                         |
| `SaveScenarioAs(string scenarioName)`                | `void`                           | Saves and remembers the new scenario name.                               |
| `LoadedScenarioName`                                 | `string?`                        | Currently loaded scenario name, or `null` after `NewScenario`.           |
| `AvailableScenarios`                                 | `IReadOnlyList<string>`          | Scenario names available in the scenarios root directory.                |
| `ActivateTool(EditorTool tool)`                      | `void`                           | Activates the specified interactive map tool.                            |
| `CommitPropertyEdit(long networkId, IReadOnlyList<object> components)` | `void`   | Publishes `UpdateEntityCommand` for the specified entity.                |
| `View`                                               | `IDerRepo`                       | Read-only DER view of the entity set (for panel data binding).           |
| `SwitchToExternalAsync()`                            | `Task`                           | Ejects local logic packs; installs ACL translator packs.                 |
| `SwitchToInternalAsync()`                            | `Task`                           | Reinstalls local logic packs; ejects ACL translator packs.               |
| `CurrentMode`                                        | `SimHostMode`                    | Current `Internal` or `External` mode.                                   |
| `CenterOnEntity(long entityId)`                      | `void`                           | Pans and zooms the canvas to center on the entity.                       |
| `SelectEntity(long entityId)`                        | `void`                           | Programmatically selects an entity and switches to the Select tool.      |
| `OpenRenameDialog(long entityId)`                    | `void`                           | Opens the in-map rename dialog for an entity.                            |
| `RebuildAndReloadAI()`                               | `void`                           | Triggers MSBuild rebuild followed by AI hot-reload of behavior DLLs.     |

### `EditorSubsystem` (sealed class)

| Member                                               | Return type                      | Description                                                              |
|------------------------------------------------------|----------------------------------|--------------------------------------------------------------------------|
| `Name`                                               | `string`                         | Returns `"Editor"`.                                                       |
| `TitleBarColor`                                      | `Vector4`                        | Slate blue `(0.15, 0.22, 0.48, 1)` - distinct from other subsystems.    |
| `Initialize(SubsystemConfig config)`                 | `void`                           | Full composition root: ECS world, kernel, modules, adapters, panels.     |
| `Update()`                                           | `void`                           | Steps time controller, ticks kernel, drains hot-reload and orch. bus.   |
| `DrawWorld()`                                        | `void`                           | Renders the 2-D map canvas (skipped in headless mode).                   |
| `DrawUI()`                                           | `void`                           | Renders ImGui panels not registered as managed windows.                  |
| `RegisterWindows(IWindowManager mgr)`                | `void`                           | Registers editor panels with the Window Manager for docking.             |
| `Shutdown()`                                         | `void`                           | Disposes kernel, ECS world, and file-system watcher.                     |
| `GetCameraView()`                                    | `MapCameraView?`                 | `IMapCameraProvider` implementation; returns current camera view.        |
| `ApplyCameraView(MapCameraView view)`                | `void`                           | `IMapCameraProvider` implementation; applies an external camera view.    |
| `GizmoController`                                    | `GizmoExecutionController`       | `IGizmoControllable` - allows external gizmo perspective switching.      |
| `AiBehaviorsProjectPath`                             | `string[]`                       | Relative path segments to `Hrot.AI.Behaviors.csproj`. Settable.          |
| `World` (internal)                                   | `EntityRepository`               | Test hook: direct access to the ECS world.                               |
| `Kernel` (internal)                                  | `ModuleHostKernel`               | Test hook: direct access to the kernel.                                  |
| `EditorLogic` (internal)                             | `IEditorLogic`                   | Test hook: direct access to the logic facade.                            |
| `TimeController` (internal)                          | `MasterSyncController`           | Test hook: direct access to the time controller.                         |
| `PreviewController` (internal)                       | `IPreviewController`             | Test hook: direct access to the preview controller.                      |

### `EditorApplication` (sealed class)

Implements `IEditorLogic`. Beyond the interface:

| Member                                                  | Return type      | Description                                                             |
|---------------------------------------------------------|------------------|-------------------------------------------------------------------------|
| `SetAvailableScenariosSource(Func<IReadOnlyList<string>> source)` | `void` | Injects scenario list provider to avoid circular references.          |

### `EditorBootstrap` (static class)

| Member                    | Return type            | Description                                                          |
|---------------------------|------------------------|----------------------------------------------------------------------|
| `ScenariosRoot`           | `string`               | Absolute path: `{NasBasePath}/{ScenariosDirectoryName}`.             |
| `CreateFileService()`     | `ScenarioFileService`  | Builds a `ScenarioFileService` with a `HrotScenarioSerializer`.      |

### `EditorTool` (enum)

| Value     | Description                                                              |
|-----------|--------------------------------------------------------------------------|
| `Select`  | Standard selection and drag mode (default).                              |
| `Spawn`   | Entity placement mode; activates `EntityPlacementGizmo`.                 |
| `Edit`    | Vertex edit mode for overlay shapes; activates `VertexEditGizmo`.        |
| `Route`   | Route waypoint edit mode; activates `RouteWaypointGizmo`.                |
| `Measure` | Measurement line mode; activates measurement tool.                       |
| `Rotate`  | Entity rotation mode; injects `EntityRotatorGizmo` via `DataDrivenGizmoSystem`. |

### `SimHostMode` (enum)

| Value      | Description                                                  |
|------------|--------------------------------------------------------------|
| `Internal` | Local FDP SimHost logic packs are installed and active.      |
| `External` | Local packs ejected; ACL translator packs are active.        |

### `OfflineNetworkFactory` (sealed class)

Implements `INetworkFactory`. All factory methods return null-object implementations:
`NullReplicationModule`, `NullCommandGateway`, `NullExConEgressWriters`,
`NullTimeControlGateway`, `NullSimHostMissionSender`,
`NullSimHostAuxiliaryTranslators`, `NullSimHostPathfindingTranslators`,
`NullSimHostPerceptionTranslators`, `NullIgTranslators`, `NullIgNetworkAdapter`.
The `Participant` property always returns `null`.

### `AiHotReloadCoordinator` (internal sealed class)

| Member                                                        | Description                                                 |
|---------------------------------------------------------------|-------------------------------------------------------------|
| `TriggerInitialLoad()`                                        | Loads the current DLL immediately on the background thread. |
| `DrainPendingCallbacks()`                                     | Must be called from the main thread each frame.             |
| `OnReloadCompleted` (event `Action<ReloadCompletedInfo>`)     | Fired after a successful reload is applied.                 |
| `OnReloadFailed` (event `Action<Exception>`)                  | Fired when a reload attempt fails.                          |
| `ScanForRegistrars(Assembly asm)`                             | Static helper: discovers `[Registrar]`-decorated classes.   |
| `Dispose()`                                                   | Stops the file-system watcher and releases resources.       |

### Adapter Public APIs

**`EditorSpawnAdapter`**

| Member                                                         | Description                                                          |
|----------------------------------------------------------------|----------------------------------------------------------------------|
| `LastSelectedTkbType`                                          | TKB type most recently passed to `StartPlacementMode`.               |
| `StartPlacementMode(long tkbType, string? initialPropertiesJson)` | Registers an `EntityPlacementGizmo` via `GlobalGizmoManager`.    |
| `StartPlacementModeWithLastType()`                             | Re-activates placement with the last selected type.                  |
| `StartAreaAuthoring(...)` / `StartRouteAuthoring(...)`         | Registers `PointSequenceGizmo` for overlay / route drawing.          |
| `CancelPlacementMode()`                                        | Removes all active placement gizmos.                                 |

**`EditorOrbatAdapter`** (implements `IOrbatDataProvider` + `IOrbatController`)

| Member                                          | Description                                          |
|-------------------------------------------------|------------------------------------------------------|
| `GetVisibleNodes(filterText, expandedNodes)`    | Rebuilds entity index cache and returns ORBAT tree.  |
| `ToggleExpanded(entityId)`                      | Expands or collapses an ORBAT node.                  |
| `SelectNode(entityId)`                          | Selects entity and centers view on it.               |
| `EmbarkUnit(passenger, vehicle)` / `DisembarkUnit(passenger)` | Publishes embark/disembark commands.   |
| `CreateUnit(parentEntityId?)`                   | Delegates to `ISpawnController.StartPlacementMode`.  |
| `DeleteUnit(entityId)`                          | Publishes `DestroyEntityCommand`.                    |

**`EditorMissionService`** (implements `IMissionEditorService`)

| Member                                                  | Description                                                       |
|---------------------------------------------------------|-------------------------------------------------------------------|
| `GetAvailableBehaviors(entityId)`                       | Returns behavior names intersected from catalog and live registry.|
| `GetMissionSnapshot(entityId)`                          | Returns current `MissionPlan` from `ActiveMissionPlan` component. |
| `CommitMissionAsync(entityId, plan, baseVersion)`       | TAP: publishes `MissionControlIntent`, resolves on ACK.           |
| `SendControlCommandAsync(entityId, commandType, ...)`   | TAP: publishes `MissionControlIntent` for control commands.       |
| `PollAcks()`                                            | Drains `MissionControlAckEvent` from bus; resolves pending tasks. |

**`EditorMapPickAdapter`** (implements `IMapPickService`)

| Member                                                  | Description                                                    |
|---------------------------------------------------------|----------------------------------------------------------------|
| `PickLocationAsync(CancellationToken)`                  | Returns `Task<GeoPoint>`; registers `LocationPickerGizmo`.     |
| `PickEntityAsync(filterPresets, CancellationToken)`     | Returns `Task<int>` (network ID); registers `EntityPickerGizmo`. |
| `PickAreaEntitiesAsync(filterPresets, CancellationToken)` | Returns `Task<IReadOnlyList<int>>`; registers `ModalBoxSelectionGizmo`. |

**`EditorMapConfigAdapter`** (implements `IMapConfigController`)

| Member                           | Description                                            |
|----------------------------------|--------------------------------------------------------|
| `GetCurrentConfig()`             | Returns `MapLayerState` from `MapViewConfig` + canvas. |
| `ApplyConfig(MapLayerState)`     | Writes `MapViewConfig` and updates canvas layer mask.  |

**`EditorZoneAdapter`** (implements `IZoneAuthoringController`)

| Member                                          | Description                                          |
|-------------------------------------------------|------------------------------------------------------|
| `SetRoadNetworkPath(zoneName, assetPath)`        | Publishes `UpdateZoneConfigCommand`.                 |
| `StartObstaclePlacementMode(zoneName, radius)`  | Registers `ObstaclePlacementGizmo`; publishes `SpawnZoneObstacleCommand` on click. |

**`EditorPreviewAdapter`** (implements `IPreviewController`)

| Member                           | Description                                                          |
|----------------------------------|----------------------------------------------------------------------|
| `IsInPreviewMode`                | True when state is `LoadingPreview` or `OperatingPreview`.           |
| `EnterPreviewMode(startPaused)`  | Calls `PreviewClusterOpHandler.TriggerLoadingPreview()`.             |
| `ExitPreviewMode()`              | Calls `PreviewClusterOpHandler.TriggerUnloadingPreview()`.           |

### ECS Systems

**`EditorCargoSystem`** (implements `IEcsModuleSystem`, phase `Simulation`)

Processes `EmbarkEntityCommand` and `DisembarkEntityCommand` to manage
`PassengerBuffer` and `IsEmbarkedTag`. Strips/restores movement and combat
capabilities (`ActorCapabilities.CanMove | CanShoot`) when units embark/disembark.

**`EditorPerceptionSetupSystem`** (implements `IEcsModuleSystem`, phase `Simulation`, `unsafe`)

Processes `SeedTargetCommand` events. For each valid perceiver/target pair, calls
`TargetMemory.AddOrUpdateTarget()` with the target's current `SimTransform` position
and score boost.

**`EditorZoneAuthoringSystem`** (implements `IEcsModuleSystem`, phase `Simulation`)

Processes `SpawnZoneObstacleCommand` (creates obstacle entity with `SimTransform`,
`PhysicsCollider`, `ZoneMembership`) and `UpdateZoneConfigCommand` (loads road network
blob and sets `ZoneEnvironmentData` singleton). Mirrors both to `ZoneManagerService`
for the save pipeline.

### Rendering

**`PerceptionMapLayer`** (implements `IMapLayer`)

| Member           | Description                                                            |
|------------------|------------------------------------------------------------------------|
| `Name`           | `"Perception Links"`                                                   |
| `LayerBitIndex`  | `9`                                                                    |
| `Draw(ctx)`      | Draws dashed red lines from each perceiver to each active target slot. |

### Gizmos

**`LocationPickerGizmo`** (implements `IEntityStatefulGizmo`)

Draws a sky-blue crosshair at the cursor. On left-click, calls `IGeographicTransform`
to convert Cartesian world position to `GeoPoint`, fires the `onLocationPicked`
callback, and unregisters itself.

**`ModalBoxSelectionGizmo`** (implements `IEntityStatefulGizmo`)

Fires `onSelectionComplete` with a list of entity indices on left-click. Currently
fires an empty list; full box-query wiring is deferred.

**`ObstaclePlacementGizmo`** (implements `IEntityStatefulGizmo`)

Draws a red sphere at the cursor. On left-click, fires `onObstaclePlaced` with the
world position and unregisters itself.

### Events and Commands

| Type                       | EventId | Kind    | Description                                             |
|----------------------------|---------|---------|---------------------------------------------------------|
| `ActivateEditorToolEvent`  | 8105    | struct  | Published when user selects a new tool from toolbar.    |
| `CenterOnEntityCommand`    | 8104    | struct  | Requests canvas pan/zoom to `NetworkId`.                |

---

## Dependencies

### Project References

| Project                          | Purpose                                                                             |
|----------------------------------|-------------------------------------------------------------------------------------|
| `Hrot.Presentation`              | `ISubsystem`, `IWindowRegistrar`, `ManagedWindow`, window manager, ImGui utilities. |
| `Fdp.Toolkits`                   | ECS core (`EntityRepository`, `FdpEventBus`), all FDP toolkits (Behavior, Vis2D, etc.). |
| `Hrot.SimHost`                   | `SimHostCoreLogicPack`, `SimHostModule`, serializer factory, attribute compiler.    |
| `Hrot.CGF`                       | `CgfLogicPack`, `CgfComponentRegistry`, CGF orchestration handlers.                |
| `Hrot.Orchestrator`              | Offline orchestrator: `ClusterMaster`, `ClusterUiCache`, orchestrator panels.       |
| `Hrot.IG`                        | `MapLayerAssignmentSystem`, `MapCullingModule`, `StyleResolutionModule`, IG gizmos.  |
| `Hrot.Network.NED`               | `AttributeCompilerFactory` used by `EditorSubsystem` for JSON-to-ECS compilation.  |
| `Fdp.Examples.Scenarios`         | `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates` and `RegisterUrbanCombatBehaviors`. |
| `Hrot.AI.Behaviors`              | Ensures the DLL is copied to the output directory for hot-reload. No static types used. |

### NuGet Packages

| Package       | Version  | Purpose                                                        |
|---------------|----------|----------------------------------------------------------------|
| `Raylib-cs`   | 7.0.2    | 2-D rendering backend: `MapCanvas`, `Raylib.DrawLineEx`, etc. |
| `rlImgui-cs`  | 3.2.0    | Raylib / Dear ImGui bridge for all editor UI panels.           |
| `NLog`        | 5.2.8    | Structured diagnostic logging throughout the editor.           |

### InternalsVisibleTo

The following test assemblies are granted access to `internal` members:

- `Hrot.Editor.Tests`
- `Hrot.ClusterRunner.Integration.Tests`
- `Hrot.Blueprints.Tests`

---

## Usage Examples

### Example 1 - Embedding the Editor as a Subsystem

```csharp
// Typical usage in ClusterRunner or standalone editor executable.
var editorSubsystem = new EditorSubsystem();
editorSubsystem.AiBehaviorsProjectPath = new[]
{
    "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj"
};

var config = new SubsystemConfig
{
    Headless         = false,
    IsActiveMapOwner = () => true,
};

editorSubsystem.Initialize(config);

// Register editor panels with the shared window manager.
editorSubsystem.RegisterWindows(windowManager);

// Game loop.
while (!shouldExit)
{
    editorSubsystem.Update();
    editorSubsystem.DrawWorld();
    editorSubsystem.DrawUI();
}

editorSubsystem.Shutdown();
```

### Example 2 - Scenario File Operations via IEditorLogic

```csharp
// Obtained from EditorSubsystem.EditorLogic (internal) or injected.
IEditorLogic logic = editorSubsystem.EditorLogic;

// Create a blank scenario.
logic.NewScenario();

// Load an existing scenario from the scenarios root directory.
logic.LoadScenarioByName("hill-attack");

// Make authoring changes via tool activations or context menu ...

// Save back to the same name.
logic.SaveCurrentScenario();

// Save a copy under a different name.
logic.SaveScenarioAs("hill-attack-v2");

// Save to an explicit file path (used in unit tests and batch export).
logic.SaveScenario(@"C:\Temp\export\scenario.json");
```

### Example 3 - Entity Placement from a UI Panel

```csharp
// Panels only hold IEditorLogic; no direct ECS or DDS references.
public sealed class MyCustomPanel
{
    private long _selectedTkbType = TkbEntityTypes.Tank_M1Abrams;

    public void DrawContent(IEditorLogic logic)
    {
        if (ImGui.Button("Place Tank"))
        {
            // Publish ActivateEditorToolEvent(Spawn) via the facade.
            logic.ActivateTool(EditorTool.Spawn);
        }

        if (ImGui.Button("Center on Entity 1042"))
        {
            logic.CenterOnEntity(1042);
        }

        ImGui.Text($"Mode: {logic.CurrentMode}");

        if (logic.CurrentMode == SimHostMode.Internal)
        {
            if (ImGui.Button("Connect to External SimHost"))
                _ = logic.SwitchToExternalAsync();
        }
        else
        {
            if (ImGui.Button("Disconnect from External"))
                _ = logic.SwitchToInternalAsync();
        }
    }
}
```

### Example 4 - Using EditorBootstrap in a Lightweight Context

```csharp
// EditorBootstrap is useful in headless tools (e.g. batch scenario validators)
// that need a serializer but do not need the full subsystem.
var fileService = EditorBootstrap.CreateFileService();

string scenariosRoot = EditorBootstrap.ScenariosRoot;
// => e.g. "\\NAS\HROT\scenarios"

string scenarioPath = Path.Combine(scenariosRoot, "hill-attack", "scenario.json");
var world = new EntityRepository();
SimHostComponentRegistry.RegisterAll(world);

// Load the scenario into a temporary world for offline inspection.
fileService.LoadScenario(world, scenarioPath);
```

### Example 5 - Adding a Custom ECS System via EditorSystemsModule Pattern

```csharp
// To add a new editor-only ECS system, follow the EditorSystemsModule pattern:
// 1. Implement IEcsModuleSystem with [UpdateInPhase(SystemPhase.Simulation)].
public sealed class MyEditorSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        // Read from view, write to repo.
        foreach (var cmd in view.ReadEvents<MyEditorCommand>())
        {
            // ...process command...
        }
    }
}

// 2. Create a module that wraps your system.
public sealed class MyEditorModule : IEcsModule
{
    private readonly MyEditorSystem _system = new();
    public string Name => "MyEditor";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public void Tick(ISimulationView view, float deltaTime)
        => _system.Execute(view, deltaTime);
}

// 3. Register in EditorSubsystem.Initialize() before kernel.Initialize():
//    _kernel.RegisterModule(new MyEditorModule());
```

---

## Best Practices

### Panel Isolation

All UI panels must access the editor state exclusively through the `IEditorLogic`
interface. Direct field access to `EntityRepository`, `FdpEventBus`, or any DDS type
inside panel code is forbidden. This rule is enforced by code review and keeps panels
unit-testable without a running kernel.

### Event-Based Tool Activation

Never activate editor tools by calling adapter methods directly from panel code.
Always call `IEditorLogic.ActivateTool(EditorTool)`, which publishes an
`ActivateEditorToolEvent`. The `EditorSubsystem` game loop drains this event and
performs the switch in a single location, preventing double-activation races.

### Hot-Reload Safety

The `AiHotReloadCoordinator` uses a two-thread model: loading happens on a background
thread, application happens on the main thread. Never call `DrainPendingCallbacks()`
from a background thread or access the `EntityRepository` from the load thread.
The mandated order inside `DrainPendingCallbacks()` is: `ClearAll` BEFORE invoking
registrars.

### Headless Mode

`EditorSubsystem.Initialize(SubsystemConfig { Headless = true })` skips all
`MapCanvas`, `MapCamera`, and adapter construction. All canvas-dependent adapters
(`EditorSpawnAdapter`, `EditorMapPickAdapter`, `EditorZoneAdapter`,
`EditorMapConfigAdapter`) are `null` in headless mode. Systems and file operations
work identically. Headless mode is used by integration tests and automated scenario
validation pipelines.

### Preview Lifecycle

Enter preview only via `IEditorLogic` (or `IPreviewController`). Never write to
`EntityRepository` outside of ECS systems while in preview mode - all changes will
be discarded on `ExitPreviewMode()`. The `TimeControlStatusBarSection` binds the
stop button's enabled state to `IsInPreviewMode`, preventing accidental rewinding
outside of a preview session.

### Serialization Order

The `HrotScenarioSerializerFactory` must be called AFTER all components are registered
with the `EntityRepository`. `FdpAutoSerializer` compiles property-extraction delegates
at `Build()` time against the current `ComponentTypeRegistry`. Calling it before
component registration results in an empty serialization schema.

### Gizmo Lifecycle

Every gizmo registered with `GlobalGizmoManager` must be unregistered via its
`onRemove` callback when the operation completes or is cancelled. The `onRemove`
parameter defaults to a no-op but must always call `GlobalGizmoManager.Unregister(id)`
in production code. Failing to unregister leaks a gizmo slot and may cause duplicate
input processing.

### Mode Switching

`SwitchToExternalAsync()` and `SwitchToInternalAsync()` are fire-and-forget async
methods. They must be called only from the main thread (e.g. from a UI button handler)
and are safe to fire-and-forget with `_ = logic.SwitchToExternalAsync()` because the
kernel drains the operation during the next few game-loop ticks. Do not `await` these
calls on the UI thread - the coroutine continuation expects to run on the game loop
thread.

---

## Related Projects

| Project                             | Relationship                                                                   |
|-------------------------------------|--------------------------------------------------------------------------------|
| `Hrot.Editor.AiShared`              | Shared AI contracts and types used by both the editor and the AI behaviors assembly. |
| `Hrot.AI.Behaviors`                 | AI behavior tree and HSM implementations hot-reloaded by `AiHotReloadCoordinator`. |
| `Hrot.ScenarioEditor`               | Gizmos, systems, services, and handlers shared between the editor and the cluster scenario editor UI (e.g. `ScenarioFileService`, `PreviewClusterOpHandler`, `SelectionInteractionSystem`). |
| `Hrot.SimHost`                      | SimHost logic packs, serializer factory, and attribute compiler used by the editor in Internal mode. |
| `Hrot.CGF`                          | CGF logic pack (BTree execution, combat, pathfinding) active when the editor runs Internal mode with AI. |
| `Hrot.Orchestrator`                 | Offline orchestrator panels (`ClusterScenarioPanel`, `ClusterUiCache`) and `ClusterMaster` for scenario listing. |
| `Hrot.IG`                           | Map layer assignment, culling, and style resolution modules used by the editor canvas. |
| `Hrot.Presentation`                 | Window manager, `ISubsystem`, `ManagedWindow`, `FdpEntityInspectorPanel`, `FdpEventBrowserPanel`. |
| `Hrot.UI.Common`                    | Shared UI facades and panels: `SpawnerPanel`, `MissionPanel`, `SharedOrbatPanel`, `ZoneEditorPanel`, `PreviewPanel`, `ConfigPanel`. |
| `Hrot.Network.NED`                  | JSON-to-ECS attribute compiler used for `InitialAttributesJson` processing during entity placement. |
| `Fdp.Toolkits`                      | FDP framework: ECS, event bus, behavior toolkit, Vis2D, physics, perception, blueprints, scenario I/O. |
| `Fdp.Examples.Scenarios`            | Urban Combat scenario registrations (TKB templates, behavior registrations) embedded in the editor. |
| `Hrot.Editor.Tests`                 | Unit and integration tests for `EditorSubsystem`, `EditorApplication`, adapters, and ECS systems. |
| `Hrot.ClusterRunner.Integration.Tests` | Integration tests that use `EditorSubsystem` as a subsystem within a full cluster context. |
| `Hrot.Blueprints.Tests`             | Blueprint tests that access editor coordinator internals via `InternalsVisibleTo`.    |
