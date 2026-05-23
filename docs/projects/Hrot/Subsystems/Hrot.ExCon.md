# Hrot.ExCon

**Project path:** `Hrot/Subsystems/Hrot.ExCon/Hrot.ExCon.csproj`
**Date:** 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` exists in `Hrot/Subsystems/Hrot.ExCon/`. All architectural context is encoded solely in XML doc-comments across the source files.

---

## Executive Overview

`Hrot.ExCon` implements the **Exercise Control** (ExCon) operator station for the HROT military simulation cluster. In the military simulation domain, Exercise Control is the "God mode" node: it is the human-facing workstation from which an exercise director or umpire monitors all simulation participants, issues commands, and controls the lifecycle of the running scenario.

Concretely, ExCon in HROT performs the following roles:

- **Scenario lifecycle control** -- issues Pause / Resume / Step / SetTimeScale commands to the cluster Orchestrator over DDS, which fans them out to every node (IG instances, SimHost, CGF nodes).
- **Entity placement and authoring** -- allows an operator to spawn simulation entities (tanks, infantry squads, overlays, routes) onto the map by activating tool modes on the Image Generator (IG). The placement is confirmed via a two-phase ACK handshake with the CGF.
- **Mission assignment** -- reads each entity's current `MissionPlan` from the DER repository and can commit replacement plans or send imperative control commands (Jump, Abort, etc.) to the CGF.
- **Cluster-wide monitoring** -- observes every node's operational status and time-sync state from DDS via a dedicated observer bus, populating the `ClusterUiCache` that drives the Cluster Scenario panel.
- **Context-menu dispatch** -- on every IG selection-change event, computes and publishes an updated `ContextActionsUpdate` DDS message so that the IG renders the correct right-click menu for the selected entity.
- **Diagnostics** -- collects rolling DDS events/second metrics, surfaces pending request timeouts, and participates in the cluster-wide diagnostic-dump workflow.

ExCon occupies a unique position in the cluster: it does **not** own simulation state (no ECS world, no scenario fragments) but observes all of it. It participates in the orchestration two-phase commit solely to ACK and not stall the cluster.

---

## Architecture

### Layer Overview

ExCon is decomposed into four conceptual layers that enforce strict dependency direction (UI panels depend only on interfaces; adapters bridge between shared facades and the logic layer; services are pure infrastructure).

```
+------------------------------------------------------------------+
|                       ExConSubsystem                             |
|  ISubsystem + IWindowRegistrar (composition root / entry point) |
+-----------------------------------+------------------------------+
                                    |
          +-------------------------+-------------------------+
          |                                                   |
+---------+----------+                             +----------+----------+
|     ExConMock      |                             |  ClusterSlave /      |
|  (frame-cycle      |                             |  SlaveSyncController |
|   orchestrator)    |                             |  ClusterUiCache      |
+---------+----------+                             +----------+----------+
          |                                                   |
+---------+----------+                             +----------+----------+
|     ExConLogic     |                             |  FdpEventBus (_bus) |
|  (IExConLogic)     |<----------------------------+  FdpEventBus        |
|  core state +      |                             |  (_observerBus)     |
|  command logic     |                             +---------------------+
+---------+----------+
          |
  +-------+--------+--------+--------+---------+
  |        |        |        |         |        |
+---+ +------+ +------+ +-------+ +-------+ +----+
|Srv| |Panel | |Panel | |Panel  | |Panel  | |Adp |
|---| |ORBAT | |Inter-| |Diagn. | |Mission| |----|
|   | |      | |action| |       | |       | |    |
+---+ +------+ +------+ +-------+ +-------+ +----+
```

### Dual-Bus Design (DDS Echo Prevention)

A central architectural decision is the use of **two separate `FdpEventBus` instances** to prevent a DDS feedback storm:

- `_bus` -- the **active command bus**. Used by `ClusterSlave`, `SlaveSyncController`, and all outgoing command translators. When ExCon publishes a `NodeOpStatus` to DDS it flows only through this bus.
- `_observerBus` -- the **observer bus**. Used by `OrchestrationObserverTranslator` and `ClusterUiCache`. These components read incoming `NodeOpStatus` messages from DDS and publish derived UI events here without ever touching `_bus`.

Without this separation, the observer would re-inject status events back into the command channel, causing `NodeOpSlaveTranslator` to re-emit DDS messages, creating an exponential feedback loop.

```
  DDS Network
      |         writes NodeOpStatus
      |<--------------------------------------- _bus (command channel)
      |
      | reads NodeOpStatus
      +------> OrchestrationObserverTranslator
                        |  publishes NodeOpCompletedEvent
                        v
               _observerBus (observer channel) -----> ClusterUiCache
                                                         (UI only)
