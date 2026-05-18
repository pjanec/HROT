# FDP.Framework.Raylib - User Guide

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

`FDP.Framework.Raylib` is an **application host framework** that eliminates boilerplate code for:
- Window creation and management
- Main loop timing
- ImGui + Raylib integration
- Input filtering (UI vs world)

### 1.2 Key Features

- **One-Class Application**: Inherit `FdpApplication`, override 4 methods, done
- **Automatic ImGui Setup**: No manual `rlImGui` initialization
- **Input Filtering**: Automatic mouse/keyboard capture detection
- **Configuration-Based**: Window size, title, FPS via simple struct
- **Persistence**: Optional window state save/restore

### 1.3 What You Get

**Without Framework** (manual boilerplate):
```csharp
// ~150 lines of setup code
Raylib.InitWindow(1280, 720, "My App");
Raylib.SetTargetFPS(60);
rlImGui.Setup(true);

while (!Raylib.WindowShouldClose())
{
    // Update logic
    // ImGui setup
    Raylib.BeginDrawing();
    // ...
    rlImGui.Begin();
    // ...
    rlImGui.End();
    Raylib.EndDrawing();
}

rlImGui.Shutdown();
Raylib.CloseWindow();
```

**With Framework**:
```csharp
public class MyApp : FdpApplication
{
    protected override void OnLoad() { /* Setup */ }
    protected override void OnUpdate(float dt) { /* Logic */ }
    protected override void OnDrawWorld() { /* Render */ }
    protected override void OnDrawUI() { /* ImGui */ }
}

static void Main() => new MyApp().Run();
```

---

## 2. Installation

### 2.1 Add Package Reference

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Framework\FDP.Framework.Raylib\FDP.Framework.Raylib.csproj" />
</ItemGroup>
```

**Dependencies** (automatically included):
- `Fdp.Kernel` - EntityRepository
- `ModuleHost.Core` - IModuleHost
- `Raylib-cs` (NuGet: 7.0.2) - Windowing and rendering
- `rlImgui-cs` (NuGet: 3.2.0) - ImGui-Raylib bridge

### 2.2 Using Statements

```csharp
using FDP.Framework.Raylib;
using FDP.Framework.Raylib.Input;
using Raylib_cs;
```

---

## 3. Core Concepts

### 3.1 Application Lifecycle

The `FdpApplication` base class manages a **standard game loop**:

```
1. InitWindow()         ─┐
2. OnLoad()              │ Setup Phase (once)
                        ─┘
3. while (!ShouldClose): ─┐
   ├─ OnUpdate(dt)        │
   ├─ BeginDrawing()      │ Main Loop (60 FPS)
   │  ├─ OnDrawWorld()    │
   │  ├─ rlImGui.Begin()  │
   │  ├─ OnDrawUI()       │
   │  ├─ rlImGui.End()    │
   │  └─ EndDrawing()     │
                        ──┘
4. OnUnload()           ─┐
5. CloseWindow()         │ Cleanup Phase (once)
                        ─┘
```

**Your Responsibilities**:
- Override lifecycle methods
- Don't call `Raylib.InitWindow()` (framework does it)
- Don't create your own loop (framework provides it)

### 3.2 Configuration-Driven Setup

Window properties are defined via `ApplicationConfig`:

```csharp
var config = ApplicationConfig.Default with
{
    WindowTitle = "My Simulation",
    Width = 1920,
    Height = 1080,
    TargetFPS = 120
};

using var app = new MyApp(config);
app.Run();
```

### 3.3 Input Filtering

**Problem**: Clicking an ImGui button also clicks the game world behind it.

**Solution**: `InputFilter` class automatically detects ImGui focus.

```csharp
// Inside your input handling
if (InputFilter.IsMouseCaptured)
    return; // ImGui wants the mouse, don't process world clicks

if (Raylib.IsMouseButtonPressed(MouseButton.Left))
    SelectEntity(); // Safe: only fires when clicking world
```

---

## 4. API Reference

### 4.1 FdpApplication (Abstract Base Class)

```csharp
namespace FDP.Framework.Raylib;

public abstract class FdpApplication : IDisposable
{
    /// <summary>
    /// Constructor. Optionally provide configuration.
    /// </summary>
    protected FdpApplication(ApplicationConfig? config = null);
    
    /// <summary>
    /// Start the application. Blocks until window closes.
    /// </summary>
    public void Run();
    
