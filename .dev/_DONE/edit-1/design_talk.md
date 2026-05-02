in the Host.Editor I would like the following features
 - standard entity inspector (as found in SimHost subsystem and IG subsystem)
    - entity context menu shraing context with similar menu when 
 - standard event browser (as found in SimHost subsystem and IG subsystem)
 - map layer & grid control panel sending requests via fdp events
 - 'Entity creator' panel with entity type selector (enumerating all tkb entities) - similat to ExCon is using
 - 'Area creator' panel activating the vertex-sequence placement tool and creating the area entity
 - 'Mission editor' panel as found in ExCon (just using pure FDP events only)
    - including target point clicking from map for the selected behavior (similar to what ExCon is having)
 - entity symbol rendering (graphicla representation and text label)
    - kinamit entities displayed as the simHost is doing, just with entity id label
 - map pan/zoom (same as IG is using)
 - entity drag & drop (as IG is using)
 - entity select/multi-select (rectangle secletion)
 - entity context menu offering
    - 'edit' for area entities and route entities
    - 'edit route' to start clicking waypoints of a personal route for kinematic entities
    - 'delete' sending the deletion request FDP event 
    - 'rename' that shows renaming dialog and send request for the name property change
 - empty map space context menu offering
   - 'measurement' tool
 - 'Route creator' that activates the route waypoint clicking tool
 - 'Preview' panel allowing to switch into Preview mode and back.


 It is nothing new, all the same stuff as already existing elsewhere. Just because they are used in the Hrot.Editor , they should all use just the fdp event bus communication. We should not duplicate the code. As the feature already exist, the Hrot.Editor should just be reusing most of them. Maybe this should result in creating some shared library of UI panels (ImGui based of course) with clear non-DDS programmatic APIs so they can be used by all the ExCon and the IG and the Htor.Editor and whatever else subsystem (to stay DRY as much as possible).

Pls suggest the approach.

Your instinct is perfectly aligned with Clean Architecture. Duplicating the ImGui panels from ExCon into the HROT Editor would violate the DRY principle and create a maintenance nightmare.

Because the HROT Editor operates entirely offline using pure ECS memory and the `FdpEventBus`, while ExCon operates over the network using `DerRepo` and CycloneDDS, we cannot share the panels if they are tightly coupled to their concrete application logic.

The correct approach is to apply the **Dependency Inversion Principle (DIP)** and the **Interface Segregation Principle (ISP)** to create a shared UI library.

Here is the architectural blueprint to achieve this without coupling the subsystems:

1\. Extract a Shared UI Library (`Hrot.UI.Common`)

Create a new project that contains the ImGui rendering logic for the Spawner, Mission Editor, and Map Layer configurations. This library must have **zero dependencies** on CycloneDDS, `DerRepo`, or `IExConLogic`.

Instead, the panels will depend on highly focused capability interfaces (Facades).

```
// Hrot.UI.Common/Facades/ISpawnController.cs
public interface ISpawnController 
{
    void StartPlacementMode(long tkbType, string initialPropertiesJson);
    void StartAreaAuthoringMode();
    void StartRouteAuthoringMode();
}

// Hrot.UI.Common/Facades/IMissionController.cs
public interface IMissionController
{
    MissionPlan? GetMissionPlan(int entityId);
    void CommitMission(int entityId, MissionPlan plan);
    // ... jump, abort, etc.
}
```

You then refactor `MissionPanel.cs` and `SpawnerPanel.cs` (currently in `Hrot.ExCon`) to consume these interfaces instead of `IExConLogic`.

2\. Implement the Facade Adapters

Both ExCon and the HROT Editor will implement these interfaces using their own domain-specific mechanisms, completely shielding the shared UI from their internal messaging.

-   **In ExCon:** The `SpawnControllerAdapter` translates `StartPlacementMode` into a DDS `CreateEntityRequest` or `MapCommandRequest`.-   **In Hrot.Editor:** The `SpawnControllerAdapter` translates the exact same call into pushing a `CreationTool` onto the local `MapCanvas`, or publishing a pure `SpawnEntityCommand` to the `FdpEventBus`.

3\. Leverage Existing FDP Framework Panels

You requested the standard entity inspector and event browser. You do not need to build these. `FDP.Toolkit.ImGui` already provides them, fully decoupled from the simulation loop:

-   **Entity Inspector:** Instantiate `FDP.Toolkit.ImGui.Panels.EntityInspectorPanel`. You simply pass it the `FdpRepositoryAdapter` wrapping the Editor's `EntityRepository`.-   **Event Browser:** Instantiate `FDP.Toolkit.ImGui.Panels.EventBrowserPanel` and feed it the Editor's `FdpEventBus` each frame.

4\. Map Interaction (Pan, Zoom, Select, Drag & Drop)

To support panning, zooming, selecting, and drag-and-drop, you do not need to write UI panel code. These mechanics are strictly handled by the `FDP.Toolkit.Vis2D` and `Hrot.ScenarioEditor.Tools` packages.

In `Hrot.Editor`, you must instantiate a `MapCanvas` (which internally provides the `MapCamera` for pan/zoom) and push the `StandardInteractionTool` onto it. That tool natively evaluates mouse coordinates against ECS entities carrying a `SimTransform`, handling selection and drag-and-drop interactions at zero allocation cost.

5\. Unifying Context Menus

For context menus (edit route, edit area, delete, rename, measure), use the existing `LambdaEntityContextMenuHandler` provided by the ImGui toolkit.

In the HROT Editor's composition root, register these actions directly against the FDP Entity Inspector:

```
_inspectorPanel.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
{
    builder.AddItem("Rename", () => OpenRenameDialog(entity));
    
    if (world.HasManagedComponent<RoutePlan>(entity))
        builder.AddItem("Edit Route", () => _canvas.PushTool(new RouteEditTool(entity, ...))); [19]

    builder.AddSeparator();
    builder.AddItem("Delete", () => 
        _eventBus.PublishManaged(new DestroyEntityCommand { NetworkId = ... }) [20]
    );
}));
```

Summary of the Refactor

-   Move the data-driven ImGui panels (Mission, Spawner, Config) from `Hrot.ExCon` to `Hrot.UI.Common`.-   Replace their `IExConLogic` dependencies with `ISpawnController`, `IMissionController`, etc.-   In `Hrot.Editor`, implement those interfaces using `FdpEventBus` and `EntityRepository`.-   Register the existing `FdpEntityInspectorPanel` and `FdpEventBrowserPanel` in the Editor's window manager.-   Embed a `MapCanvas` in the Editor and assign it the `StandardInteractionTool` to gain Pan/Zoom and Drag/Drop.

let's detail the ImGui panel library refactor please

To execute this refactor cleanly, we must apply the **Interface Segregation Principle (ISP)** and the **Dependency Inversion Principle (DIP)**. We will extract the data-driven ImGui panels into a new `Hrot.UI.Common` library.

Currently, panels like `SpawnerPanel` and `MissionPanel` are tightly coupled to the `IExConLogic` "god interface". By replacing this dependency with highly focused, capability-based interfaces (Ports) and implementing environment-specific Adapters in the host applications, we achieve perfect reuse without polluting our offline editor with CycloneDDS network concepts.

Here is the architectural blueprint for the UI library refactor.

1\. The Shared UI Library Contract (`Hrot.UI.Common`)

This new project will reference `FDP.Toolkit.ImGui`, `FDP.Toolkit.DER`, and `Hrot.NED` (strictly for shared enums and definitions, not messaging). It will **not** reference `Hrot.ExCon`, `CycloneDDS`, or `ModuleHost`.

Because `FDP.Toolkit.DER` provides the transport-agnostic `IDerRepo` interface, our panels can continue to use it as their pure CQRS Read-Model. For the Write-Model (commands and intents), we define segregated capability interfaces:

```
// Hrot.UI.Common/Facades/ISpawnController.cs
public interface ISpawnController 
{
    void StartPlacementMode(long tkbType, string? initialPropertiesJson = null);
    void StartAreaAuthoringMode(string styleOverrideJson = "");
    void StartRouteAuthoringMode();
}

// Hrot.UI.Common/Facades/IMissionController.cs
public interface IMissionController
{
    Task<MissionCommitResult> CommitMissionAsync(long entityId, MissionPlan plan, long baseVersion);
    void SendControlCommand(long entityId, eMissionCommandType type, Guid taskId);
    
    // Map picking promises
    Task<GeoPoint> PickLocationAsync();
    Task<int> PickEntityAsync(string[]? filterPresets = null);
}

// Hrot.UI.Common/Facades/IMapInteractionController.cs
public interface IMapInteractionController
{
    void SelectEntity(int entityId);
    void CenterOnEntity(int entityId);
    void DeleteEntity(int entityId);
    bool IsEntityPendingDelete(int entityId);
}
```

2\. Refactoring the Panels (The Consumers)

We migrate the panels from `Hrot.ExCon/Panels/` into `Hrot.UI.Common/Panels/`. We then strip out all references to `IExConLogic` and replace them with our new segregated interfaces.

For example, the `SpawnerPanel` currently takes `IExConLogic` to trigger spawns. We refactor its `DrawContent` and handler methods:

```
// Hrot.UI.Common/Panels/SpawnerPanel.cs
public void HandleActivatePlacementTool(ISpawnController spawnController)
{
    var patch = new EntityPropertyPatch { Affiliation = _affiliation };
    var propsJson = JsonConvert.SerializeObject(patch, ...);
    
    // The panel no longer knows if this sends a DDS MapCommandRequest 
    // or pushes a local Raylib tool!
    spawnController.StartPlacementMode(_selectedType, propsJson);
}

public void DrawContent(ISpawnController spawnCtrl)
{
    // ... UI rendering ...
    if (ImGui.Button("ACTIVATE PLACEMENT TOOL"))
        HandleActivatePlacementTool(spawnCtrl);
}
```

3\. The ExCon Adapters (Networked Operations)

In the `Hrot.ExCon` subsystem, `ExConLogic` already implements these exact method signatures. We simply declare that `ExConLogic` implements our new interfaces.

```
// Hrot.ExCon/ExConLogic.cs
public sealed class ExConLogic : IExConLogic, IMapPickService, IDisposable, 
                                 ISpawnController, IMissionController, IMapInteractionController
{
    // Existing implementations remain untouched. They continue to build 
    // MapCommandRequest and MissionControlRequest DDS messages.
}
```

When constructing the `ExConMock` UI shell, we pass `ExConLogic` into the panels, seamlessly fulfilling the new interface contracts.

4\. The Editor Adapters (Offline Operations)

In the standalone `Hrot.Editor` subsystem, we implement these interfaces as pure memory-bus adapters. They translate the panel requests into direct ECS manipulations and `FdpEventBus` domain events.

```
// Hrot.Editor/Adapters/EditorSpawnAdapter.cs
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using Hrot.ScenarioEditor.Tools;

public class EditorSpawnAdapter : ISpawnController
{
    private readonly MapCanvas _canvas;
    private readonly FdpEventBus _bus;

    public EditorSpawnAdapter(MapCanvas canvas, FdpEventBus bus)
    {
        _canvas = canvas;
        _bus = bus;
    }

    public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null)
    {
        // Instead of a DDS request, we directly push the CreationTool onto the local canvas
        var tool = new CreationTool(
            onEntityCreated: cmd => _bus.PublishManaged(cmd), 
            tkbType: tkbType,
            initialPropertiesJson: initialPropertiesJson,
            autoPopOnPlace: true);
            
        _canvas.PushTool(tool);
    }

    public void StartAreaAuthoringMode(string styleOverrideJson = "")
    {
        // Direct local tool push
        var tool = new EditTool(...); 
        _canvas.PushTool(tool);
    }
    
    // ... StartRouteAuthoringMode ...
}
```

5\. Assembling the Editor UI (Composition Root)

Finally, in the `Hrot.Editor` composition root (`Program.cs` or `EditorApplication`), we instantiate the shared panels and supply them with our offline adapters and the standard FDP framework panels.

```
// 1. Instantiate the shared UI panels
var spawnerPanel = new SpawnerPanel(tkbCatalog);
var orbatPanel = new OrbatPanel(tkbCatalog);

// 2. Instantiate our Editor-specific adapters
var spawnAdapter = new EditorSpawnAdapter(mapCanvas, world.Bus);
var interactionAdapter = new EditorInteractionAdapter(world.Bus, editorLogic);

// 3. Register everything with the WindowManager
windowManager.RegisterWindow(new ManagedSpawnerWindow(spawnerPanel, spawnAdapter));
windowManager.RegisterWindow(new ManagedOrbatWindow(orbatPanel, interactionAdapter, spawnAdapter, editorLogic.View));

// 4. Register the existing decoupled FDP panels
windowManager.RegisterWindow(new FdpEntityInspectorWindow(
    "editor_inspector", "Entity Inspector", "Editor",
    new FdpEntityInspectorPanel(), 
    () => new FdpRepositoryAdapter(world), 
    () => inspectorState));
```

**Why this is the perfect Clean Architecture solution:**

-   **DRY:** The complex ImGui rendering logic for Missions, Orbat, and Spawning is written exactly once.-   **Agnostic UI:** The `Hrot.UI.Common` panels do not know if they are controlling a distributed cluster via CycloneDDS or a monolithic memory space via Raylib.-   **Strict Boundaries:** The `Hrot.Editor` remains entirely offline and free of `Hrot.NED` network translators, preserving memory-bus speeds for local scenario authoring.

let's detail the entity context menu related implmentation (so it can be shared by entity inspector and possibly by other places where entity list is shown)

To keep our UI architecture DRY and strictly decoupled from CycloneDDS, we will encapsulate the entity context menu logic into a single, shared handler that implements the framework's existing `IEntityContextMenuHandler` interface.

Because this interface relies solely on the `Entity` struct and an `IContextMenuBuilder` abstraction, we can bind it once and reuse it across the `FdpEntityInspectorPanel`, the Map Canvas, and the ORBAT tree.

Here is the clean architecture implementation for the Editor's context menus.

1\. The Shared Entity Context Menu Handler

We create a dedicated handler class injected with `IEditorLogic` and the `EntityRepository`. It evaluates the entity's components to determine which menu items are applicable, completely isolating the ImGui rendering from the domain logic.

```
using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Hrot.Editor;

namespace Hrot.Editor.UI
{
    public class EditorEntityContextMenuHandler : IEntityContextMenuHandler
    {
        private readonly EntityRepository _repo;
        private readonly IEditorLogic _logic;
        private readonly FdpEventBus _bus;

        public EditorEntityContextMenuHandler(EntityRepository repo, IEditorLogic logic, FdpEventBus bus)
        {
            _repo = repo;
            _logic = logic;
            _bus = bus;
        }

        public void PopulateMenu(Entity entity, IContextMenuBuilder builder)
        {
            if (!_repo.IsAlive(entity)) return;

            long networkId = _repo.HasComponent<NetworkIdentity>(entity) 
                ? _repo.GetComponentRO<NetworkIdentity>(entity).Value 
                : 0;

            // 1. Rename Entity
            if (networkId != 0 && _repo.HasUnmanagedComponent<EntityInfo>(entity))
            {
                builder.AddItem("Rename...", () => 
                {
                    // Triggers a UI state flag to open a rename dialog modal in the next ImGui frame
                    _logic.OpenRenameDialog(networkId); 
                });
            }

            // 2. Edit Area/Shape (Requires EditablePolyline)
            if (_repo.HasManagedComponent<EditablePolyline>(entity))
            {
                builder.AddItem("Edit Shape", () => 
                {
                    _logic.SelectEntity(entity);
                    _logic.ActivateTool(EditorTool.Edit);
                });
            }

            // 3. Edit Route / Personal Route
            // For standalone route graphics or vehicles possessing a personal route reference
            if (_repo.HasManagedComponent<RoutePlan>(entity) || _repo.HasComponent<PersonalRouteRef>(entity))
            {
                builder.AddItem("Edit Route", () => 
                {
                    _logic.SelectEntity(entity);
                    _logic.ActivateTool(EditorTool.Route);
                });
            }

            builder.AddSeparator();

            // 4. Delete Entity
            builder.AddItem("Delete", () =>
            {
                if (networkId != 0)
                {
                    // Pure FDP domain event. The NetworkSpawningSystem intercepts this and tears down the entity cleanly.
                    _bus.PublishManaged(new DestroyEntityCommand 
                    { 
                        NetworkId = networkId, 
                        Reason = "editor_ui_context_menu" 
                    });
                }
                else
                {
                    // Fallback for local-only un-networked entities
                    _repo.DestroyEntity(entity);
                }
            });
        }
    }
}
```

