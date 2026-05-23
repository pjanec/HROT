# Fdp.Examples.Common

**Project path:** `FDP/Examples/Fdp.Examples.Common/Fdp.Examples.Common.csproj`
**Documented:** 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the canonical
architectural reference.

---

## Executive Overview

`Fdp.Examples.Common` is the shared-code library for the FDP simulation framework's
example projects. It contains the contracts, components, constants, events, helpers,
setup utilities, and systems that are reused across multiple concrete example
projects (`Fdp.Examples.Scenarios`, `Fdp.Examples.Runner`,
`Fdp.Examples.Scenarios.Tests`).

Key responsibilities:

- **Scenario contract** (`IScenario`) -- defines the lifecycle every CI-testable
  scenario script must implement.
- **Scenario runner** (`ScenarioSubsystem`) -- plugs a scenario into the
  `SubsystemOrchestrator` tick loop, handling time advancement, assertion
  evaluation, exit-code emission, and optional Vis2D camera provision.
- **Failure signalling** (`ScenarioFailureException`) -- a typed exception that
  carries a phase index and diagnostic string for CI log output.
- **ECS components** -- lightweight structs (`DemoScenarioTracker`,
  `MockBlackboardState`) that example scenarios attach to entities without
  pulling in heavyweight gameplay modules.
- **Canonical constants** -- scenario name strings, TKB template IDs, and AI
  behavior IDs that all example projects share.
- **Event types** -- two ECS bus event structs (`DemoScenarioTriggerEvent`,
  `DemoTestLogEvent`) for injecting world-state changes and logging from
  within scenario scripts.
- **Helpers** -- a road graph factory (`DemoRoadGraphFactory`) and a
  deterministic terrain stub (`MockTerrainProvider`) for headless test scenarios.
- **Setup utilities** -- `DemoTkbSetup` registers TKB entity templates needed
  by the DistributedTank scenario family.
- **Systems** -- `TransformSyncSystem` keeps `SimTransform` and
  `NetworkTransform` in sync for both owned and remote entities, with optional
  ground-clamping Z-offset smoothing.

---

## Architecture

### Layered View

The library sits between the FDP engine packages and the concrete scenario
projects that use it.

```
+====================================================================+
|                     Concrete Example Projects                      |
|  Fdp.Examples.Scenarios  |  Fdp.Examples.Runner  |  .Scenarios.Tests |
+====================================================================+
           |                        |                    |
           v                        v                    v
+====================================================================+
|                    Fdp.Examples.Common                             |
|                                                                    |
|  IScenario              ScenarioSubsystem   ScenarioFailureExcep. |
|  Components/            Constants/          Events/               |
|  Helpers/               Setup/              Systems/              |
+====================================================================+
           |                        |                    |
           v                        v                    v
+=======================+ +======================+ +=================+
|    Fdp.ModuleHost     | |   Fdp.Toolkits       | | Fdp.Presentation|
| EntityRepository      | | Fdp.Toolkit.Runner   | | MapCanvas       |
| ModuleHostKernel      | | Fdp.Toolkit.Time     | | MapCamera       |
| EventAccumulator      | | Fdp.Toolkit.Vis2D    | |                 |
| SubsystemOrchestrator | | Fdp.Toolkit.Tkb      | |                 |
| ISubsystem            | | Fdp.Toolkit.Repl.    | |                 |
+=======================+ +======================+ +=================+
           |
           v
+=======================+
|      Fdp.Core         |
| SimTransform          |
| GlobalTime            |
| FixedString32         |
+=======================+
```

### Scenario Execution Flow

The central runtime pattern is `ScenarioSubsystem` orchestrating an `IScenario`
implementation through the `SubsystemOrchestrator` tick loop.

```
+-------------------+          +----------------------+
| Application Entry |          |  ScenarioSubsystem   |
| (Runner / Test)   |          |  Initialize()        |
+-------------------+          |  - new EntityRepo    |
        |                      |  - new ModuleHostKernel
        | new ScenarioSubsystem|  - SetTimeController |
        +--------------------->|  - scenario.Configure|
                               |  - kernel.Initialize |
                               +----------+-----------+
                                          |
               +--- tick loop (SubsystemOrchestrator.Run) ---+
               |                                             |
               v                                             |
    +----------+------------------+                          |
    |  ScenarioSubsystem.Update() |                          |
    |  1. Advance GlobalTime      |                          |
    |     (SteppingTimeController)|                          |
    |  2. scenario.EvaluateTick() |<-- inject events, check  |
    |     returns bool (success)  |    assertions            |
    |     throws ScenarioFailure  |    Exception on fail     |
    |  3. kernel.Update()         |                          |
    |     (all module systems run)|                          |
    |  4. Check success / timeout |                          |
    |     ExitWith(0/1/2)         |                          |
    +-----------------------------+                          |
               |  <----------------------------------------------+
               | Shutdown()
               |  kernel.Dispose()
               |  scenario.OnShutdown()
               |  world.Dispose()
               v
    +----------+------------------+
    |  Exit code:                 |
    |   0 = CI success            |
    |   1 = assertion failed      |
    |   2 = timed out             |
    +-----------------------------+
```