    // ──────────────────────────────────────────────
    // Lifecycle Methods (Override These)
    // ──────────────────────────────────────────────
    
    /// <summary>
    /// Called once after window creation.
    /// Initialize your World, Kernel, Modules, UI here.
    /// </summary>
    protected virtual void OnLoad() { }
    
    /// <summary>
    /// Called every frame before rendering.
    /// Update simulation, physics, AI here.
    /// Default implementation: Does nothing.
    /// </summary>
    protected virtual void OnUpdate(float dt) { }
    
    /// <summary>
    /// Called every frame inside BeginDrawing()/EndDrawing().
    /// Render your game world (3D/2D graphics) here.
    /// ImGui is NOT available here (use OnDrawUI for that).
    /// </summary>
    protected virtual void OnDrawWorld() { }
    
    /// <summary>
    /// Called every frame inside ImGui context.
    /// Render ImGui windows, panels, menus here.
    /// Raylib drawing is NOT available here (use OnDrawWorld for that).
    /// </summary>
    protected virtual void OnDrawUI() { }
    
    /// <summary>
    /// Called once before window destruction.
    /// Cleanup resources, save state here.
    /// </summary>
    protected virtual void OnUnload() { }
    
    // ──────────────────────────────────────────────
    // Utilities
    // ──────────────────────────────────────────────
    
    /// <summary>
    /// Get current configuration (read-only).
    /// </summary>
    protected ApplicationConfig Config { get; }
    
    /// <summary>
    /// Dispose resources (automatically called at end of Run()).
    /// </summary>
    public void Dispose();
}
```

**Inheritance Requirements**:
- Must override at least `OnLoad()` to do something useful
- Other methods are optional (empty by default)
- Don't call `base.OnLoad()` etc unless you have a reason (base implementations are empty)

---

### 4.2 ApplicationConfig (Struct)

```csharp
namespace FDP.Framework.Raylib;

public record struct ApplicationConfig
{
    /// <summary>Window title</summary>
    public string WindowTitle { get; init; } = "FDP Application";
    
    /// <summary>Initial window width (pixels)</summary>
    public int Width { get; init; } = 1280;
    
    /// <summary>Initial window height (pixels)</summary>
    public int Height { get; init; } = 720;
    
    /// <summary>Target frames per second</summary>
    public int TargetFPS { get; init; } = 60;
    
    /// <summary>Raylib configuration flags (vsync, resizing, etc.)</summary>
    public ConfigFlags ConfigFlags { get; init; } = ConfigFlags.VSyncHint | ConfigFlags.Msaa4xHint;
    
    /// <summary>Enable window state persistence (position, size)</summary>
    public bool PersistenceEnabled { get; init; } = false;
    
    /// <summary>Default configuration (1280×720, 60 FPS)</summary>
    public static ApplicationConfig Default => new();
}
```

**Usage Patterns**:

```csharp
// Use defaults
var app = new MyApp();

// Override specific properties (C# 10 with expressions)
var app = new MyApp(ApplicationConfig.Default with 
{ 
    WindowTitle = "Physics Sim",
    Width = 1920,
    Height = 1080,
    TargetFPS = 120
});

// Fully custom
var config = new ApplicationConfig
{
    WindowTitle = "Custom App",
    Width = 800,
    Height = 600,
    TargetFPS = 30,
    ConfigFlags = ConfigFlags.WindowResizable | ConfigFlags.VSyncHint,
    PersistenceEnabled = true
};
var app = new MyApp(config);
```

**ConfigFlags Reference**:

Common flags (see Raylib-cs documentation for full list):
- `VSyncHint` - Enable V-Sync (default, prevents tearing)
- `Msaa4xHint` - Enable 4× anti-aliasing (smooth lines)
- `WindowResizable` - Allow user to resize window
- `WindowMaximized` - Start maximized
- `WindowUndecorated` - No title bar (fullscreen-ish)
- `WindowTransparent` - Transparent background (advanced)

---

### 4.3 InputFilter (Static Utility)

```csharp
namespace FDP.Framework.Raylib.Input;

public static class InputFilter
{
    /// <summary>
    /// Returns true if ImGui wants the mouse (hovering over UI).
    /// Check this before processing world clicks.
    /// </summary>
    public static bool IsMouseCaptured { get; }
    
