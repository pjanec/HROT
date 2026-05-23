# Fdp.Examples.CarKinem

**Project path:** `FDP/Examples/Fdp.Examples.CarKinem/Fdp.Examples.CarKinem.csproj`
**Documented:** 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the canonical
architectural reference.

---

## Executive Overview

`Fdp.Examples.CarKinem` is a self-contained, runnable simulation demonstrating
car kinematics integrated into the FDP ECS framework. It spawns one or more
wheeled vehicles on a 2D road network and animates them using a bicycle-model
kinematics system driven by navigation states.

Key topics demonstrated:

- **Bicycle-model kinematics** (`CarKinematicsSystem` from `CarKinem.Systems`)
  updates each vehicle's speed, steering angle, and position every tick,
  constrained by per-class physical parameters (max speed, max lateral
  acceleration, max steering angle).
- **Dual navigation modes** -- vehicles follow either a JSON-loaded road graph
  (segment-by-segment pathfinding) or an explicit custom trajectory (linear or
  Catmull-Rom spline).
- **Formation driving** -- a leader/follower system (`FormationTargetSystem`,
  `FormationController`, `FormationFollower`) keeps a group of vehicles in
  column, wedge, or line formation as the leader navigates.
- **Spatial indexing** (`SpatialHashSystem`) provides a spatial hash grid over
  vehicle positions for future collision or query use.
- **Time control** -- supports real-time continuous playback, user-driven step
  mode (one physics tick at a time), and variable time scale.
- **Flight recorder** -- `AsyncRecorder`/`PlaybackController` can capture and
  replay the entire ECS state frame by frame.
- **Headless mode** -- `HeadlessCarKinemApp` wraps the same kernel and systems
  without Raylib for automated tests.
- **ImGui UI** -- live spawning controls, simulation controls, entity inspector,
  event browser, and performance profiler are presented via docked ImGui panels.

---

## Architecture

### Layered Structure

The application is structured in four horizontal layers:

```
+-----------------------------------------------------------+
|                  Application Shell                        |
|  CarKinemApp (FdpApplication subclass, Raylib window)     |
+-----------------------------------------------------------+
|                  UI / Presentation Layer                   |
|  MainUI  SpawnControlsPanel  SimulationControlsPanel      |
|  PerformancePanel  EntityInspectorPanel  EventBrowserPanel |
+-----------------------------------------------------------+
|               Visualization Layer                         |
|  MapCanvas  RoadMapLayer  TrajectoryMapLayer               |
|  VehicleVisualizer  SelectionManager                      |
+-----------------------------------------------------------+
|               Simulation / ECS Layer                      |
|  EntityRepository  ModuleHostKernel  EventAccumulator     |
|  SpatialHashSystem  FormationTargetSystem                 |
|  VehicleCommandSystem  CarKinematicsSystem                |
+-----------------------------------------------------------+
|               Data / Resource Layer                       |
|  RoadNetworkBlob  TrajectoryPoolManager                   |
|  FormationTemplateManager  ScenarioManager                |
|  AsyncRecorder  PlaybackController                        |
+-----------------------------------------------------------+
```

### ECS Component Model

Every vehicle entity owns a fixed set of components:

```
+-------------------+   +-------------------+   +-------------------+
|  SimTransform     |   |  SimVelocity      |   |  VehicleState     |
|  Position: V3     |   |  Linear: V3       |   |  Speed: float     |
|  Rotation: Quat   |   |  Angular: V3      |   |  SteerAngle: float|
+-------------------+   +-------------------+   |  Accel: float     |
                                                 +-------------------+
+-------------------+   +-------------------+   +-------------------+
|  VehicleParams    |   |  NavState         |   |  VehicleColor     |
|  Class: enum      |   |  Mode: enum       |   |  R,G,B,A: byte    |
|  Length: float    |   |  TrajectoryId:int |   |  ComponentId 255  |
|  Width: float     |   |  ProgressS: float |   +-------------------+
|  MaxSpeedFwd:float|   |  HasArrived: byte |
|  MaxAccel: float  |   |  RoadPhase: enum  |
|  MaxSteerAngle:f  |   |  FinalDest: V2    |
|  MaxLatAccel: f   |   |  ArrivalRadius: f |
+-------------------+   +-------------------+
```

Formation entities additionally carry:

