# DESIGN.md — Shared UI Library & Hrot.Editor Feature Completion (`edit-1`)

## Background and Vision

With `packs-2` delivering the **Hrot.Editor** application shell, offline composition root,
`ScenarioEditorModule`, and the Feature Switch connecting the editor to the distributed cluster,
and `packs-3` delivering Zone Definitions, ACL hardening, and `NetworkGatewaySystem` DRY
refactor, `edit-1` focuses on **completing the Hrot.Editor UI and extracting a shared,
infrastructure-agnostic panel library**.

The core problem: all the UI panels the editor needs already exist — in `Hrot.ExCon`,
`Hrot.IG`, or `Hrot.SimHost`.  Copying them into `Hrot.Editor` would create a maintenance
nightmare and violate the DRY principle.  The clean solution is to extract the panels into a
new `Hrot.UI.Common` shared library that has **zero dependencies on CycloneDDS, DerRepo, or
IExConLogic**, consuming exclusively focused capability *Ports* (interfaces) instead.

In addition to the DRY extraction, `edit-1` introduces **three new authoring capabilities**
that are needed to reproduce the Urban Combat demo entirely through the UI:
- **Embarkation & Cargo Management** — load infantry into vehicles via ORBAT drag-and-drop.
- **Target Memory Seeding** — link a perceiver to a target entity on the map.
- **Dynamic Behavior Catalog** — mission behavior dropdowns filtered by entity TKB blueprint,
  driven by `BehaviorCatalog` in `Hrot.Map.Definitions`.

### Architecture Principles

- **Dependency Inversion Principle (DIP):** Panels depend on interfaces; adapters implement
  those interfaces using the host's concrete infrastructure (ECS/FdpEventBus for the Editor,
  IDerRepo/DDS for ExCon).
- **Interface Segregation Principle (ISP):** Each panel receives the smallest focused Port it
  needs — never a "god interface" like `IExConLogic`.
- **CQRS:** Panels never mutate ECS state directly.  They publish domain commands.  Dedicated
  ECS systems execute those commands during the thread-safe kernel tick.
- **Single Responsibility:** Rendering logic lives in `Hrot.UI.Common`.  Validation and domain
  invariants live in FDP execution systems.  File I/O lives in application-layer services.

---

## Component Map

```
┌───────────────────────────────────────────────────────────────────────┐
│                    Hrot.UI.Common (Presentation Layer)                 │
│  SpawnerPanel      MissionPanel     SharedOrbatPanel  ConfigPanel      │
│  PreviewPanel      ZoneEditorPanel  SharedContextMenuPopulator         │
│  (FdpEntityInspectorPanel & FdpEventBrowserPanel from FDP.Toolkit.ImGui│
│   are used directly — already decoupled)                               │
└────────────────────────┬──────────────────────────────────────────────┘
                         │ depend on Ports (interfaces) only
┌────────────────────────▼──────────────────────────────────────────────┐
│                 Hrot.UI.Common.Facades (Ports)                         │
│  ISpawnController         IMissionEditorService   IOrbatDataProvider   │
│  IOrbatController         IMapConfigController    IPreviewController   │
│  IZoneAuthoringController IMapPickService         IEntityActionController│
└────┬──────────────────────────────────────┬─────────────────────────┘
     │                                       │
     ▼  Hrot.Editor Adapters                 ▼  Hrot.ExCon Adapters
 EditorSpawnAdapter                      ExConOrbatAdapter
 EditorMissionService                    ExConLogic (implements ISpawnController)
 EditorOrbatAdapter                      MissionEditorService (NED adapter)
 EditorMapPickAdapter                    ExConMapConfigAdapter
 EditorZoneAdapter
 EditorEntityContextMenuHandler
 EditorPreviewAdapter
 EditorMapConfigAdapter
     │
     ▼  FDP Engine / Kernel
 FdpEventBus    EntityRepository    MapCanvas (Vis2D)
 EditorCargoSystem                  EditorPerceptionSetupSystem
 EditorZoneAuthoringSystem          PerceptionMapLayer
```

---

## Phase 0: `Hrot.UI.Common` — Shared Library Foundation

**Goal:** Create the new project, define every Port interface and shared DTO.  No
implementation code goes here — only contracts and pure rendering panels.

### 0.A — Create `Hrot.UI.Common` Project & Facade Interfaces

**New project:** `Hrot.UI.Common.csproj` (new class library inside `Hrot.UI.Common/`).

**Allowed references:**
- `FDP.Toolkit.ImGui` (for `IEntityContextMenuHandler`, `IContextMenuBuilder`,
  `FdpEntityInspectorPanel`, `FdpEventBrowserPanel` types)
- `FDP.Toolkit.DER` (for `IDerRepo` used by ExCon adapters — reference only in the
  adapter namespaces, not in panel namespaces)
- `Hrot.NED` (strictly for shared DTOs and enums — no DDS transport types)
- `Hrot.Map.Definitions` (for `TkbEntityTypes`, `BehaviorCatalog`)

**Forbidden references:** `Hrot.ExCon`, `CycloneDDS`, `ModuleHost`, `Hrot.SimHost`.

**Facade interfaces to define (namespace `Hrot.UI.Common.Facades`):**