    /// <summary>
    /// Returns true if ImGui wants the keyboard (typing in text field).
    /// Check this before processing gameplay hotkeys.
    /// </summary>
    public static bool IsKeyboardCaptured { get; }
}
```

**Usage**:

```csharp
protected override void OnUpdate(float dt)
{
    // Example: Select entity on click
    if (!InputFilter.IsMouseCaptured && Raylib.IsMouseButtonPressed(MouseButton.Left))
    {
        Vector2 mousePos = Raylib.GetMousePosition();
        // ... do hit testing ...
    }
    
    // Example: Delete entity on Del key
    if (!InputFilter.IsKeyboardCaptured && Raylib.IsKeyPressed(KeyboardKey.Delete))
    {
        DeleteSelectedEntity();
    }
}
```

**Implementation Details**:

Internally uses:
```csharp
IsMouseCaptured = ImGui.GetIO().WantCaptureMouse;
IsKeyboardCaptured = ImGui.GetIO().WantCaptureKeyboard;
```

These flags are set by ImGui when UI elements are active.

---

## 5. Usage Examples

### 5.1 Minimal Application

```csharp
using FDP.Framework.Raylib;
using Raylib_cs;

public class MinimalApp : FdpApplication
{
    protected override void OnDrawWorld()
    {
        Raylib.ClearBackground(Color.RayWhite);
        Raylib.DrawText("Hello FDP!", 100, 100, 40, Color.Black);
    }
}

class Program
{
    static void Main() => new MinimalApp().Run();
}
```

**Output**: Window with text. Press Escape or click × to exit.

---

### 5.2 Application with ECS

```csharp
using System.Numerics;
using FDP.Framework.Raylib;
using FDP.Framework.Raylib.Input;
using Fdp.Kernel;
using Raylib_cs;

public class EcsApp : FdpApplication
{
    private EntityRepository _world;
    private EntityQuery _query;
    
    protected override void OnLoad()
    {
        _world = new EntityRepository();
        
        // Create entities
        for (int i = 0; i < 100; i++)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new Position 
            { 
                Value = new Vector2(
                    Raylib.GetRandomValue(0, 1280),
                    Raylib.GetRandomValue(0, 720)
                )
            });
            _world.AddComponent(e, new Velocity 
            { 
                Value = new Vector2(
                    Raylib.GetRandomValue(-100, 100),
                    Raylib.GetRandomValue(-100, 100)
                )
            });
        }
        
        _query = _world.Query().With<Position>().With<Velocity>().Build();
    }
    
    protected override void OnUpdate(float dt)
    {
        // Physics
        foreach (var e in _query)
        {
            ref var pos = ref _world.GetComponentRW<Position>(e);
            ref readonly var vel = ref _world.GetComponentRO<Velocity>(e);
            
            pos.Value += vel.Value * dt;
            
            // Wrap around screen
            if (pos.Value.X < 0) pos.Value.X = 1280;
            if (pos.Value.X > 1280) pos.Value.X = 0;
            if (pos.Value.Y < 0) pos.Value.Y = 720;
            if (pos.Value.Y > 720) pos.Value.Y = 0;
        }
    }
    
    protected override void OnDrawWorld()
    {
        Raylib.ClearBackground(Color.Black);
        
        // Render entities
        foreach (var e in _query)
        {
            ref readonly var pos = ref _world.GetComponentRO<Position>(e);
            Raylib.DrawCircleV(pos.Value, 3f, Color.White);
        }
    }
    
    protected override void OnDrawUI()
    {
        ImGuiNET.ImGui.Begin("Stats");
        ImGuiNET.ImGui.Text($"Entities: {_world.EntityCount}");
        ImGuiNET.ImGui.Text($"FPS: {Raylib.GetFPS()}");
        ImGuiNET.ImGui.End();
    }
    
    protected override void OnUnload()
    {
        _world?.Dispose();
    }
}

public struct Position { public Vector2 Value; }
public struct Velocity { public Vector2 Value; }
```

---

### 5.3 Custom Configuration

```csharp
public class HighPerfApp : FdpApplication
{
    public HighPerfApp() : base(new ApplicationConfig
    {
        WindowTitle = "High Performance Sim",
        Width = 2560,
        Height = 1440,
        TargetFPS = 144,
        ConfigFlags = ConfigFlags.VSyncHint 
                    | ConfigFlags.Msaa4xHint 
                    | ConfigFlags.WindowResizable,
        PersistenceEnabled = true // Save window position
    })
    {
    }
    