```

### Entity Placement Two-Phase ACK

Placing an entity on the map requires a two-phase acknowledgement from the CGF:

```
  ExCon                      IG                       CGF
    |                         |                         |
    |-- CMD_PLACE_ENTITY ----->|                         |
    |   (MapCommandRequest)   |                         |
    |                         |-- CreateEntityRequest ->|
    |<-- MapCommandAck(Ack) --|                         |
    |   (status = InProgress) |                         |
    |                         |<-- EntityLifecycleAck --|
    |                         |   (Phase 1: InProgress) |
    |<-- EntityLifecycleAck --|                         |
    |   (Phase 1: entityId)   |                         |
    |  [guard entity in UI]   |                         |
    |                         |<-- EntityLifecycleAck --|
    |                         |   (Phase 2: OK)         |
    |<-- EntityLifecycleAck --|                         |
    |   (Phase 2: OK)         |                         |
    | [unguard, auto-select]  |                         |
```

Phase-1 entities are added to `_pendingEntities`; context menus and mission interactions are suppressed for them until Phase 2 arrives. On Phase-2 failure, `_globalAlert` is set and the operator sees a modal error.

### Per-Frame Update Pipeline

The `ExConSubsystem.Update` method drives the following ordered pipeline each frame:

```
  Frame N
  +--------------------------------------------------------------+
  | 1. PollIngress: timeModeTranslator, lockstepTranslator,      |
  |                  timeSyncTranslator  (write to _bus write buf)|
  |                                                              |
  | 2. SlaveSyncController.Update()  (reads _bus current buf)    |
  |                                                              |
  | 3. lockstepTranslator.ScanAndPublish()  (ACK egress)         |
  | 4. timeSyncTranslator.ScanAndPublish()  (NTP handshake)      |
  |                                                              |
  | 5. _bus.SwapBuffers()        [single boundary swap]          |
  | 6. _observerBus.SwapBuffers()                                |
  |                                                              |
  | 7. slaveTranslator.Tick()    (NodeOp ingress + egress)       |
  | 8. clusterSlave.Tick()       (heartbeat + 2PC state machine) |
  | 9. mergeWorker.Tick()        (log merge)                     |
  |10. observer.Tick()           (DDS obs -> bus events)         |
  |11. uiCache.Update()          (bus events -> UI model)        |
  |12. clusterPanel.Update()     (scenario panel tick)           |
  |13. ExConMock.Update()        (entity logic + panels)         |
  +--------------------------------------------------------------+
