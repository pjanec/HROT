# Fdp.Examples.Scenarios

| Field | Value |
|---|---|
| **Project path** | `FDP/Examples/Fdp.Examples.Scenarios/Fdp.Examples.Scenarios.csproj` |
| **Output type** | Class library (no executable entry point) |
| **Target framework** | net8.0 |
| **Date documented** | 2026-05-23 |

## README Validation

**Missing** — No README.md exists in the project folder. This document serves as the
primary reference.

---

## Executive Overview

`Fdp.Examples.Scenarios` is the **scenario library** that contains every concrete
`IScenario` implementation used by `fdp-demo-runner`. Each scenario is a self-contained
demonstration of one or more FDP toolkit subsystems.

Scenarios are organized by the subsystem they primarily exercise:

| Folder | Subsystem | Scenarios |
|---|---|---|
| `Cognitive/` | Behavior trees, HSMs, mission command | `BehaviorValidationScenario`, `MissionCommandScenario` |
| `Integrated/` | Full-stack multi-system demos | `UrbanCombatNewScenario`, `UrbanCombatValidator` |
| `Kinematics/` | Vehicle kinematics, component damage | `AutoDriveScenario`, `ComponentDamageScenario` |
| `Network/` | Distributed simulation, DDS replication | `DistributedTankScenario` |
| `Perception/` | Sensor grids, terrain clamping | `SensorGridScenario`, `TerrainClampingScenario` |
| `Physics/` | CCD ballistics, hit detection | `BallisticsAndHitScenario` |
| `Replay/` | Recording and deterministic replay | `ParallelEpisodesScenario` |

Each scenario implements `IScenario` and is verified by a phase-gated assertion loop:
`Configure()` builds the ECS world, and `EvaluateTick()` returns `true` when all phases
pass. Failure throws `ScenarioFailureException` with a descriptive phase number.

### Key learning objectives

1. **IScenario contract** — how to register components, events, systems, and entities in a
   single `Configure()` call that runs before the simulation loop starts.
2. **Phase-gated testing** — verifiable milestones at specific tick counts, not just
   "did it not crash".
3. **System execution order** — several scenarios explicitly document why their system
   pipeline order is mandatory for correctness.
4. **ECB (EntityCommandBuffer) flushing** — how deferred structural mutations are applied
   after a tick completes.
5. **Module ownership of NativeArrays** — lifetime management of unmanaged memory across
   scenario setup and teardown.

---

## Architecture

### IScenario Contract

```
+--------------------------------------------------------------+
|  IScenario                                                   |
|  +----------------------------------------------------------+|
|  | string ScenarioName { get; }                              ||
|  | void   Configure(EntityRepository, ModuleHostKernel)      ||
|  | bool   EvaluateTick(uint tick, EntityRepository)          ||
|  | void   ConfigureVisuals(MapCanvas?, EntityRepository)     ||
|  | void   OnShutdown()        // optional lifecycle hook     ||
|  +----------------------------------------------------------+|
+--------------------------------------------------------------+
         ^
         | implements
+--------+----------+   +-------------------+   +------------------+
| AutoDriveScenario |   | BallisticsAndHit  |   | ParallelEpisodes |
|                   |   | Scenario          |   | Scenario         |
+-------------------+   +-------------------+   +------------------+
```

### Scenario Lifecycle in the Runner

```
+-------------------+        +-------------------+        +-------------------+
| ScenarioRegistry  |        | ScenarioSubsystem |        | SubsystemOrch     |
| .Create("name")   |------->|                   |<-------|                   |
|                   |        | scenario.Configure|        | .Initialize()     |
| => IScenario      |        |   (world, kernel) |        |   -> Configure    |
+-------------------+        |                   |        | .Run()            |
                             | each tick:        |        |   -> tick loop    |
                             |   kernel.Update() |        | .Shutdown()       |
                             |   scenario.       |        |   -> OnShutdown   |
                             |   EvaluateTick()  |        +-------------------+
                             |   if true -> exit |
                             |   if timeout      |
                             |     -> exit 2     |
                             +-------------------+
```

### Folder / Subsystem Map

