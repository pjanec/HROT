# FDP.Toolkit.ImGui - User Guide

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
6. [Best Practices](#6-best-practices)
7. [Troubleshooting](#7-troubleshooting)

---

## 1. Introduction

### 1.1 Purpose

`FDP.Toolkit.ImGui` provides **renderer-agnostic debugging panels** for FDP applications. It includes pre-built panels for:
- Entity inspection and component editing
- Event history browsing
- System performance profiling

### 1.2 Key Features

- **Zero Configuration**: Works with any `EntityRepository` out-of-box
- **Reflection-Based UI**: Automatically generates editors for component fields
- **Editable at Runtime**: Modify component values during simulation
- **Event Capture**: Record and replay event streams
- **Performance Metrics**: Track system execution times and circuit breakers

### 1.3 Design Philosophy

- **Generic**: Works with any component types (uses reflection)
- **Decoupled**: No dependency on rendering backend (just ImGui.NET)
- **Extensible**: Subclass panels or use reflection helpers in custom UI

---

## 2. Installation

### 2.1 Add Package Reference

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Framework\FDP.Toolkit.ImGui\FDP.Toolkit.ImGui.csproj" />
</ItemGroup>
```

**Dependencies** (automatically included):
- `Fdp.Kernel` - EntityRepository, Query
- `ModuleHost.Core` - IModuleHost, ISimulationView
- `ImGui.NET` (NuGet: 1.91.0.1) - UI rendering

### 2.2 Using Statements

```csharp
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.ImGui.Panels;
using FDP.Toolkit.ImGui.Utils;
```

---

## 3. Core Concepts

### 3.1 IInspectorContext

**Purpose**: Shared state for selection and hover between panels and visualization.

```csharp
public interface IInspectorContext
{
    Entity? SelectedEntity { get; set; }
    Entity? HoveredEntity { get; set; }
}
```

**Usage**: Create one instance per application, pass to all panels.

```csharp
var context = new InspectorState(); // Default implementation
_entityInspector.Draw(World, context);
_eventBrowser.Draw();
// context.SelectedEntity is synchronized across panels
```

### 3.2 Immediate Mode

ImGui is **immediate mode**: your code runs every frame. No event subscriptions needed.

```csharp
protected override void OnDrawUI()
{
    _inspector.Draw(World, _context); // Runs 60 times/second
}
```

State (selected entity, search filter) is stored in your application, not the panel.

### 3.3 Reflection-Based Rendering

The toolkit uses **cached reflection** to inspect components at runtime:

```csharp
// You define
public struct VehicleState
{
    public Vector2 Position;
    public float Speed;
    public bool IsActive;
}

// ComponentReflector automatically generates
// [X] IsActive
// Position: [0.0] [0.0]
// Speed: [100.0]
```

**Performance**: Reflection cache is built once per type. Minimal overhead after first frame.

---

## 4. API Reference

### 4.1 IInspectorContext

```csharp
namespace FDP.Toolkit.ImGui.Abstractions;

public interface IInspectorContext
{
    /// <summary>Currently selected entity (shown in details panel)</summary>
    Entity? SelectedEntity { get; set; }
    
    /// <summary>Entity under mouse cursor (for highlighting)</summary>
    Entity? HoveredEntity { get; set; }
}

/// <summary>Default implementation with auto-properties</summary>
public class InspectorState : IInspectorContext
{
    public Entity? SelectedEntity { get; set; }
    public Entity? HoveredEntity { get; set; }
}
```

**Thread Safety**: Not thread-safe. Access only from UI thread.

---

### 4.2 EntityInspectorPanel

```csharp
namespace FDP.Toolkit.ImGui.Panels;

public class EntityInspectorPanel
{
    /// <summary>
    /// Render the inspector window.
    /// Call this every frame inside your OnDrawUI() method.
    /// </summary>
    /// <param name="repo">The entity repository to inspect</param>
    /// <param name="context">Shared selection state</param>
    public void Draw(EntityRepository repo, IInspectorContext context);
}
```

**Features**:
- Two-column layout: Entity list (left) | Component details (right)
- Search filter: Type entity ID to find specific entity
- Scrollable lists: Handles thousands of entities
- Auto-refresh: Updates every frame (no manual refresh needed)

**Component Editing**:

Supported types (editable):
- `float` → `ImGui.InputFloat`
- `int` → `ImGui.InputInt`
- `bool` → `ImGui.Checkbox`
- `Vector2` → `ImGui.InputFloat2`
- `Vector3` → `ImGui.InputFloat3`

Unsupported types (read-only text):
- Custom structs (nested)
- Enums (displays numeric value)
- Strings (upcoming feature)

**Write-Back Mechanism**:

```csharp
// When you edit a field in the inspector:
// 1. ImGui.InputFloat(...) detects change
// 2. ComponentReflector updates local object via reflection
// 3. RepoReflector calls repo.SetComponent<T>(entity, updatedValue)
// 4. ECS versioning triggered (downstream systems see change)
```

---

### 4.3 ComponentReflector

```csharp
namespace FDP.Toolkit.ImGui.Utils;

internal class ComponentReflector
{
    /// <summary>
    /// Draw all components attached to an entity with editable fields.
    /// </summary>
    /// <param name="repo">Repository to read/write components</param>
    /// <param name="e">Entity to inspect</param>
    public void DrawComponents(EntityRepository repo, Entity e);
}
```

**Note**: This class is `internal`. Use via `EntityInspectorPanel`. If you need custom rendering, copy the pattern:

```csharp
// Custom component rendering
var allTypes = ComponentTypeRegistry.GetAllTypes();
foreach (var type in allTypes)
{
    if (!RepoReflector.HasComponent(repo, entity, type)) continue;
    
    object? data = RepoReflector.GetComponent(repo, entity, type);
    if (data != null)
    {
        ImGui.Text($"{type.Name}:");
        // ... custom ImGui widgets ...
        
        // Write back
        RepoReflector.SetComponent(repo, entity, type, data);
    }
}
```

---

### 4.4 RepoReflector

```csharp
namespace FDP.Toolkit.ImGui.Utils;

internal static class RepoReflector
{
    /// <summary>Check if entity has component (generic method via reflection)</summary>
    public static bool HasComponent(EntityRepository repo, Entity entity, Type componentType);
    
    /// <summary>Get component value (boxed) via reflection</summary>
    public static object? GetComponent(EntityRepository repo, Entity entity, Type componentType);
    
    /// <summary>Set component value (triggers ECS versioning)</summary>
    public static void SetComponent(EntityRepository repo, Entity entity, Type componentType, object value);
}
```

**Usage** (for custom panels):

```csharp
// Dynamic component access
Type posType = typeof(Position);

if (RepoReflector.HasComponent(repo, entity, posType))
{
    var pos = (Position)RepoReflector.GetComponent(repo, entity, posType);
    
    // Modify
    pos.Value += new Vector2(10, 0);
    
    // Write back
    RepoReflector.SetComponent(repo, entity, posType, pos);
}
```

**Performance**: Uses cached `MethodInfo` per type. First call is slow (~1ms), subsequent calls are fast (<0.01ms).

---

### 4.5 SystemProfilerPanel

```csharp
namespace FDP.Toolkit.ImGui.Panels;

public class SystemProfilerPanel
{
    /// <summary>
    /// Display module execution statistics.
    /// </summary>
    /// <param name="host">ModuleHost to inspect</param>
    public void Draw(IModuleHost host);
}
```

**Features**:
- Table view of all registered modules
- Columns: Name, Executions, Status, Failures
- Color-coded status:
  - **Green**: Normal
  - **Yellow**: Warn (circuit breaker partial)
  - **Red**: Error (circuit breaker open)

**Usage**:

```csharp
// In your app
private IModuleHost _moduleHost;
private SystemProfilerPanel _profilerPanel = new();

protected override void OnLoad()
{
    _moduleHost = new ModuleHost();
    _moduleHost.Register(new MySystem1());
    _moduleHost.Register(new MySystem2());
}

protected override void OnDrawUI()
{
    _profilerPanel.Draw(_moduleHost);
}
```

---

### 4.6 EventBrowserPanel

```csharp
namespace FDP.Toolkit.ImGui.Panels;

public class EventBrowserPanel
{
    /// <summary>
    /// Capture events from the bus.
    /// Call this in your Update() method (not DrawUI).
    /// </summary>
    /// <param name="bus">Event bus to monitor</param>
    /// <param name="currentFrame">Current simulation frame number</param>
    public void Update(FdpEventBus bus, uint currentFrame);
    
    /// <summary>
    /// Render the event log window.
    /// Call this in your DrawUI() method.
    /// </summary>
    public void Draw();
}
```

**Features**:
- Capture events in real-time
- Pause/Resume toggle
- Clear button
- Reverse chronological display (newest first)
- Frame number stamps

**Usage**:

```csharp
private FdpEventBus _eventBus = new();
private EventBrowserPanel _eventPanel = new();
private uint _frame = 0;

protected override void OnUpdate(float dt)
{
    _frame++;
    
    // Simulate events
    _eventBus.Publish(new EntityCreatedEvent { EntityId = 123 });
    
    // Capture
    _eventPanel.Update(_eventBus, _frame);
}

protected override void OnDrawUI()
{
    _eventPanel.Draw();
}
```

**Configuration**:

```csharp
// Max events stored (default: 1000)
// Older events are discarded (FIFO)
// No public API to configure - hardcoded in implementation
```

---

## 5. Usage Examples

### 5.1 Basic Inspector Setup

```csharp
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.ImGui.Panels;
using Fdp.Kernel;

public class MyApp
{
    private EntityRepository _world;
    private EntityInspectorPanel _inspector;
    private IInspectorContext _context;

    public void Initialize()
    {
        _world = new EntityRepository();
        _inspector = new EntityInspectorPanel();
        _context = new InspectorState();
        
        // Create test entities
        for (int i = 0; i < 10; i++)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new Transform 
            { 
                Position = new Vector2(i * 10, 0),
                Rotation = 0f
            });
        }
    }

    public void DrawUI()
    {
        _inspector.Draw(_world, _context);
        
        // Access selected entity
        if (_context.SelectedEntity.HasValue)
        {
            var e = _context.SelectedEntity.Value;
            // ... do something with selection ...
        }
    }
}

