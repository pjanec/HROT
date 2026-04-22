Here are the technical specifications for implementing the polygonal area authoring workflow across the IG, SimHost, and IOS, adhering to the system's "Local Prediction, Global Commit" architecture.

1\. IG Specification: Area Authoring Tool

The IG handles local interaction and renders a temporary preview before committing the shared entity to the network.

-   **Tool Architecture:** Create an `AreaAuthoringTool` (but prioritize reusing an already existing one if suitable) class that implements `FDP.Toolkit.Vis2D.Abstractions.IMapTool`.-   **State Management:** The tool must maintain an internal `List<Vector2>` for committed vertices and a tracking variable for the current mouse position updated via `HandleHover`.-   **Input Handling:** Left-clicks (`HandleClick`) will capture the world-space coordinate and append it to the vertex list. Right-clicking will trigger the final commit sequence and pop the tool from the `MapCanvas`.-   **Local Rendering:** Inside the `Draw` method (which executes within the 2D camera context), use `Raylib` to draw lines between all established vertices, plus an "elastic" preview line connecting the final committed vertex to the current mouse position.-   **Global Commit:** Upon right-click, iterate through the local Cartesian vertices and convert them to geodetic `GeoPoint` objects using the injected `IGeographicTransform`. Construct a `CreateEntityRequest`.-   **Descriptor Payload:** The request must include an `EntityMaster` descriptor with `TkbType` set to `TacGraphic_Area` (8803). It must also include a `MapVisualOverlay` descriptor with `PersistenceMode = MODE_PERSISTENT`, setting the `Points` array to your converted geodetic vertices. Leave the `Owner` field as `default` (all-zeros) so the SimHost assumes authoritative ownership.

2\. SimHost Specification: Area Entity Creation

The SimHost acts as the Scenario Authority. It must be updated to process the incoming overlay geometry and persist it.

-   **TKB Registration:** Add a template definition for `TacGraphic_Area` (8803) in `BdcTkbCatalog` so the system recognizes the type during validation.-   **ECS Component:** Register the managed `EditablePolyline` component (or an equivalent struct) in `SimHostComponentRegistry`. This component will hold the runtime list of vertices for the physics/simulation engine.-   **Descriptor Mapping:** Update `DescriptorMapper.MapToComponents` to decode `EDescriptorType.dtMapVisualOverlay`. The mapper must iterate over the incoming geodetic `Points` array, convert them back to local Cartesian space using `IGeographicTransform.ToCartesian`, and attach the polyline ECS component to the entity.-   **Network Egress:** Create a new `MapVisualOverlayEgressTranslator` implementing `IDescriptorTranslator`. This translator will monitor locally-owned entities with the polyline component and publish their state to the `MapVisualOverlay` DDS topic so the IG can render the final, authoritative shape. Register this translator inside `SimHostModule` alongside existing egress translators.

3\. IOS Specification: Tool Activation

The IOS acts as the pure controller and instructs the IG to switch tools via a JSON Merge Patch.

-   **Constants:** Define a new tool string, such as `"AREA_AUTHORING"`, in `IosLogicConstants` to accompany the existing `"PLACEMENT"` tool name.-   **Logic Facade:** Extend the `IIosLogic` interface and `IosLogic` implementation with a `StartAreaAuthoringMode()` method.-   **Network Publication:** Inside this method, generate a new `ActiveContextId` (UUID). Construct an anonymous object where `interaction.activeTool` is set to `"AREA_AUTHORING"`, serialize it, and publish it via the `_configWriter` as a `MapInteractionConfig` message targeting `MapGroupId = 0` (broadcast).-   **UI Binding:** In `SpawnerPanel.cs`, inject an ImGui button (e.g., "Draw Area"). In the button's click handler, invoke `logic.StartAreaAuthoringMode()`.-   **IG Handshake:** When the IG's `IgApplication` receives this config patch, it will parse the `"AREA_AUTHORING"` command, pop any existing tool, and push the `AreaAuthoringTool` onto the canvas.

--------------
can the existing PointSequenceTool be re-used to avoid re-implementing it, but still send the entity creation request with proper geocoordinates?
---------------

Yes, absolutely. Reusing the existing `PointSequenceTool` is exactly the right architectural move here, and it perfectly aligns with the separation of concerns already established in the codebase.

As a senior developer, let me break down exactly how this works and how you can wire it up without writing a new tool from scratch.


----------

How the `PointSequenceTool` is Designed for Reuse

If you look at the source code for `PointSequenceTool.cs`, you'll notice it has absolutely no knowledge of the DDS network, geodetic transformations, or the ECS. It takes a single `Action<Vector2[]> onFinish` callback in its constructor.

