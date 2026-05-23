# FDP Core Framework Architecture

**Date:** 2026-05-23
**Scope:** Cross-project architectural relationship document covering Fdp.Core, Fdp.ModuleHost,
Fdp.Presentation, and Fdp.Diagnostics.Contracts as a unified simulation framework.

---

## Table of Contents

1. [The FDP Framework Overview](#1-the-fdp-framework-overview)
2. [The Three-Layer Architecture](#2-the-three-layer-architecture)
3. [The ECS Model (Fdp.Core)](#3-the-ecs-model-fdpcore)
4. [Module System (Fdp.ModuleHost)](#4-module-system-fdpmodulehost)
5. [Presentation Integration (Fdp.Presentation)](#5-presentation-integration-fdppresentation)
6. [Diagnostics Contracts (Fdp.Diagnostics.Contracts)](#6-diagnostics-contracts-fdpdiagnosticscontracts)
7. [Data Flow Diagrams](#7-data-flow-diagrams)
8. [Key Integration Patterns](#8-key-integration-patterns)
9. [Complete Application Example](#9-complete-application-example)
10. [Anti-patterns and Common Mistakes](#10-anti-patterns-and-common-mistakes)
11. [Links to Individual Project Docs](#11-links-to-individual-project-docs)

---

## 1. The FDP Framework Overview

### Problem Statement

Modern distributed simulation (military, training, game AI) requires:

- **High-frequency deterministic tick loops** running at 60 Hz or faster.
- **Heterogeneous computation loads** -- some modules are latency-critical (physics, input
  processing) while others are compute-heavy but latency-tolerant (AI planning, analytics).
- **Hot-pluggable modules** that can be installed and removed at runtime without stopping
  the simulation.
- **Recording and playback** for after-action review and deterministic replay.
- **Rich visualization** over the live simulation state without perturbing the tick loop.
- **Distributed operation** where multiple simulation nodes share world state over a network.

FDP (Flight Data Platform / Force Data Platform) solves this by composing three orthogonal
concerns -- data, orchestration, and rendering -- into a coherent layered architecture.

### Core Design Philosophy

FDP is built on three interlocking ideas:

1. **Entity-Component-System (ECS) as the data kernel.** All simulation state lives in an
   `EntityRepository`. Components are unmanaged structs laid out in contiguous `NativeChunkTable`
   blocks. Queries are O(1) bitmask filters. There is no object graph -- only flat tables.

2. **Module + System as the logic kernel.** Business logic is decomposed into `IEcsModule`
   units. Each module declares its execution policy (thread affinity, target frequency, data
   strategy). Within a module, logic is further decomposed into `IEcsModuleSystem` instances
   that run in a topologically-sorted dependency graph. The `ModuleHostKernel` orchestrates
   all of this.

3. **Presentation as an optional non-intrusive layer.** `FdpApplication` (Raylib-backed) wraps
   the kernel in a windowed game loop. The 2D map canvas (`MapCanvas`) and debug gizmo pipeline
   (`IDebugDrawBuilder`) are layered on top of the live world without modifying it.

### How It Fits Together

```
+-----------------------------------------------------------------------+
|  User Application  (e.g. Program.cs, MySimApp : FdpApplication)      |
+-----------------------------------------------------------------------+
         |                        |                        |
         v                        v                        v
+------------------+  +-----------------------+  +-------------------+
| Fdp.Presentation |  |  Fdp.ModuleHost       |  | Fdp.Diagnostics   |
| (Raylib + ImGui) |  |  (Module Orchestrator)|  | .Contracts        |
| FdpApplication   |  |  ModuleHostKernel     |  | IDebugDrawBuilder |
| MapCanvas        |  |  SystemScheduler      |  | DebugPrimitives   |
+------------------+  +-----------------------+  +-------------------+
         |                        |
         |                        v
         |             +-----------------------+
         +------------>|  Fdp.Core (ECS Kernel)|
                       |  EntityRepository     |
                       |  EntityQuery          |
                       |  FdpEventBus          |
                       |  EntityCommandBuffer  |
                       |  FlightRecorder       |
                       +-----------------------+
```

---

## 2. The Three-Layer Architecture

```
+================================+
|    Fdp.Presentation            |  Layer 3: Visualization / UI
|                                |
|  FdpApplication (app loop)     |
|  MapCanvas (2D world view)     |
|  ImGui windows / panels        |
|  DebugGizmoLayer               |
+================================+
         |  calls Kernel.Update() each frame
         |  reads World for rendering queries
         v
+================================+
|    Fdp.ModuleHost              |  Layer 2: Module / System Lifecycle
|                                |
|  ModuleHostKernel              |
|  KernelExecutionTopology (RCU) |
|  SystemScheduler (topo sort)   |
|  ISnapshotProvider (GDB/SoD)   |
|  CircuitBreaker / Resilience   |
+================================+
         |  executes systems that mutate World
         |  syncs snapshot replicas from World
         v
+================================+
|    Fdp.Core (ECS Kernel)       |  Layer 1: Data / Query Engine
|                                |
|  EntityRepository              |
|  NativeChunkTable / ManagedCT  |
|  EntityQuery + QueryBuilder    |
|  FdpEventBus (double-buffer)   |
|  EntityCommandBuffer (deferred)|
|  Phase / PhasePermission       |
|  FlightRecorder                |
+================================+
```

### Layer 1 -- Fdp.Core (ECS Kernel)

`Fdp.Core` is the pure data and query engine. It has no threading model of its own; it is
the shared state that all other layers read and write.

Contracts exposed upward:
- `EntityRepository` -- the world. All entities, components, singleton data, and the event bus.
- `EntityQuery` / `QueryBuilder` -- zero-allocation entity iteration with bitmask filtering.
- `FdpEventBus` -- double-buffered one-frame event streams (unmanaged and managed).
- `EntityCommandBuffer` -- records structural mutations for deferred playback.
- `Phase` / `PhasePermission` -- coarse write-access gating per phase.
- `FlightRecorder` -- delta-compressed binary recording and playback.

### Layer 2 -- Fdp.ModuleHost (Module Lifecycle)

`Fdp.ModuleHost` owns the simulation loop. It takes ownership of a `EntityRepository` at
construction and orchestrates all the modules that write to it.

Contracts exposed upward:
- `ModuleHostKernel` -- register modules, call `Update()` once per frame.
- `IEcsModule` -- implement to author a module.
- `IEcsModuleSystem` -- implement to author a system inside a module.
- `ISystemRegistry` -- register systems from within `RegisterSystems()`.
- `SystemPhase` -- declare which phase a system runs in.
- `ISnapshotProvider` -- strategy for reading the world from background threads.
- `ExecutionPolicy` -- factory methods for common module execution profiles.

### Layer 3 -- Fdp.Presentation (Visualization / UI)

`Fdp.Presentation` wraps the first two layers in a Raylib window + ImGui session. It adds:

- `FdpApplication` -- abstract base class. Provides the `OnLoad / OnUpdate / OnDrawWorld /
  OnDrawUI` lifecycle hooks. The subclass creates its `EntityRepository` and
  `ModuleHostKernel` inside `OnLoad()`.
- `ApplicationConfig` -- struct for window title, resolution, FPS cap, persistence.
- `MapCanvas` -- manages a camera, ordered layers (`IMapLayer`), and an optional
  `IDebugDrawBuilder`. Drives per-layer `Update()` and `Draw()`.
- `DebugGizmoLayer` -- a `MapCanvas` layer that renders debug primitives emitted via
  `IDebugDrawBuilder`.

---

## 3. The ECS Model (Fdp.Core)

### Entity

An entity is a lightweight 48-bit handle:

```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly struct Entity : IEquatable<Entity>
{
    public readonly int Index;       // 32-bit slot index
    public readonly ushort Generation; // 16-bit generation counter

    // Generation is incremented when the slot is recycled.
    // Any system holding a stale handle will detect the mismatch.
    public bool IsNull => Index < 0 || Generation == 0;
}
```

The `EntityRepository` manages a flat `EntityIndex` array. Every slot has a header that
records the live bitmask (which component types this entity owns) and the current generation.

### Component

Components are plain unmanaged structs registered at startup. Each registration assigns a
unique integer ID in [0, 255]. The ID is used in the 256-bit bitmask that forms the entity's
component signature.

```csharp
// Registration (once, at app startup):
world.RegisterComponent<SimTransform>();  // gets e.g. ID 0
world.RegisterComponent<SimVelocity>();   // gets e.g. ID 1

// Access (hot path, no dictionary lookup):
ref var transform = ref world.Get<SimTransform>(entity);
```

Unmanaged components are stored in `NativeChunkTable<T>` -- a flat array of 64KB chunks.
Managed components (reference types) are stored in `ManagedComponentTable<T>` with the same
index-based access pattern.

Singleton components bypass the entity system: they are stored by type ID in a side-table
and retrieved with `world.GetSingleton<T>()`.

### Query System

Queries are built once (or per-system initialization) and iterated every frame:

```csharp
var query = world.Query()
    .With<SimTransform>()
    .With<SimVelocity>()
    .Without<DeadTag>()
    .Build();

// Zero-allocation foreach via ref struct enumerator:
foreach (var entity in query)
{
    ref var pos = ref world.Get<SimTransform>(entity);
    ref var vel = ref world.Get<SimVelocity>(entity);
    pos.Position += vel.Velocity * deltaTime;
}
```

Filtering is O(1) per entity: the entity's component bitmask is AND-tested against the
query's include and exclude bitmasks using SIMD (`BitMask256`). There are no archetype
migrations or archetype tables; the bit-per-component approach is simpler and sufficient for
the entity counts typical in military simulation (thousands, not millions).

Authority filtering is available for distributed scenarios:

```csharp
var ownedQuery = world.Query()
    .WithOwned<SimTransform>()  // only entities where local node has authority
    .Build();
```

### Event / Command System

**Events** are one-frame signals, double-buffered in `FdpEventBus`. Writers publish in frame N;
readers consume in frame N+1 after `SwapBuffers()`:

```csharp
// Publish (any thread):
world.Bus.Publish(new UnitSpawnedEvent { EntityId = entity });

// Consume (frame N+1, main thread):
foreach (var evt in world.Bus.ReadEvents<UnitSpawnedEvent>())
    ProcessSpawn(evt);
```

**Commands** are deferred structural mutations recorded into `EntityCommandBuffer` and played
back at a safe sync point. This allows background systems to express structural intent without
touching the live world:

```csharp
var cmd = world.GetCommandBuffer();  // per-thread ECB
var newEntity = cmd.CreateEntity();
cmd.AddComponent(newEntity, new SimTransform { Position = spawnPoint });
// ... later, main thread calls:
cmd.Playback(world);
```

### Phase / Permission System

`Fdp.Core` exposes a phase gate that enforcement layers can use to catch accidental writes
at wrong times:

```csharp
world.EnterPhase(Phase.Simulation);  // grants ReadWriteAll
world.EnterPhase(Phase.NetworkReceive, PhasePermission.OwnedOnly); // restrict writes
```

Predefined phases: `Initialization`, `NetworkReceive`, `Simulation`, `NetworkSend`,
`Presentation`.

### Flight Recorder

`RecorderSystem` snapshots the live world at 60 Hz by copying changed `NativeChunkTable`
memory pages (delta compression). `PlaybackSystem` applies recorded frames back to an empty
world to replay sessions frame-accurately. The recorder understands both component tables
and event bus streams.

```
+---------------------+       delta binary         +---------------------+
|  Live World         |----------------------------->|  .fdprec file       |
|  EntityRepository   |  RecorderSystem.Record()   |  frame headers +    |
+---------------------+                             |  component deltas   |
                                                     +---------------------+
                                                              |
                                                     PlaybackSystem.Replay()
                                                              |
                                                              v
                                                     +---------------------+
                                                     |  Replay World       |
                                                     |  EntityRepository   |
                                                     +---------------------+
```

---

## 4. Module System (Fdp.ModuleHost)

### IEcsModule Contract

An `IEcsModule` is the unit of composition. Every feature of a simulation is packaged as one:

```csharp
public interface IEcsModule
{
    string Name { get; }
    ExecutionPolicy Policy { get; }

    // Pattern 1: register systems -- kernel calls them automatically.
    void RegisterSystems(ISystemRegistry registry);

    // Pattern 2: direct tick -- module code runs in kernel.Update().
    void Tick(ISimulationView view, float deltaTime);

    // Declares which component types this module touches (for partial sync).
    IEnumerable<Type>? GetRequiredComponents() => null;
}
```

Modules choose one of four execution profiles via `ExecutionPolicy`:

| Factory Method                  | Thread       | Data Strategy | Use Case                     |
|---------------------------------|--------------|---------------|------------------------------|
| `ExecutionPolicy.Synchronous()` | Main thread  | Direct live   | Physics, input, critical path|
| `ExecutionPolicy.FastReplica()` | Background   | GDB replica   | Network send, recording      |
| `ExecutionPolicy.SlowBackground(hz)` | Background | SoD snapshot | AI, analytics, pathfinding  |
| `ExecutionPolicy.FrameSynced()` | Background   | GDB replica   | Low-latency background work  |

### IEcsModuleSystem Contract

Systems are the smallest unit of execution. They are stateless value-types or simple classes:

```csharp
public interface IEcsModuleSystem
{
    void Execute(ISimulationView view, float deltaTime);
}
```

Systems declare their phase and dependencies via attributes:

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
[UpdateAfter(typeof(PhysicsSystem))]
[UpdateBefore(typeof(RenderPrepSystem))]
public class MovementSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var query = view.Query().With<SimTransform>().With<SimVelocity>().Build();
        foreach (var e in query)
        {
            ref var t = ref view.Get<SimTransform>(e);
            ref var v = ref view.Get<SimVelocity>(e);
            t.Position += v.Velocity * deltaTime;
        }
    }
}
```

### System Phases

The `SystemScheduler` recognizes the following `SystemPhase` enum values:

| Phase           | Thread       | Typical Work                              |
|-----------------|--------------|-------------------------------------------|
| `Input`         | Main thread  | Keyboard/mouse capture, command injection |
| `BeforeSync`    | Main thread  | Pre-sync prep; RCU topology swap happens  |
| `Simulation`    | Background   | Module business logic                     |
| `PostSimulation`| Main thread  | Transform interpolation, coordinate sync  |
| `Export`        | Main thread  | Network send, flight recorder write       |
| `Manual`        | Caller       | Diagnostics; module must tick manually    |

Global systems (registered with `kernel.RegisterGlobalSystem()`) only participate in the
main-thread phases: `Input`, `BeforeSync`, `PostSimulation`, and `Export`.

### Snapshot Strategies

Background modules never touch the live world directly. Instead they receive a read-only
`ISimulationView` from an `ISnapshotProvider`:

| Strategy | Type          | Description                                              |
|----------|---------------|----------------------------------------------------------|
| `GDB`    | Double buffer | Persistent replica. Synced once per sync point. Zero-copy read. |
| `SoD`    | Pool          | Snapshot-on-Demand. Acquired from a pool on each module tick, synced then. |
| `Shared` | Ref-counted   | Multiple modules share one snapshot (convoy pattern).    |

The kernel assigns a strategy based on the module's `DataStrategy` field in `ExecutionPolicy`.

### ModuleHostKernel Lifecycle

```
Construction:
  new ModuleHostKernel(world, eventAccumulator)

Registration (before Initialize):
  kernel.RegisterModule(myModule)
  kernel.RegisterGlobalSystem(myGlobalSystem)
  kernel.SetTimeController(controller)

Initialization (once):
  kernel.Initialize()
    --> builds SnapshotPool
    --> validates all module ExecutionPolicy instances
    --> assigns ISnapshotProvider to each module
    --> builds initial KernelExecutionTopology (compiles SystemScheduler)

Hot Loop (every frame, driven by FdpApplication or custom loop):
  kernel.Update()
    --> TimeController.Update()  -- advances GlobalTime
    --> UpdateInternal(dt, globalTime)

Shutdown:
  kernel.Dispose()
```

### Hot-Plug (RCU Pattern)

Modules can be added or removed at runtime without stopping the simulation:

```csharp
// Install a new module while the simulation is running:
await kernel.InstallModuleAsync(newModule);

// Uninstall and wait for cleanup:
await kernel.UninstallModuleAsync(existingModule);
```

Internally this uses Read-Copy-Update (RCU):

1. A background task compiles a new `KernelExecutionTopology` by cloning the current one
   and applying the delta.
2. The new topology is stored in `_pendingOperation` via `Volatile.Write`.
3. At the `BeforeSync` boundary of the next frame the main thread detects the pending
   operation and performs an O(1) `Volatile.Write` swap of `_activeTopology`.
4. Old modules that were removed enter a "draining" state. They are harvested when their
   in-flight task completes and then disposed on a background thread.

This guarantees zero allocations and zero stalls on the 60 Hz hot path.

---

## 5. Presentation Integration (Fdp.Presentation)

### FdpApplication Loop

`FdpApplication` is the canonical entry point for visual FDP applications. It owns the
Raylib window and the ImGui session, and calls the kernel once per frame:

```
Run()
  InitializeWindow()          -- Raylib.InitWindow, rlImGui.Setup
  OnLoad()                    -- ABSTRACT: user creates World, Kernel, registers modules
  while !quit:
    dt = Raylib.GetFrameTime()
    OnUpdate(dt)              -- ABSTRACT: kernel.Update(), canvas.Update(dt)
    Raylib.BeginDrawing()
      Raylib.ClearBackground()
      OnDrawWorld()           -- ABSTRACT: canvas.Draw(world)
      rlImGui.Begin()
        OnDrawUI()            -- ABSTRACT: ImGui windows, panels
      rlImGui.End()
    Raylib.EndDrawing()
  OnUnload()                  -- ABSTRACT: kernel.Dispose(), world.Dispose()
  ShutdownWindow()            -- rlImGui.Shutdown, Raylib.CloseWindow
```

The four abstract hooks give the application full control with minimal boilerplate.

### ApplicationConfig

```csharp
public struct ApplicationConfig
{
    public string WindowTitle  { get; set; }  // default "FDP Application"
    public int    Width        { get; set; }  // default 1280
    public int    Height       { get; set; }  // default 720
    public int    TargetFPS    { get; set; }  // default 60
    public ConfigFlags Flags   { get; set; }  // Resizable + MSAA 4x
    public bool PersistenceEnabled { get; set; } // saves imgui.ini to AppData
}
```

### MapCanvas and the Layer System

`MapCanvas` is a 2D map viewport driven by a `MapCamera` (pan / zoom). It manages an ordered
list of `IMapLayer` objects. Each frame:

1. `canvas.Update(dt)` -- processes input (pan, zoom, tool interactions), notifies layers.
2. `canvas.Draw(world)` -- iterates layers bottom-to-top and calls `layer.Draw(context)`.

Layers included in `Fdp.Presentation`:

| Layer              | Purpose                                         |
|--------------------|-------------------------------------------------|
| `GridMapLayer`     | Background grid with coordinate labels          |
| `DebugGizmoLayer`  | Renders `DebugPrimitiveBuffer` over the canvas  |

Custom layers implement `IMapLayer` and are added via `canvas.AddLayer(layer)`.

`MapCanvas` also provides entity picking: `canvas.PickTopmostEntity(worldPos)` iterates
layers in reverse draw order and returns the first entity under the cursor.

### ImGui Integration

`FdpApplication` sets up ImGui docking by default. The application subclass draws panels
inside `OnDrawUI()`. Typical panels:
- Module diagnostics (execution counts, circuit breaker states)
- Entity inspector
- System profiler
- Flight recorder controls

`Fdp.Presentation` provides adapters in `ImGui/Adapters/` that bridge FDP data structures
to ImGui rendering.

### Debug Visualization Pipeline

The diagnostic draw pipeline flows from ECS systems through the canvas to the screen:

```
ECS System (e.g. MovementDebugSystem)
  |  calls: drawBuilder.DrawLine(start, end, color)
  |         drawBuilder.DrawEntityBadge(entity, text)
  v
DebugPrimitiveBuffer (Fdp.Diagnostics.Contracts)
  |  stores primitives in a flat native buffer
  v
DebugGizmoLayer.Draw(context)
  |  iterates buffer, calls Raylib draw calls
  v
Raylib Screen
```

The `IDebugDrawBuilder` interface (defined in `Fdp.Diagnostics.Contracts`) decouples the
ECS systems from the rendering backend. A system that only draws debug primitives need not
reference `Fdp.Presentation` at all.

---

## 6. Diagnostics Contracts (Fdp.Diagnostics.Contracts)

`Fdp.Diagnostics.Contracts` is a thin assembly that defines the `IDebugDrawBuilder` interface
and `DebugPrimitiveBuffer`. It is the bridge between ECS systems and presentation backends.

### IDebugDrawBuilder

```csharp
public interface IDebugDrawBuilder
{
    void DrawLine(Vector3 start, Vector3 end, Rgba32 color,
        float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels,
        PipelineTarget target = PipelineTarget.All, byte layer = 0,
        LineStyle style = LineStyle.Solid);

    void DrawArrow(Vector3 from, Vector3 to, Rgba32 color,
        float headSize = 1f, byte layer = 0);

    void DrawSphere(Vector3 center, float radius, Rgba32 color, ...);
    void DrawBox2D(Vector2 center, Vector2 extents, Rgba32 color, ...);
    void DrawText(float x, float y, FixedString32 text, Rgba32 color, ...);
    void DrawTextLong(float x, float y, string text, Rgba32 color, ...);

    // Entity-coupled draw calls (anchor position from entity's transform):
    void DrawEntityBadge(Entity target, FixedString32 richText, ...);
    void DrawEntityLocal(Entity anchor, Vector3 localStart, Vector3 localEnd, ...);

    // Frame lifecycle:
    void EndFrame(float deltaTime);  // evict expired persistent primitives, clear transient
}
```

`SizeMode` controls whether sizes are in screen pixels or world metres.
`PipelineTarget` routes primitives to the 2D canvas, 3D viewport, or both.
`layer` is a byte sort key inside the renderer.

### EcsDebugPrimitiveExtensions

Convenience extension methods on `EntityRepository` that retrieve the singleton
`DebugPrimitiveBuffer` and forward draw calls. Systems that import only `Fdp.Core` can use:

```csharp
world.DrawDebugLine(start, end, Rgba32.Red);
world.DrawEntityBadge(entity, "HP:100");
```

---

## 7. Data Flow Diagrams

### 7.1 Frame Execution Sequence

```
FdpApplication.Run() -- one iteration
|
+-- OnUpdate(dt)
|     |
|     +-- kernel.Update()
|           |
|           +-- TimeController.Update() --> GlobalTime
|           |
|           +-- world.Tick()            -- increment GlobalVersion
|           +-- world.SetSingleton(globalTime)
|           |
|           +-- [Phase: Input]
|           |     Scheduler.ExecutePhase(Input, world, dt)
|           |       --> InputSystem.Execute(world, dt)   [main thread]
|           |
|           +-- [RCU Swap -- if pending topology operation]
|           |     Volatile.Write(ref _activeTopology, newTopology)
|           |
|           +-- [Phase: BeforeSync]
|           |     Scheduler.ExecutePhase(BeforeSync, world, dt)
|           |     ECB flush: CommandBuffer.Playback(world)
|           |     world.Bus.SwapBuffers()   -- events N-1 -> readable
|           |
|           +-- [Sync Point]
|           |     For each module: provider.Update()  -- sync GDB replicas
|           |     Harvest completed async tasks
|           |
|           +-- [Dispatch]
|           |     For each module (synchronous):
|           |       module.SimulationSystems.Execute(view, dt)
|           |       module.Tick(view, dt)
|           |     For each module (async/frame-synced):
|           |       Task.Run(module.Execute)
|           |
|           +-- Wait for FrameSynced tasks
|           |
|           +-- [Phase: PostSimulation]
|           |     Scheduler.ExecutePhase(PostSimulation, world, dt)
|           |       --> TransformSyncSystem, InterpolationSystem, etc.
|           |
|           +-- [Phase: Export]
|                 Scheduler.ExecutePhase(Export, world, dt)
|                   --> NetworkSendSystem, RecorderSystem, etc.
|
+-- OnDrawWorld()
|     canvas.Draw(world)
|       --> GridMapLayer.Draw()
|       --> EntityLayer.Draw()   [custom]
|       --> DebugGizmoLayer.Draw()
|
+-- OnDrawUI()   [ImGui]
      --> DiagnosticsWindow.Draw(kernel)
      --> EntityInspectorWindow.Draw(world)
      --> RecorderControlsWindow.Draw()
```

### 7.2 Module Registration and Initialization Flow

```
Application.OnLoad()
|
+-- world = new EntityRepository()
+-- accumulator = new EventAccumulator()
+-- kernel = new ModuleHostKernel(world, accumulator)
|
+-- kernel.RegisterModule(physicsModule)
|     --> physicsModule.GetRequiredComponents()
|         --> world.RegisterComponent<T>() for each declared type
|         --> BitMask256 componentMask computed
|         --> ModuleEntry appended to _modules list
|
+-- kernel.RegisterModule(networkModule)
+-- kernel.RegisterModule(aiModule)
|
+-- kernel.SetTimeController(new SteppingTimeController())
|
+-- kernel.Initialize()
      |
      +-- new SnapshotPool(warmupCount: 10)
      |
      +-- For each ModuleEntry:
      |     entry.Module.Policy.Validate()
      |     AssignProvider(entry)  -- creates GDB/SoD/Shared provider
      |     entry.LifecycleState = Ready
      |
      +-- BuildTopology(_modules)
            |
            +-- new SystemScheduler()
            +-- For each module:
            |     module.RegisterSystems(scheduler)
            |       --> scheduler.RegisterSystem(new MovementSystem())
            |       --> scheduler.RegisterSystem(new CollisionSystem())
            +-- globalSystems -> scheduler (re-injected)
            +-- scheduler.BuildExecutionOrders()
                  --> topological sort per phase (DependencyGraph)
                  --> throws CircularDependencyException on cycles
            +-- _activeTopology = new KernelExecutionTopology(modules, scheduler)
```

### 7.3 Query and Entity Mutation Flow

```
System executing inside kernel.Update():

ISimulationView view = AcquireView()    -- may be live world or replica

1. READ PATH (query iteration)
   |
   +-- QueryBuilder.With<SimTransform>().Without<DeadTag>().Build()
   |     --> _includeMask[SimTransform.ID] = 1
   |     --> _excludeMask[DeadTag.ID] = 1
   |
   +-- foreach (var entity in query)
         |
         +-- EntityEnumerator.MoveNext()
         |     --> entityIndex[i].ComponentMask & includeMask == includeMask?
         |     --> entityIndex[i].ComponentMask & excludeMask == 0?
         |     --> O(1) per entity via SIMD BitMask256
         |
         +-- ref var t = ref view.Get<SimTransform>(entity)
               --> NativeChunkTable<SimTransform>[entity.Index]
               --> Direct memory reference, no copy

2. WRITE PATH (deferred via ECB for background threads)
   |
   +-- var cmd = view.GetCommandBuffer()
   +-- cmd.CreateEntity()           -- opcode 0x00
   +-- cmd.AddComponent(e, comp)    -- opcode 0x02
   +-- cmd.DestroyEntity(e)         -- opcode 0x01
   |
   +-- [at sync point] cmd.Playback(world)
         --> replays opcodes against live world
         --> new entities get real indices (placeholder remapping)

3. DIRECT WRITE PATH (synchronous main-thread modules only)
   |
   +-- world.Set<SimTransform>(entity, newTransform)
   +-- world.AddComponent<NewTag>(entity)
   +-- world.DestroyEntity(entity)
```

### 7.4 Debug Visualization Flow

```
Per-frame debug draw pipeline:

ECS System (e.g. PhysicsDebugSystem, [UpdateInPhase(PostSimulation)])
|
|  drawBuilder.DrawLine(a, b, Rgba32.Yellow)
|  drawBuilder.DrawEntityBadge(entity, "vel:5.2")
|
v
DebugPrimitiveBuffer                    [in Fdp.Diagnostics.Contracts]
  NativePrimitiveList (transient)       -- cleared each frame
  NativePrimitiveList (persistent)      -- evicted by lifetime

DebugGizmoLayer.Draw(RenderContext ctx)
|
|  for each primitive in buffer:
|    if type == Line      -> Raylib.DrawLineEx(...)
|    if type == Sphere    -> Raylib.DrawCircleLines(...)
|    if type == Text      -> Raylib.DrawText(...)
|    if type == EntityBadge:
|        pos = world.Get<SimTransform>(primitive.AnchorEntity).Position
|        -> Raylib.DrawText(pos + offset, ...)
|
v
Raylib Back Buffer -> Screen

[End of frame]
drawBuilder.EndFrame(dt)
  --> advance persistence clock
  --> evict expired persistent primitives
  --> clear transient list
```

---

## 8. Key Integration Patterns

### 8.1 Minimal Application (No Visualization)

For headless simulations (tests, CI, dedicated servers):

```csharp
var world = new EntityRepository();
world.RegisterComponent<SimTransform>();
world.RegisterComponent<SimVelocity>();
world.RegisterEvent<UnitSpawnedEvent>();

var accumulator = new EventAccumulator();
var kernel = new ModuleHostKernel(world, accumulator);

kernel.RegisterModule(new PhysicsModule());
kernel.RegisterModule(new NetworkModule());
kernel.SetTimeController(new SteppingTimeController(targetHz: 60));
kernel.Initialize();

// Headless loop:
for (int i = 0; i < 600; i++)
    kernel.Update();

kernel.Dispose();
world.Dispose();
```

### 8.2 Windowed Application with MapCanvas

Subclass `FdpApplication` and override the four lifecycle hooks:

```csharp
public class MySimApp : FdpApplication
{
    private MapCanvas _canvas = null!;

    public MySimApp() : base(new ApplicationConfig
    {
        WindowTitle = "My Simulation",
        Width = 1600, Height = 900,
        TargetFPS = 60
    }) { }

    protected override void OnLoad()
    {
        World  = new EntityRepository();
        Kernel = new ModuleHostKernel(World, new EventAccumulator());

        World.RegisterComponent<SimTransform>();
        // ... register other components ...

        Kernel.RegisterModule(new PhysicsModule());
        Kernel.SetTimeController(new SteppingTimeController(60));
        Kernel.Initialize();

        _canvas = new MapCanvas();
        _canvas.AddLayer(new GridMapLayer());
        _canvas.AddLayer(new EntityRenderLayer(World));   // custom layer
        _canvas.AddLayer(new DebugGizmoLayer(31, debugBuffer, World.Bus));
    }

    protected override void OnUpdate(float dt)
        => Kernel.Update();

    protected override void OnDrawWorld()
        => _canvas.Draw(World);

    protected override void OnDrawUI()
    {
        ImGui.Begin("Diagnostics");
        foreach (var diag in Kernel.GetModuleDiagnostics())
            ImGui.Text($"{diag.ModuleName}: {diag.ExecutionCount} ticks");
        ImGui.End();
    }

    protected override void OnUnload()
    {
        Kernel.Dispose();
        World.Dispose();
    }
}
```

### 8.3 System Ordering and Phase Assignment

Systems within the same phase are sorted topologically using `[UpdateAfter]` /
`[UpdateBefore]` attributes. The kernel detects circular dependencies at `Initialize()` time
and throws `CircularDependencyException`.

```csharp
// Input systems
[UpdateInPhase(SystemPhase.Input)]
public class InputCollectorSystem : IEcsModuleSystem { ... }

// Physics depends on input being processed first
[UpdateInPhase(SystemPhase.Simulation)]
[UpdateAfter(typeof(VelocityApplySystem))]
public class CollisionDetectionSystem : IEcsModuleSystem { ... }

[UpdateInPhase(SystemPhase.Simulation)]
public class VelocityApplySystem : IEcsModuleSystem { ... }

// Export runs after all sim phases are complete
[UpdateInPhase(SystemPhase.Export)]
[UpdateAfter(typeof(CollisionDetectionSystem))]
public class NetworkExportSystem : IEcsModuleSystem { ... }
```

### 8.4 Module Composition Patterns

**Feature Module** -- one module per feature, registers all its systems:

```csharp
public class CombatModule : IEcsModule
{
    public string Name => "Combat";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public void RegisterSystems(ISystemRegistry reg)
    {
        reg.RegisterSystem(new WeaponFireSystem());
        reg.RegisterSystem(new DamageApplySystem());
        reg.RegisterSystem(new DeathCleanupSystem());
    }

    public void Tick(ISimulationView view, float dt) { }  // empty -- systems handle it
}
```

**Toolkit Adapter Module** -- wraps a third-party toolkit so it plugs into FDP:

```csharp
public class PhysicsToolkitModule : IEcsModule
{
    public string Name => "Physics";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly PhysicsWorld _physicsWorld = new();

    public void RegisterSystems(ISystemRegistry reg)
    {
        reg.RegisterSystem(new BroadphaseSystem(_physicsWorld));
        reg.RegisterSystem(new NarrowphaseSystem(_physicsWorld));
        reg.RegisterSystem(new PhysicsResultApplySystem(_physicsWorld));
    }

    public void Tick(ISimulationView view, float dt) { }
}
```

**Background Analytics Module** -- runs async with SoD snapshots:

```csharp
public class ThreatAnalysisModule : IEcsModule
{
    public string Name => "ThreatAnalysis";
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(frequencyHz: 5);

    public void RegisterSystems(ISystemRegistry reg) { }

    public void Tick(ISimulationView view, float dt)
    {
        // This runs on a background thread with a snapshot copy.
        // Reading the view is safe; mutations must go through the ECB.
        var threats = ComputeThreatMap(view);
        PublishResults(view, threats);
    }
}
```

### 8.5 Best Practices for the Full Stack

- **Register components before `kernel.Initialize()`**. Dynamic registration after init is
  supported but requires a new topology compile.
- **Prefer system-based modules** (`RegisterSystems`) over direct-tick modules. Systems
  get phase control, dependency ordering, and profiling for free.
- **Use `ExecutionPolicy.Synchronous()` only for latency-critical work.** Heavy computation
  on the main thread breaks the 60 Hz budget.
- **Use `[UpdateInPhase(SystemPhase.Export)]` for all network and recording writes.** This
  keeps the export fence clean and makes it easy to disable export in headless tests.
- **Read the world via `ISimulationView`, not `EntityRepository` directly.** The interface
  is honoured by both the live world and snapshot replicas; modules that use the interface
  work in all execution modes.
- **Keep `FdpApplication.OnUpdate()` thin.** Put module logic in systems, not in the
  application class.
- **Set `ActiveLayerMask` on `MapCanvas`** to quickly show/hide diagnostic layers without
  removing them from the pipeline.

---

## 9. Complete Application Example

The following example assembles all three layers into a minimal but complete simulation
application: a moving-entities demo with a debug visualization overlay and an ImGui panel.

### Step 1 -- Define Components and Events

```csharp
// Components.cs
using Fdp.Core;

[ComponentId(0)] public struct SimTransform
{
    public System.Numerics.Vector3 Position;
    public float HeadingDeg;
}

[ComponentId(1)] public struct SimVelocity
{
    public System.Numerics.Vector3 Velocity;
}

[ComponentId(2)] public struct UnitTag { }   // tag, no data

[EventId(100)] public struct UnitReachedWaypointEvent
{
    public Entity UnitEntity;
}
```

### Step 2 -- Define Systems

```csharp
// Systems.cs
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using System.Numerics;

[UpdateInPhase(SystemPhase.Simulation)]
public class MovementSystem : IEcsModuleSystem
{
    private readonly EntityQuery _query;

    public MovementSystem(EntityRepository world)
    {
        _query = world.Query()
            .With<SimTransform>()
            .With<SimVelocity>()
            .Build();
    }

    public void Execute(ISimulationView view, float dt)
    {
        foreach (var e in _query)
        {
            ref var t = ref view.Get<SimTransform>(e);
            ref var v = ref view.Get<SimVelocity>(e);
            t.Position += v.Velocity * dt;
        }
    }
}

[UpdateInPhase(SystemPhase.PostSimulation)]
[UpdateAfter(typeof(MovementSystem))]
public class MovementDebugSystem : IEcsModuleSystem
{
    private readonly EntityQuery _query;
    private readonly IDebugDrawBuilder _draw;

    public MovementDebugSystem(EntityRepository world, IDebugDrawBuilder draw)
    {
        _query = world.Query().With<SimTransform>().With<SimVelocity>().Build();
        _draw  = draw;
    }

    public void Execute(ISimulationView view, float dt)
    {
        foreach (var e in _query)
        {
            ref var t = ref view.Get<SimTransform>(e);
            ref var v = ref view.Get<SimVelocity>(e);
            var tip = t.Position + Vector3.Normalize(v.Velocity) * 10f;
            _draw.DrawArrow(t.Position, tip, new Rgba32(255, 200, 0, 255));
            _draw.DrawEntityBadge(e, $"spd:{v.Velocity.Length():F1}");
        }
    }
}
```

### Step 3 -- Define Module

```csharp
// UnitSimModule.cs
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

public class UnitSimModule : IEcsModule
{
    private readonly EntityRepository _world;
    private readonly IDebugDrawBuilder _draw;

    public string Name => "UnitSimulation";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public UnitSimModule(EntityRepository world, IDebugDrawBuilder draw)
    {
        _world = world;
        _draw  = draw;
    }

    public void RegisterSystems(ISystemRegistry reg)
    {
        reg.RegisterSystem(new MovementSystem(_world));
        reg.RegisterSystem(new MovementDebugSystem(_world, _draw));
    }

    public void Tick(ISimulationView view, float dt) { }

    public System.Collections.Generic.IEnumerable<System.Type> GetRequiredComponents()
    {
        yield return typeof(SimTransform);
        yield return typeof(SimVelocity);
        yield return typeof(UnitTag);
    }
}
```

### Step 4 -- Assemble the Application

```csharp
// App.cs
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Presentation.Raylib;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Diagnostics.Gizmos;
using ImGuiNET;

public class DemoApp : FdpApplication
{
    private MapCanvas          _canvas  = null!;
    private DebugPrimitiveBuffer _dbuf  = null!;

    public DemoApp() : base(new ApplicationConfig
    {
        WindowTitle = "FDP Demo",
        Width       = 1280,
        Height      = 720,
        TargetFPS   = 60
    }) { }

    protected override void OnLoad()
    {
        // Layer 1: ECS world
        World = new EntityRepository();
        World.RegisterComponent<SimTransform>();
        World.RegisterComponent<SimVelocity>();
        World.RegisterComponent<UnitTag>();
        World.RegisterEvent<UnitReachedWaypointEvent>();

        // Diagnostics buffer (Layer 3 -> Layer 1 bridge)
        _dbuf = new DebugPrimitiveBuffer();

        // Layer 2: kernel + module
        Kernel = new ModuleHostKernel(World, new EventAccumulator());
        Kernel.RegisterModule(new UnitSimModule(World, _dbuf));
        Kernel.SetTimeController(new Fdp.Toolkit.Time.SteppingTimeController(60));
        Kernel.Initialize();

        // Spawn some test entities
        for (int i = 0; i < 10; i++)
        {
            var e = World.CreateEntity();
            World.AddComponent(e, new SimTransform
            {
                Position = new System.Numerics.Vector3(i * 50f, i * 30f, 0f)
            });
            World.AddComponent(e, new SimVelocity
            {
                Velocity = new System.Numerics.Vector3(10f + i * 2f, 5f, 0f)
            });
            World.AddComponent(e, new UnitTag());
        }

        // Layer 3: canvas
        _canvas = new MapCanvas();
        _canvas.DrawBuffer = _dbuf;
        _canvas.AddLayer(new GridMapLayer());
        _canvas.AddLayer(new DebugGizmoLayer(31, _dbuf, World.Bus));
    }

    protected override void OnUpdate(float dt)
    {
        _canvas.Update(dt);
        Kernel.Update();
        _dbuf.EndFrame(dt);   // evict expired primitives after all systems ran
    }

    protected override void OnDrawWorld()
        => _canvas.Draw(World);

    protected override void OnDrawUI()
    {
        ImGui.Begin("Kernel Diagnostics");
        ImGui.Text($"Frame: {Kernel.CurrentTime.FrameNumber}");
        foreach (var m in Kernel.GetModuleDiagnostics())
        {
            ImGui.Text($"{m.ModuleName}  ticks:{m.ExecutionCount}" +
                       $"  circuit:{m.CircuitState}");
        }
        ImGui.End();
    }

    protected override void OnUnload()
    {
        Kernel.Dispose();
        World.Dispose();
    }
}

// Program.cs
new DemoApp().Run();
```

---

## 10. Anti-patterns and Common Mistakes

### Writing to the Live World from a Background Module

**Problem:** A `SlowBackground` or `FastReplica` module receives a snapshot view, not the
live world. Writing directly to the live `EntityRepository` bypasses the snapshot mechanism
and causes race conditions.

**Wrong:**
```csharp
// BAD: module has DataStrategy.SoD -- view is a snapshot, not the live world!
public void Tick(ISimulationView view, float dt)
{
    ref var t = ref ((EntityRepository)view).Get<SimTransform>(e); // cast fails silently
    t.Position = newPos;  // mutation on wrong object
}
```

**Correct:**
```csharp
public void Tick(ISimulationView view, float dt)
{
    var cmd = view.GetCommandBuffer();
    cmd.SetComponent(e, new SimTransform { Position = newPos });
    // cmd.Playback(liveWorld) happens at the next sync point
}
```

### Registering Components After Initialize()

**Problem:** Component IDs are assigned at registration time. Registering a component after
`kernel.Initialize()` will not automatically propagate the new ID to snapshot replicas that
were already created.

**Correct:** Register all components before calling `kernel.Initialize()`. If dynamic
registration is truly required, use `kernel.InstallModuleAsync()` which handles replica
propagation via the RCU path.

### Building Queries Inside Execute()

**Problem:** `QueryBuilder.Build()` allocates a new `EntityQuery` object every call.
Building a query inside `Execute()` creates allocation pressure every frame.

**Wrong:**
```csharp
public void Execute(ISimulationView view, float dt)
{
    // BAD: allocates a new EntityQuery every frame
    var query = view.Query().With<SimTransform>().Build();
    foreach (var e in query) { ... }
}
```

**Correct:**
```csharp
public class MySystem : IEcsModuleSystem
{
    private readonly EntityQuery _query;

    public MySystem(EntityRepository world)
    {
        // Build once at construction:
        _query = world.Query().With<SimTransform>().Build();
    }

    public void Execute(ISimulationView view, float dt)
    {
        foreach (var e in _query) { ... }  // zero allocation
    }
}
```

### Calling kernel.Update() from OnDrawUI()

**Problem:** The kernel must be updated exactly once per frame. Calling it inside the ImGui
draw pass runs the simulation twice per visual frame, causing determinism issues.

**Correct:** Call `kernel.Update()` exclusively inside `OnUpdate(dt)`.

### Using SystemPhase.Simulation for Global Systems

**Problem:** `ModuleHostKernel` only executes `SystemPhase.Simulation` for systems that
belong to modules (dispatched on background threads). Global systems registered with
`RegisterGlobalSystem()` are only called for: `Input`, `BeforeSync`, `PostSimulation`,
`Export`.

**Wrong:**
```csharp
[UpdateInPhase(SystemPhase.Simulation)]  // will never execute for global systems
public class MyGlobalSystem : IEcsModuleSystem { ... }
kernel.RegisterGlobalSystem(new MyGlobalSystem());  // silently ignored in Simulation phase
```

**Correct:** Use `SystemPhase.PostSimulation` or `SystemPhase.Export` for global systems,
or package the system inside a module.

### Forgetting to Dispose ISnapshotProvider Views

**Problem:** `SoD` and `Shared` providers pool their snapshots. Failing to call
`provider.ReleaseView(view)` after each module tick exhausts the pool and causes stalls.

The kernel handles this automatically for modules dispatched through the standard
`ExecuteModuleSafe` path. The issue only arises when calling `AcquireView()` manually.

**Correct:**
```csharp
var view = provider.AcquireView();
try
{
    ProcessView(view);
}
finally
{
    provider.ReleaseView(view);  // always release
}
```

### Circular System Dependencies

**Problem:** `[UpdateAfter(typeof(A))]` on system B and `[UpdateAfter(typeof(B))]` on
system A within the same phase will throw `CircularDependencyException` at `Initialize()`.

The exception message lists all system names in the cycle. Fix by removing one dependency
or splitting into separate phases.

### Using Direct DataStrategy with Async RunMode

**Problem:** `DataStrategy.Direct` gives the module a reference to the live world. Combined
with `RunMode.Asynchronous`, this causes concurrent writes to unprotected state.

`ExecutionPolicy.Validate()` catches this combination and throws at `Initialize()` time.

---

## 11. Links to Individual Project Docs

| Project                      | Doc Path                                                                              |
|------------------------------|---------------------------------------------------------------------------------------|
| Fdp.Core                     | [FDP/Engine/docs/](../../../FDP/Engine/docs/)                                        |
| Fdp.ModuleHost               | [FDP/Engine/Fdp.ModuleHost/docs/](../../../FDP/Engine/Fdp.ModuleHost/docs/)          |
| Fdp.Presentation (ImGui)     | [FDP/Docs/USER-GUIDE-IMGUI.md](../../../FDP/Docs/USER-GUIDE-IMGUI.md)               |
| Fdp.Presentation (Raylib)    | [FDP/Docs/USER-GUIDE-RAYLIB.md](../../../FDP/Docs/USER-GUIDE-RAYLIB.md)             |
| Vis2D (MapCanvas)            | [FDP/Docs/USER-GUIDE-VIS2D.md](../../../FDP/Docs/USER-GUIDE-VIS2D.md)               |
| Overview                     | [FDP/Docs/USER-GUIDE-OVERVIEW.md](../../../FDP/Docs/USER-GUIDE-OVERVIEW.md)         |
| Architectural Rules          | [FDP/Docs/architectural-rules.md](../../../FDP/Docs/architectural-rules.md)         |

---

*End of FDP Core Framework Architecture document.*
