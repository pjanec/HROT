# Hrot.StrideMock

**Project path:** `Hrot/Subsystems/Hrot.StrideMock/`
**Project file:** `Hrot.StrideMock.csproj`
**Date:** 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` file exists in the project folder. Documentation is provided exclusively
through XML doc-comments on the public API and this architectural document.

---

## Executive Overview

`Hrot.StrideMock` is a headless-compatible rendering and simulation node for the HROT
military simulation system. Its purpose is twofold:

1. **Fake Stride types** - A minimal set of plain C# classes (`FakeStrideEntity`,
   `FakeStrideEffect`, `FakeStrideScript`) that mirror the shape of the Stride game engine
   API without requiring the Stride runtime to be present. These enable the ECS-to-engine
   synchronization logic to be developed, tested, and executed outside of Stride.

2. **Full simulation node** - A complete FDP/HROT node (`StrideNodeBootstrapper`) running
   the kinematic, perception, combat, navigation, and visual-effect subsystems in the same
   process. The node communicates over DDS and can run in headless mode for automated tests.

The project is named "StrideMock" because it sits in the architectural slot where a real
Stride engine node would eventually live. When Stage 2 integration arrives, only the three
`Fake*` types need replacement; all orchestration, synchronization, and ECS logic stays.

### What Stride Types Are Mocked

| Stride concept       | Fake type in this project  | Purpose                                    |
|----------------------|----------------------------|--------------------------------------------|
| `SyncScript`         | `FakeStrideScript`         | Abstract per-frame script base class       |
| `Entity` (transform) | `FakeStrideEntity`         | Position + yaw of a live simulation entity |
| Entity + VFX         | `FakeStrideEffect`         | Visual effect state: type, position, alpha |

### Why This Approach

- The full Stride engine cannot run in a CI/CD environment or headless test process because
  it requires a GPU context and a display server.
- The FDP ECS, DDS networking, and combat event pipeline are all engine-agnostic. Testing
  those layers only requires the output they produce (entity positions and effect states),
  not a live rendering context.
- The fake types are purely mutable data containers. Any test can inspect them after calling
  `SyncFdpToStrideScript.Update()` without mocking or dependency injection.

---

## Architecture

### Layered Design

The project is organized in three clearly separated tiers:

```
+--------------------------------------------------------------+
|                   StrideMockSubsystem                        |
|  ISubsystem  +  IMapCameraProvider  +  IWindowRegistrar      |
|                                                              |
|  Lifecycle: Initialize / Update / DrawWorld / DrawUI /       |
|             Shutdown  (delegates everything to core)         |
+-------------------------------+------------------------------+
                                |
               +----------------+----------------+
               |                                 |
+--------------v--------------+   +--------------v-----------+
|   StrideNodeBootstrapper    |   |  SyncFdpToStrideScript   |
|                             |   |                          |
|  SharedApplicationBootstrap |   |  FakeStrideScript        |
|  + 7-phase ECS bootstrap    |   |  + 2-pass differential   |
|  + Tick() main-loop pump    |   |    entity/effect sync    |
|  + ProducerBuffer           |   |  + state-gate            |
|  + ConsumerBuffer           |   |  + yaw extraction        |
|  + MapCamera                |   |                          |
+----------+------------------+   +-------+------------------+
           |                               |
           |  reads                        |  reads/writes
           v                               v
+----------+-------------------------------+------------------+
|                    FDP ECS World                           |
|  EntityRepository  /  ISimulationView                      |
|  Components: SimTransform, VisualEffectState, TracerTarget |
|  Systems:    EventToEffectSystem, VisualEffectCleanup...   |
+------------------------------------------------------------+
```

### ECS Synchronization Flow

`SyncFdpToStrideScript` bridges the FDP ECS world to the fake Stride layer using a
differential two-pass strategy executed every frame:

```
                        Update(deltaTime)
                               |
              +----------------+------------------+
              |                                   |
         IsOperatingState?                   CurrentStateMessage
            YES                                = "Cluster: {state}"
              |
     +--------+--------+
     |                 |
SyncStrideEntities  SyncStrideEffects
     |                 |
     |   Pass 1: iterate dictionary, collect stale keys (IsAlive = false)
     |            remove stale entries  --->  FakeStrideEntity/Effect destroyed
     |
     |   Pass 2: ECS query (.With<SimTransform>...)
     |            upsert FakeStrideEntity or FakeStrideEffect
     |            copy position, yaw, effect type, scale, alpha, tracer end
     v
 ActiveEntities / ActiveEffects (IEnumerable<> read by renderer)
