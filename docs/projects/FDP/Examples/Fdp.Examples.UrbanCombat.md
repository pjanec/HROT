# Fdp.Examples.UrbanCombat

| Field | Value |
|---|---|
| **Project path** | `FDP/Examples/Fdp.Examples.UrbanCombat/Fdp.Examples.UrbanCombat.csproj` |
| **Output type** | Executable (`<OutputType>Exe</OutputType>`) |
| **Target framework** | net8.0 |
| **Simulation** | 600 frames at 60 Hz (10 seconds) |
| **Date documented** | 2026-05-23 |

## README Validation

**Missing** — No README.md exists in the project folder. This document serves as the
primary reference.

---

## Executive Overview

`Fdp.Examples.UrbanCombat` is the **flagship headless simulation demo** that integrates
the maximum number of FDP toolkit subsystems into a single coherent scenario: the
"Urban Ambush". A military APC convoy with infantry escort travels through a city
intersection while an insurgent lying in wait launches an RPG ambush.

The simulation runs for 600 frames (10 seconds at 60 Hz) and demonstrates:

- **TKB (Transient Knowledge Base)** blueprint registration and template-based entity
  spawning.
- **Hierarchical State Machines (FastHSM)** for the APC's behavior (Cruising ->
  Disabled on mobility loss).
- **Behavior Trees (Fbt)** for insurgent Ambush and civilian WanderCivil behaviors.
- **CarKinematics** road-graph navigation on a 4-way city intersection.
- **Physics**: CCD ballistics, raycast collision, `PhysicsCollider` hit detection.
- **Perception**: `TargetMemory` pre-seeding; `SensorBroadphaseSystem`.
- **Combat**: `WeaponFireIntent` -> `FireProcessingSystem` -> `BallisticsSystem` ->
  `HitResolutionSystem` -> `DamageSystem` CQRS chain.
- **Telemetry**: `TelemetryReporterSystem` prints per-tick state to stdout.
- **Network entity map**: `NetworkEntityMap` resolves network IDs to ECS entities for the
  DDS-ready combat chain.

### Scenario cast (14 entities)

| Type | TKB ID | Count | Faction | Behavior |
|---|---|---|---|---|
| `CivilianPedestrian` | 1001 | 5 | Neutral | `WanderCivil` |
| `CivilianCar` | 1002 | 3 | Neutral | `WanderCivil` |
| `MilitaryAPC` | 2001 | 1 | Blue | `ConvoyEscort` (HSM) |
| `InfantrySoldier` | 2002 | 4 | Blue | `InfantryCombat` (BTree) |
| `Insurgent` | 2003 | 1 | Red | `Ambush` (BTree) |

### Key learning objectives

1. **TkbTemplate + ITkbDatabase** pattern for data-driven entity spawning without
   hard-coded `world.AddComponent` calls scattered across the codebase.
2. **FastHSM integration** — `HsmBuilder` fluent API, `HsmNormalizer`, `HsmFlattener`,
   `HsmEmitter`, and how `HsmActionDispatcher` invokes unmanaged action delegates.
3. **Fbt (Fast Behavior Tree)** JSON deserialization and action node delegate binding.
4. **PhysicsToolkitModule** ownership of `RaycastBatchData` NativeArrays — why the module
   must be a field (not `using`), and how `Dispose()` is deferred to teardown.
5. **Multi-phase system pipelines** — Input / Sim / PostSim / Export execution phases.
6. **Entity translators** (`ITkbEntityTranslator`) for adding subsystem-specific components
   to a blueprint without coupling the blueprint to any subsystem.

---

## Architecture

### Module and System Pipeline

