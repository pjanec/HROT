# TASK-DETAIL.md — Shared UI Library & Hrot.Editor Feature Completion (`edit-1`)

**Design Reference:** See [DESIGN.md](./DESIGN.md) for architecture overview, phase goals, and
rationale.

---

## Phase 0: `Hrot.UI.Common` — Shared Library Foundation

**Design Reference:** [DESIGN.md §Phase 0](./DESIGN.md#phase-0-hrotuicommon--shared-library-foundation)

---

### EDIT1-L001 — Create `Hrot.UI.Common` Project & All Facade Interfaces

**Design Reference:** DESIGN.md §0.A

**Context:**
All shared ImGui panels need a home project with zero coupling to CycloneDDS, DerRepo, or
IExConLogic.  Without this project the panels cannot be shared between Hrot.Editor and ExCon.

**Scope:**

- Create `Hrot.UI.Common/Hrot.UI.Common.csproj` — class library targeting the same framework
  version as the rest of the solution.
- Add project references: `FDP.Toolkit.ImGui`, `Hrot.NED` (DTOs/enums only), `Hrot.Map.Definitions`.
- Create **nine** Port interfaces in `Hrot.UI.Common/Facades/`:
  - `ISpawnController.cs`
  - `IMissionEditorService.cs`
  - `IOrbatDataProvider.cs`
  - `IOrbatController.cs`  (includes `RequestEmbark` + `RequestDisembark`)
  - `IMapConfigController.cs`
  - `IPreviewController.cs`
  - `IZoneAuthoringController.cs`
  - `IMapPickService.cs`   (three overloads: location, entity, area)
  - `IEntityActionController.cs`
- Create shared DTOs in `Hrot.UI.Common/Models/`:
  - `OrbatNodeViewModel.cs` (sealed record)
  - `MapLayerState.cs` (record)
  - `MissionCommitResult.cs` (record)
- All interfaces use only FDP-toolkit types (`GeoPoint`, `MissionPlan`, `eMissionCommandType`,
  `Guid`, `Task<T>`, `CancellationToken`) — no ECS or DDS types.

**Out of Scope:**
- Panel implementations (those are Phase 1, 2).
- Adapter implementations (Phase 4, 6).

**Files:**

| File | Change |
|------|--------|
| `Hrot.UI.Common/Hrot.UI.Common.csproj` | New project |
| `Hrot.UI.Common/Facades/ISpawnController.cs` | New |
| `Hrot.UI.Common/Facades/IMissionEditorService.cs` | New |
| `Hrot.UI.Common/Facades/IOrbatDataProvider.cs` | New |
| `Hrot.UI.Common/Facades/IOrbatController.cs` | New |
| `Hrot.UI.Common/Facades/IMapConfigController.cs` | New |
| `Hrot.UI.Common/Facades/IPreviewController.cs` | New |
| `Hrot.UI.Common/Facades/IZoneAuthoringController.cs` | New |
| `Hrot.UI.Common/Facades/IMapPickService.cs` | New |
| `Hrot.UI.Common/Facades/IEntityActionController.cs` | New |
| `Hrot.UI.Common/Models/OrbatNodeViewModel.cs` | New |
| `Hrot.UI.Common/Models/MapLayerState.cs` | New |
| `Hrot.UI.Common/Models/MissionCommitResult.cs` | New |

**Success Conditions:**

1. *(Compile)* `Hrot.UI.Common` builds cleanly with zero warnings and zero references to
   `Hrot.ExCon`, `CycloneDDS`, or `ModuleHost`.
2. *(Compile)* Any project that adds a `<ProjectReference>` to `Hrot.UI.Common` can resolve
   all nine Port interfaces and all three DTO types.
3. *(Interface check)* `IMapPickService` declares exactly three methods: `PickLocationAsync`,
   `PickEntityAsync`, `PickAreaEntitiesAsync`.
4. *(Interface check)* `IOrbatController` declares `RequestEmbark(int, int)` and
   `RequestDisembark(int)`.

---

### EDIT1-L002 — `BehaviorCatalog` in `Hrot.Map.Definitions`

**Design Reference:** DESIGN.md §0.B

**Context:**
Behavior filtering per entity type must not live in the FDP engine and must not be duplicated
between the Editor adapter and the ExCon adapter.  `Hrot.Map.Definitions` is already shared by
all subsystems.

**Scope:**

- Create `Hrot.Map.Definitions/Tkb/BehaviorCatalog.cs` — `public static class`.
- Implement `GetValidBehaviors(long tkbType) → IReadOnlyList<string>` using C# 12 switch
  expression with collection literals.
- Entries must cover at minimum:
  - `TkbEntityTypes.CivilianPedestrian` → `["WanderCivil", "PanicFlee"]`
  - `TkbEntityTypes.CivilianCar` → `["WanderCivil", "PanicFlee"]`
  - `TkbEntityTypes.MilitaryApc` → `["ConvoyEscort", "MoveToLocation", "FollowRoute"]`
  - `TkbEntityTypes.InfantrySoldier` → `["InfantryCombat", "MoveToLocation", "JoinFormation"]`
  - `TkbEntityTypes.Insurgent` → `["Ambush", "MoveToLocation"]`
  - `_` (default) → `["MoveToLocation", "FollowRoute", "JoinFormation", "Idle"]`
- Each returned list should be a static `readonly` field to avoid per-call allocation.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Map.Definitions/Tkb/BehaviorCatalog.cs` | New |

**Success Conditions:**

1. *(Unit test)* `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.Insurgent)` returns a list
   containing `"Ambush"` and not containing `"WanderCivil"`.
2. *(Unit test)* `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianPedestrian)` returns
   a list containing `"WanderCivil"` and not containing `"Ambush"`.
3. *(Unit test)* `BehaviorCatalog.GetValidBehaviors(-999)` (unknown TKB) returns the fallback
   list containing `"MoveToLocation"`.
4. *(Compile)* Zero allocation per call — lists are backed by static readonly fields.

---

### EDIT1-L003 — `BehaviorRegistry.GetRegisteredNames()`

**Design Reference:** DESIGN.md §0.C

**Context:**
`EditorMissionService` needs to cross-check the `BehaviorCatalog` result against behaviors
actually registered in the live engine to avoid presenting inaccessible options.

**Scope:**

- In `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs`:
  - Add `public IReadOnlyList<string> GetRegisteredNames()`.
  - Implementation: `_nameToId.Keys.ToList()` (or equivalent snapshot).
- Additionally add `public bool TryGetId(string name, out int id)` if not already present
  (used by `EditorMissionService` filter logic).

**Files:**

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs` | Add two methods |

**Success Conditions:**

1. *(Unit test)* Register two behaviors, call `GetRegisteredNames()`, assert both names are
   returned.
2. *(Unit test)* Empty registry → `GetRegisteredNames()` returns empty list (not null).
3. *(Compile)* `TryGetId` is accessible to `EditorMissionService` without reflection.

---

## Phase 1: Migrate Core Panels to `Hrot.UI.Common`