```
+-------------------------+   +-------------------------+
|  FormationController    |   |  FormationFollower      |
|  (leader marker)        |   |  LeaderEntity: Entity   |
+-------------------------+   |  SlotIndex: int         |
                               +-------------------------+
+-------------------------+
|  FormationTarget        |
|  TargetPosition: V2     |
|  TargetHeading: V2      |
+-------------------------+
```

### Simulation Update Loop

Each frame the application executes the following pipeline:

```
OnUpdate(dt)
    |
    +-- 1. Input: time-mode switching, recording toggle, replay toggle
    |
    +-- 2. Kernel.Update()          (swaps event buffers, advances GlobalTime)
    |
    +-- 3. Logic systems (always):
    |       VehicleCommandSystem    (spawns entities, applies commands)
    |       FormationTargetSystem   (computes slot positions for followers)
    |
    +-- 4. Physics gate (if not paused AND not replaying):
    |       SpatialHashSystem       (rebuilds spatial grid)
    |       CarKinematicsSystem     (integrates speed/steer -> position)
    |       ScenarioManager.Update()(waypoint progression, roamer re-routing)
    |
    +-- 5. AsyncRecorder (optional) -- CaptureKeyframe every 60 frames,
    |                                   CaptureFrame every frame
    |
    +-- 6. MapCanvas.Update(dt)     (camera pan/zoom)
    |
    +-- 7. EventHistoryService.Capture()
```

During **playback** the physics gate is replaced by `PlaybackController.StepForward(repository)`,
which restores serialised ECS state into the live `EntityRepository`.

### Navigation State Machine

`NavState.Mode` drives which subsystem moves the vehicle each tick:

```
                  +----------+
                  |   None   |  (stationary, no movement)
                  +----+-----+
                       |  SetDestination()
         +-------------+-------------+
         |                           |
+--------+--------+         +--------+--------+
| CustomTrajectory|         |   RoadGraph     |
| (spline/linear) |         | (segment-based) |
+-----------------+         +--------+--------+
         |  join formation           |
         |                  +--------+--------+
         +----------------> |   Formation     |
                             | (slot-based)   |
                             +-----------------+
```

---

## Source Structure

```
Fdp.Examples.CarKinem/
|
+-- Program.cs                         Entry point; creates CarKinemApp, calls Run()
+-- CarKinemApp.cs                     Main application class (FdpApplication)
+-- CarKinemInspectorAdapter.cs        Bridges SelectionManager to IInspectorContext
|
+-- Components/
|   +-- VehicleColor.cs                RGBA ECS component (ComponentId 255)
|
+-- Core/
|   +-- SimulationConstants.cs         FIXED_STEP_DT = 1/60 s
|   +-- SelectionManager.cs            Multi-entity selection with primary tracking
|   +-- ScenarioManager.cs             Entity spawning & scenario scripts
|   +-- VehiclePresets.cs              Color helpers and preset lookup wrappers
|
+-- UI/
|   +-- MainUI.cs                      Top-level ImGui layout; owns all sub-panels
|   +-- SpawnControlsPanel.cs          Spawn count, vehicle class, formation controls
|   +-- SimulationControlsPanel.cs     Play/pause/step, time scale, record/replay
|   +-- PerformancePanel.cs            FPS/frame-time display; system profiler window
|
+-- Visualization/
|   +-- VehicleVisualizer.cs           Per-entity rotated rectangle + selection ring
|   +-- RoadMapLayer.cs                IMapLayer that draws road segments and nodes
|   +-- TrajectoryMapLayer.cs          IMapLayer that draws the selected entity's path
|
+-- Headless/
|   +-- HeadlessCarKinemApp.cs         No-Raylib wrapper for automated tests
|
+-- Assets/
    +-- sample_road.json               4-node intersection road network (JSON)
```

### Namespaces

| Namespace | Files |
|---|---|
| `Fdp.Examples.CarKinem` | `Program.cs`, `CarKinemApp.cs`, `CarKinemInspectorAdapter.cs` |
| `Fdp.Examples.CarKinem.Components` | `VehicleColor.cs` |
| `Fdp.Examples.CarKinem.Core` | `SimulationConstants.cs`, `SelectionManager.cs`, `ScenarioManager.cs`, `VehiclePresets.cs` |
| `Fdp.Examples.CarKinem.UI` | `MainUI.cs`, `SpawnControlsPanel.cs`, `SimulationControlsPanel.cs`, `PerformancePanel.cs` |
| `Fdp.Examples.CarKinem.Visualization` | `VehicleVisualizer.cs`, `RoadMapLayer.cs`, `TrajectoryMapLayer.cs` |
| `Fdp.Examples.CarKinem.Headless` | `HeadlessCarKinemApp.cs` |