2\. Wiring to the Entity Inspector

The framework's `EntityInspectorPanel` already natively supports context menu handlers. In your Editor's composition root, simply instantiate the shared handler and register it.

```
var sharedContextMenuHandler = new EditorEntityContextMenuHandler(world, editorLogic, world.Bus);

// Register with the standard FDP Entity Inspector
_fdpEntityInspectorPanel.RegisterContextMenuHandler(sharedContextMenuHandler);
```

3\. Reusing the Handler in the ORBAT Panel

If you build an `EditorOrbatPanel` displaying entities in a hierarchy, you can invoke the exact same handler when the user right-clicks a node. The framework provides the `ContextMenuBuilder` specifically for this dynamic building.

```
// Inside EditorOrbatPanel.DrawContent()
if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
{
    ImGui.OpenPopup($"OrbatCtx_{entity.Index}");
}

if (ImGui.BeginPopup($"OrbatCtx_{entity.Index}"))
{
    // Reuse the exact same menu logic!
    var builder = new FDP.Toolkit.ImGui.Utils.ContextMenuBuilder();
    _sharedContextMenuHandler.PopulateMenu(entity, builder);
    ImGui.EndPopup();
}
```

4\. The Empty Map Space Context Menu

To satisfy the requirement for an empty-space context menu (offering the Measurement tool), we hook into the `StandardInteractionTool`'s `OnWorldClick` event. This tool already evaluates the hit-stack.

Because ImGui popups must be rendered during the ImGui frame (not the Raylib input phase), we cache the click state and draw the menu in the UI pass.

```
// 1. In the Editor Application setup, subscribe to world clicks:
_interactionTool.OnWorldClick += (pos, button, shift, ctrl, hitEntity) =>
{
    if (button == Raylib_cs.MouseButton.Right && hitEntity == Entity.Null)
    {
        // Flag the UI layer to open the map context menu next frame
        _uiState.RequestMapContextMenu = true;
    }
};

// 2. In the Editor's main UI render loop (DrawUI):
if (_uiState.RequestMapContextMenu)
{
    ImGui.OpenPopup("EmptyMapContextMenu");
    _uiState.RequestMapContextMenu = false;
}

if (ImGui.BeginPopup("EmptyMapContextMenu"))
{
    if (ImGui.MenuItem("Measurement Tool"))
    {
        _editorLogic.ActivateTool(EditorTool.Measure);
    }
    ImGui.EndPopup();
}
```

Handling the Rename Dialog Flow

The `Rename...` menu item triggers `_logic.OpenRenameDialog(networkId)`. This should set a flag in your editor's UI state to render a modal popup via `ImGui.BeginPopupModal`.

When the user types the new name and clicks "Apply" in that modal, you fulfill the anti-corruption boundary requirement by constructing a fresh component and routing it through the standard `CommitPropertyEdit` facade:

```
// When the user submits the rename dialog:
var updatedInfo = new Hrot.IG.Components.EntityInfo 
{ 
    Name = newNameInput, 
    ForceId = existingAffiliation // preserve existing
};

_editorLogic.CommitPropertyEdit(
    targetNetworkId, 
    new List<object> { updatedInfo }
);
```

The `UpdateEntityCommandEgressTranslator` or local simulation logic will then intercept this edit and apply it cleanly to the ECS world.

pls detail how the point picking tool is triggered from the mission editor panel when the behavior requires target point coordinates

When the operator selects a behavior that requires spatial coordinates, such as `MoveToLocation`, the `MissionPanel` cleanly abstracts away raw JSON editing by rendering a dedicated "Pick Location" button. The flow to capture this coordinate relies on a strictly decoupled, asynchronous request-response cycle using the Task-based Asynchronous Pattern (TAP) and CQRS principles.

Here is exactly how the architecture handles the spatial pick:

**1\. The UI Intent (****MissionPanel****)** When the operator clicks "Pick Location", the panel invokes `HandlePickLocation`. This method asks the injected application facade for a location by calling `logic.MapPickService.PickLocationAsync()` and stores the resulting `Task` in a pending state field without blocking the UI thread.

**2\. The Network Boundary (****ExConLogic****)** The `ExConLogic` implements `IMapPickService`. When called, it generates a unique `ActiveContextId`, caches a `TaskCompletionSource<GeoPoint>`, and publishes a `MapCommandRequest` of type `CMD_PICK_LOCATION` over the DDS network to the map owner (e.g., the IG).

**3\. Tool Activation (****IgApplication** **&** **LocationPickerTool****)** The Image Generator receives the `CMD_PICK_LOCATION` command and parses the payload. It extracts the context ID and pushes the `LocationPickerTool` onto its `MapCanvas` tool stack. This tool takes over the map's input pipeline, rendering a lightweight crosshair cursor without any per-frame heap allocations.

**4\. Operator Interaction** When the operator left-clicks on the canvas, the `LocationPickerTool` pops itself from the stack and fires an event containing the world-space coordinates. The IG translates this click into geodetic coordinates and publishes a `MapClickEvent` back to the DDS wire, stamping it with the exact `InteractionContextId` provided in the initial command.

**5\. Resolution & Data Binding** Back in the ExCon, the ingress handler queues the `MapClickEvent`. The `ExConLogic` processes the queue, validates that the `InteractionContextId` matches the currently active pick session (dropping any stale clicks), and sets the result on the pending `TaskCompletionSource`.

Meanwhile, the `MissionPanel` evaluates `PollPickCompletion()` every UI frame. Once it observes that the pending `Task` has successfully completed, it extracts the `GeoPoint`, builds the required JSON parameters string (e.g., `{"targetLat":..., "targetLon":...}`), and applies the update to the draft mission task.

This architecture guarantees that our UI rendering remains strictly decoupled from network messaging and interaction state machines, maintaining a highly responsive and allocation-free hot path.

no the mission editor is part Hrot,Editor and in this mode we do not use DDS at all. I need you you to design how this will work in the Hrot.Editor, not how it works in ExCon now

You are absolutely right, apologies for falling back into the ExCon network topology. In the standalone `Hrot.Editor`, we have zero CycloneDDS overhead and direct access to local memory. We must keep the architecture purely local, synchronous, and allocation-free on the hot path while still satisfying the asynchronous contract demanded by the shared UI library.

Because we are decoupling the `MissionPanel` into `Hrot.UI.Common`, the panel still relies on the segregated `IMapPickService` interface. However, instead of building a DDS `MapCommandRequest`, the `Hrot.Editor` will implement this interface using a direct adapter that pushes the existing `LocationPickerTool` onto the local `MapCanvas` and wraps the C# events into a `Task`.

Here is the clean, offline architecture for the `Hrot.Editor`:

1\. The Shared Interface (Hrot.UI.Common)

The UI library defines the capability. It knows nothing about networks or local canvases.

```
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Common;

namespace Hrot.UI.Common.Facades
{
    public interface IMapPickService
    {
        Task<GeoPoint> PickLocationAsync(CancellationToken ct = default);
        Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default);
    }
}
```

2\. The Offline Adapter (`EditorMapPickAdapter`)

In the `Hrot.Editor` project, we implement this interface. When the UI requests a location, we instantiate the framework's native `LocationPickerTool`. We wire its standard C# events directly to a `TaskCompletionSource`, and push it onto the editor's local `MapCanvas`.

Because missions require geodetic coordinates (`targetLat` / `targetLon`) but the `LocationPickerTool` returns a Cartesian `Vector2` representing the 2D canvas click, the adapter also performs the necessary coordinate conversion.

```
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Tools;
using Hrot.NED.Common;
using Hrot.UI.Common.Facades;

namespace Hrot.Editor.Adapters
{
    public class EditorMapPickAdapter : IMapPickService
    {
        private readonly MapCanvas _canvas;
        private readonly IGeographicTransform _geoTransform;

        public EditorMapPickAdapter(MapCanvas canvas, IGeographicTransform geoTransform)
        {
            _canvas = canvas;
            _geoTransform = geoTransform;
        }

        public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<GeoPoint>(TaskCreationOptions.RunContinuationsAsynchronously);

            // 1. Instantiate the native Vis2D tool
            var tool = new LocationPickerTool();

            // 2. Wire the success callback to resolve the Task
            tool.OnLocationPicked += worldPos =>
            {
                // The canvas operates in XZ (X = east, Y = north = world Z) [3].
                // Convert the Cartesian click to WGS-84 geodetic.
                var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(worldPos.X, worldPos.Y, 0f));
                
                tcs.TrySetResult(new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt });
            };

            // 3. Wire the cancellation callback (Right-click or ESC) [2]
            tool.OnCancelled += () =>
            {
                tcs.TrySetCanceled();
            };

            // Register cancellation token to pop the tool if the UI aborts the request
            if (ct.CanBeCanceled)
            {
                ct.Register(() => 
                {
                    if (_canvas.ActiveTool == tool) _canvas.PopTool();
                    tcs.TrySetCanceled();
                });
            }

            // 4. Push the tool to the local canvas. It takes over the input pipeline instantly.
            _canvas.PushTool(tool);

            return tcs.Task;
        }

        public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default)
        {
            // Similar implementation using FDP.Toolkit.Vis2D.Tools.EntityPickerTool [4]
            // ...
        }
    }
}
```

3\. The UI Consumption (Mission Panel)

Inside `Hrot.UI.Common.Panels.MissionPanel`, the operator clicks "Pick Location". The panel kicks off the async flow, polling the `Task` completion just as it did before.

```
// Inside MissionPanel.cs
public void HandlePickLocation(int index, IEditorLogic logic)
{
    // The panel doesn't know if this sends a DDS MapCommandRequest (ExCon)
    // or pushes a Raylib tool onto the local canvas (Hrot.Editor).
    _pendingPickTaskIndex = index;
    _pendingLocationPick  = logic.MapPickService.PickLocationAsync();
}

private void PollPickCompletion()
{
    if (_pendingLocationPick?.IsCompleted == true)
    {
        if (!_pendingLocationPick.IsFaulted && !_pendingLocationPick.IsCanceled)
        {
            var pos = _pendingLocationPick.Result;
            string json = BuildMoveToLocationParams(pos.Latitude, pos.Longitude);
            HandleEditBehaviorParams(_pendingPickTaskIndex, json);
        }
        _pendingLocationPick = null;
    }
}
```

Architectural Benefits:

-   **Zero Network Pollution:** The `Hrot.Editor` operates entirely at memory-bus speeds without publishing or polling `MapClickEvent` or `MapCommandRequest` DDS topics.-   **Perfect DRY UI:** The `MissionPanel` code is completely identical for both the distributed ExCon subsystem and the offline Editor.-   **Immediate Interaction:** By directly pushing the `LocationPickerTool` to the `MapCanvas`, the user's cursor instantly turns into a crosshair without any network-hop latency.

The Hrot.Editor ORBAT panel should also work with FDP only stuff. But it should not tound the ECS directly. Because it is a shared component for ExCon as well as for Hrot.Editor, its interface must be independent on both DDS and FDP i guess.

You are absolutely correct. To perfectly adhere to the Dependency Inversion Principle (DIP) and make the ORBAT panel a truly shared, reusable component in `Hrot.UI.Common`, it must be strictly decoupled from both the FDP ECS kernel (`EntityRepository`) and the CycloneDDS network layer (`IDerRepo` and `Hrot.NED.Descriptors`).

Currently, the codebase violates this boundary. The ExCon's `OrbatPanel` is tightly coupled to `IExConLogic` and physically extracts the `Hrot.NED.Descriptors.EntityInfo` descriptor to read the `CommanderId` for tree building. Meanwhile, the `Hrot.Editor` has its own isolated, simplistic `EditorOrbatPanel` that just lists entities without any hierarchical tree logic.

To achieve a pristine Clean Architecture where the UI is DRY and completely infrastructure-ignorant, we must implement the **Model-View-Presenter (MVP) / Adapter pattern**. We will segregate the UI's needs into pure Read (Queries) and Write (Commands) interfaces.

Here is the exact architectural blueprint to refactor the ORBAT panel:

1\. The Pure UI View Model (`Hrot.UI.Common`)

First, we define a pure C# DTO that the ImGui panel will consume. This replaces the panel's current reliance on network/ECS types.

```
namespace Hrot.UI.Common.Models
{
    public sealed record OrbatNodeViewModel(
        int EntityId,
        string Name,
        int Depth,
        bool HasChildren,
        bool IsPendingDelete
    );
}
```

2\. The Segregated UI Interfaces (`Hrot.UI.Common`)

The shared ImGui panel will depend ONLY on these two interfaces. It will not know what an ECS `Entity` or an `IDerEntity` is.

```
namespace Hrot.UI.Common.Facades
{
    // The Query (Read Model)
    public interface IOrbatDataProvider
    {
        IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(string filterText, HashSet<int> expandedNodes);
    }

    // The Command (Write Model)
    public interface IOrbatController
    {
        void SelectEntity(int entityId);
        void CreateUnit(long tkbType);
        void ToggleExpanded(int entityId);
    }
}
```

3\. The Shared ImGui Panel (`Hrot.UI.Common`)

We move the complex tree-rendering logic out of `Hrot.ExCon` and into the shared UI library. The panel now injects the facades.

```
namespace Hrot.UI.Common.Panels
{
    public sealed class SharedOrbatPanel
    {
        private readonly HashSet<int> _expandedNodes = new();
        private string _filterText = string.Empty;
        
        public void DrawContent(IOrbatDataProvider dataProvider, IOrbatController controller)
        {
            // The panel is totally ignorant of WHERE this data comes from
            var nodes = dataProvider.GetVisibleNodes(_filterText, _expandedNodes);

            foreach (var node in nodes)
            {
                // ... Render tree indents using node.Depth ...
                if (ImGui.Selectable(node.Name)) 
                {
                    controller.SelectEntity(node.EntityId);
                }
            }
        }
    }
}
```

4\. The ExCon Adapter (DDS / NED Network Layer)

In the `Hrot.ExCon` project, we implement the data provider by querying the `IDerRepo`. This adapter handles the `Hrot.NED` knowledge.

```
public class ExConOrbatDataProvider : IOrbatDataProvider
{
    private readonly IDerRepo _repo;

    public ExConOrbatDataProvider(IDerRepo repo) => _repo = repo;

    public IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(string filterText, HashSet<int> expandedNodes)
    {
        // 1. Scan _repo.GetAllEntities()
        // 2. Read Hrot.NED.Descriptors.EntityInfo.CommanderId
        // 3. Build tree and map to OrbatNodeViewModel
        // 4. Return list
    }
}
```

