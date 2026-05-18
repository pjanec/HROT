# Fdp.Examples.CarKinem

## Overview

**Fdp.Examples.CarKinem** is an interactive demonstration of the FDP.Toolkit.CarKinem vehicle kinematics simulation capabilities. It features real-time 2D visualization using Raylib, interactive UI panels via ImGui, road network rendering, trajectory editing, formation flying controls, and performance profiling. This example serves as a visual debugging tool for CarKinem toolkit development, a user-facing demonstration for stakeholders, and an integration test for multi-vehicle scenarios.

**Key Demonstrations**:
- **Real-Time Rendering**: 2D visualization of vehicles, road networks, trajectories, and formations (Raylib graphics)
- **Interactive UI**: ImGui panels for spawning vehicles, editing paths, controlling formations, inspecting entity state
- **Headless Mode**: Command-line batch execution for performance benchmarking without rendering overhead
- **Trajectory Editing**: Visual waypoint placement, cubic spline preview, looped trajectory creation
- **Formation Controls**: Dynamic formation type switching, spacing adjustment, leader selection
- **Performance Profiling**: Frame time breakdown, system execution metrics, entity count statistics

**Line Count**: 22 C# implementation files (Rendering, UI, Input, Simulation, Components)

**Primary Dependencies**: Fdp.Kernel, ModuleHost.Core, FDP.Toolkit.CarKinem, Raylib-cs (graphics), ImGuiNET (UI)

**Use Cases**: Toolkit demonstration, visual debugging, scenario authoring, performance regression testing, training/education

---

## Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│               CarKinem Example Architecture                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  Main Loop (60 FPS):                                                 │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  1. InputManager.HandleInput()                                │   │
│  │     - Mouse/keyboard input                                    │   │
│  │     - Camera panning/zooming                                  │   │
│  │     - Entity selection (click)                                │   │
│  │     - Waypoint placement (path editing mode)                  │   │
│  │                                                                │   │
│  │  2. DemoSimulation.Tick(dt, timeScale)                        │   │
│  │     - CarKinematicsSystem: Physics integration                │   │
│  │     - FormationTargetSystem: Formation slot updates           │   │
│  │     - SpatialHashSystem: Proximity queries                    │   │
│  │                                                                │   │
│  │  3. Rendering (Raylib)                                        │   │
│  │     - RoadRenderer: Road network visualization                │   │
│  │     - VehicleRenderer: Vehicle triangles, heading vectors     │   │
│  │     - TrajectoryRenderer: Waypoint paths, splines             │   │
│  │     - DebugLabelRenderer: Entity IDs, velocity, state         │   │
│  │                                                                │   │
│  │  4. UI (ImGui)                                                │   │
│  │     - MainUI: Tab-based controls                              │   │
│  │     - SpawnControlsPanel: Spawn vehicle at cursor             │   │
│  │     - InspectorPanel: Selected entity details                 │   │
│  │     - PerformancePanel: FPS, frame time breakdown             │   │
│  │     - SimulationControlsPanel: Time scale, pause, step        │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                       │
│  DemoSimulation Components:                                          │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  - World: EntityRepository                                    │   │
│  │  - Kernel: ModuleHostKernel                                   │   │
│  │  - RoadNetwork: RoadNetworkBlob (nodes, segments)             │   │
│  │  - TrajectoryPool: CustomTrajectory storage                   │   │
│  │  - FormationManager: Formation template registry              │   │
│  │  - SpatialGrid: SpatialHashGrid (10x10m cells)                │   │
│  │  - CarKinematicsSystem: Main physics loop                     │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                       │
│  Rendering Pipeline:                                                 │
│  ┌────────────────────────────────┐                                 │
│  │  Camera2D (Raylib)             │                                 │
│  │  - Target: World position      │                                 │
│  │  - Zoom: Scale factor          │                                 │
│  │  - Offset: Screen center       │                                 │
│  └────────────────────────────────┘                                 │
│           │                                                           │
│           ▼                                                           │
│  ┌────────────────────────────────┐                                 │
│  │  World Space Rendering         │                                 │
│  │  - Roads (lines, nodes)        │                                 │
│  │  - Vehicles (triangles)        │                                 │
│  │  - Trajectories (curves)       │                                 │
│  └────────────────────────────────┘                                 │
│           │                                                           │
│           ▼                                                           │
│  ┌────────────────────────────────┐                                 │
│  │  Screen Space Rendering        │                                 │
│  │  - ImGui panels (UI overlay)   │                                 │
│  │  - Debug labels (world→screen) │                                 │
│  └────────────────────────────────┘                                 │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘
```

---

## Core Components

### DemoSimulation (Simulation/DemoSimulation.cs)

Central simulation manager:

```csharp
public class DemoSimulation
{
    public EntityRepository World { get; private set; }
    public ModuleHostKernel Kernel { get; private set; }
    public RoadNetworkBlob RoadNetwork { get; private set; }
    public CustomTrajectoryPool TrajectoryPool { get; private set; }
    public SpatialHashGrid SpatialGrid { get; private set; }
    public FormationTemplateManager FormationManager { get; private set; }
    