```

### Component Registration Map

Components registered in `RegisterDomainComponents` and consumed in the sync script:

```
+---------------------------+    required by    +---------------------------+
|  HrotSharedComponentReg.  |------------------>|  Network replication       |
+---------------------------+                   +---------------------------+

+---------------------------+    required by    +---------------------------+
|  MuscleRoleComponentReg.  |------------------>|  VehicleState, NavState,  |
+---------------------------+                   |  FormationSlot etc.       |
                                                +---------------------------+

+---------------------------+    required by    +---------------------------+
|  PresentationComponentReg |------------------>|  Vis2D components         |
+---------------------------+                   +---------------------------+

+---------------------------+    queried by     +---------------------------+
|  VisualEffectState        |------------------>|  SyncFdpToStrideScript    |
|  TracerTarget             |   (explicit reg.) |  EventToEffectSystem      |
+---------------------------+                   +---------------------------+

+---------------------------+    required by    +---------------------------+
|  GenesisIntentRegistry    |------------------>|  GenesisMaterializationSys|
+---------------------------+                   +---------------------------+
```

### Bootstrap Phase Pipeline

`StrideNodeBootstrapper.BootstrapNode` calls the inherited 7-phase pipeline from
`SharedApplicationBootstrapper`:

```
Phase 1:  BuildContext
           HrotNodeBuilder.Build() -> HrotNodeContext
           (ECS world, kernel, event bus, participant)

Phase 2:  RegisterDomainComponents
           HrotSharedComponentRegistry + MuscleRoleComponentRegistry
           + PresentationComponentRegistry + VisualEffectState
           + TracerTarget + GenesisIntentRegistry

Phase 3:  BuildSerializer
           HrotScenarioSerializerFactory.Build()
           (StrideMock does NOT load scenarios but serializer is required
            by PopulateSystems for BehaviorRegistry wiring)

Phase 4:  PopulateSystems
           sim.Add(EventToEffectSystem)
           postSim.Add(VisualEffectCleanupSystem)

Phase 5:  BuildOrchestration
           NodeBootstrapper.BuildOrchestration()
           -> ClusterSlave, SlaveTranslator side-effect captured

Phase 6a: NedReplication module registration (base class)
Phase 6b: Additional modules: kinematics, perception, combat, navigation
Phase 6c: Time-sync translators (base class, no subclass hook)
Phase 6d: RegisterApplicationSystems (ApplicationSystemsRegistrar callback)
           -> EventHistoryCaptureSystem

Phase 6e: RegisterNetworkTranslators
           SimHostAuxiliaryTranslators (when factory != null)

Phase 6f: RegisterSpawningPipeline
           GenesisMaterializationSystem (always)
           NetworkSpawningSystem (when IdAllocator != null)

