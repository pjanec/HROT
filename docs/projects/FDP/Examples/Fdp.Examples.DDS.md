# Fdp.Examples.DDS

| Field | Value |
|---|---|
| **Project path** | `FDP/Examples/Fdp.Examples.DDS/Fdp.Examples.DDS.csproj` |
| **Output type** | Class library (no executable entry point) |
| **Target framework** | net8.0 |
| **Date documented** | 2026-05-23 |

## README Validation

**Missing** — No README.md exists in the project folder. This document serves as the primary reference.

---

## Executive Overview

`Fdp.Examples.DDS` is the **shared DDS message schema library** for the multi-node example
scenarios. It contains exactly five flat IDL-like C# structs, each annotated with
`[DdsTopic]` and decorated with `[DdsKey]` on the fields that identify a unique instance
(keyed topics).

These message types are consumed by two scenarios:

- **DistributedTankScenario** (`Fdp.Examples.Scenarios/Network`) — splits the tank
  simulation across a "brain" node and a "muscle" node. Transform, locomotion, weapon, spawn,
  and combat-interaction data flows over DDS.
- **UrbanCombatNewScenario** (`Fdp.Examples.Scenarios/Integrated`) — full urban-ambush
  simulation with the same DDS wire format.

### Key learning objectives

1. **Defining DDS topics in C#** using `CycloneDDS.NET` attributes instead of IDL files.
2. **Keyed vs. keyless topics** — fields marked `[DdsKey]` identify the lifecycle of a
   specific instance; un-keyed topics broadcast state changes without per-instance identity.
3. **Flat Cartesian representation** — the project deliberately avoids geodetic coordinates,
   showing how to replicate simple simulation-space transforms.
4. **Partial structs** — all types are `partial struct`, leaving room for CycloneDDS
   source-generator output to add serialization methods.
5. **Separation of concerns** — the schema library has no application logic. Any node can
   reference it without pulling in a runtime dependency.

---

## Architecture

### Role in the System

```
+-------------------------------+       +-------------------------------+
|    Brain Node (cognitive)     |       |    Muscle Node (physics)      |
|                               |       |                               |
|  NavigationSystem             |       |  CarKinematicsSystem          |
|  BehaviorSystem               |       |  WeaponPhysicsSystem          |
|         |                     |       |          ^                    |
|         | DDS WRITE           |       |          | DDS READ           |
|         v                     |       |          |                    |
|  DdsWriter<DemoLocomotionMsg> |------>|  DdsReader<DemoLocomotionMsg> |
|  DdsWriter<DemoWeaponMsg>     |------>|  DdsReader<DemoWeaponMsg>     |
|  DdsWriter<DemoSpawnMsg>      |------>|  DdsReader<DemoSpawnMsg>      |
|  DdsReader<DemoTransformMsg>  |<------|  DdsWriter<DemoTransformMsg>  |
|  DdsReader<DemoCombatMsg>     |<------|  DdsWriter<DemoCombatMsg>     |
+-------------------------------+       +-------------------------------+
            Both nodes reference Fdp.Examples.DDS (shared schema)
```

### Topic Flow per Scenario

```
+-------------------+   Spawn/Destroy    +-------------------+
|   Brain Node      |------------------->|  All Subscribers  |
|                   |   DemoSpawnMsg     |                   |
|                   |   (Keyed: NetId)   |                   |
+-------------------+                   +-------------------+

+-------------------+   Commands         +-------------------+
|   Brain Node      |------------------->|  Muscle Node      |
|                   |   DemoLocomotionMsg|                   |
|                   |   DemoWeaponMsg    |                   |
|                   |   (Keyed: NetId)   |                   |
+-------------------+                   +-------------------+

+-------------------+   Ground truth     +-------------------+
|   Muscle Node     |------------------->|  Brain Node       |
|                   |   DemoTransformMsg |  + Vis Node(s)     |
|                   |   (Keyed: NetId)   |                   |
+-------------------+                   +-------------------+

+-------------------+   Fire/Hit events  +-------------------+
|   Muscle Node     |------------------->|  Brain Node       |
| (ballistics)      |   DemoCombatMsg    |  + UI/Score       |
|                   |   (Keyless)        |                   |
+-------------------+                   +-------------------+
```