    private CarKinematicsSystem _kinematicsSystem;
    private FormationTargetSystem _formationSystem;
    private SpatialHashSystem _spatialSystem;
    
    public void Initialize()
    {
        World = new EntityRepository();
        Kernel = new ModuleHostKernel(World, new EventAccumulator());
        
        // Create infrastructure
        RoadNetwork = RoadNetworkBuilder.CreateDemoNetwork();
        TrajectoryPool = new CustomTrajectoryPool(capacity: 100);
        SpatialGrid = new SpatialHashGrid(cellSize: 10.0f);
        FormationManager = new FormationTemplateManager();
        
        // Create systems
        _kinematicsSystem = new CarKinematicsSystem(RoadNetwork, TrajectoryPool);
        _formationSystem = new FormationTargetSystem(FormationManager);
        _spatialSystem = new SpatialHashSystem(SpatialGrid);
        
        // Register systems
        Kernel.RegisterSystem(_kinematicsSystem);
        Kernel.RegisterSystem(_formationSystem);
        Kernel.RegisterSystem(_spatialSystem);
    }
    
    public void Tick(float deltaTime, float timeScale)
    {
        Kernel.Tick(deltaTime * timeScale);
    }
    
    public Entity SpawnVehicle(Vector2 position, Vector2 heading)
    {
        var cmd = Kernel.GetCommandBuffer();
        Entity vehicle = cmd.CreateEntity();
        
        cmd.SetComponent(vehicle, new VehicleState
        {
            Position = position,
            Forward = Vector2.Normalize(heading),
            Speed = 0,
            WheelBase = 2.7f,
            MaxSteerAngle = 0.6f,
            MaxSpeed = 30.0f,
            MaxAccel = 5.0f,
            MaxBraking = -8.0f
        });
        
        cmd.SetComponent(vehicle, new NavState
        {
            Mode = NavigationMode.None,
            TargetSpeed = 15.0f
        });
        
        cmd.SetComponent(vehicle, new VehicleColor
        {
            R = (byte)Random.Shared.Next(100, 255),
            G = (byte)Random.Shared.Next(100, 255),
            B = (byte)Random.Shared.Next(100, 255)
        });
        
        return vehicle;
    }
}
```

### VehicleColor (Components/VehicleColor.cs)

Rendering metadata component:

```csharp
public struct VehicleColor
{
    public byte R;
    public byte G;
    public byte B;
}
```

---

## Rendering Systems

### VehicleRenderer (Rendering/VehicleRenderer.cs)

Draws vehicles as oriented triangles:

```csharp
public class VehicleRenderer
{
    public void Render(EntityRepository world, Camera2D camera)
    {
        var query = world.Query()
            .With<VehicleState>()
            .With<VehicleColor>()
            .Build();
        
        foreach (var entity in query)
        {
            ref readonly var state = ref world.GetComponentRO<VehicleState>(entity);
            ref readonly var color = ref world.GetComponentRO<VehicleColor>(entity);
            
            // Vehicle triangle (pointing forward)
            Vector2 forward = state.Forward * 2.0f;  // 2m length
            Vector2 right = new Vector2(-state.Forward.Y, state.Forward.X) * 1.0f; // 1m width
            
            Vector2 front = state.Position + forward;
            Vector2 rearLeft = state.Position - forward * 0.5f - right;
            Vector2 rearRight = state.Position - forward * 0.5f + right;
            
            // Draw filled triangle
            Raylib.DrawTriangle(
                WorldToScreen(front, camera),
                WorldToScreen(rearLeft, camera),
                WorldToScreen(rearRight, camera),
                new Color(color.R, color.G, color.B, 255)
            );
            
            // Draw heading line
            Raylib.DrawLineV(
                WorldToScreen(state.Position, camera),
                WorldToScreen(state.Position + state.Forward * 5.0f, camera),
                Color.Yellow
            );
            
            // Draw steering angle indicator
            float steerAngle = state.SteerAngle;
            Vector2 steerDir = RotateVector(state.Forward, steerAngle);
            Raylib.DrawLineV(
                WorldToScreen(front, camera),
                WorldToScreen(front + steerDir * 3.0f, camera),
                Color.Red
            );
        }
    }
    