### Component / Event Data Flow

```
+------------------------------+       +-----------------------------+
|  IScenario.EvaluateTick()    |       |  DemoScenarioTracker (ECS)  |
|  - reads CurrentPhase,       |       |  - CurrentPhase : int       |
|    TicksInPhase, LatchMask   +------>|  - TicksInPhase : uint      |
|    from entity component     |       |  - LatchMask    : int (32b) |
+------------------------------+       +-----------------------------+

+------------------------------+       +-----------------------------+
|  IScenario.EvaluateTick()    |       |  DemoScenarioTriggerEvent   |
|  - publishes trigger events  +------>|  - TriggerType  : byte      |
|    on the ECS event bus      |       |  - TargetEntityIndex : int  |
+------------------------------+       +-----------------------------+

+------------------------------+       +-----------------------------+
|  ScenarioSubsystem.Update()  |       |  DemoTestLogEvent           |
|  - publishes log events on   +------>|  - ScenarioName : Fixed32   |
|    success / phase change    |       |  - PhaseId      : int       |
|                              |       |  - IsSuccess    : bool      |
+------------------------------+       +-----------------------------+
```

---

## Source Structure

```
Fdp.Examples.Common/
|-- IScenario.cs                         namespace Fdp.Examples.Common
|-- ScenarioFailureException.cs          namespace Fdp.Examples.Common
|-- ScenarioSubsystem.cs                 namespace Fdp.Examples.Common
|
+-- Components/
|   |-- DemoScenarioTracker.cs           namespace Fdp.Examples.Common.Components
|   +-- MockBlackboardState.cs           namespace Fdp.Examples.Common.Components
|
+-- Constants/
|   |-- DemoBehaviorIds.cs               namespace Fdp.Examples.Common.Constants
|   |-- DemoTemplateIds.cs               namespace Fdp.Examples.Common.Constants
|   +-- ScenarioNames.cs                 namespace Fdp.Examples.Common.Constants
|
+-- Events/
|   |-- DemoScenarioTriggerEvent.cs      namespace Fdp.Examples.Common.Events
|   +-- DemoTestLogEvent.cs              namespace Fdp.Examples.Common.Events
|
+-- Helpers/
|   |-- DemoRoadGraphFactory.cs          namespace Fdp.Examples.Common.Helpers
|   +-- MockTerrainProvider.cs           namespace Fdp.Examples.Common.Helpers
|
+-- Setup/
|   +-- DemoTkbSetup.cs                  namespace Fdp.Examples.Common.Setup
|
+-- Systems/
    +-- TransformSyncSystem.cs           namespace Fdp.Examples.Common.Systems
```

---

## Public API Reference

### `Fdp.Examples.Common` (root namespace)

---

#### `interface IScenario`

Contract for all CI-testable scenario scripts. Implementations must be
deterministic and must not reference Raylib or wall-clock time.

| Member | Signature | Description |
|---|---|---|
| `ScenarioName` | `string ScenarioName { get; }` | Unique key used by the CLI `--scenario` flag. |
| `Configure` | `void Configure(EntityRepository world, ModuleHostKernel kernel)` | Called once at startup; register modules and spawn entities here. |
| `EvaluateTick` | `bool EvaluateTick(uint currentTick, EntityRepository world)` | Called every tick before `kernel.Update()`. Return `true` to signal CI success; throw `ScenarioFailureException` on failure. |
| `ConfigureVisuals` | `void ConfigureVisuals(MapCanvas? canvas, EntityRepository world)` | Optional Vis2D registration; called only when `--attach-vis2d` is set. |
| `OnShutdown` | `void OnShutdown()` | Default no-op. Override to release unmanaged resources after the kernel is disposed but before the world is disposed. |

---

#### `sealed class ScenarioSubsystem : ISubsystem, IMapCameraProvider`

Wraps an `IScenario` as an `ISubsystem` for use with `SubsystemOrchestrator`.