---

## Public API Reference

### `CarKinemApp` -- `Fdp.Examples.CarKinem`

Inherits `FdpApplication` (Fdp.Presentation.Raylib). Executable entry class.

| Member | Kind | Description |
|---|---|---|
| `CarKinemApp()` | ctor | Configures 1280x720 window, 60 FPS, resizable + MSAA. |
| `OnLoad()` | override | Creates `EntityRepository`, `ModuleHostKernel`, registers components and events, loads road network, creates systems and managers, wires visualisation layers. |
| `OnUpdate(float dt)` | override | Main per-frame update: input, time-mode switch, kernel update, logic systems, conditional physics step, recording, map update. |
| `OnDrawWorld()` | override | Delegates to `MapCanvas.Draw()`. |
| `OnDrawUI()` | override | Delegates to `MainUI.Render(...)`. |
| `OnUnload()` | override | Disposes recorder, playback, road network, trajectory pool, formation templates, kernel, repository. |

### `CarKinemInspectorAdapter` -- `Fdp.Examples.CarKinem`

Implements `IInspectorContext` and `ISelectionState` by delegating to `SelectionManager`.

| Member | Kind | Description |
|---|---|---|
| `CarKinemInspectorAdapter(SelectionManager, EntityRepository)` | ctor | Stores both dependencies. |
| `IsSelected(Entity)` | method | Returns `true` when entity is in the selection set. |
| `SelectedEntities` | property | Read-only view of all currently selected entities. |
| `PrimarySelected` | property (get/set) | Gets or sets the primary selection; `null` clears. |
| `SelectedEntity` | property (get/set) | Alias for `PrimarySelected`. |
| `HoveredEntity` | property (get/set) | Delegates hover tracking to `SelectionManager`. |

---

### `VehicleColor` -- `Fdp.Examples.CarKinem.Components`

Blittable ECS component (`StructLayout.Sequential`, `ComponentId(255)`).
JSON-serialised as a 4-element array `[R, G, B, A]`.

| Member | Kind | Description |
|---|---|---|
| `VehicleColor(byte r, byte g, byte b, byte a = 255)` | ctor | Direct RGBA construction. |
| `R`, `G`, `B`, `A` | fields | Byte colour channels. |
| `Red` | static field | `(255, 0, 0, 255)` |
| `Green` | static field | `(0, 255, 0, 255)` |
| `Blue` | static field | `(50, 100, 255, 255)` -- road users |
| `Orange` | static field | `(255, 165, 0, 255)` -- roamers |
| `Cyan` | static field | `(0, 200, 255, 255)` -- formation members |
| `Magenta` | static field | `(255, 0, 255, 255)` -- formation leaders |
| `GreenYellow` | static field | `(173, 255, 47, 255)` -- default spawn |
| `Gray` | static field | `(200, 200, 200, 255)` |

`VehicleColorArrayConverter` -- custom `JsonConverter<VehicleColor>` that reads and
writes the value as a compact JSON array.

---

### `SimulationConstants` -- `Fdp.Examples.CarKinem.Core`

| Member | Kind | Description |
|---|---|---|
| `FIXED_STEP_DT` | const float | `1.0f / 60.0f` -- canonical physics timestep. |

---

### `SelectionManager` -- `Fdp.Examples.CarKinem.Core`

Thread-safe (single-threaded) manager for multi-entity selection.

| Member | Kind | Description |
|---|---|---|
| `SelectionChanged` | event `Action` | Raised whenever the selection set changes. |
| `SelectedEntities` | property | `IReadOnlyCollection<Entity>` of all selected entities. |
| `PrimarySelected` | property | The most recently selected entity; `null` if empty. |
| `SelectedEntity` | property | Alias for `PrimarySelected`. |
| `HoveredEntity` | property (get/set) | Currently hovered entity (hover highlight). |
| `Count` | property | Number of entities currently selected. |
| `Clear()` | method | Removes all selections and fires `SelectionChanged`. |
| `Set(Entity)` | method | Replaces the entire selection with one entity. |
| `Add(Entity)` | method | Adds entity to the selection, promoting it to primary. |
| `Remove(Entity)` | method | Removes entity; updates primary to another if needed. |
| `SetMultiple(IEnumerable<Entity>)` | method | Replaces selection with a set. Last in enumeration becomes primary. |
| `Contains(Entity)` | method | Returns whether entity is currently selected. |