Its only responsibilities are capturing local Cartesian mouse clicks, rendering the elastic "ghost" lines using Raylib, and handing the final array of `Vector2` points back to the caller when the operator right-clicks.

Because of this decoupled design, you can easily repurpose it for area authoring by injecting your geodetic translation and DDS network logic directly via the callback closure.

Implementation: Wiring it up in `IgApplication.cs`

You can implement this entirely inside the IG's `ParseAndApplyConfig` method where the IOS commands are processed. When the IOS sends the `"AREA_AUTHORING"` command, you push the `PointSequenceTool` onto the canvas and use its callback to do the heavy lifting.

Here is the exact implementation you need:

```
// Inside IgApplication.ParseAndApplyConfig()
if (toolEl.GetString() == "AREA_AUTHORING")
{
    // Pop any existing tool to prevent stack accumulation
    if (_canvas.ActiveTool is PointSequenceTool)
        _canvas.PopTool();

    // Instantiate the existing tool and pass our custom closure
    var pathTool = new PointSequenceTool(points =>
    {
        // A polygon requires at least 3 vertices
        if (points.Length < 3) return;

        // 1. Convert local Cartesian Vector2 points to WGS84 Geodetic positions
        var geoPoints = new List<GeoPoint>();
        foreach (var pt in points)
        {
            var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(pt.X, pt.Y, 0f));
            geoPoints.Add(new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt });
        }

        // 2. Build the CreateEntityRequest asking SimHost to take ownership
        var request = new CreateEntityRequest
        {
            RequestId = Guid.NewGuid(),
            Owner = default, // Implicitly assigns ownership to SimHost (Scenario Authority)
            Flags = 0,
            InitialDescriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d = EDescriptorType.dtEntityMaster,
                    // 8803 is the TKB Type for TacGraphic_Area
                    EntityMaster = new EntityMaster { TkbType = TkbEntityTypes.TacGraphic_Area }
                },
                new EntityDescriptorUnion
                {
                    _d = EDescriptorType.dtMapVisualOverlay,
                    MapVisualOverlay = new MapVisualOverlay 
                    {
                        PersistenceMode = PersistenceMode.MODE_PERSISTENT,
                        Points = geoPoints,
                        IsEditable = true,
                        IsClickable = true
                    }
                }
            }
        };

        // 3. Publish to the DDS backbone
        _createEntityDdsWriter.Write(request);
        
        // 4. Return to the default interaction tool
        _canvas.SwitchTool(_interactionTool);
    });

    _canvas.PushTool(pathTool);
}
```

Why this is a superior approach:

-   **Zero Duplicate Code:** You avoid rewriting the local Raylib click-capture and elastic line rendering logic.-   **Proper Projection:** Because `IgApplication` already owns the `_geoTransform` and `_createEntityDdsWriter` dependencies, the lambda safely captures them. It correctly converts the flat screen coordinates to `GeoPoint` (Latitude/Longitude) before dispatching the payload.-   **Global Commit:** The tool stays purely visual ("Local Prediction") and the callback handles the "Global Commit" to the DDS backbone seamlessly.

how to support this SimHost side?

To support this on the SimHost side, you need to implement the backend pipeline that receives the `CreateEntityRequest`, stores the polygon geometry in the ECS, and continuously publishes it to the DDS backbone.

Because SimHost acts as the Scenario Authority, it requires five specific additions to process the new `dtMapVisualOverlay` descriptor correctly.

Here is the step-by-step implementation guide:

1\. Define the ECS Component for Geometry

SimHost needs a managed component to store the multi-vertex list in its local Cartesian space. _(Note: While the IG has an_ _EditablePolyline_ _component, it is best to define a shared or SimHost-specific component for authoritative scenario geometry)._

```
using System.Collections.Generic;
using System.Numerics;
using Fdp.Kernel;

namespace Hrot.SimHost.Components
{
    // Ensure you assign a unique ID in GlobalComponentIds (e.g., 123)
    [ComponentId(GlobalComponentIds.MapOverlayGeometry)]
    public class MapOverlayGeometry
    {
        public List<Vector3> LocalPoints { get; set; } = new();
        public bool IsClosedPolygon { get; set; } = true;
    }
}
```

_Register this in_ _SimHostComponentRegistry.cs__:_ `world.RegisterManagedComponent<MapOverlayGeometry>();`

2\. Update `DescriptorMapper.cs`

When `CreateEntityRequestSystem` receives the request, it uses `DescriptorMapper.MapToComponents` to convert DDS descriptors into ECS components. You need to add a branch to parse `dtMapVisualOverlay` and project the WGS84 Geodetic coordinates back to flat Cartesian space.