```
Fdp.Examples.Scenarios/
+-- BehaviorValidationBehaviorIds.cs     (shared constants)
+-- Cognitive/
|     BehaviorValidationScenario.cs      DEM1-D005  HSM + BTree validation
|     MissionCommandScenario.cs          DEM1-D006  Mission command tree
+-- Integrated/
|     UrbanCombatNewScenario.cs          DEM1-D010  Full urban ambush
|     UrbanCombatValidator.cs            Validator helper for urban combat
+-- Kinematics/
|     AutoDriveScenario.cs              DEM1-D001  Road-graph auto-drive
|     ComponentDamageScenario.cs        DEM1-D002  Mobility damage effects
+-- Network/
|     DistributedTankScenario.cs        DEM1-D009  Dual-node DDS tank
+-- Perception/
|     SensorGridScenario.cs             DEM1-D004  Sensor detection grid
|     TerrainClampingScenario.cs        DEM1-D007  Ground clamping + DEM
+-- Physics/
|     BallisticsAndHitScenario.cs       DEM1-D003  CCD anti-tunneling
+-- Replay/
      ParallelEpisodesScenario.cs       DEM1-D008  LZ4 recording + replay
```

---

## Source Structure (all files and types)

### Root

| File | Namespace | Type |
|---|---|---|
| `BehaviorValidationBehaviorIds.cs` | `Fdp.Examples.Scenarios` | `static class BehaviorValidationBehaviorIds` |

### Cognitive/

| File | Namespace | Type |
|---|---|---|
| `BehaviorValidationScenario.cs` | `Fdp.Examples.Scenarios.Cognitive` | `sealed class BehaviorValidationScenario : IScenario` |
| `MissionCommandScenario.cs` | `Fdp.Examples.Scenarios.Cognitive` | `sealed class MissionCommandScenario : IScenario` |

### Integrated/

| File | Namespace | Type |
|---|---|---|
| `UrbanCombatNewScenario.cs` | `Fdp.Examples.Scenarios.Integrated` | `sealed class UrbanCombatNewScenario : IScenario` |
| `UrbanCombatValidator.cs` | `Fdp.Examples.Scenarios.Integrated` | `sealed class UrbanCombatValidator` |

### Kinematics/

| File | Namespace | Type |
|---|---|---|
| `AutoDriveScenario.cs` | `Fdp.Examples.Scenarios.Kinematics` | `sealed class AutoDriveScenario : IScenario` |
| `ComponentDamageScenario.cs` | `Fdp.Examples.Scenarios.Kinematics` | `sealed class ComponentDamageScenario : IScenario` |

### Network/

| File | Namespace | Type |
|---|---|---|
| `DistributedTankScenario.cs` | `Fdp.Examples.Scenarios.Network` | `sealed class DistributedTankScenario : IScenario` |

### Perception/

| File | Namespace | Type |
|---|---|---|
| `SensorGridScenario.cs` | `Fdp.Examples.Scenarios.Perception` | `sealed class SensorGridScenario : IScenario` |
| `TerrainClampingScenario.cs` | `Fdp.Examples.Scenarios.Perception` | `sealed class TerrainClampingScenario : IScenario` |

### Physics/

| File | Namespace | Type |
|---|---|---|
| `BallisticsAndHitScenario.cs` | `Fdp.Examples.Scenarios.Physics` | `sealed class BallisticsAndHitScenario : IScenario` |

### Replay/

| File | Namespace | Type |
|---|---|---|
| `ParallelEpisodesScenario.cs` | `Fdp.Examples.Scenarios.Replay` | `sealed class ParallelEpisodesScenario : IScenario` |

---

## Public API Reference

### `BehaviorValidationBehaviorIds`

```csharp
public static class BehaviorValidationBehaviorIds
{
    public const int Combat = 2900;
}
```

Compile-time behavior ID for the combat BTree used by `BehaviorValidationScenario`.
Range 2001-2999 (upper military range) avoids conflicts with `BehaviorIds` and
`DemoBehaviorIds`.

---

### Scenario Detail: `AutoDriveScenario`

**Demo ID:** DEM1-D001  
**Scenario name:** `"autodrive"`

Exercises the `CarKinematicsSystem` road-graph follow behavior. A single vehicle spawned
at a road start node drives along a predefined route to a goal node.

**Phase table:**