public struct Transform
{
    public Vector2 Position;
    public float Rotation;
}
```

### 5.2 Multi-Panel Layout

```csharp
private EntityInspectorPanel _entityInspector = new();
private EventBrowserPanel _eventBrowser = new();
private SystemProfilerPanel _systemProfiler = new();

protected override void OnDrawUI()
{
    // Layout with ImGui docking
    ImGui.DockSpaceOverViewport();
    
    // Each panel in its own window
    _entityInspector.Draw(World, _context);
    _eventBrowser.Draw();
    _systemProfiler.Draw(_moduleHost);
    
    // Custom panel
    if (ImGui.Begin("Custom Stats"))
    {
        ImGui.Text($"FPS: {1.0f / Raylib.GetFrameTime():F1}");
        ImGui.Text($"Entities: {World.EntityCount}");
    }
    ImGui.End();
}
```

### 5.3 Conditional Entity Editing

```csharp
protected override void OnDrawUI()
{
    _inspector.Draw(World, _context);
    
    // Disable editing for certain entities
    if (_context.SelectedEntity.HasValue)
    {
        var e = _context.SelectedEntity.Value;
        
        if (World.HasComponent<ReadOnlyTag>(e))
        {
            ImGui.Begin("Warning");
            ImGui.TextColored(new Vector4(1,0,0,1), "This entity is read-only!");
            ImGui.End();
            
            // Note: ComponentReflector doesn't expose disable API yet
            // Workaround: Clear selection
            _context.SelectedEntity = null;
        }
    }
}
```

### 5.4 Custom Component Rendering

```csharp
// For full control, bypass EntityInspectorPanel
private ComponentReflector _reflector = new ComponentReflector();