---

### `ScenarioManager` -- `Fdp.Examples.CarKinem.Core`

Manages entity lifecycle, navigation assignment, and pre-built scenario scripts.

| Member | Kind | Description |
|---|---|---|
| `ScenarioManager(EntityRepository, RoadNetworkBlob, TrajectoryPoolManager, FormationTemplateManager)` | ctor | Stores references to all shared resources. |
| `ClearAll()` | method | Destroys all vehicle entities and clears local tracking state. |
| `Update()` | method | Per-frame: updates waypoint queue progress and re-routes arrived roamers. |
| `SpawnVehicle(Vector2, Vector2, VehicleClass)` | method | Creates a vehicle entity with all required components at the given world position and heading. Returns `Entity`. |
| `AddWaypoint(Entity, Vector2, TrajectoryInterpolation)` | method | Appends a waypoint to the entity's queued path, rebuilds trajectory, writes `NavState`. |
| `SetDestination(Entity, Vector2, TrajectoryInterpolation)` | method | Clears waypoint queue and calls `AddWaypoint` once. |
| `SpawnCollisionTest(VehicleClass)` | method | Spawns 5 pairs of vehicles on head-on collision courses. |
| `SpawnFastOne()` | method | Spawns one fast vehicle between two random road nodes. Requires a valid road network. |
| `SpawnRoadUsers(int, VehicleClass)` | method | Spawns N vehicles routed between random road nodes (`KinematicsMode.RoadGraph`). |
| `SpawnRoamers(int, VehicleClass, TrajectoryInterpolation)` | method | Spawns N vehicles that re-route to a new random destination whenever they arrive. |
| `SpawnFormation(VehicleClass, FormationType, int, TrajectoryInterpolation)` | method | Spawns a leader + (count-1) followers, creates the formation via the event bus, assigns a destination to the leader. |

---

### `ExampleVehiclePresets` -- `Fdp.Examples.CarKinem.Core`

| Member | Kind | Description |
|---|---|---|
| `ColorFormationMember` | static field | Raylib `Color` for formation members (cyan). |
| `ColorFormationLeader` | static field | Raylib `Color` for formation leaders (magenta). |
| `ColorRoadNav` | static field | Raylib `Color` for road-graph navigating vehicles (blue). |
| `ColorTrajectoryNav` | static field | Raylib `Color` for trajectory navigating vehicles (green-yellow). |
| `ColorDefaultNav` | static field | Raylib `Color` fallback (gray). |
| `GetColorForEntity(ISimulationView, Entity, VehicleParams)` | static method | Returns the render `Color` for an entity using a priority chain: `VehicleColor` component > formation role > `NavState` mode > class default. |

---

### `UIState` -- `Fdp.Examples.CarKinem.UI`

Simple data-transfer object shared between `MainUI` and sub-panels.

| Member | Kind | Description |
|---|---|---|
| `SelectedVehicleClass` | property | `VehicleClass` chosen in the spawn panel (default `PersonalCar`). |
| `SelectedFormationType` | property | `FormationType` chosen for formation spawning (default `Column`). |
| `InterpolationMode` | property | `TrajectoryInterpolation` for roamers and formations (default `CatmullRom`). |

---

### `MainUI` -- `Fdp.Examples.CarKinem.UI`

| Member | Kind | Description |
|---|---|---|
| `MainUI(IDiagnosticEventHistoryService)` | ctor | Creates all sub-panels; wraps `EventBrowserPanel` around the service. |
| `UIState` | property | Shared `UIState` instance for spawn/interpolation settings. |
| `IsPaused` | property (get/set) | Forwarded from `SimulationControlsPanel`. |
| `TimeScale` | property (get/set) | Forwarded from `SimulationControlsPanel`. |
| `IsRecording` | property (get/set) | Drive the recording indicator label. |
| `IsReplaying` | property (get/set) | Activates replay timeline UI. |
| `ConsumeRecordingToggle()` | method | Returns and clears the one-shot recording toggle flag. |
| `ConsumeReplayToggle()` | method | Returns and clears the one-shot replay toggle flag. |
| `ConsumeStepRequest()` | method | Returns and clears the one-shot step-frame flag. |
| `Render(EntityRepository, ModuleHostKernel, ScenarioManager, IInspectorContext, IEnumerable<IEcsModuleSystem>, PlaybackController?)` | method | Draws the "Simulation Control" ImGui window and all child panels. |

---

### `SpawnControlsPanel` -- `Fdp.Examples.CarKinem.UI`