5\. The Editor Adapter (Offline FDP ECS Layer)

In the standalone `Hrot.Editor` project, we implement the exact same interface using local memory-bus speeds. We query the `EntityRepository` and use the FDP-native `VisHierarchyNode` and `Hrot.IG.Components.EntityInfo` to build the tree.

```
public class EditorOrbatDataProvider : IOrbatDataProvider
{
    private readonly EntityRepository _world;

    public EditorOrbatDataProvider(EntityRepository world) => _world = world;

    public IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(string filterText, HashSet<int> expandedNodes)
    {
        // 1. var query = _world.Query().With<VisHierarchyNode>().With<EntityInfo>().Build();
        // 2. Resolve parent-child structure at memory-bus speeds
        // 3. Map to OrbatNodeViewModel
        // 4. Return list
    }
}
```

Why this is the ultimate Clean Architecture solution:

-   **Total Decoupling:** The UI logic in `SharedOrbatPanel` does not reference `IDerRepo`, `EntityRepository`, `Hrot.NED.Descriptors`, or CycloneDDS.-   **Perfect DRY:** The complex ImGui rendering code, recursive tree traversal safeguards, and filtering logic are written exactly once.-   **Safe Composition:** The `Hrot.Editor` stays perfectly offline. It simply injects the `EditorOrbatDataProvider` and `EditorOrbatController` (which routes intents to the `FdpEventBus`) directly into the shared panel upon startup.

the Hrot.Editor's mission control panel a;so needs to be fully decoupled, correct? How?

You are absolutely correct. To maintain a pristine Clean Architecture, the Mission Control panel must be completely decoupled from its current CycloneDDS dependencies so it can operate identically in the distributed `ExCon` node and the offline `Hrot.Editor` monolith.

The beauty of the recent `PACK-P001` refactoring is that the engine-side `MissionControlExecutionSystem` is already a pure-ECS, DDS-ignorant system. It consumes `MissionControlIntent` events from the `FdpEventBus` and publishes `MissionControlAckEvent` events. This means the offline editor can reuse the exact same backend execution logic natively.

Here is how we decouple the UI using the Dependency Inversion Principle (DIP):

1\. Extract the Mission Service Contract

First, we must lift `IMissionEditorService` and `MissionCommitResult` out of `Hrot.ExCon.Services` and place them in the `Hrot.UI.Common.Facades` library alongside the `MissionPanel`. This interface deals strictly with domain objects (`MissionPlan`, `eMissionCommandType`) and knows nothing about networking or ECS storage.

```
namespace Hrot.UI.Common.Facades
{
    public interface IMissionEditorService
    {
        (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId);
        Task<MissionCommitResult> CommitMissionAsync(long entityId, MissionPlan newPlan, long baseVersion);
        Task<MissionCommitResult> SendControlCommandAsync(long entityId, eMissionCommandType type, Guid taskId);
    }
}
```

2\. Implement the Offline Editor Adapter

In the `Hrot.Editor` project, we implement this interface using a local adapter. Because the `Hrot.Editor` runs the FDP `ModuleHostKernel` internally, we bypass CycloneDDS entirely and interact directly with the memory-bus.

```
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Kernel;
using Hrot.Common.Events;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using FDP.Toolkit.Behavior.Components;
using Hrot.UI.Common.Facades;

namespace Hrot.Editor.Adapters
{
    public class EditorMissionService : IMissionEditorService
    {
        private readonly FdpEventBus _bus;
        private readonly EntityRepository _repo;
        private readonly Dictionary<Guid, TaskCompletionSource<MissionCommitResult>> _pendingCommits = new();

        public EditorMissionService(FdpEventBus bus, EntityRepository repo)
        {
            _bus = bus;
            _repo = repo;
        }

        public (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId)
        {
            // Directly query the local ECS world at memory-bus speeds
            var entity = _repo.GetEntityByIndex((int)entityId); // Assuming index-based lookup or NetworkEntityMap
            
            if (_repo.IsAlive(entity) && _repo.HasManagedComponent<ActiveMissionPlan>(entity))
            {
                var activePlan = _repo.GetManagedComponentRO<ActiveMissionPlan>(entity);
                // Map DomainMissionPlan to UI MissionPlan
                return (MapToUiPlan(activePlan.Plan), 0); // Offline editors don't need OCC versioning
            }
            return (null, 0);
        }

        public Task<MissionCommitResult> CommitMissionAsync(long entityId, MissionPlan newPlan, long baseVersion)
        {
            var requestId = Guid.NewGuid();
            var tcs = new TaskCompletionSource<MissionCommitResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingCommits[requestId] = tcs;

            // Publish the pure domain event directly to the local FDP Event Bus
            _bus.PublishManaged(new MissionControlIntent
            {
                RequestId      = requestId,
                TargetEntityId = entityId,
                BaseVersion    = baseVersion,
                Payload        = new MissionCommandUnion 
                { 
                    _d = eMissionCommandType.CMD_REPLACE_MISSION, 
                    FullMissionData = newPlan 
                }
            });

            return tcs.Task;
        }
        
        // Polled each frame by the Editor's main update loop
        public void PollAcks()
        {
            // The local MissionControlExecutionSystem processes the intent and fires this ACK
            foreach (var ack in _bus.Consume<MissionControlAckEvent>())
            {
                if (_pendingCommits.Remove(ack.RequestId, out var tcs))
                {
                    tcs.TrySetResult(new MissionCommitResult 
                    { 
                        Success = ack.ErrorCode == 0, 
                        NewVersion = ack.NewVersion 
                    });
                }
            }
        }
    }
}
```

3\. The Composition Root (Wiring it together)

When starting `Hrot.Editor`, you ensure the `MissionControlExecutionSystem` is registered into your local kernel group.

In the Editor's application loop, the execution flow handles itself natively:

-   The operator edits a mission in the shared `MissionPanel` and clicks "Commit".-   The panel calls `EditorMissionService.CommitMissionAsync()`, which drops a `MissionControlIntent` onto the local event bus.-   The kernel ticks. `MissionControlExecutionSystem` consumes the intent, validates it, modifies the entity's `ActiveMissionPlan` component in the local ECS, and publishes a `MissionControlAckEvent`.-   The `EditorMissionService.PollAcks()` catches the ACK and completes the pending `Task`, resolving the UI state instantaneously.

By routing strictly through the `FdpEventBus` and the `IMissionEditorService` abstraction, the shared `MissionPanel` code remains absolutely identical for both the distributed `ExCon` application and the offline `Hrot.Editor` application, fully satisfying the DRY principle.

together with the offline adapter we for sure need NED adater, correct? For both the ORBAT and Mission Enditor

Exactly. You have perfectly identified the final piece of the **Ports and Adapters (Hexagonal Architecture)** pattern.

To keep the UI panels in `Hrot.UI.Common` entirely ignorant of the underlying infrastructure, we must provide implementations (Adapters) for the networked ExCon subsystem that speak the CycloneDDS/NED language, just as we provide offline adapters for the HROT Editor.

Because ExCon is a lightweight C2 node without an FDP simulation kernel, it maintains its world state in the `DerRepo` (Domain Entity Repository). Therefore, the NED adapters will use `IDerRepo` for their Read-Model and DDS Writers (or Egress Translators) for their Write-Model.

Here is how the NED adapters for both ORBAT and Mission Editor are cleanly implemented for ExCon.

1\. The ORBAT NED Adapter (`ExConOrbatAdapter`)

In ExCon, the Read-Model queries the thread-safe `IDerRepo` which is constantly hydrated by DDS ingress handlers (like `EntityInfoIngressTranslator` equivalent for DER). The Write-Model dispatches commands via `IExConLogic`.

```
using System.Collections.Generic;
using System.Linq;
using FDP.Toolkit.DER;
using Hrot.ExCon;
using Hrot.NED.Descriptors;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.ExCon.Adapters
{
    public class ExConOrbatAdapter : IOrbatDataProvider, IOrbatController
    {
        private readonly IDerRepo _repo;
        private readonly IExConLogic _logic;

        public ExConOrbatAdapter(IDerRepo repo, IExConLogic logic)
        {
            _repo = repo;
            _logic = logic;
        }

        // --- READ MODEL (Querying the DER Replica) ---
        public IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(string filterText, HashSet<int> expandedNodes)
        {
            var result = new List<OrbatNodeViewModel>();
            
            // O(n) pass to build parent-child lookup from NED descriptors
            var childrenLookup = new Dictionary<int, List<IDerEntity>>();
            foreach (var entity in _repo.GetAllEntities())
            {
                if (!entity.HasDescriptor<EntityInfo>()) continue;
                var info = entity.GetDescriptor<EntityInfo>();
                
                if (!childrenLookup.TryGetValue(info.CommanderId, out var siblings))
                {
                    siblings = new List<IDerEntity>();
                    childrenLookup[info.CommanderId] = siblings;
                }
                siblings.Add(entity);
            }

            // Recursive tree build starting at CommanderId == 0 (roots)
            if (childrenLookup.TryGetValue(0, out var roots))
            {
                foreach (var root in roots)
                {
                    CollectNodes(root, childrenLookup, result, expandedNodes, filterText, depth: 0);
                }
            }

            return result;
        }

        private void CollectNodes(...) { /* Recursive mapping to OrbatNodeViewModel */ }

        // --- WRITE MODEL (Publishing to DDS via ExConLogic) ---
        public void SelectEntity(int entityId)
        {
            // Sends a CMD_SET_SELECTION MapCommandRequest over DDS [4]
            _logic.SendSetSelection(entityId); 
        }

        public void CreateUnit(long tkbType)
        {
            // Pushes a CreateEntityRequest over DDS to the SimHost
            _logic.StartPlacementMode(tkbType, null);
        }

        public void ToggleExpanded(int entityId) { /* Local UI state mutation */ }
    }
}
```

2\. The Mission Editor NED Adapter

The beauty of the current architecture is that you actually **already have this adapter**. The `Hrot.ExCon.Services.MissionEditorService` was recently refactored to perfectly fit this adapter role.

If you look at its implementation, it already satisfies the `IMissionEditorService` contract we defined for `Hrot.UI.Common.Facades`:

-   **Read Model:** `GetMissionSnapshot` reads the `EntityMission` and `DescriptorOptimisticLock` directly from the `IDerRepo`.-   **Write Model:** `CommitMissionAsync` publishes a `MissionControlIntent` to ExCon's internal `FdpEventBus`.-   **DDS Translation:** ExCon runs a `MissionControlEgressTranslator` that listens to that internal bus and seamlessly translates the intent into a `MissionControlRequest` DDS message sent to the cluster.

To complete the DIP refactor for the Mission Editor, you simply pass ExCon's existing `MissionEditorService` into the shared `MissionPanel`:

```
// Inside ExConMock.cs or your ExCon composition root

// 1. Resolve the existing ExCon MissionEditorService (which speaks DDS)
IMissionEditorService missionAdapter = _logic.MissionEditorService;

// 2. Pass it to the shared UI panel
_missionPanel.DrawContent(missionAdapter);
```

Why this is a Massive Architectural Win

By establishing these Adapters, the `Hrot.UI.Common` library remains a pure, domain-centric UI layer.

-   **Hrot.Editor** injects `EditorOrbatAdapter` and `EditorMissionService`. These adapters hit the ECS memory-bus directly at sub-millisecond speeds and bypass CycloneDDS entirely.-   **Hrot.ExCon** injects `ExConOrbatAdapter` and `MissionEditorService`. These adapters read from the network-hydrated `DerRepo` and write commands to CycloneDDS.

You have achieved 100% UI code reuse (DRY) without leaking a single byte of networking code into your offline tools or a single byte of ECS memory-management into your lightweight C2 client.

what other UI panel I required will need this decoupling?

Based on your requirements in `edit-1-req.md`, there are two specific UI panels remaining that currently violate Clean Architecture or lack a decoupled implementation. We must extract these into our `Hrot.UI.Common` library using the exact same Ports and Adapters (DIP) pattern.

Here are the remaining panels and how we will decouple them.

1\. The Map Layer & Grid Control Panel (`ConfigPanel`)

**The Problem:** You requested a map layer and grid control panel. In the current codebase, ExCon already has `Hrot.ExCon.Panels.ConfigPanel` which manages checkboxes for "Satellite Layer", "Ground Units", "Grid", etc. However, it is tightly coupled to the network: when the operator clicks "Send", it builds a JSON Merge Patch and calls `IExConLogic.SendConfigPatch`, which blasts a `MapInteractionConfig` over CycloneDDS. The offline Editor needs this exact same UI, but without the JSON/DDS overhead.

**The Clean Architecture Fix:** We extract the panel into `Hrot.UI.Common` and segregate the interface. Instead of passing JSON patches around, we use a strongly-typed domain record.

```
// 1. The Port (Hrot.UI.Common/Facades/IMapConfigController.cs)
public record MapLayerState(bool Satellite, bool GroundUnits, bool AirUnits, bool Grid /*...*/);

public interface IMapConfigController
{
    MapLayerState GetCurrentConfig();
    void ApplyConfig(MapLayerState config);
}
```

-   **The ExCon Adapter (Networked):** Implements `IMapConfigController` by taking the `MapLayerState`, serialising it to the JSON patch schema, and writing the `MapInteractionConfig` DDS message exactly as it does today.-   **The Editor Adapter (Offline):** Because the Editor runs the local FDP engine, it bypasses JSON entirely. The adapter simply injects the data directly into the local `MapUserConfig` singleton, which the `StyleResolutionSystem` and `MapLayerAssignmentSystem` read at memory-bus speeds every frame.

2\. The Preview Panel (Edit ↔ Preview Mode Switch)

**The Problem:** You requested a "'Preview' panel allowing to switch into Preview mode and back". This is a crucial workflow: an operator places units in Edit mode, hits "Preview" to let the AI and physics run (capturing an ECS snapshot), and then hits "Stop" to rewind the world back to the exact placement state.

Currently, ExCon handles state transitions via the heavy `ClusterScenarioPanel`, which builds raw JSON payloads like `{"TargetState": 20}` and fires `ClusterOpRequest` over DDS.

**The Clean Architecture Fix:** We create a focused, lightweight `PreviewPanel` in `Hrot.UI.Common` and define a strict lifecycle interface.

```
// 1. The Port (Hrot.UI.Common/Facades/IPreviewController.cs)
public interface IPreviewController
{
    bool IsInPreviewMode { get; }
    void EnterPreviewMode();
    void ExitPreviewMode();
}
```

-   **The ExCon Adapter (Networked):** Implements the interface by publishing a `ClusterOpIntent` to transition the cluster state machine. `EnterPreviewMode` sends `TargetState = 20` (LoadingPreview), and `ExitPreviewMode` sends `TargetState = 22` (UnloadingPreview).-   **The Editor Adapter (Offline):** In the offline `Hrot.Editor`, we completely bypass the distributed orchestrator. The adapter talks directly to the local `PreviewClusterOpHandler`. When `EnterPreviewMode()` is called, it triggers `LoadingPreviewCommit()`, which calls `snap.SyncFrom(_liveRepo)` to perform a microsecond-fast memory snapshot of the ECS world. When `ExitPreviewMode()` is called, it triggers `UnloadingPreviewCommit()`, which rewinds the ECS memory back to the snapshot.