if (ImGui.Begin("Custom Inspector"))
{
    if (_context.SelectedEntity.HasValue)
    {
        var e = _context.SelectedEntity.Value;
        
        // Standard components (automatic)
        _reflector.DrawComponents(World, e);
        
        ImGui.Separator();
        
        // Custom component with special UI
        if (World.HasComponent<HealthComponent>(e))
        {
            ref var health = ref World.GetComponentRW<HealthComponent>(e);
            
            ImGui.Text("Health:");
            
            // Custom progress bar
            float fraction = health.Current / health.Max;
            ImGui.ProgressBar(fraction, new Vector2(-1, 0), $"{health.Current}/{health.Max}");
            
            // Quick heal button
            if (ImGui.Button("Heal"))
                health.Current = health.Max;
        }
    }
}
ImGui.End();
```

### 5.5 Event Filtering

```csharp
// EventBrowserPanel doesn't expose filtering yet
// Workaround: Selective publishing

private EventBrowserPanel _eventPanel = new();
private bool _captureMovement = false; // Toggle in UI

protected override void OnUpdate(float dt)
{
    // Important events: always capture
    _eventBus.Publish(new EntityCreatedEvent { ... });
    _eventPanel.Update(_eventBus, _frame);
    
    // Spammy events: optional
    if (_captureMovement)
    {
        _eventBus.Publish(new EntityMovedEvent { ... });
        _eventPanel.Update(_eventBus, _frame);
    }
}