```

---

## Source Structure

All files reside under `Hrot/Subsystems/Hrot.ExCon/`. The root namespace is `Hrot.ExCon`.

### Root namespace -- `Hrot.ExCon`

| File | Type | Description |
|------|------|-------------|
| `ExConSubsystem.cs` | `class ExConSubsystem` | `ISubsystem` + `IWindowRegistrar` entry point. Composition root: creates all services, buses, panels, and the `ExConMock`. |
| `ExConLogic.cs` | `class ExConLogic`, `enum ExConPickMode` | Core application logic. Drives per-frame DDS ingress polling, event processing, pick-mode state machine, and command dispatch. Implements `IExConLogic`, `IMapPickService`, `ISpawnController`. |
| `IExConLogic.cs` | `interface IExConLogic` | Facade consumed by all UI panels. Decouples panels from `ExConLogic` for unit testability. |
| `ExConMock.cs` | `class ExConMock` | Frame-cycle orchestrator. Owns the `ExConLogic` instance and wires it to all panels. Drives `Update` and `DrawUI`. |
| `ExConPanelAdapters.cs` | `ExConSpawnShim`, `ExConMapConfigShim`, `ExConMissionShim` | Temporary shim adapters bridging `IExConLogic` to shared UI facade interfaces (to be replaced by Phase 6 proper adapters). |
| `ExConLogicConstants.cs` | `static class ExConLogicConstants` | All named constants for map group IDs, target map IDs, tool names, and log topic names. |

### Panels -- `Hrot.ExCon.Panels`

| File | Type | Description |
|------|------|-------------|
| `OrbatPanel.cs` | `class OrbatPanel`, `class OrbatNode` | Order-of-Battle tree panel. Builds a collapsible entity hierarchy from `EntityInfo.CommanderId` with cycle guard and depth cap. |
| `InteractionPanel.cs` | `class InteractionPanel`, `record LogEntry` | Live DDS interaction log (TX/RX). Thread-safe via `ConcurrentQueue`; capped at `PanelConstants.MaxLogEntries`. |
| `DiagnosticsPanel.cs` | `class DiagnosticsPanel` | Runtime diagnostics: entity count, pending requests, rolling events/s metric. |
| `InspectorPanel.cs` | `class InspectorPanel`, `record InspectorLine` | **Deprecated.** Raw descriptor field viewer using cached reflection. Superseded by `DerEntityInspectorPanel`. |
| `DataMonitorPanel.cs` | `class DataMonitorPanel` | **Deprecated.** Entity list + descriptor tree viewer. Superseded by `DerEntityInspectorPanel`. |
| `ExConPanelColors.cs` | `static class ExConPanelColors` | Violet title-bar colour constants and `Push`/`Pop` helpers for ImGui styling. |
| `PanelConstants.cs` | `static class PanelConstants` | Centralised constants for all panels (capacities, thresholds, sentinel values). |

### Logic -- `Hrot.ExCon.Logic`

| File | Type | Description |
|------|------|-------------|
| `IContextMenuLogic.cs` | `interface IContextMenuLogic`, `enum MenuStrategy` | Context-menu strategy manager interface. |
| `ContextMenuLogic.cs` | `class ContextMenuLogic` | Strategy-based context menu generator. Builds and publishes `ContextActionsUpdate` on each IG selection-change event. |
| `ContextMenuItem.cs` | `class ContextMenuItem` | Single context menu entry. Serialises to the JSON schema expected by the IG. |

### Services -- `Hrot.ExCon.Services`

| File | Type | Description |
|------|------|-------------|
| `IEventQueue.cs` | `interface IEventQueue<T>` | Thread-safe pull queue for DDS event samples. |
| `ConcurrentEventQueue.cs` | `class ConcurrentEventQueue<T>` | Default `IEventQueue<T>` backed by `ConcurrentQueue<T>`. |
| `IRequestTransactionManager.cs` | `interface IRequestTransactionManager` | Tracks in-flight DDS requests and their correlation IDs. |
| `RequestTransactionManager.cs` | `class RequestTransactionManager` | Implements `IRequestTransactionManager` with 5-second timeout detection. |
| `PendingRequest.cs` | `class PendingRequest` | Immutable snapshot of a single in-flight DDS request. |
| `IMissionEditorService.cs` | `interface IMissionEditorService` | Mission snapshot reads and async commit with optimistic concurrency. |
| `MissionEditorService.cs` | `class MissionEditorService` | Implements `IMissionEditorService` via `IDerRepo` reads and `ICommandGateway` dispatches. |
| `IMapPickService.cs` | `interface IMapPickService` | Async location and entity pick service backed by `CMD_PICK_LOCATION` / `CMD_PICK_ENTITY` DDS round-trips. |
| `ITimeProvider.cs` | `interface ITimeProvider`, `class SystemTimeProvider` | Clock abstraction for deterministic unit testing of timeout logic. |
| `DdsEventIngressHandlers.cs` | `class TimeModeIngressHandler` | DDS ingress handler that polls `SwitchTimeModeWireDto` and forwards samples to a callback. |

### Adapters -- `Hrot.ExCon.Adapters`

| File | Type | Description |
|------|------|-------------|
| `ExConOrbatAdapter.cs` | `class ExConOrbatAdapter` | Implements `IOrbatDataProvider` and `IOrbatController` over `IDerRepo` + `IExConLogic`. Builds the ORBAT tree with an O(n) parent-map pass and DFS walk. |
| `ExConMapConfigAdapter.cs` | `class ExConMapConfigAdapter` | Implements `IMapConfigController` by serialising `MapLayerState` to a JSON Merge Patch and forwarding via `IExConLogic.SendConfigPatch`. |
| `ExConEntityActionAdapter.cs` | `class ExConEntityActionAdapter` | Implements `IEntityActionController` by forwarding calls to `IExConLogic`. |
| `JsonContextMenuBuilder.cs` | `class JsonContextMenuBuilder` | Implements `IContextMenuBuilder` by collecting `ContextMenuItem` DTOs with monotonically increasing IDs and storing associated action callbacks. |

### Windows -- `Hrot.ExCon.Windows`

| File | Type | Description |
|------|------|-------------|
| `ExConWindows.cs` | `ExConConfigWindow`, `ExConOrbatWindow`, `ExConMissionWindow`, `ExConDataMonitorWindow`, `ExConSpawnerWindow`, `ExConDiagnosticsWindow` + `ExConWindowColor` | Perspective-bound `ManagedWindow` wrappers that host each ExCon panel within the shared Window Manager. All carry the violet title-bar colour. |

---

## Public API Reference

### `ExConSubsystem`

```csharp
public sealed class ExConSubsystem : ISubsystem, IWindowRegistrar
```

| Member | Signature | Description |
|--------|-----------|-------------|
| `Name` | `string Name { get; }` | Returns `"ExCon"`. |
| `TitleBarColor` | `Vector4 TitleBarColor { get; }` | Violet (0.32, 0.08, 0.48, 1). |
| `.ctor()` | `ExConSubsystem()` | Creates subsystem without a network factory (offline/legacy). |
| `.ctor(factory)` | `ExConSubsystem(INetworkFactory)` | Creates subsystem with an injected network factory from the composition root. |
| `Initialize` | `void Initialize(SubsystemConfig)` | Builds the entire ExCon object graph: buses, cluster slave, time sync, services, panels, logic. |
| `Update` | `void Update(float deltaTime)` | Drives one frame: time-sync ingress, bus swap, orchestration tick, ExCon logic tick. |
| `DrawWorld` | `void DrawWorld()` | No-op (ExCon has no 3-D world visuals). |
| `DrawUI` | `void DrawUI()` | Renders all ImGui panels. Skipped in headless mode. |
| `RegisterWindows` | `void RegisterWindows(WindowManager)` | Registers all ExCon panels as `ManagedWindow` instances with the shared Window Manager. |
| `Shutdown` | `void Shutdown()` | Disposes all owned resources in reverse construction order. |

Internal test hooks (visible to `Hrot.ExCon.Tests`, `Hrot.ClusterRunner.Tests`, `Hrot.ClusterRunner.Integration.Tests`):

| Member | Type | Purpose |
|--------|------|---------|
| `BusForTest` | `FdpEventBus?` | Active command bus. |
| `ObserverBusForTest` | `FdpEventBus?` | Observer-only bus. |
| `UiCacheForTest` | `ClusterUiCache?` | Cluster UI state cache. |
| `Logic` | `ExConLogic` | Core logic instance. |
| `TestHook_NodeIdOverride` | `int` | Effective node ID wired at init. |
| `TestHook_ClusterSlave` | `ClusterSlave?` | Cluster slave for handler-registration assertions. |
| `TestHook_SlaveSyncController` | `SlaveSyncController?` | Time-sync controller. |

---

### `IExConLogic`

```csharp
public interface IExConLogic
```

State properties:

| Property | Type | Description |
|----------|------|-------------|
| `Repo` | `IDerRepo` | DER entity repository (simulation state). |
| `MissionEditorService` | `IMissionEditorService` | Mission plan read/commit service. |
| `MapPickService` | `IMapPickService` | Async location/entity pick service. |
| `TransactionManager` | `IRequestTransactionManager` | In-flight DDS request tracker. |
| `GlobalAlert` | `string?` | Non-null indicates a Phase-2 failure that should be surfaced as a modal alert. |
| `MasterSimTime` | `double` | Current simulation time in seconds. |
| `MasterWallTicks` | `long` | Current wall-clock ticks (UTC). |
| `MasterTimeScale` | `float` | Current time scale factor. |
| `IsPaused` | `bool` | True when the simulation is paused. |

Command methods:

| Method | Description |
|--------|-------------|
| `SendConfigPatch(string jsonPatch)` | Publishes a JSON Merge Patch as a `MapInteractionConfig` message. |
| `StartPlacementMode(long tkbType, string?)` | Activates entity placement tool on the IG via `CMD_PLACE_ENTITY`. |
| `StartPlacementMode(long tkbType, EntityPropertyPatch?)` | Typed overload; serialises properties to JSON. |
| `StartAreaAuthoringMode(string styleOverrideJson)` | Activates polygon area authoring via `CMD_START_AUTHORING`. |
| `StartRouteAuthoringMode()` | Activates route authoring tool via `CMD_START_AUTHORING` with `TacGraphic_Route`. |
| `StartEditingMode(long networkEntityId)` | Activates overlay vertex editing via `CMD_START_EDITING`. |
| `SelectEntity(int entityId)` | Sets local selection state. |
| `SendSetSelection(int entityId)` | Sets selection locally and publishes `CMD_SET_SELECTION` to the IG. |
| `CenterOnEntity(int entityId)` | Publishes `CMD_SET_VIEW` to pan the IG camera to the entity. |
| `DeleteEntity(int entityId)` | Issues a delete command and adds the entity to the pending-delete guard set. |
| `IsEntityPendingDelete(int entityId)` | Returns true while a delete ACK is awaited. |
| `StartPersonalRouteAuthoring(int vehicleEntityId)` | Issues `CMD_DRAW_PERSONAL_ROUTE` for a specific vehicle. |
| `IsEntityPending(int entityId)` | Returns true while Phase-1 ACK received but Phase-2 not yet arrived. |
| `DismissAlert()` | Clears `GlobalAlert`. |
| `OpenSpawner()` | Signals the shell to bring the Spawner panel to the foreground. |
| `RequestPause()` | Sends a PauseTime `ClusterOpRequest` to the Orchestrator. |
| `RequestResume()` | Sends a ResumeTime `ClusterOpRequest`. |
| `RequestStep()` | Sends a StepTime `ClusterOpRequest`. |
| `SetTimeScale(float scale)` | Sends a SetTimeScale `ClusterOpRequest`. |

---

### `ExConLogic`

```csharp
public sealed class ExConLogic : IExConLogic, IMapPickService,
    Hrot.UI.Common.Facades.ISpawnController, IDisposable