Summary

By defining `IMapConfigController` and `IPreviewController` in your shared UI library, you satisfy the remaining requirements from `edit-1-req.md`.

The ImGui rendering code for these panels is written **exactly once**. The ExCon continues to use them to orchestrate the distributed CycloneDDS cluster, while the `Hrot.Editor` uses them to mutate local ECS memory synchronously with zero network allocations.

Does the context menu machinery requires some similar decoupling to become reusable?

Yes. Your intuition is spot on. The current context menu implementation violates both the DRY and Dependency Inversion principles if we intend to reuse its logic across the offline Editor.

Currently, the logic that dictates _what_ appears in a context menu (e.g., showing "Edit Overlay" for area entities, or "Edit Route" for route entities) is hardcoded inside `Hrot.ExCon.Logic.ContextMenuLogic`. This class manually constructs a `List<ContextMenuItem>`, serializes it to JSON, and publishes a `ContextActionsUpdate` DDS message over the network. Because it is permanently bound to the CycloneDDS network layer and JSON serialization, the `Hrot.Editor` cannot reuse these menu definitions without duplicating the evaluation rules.

Fortunately, the FDP toolkit already provides the perfect abstraction for this: `IContextMenuBuilder`. We can achieve a perfectly decoupled architecture by executing the following Hexagonal refactor:

1\. Extract the Shared Menu Definitions (`Hrot.UI.Common`)

We move the strategy and entity-evaluation logic out of ExCon and into a shared populator. This class relies strictly on the `IContextMenuBuilder` abstraction to define the layout, meaning it has zero knowledge of ImGui or JSON. To handle the actions, we inject a highly focused capability interface (a Port).

```
// Hrot.UI.Common/Facades/IEntityActionController.cs
public interface IEntityActionController
{
    void CenterOnEntity(long entityId);
    void DeleteEntity(long entityId);
    void EditOverlay(long entityId);
    // ...
}

// Hrot.UI.Common/Menus/SharedContextMenuPopulator.cs
public static class SharedContextMenuPopulator
{
    // Evaluates primitive state to decouple from both IDerEntity and ECS Entity
    public static void Populate(long entityId, long tkbType, bool hasEditableOverlay, 
                                IContextMenuBuilder builder, IEntityActionController actions)
    {
        builder.AddItem("Center on Entity", () => actions.CenterOnEntity(entityId));
        builder.AddItem("Delete", () => actions.DeleteEntity(entityId));

        if (hasEditableOverlay)
        {
            builder.AddSeparator();
            builder.AddItem("Edit Drawing", () => actions.EditOverlay(entityId));
        }
    }
}
```

2\. The Offline Editor Adapter (Direct Memory)

In `Hrot.Editor`, we already have the native `FDP.Toolkit.ImGui.Utils.ContextMenuBuilder` which directly issues `ImGui.MenuItem` rendering calls. We implement `IEntityActionController` using pure memory-bus domain events (e.g., publishing `DestroyEntityCommand` directly to the `FdpEventBus`).

When the user right-clicks the Entity Inspector or the Map Canvas, we simply pass the native ImGui builder and our local action adapter into the shared populator. The menus render instantly with zero allocations or string parsing.

3\. The ExCon Adapter (JSON / DDS Proxy)

To keep ExCon working with the remote IG over the network, we create a proxy builder: `JsonContextMenuBuilder`, which implements `IContextMenuBuilder`.

When `SharedContextMenuPopulator.Populate` calls `builder.AddItem(label, callback)`, this proxy adapter does not render UI. Instead, it:

-   Creates a `ContextMenuItem` DTO.-   Generates a unique integer ID for this item.-   Caches the C# `callback` delegate in a dictionary keyed by that ID.

The ExCon then serializes the resulting DTO list to JSON and sends the `ContextActionsUpdate` over DDS. Later, when the remote IG user clicks the menu in the 3D view, the IG sends back a `ContextActionInvoked` DDS message containing the selected ID. The ExCon receives it, looks up the cached callback delegate, and executes it.

The Architectural Win

This completely isolates your business rules. The rules defining which entities get which menu options are written exactly once in `Hrot.UI.Common`. The offline Editor consumes them to render immediate-mode ImGui UI, while the ExCon C2 node consumes the exact same rules to generate a declarative JSON payload and correlate remote RPC callbacks, all while remaining strictly decoupled.

what about the entity creator panel and all the map stuff editing tools, are they already decoupled or will they need some changes?

The actual map interaction tools—such as the `CreationTool`, `EditTool`, `RouteEditTool`, and `MeasureTool`—are already beautifully decoupled and adhere to Clean Architecture principles. However, the ImGui frontend (the Entity Creator panel itself) is currently coupled and will require the Dependency Inversion refactor we discussed.

Here is the exact breakdown of what is pristine and what needs changing based on the sources:

1\. The Map Interaction Tools (Already Decoupled)

The Vis2D tools that handle map canvas interactions are perfectly isolated from the network and the specific application shell.

-   **CreationTool****:** Translates a canvas left-click into a pure `SpawnEntityCommand` and routes it through an injected `Action<SpawnEntityCommand>` delegate. It has absolutely zero knowledge of CycloneDDS or whether it is running in a distributed cluster or an offline editor.-   **EditTool** **and** **RouteEditTool****:** These tools mutate an in-memory "ghost" list of vertices as the operator drags them. When the operator right-clicks to commit, they simply fire `OnPolylineCommitted` and `OnRouteCommitted` delegates, leaving the host application to handle the ECS or network persistence.-   **MeasureTool****:** Operates entirely statelessly, capturing world-space start and end points and measuring Euclidean distance with zero allocations on the hot path.

Furthermore, the "Phase 3 Anti-Corruption Layer (ACL) Backdoor Elimination" strictly enforces that tools like `AreaAuthoringTool` and `RouteAuthoringTool` must stop building raw DDS `CreateEntityRequest` payloads. Instead, they pass pure ECS domain components like `EditablePolyline` and `MapOverlayStyle` through `SpawnEntityCommand.InitialComponents`, fully severing the UI layer's coupling to the network edge.

2\. The Entity Creator UI (Needs Decoupling)

While the map tools are clean, the data-driven ImGui panel that triggers them—the `SpawnerPanel`—is currently trapped inside the `Hrot.ExCon.Panels` namespace and relies directly on the `IExConLogic` god-interface.

To reuse this panel in the standalone `Hrot.Editor`, we must apply the same **Ports and Adapters** pattern we used for the Mission and ORBAT panels:

**A. Define the Port in** **Hrot.UI.Common** We create a segregated interface for spawning capabilities:

```
public interface ISpawnController 
{
    void StartPlacementMode(long tkbType, string? initialPropertiesJson = null);
    void StartAreaAuthoringMode(string styleOverrideJson = "");
    void StartRouteAuthoringMode();
}
```

**B. Refactor** **SpawnerPanel** Move the `SpawnerPanel` into the shared UI library and swap `IExConLogic` for `ISpawnController` in its button handlers.

**C. Implement the Adapters**

-   **ExCon Adapter (Networked):** `ExConLogic` already implements these methods by generating an `ActiveContextId` and publishing a `MapCommandRequest` or `MapInteractionConfig` over CycloneDDS to activate the tool remotely on the IG.-   **Editor Adapter (Offline):** In the offline `Hrot.Editor`, you will create a new adapter that implements `ISpawnController` by instantiating the decoupled `CreationTool`, `EditTool`, or `PointSequenceTool` and pushing it directly onto the Editor's local `MapCanvas` at memory-bus speeds.

By moving the `SpawnerPanel` to `Hrot.UI.Common` and injecting these adapters, you achieve 100% UI code reuse while keeping your Editor perfectly offline and network-agnostic.

I would like all these UIs and map tools to be usable for authoring the same kind of scenario as the urban coman demo is. What UIs/tols are we still missing to achieve that?

To fully author the exact "Urban Combat" scenario (`UrbanCombatNewScenario`) using only the UI, we must account for the specific narrative setup that the programmatic script currently bypasses.

While our existing suite of decoupled panels and interaction tools handles spawning, routing, and properties, we are missing a few specific tools and UI workflows to replicate the exact state initialized in `ScenarioDirector.SetupAmbushScenario()`.

Here are the missing UI capabilities and tools we need to build:

1\. Embarkation & Cargo Management UI

**The Requirement:** The urban combat scenario pre-embarks 4 Infantry Soldiers (TKB 2002) inside the Military APC (TKB 2001) before the scenario begins. **The Missing UI:** We currently have no interface to load entities into a `PassengerBuffer`. **The Architectural Fix:** We need a Cargo/Embarkation panel, or we need to leverage the newly decoupled `SharedOrbatPanel`. By supporting drag-and-drop within the ORBAT tree, an operator could drag infantry nodes onto the APC node, which the `EditorOrbatAdapter` would translate into a pure domain event (e.g., `EmbarkEntityCommand`) to modify the ECS `IsEmbarkedTag` and `PassengerBuffer` components.

2\. Target Memory Seeding (Link Tool)

**The Requirement:** The demo explicitly pre-seeds the Insurgent's `TargetMemory` with the APC to guarantee an immediate ambush, and seeds one Civilian Pedestrian's memory with the Insurgent to trigger an immediate `FLEE` locomotion state. **The Missing UI:** We do not have a map tool to establish perceptual links between entities. **The Architectural Fix:** We need a new Vis2D tool (e.g., `TargetLinkTool`). The operator would select the Insurgent, activate the tool, and click the APC. The `StandardInteractionTool` can easily be extended to support this. Once clicked, the offline adapter would push the target's `Entity` reference directly into the perceiver's `TargetMemory` buffer.

3\. Static Zone & Obstacle Authoring

**The Requirement:** Phase 2 of the refactor introduced the `ZoneDefinitionDto`, which bundles a `RoadNetworkPath` (e.g., `sample_road.json`) and static `ZoneObstacleDto` definitions (2.5D cylindrical physics colliders) into the scenario file. The urban combat scenario uses this to place a wall for the insurgent to hide behind. **The Missing UI:** The `SpawnerPanel` only enumerates dynamic TKB entities. We have no UI to define the scenario's static environment or drop physics obstacles. **The Architectural Fix:** We need a `ZoneEditorPanel` added to our shared UI library. This panel would provide:

-   A file-picker to assign the `RoadNetworkPath` for the active zone.-   An obstacle placement tool (similar to `CreationTool`) that emits a pure event to spawn an unmanaged entity with a `PhysicsCollider` and `SimTransform`, which the `ZoneManagerService` will serialize into the `ZoneObstacleDto` list on save.

4\. Expanding the Mission/Behavior Catalog

**The Requirement:** The scenario assigns specific military and civilian behaviors: `ConvoyEscort`, `InfantryCombat`, `Ambush`, and `WanderCivil`. **The Missing UI:** If you look at the current `MissionPanel` implementation, the `_behaviorIds` dropdown is rigidly hardcoded to a capacity of 4: `MoveToLocation`, `FollowRoute`, `JoinFormation`, and `Idle`. **The Architectural Fix:** The shared `MissionPanel` needs to stop hardcoding these string literals. The `IMissionEditorService` facade should expose a method to query the available behavior IDs dynamically, allowing the Editor adapter to read them straight from the local `BehaviorRegistry`.

these are great improvements. let's pls detail one by one. start with 1. Embarkation & Cargo Management UI

To enable Embarkation and Cargo Management in a clean, decoupled way, we will leverage the **Shared ORBAT Panel** (`SharedOrbatPanel`) we extracted to `Hrot.UI.Common` and extend it with native ImGui drag-and-drop capabilities.

By treating embarkation purely as a UI intent that emits a domain event, we ensure the offline `Hrot.Editor` can process the ECS component mutations at memory-bus speeds without touching `Hrot.NED` or CycloneDDS, perfectly adhering to the Dependency Inversion Principle.

Here is the exact architectural blueprint and step-by-step implementation to achieve this.

1\. Extend the UI Contract (The Port)

First, we must extend the segregated `IOrbatController` in `Hrot.UI.Common` to support cargo intents. The UI library does not know _how_ embarkation works; it only knows that the user dragged one unit onto another.

```
// Hrot.UI.Common/Facades/IOrbatController.cs
public interface IOrbatController
{
    void SelectEntity(int entityId);
    void CreateUnit(long tkbType);
    void ToggleExpanded(int entityId);
    
    // NEW: Cargo Management Intents
    void RequestEmbark(int passengerEntityId, int vehicleEntityId);
    void RequestDisembark(int passengerEntityId);
}
```

2\. Implement ImGui Drag-and-Drop in `SharedOrbatPanel`

Inside the shared ORBAT rendering loop, we utilize ImGui's native payload system. When rendering a node, we declare it as both a potential drag source (the passenger) and a potential drop target (the vehicle).

```
// Hrot.UI.Common/Panels/SharedOrbatPanel.cs
public unsafe void DrawContent(IOrbatDataProvider dataProvider, IOrbatController controller)
{
    var nodes = dataProvider.GetVisibleNodes(_filterText, _expandedNodes);

    foreach (var node in nodes)
    {
        // Render the selectable tree node
        if (ImGui.Selectable(node.Name)) 
            controller.SelectEntity(node.EntityId);

        // 1. DRAG SOURCE: Allow this entity to be picked up
        if (ImGui.BeginDragDropSource())
        {
            int draggedId = node.EntityId;
            ImGui.SetDragDropPayload("ORBAT_ENTITY", (IntPtr)(&draggedId), sizeof(int));
            ImGui.Text($"Embark {node.Name}");
            ImGui.EndDragDropSource();
        }

        // 2. DROP TARGET: Allow entities to be dropped onto this vehicle
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ORBAT_ENTITY");
            if (payload.NativePtr != null)
            {
                int passengerId = *(int*)payload.Data;
                if (passengerId != node.EntityId) // Prevent self-embarkation
                {
                    controller.RequestEmbark(passengerId, node.EntityId);
                }
            }
            ImGui.EndDragDropTarget();
        }
    }
}
```

3\. Define the Domain Command (FDP Kernel)

We need a pure domain event to represent this intent inside the FDP engine, so that the Editor's adapter can publish it.

```
// FDP/Toolkits/FDP.Toolkit.Behavior/Events/EmbarkEntityCommand.cs
using Fdp.Kernel;

namespace FDP.Toolkit.Behavior.Events
{
    [EventId(3201)] // Assigned a unique ID in the Behavior block
    public struct EmbarkEntityCommand
    {
        public Entity Passenger;
        public Entity Vehicle;
    }
}
```

4\. The Offline Editor Adapter (`EditorOrbatAdapter`)

In the `Hrot.Editor` project, we implement `IOrbatController.RequestEmbark`. Because the Editor has direct access to the `EntityRepository` and `FdpEventBus`, this adapter simply translates the integer IDs into `Entity` structs and fires the command.

```
// Hrot.Editor/Adapters/EditorOrbatAdapter.cs
public void RequestEmbark(int passengerEntityId, int vehicleEntityId)
{
    // Assuming the Editor's EntityMap or IDerRepo maps the int to an ECS entity index
    var passenger = _world.GetEntityByIndex(passengerEntityId);
    var vehicle = _world.GetEntityByIndex(vehicleEntityId);

    if (_world.IsAlive(passenger) && _world.IsAlive(vehicle))
    {
        _bus.Publish(new EmbarkEntityCommand 
        { 
            Passenger = passenger, 
            Vehicle = vehicle 
        });
    }
}
```

