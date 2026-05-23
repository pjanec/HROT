# Hrot.UI.Common

| Property | Value |
|---|---|
| **Project file** | `Hrot/Engine/Hrot.UI.Common/Hrot.UI.Common.csproj` |
| **Namespace root** | `Hrot.UI.Common` |
| **Target framework** | .NET 8.0 |
| **Documented** | 2026-05-23 |

---

## README Validation

**Missing** -- no `README.md` exists in the project folder.

---

## Executive Overview

`Hrot.UI.Common` is a shared UI library that sits at the boundary between the HROT
simulation Engine and its user-facing shells.  It provides:

- **Port interfaces** (the `Facades/` layer) that decouple every reusable panel from
  the concrete ECS, DDS transport, and rendering implementations.
- **Reusable ImGui panels** that can be hosted by any shell application -- the
  standalone editor (`Hrot.Presentation`), the exercise control station
  (`Hrot.ExCon`), or future consumers -- without modification.
- **Shared view-model types and constants** consumed by both the panels and the
  adapter code that lives in the host shells.

The guiding architectural principle is the **Hexagonal (Ports and Adapters)**
pattern: all UI logic depends only on interfaces declared in this project; no
panel ever imports a concrete ECS world, a DDS topic, or a network transport.
This boundary is what allows the same `SharedOrbatPanel` to work identically
in a local editor (where the adapter talks directly to an in-process ECS) and
in a remote operator console (where the adapter sends DDS commands over the
network).

The project grants `InternalsVisibleTo` access to `Hrot.ExCon.Tests`, allowing
panel logic exposed as `internal` methods to be exercised by that test assembly
without an active ImGui render frame.

---

## Architecture

### Layering

```
+------------------------------------------------------------------+
|                     Host application shells                       |
|   (Hrot.Presentation / Hrot.ExCon / future shells)               |
|                                                                   |
|   Concrete adapters: implement each I*Controller / I*Service      |
|   interface declared in Hrot.UI.Common.Facades                    |
+------------------------------------------------------------------+
                          |  implements
                          v
+------------------------------------------------------------------+
|                      Hrot.UI.Common                               |
|                                                                   |
|  Facades/     - 9 port interfaces (I*Controller / I*Service)     |
|  Panels/      - 5 panel classes + PanelConstants                  |
|  Models/      - 3 record DTOs                                     |
|  Menus/       - SharedContextMenuPopulator (static)               |
+------------------------------------------------------------------+
          |                           |
          | ProjectReference          | ProjectReference
          v                           v
+--------------------+   +--------------------------+
|   Hrot.Core        |   |   Fdp.Presentation       |
|                    |   |                          |
|  MissionPlan       |   |  IContextMenuBuilder     |
|  eMissionCommandType   |  (ImGui panel framework) |
|  eForceIdentifier  |   +--------------------------+
|  GeoPoint          |
+--------------------+
```

### Panels and their port dependencies

```
+---------------------+   uses   +------------------------+
|  ConfigPanel        |--------->|  IMapConfigController  |
+---------------------+          +------------------------+

+---------------------+   uses   +------------------------+
|  SpawnerPanel       |--------->|  ISpawnController      |
+---------------------+          +------------------------+

+---------------------+   uses   +------------------------+
|  SharedOrbatPanel   |--------->|  IOrbatDataProvider    |
|                     |--------->|  IOrbatController      |
+---------------------+          +------------------------+

+---------------------+   uses   +------------------------+
|  PreviewPanel       |--------->|  IPreviewController    |
+---------------------+          +------------------------+

+---------------------+   uses   +------------------------+
|  ZoneEditorPanel    |--------->|  IZoneAuthoringController
+---------------------+          +------------------------+

+-----------------------------+   uses   +---------------------------+
|  SharedContextMenuPopulator |--------->|  IEntityActionController  |
|  (static, stateless)        |--------->|  IContextMenuBuilder      |
+-----------------------------+          +---------------------------+
```