### Message Taxonomy

```
+---------------------------+------+------------------+--------------------+
| Type                      | Keyed| DDS Topic name   | Direction          |
+---------------------------+------+------------------+--------------------+
| DemoSpawnMsg              |  Yes | FDP.Demo_Spawn   | Brain -> All       |
| DemoTransformMsg          |  Yes | FDP.Demo_Transform| Muscle -> Brain   |
| DemoLocomotionMsg         |  Yes | FDP.Demo_Locomotion| Brain -> Muscle  |
| DemoWeaponMsg             |  Yes | FDP.Demo_Weapon  | Brain -> Muscle    |
| DemoCombatInteractionMsg  |  No  | FDP.Demo_CombatInteraction| Muscle->All|
+---------------------------+------+------------------+--------------------+
```

---

## Source Structure

```
FDP/Examples/Fdp.Examples.DDS/
+-- Fdp.Examples.DDS.csproj
+-- DemoCombatInteractionMsg.cs     namespace Fdp.Examples.DDS
+-- DemoLocomotionMsg.cs            namespace Fdp.Examples.DDS
+-- DemoSpawnMsg.cs                 namespace Fdp.Examples.DDS
+-- DemoTransformMsg.cs             namespace Fdp.Examples.DDS
+-- DemoWeaponMsg.cs                namespace Fdp.Examples.DDS
```

All types live in the single namespace `Fdp.Examples.DDS`.

---

## Public API Reference

### `DemoSpawnMsg`

```
[DdsTopic("FDP.Demo_Spawn")]
public partial struct DemoSpawnMsg
```

Spawns or destroys a networked entity. Published once by the authoritative owner node
when an entity enters or leaves the simulation.

| Field | Type | DDS role | Description |
|---|---|---|---|
| `NetworkId` | `long` | `[DdsKey]` | Unique long-lived network identifier |
| `TkbType` | `long` | data | TKB template type ID (e.g. 100 = CommandTank) |
| `OwnerNodeId` | `int` | data | Node ID of the authoritative owner |
| `IsDestroyed` | `bool` | data | When `true`, the entity should be removed |

**Lifecycle semantics:** Because `NetworkId` is a key, CycloneDDS tracks a separate
instance for each unique `NetworkId`. A `DisposeInstance` call signals the subscriber
that the instance's lifecycle has ended, triggering entity destruction logic.

### `DemoTransformMsg`

```
[DdsTopic("FDP.Demo_Transform")]
public partial struct DemoTransformMsg
```

Replicates `SimTransform` (position + rotation) in flat Cartesian space. Published every
physics tick by the node that owns the entity's kinematics.

| Field | Type | DDS role | Description |
|---|---|---|---|
| `NetworkId` | `long` | `[DdsKey]` | Entity identifier |
| `PosX` | `float` | data | Position X in metres |
| `PosY` | `float` | data | Position Y in metres |
| `PosZ` | `float` | data | Position Z in metres |
| `RotX` | `float` | data | Quaternion X component |
| `RotY` | `float` | data | Quaternion Y component |
| `RotZ` | `float` | data | Quaternion Z component |
| `RotW` | `float` | data | Quaternion W component |

### `DemoLocomotionMsg`

```
[DdsTopic("FDP.Demo_Locomotion")]
public partial struct DemoLocomotionMsg
```

Replicates `LocomotionChannel` from the behavior (brain) node to the physics (muscle) node.
This is a command channel — the muscle node executes the action on the physical model.

| Field | Type | DDS role | Description |
|---|---|---|---|
| `NetworkId` | `long` | `[DdsKey]` | Entity identifier |
| `ActiveAction` | `ushort` | data | Currently active locomotion action ID |
| `BehaviorInstanceId` | `uint` | data | Behavior instance governing this command |
| `ActionInstanceId` | `uint` | data | Unique action instance ID for preemption |

### `DemoWeaponMsg`

```
[DdsTopic("FDP.Demo_Weapon")]
public partial struct DemoWeaponMsg
```

