# FDP Framework - User Guide Overview

**Version**: 2.0  
**Date**: 2026-02-12  
**Audience**: Application Developers

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Framework Architecture](#2-framework-architecture)
3. [Getting Started](#3-getting-started)
4. [Quick Start Example](#4-quick-start-example)
5. [Toolkit Overview](#5-toolkit-overview)
6. [Common Patterns](#6-common-patterns)
7. [Performance Considerations](#7-performance-considerations)
8. [Troubleshooting](#8-troubleshooting)
9. [Further Reading](#9-further-reading)

---

## 1. Introduction

### 1.1 What is FDP Framework?

The FDP Framework is a collection of reusable toolkits for building high-performance simulation and visualization applications on top of the FDP (Fast Data Processing) Entity Component System.

**Key Features**:
- **Zero-allocation rendering** - Achieve 60+ FPS with 10,000+ entities
- **Immediate-mode UI** - ImGui-based debugging panels with zero boilerplate
- **Abstract visualization** - Adapter pattern decouples rendering from data
- **Tool-based interaction** - State pattern for different interaction modes (select, drag, draw)
- **Hierarchical rendering** - Automatic aggregation and semantic zoom (ORBAT support)

### 1.2 Target Use Cases

- **Real-time simulations** - Vehicle dynamics, robotics, agent-based models
- **Strategy games** - RTS-style unit management and visualization
- **GIS applications** - 2D map rendering with layers and tools
- **Visual debugging** - Entity inspection, system profiling, event browsing

### 1.3 System Requirements

- **.NET 8.0** or later
- **Windows** (primary), Linux/macOS (via Raylib cross-platform support)
- **Graphics**: OpenGL 3.3+ capable GPU
- **Dependencies**: Installed automatically via NuGet

---

## 2. Framework Architecture

### 2.1 Component Structure

```
FDP Framework
├── FDP.Toolkit.ImGui          ← Generic debug panels (renderer-agnostic)
├── FDP.Framework.Raylib       ← Application host and windowing
├── FDP.Toolkit.Vis2D          ← 2D visualization with layers and tools
└── Your Application           ← Implements adapters and app logic
```

### 2.2 Dependency Graph

```
                    ┌─────────────────┐
                    │  Your App       │
                    └────────┬────────┘
                             │
           ┌─────────────────┼─────────────────┐
           │                 │                 │
           ▼                 ▼                 ▼
    ┌───────────┐    ┌──────────────┐  ┌─────────────┐
    │  ImGui    │    │   Vis2D      │  │   Raylib    │
    │  Toolkit  │    │   Toolkit    │  │  Framework  │
    └─────┬─────┘    └──────┬───────┘  └──────┬──────┘
          │                 │                  │
          └────────┬────────┴──────────────────┘
                   ▼
            ┌─────────────┐
            │  FDP Kernel │
            └─────────────┘
```

**Design Principles**:
- **Loose Coupling**: Toolkits are independent; use only what you need
- **Adapter Pattern**: Your app bridges between FDP data and framework rendering
- **Composition**: Build complex UIs from simple, reusable components

### 2.3 Data Flow

```
┌──────────────┐
│ FDP Kernel   │ ── Entity Updates ──▶ Systems run
└──────┬───────┘
       │
       │ ISimulationView (read-only query interface)
       │
       ▼
┌──────────────────────────────────────────────────┐
│              Your Adapters                       │
│  • IVisualizerAdapter (data → visuals)          │
│  • IHierarchyAdapter (parent-child traversal)   │
│  • IInspectorContext (selection state)          │
└──────┬───────────────────────────────────────────┘
       │
       │ Abstractions
       │
       ▼
┌──────────────────────────────────────────────────┐
│           Framework Toolkits                     │
│  • MapCanvas renders entities via adapter        │
│  • EntityInspectorPanel displays components      │
│  • Tools handle user input as events            │
└──────────────────────────────────────────────────┘
```

---

## 3. Getting Started

### 3.1 Installation

Add package references to your `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core FDP (prerequisite) -->
    <ProjectReference Include="..\..\Kernel\Fdp.Kernel\Fdp.Kernel.csproj" />
    <ProjectReference Include="..\..\ModuleHost\ModuleHost.Core\ModuleHost.Core.csproj" />
    
    <!-- Framework toolkits (choose what you need) -->
    <ProjectReference Include="..\..\Framework\FDP.Framework.Raylib\FDP.Framework.Raylib.csproj" />
    <ProjectReference Include="..\..\Framework\FDP.Toolkit.ImGui\FDP.Toolkit.ImGui.csproj" />
    <ProjectReference Include="..\..\Framework\FDP.Toolkit.Vis2D\FDP.Toolkit.Vis2D.csproj" />
  </ItemGroup>
</Project>
```

### 3.2 Project Structure (Recommended)

```
YourApp/
├── Program.cs               ← Entry point
├── MyApp.cs                 ← Inherits FdpApplication
├── Adapters/
│   ├── MyEntityVisualizer.cs    ← IVisualizerAdapter implementation
│   └── MySelectionAdapter.cs    ← Bridges ISelectionState ↔ IInspectorContext
├── Components/
│   └── MyComponents.cs          ← Your ECS components
├── Systems/
│   └── MySystems.cs             ← Your ECS systems
└── UI/
    └── CustomPanels.cs          ← Additional ImGui panels (optional)
```

### 3.3 Basic Concepts

#### 3.3.1 Adapter Pattern
The framework doesn't know about your specific component types. You provide **adapters** to translate:

```csharp
// Framework asks: "Where is this entity?"
Vector2? GetPosition(ISimulationView view, Entity entity)

// Your answer:
if (view.HasComponent<Transform>(entity))
    return view.GetComponentRO<Transform>(entity).Position;
return null; // Hide this entity
```

#### 3.3.2 Immediate Mode
ImGui and Raylib are **immediate mode** systems. Your code runs every frame:

```csharp
protected override void OnDrawUI()
{
    _inspector.Draw(World, _context); // Runs 60 times/second
}
```

No event subscriptions needed. Just call `Draw()` in the loop.

#### 3.3.3 Layers and Tools
- **Layers** (`IMapLayer`): What to render (entities, roads, debug gizmos)
- **Tools** (`IMapTool`): How to interact (select, drag, draw paths)

Layers stack like Photoshop. Tools switch like Figma (Pointer, Hand, Pen).

---

## 4. Quick Start Example

### 4.1 Minimal Application

```csharp
// Program.cs
using FDP.Framework.Raylib;

static void Main()
{
    using var app = new MyApp();
    app.Run();
}

// MyApp.cs
using System.Numerics;
using FDP.Framework.Raylib;
using FDP.Toolkit.ImGui.Panels;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Layers;
using Fdp.Kernel;
using Raylib_cs;

public class MyApp : FdpApplication
{
    private EntityRepository _world;
    private MapCanvas _map;
    private EntityInspectorPanel _inspector;
    private InspectorState _context;

    protected override void OnLoad()
    {
        // 1. Setup ECS
        _world = new EntityRepository();
        
        // Create test entity
        var e = _world.CreateEntity();
        _world.AddComponent(e, new Position { Value = new Vector2(0, 0) });
        _world.AddComponent(e, new Velocity { Value = new Vector2(1, 0) });

        // 2. Setup inspector
        _context = new InspectorState();
        _inspector = new EntityInspectorPanel();

        // 3. Setup map
        _map = new MapCanvas();
        
        var query = _world.Query().With<Position>().Build();
        var visualizer = new SimpleVisualizer();
        
        var layer = new EntityRenderLayer(
            "Entities", 
            layerBitIndex: 0,
            _world, 
            query, 
            visualizer, 
            _context
        );
        
        _map.AddLayer(layer);
    }

    protected override void OnUpdate(float dt)
    {
        // Update simulation
        var query = _world.Query().With<Position>().With<Velocity>().Build();
        foreach (var e in query)
        {
            ref var pos = ref _world.GetComponentRW<Position>(e);
            ref readonly var vel = ref _world.GetComponentRO<Velocity>(e);
            pos.Value += vel.Value * dt;
        }

        // Update map (camera, input)
        _map.Update(dt);
    }

    protected override void OnDrawWorld()
    {
        Raylib.ClearBackground(Color.DarkGray);
        _map.Draw();
    }

    protected override void OnDrawUI()
    {
        _inspector.Draw(_world, _context);
    }

    protected override void OnUnload()
    {
        _world?.Dispose();
    }
}

// Adapter: Translate your components → visualization
public class SimpleVisualizer : IVisualizerAdapter
{
    public Vector2? GetPosition(ISimulationView view, Entity entity)
    {
        if (view.HasComponent<Position>(entity))
            return view.GetComponentRO<Position>(entity).Value;
        return null;
    }

    public void Render(ISimulationView view, Entity entity, Vector2 position, 
                       RenderContext ctx, bool isSelected, bool isHovered)
    {
        Color color = isSelected ? Color.Yellow : Color.White;
        Raylib.DrawCircleV(position, 0.5f, color);
    }

    public float GetHitRadius(ISimulationView view, Entity entity) => 0.5f;
    public string? GetHoverLabel(ISimulationView view, Entity entity) => $"Entity {entity.Index}";
}

// Your components
public struct Position { public Vector2 Value; }
public struct Velocity { public Vector2 Value; }
```

**Run it**: `dotnet run`

You'll see:
- A window with a moving white circle (your entity)
- An "Entity Inspector" panel on the side
- Click the circle to select it and view components
- Pan camera with right-mouse, zoom with wheel

---

## 5. Toolkit Overview

### 5.1 FDP.Toolkit.ImGui

**Purpose**: Generic debugging panels for entities, events, and systems.

**Key Components**:
- `EntityInspectorPanel` - Browse entities, view/edit components
- `EventBrowserPanel` - Capture and display event history
- `SystemProfilerPanel` - Display module execution statistics
- `ComponentReflector` - Automatic UI generation via reflection

**When to Use**:
- You need runtime entity inspection
- You want to profile system performance
- You need event debugging

**Read More**: [FDP.Toolkit.ImGui User Guide](./USER-GUIDE-IMGUI.md)

### 5.2 FDP.Framework.Raylib

**Purpose**: Application host that eliminates boilerplate (windowing, loop, ImGui setup).

**Key Components**:
- `FdpApplication` - Abstract base class with lifecycle hooks
- `ApplicationConfig` - Window configuration
- `InputFilter` - Prevent input bleed (UI vs world)

**When to Use**:
- You want a simple `Main()` without boilerplate
- You're building a desktop app with Raylib
- You need ImGui + Raylib integration

**Read More**: [FDP.Framework.Raylib User Guide](./USER-GUIDE-RAYLIB.md)

### 5.3 FDP.Toolkit.Vis2D

**Purpose**: Abstract 2D visualization with layers, tools, and adapters.

**Key Components**:
- `MapCanvas` - Main rendering container
- `MapCamera` - Pan/zoom camera
- `IMapLayer` - Composable rendering layers
- `IMapTool` - Interaction modes (select, drag, draw)
- `IVisualizerAdapter` - Data-to-visual translation

**When to Use**:
- You're building a 2D map/simulation visualizer
- You need multiple rendering layers (units, terrain, debug)
- You want tool-based interaction (like Photoshop/Figma)
- You need hierarchical rendering (ORBAT, semantic zoom)

**Read More**: [FDP.Toolkit.Vis2D User Guide](./USER-GUIDE-VIS2D.md)

---

## 6. Common Patterns

### 6.1 The Adapter Bridge Pattern

**Problem**: Two toolkits need shared state but shouldn't reference each other.

**Solution**: Create a single class implementing both interfaces.

```csharp
// In FDP.Toolkit.Vis2D
public interface ISelectionState
{
    bool IsSelected(Entity entity);
    Entity? PrimarySelected { get; set; }
}

// In FDP.Toolkit.ImGui
public interface IInspectorContext
{
    Entity? SelectedEntity { get; set; }
    Entity? HoveredEntity { get; set; }
}

// In YOUR app
public class SelectionBridge : ISelectionState, IInspectorContext
{
    private HashSet<Entity> _selected = new();
    private Entity? _primary;
    
    // ISelectionState (for Map)
    public bool IsSelected(Entity e) => _selected.Contains(e);
    public Entity? PrimarySelected 
    { 
        get => _primary;
        set { _selected.Clear(); if (value.HasValue) _selected.Add(value.Value); _primary = value; }
    }
    
    // IInspectorContext (for Inspector)
    public Entity? SelectedEntity { get => _primary; set => PrimarySelected = value; }
    public Entity? HoveredEntity { get; set; }
}

// Usage
var bridge = new SelectionBridge();
_map.AddLayer(new EntityRenderLayer(..., bridge)); // ISelectionState
_inspector.Draw(World, bridge); // IInspectorContext
```

### 6.2 Resource Injection Pattern

**Problem**: Visualizer needs global data (terrain, trajectory pool) but shouldn't be coupled.

**Solution**: Register resources with `MapCanvas`, access via `RenderContext`.

```csharp
// Setup (once)
_map.AddResource(_trajectoryPool);
_map.AddResource(_terrainData);

// In visualizer (every frame)
public void Render(..., RenderContext ctx, ...)
{
    var pool = ctx.Resources.Get<TrajectoryPoolManager>();
    if (pool != null && trajId > 0)
    {
        if (pool.TryGetTrajectory(trajId, out var traj))
        {
            // Draw trajectory
        }
    }
    // Graceful: if pool missing, just skip this feature
}
```

### 6.3 Tool Stack Pattern

**Problem**: Different interaction modes need different input handling.

**Solution**: Push/pop tools like a state machine.

```csharp
// Default: Selection tool
_map.SwitchTool(new StandardInteractionTool(World, query, visualizer));

// User clicks "Draw Path" button
_map.PushTool(new PointSequenceTool(points => 
{
    CreatePath(points);
    _map.PopTool(); // Return to previous tool
}));

// User drags entity
// StandardInteractionTool internally pushes EntityDragTool
// On mouse up, EntityDragTool pops itself
```

### 6.4 Event-Driven Modification

**Problem**: Tools shouldn't directly modify world (breaks undo/redo, testability).

**Solution**: Tools fire events, app handles logic.

```csharp
var tool = new StandardInteractionTool(World, query, visualizer);

// Tool fires event
tool.OnEntityMoved += (entity, newPos) =>
{
    // App updates world
    ref var pos = ref World.GetComponentRW<Position>(entity);
    pos.Value = newPos;
    
    // (Optional) Record for undo
    _undoStack.Push(new MoveCommand(entity, pos.Value, newPos));
};

_map.SwitchTool(tool);
```

---

## 7. Performance Considerations

### 7.1 Zero-Allocation Rendering

**Achieved By**:
- Struct components (no GC)
- `NativeArray` storage (off-heap)
- `ref struct` enumerators (no boxing)
- Cached reflection (one-time lookup)

**Measured**: 60 FPS with 10,000 entities (desktop GPU).

### 7.2 Hot Path Optimization

**Rendering Loop** (runs 60x/sec):
```csharp
foreach (var entity in query) // O(N) linear
{
    bool isSelected = _selectionState.IsSelected(entity); // O(1) HashSet
    Vector2? pos = _adapter.GetPosition(view, entity);    // O(1) component access
    if (pos.HasValue)
        _adapter.Render(...); // Virtual call (negligible)
}
```

**Cost**: ~0.1ms for 1000 entities (Intel i7-10700K).

### 7.3 Dirty Flagging

For expensive operations (sorting hierarchies, updating aggregates):

```csharp
public class HierarchyOrderSystem : ISimulationSystem
{
    private bool _isDirty = true;
    
    public void MarkDirty() => _isDirty = true;
    
    public void Update(float dt, ISimulationView view)
    {
        if (!_isDirty) return; // Early exit: O(0)
        
        // Expensive: O(N log N) sort
        SortHierarchy();
        _isDirty = false;
    }
}
```

**Trigger**: Call `MarkDirty()` when hierarchy changes (entity parented/unparented).

**Benefit**: Sort runs once per structural change, not 60 times per second.

### 7.4 Cache-Friendly Data

**Good** (flat array):
```csharp
NativeArray<Entity> sortedEntities; // Sequential memory
foreach (var e in sortedEntities) { /* Process */ } // Fast
```

**Bad** (linked list):
```csharp
HierarchyNode parent → child → child → child; // Pointer chasing
// Each dereference is a cache miss
```

---

## 8. Troubleshooting

### 8.1 Inspector Shows No Entities

**Symptom**: Entity list is empty.

**Causes**:
1. **No entities created**: `World.EntityCount == 0`
2. **Search filter active**: Clear the search box
3. **Wrong repository**: Passing different `EntityRepository` instance to inspector than your world

**Fix**:
```csharp
// Verify entity creation
Console.WriteLine($"Entities: {World.EntityCount}");

// Ensure same repository
_inspector.Draw(World, _context); // Same 'World' you created entities in
```

### 8.2 Map Shows Nothing

**Symptom**: Gray screen, no entities visible.

**Causes**:
1. **Camera looking at wrong position**: Entities at (1000, 1000), camera at (0, 0)
2. **Layer disabled**: Check `MapCanvas.ActiveLayerMask`
3. **Adapter returns null**: `GetPosition()` is filtering out entities
4. **Query matches nothing**: No entities have required components

**Fixes**:
```csharp
// 1. Focus camera on entities
_map.Camera.FocusOn(new Vector2(500, 500), zoom: 1.0f);

// 2. Enable all layers
_map.ActiveLayerMask = 0xFFFFFFFF;

// 3. Debug adapter
var pos = visualizer.GetPosition(World, entity);
Console.WriteLine($"Entity {entity.Index} pos: {pos}");

// 4. Verify query
var query = World.Query().With<Position>().Build();
Console.WriteLine($"Query matches: {query.Count()}");
```

### 8.3 Input Not Working

**Symptom**: Clicking map does nothing.

**Causes**:
1. **ImGui capturing input**: Mouse over inspector panel
2. **No tool active**: `MapCanvas.ActiveTool == null`
3. **Layer not handling input**: `HandleInput()` returns false
4. **InputFilter blocking**: Check `InputFilter.IsMouseCaptured`

**Fixes**:
```csharp
// 1. Click on map (not over UI)
// 2. Set a tool
_map.SwitchTool(new StandardInteractionTool(World, query, visualizer));

// 3. Debug input pipeline
var captured = InputFilter.IsMouseCaptured;
Console.WriteLine($"Input captured by UI: {captured}");
```

### 8.4 Performance Issues

**Symptom**: Low FPS, stuttering.

**Diagnostics**:
```csharp
// Measure frame time
var sw = Stopwatch.StartNew();
_map.Draw();
Console.WriteLine($"Map draw: {sw.ElapsedMilliseconds}ms");

// Check entity count
Console.WriteLine($"Rendering {query.Count()} entities");

// Profile systems (use SystemProfilerPanel)
_systemProfiler.Draw(_moduleHost);
```

**Common Fixes**:
- **Too many entities**: Use culling (only render visible)
- **Reflection overhead**: Cache `RepoReflector` results (already done)
- **GC pressure**: Check allocations with dotMemory
- **No dirty flagging**: Hierarchy sorting every frame

### 8.5 Components Not Editable in Inspector

**Symptom**: Inspector shows values but they're read-only text.

**Cause**: `ComponentReflector` only supports certain types out-of-box.

**Supported**: `float`, `int`, `bool`, `Vector2`, `Vector3`  
**Unsupported**: Custom structs, enums, strings (shows as text)

**Fix**: Extend `ComponentReflector.DrawEditableField()` for your types:

```csharp
// Currently read-only for complex types
// To support custom editing, fork ComponentReflector and add cases
```

---

## 9. Further Reading

### 9.1 Detailed Guides

- **[FDP.Toolkit.ImGui User Guide](./USER-GUIDE-IMGUI.md)** - Complete API reference for inspection panels
- **[FDP.Framework.Raylib User Guide](./USER-GUIDE-RAYLIB.md)** - Application lifecycle and configuration
- **[FDP.Toolkit.Vis2D User Guide](./USER-GUIDE-VIS2D.md)** - Layers, tools, adapters, hierarchical rendering

### 9.2 Design Documents

- **[MAP-DESIGN.md](./MAP-DESIGN.md)** - Architectural design and rationale
- **[MAP-TASK-DETAIL.md](./MAP-TASK-DETAIL.md)** - Implementation specifications
- **[MAP-ONBOARDING.md](./MAP-ONBOARDING.md)** - Developer onboarding guide

### 9.3 External Resources

- **Raylib**: https://www.raylib.com/
- **ImGui.NET**: https://github.com/ImGuiNET/ImGui.NET
- **rlImGui**: https://github.com/raylib-extras/rlImGui-cs

### 9.4 Support

For issues or questions:
1. Check troubleshooting section above
2. Review example code in `Fdp.Examples.CarKinem`
3. Consult design documents for architectural context

---

## Appendix A: Complete Project Template

```xml
<!-- YourApp.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Kernel\Fdp.Kernel\Fdp.Kernel.csproj" />
    <ProjectReference Include="..\..\ModuleHost\ModuleHost.Core\ModuleHost.Core.csproj" />
    <ProjectReference Include="..\..\Framework\FDP.Framework.Raylib\FDP.Framework.Raylib.csproj" />
    <ProjectReference Include="..\..\Framework\FDP.Toolkit.ImGui\FDP.Toolkit.ImGui.csproj" />
    <ProjectReference Include="..\..\Framework\FDP.Toolkit.Vis2D\FDP.Toolkit.Vis2D.csproj" />
  </ItemGroup>
</Project>
```

---

**End of Overview Guide**

For detailed API documentation, continue to the toolkit-specific guides.