    // ... rest of app ...
}
```

---

### 5.4 Input Filtering Example

```csharp
protected override void OnUpdate(float dt)
{
    // Safe world interaction
    if (!InputFilter.IsMouseCaptured)
    {
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            SelectEntityAt(mousePos);
        }
        
        if (Raylib.IsMouseButtonPressed(MouseButton.Right))
        {
            ShowContextMenu();
        }
    }
    
    // Safe hotkeys
    if (!InputFilter.IsKeyboardCaptured)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            TogglePause();
        }
        
        if (Raylib.IsKeyPressed(KeyboardKey.Delete))
        {
            DeleteSelected();
        }
    }
}
```

**Behavior**:
- Mouse over ImGui window → `IsMouseCaptured == true` → clicks don't affect world
- Typing in ImGui text field → `IsKeyboardCaptured == true` → Space doesn't pause

---

### 5.5 Full Application with Modules

```csharp
using ModuleHost.Core;

public class ModularApp : FdpApplication
{
    private EntityRepository _world;
    private IModuleHost _moduleHost;
    private SystemProfilerPanel _profiler;
    
    protected override void OnLoad()
    {
        _world = new EntityRepository();
        _moduleHost = new ModuleHost();
        
        // Register systems
        _moduleHost.Register(new PhysicsSystem(_world));
        _moduleHost.Register(new CollisionSystem(_world));
        _moduleHost.Register(new AISystem(_world));
        
        // Setup profiler
        _profiler = new SystemProfilerPanel();
    }
    
    protected override void OnUpdate(float dt)
    {
        // Execute all registered systems
        _moduleHost.Execute(dt, _world);
    }
    
    protected override void OnDrawWorld()
    {
        Raylib.ClearBackground(Color.DarkGray);
        
        // ... render world ...
    }
    
    protected override void OnDrawUI()
    {
        _profiler.Draw(_moduleHost);
    }
    
    protected override void OnUnload()
    {
        _world?.Dispose();
    }
}
```

---

## 6. Best Practices

### 6.1 Lifecycle Method Ordering

**Typical Usage**:

```csharp
protected override void OnLoad()
{
    // 1. Create data structures
    _world = new EntityRepository();
    
    // 2. Load assets
    _texture = Raylib.LoadTexture("assets/sprite.png");
    
    // 3. Create entities
    SpawnInitialEntities();
    
    // 4. Setup UI
    _inspector = new EntityInspectorPanel();
}

protected override void OnUpdate(float dt)
{
    // 1. Handle input (world interaction)
    if (!InputFilter.IsMouseCaptured)
        HandleMouseInput();
    
    // 2. Update simulation
    _moduleHost.Execute(dt, _world);
    
    // 3. Update camera/UI state
    _camera.Update(dt);
}

protected override void OnDrawWorld()
{
    // 1. Clear
    Raylib.ClearBackground(Color.SkyBlue);
    
    // 2. 3D/2D camera mode
    _camera.BeginMode();
    
    // 3. Render world
    RenderEntities();
    
    // 4. End camera
    _camera.EndMode();
}

protected override void OnDrawUI()
{
    // Draw all ImGui windows
    _inspector.Draw(_world, _context);
    DrawMainMenu();
}

protected override void OnUnload()
{
    // 1. Save state
    SaveConfiguration();
    
    // 2. Unload assets
    Raylib.UnloadTexture(_texture);
    
    // 3. Dispose managed resources
    _world?.Dispose();
}
```

### 6.2 Error Handling

```csharp
protected override void OnLoad()
{
    try
    {
        _world = new EntityRepository();
        LoadScenario("scenario.json");
    }
    catch (FileNotFoundException ex)
    {
        Console.WriteLine($"Scenario not found: {ex.Message}");
        LoadDefaultScenario();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to load: {ex}");
        Environment.Exit(1); // Fatal error
    }
}

protected override void OnUpdate(float dt)
{
    try
    {
        _moduleHost.Execute(dt, _world);
    }
    catch (Exception ex)
    {
        // Log error but don't crash
        Console.WriteLine($"Update error: {ex.Message}");
        // Optionally: disable offending system
    }
}
```

### 6.3 Performance Monitoring

```csharp
private System.Diagnostics.Stopwatch _updateTimer = new();
private System.Diagnostics.Stopwatch _renderTimer = new();