| Phase | Tick | Assertion |
|---|---|---|
| 1 | ~50 | Vehicle has moved (distance > threshold from start) |
| 2 | ~120 | Vehicle velocity is non-zero (still moving) |
| 3 | ~200 | Vehicle has reached arrival radius of goal node |

---

### Scenario Detail: `ComponentDamageScenario`

**Demo ID:** DEM1-D002  
**Scenario name:** `"componentdamage"`

Demonstrates mobility loss cascade: a tank is hit in the drivetrain, its mobility
component health drops to zero, the `MobilityDamageSystem` raises a mobility-lost event,
and the vehicle's behavior HSM transitions to the Disabled state.

**Phase table:**

| Phase | Tick | Assertion |
|---|---|---|
| 1 | early | Vehicle is mobile (locomotion active) |
| 2 | ~10 | Damage applied to drivetrain component |
| 3 | ~12 | Vehicle stopped (velocity near zero) |

---

### Scenario Detail: `BallisticsAndHitScenario`

**Demo ID:** DEM1-D003  
**Scenario name:** `"ballisticsandhit"`

Demonstrates CCD (Continuous Collision Detection) anti-tunneling. A bullet with muzzle
velocity 2000 m/s travels ~33 m per tick — far more than the target's 4 m diameter. The
swept-segment raycast in `BallisticsSystem` still detects the crossing.

**Key constants:**

```csharp
public const float MuzzleVelocity = 2000f; // m/s -> ~33 m/tick at 60Hz
```

**Phase table:**

| Phase | Tick | Assertion |
|---|---|---|
| 1 | 2 | Bullet spawned; `SimVelocity.Linear.X == 2000 m/s` |
| 2 | 3 | `bullet.Position.X > 10` (bullet past target in raw space) |
| 3 | 4 | `target.Health < 100` (CCD hit applied by DamageSystem) |
| 4 | 4 | Bullet entity destroyed (single-hit semantics) |

**System pipeline (order is mandatory):**

```
FireProcessingSystem
  -> SpatialHashSystem
  -> BallisticsSystem         (records PreviousPosition; submits swept-segment raycast)
  -> LinearKinematicsSystem   (advances bullet position)
  -> RaycastSolverSystem      (resolves batch from BallisticsSystem)
  -> HitResolutionSystem      (publishes HitEvent to write bus)
  -> DamageSystem             (reads HitEvent after SwapBuffers; applies damage)
```

---

### Scenario Detail: `SensorGridScenario`

**Demo ID:** DEM1-D004  
**Scenario name:** `"sensorgrid"`

Places a grid of sensor entities and a target. Verifies that `SensorBroadphaseSystem`
detects the target when it moves into sensor range and stops detecting it when it moves
out of range.

**Phase table:**

| Phase | Tick | Assertion |
|---|---|---|
| 1 | ~20 | Target inside sensor range — detection count > 0 |
| 2 | ~50 | Target outside sensor range — detection count == 0 |

---

### Scenario Detail: `BehaviorValidationScenario`

**Demo ID:** DEM1-D005  
**Scenario name:** `"behaviorvalidation"`

Validates that a combat BTree (behavior ID `BehaviorValidationBehaviorIds.Combat = 2900`)
executes the correct action sequence:

```
Selector
  +-- Sequence
  |     +-- Condition_ThreatVisible
  |     +-- Condition_HasAmmo
  |     +-- Action_AimAndFire
  +-- Action_Flee
```

**Phase table:**

| Phase | Tick | Assertion |
|---|---|---|
| 1 | ~5 | `Condition_ThreatVisible` true, `Action_AimAndFire` active |
| 2 | ~30 | After ammo depleted, `Action_Flee` active |

---

### Scenario Detail: `MissionCommandScenario`

**Demo ID:** DEM1-D006  
**Scenario name:** `"missioncommand"`

Exercises the mission command hierarchy: a commander entity issues move-to-waypoint
orders that propagate through the `MissionDirectorSystem` to subordinate units. The
scenario verifies that all subordinates reach their assigned waypoints within the tick
budget.

---

### Scenario Detail: `TerrainClampingScenario`

**Demo ID:** DEM1-D007  
**Scenario name:** `"terrainclamping"`

Verifies the `GroundClampingSystem` pipeline over a synthetic DEM with three zones:
flat (no clamping), ramp (smooth transition), and spike (outlier rejection).