### Call flow: operator clicks "Apply Road Network" in ZoneEditorPanel

```
  Host render loop
       |
       v
  ZoneEditorPanel.DrawContent(IZoneAuthoringController)
       |  ImGui.Button("Apply Road Network") returns true
       v
  ZoneEditorPanel.HandleApplyRoadNetwork(ctrl)          [internal, testable]
       |
       v
  IZoneAuthoringController.SetRoadNetworkPath(zoneName, path)
       |                                                 [adapter in host shell]
       v
  (concrete adapter dispatches ECS command or DDS message)
```

### Async map-pick workflow (IMissionEditorService + IMapPickService)

```
  UI panel (e.g., MissionPanel in host shell)
       |
       | await IMapPickService.PickLocationAsync(ct)
       v
  IMapPickService adapter
    - suspends authoring input, shows crosshair cursor
    - waits for IG map click event
    - returns GeoPoint to caller
       |
       v
  Panel builds MissionPlan from returned GeoPoint
       |
       | await IMissionEditorService.CommitMissionAsync(entityId, plan, version)
       v
  IMissionEditorService adapter
    - serialises plan to DDS command
    - waits for ACK from simulation server
    - returns MissionCommitResult(Success, NewVersion, ErrorMessage)
       |
       v
  Panel reacts: shows success or version-conflict error banner
```

---

## Source Structure

### Namespace `Hrot.UI.Common.Facades`

Port interfaces that define the contracts between the shared UI and the host application.
All interfaces are `public`.

| File | Type | Role |
|---|---|---|
| `IEntityActionController.cs` | `interface IEntityActionController` | Entity-level map/editor actions |
| `IMapConfigController.cs` | `interface IMapConfigController` | Map layer visibility read/write |
| `IMapPickService.cs` | `interface IMapPickService` | Async operator map-pick operations |
| `IMissionEditorService.cs` | `interface IMissionEditorService` | Mission plan read/commit (async) |
| `IOrbatController.cs` | `interface IOrbatController` | ORBAT command dispatch |
| `IOrbatDataProvider.cs` | `interface IOrbatDataProvider` | ORBAT tree data query |
| `IPreviewController.cs` | `interface IPreviewController` | Edit/preview mode switching |
| `ISpawnController.cs` | `interface ISpawnController` | Entity placement authoring |
| `IZoneAuthoringController.cs` | `interface IZoneAuthoringController` | Zone road-network and obstacle authoring |

### Namespace `Hrot.UI.Common.Models`

View-model and result types shared between panels and adapters.  All types are `public`
positional records.

| File | Type | Role |
|---|---|---|
| `MapLayerState.cs` | `record MapLayerState` | Seven-flag layer visibility snapshot |
| `MissionCommitResult.cs` | `record MissionCommitResult` | ACK result from a mission commit |
| `OrbatNodeViewModel.cs` | `sealed record OrbatNodeViewModel` | Flat ORBAT tree node for ImGui rendering |

### Namespace `Hrot.UI.Common.Panels`

Reusable ImGui panel implementations.  All panel classes are `public sealed`.

| File | Type | Role |
|---|---|---|
| `ConfigPanel.cs` | `class ConfigPanel` | Map layer visibility editor |
| `PanelConstants.cs` | `static class PanelConstants` | Shared numeric and string constants |
| `PreviewPanel.cs` | `class PreviewPanel` | Edit / Preview mode toggle |
| `SharedOrbatPanel.cs` | `class SharedOrbatPanel` | ORBAT hierarchy tree with DnD embark |
| `SpawnerPanel.cs` | `class SpawnerPanel` | TKB entity type catalog and placement |
| `ZoneEditorPanel.cs` | `class ZoneEditorPanel` | Zone road-network and obstacle authoring |

### Namespace `Hrot.UI.Common.Menus`