```
+--------------------------------------------------------------+
|  HeadlessDemoApp.RunSimulation()                             |
|  Loop (600 frames, dt = 1/60 s):                             |
|                                                              |
|  INPUT PHASE:                                                |
|    PerceptionUpdateSystem      (sense world)                 |
|    HsmTickSystem<BrainHsm128>  (tick APC state machine)      |
|    BTreeTickSystem             (tick Insurgent + Civ BTrees) |
|    FireProcessingSystem        (consume FireRequestEvent)    |
|    NavigationUpdateSystem      (update route)                |
|                                                              |
|  SIM PHASE:                                                  |
|    SpatialHashSystem           (rebuild spatial grid)        |
|    CarKinematicsSystem         (move vehicles on road graph) |
|    BallisticsSystem            (swept-segment CCD raycasts)  |
|    LinearKinematicsSystem      (advance bullets)             |
|    DamageSystem                (apply HitEvents to Health)   |
|    HsmDamageBridgeSystem       (capability loss -> HSM event)|
|                                                              |
|  POSTSIM PHASE:                                              |
|    RaycastSolverSystem         (resolve CCD hit batch)       |
|    HitResolutionSystem         (emit HitEvent)               |
|                                                              |
|  EXPORT PHASE:                                               |
|    TelemetryReporterSystem     (stdout telemetry)            |
|    TrafficBrainSystem          (civilian locomotion)         |
+--------------------------------------------------------------+
```

### Entity Spawn Diagram

```
+------------------------------------------------------------+
|  ScenarioDirector.SetupAmbushScenario()                    |
|                                                            |
|  1. 5x CivilianPedestrian (TKB 1001)                       |
|     Positions: scattered +-30..50 m from intersection (0,0)|
|     Behavior: WanderCivil                                  |
|     TargetMemory[0] seeded with Insurgent -> FLEE from T1  |
|                                                            |
|  2. 3x CivilianCar (TKB 1002)                              |
|     Positions: N(0,60), S(0,-60), E(60,0) road arms        |
|     Behavior: WanderCivil                                  |
|                                                            |
|  3. 1x MilitaryAPC (TKB 2001)                              |
|     Position: (0,-80), heading north (yaw = PI/2)          |
|     Behavior: ConvoyEscort (FastHSM)                       |
|                                                            |
|  4. 4x InfantrySoldier (TKB 2002)                          |
|     Position: co-located with APC at (0,-80)               |
|     Behavior: InfantryCombat (BTree)                       |
|     Embarked in APC (EmbarkSoldiers)                       |
|                                                            |
|  5. 1x Insurgent (TKB 2003)                                |
|     Position: building corner at (60,20)                   |
|     Behavior: Ambush (BTree from Assets/Ambush.json)        |
|     TargetMemory seeded with APC -> fires from T1           |
+------------------------------------------------------------+
```

### Road Network Topology

```
                North (0, +100)
                    |
                    | Segment 0: North->Centre (inbound)
                    | Segment 1: Centre->North (outbound)
                    |
West (-100,0) ------+------ East (+100,0)
                    | Centre (0,0)
                    |
                    | Segment 2: South->Centre (inbound)
                    | Segment 3: Centre->South (outbound)
                    |
                South (0, -100)

5 nodes, 8 Hermite-spline segments (4 inbound + 4 outbound)
APC starts at (0,-80) heading north (toward Centre, then North arm)
```

### Behavior Architecture

```
+-----------------------------------+
|  MilitaryAPC (FastHSM)            |
|                                   |
|  [Cruising] --MobilityLost--> [Disabled]
|  Activity_Cruise:                 |
|    loco.ActiveAction =            |
|      ActionIdFollowRoute          |
|  OnEnter_Disabled:                |
|    loco.ActiveAction = 0          |
|    interact.ActiveAction =        |
|      ActionIdEjectPassengers      |
+-----------------------------------+

+-----------------------------------+
|  Insurgent (Fbt BTree)            |
|                                   |
|  Selector                         |
|    Sequence                       |
|      Condition_HasTarget   (read TargetMemory)
|      Action_AimAndFire     (write WeaponChannel)
|    Action_HoldPosition     (fallback)
+-----------------------------------+

+-----------------------------------+
|  InfantrySoldier (Fbt BTree)      |
|                                   |
|  Action: HoldPosition             |
|  (minimal; holds spawn position)  |
+-----------------------------------+
```

### TKB Blueprint Registration Flow

