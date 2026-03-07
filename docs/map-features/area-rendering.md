does the IG contains rendering code for the areas? I guess the area as any other tactical drawing might come with some extra styling.
------------
Looking at the provided source code, the IG **does not yet contain the actual rendering code for persistent tactical areas or multi-vertex lines**, even though the network data model and styling architecture are fully prepared to support them.

Here is the exact breakdown of what is implemented in the codebase versus what is missing:

**1\. The Styling Data Model (Fully Supported)** You are absolutely correct that tactical drawings require extra styling. The DDS protocol and architecture are already designed to handle this via the `MapVisualOverlay` descriptor, which explicitly defines a 3-layer style resolution strategy:

-   **JSON Override (****StyleOverrideJson****):** The highest priority. This allows the IOS to send instance-specific fine-grained JSON overrides (e.g., `{ "colorOverride": "#0000FF", "lineWidth": 3 }`).-   **Preset Name (****StylePresetName****):** A named style variant like `"Hostile_Dashed"`.-   **TKB Default:** The fallback baseline properties if no overrides are provided, resolved via the `tkbTypeId`.

The descriptor also holds the multi-vertex area geometry in its `Points` sequence.

**2\. The Rendering Engine (Currently Missing)** While the IG has local rendering logic inside its _interaction tools_ (for instance, the `EditTool` uses `Raylib.DrawLineEx` to draw yellow "ghost" polylines during vertex dragging, and the `MeasureTool` draws cyan measurement lines), the authoritative entity rendering pipeline lacks support for polygons.

Currently, the IG relies heavily on the `SstVisualizerAdapter` for drawing entities. If you look at its implementation of `GetPosition` and `Render`, it strictly expects a single Cartesian coordinate from `SimTransform.Position` and renders a single `Texture2D` icon, a fallback circle, and a damage bar at that point. It has no logic to iterate over a list of vertices or parse the `MapVisualOverlay` style JSON to draw filled polygons.

**3\. How We Would Implement It** To render these styled areas, you would need to extend the IG rendering pipeline by implementing a dedicated `IMapLayer` (similar to the existing `EntityRenderLayer`) or a specialized `IVisualizerAdapter`.

This new renderer would:

-   Query the ECS for entities containing the `EditablePolyline` component (which stores the local Cartesian vertices).-   Read the resolved styles (parsing the JSON for fill colors, line thickness, and dash patterns).-   Execute standard Raylib polygon drawing functions (such as `Raylib.DrawTriangleFan` for the filled area and `Raylib.DrawLineStrip` for the border) inside the `MapCanvas` rendering loop.
----------
pls write a specification for this rendering part with styling, and suggest the implementation (simplified pseudocode)
--------------
As a senior software developer, I can outline exactly how we integrate the rendering of styled polygon areas into the existing IG architecture. We will tap directly into the Entity-Component System (ECS) and the `IMapLayer` rendering pipeline, respecting the established 3-layer styling resolution priority , .

Specification: Map Overlay Rendering Pipeline

**1\. Data Model & ECS Components**

-   **Geometry Storage:** We will utilize the existing `EditablePolyline` managed component, which natively stores the `List<Vector2> Points` in local Cartesian coordinates .-   **Styling Component:** We must introduce a new managed component (e.g., `MapOverlayStyle`) to cache the parsed visual properties. The existing `ResolvedStyle` component is highly optimized as an unmanaged struct strictly limited to 64 bytes (`MaxStyleBytes`) and is tailored for unit icons (tint, texture, damage) , . Tactical areas require different data (fill color, border color, line thickness, dash patterns).-   **Style Resolution:** A new `OverlayStyleResolutionSystem` will run in the `PostSimulation` phase . It will read the `MapVisualOverlay` DDS descriptor and apply the 3-layer styling priority: first checking `StyleOverrideJson` for instance-specific tweaks, falling back to `StylePresetName` (e.g., "Hostile\_Dashed"), and ultimately defaulting to the TKB baseline .

**2\. The Rendering Layer**

-   **Implementation:** We will create a `MapOverlayRenderLayer` that implements `FDP.Toolkit.Vis2D.Abstractions.IMapLayer` .-   **Layer Filtering:** We will assign it a `LayerBitIndex` corresponding to the `tactical_graphics` configuration layer so the operator can toggle its visibility . During `Draw()`, the layer will respect the `ctx.VisibleLayersMask` .-   **Execution Context:** Because `MapCanvas.Draw()` automatically calls `Camera.BeginMode()` before invoking our layer's `Draw()` method, all Raylib rendering calls will automatically be scaled and panned according to the current 2D world camera .

**3\. Hit Testing (Picking)**

-   To support selection and context menus, the layer must implement `PickEntity(Vector2 worldPos)` . We will implement a standard Point-in-Polygon (Ray-Casting) algorithm against the `EditablePolyline` vertices to determine if the user clicked inside the area.

\--------------------------------------------------------------------------------