| File | Type | Role |
|---|---|---|
| `SharedContextMenuPopulator.cs` | `static class SharedContextMenuPopulator` | Stateless ImGui context menu factory |

### Namespace `Hrot.UI.Common.Panels` (supporting record)

| File | Type | Role |
|---|---|---|
| `SpawnerPanel.cs` | `sealed record TkbCatalogEntry` | Immutable catalog entry (TkbId, Name) |

---

## Public API Reference

### IEntityActionController

Declared in `Facades/IEntityActionController.cs`.

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

Used by `SharedContextMenuPopulator` to execute actions chosen from entity context menus.

| Member | Description |
|---|---|
| `CenterOnEntity` | Pans and zooms the map to centre on the entity. |
| `DeleteEntity` | Requests deletion of the entity from the scenario. |
| `EditOverlay` | Opens the tactical graphic (overlay) editor. |
| `EditRoute` | Opens the route editor for the entity. |
| `Rename` | Opens the rename dialog. |
| `ActivateMeasureTool` | Activates the distance measurement tool. |
| `ActivateRotateTool` | Activates the entity rotation tool. |

---

### IMapConfigController

Declared in `Facades/IMapConfigController.cs`.

```csharp
public interface IMapConfigController
{
    MapLayerState GetCurrentConfig();
    void ApplyConfig(MapLayerState config);
}
```

| Member | Description |
|---|---|
| `GetCurrentConfig` | Returns the current layer visibility state. |
| `ApplyConfig` | Submits a new layer state to the rendering pipeline. |

---

### IMapPickService

Declared in `Facades/IMapPickService.cs`.

```csharp
public interface IMapPickService
{
    Task<GeoPoint> PickLocationAsync(CancellationToken ct = default);
    Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default);
    Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default);
}
```

All methods suspend the caller until the operator completes a map interaction.

| Member | Description |
|---|---|
| `PickLocationAsync` | Resolves to a `GeoPoint` when the operator clicks the map. |
| `PickEntityAsync` | Resolves to a single entity ID; optional `filterPresets` restricts the pickable layers. |
| `PickAreaEntitiesAsync` | Resolves to all entity IDs inside the operator-drawn rectangle. |

---

### IMissionEditorService

Declared in `Facades/IMissionEditorService.cs`.

```csharp
public interface IMissionEditorService
{
    IReadOnlyList<string> GetAvailableBehaviors(long entityId);
    (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId);
    Task<MissionCommitResult> CommitMissionAsync(long entityId, MissionPlan plan, long baseVersion);
    Task<MissionCommitResult> SendControlCommandAsync(long entityId, eMissionCommandType type, Guid taskId);
}
```

Provides optimistic-concurrency mission editing over an async transport.

| Member | Description |
|---|---|
| `GetAvailableBehaviors` | Lists behavior names valid for the entity's TKB type. |
| `GetMissionSnapshot` | Returns the current plan and its optimistic-lock version. |
| `CommitMissionAsync` | Sends a full mission-replace command; rejects on version mismatch. |
| `SendControlCommandAsync` | Sends an imperative control command (e.g. abort, jump-to-task). |

---

### IOrbatController

Declared in `Facades/IOrbatController.cs`.

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

All write operations delegate to the underlying ECS command bus or DDS transport.

| Member | Description |
|---|---|
| `SelectEntity` | Selects and centres the map on the entity. |
| `CreateUnit` | Creates a new unplaced unit of the given TKB type. |
| `ToggleExpanded` | Notifies the domain layer that the tree node was expanded/collapsed. |
| `RequestEmbark` | Asks the execution system to embark a passenger into a vehicle. |
| `RequestDisembark` | Asks the execution system to disembark a passenger. |
| `RequestAssignSubordinate` | Assigns an entity as subordinate to a commanding entity. |
| `RequestRemoveSubordinate` | Removes an entity from its command hierarchy. |

---

### IOrbatDataProvider

Declared in `Facades/IOrbatDataProvider.cs`.