Phase 7:  Kernel.Initialize()
```

---

## Source Structure

### Namespace: `Hrot.StrideMock`

All production source files live directly in the project root folder. There is no
sub-namespace hierarchy.

```
Hrot.StrideMock/
+-- FakeStrideEntity.cs           <- Fake Stride entity data container
+-- FakeStrideEffect.cs           <- Fake Stride visual effect data container
+-- FakeStrideScript.cs           <- Abstract fake Stride SyncScript base
+-- SyncFdpToStrideScript.cs      <- ECS-to-fake-Stride synchronization script
+-- StrideNodeBootstrapper.cs     <- Full simulation node bootstrapper
+-- StrideMockSubsystem.cs        <- ISubsystem / IMapCameraProvider adapter
```

### Namespace: `Hrot.StrideMock.Tests`

```
Hrot.StrideMock.Tests/
+-- SharedApplicationBootstrapperTests.cs   <- SC_SM002_x (10 tests)
+-- StrideNodeBootstrapperTests.cs          <- SC_SM003_x + SC_SM005_x (13 tests)
+-- SyncFdpToStrideScriptTests.cs           <- SC_SM004_x (8 tests)
+-- StrideMockSubsystemTests.cs             <- SC_SM006_x (11 tests)
```

---

## Public API Reference

### `FakeStrideScript` (abstract class)

```
Namespace : Hrot.StrideMock
Base      : object
Modifier  : abstract
```

Mirrors the lifecycle contract of Stride's `SyncScript`. Subclass to implement
per-frame simulation logic that can be ported to a real Stride project by replacing
the fake entity/effect types with their Stride equivalents.

| Member                       | Kind             | Description                              |
|------------------------------|------------------|------------------------------------------|
| `Start()`                    | abstract method  | Called once before the first Update.     |
| `Update(float deltaTime)`    | abstract method  | Called every frame with elapsed seconds. |

---

### `FakeStrideEntity` (sealed class)

```
Namespace : Hrot.StrideMock
Modifier  : sealed
```

Mutable runtime representation of a live ECS entity. Updated each frame by
`SyncFdpToStrideScript`.

| Member           | Type      | Description                                              |
|------------------|-----------|----------------------------------------------------------|
| `Position`       | `Vector3` | World-space position in metres (flat-Earth Cartesian).   |
| `Rotation`       | `float`   | Yaw angle in radians (rotation around world-up Z axis).  |

---

### `FakeStrideEffect` (sealed class)

```
Namespace : Hrot.StrideMock
Modifier  : sealed
```

Mutable runtime representation of a visual effect entity (explosion, tracer, fire).
Updated each frame by `SyncFdpToStrideScript` from the ECS `VisualEffectState` component.

| Member       | Type         | Description                                                       |
|--------------|--------------|-------------------------------------------------------------------|
| `Type`       | `EffectType` | Explosion, Tracer, or Fire.                                       |
| `Position`   | `Vector3`    | World-space origin of the effect.                                 |
| `TracerEnd`  | `Vector3`    | World-space endpoint for tracer line rendering. Zero otherwise.   |
| `Scale`      | `float`      | Current visual scale.                                             |
| `Alpha`      | `float`      | Opacity in [0,1]. Decreases as the effect ages toward expiry.     |

---

### `SyncFdpToStrideScript` (sealed class)

```
Namespace : Hrot.StrideMock
Base      : FakeStrideScript
Modifier  : sealed
```

ECS-to-engine synchronisation script. Inherits `FakeStrideScript`. Performs a
differential two-pass sync each frame. Sync is suppressed during non-operating cluster
states (loading, editing) and replaced with a splash message.

#### Constructor

| Signature                                        | Description                                     |
|--------------------------------------------------|-------------------------------------------------|
| `SyncFdpToStrideScript(StrideNodeBootstrapper)`  | Requires a bootstrapped node. Throws `ArgumentNullException` if null. |

#### Properties

| Member                 | Type                          | Description                                                       |
|------------------------|-------------------------------|-------------------------------------------------------------------|
| `ActiveEntities`       | `IEnumerable<FakeStrideEntity>` | All currently live simulation entities (non-effect).            |
| `ActiveEffects`        | `IEnumerable<FakeStrideEffect>` | All currently live visual effect entities.                      |
| `CurrentStateMessage`  | `string`                      | Non-empty splash text during non-operating states; empty otherwise. |
| `CurrentClusterState`  | `ClusterState`                | The most recently observed cluster state.                        |

#### Methods (from `FakeStrideScript`)

| Member              | Description                     |
|---------------------|---------------------------------|
| `Start()`           | No-op (hook for future use).    |
| `Update(float dt)`  | Runs the two-pass ECS sync or sets the splash message. |

---

### `StrideNodeBootstrapper` (sealed class)

```
Namespace : Hrot.StrideMock
Base      : SharedApplicationBootstrapper, IDisposable
Modifier  : sealed
```

Concrete bootstrapper for the Stride mock simulation node. Implements all six abstract
hooks of `SharedApplicationBootstrapper` and exposes the resulting context for use by
`SyncFdpToStrideScript` and `StrideMockSubsystem`.

#### Static Members

| Member  | Type       | Value                                                                  |
|---------|------------|------------------------------------------------------------------------|
| `Role`  | `NodeRole` | `MuscleGround | Perception | NavigationSolver | ImageGenerator`        |

#### Constructor

| Signature                                                                                              | Description                                      |
|--------------------------------------------------------------------------------------------------------|--------------------------------------------------|
| `StrideNodeBootstrapper(IEcsModule? kinematics, IEcsModule? perception, IEcsModule? combat, IEcsModule? navigation)` | All parameters optional; default null. |

#### Properties

| Member                        | Type                         | Availability             | Description                                               |
|-------------------------------|------------------------------|--------------------------|-----------------------------------------------------------|
| `Context`                     | `HrotNodeContext`            | After `BootstrapNode`    | Fully wired node context.                                 |
| `SimGroup`                    | `TogglableSimulationGroup`   | After `BootstrapNode`    | Sim-phase togglable group for replay control.             |
| `PostSimGroup`                | `TogglablePostSimulationGroup` | After `BootstrapNode`  | Post-sim togglable group.                                 |
| `ApplicationSystemsRegistrar` | `Action<HrotNodeContext>?`   | Settable before boot     | Optional callback invoked in Phase 6d.                    |
| `ProducerBuffer`              | `DebugPrimitiveBuffer`       | Always                   | Local ECS systems write gizmos here for DDS publication.  |
| `ConsumerBuffer`              | `DebugPrimitiveBuffer`       | Always                   | Populated from DDS by debug-primitives ingress.           |
| `Camera`                      | `MapCamera`                  | Always                   | 2D viewport navigation camera.                            |
| `TimeControl` (inherited)     | `ITimeControlGateway?`       | After `BootstrapNode`    | Non-null when factory returns a time gateway.             |

#### Methods

| Member                                                               | Description                                           |
|----------------------------------------------------------------------|-------------------------------------------------------|
| `BootstrapNode(HrotNodeConfig, NodeRole, INetworkFactory)`           | Runs 7-phase pipeline; returns `HrotNodeContext`.     |
| `Tick(float dt)`                                                     | Advances one frame (call once per application frame). |
| `Dispose()`                                                          | Disposes the DDS participant.                         |

---

### `StrideMockSubsystem` (sealed class)

```
Namespace : Hrot.StrideMock
Implements: ISubsystem, IMapCameraProvider, IWindowRegistrar
Modifier  : sealed
```

Thin adapter embedding a `StrideNodeBootstrapper` core and a `SyncFdpToStrideScript`.
All simulation logic delegates to the core; this class only handles lifecycle wiring
and Raylib/ImGui rendering calls.

#### Constructor

| Signature                                               | Description                                                         |
|---------------------------------------------------------|---------------------------------------------------------------------|
| `StrideMockSubsystem(INetworkFactory networkFactory)`   | Throws `ArgumentNullException` if `networkFactory` is null.        |

#### Properties (ISubsystem)

| Member          | Type      | Value                                |
|-----------------|-----------|--------------------------------------|
| `Name`          | `string`  | `"StrideMock"`                       |
| `TitleBarColor` | `Vector4` | Orange: `(0.8f, 0.4f, 0.1f, 1.0f)` |

#### Methods (ISubsystem)

| Member                       | Description                                                             |
|------------------------------|-------------------------------------------------------------------------|
| `Initialize(SubsystemConfig)` | Creates `HrotNodeConfig`, boots `StrideNodeBootstrapper`, starts script. |
| `Update(float deltaTime)`    | Handles camera input, advances camera, runs script, ticks core.        |
| `DrawWorld()`                | Renders fake entities (red circles) and effects (orange / yellow line). No-op in headless. |
| `DrawUI()`                   | Renders splash message overlay during non-operating states. No-op in headless. |
| `Shutdown()`                 | Disposes core, nulls references. Safe to call before Initialize.       |

#### Methods (IMapCameraProvider)

| Member                             | Description                                      |
|------------------------------------|--------------------------------------------------|
| `GetCameraView()`                  | Returns current `MapCameraView?`.                |
| `ApplyCameraView(MapCameraView)`   | Applies an external camera view (multi-panel sync). |

#### Methods (IWindowRegistrar)

| Member                               | Description                                                               |
|--------------------------------------|---------------------------------------------------------------------------|
| `RegisterWindows(WindowManager)`     | Registers ArchitectureDiagnostics, EntityInspector, EventBrowser windows. |

---

## Dependencies

### Project References

| Reference project          | Purpose                                                              |
|----------------------------|----------------------------------------------------------------------|
| `Hrot.Common`              | `SharedApplicationBootstrapper` base class, `HrotNodeBuilder`, `HrotNodeConfig` |
| `Hrot.SimHost`             | `NodeBootstrapper`, `HrotScenarioSerializerFactory`                  |
| `Fdp.Core`                 | ECS world (`EntityRepository`, `ISimulationView`), `SimTransform`, `Entity` |
| `Fdp.Presentation`         | `ISubsystem`, `IMapCameraProvider`, `FdpApplication`, `WindowManager`, panels |
| `Fdp.Toolkits`             | `ClusterSlave`, `ScenarioSerializer`, `ISubsystem`, `TogglableSimulationGroup` |
| `Hrot.IG`                  | `VisualEffectState`, `TracerTarget`, `EventToEffectSystem`, `VisualEffectCleanupSystem` |
| `Fdp.Examples.Scenarios`   | `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates` (TKB IDs 1001-2003) |

### NuGet Packages

| Package        | Version | Purpose                             |
|----------------|---------|-------------------------------------|
| `Raylib-cs`    | 7.0.2   | 2D rendering in `DrawWorld`         |
| `rlImGui-cs`   | 3.2.0   | ImGui overlay in `DrawUI`           |

### Note on Test Project Exclusion

The `<Compile Remove="Hrot.StrideMock.Tests\**" />` item in the project file excludes the
test subfolder from the library build. Tests are compiled only when the test project runner
includes the `Hrot.StrideMock.Tests` folder explicitly.

---

## Usage Examples

### Example 1: Headless Bootstrap and Per-Frame Tick

The most common test usage: bootstrap the node in headless mode, advance time, and inspect
the fake entity state produced by the ECS.

```csharp
using Hrot.Common.Infrastructure;
using Hrot.StrideMock;