Replicates `WeaponChannel` from the brain node to the turret / weapon physics node.
Mirrors the structure of `DemoLocomotionMsg` for the weapon subsystem.

| Field | Type | DDS role | Description |
|---|---|---|---|
| `NetworkId` | `long` | `[DdsKey]` | Entity identifier |
| `ActiveAction` | `ushort` | data | Currently active weapon action ID |
| `BehaviorInstanceId` | `uint` | data | Behavior instance governing this command |
| `ActionInstanceId` | `uint` | data | Unique action instance ID for preemption |

### `DemoCombatInteractionMsg`

```
[DdsTopic("FDP.Demo_CombatInteraction")]
public partial struct DemoCombatInteractionMsg
```

Broadcast fire/hit notification. Not keyed — each published sample is an independent event.

| Field | Type | DDS role | Description |
|---|---|---|---|
| `ShooterNetId` | `long` | data | Network ID of the firing entity |
| `TargetNetId` | `long` | data | Network ID of the target entity |
| `IsHit` | `bool` | data | `true` when the projectile successfully hit |
| `Damage` | `float` | data | Damage applied in hit points |

---

## Dependencies

### NuGet packages

| Package | Version | Purpose |
|---|---|---|
| `CycloneDDS.NET` | 0.2.2 | Provides `[DdsTopic]`, `[DdsKey]`, `DdsWriter<T>`, `DdsReader<T>`, `DdsParticipant` |

### Project references

None. This is a pure schema library with no FDP engine dependencies.

### Dependents (who references this project)

| Project | Use |
|---|---|
| `Fdp.Examples.Scenarios` | Imports message types for `DistributedTankScenario` and `UrbanCombatNewScenario` |
| `Fdp.Examples.UrbanCombat` | Indirectly, through `Fdp.Examples.Scenarios` reference |

---

## Usage Examples

### Example 1 — Publishing a spawn event

```csharp
using CycloneDDS.Runtime;
using Fdp.Examples.DDS;

// Initialize DDS participant for domain 0
using var participant = new DdsParticipant(domainId: 0);
using var spawnWriter = new DdsWriter<DemoSpawnMsg>(participant);

// Announce entity with network ID 42 of TKB type 100 (CommandTank)
spawnWriter.Write(new DemoSpawnMsg
{
    NetworkId   = 42L,
    TkbType     = 100L,
    OwnerNodeId = 1,
    IsDestroyed = false,
});

// Later: remove the entity from all subscribers
spawnWriter.DisposeInstance(new DemoSpawnMsg { NetworkId = 42L });
```

### Example 2 — Subscribing to transform updates

```csharp
using CycloneDDS.Runtime;
using Fdp.Examples.DDS;

using var participant = new DdsParticipant(domainId: 0);
using var transformReader = new DdsReader<DemoTransformMsg>(participant);

// Poll-based read in a simulation loop
while (!cancellationToken.IsCancellationRequested)
{
    var samples = transformReader.Take();
    foreach (var sample in samples)
    {
        if (!sample.IsValid) continue;

        var msg = sample.Data;
        // Update local mirror of entity position
        entityMap.UpdateTransform(msg.NetworkId,
            position: new Vector3(msg.PosX, msg.PosY, msg.PosZ),
            rotation: new Quaternion(msg.RotX, msg.RotY, msg.RotZ, msg.RotW));
    }
    await Task.Delay(16); // ~60 Hz
}
```

### Example 3 — Replicating locomotion commands from brain to muscle

```csharp
using CycloneDDS.Runtime;
using Fdp.Examples.DDS;

// Brain node: publish locomotion command
using var locoWriter = new DdsWriter<DemoLocomotionMsg>(participant);

locoWriter.Write(new DemoLocomotionMsg
{
    NetworkId          = entityNetId,
    ActiveAction       = NavigationConstants.ActionIdFollowRoute,
    BehaviorInstanceId = behavior.InstanceId,
    ActionInstanceId   = behavior.CurrentActionInstanceId,
});

// Muscle node: read and apply
using var locoReader = new DdsReader<DemoLocomotionMsg>(participant);
var samples = locoReader.Take();
foreach (var s in samples)
{
    if (!s.IsValid) continue;
    ref var loco = ref world.GetComponentRW<LocomotionChannel>(
        entityMap.Resolve(s.Data.NetworkId));
    loco.ActiveAction       = s.Data.ActiveAction;
    loco.BehaviorInstanceId = s.Data.BehaviorInstanceId;
    loco.ActionInstanceId   = s.Data.ActionInstanceId;
}
```