```

Key observable state beyond the interface:

| Property | Type | Description |
|----------|------|-------------|
| `ActiveContextId` | `Guid` | Context ID embedded in the most recent `MapInteractionConfig` published. Incoming click events are dropped when their context ID does not match. |
| `PlacementType` | `long` | TKB type requested for the next entity placement. 0 when none. |
| `SelectedEntityId` | `int` | Entity ID currently selected in the UI. 0 when none. |
| `PickMode` | `ExConPickMode` | Active interactive pick mode (`None`, `EntityCreation`, `Location`, `Entity`). |
| `SpawnerRequested` | `bool` | Set by `OpenSpawner`; cleared by `ConsumeSpawnerRequest`. |

Key methods:

| Method | Description |
|--------|-------------|
| `Update()` | Polls ingress handlers, drains log queue, processes event queues, checks timeouts. |
| `CancelPendingPick()` | Cancels any outstanding location or entity `TaskCompletionSource`. |
| `ConsumeSpawnerRequest()` | Resets `SpawnerRequested` after the shell has acted on it. |

---

### `ExConMock`

```csharp
public sealed class ExConMock : IDisposable
```

| Member | Description |
|--------|-------------|
| `Logic` | Exposes the underlying `ExConLogic` for testing and diagnostics. |
| `Update(float dt)` | Drives one frame: calls `ExConLogic.Update`, syncs selection to `MissionPanel`, forwards `SpawnerRequested`. |
| `DrawUI()` | Renders all panels in an ImGui `DockSpace` or free-floating mode. |
| `GetConfigPanel()` | Returns the `ConfigPanel` instance. |
| `GetOrbatPanel()` | Returns the `OrbatPanel` instance. |
| `GetMissionPanel()` | Returns the `MissionPanel` instance. |
| `GetInteractionPanel()` | Returns the `InteractionPanel` instance. |
| `GetSpawnerPanel()` | Returns the `SpawnerPanel` instance. |
| `GetDiagnosticsPanel()` | Returns the `DiagnosticsPanel` instance. |
| `MapConfigAdapter` | Returns the `IMapConfigController` (Phase 6 `ExConMapConfigAdapter`). |
| `MissionShim` | Returns the `IMissionEditorService` bridge. |
| `MapPickShim` | Returns the `IMapPickService` bridge. |
| `SpawnController` | Returns the `ISpawnController` (delegated to `ExConLogic` directly). |

---

### `OrbatPanel` / `OrbatNode`

```csharp
public sealed class OrbatPanel
public sealed class OrbatNode
```

| Member | Description |
|--------|-------------|
| `FilterText` | Case-insensitive substring filter for entity names. |
| `IsExpanded(int entityId)` | True when the node is currently expanded. |
| `FindRootEntities(IDerRepo)` | Enumerates entities with `CommanderId == 0`. |
| `FindChildren(int parentId, IDerRepo)` | Enumerates direct subordinates of a parent entity. |
| `MatchesFilter(string name, string filter)` | Returns true when the name passes the current filter. |
| `ToggleExpanded(int entityId)` | Toggles expanded/collapsed state. |
| `HandleEntityClick(int entityId, IExConLogic)` | Processes an operator click on an ORBAT node. |
| `GetVisibleNodes(IDerRepo, IExConLogic)` | Returns the flattened, filtered, and depth-capped node list. |

---

### `InteractionPanel` / `LogEntry`

```csharp
public sealed class InteractionPanel
public sealed record LogEntry(DateTime Time, string Direction, string Topic, string Details)
```

| Member | Description |
|--------|-------------|
| `Entries` | Read-only view of committed log entries (oldest to newest). |
| `EntryCount` | Number of committed entries. |
| `AddLog(string direction, string topic, string details)` | Thread-safe enqueue into staging buffer. |
| `DrainPendingLogs()` | Main-thread drain from staging to committed list. Returns count drained. |
| `Draw(IExConLogic)` | Renders the two-pane log table via ImGui. |

---

### `DiagnosticsPanel`

```csharp
public sealed class DiagnosticsPanel
```

| Member | Description |
|--------|-------------|
| `EventsPerSecond` | Most recently committed events/s reading. |
| `RecordEvent()` | Increments the in-window event counter. |
| `Update(float dt)` | Advances the sample window; commits rate after `DiagnosticsEventRateSampleWindowS` seconds. |
| `GetEntityCount(IDerRepo)` | Static helper: total entity count. |
| `GetPendingRequestSnapshot(IRequestTransactionManager)` | Static helper: snapshot of pending requests. |

---

### `IRequestTransactionManager` / `RequestTransactionManager`

```csharp
public interface IRequestTransactionManager
public sealed class RequestTransactionManager : IRequestTransactionManager
```

| Member | Description |
|--------|-------------|
| `TrackRequest(Guid, string)` | Begins tracking a new outgoing request. |
| `CompleteRequest(Guid, bool, string?)` | Marks a request resolved; no-op for unknown IDs. |
| `GetPendingRequests()` | Returns a snapshot of all pending requests. |
| `CheckTimeouts()` | Completes any requests exceeding `DefaultTimeoutMs` (5000 ms) with `success=false`. |
| `DefaultTimeoutMs` | `5000` ms. |

---

### `IMissionEditorService` / `MissionEditorService`

```csharp
public interface IMissionEditorService : IDisposable
public sealed class MissionEditorService : IMissionEditorService
```

| Member | Description |
|--------|-------------|
| `GetAvailableBehaviors(long entityId)` | Returns valid behavior names for the entity's TKB type. |
| `GetMissionSnapshot(long entityId)` | Returns `(MissionPlan?, version)` from the DER descriptor. |
| `CommitMissionAsync(long, MissionPlan, long)` | Sends a full-replace mission command; awaits CGF ACK with 5 s timeout. |
| `SendControlCommandAsync(long, eMissionCommandType, Guid)` | Sends an imperative control command and awaits ACK. |
| `SendControlCommand(long, eMissionCommandType, Guid)` | Fire-and-forget control command. |
| `DefaultCommitTimeoutMs` | `5000` ms. |

---

### `IMapPickService`

```csharp
public interface IMapPickService
```

| Method | Description |
|--------|-------------|
| `PickLocationAsync(CancellationToken)` | Sends `CMD_PICK_LOCATION` to the IG; returns `Task<GeoPoint>` resolved on operator map click. |
| `PickEntityAsync(string[]?, CancellationToken)` | Sends `CMD_PICK_ENTITY` with optional layer filters; returns `Task<int>` resolved with the entity ID. |

---

### `IContextMenuLogic` / `ContextMenuLogic`

```csharp
public interface IContextMenuLogic
public sealed class ContextMenuLogic : IContextMenuLogic
public enum MenuStrategy { Standard, Admin, DamageControl, Logistics }
```

| Member | Description |
|--------|-------------|
| `CurrentStrategy` | Active menu-building strategy. |
| `SetStrategy(MenuStrategy)` | Switches active strategy; takes effect on next push. |
| `OnSelectionChanged(SelectionChangedEventDto, Func<int,bool>?)` | Builds and publishes context actions for the new selection. |
| `OnActionInvoked(ContextActionInvokedDto)` | Dispatches the invoked action callback; fires `ActionInvoked`. |
| `ActionInvoked` | Event raised when the IG user invokes a context action. |

---

### `ExConOrbatAdapter`

```csharp
public sealed class ExConOrbatAdapter : IOrbatDataProvider, IOrbatController
```

Builds the ORBAT tree via an O(n) parent-map pass over the DER repo followed by a DFS walk from root entities. Implements cycle guard and respects `IsPendingDelete` guards.

---

### `ExConMapConfigAdapter`

```csharp
public sealed class ExConMapConfigAdapter : IMapConfigController
```

Converts `MapLayerState` to a JSON Merge Patch (RFC 7396) and dispatches via `IExConLogic.SendConfigPatch`.

---

### `JsonContextMenuBuilder`

```csharp
public sealed class JsonContextMenuBuilder : IContextMenuBuilder
```

| Method | Description |
|--------|-------------|
| `AddItem(string, Action, bool)` | Adds an interactive menu item with a monotonically increasing integer ID. |
| `BeginSubmenu(string)` | Returns `this` (sub-menus are flattened). |
| `EndSubmenu()` | No-op. |
| `AddSeparator()` | Adds a separator entry. |
| `Build()` | Returns the ordered item list for JSON serialisation. |
| `GetCallbackRegistry()` | Returns the ID-to-callback map for dispatch on `ContextActionInvoked`. |

---

### `PanelConstants`

```csharp
public static class PanelConstants
```

| Constant | Value | Purpose |
|----------|-------|---------|
| `IconScaleMin` | `0.5f` | Config panel icon scale lower bound. |
| `IconScaleMax` | `2.0f` | Config panel icon scale upper bound. |
| `IconScaleDefault` | `1.0f` | Config panel icon scale default. |
| `MaxLogEntries` | `100` | Interaction log rolling cap. |
| `MaxOrbatDepth` | `32` | ORBAT tree recursion depth cap. |
| `FilterTextMaxLength` | `256` | ImGui filter text buffer size. |
| `InspectorNoSelection` | `0` | Sentinel entity ID for "no selection". |
| `InspectorMaxTotalLines` | `256` | Max reflection lines per entity (InspectorPanel). |
| `DiagnosticsEventRateSampleWindowS` | `5.0f` | Rolling events/s sample window duration. |
| `MissionBehaviorParamsMaxLength` | `2048` | Max chars in mission behavior params editor. |

---

## Dependencies

### Project References

| Project | Path | Role |
|---------|------|------|
| `Hrot.Presentation` | `Hrot/Engine/Hrot.Presentation` | Shared UI panel library (ConfigPanel, MissionPanel, SpawnerPanel, SharedOrbatPanel, DerEntityInspectorPanel, ManagedWindow, WindowManager). |
| `Hrot.Common` | `Hrot/Engine/Hrot.Common` | HROT data model and map types (`EntityInfo`, `GeoPoint`, `DerRepo`, DDS message types). |
| `Fdp.Presentation` | `FDP/Engine/Fdp.Presentation` | FDP toolkit UI layer (`WinFormsFileDialogService`, `ImGuiPropertyTree`, etc.). |
| `Hrot.Network.Orchestration` | `Hrot/Network/Hrot.Network.Orchestration` | HROT-specific orchestration network module (ingress/egress factory methods). |
| `Hrot.Orchestrator` | `Hrot/Subsystems/Hrot.Orchestrator` | Cluster scenario, diagnostics, and window panels shared between ExCon and ClusterRunner. |

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Newtonsoft.Json` | 13.0.3 | JSON serialisation of command arguments, context menus, and mission plans. |
| `Raylib-cs` | 7.0.2 | Window creation and rendering shell (used by application executable, not by the library itself). |
| `rlImGui-cs` | 3.2.0 | Raylib-to-ImGui bridge (`rlImGui.Begin`/`End` block used in `DrawUI`). |