**Key constants:**

```csharp
private const float VehicleSpeedMs          = 10.0f;  // m/s
private const float SmoothedOffsetTolerance = 0.5f;   // metres
```

**Phase table:**

| Phase | Tick | Assertion |
|---|---|---|
| 1 | 10 | `state.CurrentZOffset < 0.01` (flat zone, no clamping) |
| 2 | 150 | `state.TargetZOffset > 0.5` AND `CurrentZOffset < TargetZOffset` (smoothing lags) |
| 3 | 240 | `state.LastValidIgAltitude < 10` (spike rejected) |
| 4 | 300 | `state.TargetZOffset ≈ 6.0 ± 0.5` (post-recovery) |

---

### Scenario Detail: `ParallelEpisodesScenario`

**Demo ID:** DEM1-D008  
**Scenario name:** `"parallelepisodes"`

Proves that `FlightRecorder` LZ4 recording and naked-node replay produce bit-identical
positions.

**Phase A (in `Configure`):** A separate live world drives a vehicle for `LiveRunTicks=50`
ticks, capturing positions into `_livePositions`. The `.fdprec` file is written at the
end of phase A.

**Phase B (main loop):** A `ReplayModule` (no kinematics) replays the recording. At ticks
26 and 51 the replayed position is compared against the stored live position.

**Key constants:**

```csharp
public const int   LiveRunTicks       = 50;
public const float FixedDelta         = 1.0f / 60.0f;
public const float PositionTolerance  = 0.001f; // metres
```

---

### Scenario Detail: `DistributedTankScenario`

**Demo ID:** DEM1-D009  
**Scenario name:** `"distributedtank"`

Splits a single tank simulation across a brain node and a muscle node within the same
process, communicating via the `Fdp.Examples.DDS` message types. The brain node runs
behavior and navigation; the muscle node runs kinematics and physics.

---

### Scenario Detail: `UrbanCombatNewScenario`

**Demo ID:** DEM1-D010  
**Scenario name:** `"urbancombat"`

Full-stack integration scenario using the `Fdp.Examples.UrbanCombat` module. Spawns 14
entities (5 civilians, 3 civilian cars, 1 APC, 4 soldiers, 1 insurgent) and runs the
Urban Ambush scenario for a fixed tick count. Delegates setup to `HeadlessDemoApp` and
validation to `UrbanCombatValidator`.

---

## Scenario Phase Diagram (BallisticsAndHit)

```
Tick 0    Tick 1         Tick 2           Tick 3            Tick 4
  |         |               |                |                 |
  | Config  | FireRequest   | Bullet exists  | Bullet past     | Damage applied
  | Spawn   | injected      | vel=2000 m/s   | target X=10     | bullet destroyed
  | shooter |               | (Phase 1 PASS) | (Phase 2 PASS)  | (Phase 3+4 PASS)
  | target  |               |                |                 | -> return true
  |         |               |                |
  |<------->|<------------->|<-------------->|<--------------->|
  | 1 tick  |   1 tick      |    1 tick      |    1 tick       |
```

## Scenario Phase Diagram (TerrainClamping)

```
Tick 0      Tick 10      Tick 150       Tick 240       Tick 300
  |            |             |              |              |
  | Spawn    flat zone    ramp zone      spike zone    post-recovery
  | vehicle  ZOffset<0.01 Target>0.5    LastValid<10  Target~6.0
  | driving  (Phase 1)   Current<Target (Phase 3)    (Phase 4 -> true)
  |          passed       (Phase 2)     passed       passed
  |<-------->|<---------->|<------------>|<----------->|
  |  10 ticks|  140 ticks |   90 ticks   |   60 ticks  |
```

---

## Dependencies

### NuGet packages

None directly (all come transitively from referenced projects).

### Project references

| Project | Scenarios using it |
|---|---|
| `Fdp.Network.Cyclone` | `DistributedTankScenario` |
| `Fdp.Examples.Common` | All scenarios (`IScenario`, `ScenarioNames`, `DemoBehaviorIds`) |
| `Fdp.Examples.DDS` | `DistributedTankScenario`, `UrbanCombatNewScenario` |
| `Fhsm.Compiler` | `BehaviorValidationScenario`, `UrbanCombatNewScenario` |
| `Fdp.Toolkits.Analyzers` | Analyzer only (Roslyn, no runtime reference) |