### Example 4 — Publishing a combat interaction (keyless broadcast)

```csharp
using CycloneDDS.Runtime;
using Fdp.Examples.DDS;

using var combatWriter = new DdsWriter<DemoCombatInteractionMsg>(participant);

// Publish each time a shot is resolved
combatWriter.Write(new DemoCombatInteractionMsg
{
    ShooterNetId = shooterNetId,
    TargetNetId  = targetNetId,
    IsHit        = true,
    Damage       = 75.5f,
});
```

---

## Best Practices

### 1. Use `long` for network IDs

All keyed fields use `long` (64-bit) network IDs, not ECS `Entity` handles. This is
intentional: ECS entity handles are process-local and version-stamped. A network ID
assigned by `DdsIdAllocatorServer` is globally unique across all nodes for the session
lifetime.

### 2. Separate schema from logic

The project contains no writer/reader construction, no polling loops, and no application
logic. Adding writer wrappers or polling infrastructure here would create tight coupling
between the schema definition and a particular node topology.

### 3. Partial structs for source-generator compatibility

All types are declared `partial`, enabling `CycloneDDS.NET`'s source generator to add
serialization methods in a separate generated partial. Do not remove `partial`.

### 4. Keyless vs. keyed topics

`DemoCombatInteractionMsg` is intentionally keyless (no `[DdsKey]`). Combat interactions
are fire-and-forget events. Using a key would create instances that require explicit
lifecycle management, which is unnecessary for transient events.

### 5. Flat quaternion storage

`DemoTransformMsg` stores all four quaternion components. Do not compress to Euler angles
on the wire — floating-point accumulation errors in reconstruction can cause divergence
between nodes.

### 6. ActionInstanceId for preemption