### `InternalsVisibleTo` grants

Internal members (test hooks, shims) are exposed to:
- `Hrot.ExCon.Tests`
- `Hrot.ClusterRunner.Tests`
- `Hrot.ClusterRunner.Integration.Tests`

---

## Usage Examples

### 1. Bootstrapping ExCon in a Host Application

The standard integration path is via `SubsystemConfig`. The host (e.g. `ClusterRunner`) constructs an `INetworkFactory` that wraps an existing `DdsParticipant` and passes it to `ExConSubsystem`.

```csharp
// Composition root (application executable)
var participant  = new DdsParticipant();
var factory      = new HrotNetworkFactory(participant);
var exConSubsys  = new ExConSubsystem(factory);

var config = new SubsystemConfig
{
    NodeId   = 500,
    Headless = false,
    OwnWindow = true,
};

exConSubsys.Initialize(config);

// Register panels with the shared window manager
exConSubsys.RegisterWindows(windowManager);

// Main loop
while (!Raylib.WindowShouldClose())
{
    float dt = Raylib.GetFrameTime();

    exConSubsys.Update(dt);

    Raylib.BeginDrawing();
    rlImGui.Begin();
    exConSubsys.DrawUI();
    rlImGui.End();
    Raylib.EndDrawing();
}

exConSubsys.Shutdown();
```