---

## Usage Examples

### Example 1 — Running BallisticsAndHitScenario from the runner

```bash
dotnet fdp-demo-runner.dll --scenario ballisticsandhit --max-ticks 10
# Exit 0: all 4 phases passed by tick 4
```

### Example 2 — Asserting phase values in a test

```csharp
// In Fdp.Examples.Scenarios.Tests:
[Test]
public void BallisticsAndHit_BulletVelocityMatchesMuzzleVelocity()
{
    int code = -1;
    Program.RunMain(
        ["--scenario", "ballisticsandhit", "--max-ticks", "10"],
        Console.Out,
        c => code = c);

    Assert.AreEqual(0, code);
}
```

### Example 3 — Writing a custom scenario

```csharp
using Fdp.Examples.Common;
using Fdp.Core;
using Fdp.ModuleHost;

namespace Fdp.Examples.Scenarios.Custom
{
    public sealed class MyDemoScenario : IScenario
    {
        public string ScenarioName => "mydemo";

        private Entity _target;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // 1. Register components
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<Health>();

            // 2. Register systems via a module
            kernel.RegisterModule(new MyModule(world));

            // 3. Spawn entities
            _target = world.CreateEntity();
            world.AddComponent(_target, new SimTransform());
            world.AddComponent(_target, new Health { Current = 100f, Max = 100f });
        }

        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            if (tick == 50)
            {
                var h = world.GetComponent<Health>(_target);
                if (h.Current >= 100f)
                    throw new ScenarioFailureException(1, "Health should have decreased");
                return true;
            }
            return false;
        }

        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
    }
}
```

### Example 4 — TerrainClamping: reading observable state in a test

```csharp
// TerrainClampingScenario exposes observable properties for assertions:
var scenario = new TerrainClampingScenario();

// After running to completion:
Assert.IsTrue(scenario.Phase1CurrentZOffset < 0.01f);
Assert.IsTrue(scenario.Phase2TargetZOffset > 0.5f);
Assert.IsTrue(scenario.Phase2CurrentZOffset < scenario.Phase2TargetZOffset);
Assert.IsTrue(scenario.Phase3LastValidIgAltitude < 10f);
Assert.IsTrue(MathF.Abs(scenario.Phase4TargetZOffset - 6.0f) < 0.5f);
```

---

## Best Practices

### 1. Expose observable properties for test assertions

Rather than testing exit codes alone, scenarios expose `public` properties (e.g.,
`BulletVelocityXAtTick2`, `Phase2TargetZOffset`) that tests can inspect directly after
running the scenario. This provides richer failure diagnostics.

### 2. Use `ScenarioFailureException` for phase failures

Returning `false` indefinitely burns the tick budget and produces a generic timeout
(exit 2). Throwing `ScenarioFailureException(phaseNumber, message)` produces a specific
log entry and a non-timeout exit code.

### 3. System execution order must be documented

When a scenario pipeline has order-dependent correctness (e.g., BallisticsSystem must run
before LinearKinematicsSystem), document this in the class XML doc with a numbered list
and the reason for each constraint.

### 4. Own NativeArrays via a module

Scenarios that use physics (`PhysicsToolkitModule`) or other unmanaged resources must
store the module as a field and dispose it in `OnShutdown()`. Using `using` in `Configure`
would free the memory before the simulation loop runs.

### 5. Phase table in the XML doc

Every scenario class should have a `<list type="table">` in its XML doc that summarizes
each phase by tick number and assertion. This makes the expected behavior legible from the
IDE without running the scenario.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Examples.Runner` | Executable host that runs scenarios by name |
| `Fdp.Examples.Common` | Defines `IScenario`, `ScenarioNames`, shared constants |
| `Fdp.Examples.Scenarios.Tests` | Test project that asserts each scenario's exit code and observable state |
| `Fdp.Examples.UrbanCombat` | `UrbanCombatNewScenario` delegates all setup to `HeadlessDemoApp` here |
| `Fdp.Examples.DDS` | Message types for `DistributedTankScenario` |
| `Fdp.Toolkits` | All toolkit subsystems exercised by the scenarios |