protected override void OnDrawUI()
{
    _eventPanel.Draw();
    
    if (ImGui.Begin("Event Settings"))
    {
        ImGui.Checkbox("Capture Movement Events", ref _captureMovement);
    }
    ImGui.End();
}
```

---

## 6. Best Practices

### 6.1 Selection Synchronization

**Do**: Use a single `IInspectorContext` instance shared across all UI and visualization.

```csharp
// Good
var context = new InspectorState();
_inspector.Draw(World, context);
_map.AddLayer(new EntityRenderLayer(..., context)); // Same instance
```

**Don't**: Create multiple instances (selection will desync).

```csharp
// Bad
_inspector.Draw(World, new InspectorState()); // New instance each frame!
```

### 6.2 Performance with Many Entities

The inspector uses a **limit of 1000 entities** in the list for performance.

**Strategies for Large Worlds**:

1. **Use Search Filter**: Type entity ID to jump directly
2. **Create Debug Queries**: Only show entities with debug tags

```csharp
// Add tag to entities you want to inspect
World.AddComponent(debugEntity, new InspectorVisible());

// Filter in your app
var debugQuery = World.Query().With<InspectorVisible>().Build();

// Pass filtered repository (advanced: requires wrapper)
```

3. **Pagination** (not built-in, requires custom panel):

```csharp
// Custom paginated inspector
const int PAGE_SIZE = 100;
int currentPage = 0;

var entities = World.Query().Build().Skip(currentPage * PAGE_SIZE).Take(PAGE_SIZE);
```

### 6.3 Extending ComponentReflector

To support additional types:

```csharp
// Copy ComponentReflector.cs to your project
// Modify DrawEditableField() method

private bool DrawEditableField(FieldInfo field, ref object? value, string id)
{
    Type fieldType = field.FieldType;
    
    // ... existing cases ...
    
    // Add your custom type
    else if (fieldType == typeof(MyCustomType))
    {
        var val = (MyCustomType)(value ?? default(MyCustomType));
        
        // Render with ImGui
        if (ImGui.InputFloat($"Custom##{id}", ref val.SomeField))
        {
            value = val;
            return ImGui.IsItemDeactivatedAfterEdit();
        }
    }
    
    // ... fallback ...
}
```

### 6.4 Thread Safety

**All panels must be called from the main thread** (where ImGui context lives).

**Don't**:
```csharp
// Wrong: Calling from background thread
Task.Run(() => _inspector.Draw(World, _context)); // Crash!
```

**Do**:
```csharp
// Correct: Call from main loop
protected override void OnDrawUI() // Main thread
{
    _inspector.Draw(World, _context);
}
```

### 6.5 Memory Management

Panels allocate minimal memory:
- `EntityInspectorPanel`: ~100 KB (reflection cache)
- `EventBrowserPanel`: ~100 KB (1000 events × ~100 bytes each)
- `SystemProfilerPanel`: ~10 KB

**No manual cleanup needed**. GC handles it when panels go out of scope.

---

## 7. Troubleshooting

### 7.1 Components Not Showing

**Symptom**: Inspector shows entity but no components listed.

**Causes**:
1. Components not registered in `ComponentTypeRegistry`
2. Entity was just created (components added after inspector queries)

**Fix**:

```csharp
// Ensure components are registered
ComponentTypeRegistry.Register<MyComponent>(); // Usually automatic