**Design Reference:** [DESIGN.md §Phase 1](./DESIGN.md#phase-1-migrate-core-excon-panels-to-hrotui-common)

---

### EDIT1-P001 — Migrate `SpawnerPanel` to `Hrot.UI.Common`

**Design Reference:** DESIGN.md §1.A

**Context:**
`Hrot.ExCon/Panels/SpawnerPanel.cs` currently depends on `IExConLogic`.  It must be moved to
the shared library and rewired to `ISpawnController`.

**Scope:**

- Move the file to `Hrot.UI.Common/Panels/SpawnerPanel.cs`.
- Replace all `IExConLogic` references in the panel with `ISpawnController`.
- The `DrawContent` method signature becomes `DrawContent(ISpawnController spawn)`.
- All button handlers that previously called `logic.StartPlacementMode(...)` now call
  `spawn.StartPlacementMode(...)`.  No DDS payload construction inside the panel.
- Internal state (selected TKB type, affiliation, initial properties) is unchanged.
- The old location `Hrot.ExCon/Panels/SpawnerPanel.cs` is **deleted** (not kept as a shim).
- `Hrot.ExCon` adds a project reference to `Hrot.UI.Common` and instantiates the panel via
  the `ExConLogic` adapter (which implements `ISpawnController` — see EDIT1-X002).

**Out of Scope:**
- TKB catalog changes.
- Any ExCon-specific button.

**Files:**

| File | Change |
|------|--------|
| `Hrot.UI.Common/Panels/SpawnerPanel.cs` | New (moved + refactored) |
| `Hrot.ExCon/Panels/SpawnerPanel.cs` | Deleted |
| `Hrot.ExCon/Hrot.ExCon.csproj` | Add `<ProjectReference>` to `Hrot.UI.Common` |

**Success Conditions:**

1. *(Compile)* `Hrot.UI.Common` compiles; `SpawnerPanel` references zero types from
   `Hrot.ExCon` or CycloneDDS.
2. *(Regression)* ExCon integration tests that exercise the spawner panel continue to pass.
3. *(Compile)* `Hrot.Editor` can instantiate `SpawnerPanel` and pass an `EditorSpawnAdapter`
   without import errors.

---

### EDIT1-P002 — Migrate `MissionPanel` with Dynamic Behavior Catalog

**Design Reference:** DESIGN.md §1.B

**Context:**
`MissionPanel` is hardcoded with 4 behaviour IDs and depends on `IExConLogic`.  After
migration, it must be data-driven: the behavior dropdown is populated by
`IMissionEditorService.GetAvailableBehaviors(entityId)`.

**Scope:**

- Move to `Hrot.UI.Common/Panels/MissionPanel.cs`.
- `DrawContent(IMissionEditorService service, IMapPickService pick)` replaces the old
  `DrawContent(IExConLogic logic)` signature.
- Remove the hardcoded `string[]` constants and the dummy `BehaviorRegistry` constructor call.
- In the task-behaviour combo box, call `service.GetAvailableBehaviors(_selectedEntityId)` to
  populate the dropdown items dynamically.
- The "Pick Location" button calls `pick.PickLocationAsync()` and stores the pending `Task`.
  `PollPickCompletion()` is called every frame inside `DrawContent`.
- Delete `Hrot.ExCon/Panels/MissionPanel.cs`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.UI.Common/Panels/MissionPanel.cs` | New (moved + refactored) |
| `Hrot.ExCon/Panels/MissionPanel.cs` | Deleted |

**Success Conditions:**

1. *(Compile)* `MissionPanel` has zero references to `IExConLogic`, `BehaviorRegistry`
   constructor calls, or hardcoded behavior name arrays.
2. *(Unit test)* Construct `MissionPanel`, call `DrawContent` with a mock
   `IMissionEditorService` that returns `["Ambush"]`; assert the combo box items list matches.
3. *(Regression)* ExCon mission panel integration tests pass with the shared panel +
   ExCon's `MissionEditorService` adapter.

---

### EDIT1-P003 — Migrate `ConfigPanel` to `Hrot.UI.Common`

**Design Reference:** DESIGN.md §1.C

**Context:**
`Hrot.ExCon/Panels/ConfigPanel.cs` builds JSON patches and calls `IExConLogic.SendConfigPatch`.
After migration, it calls `IMapConfigController.ApplyConfig(MapLayerState)`.

**Scope:**

- Move to `Hrot.UI.Common/Panels/ConfigPanel.cs`.
- Constructor no longer takes `IExConLogic`; `DrawContent(IMapConfigController ctrl)` is the
  new signature.
- On open: call `ctrl.GetCurrentConfig()` to initialise checkbox states.
- On "Send" (or equivalent apply): call `ctrl.ApplyConfig(new MapLayerState(...))`.
- No JSON construction inside the panel.
- Delete `Hrot.ExCon/Panels/ConfigPanel.cs`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.UI.Common/Panels/ConfigPanel.cs` | New (moved + refactored) |
| `Hrot.ExCon/Panels/ConfigPanel.cs` | Deleted |

**Success Conditions:**

1. *(Compile)* Zero JSON serialisation or DDS references inside the panel file.
2. *(Regression)* ExCon config panel continues to function when supplied an
   `ExConMapConfigAdapter` that reconstitutes the JSON patch and sends DDS.

---

## Phase 2: New Shared Panels

**Design Reference:** [DESIGN.md §Phase 2](./DESIGN.md#phase-2-new-shared-panels)

---

### EDIT1-N001 — `SharedOrbatPanel` with Embarkation Drag-and-Drop

**Design Reference:** DESIGN.md §2.A

**Context:**
The existing Editor orbat UI is simplistic.  The shared panel must render a full hierarchical
entity tree and support embarkation via ImGui drag-and-drop — the first new authoring feature.

**Scope:**

- Create `Hrot.UI.Common/Panels/SharedOrbatPanel.cs`.
- `DrawContent(IOrbatDataProvider data, IOrbatController ctrl)` calls `data.GetVisibleNodes()`
  and renders each `OrbatNodeViewModel` with depth-based indentation.
- **Filter text box** at the top; filter is passed to `GetVisibleNodes` each frame.
- **Selection**: `ImGui.Selectable` click → `ctrl.SelectEntity(node.EntityId)`.
- **Drag source**: `ImGui.BeginDragDropSource()` / `SetDragDropPayload("ORBAT_ENTITY", &id, 4)`.
- **Drop target**: `ImGui.BeginDragDropTarget()` / `AcceptDragDropPayload` → on valid drop
  call `ctrl.RequestEmbark(passengerId, vehicleId)`.  Only fires if `passengerId != vehicleId`.
- **Right-click context on embarked node**: `ctrl.RequestDisembark(entityId)`.
- Uses `unsafe` block for ImGui pointer APIs; `unsafe` must be confined to the single method.

**Files:**

| File | Change |
|------|--------|
| `Hrot.UI.Common/Panels/SharedOrbatPanel.cs` | New |

**Success Conditions:**

1. *(Compile)* `allowUnsafeBlocks` only needed inside `SharedOrbatPanel`; does not leak unsafe
   to callers.
2. *(Unit test)* Supply 2 `OrbatNodeViewModel` records (depth 0, depth 1); assert `DrawContent`
   calls `ctrl.SelectEntity` when the first node is clicked.
3. *(Unit test)* Simulate drop payload → assert `ctrl.RequestEmbark` is invoked with correct
   IDs.
4. *(Unit test)* Self-embarkation (same entity dragged onto itself) → `RequestEmbark` is NOT
   called.

---

### EDIT1-N002 — `PreviewPanel` (`IPreviewController`)

**Design Reference:** DESIGN.md §2.B

**Context:**
The editor needs a dedicated, lightweight panel to switch between Edit and Preview modes.

**Scope:**

- Create `Hrot.UI.Common/Panels/PreviewPanel.cs`.
- `DrawContent(IPreviewController ctrl)`:
  - If `!ctrl.IsInPreviewMode`: render green "▶ Enter Preview" button → call `ctrl.EnterPreviewMode()`.
  - If `ctrl.IsInPreviewMode`: render red "■ Stop Preview" button → call `ctrl.ExitPreviewMode()`.
  - Status label shows "● EDIT" (green) or "● PREVIEW" (amber) using `ImGui.TextColored`.
- No internal state beyond reading `ctrl.IsInPreviewMode` each frame.

**Files:**

| File | Change |
|------|--------|
| `Hrot.UI.Common/Panels/PreviewPanel.cs` | New |

**Success Conditions:**

1. *(Compile)* Zero references to `PreviewClusterOpHandler`, ECS, or DDS types.
2. *(Unit test)* Mock `IPreviewController.IsInPreviewMode = false`; assert `EnterPreviewMode` is
   called when the button is "clicked" (via a test double).
3. *(Unit test)* Mock `IsInPreviewMode = true`; assert `ExitPreviewMode` is called.

---

### EDIT1-N003 — `ZoneEditorPanel` (`IZoneAuthoringController`)

**Design Reference:** DESIGN.md §2.C

**Context:**
No existing panel provides static environment authoring.  The `ZoneEditorPanel` is entirely
new and enables operators to define road networks and physics obstacles without writing JSON.

**Scope:**

- Create `Hrot.UI.Common/Panels/ZoneEditorPanel.cs`.
- Internal state: `_zoneName` (default `"urban_combat_zone"`), `_roadNetworkPath`
  (default `"Assets/sample_road.json"`), `_obstacleRadius` (default `5.0f`).
- `DrawContent(IZoneAuthoringController ctrl)`:
  - `ImGui.InputText("Zone Name", ...)`.
  - `ImGui.InputText("Road Network JSON", ...)` + "Apply Road Network" button →
    `ctrl.SetRoadNetworkPath(_zoneName, _roadNetworkPath)`.
  - `ImGui.SliderFloat("Obstacle Radius (m)", ref _obstacleRadius, 1.0f, 50.0f)`.
  - "Place LOS Obstacle" button → `ctrl.StartObstaclePlacementMode(_zoneName, _obstacleRadius)`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.UI.Common/Panels/ZoneEditorPanel.cs` | New |

**Success Conditions:**

1. *(Compile)* No FDP ECS or DDS imports.
2. *(Unit test)* "Apply Road Network" action invokes `ctrl.SetRoadNetworkPath` with correct
   zone name and path values.
3. *(Unit test)* "Place LOS Obstacle" action invokes `ctrl.StartObstaclePlacementMode` with
   the current `_obstacleRadius`.

---

### EDIT1-N004 — `SharedContextMenuPopulator` + `IEntityActionController`

**Design Reference:** DESIGN.md §2.D

**Context:**
Context menu logic currently lives inside ExCon's `ContextMenuLogic`, tightly coupled to JSON
and DDS.  Extracting it as a pure static populator enables reuse in both ImGui and a future
declarative JSON proxy.

**Scope:**

- Create `Hrot.UI.Common/Menus/SharedContextMenuPopulator.cs` — `public static class`.
- `PopulateEntityMenu(long entityId, long tkbType, bool hasEditablePolyline, bool hasRoutePlan,
  IContextMenuBuilder builder, IEntityActionController actions)` — adds items:
  - "Center on Entity" always.
  - "Rename..." if `entityId != 0`.
  - "Edit Shape" if `hasEditablePolyline`.
  - "Edit Route" if `hasRoutePlan`.
  - Separator then "Delete".
- `PopulateEmptyMapMenu(IContextMenuBuilder builder, IEntityActionController actions)` — adds:
  - "Measurement Tool" → `actions.ActivateMeasureTool()`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.UI.Common/Menus/SharedContextMenuPopulator.cs` | New |

**Success Conditions:**

1. *(Unit test)* Call `PopulateEntityMenu` with `hasEditablePolyline = true, hasRoutePlan = false`;
   assert "Edit Shape" item is added, "Edit Route" is not.
2. *(Unit test)* `entityId == 0` → "Rename..." item is not added.
3. *(Unit test)* `PopulateEmptyMapMenu` → only "Measurement Tool" item added.
4. *(Compile)* No ImGui, ECS, or DDS imports inside the populator — only
   `IContextMenuBuilder` from `FDP.Toolkit.ImGui`.

---

## Phase 3: New Domain Events

**Design Reference:** [DESIGN.md §Phase 3](./DESIGN.md#phase-3-new-domain-events)

---

### EDIT1-E001 — `EmbarkEntityCommand` and `DisembarkEntityCommand`

**Design Reference:** DESIGN.md §3.A

**Scope:**

- Create `FDP/Toolkits/FDP.Toolkit.Behavior/Events/EmbarkEntityCommand.cs`:
  ```csharp
  [EventId(3201)]
  public struct EmbarkEntityCommand { public Entity Passenger; public Entity Vehicle; }
  ```
- Create `FDP/Toolkits/FDP.Toolkit.Behavior/Events/DisembarkEntityCommand.cs`:
  ```csharp
  [EventId(3202)]
  public struct DisembarkEntityCommand { public Entity Passenger; }
  ```
- Event IDs must not collide with existing IDs in `GlobalEventIds.cs` (verify before
  assigning; use the next available slot in the Behavior block).

**Files:**

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Behavior/Events/EmbarkEntityCommand.cs` | New |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Events/DisembarkEntityCommand.cs` | New |

**Success Conditions:**

1. *(Compile)* Both structs are unmanaged (no managed fields); `sizeof` evaluates at compile time.
2. *(Unit test)* `Bus.Publish(new EmbarkEntityCommand {...})` followed by
   `Bus.Consume<EmbarkEntityCommand>()` returns the same value (basic round-trip).
3. *(Compile)* EventId values are unique — verified by a compile-time attribute or by manual
   inspection of `GlobalEventIds.cs`.

---

### EDIT1-E002 — `SeedTargetCommand`

**Design Reference:** DESIGN.md §3.B

**Scope:**

- Create `FDP/Toolkits/FDP.Toolkit.Perception/Events/SeedTargetCommand.cs`:
  ```csharp
  [EventId(4101)]
  public struct SeedTargetCommand { public Entity Perceiver; public Entity Target; public float ScoreBoost; }
  ```
- ID 4101 must be in the Perception block and verified against `GlobalEventIds.cs`.

**Files:**

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Perception/Events/SeedTargetCommand.cs` | New |

**Success Conditions:**

1. *(Compile)* Unmanaged struct, EventId unique.
2. *(Unit test)* Round-trip publish/consume on a bare `FdpEventBus`.

---

### EDIT1-E003 — `SpawnZoneObstacleCommand` and `UpdateZoneConfigCommand`

**Design Reference:** DESIGN.md §3.C

**Context:**
These are application-layer events (they contain `string` fields); they live in
`Hrot.Map.Common`, not in FDP toolkits.

**Scope:**

- Create `Hrot.Map.Common/Events/SpawnZoneObstacleCommand.cs`:
  ```csharp
  public sealed class SpawnZoneObstacleCommand
  {
      public string ZoneName   { get; init; } = string.Empty;
      public Vector2 Position  { get; init; }
      public float Radius      { get; init; }
  }
  ```
- Create `Hrot.Map.Common/Events/UpdateZoneConfigCommand.cs`:
  ```csharp
  public sealed class UpdateZoneConfigCommand
  {
      public string ZoneName        { get; init; } = string.Empty;
      public string? RoadNetworkPath { get; init; }
  }
  ```
- Both are **managed** events published via `Bus.PublishManaged(...)`.
- Assign `[EventId(...)]` from the application-layer block (IDs 160–199 as per project convention
  in `GlobalComponentIds.cs` documentation; verify block boundaries).

**Files:**

| File | Change |
|------|--------|
| `Hrot.Map.Common/Events/SpawnZoneObstacleCommand.cs` | New |
| `Hrot.Map.Common/Events/UpdateZoneConfigCommand.cs` | New |

**Success Conditions:**

1. *(Compile)* Classes are sealed; fields use `init` accessors; `Hrot.Map.Common` compiles.
2. *(Unit test)* `Bus.PublishManaged(new SpawnZoneObstacleCommand {...})` →
   `Bus.ConsumeManagedSequence<SpawnZoneObstacleCommand>()` returns the command.

---

### EDIT1-E004 — Register New Events in Component Registries

**Design Reference:** DESIGN.md §3.D

**Context:**
The FDP engine strictly requires every event type to be registered at startup via
`world.RegisterEvent<T>()` (unmanaged) or `world.RegisterManagedEvent<T>()` (managed).
Publishing an unregistered event throws an unmanaged memory exception at runtime.  The five
new events from EDIT1-E001, EDIT1-E002, and EDIT1-E003 must be registered in the appropriate
extension points so that **all** subsystems that host a simulation kernel (SimHost, CGF, Editor)
registers them automatically.

**Scope:**

- In `Hrot.Common/Registries/CognitiveComponentRegistry.cs`
  (or equivalent shared registry file invoked by SimHost, CGF, and Editor):
  - `world.RegisterEvent<EmbarkEntityCommand>();`
  - `world.RegisterEvent<DisembarkEntityCommand>();`

- In `Hrot.Common/Registries/CombatComponentRegistry.cs`:
  - `world.RegisterEvent<SeedTargetCommand>();`

- In `Hrot.Map.Common/HrotSharedComponentRegistry.cs`:
  - `world.RegisterManagedEvent<SpawnZoneObstacleCommand>();`
  - `world.RegisterManagedEvent<UpdateZoneConfigCommand>();`

If any of the above registry files do not exist yet or the exact registration API name differs
(e.g. `RegisterUnmanagedEvent`, `AddEvent`), use the pattern already established for other
events in the same file — do not invent a new pattern.

**Dependencies:** Must be done after EDIT1-E001, EDIT1-E002, EDIT1-E003 (event types must
exist before they can be registered).

**Files:**

| File | Change |
|------|--------|
| `Hrot.Common/Registries/CognitiveComponentRegistry.cs` | Add 2 `RegisterEvent` calls |
| `Hrot.Common/Registries/CombatComponentRegistry.cs` | Add 1 `RegisterEvent` call |
| `Hrot.Map.Common/HrotSharedComponentRegistry.cs` | Add 2 `RegisterManagedEvent` calls |

**Success Conditions:**

1. *(Integration test)* `EditorAuthoringIntegrationTests` harness starts without throwing an
   unmanaged memory or unregistered-event exception when any of the five commands is published.
2. *(Unit test)* Publish each new event type on a freshly initialised harness with registries
   applied; assert no exception thrown.
3. *(Regression)* Existing integration tests that exercise `CognitiveComponentRegistry`,
   `CombatComponentRegistry`, and `HrotSharedComponentRegistry` continue to pass.

**Design Reference:** [DESIGN.md §Phase 4](./DESIGN.md#phase-4-hroteditor-adapters--ecs-systems)

---

### EDIT1-A001 — `EditorSpawnAdapter` (`ISpawnController`)

**Design Reference:** DESIGN.md §4.A

**Scope:**

- Create `Hrot.Editor/Adapters/EditorSpawnAdapter.cs`.
- Constructor: `EditorSpawnAdapter(MapCanvas canvas, FdpEventBus bus)`.
- Implements `ISpawnController`:
  - `StartPlacementMode(tkbType, initialPropertiesJson)` → creates `CreationTool` with
    `onEntityCreated: cmd => _bus.PublishManaged(cmd)` and pushes onto `_canvas`.
  - `StartAreaAuthoringMode(styleOverrideJson)` → pushes `AreaAuthoringTool`.
  - `StartRouteAuthoringMode()` → pushes `RouteAuthoringTool`.
- No DDS types.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Adapters/EditorSpawnAdapter.cs` | New |

**Success Conditions:**

1. *(Compile)* Zero DDS or `Hrot.ExCon` imports.
2. *(Unit test)* Call `StartPlacementMode(2001, null)` with a test `MapCanvas` spy; assert
   `MapCanvas.PushTool` was called with a `CreationTool`.
3. *(Unit test)* `StartAreaAuthoringMode` pushes `AreaAuthoringTool`.

---

### EDIT1-A002 — `EditorMissionService` (`IMissionEditorService`)

**Design Reference:** DESIGN.md §4.B

**Scope:**

- Create `Hrot.Editor/Adapters/EditorMissionService.cs`.
- Constructor: `(FdpEventBus bus, EntityRepository repo, BehaviorRegistry registry)`.
- `GetAvailableBehaviors(long entityId)`:
  1. Get ECS entity by index (cast `entityId` to `int`).
  2. Guard: alive + has `TkbIdentity`; else return `Array.Empty<string>()`.
  3. `long tkbType = repo.GetComponentRO<TkbIdentity>(e).TkbType`.
  4. `var catalog = BehaviorCatalog.GetValidBehaviors(tkbType)`.
  5. Return `catalog.Where(n => registry.TryGetId(n, out _)).ToList()`.
- `GetMissionSnapshot(entityId)` → read `ActiveMissionPlan` managed component; map to
  `(MissionPlan?, long)`.  Return `(null, 0)` if not present.
- `CommitMissionAsync(entityId, plan, baseVersion)` → generate `requestId = Guid.NewGuid()`;
  cache `TaskCompletionSource<MissionCommitResult>` in `_pendingCommits[requestId]`; publish
  `MissionControlIntent` to bus.  Return `tcs.Task`.
- `SendControlCommandAsync(entityId, type, taskId)` → same pattern with command type.
- `PollAcks()` (called by the Editor update loop) → consume `MissionControlAckEvent` from bus;
  resolve matching pending tasks.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Adapters/EditorMissionService.cs` | New |

**Success Conditions:**

1. *(Compile)* No DDS, no `IExConLogic`, no `IDerRepo` imports.
2. *(Integration test — EDIT1-T004)* Binding tested via behavior filtering test.
3. *(Unit test)* Spawn`TkbInsurgent` entity; `GetAvailableBehaviors` returns list containing
   `"Ambush"` (requires BehaviorRegistry with `"Ambush"` registered).
4. *(Unit test)* `CommitMissionAsync` → pump 1 frame → `MissionControlAckEvent` consumed →
   returned `Task` completes.

---

### EDIT1-A003 — `EditorOrbatAdapter` (`IOrbatDataProvider` + `IOrbatController`)

**Design Reference:** DESIGN.md §4.C

**Scope:**

- Create `Hrot.Editor/Adapters/EditorOrbatAdapter.cs`.
- Constructor: `(EntityRepository world, FdpEventBus bus, IEditorLogic logic)`.
- **`IOrbatDataProvider.GetVisibleNodes`**:
  1. Build query: `.With<EntityInfo>().With<VisHierarchyNode>()`.
  2. Pass over all entities; group by `EntityInfo.CommanderId` to build parent-child map.
  3. Walk tree (BFS/DFS) starting at `CommanderId == 0`.
  4. Apply `filterText` on `EntityInfo.Name`.
  5. Skip subtrees for collapsed `expandedNodes`.
  6. Map to `OrbatNodeViewModel(EntityId: entity.Index, Name: info.Name, Depth: depth, ...)`.
- **`IOrbatController`**:
  - `SelectEntity` → `_logic.SelectEntity(...)`.
  - `CreateUnit` → delegate to `EditorSpawnAdapter.StartPlacementMode(tkbType, null)` (inject
    `ISpawnController`).
  - `RequestEmbark` → resolve both entities by index; publish `EmbarkEntityCommand`.
  - `RequestDisembark` → resolve entity; publish `DisembarkEntityCommand`.
  - `ToggleExpanded` → manages local `HashSet<int>` or delegates to caller's `expandedNodes`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Adapters/EditorOrbatAdapter.cs` | New |

**Success Conditions:**

1. *(Compile)* No DDS, `IDerRepo`, or `Hrot.NED.Descriptors` imports.
2. *(Integration test — EDIT1-T001)* `RequestEmbark` correctly flows to `EditorCargoSystem`.
3. *(Unit test)* With 2 entities in ECS (parent + child via `EntityInfo.CommanderId`),
   `GetVisibleNodes` returns 2 nodes with correct `Depth` values.

---

### EDIT1-A004 — `EditorMapPickAdapter` (`IMapPickService`)

**Design Reference:** DESIGN.md §4.D

**Scope:**

- Create `Hrot.Editor/Adapters/EditorMapPickAdapter.cs`.
- Constructor: `(MapCanvas canvas, IGeographicTransform geoTransform, ISimulationView view)`.
- **`PickLocationAsync(ct)`**:
  - Create `LocationPickerTool`; wire `OnLocationPicked` → `TCS.TrySetResult(geoTransform.ToGeodetic(worldPos))`.
  - Wire `OnCancelled` → `TCS.TrySetCanceled()`.
  - `ct.Register` → if canvas.ActiveTool == tool, pop + cancel.
  - `_canvas.PushTool(tool)`.  Return `tcs.Task`.
- **`PickEntityAsync(filterPresets, ct)`**:
  - Same pattern with `EntityPickerTool`; resolves `Task<int>` with `entity.Index`.
- **`PickAreaEntitiesAsync(filterPresets, ct)`**:
  - Push `ModalBoxSelectionTool` (new local class); resolves `Task<IReadOnlyList<int>>`.
  - `ModalBoxSelectionTool` waits for first mouse-down, sets drag start point, delegates to
    `BoxSelectionTool`; fires `onComplete(List<Entity>)` on mouse-up.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Adapters/EditorMapPickAdapter.cs` | New |
| `Hrot.Editor/Tools/ModalBoxSelectionTool.cs` | New (local helper tool) |

**Success Conditions:**

1. *(Compile)* No DDS references.
2. *(Unit test)* Inject a stub `MapCanvas`; call `PickLocationAsync`; simulate tool's
   `OnLocationPicked` event; assert returned `Task` completes with a `GeoPoint`.
3. *(Unit test)* Cancellation token cancelled before pick → `Task` is cancelled; tool is
   popped from canvas.

---

### EDIT1-A005 — `EditorZoneAdapter` (`IZoneAuthoringController`)

**Design Reference:** DESIGN.md §4.E

**Scope:**

- Create `Hrot.Editor/Adapters/EditorZoneAdapter.cs`.
- Constructor: `(MapCanvas canvas, FdpEventBus bus)`.
- `SetRoadNetworkPath(zoneName, assetPath)` → `_bus.PublishManaged(new UpdateZoneConfigCommand { ZoneName = zoneName, RoadNetworkPath = assetPath })`.
- `StartObstaclePlacementMode(zoneName, radius)`:
  - Create `ObstaclePlacementTool(radius, onClickPos => _bus.Publish(new SpawnZoneObstacleCommand { ... }))`.
  - `_canvas.PushTool(tool)`.
- `ObstaclePlacementTool` is a minimal new Vis2D tool: on left-click fires the callback with
  canvas world position; then pops itself.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Adapters/EditorZoneAdapter.cs` | New |
| `Hrot.Editor/Tools/ObstaclePlacementTool.cs` | New |

**Success Conditions:**

1. *(Compile)* No DDS references.
2. *(Integration test — EDIT1-T003)* Correct ECS entities spawned after round-trip.

---

### EDIT1-A006 — `EditorEntityContextMenuHandler` (`IEntityContextMenuHandler` + `IEntityActionController`)

**Design Reference:** DESIGN.md §4.F

**Scope:**

- Create `Hrot.Editor/UI/EditorEntityContextMenuHandler.cs`.
- Constructor: `(EntityRepository repo, IEditorLogic logic, FdpEventBus bus, IMapPickService pick, ISelectionState selection)`.
- The class must implement **both** `IEntityContextMenuHandler` and `IEntityActionController`.
  Because `SharedContextMenuPopulator.PopulateEntityMenu` strictly requires an
  `IEntityActionController` argument, the handler provides `this` as that argument — no nested
  class or extra object is needed, since all required dependencies (`_bus`, `_logic`) are
  already held by the handler itself.
- **`IEntityActionController` implementations:**
  ```csharp
  public void CenterOnEntity(long entityId) => _logic.CenterOnEntity(entityId);
  public void DeleteEntity(long entityId)   => _bus.PublishManaged(new DestroyEntityCommand { NetworkId = entityId });
  public void EditOverlay(long entityId)    => { _logic.SelectEntity(entityId); _logic.ActivateTool(EditorTool.Edit); }
  public void EditRoute(long entityId)      => { _logic.SelectEntity(entityId); _logic.ActivateTool(EditorTool.Route); }
  public void Rename(long entityId)         => _logic.OpenRenameDialog(entityId);
  public void ActivateMeasureTool()         => _logic.ActivateTool(EditorTool.Measure);
  ```
- **`IEntityContextMenuHandler.PopulateMenu(Entity entity, IContextMenuBuilder builder)`:**
  1. Guard: `repo.IsAlive(entity)`.
  2. Read `NetworkIdentity` (→ `networkId`), `EditablePolyline` presence, `RoutePlan` presence.
  3. Call `SharedContextMenuPopulator.PopulateEntityMenu(networkId, tkbType, hasPolyline, hasRoute, builder, actions: this)`.
  4. If entity has `TargetMemory`:
     - Count valid perceivers from `selection.SelectedEntities`.
     - Add "Mark Target..." (single pick) / "Mark Area Targets..." (area pick).
     - Both are `async void` handlers that `await` the respective `IMapPickService` method, then
       fan-out `SeedTargetCommand` for each (perceiver × target) combination.
- Register handler with `FdpEntityInspectorPanel.RegisterContextMenuHandler(this)`.
- **Empty map space context menu** is handled in the Editor update loop (not inside this class):
  when `StandardInteractionTool.OnWorldClick` fires with `hitEntity == Entity.Null`
  and `button == Right`, set a UI state flag; in the next ImGui frame open
  `"EmptyMapContextMenu"` popup and call
  `SharedContextMenuPopulator.PopulateEmptyMapMenu(builder, actions: contextMenuHandler)`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/UI/EditorEntityContextMenuHandler.cs` | New |

**Success Conditions:**

1. *(Compile)* Class declaration contains both `: IEntityContextMenuHandler, IEntityActionController`; no DDS types.
2. *(Unit test)* Entity with `EditablePolyline` → "Edit Shape" item present; entity without → absent.
3. *(Unit test)* Entity without `NetworkIdentity` (networkId == 0) → "Rename..." absent.
4. *(Unit test)* Entity with `TargetMemory` + 2 selected perceivers → label says "Mark Target for 2 Units...".
5. *(Unit test)* `DeleteEntity(42L)` call → a `DestroyEntityCommand { NetworkId = 42 }` is published to the bus.

---

### EDIT1-A007 — `EditorPreviewAdapter` (`IPreviewController`)

**Design Reference:** DESIGN.md §4.G

**Scope:**

- Create `Hrot.Editor/Adapters/EditorPreviewAdapter.cs`.
- Constructor: `(PreviewClusterOpHandler handler, IScenarioStateProvider stateProvider)`.
- `IsInPreviewMode` → `stateProvider.CurrentState == ScenarioEditorState.OperatingPreview ||
  stateProvider.CurrentState == ScenarioEditorState.LoadingPreview`.
- `EnterPreviewMode()` → `handler.LoadingPreviewCommit()` (synchronous ECS snapshot).
- `ExitPreviewMode()` → `handler.UnloadingPreviewCommit()` (synchronous ECS rewind).

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Adapters/EditorPreviewAdapter.cs` | New |

**Success Conditions:**

1. *(Compile)* No DDS, no `ClusterOpRequest` JSON construction.
2. *(Regression)* Existing `EditorPreviewAndSaveIntegrationTests` (PACK3-U003) continue to
   pass since this adapter wraps the same `PreviewClusterOpHandler`.

---

### EDIT1-A008 — `EditorMapConfigAdapter` (`IMapConfigController`)

**Design Reference:** DESIGN.md §4.H

**Scope:**

- Create `Hrot.Editor/Adapters/EditorMapConfigAdapter.cs`.
- Constructor: `(EntityRepository repo)`.
- `GetCurrentConfig()` → read `MapUserConfig` ECS singleton; map to `MapLayerState`.
  Return a default `MapLayerState` with all layers enabled if singleton absent.
- `ApplyConfig(MapLayerState cfg)` → overwrite `MapUserConfig` singleton directly.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Adapters/EditorMapConfigAdapter.cs` | New |

**Success Conditions:**

1. *(Compile)* No DDS references.
2. *(Unit test)* `ApplyConfig(new MapLayerState(Satellite: false, ...))` → singleton
   `MapUserConfig.ShowSatelliteLayer` is `false`.

---

### EDIT1-A009 — `EditorCargoSystem`

**Design Reference:** DESIGN.md §4.I

**Scope:**

- Create `Hrot.Editor/Systems/EditorCargoSystem.cs`.
- `[UpdateInPhase(SystemPhase.Input)]`; extends `ComponentSystem`.
- `OnUpdate()` consumes `EmbarkEntityCommand` and `DisembarkEntityCommand` from `World.Bus`.
- Embark logic:
  1. Guard both alive; vehicle has `PassengerBuffer`.
  2. Capacity check: `if (buffer.Count >= PassengerBuffer.Capacity) continue`.
  3. `buffer.Passengers[buffer.Count++] = cmd.Passenger`.
  4. Strip `CanMove | CanShoot` from `ActorCapabilityState`.
  5. `World.AddComponent(cmd.Passenger, new IsEmbarkedTag { VehicleEntity = cmd.Vehicle })`.
- Disembark logic:
  1. Guard alive; has `IsEmbarkedTag`.
  2. Find vehicle; remove from `PassengerBuffer`.
  3. Restore `CanMove | CanShoot`.
  4. `World.RemoveComponent<IsEmbarkedTag>(cmd.Passenger)`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Systems/EditorCargoSystem.cs` | New |

**Success Conditions:**

1. *(Integration test — EDIT1-T001)* All three embarkation/disembarkation tests pass.
2. *(Compile)* No DDS or `Hrot.ExCon` imports.

---

### EDIT1-A010 — `EditorPerceptionSetupSystem`

**Design Reference:** DESIGN.md §4.J

**Scope:**

- Create `Hrot.Editor/Systems/EditorPerceptionSetupSystem.cs`.
- `[UpdateInPhase(SystemPhase.Input)]`.
- `OnUpdate()` consumes `SeedTargetCommand` from `World.Bus`:
  1. Guard: both alive; perceiver has `TargetMemory`; target has `SimTransform`.
  2. `GetComponentRW<TargetMemory>(cmd.Perceiver)`.
  3. `GetComponentRO<SimTransform>(cmd.Target)`.
  4. `TargetMemory.AddOrUpdateTarget(ref mem, (long)cmd.Target.PackedValue, pos.X, pos.Y, cmd.ScoreBoost, World.Tick)`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Systems/EditorPerceptionSetupSystem.cs` | New |

**Success Conditions:**

1. *(Integration test — EDIT1-T002)* All three target-seeding tests pass.
2. *(Unit test)* Publish command with dead `Perceiver` → no `InvalidOperationException`; system
   silently skips.

---

### EDIT1-A011 — `EditorZoneAuthoringSystem`

**Design Reference:** DESIGN.md §4.K

**Scope:**

- Create `Hrot.Editor/Systems/EditorZoneAuthoringSystem.cs`.
- `[UpdateInPhase(SystemPhase.Input)]`.
- Consumes `SpawnZoneObstacleCommand`:
  1. `CreateEntity()`.
  2. `AddComponent(e, new SimTransform { Position = new Vector3(cmd.Position.X, cmd.Position.Y, 0f) })`.
  3. `AddComponent(e, new PhysicsCollider { Radius = cmd.Radius, CollisionLayer = PhysicsConstants.EntityCollisionLayer })`.
  4. `AddManagedComponent(e, new ZoneMembership { ZoneName = cmd.ZoneName })`.
- Consumes `UpdateZoneConfigCommand`:
  1. If `HasSingleton<ZoneEnvironmentData>()` → dispose existing `RoadNetworkBlob`.
  2. `RoadNetworkLoader.LoadFromJson(cmd.RoadNetworkPath)` → `SetSingleton(new ZoneEnvironmentData { RoadNetwork = blob })`.
  3. Update `ZoneManagerService` path tracking.
- `ZoneMembership` is a small new managed component (`string ZoneName`) that lives in
  `Hrot.Map.Common` alongside other zone types.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Systems/EditorZoneAuthoringSystem.cs` | New |
| `Hrot.Map.Common/Components/ZoneMembership.cs` | New |

**Success Conditions:**

1. *(Integration test — EDIT1-T003)* Sum scenario obstacle + road network tests pass.
2. *(Unit test)* Obstacle spawned with correct `PhysicsCollider.Radius`.

---

### EDIT1-A012 — `PerceptionMapLayer` (`IMapLayer`)

**Design Reference:** DESIGN.md §4.L

**Scope:**

- Create `Hrot.Editor/Rendering/PerceptionMapLayer.cs`.
- Implements `IMapLayer`; tagged `[MapLayerOrder(500)]` or equivalent ordering attribute.
- Constructor: `(ISimulationView view)`.
  - Builds query: `view.Query().With<TargetMemory>().With<SimTransform>().Build()`.
- `Draw(RenderContext ctx)`:
  - Iterate query (zero allocation).
  - For each entity with `TargetMemory`: read `SimTransform.Position` as 2D canvas coords.
  - For each `TargetMemory.EntityIds[i]` (while i < `mem.Count`): resolve target entity;
    check `IsAlive`; read `SimTransform`.
  - `Raylib.DrawLineEx(perceiverPos2D, targetPos2D, 1.5f, new Color(255, 60, 60, 160))`.
- Does **not** register as an ECS system; does **not** mutate any component.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Rendering/PerceptionMapLayer.cs` | New |

**Success Conditions:**

1. *(Compile)* No ECS mutation methods called.
2. *(Unit test / smoke)* Layer constructed and `Draw` called with 1 seeded target →
   `Raylib.DrawLineEx` invoked once (via Raylib spy or render snapshot).

---

## Phase 5: Hrot.Editor Composition Root Wiring

**Design Reference:** [DESIGN.md §Phase 5](./DESIGN.md#phase-5-hroteditor-composition-root-wiring)

---

### EDIT1-W002 — `ScenarioFileService` Zone Save Integration

**Design Reference:** DESIGN.md §5.B

**Context:**
`ScenarioFileService.SaveScenario` currently serialises only the FDP entity DOM.  Without this
task the `HrotScenarioEnvelopeDto.Zones` property is never populated, causing the zone save
pipeline integration test (EDIT1-T003 Test 3) to fail even though `ZoneManagerService` and
`EditorZoneAuthoringSystem` are correctly wired.

**Scope:**

- In `Hrot.ScenarioEditor/Services/ScenarioFileService.cs`:
  1. Add `IZoneManagerService _zoneManagerService` as a constructor parameter and backing field.
  2. In `SaveScenario(string filePath)`:
     a. After calling `_serializer.Serialize(repo, ...)` to produce the FDP DOM, call
        `var zones = _zoneManagerService.GetActiveZones()`.
     b. Construct `HrotScenarioEnvelopeDto` with `Header`, `Zones = zones.Count > 0 ? zones : null`,
        and `Entities = fdpDom["Entities"]?.AsObject()`.
     c. Serialize the full envelope using `HrotSerializerOptions.Default` and write to disk.
- In the `ScenarioEditorModule` composition root (or wherever
  `ScenarioFileService` is constructed), pass the `IZoneManagerService` instance.
- Add `<ProjectReference>` to `Hrot.Map.Common` in `Hrot.ScenarioEditor.csproj` if not
  already present (to resolve `IZoneManagerService`, `ZoneDefinitionDto`, and the DTO types).

**Out of Scope:**
- Changes to `ZoneManagerService` itself (already covered by PACK3-Z003).
- The load path (`HrotEditLoadHandler`) — already covered by PACK3-Z004.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` | Update constructor + `SaveScenario` |
| `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj` | Add `Hrot.Map.Common` project ref (if absent) |
| Composition root that instantiates `ScenarioFileService` | Pass `IZoneManagerService` argument |

**Success Conditions:**

1. *(Compile)* `ScenarioFileService` compiles; no string-literal zone JSON construction.
2. *(Integration test)* EDIT1-T003 Test 3 (`ZoneAuthoring_FullSave_BundlesZoneDtoInEnvelope`)
   passes: the saved envelope contains `zones["test"].RoadNetworkPath == "Assets/sample_road.json"`
   and one obstacle entry.
3. *(Regression)* Existing `EditorPreviewAndSaveIntegrationTests` (PACK3-U003) continues to
   pass (i.e. a save with zero active zones produces an envelope with `Zones: null`, which
   round-trips correctly).

**Design Reference:** DESIGN.md §Phase 5

**Context:**
All adapters, panels, systems, and map layers created in Phases 1–4 must be wired together
in `EditorApplication.cs` (or equivalent initialisation entry point).

**Scope:**

- Add `Hrot.Editor/Hrot.Editor.csproj` project reference to `Hrot.UI.Common`.
- In the initialisation sequence of `EditorApplication`:
  1. **Register ECS systems** with kernel: `EditorCargoSystem`, `EditorPerceptionSetupSystem`,
     `EditorZoneAuthoringSystem`.
  2. **Instantiate adapters**:
     - `EditorSpawnAdapter(mapCanvas, world.Bus)`
     - `EditorMissionService(world.Bus, world, behaviorRegistry)`
     - `EditorOrbatAdapter(world, world.Bus, editorLogic)`
     - `EditorMapPickAdapter(mapCanvas, geoTransform, simulationView)`
     - `EditorZoneAdapter(mapCanvas, world.Bus)`
     - `EditorEntityContextMenuHandler(world, editorLogic, world.Bus, mapPickAdapter, selectionState)`
     - `EditorPreviewAdapter(previewHandler, scenarioStateProvider)`
     - `EditorMapConfigAdapter(world)`
  3. **Instantiate shared panels**:
     - `new SpawnerPanel()` (pass `spawnAdapter` in `DrawContent`)
     - `new MissionPanel()` (pass `missionService`, `mapPickAdapter` in `DrawContent`)
     - `new SharedOrbatPanel()` (pass `orbatAdapter`, `orbatAdapter` in `DrawContent`)
     - `new ConfigPanel()` (pass `mapConfigAdapter`)
     - `new PreviewPanel()` (pass `previewAdapter`)
     - `new ZoneEditorPanel()` (pass `zoneAdapter`)
  4. **Instantiate FDP panels**:
     - `new FdpEntityInspectorPanel()` configured with `FdpRepositoryAdapter(world)`
     - `new FdpEventBrowserPanel()` fed `world.Bus` each frame
  5. **Register context menu handler** with the `FdpEntityInspectorPanel`.
  6. **MapCanvas**:
     - Push `StandardInteractionTool` (pan/zoom, entity selection, drag-drop).
     - Register `PerceptionMapLayer` with the canvas render chain.
   7. **Empty map space context menu** in the Editor update loop:
      - `standardInteractionTool.OnWorldClick += (pos, btn, ...) => if (btn == Right && hitEntity == Null) _uiState.RequestMapContextMenu = true`.
      - In `DrawUI`: if flagged → `ImGui.OpenPopup("EmptyMapContextMenu")` → call
        `SharedContextMenuPopulator.PopulateEmptyMapMenu(builder, mapConfigAdapter)`.
  8. **Entity symbol rendering with entity id labels:** Register `EntityRenderLayer` from
     `FDP.Toolkit.Vis2D` with the `MapCanvas`.  Configure its label provider (delegate or
     `LabelMode` enum) to display the entity's ECS index / NetworkId, so designers can
     correlate map symbols with the inspector.
  9. **ORBAT context menu sharing:** Inject `EditorEntityContextMenuHandler` into
     `SharedOrbatPanel` (or into `EditorOrbatAdapter`).  On right-click of a node, the panel
     opens an ImGui popup and delegates to `contextMenuHandler.PopulateMenu(entity, builder)`.
  10. **Register all windows** with `WindowManager`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/EditorApplication.cs` | Update initialisation |
| `Hrot.Editor/Hrot.Editor.csproj` | Add `Hrot.UI.Common` project reference |

**Success Conditions:**

1. *(Compile)* `Hrot.Editor` builds with all new adapters referenced.
2. *(Smoke test)* Editor starts without exception; each panel window renders without crash.
3. *(Smoke test)* Entity symbols appear on the map canvas with numeric id labels visible.
4. *(Smoke test)* Right-clicking a node in the ORBAT panel opens the full context menu
   (rename/delete/edit options — same as the entity inspector context menu).
5. *(Regression)* Existing `EditorHarness`-based tests (PACK3-U003, PACK3-Z006) continue to
   pass after composition root changes.

---

## Phase 6: ExCon Adapters

**Design Reference:** [DESIGN.md §Phase 6](./DESIGN.md#phase-6-excon-adapters--dry-network-side)

---

### EDIT1-X001 — `ExConOrbatAdapter` (`IOrbatDataProvider` + `IOrbatController`)

**Design Reference:** DESIGN.md §6.A

**Scope:**

- Create `Hrot.ExCon/Adapters/ExConOrbatAdapter.cs`.
- Constructor: `(IDerRepo repo, IExConLogic logic)`.
- **`IOrbatDataProvider.GetVisibleNodes`**:
  - Iterate `_repo.GetAllEntities()`.
  - Filter `entity.HasDescriptor<EntityInfo>()`.
  - Build `commanderId → children` lookup using `EntityInfo.CommanderId`.
  - Walk from `CommanderId == 0` roots recursively.
  - Map each to `OrbatNodeViewModel(entity.EntityId, info.Name, depth, ...)`.
- **`IOrbatController`**:
  - `SelectEntity` → `_logic.SendSetSelection(entityId)`.
  - `CreateUnit` → `_logic.StartPlacementMode(tkbType, null)`.
  - `RequestEmbark` → `_logic.SendEmbarkRequest(passengerEntityId, vehicleEntityId)` (or
    equivalent DDS command — must be specified by host integration; placeholder acceptable
    if ExCon doesn't yet implement embarkation over DDS).
  - `RequestDisembark` → similar placeholder.
  - `ToggleExpanded` → local UI state only.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ExCon/Adapters/ExConOrbatAdapter.cs` | New |

**Success Conditions:**

1. *(Compile)* Zero ECS imports (`EntityRepository`, `Entity`, `ComponentSystem`).
2. *(Unit test)* Populate stub `IDerRepo` with 2 entities (parent + child); assert
   `GetVisibleNodes` returns 2 `OrbatNodeViewModel` with correct depth.

---

### EDIT1-X002 — `ExConLogic` Implements `ISpawnController`

**Design Reference:** DESIGN.md §6.B

**Scope:**

- In `Hrot.ExCon/ExConLogic.cs`, add `: ISpawnController` to the class declaration.
- The existing `StartPlacementMode`, `StartAreaAuthoringMode`, `StartRouteAuthoringMode`
  implementations already do the correct DDS work — **no logic change**.
- Only the interface declaration and any missing parameter signature adjustments are needed.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ExCon/ExConLogic.cs` | Add `: ISpawnController` |

**Success Conditions:**

1. *(Compile)* `ExConLogic` satisfies all members of `ISpawnController`.
2. *(Regression)* All existing ExCon tests pass unchanged.

---

### EDIT1-X003 — `MissionEditorService` NED Adapter — Dynamic Behavior Filter

**Design Reference:** DESIGN.md §6.C

**Scope:**

- Update `Hrot.ExCon/Services/MissionEditorService.cs`:
  - Add `IDerRepo _repo` constructor parameter.
  - Implement `GetAvailableBehaviors(long entityId)`:
    1. `var entity = _repo.GetEntity((int)entityId)`.
    2. If entity is null → `Array.Empty<string>()`.
    3. `return BehaviorCatalog.GetValidBehaviors(entity.TkbType)`.
  - Remove any existing hardcoded `_knownBehaviors` list (if present).

**Files:**

| File | Change |
|------|--------|
| `Hrot.ExCon/Services/MissionEditorService.cs` | Update |

**Success Conditions:**

1. *(Compile)* No hardcoded behavior string list.
2. *(Unit test)* Stub `IDerRepo` returns entity with `TkbType = TkbEntityTypes.Insurgent`;
   `GetAvailableBehaviors` returns list containing `"Ambush"`.

---

### EDIT1-X004 — ExCon Composition Root: Wire Shared Panels

**Design Reference:** DESIGN.md §6.D

**Scope:**

- In the ExCon composition root (`ExConMock.cs` or equivalent window-manager setup file):
  - Replace old `Hrot.ExCon.Panels.OrbatPanel` instantiation with `SharedOrbatPanel` and
    supply `ExConOrbatAdapter` (implements both `IOrbatDataProvider` and `IOrbatController`).
  - Replace old `MissionPanel` with the shared version; supply updated `MissionEditorService`
    and an `ExConMapPickAdapter` (maps `PickLocationAsync` → DDS `CMD_PICK_LOCATION` roundtrip).
  - Replace old `SpawnerPanel` with shared version; supply `ExConLogic` as `ISpawnController`.
  - Replace old `ConfigPanel` with shared version; supply an `ExConMapConfigAdapter` that
    builds the JSON patch and sends DDS (existing logic extracted to adapter).
- **Note:** `ExConMapPickAdapter` and `ExConMapConfigAdapter` may already exist partially;
  this task only ensures the shared panels are wired to them.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ExCon/ExConMock.cs` (or equivalent) | Update panel instantiation |
| `Hrot.ExCon/Adapters/ExConMapConfigAdapter.cs` | New (or update if exists) |

**Success Conditions:**

1. *(Compile)* `Hrot.ExCon` builds without old panel references.
2. *(Regression)* All ExCon integration tests pass.

---

### EDIT1-X005 — ExCon `ContextMenuLogic` Refactor via `SharedContextMenuPopulator`

**Design Reference:** DESIGN.md §6.E

**Context:**
`Hrot.ExCon/Logic/ContextMenuLogic.cs` currently hardcodes which menu items to include and
serialises them directly to a `ContextActionsUpdate` DDS message.  Not refactoring this leaves
ExCon in violation of the DRY principle — the same menu-definition rules are duplicated
separately from `SharedContextMenuPopulator`.

**Scope:**

1. Create `Hrot.ExCon/Adapters/JsonContextMenuBuilder.cs` implementing `IContextMenuBuilder`:
   - `AddItem(string label, Action callback)`: generate a monotonically incrementing integer
     `id`; cache `(id → callback)` in `Dictionary<int, Action> _callbacks`; append a
     `ContextMenuItem` DTO to the internal list.
   - `AddSeparator()`: append a separator sentinel DTO.
   - `IReadOnlyList<ContextMenuItem> Build()` → return the collected list.
   - `IReadOnlyDictionary<int, Action> GetCallbackRegistry()` → return `_callbacks`.
   - Class is **not** thread-safe; it is constructed fresh per right-click event.

2. Create `Hrot.ExCon/Adapters/ExConEntityActionAdapter.cs` implementing `IEntityActionController`:
   - Constructor: `(IExConLogic logic)`.
   - Map each method to the corresponding `IExConLogic` / DDS call (see DESIGN.md §6.E for
     the exact method mappings).  Use placeholder `_logic.Log("not implemented")` for any
     method that has no ExCon equivalent yet.

3. Update `Hrot.ExCon/Logic/ContextMenuLogic.cs`:
   - Replace the hardcoded item-list construction block in `BuildMenu(IDerEntity entity)` with:
     ```csharp
     var builder = new JsonContextMenuBuilder();
     var actions = new ExConEntityActionAdapter(_logic);

     bool hasPolyline = entity.HasDescriptor<MapVisualOverlay>() || entity.HasDescriptor<MapRoute>();
     bool hasRoute    = entity.HasDescriptor<MapRoute>();

     SharedContextMenuPopulator.PopulateEntityMenu(
         entity.EntityId, entity.TkbType, hasPolyline, hasRoute, builder, actions);

     _logic.SendContextActionsUpdate(builder.Build(), builder.GetCallbackRegistry());
     ```
   - The existing callback-invocation path (when `ContextActionInvoked` arrives from the IG)
     must look up the integer `id` in the registry and execute the cached `Action`.  Adjust
     the existing dispatch code to accept the `GetCallbackRegistry()` dictionary from the
     builder.  If a session-scoped registry is needed (e.g. stored as a field between the
     `BuildMenu` call and the `ContextActionInvoked` response), store it accordingly.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ExCon/Adapters/JsonContextMenuBuilder.cs` | New |
| `Hrot.ExCon/Adapters/ExConEntityActionAdapter.cs` | New |
| `Hrot.ExCon/Logic/ContextMenuLogic.cs` | Update `BuildMenu` method |

**Success Conditions:**

1. *(Compile)* `Hrot.ExCon` builds; `ContextMenuLogic` has zero hardcoded item-label strings.
2. *(Unit test)* Construct `JsonContextMenuBuilder`; call `AddItem("Delete", cb)` and
   `AddSeparator()`; assert `Build()` returns list of 2 items; assert `GetCallbackRegistry()`
   contains 1 entry with the correct callback.
3. *(Unit test)* `ContextMenuLogic.BuildMenu` for an entity with `MapVisualOverlay` descriptor
   → `SendContextActionsUpdate` called with a list that includes an item labelled "Edit Shape".
4. *(Regression)* ExCon context menu integration tests (if any) continue to pass.

**Design Reference:** [DESIGN.md §Phase 7](./DESIGN.md#phase-7-headless-integration-tests)

---

### EDIT1-T001 — Embarkation & Cargo Integration Tests

**Design Reference:** DESIGN.md §7.A

**Scope:**

- Add test class `EditorAuthoringIntegrationTests` to `Hrot.ClusterRunner.Integration.Tests`.
- Add three test methods:

  **`Embarkation_ValidRequest_UpdatesPassengerBufferAndStripsCapabilities`**
  ```
  Setup:  EditorHarness; spawn APC (with PassengerBuffer) + Soldier (with ActorCapabilityState).
  Act:    new EditorOrbatAdapter(...).RequestEmbark(soldier.Index, apc.Index); PumpFrames(1).
  Assert: PassengerBuffer.Count == 1; buffer.Passengers[0] == soldier;
          IsEmbarkedTag present on soldier;
          ActorCapabilityState.Capabilities does not have CanMove flag.
  ```

  **`Embarkation_CapacityLimitEnforced_NoMutationOnOverflow`**
  ```
  Setup:  Fill APC to PassengerBuffer.Capacity soldiers.
  Act:    RequestEmbark(extraSoldier.Index, apc.Index); PumpFrames(1).
  Assert: buffer.Count == Capacity; extra soldier has no IsEmbarkedTag.
  ```

  **`Disembark_RestoresCapabilities`**
  ```
  Setup:  Embark soldier (via EDIT1-T001 test 1 steps).
  Act:    RequestDisembark(soldier.Index); PumpFrames(1).
  Assert: soldier has no IsEmbarkedTag;
          ActorCapabilityState has CanMove | CanShoot restored.
  ```

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner.Integration.Tests/EditorAuthoringIntegrationTests.cs` | New |

**Success Conditions:**

1. All three test methods pass deterministically.
2. Tests run headless (no GPU, no DDS, no Raylib window) using `EditorHarness`.
3. Total execution time < 500 ms for all three tests combined.

---

### EDIT1-T002 — Target Memory Seeding Integration Tests

**Design Reference:** DESIGN.md §7.B

**Scope:**

Add three test methods to `EditorAuthoringIntegrationTests`:

  **`TargetSeeding_SinglePerceiver_SeedsMemoryBuffer`**
  ```
  Setup:  Insurgent (TargetMemory) + APC (SimTransform at (10, 20, 0)).
  Act:    Bus.Publish(new SeedTargetCommand { Perceiver=insurgent, Target=apc, ScoreBoost=100f }); PumpFrames(1).
  Assert: TargetMemory.Count == 1; EntityIds[0] == (long)apc.PackedValue; Scores[0] >= 100f.
  ```

  **`TargetSeeding_NToOne_AllPerceiversReceiveTarget`**
  ```
  Setup:  3 insurgents (each with TargetMemory) + 1 APC.
  Act:    Publish SeedTargetCommand for each insurgent → same APC. PumpFrames(1).
  Assert: Each insurgent's TargetMemory.Count == 1.
  ```

  **`TargetSeeding_OneToN_PerceiverReceivesAllTargets`**
  ```
  Setup:  1 insurgent (TargetMemory) + 3 APCs (each with SimTransform).
  Act:    Publish SeedTargetCommand(insurgent, apc1), (insurgent, apc2), (insurgent, apc3). PumpFrames(1).
  Assert: insurgent TargetMemory.Count == 3.
  ```

**Success Conditions:**

1. All three methods pass.
2. No unsafe memory access exceptions (fixed-array bounds respected).

---

### EDIT1-T003 — Zone Obstacle Authoring & Save Pipeline Tests

**Design Reference:** DESIGN.md §7.C

**Scope:**

Add three test methods to `EditorAuthoringIntegrationTests`:

  **`ZoneAuthoring_ObstaclePlacement_SpawnsPhysicsCollider`**
  ```
  Setup:  EditorHarness.
  Act:    Bus.PublishManaged(SpawnZoneObstacleCommand { ZoneName="test", Position=(50,25), Radius=10 }); PumpFrames(1).
  Assert: repo.Query().With<PhysicsCollider>().Count() == 1;
          collider.Radius == 10f; SimTransform.Position == (50, 25, 0).
  ```

  **`ZoneAuthoring_RoadNetworkUpdate_InjectsZoneEnvironmentDataSingleton`**
  ```
  Setup:  EditorHarness (sample_road.json present in test assets).
  Act:    Bus.PublishManaged(UpdateZoneConfigCommand { ZoneName="test", RoadNetworkPath="Assets/sample_road.json" }); PumpFrames(1).
  Assert: repo.HasSingleton<ZoneEnvironmentData>() == true;
          ZoneEnvironmentData.RoadNetwork.Nodes.IsCreated == true.
  ```

  **`ZoneAuthoring_FullSave_BundlesZoneDtoInEnvelope`**
  ```
  Setup:  Run both previous scenarios (obstacle + road network). Then call harness.Editor.SaveScenario(tempFile).
  Assert: Deserialise file with HrotJsonOptions;
          envelope.Zones["test"].RoadNetworkPath == "Assets/sample_road.json";
          envelope.Zones["test"].Obstacles.Count == 1;
          obstacles[0].X == 50f; obstacles[0].Radius == 10f.
  Cleanup: File.Delete(tempFile).
  ```

**Success Conditions:**

1. All three methods pass.
2. Temp file always cleaned up even on failure (use `try/finally`).

---

### EDIT1-T004 — Behavior Catalog Filtering Tests

**Design Reference:** DESIGN.md §7.D

**Scope:**

Add three test methods to `EditorAuthoringIntegrationTests` (or a separate
`BehaviorCatalogTests` unit test class in `Hrot.Map.Common.Tests` / `Hrot.IG.Tests`):

  **`BehaviorCatalog_Insurgent_ReturnsInsurgentBehaviors`**
  ```
  Act:    BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.Insurgent).
  Assert: Contains "Ambush"; DoesNotContain "WanderCivil".
  ```

  **`BehaviorCatalog_Civilian_ReturnsCivilianBehaviors`**
  ```
  Act:    BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianPedestrian).
  Assert: Contains "WanderCivil"; DoesNotContain "Ambush".
  ```

  **`EditorMissionService_FiltersOutUnregisteredBehaviors`**
  ```
  Setup:  EditorHarness; spawn Insurgent entity with TkbIdentity.TkbType = Insurgent;
          construct BehaviorRegistry; register "Ambush" only (not "MoveToLocation").
          Construct EditorMissionService(..., registry).
  Act:    service.GetAvailableBehaviors(insurgent.Index).
  Assert: Contains "Ambush"; DoesNotContain "MoveToLocation" (not in registry).
  ```

**Success Conditions:**

1. All three methods pass.
2. Tests run without `EditorHarness` for the pure catalog tests (they are pure static logic).