```csharp
public interface IOrbatDataProvider
{
    IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(string filterText, HashSet<int> expandedNodes);
}
```

Returns a flat, pre-filtered list of `OrbatNodeViewModel` ready for ImGui iteration.
The `expandedNodes` set is owned by `SharedOrbatPanel`; the provider respects it when
deciding which children to include.

---

### IPreviewController

Declared in `Facades/IPreviewController.cs`.

```csharp
public interface IPreviewController
{
    bool IsInPreviewMode { get; }
    void EnterPreviewMode(bool startPaused = false);
    void ExitPreviewMode();
}
```

| Member | Description |
|---|---|
| `IsInPreviewMode` | Read each frame to determine which button to show. |
| `EnterPreviewMode` | Suspends authoring and starts scenario simulation. |
| `ExitPreviewMode` | Restores authoring mode. |

---

### ISpawnController

Declared in `Facades/ISpawnController.cs`.

```csharp
public interface ISpawnController
{
    void StartPlacementMode(long tkbType, string? initialPropertiesJson = null);
    void StartAreaAuthoringMode(string styleOverrideJson = "");
    void StartRouteAuthoringMode();
}
```

| Member | Description |
|---|---|
| `StartPlacementMode` | Activates single-entity placement; `initialPropertiesJson` carries force-affiliation overrides. |
| `StartAreaAuthoringMode` | Activates filled-area drawing; `styleOverrideJson` carries fill colour and line thickness. |
| `StartRouteAuthoringMode` | Activates polyline route drawing. |

---

### IZoneAuthoringController

Declared in `Facades/IZoneAuthoringController.cs`.

```csharp
public interface IZoneAuthoringController
{
    void SetRoadNetworkPath(string activeZoneName, string assetPath);
    void StartObstaclePlacementMode(string activeZoneName, float radius);
}
```

| Member | Description |
|---|---|
| `SetRoadNetworkPath` | Assigns a road-network JSON asset to the named zone. |
| `StartObstaclePlacementMode` | Activates click-to-place circular obstacle mode for the named zone. |

---

### MapLayerState

Declared in `Models/MapLayerState.cs`.

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

Immutable snapshot of map layer visibility.  Passed bidirectionally between
`ConfigPanel` and `IMapConfigController`.

---

### MissionCommitResult

Declared in `Models/MissionCommitResult.cs`.

```csharp
public record MissionCommitResult(bool Success, long NewVersion, string? ErrorMessage = null);
```

| Property | Description |
|---|---|
| `Success` | `true` when the server accepted and applied the change. |
| `NewVersion` | The new optimistic-lock version after a successful commit; `0` on failure. |
| `ErrorMessage` | Human-readable error description; `null` on success. |

---

### OrbatNodeViewModel

Declared in `Models/OrbatNodeViewModel.cs`.

```csharp
public sealed record OrbatNodeViewModel(
    int EntityId,
    string Name,
    int Depth,
    bool HasChildren,
    bool IsPendingDelete,
    bool CanAcceptSubordinates);
```

| Property | Description |
|---|---|
| `EntityId` | Network entity ID used as the drag-drop payload and select key. |
| `Name` | Display label rendered in the selectable row. |
| `Depth` | Zero-based tree depth; multiplied by 12 px for ImGui indentation. |
| `HasChildren` | Controls whether an arrow-toggle button is rendered. |
| `IsPendingDelete` | When `true` the row should render grayed out. |
| `CanAcceptSubordinates` | When `true` a drop dispatches `RequestAssignSubordinate` rather than `RequestEmbark`. |

---

### TkbCatalogEntry

Declared in `Panels/SpawnerPanel.cs`.

```csharp
public sealed record TkbCatalogEntry(long TkbId, string Name);
```

Immutable catalog entry injected into `SpawnerPanel` at construction time.
Constructed once at application startup from the TKB template registry.

---

### ConfigPanel