`DemoLocomotionMsg` and `DemoWeaponMsg` include `ActionInstanceId`. The muscle node must
compare this field to detect when a new command supersedes an in-progress action. Ignoring
it causes actions to be replayed on every topic sample update.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Network.Cyclone` | Provides `DdsIdAllocatorServer` and `DdsIdAllocatorClient` used alongside these messages |
| `Fdp.Examples.Scenarios` | Consumes these message types in `DistributedTankScenario` and `UrbanCombatNewScenario` |
| `Fdp.Examples.UrbanCombat` | Full standalone application using this schema |
| `Fdp.Toolkits` | Defines `LocomotionChannel`, `WeaponChannel` that these messages mirror over DDS |
| `Fdp.Examples.IdAllocatorDemo` | Shows how to run a `DdsIdAllocatorServer` that hands out `NetworkId` values |

---

## Architecture Deep Dive

### Why a Separate Schema Project

Keeping message type definitions in a dedicated library (with no FDP engine dependency) solves
several practical problems in a distributed simulation:

1. **No circular references** — Brain node, muscle node, and visualization nodes all depend on
   this library without depending on each other.
2. **Language server discovery** — IDEs index `[DdsTopic]` annotations across the solution when
   they live in a shared project, enabling "Find All References" to show every place a topic is
   published or subscribed.
3. **Independent versioning** — Message schemas evolve on their own cadence, independent of
   toolkit or application release cycles.

### Topic QoS Not Set Here

The `[DdsTopic]` attributes in this library do NOT include `[DdsQos]`. QoS policy
(Reliability, Durability, History) is a deployment concern, not a schema concern:

- **Transform** topics (high frequency, position): typically BestEffort + Volatile + KeepLast(1)
- **Spawn / Lifecycle** topics (critical, infrequent): typically Reliable + TransientLocal + KeepLast(1)
- **Command** topics (locomotion, weapon): typically Reliable + Volatile + KeepLast(1)

QoS is applied by the writer/reader constructor in the consuming application.

### Wire Size Estimates

| Message type | Fields | Approx wire bytes (CDR encoding) |
|---|---|---|
| `DemoSpawnMsg` | 4 fields | ~32 bytes |
| `DemoTransformMsg` | 8 fields | ~36 bytes |
| `DemoLocomotionMsg` | 4 fields | ~18 bytes |
| `DemoWeaponMsg` | 4 fields | ~18 bytes |
| `DemoCombatInteractionMsg` | 4 fields | ~21 bytes |

At 60 Hz with 10 entities: `DemoTransformMsg` ~21 KB/s, all other topics combined ~5 KB/s.
These are negligible compared to typical gigabit LAN capacity.

### Versioning Strategy

If a field must be added to an existing message type, prefer adding it at the end of the
struct. CycloneDDS CDR encoding is positional; appending a field does not break existing
readers (they ignore trailing bytes). Removing or reordering fields is a breaking change
requiring a topic name version bump (e.g., `FDP.Demo_Transform_v2`).

### Distributed Scenario Context

The two primary consumers — `DistributedTankScenario` and `UrbanCombatNewScenario` — run
all nodes within a single process during automated tests. In production deployments these
scenarios may run across two or more physical machines:

```
Machine A (Brain)                       Machine B (Muscle)
+---------------------------+            +---------------------------+
|  DistributedTankScenario  |            |  DistributedTankScenario  |
|  role: brain              |            |  role: muscle             |
|                           |  Ethernet  |                           |
|  DdsWriter<DemoLocomotion>|----------->|  DdsReader<DemoLocomotion>|
|  DdsWriter<DemoWeapon>    |----------->|  DdsReader<DemoWeapon>    |
|  DdsWriter<DemoSpawn>     |----------->|  DdsReader<DemoSpawn>     |
|  DdsReader<DemoTransform> |<-----------|  DdsWriter<DemoTransform> |
|  DdsReader<DemoCombat>    |<-----------|  DdsWriter<DemoCombat>    |
+---------------------------+            +---------------------------+
         Both link to Fdp.Examples.DDS.dll
```

CycloneDDS auto-discovers peers on the same domain via UDP multicast — no broker or
nameserver required. Domain ID isolation (e.g., domain 0 for production, domain 99 for
tests) prevents message leakage between concurrent simulation sessions.

### ActionInstanceId Preemption Protocol

`DemoLocomotionMsg` and `DemoWeaponMsg` carry `ActionInstanceId` in addition to
`BehaviorInstanceId`. The muscle node implements the following comparison each tick when
reading locomotion samples:

```
if sample.BehaviorInstanceId == loco.BehaviorInstanceId
    AND sample.ActionInstanceId == loco.ActionInstanceId:
        # same action still running, no preemption needed
else:
    # new behavior or new action instance -- preempt and restart
    loco.ActiveAction       = sample.ActiveAction
    loco.BehaviorInstanceId = sample.BehaviorInstanceId
    loco.ActionInstanceId   = sample.ActionInstanceId
```

This avoids replaying the action startup sequence every time the topic is sampled, while
still detecting preemptions correctly when the brain issues a new action mid-execution.

### Adding a New Message Type

To add a new DDS message type to this library:

```csharp
// 1. Create a new file, e.g. DemoSensorReadingMsg.cs:
using CycloneDDS.Schema;

namespace Fdp.Examples.DDS
{
    [DdsTopic("FDP.Demo_SensorReading")]
    public partial struct DemoSensorReadingMsg
    {
        [DdsKey]
        public long SensorEntityId;

        [DdsKey]
        public long TargetEntityId;

        public float Range;
        public float Bearing;
        public float Confidence;
    }
}
// 2. No registration or DI wiring needed -- the [DdsTopic] attribute is
//    picked up by CycloneDDS.NET source generators at build time.
// 3. Reference the new type directly in DdsWriter<DemoSensorReadingMsg>
//    or DdsReader<DemoSensorReadingMsg> in the consuming node.
```