```
ISpawnController
    void StartPlacementMode(long tkbType, string? initialPropertiesJson = null)
    void StartAreaAuthoringMode(string styleOverrideJson = "")
    void StartRouteAuthoringMode()

IMissionEditorService
    IReadOnlyList<string> GetAvailableBehaviors(long entityId)
    (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId)
    Task<MissionCommitResult> CommitMissionAsync(long entityId, MissionPlan plan, long baseVersion)
    Task<MissionCommitResult> SendControlCommandAsync(long entityId, eMissionCommandType type, Guid taskId)

IOrbatDataProvider
    IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(string filterText, HashSet<int> expandedNodes)

IOrbatController
    void SelectEntity(int entityId)
    void CreateUnit(long tkbType)
    void ToggleExpanded(int entityId)
    void RequestEmbark(int passengerEntityId, int vehicleEntityId)
    void RequestDisembark(int passengerEntityId)

IMapConfigController
    MapLayerState GetCurrentConfig()
    void ApplyConfig(MapLayerState config)

IPreviewController
    bool IsInPreviewMode { get; }
    void EnterPreviewMode()
    void ExitPreviewMode()

IZoneAuthoringController
    void SetRoadNetworkPath(string activeZoneName, string assetPath)
    void StartObstaclePlacementMode(string activeZoneName, float radius)

IMapPickService
    Task<GeoPoint> PickLocationAsync(CancellationToken ct = default)
    Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default)
    Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null,
                                                   CancellationToken ct = default)

IEntityActionController
    void CenterOnEntity(long entityId)
    void DeleteEntity(long entityId)
    void EditOverlay(long entityId)
    void EditRoute(long entityId)
    void Rename(long entityId)
    void ActivateMeasureTool()
```

**Shared DTOs (namespace `Hrot.UI.Common.Models`):**

```csharp
public sealed record OrbatNodeViewModel(
    int EntityId, string Name, int Depth, bool HasChildren, bool IsPendingDelete);

public record MapLayerState(bool Satellite, bool GroundUnits, bool AirUnits, bool Grid);
public record MissionCommitResult(bool Success, long NewVersion, string? ErrorMessage = null);
```

### 0.B — `BehaviorCatalog` in `Hrot.Map.Definitions`

A static, compile-time capability map that restricts which behaviors each TKB entity type
may use.  Lives alongside `TkbEntityTypes` in `Hrot.Map.Definitions.Tkb`.

```csharp
// Hrot.Map.Definitions/Tkb/BehaviorCatalog.cs
public static class BehaviorCatalog
{
    public static IReadOnlyList<string> GetValidBehaviors(long tkbType) => tkbType switch
    {
        TkbEntityTypes.CivilianPedestrian => ["WanderCivil", "PanicFlee"],
        TkbEntityTypes.CivilianCar        => ["WanderCivil", "PanicFlee"],
        TkbEntityTypes.MilitaryApc        => ["ConvoyEscort", "MoveToLocation", "FollowRoute"],
        TkbEntityTypes.InfantrySoldier    => ["InfantryCombat", "MoveToLocation", "JoinFormation"],
        TkbEntityTypes.Insurgent          => ["Ambush", "MoveToLocation"],
        _                                 => ["MoveToLocation", "FollowRoute", "JoinFormation", "Idle"]
    };
}
```

**Rationale:** The FDP engine's `BehaviorRegistry` must not be polluted with
application-level `TkbType` routing rules.  The catalog lives where it belongs —
next to the unit type constants.

### 0.C — `BehaviorRegistry` Extension: `GetRegisteredNames()`

The existing `FDP.Toolkit.Behavior.BehaviorRegistry` class gains a single new read-only
property / method:

```csharp
public IReadOnlyList<string> GetRegisteredNames()
    => _nameToId.Keys.ToList();
```

Used by `EditorMissionService` to cross-check catalog names against actually-registered
behaviors (preventing UI offering a behavior the local engine cannot execute).

---

## Phase 1: Migrate Core ExCon Panels to `Hrot.UI.Common`

**Goal:** Move the three panels currently hardwired to `IExConLogic` out of `Hrot.ExCon` into
the shared library.  No new behavior — only the dependency wiring changes.

### 1.A — Migrate `SpawnerPanel`

Move `Hrot.ExCon/Panels/SpawnerPanel.cs` to `Hrot.UI.Common/Panels/SpawnerPanel.cs`.
Replace the `IExConLogic` parameter in `DrawContent` and button handlers with `ISpawnController`.

The panel's internal state (selected TKB type, affiliation picker, initial properties JSON) is
preserved.  The translation from "Activate Placement Tool" button → `StartPlacementMode` remains
inside the panel; the panel no longer constructs DDS payloads.

### 1.B — Migrate `MissionPanel` (Dynamic Behavior Catalog)

Move to `Hrot.UI.Common/Panels/MissionPanel.cs`.
- Remove the hardcoded `_behaviorIds` constants array and the dummy `BehaviorRegistry`
  instantiation in the panel constructor.
- Replace with dynamic calls: `logic.GetAvailableBehaviors(_selectedEntityId)` where `logic`
  is the injected `IMissionEditorService`.
- The `DrawContent(IMissionEditorService service, IMapPickService pick)` signature supplies both
  services; the mission pick flow (clicking "Pick Location") delegates to `IMapPickService.PickLocationAsync()`.

### 1.C — Migrate `ConfigPanel` (`IMapConfigController`)

Move `Hrot.ExCon/Panels/ConfigPanel.cs` to `Hrot.UI.Common/Panels/ConfigPanel.cs`.
- Replace the `IExConLogic.SendConfigPatch` call with `IMapConfigController.ApplyConfig(state)`.
- The `GetCurrentConfig()` return value is bound to panel checkbox state on open.
- No JSON patch construction inside the panel.

---

## Phase 2: New Shared Panels

**Goal:** Create the panels that are entirely new — they have no ExCon predecessor to migrate.

### 2.A — `SharedOrbatPanel` with Embarkation Drag-and-Drop

New file: `Hrot.UI.Common/Panels/SharedOrbatPanel.cs`.

The panel renders the entity hierarchy using `IOrbatDataProvider.GetVisibleNodes()` which
returns flat `OrbatNodeViewModel` records.  The `Depth` field drives tree indentation via
`ImGui.Indent` / `ImGui.Unindent`.