protected override void OnUpdate(float dt)
{
    _updateTimer.Restart();
    
    // ... update logic ...
    
    _updateTimer.Stop();
}

protected override void OnDrawWorld()
{
    _renderTimer.Restart();
    
    // ... rendering ...
    
    _renderTimer.Stop();
}

protected override void OnDrawUI()
{
    ImGui.Begin("Performance");
    ImGui.Text($"Update: {_updateTimer.ElapsedMilliseconds}ms");
    ImGui.Text($"Render: {_renderTimer.ElapsedMilliseconds}ms");
    ImGui.Text($"FPS: {Raylib.GetFPS()}");
    ImGui.End();
}
```

### 6.4 Resource Management

**Do**: Dispose resources in `OnUnload()`.

```csharp
private Texture2D _texture;
private EntityRepository _world;

protected override void OnLoad()
{
    _texture = Raylib.LoadTexture("sprite.png");
    _world = new EntityRepository();
}

protected override void OnUnload()
{
    Raylib.UnloadTexture(_texture);
    _world?.Dispose();
}
```

**Don't**: Dispose in destructor or `Dispose()` (framework handles it).

```csharp
// Wrong
~MyApp() { _world.Dispose(); } // May run too late

// Wrong
public override void Dispose()
{
    _world.Dispose();
    base.Dispose(); // Don't override Dispose
}
```

### 6.5 Configuration Best Practices

**Store Config in File**:

```csharp
// appsettings.json
{
  "WindowTitle": "My App",
  "Width": 1920,
  "Height": 1080,
  "TargetFPS": 60
}

// Load at startup
var json = File.ReadAllText("appsettings.json");
var config = JsonSerializer.Deserialize<ApplicationConfig>(json);
using var app = new MyApp(config);
app.Run();
```

**User-Configurable**:

```csharp
// Save current window state
protected override void OnUnload()
{
    var state = new
    {
        Width = Raylib.GetScreenWidth(),
        Height = Raylib.GetScreenHeight(),
        // ... other settings ...
    };
    
    File.WriteAllText("window_state.json", JsonSerializer.Serialize(state));
}
```

---

## 7. Troubleshooting

### 7.1 Window Not Appearing

**Symptom**: Program runs but no window shows.

**Causes**:
1. Calling `Raylib.InitWindow()` manually (conflicts with framework)
2. Exception in `OnLoad()` crashes before window opens
3. Running in headless environment (no display)

**Fix**:

```csharp
// Remove manual init
// Raylib.InitWindow(...); // DELETE THIS

protected override void OnLoad()
{
    Console.WriteLine("OnLoad called"); // Debug
    
    try
    {
        // ... your code ...
    }
    catch (Exception ex)
    {
        Console.WriteLine($"OnLoad failed: {ex}");
        throw; // Re-throw after logging
    }
}
```

### 7.2 ImGui Not Rendering

**Symptom**: ImGui code runs but nothing appears on screen.

**Causes**:
1. Calling ImGui outside `OnDrawUI()`
2. Calling Raylib drawing inside `OnDrawUI()`
3. ImGui window is off-screen or docked

**Fix**:

```csharp
// Wrong: ImGui in wrong method
protected override void OnDrawWorld()
{
    ImGui.Begin("Test"); // Wrong place!
    ImGui.End();
}

// Correct: ImGui in OnDrawUI
protected override void OnDrawUI()
{
    ImGui.Begin("Test");
    ImGui.End();
}

// Check if window is visible
protected override void OnDrawUI()
{
    ImGui.ShowDemoWindow(); // Shows all ImGui features - helpful for testing
}
```

### 7.3 Input Captured Permanently

**Symptom**: World input stops working after clicking UI once.

**Cause**: ImGui window has "always capture" flag or is transparent overlay.

**Diagnostic**:

```csharp
protected override void OnUpdate(float dt)
{
    Console.WriteLine($"Mouse captured: {InputFilter.IsMouseCaptured}");
    Console.WriteLine($"ImGui WantCaptureMouse: {ImGui.GetIO().WantCaptureMouse}");
}
```

**Fix**: Check for fullscreen transparent windows.

```csharp
// Wrong: Fullscreen overlay
ImGui.SetNextWindowPos(Vector2.Zero);
ImGui.SetNextWindowSize(new Vector2(1280, 720));
ImGui.Begin("Overlay", ImGuiWindowFlags.NoBackground); // Captures all input!