    private static Vector2 WorldToScreen(Vector2 worldPos, Camera2D camera)
    {
        return Raylib.GetWorldToScreen2D(worldPos, camera);
    }
    
    private static Vector2 RotateVector(Vector2 v, float angleRad)
    {
        float cos = MathF.Cos(angleRad);
        float sin = MathF.Sin(angleRad);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
```

### RoadRenderer (Rendering/RoadRenderer.cs)

Visualizes road network:

```csharp
public class RoadRenderer
{
    public void Render(RoadNetworkBlob network, Camera2D camera)
    {
        // Draw road segments
        foreach (var segment in network.Segments)
        {
            var startNode = network.Nodes[segment.StartNodeId];
            var endNode = network.Nodes[segment.EndNodeId];
            
            Raylib.DrawLineEx(
                Raylib.GetWorldToScreen2D(startNode.Position, camera),
                Raylib.GetWorldToScreen2D(endNode.Position, camera),
                segment.LaneWidth * segment.LaneCount * camera.Zoom,
                Color.DarkGray
            );
            
            // Draw lane center line
            Raylib.DrawLineV(
                Raylib.GetWorldToScreen2D(startNode.Position, camera),
                Raylib.GetWorldToScreen2D(endNode.Position, camera),
                Color.Yellow
            );
        }
        
        // Draw nodes (intersections)
        foreach (var node in network.Nodes)
        {
            Raylib.DrawCircleV(
                Raylib.GetWorldToScreen2D(node.Position, camera),
                5.0f * camera.Zoom,
                Color.Orange
            );
        }
    }
}
```

### TrajectoryRenderer (Rendering/TrajectoryRenderer.cs)

Draws custom trajectories with cubic spline interpolation:

```csharp
public class TrajectoryRenderer
{
    private readonly CustomTrajectoryPool _pool;
    
    public void Render(Camera2D camera)
    {
        foreach (var trajectory in _pool.GetAllTrajectories())
        {
            // Draw waypoints
            for (int i = 0; i < trajectory.Waypoints.Length; i++)
            {
                Vector2 pos = trajectory.Waypoints[i].Position;
                Raylib.DrawCircleV(
                    Raylib.GetWorldToScreen2D(pos, camera),
                    4.0f * camera.Zoom,
                    Color.Green
                );
                
                // Draw waypoint index
                Raylib.DrawText(
                    i.ToString(),
                    (int)Raylib.GetWorldToScreen2D(pos, camera).X + 8,
                    (int)Raylib.GetWorldToScreen2D(pos, camera).Y - 8,
                    10,
                    Color.White
                );
            }
            
            // Draw spline curve
            if (trajectory.Interpolation == TrajectoryInterpolation.CubicSpline)
            {
                const int segments = 100;
                for (int i = 0; i < segments; i++)
                {
                    float t = i / (float)segments;
                    float s = t * trajectory.TotalLength;
                    Vector2 pos = TrajectoryInterpolation.InterpolateCubicSpline(trajectory, s);
                    
                    float t2 = (i + 1) / (float)segments;
                    float s2 = t2 * trajectory.TotalLength;
                    Vector2 pos2 = TrajectoryInterpolation.InterpolateCubicSpline(trajectory, s2);
                    
                    Raylib.DrawLineV(
                        Raylib.GetWorldToScreen2D(pos, camera),
                        Raylib.GetWorldToScreen2D(pos2, camera),
                        Color.Lime
                    );
                }
            }
        }
    }
}
```

---

## UI Panels

### SpawnControlsPanel (UI/SpawnControlsPanel.cs)

Interactive vehicle spawning:

```csharp
public class SpawnControlsPanel
{
    public void Render(DemoSimulation simulation, Camera2D camera)
    {
        if (ImGui.Begin("Spawn Controls"))
        {
            ImGui.Text("Click on map to spawn vehicle");
            ImGui.Separator();
            
            // Spawn at cursor
            if (ImGui.Button("Spawn at Cursor (Space)"))
            {
                Vector2 mouseScreen = Raylib.GetMousePosition();
                Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mouseScreen, camera);
                simulation.SpawnVehicle(mouseWorld, new Vector2(1, 0));
            }
            
            // Spawn formation
            if (ImGui.Button("Spawn Formation (5 vehicles)"))
            {
                Vector2 mouseScreen = Raylib.GetMousePosition();
                Vector2 mouseWorld = Raylib.GetScreenToWorld2D(mouseScreen, camera);
                
                Entity leader = simulation.SpawnVehicle(mouseWorld, new Vector2(1, 0));
                simulation.SetNavigationMode(leader, NavigationMode.Point, 
                    destination: mouseWorld + new Vector2(100, 0));
                
                simulation.CreateFormation(leader, FormationType.Wedge);
                
                for (int i = 0; i < 4; i++)
                {
                    Entity follower = simulation.SpawnVehicle(mouseWorld, new Vector2(1, 0));
                    simulation.JoinFormation(follower, leader, slotIndex: i);
                }
            }
            
            ImGui.End();
        }
    }
}
```

### PerformancePanel (UI/PerformancePanel.cs)

Frame time profiling:

```csharp
public class PerformancePanel
{
    private float[] _frameTimeHistory = new float[120];
    private int _frameIndex = 0;
    