**Constructor:**

```csharp
ScenarioSubsystem(
    IScenario scenario,
    int maxTicks,
    Action<int>? exitCallback = null,
    float fixedDeltaSeconds = 1.0f / 60.0f)
```

| Parameter | Description |
|---|---|
| `scenario` | The `IScenario` implementation to execute. |
| `maxTicks` | Maximum ticks before a timeout exit-code 2 is emitted. |
| `exitCallback` | Called with exit code (0/1/2). Pass `null` to use `Environment.Exit`. |
| `fixedDeltaSeconds` | Fixed simulation step in seconds (default 1/60 s). |

**Public members:**

| Member | Signature | Description |
|---|---|---|
| `Name` | `string Name { get; }` | Returns `"ScenarioSubsystem[<ScenarioName>]"`. |
| `TitleBarColor` | `Vector4 TitleBarColor { get; }` | Green tint `(0.2, 0.7, 0.3, 1.0)` for the orchestrator UI panel. |
| `AttachOrchestrator` | `void AttachOrchestrator(SubsystemOrchestrator orchestrator)` | Must be called before `SubsystemOrchestrator.Run()` so the subsystem can stop the loop. |
| `Initialize` | `void Initialize(SubsystemConfig config)` | Creates `EntityRepository`, `ModuleHostKernel`, `SteppingTimeController`; calls `scenario.Configure`; optionally builds `MapCanvas`. |
| `Update` | `void Update(float deltaTime)` | Tick: advance time, evaluate scenario, update kernel, check exit. |
| `DrawWorld` | `void DrawWorld()` | No-op. |
| `DrawUI` | `void DrawUI()` | No-op. |
| `Shutdown` | `void Shutdown()` | Disposes kernel, calls `scenario.OnShutdown()`, disposes world. |
| `GetCameraView` | `MapCameraView? GetCameraView()` | `IMapCameraProvider` implementation. |
| `ApplyCameraView` | `void ApplyCameraView(MapCameraView view)` | `IMapCameraProvider` implementation. |
| `GetMapCamera` | `MapCamera? GetMapCamera()` | Non-interface helper, kept for backward compatibility. |

---

#### `sealed class ScenarioFailureException : Exception`

Thrown by `IScenario.EvaluateTick` to signal a deterministic assertion failure.

| Member | Signature | Description |
|---|---|---|
| `PhaseId` | `int PhaseId { get; }` | Phase number in which the failure occurred. |
| `Diagnostics` | `string Diagnostics { get; }` | Human-readable diagnostic string, e.g. `"Y=0.1 expected >2.0"`. |
| Constructor | `ScenarioFailureException(int phaseId, string message)` | `message` is forwarded to `Exception.Message` and to `Diagnostics`. |

---

### `Fdp.Examples.Common.Components`

---

#### `struct DemoScenarioTracker`

ECS component attached to a "Scenario Master" entity. Tracks phase progression
and boolean latches in deterministic scenario execution.

| Field | Type | Description |
|---|---|---|
| `CurrentPhase` | `int` | Current scenario phase index (0-based). |
| `TicksInPhase` | `uint` | Ticks elapsed within the current phase. |
| `LatchMask` | `int` | Bit flags for up to 32 sequential boolean latches. Bit N is set when latch N fires. |

---

#### `unsafe struct MockBlackboardState`

Overlay struct representing cognitive blackboard memory state in test scenarios,
without requiring a full `BrainBlackboard` dependency.

| Field | Type | Description |
|---|---|---|
| `ThreatVisible` | `bool` | Whether a threat is currently visible to this entity. |
| `AmmoCount` | `int` | Current ammo count available for combat. |
| `CurrentRoE` | `byte` | Rules of Engagement: 0 = hold fire, 1 = weapons free, 2 = weapons tight. |

---

### `Fdp.Examples.Common.Constants`

---

#### `static class DemoBehaviorIds`

Behavior hash constants for AI entities in demo scenarios.

| Constant | Value | Description |
|---|---|---|
| `Patrol` | `100u` | Patrol behavior hash. |
| `Combat` | `200u` | Combat behavior hash. |
| `Ambush` | `300u` | Ambush behavior hash. |
| `ConvoyEscort` | `400u` | Convoy escort behavior hash. |
| `WanderCivil` | `500u` | Civilian wander behavior hash. |

---

#### `static class DemoTemplateIds`

TKB integer entity type IDs used by demo scenarios.