// OfflineNetworkFactory provides no-op DDS stubs (available from Hrot.Editor).
var factory = new Hrot.Editor.OfflineNetworkFactory();

var config = new HrotNodeConfig
{
    Headless             = true,
    SkipAllocatorRouting = true,  // skip ID allocator DDS round-trip
    SubsystemName        = "StrideMock",
    NodeId               = 1,
    LocalTempRoot        = @"C:\Temp\FDP_Nodes\node-1",
};

var bootstrapper = new StrideNodeBootstrapper();
bootstrapper.BootstrapNode(config, StrideNodeBootstrapper.Role, factory);

var script = new SyncFdpToStrideScript(bootstrapper);
script.Start();

// Transition to OperatingLive so sync is enabled.
bootstrapper.Context.ClusterSlave.EnqueueIntentForTest(new ExecuteNodeOpIntent
{
    TransactionId = Guid.NewGuid(),
    TargetNodeId  = 0,
    Operation     = NodeOpType.CommitState,
    DomainPayload = new CommitStatePayload(ClusterState.OperatingLive),
});
bootstrapper.Tick(0f); // processes the intent in ClusterSlave.Tick()

// Spawn an entity with a position.
var entity = bootstrapper.Context.World.CreateEntity();
bootstrapper.Context.World.AddComponent(entity, new SimTransform
{
    Position = new System.Numerics.Vector3(100f, 200f, 0f),
    Rotation = System.Numerics.Quaternion.Identity,
});