| Member | Kind | Description |
|---|---|---|
| `Render(ScenarioManager, UIState)` | method | Draws spawn count slider, vehicle class combo, formation type radios, and action buttons (Spawn Vehicles, Spawn Road Users, Spawn Roamers, Spawn Collision Test, Spawn Formation). |

---

### `SimulationControlsPanel` -- `Fdp.Examples.CarKinem.UI`

| Member | Kind | Description |
|---|---|---|
| `IsPaused` | property | True while simulation is paused. |
| `TimeScale` | property | Multiplier applied to the time controller (0.1 -- 5.0). |
| `StepRequested` | property | Set to true by the "Step" button; consumed by `CarKinemApp`. |
| `IsRecording`, `IsReplaying` | properties | Status flags used to drive label colours. |
| `RecordingToggleInput`, `ReplayToggleInput` | properties | One-shot toggle flags. |
| `Render(EntityRepository, ModuleHostKernel, PlaybackController?)` | method | Draws Play/Pause, Step, Speed slider, Record/Replay buttons, replay timeline slider, and current simulation time. |

---

### `PerformancePanel` -- `Fdp.Examples.CarKinem.UI`

| Member | Kind | Description |
|---|---|---|
| `Render()` | method | Shows FPS and frame time in milliseconds. |

### `SystemPerformanceWindow` -- `Fdp.Examples.CarKinem.UI`

| Member | Kind | Description |
|---|---|---|
| `IsOpen` | field | Controls window visibility; toggled by a checkbox in the main panel. |
| `Render(IEnumerable<IEcsModuleSystem>)` | method | Shows a two-column table of registered system names. |

---

### `VehicleVisualizer` -- `Fdp.Examples.CarKinem.Visualization`

| Member | Kind | Description |
|---|---|---|
| `GetPosition(ISimulationView, Entity)` | method | Returns 2D world position from `SimTransform`, or `null` if missing. |
| `GetHitRadius(ISimulationView, Entity)` | method | Returns entity pick radius (half the vehicle length). |
| `Render(ISimulationView, Entity, Vector2, RenderContext, bool, bool)` | method | Draws a rotation-correct rectangle with direction indicator; green selection ring with trajectory overlay when selected; magenta leader ring for `FormationController` entities. |
| `GetHoverLabel(ISimulationView, Entity)` | method | Returns `"<Class> #<Index>"` for tooltip display. |

---

### `RoadMapLayer` -- `Fdp.Examples.CarKinem.Visualization`

Implements `IMapLayer`.

| Member | Kind | Description |
|---|---|---|
| `Name` | property | `"Road Network"` |
| `LayerBitIndex` | property | `0` |
| `Draw(RenderContext)` | method | Draws each road segment as a gray filled strip (lane width * count) with a yellow centre line; draws each node as a blue circle. |

---

### `TrajectoryMapLayer` -- `Fdp.Examples.CarKinem.Visualization`

Implements `IMapLayer`. Renders the active path of the selected entity only.

| Member | Kind | Description |
|---|---|---|
| `Name` | property | `"Trajectories"` |
| `LayerBitIndex` | property | `-1` (always-on overlay) |
| `Draw(RenderContext)` | method | If the selected entity has a `CustomTrajectory` nav state, renders the remaining path using either straight line segments (linear) or orange Hermite-smoothed segments (Catmull-Rom). |

---

### `HeadlessCarKinemApp` -- `Fdp.Examples.CarKinem.Headless`

`IDisposable` wrapper that replicates `CarKinemApp`'s simulation logic without
any Raylib or ImGui dependency.

| Member | Kind | Description |
|---|---|---|
| `Repository` | property | The live `EntityRepository`. |
| `Kernel` | property | The `ModuleHostKernel`. |
| `TimeController` | property | Active `ITimeController`. |
| `SteppingTime` | property | The `SteppingTimeController` instance. |
| `ContinuousTime` | property | The continuous `ITimeController` instance. |
| `Recorder` | property | Active `AsyncRecorder`, or `null`. |
| `Playback` | property | Active `PlaybackController`, or `null`. |
| `Initialize(bool useSteppingTime)` | method | Creates repository, kernel, registers all components and events, sets up a minimal 2-node dummy road, creates systems and managers. |
| `Update()` | method | Runs one full simulation tick: Kernel update -> systems pipeline -> recording. |
| `SpawnFastOne()` | method | Delegates to `ScenarioManager.SpawnFastOne()`. |
| `StartRecording(string)` | method | Creates `AsyncRecorder` writing to the given path. |
| `StopRecording()` | method | Disposes `AsyncRecorder` and sets to `null`. |
| `StartPlayback(string)` | method | Stops any active recording, creates `PlaybackController`. |
| `StopPlayback()` | method | Disposes `PlaybackController`. |
| `Dispose()` | method | Stops recording and playback, disposes kernel, repository, and road network. |