```
DemoTkbSetup.RegisterAll(tkb)
  |
  +-> RegisterCivilianPedestrian(tkb)
  |     new TkbTemplate("CivilianPedestrian", 1001)
  |     t.AddDescriptor(new TkbMasterDto)
  |     t.AddDescriptor(new SimTransformDto)
  |     t.AddDescriptor(new PhysicsColliderDto { Radius=0.4 })
  |     t.AddDescriptor(new TargetMemoryDto)
  |     ...
  |     tkb.Register(t)
  |
  +-> RegisterCivilianCar(tkb)
  +-> RegisterMilitaryAPC(tkb)
  +-> RegisterInfantrySoldier(tkb)
  +-> RegisterInsurgent(tkb)

SpawnEntity(tkbTypeId: 1001, position, yaw, behaviorId):
  var template = tkb.GetByType(1001)
  var entity   = world.CreateEntity()
  template.ApplyTo(world, entity)        <- adds all descriptor components
  world.GetComponentRW<SimTransform>(entity).Position = position
  world.GetComponentRW<BehaviorState>(entity).BehaviorId = behaviorId
  if entityMap != null: entityMap.Register(++_nextNetId, entity)
  return entity
```

---

## Source Structure

```
FDP/Examples/Fdp.Examples.UrbanCombat/
+-- Fdp.Examples.UrbanCombat.csproj
+-- Program.cs                       Top-level statements entry point
+-- HeadlessDemoApp.cs               namespace Fdp.Examples.UrbanCombat
|     class HeadlessDemoApp : IDisposable
+-- ScenarioDirector.cs              namespace Fdp.Examples.UrbanCombat
|     class ScenarioDirector
+-- UrbanCombatConstants.cs          namespace Fdp.Examples.UrbanCombat
|     static class UrbanCombatConstants
+-- Assets/
|     Ambush.json                    BTree JSON for Insurgent behavior
+-- Blueprints/
|     EntityBlueprints.cs            namespace Fdp.Examples.UrbanCombat.Blueprints
|       [Obsolete] static class EntityBlueprints  (ID constants only)
+-- Brains/
|     ApcHsmActions.cs               namespace Fdp.Examples.UrbanCombat.Brains
|       static unsafe class ApcHsmActions
|     ApcHsmSetup.cs                 namespace Fdp.Examples.UrbanCombat.Brains
|       static class ApcHsmSetup
|     InsurgentNodes.cs              namespace Fdp.Examples.UrbanCombat.Brains
|       static class InsurgentNodes
+-- Setup/
|     DemoEnvironmentSetup.cs        namespace Fdp.Examples.UrbanCombat.Setup
|       static class DemoEnvironmentSetup
|     DemoTkbSetup.cs                namespace Fdp.Examples.UrbanCombat.Setup
|       static class DemoTkbSetup
+-- Systems/
      TelemetryReporterSystem.cs     namespace Fdp.Examples.UrbanCombat.Systems
        class TelemetryReporterSystem : IEcsModuleSystem
      TrafficBrainSystem.cs          namespace Fdp.Examples.UrbanCombat.Systems
        class TrafficBrainSystem : IEcsModuleSystem
```

---

## Public API Reference

### `HeadlessDemoApp`

```csharp
public class HeadlessDemoApp : IDisposable
{
    public EntityRepository World    { get; private set; }
    public ITkbDatabase     Tkb      { get; }
    public BehaviorRegistry BehaviorRegistry { get; }
    public RoadNetworkBlob  Road     { get; private set; }
    public NetworkEntityMap EntityMap { get; }

    public HeadlessDemoApp();
    public void Initialize();
    public void Run();
    public void Dispose();
}
```

| Method | Description |
|---|---|
| `Initialize()` | Registers components, TKB blueprints, HSM actions, road network, physics singleton, behavior registry, and system pipeline. Must be called once before `Run()`. |
| `Run()` | Calls `SetupAmbushScenario()` then executes the 600-frame simulation loop at `dt = 1/60 s`. |
| `Dispose()` | Disposes `PhysicsToolkitModule` (frees NativeArrays), disposes `RoadNetworkBlob`. |

**Simulation constants:**

```csharp
private const float Dt          = 1f / 60f;   // 60 Hz
private const int TotalFrames   = 600;         // 10 seconds
```

### `ScenarioDirector`

```csharp
public class ScenarioDirector
{
    public ScenarioDirector(
        EntityRepository world,
        ITkbDatabase tkb,
        RoadNetworkBlob road,
        BehaviorRegistry registry,
        NetworkEntityMap? entityMap = null,
        IReadOnlyList<ITkbEntityTranslator>? translators = null);

    public unsafe void SetupAmbushScenario();
}
```