// Run one frame.
const float dt = 1f / 60f;
script.Update(dt);
bootstrapper.Tick(dt);

// Inspect the fake Stride state.
foreach (var fe in script.ActiveEntities)
{
    Console.WriteLine($"Entity at ({fe.Position.X}, {fe.Position.Y}), yaw={fe.Rotation:F3} rad");
}
```

---

### Example 2: Injecting Domain Modules

When running a fuller simulation with kinematics and combat, inject the modules via the
constructor. The bootstrapper forwards them as additional ECS modules during Phase 6b.

```csharp
using CarKinem.Ground;           // GroundKinematicsModule
using Hrot.AI.Perception;       // CognitiveSpatialModule (optional)
using Hrot.Combat;              // CombatModule
using Hrot.StrideMock;

var factory = new Hrot.Editor.OfflineNetworkFactory();

var bootstrapper = new StrideNodeBootstrapper(
    kinematicsModule: new GroundKinematicsModule(),
    perceptionModule: null,         // omit if not needed
    combatModule:     new CombatModule(),
    navigationModule: null
);

var config = new HrotNodeConfig
{
    Headless             = true,
    SkipAllocatorRouting = true,
    SubsystemName        = "StrideMock",
    NodeId               = 2,
    LocalTempRoot        = @"C:\Temp\FDP_Nodes\node-2",
};

bootstrapper.BootstrapNode(config, StrideNodeBootstrapper.Role, factory);

var script = new SyncFdpToStrideScript(bootstrapper);
script.Start();

// With combat module wired, weapon fire events spawn FakeStrideEffect entries.
// Publish a WeaponFireNotification and tick to see the effect appear.
bootstrapper.Context.World.Bus.Publish(new Fdp.Toolkit.Combat.Events.WeaponFireNotification
{
    Shooter = someEntity,
    Target  = anotherEntity,
});

bootstrapper.Tick(0f);   // EventToEffectSystem sees the event, spawns VisualEffectState
script.Update(0f);       // SyncStrideEffects adds the new effect to ActiveEffects

foreach (var fx in script.ActiveEffects)
{
    Console.WriteLine($"Effect: {fx.Type}  pos=({fx.Position.X},{fx.Position.Y})  alpha={fx.Alpha:F2}");
}
```

---

### Example 3: ISubsystem Lifecycle via StrideMockSubsystem

When embedding `Hrot.StrideMock` as a subsystem inside the multi-panel application runner
(e.g. `Fdp.Examples.Runner`), use `StrideMockSubsystem` instead of the bootstrapper
directly. It handles all lifecycle delegation and Raylib drawing.

```csharp
using Fdp.Toolkit.Runner;
using Hrot.StrideMock;

// Composition root provides the network factory.
INetworkFactory networkFactory = CreateNetworkFactory(domainId: 1);