```csharp
public sealed class ConfigPanel
{
    public ConfigPanel(long localNodeId = 0);

    // State properties (all read/write)
    public bool  SatelliteLayer   { get; set; }
    public bool  GroundUnits      { get; set; }
    public bool  TacticalGraphics { get; set; }
    public bool  AirUnits         { get; set; }
    public bool  Vehicles         { get; set; }
    public bool  RoadGraphs       { get; set; }
    public bool  Grid             { get; set; }
    public float IconScale        { get; set; }  // clamped [0.5, 2.0]

    // Logic
    public void LoadConfig(IMapConfigController ctrl);
    public void HandleSendConfigPatch(IMapConfigController ctrl);

    // Render
    public void DrawContent(IMapConfigController ctrl);
    public void Draw(IMapConfigController ctrl);           // includes Begin/End
}
```

`LoadConfig` is called once at startup to populate the panel from the live
rendering state.  `HandleSendConfigPatch` is called when the operator presses
the button; it builds a `MapLayerState` record and calls `ApplyConfig`.
`Draw` guards itself with `ImGui.GetCurrentContext() == IntPtr.Zero`.

---

### PanelConstants

```csharp
public static class PanelConstants
{
    // ConfigPanel
    public const float IconScaleMin     = 0.5f;
    public const float IconScaleMax     = 2.0f;
    public const float IconScaleDefault = 1.0f;

    // SpawnerPanel
    public const int   FilterTextMaxLength = 256;

    // MissionPanel (used by host shells)
    public const int   MissionBehaviorParamsMaxLength      = 2048;
    public const float MoveToLocationDefaultSpeed          = 15f;
    public const float MoveToLocationDefaultArrivalRadius  = 50f;
    public const int   MissionBehaviorParamsEditorLines    = 4;
    public const string VersionConflictErrorMessage        = "ERR_VERSION_CONFLICT";
    public const string FilterPresetRoadGraphs             = "road_graphs";
}
```

Note that several `MissionPanel` constants are declared here even though the
`MissionPanel` class itself lives in the host shells.  This allows those shells
to share consistent defaults without duplicating magic numbers.

---

### PreviewPanel

```csharp
public sealed class PreviewPanel
{
    public void DrawContent(IPreviewController ctrl);

    // internal -- exposed for unit tests
    internal void HandleEnterPreview(IPreviewController ctrl);
    internal void HandleExitPreview(IPreviewController ctrl);
}
```

Stateless; reads `IPreviewController.IsInPreviewMode` each frame.  Renders a
green "Enter Preview" button when in edit mode, and an amber "Stop Preview"
button when in preview mode.

---

### SharedContextMenuPopulator

```csharp
public static class SharedContextMenuPopulator
{
    public static void PopulateEntityMenu(
        long entityId,
        long tkbType,
        bool hasEditablePolyline,
        bool hasRoutePlan,
        IContextMenuBuilder builder,
        IEntityActionController actions);

    public static void PopulateEmptyMapMenu(
        IContextMenuBuilder builder,
        IEntityActionController actions);
}
```

Purely functional: no state, no ImGui calls.  The host passes an open
`IContextMenuBuilder` (from `Fdp.Presentation`) and an adapter.  This makes
both methods fully unit-testable without an ImGui context.

| Method | Description |
|---|---|
| `PopulateEntityMenu` | Adds Centre, Rename, Edit Shape, Edit Route, Rotate, and Delete items. Rename is omitted when `entityId == 0`. Edit Shape and Edit Route are conditional on their respective flags. |
| `PopulateEmptyMapMenu` | Adds only a Measurement Tool item. |

---

### SharedOrbatPanel

