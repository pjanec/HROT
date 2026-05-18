# FDP.Toolkit.Vis2D - User Guide

**Version**: 2.0  
**Date**: 2026-02-12  
**Audience**: Application Developers

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Installation](#2-installation)
3. [Core Concepts](#3-core-concepts)
4. [API Reference](#4-api-reference)
5. [Usage Examples](#5-usage-examples)
6. [Advanced Topics](#6-advanced-topics)
7. [Best Practices](#7-best-practices)
8. [Troubleshooting](#8-troubleshooting)

---

## 1. Introduction

### 1.1 Purpose

`FDP.Toolkit.Vis2D` is a **2D visualization framework** for rendering ECS entities as map symbols. It provides:
- **Layer-based rendering** (base map → entities → overlays)
- **Interactive tools** (selection, drag, drawing)
- **Camera management** (pan, zoom, focus)
- **Adapter pattern** (custom symbol rendering)

### 1.2 Key Features

- **MapCanvas**: Main container managing layers + tools
- **IVisualizerAdapter**: Bridge from your ECS → rendering
- **IMapLayer**: Pluggable rendering layers (entities, grids, overlays)
- **IMapTool**: Pluggable interaction (select, drag, measure, draw)
- **MapCamera**: 2D camera with smooth pan/zoom
- **RenderContext**: Dependency injection for resources (fonts, textures)

### 1.3 Architecture Overview

```
┌─────────────────────────────────────────────┐
│          MapCanvas (Coordinator)            │
│  ┌───────────────────────────────────────┐  │
│  │  Layer Stack (bottom → top)           │  │
│  │  ┌──────────────────────────────────┐ │  │
│  │  │ EntityRenderLayer (entities)     │ │  │
│  │  │   ↓ calls                        │ │  │
│  │  │ IVisualizerAdapter (YOUR CODE)   │ │  │
│  │  └──────────────────────────────────┘ │  │
│  │  ┌──────────────────────────────────┐ │  │
│  │  │ GridLayer (optional grid)        │ │  │
│  │  └──────────────────────────────────┘ │  │
│  └───────────────────────────────────────┘  │
│  ┌───────────────────────────────────────┐  │
│  │  Tool Stack (pushed/popped)           │  │
│  │  ┌──────────────────────────────────┐ │  │
│  │  │ StandardInteractionTool (base)   │ │  │
│  │  └──────────────────────────────────┘ │  │
│  │  ┌──────────────────────────────────┐ │  │
│  │  │ EntityDragTool (when dragging)   │ │  │
│  │  └──────────────────────────────────┘ │  │
│  └───────────────────────────────────────┘  │
│                                             │
│  MapCamera (pan/zoom state)                 │
│  RenderContext (resources)                  │
└─────────────────────────────────────────────┘
```

### 1.4 Workflow

1. **Setup**: Create `MapCanvas`, add layers, set adapter
2. **Update**: Call `canvas.Update(dt)` every frame
3. **Draw**: Call `canvas.Draw()` inside `OnDrawWorld()`
4. **Interact**: Tools automatically handle input

---

## 2. Installation

### 2.1 Add Package Reference

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Toolkits\FDP.Toolkit.Vis2D\FDP.Toolkit.Vis2D.csproj" />
</ItemGroup>
```

**Dependencies** (automatically included):
- `Fdp.Kernel` - EntityRepository, EntityQuery
- `Raylib-cs` - Rendering

### 2.2 Using Statements

```csharp
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Layers;
using FDP.Toolkit.Vis2D.Tools;
using FDP.Toolkit.Vis2D.Camera;
using System.Numerics;
using Raylib_cs;
```

---

## 3. Core Concepts

### 3.1 MapCanvas: The Central Coordinator

`MapCanvas` is the **main entry point**:
- Owns layer stack (rendering in order)
- Owns tool stack (interaction)
- Owns camera (transforms)
- Provides `Update(dt)` and `Draw()` methods

**Responsibilities**:
- Execute tool input handling
- Update camera based on tool requests
- Render all layers in order
- Coordinate between tools and layers

### 3.2 Layers: Rendering Pipeline

Layers are **rendering stages** drawn bottom-to-top:

```
┌─────────────────────────┐
│  Top: UI Overlays       │ ← Draw last (on top)
├─────────────────────────┤
│  Middle: Entities       │
├─────────────────────────┤
│  Bottom: Base Map/Grid  │ ← Draw first (background)
└─────────────────────────┘
```

**IMapLayer** interface:
```csharp
public interface IMapLayer
{
    void Draw(RenderContext context);
    bool Visible { get; set; }
    int RenderOrder { get; }
}
```

**Built-in Layers**:
- `EntityRenderLayer` - Renders entities via adapter
- (Custom layers for grid, map tiles, overlays, etc.)

### 3.3 Tools: Interaction Pipeline

Tools are **input handlers** organized as a stack:

```
┌─────────────────────────────────────────┐
│  Tool Stack (top handles input first)   │
├─────────────────────────────────────────┤
│  EntityDragTool ← Currently active      │
│  OnClick: Move entity                   │
│  OnDrag: Update position                │
│  OnRelease: Commit change               │
├─────────────────────────────────────────┤
│  StandardInteractionTool ← Base         │
│  OnClick: Select entity                 │
│  OnRightClick: Context menu             │
│  OnWheel: Zoom camera                   │
└─────────────────────────────────────────┘
```

**IMapTool** interface:
```csharp
public interface IMapTool
{
    void OnEnter();  // Tool activated
    void OnExit();   // Tool deactivated
    void Update(float dt);
    void Draw(RenderContext context);
    bool HandleClick(Vector2 worldPos, MouseButton button);
    bool HandleDrag(Vector2 worldPos, Vector2 delta);
    bool HandleHover(Vector2 worldPos);
}
```

**Built-in Tools**:
- `StandardInteractionTool` - Click selection, box select, camera pan
- `EntityDragTool` - Drag entities to move
- `BoxSelectionTool` - Rectangle selection
- `PointSequenceTool` - Click to add waypoints

### 3.4 IVisualizerAdapter: Your Custom Rendering

The **bridge** between your ECS components and Vis2D rendering:

```csharp
public interface IVisualizerAdapter
{
    void GetEntities(List<EntityID> outList);
    Vector2 GetPosition(EntityID entity);
    void DrawSymbol(EntityID entity, RenderContext context);
    bool IsVisible(EntityID entity);
    int GetLayerMask(EntityID entity);
}
```

**You implement this** to tell Vis2D:
- Which entities to render
- Where they are (for camera culling)
- How to draw them (custom symbols)
- Which layer they belong to

### 3.5 MapCamera: View Transform

Handles **pan and zoom**:

```csharp
public class MapCamera
{
    public Vector2 Position { get; set; }      // Center of view (world coords)
    public float Zoom { get; set; }            // Zoom level (1.0 = 1:1)
    
    public void HandleInput();                 // Keyboard/mouse pan
    public void Update(float dt);              // Smooth movement
    public void FocusOn(Vector2 worldPos);     // Center on point
    
    public Vector2 ScreenToWorld(Vector2 screenPos);
    public Vector2 WorldToScreen(Vector2 worldPos);
}
```

### 3.6 RenderContext: Resource Injection

**Dependency injection** for rendering:

```csharp
public struct RenderContext
{
    public EntityRepository World;
    public MapCamera Camera;
    public Dictionary<string, Texture2D> Textures;
    public Dictionary<string, Font> Fonts;
    public ISelectionState Selection;
    public float DeltaTime;
}
```

Passed to every `Draw()` call so you don't need global state.

---

## 4. API Reference

### 4.1 MapCanvas

```csharp
namespace FDP.Toolkit.Vis2D;

public class MapCanvas
{
    // ───────────────────────────────────────────
    // Constructor
    // ───────────────────────────────────────────
    
    /// <summary>
    /// Create a new map canvas.
    /// </summary>
    /// <param name="camera">Camera for view transforms</param>
    /// <param name="context">Shared rendering resources</param>
    public MapCanvas(MapCamera camera, RenderContext context);
    
    // ───────────────────────────────────────────
    // Lifecycle
    // ───────────────────────────────────────────
    
    /// <summary>
    /// Update camera and tools. Call every frame before Draw().
    /// </summary>
    public void Update(float dt);
    
    /// <summary>
    /// Render all layers and tools. Call inside OnDrawWorld().
    /// </summary>
    public void Draw();
    
    // ───────────────────────────────────────────
    // Layer Management
    // ───────────────────────────────────────────
    
    /// <summary>
    /// Add a rendering layer. Layers are sorted by RenderOrder.
    /// </summary>
    public void AddLayer(IMapLayer layer);
    
    /// <summary>
    /// Remove a layer by reference.
    /// </summary>
    public void RemoveLayer(IMapLayer layer);
    
    /// <summary>
    /// Get all layers (read-only).
    /// </summary>
    public IReadOnlyList<IMapLayer> Layers { get; }
    
    // ───────────────────────────────────────────
    // Tool Management
    // ───────────────────────────────────────────
    
    /// <summary>
    /// Push a tool onto the stack. The new tool becomes active.
    /// Calls OnEnter() on the new tool.
    /// </summary>
    public void PushTool(IMapTool tool);
    
    /// <summary>
    /// Pop the current tool from the stack.
    /// Calls OnExit() on the popped tool.
    /// </summary>
    public void PopTool();
    
    /// <summary>
    /// Get the currently active tool (top of stack).
    /// Returns null if no tools are active.
    /// </summary>
    public IMapTool? ActiveTool { get; }
    
    // ───────────────────────────────────────────
    // Properties
    // ───────────────────────────────────────────
    
    /// <summary>
    /// The camera used for view transforms.
    /// </summary>
    public MapCamera Camera { get; }
    
    /// <summary>
    /// The render context (resources, world, selection).
    /// </summary>
    public RenderContext Context { get; set; }
}
```

---

### 4.2 IVisualizerAdapter

```csharp
namespace FDP.Toolkit.Vis2D;

/// <summary>
/// Bridge between your ECS and the visualization system.
/// Implement this to define how entities are rendered.
/// </summary>
public interface IVisualizerAdapter
{
    /// <summary>
    /// Populate outList with all entities to be rendered.
    /// Called once per frame before rendering.
    /// </summary>
    void GetEntities(List<EntityID> outList);
    
    /// <summary>
    /// Get the world position of an entity (for camera culling).
    /// </summary>
    Vector2 GetPosition(EntityID entity);
    
    /// <summary>
    /// Draw the symbol for an entity.
    /// Use context.Camera for world→screen transforms.
    /// Use context.Selection to check if entity is selected.
    /// </summary>
    void DrawSymbol(EntityID entity, RenderContext context);
    
    /// <summary>
    /// Check if entity should be visible (for layer filtering).
    /// Return false to skip rendering.
    /// </summary>
    bool IsVisible(EntityID entity);
    
    /// <summary>
    /// Get the layer mask for an entity (bit flags).
    /// Used by EntityRenderLayer for filtering.
    /// Example: 0x01 = BaseLayer, 0x02 = OverlayLayer
    /// </summary>
    int GetLayerMask(EntityID entity);
}
```

---

### 4.3 IMapLayer

```csharp
namespace FDP.Toolkit.Vis2D.Layers;

/// <summary>
/// A rendering layer in the map canvas.
/// Layers are drawn in order of RenderOrder (lowest first).
/// </summary>
public interface IMapLayer
{
    /// <summary>
    /// Render this layer. Called once per frame.
    /// </summary>
    void Draw(RenderContext context);
    
    /// <summary>
    /// Whether this layer is currently visible.
    /// </summary>
    bool Visible { get; set; }
    
    /// <summary>
    /// Rendering order (lower values draw first, i.e., behind).
    /// Example: Background = 0, Entities = 100, Overlays = 200
    /// </summary>
    int RenderOrder { get; }
}
```

**Built-in Implementation: EntityRenderLayer**

```csharp
namespace FDP.Toolkit.Vis2D.Layers;

/// <summary>
/// Renders entities using an IVisualizerAdapter.
/// Supports layer masking and culling.
/// </summary>
public class EntityRenderLayer : IMapLayer
{
    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="adapter">Your custom adapter</param>
    /// <param name="renderOrder">Order in layer stack</param>
    /// <param name="layerMask">Bitmask for filtering entities</param>
    public EntityRenderLayer(
        IVisualizerAdapter adapter, 
        int renderOrder = 100,
        int layerMask = 0xFF);
    
    public void Draw(RenderContext context);
    public bool Visible { get; set; }
    public int RenderOrder { get; }
    
    /// <summary>
    /// Only render entities where (GetLayerMask(e) &amp; LayerMask) != 0.
    /// </summary>
    public int LayerMask { get; set; }
}
```

---

### 4.4 IMapTool

```csharp
namespace FDP.Toolkit.Vis2D.Tools;

/// <summary>
/// An interactive tool for the map canvas.
/// Tools are organized as a stack (top tool handles input first).
/// </summary>
public interface IMapTool
{
    /// <summary>
    /// Called when this tool becomes active (pushed onto stack).
    /// Use for initialization, cursor changes, etc.
    /// </summary>
    void OnEnter();
    
    /// <summary>
    /// Called when this tool is deactivated (popped from stack).
    /// Use for cleanup, restoring cursor, etc.
    /// </summary>
    void OnExit();
    
    /// <summary>
    /// Update tool state. Called every frame before Draw().
    /// </summary>
    void Update(float dt);
    
    /// <summary>
    /// Draw tool UI (e.g., drag preview, selection box).
    /// Called after layers are drawn.
    /// </summary>
    void Draw(RenderContext context);
    
    /// <summary>
    /// Handle mouse click.
    /// </summary>
    /// <param name="worldPos">Click position in world coordinates</param>
    /// <param name="button">Mouse button pressed</param>
    /// <returns>True if handled (stops propagation to tools below)</returns>
    bool HandleClick(Vector2 worldPos, MouseButton button);
    
    /// <summary>
    /// Handle mouse drag.
    /// </summary>
    /// <param name="worldPos">Current drag position (world coords)</param>
    /// <param name="delta">Delta since last frame (world coords)</param>
    /// <returns>True if handled</returns>
    bool HandleDrag(Vector2 worldPos, Vector2 delta);
    
    /// <summary>
    /// Handle mouse hover (no buttons pressed).
    /// </summary>
    /// <param name="worldPos">Current mouse position (world coords)</param>
    /// <returns>True if handled</returns>
    bool HandleHover(Vector2 worldPos);
}
```

**Built-in Tools**:

**StandardInteractionTool**:
```csharp
/// <summary>
/// Default tool for selection and camera pan.
/// - Left click: Select entity
/// - Left drag: Box select
/// - Right drag: Pan camera
/// - Mouse wheel: Zoom
/// </summary>
public class StandardInteractionTool : IMapTool
{
    public StandardInteractionTool(
        IVisualizerAdapter adapter,
        ISelectionState selection,
        MapCamera camera);
}
```

**EntityDragTool**:
```csharp
/// <summary>
/// Drag entities to move them.
/// Typically pushed onto stack when starting a drag operation.
/// </summary>
public class EntityDragTool : IMapTool
{
    public EntityDragTool(
        EntityID entity,
        EntityRepository world,
        MapCamera camera);
}
```

**BoxSelectionTool**:
```csharp
/// <summary>
/// Rectangle selection tool.
/// Draw a box to select multiple entities.
/// </summary>
public class BoxSelectionTool : IMapTool
{
    public BoxSelectionTool(
        IVisualizerAdapter adapter,
        ISelectionState selection,
        MapCamera camera);
}
```

**PointSequenceTool**:
```csharp
/// <summary>
/// Click to add points (waypoints, polygon vertices, etc.).
/// Right-click or Escape to finish.
/// </summary>
public class PointSequenceTool : IMapTool
{
    public PointSequenceTool(Action<List<Vector2>> onComplete);
    
    public List<Vector2> Points { get; }
}
```

---

### 4.5 MapCamera

```csharp
namespace FDP.Toolkit.Vis2D.Camera;

public class MapCamera
{
    // ───────────────────────────────────────────
    // Properties
    // ───────────────────────────────────────────
    
    /// <summary>Center of view (world coordinates)</summary>
    public Vector2 Position { get; set; }
    
    /// <summary>Zoom level (1.0 = 1:1, 2.0 = 2× zoom in)</summary>
    public float Zoom { get; set; }
    
    /// <summary>Minimum zoom level (default: 0.1)</summary>
    public float MinZoom { get; set; }
    
    /// <summary>Maximum zoom level (default: 10.0)</summary>
    public float MaxZoom { get; set; }
    
    /// <summary>Pan speed for keyboard (world units per second)</summary>
    public float PanSpeed { get; set; }
    
    /// <summary>Zoom speed for mouse wheel</summary>
    public float ZoomSpeed { get; set; }
    
    // ───────────────────────────────────────────
    // Methods
    // ───────────────────────────────────────────
    
    /// <summary>
    /// Handle keyboard input for panning (WASD or Arrow keys).
    /// Call this in Update() if you want keyboard pan.
    /// </summary>
    public void HandleInput();
    
    /// <summary>
    /// Update smooth interpolation. Call every frame.
    /// </summary>
    public void Update(float dt);
    
    /// <summary>
    /// Smoothly move camera to focus on a world position.
    /// </summary>
    public void FocusOn(Vector2 worldPos, float transitionTime = 0.5f);
    
    /// <summary>
    /// Convert screen coordinates to world coordinates.
    /// </summary>
    public Vector2 ScreenToWorld(Vector2 screenPos);
    
    /// <summary>
    /// Convert world coordinates to screen coordinates.
    /// </summary>
    public Vector2 WorldToScreen(Vector2 worldPos);
    
    /// <summary>
    /// Get the current view rectangle (world bounds visible on screen).
    /// </summary>
    public Rectangle GetViewBounds();
}
```

---

### 4.6 RenderContext

```csharp
namespace FDP.Toolkit.Vis2D;

/// <summary>
/// Shared rendering resources passed to layers and tools.
/// Use this instead of global variables.
/// </summary>
public struct RenderContext
{
    /// <summary>The ECS world</summary>
    public EntityRepository World;
    
    /// <summary>The camera for transforms</summary>
    public MapCamera Camera;
    
    /// <summary>Loaded textures by name</summary>
    public Dictionary<string, Texture2D> Textures;
    
    /// <summary>Loaded fonts by name</summary>
    public Dictionary<string, Font> Fonts;
    
    /// <summary>Current selection state</summary>
    public ISelectionState Selection;
    
    /// <summary>Delta time for this frame</summary>
    public float DeltaTime;
}
```

---

## 5. Usage Examples

### 5.1 Minimal Setup

```csharp
using FDP.Framework.Raylib;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Layers;
using FDP.Toolkit.Vis2D.Tools;
using Fdp.Kernel;
using System.Numerics;
using Raylib_cs;

public class MinimalVisApp : FdpApplication
{
    private EntityRepository _world;
    private MapCamera _camera;
    private MapCanvas _canvas;
    private SimpleAdapter _adapter;
    
    protected override void OnLoad()
    {
        _world = new EntityRepository();
        
        // Create camera
        _camera = new MapCamera
        {
            Position = Vector2.Zero,
            Zoom = 1.0f
        };
        
        // Create render context
        var context = new RenderContext
        {
            World = _world,
            Camera = _camera,
            Textures = new(),
            Fonts = new(),
            Selection = new SimpleSelection()
        };
        
        // Create canvas
        _canvas = new MapCanvas(_camera, context);
        
        // Add adapter
        _adapter = new SimpleAdapter(_world);
        
        // Add entity layer
        _canvas.AddLayer(new EntityRenderLayer(_adapter));
        
        // Add default interaction tool
        _canvas.PushTool(new StandardInteractionTool(_adapter, context.Selection, _camera));
        
        // Create test entities
        for (int i = 0; i < 10; i++)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new Position 
            { 
                Value = new Vector2(i * 50, i * 50) 
            });
        }
    }
    
    protected override void OnUpdate(float dt)
    {
        _canvas.Update(dt);
    }
    
    protected override void OnDrawWorld()
    {
        Raylib.ClearBackground(Color.DarkGray);
        _canvas.Draw();
    }
}

// Simple adapter implementation
public class SimpleAdapter : IVisualizerAdapter
{
    private readonly EntityRepository _world;
    private readonly EntityQuery _query;
    
    public SimpleAdapter(EntityRepository world)
    {
        _world = world;
        _query = world.Query().With<Position>().Build();
    }
    
    public void GetEntities(List<EntityID> outList)
    {
        outList.Clear();
        foreach (var e in _query)
            outList.Add(e);
    }
    
    public Vector2 GetPosition(EntityID entity)
    {
        return _world.GetComponentRO<Position>(entity).Value;
    }
    
    public void DrawSymbol(EntityID entity, RenderContext context)
    {
        var worldPos = GetPosition(entity);
        var screenPos = context.Camera.WorldToScreen(worldPos);
        
        var color = context.Selection.IsSelected(entity) ? Color.Yellow : Color.White;
        Raylib.DrawCircleV(screenPos, 5f, color);
    }
    
    public bool IsVisible(EntityID entity) => true;
    public int GetLayerMask(EntityID entity) => 0xFF;
}

// Simple selection state
public class SimpleSelection : ISelectionState
{
    private readonly HashSet<EntityID> _selected = new();
    
    public void Select(EntityID entity) => _selected.Add(entity);
    public void Deselect(EntityID entity) => _selected.Remove(entity);
    public void Clear() => _selected.Clear();
    public bool IsSelected(EntityID entity) => _selected.Contains(entity);
    public IEnumerable<EntityID> GetSelected() => _selected;
}

public struct Position { public Vector2 Value; }
```

---

### 5.2 Custom Symbol Rendering

```csharp
public class VehicleAdapter : IVisualizerAdapter
{
    private readonly EntityRepository _world;
    private readonly EntityQuery _vehicleQuery;
    private readonly Dictionary<string, Texture2D> _textures;
    
    public VehicleAdapter(EntityRepository world, Dictionary<string, Texture2D> textures)
    {
        _world = world;
        _textures = textures;
        _vehicleQuery = world.Query()
            .With<Position>()
            .With<Heading>()
            .With<VehicleType>()
            .Build();
    }
    
    public void DrawSymbol(EntityID entity, RenderContext context)
    {
        ref readonly var pos = ref _world.GetComponentRO<Position>(entity);
        ref readonly var heading = ref _world.GetComponentRO<Heading>(entity);
        ref readonly var type = ref _world.GetComponentRO<VehicleType>(entity);
        
        var screenPos = context.Camera.WorldToScreen(pos.Value);
        
        // Get texture based on type
        var textureName = type.Type switch
        {
            "Tank" => "tank_icon",
            "Truck" => "truck_icon",
            _ => "default_icon"
        };
        
        if (_textures.TryGetValue(textureName, out var texture))
        {
            // Draw rotated sprite
            var rect = new Rectangle(0, 0, texture.Width, texture.Height);
            var dest = new Rectangle(screenPos.X, screenPos.Y, 32, 32);
            var origin = new Vector2(16, 16);
            
            Raylib.DrawTexturePro(
                texture, 
                rect, 
                dest, 
                origin, 
                heading.Degrees, 
                Color.White);
        }
        else
        {
            // Fallback: draw triangle
            DrawArrow(screenPos, heading.Degrees, Color.Blue);
        }
        
        // Selection highlight
        if (context.Selection.IsSelected(entity))
        {
            Raylib.DrawCircleLines((int)screenPos.X, (int)screenPos.Y, 20, Color.Yellow);
        }
    }
    
    private void DrawArrow(Vector2 pos, float degrees, Color color)
    {
        float rad = degrees * (MathF.PI / 180f);
        Vector2 dir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
        Vector2 tip = pos + dir * 15f;
        
        Raylib.DrawLineEx(pos, tip, 2f, color);
        // ... draw arrowhead ...
    }
    
    public void GetEntities(List<EntityID> outList)
    {
        outList.Clear();
        foreach (var e in _vehicleQuery)
            outList.Add(e);
    }
    
    public Vector2 GetPosition(EntityID entity)
    {
        return _world.GetComponentRO<Position>(entity).Value;
    }
    
    public bool IsVisible(EntityID entity) => true;
    public int GetLayerMask(EntityID entity) => 0x01; // Base layer
}

public struct Heading { public float Degrees; }
public struct VehicleType { public string Type; }
```

---

### 5.3 Multiple Layers with Layer Masking

```csharp
protected override void OnLoad()
{
    // ... setup world, camera, context ...
    
    // Create adapters for different entity types
    var vehicleAdapter = new VehicleAdapter(_world, _textures);
    var infrastructureAdapter = new InfrastructureAdapter(_world, _textures);
    var overlayAdapter = new OverlayAdapter(_world);
    
    // Layer 0: Infrastructure (roads, buildings) - always visible
    _canvas.AddLayer(new EntityRenderLayer(
        infrastructureAdapter, 
        renderOrder: 0, 
        layerMask: 0x01));
    
    // Layer 1: Vehicles - can be toggled
    var vehicleLayer = new EntityRenderLayer(
        vehicleAdapter, 
        renderOrder: 100, 
        layerMask: 0x02);
    _canvas.AddLayer(vehicleLayer);
    
    // Layer 2: Overlays (trajectories, ranges) - can be toggled
    var overlayLayer = new EntityRenderLayer(
        overlayAdapter, 
        renderOrder: 200, 
        layerMask: 0x04);
    _canvas.AddLayer(overlayLayer);
    
    // UI for toggling layers
    _showVehicles = true;
    _showOverlays = true;
}

protected override void OnDrawUI()
{
    ImGui.Begin("Layers");
    
    if (ImGui.Checkbox("Show Vehicles", ref _showVehicles))
    {
        var layer = _canvas.Layers.FirstOrDefault(l => l.RenderOrder == 100);
        if (layer != null)
            layer.Visible = _showVehicles;
    }
    
    if (ImGui.Checkbox("Show Overlays", ref _showOverlays))
    {
        var layer = _canvas.Layers.FirstOrDefault(l => l.RenderOrder == 200);
        if (layer != null)
            layer.Visible = _showOverlays;
    }
    
    ImGui.End();
}
```

---

### 5.4 Custom Tool: Measure Distance

```csharp
public class MeasureTool : IMapTool
{
    private readonly MapCamera _camera;
    private Vector2? _startPoint;
    private Vector2? _endPoint;
    
    public MeasureTool(MapCamera camera)
    {
        _camera = camera;
    }
    
    public void OnEnter()
    {
        _startPoint = null;
        _endPoint = null;
        Console.WriteLine("Measure tool: Click start point");
    }
    
    public void OnExit()
    {
        Console.WriteLine("Measure tool: Exited");
    }
    
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            if (_startPoint == null)
            {
                _startPoint = worldPos;
                Console.WriteLine($"Start: {worldPos}");
                return true;
            }
            else
            {
                _endPoint = worldPos;
                float distance = Vector2.Distance(_startPoint.Value, _endPoint.Value);
                Console.WriteLine($"End: {worldPos}, Distance: {distance:F2}m");
                
                // Reset for next measurement
                _startPoint = null;
                _endPoint = null;
                return true;
            }
        }
        
        return false;
    }
    
    public bool HandleHover(Vector2 worldPos)
    {
        if (_startPoint != null)
        {
            _endPoint = worldPos; // Live preview
        }
        return _startPoint != null;
    }
    
    public void Draw(RenderContext context)
    {
        if (_startPoint != null)
        {
            var startScreen = context.Camera.WorldToScreen(_startPoint.Value);
            
            if (_endPoint != null)
            {
                var endScreen = context.Camera.WorldToScreen(_endPoint.Value);
                Raylib.DrawLineEx(startScreen, endScreen, 2f, Color.Yellow);
                
                // Draw distance label
                var midScreen = (startScreen + endScreen) / 2f;
                float distance = Vector2.Distance(_startPoint.Value, _endPoint.Value);
                Raylib.DrawText($"{distance:F1}m", (int)midScreen.X, (int)midScreen.Y, 20, Color.Yellow);
            }
            
            // Draw start point
            Raylib.DrawCircleV(startScreen, 5f, Color.Green);
        }
    }
    
    public void Update(float dt) { }
    public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;
}

// Usage: Push tool when user clicks "Measure" button
protected override void OnDrawUI()
{
    ImGui.Begin("Tools");
    
    if (ImGui.Button("Measure Distance"))
    {
        _canvas.PushTool(new MeasureTool(_camera));
    }
    
    if (ImGui.Button("Cancel Tool") && _canvas.ActiveTool != null)
    {
        _canvas.PopTool();
    }
    
    ImGui.End();
}
```

---

### 5.5 Dynamic Tool Switching

```csharp
public class ToolPanelApp : FdpApplication
{
    private MapCanvas _canvas;
    private IMapTool _standardTool;
    private IMapTool _measureTool;
    private IMapTool _drawTool;
    private string _activeTool = "Standard";
    
    protected override void OnLoad()
    {
        // ... setup canvas ...
        
        // Create tools
        _standardTool = new StandardInteractionTool(_adapter, _selection, _camera);
        _measureTool = new MeasureTool(_camera);
        _drawTool = new PointSequenceTool(points =>
        {
            Console.WriteLine($"Drew polygon with {points.Count} points");
            // ... create entity from points ...
        });
        
        // Start with standard tool
        _canvas.PushTool(_standardTool);
    }
    
    protected override void OnDrawUI()
    {
        ImGui.Begin("Tool Palette");
        
        if (ImGui.RadioButton("Select", _activeTool == "Standard"))
        {
            SwitchTool("Standard", _standardTool);
        }
        
        if (ImGui.RadioButton("Measure", _activeTool == "Measure"))
        {
            SwitchTool("Measure", _measureTool);
        }
        
        if (ImGui.RadioButton("Draw", _activeTool == "Draw"))
        {
            SwitchTool("Draw", _drawTool);
        }
        
        ImGui.End();
    }
    
    private void SwitchTool(string name, IMapTool tool)
    {
        if (_activeTool == name)
            return;
        
        // Pop all tools
        while (_canvas.ActiveTool != null)
            _canvas.PopTool();
        
        // Push new tool
        _canvas.PushTool(tool);
        _activeTool = name;
    }
}
```

---

## 6. Advanced Topics

### 6.1 Camera Culling for Performance

```csharp
public class CullingAdapter : IVisualizerAdapter
{
    private readonly EntityRepository _world;
    private readonly EntityQuery _query;
    
    public void GetEntities(List<EntityID> outList)
    {
        outList.Clear();
        foreach (var e in _query)
            outList.Add(e);
    }
    
    public void DrawSymbol(EntityID entity, RenderContext context)
    {
        var worldPos = GetPosition(entity);
        
        // Frustum culling
        var viewBounds = context.Camera.GetViewBounds();
        if (!IsInView(worldPos, viewBounds))
            return; // Skip off-screen entities
        
        // ... render ...
    }
    
    private bool IsInView(Vector2 worldPos, Rectangle viewBounds)
    {
        return worldPos.X >= viewBounds.X &&
               worldPos.X <= viewBounds.X + viewBounds.Width &&
               worldPos.Y >= viewBounds.Y &&
               worldPos.Y <= viewBounds.Y + viewBounds.Height;
    }
}
```

### 6.2 Level-of-Detail (LOD)

```csharp
public void DrawSymbol(EntityID entity, RenderContext context)
{
    var worldPos = GetPosition(entity);
    var screenPos = context.Camera.WorldToScreen(worldPos);
    
    // LOD based on zoom
    if (context.Camera.Zoom < 0.5f)
    {
        // Zoomed out: draw simple dot
        Raylib.DrawCircleV(screenPos, 2f, Color.White);
    }
    else if (context.Camera.Zoom < 2.0f)
    {
        // Medium zoom: draw icon
        DrawIcon(screenPos, entity);
    }
    else
    {
        // Zoomed in: draw detailed symbol + label
        DrawDetailedSymbol(screenPos, entity);
        DrawLabel(screenPos, entity);
    }
}
```

### 6.3 Aggregation (Clustering)

```csharp
public class ClusteringAdapter : IVisualizerAdapter
{
    private readonly EntityRepository _world;
    private readonly Dictionary<Vector2, List<EntityID>> _clusters = new();
    private const float ClusterRadius = 50f; // World units
    
    public void GetEntities(List<EntityID> outList)
    {
        // Rebuild clusters based on current camera zoom
        _clusters.Clear();
        
        foreach (var e in _query)
        {
            var pos = GetPosition(e);
            var clusterKey = new Vector2(
                MathF.Floor(pos.X / ClusterRadius) * ClusterRadius,
                MathF.Floor(pos.Y / ClusterRadius) * ClusterRadius);
            
            if (!_clusters.ContainsKey(clusterKey))
                _clusters[clusterKey] = new List<EntityID>();
            
            _clusters[clusterKey].Add(e);
        }
        
        // Return cluster representatives
        outList.Clear();
        foreach (var kvp in _clusters)
        {
            outList.Add(kvp.Value[0]); // Use first entity as representative
        }
    }
    
    public void DrawSymbol(EntityID entity, RenderContext context)
    {
        var pos = GetPosition(entity);
        var screenPos = context.Camera.WorldToScreen(pos);
        
        // Find cluster for this entity
        var clusterKey = new Vector2(
            MathF.Floor(pos.X / ClusterRadius) * ClusterRadius,
            MathF.Floor(pos.Y / ClusterRadius) * ClusterRadius);
        
        if (_clusters.TryGetValue(clusterKey, out var cluster))
        {
            if (context.Camera.Zoom < 1.0f && cluster.Count > 1)
            {
                // Draw cluster marker
                Raylib.DrawCircleV(screenPos, 10f, Color.Blue);
                Raylib.DrawText($"{cluster.Count}", (int)screenPos.X - 5, (int)screenPos.Y - 5, 14, Color.White);
            }
            else
            {
                // Draw individual entity
                Raylib.DrawCircleV(screenPos, 5f, Color.White);
            }
        }
    }
}
```

### 6.4 Hierarchical Rendering (Parent/Child)

```csharp
public struct Parent { public EntityID Value; }
public struct LocalPosition { public Vector2 Value; }

public class HierarchicalAdapter : IVisualizerAdapter
{
    private readonly EntityRepository _world;
    
    public Vector2 GetPosition(EntityID entity)
    {
        // Accumulate transforms up the hierarchy
        Vector2 worldPos = Vector2.Zero;
        EntityID current = entity;
        
        while (_world.HasComponent<Position>(current))
        {
            if (_world.HasComponent<LocalPosition>(current))
            {
                worldPos += _world.GetComponentRO<LocalPosition>(current).Value;
            }
            else
            {
                worldPos += _world.GetComponentRO<Position>(current).Value;
            }
            
            // Move up to parent
            if (_world.HasComponent<Parent>(current))
            {
                current = _world.GetComponentRO<Parent>(current).Value;
            }
            else
            {
                break; // Root reached
            }
        }
        
        return worldPos;
    }
    
    public void DrawSymbol(EntityID entity, RenderContext context)
    {
        var worldPos = GetPosition(entity);
        var screenPos = context.Camera.WorldToScreen(worldPos);
        
        // Draw entity
        Raylib.DrawCircleV(screenPos, 5f, Color.White);
        
        // Draw link to parent
        if (_world.HasComponent<Parent>(entity))
        {
            var parent = _world.GetComponentRO<Parent>(entity).Value;
            var parentWorldPos = GetPosition(parent);
            var parentScreenPos = context.Camera.WorldToScreen(parentWorldPos);
            
            Raylib.DrawLineEx(screenPos, parentScreenPos, 1f, Color.Gray);
        }
    }
}
```

### 6.5 Decluttering (Label Collision Avoidance)

```csharp
public class DeclutteringLayer : IMapLayer
{
    private readonly IVisualizerAdapter _adapter;
    private readonly List<(Vector2 pos, string label)> _labels = new();
    
    public int RenderOrder => 1000; // Draw on top
    public bool Visible { get; set; } = true;
    
    public void Draw(RenderContext context)
    {
        _labels.Clear();
        
        // Collect all labels
        var entities = new List<EntityID>();
        _adapter.GetEntities(entities);
        
        foreach (var e in entities)
        {
            var worldPos = _adapter.GetPosition(e);
            var screenPos = context.Camera.WorldToScreen(worldPos);
            var label = GetLabel(e, context.World);
            
            _labels.Add((screenPos, label));
        }
        
        // Simple decluttering: only draw if no overlap
        var drawnLabels = new List<Rectangle>();
        
        foreach (var (pos, label) in _labels)
        {
            var textSize = Raylib.MeasureTextEx(Raylib.GetFontDefault(), label, 14, 1);
            var bounds = new Rectangle(pos.X, pos.Y - 20, textSize.X, textSize.Y);
            
            bool overlaps = drawnLabels.Any(r => CheckCollisionRecs(r, bounds));
            
            if (!overlaps)
            {
                Raylib.DrawText(label, (int)pos.X, (int)(pos.Y - 20), 14, Color.White);
                drawnLabels.Add(bounds);
            }
        }
    }
    
    private string GetLabel(EntityID entity, EntityRepository world)
    {
        if (world.HasComponent<Name>(entity))
            return world.GetComponentRO<Name>(entity).Value;
        return entity.ToString();
    }
}

public struct Name { public string Value; }
```

---

## 7. Best Practices

### 7.1 Adapter Design

**Do**: Keep adapters focused on view logic only.

```csharp
// Good: Adapter only handles rendering
public class VehicleAdapter : IVisualizerAdapter
{
    public void DrawSymbol(EntityID entity, RenderContext context)
    {
        var pos = GetPosition(entity);
        DrawVehicleIcon(pos, entity);
    }
}

// Bad: Adapter modifies data
public class BadAdapter : IVisualizerAdapter
{
    public void DrawSymbol(EntityID entity, RenderContext context)
    {
        // Don't do simulation in adapter!
        ref var pos = ref _world.GetComponentRW<Position>(entity);
        pos.Value += Vector2.One * context.DeltaTime;
        
        DrawIcon(pos.Value);
    }
}
```

**Do**: Use RenderContext for resources, not global variables.

```csharp
// Good
public void DrawSymbol(EntityID entity, RenderContext context)
{
    var texture = context.Textures["tank"];
    Raylib.DrawTexture(texture, ...);
}

// Bad
private static Texture2D _globalTexture; // Global state is hard to test

public void DrawSymbol(EntityID entity, RenderContext context)
{
    Raylib.DrawTexture(_globalTexture, ...);
}
```

### 7.2 Layer Organization

**Typical Layer Stack**:

```csharp
// Background (renderOrder: 0)
_canvas.AddLayer(new GridLayer(renderOrder: 0));

// Base map (renderOrder: 10)
_canvas.AddLayer(new TerrainLayer(renderOrder: 10));

// Entities - multiple layers for Z-ordering
_canvas.AddLayer(new EntityRenderLayer(_infrastructureAdapter, renderOrder: 50));
_canvas.AddLayer(new EntityRenderLayer(_groundVehicleAdapter, renderOrder: 100));
_canvas.AddLayer(new EntityRenderLayer(_aircraftAdapter, renderOrder: 150));

// Effects (renderOrder: 200)
_canvas.AddLayer(new TrajectoryLayer(renderOrder: 200));

// UI overlays (renderOrder: 1000)
_canvas.AddLayer(new LabelLayer(renderOrder: 1000));
```

### 7.3 Tool Stack Management

**Pattern**: Use stack for temporary tools.

```csharp
// Base tool always on stack
_canvas.PushTool(_standardTool);

// User starts dragging
if (userStartsDrag)
{
    _canvas.PushTool(new EntityDragTool(entity, _world, _camera));
}

// When drag ends, tool calls
canvas.PopTool(); // Returns to StandardInteractionTool
```

**Pattern**: Single tool for modal operations.

```csharp
// User clicks "Draw Polygon" button
canvas.PopTool(); // Remove StandardTool
canvas.PushTool(new PointSequenceTool(points =>
{
    CreatePolygon(points);
    
    // Restore standard tool
    canvas.PopTool();
    canvas.PushTool(_standardTool);
}));
```

### 7.4 Performance Optimization

**1. Cache entity lists**:
```csharp
private List<EntityID> _cachedEntities = new();
private int _lastFrameCount = -1;

public void GetEntities(List<EntityID> outList)
{
    int currentFrame = Raylib.GetFrameTime();
    if (currentFrame != _lastFrameCount)
    {
        _cachedEntities.Clear();
        foreach (var e in _query)
            _cachedEntities.Add(e);
        _lastFrameCount = currentFrame;
    }
    
    outList.AddRange(_cachedEntities);
}
```

**2. Frustum culling**:
```csharp
public void DrawSymbol(EntityID entity, RenderContext context)
{
    var worldPos = GetPosition(entity);
    var screenPos = context.Camera.WorldToScreen(worldPos);
    
    // Early exit if off-screen
    if (screenPos.X < -50 || screenPos.X > 1280 + 50 ||
        screenPos.Y < -50 || screenPos.Y > 720 + 50)
        return;
    
    // ... render ...
}
```

**3. Batch rendering**:
```csharp
// Slow: many draw calls
foreach (var e in entities)
{
    Raylib.DrawCircleV(GetPosition(e), 5f, Color.White);
}

// Fast: batch similar primitives
var positions = entities.Select(e => GetPosition(e)).ToArray();
Raylib.BeginBlendMode(BlendMode.Alpha);
foreach (var pos in positions)
{
    Raylib.DrawCircleV(pos, 5f, Color.White);
}
Raylib.EndBlendMode();
```

### 7.5 Testing Adapters

```csharp
[TestMethod]
public void Adapter_ReturnsCorrectPosition()
{
    // Arrange
    var world = new EntityRepository();
    var entity = world.CreateEntity();
    world.AddComponent(entity, new Position { Value = new Vector2(100, 200) });
    
    var adapter = new VehicleAdapter(world, new());
    
    // Act
    var pos = adapter.GetPosition(entity);
    
    // Assert
    Assert.AreEqual(new Vector2(100, 200), pos);
}

[TestMethod]
public void Adapter_FiltersInvisibleEntities()
{
    var world = new EntityRepository();
    var visible = world.CreateEntity();
    var invisible = world.CreateEntity();
    
    world.AddComponent(visible, new Position { Value = Vector2.Zero });
    world.AddComponent(visible, new Visible { Value = true });
    
    world.AddComponent(invisible, new Position { Value = Vector2.Zero });
    world.AddComponent(invisible, new Visible { Value = false });
    
    var adapter = new VehicleAdapter(world, new());
    var entities = new List<EntityID>();
    adapter.GetEntities(entities);
    
    Assert.AreEqual(1, entities.Count);
    Assert.AreEqual(visible, entities[0]);
}
```

---

## 8. Troubleshooting

### 8.1 Entities Not Rendering

**Symptom**: Entities exist but don't appear on screen.

**Diagnostics**:

```csharp
// 1. Check adapter is returning entities
var entities = new List<EntityID>();
_adapter.GetEntities(entities);
Console.WriteLine($"Entities to render: {entities.Count}");

// 2. Check positions
foreach (var e in entities)
{
    var pos = _adapter.GetPosition(e);
    Console.WriteLine($"Entity {e}: Pos {pos}");
}

// 3. Check camera transform
var testWorld = new Vector2(100, 100);
var testScreen = _camera.WorldToScreen(testWorld);
Console.WriteLine($"Camera transform: World {testWorld} → Screen {testScreen}");

// 4. Verify layer is visible
foreach (var layer in _canvas.Layers)
{
    Console.WriteLine($"Layer {layer.GetType().Name}: Visible={layer.Visible}, Order={layer.RenderOrder}");
}
```

**Common Causes**:
1. Layer visibility set to false
2. Camera positioned far from entities
3. Layer mask mismatch (`GetLayerMask() & layer.LayerMask == 0`)
4. Query in adapter doesn't match entity components

---

### 8.2 Selection Not Working

**Symptom**: Clicking entities doesn't select them.

**Diagnostics**:

```csharp
// In StandardInteractionTool.HandleClick
public bool HandleClick(Vector2 worldPos, MouseButton button)
{
    Console.WriteLine($"Tool clicked at: {worldPos}");
    
    var entities = new List<EntityID>();
    _adapter.GetEntities(entities);
    
    foreach (var e in entities)
    {
        var pos = _adapter.GetPosition(e);
        float dist = Vector2.Distance(worldPos, pos);
        Console.WriteLine($"Entity {e} at {pos}, distance: {dist}");
        
        if (dist < 10f) // Hit radius
        {
            Console.WriteLine($"Hit entity {e}!");
            _selection.Select(e);
            return true;
        }
    }
    
    return false;
}
```

**Common Causes**:
1. Input filtered by ImGui (`InputFilter.IsMouseCaptured == true`)
2. Hit radius too small (entities are small on screen)
3. Tool not on top of stack (another tool handling input first)
4. World position calculation incorrect

---

### 8.3 Camera Not Moving

**Symptom**: Pan/zoom inputs don't affect camera.

**Diagnostics**:

```csharp
protected override void OnUpdate(float dt)
{
    Console.WriteLine($"Camera before: Pos={_camera.Position}, Zoom={_camera.Zoom}");
    
    _canvas.Update(dt);
    
    Console.WriteLine($"Camera after: Pos={_camera.Position}, Zoom={_camera.Zoom}");
}
```

**Common Causes**:
1. `MapCanvas.Update()` not called
2. `MapCamera.HandleInput()` not called (if using keyboard pan)
3. Input captured by ImGui
4. Tool not implementing camera pan (StandardInteractionTool does, custom tool may not)

**Fix**: Ensure StandardInteractionTool is on stack and canvas Update is called.

---

### 8.4 Tool Not Receiving Input

**Symptom**: Tool.HandleClick never called.

**Diagnostics**:

```csharp
// In your tool
public bool HandleClick(Vector2 worldPos, MouseButton button)
{
    Console.WriteLine("TOOL CLICKED!"); // Add this
    // ... rest of code ...
}

// Check tool stack
Console.WriteLine($"Active tool: {_canvas.ActiveTool?.GetType().Name}");
Console.WriteLine($"Tool stack depth: {_canvas.Layers.Count}"); // Wrong property!

// Correct way (if MapCanvas exposes it)
// Or just check if ActiveTool is your tool
if (_canvas.ActiveTool is MeasureTool)
    Console.WriteLine("MeasureTool is active");
```

**Common Causes**:
1. Tool not pushed onto stack
2. Another tool on top of stack (consuming input first)
3. Input captured by ImGui
4. Tool's `HandleClick` returns false (input falls through to tools below)

---

### 8.5 Layer Ordering Issues

**Symptom**: Entities draw in wrong order (e.g., overlays behind entities).

**Diagnostic**:

```csharp
// Print layer order
var layers = _canvas.Layers.OrderBy(l => l.RenderOrder).ToList();
foreach (var layer in layers)
{
    Console.WriteLine($"Layer: {layer.GetType().Name}, Order: {layer.RenderOrder}");
}
```

**Fix**: Ensure `RenderOrder` values are correct (lower = behind).

```csharp
// Wrong: overlay draws first (behind)
_canvas.AddLayer(new OverlayLayer() { RenderOrder = 0 });
_canvas.AddLayer(new EntityRenderLayer(_adapter) { RenderOrder = 100 });

// Correct: overlay draws last (on top)
_canvas.AddLayer(new EntityRenderLayer(_adapter) { RenderOrder = 100 });
_canvas.AddLayer(new OverlayLayer() { RenderOrder = 200 });
```

---

### 8.6 Memory Leak from Textures

**Symptom**: Memory usage grows over time.

**Cause**: Loading textures repeatedly without unloading.

**Fix**:

```csharp
// Bad: loads texture every frame
public void DrawSymbol(EntityID entity, RenderContext context)
{
    var texture = Raylib.LoadTexture("icon.png"); // LEAK!
    Raylib.DrawTexture(texture, ...);
    // Never unloaded
}

// Good: load once, store in RenderContext
protected override void OnLoad()
{
    var textures = new Dictionary<string, Texture2D>();
    textures["tank"] = Raylib.LoadTexture("assets/tank.png");
    textures["truck"] = Raylib.LoadTexture("assets/truck.png");
    
    _context.Textures = textures;
}

public void DrawSymbol(EntityID entity, RenderContext context)
{
    var texture = context.Textures["tank"];
    Raylib.DrawTexture(texture, ...);
}

protected override void OnUnload()
{
    foreach (var texture in _context.Textures.Values)
    {
        Raylib.UnloadTexture(texture);
    }
}
```

---

## Appendix A: Complete Interface Summary

```csharp
// Main API
public class MapCanvas
{
    public MapCanvas(MapCamera camera, RenderContext context);
    public void Update(float dt);
    public void Draw();
    public void AddLayer(IMapLayer layer);
    public void RemoveLayer(IMapLayer layer);
    public void PushTool(IMapTool tool);
    public void PopTool();
    public IMapTool? ActiveTool { get; }
    public IReadOnlyList<IMapLayer> Layers { get; }
    public MapCamera Camera { get; }
    public RenderContext Context { get; set; }
}

// Adapter (YOUR IMPLEMENTATION)
public interface IVisualizerAdapter
{
    void GetEntities(List<EntityID> outList);
    Vector2 GetPosition(EntityID entity);
    void DrawSymbol(EntityID entity, RenderContext context);
    bool IsVisible(EntityID entity);
    int GetLayerMask(EntityID entity);
}

// Layers
public interface IMapLayer
{
    void Draw(RenderContext context);
    bool Visible { get; set; }
    int RenderOrder { get; }
}

public class EntityRenderLayer : IMapLayer
{
    public EntityRenderLayer(IVisualizerAdapter adapter, int renderOrder = 100, int layerMask = 0xFF);
    public int LayerMask { get; set; }
}

// Tools
public interface IMapTool
{
    void OnEnter();
    void OnExit();
    void Update(float dt);
    void Draw(RenderContext context);
    bool HandleClick(Vector2 worldPos, MouseButton button);
    bool HandleDrag(Vector2 worldPos, Vector2 delta);
    bool HandleHover(Vector2 worldPos);
}

// Camera
public class MapCamera
{
    public Vector2 Position { get; set; }
    public float Zoom { get; set; }
    public float MinZoom { get; set; }
    public float MaxZoom { get; set; }
    public void HandleInput();
    public void Update(float dt);
    public void FocusOn(Vector2 worldPos, float transitionTime = 0.5f);
    public Vector2 ScreenToWorld(Vector2 screenPos);
    public Vector2 WorldToScreen(Vector2 worldPos);
    public Rectangle GetViewBounds();
}

// Context
public struct RenderContext
{
    public EntityRepository World;
    public MapCamera Camera;
    public Dictionary<string, Texture2D> Textures;
    public Dictionary<string, Font> Fonts;
    public ISelectionState Selection;
    public float DeltaTime;
}

// Selection (YOUR IMPLEMENTATION)
public interface ISelectionState
{
    void Select(EntityID entity);
    void Deselect(EntityID entity);
    void Clear();
    bool IsSelected(EntityID entity);
    IEnumerable<EntityID> GetSelected();
}
```

---

## Appendix B: Common Patterns

### Pattern 1: Basic App Structure

```csharp
public class MyApp : FdpApplication
{
    private EntityRepository _world;
    private MapCamera _camera;
    private MapCanvas _canvas;
    private MyAdapter _adapter;
    
    protected override void OnLoad()
    {
        _world = new EntityRepository();
        _camera = new MapCamera();
        _adapter = new MyAdapter(_world);
        
        var context = new RenderContext
        {
            World = _world,
            Camera = _camera,
            Textures = LoadTextures(),
            Selection = new SimpleSelection()
        };
        
        _canvas = new MapCanvas(_camera, context);
        _canvas.AddLayer(new EntityRenderLayer(_adapter));
        _canvas.PushTool(new StandardInteractionTool(_adapter, context.Selection, _camera));
        
        CreateEntities();
    }
    
    protected override void OnUpdate(float dt) => _canvas.Update(dt);
    protected override void OnDrawWorld() { Raylib.ClearBackground(Color.Black); _canvas.Draw(); }
}
```

### Pattern 2: Adapter with Component Query

```csharp
public class MyAdapter : IVisualizerAdapter
{
    private readonly EntityRepository _world;
    private readonly EntityQuery _query;
    
    public MyAdapter(EntityRepository world)
    {
        _world = world;
        _query = world.Query().With<Position>().With<Renderable>().Build();
    }
    
    public void GetEntities(List<EntityID> outList)
    {
        outList.Clear();
        foreach (var e in _query)
            outList.Add(e);
    }
    
    public Vector2 GetPosition(EntityID entity) => _world.GetComponentRO<Position>(entity).Value;
    
    public void DrawSymbol(EntityID entity, RenderContext context)
    {
        var screenPos = context.Camera.WorldToScreen(GetPosition(entity));
        Raylib.DrawCircleV(screenPos, 5f, Color.White);
    }
    
    public bool IsVisible(EntityID entity) => true;
    public int GetLayerMask(EntityID entity) => 0x01;
}
```

### Pattern 3: Modal Tool

```csharp
// User enters "draw mode"
public void EnterDrawMode()
{
    var drawTool = new PointSequenceTool(points =>
    {
        Console.WriteLine($"Drew {points.Count} points");
        
        // Create entity from points
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new Polygon { Points = points });
        
        // Exit draw mode
        _canvas.PopTool();
        _canvas.PushTool(_standardTool);
    });
    
    _canvas.PopTool(); // Remove standard tool
    _canvas.PushTool(drawTool);
}
```

---

**End of FDP.Toolkit.Vis2D User Guide**

For framework overview, see [USER-GUIDE-OVERVIEW.md](./USER-GUIDE-OVERVIEW.md)  
For application hosting, see [USER-GUIDE-RAYLIB.md](./USER-GUIDE-RAYLIB.md)  
For debugging panels, see [USER-GUIDE-IMGUI.md](./USER-GUIDE-IMGUI.md)