---

## Dependencies

### NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| `Raylib-cs` | 7.0.2 | 2D/3D rendering, window management, input (C# bindings for Raylib 5). |
| `rlImgui-cs` | 3.2.0 | Raylib + Dear ImGui integration for debug UI panels. |

### Project References

| Project | Provides |
|---|---|
| `Fdp.Toolkits` (`Fdp.Toolkits.csproj`) | `Fdp.Toolkit.Vis2D` (MapCanvas, IMapLayer, RenderContext), `Fdp.Toolkit.Time.Controllers` (SteppingTimeController, TimeControllerFactory), `CarKinem.*` simulation modules (CarKinematicsSystem, RoadNetworkBlob, TrajectoryPoolManager, FormationTemplateManager, etc.) |
| `Fdp.ModuleHost` (`Fdp.ModuleHost.csproj`) | `ModuleHostKernel`, `EventAccumulator`, `ITimeController`, `GlobalTime`, `ISimulationView`, `IEcsModuleSystem` |
| `Fdp.Presentation` (`Fdp.Presentation.csproj`) | `FdpApplication`, `ApplicationConfig`, framework ImGui panels (`EntityInspectorPanel`, `EventBrowserPanel`, `SystemPerformanceWindow`), `IInspectorContext`, `RepositoryAdapter`, `DiagnosticEventHistoryService` |

### Transitive Dependencies (via above projects)

| Component | Origin | Usage |
|---|---|---|
| `Fdp.Core` | `EntityRepository`, `Entity`, `SimTransform`, `SimVelocity`, `VehicleState`, `VehicleParams`, `NavState`, `GlobalTime` component types, component registration API |
| `CarKinem.Core` | `VehicleClass`, `VehiclePresets`, `KinematicsMode` |
| `CarKinem.Road` | `RoadNetworkBlob`, `RoadNode`, `RoadSegment`, `RoadNetworkLoader`, `RoadGraphPhase` |
| `CarKinem.Trajectory` | `TrajectoryPoolManager`, `CustomTrajectory`, `TrajectoryInterpolation` |
| `CarKinem.Formation` | `FormationFollower`, `FormationController`, `FormationTarget`, `FormationType`, `FormationTemplateManager`, `FormationParams` |
| `CarKinem.Commands` | `CmdSpawnVehicle`, `CmdCreateFormation`, `CmdJoinFormation`, `CmdLeaveFormation` |
| `CarKinem.Systems` | `SpatialHashSystem`, `FormationTargetSystem`, `VehicleCommandSystem`, `CarKinematicsSystem` |
| `CarKinem.Spatial` | Spatial hash data structures |
| `Fdp.Core.FlightRecorder` | `AsyncRecorder`, `PlaybackController` |
| `Fdp.Core.Collections` | `NativeArray<T>`, `Allocator` |

---

## Usage Examples

### Example 1: Creating and Running the Simulation

The minimal path to launch the interactive simulation is the entry point in
`Program.cs`:

```csharp
namespace Fdp.Examples.CarKinem
{
    class Program
    {
        static void Main(string[] args)
        {
            // CarKinemApp inherits FdpApplication which owns the Raylib window loop.
            using var app = new CarKinemApp();
            app.Run(); // Blocks until the window is closed.
        }
    }
}
```

`CarKinemApp.Run()` calls `OnLoad` once, then repeatedly calls `OnUpdate`,
`OnDrawWorld`, and `OnDrawUI` at the configured 60 FPS, then calls `OnUnload`
before returning.

---

### Example 2: Spawning Vehicles and Assigning Navigation

`ScenarioManager` exposes a fluent-style API for creating vehicles and assigning
different navigation modes. The following snippet shows how the three main modes
are seeded at startup:

```csharp
// Road-graph navigation (follows road segments to a destination)
Entity roadUser = scenarioManager.SpawnVehicle(
    startNode.Position,
    heading: new Vector2(1, 0),
    VehicleClass.PersonalCar);

// Write NavState directly for road-graph mode
var nav = repository.GetComponent<NavState>(roadUser);
nav.Mode             = KinematicsMode.RoadGraph;
nav.RoadPhase        = RoadGraphPhase.Approaching;
nav.FinalDestination = endNode.Position;
nav.ArrivalRadius    = 5.0f;
nav.CurrentSegmentId = -1;
nav.ProgressS        = 0f;
nav.HasArrived       = 0;
repository.SetComponent(roadUser, nav);

// Custom trajectory (linear or Catmull-Rom spline)
Entity roamer = scenarioManager.SpawnVehicle(
    new Vector2(100, 200),
    heading: new Vector2(0, 1),
    VehicleClass.Truck);

scenarioManager.SetDestination(
    roamer,
    destination: new Vector2(400, 400),
    TrajectoryInterpolation.CatmullRom);

// Colour override via ECS component
repository.SetComponent(roamer, VehicleColor.Orange);
```

---

### Example 3: Formation Spawning

Formations require a leader entity and one or more followers connected via the
event bus. `ScenarioManager.SpawnFormation` orchestrates this:

```csharp
// Spawn a 5-vehicle column formation of trucks
scenarioManager.SpawnFormation(
    vClass:        VehicleClass.Truck,
    type:          FormationType.Column,
    count:         5,
    interpolation: TrajectoryInterpolation.CatmullRom);

// Internally, SpawnFormation does:
//   1. SpawnVehicle -> leader entity, color = Magenta
//   2. Bus.Publish(new CmdCreateFormation { LeaderEntity, Type, Params })
//   3. For each follower slot:
//        SpawnVehicle at slot position, color = Cyan
//        Bus.Publish(new CmdJoinFormation { Entity, LeaderEntity, SlotIndex })
//   4. ScenarioManager.SetDestination(leader, destination)
//
// VehicleCommandSystem processes CmdCreateFormation and CmdJoinFormation
// on the next kernel tick, adding FormationController / FormationFollower
// components. FormationTargetSystem then computes FormationTarget positions
// each frame so followers track their leader.
```

---

### Example 4: Headless Mode (for Automated Tests)

`HeadlessCarKinemApp` wraps the full simulation without any rendering, making
it suitable for integration tests and benchmarks:

```csharp
using var sim = new HeadlessCarKinemApp();
sim.Initialize(useSteppingTime: true);  // deterministic stepping

// Spawn a vehicle on the dummy 2-node road
sim.SpawnFastOne();

// Optional: record the run to disk
sim.StartRecording("test_run.fdprec");

// Advance exactly N physics ticks
const int TicksToRun = 300; // 5 seconds at 60 Hz
for (int i = 0; i < TicksToRun; i++)
{
    sim.SteppingTime.Step(SimulationConstants.FIXED_STEP_DT);
    sim.Update();
}

sim.StopRecording();

// Inspect ECS state after simulation
var query = sim.Repository.Query()
    .With<VehicleState>()
    .With<SimTransform>()
    .Build();

foreach (var entity in query)
{
    ref readonly var state = ref sim.Repository.GetComponentRO<VehicleState>(entity);
    ref readonly var tf    = ref sim.Repository.GetComponentRO<SimTransform>(entity);
    Console.WriteLine(
        $"Entity {entity.Index}: speed={state.Speed:F2} m/s  " +
        $"pos=({tf.Position.X:F1}, {tf.Position.Y:F1})");
}
```

---

### Example 5: Flight Recorder Round-Trip

The recorder persists the full ECS state to a binary file; the playback
controller rehydrates it frame by frame:

```csharp
// --- Recording ---
using var sim = new HeadlessCarKinemApp();
sim.Initialize();
sim.SpawnFastOne();
sim.StartRecording("scenario.fdprec");
for (int i = 0; i < 600; i++) sim.Update();  // record 10 seconds
sim.StopRecording();
sim.Dispose();

// --- Playback ---
using var replay = new HeadlessCarKinemApp();
replay.Initialize();
replay.StartPlayback("scenario.fdprec");

bool moreFrames = true;
while (moreFrames)
{
    replay.Update();  // internally calls PlaybackController.StepForward
    if (replay.Playback == null) break;  // StopPlayback called internally on EOF
    moreFrames = replay.Playback.CurrentFrame < replay.Playback.TotalFrames - 1;
}
```

---

## Best Practices

### Physics in ECS

1. **Keep physics components blittable.**
   `SimTransform`, `SimVelocity`, `VehicleState`, and `VehicleParams` are all
   unmanaged structs with `StructLayout.Sequential`. This allows the entity
   repository to store them in dense arrays, enabling cache-friendly iteration
   inside `CarKinematicsSystem`.