```
// Inside Hrot.Map.Common.Replication.Utils.DescriptorMapper.MapToComponents()
foreach (var d in descriptors)
{
    // ... existing dtWorldPos mapping ...

    if (d._d == EDescriptorType.dtMapVisualOverlay)
    {
        var geometry = new MapOverlayGeometry();
        var overlayData = d.MapVisualOverlay;

        if (geoTransform != null && overlayData.Points != null)
        {
            foreach (var geoPt in overlayData.Points)
            {
                var cart = geoTransform.ToCartesian(geoPt.Latitude, geoPt.Longitude, geoPt.Altitude);
                geometry.LocalPoints.Add(new Vector3((float)cart.X, (float)cart.Y, (float)cart.Z));
            }
        }
        
        components.Add(geometry);
    }
}
```

3\. Register the TKB Template

`CreateEntityRequestSystem` validates the incoming `TkbType` against the `ITkbDatabase` and returns a 404 error if it doesn't exist. You must register `TacGraphic_Area` (8803) during startup.

```
// Inside Hrot.SimHost.Setup.DemoTkbSetup.RegisterAll() (or wherever your templates are defined)
private static void RegisterTacGraphicArea(ITkbDatabase tkb)
{
    var t = new TkbTemplate("TacGraphic_Area", 8803);
    
    // Add default network components required by the spawning system
    t.AddComponent(new NetworkIdentity());
    t.AddComponent(new NetworkOwnership());
    
    // It's a managed component, so we use AddManagedComponent factory delegate
    t.AddManagedComponent(() => new MapOverlayGeometry());
    
    tkb.Register(t);
}
```

4\. Create the Egress Translator

To make the polygon visible to the IG and IOS clients, SimHost must publish it back to the DDS backbone. Create a new egress translator that implements `IDescriptorTranslator`.

```
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Utilities;
using ModuleHost.Core.Abstractions;
using Hrot.SimHost.Components;

namespace Hrot.SimHost.Translators
{
    public class MapVisualOverlayEgressTranslator : IDescriptorTranslator
    {
        public string TopicName => "MapVisualOverlay";
        public long DescriptorOrdinal => (long)EDescriptorType.dtMapVisualOverlay;

        private readonly DdsWriter<MapVisualOverlay> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;

        public MapVisualOverlayEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, IGeographicTransform geoTransform)
        {
            _writer = new DdsWriter<MapVisualOverlay>(participant, TopicName);
            _entityMap = entityMap;
            _geoTransform = geoTransform;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .WithManaged<MapOverlayGeometry>()
                .With<NetworkIdentity>()
                .With<NetworkOwnership>()
                .Build();

            foreach (var entity in query)
            {
                // Only publish entities that this SimHost owns
                if (!view.HasAuthority(entity, DescriptorOrdinal)) continue;

                // For static areas, we only need to publish if it's dirty/newly created
                if (!SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal)) continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                var geometry = view.GetManagedComponentRO<MapOverlayGeometry>(entity);

                var geoPoints = new List<GeoPoint>();
                foreach (var pt in geometry.LocalPoints)
                {
                    var (lat, lon, alt) = _geoTransform.ToGeodetic(pt);
                    geoPoints.Add(new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt });
                }

                _writer.Write(new MapVisualOverlay
                {
                    EntityId = (int)netId.Value,
                    PersistenceMode = PersistenceMode.MODE_PERSISTENT,
                    Points = geoPoints,
                    IsEditable = true,
                    IsClickable = true
                });

                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { /* Handle DDS disposal here */ }
    }
}
```

5\. Wire up the Translator

Finally, instantiate your new translator inside `SimHostModule.cs` where the Cyclone network module is assembled, and add it to the custom translators list.

```
// Inside SimHostModule.cs constructor
var overlayEgress = new MapVisualOverlayEgressTranslator(participant, entityMap, geoTransform);
translators.Add(overlayEgress);
```

**Architecture Flow Complete:** When the IG sends the `CreateEntityRequest`, the `CreateEntityRequestSystem` will successfully allocate an ID, map the WGS84 vertices to your new `MapOverlayGeometry` component, and fire a `SpawnEntityCommand`. On the next frame, `NetworkSpawningSystem` instantiates it, and your `MapVisualOverlayEgressTranslator` immediately detects the un-published state, converts the geometry back, and broadcasts the authoritative `MapVisualOverlay` payload to all listening clients.


Add integration test to Hrot.ClusterRunner.Integration.Tests as needede to verify it all works. Keep fixing until tests pass.

The solution must compile.