// Correct: Normal window
ImGui.Begin("Inspector"); // Only captures when hovering
```

### 7.4 Low FPS Despite Simple Scene

**Symptoms**: 
- Target FPS is 60, actual FPS is 30
- V-Sync seems to be working incorrectly

**Causes**:
1. V-Sync locking to 30 Hz monitor
2. Expensive OnUpdate logic
3. ConfigFlags not set correctly

**Diagnostic**:

```csharp
Console.WriteLine($"Monitor refresh: {Raylib.GetMonitorRefreshRate(0)} Hz");
Console.WriteLine($"Target FPS: {Config.TargetFPS}");
Console.WriteLine($"Actual FPS: {Raylib.GetFPS()}");
```

**Fixes**:

```csharp
// 1. Disable V-Sync for testing
var config = ApplicationConfig.Default with
{
    ConfigFlags = ConfigFlags.Msaa4xHint // Remove VSyncHint
};

// 2. Profile update
var sw = Stopwatch.StartNew();
protected override void OnUpdate(float dt)
{
    sw.Restart();
    // ... logic ...
    if (sw.ElapsedMilliseconds > 16)
        Console.WriteLine($"Slow update: {sw.ElapsedMilliseconds}ms");
}

// 3. Check monitor
// Ensure monitor is set to 60+ Hz in Windows display settings
```

### 7.5 App Crashes on Exit

**Symptom**: Exception when closing window.

**Causes**:
1. Disposing resources twice
2. Accessing disposed resources in OnUnload
3. Background threads not stopped

**Fix**:

```csharp
protected override void OnUnload()
{
    try
    {
        // Stop background threads first
        _updateThread?.Cancel();
        _updateThread?.Wait(); // Wait for clean exit
        
        // Dispose in reverse order of creation
        _world?.Dispose();
        _moduleHost?.Dispose();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unload error: {ex.Message}");
        // Don't re-throw (already shutting down)
    }
}
```

---

## Appendix A: Complete API Listing

```csharp
// Main class
namespace FDP.Framework.Raylib
{
    public abstract class FdpApplication : IDisposable
    {
        protected FdpApplication(ApplicationConfig? config = null);
        public void Run();
        
        protected virtual void OnLoad();
        protected virtual void OnUpdate(float dt);
        protected virtual void OnDrawWorld();
        protected virtual void OnDrawUI();
        protected virtual void OnUnload();
        
        protected ApplicationConfig Config { get; }
        public void Dispose();
    }
    
    public record struct ApplicationConfig
    {
        public string WindowTitle { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int TargetFPS { get; init; }
        public ConfigFlags ConfigFlags { get; init; }
        public bool PersistenceEnabled { get; init; }
        
        public static ApplicationConfig Default { get; }
    }
}

// Input utility
namespace FDP.Framework.Raylib.Input
{
    public static class InputFilter
    {
        public static bool IsMouseCaptured { get; }
        public static bool IsKeyboardCaptured { get; }
    }
}
```

---

## Appendix B: Comparison with Manual Setup

**Manual Raylib + ImGui** (without framework):
- ~150-200 lines of boilerplate
- Error-prone init/shutdown sequence
- Manual delta time calculation
- Manual ImGui frame management
- Forgotten `UnloadTexture()` calls = memory leaks

**With FDP.Framework.Raylib**:
- ~20 lines of app-specific code
- Automatic resource management
- Consistent lifecycle
- Testable (can mock `FdpApplication` for unit tests)

**When to Use Framework**:
- Starting new FDP project
- Want rapid prototyping
- Need standard game loop

**When to Avoid Framework**:
- Integrating into existing app (different main loop)
- Need custom windowing (multiple windows, embedded)
- Using different rendering backend (not Raylib)

---

**End of FDP.Framework.Raylib User Guide**

For framework overview, see [USER-GUIDE-OVERVIEW.md](./USER-GUIDE-OVERVIEW.md)  
For debugging panels, see [USER-GUIDE-IMGUI.md](./USER-GUIDE-IMGUI.md)  
For 2D visualization, see [USER-GUIDE-VIS2D.md](./USER-GUIDE-VIS2D.md)