2. **Separate logic from physics.**
   `VehicleCommandSystem` and `FormationTargetSystem` run unconditionally every
   frame (they only set intentions). `CarKinematicsSystem` and
   `SpatialHashSystem` are gated behind the physics step flag. This decoupling
   lets you pause, step, or replay physics without losing queued commands.

3. **Use a fixed timestep constant.**
   `SimulationConstants.FIXED_STEP_DT = 1/60 s` is the single source of truth.
   Both the interactive app and the headless test harness refer to it, making
   recorded sessions deterministically replayable.

4. **Time-mode switching via a proxy.**
   The app maintains both a `ContinuousTimeController` and a
   `SteppingTimeController`. Switching between them requires seeding the new
   controller with the current time state
   (`SeedState(currentController.GetCurrentState())`). This ensures no
   discontinuity in `GlobalTime.TotalTime` or `FrameNumber` when the user
   pauses.

5. **Component-based colour overrides.**
   Rather than hard-coding visual roles in the renderer, `VehicleColor` is an
   ECS component. The visualiser reads it with the highest priority. Scenario
   scripts simply call `repository.SetComponent(entity, VehicleColor.Orange)`.
   This avoids coupling between the kinematics domain and the presentation
   layer.

6. **Trajectory ownership via a pool.**
   Trajectories are not stored on entities (which would require managed or
   pointer-sized members). Instead, `TrajectoryPoolManager` is a shared resource
   and `NavState.TrajectoryId` is a plain `int` handle. When a vehicle is
   destroyed or re-routed, the old trajectory id is explicitly freed from the
   pool. This prevents memory leaks and keeps `NavState` blittable.

7. **Roaming entities re-route on arrival.**
   `ScenarioManager.UpdateRoamers` checks `NavState.HasArrived` each frame and
   issues a new `SetDestination` call when the vehicle arrives. This avoids
   idle entities accumulating in the world and demonstrates how a simple
   finite-state pattern (arrive -> pick-new-goal) can be layered on top of the
   navigation abstraction.

8. **Headless parity.**
   `HeadlessCarKinemApp` registers the exact same components and systems as
   `CarKinemApp`. Any regression test that passes headlessly is guaranteed to
   reproduce correctly in the interactive window, because both share the same
   ECS kernel.

9. **Selection state via interfaces.**
   `CarKinemInspectorAdapter` implements both `IInspectorContext` and
   `ISelectionState` and is passed to visualization layers and ImGui panels.
   The concrete `SelectionManager` is never exposed outside the application
   class. UI panels and map layers depend only on the interface, keeping them
   reusable across other example projects.

10. **Flight recorder keyframe cadence.**
    A full keyframe (all component values for all entities) is captured every
    60 frames; incremental frames are captured every frame. This balances file
    size against seek granularity. In headless mode the recorder is called with
    `blocking: true` to ensure no frames are dropped on fast-forward runs.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Examples.CarKinem.Tests` | Test project that uses `HeadlessCarKinemApp` to run automated kinematics regression and recording/playback round-trip tests. |
| `Fdp.Examples.Common` | Shared example contracts (`IScenario`, `ScenarioSubsystem`). Not depended on directly by `CarKinem`, but sibling projects use it. |
| `Fdp.Examples.Scenarios` | Scenario scripts that use the same FDP ECS patterns as `CarKinem` but target the `ScenarioSubsystem` contract. |
| `Fdp.Examples.Runner` | Headless runner that can execute `IScenario` scripts from the command line; analogous to `HeadlessCarKinemApp` but generic. |
| `Fdp.Examples.NetworkDemo` | Demonstrates multi-instance networked simulation using the same `Fdp.Core` ECS kernel and `Fdp.ModuleHost` time control. |
| `Fdp.Toolkits` (`CarKinem.*` modules) | The actual kinematics engine, road graph, trajectory pool, and formation system that `CarKinem` wraps in an interactive example. |
| `Fdp.Presentation` | Provides `FdpApplication`, `EntityInspectorPanel`, `EventBrowserPanel`, and `RepositoryAdapter` used by the interactive app. |
| `Fdp.ModuleHost` | Kernel, time controllers, and `IEcsModuleSystem` base -- the backbone of the simulation loop. |
| `Fdp.Core` | Entity-component repository, event bus, `GlobalTime` singleton, blittable component infrastructure. |