var subsystem = new StrideMockSubsystem(networkFactory);

// Initialize with a headless config (no Raylib window, no ImGui context).
var config = new SubsystemConfig
{
    DomainId      = 1,
    Headless      = true,
    OwnWindow     = false,
    NodeId        = 700,
    SubsystemName = "StrideMock",
};
subsystem.Initialize(config);

// The subsystem is now wired into the FDP application loop.
// In non-headless mode the runner calls DrawWorld / DrawUI each frame.
// In headless mode those calls are safe no-ops.

float dt = 1f / 60f;
for (int frame = 0; frame < 300; frame++)
{
    subsystem.Update(dt);
    // DrawWorld() and DrawUI() are no-ops in headless mode.
}

// Share camera state with adjacent subsystems using IMapCameraProvider.
var cameraView = subsystem.GetCameraView();
if (cameraView.HasValue)
{
    otherSubsystem.ApplyCameraView(cameraView.Value);
}

subsystem.Shutdown();
```

---

### Example 4: Registering Diagnostic Windows

When the application uses a `WindowManager` (ImGui-based multi-panel system), the subsystem
exposes three debug windows via `IWindowRegistrar.RegisterWindows`.

```csharp
using Fdp.Presentation.WindowManager;
using Hrot.StrideMock;

var windowManager = new WindowManager();
var subsystem     = new StrideMockSubsystem(networkFactory);
subsystem.Initialize(config);

// Register all StrideMock debug panels into the window manager.
subsystem.RegisterWindows(windowManager);

// The following windows are now available under the "StrideMock" group:
//   - "StrideMock Diagnostics"       (id: stridemock_architecture_diagnostics)
//   - "StrideMock Entity Inspector"  (id: stridemock_fdp_inspector)
//   - "StrideMock Event Browser"     (id: stridemock_fdp_events)
```

---

## Visual Effect Lifecycle

The pipeline from a game event to a rendered fake effect involves three layers:

```
  ECS Event Bus
  (FdpEventBus)
       |
       |  WeaponFireNotification published
       v
+------+---------------------+
|  EventToEffectSystem       |   Simulation phase (TogglableSimulationGroup)
|                            |
|  Reads WeaponFireNotif.    |
|  Spawns entity with:       |
|    SimTransform (pos)      |
|    VisualEffectState       |
|    TracerTarget            |
+------+---------------------+
       |
       |  entity lives in ECS world
       v
+------+---------------------+
|  SyncFdpToStrideScript     |   Per-frame Update()
|                            |
|  Pass 1: stale check       |
|  Pass 2: upsert FakeEffect |
|    .Type, .Position        |
|    .Scale, .Alpha          |
|    .TracerEnd (if tracer)  |
+------+---------------------+
       |
       |  FakeStrideEffect in ActiveEffects
       v
+------+---------------------+
|  StrideMockSubsystem       |   DrawWorld() (Raylib)
|  DrawWorld()               |
|                            |
|  Explosion -> orange circle|
|  Tracer    -> yellow line  |
+----------------------------+

  ... time passes (dt > TracerDurationSeconds = 0.3 s) ...

+------+---------------------+
|  VisualEffectCleanupSystem |   Post-simulation phase (TogglablePostSimGroup)
|                            |
|  Increments ElapsedTime    |
|  If elapsed > Duration:    |
|    DestroyEntity (queued)  |
+------+---------------------+
       |
       |  command buffer flushed at BeforeSync of NEXT tick
       v
  Entity removed from ECS world
  -> Pass 1 stale check removes FakeStrideEffect from ActiveEffects
```

---

## Generational Entity Safety

A key implementation detail of the two-pass sync is its handling of the ECS generational
entity handle scheme.

When an entity at slot index `N` is destroyed and a new entity is immediately created at
the same slot, the new entity gets a higher generation number. The old handle (slot N,
generation G) is now stale: `EntityRepository.IsAlive(oldHandle)` returns `false` because
the stored generation does not match.

Pass 1 exploits this in O(1) per dictionary entry without any event subscriptions or
change tracking:

```csharp
// Pass 1 - destructions
_staleEntities.Clear();
foreach (var kvp in _entities)
{
    if (!world.IsAlive(kvp.Key))   // O(1) generational check
        _staleEntities.Add(kvp.Key);
}
foreach (var stale in _staleEntities)
    _entities.Remove(stale);