**Embarkation drag-and-drop:**
- Every tree node is simultaneously a **drag source** (payload type `"ORBAT_ENTITY"`, carries
  the integer `EntityId`) and a **drop target**.
- When a valid payload is dropped onto a different node, the panel calls
  `IOrbatController.RequestEmbark(passengerId, vehicleId)`.
- The panel **never performs capacity validation** — it blindly fires the intent and lets the
  execution system (`EditorCargoSystem`) enforce the domain invariant.
- A "Disembark" context menu item on embarked entities calls `RequestDisembark(entityId)`.

### 2.B — `PreviewPanel` (`IPreviewController`)

New file: `Hrot.UI.Common/Panels/PreviewPanel.cs`.

Simple two-state toggle panel:
- When `IPreviewController.IsInPreviewMode == false`: renders a "▶ Enter Preview" button that
  calls `EnterPreviewMode()`.
- When true: renders a "■ Stop Preview" button that calls `ExitPreviewMode()`.
- A status label shows "EDIT" or "PREVIEW" in contrasting colours.

### 2.C — `ZoneEditorPanel` (`IZoneAuthoringController`)

New file: `Hrot.UI.Common/Panels/ZoneEditorPanel.cs`.

The panel contains:
- An `ImGui.InputText` for the **zone name** (default `"urban_combat_zone"`).
- An `ImGui.InputText` for the **road network JSON path** + an "Apply" button that calls
  `SetRoadNetworkPath(zoneName, path)`.
- An `ImGui.SliderFloat` for **obstacle radius** (1–50 m).
- A "Place LOS Obstacle" button that calls `StartObstaclePlacementMode(zoneName, radius)`.

### 2.D — `SharedContextMenuPopulator` + `IEntityActionController`

New file: `Hrot.UI.Common/Menus/SharedContextMenuPopulator.cs`.

A **static** class that receives primitive entity state (entityId, tkbType, bool flags) and an
`IContextMenuBuilder`, builds the menu items without any infrastructure knowledge:

```
PopulateEntityMenu(long entityId, long tkbType, bool hasEditablePolyline,
                   bool hasRoutePlan, IContextMenuBuilder builder,
                   IEntityActionController actions)
    → AddItem("Center on Entity", ...) always
    → AddItem("Rename...",        ...) if entityId != 0
    → AddItem("Edit Shape",       ...) if hasEditablePolyline
    → AddItem("Edit Route",       ...) if hasRoutePlan
    → AddSeparator()
    → AddItem("Delete",           ...)

PopulateEmptyMapMenu(IContextMenuBuilder builder, IEntityActionController actions)
    → AddItem("Measurement Tool", () => actions.ActivateMeasureTool())
```

**Why not a class:**  A static method avoids capturing state in a service object for what is 
purely menu-structure logic.  The host application instantiates and passes action callbacks.

---

## Phase 3: New Domain Events

**Goal:** Define the pure FDP domain commands that enable the new Editor authoring features.
These events travel on `FdpEventBus` and are consumed by execution systems in the kernel.

### 3.A — Embarkation Commands

```
// FDP/Toolkits/FDP.Toolkit.Behavior/Events/
EmbarkEntityCommand   { Entity Passenger; Entity Vehicle; }
DisembarkEntityCommand { Entity Passenger; }
```

Both are **unmanaged structs** tagged with `[EventId(...)]` assigned from the Behavior block.

### 3.B — `SeedTargetCommand`

```
// FDP/Toolkits/FDP.Toolkit.Perception/Events/
SeedTargetCommand { Entity Perceiver; Entity Target; float ScoreBoost; }
```

Unmanaged struct, EventId in the Perception block.

### 3.C — Zone Authoring Commands

```
// Hrot.Map.Common/Events/
SpawnZoneObstacleCommand  { string ZoneName; Vector2 Position; float Radius; }
UpdateZoneConfigCommand   { string ZoneName; string? RoadNetworkPath; }
```

These are **managed** events (contain a `string`) and live in `Hrot.Map.Common` at the
application layer (not in FDP toolkits).

### 3.D — Event Registration in Component Registries

The FDP engine requires every event type to be registered via `world.RegisterEvent<T>()` at
startup before any `Bus.Publish` call; attempting to publish an unregistered event throws an
unmanaged memory exception.  The three new unmanaged events must be added to the appropriate
centralised registries:

| Event | Registry | File |
|-------|----------|------|
| `EmbarkEntityCommand` | `CognitiveComponentRegistry.RegisterAll` | `Hrot.Common/Registries/CognitiveComponentRegistry.cs` |
| `DisembarkEntityCommand` | `CognitiveComponentRegistry.RegisterAll` | same |
| `SeedTargetCommand` | `CombatComponentRegistry.RegisterAll` | `Hrot.Common/Registries/CombatComponentRegistry.cs` |
| `SpawnZoneObstacleCommand` | `HrotSharedComponentRegistry.RegisterAll` | `Hrot.Map.Common/HrotSharedComponentRegistry.cs` |
| `UpdateZoneConfigCommand` | `HrotSharedComponentRegistry.RegisterAll` | same |

**Note for managed events:** `SpawnZoneObstacleCommand` and `UpdateZoneConfigCommand` are
managed (contain `string` fields) and use `Bus.PublishManaged` / `Bus.ConsumeManagedSequence`.
They still require `world.RegisterManagedEvent<T>()` (or the equivalent managed-event
registration API in the FDP kernel) to be called at startup.

---

## Phase 4: Hrot.Editor Adapters & ECS Systems

**Goal:** Implement every adapter and execution system that the `Hrot.Editor` needs.

### 4.A — `EditorSpawnAdapter` (`ISpawnController`)