| Constant | Value | Description |
|---|---|---|
| `CivilianPedestrian` | `1001` | Civilian pedestrian entity type. |
| `CivilianCar` | `1002` | Civilian car entity type. |
| `MilitaryApc` | `2001` | Military APC entity type. |
| `InfantrySoldier` | `2002` | Infantry soldier entity type. |
| `Insurgent` | `2003` | Insurgent entity type. |
| `CommandTank` | `100` | DistributedTank hull node. |
| `TankTurret` | `101` | DistributedTank turret child node (Brain-only, not TKB-registered). |

---

#### `static class ScenarioNames`

String constants for all registered scenario keys; used by the CLI `--scenario`
flag and by `ScenarioRegistry`.

| Constant | Value |
|---|---|
| `AutoDrive` | `"autodrive"` |
| `ComponentDamage` | `"componentdamage"` |
| `BallisticsAndHit` | `"ballisticsandhit"` |
| `BehaviorValidation` | `"behaviorvalidation"` |
| `SensorGrid` | `"sensorgrid"` |
| `MissionCommand` | `"missioncommand"` |
| `TerrainClamping` | `"terrainclamping"` |
| `ParallelEpisodes` | `"parallelepisodes"` |
| `DistributedTank` | `"distributedtank"` |
| `UrbanCombat` | `"urbancombat"` |

---

### `Fdp.Examples.Common.Events`

---

#### `struct DemoScenarioTriggerEvent`

Event injected by scenario scripts to simulate external world-state changes
(spawning an ambush, forcing hold-fire) without depending on real AI or network
triggers.

| Field | Type | Description |
|---|---|---|
| `TriggerType` | `byte` | Kind of trigger: `1` = ForceHoldFire, `2` = SpawnAmbush. |
| `TargetEntityIndex` | `int` | Entity array index of the target entity affected by this trigger. |

---

#### `struct DemoTestLogEvent`

Synthetic logging event fired to the ECS event bus during scenario execution to
record phase transitions and assertion checkpoints.

| Field | Type | Description |
|---|---|---|
| `ScenarioName` | `FixedString32` | Scenario identifier (matches `IScenario.ScenarioName`). |
| `PhaseId` | `int` | Phase identifier at the time the event was raised. |
| `IsSuccess` | `bool` | True when the event marks a successful phase completion. |

---

### `Fdp.Examples.Common.Helpers`

---

#### `static class DemoRoadGraphFactory`

Builds a minimal 4-way city intersection `RoadNetworkBlob` for use in
deterministic offline test scenarios that exercise `CarKinematicsSystem`.

The graph has **5 nodes** (Centre + 4 cardinal endpoints) and **8 directed
segments** (one inbound + one outbound per arm).

Node layout:

```
          [1] North (0, 100)
               |
               | segs 0,1
               |
[4] West ------+------ [3] East
(-100,0)  segs |  (100, 0)
          6,7  |  segs 4,5
               |
               | segs 2,3
               |
          [2] South (0, -100)
```

| Member | Signature | Description |
|---|---|---|
| `CreateCityIntersection` | `static RoadNetworkBlob CreateCityIntersection()` | Creates the 4-way intersection blob. Caller is responsible for calling `Dispose()` on the returned blob. |

Private constants:

| Constant | Value | Purpose |
|---|---|---|
| `EndpointDistance` | `100f` | Distance from centre to each cardinal endpoint (m). |
| `GridCellSize` | `20f` | Spatial-hash cell size for the road grid. |
| `GridWidth` | `20` | Spatial-hash grid width (cells). |
| `GridHeight` | `20` | Spatial-hash grid height (cells). |

---

#### `sealed class MockTerrainProvider : ITerrainProvider`

Deterministic terrain stub for offline test scenarios. Returns a piecewise
height profile based on the query X-coordinate, producing bit-identical results
on all hardware.

Height profile:

```
Z
100 |                   *
    |                  * *
    |                 *   *
  12|           ......
    |          .
    |         .
    |        .
  0 |........___________
    +------+---+---+---+---> X
    0     20  40  60  80
         ramp  spike
```

Zones:
- `0 -- 20 m`: Z = 0 (flat)
- `20 -- 80 m`: Z = (x - 20) * 0.2 (linear ramp, slope 0.2 m/m)
- `x ~= 40 m` (within 0.5 m tolerance): Z = 100 (spike / bad-raycast anomaly)

| Member | Signature | Description |
|---|---|---|
| `QueryBatch` | `void QueryBatch(NativeArray<TerrainQueryRequest>, int count, NativeArray<TerrainQueryResult>)` | Fills `results[0..count-1]` with `{HitZ, HasHit=true}` for each request. |