### 2. Placing an Entity on the Map

The ExCon operator clicks "Spawn" in the UI, which eventually calls `StartPlacementMode`. Subsequent map clicks are processed by `ExConLogic.Update` via the `MapClickEvent` ingress queue.

```csharp
// From a UI button callback (main thread)
IExConLogic logic = exConMock.Logic;

// Create an M1 Abrams with a friendly affiliation override
var props = new EntityPropertyPatch
{
    Name        = "Alpha-1",
    Affiliation = "FORCE_FRIENDLY",
};
logic.StartPlacementMode(TkbEntityTypes.Tank_M1Abrams, props);

// ExConLogic publishes CMD_PLACE_ENTITY to the IG.
// The IG activates the placement tool cursor.
// When the operator clicks the map, ExConLogic receives MapClickEvent,
// fires CreateEntityRequest to the CGF, and starts a two-phase ACK cycle.

// Meanwhile, panels can poll for pending state:
if (logic.IsEntityPending(lastKnownEntityId))
{
    // Disable mission-panel interactions until Phase-2 ACK arrives
}
```

### 3. Requesting an Async Map Location Pick

Panel code uses `IMapPickService.PickLocationAsync` to request the operator to click a location on the IG map canvas. This is used by the `MissionPanel` when editing a "move to" behavior parameter.