5\. The Execution System (`EditorCargoSystem`)

Finally, we need a system running in the `Hrot.Editor`'s local kernel to execute the command and safely mutate the ECS components. We strictly mirror the exact ECS component logic originally hardcoded in the `ScenarioDirector.EmbarkSoldiers` method.

```
// Hrot.Editor/Systems/EditorCargoSystem.cs
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Events;
using ModuleHost.Core.Abstractions;

namespace Hrot.Editor.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class EditorCargoSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            var commands = World.Bus.Consume<EmbarkEntityCommand>();
            foreach (ref readonly var cmd in commands)
            {
                if (!World.HasComponent<PassengerBuffer>(cmd.Vehicle)) continue;

                ref var buffer = ref World.GetComponentRW<PassengerBuffer>(cmd.Vehicle);
                
                // Capacity check
                if (buffer.Count >= PassengerBuffer.Capacity) continue;

                // 1. Add passenger to the vehicle's buffer
                buffer.Passengers[buffer.Count] = cmd.Passenger;
                buffer.Count++;

                // 2. Strip mobility and weapon capabilities from the passenger
                if (World.HasComponent<ActorCapabilityState>(cmd.Passenger))
                {
                    ref var caps = ref World.GetComponentRW<ActorCapabilityState>(cmd.Passenger);
                    caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);
                }

                // 3. Tag the passenger as embarked
                World.AddComponent(cmd.Passenger, new IsEmbarkedTag { VehicleEntity = cmd.Vehicle });
            }
        }
    }
}
```

Why this satisfies Clean Architecture

-   **Single Responsibility:** The UI library only handles drawing and translating mouse drags into intent requests.-   **Network Decoupling:** The `SharedOrbatPanel` does not reference DDS or raw JSON commands.-   **Extensibility (ExCon):** If you reuse this panel in the networked `ExCon` node later, you simply implement an `ExConOrbatAdapter` that converts `RequestEmbark` into a `MapCommandRequest` over CycloneDDS. The ImGui rendering code remains untouched.

good now pls detail 2. Target Memory Seeding (Link Tool)

To implement the Target Memory Seeding capability cleanly within the offline `Hrot.Editor`, we actually do not need to write a brand new Vis2D canvas tool. The framework already provides a highly optimized, allocation-free `EntityPickerTool`.

By adhering to the DRY principle and leveraging the asynchronous `IMapPickService` facade we designed earlier, we can compose this workflow using pure CQRS.

Here is the exact Clean Architecture implementation to allow operators to seed the `TargetMemory` component without coupling the UI to the ECS.

1\. Define the Domain Command (FDP Kernel)

First, we define a pure unmanaged domain event to represent the operator's intent to link two entities. This ensures the ECS mutation is handled by a scheduled system rather than the UI thread.

```
// FDP/Toolkits/FDP.Toolkit.Perception/Events/SeedTargetCommand.cs
using Fdp.Kernel;

namespace FDP.Toolkit.Perception.Events
{
    [EventId(4101)] // Assigned an unused ID in the Perception block
    public struct SeedTargetCommand
    {
        public Entity Perceiver;
        public Entity Target;
        public float ScoreBoost;
    }
}
```

2\. The UI Trigger (Context Menu & Async Pick)

We will add this interaction directly into the shared `EditorEntityContextMenuHandler` we refactored earlier. If the selected entity possesses a `TargetMemory` component, we expose a "Mark Target..." option.

When clicked, we await the `IMapPickService.PickEntityAsync()` method. In the Editor, our offline adapter fulfills this by pushing the native `EntityPickerTool` onto the `MapCanvas`. Once the operator clicks a valid target, we publish the command.

```
// Inside Hrot.Editor.UI.EditorEntityContextMenuHandler.PopulateMenu:

if (_repo.HasUnmanagedComponent<TargetMemory>(entity))
{
    builder.AddItem("Mark Target...", async () => 
    {
        // 1. The UI delegates to the abstracted pick service.
        // In the Editor, this pushes the FDP EntityPickerTool to the local canvas.
        int targetIndex = await _logic.MapPickService.PickEntityAsync();
        
        if (targetIndex > 0)
        {
            var targetEntity = _repo.GetEntityByIndex(targetIndex);
            
            // 2. Publish the pure domain event to the memory-bus.
            _bus.Publish(new SeedTargetCommand 
            { 
                Perceiver = entity, 
                Target = targetEntity, 
                ScoreBoost = 100f // Immediate engagement priority
            });
        }
    });
}
```

3\. The Execution System (`EditorPerceptionSetupSystem`)

Finally, we need a lightweight system running in the `Hrot.Editor`'s local simulation loop to consume the command and safely mutate the `TargetMemory` component.

Following strict ECS patterns, we read the target's current `SimTransform` and use the built-in `TargetMemory.AddOrUpdateTarget` method. Crucially, the engine expects the target's `EntityId` inside the memory buffer to be the full `PackedValue` of the `Entity` struct.

```
// Hrot.Editor/Systems/EditorPerceptionSetupSystem.cs
using Fdp.Kernel;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using ModuleHost.Core.Abstractions;

namespace Hrot.Editor.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class EditorPerceptionSetupSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            var commands = World.Bus.Consume<SeedTargetCommand>();
            
            foreach (ref readonly var cmd in commands)
            {
                // Generational safety guard
                if (!World.IsAlive(cmd.Perceiver) || !World.IsAlive(cmd.Target)) 
                    continue;

                if (!World.HasComponent<TargetMemory>(cmd.Perceiver) || 
                    !World.HasComponent<SimTransform>(cmd.Target))
                    continue;

                ref var mem = ref World.GetComponentRW<TargetMemory>(cmd.Perceiver);
                ref readonly var targetTf = ref World.GetComponentRO<SimTransform>(cmd.Target);

                // Safely inject the target into the entity's memory buffer
                TargetMemory.AddOrUpdateTarget(
                    ref mem,
                    entityId: (long)cmd.Target.PackedValue, [7, 8]
                    posX: targetTf.Position.X,
                    posY: targetTf.Position.Y,
                    scoreBoost: cmd.ScoreBoost,
                    tick: World.Tick
                );
                
                // Logging for the Editor Data Monitor
                FDP.Kernel.Logging.FdpLog<EditorPerceptionSetupSystem>.Info(
                    "[Editor] Seeded TargetMemory for {0} with target {1}", 
                    cmd.Perceiver.Index, cmd.Target.Index);
            }
        }
    }
}
```

Why this satisfies Clean Architecture & Best Practices:

-   **Maximum Reuse:** We didn't write a single line of Raylib or input-handling code. We reused the existing `EntityPickerTool` and the `IMapPickService` facade.-   **Separation of Concerns:** The UI layer remains completely asynchronous and unaware of how ECS components are laid out in memory. It only dispatches the `SeedTargetCommand`.-   **Memory-Safety:** The `TargetMemory` struct contains fixed-size unsafe arrays (`fixed long EntityIds[MaxTrackedTargets]`). By confining the mutation to the `EditorPerceptionSetupSystem`, we keep the unsafe memory operations out of the UI layer and guarantee they only occur during the thread-safe `Input` phase of the kernel tick.

Detail the 3. Static Zone & Obstacle Authoring implementation

To implement Static Zone & Obstacle Authoring in a way that respects our Anti-Corruption Layer (ACL) and keeps the offline `Hrot.Editor` decoupled from the distributed network, we will apply the Dependency Inversion Principle (DIP) using the Ports and Adapters pattern.

Because the `ZoneManagerService` relies on pure application-layer Data Transfer Objects (DTOs) like `ZoneDefinitionDto` and `ZoneObstacleDto` to bridge the gap between scenario files and the FDP engine, our UI must act purely as an intent generator.

Here is the clean architecture blueprint for authoring static zones and physics obstacles.

1\. The UI Port (Shared Contract)

We define a segregated interface in the `Hrot.UI.Common.Facades` library. This interface declares the authoring capabilities without knowing anything about ECS components, `ZoneManagerService`, or DDS.

```
namespace Hrot.UI.Common.Facades
{
    public interface IZoneAuthoringController
    {
        // Assigns the road network JSON file to the active zone
        void SetRoadNetworkPath(string activeZoneName, string assetPath);
        
        // Activates the map tool to drop a cylindrical LOS obstacle
        void StartObstaclePlacementMode(string activeZoneName, float radius);
    }
}
```

2\. The Shared UI Panel (`ZoneEditorPanel`)

We create the data-driven ImGui panel in `Hrot.UI.Common`. This panel provides the interface for selecting a road network asset and defining the radii of static obstacles, which are strictly upright, non-oriented 2.5D cylinders.

```
namespace Hrot.UI.Common.Panels
{
    public class ZoneEditorPanel
    {
        private string _zoneName = "urban_combat_zone";
        private string _roadNetworkPath = "Assets/sample_road.json";
        private float _obstacleRadius = 5.0f;

        public void DrawContent(IZoneAuthoringController controller)
        {
            ImGui.InputText("Zone Name", ref _zoneName, 64);
            ImGui.Separator();

            ImGui.InputText("Road Network JSON", ref _roadNetworkPath, 256);
            if (ImGui.Button("Apply Road Network"))
            {
                controller.SetRoadNetworkPath(_zoneName, _roadNetworkPath);
            }

            ImGui.Separator();
            ImGui.SliderFloat("Obstacle Radius (m)", ref _obstacleRadius, 1.0f, 50.0f);
            if (ImGui.Button("Place LOS Obstacle"))
            {
                // Delegates to the host application to handle the map interaction
                controller.StartObstaclePlacementMode(_zoneName, _obstacleRadius);
            }
        }
    }
}
```

3\. The Offline Editor Adapter

In the `Hrot.Editor` project, we implement `IZoneAuthoringController`. For obstacle placement, we push a decoupled placement tool onto the local `MapCanvas` at memory-bus speeds. When the operator clicks, the tool emits a pure domain command.

```
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using Hrot.UI.Common.Facades;
using Hrot.Map.Common.Events; // Assume we define SpawnZoneObstacleCommand here

namespace Hrot.Editor.Adapters
{
    public class EditorZoneAdapter : IZoneAuthoringController
    {
        private readonly MapCanvas _canvas;
        private readonly FdpEventBus _bus;

        public EditorZoneAdapter(MapCanvas canvas, FdpEventBus bus)
        {
            _canvas = canvas;
            _bus = bus;
        }

        public void SetRoadNetworkPath(string activeZoneName, string assetPath)
        {
            // Emits a domain event handled by the local ECS to update the ZoneEnvironmentData singleton
            _bus.PublishManaged(new UpdateZoneConfigCommand { ZoneName = activeZoneName, RoadNetworkPath = assetPath });
        }

        public void StartObstaclePlacementMode(string activeZoneName, float radius)
        {
            // Reuse the existing pattern: push a lightweight Vis2D tool
            var tool = new ObstaclePlacementTool(radius, onClickPos => 
            {
                _bus.Publish(new SpawnZoneObstacleCommand 
                { 
                    ZoneName = activeZoneName, 
                    Position = onClickPos, 
                    Radius = radius 
                });
            });
            
            _canvas.PushTool(tool);
        }
    }
}
```

4\. The ECS Execution System

Finally, we create a lightweight system running inside the Editor's simulation loop to safely mutate the ECS world in response to the operator's commands.

This system perfectly mirrors the logic dictated for `ZoneManagerService.LoadZones`: it spawns an unmanaged entity and attaches a `SimTransform` and a `PhysicsCollider` configured with `PhysicsConstants.EntityCollisionLayer`.

```
using Fdp.Kernel;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using ModuleHost.Core.Abstractions;

namespace Hrot.Editor.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class EditorZoneAuthoringSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            var commands = World.Bus.Consume<SpawnZoneObstacleCommand>();
            foreach (ref readonly var cmd in commands)
            {
                var entity = World.CreateEntity();
                
                // Construct the exact components the engine narrow-phase solver expects
                World.AddComponent(entity, new SimTransform 
                { 
                    Position = new Vector3(cmd.Position.X, cmd.Position.Y, 0f),
                    Rotation = Quaternion.Identity 
                });
                
                World.AddComponent(entity, new PhysicsCollider 
                { 
                    Radius = cmd.Radius, 
                    CollisionLayer = PhysicsConstants.EntityCollisionLayer 
                });

                // Optional: Tag it with the zone name so ZoneManagerService.GetActiveZones() can collect it on save
                World.AddManagedComponent(entity, new ZoneMembership { ZoneName = cmd.ZoneName });
            }
        }
    }
}
```

The Serialization Payoff

By keeping the entities strictly as standard `SimTransform` + `PhysicsCollider` configurations, we preserve the integrity of the save pipeline.

When the operator clicks "Save" in the `ScenarioBrowserPanel`, the `ScenarioFileService` will call `_zoneManagerService.GetActiveZones()`, which will effortlessly query these exact components, map them to `ZoneObstacleDto` instances, and bundle them into the `HrotScenarioEnvelopeDto.Zones` dictionary for writing to disk. The UI never has to parse JSON, and the FDP engine never has to know about file paths.

ps detail 4. Expanding the Mission/Behavior Catalog

To fully support authoring the Urban Combat scenario from the UI, the operator needs to be able to select the specific narrative behaviors (`ConvoyEscort`, `InfantryCombat`, `Ambush`, and `WanderCivil`) from the Mission Editor dropdown.

Currently, this is impossible because the `MissionPanel` severely violates Clean Architecture. Looking at the source, `MissionPanel` manually instantiates a dummy `BehaviorRegistry` in its constructor and rigidly hardcodes exactly four behaviors (`MoveToLocation`, `FollowRoute`, `JoinFormation`, `Idle`).

To fix this and make the panel fully data-driven, we must extract the catalog knowledge out of the UI and push it down into the host application via the `IMissionEditorService` adapter.

Here is the exact step-by-step implementation.

1\. Extend the Engine's `BehaviorRegistry`

Currently, `BehaviorRegistry` holds the dictionary of definitions but does not expose the list of registered names. We must add a simple getter so the adapters can read the actual registered capabilities of the running engine.

```
// FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs
public sealed class BehaviorRegistry
{
    // Existing fields...
    private readonly Dictionary<string, int> _nameToId = new(StringComparer.Ordinal);

    // NEW: Expose registered names for the UI adapters
    public IReadOnlyList<string> GetRegisteredNames() 
        => _nameToId.Keys.ToList();
        
    // ... existing methods ...
}
```

2\. Update the UI Port (`IMissionEditorService`)

Extend the segregated interface in `Hrot.UI.Common.Facades` to request the available behaviors. The UI library does not need to know _what_ these behaviors are, only that it should render them as options in the dropdown.

```
// Hrot.UI.Common/Facades/IMissionEditorService.cs
public interface IMissionEditorService
{
    // NEW: Capability query for the dropdown
    IReadOnlyList<string> GetAvailableBehaviors();

    (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId);
    Task<MissionCommitResult> CommitMissionAsync(long entityId, MissionPlan newPlan, long baseVersion);
    // ...
}
```

3\. Refactor the Shared `MissionPanel`

Remove the hardcoded constants and the dummy `BehaviorRegistry` instantiation from the panel. Instead, fetch the available behaviors dynamically from the injected facade.