| Parameter | Description |
|---|---|
| `world` | The ECS world (must have all components registered) |
| `tkb` | TKB database populated by `DemoTkbSetup.RegisterAll()` |
| `road` | Road network for vehicle navigation |
| `registry` | Behavior registry with HSM blob and BTree definitions |
| `entityMap` | Optional network ID map for the combat CQRS chain |
| `translators` | List of `ITkbEntityTranslator` instances (defaults to all 5 standard translators) |

`SetupAmbushScenario()` spawns all 14 entities and wires up pre-seeded `TargetMemory`
entries so combat begins from frame 1.

### `UrbanCombatConstants`

```csharp
public static class UrbanCombatConstants
{
    // Factions
    public const byte FactionNeutral = 0;
    public const byte FactionBlue    = 1;
    public const byte FactionRed     = 2;

    // Collider radii (metres)
    public const float HumanoidColliderRadius = 0.4f;
    public const float CarColliderRadius      = 2.0f;
    public const float ApcColliderRadius      = 3.5f;

    // Health
    public const float ApcMaxHealth     = 500f;
    public const float SoldierMaxHealth = 100f;

    // Rifle (InfantrySoldier)
    public const int   RifleAmmo           = 30;
    public const float RifleMuzzleVelocity = 800f;

    // RPG (Insurgent)
    public const int   RpgAmmo           = 1;
    public const float RpgMuzzleVelocity = 300f;

    // Perception ranges (metres)
    public const float CivilianVisionRange  = 30f;
    public const float CivilianHearingRange = 100f;
    public const float SoldierVisionRange   = 150f;
    public const float SoldierHearingRange  = 200f;
}
```

### `ApcHsmSetup`

```csharp
public static class ApcHsmSetup
{
    public const ushort CruisingStateIndex = 1;
    public const ushort DisabledStateIndex = 2;

    public static HsmDefinitionBlob Build();
}
```

`Build()` compiles the "ConvoyEscort_HSM" state machine using the `HsmBuilder` fluent
API, normalizes, validates, flattens, and emits a `HsmDefinitionBlob`. The blob is
registered in `HeadlessDemoApp.RegisterBehaviors()` and stored in `BehaviorRegistry`.

### `ApcHsmActions`

```csharp
public static unsafe class ApcHsmActions
{
    [HsmAction]
    public static void Activity_Cruise(void* instance, void* context, HsmCommandWriter* writer);

    [HsmAction]
    public static void OnEnter_Disabled(void* instance, void* context, HsmCommandWriter* writer);
}
```

Unmanaged HSM action delegates. `HsmKernelBridge*` context carries `Entity Self` and
`IntPtr WorldHandle` (a `GCHandle` to the `EntityRepository`). Actions recover the world
via `GCHandle.FromIntPtr(bridge->WorldHandle).Target`.

### `ApcHsmSetup.CruisingStateIndex` / `DisabledStateIndex`

BFS-order flat state indices after normalization. Used by test code and `ScenarioDirector`
to pre-initialize `BrainHsm128.CurrentState`.

### `DemoEnvironmentSetup`

```csharp
public static class DemoEnvironmentSetup
{
    public static RoadNetworkBlob CreateCityIntersection();
}
```

Creates a 4-way intersection road graph: 5 nodes (Centre, N, S, E, W), 8 Hermite-spline
segments (4 inbound + 4 outbound). The caller is responsible for disposing the returned
`RoadNetworkBlob`.

### `DemoTkbSetup`

```csharp
public static class DemoTkbSetup
{
    public static void RegisterAll(ITkbDatabase tkb);
}
```

Registers five `TkbTemplate` entries for entity types 1001-2003. Each template includes
all descriptor components required by the respective subsystem translators.

### `EntityBlueprints` (Obsolete)

```csharp
[Obsolete("Factory methods removed in BATCH-15. Use DemoTkbSetup.RegisterAll + tkb.GetByType instead.")]
public static class EntityBlueprints
{
    public const int Id_CivilianPedestrian = 1001;
    public const int Id_CivilianCar        = 1002;
    public const int Id_MilitaryAPC        = 2001;
    public const int Id_InfantrySoldier    = 2002;
    public const int Id_Insurgent          = 2003;
}
```

Retained for the ID constants. Factory methods were removed in BATCH-15 in favor of
the TKB pattern. Do not add new factory methods here.

### `InsurgentNodes`

```csharp
public static class InsurgentNodes
{
    public static NodeStatus Condition_HasTarget(...);
    public static NodeStatus Action_AimAndFire(...);
    public static NodeStatus Action_HoldPosition(...);
}
```