// Verify registration
var types = ComponentTypeRegistry.GetAllTypes();
Console.WriteLine($"Registered types: {types.Count()}");
```

### 7.2 Edits Not Persisting

**Symptom**: Change value in inspector, but it reverts next frame.

**Causes**:
1. Another system is overwriting the component
2. Component is a managed type and reference is stale
3. Inspector is reading from wrong repository

**Diagnostic**:

```csharp
// Add logging to your system
public void Update(float dt, ISimulationView view)
{
    ref var pos = ref _repo.GetComponentRW<Position>(entity);
    Console.WriteLine($"System writing position: {pos.Value}");
    pos.Value = CalculateNewPosition();
}

// Compare with inspector changes
// If system writes after inspector, system wins
```

**Fix**: Change update order or disable conflicting system while editing.

### 7.3 Slow Inspector with Many Components

**Symptom**: Inspector UI is laggy when many component types registered.

**Cause**: Reflection overhead (first frame only) or too many ImGui widgets.

**Mitigation**:

```csharp
// Collapse sections you don't need
// ComponentReflector uses ImGui.CollapsingHeader (collapsible by default)

// Filter components (custom implementation)
var relevantTypes = ComponentTypeRegistry.GetAllTypes()
    .Where(t => t.Namespace == "MyGame.Components");

foreach (var type in relevantTypes)
{
    // ... draw only relevant components ...
}
```

### 7.4 Event Browser Not Updating

**Symptom**: Event panel shows old events or none.

**Cause**: Forgot to call `Update()` in your main loop.

**Fix**:

```csharp
protected override void OnUpdate(float dt) // Not DrawUI!
{
    _eventPanel.Update(_eventBus, _frame++);
}

protected override void OnDrawUI()
{
    _eventPanel.Draw(); // Just rendering
}
```

### 7.5 ImGui Window Not Appearing

**Symptom**: Call `_inspector.Draw(...)` but no window shows.

**Causes**:
1. ImGui context not initialized (using outside `FdpApplication`)
2. Window is docked and hidden
3. Window was closed by user (× button)

**Fix**:

```csharp
// 1. Ensure ImGui is initialized (FdpApplication does this)
// 2. Right-click on main window, "Reset Layout"
// 3. Windows don't auto-reopen after close. Check ImGui demo for patterns:

bool _showInspector = true; // Toggle

protected override void OnDrawUI()
{
    // Menu bar
    if (ImGui.BeginMainMenuBar())
    {
        if (ImGui.BeginMenu("Windows"))
        {
            ImGui.MenuItem("Inspector", null, ref _showInspector);
            ImGui.EndMenu();
        }
        ImGui.EndMainMenuBar();
    }
    
    // Conditional draw
    if (_showInspector)
        _inspector.Draw(World, _context);
}
```

---

## Appendix A: Complete API Listing

```csharp
// Abstractions
namespace FDP.Toolkit.ImGui.Abstractions
{
    public interface IInspectorContext
    {
        Entity? SelectedEntity { get; set; }
        Entity? HoveredEntity { get; set; }
    }
    
    public class InspectorState : IInspectorContext { }
}

// Panels
namespace FDP.Toolkit.ImGui.Panels
{
    public class EntityInspectorPanel
    {
        public void Draw(EntityRepository repo, IInspectorContext context);
    }
    
    public class EventBrowserPanel
    {
        public void Update(FdpEventBus bus, uint currentFrame);
        public void Draw();
    }
    
    public class SystemProfilerPanel
    {
        public void Draw(IModuleHost host);
    }
}

// Utils (internal, use via panels)
namespace FDP.Toolkit.ImGui.Utils
{
    internal class ComponentReflector
    {
        public void DrawComponents(EntityRepository repo, Entity e);
    }
    
    internal static class RepoReflector
    {
        public static bool HasComponent(EntityRepository repo, Entity entity, Type componentType);
        public static object? GetComponent(EntityRepository repo, Entity entity, Type componentType);
        public static void SetComponent(EntityRepository repo, Entity entity, Type componentType, object value);
    }
}
```

---

**End of FDP.Toolkit.ImGui User Guide**

For framework overview, see [USER-GUIDE-OVERVIEW.md](./USER-GUIDE-OVERVIEW.md)  
For visualization, see [USER-GUIDE-VIS2D.md](./USER-GUIDE-VIS2D.md)  
For application hosting, see [USER-GUIDE-RAYLIB.md](./USER-GUIDE-RAYLIB.md)