```
// Hrot.UI.Common/Panels/MissionPanel.cs
public void DrawContent(IExConLogic logic)
{
    // ... existing selection checks ...

    // Fetch the dynamic list from the host application
    var availableBehaviors = logic.MissionEditorService.GetAvailableBehaviors();

    // ... inside the task loop:
    var behaviorLabel = string.IsNullOrEmpty(task.BehaviorId) ? "<none>" : task.BehaviorId;

    if (ImGui.BeginCombo($"Behavior##{i}", behaviorLabel))
    {
        foreach (var behaviorName in availableBehaviors)
        {
            bool selected = task.BehaviorId == behaviorName;
            if (ImGui.Selectable(behaviorName, selected))
            {
                HandleEditBehaviorId(i, behaviorName);
            }
            if (selected) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }
    // ...
}
```

4\. The Offline Editor Adapter (`EditorMissionService`)

In the `Hrot.Editor` project, we inject the live `BehaviorRegistry` into our adapter. When the UI asks for available behaviors, we simply query the registry at memory-bus speeds.

Because the `Hrot.Editor` boots up with the `SimHostComponentRegistry` and registers all the Urban Combat behaviors (like `Ambush` and `ConvoyEscort`), they will immediately appear in the dropdown.

```
// Hrot.Editor/Adapters/EditorMissionService.cs
public class EditorMissionService : IMissionEditorService
{
    private readonly FdpEventBus _bus;
    private readonly EntityRepository _repo;
    private readonly BehaviorRegistry _registry;

    public EditorMissionService(FdpEventBus bus, EntityRepository repo, BehaviorRegistry registry)
    {
        _bus = bus;
        _repo = repo;
        _registry = registry;
    }

    public IReadOnlyList<string> GetAvailableBehaviors()
    {
        // Dynamically returns whatever behaviors the Editor kernel has registered
        // e.g. ["MoveToLocation", "FollowRoute", "Ambush", "ConvoyEscort", "WanderCivil"]
        return _registry.GetRegisteredNames();
    }
    
    // ... existing CommitMissionAsync / GetMissionSnapshot implementation ...
}
```

5\. The ExCon Adapter (Network Layer)

For the networked `ExCon` node, the `MissionEditorService` currently does not have access to the SimHost's internal `BehaviorRegistry`.

To fulfill the contract without adding complex DDS discovery for behaviors just yet, we can simply construct ExCon's `MissionEditorService` with a pre-configured list of known behavior strings provided by the `ExConLogic` composition root.

```
// Hrot.ExCon/Services/MissionEditorService.cs
public sealed class MissionEditorService : IMissionEditorService
{
    private readonly IReadOnlyList<string> _knownBehaviors;

    public MissionEditorService(IDerRepo repo, FdpEventBus bus, IReadOnlyList<string> knownBehaviors)
    {
        // ...
        _knownBehaviors = knownBehaviors;
    }

    public IReadOnlyList<string> GetAvailableBehaviors() => _knownBehaviors;
}
```

Why this satisfies Clean Architecture:

By implementing this, the `MissionPanel` becomes a pure presentation component. It blindly renders the strings provided by the `IMissionEditorService`. When you boot the `Hrot.Editor` to author the Urban Combat scenario, your local adapter seamlessly supplies `Ambush`, `ConvoyEscort`, and `WanderCivil`, allowing the operator to assign these tactical behaviors to entities completely through the UI without writing a single line of scenario-specific application code.

How should the Drag-and-Drop system handle capacity limits for vehicles?

To maintain a pristine Clean Architecture, we must ensure the UI layer remains completely ignorant of domain invariants like vehicle passenger limits. The Drag-and-Drop system should only express the user's _intent_, delegating the actual capacity validation to the ECS simulation kernel.

Here is how we handle it using strict separation of concerns:

-   **The UI Intent:** When an operator drags a soldier onto a vehicle (like an APC) in the shared ORBAT panel, the UI does not perform any capacity checks. It blindly translates the drop action into a `RequestEmbark` command through our decoupled capability facade.-   **The Component Contract:** The memory state is modeled by the `PassengerBuffer` ECS component, which defines a strict structural invariant of `public const int Capacity = 8;`.-   **Domain Enforcement:** The actual validation is isolated strictly inside the execution system, such as the `EmbarkExecutor`. When processing the interaction, the executor retrieves the target vehicle's `PassengerBuffer` via `GetComponentRW` and evaluates `if (buffer.Count >= PassengerBuffer.Capacity)`.-   **Graceful Rejection:** If the buffer is at maximum capacity, the executor refuses to mutate the ECS state, aborts the embarkation, and cleanly sets the `InteractionChannel.Status = NodeStatus.Failure`. (In our offline `EditorCargoSystem` equivalent, it simply skips the command and leaves the entities untouched).

By encapsulating the capacity logic exclusively within the FDP engine's execution phase, we uphold the Single Responsibility Principle. If designers decide to vary the capacity based on vehicle parameters in the future, our shared ImGui Drag-and-Drop code requires absolutely zero modifications.

Can we create a shared catalog for all Behavior IDs?

Yes, we can, but we must strictly respect the Anti-Corruption Layer between the generic FDP engine framework and the specific HROT application.

Currently, Behavior IDs are scattered but carefully managed through block allocations to ensure they are globally unique across the entire project without colliding.

-   **Framework-level behaviors:** Concepts like `WanderCivil` (1001), `ConvoyEscort` (2001), and `Ambush` (2003) live inside `FDP.Toolkit.Behavior.BehaviorIds`.-   **Application-level behaviors:** HROT-specific tactical implementations like `MoveTo_BT` (3001) and `FollowRoute_BT` (3002) live inside `Hrot.SimHost.SimHostBehaviorIds`.

From a Clean Architecture perspective, we **cannot** shove the HROT-specific behaviors down into the FDP toolkit, because the underlying simulation engine should not depend on the specific tactical behaviors of the host application.

However, we can achieve your goal using a two-pronged approach:

1\. The Compile-Time Catalog (`Hrot.Map.Definitions`)

Just as we centralized all unit types in `TkbEntityTypes` inside the `Hrot.Map.Definitions` project, we should move `SimHostBehaviorIds` out of `Hrot.SimHost` and into `Hrot.Map.Definitions`.

By placing `HrotBehaviorIds` in the definitions library, all application subsystems (`Hrot.ExCon`, `Hrot.Editor`, and `Hrot.SimHost`) can safely reference the compile-time constants without taking a dependency on the heavyweight simulation host.

2\. The Runtime Catalog (`BehaviorRegistry`)

The true shared catalog is already built into the architecture: the `BehaviorRegistry`.

At startup, the composition root registers both the FDP toolkit behaviors and the HROT application behaviors into this single registry, mapping their stable integer IDs to their behavior string names.

Because of the Dependency Inversion refactor we just designed for the `MissionPanel`, your UI doesn't actually need to reference the compile-time IDs at all. By calling `logic.MissionEditorService.GetAvailableBehaviors()`, the UI dynamically polls the `BehaviorRegistry` to populate its dropdowns at memory-bus speeds.

This means your UI remains 100% data-driven and blissfully ignorant of the underlying ID routing, while the backend maintains strict separation between engine and application boundaries.

How will the TargetLinkTool visualize the links on the map?

To clarify our previous architectural design, we are not building a bespoke `TargetLinkTool` because that would violate the DRY principle. Instead, we are repurposing the framework's existing, allocation-free `EntityPickerTool`.

Because we strictly separate interaction logic from presentation state, the visualization happens in two distinct phases:

**1\. During the Interaction (The Picking Phase)** While the operator is selecting a target, the `EntityPickerTool` takes over the canvas and renders a highly optimized crosshair cursor. It performs an allocation-free spatial hit-test every frame. When hovering over empty space, the crosshair renders in amber, and the moment it hovers over a valid entity, it instantly snaps to red `(255, 0, 0)`. This provides immediate, zero-latency visual feedback that a target can be linked.

**2\. After Establishment (The Map Rendering Phase)** Once the operator clicks and the `TargetMemory` component is successfully seeded in the ECS, the tool pops itself off the stack. At this point, the UI tool has done its job, and the rendering pipeline takes over.

I must note that the provided sources do not currently contain the rendering logic to visualize the populated `TargetMemory` component on the map. To implement this while adhering to Clean Architecture, we must rely on the existing Entity Component System rendering paradigms rather than coupling draw calls to the UI tool.

We should implement this by mirroring how the framework currently renders `FormationRoster` leader lines. We will extend the `VehicleVisualizer` (or create a dedicated `PerceptionMapLayer`) to query the ECS for entities possessing a `TargetMemory` and a `SimTransform`. During the `Raylib.BeginMode2D` draw pass, the visualizer will iterate through the fixed-size `EntityIds` array inside the memory buffer, extract the targets' Cartesian positions, and draw a connection using `Raylib.DrawLineEx` (e.g., a translucent red line indicating threat focus).

By pushing the visualization down into the `IVisualizerAdapter` or an `IMapLayer`, the links will render continuously and accurately track moving targets at 60 FPS, completely decoupled from the tool that authored them.

How do we handle multi-select targets for the Link Tool?

To handle multi-select targets elegantly, we must recognize that "multi-select" can happen on two different axes:

-   **Multiple Perceivers, One Target:** An operator selects a platoon (N entities) and commands them all to track a single hostile tank.-   **One Perceiver, Multiple Targets:** An operator selects a single sniper and draws a bounding box over a squad of enemies to mark all of them as targets.

Because we have strictly segregated our intents into a pure domain event (`SeedTargetCommand`), our execution layer (`EditorPerceptionSetupSystem`) requires **absolutely zero changes**. It will blindly consume the commands off the memory-bus and update the `TargetMemory` components safely. We only need to orchestrate the UI Port and the adapters.

Here is the clean architecture approach to handling both N-to-1 and 1-to-N linking.

1\. N-to-1: Multiple Perceivers, One Target

We leverage the existing `ISelectionState` to read the operator's current selection. Instead of executing the command against just the right-clicked entity, the `EditorEntityContextMenuHandler` evaluates all currently selected entities.

```
// Inside EditorEntityContextMenuHandler.cs
public void PopulateMenu(Entity entity, IContextMenuBuilder builder)
{
    // If the right-clicked entity has TargetMemory, offer the tool
    if (_repo.HasUnmanagedComponent<TargetMemory>(entity))
    {
        // Pluralize label if multiple valid perceivers are selected
        var validPerceivers = _selectionState.SelectedEntities
            .Where(e => _repo.HasUnmanagedComponent<TargetMemory>(e))
            .ToList();
            
        string label = validPerceivers.Count > 1 
            ? $"Mark Target for {validPerceivers.Count} Units..." 
            : "Mark Target...";

        builder.AddItem(label, async () => 
        {
            // 1. Wait for the operator to pick a single target using the existing async facade
            int targetIndex = await _logic.MapPickService.PickEntityAsync();
            if (targetIndex <= 0) return;

            var targetEntity = _repo.GetEntityByIndex(targetIndex);

            // 2. Fan-out pure domain events to the memory bus
            foreach (var perceiver in validPerceivers)
            {
                _bus.Publish(new SeedTargetCommand 
                { 
                    Perceiver = perceiver, 
                    Target = targetEntity, 
                    ScoreBoost = 100f 
                });
            }
        });
    }
}
```

2\. 1-to-N: One Perceiver, Multiple Targets (Area Pick)

If the operator wants to mark an entire hostile squad at once, we need to extend our `IMapPickService` port to support area selection. We will reuse the framework's existing `BoxSelectionTool` so we do not violate the DRY principle.

**Extend the Port:**

```
// Hrot.UI.Common/Facades/IMapPickService.cs
public interface IMapPickService
{
    Task<GeoPoint> PickLocationAsync(CancellationToken ct = default);
    Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default);
    
    // NEW: Capability to pick multiple targets via an area box
    Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default);
}
```

**Implement the Offline Adapter:** In the `Hrot.Editor` adapter, we wrap the native `BoxSelectionTool` in a `TaskCompletionSource`. However, because `BoxSelectionTool` normally requires a `startPos` on mouse-down, we will create a lightweight wrapper tool (`ModalBoxSelectionTool`) that waits for the user's first click, delegates to `BoxSelectionTool` for the drag, and resolves the task on mouse-up.

```
// Hrot.Editor/Adapters/EditorMapPickAdapter.cs
public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default)
{
    var tcs = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);

    // Apply the exact same IEntityFilterFactory logic to build the mask
    var filter = _filterFactory.CreateFilter(filterPresets ?? Array.Empty<string>());

    // We instantiate a modal wrapper that waits for a drag gesture, internally 
    // using the framework's BoxSelectionTool to gather the Entity list [3, 4].
    var tool = new ModalBoxSelectionTool(_view, _query, _adapter, filter, 
        onComplete: (List<Entity> selectedEntities) =>
        {
            var ids = selectedEntities.Select(e => e.Index).ToList();
            tcs.TrySetResult(ids);
        },
        onCancel: () => tcs.TrySetCanceled());

    if (ct.CanBeCanceled)
    {
        ct.Register(() => 
        {
            if (_canvas.ActiveTool == tool) _canvas.PopTool();
            tcs.TrySetCanceled();
        });
    }

    _canvas.PushTool(tool);
    return tcs.Task;
}
```

3\. The Composition (N-to-M Linking)

By combining these two patterns in the UI layer, your operators can perform complex N-to-M assignments entirely through the UI.

```
// Context Menu Handler
builder.AddItem("Mark Area Targets...", async () => 
{
    // Wait for the bounding box
    var targetIndices = await _logic.MapPickService.PickAreaEntitiesAsync();

    // Fan-out: N Perceivers * M Targets
    foreach (var targetId in targetIndices)
    {
        var targetEntity = _repo.GetEntityByIndex(targetId);
        foreach (var perceiver in validPerceivers)
        {
            _bus.Publish(new SeedTargetCommand 
            { 
                Perceiver = perceiver, 
                Target = targetEntity, 
                ScoreBoost = 100f 
            });
        }
    }
});
```

**Why this is clean:**

-   **Zero ECS Pollution:** The execution system doesn't know what a bounding box or a selection state is. It just processes `SeedTargetCommand` events.-   **Zero Network Pollution:** The UI handles the combinatorial expansion. If this UI panel is later used in the networked `ExCon` application, it simply calls `ExConLogic.MapPickService.PickAreaEntitiesAsync`, which translates the request into a DDS `CMD_PICK_AREA` over the network, bringing back the IDs to generate the exact same batch of intent events.

Does the PerceptionMapLayer need its own ECS system?

No, it does not. In our Vis2D architecture, visual map layers do not require a dedicated ECS system (`ComponentSystem` or `IEcsModuleSystem`).

Map layers strictly implement the `IMapLayer` interface, which defines the `Update(dt)` and `Draw(RenderContext ctx)` presentation lifecycle hooks. You simply inject an `ISimulationView` and a pre-built `EntityQuery` directly into the layer's constructor, perfectly matching the pattern used by our existing presentation layers like `EntityRenderLayer` and `RouteRenderLayer`.

For the `PerceptionMapLayer`, you would configure the query in the constructor:

```
_query = view.Query().With<TargetMemory>().With<SimTransform>().Build();
```

During the application's rendering pipeline, the `MapCanvas.Draw()` method sequentially invokes `Draw(RenderContext ctx)` on all registered and visible layers. Inside that `Draw` implementation, you iterate over the query, safely extract the read-only memory buffers via `GetComponentRO`, and issue your `Raylib.DrawLineEx` calls directly.

This design enforces a pristine Clean Architecture boundary. The rendering logic executes entirely within the application's presentation phase (e.g., inside the `OnDrawWorld()` overrides). By relying solely on read-only views of the ECS memory during the draw pass, we avoid polluting the highly-optimized FDP simulation kernel schedule with GUI presentation systems.