```csharp
public sealed class SharedOrbatPanel
{
    // State
    public string FilterText { get; set; }
    public IReadOnlySet<int> ExpandedNodes { get; }

    // Render
    public void DrawContent(IOrbatDataProvider data, IOrbatController ctrl);

    // internal -- exposed for unit tests
    internal void HandleDropPayload(int passengerId, int vehicleId, IOrbatController ctrl);
    internal void HandleHierarchyDropPayload(int subId, OrbatNodeViewModel targetNode, IOrbatController ctrl);
    internal void HandleSelectEntity(int entityId, IOrbatController ctrl);
}
```

Key implementation notes:

- The panel calls `IOrbatDataProvider.GetVisibleNodes` each frame with its
  `_filterText` and `_expandedNodes`, receiving a pre-filtered flat list.
- Each row is simultaneously a **drag source** (payload type `"ORBAT_ENTITY"`,
  4-byte entity ID) and a **drop target**.
- `unsafe` blocks are confined to the pointer reads of the ImGui DnD payload;
  all conditional logic runs in safe `Handle*` methods.
- Dropping a node onto another node delegates to `HandleHierarchyDropPayload`:
  `RequestAssignSubordinate` when `CanAcceptSubordinates` is true;
  `RequestEmbark` otherwise.
- A background `ImGui.Dummy` drop target handles "drop onto empty space" to
  remove an entity from its command hierarchy.

---

### SpawnerPanel

```csharp
public sealed class SpawnerPanel
{
    public SpawnerPanel(IEnumerable<TkbCatalogEntry> catalog);
    public SpawnerPanel();   // empty catalog (useful in tests)

    // State
    public string  SearchFilter    { get; set; }   // rebuilds FilteredEntries on set
    public long    SelectedType    { get; }
    public eForceIdentifier SelectedAffiliation { get; }
    public IReadOnlyList<TkbCatalogEntry> FilteredEntries { get; }

    // Handlers (public for testability)
    public void HandleTypeSelected(long tkbId);
    public void HandleAffiliationChange(eForceIdentifier affiliation);
    public void HandleActivatePlacementTool(ISpawnController spawn);
    public void HandleStartAreaAuthoring(ISpawnController spawn);
    public void HandleStartRouteAuthoring(ISpawnController spawn);

    // Render
    public void DrawContent(ISpawnController spawn);
    public void Draw(ISpawnController spawn);   // includes Begin/End
}
```

Key implementation notes:

- The catalog is injected at construction and never mutated.
- `SearchFilter` setter always calls `RebuildFilter()`, which repopulates
  `_filteredEntries` using `OrdinalIgnoreCase` substring matching.  No LINQ
  allocations occur inside `Draw`.
- `HandleActivatePlacementTool` serialises the selected `eForceIdentifier` to
  JSON (`{ "Affiliation": "FORCE_FRIENDLY" }`) before calling
  `ISpawnController.StartPlacementMode`.
- `HandleStartAreaAuthoring` serialises fill colour and line thickness to JSON
  before calling `StartAreaAuthoringMode`.

---

### ZoneEditorPanel

```csharp
public sealed class ZoneEditorPanel
{
    // State (public read/write for test setup)
    public string ZoneName        { get; set; }   // default: "urban_combat_zone"
    public string RoadNetworkPath { get; set; }   // default: "Assets/sample_road.json"
    public float  ObstacleRadius  { get; set; }   // clamped [1, 50]

    // Render
    public void DrawContent(IZoneAuthoringController ctrl);

    // internal -- exposed for unit tests
    internal void HandleApplyRoadNetwork(IZoneAuthoringController ctrl);
    internal void HandlePlaceObstacle(IZoneAuthoringController ctrl);
}
```

---

## Dependencies

### Project References

| Dependency | Path | Consumed symbols |
|---|---|---|
| `Hrot.Core` | `Hrot/Engine/Hrot.Core/Hrot.Core.csproj` | `MissionPlan`, `eMissionCommandType`, `eForceIdentifier`, `GeoPoint` |
| `Fdp.Presentation` | `FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj` | `IContextMenuBuilder` (used by `SharedContextMenuPopulator`) |

### NuGet Packages