```csharp
// Inside a panel method running on the main thread
private Task<GeoPoint>? _pendingPick;

void OnMoveToButtonClicked(IExConLogic logic)
{
    _pendingPick = logic.MapPickService.PickLocationAsync();
}

// Called once per frame from panel Draw
void Draw(IExConLogic logic)
{
    if (_pendingPick is { IsCompleted: true })
    {
        if (!_pendingPick.IsCanceled && !_pendingPick.IsFaulted)
        {
            GeoPoint pos = _pendingPick.Result;
            HandleMoveToLocation(pos);
        }
        _pendingPick = null;
    }
    // ... rest of panel rendering
}
```

### 4. Committing a Mission Plan

```csharp
// Read the current plan snapshot (synchronous, main thread safe)
var (plan, version) = logic.MissionEditorService.GetMissionSnapshot(entityId);
if (plan is null) return;

// Modify the plan
plan.Tasks[0].BehaviorParams = "{\"speed\": 12.0}";

// Commit asynchronously and await (do not block the main thread;
// use async button callback or fire-and-forget with continuation)
var result = await logic.MissionEditorService.CommitMissionAsync(entityId, plan, version);
if (!result.Success)
    interactionPanel.AddLog("ERR", "MissionCommit", result.ErrorMessage ?? "Unknown error");
```

### 5. Using DiagnosticsPanel in Isolation (Unit Test Pattern)

```csharp
// Tests exercise DiagnosticsPanel without an ImGui context
var panel = new DiagnosticsPanel();

// Simulate DDS events arriving
for (int i = 0; i < 10; i++)
    panel.RecordEvent();

// Advance past the 5-second sample window
panel.Update(5.1f);

Assert.Equal(10.0f / 5.1f, panel.EventsPerSecond, precision: 1);
```