How will the FormationRoster lines be rendered on the map?

The rendering of `FormationRoster` lines is completely encapsulated within the presentation layer, specifically in the `IVisualizerAdapter` implementation (such as `VehicleVisualizer`). This maintains a strict architectural boundary where the visualization code only queries ECS state without mutating it or coupling to the underlying formation simulation logic.

During the render pass, the visualizer checks if the current entity possesses a `FormationRoster` component. If the entity is a leader with active members (`roster.Count > 0`), the rendering executes as follows:

-   **Leader Highlight:** It draws a prominent magenta ring around the leader entity's position using `Raylib.DrawRing`.-   **Follower Evaluation:** It iterates through the roster's member entities, starting at index 1 (since index 0 is always the leader itself).-   **Line Drawing:** For each follower that is currently alive and possesses a `SimTransform` component, the visualizer extracts the follower's Cartesian world-space coordinates. It then uses `Raylib.DrawLineEx` to render a translucent magenta line (`new Color(255, 0, 255, 128)`) from the leader's position directly to the follower's position.

Because the `FormationRoster` uses a fixed-capacity unmanaged array for its members, this iteration and rendering process is extremely fast and generates zero heap allocations on the 60 FPS hot path, perfectly adhering to our performance and clean architecture constraints.

What does the ZoneEditorPanel's interface look like for obstacles?

The `ZoneEditorPanel` handles obstacle authoring through a strictly segregated port, ensuring the UI knows absolutely nothing about the underlying map canvas, Vis2D tools, or ECS memory.

Because the engine's narrow-phase solver evaluates static physics using `Intersection2D.RaycastCircle`, obstacles are strictly represented as upright, non-oriented 2.5D cylinders. Therefore, our UI only needs to capture a single spatial parameter: the radius.

Here is the exact interface contract (the Port) that the panel relies on:

```
public interface IZoneAuthoringController
{
    // Other methods...
    
    void StartObstaclePlacementMode(string activeZoneName, float radius);
}
```

Inside the `ZoneEditorPanel` (the Consumer), we maintain a simple backing field for the radius and render the ImGui controls. When the operator clicks the placement button, the panel delegates the action to the injected controller:

```
public class ZoneEditorPanel
{
    private string _zoneName = "urban_combat_zone";
    private float _obstacleRadius = 5.0f;

    public void DrawContent(IZoneAuthoringController controller)
    {
        // ... zone name and road network inputs ...

        ImGui.Separator();
        
        // Expose a slider for the 2.5D cylinder radius
        ImGui.SliderFloat("Obstacle Radius (m)", ref _obstacleRadius, 1.0f, 50.0f);
        
        if (ImGui.Button("Place LOS Obstacle"))
        {
            // The UI delegates the intent. It has no idea if this triggers a DDS
            // network message or pushes a Raylib tool onto a local canvas.
            controller.StartObstaclePlacementMode(_zoneName, _obstacleRadius);
        }
    }
}
```

By passing the `_zoneName` and `_obstacleRadius` through the `IZoneAuthoringController` abstraction, we perfectly adhere to the Dependency Inversion Principle. The presentation layer remains purely data-driven, while the host application (like the `Hrot.Editor`) handles the actual map interaction by pushing an `ObstaclePlacementTool` onto its local canvas and subsequently emitting the domain events to spawn the `PhysicsCollider` entities.

How will the Editor's save process incorporate these new Zone DTOs?

The save pipeline achieves perfect separation of concerns by utilizing `ScenarioFileService` as an application-layer orchestrator. The `ScenarioFileService.SaveScenario` method is updated to construct a comprehensive scenario envelope. To maintain strict architectural boundaries, the service relies on constructor injection to obtain an `IZoneManagerService`.

First, it delegates the dynamic state extraction to the framework by calling `_fdpSerializer.Serialize(repo, new ScenarioHeader("Hrot.Scenario"))` to generate an intermediate JSON DOM representing the dynamic ECS entities. Next, it retrieves the static environment data by invoking `_zoneManagerService.GetActiveZones()`. Behind this interface, the manager service safely queries the ECS for the `ZoneEnvironmentData` singleton and the unmanaged static `PhysicsCollider` entities, translating them into pure `ZoneDefinitionDto` and `ZoneObstacleDto` records without leaking engine primitives.

The service then composes a strongly-typed `HrotScenarioEnvelopeDto` containing the `Header`, the resolved `Zones` dictionary, and the dynamic `Entities` block extracted from the FDP DOM. Finally, it writes the entire structure to disk using `JsonSerializer.Serialize` configured with a unified `HrotJsonOptions` instance. This shared configuration enforces a camel-case naming policy and ignores null values, ensuring that if no zones are active, the `Zones` property is omitted from the JSON payload entirely to preserve backward compatibility. This design guarantees that our persistent storage mechanism remains entirely decoupled from UI input paradigms and raw memory layout.

How can we filter the availble behavior per the type of the entity?

To enforce strict entity-to-behavior constraints (e.g., an Infantry Soldier can use "InfantryCombat", an APC uses "ConvoyEscort", and a Civilian uses "WanderCivil") without violating Clean Architecture, we must rely on our **Ports and Adapters** design.

Because the `MissionPanel` (the UI View) is entirely ignorant of the ECS engine and the CycloneDDS network, it cannot directly inspect an entity's capabilities or its `BrainTier`. Instead, we must pass the context of _which_ entity is selected through the port, and let the backend adapters resolve the business rules.

Here is the exact architectural blueprint to achieve this natively across both the standalone Editor and the distributed ExCon.

1\. Update the UI Port (`Hrot.UI.Common`)

First, we modify the segregated `IMissionEditorService` interface. We change the capability query to accept the `entityId` so the adapters have the context they need.

```
namespace Hrot.UI.Common.Facades
{
    public interface IMissionEditorService
    {
        // CHANGED: The UI now passes the selected entity's ID to ask for applicable behaviors
        IReadOnlyList<string> GetAvailableBehaviors(long entityId);

        (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId);
        Task<MissionCommitResult> CommitMissionAsync(long entityId, MissionPlan newPlan, long baseVersion);
        Task<MissionCommitResult> SendControlCommandAsync(long entityId, eMissionCommandType type, Guid taskId);
    }
}
```

Inside the `MissionPanel`, you simply update the call:

```
// Inside MissionPanel.DrawContent
var availableBehaviors = logic.MissionEditorService.GetAvailableBehaviors(_selectedEntityId);
```

2\. Create the Shared Capability Catalog (`Hrot.Map.Definitions`)

We must avoid duplicating the filtering rules in both ExCon and the Editor. The cleanest approach is to define a statically typed capability map alongside the TKB definitions in the `Hrot.Map.Definitions` project. This library is already shared by all subsystems.

```
using System.Collections.Generic;
using Hrot.Map.Common;

namespace Hrot.Map.Definitions.Tkb
{
    public static class BehaviorCatalog
    {
        private static readonly IReadOnlyList<string> _civilianBehaviors = new[] { "WanderCivil", "PanicFlee" };
        private static readonly IReadOnlyList<string> _vehicleBehaviors = new[] { "MoveToLocation", "FollowRoute", "JoinFormation" };
        private static readonly IReadOnlyList<string> _apcBehaviors = new[] { "ConvoyEscort", "MoveToLocation", "FollowRoute" };
        private static readonly IReadOnlyList<string> _infantryBehaviors = new[] { "InfantryCombat", "MoveToLocation", "JoinFormation" };
        private static readonly IReadOnlyList<string> _insurgentBehaviors = new[] { "Ambush", "MoveToLocation" };

        public static IReadOnlyList<string> GetValidBehaviors(long tkbType)
        {
            return tkbType switch
            {
                TkbEntityTypes.CivilianPedestrian => _civilianBehaviors,
                TkbEntityTypes.CivilianCar        => _civilianBehaviors,
                TkbEntityTypes.MilitaryApc        => _apcBehaviors,
                TkbEntityTypes.InfantrySoldier    => _infantryBehaviors,
                TkbEntityTypes.Insurgent          => _insurgentBehaviors,
                // Fallback for standard vehicles (Tanks, IFVs, HMMWV)
                _ => _vehicleBehaviors 
            };
        }
    }
}
```

3\. The Offline Editor Adapter (`EditorMissionService`)

In the `Hrot.Editor` monolith, the adapter receives the `entityId` (which corresponds to the ECS entity index). It safely queries the local `EntityRepository` to read the `TkbIdentity` component at memory-bus speeds, and then evaluates the shared catalog.

```
// Hrot.Editor/Adapters/EditorMissionService.cs
using Hrot.Map.Definitions.Tkb;
using FDP.Toolkit.Replication.Components;

public class EditorMissionService : IMissionEditorService
{
    private readonly EntityRepository _repo;
    private readonly BehaviorRegistry _registry; // Existing FDP registry

    public IReadOnlyList<string> GetAvailableBehaviors(long entityId)
    {
        var entity = _repo.GetEntityByIndex((int)entityId);
        if (!_repo.IsAlive(entity)) return Array.Empty<string>();

        // 1. Get the entity's blueprint type
        if (!_repo.HasUnmanagedComponent<TkbIdentity>(entity)) 
            return Array.Empty<string>();

        long tkbType = _repo.GetUnmanagedComponentRO<TkbIdentity>(entity).TkbType;

        // 2. Fetch the allowed string names from the shared catalog
        var allowedNames = BehaviorCatalog.GetValidBehaviors(tkbType);

        // 3. Ensure the local FDP engine actually has these behaviors registered
        // (Prevents the UI from offering a behavior that the engine can't execute)
        return allowedNames.Where(name => _registry.TryGetId(name, out _)).ToList();
    }
}
```

4\. The ExCon Adapter (`MissionEditorService`)

In the lightweight `ExCon` node, there is no FDP engine or `BehaviorRegistry`. The UI simply needs to know what behaviors are conceptually valid for the entity selected. The adapter queries the `IDerRepo` to get the `TkbType` and applies the exact same shared catalog.

```
// Hrot.ExCon/Services/MissionEditorService.cs
using Hrot.Map.Definitions.Tkb;

public sealed class MissionEditorService : IMissionEditorService
{
    private readonly IDerRepo _repo;

    public IReadOnlyList<string> GetAvailableBehaviors(long entityId)
    {
        // Read the domain entity from the thread-safe DER network replica
        var entity = _repo.GetEntity((int)entityId);
        
        if (entity == null) 
            return Array.Empty<string>();

        // Map the TKB type directly to the allowed behaviors using the shared catalog
        return BehaviorCatalog.GetValidBehaviors(entity.TkbType);
    }
}
```

Why this is the correct architectural approach:

-   **Zero Network Round-Trips:** When the operator clicks an entity in the UI, the ExCon doesn't need to ask the cluster what behaviors are allowed. It resolves them locally in microseconds.-   **Total UI Ignorance:** The `MissionPanel` stays completely data-driven. It blindly renders whatever strings the adapter hands it.-   **Strict Anti-Corruption Layer:** The FDP Engine's `BehaviorRegistry` doesn't get polluted with application-level `TkbType` business rules. The mapping lives exactly where it should: in `Hrot.Map.Definitions.Tkb` alongside the vehicle physics and visual definitions.

pls summarize the concepts and refactors and new features we have just discussed.

To satisfy the requirements for the `Hrot.Editor` while maintaining a pristine Clean Architecture, we have engineered a comprehensive **Ports and Adapters (Hexagonal Architecture)** refactoring strategy. By rigorously applying the Dependency Inversion Principle (DIP), we ensure the UI layer remains completely ignorant of both the CycloneDDS network layer and the raw ECS memory layouts.

Here is the summary of the architectural concepts, refactors, and new features we established to fulfill your requirements:

1\. Core UI Decoupling (The Ports & Adapters Pattern)

We extracted the tightly coupled ExCon panels into a shared, infrastructure-agnostic library (`Hrot.UI.Common`). We segregated their interfaces into pure Read (Query) and Write (Command) ports, allowing us to build two distinct adapters: one for the networked `ExCon` node and one for the offline, memory-bus speed `Hrot.Editor`.

-   **Shared ORBAT Panel:** Extracted into a shared ImGui component that consumes pure view models (`OrbatNodeViewModel`). The offline editor adapter safely builds the hierarchical tree directly from local ECS memory, completely bypassing `IDerRepo` and network types.-   **Mission Editor Panel:** Refactored to depend strictly on an `IMissionEditorService` facade. The offline adapter handles commits by dropping a `MissionControlIntent` directly onto the local `FdpEventBus`, invoking the ECS engine natively.-   **Map Layer & Config Panel:** Segregated into `IMapConfigController`. Instead of blasting JSON patches over DDS, the offline editor adapter injects operator selections directly into the local `MapUserConfig` singleton.-   **Preview Mode Toggle:** Extracted into an `IPreviewController`. In the offline editor, this bypasses the distributed orchestrator entirely and talks directly to the `PreviewClusterOpHandler` to capture and rewind microsecond-fast ECS memory snapshots.-   **Entity & Empty Space Context Menus:** Decoupled via `IContextMenuBuilder` and a shared `SharedContextMenuPopulator`. The logic determining menu actions (like "Edit Route" or "Measure") is written exactly once and resolves locally via memory-bus domain commands like `DestroyEntityCommand`.-   **Entity Creator & Map Tools:** Moved the data-driven `SpawnerPanel` behind an `ISpawnController`. The map tools themselves (`CreationTool`, `EditTool`, `RouteEditTool`) were already perfectly decoupled, emitting pure domain events that the offline editor now consumes directly, entirely ignoring the Anti-Corruption Layer (ACL) egress translators.

2\. New Features for Urban Combat Scenario Authoring

To replicate the programmatic Urban Combat demo purely through the data-driven UI, we designed several new interactions rooted in strict Command Query Responsibility Segregation (CQRS) and domain events.

-   **Embarkation & Cargo Management (Drag-and-Drop):**
    -   We added ImGui payload drag-and-drop to the shared ORBAT panel.-   The UI emits a pure `EmbarkEntityCommand`.-   The execution is handled safely by an `EditorCargoSystem` that validates the strict `Capacity` of the `PassengerBuffer` component, entirely isolating domain invariants from the presentation layer.-   **Target Memory Seeding (N-to-M Link Tool):**
    -   Instead of writing a redundant tool, we reused the framework's highly optimized, allocation-free `EntityPickerTool` and `BoxSelectionTool` behind an async `IMapPickService` facade.-   The UI emits a pure `SeedTargetCommand`. The backend `EditorPerceptionSetupSystem` safely injects target references into the unsafe fixed-size arrays of the `TargetMemory` component during the thread-safe `Input` phase.-   **Static Zone & Obstacle Authoring:**
    -   Created a `ZoneEditorPanel` to assign `RoadNetworkPath` definitions and define static obstacle radii.-   The editor adapter uses a lightweight canvas tool to emit an event that spawns an unmanaged `SimTransform` + `PhysicsCollider` entity.-   This preserves the integrity of the save pipeline, as the `ZoneManagerService` easily translates these components into `ZoneObstacleDto` records for the `HrotScenarioEnvelopeDto` file.-   **Dynamic Mission/Behavior Catalog Filtering:**
    -   Eliminated the hardcoded behavior strings (like `MoveToLocation`) from the `MissionPanel`.-   The UI now dynamically polls the `IMissionEditorService` for available behaviors.-   We introduced a statically typed capability map (`BehaviorCatalog`) alongside the `TkbEntityTypes` in `Hrot.Map.Definitions`. The backend adapter evaluates the entity's `TkbIdentity` to restrict the behavior list (e.g., ensuring an APC gets `ConvoyEscort` while an Insurgent gets `Ambush`) without polluting the engine's generic `BehaviorRegistry`.