---

### `Fdp.Examples.Common.Setup`

---

#### `static class DemoTkbSetup`

TKB blueprint registration for the DistributedTank scenario entity types.
Only registers the Muscle-side `CommandTank` template (ID 100). The `TankTurret`
(ID 101) is Brain-only and never ghost-promoted on the Muscle node.

| Member | Signature | Description |
|---|---|---|
| `RegisterAll` | `static void RegisterAll(ITkbDatabase tkb)` | Registers all DistributedTank templates. Idempotent: skips if `CommandTank` is already registered. Call once before `ModuleHostKernel.Initialize()`. |

`CommandTank` blueprint parameters (registered via `VehicleParametersDto`):

| Parameter | Value |
|---|---|
| Length | 7.0 m |
| Width | 3.5 m |
| MaxSpeedFwd | 12.0 m/s |
| MaxSpeedRev | 8.0 m/s |
| MaxAccel | 2.0 m/s^2 |

---

### `Fdp.Examples.Common.Systems`

---

#### `class TransformSyncSystem : IEcsModuleSystem` `[UpdateInPhase(SystemPhase.PostSimulation)]`

Synchronizes `SimTransform` with `NetworkTransform` for entities that have
network authority. Originally defined in `Fdp.Examples.NetworkDemo.Systems`;
duplicated here so that `Fdp.Examples.Scenarios` and other projects avoid a
dependency on the full NetworkDemo project.

**Constructor:**

```csharp
TransformSyncSystem(
    bool driveFromNetwork = false,
    float groundClampZSmoothingRate = 5f)
```

| Parameter | Description |
|---|---|
| `driveFromNetwork` | When `true`, treat all entities as remote-driven (used in replay mode). |
| `groundClampZSmoothingRate` | Lerp multiplier for `GroundClampingState.CurrentZOffset` convergence each frame. Lower for demos that assert mid-convergence (e.g. DEM1-D007). |

**Behavior:**

- **Owned entities** (`PrimaryOwnerId == LocalNodeId`): copies `SimTransform`
  position and rotation into `NetworkTransform.LastPosition/LastRotation`.
- **Remote entities** (or all when `driveFromNetwork = true`): smoothly lerps
  `SimTransform.Position` toward `NetworkTransform.LastPosition` at
  `SMOOTHING_RATE (10.0) * deltaTime` per frame.
- **Ground clamping:** when `GroundClampingState` is present on a remote entity,
  also lerps `CurrentZOffset` toward `TargetZOffset` and adjusts the smoothed
  Z position accordingly. The visual correction does not feed back into the
  dead-reckoning calculation.
- Query includes `EntityLifecycle.All` -- constructing entities are included so
  that `NetworkTransform` tracks movement even before peer ACKs are received.

| Member | Signature | Description |
|---|---|---|
| `Execute` | `void Execute(ISimulationView view, float deltaTime)` | Dispatches to `SyncOwnedEntities` and/or `SyncRemoteEntities` depending on mode. |

---

## Dependencies

### Project References

| Referenced Project | Purpose |
|---|---|
| `Fdp.ModuleHost` | `EntityRepository`, `ModuleHostKernel`, `EventAccumulator`, `ISubsystem`, `SubsystemOrchestrator`, `SubsystemConfig` |
| `Fdp.Toolkits` | `ITerrainProvider`, `SteppingTimeController`, `GlobalTime`, `Fdp.Toolkit.Runner`, `Fdp.Toolkit.Vis2D`, `Fdp.Toolkit.Tkb`, `Fdp.Toolkit.Replication` |
| `Fdp.Presentation` | `MapCanvas`, `MapCamera`, `MapCameraView`, `IMapCameraProvider` |

### NuGet Packages

No direct NuGet package references. All third-party dependencies are brought in
transitively through `Fdp.ModuleHost`, `Fdp.Toolkits`, and `Fdp.Presentation`.

### Build Configuration

| Property | Value |
|---|---|
| `TargetFramework` | `net8.0` |
| `AllowUnsafeBlocks` | `true` (required by `MockBlackboardState`) |
| `Nullable` | `enable` |
| `ImplicitUsings` | `enable` |

---

## Usage Examples

### Example 1: Implementing a minimal CI scenario

A scenario must implement `IScenario`, register its name in `ScenarioNames`, and
throw `ScenarioFailureException` when a deterministic assertion fails.