None declared directly.  `ImGuiNET` is available transitively through
`Fdp.Presentation`.

### Compiler settings

| Setting | Value |
|---|---|
| `Nullable` | `enable` |
| `ImplicitUsings` | `enable` |
| `AllowUnsafeBlocks` | `true` (required for ORBAT DnD payload pointer reads) |
| `TreatWarningsAsErrors` | `true` |

### InternalsVisibleTo

```csharp
[assembly: InternalsVisibleTo("Hrot.ExCon.Tests")]
```

Grants `Hrot.ExCon.Tests` access to the `internal Handle*` methods on the
panels so that controller dispatch can be tested without an active ImGui context.

---

## Usage Examples

### Example 1 -- Hosting ConfigPanel in an application shell

```csharp
// In the host shell's startup / DI registration:
// The host provides a concrete adapter that knows how to talk to the renderer.
IMapConfigController mapConfigAdapter = new MyMapConfigAdapter(renderingPipeline);

var configPanel = new ConfigPanel(localNodeId: myNodeId);
configPanel.LoadConfig(mapConfigAdapter);   // pre-populate from current renderer state

// In the per-frame render loop:
if (ImGui.Begin("Map Configuration"))
{
    configPanel.DrawContent(mapConfigAdapter);
    ImGui.End();
}
// Or, using the self-contained overload:
configPanel.Draw(mapConfigAdapter);
```

### Example 2 -- Hosting SpawnerPanel with a TKB catalog

```csharp
// Build the catalog once at startup from TKB template registry.
var catalog = tkbRegistry
    .GetAllTemplates()
    .Select(t => new TkbCatalogEntry(t.TkbType, t.DisplayName));

var spawnerPanel = new SpawnerPanel(catalog);

// Concrete adapter injected from the host shell.
ISpawnController spawnAdapter = new MySpawnAdapter(ecsWorld, mapPickService);

// Per-frame render:
spawnerPanel.Draw(spawnAdapter);
```

### Example 3 -- Unit-testing SharedOrbatPanel drag-and-drop without ImGui

```csharp
// Arrange
var panel = new SharedOrbatPanel();
var ctrl  = Substitute.For<IOrbatController>();

var vehicleNode = new OrbatNodeViewModel(
    EntityId: 42,
    Name: "Leopard 2A7",
    Depth: 0,
    HasChildren: false,
    IsPendingDelete: false,
    CanAcceptSubordinates: false);

// Act -- passenger 7 dropped onto vehicle 42
panel.HandleHierarchyDropPayload(passengerId: 7, targetNode: vehicleNode, ctrl: ctrl);

// Assert -- RequestEmbark is called because CanAcceptSubordinates is false
ctrl.Received(1).RequestEmbark(7, 42);
```

### Example 4 -- Unit-testing ZoneEditorPanel without ImGui

```csharp
// Arrange
var panel = new ZoneEditorPanel
{
    ZoneName       = "northern_sector",
    RoadNetworkPath = "Data/northern_roads.json"
};
var ctrl = Substitute.For<IZoneAuthoringController>();

// Act
panel.HandleApplyRoadNetwork(ctrl);

// Assert
ctrl.Received(1).SetRoadNetworkPath("northern_sector", "Data/northern_roads.json");
```

### Example 5 -- Populating a shared entity context menu

```csharp
// Called inside an ImGui.BeginPopup / EndPopup block in the host shell:
if (ImGui.BeginPopup("EntityCtxMenu"))
{
    var builder = new ContextMenuBuilder();   // from Fdp.Presentation
    SharedContextMenuPopulator.PopulateEntityMenu(
        entityId: selectedEntityId,
        tkbType: selectedEntityTkbType,
        hasEditablePolyline: entity.HasOverlay,
        hasRoutePlan: entity.HasRoute,
        builder: builder,
        actions: entityActionAdapter);
    ImGui.EndPopup();
}
```