```

The `_staleEntities` list is pre-allocated with capacity 64 and reused across every frame
to avoid per-frame heap allocation. This is verified by test `SC_SM004_8` via reflection.

---

## Cluster State Gate

The sync script inspects the cluster state each frame and only runs the ECS queries during
operating states. This prevents stale data from being pushed to the fake Stride layer
during scenario loading, seek operations, or replay.

```
ClusterState.OperatingLive     -> sync enabled, splash empty
ClusterState.OperatingEdit     -> sync enabled, splash empty
ClusterState.OperatingPreview  -> sync enabled, splash empty
ClusterState.OperatingReplay   -> sync enabled, splash empty

ClusterState.LoadingLive       -> sync suppressed, splash = "Cluster: LoadingLive"
ClusterState.LoadingReplay     -> sync suppressed, splash shown
ClusterState.Idle              -> sync suppressed, splash shown
(all other states)             -> sync suppressed, splash shown
```

The splash message is rendered by `StrideMockSubsystem.DrawUI` as a fixed-position
borderless ImGui window at screen position (20, 20).

---

## Best Practices

### Use StrideNodeBootstrapper Directly in Tests

`StrideMockSubsystem` is the integration adapter for the multi-panel runner. For unit and
integration tests, use `StrideNodeBootstrapper` and `SyncFdpToStrideScript` directly. This
avoids the Raylib/ImGui dependency and allows running tests without a display server.

### Always Set SkipAllocatorRouting = true in Headless Tests

When `SkipAllocatorRouting` is `false`, `StrideNodeBootstrapper` wires in a
`NetworkSpawningSystem` that expects a live DDS ID allocator server. In headless tests this
server is not available and the system will stall on the first entity creation. Set
`SkipAllocatorRouting = true` to bypass this path.

### Tick Before script.Update

`StrideNodeBootstrapper.Tick` calls `EventBus.SwapBuffers()`, which moves newly published
events into the readable buffer that ECS systems consume. If you publish an event and
immediately call `script.Update()` without ticking first, `EventToEffectSystem` will not
see the event. The correct ordering is:

```
world.Bus.Publish(someEvent);
bootstrapper.Tick(dt);    // SwapBuffers + kernel.Update + EventToEffectSystem runs
script.Update(dt);        // SyncStrideEffects sees the new VisualEffectState entities
```

### Populate TKB After BootstrapNode

The TKB database (`context.TkbDb`) is created during `BootstrapNode`. Template registration
must happen after the bootstrap completes and before any entity spawning:

```csharp
bootstrapper.BootstrapNode(config, role, factory);
var tkb = bootstrapper.Context.TkbDb;
if (tkb != null)
{
    Fdp.Examples.Common.Setup.DemoTkbSetup.RegisterAll(tkb);
    UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb);
}
```

### Do Not Reference Raylib or ImGui from StrideNodeBootstrapper

The bootstrapper comment is explicit: it must not reference `Raylib`, `ImGui`, or
`IMapCameraProvider`. All rendering concerns belong in `StrideMockSubsystem`. This
separation ensures the bootstrapper and sync script can run in headless CI pipelines.

### ApplicationSystemsRegistrar for Optional Diagnostic Systems

Rather than subclassing `StrideNodeBootstrapper` to override `RegisterApplicationSystems`,
set the `ApplicationSystemsRegistrar` property before calling `BootstrapNode`. This is
the pattern used by `StrideMockSubsystem` to wire the event history capture system:

```csharp
_core = new StrideNodeBootstrapper();
_core.ApplicationSystemsRegistrar = ctx =>
{
    ctx.Kernel.RegisterGlobalSystem(
        new EventHistoryCaptureSystem("World", _fdpEventHistory, ctx.World.Bus));
};
_core.BootstrapNode(nodeConfig, StrideNodeBootstrapper.Role, _networkFactory!);
```

### Effect Expiry Requires Two Ticks

`VisualEffectCleanupSystem` queues a `DestroyEntity` command when an effect expires. ECS
command buffers from post-simulation global systems are flushed at the `BeforeSync` phase
of the **next** tick, not the current one. After advancing time past the effect duration,
you must call `Tick` once more before `script.Update` will see the effect removed from
`ActiveEffects`. See test `SC_SM004_7` for the canonical sequence.

---

## Test Coverage Summary

The embedded test suite covers all published success conditions:

| Test class                          | Conditions | Scope                                    |
|-------------------------------------|------------|------------------------------------------|
| `SharedApplicationBootstrapperTests`| SC_SM002_x | 7-phase base class contract              |
| `StrideNodeBootstrapperTests`       | SC_SM003_x | Concrete bootstrapper contract           |
| `SyncFdpToStrideScriptTests`        | SC_SM004_x | Differential sync correctness            |
| `StrideNodeBootstrapperTests`       | SC_SM005_x | Visual effect system placement           |
| `StrideMockSubsystemTests`          | SC_SM006_x | ISubsystem adapter contract              |

Notable test techniques:
- Real `StrideNodeBootstrapper` instances run in headless mode (no test doubles for the ECS).
- `OfflineNetworkFactory` from `Hrot.Editor` provides no-op DDS stubs.
- `ClusterSlave.EnqueueIntentForTest` drives state machine transitions without a live
  orchestrator.
- Reflection is used in `SC_SM004_8` to verify the `_staleEntities` list identity across
  frames (zero GC allocation guarantee).
- `SC_SM002_5` uses reflection to assert the exact set of abstract and virtual hooks on
  `SharedApplicationBootstrapper`, preventing accidental API drift.

---

## Related Projects

| Project                    | Relationship                                                                     |
|----------------------------|----------------------------------------------------------------------------------|
| `Hrot.SimHost`             | Provides `SharedApplicationBootstrapper`, `NodeBootstrapper`, `HrotScenarioSerializerFactory`. The SimHost bootstrapper (`SimHostNodeBootstrapper`) is the parallel concrete implementation for the authoritative server node. |
| `Hrot.Common`              | Provides `HrotNodeBuilder`, `HrotNodeConfig`, `HrotNodeContext`, `NodeRole`, and shared component registries. |
| `Hrot.IG`                  | Source of `VisualEffectState`, `TracerTarget`, `EventToEffectSystem`, `VisualEffectCleanupSystem`, and `EffectType`. These components and systems are the bridge between combat simulation and visual output. |
| `Hrot.FakeStrideApp`       | A runner project that hosts `StrideMockSubsystem` in a Raylib window. Provides the `main()` entry point and application shell. Not a library; does not export reusable types. |
| `Hrot.Editor`              | Exports `OfflineNetworkFactory` used by all headless tests in this project.      |
| `Fdp.Core`                 | ECS foundation: `EntityRepository`, `ISimulationView`, `Entity`, `SimTransform`, `FdpEventBus`. |
| `Fdp.Presentation`         | `ISubsystem`, `IMapCameraProvider`, `WindowManager`, `EntityInspectorPanel`, `EventBrowserPanel`, `ArchitectureDiagnosticsWindow`. |
| `Fdp.Toolkits`             | `ClusterSlave`, `TogglableSimulationGroup`, `TogglablePostSimulationGroup`, `ScenarioSerializer`, orchestration infrastructure. |
| `Fdp.Examples.Scenarios`   | `UrbanCombatNewScenario` provides the TKB template registrations (IDs 1001-2003) consumed during `Initialize`. |

---

## Architectural Notes and Known Limitations

### Stage 2 Migration Path

The entire design anticipates a future Stage 2 migration to a real Stride engine node.
The migration path is:

1. Replace `FakeStrideEntity` with the Stride `Entity` + `TransformComponent` API.
2. Replace `FakeStrideEffect` with a Stride `ParticleSystem` or visual script entity.
3. Subclass or replace `FakeStrideScript` with Stride's `AsyncScript` or `SyncScript`.
4. Keep `StrideNodeBootstrapper`, `SyncFdpToStrideScript`, and all ECS logic unchanged.

### ScenarioSerializer Not Used

`StrideNodeBootstrapper.BuildOrchestration` passes `scenarioSerializer: null` to
`NodeBootstrapper.BuildOrchestration`. The Stride node does not load or save scenarios
directly. Scenarios arrive via network replication from the SimHost node. The serializer
is still built (Phase 3) because the `PopulateSystems` and module wiring infrastructure
depends on the behavior registry it produces.

### Deprecated Kernel.Update(float) Overload

`StrideNodeBootstrapper.Tick` calls `Context.Kernel.Update(dt)` with the legacy overload
(suppressed with `#pragma warning disable CS0618`). This is intentional and correct for
the mock context: deterministic network sync events are absent in headless/offline mode,
so the legacy path that accepts a float delta directly is appropriate. A live DDS-connected
node would use the non-deprecated zero-argument `Update()` path driven by
`SlaveSyncController`.

### DDS Gizmo Ingress (Planned)

The line `// _gizmoIngress?.PollAndApply();` in `Tick` is a placeholder for DDS-based
debug primitive ingress (SM-009). When wired, it will populate `ConsumerBuffer` from the
DDS gizmo topic so that remotely published debug shapes can be rendered by `DrawWorld`.

### Camera Input in Headless Mode

`StrideMockSubsystem.Update` gates the `Camera.HandleInput` call on both `!_headless` and
`_isActiveMapOwner()`. In headless mode the guard short-circuits before any Raylib input
polling, making the subsystem safe to run without a Raylib context.