BTree node delegates for the Insurgent "Ambush_BT" behavior tree. Loaded from
`Assets/Ambush.json` via `Fbt.Serialization.BTreeLoader`. Node methods use
`BTreeContext.World` to access ECS components (DEBT-007 pattern, zero static state).

### `TelemetryReporterSystem`

```csharp
public class TelemetryReporterSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime);
}
```

Prints per-tick telemetry to stdout: entity count, APC health, insurgent state, frame
number. Runs in the Export phase.

### `TrafficBrainSystem`

```csharp
public class TrafficBrainSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime);
}
```

Drives civilian locomotion: reads `TargetMemory` to decide between `ActionIdWander` and
`ActionIdFlee`. Runs in the Export phase.

---

## Dependencies

### Project references

| Project | Purpose |
|---|---|
| `Fdp.Core` | `EntityRepository`, `EntityCommandBuffer`, `FdpConfig` |
| `Fdp.Toolkits` | All toolkit subsystems: behavior, combat, navigation, perception, physics, replay, CarKinem |
| `Fhsm.Compiler` | `HsmBuilder`, `HsmNormalizer`, `HsmFlattener`, `HsmEmitter`, `HsmDefinitionBlob` |
| `Fdp.Toolkits.Analyzers` | Roslyn analyzer (output only, no runtime reference) |

### NuGet packages

None directly (all resolved transitively through `Fdp.Toolkits`).

---

## Usage Examples

### Example 1 — Running the Urban Ambush

```bash
cd FDP/Examples/Fdp.Examples.UrbanCombat
dotnet run

# Output (per-tick telemetry from TelemetryReporterSystem):
# [Frame 1] Entities=14 APC.Health=500 APC.State=Cruising
# [Frame 2] Entities=14 APC.Health=500 APC.State=Cruising
# ...
# [Frame N] Insurgent fires RPG at APC
# [Frame N+2] APC.Health=425 (RPG hit applied)
# ...
# Simulation complete. 600 frames executed.
```

### Example 2 — Building the APC HSM manually

```csharp
using Fhsm.Compiler;
using Fdp.Examples.UrbanCombat.Brains;
using Fdp.Toolkit.Behavior;

// Compile the ConvoyEscort_HSM state machine definition
var blob = ApcHsmSetup.Build();

Console.WriteLine($"Structure hash: {blob.Header.StructureHash:X8}");
Console.WriteLine($"State count: {blob.Header.StateCount}");
// State count == 3 (root, Cruising, Disabled)

// Register with behavior registry for runtime use:
var registry = new BehaviorRegistry();
registry.RegisterHsm(BehaviorIds.ConvoyEscort, blob);
```

### Example 3 — Spawning entities via TKB

```csharp
// Register all blueprints
var tkb = new TkbDatabase();
DemoTkbSetup.RegisterAll(tkb);

// Spawn a MilitaryAPC at a custom position
var world = new EntityRepository();
// ... register all component types first ...
var road  = DemoEnvironmentSetup.CreateCityIntersection();
var registry = new BehaviorRegistry();
// ... register behaviors ...

var director = new ScenarioDirector(world, tkb, road, registry);
director.SetupAmbushScenario();

// All 14 entities are now in the world:
var apcQuery = world.Query().With<BehaviorState>().Build();
int entityCount = 0;
foreach (var e in apcQuery) entityCount++;
Console.WriteLine($"Spawned {entityCount} entities with behavior");
```

### Example 4 — Understanding the APC HSM transition

```csharp
// The APC transitions from Cruising to Disabled when MobilityLost fires.
// HsmDamageBridgeSystem monitors HealthData.Current for the APC and
// raises the MobilityLost event when health drops to zero:

// Before hit: APC state == ApcHsmSetup.CruisingStateIndex (1)
// After RPG hits APC and health <= 0:
//   HsmDamageBridgeSystem -> HSM event BehaviorConstants.EventId_MobilityLost
//   HsmTickSystem -> OnEnter_Disabled fires:
//     loco.ActiveAction = 0          // APC stops moving
//     interact.ActiveAction =
//       BehaviorConstants.ActionIdEjectPassengers  // soldiers disembark
// After: APC state == ApcHsmSetup.DisabledStateIndex (2)
```