```csharp
using Fdp.Core;
using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;
using Fdp.ModuleHost;
using Fdp.Toolkit.Vis2D;

namespace Fdp.Examples.Scenarios
{
    public sealed class AutoDriveScenario : IScenario
    {
        public string ScenarioName => ScenarioNames.AutoDrive;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // Register required modules.
            kernel.AddModule(new CarKinematicsModule());
            kernel.AddModule(new GeographicModule());

            // Spawn the car entity and attach the demo tracker.
            var car = world.CreateEntity();
            world.AddComponent(car, new SimTransform { Position = new(0, 0, 0) });
            world.AddComponent(car, new DemoScenarioTracker { CurrentPhase = 0 });
        }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            ref var tracker = ref world.GetSingletonRef<DemoScenarioTracker>();

            if (tracker.CurrentPhase == 0 && currentTick >= 60)
            {
                // Assert the car has moved more than 2 m after 60 ticks.
                var tf = world.GetSingleton<SimTransform>();
                if (tf.Position.X < 2.0f)
                    throw new ScenarioFailureException(0,
                        $"X={tf.Position.X:F2} expected >2.0 after {currentTick} ticks");

                tracker.CurrentPhase = 1;
            }

            return tracker.CurrentPhase >= 1;
        }

        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
    }
}
```

---

### Example 2: Wiring a scenario into the runner

`ScenarioSubsystem` is constructed around the scenario and attached to the
orchestrator. The `exitCallback` parameter allows tests to capture the exit
code instead of calling `Environment.Exit`.

```csharp
using Fdp.Examples.Common;
using Fdp.Toolkit.Runner;

namespace Fdp.Examples.Runner
{
    internal static class ScenarioRunner
    {
        public static int Run(IScenario scenario, int maxTicks = 1000)
        {
            int capturedCode = -1;

            var subsystem = new ScenarioSubsystem(
                scenario,
                maxTicks,
                exitCallback: code => capturedCode = code,
                fixedDeltaSeconds: 1.0f / 60.0f);

            var orchestrator = new SubsystemOrchestrator();
            subsystem.AttachOrchestrator(orchestrator);
            orchestrator.AddSubsystem(subsystem);

            orchestrator.Run(new SubsystemConfig
            {
                Deterministic        = true,
                FixedDeltaSeconds    = 1.0f / 60.0f,
                Headless             = true,
            });

            return capturedCode; // 0 = success, 1 = failure, 2 = timeout
        }
    }
}
```

---

### Example 3: Using the terrain stub in a test scenario

`MockTerrainProvider` provides a deterministic, no-native-library terrain surface
for scenarios that exercise ground-clamping logic.

```csharp
using Fdp.Examples.Common;
using Fdp.Examples.Common.Helpers;
using Fdp.Examples.Common.Constants;
using Fdp.ModuleHost;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;

namespace Fdp.Examples.Scenarios
{
    public sealed class TerrainClampingScenario : IScenario
    {
        public string ScenarioName => ScenarioNames.TerrainClamping;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // Inject the mock terrain provider as a singleton so that
            // GeographicModule.GroundClampingSystem uses it.
            var terrainProvider = new MockTerrainProvider();
            world.SetSingleton<ITerrainProvider>(terrainProvider);

            kernel.AddModule(new GeographicModule());

            var unit = world.CreateEntity();
            world.AddComponent(unit, new SimTransform { Position = new(30f, 0f, 0f) });
            world.AddComponent(unit, new GroundClampingState());
        }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            // After 10 ticks, the unit at X=30 should be clamped to Z~=2.0
            // ((30 - 20) * 0.2 = 2.0 per the MockTerrainProvider ramp).
            if (currentTick < 10) return false;

            var tf = world.GetSingleton<SimTransform>();
            float expectedZ = (30f - 20f) * 0.2f; // = 2.0
            if (MathF.Abs(tf.Position.Z - expectedZ) > 0.05f)
                throw new ScenarioFailureException(0,
                    $"Z={tf.Position.Z:F3} expected ~{expectedZ:F3}");

            return true;
        }

        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
    }
}
```

---

### Example 4: Building a road network for urban-combat tests

`DemoRoadGraphFactory` eliminates the boilerplate of constructing a test road
graph by hand.

```csharp
using CarKinem.Road;
using Fdp.Examples.Common.Helpers;

namespace Fdp.Examples.Scenarios
{
    internal static class UrbanCombatSetup
    {
        public static RoadNetworkBlob CreateTestRoads()
        {
            // Returns a 4-way intersection: 5 nodes, 8 segments.
            // Node 0 = Centre (0,0), 1=North, 2=South, 3=East, 4=West.
            return DemoRoadGraphFactory.CreateCityIntersection();
        }
    }
}
```