`Hrot.Editor/Adapters/EditorSpawnAdapter.cs`

- `StartPlacementMode` → instantiate `CreationTool(onEntityCreated: cmd => _bus.PublishManaged(cmd), tkbType, initialPropertiesJson, autoPopOnPlace: true)` and push onto `MapCanvas`.
- `StartAreaAuthoringMode` → push `AreaAuthoringTool` (from `Hrot.ScenarioEditor.Tools`).
- `StartRouteAuthoringMode` → push `RouteAuthoringTool` (from `Hrot.ScenarioEditor.Tools`).

### 4.B — `EditorMissionService` (`IMissionEditorService`)

`Hrot.Editor/Adapters/EditorMissionService.cs`

- Injected with `FdpEventBus`, `EntityRepository`, `BehaviorRegistry`.
- `GetAvailableBehaviors(entityId)`:
  1. Read `TkbIdentity.TkbType` from ECS.
  2. Call `BehaviorCatalog.GetValidBehaviors(tkbType)`.
  3. Filter to names actually registered in `BehaviorRegistry`.
  4. Return filtered list.
- `GetMissionSnapshot` → query `ActiveMissionPlan` managed component from ECS.
- `CommitMissionAsync` → publish `MissionControlIntent` to event bus; cache `TaskCompletionSource<MissionCommitResult>` keyed by `RequestId`.
- `PollAcks()` → consume `MissionControlAckEvent` from bus each frame; resolve pending tasks.

### 4.C — `EditorOrbatAdapter` (`IOrbatDataProvider` + `IOrbatController`)

`Hrot.Editor/Adapters/EditorOrbatAdapter.cs`

**Read model (`IOrbatDataProvider`):**
- Query entities with `VisHierarchyNode` + `EntityInfo` (Hrot.IG.Components).
- Build parent-child map using `EntityInfo.CommanderId`.
- Walk tree recursively; apply `filterText` and `expandedNodes`; map to `OrbatNodeViewModel`.

**Write model (`IOrbatController`):**
- `SelectEntity` → update `IEditorLogic.SelectedEntity`.
- `CreateUnit` → delegate to `EditorSpawnAdapter.StartPlacementMode(tkbType, null)`.
- `RequestEmbark` → resolve ECS entities by index, publish `EmbarkEntityCommand` to bus.
- `RequestDisembark` → resolve ECS entity, publish `DisembarkEntityCommand` to bus.

### 4.D — `EditorMapPickAdapter` (`IMapPickService`)

`Hrot.Editor/Adapters/EditorMapPickAdapter.cs`

- **`PickLocationAsync`** → create `LocationPickerTool`, wire `OnLocationPicked` event to
  `TaskCompletionSource<GeoPoint>` (converting Cartesian canvas coords to WGS-84 via
  `IGeographicTransform`), push onto `MapCanvas`.  Wire `OnCancelled` and
  `CancellationToken.Register` for cleanup.
- **`PickEntityAsync`** → same pattern with `EntityPickerTool`; resolves `Task<int>` with
  the entity's index.
- **`PickAreaEntitiesAsync`** → push `ModalBoxSelectionTool` (locally defined wrapper that
  waits for first mouse-down, delegates to `BoxSelectionTool` for drag, resolves
  `Task<IReadOnlyList<int>>` on mouse-up).

### 4.E — `EditorZoneAdapter` (`IZoneAuthoringController`)

`Hrot.Editor/Adapters/EditorZoneAdapter.cs`

- `SetRoadNetworkPath` → publish `UpdateZoneConfigCommand { ZoneName, RoadNetworkPath }`.
- `StartObstaclePlacementMode` → push `ObstaclePlacementTool` onto `MapCanvas`; on click,
  publish `SpawnZoneObstacleCommand { ZoneName, Position = canvasClickPos, Radius }`.

### 4.F — `EditorEntityContextMenuHandler` (`IEntityContextMenuHandler`)

`Hrot.Editor/UI/EditorEntityContextMenuHandler.cs`

- Implements **both** `IEntityContextMenuHandler` and `IEntityActionController`.  The class
  satisfies `IEntityActionController` directly (no nested helper required) because all its
  action methods already hold the required `_bus`, `_logic` references:
  ```csharp
  public void CenterOnEntity(long entityId) => _logic.CenterOnEntity(entityId);
  public void DeleteEntity(long entityId)   => _bus.PublishManaged(new DestroyEntityCommand { NetworkId = entityId });
  public void EditOverlay(long entityId)   => { _logic.SelectEntity(entityId); _logic.ActivateTool(EditorTool.Edit); }
  public void EditRoute(long entityId)     => { _logic.SelectEntity(entityId); _logic.ActivateTool(EditorTool.Route); }
  public void Rename(long entityId)        => _logic.OpenRenameDialog(entityId);
  public void ActivateMeasureTool()        => _logic.ActivateTool(EditorTool.Measure);
  ```
- `PopulateMenu(Entity entity, IContextMenuBuilder builder)` inspects ECS components to
  determine flag arguments, then calls:
  ```csharp
  SharedContextMenuPopulator.PopulateEntityMenu(
      entityId: networkId, tkbType, hasEditablePolyline, hasRoutePlan, builder, actions: this);
  ```
- Multi-select target seeding:  
  If the right-clicked entity has `TargetMemory`, and `ISelectionState.SelectedEntities` has ≥1
  valid perceivers, the menu item label pluralises ("Mark Target for N Units...").  On
  activation, awaits `IMapPickService.PickEntityAsync()` and fans out `SeedTargetCommand`
  for each selected perceiver.
- Handles **1-to-N mark via area pick**: a second menu item "Mark Area Targets..." awaits
  `IMapPickService.PickAreaEntitiesAsync()` and fans out `SeedTargetCommand` for each
  (perceiver × target) combination.