Suggested Implementation (C# Pseudocode)

Here is how the implementation looks within the IG's current architectural boundaries.

1\. The Style Component

```
using Fdp.Kernel;
using Raylib_cs;

namespace Bagira.IG.Components
{
    // Registered as a managed component due to potential complex style needs
    [ComponentId(GlobalComponentIds.MapOverlayStyle)] // Assumes ID assigned in registry
    public class MapOverlayStyle
    {
        public Color FillColor { get; set; } = new Color(0, 0, 0, 0); // Transparent default
        public Color BorderColor { get; set; } = Color.White;
        public float LineThickness { get; set; } = 2.0f;
        public bool IsClosed { get; set; } = true;
    }
}
```

2\. The Rendering Layer

```
using System.Numerics;
using Bagira.IG.Components;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Bagira.IG.Layers
{
    /// <summary>
    /// Renders tactical areas and multi-vertex polylines on the 2D map.
    /// </summary>
    public class MapOverlayRenderLayer : IMapLayer
    {
        public string Name => "Tactical Graphics";
        
        // Maps to "tactical_graphics" bitmask index established by layer convention
        public int LayerBitIndex { get; } 
        
        private readonly ISimulationView _view;
        private readonly EntityQuery _query;

        public MapOverlayRenderLayer(ISimulationView view, int layerBitIndex)
        {
            _view = view;
            LayerBitIndex = layerBitIndex;
            
            // Query entities that have both geometry and overlay styling
            _query = _view.Query()
                .WithManaged<EditablePolyline>()
                .WithManaged<MapOverlayStyle>()
                .With<CullingState>() // Respect viewport culling
                .Build();
        }

        public void Update(float dt) { }

        public void Draw(RenderContext ctx)
        {
            // 1. Verify global layer visibility mask [10]
            uint maskBit = 1u << LayerBitIndex;
            if ((ctx.VisibleLayersMask & maskBit) == 0 && LayerBitIndex >= 0)
                return;

            foreach (var entity in _query)
            {
                // 2. Gate rendering via CullingState [13]
                ref readonly var culling = ref _view.GetComponentRO<CullingState>(entity);
                if (!culling.IsVisible) continue;

                var polyline = _view.GetManagedComponentRO<EditablePolyline>(entity);
                var style = _view.GetManagedComponentRO<MapOverlayStyle>(entity);

                if (polyline.Points.Count < 2) continue;

                // Extract raw array for Raylib interop
                var points = polyline.Points.ToArray();

                // 3. Draw Fill (if polygon is closed and has fill color)
                if (style.IsClosed && style.FillColor.A > 0 && points.Length >= 3)
                {
                    // Raylib expects a triangle fan for simple polygons
                    Raylib.DrawTriangleFan(points, points.Length, style.FillColor);
                }

                // 4. Draw Border
                // Raylib DrawLineStrip handles connected lines; we append the start point to close it
                if (style.IsClosed && points.Length >= 3)
                {
                    var closedPoints = new Vector2[points.Length + 1];
                    points.CopyTo(closedPoints, 0);
                    closedPoints[^1] = points;
                    Raylib.DrawLineStrip(closedPoints, closedPoints.Length, style.BorderColor);
                }
                else
                {
                    Raylib.DrawLineStrip(points, points.Length, style.BorderColor);
                }
                
                // 5. Draw Selection Highlight
                if (_view.HasComponent<SelectionState>(entity))
                {
                    ref readonly var sel = ref _view.GetComponentRO<SelectionState>(entity);
                    if (sel.IsSelected)
                    {
                        Color highlight = sel.IsPrimarySelection ? Color.Green : Color.Yellow;
                        // Draw a thicker highlight strip over the border [14]
                        Raylib.DrawLineStrip(style.IsClosed ? closedPoints : points, 
                                             style.IsClosed ? closedPoints.Length : points.Length, 
                                             highlight);
                    }
                }
            }
        }

        public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;

        public Entity? PickEntity(Vector2 worldPos)
        {
            // Reverse iteration to pick the top-most drawn entity first
            Entity? bestHit = null;

            foreach (var entity in _query)
            {
                var polyline = _view.GetManagedComponentRO<EditablePolyline>(entity);
                var style = _view.GetManagedComponentRO<MapOverlayStyle>(entity);
                
                if (style.IsClosed && polyline.Points.Count >= 3)
                {
                    if (IsPointInPolygon(worldPos, polyline.Points))
                    {
                        bestHit = entity; 
                    }
                }
            }
            return bestHit;
        }

        // Standard Ray-Casting Point-in-Polygon algorithm
        private bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool isInside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    isInside = !isInside;
                }
            }
            return isInside;
        }
    }
}
```

3\. Wiring It Up

Finally, to inject this into the application, you simply add it to the `MapCanvas` inside the `IgApplication.InitializeNetwork` setup block, right next to where the `EntityRenderLayer` is added :

```
// Inside IgApplication.InitializeNetwork()
var overlayLayer = new MapOverlayRenderLayer(_world, layerBitIndex: 3 /* tactical_graphics */);
_canvas.AddLayer(overlayLayer);
```

---------
HOw the IOS can affect the styling of the tactical drawing? I think it is missing the UI for setting the style. Maybe we can add some UI panel that defines the style to be used for the next map drawing?
--------------
The architecture is explicitly designed to handle this through a 3-layer style resolution strategy: JSON Override (highest priority), Preset Name, and TKB Default. To control the styling of the _next_ tactical drawing, the IOS acts as the pure controller and must push the desired style parameters down to the IG via the `MapInteractionConfig` JSON payload before the operator starts drawing,.

Here is exactly how we can implement the UI and wire it through the network pipeline.

1\. IOS Side: ImGui Panel & Config Payload

Currently, the IOS builds the activation payload in `IosLogic.BuildPlacementPatch` using an anonymous object. We need to expose a style selector in the UI and inject those settings into the `toolConfig` node.

**In** **SpawnerPanel.cs** **(The UI):** Add state for the desired style and expose it via ImGui widgets,.

```
// Add to state fields
private string _fillColorHex = "#FF000080"; // Semi-transparent red
private float _lineThickness = 2.0f;

// Inside public void Draw(IIosLogic logic)
ImGui.Separator();
ImGui.Text("Drawing Style");
ImGui.InputText("Fill Color (Hex)", ref _fillColorHex, 10);
ImGui.SliderFloat("Line Thickness", ref _lineThickness, 1.0f, 10.0f);

if (ImGui.Button("DRAW AREA"))
{
    // Build the JSON style override string
    string styleJson = $"{{\"FillColor\":\"{_fillColorHex}\", \"LineThickness\":{_lineThickness}}}";
    logic.StartAreaAuthoringMode(styleJson);
}
```

**In** **IosLogic.cs** **(The Controller):** Modify the logic to embed this `styleOverrideJson` into the `MapInteractionConfig` patch.

```
public void StartAreaAuthoringMode(string styleOverrideJson)
{
    ThrowIfDisposed();
    ActiveContextId = Guid.NewGuid();

    // Embed the style settings into the JSON Merge Patch
    string patch = JsonConvert.SerializeObject(new
    {
        interaction = new
        {
            activeTool = "AREA_AUTHORING",
            toolSettings = new
            {
                styleOverrideJson = styleOverrideJson
            }
        }
    });

    _configWriter.Write(new MapInteractionConfig
    {
        MapGroupId = _mapGroupId,
        ActiveContextId = ActiveContextId,
        JsonSchemaVersion = IosLogicConstants.JsonSchemaVersion,
        ConfigurationJson = patch
    });
}
```

2\. IG Side: Parsing and Tool Activation

When the IG receives the `MapInteractionConfig` patch, it parses the JSON. We must extract the `styleOverrideJson` from `toolSettings` and hand it to the authoring tool,.

**In** **IgApplication.ParseAndApplyConfig()****:**

```
if (root.TryGetProperty("interaction", out var interactionEl)
    && interactionEl.TryGetProperty("activeTool", out var toolEl)
    && toolEl.GetString() == "AREA_AUTHORING"
    && interactionEl.TryGetProperty("toolSettings", out var toolSettingsEl))
{
    string styleJson = "";
    if (toolSettingsEl.TryGetProperty("styleOverrideJson", out var styleEl))
    {
        styleJson = styleEl.GetString() ?? "";
    }

    ActivateAreaAuthoringTool(styleJson);
}
```

3\. IG Side: Committing the Authoritative Entity

When the operator finishes drawing and right-clicks, the local tool (e.g., our `PointSequenceTool` wrapper) constructs the `CreateEntityRequest`. We map the cached `styleJson` directly into the `MapVisualOverlay` descriptor.

**Inside the Tool's Commit Callback:**

```
private void ActivateAreaAuthoringTool(string styleJson)
{
    if (_canvas.ActiveTool is PointSequenceTool) _canvas.PopTool();

    var pathTool = new PointSequenceTool(points =>
    {
        if (points.Length < 3) return;

        var geoPoints = ConvertToGeodetic(points);

        var request = new CreateEntityRequest
        {
            RequestId = Guid.NewGuid(),
            Owner = default, // SimHost takes ownership
            InitialDescriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d = EDescriptorType.dtEntityMaster,
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
                        IsClickable = true,
                        // Inject the IOS-defined style override here
                        StyleOverrideJson = styleJson 
                    }
                }
            }
        };

        _createEntityDdsWriter.Write(request);
        _canvas.SwitchTool(_interactionTool);
    });

    _canvas.PushTool(pathTool);
}
```

By passing the style properties through `MapInteractionConfig.ConfigurationJson`, you maintain the strict Controller/View separation. The IOS dictates the visual intent without ever needing to touch the heavy multi-vertex geometry payloads,. The SimHost acts as the scenario owner, and when it broadcasts the final `MapVisualOverlay` over the DDS backbone, the IG's rendering layer parses the JSON to draw the customized colors and thicknesses exactly as requested,.