The caller must dispose the returned blob when the scenario shuts down:

```csharp
public void OnShutdown()
{
    _roadGraph.Dispose();
}
```

---

### Example 5: Registering TKB templates for a distributed entity scenario

```csharp
using Fdp.Examples.Common.Setup;
using Fdp.Toolkit.Tkb;

namespace Fdp.Examples.Scenarios
{
    public sealed class DistributedTankScenario : IScenario
    {
        private ITkbDatabase _tkb = null!;

        public string ScenarioName => ScenarioNames.DistributedTank;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            _tkb = new TkbDatabase();

            // Register CommandTank (ID 100) blueprint before kernel init.
            DemoTkbSetup.RegisterAll(_tkb);

            var replication = new ReplicationLogicModule(entityMap, _tkb, lifecycle);
            kernel.AddModule(replication);
        }

        public bool EvaluateTick(uint currentTick, EntityRepository world) => false;
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
    }
}
```

---

## Best Practices

### Do: Keep scenarios deterministic

`IScenario` implementations must be fully deterministic. Do not call
`DateTime.Now`, `Random.Shared`, or `Environment.TickCount` from within
`Configure` or `EvaluateTick`. Use the `SteppingTimeController`-managed
`GlobalTime` singleton instead.

### Do: Inject assertions in EvaluateTick, not in systems

Assertions belong in `EvaluateTick`, not in ECS systems or module callbacks.
Placing assertions in a system makes CI failure messages harder to attribute to
a scenario phase and creates hidden coupling to module internals.

### Do: Use ScenarioFailureException with a meaningful diagnostic

Include the actual vs. expected values in the diagnostic string so CI logs are
immediately actionable without attaching a debugger:

```csharp
throw new ScenarioFailureException(phaseId,
    $"Velocity.X={vel.X:F3} expected >={minVel:F3} at tick={tick}");
```

### Do: Call AttachOrchestrator before Run

`ScenarioSubsystem.ExitWith` calls `_orchestrator?.Stop()` before the exit
callback. Without calling `AttachOrchestrator`, the orchestrator loop will
keep running after the scenario completes.

### Do not: Throw from OnShutdown

`OnShutdown` is called after the kernel is disposed. Exceptions thrown here are
not caught by `ScenarioSubsystem`. If resource release may fail, guard it with a
try/catch and log via `FdpLog`.

### Do: Dispose RoadNetworkBlob in OnShutdown

`DemoRoadGraphFactory.CreateCityIntersection()` allocates native memory.
Always dispose the returned blob in `IScenario.OnShutdown`:

```csharp
public void OnShutdown() => _roadGraph.Dispose();
```

### Do not: Add Raylib or wall-clock dependencies to IScenario implementations

Scenarios are designed for headless CI execution. Any Raylib or wall-clock
dependency breaks the headless path and makes scenarios non-deterministic.
Visual setup belongs exclusively in `ConfigureVisuals`.

### Do: Use DemoScenarioTracker for multi-phase scripts

Attach `DemoScenarioTracker` to a singleton entity to track phase and latch
state persistently across ticks without allocating managed objects per tick.
The 32-bit `LatchMask` supports up to 32 one-shot events per phase.

### Do not: Modify MockTerrainProvider for production use

`MockTerrainProvider` is intentionally simplified (piecewise constant + ramp)
so that test assertions can predict exact Z values analytically. It must not
be used with real terrain data or in production builds.

### Do: Keep ScenarioNames in sync with ScenarioRegistry

Every key in `ScenarioNames` must correspond to a registered entry in the
concrete `ScenarioRegistry` in `Fdp.Examples.Scenarios`. Adding a constant here
without a matching registry entry will cause a silent `--scenario` resolution
failure at runtime.

---

## Related Projects

The following projects reference `Fdp.Examples.Common` directly:

### `Fdp.Examples.Scenarios`

The main collection of CI scenario implementations. Each class implements
`IScenario`, uses `ScenarioNames` constants, attaches `DemoScenarioTracker` to
entities, and fires `DemoScenarioTriggerEvent` or `DemoTestLogEvent` as needed.

### `Fdp.Examples.Runner`

The headless command-line runner. Constructs a `ScenarioSubsystem` around the
chosen scenario, wires it to a `SubsystemOrchestrator`, and starts the tick loop.
The `--scenario` flag value is matched against `ScenarioNames` constants.