By executing these refactors, you achieve 100% UI code reuse between your lightweight distributed C2 client (`ExCon`) and your offline monolithic authoring environment (`Hrot.Editor`). The UI remains fiercely data-driven, while domain validation and ECS memory safety are strictly confined to the engine execution phases.

I would like thse new features to be tested in headless configuration. By separating the UI from the execution machinery thois should be possible in completetely autonomous and CI-friendly headless way (some kind of integration tests). Pls suggest the tests so they cover the Hrot.Editor capabilities.

You have hit on exactly why we apply the Ports and Adapters pattern in the first place. By completely segregating the ImGui presentation layer from the application intents, your CI pipeline can exercise 100% of the scenario authoring business logic completely headlessly.

Because we decoupled the UI, we can drop our adapters directly into the existing `EditorHarness`. This harness boots the `SimHostCoreLogicPack`, `CgfLogicPack`, and `ScenarioEditorModule` in a pure offline memory space with zero CycloneDDS participants, giving us sub-millisecond, deterministic integration tests.

Here is the test suite I propose to guarantee the integrity of your new Urban Combat authoring features. We will add a new test class, `EditorAuthoringIntegrationTests`, to the `Hrot.ClusterRunner.Integration.Tests` project.

1\. Embarkation & Cargo Management Test

We must prove that dragging a soldier onto an APC correctly mutates the ECS structural invariants (capacity limits and mobility stripping) without touching UI code.

```
[Fact]
public void Embarkation_ValidRequest_UpdatesPassengerBufferAndStripsCapabilities()
{
    using var harness = new EditorHarness();
    var adapter = new EditorOrbatAdapter(harness.Repo, harness.Bus);

    // 1. Arrange: Spawn APC and Soldier
    var apc = harness.Repo.CreateEntity();
    harness.Repo.AddComponent(apc, new PassengerBuffer());
    
    var soldier = harness.Repo.CreateEntity();
    harness.Repo.AddComponent(soldier, new ActorCapabilityState 
    { 
        Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot 
    });

    // 2. Act: Simulate the UI drag-and-drop intent
    adapter.RequestEmbark(soldier.Index, apc.Index);
    harness.PumpFrames(1); // Flush the FdpEventBus and run EditorCargoSystem

    // 3. Assert: Domain invariants were enforced
    var buffer = harness.Repo.GetComponentRO<PassengerBuffer>(apc);
    Assert.Equal(1, buffer.Count);
    Assert.Equal(soldier, buffer.Passengers);

    var caps = harness.Repo.GetComponentRO<ActorCapabilityState>(soldier);
    Assert.False(caps.Capabilities.HasFlag(ActorCapabilities.CanMove));
    Assert.True(harness.Repo.HasComponent<IsEmbarkedTag>(soldier));
}
```

2\. Target Memory Seeding Test

We must prove that the Context Menu's async picking workflow safely injects target pointers into the FDP engine's unsafe memory buffers.

```
[Fact]
public void TargetLinkTool_SeedsTargetMemory_WithImmediateThreatBoost()
{
    using var harness = new EditorHarness();
    
    // 1. Arrange: Insurgent (Perceiver) and APC (Target)
    var insurgent = harness.Repo.CreateEntity();
    harness.Repo.AddComponent(insurgent, new TargetMemory());
    
    var apc = harness.Repo.CreateEntity();
    harness.Repo.AddComponent(apc, new SimTransform { Position = new Vector3(10, 20, 0) });

    // 2. Act: Publish the decoupled domain command directly
    harness.Bus.Publish(new SeedTargetCommand 
    { 
        Perceiver = insurgent, 
        Target = apc, 
        ScoreBoost = 100f 
    });
    harness.PumpFrames(1); // Run EditorPerceptionSetupSystem

    // 3. Assert: The unsafe fixed array was safely populated
    ref readonly var mem = ref harness.Repo.GetComponentRO<TargetMemory>(insurgent);
    Assert.Equal(1, mem.Count);
    Assert.Equal((long)apc.PackedValue, mem.EntityIds);
    Assert.True(mem.Scores >= 100f); // Threat boost applied
}
```

3\. Static Zone Authoring & Save Pipeline Test

We must prove that placing an obstacle creates a `PhysicsCollider` and that the `ScenarioFileService` successfully packages it into the `ZoneDefinitionDto` without the UI's involvement.

```
[Fact]
public void ZoneAuthoring_ObstaclePlacement_SerializesToZoneDto()
{
    using var harness = new EditorHarness();
    var adapter = new EditorZoneAdapter(new MapCanvas(), harness.Bus);
    
    string zoneName = "urban_combat_zone";
    
    // 1. Act: Simulate the UI configuring a zone and dropping an obstacle
    adapter.SetRoadNetworkPath(zoneName, "Assets/sample_road.json");
    
    // Simulate Map Canvas tool click emitting the command
    harness.Bus.Publish(new SpawnZoneObstacleCommand 
    { 
        ZoneName = zoneName, 
        Position = new Vector2(50, 25), 
        Radius = 10f 
    });
    harness.PumpFrames(1);

    // 2. Act: Save the scenario to a temporary file via the application service
    var tempFile = Path.GetTempFileName();
    try
    {
        harness.Editor.SaveScenario(tempFile);

        // 3. Assert: The application layer translated ECS state into the correct DTO
        var json = File.ReadAllText(tempFile);
        var envelope = JsonSerializer.Deserialize<HrotScenarioEnvelopeDto>(json, HrotSerializerOptions.Default);
        
        Assert.NotNull(envelope.Zones);
        Assert.True(envelope.Zones.TryGetValue(zoneName, out var zoneDef));
        Assert.Equal("Assets/sample_road.json", zoneDef.RoadNetworkPath);
        Assert.Single(zoneDef.Obstacles);
        Assert.Equal(50f, zoneDef.Obstacles.X);
        Assert.Equal(10f, zoneDef.Obstacles.Radius);
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

4\. Behavior Catalog Filtering Test

We must prove our Anti-Corruption Layer restricts the Mission dropdown options based on the target's `TkbIdentity` blueprint, completely independently of the ImGui combo box.

```
[Fact]
public void MissionEditorService_FiltersAvailableBehaviors_ByTkbBlueprint()
{
    using var harness = new EditorHarness();
    
    // Setup the adapter with the live offline BehaviorRegistry
    var registry = new BehaviorRegistry();
    registry.Register(1001, "WanderCivil", new BehaviorDefinition());
    registry.Register(2003, "Ambush", new BehaviorDefinition());
    var adapter = new EditorMissionService(harness.Bus, harness.Repo, registry);

    // 1. Arrange: Spawn an Insurgent and a Civilian
    var insurgent = harness.Repo.CreateEntity();
    harness.Repo.AddComponent(insurgent, new TkbIdentity { TkbType = TkbEntityTypes.Insurgent });
    
    var civilian = harness.Repo.CreateEntity();
    harness.Repo.AddComponent(civilian, new TkbIdentity { TkbType = TkbEntityTypes.CivilianPedestrian });

    // 2. Act & Assert: The UI adapter queries the capabilities dynamically
    var insurgentBehaviors = adapter.GetAvailableBehaviors(insurgent.Index);
    Assert.Contains("Ambush", insurgentBehaviors);
    Assert.DoesNotContain("WanderCivil", insurgentBehaviors);

    var civilianBehaviors = adapter.GetAvailableBehaviors(civilian.Index);
    Assert.Contains("WanderCivil", civilianBehaviors);
    Assert.DoesNotContain("Ambush", civilianBehaviors);
}
```

Why this approach is optimal

By strictly targeting the adapters (`EditorOrbatAdapter`, `EditorZoneAdapter`, `EditorMissionService`), these tests execute in a few milliseconds without requiring any GPU context, Raylib window, or ImGui contexts. They run directly on the thread-safe `EditorHarness`, proving that the core FDP engine logic and your new Urban Combat scenario features are robust, completely satisfying your CI requirements.

pls write mermaid diagrams describing the features. the building blocks the hrot.editor is built from and how they relate.

Here are the architectural blueprints that map out the pristine Ports and Adapters (Hexagonal) design we have established for the `Hrot.Editor`. By aggressively adhering to the Dependency Inversion Principle (DIP) and Command Query Responsibility Segregation (CQRS), we have completely decoupled the presentation layer from the high-performance FDP simulation kernel.

1\. The Hexagonal Architecture (Ports & Adapters)

This diagram illustrates the macro-architecture of the offline Editor. Notice how the pure presentation components in `Hrot.UI.Common` depend strictly on interface contracts (Ports). The `Hrot.Editor` project injects offline, memory-bus speed Adapters that translate user intent into pure domain events on the local `FdpEventBus`.

```
graph TD
    %% Presentation Layer (Agnostic of Infrastructure)
    subgraph "Hrot.UI.Common (Presentation Layer)"
        direction TB
        UI_Orbat[SharedOrbatPanel]
        UI_Mission[MissionPanel]
        UI_Zone[ZoneEditorPanel]
        UI_Spawner[SpawnerPanel]
        UI_Context[ContextMenuHandler]
    end

    %% Ports (Domain Contracts)
    subgraph "Hrot.UI.Common.Facades (Ports)"
        direction TB
        Port_Orbat[[IOrbatController]]
        Port_Mission[[IMissionEditorService]]
        Port_Zone[[IZoneAuthoringController]]
        Port_Spawn[[ISpawnController]]
        Port_Pick[[IMapPickService]]
    end

    %% Adapters (Offline / Direct Memory)
    subgraph "Hrot.Editor (Adapters)"
        direction TB
        Adp_Orbat(EditorOrbatAdapter)
        Adp_Mission(EditorMissionService)
        Adp_Zone(EditorZoneAdapter)
        Adp_Pick(EditorMapPickAdapter)
    end

    %% Infrastructure / Kernel
    subgraph "FDP Engine (Core Domain)"
        direction TB
        Bus((FdpEventBus))
        Repo[(EntityRepository)]
        
        Sys_Cargo[EditorCargoSystem]
        Sys_Zone[EditorZoneAuthoringSystem]
        Sys_Perception[EditorPerceptionSetupSystem]
        Sys_Core[SimHostCoreLogicPack]
    end

    %% Wiring UI to Ports
    UI_Orbat --> Port_Orbat
    UI_Mission --> Port_Mission
    UI_Zone --> Port_Zone
    UI_Spawner --> Port_Spawn
    UI_Context --> Port_Pick

    %% Wiring Adapters to Ports
    Port_Orbat <|.. Adp_Orbat
    Port_Mission <|.. Adp_Mission
    Port_Zone <|.. Adp_Zone
    Port_Pick <|.. Adp_Pick

    %% Wiring Adapters to the Engine
    Adp_Orbat -->|Publish| Bus
    Adp_Zone -->|Publish| Bus
    Adp_Mission -->|Query| Repo
    Adp_Pick -->|Read Map State| Repo
    UI_Context -->|Publish| Bus

    %% Execution Pipeline
    Bus -->|Consume| Sys_Cargo
    Bus -->|Consume| Sys_Zone
    Bus -->|Consume| Sys_Perception
    
    Sys_Cargo -->|GetComponentRW| Repo
    Sys_Zone -->|CreateEntity| Repo
    Sys_Perception -->|Mutate| Repo
```

2\. Strict CQRS Flow: Target Memory Seeding (Link Tool)

To understand how we maintain memory safety while providing interactive map tools, let us trace the specific workflow of the N-to-1 Target Linking feature. The UI thread never touches raw ECS memory. Instead, the UI awaits an asynchronous pick, then publishes an immutable command. The mutation is safely confined to the thread-safe `InputSystemGroup` phase of the kernel tick.

```
sequenceDiagram
    autonumber
    actor User
    participant ContextMenu as EditorEntityContextMenuHandler
    participant PickPort as IMapPickService (Port)
    participant PickAdp as EditorMapPickAdapter
    participant Canvas as MapCanvas (Vis2D)
    participant Bus as FdpEventBus
    participant Sys as EditorPerceptionSetupSystem
    participant ECS as EntityRepository

    User->>ContextMenu: Right-click -> "Mark Target..."
    ContextMenu->>PickPort: PickEntityAsync()
    PickPort->>PickAdp: (Interface Call)
    PickAdp->>Canvas: PushTool(EntityPickerTool)
    Canvas-->>User: Render crosshair cursor
    
    User->>Canvas: Left-click enemy vehicle
    Canvas-->>PickAdp: Entity Index Resolved
    PickAdp-->>ContextMenu: Return targetIndex
    
    ContextMenu->>ECS: GetEntityByIndex(targetIndex)
    ContextMenu->>Bus: Publish(SeedTargetCommand)
    Note over ContextMenu, Bus: UI Thread finishes. Zero ECS writes.

    %% Next Kernel Tick
    Note over Bus, ECS: Kernel Tick Phase: Input (Main Thread)
    Bus->>Sys: Consume<SeedTargetCommand>()
    loop For each Command
        Sys->>ECS: GetComponentRO<SimTransform>(Target)
        Sys->>ECS: GetComponentRW<TargetMemory>(Perceiver)
        Sys->>Sys: TargetMemory.AddOrUpdateTarget()
    end
    Note over Sys, ECS: Unsafe fixed array mutated safely
```

3\. Editor Composition Root (The Monolith)

Finally, this structural diagram demonstrates how `Hrot.Editor` boots up as a self-contained monolith. By loading the exact same `SimHostCoreLogicPack` and `CgfLogicPack` used in production, the offline Editor perfectly replicates the tactical behaviour and physics of the distributed cluster. Because the network translators (`CycloneNetworkModule`) are intentionally excluded, this environment executes at maximum CPU throughput with zero DDS overhead.

```
classDiagram
    class EditorApplication {
        +NewScenario()
        +SaveScenario(string filePath)
        +LoadScenario(string filePath)
        +SwitchToExternalAsync()
    }

    class ModuleHostKernel {
        +RegisterModule(IEcsModule module)
        +Update()
    }

    class SimHostCoreLogicPack {
        <<IEcsModule>>
        +CombatModule
        +ActionDispatchModule
        +GroundKinematicsModule
    }

    class CgfLogicPack {
        <<IEcsModule>>
        +MissionControlModule
        +CognitiveRuntimeModule
    }

    class ScenarioEditorModule {
        <<IEcsModule>>
        +ScenarioFileService
    }

    class EntityRepository {
        <<ECS World>>
        +FdpEventBus Bus
    }

    EditorApplication *-- ModuleHostKernel
    EditorApplication *-- EntityRepository
    
    ModuleHostKernel o-- SimHostCoreLogicPack : Registers
    ModuleHostKernel o-- CgfLogicPack : Registers
    ModuleHostKernel o-- ScenarioEditorModule : Registers

    note for SimHostCoreLogicPack "Executes Muscle-tier logic\n(Physics, NavExecution, Ballistics)"
    note for CgfLogicPack "Executes Brain-tier logic\n(HSM/BTree, Behaviors, Mission Plans)"
    note for ScenarioEditorModule "Handles File I/O and\nOffline UI Adapters"
```

This architecture ensures that every business rule, validation check, and ECS memory layout constraint is centralized within the FDP engine, isolating the UI components entirely.