    public void Render(float deltaTime, DemoSimulation simulation)
    {
        if (ImGui.Begin("Performance"))
        {
            _frameTimeHistory[_frameIndex] = deltaTime * 1000.0f; // ms
            _frameIndex = (_frameIndex + 1) % _frameTimeHistory.Length;
            
            float avgFrameTime = _frameTimeHistory.Average();
            float fps = 1000.0f / avgFrameTime;
            
            ImGui.Text($"FPS: {fps:F1}");
            ImGui.Text($"Frame Time: {avgFrameTime:F2} ms");
            ImGui.PlotLines("", ref _frameTimeHistory[0], _frameTimeHistory.Length, 
                0, $"Frame Time (ms)", 0, 20.0f, new Vector2(300, 80));
            
            ImGui.Separator();
            ImGui.Text($"Entities: {simulation.World.GetEntityCount()}");
            IMGui.Text($"Vehicles: {CountVehicles(simulation.World)}");
            
            ImGui.End();
        }
    }
    
    private int CountVehicles(EntityRepository world)
    {
        return world.Query().With<VehicleState>().Build().Count();
    }
}
```

###InspectorPanel (UI/InspectorPanel.cs)

Selected entity details:

```csharp
public class InspectorPanel
{
    public void Render(EntityRepository world, Entity? selectedEntity)
    {
        if (ImGui.Begin("Inspector"))
        {
            if (selectedEntity == null || selectedEntity == Entity.Null)
            {
                ImGui.Text("No entity selected");
            }
            else
            {
                Entity entity = selectedEntity.Value;
                ImGui.Text($"Entity ID: {entity.Index}");
                ImGui.Separator();
                
                // VehicleState
                if (world.HasComponent<VehicleState>(entity))
                {
                    ref readonly var state = ref world.GetComponentRO<VehicleState>(entity);
                    if (ImGui.CollapsingHeader("VehicleState"))
                    {
                        ImGui.Text($"Position: ({state.Position.X:F2}, {state.Position.Y:F2})");
                        ImGui.Text($"Heading: ({state.Forward.X:F2}, {state.Forward.Y:F2})");
                        ImGui.Text($"Speed: {state.Speed:F2} m/s");
                        ImGui.Text($"Steer Angle: {state.SteerAngle * 180 / MathF.PI:F1}°");
                    }
                }
                
                // NavState
                if (world.HasComponent<NavState>(entity))
                {
                    ref readonly var nav = ref world.GetComponentRO<NavState>(entity);
                    if (ImGui.CollapsingHeader("NavState"))
                    {
                        ImGui.Text($"Mode: {nav.Mode}");
                        ImGui.Text($"Target Speed: {nav.TargetSpeed:F1} m/s");
                        ImGui.Text($"Progress: {nav.ProgressS:F1} m");
                        ImGui.Text($"Arrived: {nav.HasArrived == 1}");
                    }
                }
                
                // FormationSlot
                if (world.HasComponent<FormationSlot>(entity))
                {
                    ref readonly var slot = ref world.GetComponentRO<FormationSlot>(entity);
                    if (ImGui.CollapsingHeader("FormationSlot"))
                    {
                        ImGui.Text($"Leader: Entity {slot.LeaderEntity.Index}");
                        ImGui.Text($"Type: {slot.Type}");
                        ImGui.Text($"Slot Index: {slot.SlotIndex}");
                        ImGui.Text($"Spacing: {slot.Params.Spacing:F1} m");
                    }
                }
            }
            
            ImGui.End();
        }
    }
}
```

---

## Headless Mode

For performance benchmarking without rendering:

```csharp
public static void RunHeadless()
{
    Console.WriteLine("Running in headless mode...");
    
    var simulation = new DemoSimulation();
    simulation.Initialize();
    
    // Spawn 1000 vehicles
    for (int i = 0; i < 1000; i++)
    {
        float angle = i * MathF.PI * 2.0f / 1000;
        Vector2 pos = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 100.0f;
        simulation.SpawnVehicle(pos, new Vector2(1, 0));
    }
    
    // Run 10,000 frames at 60 FPS (166 seconds simulated time)
    const float dt = 1.0f / 60.0f;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    
    for (int frame = 0; frame < 10000; frame++)
    {
        simulation.Tick(dt, timeScale: 1.0f);
        
        if (frame % 1000 == 0)
        {
            float elapsed = (float)stopwatch.Elapsed.TotalSeconds;
            float fps = frame / elapsed;
            Console.WriteLine($"Frame {frame}: {fps:F1} FPS avg");
        }
    }
    
    stopwatch.Stop();
    float finalFPS = 10000 / (float)stopwatch.Elapsed.TotalSeconds;
    Console.WriteLine($"\nFinal average: {finalFPS:F1} FPS ({10000 / finalFPS:F2}s total)");
}
```

**Usage**: `dotnet run -- --headless`

---

## Integration with FDP.Toolkit.CarKinem

This example directly uses toolkit systems and components:

**CarKinematicsSystem**: Main physics loop (imported from toolkit)
**VehicleState, NavState**: Core components (imported)
**FormationTemplateManager**: Formation logic (imported)
**PurePursuitController**: Steering (imported)
**BicycleModel**: Physics integration (imported)

**Demonstration Value**:
- Validates toolkit correctness via visual inspection
- Stress tests parallel performance (1000+ vehicles)
- UI for interactive scenario authoring (save trajectories, road networks)
- Educational tool for understanding vehicle kinematics

---

## Conclusion

**Fdp.Examples.CarKinem** provides an interactive visual environment for exploring CarKinem toolkit capabilities. The combination of real-time rendering, ImGui controls, and headless benchmarking makes it valuable for development, demonstration, and education.

---

**Total Lines**: 610