### `Fdp.Examples.Scenarios.Tests`

The xUnit test project that runs scenarios in-process. Uses `ScenarioSubsystem`
with a custom `exitCallback` to capture the exit code as a test assertion rather
than calling `Environment.Exit`.

### Indirect consumers

- `Fdp.Examples.CarKinem` -- uses `DemoRoadGraphFactory` and `ScenarioNames.AutoDrive`.
- `Fdp.Examples.UrbanCombat` -- uses `DemoRoadGraphFactory` for environment setup.
- `Fdp.Examples.NetworkDemo` -- uses `TransformSyncSystem` (its own copy) and the
  shared event types.
- `Fdp.Examples.DER` -- uses `DemoTemplateIds` and `DemoBehaviorIds` for entity
  configuration.
- `Fdp.Examples.DDS` -- uses `MockTerrainProvider` in headless DDS integration tests.

---

## Module Interaction Diagram

The diagram below shows how the three concrete dependent projects interact with
`Fdp.Examples.Common` types at runtime.

```
+======================+   +===========================+   +======================+
|  Fdp.Examples.Runner |   | Fdp.Examples.Scenarios    |   | .Scenarios.Tests     |
|                      |   |                            |   |                      |
| ScenarioSubsystem    |   | AutoDriveScenario          |   | [Fact] RunAutoDrive  |
|   .ctor(scenario,    |   | BehaviorValidationScenario |   |   new ScenarioSubsys |
|    maxTicks,         |   | TerrainClampingScenario    |   |   exitCallback:      |
|    exitCallback:     |   | DistributedTankScenario    |   |   code => Assert.    |
|    Environment.Exit) |   | UrbanCombatScenario        |   |   Equal(0, code)     |
|                      |   | ...                        |   |                      |
| AttachOrchestrator() |   |                            |   |                      |
| orchestrator.Run()   |   | : IScenario                |   |                      |
+======================+   +===========================+   +======================+
           |                           |                            |
           |       (shared types)      |                            |
           +----------+----------------+----------------------------+
                      |
                      v
+=====================================================+
|                Fdp.Examples.Common                  |
|                                                     |
|  IScenario (contract)                               |
|  ScenarioSubsystem (runner)                         |
|  ScenarioFailureException (failure signal)          |
|  DemoScenarioTracker (ECS state component)          |
|  MockBlackboardState (ECS AI stub component)        |
|  DemoBehaviorIds (behavior hash constants)          |
|  DemoTemplateIds (TKB type ID constants)            |
|  ScenarioNames (CLI key constants)                  |
|  DemoScenarioTriggerEvent (event bus injection)     |
|  DemoTestLogEvent (CI checkpoint event)             |
|  DemoRoadGraphFactory (road network builder)        |
|  MockTerrainProvider (deterministic terrain stub)   |
|  DemoTkbSetup (TKB template registration)           |
|  TransformSyncSystem (network transform sync)       |
+=====================================================+
```

---

## Appendix: File-by-File Summary

| File | Type | Namespace |
|---|---|---|
| `IScenario.cs` | `interface IScenario` | `Fdp.Examples.Common` |
| `ScenarioFailureException.cs` | `sealed class` | `Fdp.Examples.Common` |
| `ScenarioSubsystem.cs` | `sealed class` | `Fdp.Examples.Common` |
| `Components/DemoScenarioTracker.cs` | `struct` | `Fdp.Examples.Common.Components` |
| `Components/MockBlackboardState.cs` | `unsafe struct` | `Fdp.Examples.Common.Components` |
| `Constants/DemoBehaviorIds.cs` | `static class` | `Fdp.Examples.Common.Constants` |
| `Constants/DemoTemplateIds.cs` | `static class` | `Fdp.Examples.Common.Constants` |
| `Constants/ScenarioNames.cs` | `static class` | `Fdp.Examples.Common.Constants` |
| `Events/DemoScenarioTriggerEvent.cs` | `struct` | `Fdp.Examples.Common.Events` |
| `Events/DemoTestLogEvent.cs` | `struct` | `Fdp.Examples.Common.Events` |
| `Helpers/DemoRoadGraphFactory.cs` | `static class` | `Fdp.Examples.Common.Helpers` |
| `Helpers/MockTerrainProvider.cs` | `sealed class` | `Fdp.Examples.Common.Helpers` |
| `Setup/DemoTkbSetup.cs` | `static class` | `Fdp.Examples.Common.Setup` |
| `Systems/TransformSyncSystem.cs` | `class` | `Fdp.Examples.Common.Systems` |