---

## Best Practices

### Threading Model

- `ExConLogic.Update`, `ExConMock.Update`, and all `Draw*` methods must be called from the **main (Raylib) thread** only.
- `InteractionPanel.AddLog` is the **only** method safe to call from background threads (DDS ingress callbacks). Always call `DrainPendingLogs` from `Update` before drawing.
- Never write to DDS writers from a background thread. All DDS writes happen inside `Update` or synchronous main-thread button callbacks.

### Dual-Bus Invariant

When adding new network-facing translators to `ExConSubsystem.Initialize`:

- Translators that **publish** `NodeOpStatus` or outgoing cluster commands must be wired to `_bus`.
- Translators that **observe** cluster status (for UI display only) must be wired to `_observerBus`.
- Violating this rule causes the DDS echo storm described in the `_observerBus` field comment.

### Context ID Discipline

Every `MapInteractionConfig` or `MapCommandRequest` sent to the IG must embed a freshly generated `ActiveContextId`. Incoming `MapClickEvent` samples with a stale context ID are silently dropped by `ExConLogic.ProcessClickEvents`. Always call `Guid.NewGuid()` when activating a new tool mode; never reuse the previous ID.

### Two-Phase ACK Guards

Before allowing the operator to assign a mission or invoke context-menu actions on an entity, always check `IsEntityPending(entityId)`. The entity exists in the DER repo from Phase-1 but may not be fully committed in the CGF until Phase-2 arrives. Interacting with a half-baked entity can produce undefined behavior in the simulation.

### Avoid Null Factory in Production

The parameterless `ExConSubsystem()` constructor is a legacy path for offline tooling only. In production cluster deployments, always provide an `INetworkFactory` so that DDS participants, ingress handlers, and gateways are properly wired. Without a factory, all egress writers are `Null*` stubs and no DDS traffic occurs.

### `InspectorPanel` / `DataMonitorPanel` are Deprecated

Both `InspectorPanel` and `DataMonitorPanel` are annotated `[Obsolete]` and will be removed. New code should use `DerEntityInspectorPanel` from the `Fdp.Presentation` toolkit, which supports live descriptor updates, search, and extensible context menus via `RegisterContextMenuHandler`.

---

## Related Projects

| Project | Relationship |
|---------|-------------|
| `Hrot.Orchestrator` | Provides `ClusterScenarioPanel`, `ClusterDiagnosticsPanel`, `DiagnosticLogMergeWorker`, and window types used by ExCon. ExCon depends on this subsystem. |
| `Hrot.Network.Orchestration` | Provides the `INetworkFactory`, ingress/egress handler factories, and all DDS-backed gateways (`IExConEgressWriters`, `ICommandGateway`, `ITimeControlGateway`, `ISlaveOrchestrationTranslator`, `IOrchestrationObserver`). |
| `Hrot.Common` | Provides the DER entity model (`IDerRepo`, `IDerEntity`, `EntityInfoDescriptor`, `EntityMissionDescriptor`), DDS message DTOs (`MapClickEventDto`, `SelectionChangedEventDto`, `MapCommandDto`, etc.), and `GeoPoint`. |
| `Hrot.Presentation` | Provides the shared UI panel library: `ConfigPanel`, `MissionPanel`, `SpawnerPanel`, `SharedOrbatPanel`, `DerEntityInspectorPanel`, `ManagedWindow`, `WindowManager`. ExCon wires these panels to its logic layer via the adapter and shim types. |
| `Fdp.Presentation` | Provides `WinFormsFileDialogService`, `ImGuiPropertyTree`, and toolkit-level ImGui helpers. |
| `Fdp.Toolkit.Orchestration` | Provides `ClusterSlave`, `FdpEventBus`, `OrchestrationEventRegistry`, `SlaveSyncController`, `TimeNetworkModule`, and all cluster orchestration infrastructure. ExCon acts as a `ClusterSlave` participant (not a master). |
| `Fdp.Toolkit.DER` | Provides `IDerRepo`, `DerRepo`, `IDerEntity`, generic descriptor access, and `TkbEntityTypes` constants used for entity spawning. |
| `Hrot.IG` (Image Generator) | **Runtime peer.** ExCon sends `MapInteractionConfig`, `MapCommandRequest`, and `ContextActionsUpdate` messages to the IG over DDS. The IG sends back `MapClickEvent`, `SelectionChangedEvent`, and `MapCommandAck`. |
| `Hrot.CGF` (Computer-Generated Forces) | **Runtime peer.** ExCon sends `CreateEntityRequest`, `MissionControlRequest`, and delete commands to the CGF. The CGF responds with `EntityLifecycleAck` and `MissionControlAck`. |
| `Hrot.ClusterRunner` | **Host executable.** Embeds `ExConSubsystem` alongside `IgSubsystem`, `SimHostSubsystem`, and other subsystems. Provides the `INetworkFactory` and calls the `ISubsystem` lifecycle methods. |