### Example 6 -- Async map-pick inside a mission workflow

```csharp
// Inside a panel method that awaits user input (runs on the UI thread):
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    GeoPoint location = await mapPickService.PickLocationAsync(cts.Token);

    var plan = new MissionPlan();
    plan.AddTask(new MoveToLocationTask(location,
        speed: PanelConstants.MoveToLocationDefaultSpeed,
        arrivalRadius: PanelConstants.MoveToLocationDefaultArrivalRadius));

    MissionCommitResult result =
        await missionEditorService.CommitMissionAsync(entityId, plan, currentVersion);

    if (!result.Success)
        ShowAlert(result.ErrorMessage ?? PanelConstants.VersionConflictErrorMessage);
    else
        currentVersion = result.NewVersion;
}
catch (OperationCanceledException)
{
    // Operator cancelled the pick or the 30-second timeout expired.
}
```

---

## Best Practices

### Implement facades as thin adapters

Concrete adapter classes in the host shells should be thin: validate inputs, translate
the call to an ECS command or DDS message, and return.  Do not let business logic leak
into adapters.  All rendering logic and operator interaction state belongs in the panels.

### Never inject ImGui into adapters

Adapters implement the facade interfaces and must not make any ImGui calls.  ImGui calls
are restricted to `DrawContent` / `Draw` methods and to the `Draw` bodies of panels in
this library.  This ensures that unit tests for adapter logic can run headlessly.

### Prefer `DrawContent` over `Draw` in managed windows

When a panel is hosted by a window manager that calls `ImGui.Begin`/`End` externally,
use `DrawContent`.  The standalone `Draw` method that includes `Begin`/`End` exists only
for shells that do not have a window manager layer.

### Keep panel state minimal

Panels hold only transient UI state (text buffers, selection indices, filter strings).
Persistent scenario data lives in the simulation engine and is read each frame through
the facade interfaces, never cached in the panel.

### Thread safety: call from the render thread only

All facade interface implementations are called from the ImGui render thread.
Adapters that need to dispatch to an ECS or network layer should enqueue commands and
return immediately, performing the actual dispatch on the simulation thread.

### Use the `internal Handle*` methods for testing, not `DrawContent`

Avoid driving tests through `DrawContent` as that requires an active ImGui context.
Instead, call the `internal Handle*` methods directly.  The `InternalsVisibleTo`
declaration in `AssemblyInfo.cs` exists for exactly this purpose.

### Optimistic-concurrency: always propagate `NewVersion`

After a successful call to `IMissionEditorService.CommitMissionAsync`, store the
returned `MissionCommitResult.NewVersion` and use it as `baseVersion` in the next call.
Never hard-code `0` as `baseVersion` after the first commit or conflicts will follow.

### Filter rebuild is O(n) in catalog size

`SpawnerPanel.SearchFilter` setter runs `RebuildFilter`, which iterates the full
catalog.  For catalogs with hundreds of entries this is fast enough; for thousands,
consider debouncing the filter text input before assigning to `SearchFilter`.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Hrot.Core` | Domain types consumed by the facade interfaces (`MissionPlan`, `GeoPoint`, `eForceIdentifier`). Direct project reference. |
| `Fdp.Presentation` | Provides `IContextMenuBuilder` and the ImGui framework. Direct project reference. |
| `Hrot.Presentation` | Primary consumer. Hosts all panels, provides concrete adapter implementations, and renders the full scenario editor UI. References `Hrot.UI.Common`. |
| `Hrot.ExCon.Tests` | Test assembly with `InternalsVisibleTo` access. Tests the `internal Handle*` panel methods using mock facades. |
| `Hrot.Presentation.Tests` | Tests for `Hrot.Presentation`; may exercise panel logic through `Hrot.UI.Common` types. |
| `Fdp.Presentation.Tests` | Tests `IContextMenuBuilder` implementations; relevant when verifying `SharedContextMenuPopulator` end-to-end. |