- Rename flow: calls `IEditorLogic.OpenRenameDialog(networkId)` which sets a UI state flag;
  when the modal is confirmed, produces a `CommitPropertyEdit` call.

### 4.G — `EditorPreviewAdapter` (`IPreviewController`)

`Hrot.Editor/Adapters/EditorPreviewAdapter.cs`

- `IsInPreviewMode` → derived from the `ScenarioEditorModule`'s current cluster state.
- `EnterPreviewMode` → call `PreviewClusterOpHandler.LoadingPreviewCommit()` → snapshot ECS.
- `ExitPreviewMode` → call `PreviewClusterOpHandler.UnloadingPreviewCommit()` → rewind ECS.

No DDS messages; no `ClusterOpRequest`.  The Editor talks directly to the local
`PreviewClusterOpHandler` at memory-bus speeds.

### 4.H — `EditorMapConfigAdapter` (`IMapConfigController`)

`Hrot.Editor/Adapters/EditorMapConfigAdapter.cs`

- `GetCurrentConfig` → read the local `MapUserConfig` singleton from ECS.
- `ApplyConfig` → directly overwrite the `MapUserConfig` singleton (no JSON, no DDS).

### 4.I — `EditorCargoSystem`

`Hrot.Editor/Systems/EditorCargoSystem.cs` — `[UpdateInPhase(SystemPhase.Input)]`

Consumes `EmbarkEntityCommand` from bus each tick:
1. Guard: both entities alive, vehicle has `PassengerBuffer`.
2. Capacity check: `if (buffer.Count >= PassengerBuffer.Capacity) continue;`
3. Add passenger to `buffer.Passengers[buffer.Count++]`.
4. Strip `ActorCapabilities.CanMove | CanShoot` from passenger's `ActorCapabilityState`.
5. Add `IsEmbarkedTag { VehicleEntity = cmd.Vehicle }` to passenger.

Consumes `DisembarkEntityCommand`:
1. Guard: entity alive, has `IsEmbarkedTag`.
2. Read vehicle from `IsEmbarkedTag.VehicleEntity`.
3. Remove passenger from vehicle's `PassengerBuffer`.
4. Restore `ActorCapabilities.CanMove | CanShoot`.
5. Remove `IsEmbarkedTag`.

### 4.J — `EditorPerceptionSetupSystem`

`Hrot.Editor/Systems/EditorPerceptionSetupSystem.cs` — `[UpdateInPhase(SystemPhase.Input)]`

Consumes `SeedTargetCommand` from bus each tick:
1. Guard: both entities alive; perceiver has `TargetMemory`; target has `SimTransform`.
2. `GetComponentRW<TargetMemory>(perceiver)`.
3. `GetComponentRO<SimTransform>(target)`.
4. `TargetMemory.AddOrUpdateTarget(ref mem, entityId: (long)cmd.Target.PackedValue, posX, posY, cmd.ScoreBoost, World.Tick)`.

Thread-safety rationale: mutation is confined to the `Input` phase, which runs single-threaded
before the parallel simulation phases.

### 4.K — `EditorZoneAuthoringSystem`

`Hrot.Editor/Systems/EditorZoneAuthoringSystem.cs` — `[UpdateInPhase(SystemPhase.Input)]`

Consumes `SpawnZoneObstacleCommand`:
1. Create ECS entity.
2. Attach `SimTransform { Position = new Vector3(cmd.Position.X, cmd.Position.Y, 0f) }`.
3. Attach `PhysicsCollider { Radius = cmd.Radius, CollisionLayer = PhysicsConstants.EntityCollisionLayer }`.
4. Attach managed `ZoneMembership { ZoneName = cmd.ZoneName }` (so `ZoneManagerService.GetActiveZones()` can collect on save).

Consumes `UpdateZoneConfigCommand`:
1. If `repo.HasSingleton<ZoneEnvironmentData>()`, dispose existing `RoadNetworkBlob`.
2. `RoadNetworkLoader.LoadFromJson(cmd.RoadNetworkPath)` → `repo.SetSingleton(new ZoneEnvironmentData { RoadNetwork = blob })`.
3. Store updated path in `ZoneManagerService` internal state for `GetActiveZones()`.

### 4.L — `PerceptionMapLayer` (`IMapLayer`)

`Hrot.Editor/Rendering/PerceptionMapLayer.cs`

Implements `IMapLayer.Draw(RenderContext ctx)`.  Injected with `ISimulationView`.

Constructor builds query: `.With<TargetMemory>().With<SimTransform>()`.

In `Draw`:
- Iterate query results (zero heap allocation — ECS iteration).
- For each perceiver: read `SimTransform.Position` and iterate `TargetMemory.EntityIds`.
- For each live target entity: read its `SimTransform`.
- Draw `Raylib.DrawLineEx(perceiverPos2D, targetPos2D, 1.5f, new Color(255, 60, 60, 160))`.

No system, no ECS mutation — purely a read-only rendering component.

---

## Phase 5: Hrot.Editor Composition Root Wiring

**Goal:** Connect every panel to its adapter and register all layers/systems in the Editor's
application startup.

### 5.B — `ScenarioFileService` Zone Save Integration

`Hrot.ScenarioEditor/Services/ScenarioFileService.cs`

The save pipeline for zone data requires two changes to `ScenarioFileService`:

1. **Constructor injection:** Add `IZoneManagerService zoneManagerService` parameter.
   The service is already available in the `ScenarioEditorModule` composition root which
   instantiates `ScenarioFileService`; extend that constructor call accordingly.