### Example 5 — Extending with a new entity type

```csharp
// 1. Add constants to UrbanCombatConstants:
public const int Id_SniperTeam = 2004;

// 2. Register blueprint in DemoTkbSetup:
private static void RegisterSniperTeam(ITkbDatabase tkb)
{
    var t = new TkbTemplate("SniperTeam", tkbType: 2004);
    t.AddDescriptor(new TkbMasterDto { CustomName = "SniperTeam" });
    t.AddDescriptor(new SimTransformDto());
    t.AddDescriptor(new PhysicsColliderDto
    {
        Radius         = UrbanCombatConstants.HumanoidColliderRadius,
        CollisionLayer = 1,
    });
    t.AddDescriptor(new WeaponStateDto
    {
        Ammo           = 5,
        MuzzleVelocity = 900f,
    });
    t.AddDescriptor(new HealthDataDto
    {
        Current = UrbanCombatConstants.SoldierMaxHealth,
        Max     = UrbanCombatConstants.SoldierMaxHealth,
    });
    t.AddDescriptor(new FactionDto { FactionId = UrbanCombatConstants.FactionRed });
    tkb.Register(t);
}

// 3. Spawn in ScenarioDirector.SetupAmbushScenario():
SpawnEntity(
    tkbTypeId:  2004,
    position:   new Vector3(-60f, 20f, 0f),
    yawRadians: 0f,
    behaviorId: BehaviorIds.Ambush);
```

---

## Best Practices

### 1. Never use `using` for PhysicsToolkitModule

`PhysicsToolkitModule.Initialize(world)` allocates `RaycastBatchData` NativeArrays
**inside the world singleton**. If the module is disposed before the simulation ends,
the world will hold a dangling pointer. Always store the module as a field and dispose
it in `Dispose()` after the simulation loop completes.

```csharp
// CORRECT:
_physicsModule = new PhysicsToolkitModule();
_physicsModule.Initialize(world);
// ... run 600 frames ...
// In Dispose():
_physicsModule?.Dispose();

// WRONG - disposes NativeArrays before the loop runs:
// using var physicsModule = new PhysicsToolkitModule();
// physicsModule.Initialize(world);
```

### 2. Use entity translators, not direct world.AddComponent in blueprints

The `ITkbEntityTranslator` pattern (`SpatialCoreTkbTranslator`, `BehaviorTkbTranslator`,
etc.) decouples blueprint definitions from specific subsystem APIs. If a subsystem adds a
new required component, you add it to the translator, not to every blueprint separately.

### 3. Pre-seed TargetMemory for headless scenarios

`SensorBroadphaseSystem` requires multiple frames of perception data before `TargetMemory`
is populated. In headless scenarios without the full perception pipeline, pre-seed
`TargetMemory` directly (as `ScenarioDirector` does for the insurgent targeting the APC)
to ensure behaviors fire from frame 1.

### 4. Use BFS state indices in tests

`ApcHsmSetup.CruisingStateIndex` and `DisabledStateIndex` are BFS-normalized flat indices.
Use these named constants, not raw integers, in tests and telemetry assertions. The BFS
normalization is deterministic (root=0, children in definition order) but brittle if
states are reordered.

### 5. Road network blob disposal is caller's responsibility

`DemoEnvironmentSetup.CreateCityIntersection()` returns a `RoadNetworkBlob` that wraps
a NativeArray. The caller (in this case `HeadlessDemoApp`) must dispose it. Do not pass
the blob to a `using` block if it is needed across the entire session lifetime.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Core` | ECS kernel (`EntityRepository`, event bus) |
| `Fdp.Toolkits` | All subsystem toolkits: behavior, combat, navigation, perception, physics, CarKinem |
| `Fhsm.Compiler` (ExtDeps/FastHSM) | HSM compilation pipeline used by `ApcHsmSetup` |
| `Fdp.Examples.Scenarios` | `UrbanCombatNewScenario` runs `HeadlessDemoApp` as an `IScenario` |
| `Fdp.Examples.DDS` | DDS message types for the combat CQRS chain over DDS in multi-node mode |
| `Fdp.Examples.Runner` | CLI runner that executes `UrbanCombatNewScenario` via `--scenario urbancombat` |
| `Fdp.Examples.Showcase` | Sister GUI demo using the same toolkit subsystems |