2. **`SaveScenario` update:** After the FDP serialiser produces the entity DOM, call
   `_zoneManagerService.GetActiveZones()` to retrieve the `Dictionary<string, ZoneDefinitionDto>`
   for the currently loaded zones.  Inject this dictionary into
   `HrotScenarioEnvelopeDto.Zones` before serialising the envelope to disk.

```csharp
public void SaveScenario(string filePath)
{
    var fdpDom    = _serializer.Serialize(_repo, new ScenarioHeader("Hrot.Scenario"));
    var activeZones = _zoneManagerService.GetActiveZones();   // NEW
    var envelope  = new HrotScenarioEnvelopeDto
    {
        Header   = new ScenarioHeaderDto { SubsystemType = "Hrot.Scenario", SchemaVersion = 1 },
        Zones    = activeZones.Count > 0 ? activeZones : null,  // NEW — omit when empty
        Entities = fdpDom["Entities"]?.AsObject()
    };
    File.WriteAllText(filePath, JsonSerializer.Serialize(envelope, HrotSerializerOptions.Default));
}
```

**Dependency note:** `ScenarioFileService` must add a `<ProjectReference>` to `Hrot.Map.Common`
if not already present, to resolve `IZoneManagerService` and the DTO types.

All wiring is performed in `EditorApplication.cs` (or its initialisation method):

```
1. Instantiate adapters (SpawnAdapter, MissionService, OrbatAdapter, MapPickAdapter,
   ZoneAdapter, ContextMenuHandler, PreviewAdapter, MapConfigAdapter)

2. Register ECS systems with ModuleHostKernel:
   EditorCargoSystem, EditorPerceptionSetupSystem, EditorZoneAuthoringSystem

3. Instantiate shared panels, passing adapters:
   new SpawnerPanel(),   new MissionPanel(),
   new SharedOrbatPanel(), new ConfigPanel(),
   new PreviewPanel(),   new ZoneEditorPanel()

4. Instantiate FDP panels directly (already decoupled):
   new FdpEntityInspectorPanel()   // + FdpRepositoryAdapter wrapping repo
   new FdpEventBrowserPanel()      // + fed the Editor's FdpEventBus each frame

5. Register EditorEntityContextMenuHandler with FdpEntityInspectorPanel

6. Instantiate MapCanvas (provides MapCamera for pan/zoom)
   Push StandardInteractionTool (supplies selection + drag-drop)

7. Register PerceptionMapLayer with MapCanvas render chain

8. Register all panels/windows with WindowManager
```

**Panels provided by `Hrot.UI.Common` vs FDP:** The entity inspector and event browser are
consumed directly from `FDP.Toolkit.ImGui` — they require zero changes; just instantiation and
a `FdpRepositoryAdapter`.

**Map interaction and entity rendering:** Pan, zoom, entity selection, rectangle multi-select,
and entity drag-and-drop are handled entirely by `MapCanvas` + `StandardInteractionTool` from
`FDP.Toolkit.Vis2D` — no panel code needed.

**Entity symbol rendering with ID labels:** The existing `EntityRenderLayer` from
`FDP.Toolkit.Vis2D` renders entity symbols on the map.  In the Editor, labels should show the
entity's **NetworkId / ECS index** (not name) so designers can correlate map symbols with the
entity inspector readout.  If `EntityRenderLayer` supports a `LabelProvider` delegate or a
configuration flag, pass one that formats the entity index.  This is a
configuration-at-composition-root decision, not a new system or panel.

**ORBAT context menu sharing:** The `SharedOrbatPanel` can reuse exactly the same
`EditorEntityContextMenuHandler` that is already registered with `FdpEntityInspectorPanel`.
On right-click of an ORBAT node, the panel opens an ImGui popup and calls
`_contextMenuHandler.PopulateMenu(entity, builder)` — giving operators the identical menu
options (rename, delete, edit shape/route, seed target) regardless of which panel they are in.

---

## Phase 6: ExCon Adapters — DRY Network Side

**Goal:** Update `Hrot.ExCon` to consume the shared panels via network-aware adapters so that
ExCon also benefits from the DRY refactor.

### 6.A — `ExConOrbatAdapter` (`IOrbatDataProvider` + `IOrbatController`)

`Hrot.ExCon/Adapters/ExConOrbatAdapter.cs`

**Read model:** Query `IDerRepo`, use `Hrot.NED.Descriptors.EntityInfo.CommanderId` to build
the parent-child tree.  Map to `OrbatNodeViewModel`.

**Write model:** `SelectEntity` → `IExConLogic.SendSetSelection(entityId)`.
`CreateUnit` → `IExConLogic.StartPlacementMode(tkbType, null)`.
`RequestEmbark` → publish a `MapCommandRequest` of type `CMD_EMBARK` over DDS
(or `IExConLogic.SendEmbarkRequest` — see §6 detail).

### 6.B — `ExConLogic` — Implement `ISpawnController`

Declare `ExConLogic : ISpawnController`.  The existing `StartPlacementMode`,
`StartAreaAuthoringMode`, and `StartRouteAuthoringMode` implementations already do the correct
DDS-layer calls.  No new logic — only interface declaration added.

### 6.C — `MissionEditorService` NED Adapter — `GetAvailableBehaviors`

Update `Hrot.ExCon/Services/MissionEditorService.cs`:
- Add `IDerRepo _repo` injection.
- `GetAvailableBehaviors(entityId)`:
  1. Get entity from `IDerRepo`.
  2. Check `TkbType` from NED descriptor.
  3. Return `BehaviorCatalog.GetValidBehaviors(tkbType)`.
  (No fallback to hardcoded list; returns empty on lookup failure.)

### 6.D — ExCon Composition Root: Wire Shared Panels

In the ExCon composition root (where `ExConMock.cs` builds the window manager):
- Replace the existing `OrbatPanel` + `MissionPanel` + `SpawnerPanel` instantiations with the
  shared versions from `Hrot.UI.Common`, passing the `ExConOrbatAdapter`, the updated
  `MissionEditorService`, and the `ExConLogic` (as `ISpawnController`).

### 6.E — ExCon `ContextMenuLogic` Refactor (DRY — `SharedContextMenuPopulator`)

`Hrot.ExCon/Logic/ContextMenuLogic.cs`  (existing file — update)

Currently `ContextMenuLogic` hardcodes menu items as strings and serialises them to a
`ContextActionsUpdate` DDS message.  After this refactor the menu **definition** comes from the
shared populator; only the DDS serialisation/invocation machinery stays in ExCon.

**Two new helper classes in `Hrot.ExCon/Adapters/`:**

- **`JsonContextMenuBuilder : IContextMenuBuilder`** — collects `(label, callbackId)` pairs
  instead of rendering ImGui widgets.  On `AddItem(label, callback)`: generates a unique
  integer `id`, caches `(id → callback)` in a dictionary, appends a `ContextMenuItem(id, label)`
  to the output list.  `AddSeparator()` appends a separator sentinel.  Result is available via
  `IReadOnlyList<ContextMenuItem> Build()`.

- **`ExConEntityActionAdapter : IEntityActionController`** — maps each port method to the
  corresponding `IExConLogic` / DDS call:
  ```csharp
  public void CenterOnEntity(long id) => _logic.SendCenterOnEntity(id);   // existing DDS call
  public void DeleteEntity(long id)   => _logic.SendDeleteEntity(id);
  public void EditOverlay(long id)    => _logic.ActivateTool(MapToolType.EditOverlay, id);
  public void EditRoute(long id)      => _logic.ActivateTool(MapToolType.EditRoute, id);
  public void Rename(long id)         => _logic.OpenRenameDialog(id);
  public void ActivateMeasureTool()   => _logic.ActivateTool(MapToolType.Measure);
  ```

**Updated `ContextMenuLogic.BuildMenu(IDerEntity entity)`:**
```csharp
var builder = new JsonContextMenuBuilder();
var actions = new ExConEntityActionAdapter(_logic);

bool hasPolyline = entity.HasDescriptor<MapVisualOverlay>() || entity.HasDescriptor<MapRoute>();
bool hasRoute    = entity.HasDescriptor<MapRoute>();

SharedContextMenuPopulator.PopulateEntityMenu(
    entity.EntityId, entity.TkbType, hasPolyline, hasRoute, builder, actions);

var items = builder.Build();
// Serialize items → ContextActionsUpdate DDS message (existing serialisation code)
_logic.SendContextActionsUpdate(items, builder.GetCallbackRegistry());
```

When the operator selects a menu item, the IG returns a `ContextActionInvoked` DDS message
containing the integer `id`.  `ContextMenuLogic` looks up the callback in
`builder.GetCallbackRegistry()` and invokes it.  This is identical to current behaviour but
the menu definition is now driven entirely by `SharedContextMenuPopulator` — DRY enforced.

---

## Phase 7: Headless Integration Tests

**Goal:** CI-friendly, deterministic tests that prove the new authoring features work without
any GPU context, Raylib window, or DDS participants.

All tests live in a new class `EditorAuthoringIntegrationTests` in
`Hrot.ClusterRunner.Integration.Tests`.  They reuse the existing `EditorHarness`.

### 7.A — Embarkation & Cargo Tests

**Test 1: Valid embarkation populates buffer and strips capabilities**
- Spawn APC + Soldier entities in harness repo.
- `EditorOrbatAdapter.RequestEmbark(soldier.Index, apc.Index)`, pump 1 frame.
- Assert `PassengerBuffer.Count == 1`, `buffer.Passengers[0] == soldier`.
- Assert `IsEmbarkedTag` present on soldier; `ActorCapabilityState` has `CanMove` cleared.

**Test 2: Capacity limit enforced (no ECS mutation on overflow)**
- Fill APC to `PassengerBuffer.Capacity`.
- `RequestEmbark` for one more passenger, pump 1 frame.
- Assert `PassengerBuffer.Count == Capacity`; new passenger has no `IsEmbarkedTag`.

**Test 3: Disembark restores capabilities**
- Embark soldier (as Test 1), then `RequestDisembark(soldier.Index)`, pump 1 frame.
- Assert `IsEmbarkedTag` removed; `CanMove` + `CanShoot` restored.

### 7.B — Target Memory Seeding Tests

**Test 1: Single perceiver seeded via `SeedTargetCommand`**
- Spawn insurgent with `TargetMemory` + APC with `SimTransform { Position = (10, 20, 0) }`.
- Publish `SeedTargetCommand { Perceiver = insurgent, Target = apc, ScoreBoost = 100f }`, pump 1 frame.
- Assert `TargetMemory.Count == 1`; `EntityIds[0] == (long)apc.PackedValue`; score ≥ 100.

**Test 2: N-to-1 fan-out (multiple perceivers, one target)**
- Spawn 3 insurgents with `TargetMemory` + 1 APC.
- Publish 3 `SeedTargetCommand` events (one per perceiver), pump 1 frame.
- Assert each insurgent's `TargetMemory.Count == 1`.

**Test 3: 1-to-N fan-out (one perceiver, multiple targets)**
- Spawn 1 insurgent with `TargetMemory` + 3 APC entities.
- Publish 3 `SeedTargetCommand` events (one per target-APC), pump 1 frame.
- Assert insurgent's `TargetMemory.Count == 3`.

### 7.C — Zone Obstacle Authoring & Save Pipeline

**Test 1: Obstacle placement spawns correct ECS entity**
- Publish `SpawnZoneObstacleCommand { ZoneName = "test", Position = (50, 25), Radius = 10 }`, pump 1 frame.
- Assert entity count `WITH<PhysicsCollider> == 1`; assert collider radius == 10; position == (50, 25, 0).

**Test 2: Road network config update injects `ZoneEnvironmentData` singleton**
- Publish `UpdateZoneConfigCommand { ZoneName = "test", RoadNetworkPath = "Assets/sample_road.json" }`, pump 1 frame.
- Assert `repo.HasSingleton<ZoneEnvironmentData>()` is true.
- Assert `ZoneEnvironmentData.RoadNetwork.Nodes.IsCreated`.

**Test 3: Full save pipeline bundles zone into `HrotScenarioEnvelopeDto`**
- Place obstacle + set road network (Tests 1+2).
- `harness.Editor.SaveScenario(tempFile)`.
- Deserialise the file using `HrotJsonOptions`.
- Assert `envelope.Zones["test"].RoadNetworkPath == "Assets/sample_road.json"`.
- Assert `envelope.Zones["test"].Obstacles.Count == 1`; `Obstacles[0].X == 50`.

### 7.D — Behavior Catalog Filtering

**Test 1: Insurgent TKB resolves only Insurgent-valid behaviors**
- Spawn entity with `TkbIdentity { TkbType = TkbEntityTypes.Insurgent }`.
- `EditorMissionService.GetAvailableBehaviors(entity.Index)`.
- Assert contains `"Ambush"`; does not contain `"WanderCivil"`.

**Test 2: Civilian TKB resolves only civilian behaviors**
- Spawn entity with `TkbIdentity { TkbType = TkbEntityTypes.CivilianPedestrian }`.
- Assert contains `"WanderCivil"`; does not contain `"Ambush"`.

**Test 3: Missing engine registration is filtered out**
- Register only `"Ambush"` in `BehaviorRegistry`; entity is Insurgent.
- Assert returns `["Ambush"]` (other catalog entries absent from registry are excluded).

---

## Cross-Cutting Concerns

### Data Flow for Async Map Picks (TAP + CQRS)

All three `IMapPickService` methods use the same pattern:

```
UI Thread:
  1. Create TaskCompletionSource<T>.
  2. Instantiate FDP Vis2D tool (LocationPickerTool / EntityPickerTool / ModalBoxSelectionTool).
  3. Wire tool's OnXxxPicked/OnCancelled events to TCS.TrySetResult / TCS.TrySetCanceled.
  4. Register CancellationToken.Register for cleanup.
  5. MapCanvas.PushTool(tool).
  6. Return TCS.Task.

Kernel Tick / Next Input Phase:
  7. Tool processes input, fires event.
  8. TCS resolves.
  9. Awaiting UI code continues; publishes domain command.
  10. Domain command consumed by execution system in same/next tick.
```

This pattern guarantees: zero network latency (no DDS roundtrip), zero heap allocation on the
tool hot path, and clean cancellation if the operator presses Escape.

### Naming Conventions

- Adapter classes in `Hrot.Editor/Adapters/` are named `Editor{Capability}Adapter.cs`.
- Editor ECS systems are in `Hrot.Editor/Systems/` and are named `Editor{Domain}System.cs`.
- All shared panels remain stateless regarding which host is running them — they hold only
  UI presentation state (text input buffers, filter strings, expanded node sets).

### FDP Toolkit Panels Used Directly (No Wrapping Needed)

| Panel | Source | How registered in Editor |
|-------|--------|--------------------------|
| Entity Inspector | `FDP.Toolkit.ImGui.Panels.FdpEntityInspectorPanel` | Wrapped in `FdpEntityInspectorWindow`, supplied with `FdpRepositoryAdapter(repo)` |
| Event Browser    | `FDP.Toolkit.ImGui.Panels.FdpEventBrowserPanel`    | Wrapped in `FdpEventBrowserWindow`, fed `FdpEventBus` each frame |

### Dependency Order

- Phase 0 (contracts) must complete before Phases 1, 2, 4, 6.
- Phase 3 (domain events) must complete before Phase 4 (systems consume them).
- Phase 4 (adapters + systems) must complete before Phase 5 (composition root).
- Phase 6 (ExCon adapters) is independent of Phase 4 but depends on Phase 0.
- Phase 7 (tests) depends on Phase 4 and reuses `EditorHarness` from Phase 5.

---

## Summary of Deliverables

| Phase | Key Deliverables |
|-------|-----------------|
| 0 | `Hrot.UI.Common` project; all 9 Facade interfaces; `OrbatNodeViewModel`/`MapLayerState`/`MissionCommitResult` DTOs; `BehaviorCatalog` in `Hrot.Map.Definitions`; `BehaviorRegistry.GetRegisteredNames()` |
| 1 | Migrated `SpawnerPanel`, `MissionPanel` (dynamic behavior), `ConfigPanel` — all wired to Ports |
| 2 | `SharedOrbatPanel` (embarkation D&D), `PreviewPanel`, `ZoneEditorPanel`, `SharedContextMenuPopulator` |
| 3 | `EmbarkEntityCommand`, `DisembarkEntityCommand`, `SeedTargetCommand`, `SpawnZoneObstacleCommand`, `UpdateZoneConfigCommand` |
| 4 | 8 Editor adapter classes; `EditorCargoSystem`, `EditorPerceptionSetupSystem`, `EditorZoneAuthoringSystem`, `PerceptionMapLayer` |
| 5 | Full `Hrot.Editor` composition root wiring (all panels, adapters, map canvas, systems, layers) |
| 6 | `ExConOrbatAdapter`; `ExConLogic: ISpawnController`; updated `MissionEditorService`; ExCon comp-root wired to shared panels |
| 7 | `EditorAuthoringIntegrationTests` — 10 headless tests covering embarkation, target seeding, zone authoring, behavior filtering |
