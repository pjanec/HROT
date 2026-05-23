# Hrot.AI.Behaviors

**Project path:** `Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj`
**Assembly:** `Hrot.AI.Behaviors`
**Target framework:** net8.0
**Date:** 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the primary
architectural reference.

---

## Executive Overview

`Hrot.AI.Behaviors` is the runtime AI behavior execution library for the HROT
military combat simulation. It is a pure C# class library (no executable, no
entry point) that:

1. **Defines behavior trees (BTree)** using the FastBTree (`Fbt.*`) fluent compiler
   API and the `[BTreeDefinition]` / `[BTreeAction]` / `[BTreeCondition]` source-
   generator attributes.
2. **Defines one hierarchical state machine (HSM)** using the FastHSM
   (`Fhsm.*`) compiler for the trivial `Idle` state.
3. **Registers all compiled behavior blobs** into the simulation's
   `BehaviorRegistry` via the single entry point `AiBehaviorFactory`.
4. **Maps high-level tactical intents** (e.g. `"DefendArea"`, `"HullDownAttack"`)
   to concrete behavior assignments via `ITacticalOrderMapper` implementations.
5. **Provides structured diagnostic logging** and a debug gizmo overlay for
   visualising AI world-state during simulation.

The library implements eight behaviors across two tiers:

| ID   | Name              | Tier  | Roles                            |
|------|-------------------|-------|----------------------------------|
| 3001 | MoveToLocation    | BTree | All ground entities              |
| 3002 | FollowRoute       | BTree | All ground entities              |
| 3003 | JoinFormation     | BTree | All ground entities              |
| 3010 | Idle              | HSM   | All entities (default state)     |
| 3011 | WanderMilitary    | BTree | All ground entities              |
| 3012 | FireAtTarget      | BTree | Combat entities                  |
| 3013 | HullDownAttackRun | BTree | Tank subordinates                |
| 3014 | PlatoonHillAttack | BTree | Commander entities               |

---

## Architecture

### Runtime Execution Model

The simulation ticks behaviors through an ECS (Entity-Component-System) world.
Each entity that has an active behavior carries:

- `BrainBlackboard` -- a fixed-size struct holding the current behavior's
  parameter DTO (60-byte param region) and a separate 1024-byte heavy-state
  component (`Blackboard1024`) for complex commander behaviors.
- `BehaviorState` -- the current behavior hash (integer ID), behavior instance
  counter, and tick counter.
- `LocomotionChannel` / `WeaponChannel` -- write-only command channels consumed
  by downstream executor systems.

The behavior interpreter is `Interpreter<BrainBlackboard, BTreeContext>` from
FastBTree. Each simulation tick the ECS calls `Interpreter.Tick()`, which walks
the pre-compiled BTree blob and dispatches action/condition delegates registered
in the `ActionRegistry<BrainBlackboard, BTreeContext>`.

Action delegates write to `LocomotionChannel` or `WeaponChannel`; they never
directly move entities. Locomotion and weapon executor systems consume those
channels on the same or next tick.

### Two-Phase Registration

`AiBehaviorFactory.BuildRegistrationAction()` separates CPU-intensive work from
main-thread work:

```
Background thread                    Main thread
--------------------                 -----------------------
FbtActionRegistrar.RegisterAll()     registry.Register(id, name, def)
FbtTreeCatalog.GetXxx()              registry.Register(...)
HsmBuilder.Build() / Emit()          ...
return Action<BehaviorRegistry>  --> DrainPendingCallbacks()
```

This design allows hot-reload of the assembly (via `FbtAssemblyHotReloader`)
without stalling the 60 Hz simulation loop.

### Tactical Intent Pipeline

High-level intent events (`AssignTacticalIntentEvent`) flow from commander
behavior trees through mapper classes to concrete `AssignBehaviorEvent` records:

```
Commander BTree
  publishes AssignTacticalIntentEvent { IntentId, JsonParams }
      |
      v
TacticalIntentResolutionSystem
      |
      +--> ITacticalOrderMapper.TryMap()
      |         DefendAreaMapper      -> "ConvoyEscort" / "InfantryCombat"
      |         HullDownAttackMapper  -> "HullDownAttackRun" (tanks only)
      |
      v
BehaviorIngressSystem
  parses JsonParams -> BrainBlackboard.BehaviorParameters
  activates Interpreter
```

### Blackboard Memory Layout

All parameter DTOs are `[StructLayout(LayoutKind.Sequential)]` structs written
directly into `BrainBlackboard.BehaviorParameters` via `Unsafe.Write`. The source
generator (`Fbt.SourceGen`) computes byte offsets at compile time and emits
bridge closures in `FbtActionRegistrar.g.cs` that project the runtime
`BrainBlackboard` to the typed DTO using `Unsafe.As`.

---

## ASCII Block Diagrams

### Diagram 1: Assembly Dependency Graph

```
+----------------------------+
|   Hrot.AI.Behaviors        |  net8.0 class library
+----------------------------+
   |        |        |      |
   |        |        |      +-- Hrot.Core
   |        |        |             (TkbEntityTypes)
   |        |        |
   |        |        +--------- Fdp.Core
   |        |                      (Entity, EntityRepository)
   |        |
   |        +------------------ Fdp.Toolkits
   |                               (BrainBlackboard, BTreeContext,
   |                                LocomotionChannel, WeaponChannel,
   |                                BehaviorRegistry, BehaviorDefinition,
   |                                NavigationConstants, MoveToParams,
   |                                TargetMemory, WeaponState, ...)
   |
   +-- Fbt.Compiler  (BTreeBuilder<TBB, TCtx>)
   +-- Fhsm.Compiler (HsmBuilder, HsmCompiler)
   +-- Fhsm.Kernel   (HsmDefinitionBlob, HsmEmitter, ...)
   |
   +-- [Analyzer] Fdp.Toolkits.Analyzers
   |      emits: FbtActionRegistrar.g.cs
   |             FbtTreeCatalog.g.cs
   |             HsmActionRegistrar.g.cs
   |             GizmoRegistrar.g.cs
   |
   +-- [Analyzer] Hrot.Blueprints.Generators
          emits: *.g.cs from *.bp.json AdditionalFiles
```

### Diagram 2: Behavior Registration Flow

```
+-------------------------+        +-----------------------+
|  AiBehaviorFactory      |        |  FbtActionRegistrar   |
|  (static)               |        |  (source-generated)   |
+-------------------------+        +-----------------------+
| RegisterAll() -----------+------> RegisterAll(registry)  |
|                          |        +-------+---------------+
| BuildRegistrationAction()|                | bridge closures
|   [background thread]    |                v
|   1. create ActionRegistry              ActionRegistry
|   2. RegisterAll(actions)              <BrainBlackboard,
|   3. GetXxx() BTree blobs               BTreeContext>
|   4. Build/Emit HSM blob                                  |
|   5. return Action<BehaviorRegistry>                      |
|                          |                                |
|   [main thread] -----+---+   +---------------------------+
|                       |       |  FbtTreeCatalog           |
|   registry.Register() +------>|  (source-generated)       |
|   (8 behaviors)       |       |  GetMoveToLocation()      |
+-------------------------+     |  GetFollowRoute()         |
                                |  GetHullDownAttackRun()   |
                                |  GetPlatoonHillAttack()   |
                                |  ...                      |
                                +---------------------------+
```

### Diagram 3: PlatoonHillAttack Commander BTree

```
+------------------------------------------------------------+
|  BTree: PlatoonHillAttack (ID 3014)                        |
|  TBlackboard = PlatoonHillAttackBlackboard                 |
+------------------------------------------------------------+
|  Sequence                                                  |
|    +-- Action_CalculateSegments                            |
|    |     Computes TotalSlots from segment / TankSpacing    |
|    |     Zeroes all bitmasks in HillAttackMutableState     |
|    |                                                       |
|    +-- Action_DispatchAllToBaseline                        |
|    |     Reads UnitRoster; publishes MoveToLocation intent |
|    |     for every alive subordinate                       |
|    |                                                       |
|    +-- Condition_AreAllAtBaseline                          |
|    |     Polls NavigationStatus; Running until all arrive  |
|    |                                                       |
|    +-- Repeater(-1)  <---- loops until Sequence fails      |
|         Sequence                                           |
|           +-- Action_RequestAreaQuery                      |
|           |     Submits AreaQueryBatchHelper request       |
|           |                                                |
|           +-- Condition_IsAreaQueryResolved               |
|           |     Polls result; Failure = area clear         |
|           |     (breaks Repeater, behavior ends)           |
|           |                                                |
|           +-- Action_DispatchWaveWithTargets               |
|           |     Assigns firing/baseline slots per tank     |
|           |     Publishes HullDownAttack intent per tank   |
|           |     Toggles CurrentWave (0 <-> 1)             |
|           |                                                |
|           +-- Condition_IsWaveCompleted                   |
|                 Monitors BehaviorState.ActiveBehaviorHash |
|                 Running until all attackers return         |
+------------------------------------------------------------+
```

### Diagram 4: HullDownAttackRun Subordinate Tank BTree

```
+-------------------------------------------------------+
|  BTree: HullDownAttackRun (ID 3013)                   |
|  TBlackboard = HullDownAttackBlackboard               |
+-------------------------------------------------------+
|  Sequence                                             |
|    +-- Selector                                       |
|    |     +-- Sequence  (engagement path)              |
|    |     |     +-- Action_CreepToAndBeyondSlot        |
|    |     |     |     Phase 1 (far):  MoveTo slot      |
|    |     |     |       at ApproachSpeed               |
|    |     |     |     Phase 2 (near): MoveTo far pt    |
|    |     |     |       at CreepSpeed                  |
|    |     |     |     Failure on overshoot > 50m       |
|    |     |     |                                      |
|    |     |     +-- Action_AimAndFireSpecific           |
|    |     |           Resolves TargetNetworkId         |
|    |     |           Writes AimAndFire to WeaponChannel|
|    |     |           Success when target destroyed    |
|    |     |                                            |
|    |     +-- Action_AbortEngagement                   |
|    |           Always Success (overshoot fallback)    |
|    |                                                  |
|    +-- Action_ReverseToBaseline                       |
|          MoveTo (BaselineX, BaselineY) reverse=1      |
|          Success on arrival; publishes ClearBehavior  |
+-------------------------------------------------------+
```

### Diagram 5: Tactical Intent Mapper Chain

```
+----------------------+    AssignTacticalIntentEvent
| Commander BTree Node |----> Bus.PublishManaged()
| (e.g. Action_        |       IntentId = "HullDownAttack"
|  DispatchWave...)    |       JsonParams = <HullDownAttackParams JSON>
+----------------------+
                                      |
                                      v
                      +------------------------------+
                      | TacticalIntentResolutionSys  |
                      | (Hrot.CGF, not in this proj) |
                      +------------------------------+
                              |           |
                    +---------+           +-----------+
                    v                                 v
         +--------------------+          +----------------------+
         | DefendAreaMapper   |          | HullDownAttackMapper |
         | "DefendArea"       |          | "HullDownAttack"     |
         | APC -> ConvoyEscort|          | Tank -> HullDown     |
         | Inf -> InfantryCbt |          | AttackRun            |
         +--------------------+          +----------------------+
                    |                                 |
                    v                                 v
             AssignBehaviorEvent              AssignBehaviorEvent
             BehaviorName=...                 BehaviorName=
                                              "HullDownAttackRun"
```

---

## Source Structure

### Namespace: `Hrot.AI.Behaviors`

| File | Class / Type | Description |
|------|-------------|-------------|
| `AiBehaviorFactory.cs` | `AiBehaviorFactory` (static) | Entry point; compiles all BTree/HSM blobs and registers `BehaviorDefinition` records |
| `Brains/CgfHsmNodes.cs` | `CgfHsmNodes` (internal static unsafe) | HSM action delegate for Idle state |

### Namespace: `Hrot.AI.Behaviors.Brains`

| File | Class / Type | Description |
|------|-------------|-------------|
| `Brains/CgfNodes.cs` | `CgfNodes` (public static) | BTree action/condition/definition nodes for MoveTo, FollowRoute, JoinFormation, WanderMilitary, FireAtTarget |
| `Brains/CgfNodes.cs` | `CgfNodes.MoveToBlackboard` | Typed blackboard wrapper for MoveToLocation |
| `Brains/CgfNodes.cs` | `CgfNodes.FollowRouteBlackboard` | Typed blackboard wrapper for FollowRoute |
| `Brains/CgfNodes.cs` | `CgfNodes.JoinFormationBlackboard` | Typed blackboard wrapper for JoinFormation |
| `Brains/CgfNodes.cs` | `CgfNodes.FireAtTargetBlackboard` | Typed blackboard wrapper for FireAtTarget |
| `Brains/CgfNodes.cs` | `CgfNodes.MoveToLocationParams` | Blackboard DTO (X, Y, Speed, ArrivalRadius) |
| `Brains/CgfNodes.cs` | `CgfNodes.FollowRouteParams` | Blackboard DTO (TrajectoryId, Speed, Loop) |
| `Brains/CgfNodes.cs` | `CgfNodes.JoinFormationParams` | Blackboard DTO (LeaderNetworkId, FormationTypeId) |
| `Brains/CgfNodes.cs` | `CgfNodes.FireAtTargetParams` | Blackboard DTO (TargetPacked, MaxRounds, CooldownSeconds, RoundsFired) |
| `Brains/CommanderNodes.cs` | `CommanderNodes` (public static) | BTree action node for IssueTacticalIntent; reference implementation |
| `Brains/CommanderNodes.cs` | `CommanderNodes.IssueTacticalIntentBlackboard` | Typed blackboard wrapper |
| `Brains/CommanderNodes.cs` | `CommanderNodes.IssueTacticalIntentParams` | Blackboard DTO (SubordinatePacked, IntentTypeOrdinal) |
| `Brains/HillAttackDtos.cs` | `PlatoonHillAttackParams` (struct) | Static config for commander (52 bytes: firing-line, baseline, attack direction, spacing) |
| `Brains/HillAttackDtos.cs` | `PlatoonHillAttackBlackboard` (struct) | Single-field blackboard wrapper |
| `Brains/HillAttackDtos.cs` | `HillAttackMutableState` (unsafe struct) | Mutable working state projected onto `Blackboard1024` (120 bytes; SoA per-attacker arrays) |
| `Brains/HillAttackDtos.cs` | `HullDownAttackParams` (struct) | Per-tank config (52 bytes: slot position, baseline, attack dir, speeds, target, rounds) |
| `Brains/HillAttackDtos.cs` | `HullDownAttackBlackboard` (struct) | Single-field blackboard wrapper |
| `Brains/HillAttackCommanderNodes.cs` | `HillAttackCommanderNodes` (public static unsafe) | All BTree node delegates for PlatoonHillAttack plus `BuildPlatoonHillAttackTree()` |
| `Brains/HillAttackTankNodes.cs` | `HillAttackConstants` (public static) | Named constants: MaxOvershootMeters=50, SlotArrivalThresholdMeters=15, CreepLookAheadMeters=10000 |
| `Brains/HillAttackTankNodes.cs` | `HillAttackTankNodes` (public static) | BTree node delegates for HullDownAttackRun plus `BuildHullDownAttackRunTree()` |

### Namespace: `Hrot.AI.Behaviors.Logging`

| File | Class / Type | Description |
|------|-------------|-------------|
| `Logging/BehaviorLog.cs` | `BehaviorLog` (public static) | NLog-backed structured logger for all behavior nodes; emits to logger `"AI.Behavior"` |
| `Logging/BehaviorTraceLogEmitter.cs` | `BehaviorTraceLogEmitter` (public sealed) | Implements `IBehaviorTraceLogEmitter`; wraps `BehaviorLog.Trace()` |

### Namespace: `Hrot.AI.Behaviors.Mappers`

| File | Class / Type | Description |
|------|-------------|-------------|
| `Mappers/DefendAreaMapper.cs` | `DefendAreaMapper` (public sealed) | Maps `"DefendArea"` intent to `"ConvoyEscort"` (APC) or `"InfantryCombat"` (infantry) |
| `Mappers/HullDownAttackMapper.cs` | `HullDownAttackMapper` (public sealed) | Maps `"HullDownAttack"` intent to `"HullDownAttackRun"` for M1 Abrams, Bradley, T-72 |

### Namespace: `Hrot.AI.Behaviors.Gizmos`

| File | Class / Type | Description |
|------|-------------|-------------|
| `Gizmos/HillAttackGizmo.cs` | `HillAttackGizmo` (public sealed) | Stateless gizmo; draws firing-line (blue) and baseline (green) for PlatoonHillAttack entities |
| `Gizmos/HillAttackGizmoSettings.cs` | `HillAttackGizmoSettings` (internal static) | Registers `"HillAttack.ShowSlots"` toggle setting (default: true) |

---

## Public API Reference

### `AiBehaviorFactory` (static)

```csharp
namespace Hrot.AI.Behaviors

[BlueprintRegistrar]
public static class AiBehaviorFactory
```

| Member | Signature | Description |
|--------|-----------|-------------|
| `RegisterAll` | `static unsafe void RegisterAll(BehaviorRegistry registry, IGeographicTransform? geoTransform, NetworkEntityMap? entityMap)` | One-shot registration; delegates to `BuildRegistrationAction`. Entry point for `AiHotReloadCoordinator`. |
| `BuildRegistrationAction` | `static unsafe Action<BehaviorRegistry> BuildRegistrationAction(IGeographicTransform? geoTransform, NetworkEntityMap entityMap)` | Compiles all BTree/HSM blobs on the calling thread; returns a lightweight delegate safe to call on the main thread. |

**Behavior ID constants** (private, but stable ABI):

| Constant | Value |
|----------|-------|
| `MoveTo_BT` | 3001 |
| `FollowRoute_BT` | 3002 |
| `JoinFormation_BT` | 3003 |
| `Idle_HSM` | 3010 |
| `WanderMilitary_BT` | 3011 |
| `FireAtTarget_BT` | 3012 |
| `HullDownAttackRun_BT` | 3013 |
| `PlatoonHillAttack_BT` | 3014 |

---

### `CgfNodes` (static)

```csharp
namespace Hrot.AI.Behaviors.Brains
public static class CgfNodes
```

**Typed blackboard wrappers** (all `[StructLayout(LayoutKind.Sequential)]`):

| Type | Field |
|------|-------|
| `MoveToBlackboard` | `MoveToLocationParams Params` |
| `FollowRouteBlackboard` | `FollowRouteParams Params` |
| `JoinFormationBlackboard` | `JoinFormationParams Params` |
| `FireAtTargetBlackboard` | `FireAtTargetParams Params` |

**Parameter DTOs:**

| Type | Fields |
|------|--------|
| `MoveToLocationParams` | `float X, Y, Speed, ArrivalRadius` |
| `FollowRouteParams` | `int TrajectoryId; float Speed; bool Loop` |
| `JoinFormationParams` | `int LeaderNetworkId; byte FormationTypeId` |
| `FireAtTargetParams` | `long TargetPacked; int MaxRounds; float CooldownSeconds; int RoundsFired` |

**Parse methods (unsafe, cold path):**

| Method | Description |
|--------|-------------|
| `ParseMoveToParams(string json, byte* ptr, IGeographicTransform geoTransform)` | Deserializes JSON; converts lat/lon to Cartesian if geo-context is available. Default speed: 15 m/s. |
| `ParseFollowRouteParams(string json, byte* ptr)` | Direct struct deserialization. |
| `ParseFireAtTargetParams(string json, byte* ptr, NetworkEntityMap entityMap)` | Resolves `TargetNetworkId` to packed entity value via entity map. |

**Action / Condition delegates** (`[BTreeAction]` / `[BTreeCondition]`):

| Method | Return | Description |
|--------|--------|-------------|
| `Action_WriteMoveToChannel(ref MoveToLocationParams, ref BehaviorTreeState, ref BTreeContext)` | `NodeStatus` | Writes `MoveTo` to `LocomotionChannel`; forwards executor status. |
| `Action_WriteFollowRouteChannel(ref FollowRouteParams, ref BehaviorTreeState, ref BTreeContext)` | `NodeStatus` | Writes `FollowRoute` to `LocomotionChannel`. |
| `Action_WriteJoinFormationChannel(ref JoinFormationParams, ref BehaviorTreeState, ref BTreeContext)` | `NodeStatus` | Writes `JoinFormation` to `LocomotionChannel`. |
| `Action_Wander(ref BrainBlackboard, ref BehaviorTreeState, ref BTreeContext, int)` | `NodeStatus` | Picks random destination within 1000 m radius; always returns Running. |
| `Condition_TargetAliveAndVisible(ref FireAtTargetParams, ref BehaviorTreeState, ref BTreeContext)` | `NodeStatus` | Success=visible, Running=alive but unseen, Failure=dead. |
| `Action_FireAtTarget(ref FireAtTargetParams, ref BehaviorTreeState, ref BTreeContext)` | `NodeStatus` | Writes `AimAndFire` to `WeaponChannel`; counts rounds via `WeaponState.CooldownSecondsRemaining`. |
| `Action_HoldPosition(ref BrainBlackboard, ref BehaviorTreeState, ref BTreeContext, int)` | `NodeStatus` | Always Running; holds entity in place. |

**BTree definition methods** (`[BTreeDefinition("Name")]`):

| Method | Tree Name |
|--------|-----------|
| `BuildMoveToLocationTree()` | `"MoveToLocation"` |
| `BuildFollowRouteTree()` | `"FollowRoute"` |
| `BuildJoinFormationTree()` | `"JoinFormation"` |
| `BuildWanderMilitaryTree()` | `"WanderMilitary"` |
| `BuildFireAtTargetTree()` | `"FireAtTarget"` |

---

### `HillAttackCommanderNodes` (public static unsafe)

```csharp
namespace Hrot.AI.Behaviors.Brains
public static unsafe class HillAttackCommanderNodes
```

| Method | Description |
|--------|-------------|
| `Action_CalculateSegments(ref PlatoonHillAttackParams, ...)` | Computes `TotalSlots` (capped at 16); zeroes all `HillAttackMutableState` bitmasks. |
| `Action_DispatchAllToBaseline(ref PlatoonHillAttackParams, ...)` | Iterates `UnitRoster`; publishes `AssignTacticalIntentEvent` with `"MoveToLocation"` for each subordinate. |
| `Condition_AreAllAtBaseline(ref PlatoonHillAttackParams, ...)` | Polls `NavigationStatus` of all subordinates; returns Running until all have a non-InProgress result. |
| `Action_RequestAreaQuery(ref PlatoonHillAttackParams, ...)` | Submits `AreaQueryBatchHelper.RequestAreaQuery`; caches request ID; guards against duplicate submission. |
| `Condition_IsAreaQueryResolved(ref PlatoonHillAttackParams, ...)` | Polls batch result; 5-second timeout; Failure if area clear; Success caches `TargetGroupHandle`. |
| `Action_DispatchWaveWithTargets(ref PlatoonHillAttackParams, ...)` | Assigns firing/baseline slots per attacker; publishes `"HullDownAttack"` intent; toggles `CurrentWave`. |
| `Condition_IsWaveCompleted(ref PlatoonHillAttackParams, ...)` | Monitors `BehaviorState.ActiveBehaviorHash` of each attacker; burns slots on death; swap-removes completed. |
| `ParsePlatoonHillAttackParams(string json, byte* ptr, IGeographicTransform?, NetworkEntityMap)` | Deserializes JSON; converts geodetic to Cartesian; computes attack direction automatically. |
| `BuildPlatoonHillAttackTree()` | `[BTreeDefinition("PlatoonHillAttack")]` BTree builder. |

---

### `HillAttackTankNodes` (public static)

```csharp
namespace Hrot.AI.Behaviors.Brains
public static class HillAttackTankNodes
```

| Method | Description |
|--------|-------------|
| `Condition_HasTarget(ref HullDownAttackParams, ...)` | Resolves `TargetNetworkId` via `NetworkEntityMap`; scans `TargetMemory` for positive threat score. |
| `Action_CreepToAndBeyondSlot(ref HullDownAttackParams, ...)` | Two-phase movement: approach at `ApproachSpeed`, then creep at `CreepSpeed`; Failure on overshoot > 50 m. |
| `Action_AimAndFireSpecific(ref HullDownAttackParams, ...)` | Resolves target entity; writes `AimAndFire` to `WeaponChannel`; tracks rounds via ammo delta. |
| `Action_ReverseToBaseline(ref HullDownAttackParams, ...)` | Reverse `MoveTo (BaselineX, BaselineY)`; publishes `ClearBehaviorEvent` on arrival. |
| `Action_AbortEngagement(ref HullDownAttackParams, ...)` | Always Success; overshoot fallback node in Selector. |
| `ParseHullDownAttackParams(string json, byte* ptr)` | Deserializes JSON; resets `RoundsFired=0`, `LastObservedAmmo=-1`. |
| `BuildHullDownAttackRunTree()` | `[BTreeDefinition("HullDownAttackRun")]` BTree builder. |

---

### `CommanderNodes` (public static)

```csharp
namespace Hrot.AI.Behaviors.Brains
public static class CommanderNodes
```

| Method | Description |
|--------|-------------|
| `Action_IssueTacticalIntent(ref IssueTacticalIntentParams, ref BehaviorTreeState, ref BTreeContext)` | Publishes `AssignTacticalIntentEvent` for a single subordinate; returns Failure if `SubordinatePacked == 0`. |

---

### `BehaviorLog` (public static)

```csharp
namespace Hrot.AI.Behaviors.Logging
public static class BehaviorLog
```

| Member | Signature | Description |
|--------|-----------|-------------|
| `IsDebugEnabled` | `bool` (property) | Guard for debug-level string allocations. |
| `IsTraceEnabled` | `bool` (property) | Guard for trace-level string allocations. |
| `IsWarnEnabled` | `bool` (property) | Guard for warn-level. |
| `IsErrorEnabled` | `bool` (property) | Guard for error-level. |
| `Debug(ref BTreeContext, string, [CallerMemberName])` | void | Logs debug with entity/behavior/node context. |
| `Trace(ref BTreeContext, string, [CallerMemberName])` | void | Logs trace with entity/behavior/node context. |
| `Warn(ref BTreeContext, string, [CallerMemberName])` | void | Logs warning with entity/behavior/node context. |
| `Error(ref BTreeContext, string, [CallerMemberName])` | void | Logs error with entity/behavior/node context. |
| `Debug(Entity, EntityRepository, string, [CallerMemberName])` | void | HSM / shared-AI overload. |
| `Trace(Entity, EntityRepository, string, [CallerMemberName])` | void | HSM / shared-AI overload. |
| `Warn(Entity, EntityRepository, string, [CallerMemberName])` | void | HSM / shared-AI overload. |
| `Error(Entity, EntityRepository, string, [CallerMemberName])` | void | HSM / shared-AI overload. |
| `ParseWarn(string, [CallerMemberName])` | void | Cold-path parse warning; no entity context. |
| `ParseError(string, [CallerMemberName])` | void | Cold-path parse error; no entity context. |

Log message format:
```
Entity:[{EntityId}] Behavior:[{BehaviorHash}] Node:[{ActionName}] | {UserMessage}
```

---

### `BehaviorTraceLogEmitter` (public sealed)

```csharp
namespace Hrot.AI.Behaviors.Logging
public sealed class BehaviorTraceLogEmitter : IBehaviorTraceLogEmitter
```

| Member | Description |
|--------|-------------|
| `IsTraceEnabled` | Delegates to `BehaviorLog.IsTraceEnabled`. |
| `EmitTrace(Entity, EntityRepository, string, string)` | Delegates to `BehaviorLog.Trace(Entity, ...)`. |

---

### `DefendAreaMapper` (public sealed)

```csharp
namespace Hrot.AI.Behaviors.Mappers
public sealed class DefendAreaMapper : ITacticalOrderMapper
```

| Member | Description |
|--------|-------------|
| `TargetIntentId` | `"DefendArea"` |
| `TryMap(Entity, EntityRepository, string, out AssignBehaviorEvent)` | APC -> `"ConvoyEscort"`, Infantry -> `"InfantryCombat"`, others -> false. |

---

### `HullDownAttackMapper` (public sealed)

```csharp
namespace Hrot.AI.Behaviors.Mappers
public sealed class HullDownAttackMapper : ITacticalOrderMapper
```

| Member | Description |
|--------|-------------|
| `TargetIntentId` | `"HullDownAttack"` |
| `TryMap(Entity, EntityRepository, string, out AssignBehaviorEvent)` | Tank (M1 Abrams, Bradley, T-72) -> `"HullDownAttackRun"`, others -> false. |

---

### `HillAttackGizmo` (public sealed)

```csharp
namespace Hrot.AI.Behaviors.Gizmos
[GizmoProjector(typeof(BrainBlackboard), typeof(BehaviorState), typeof(SimTransform))]
public sealed class HillAttackGizmo : IStatelessGizmo
```

| Member | Description |
|--------|-------------|
| `Draw(ISimulationView, Entity, IDebugDrawBuilder)` | Projects `BrainBlackboard` to `PlatoonHillAttackParams`; draws firing-line (blue) and baseline (green); optionally draws slot spheres and labels. |

---

## Dependencies

### Project References

| Referenced Project | Type | Purpose |
|-------------------|------|---------|
| `Fdp.Toolkits` | Runtime | Core runtime types: `BrainBlackboard`, `BTreeContext`, `LocomotionChannel`, `WeaponChannel`, `BehaviorRegistry`, `BehaviorDefinition`, `BehaviorState`, `NavigationConstants`, `MoveToParams`, `TargetMemory`, `WeaponState`, `CarKinem.*`, `Blackboard1024`, `UnitRoster`, etc. |
| `Fdp.Toolkits.Analyzers` | Roslyn Analyzer | Emits `FbtActionRegistrar.g.cs`, `FbtTreeCatalog.g.cs`, `HsmActionRegistrar.g.cs`, `GizmoRegistrar.g.cs` from `[BTreeAction]`, `[BTreeDefinition]`, `[HsmAction]`, `[GizmoProjector]` attributes. |
| `Fdp.Core` | Runtime | `Entity`, `EntityRepository`, `FixedString32`, serialization helpers. |
| `Hrot.Core` | Runtime | `TkbEntityTypes` constants for entity-type dispatch in mappers. |
| `Fbt.Compiler` | Runtime | `BTreeBuilder<TBlackboard, TContext>` fluent API for tree construction. |
| `Fhsm.Kernel` | Runtime | `HsmDefinitionBlob`, `HsmEmitter`, `HsmNormalizer`, `HsmFlattener`, `MachineMetadata`, `HsmCommandWriter`. |
| `Fhsm.Compiler` | Runtime | `HsmBuilder`, `HsmCompiler` for constructing the Idle HSM. |
| `Hrot.Blueprints.Generators` | Roslyn Analyzer | Emits C# from `*.bp.json` AdditionalFiles. |
| `Hrot.Blueprints.Compiler` | Roslyn Analyzer (dependency) | Blueprint generator netstandard2.0 dependency. |

### NuGet Packages

All packages are inherited transitively. Directly observable usages:

| Package | Usage |
|---------|-------|
| `NLog` | `BehaviorLog` uses `NLog.Logger` and `NLog.LogManager`. |
| `System.Text.Json` | All `Parse*Params` methods use `JsonSerializer.Deserialize`. |

### InternalsVisibleTo

`Hrot.IG.Tests` is granted access to internal types via `AssemblyAttribute`:
```csharp
[assembly: InternalsVisibleTo("Hrot.IG.Tests")]
```

---

## Usage Examples

### Example 1: Registering All Behaviors at Startup

```csharp
// In the CGF subsystem startup (CgfBehaviorSetup or similar):
using Hrot.AI.Behaviors;

// Option A: one-shot (called from the hot-reload coordinator callback on the main thread)
AiBehaviorFactory.RegisterAll(registry, geoTransform, entityMap);

// Option B: two-phase hot-reload (background thread compiles, main thread registers)
var applyAction = await Task.Run(() =>
    AiBehaviorFactory.BuildRegistrationAction(geoTransform, entityMap));

// Later on the main thread via DrainPendingCallbacks():
applyAction(behaviorRegistry);
```

### Example 2: Issuing a MoveToLocation Behavior from a Scenario Plan

```csharp
// Scenario plan JSON emitted by the editor:
string json = """
{
    "TargetLat": 48.1234,
    "TargetLon": 16.5678,
    "Speed": 12.0,
    "ArrivalRadius": 5.0
}
""";

// The behavior ingress system parses and activates:
// unsafe context inside BehaviorIngressSystem:
unsafe
{
    fixed (byte* ptr = &brainBlackboard.BehaviorParameters[0])
    {
        CgfNodes.ParseMoveToParams(json, ptr, geoTransform);
    }
}

// The BehaviorRegistry activates behavior ID 3001 on the entity.
// On subsequent ticks, Action_WriteMoveToChannel writes to LocomotionChannel
// and LocomotionDispatcherSystem moves the entity.
```

### Example 3: Commanding a Platoon Hill Attack (Commander Brain)

```csharp
// JSON authored in the scenario editor for PlatoonHillAttack:
string json = """
{
    "FiringLineStart": { "Latitude": 48.100, "Longitude": 16.200 },
    "FiringLineEnd":   { "Latitude": 48.101, "Longitude": 16.210 },
    "BaselineStart":   { "Latitude": 48.095, "Longitude": 16.200 },
    "BaselineEnd":     { "Latitude": 48.095, "Longitude": 16.210 },
    "TankSpacing": 30.0
}
""";

// The ingress system calls (unsafe context):
unsafe
{
    fixed (byte* ptr = &brainBlackboard.BehaviorParameters[0])
    {
        HillAttackCommanderNodes.ParsePlatoonHillAttackParams(
            json, ptr, geoTransform, entityMap);
    }
}

// AiBehaviorFactory registered behavior ID 3014 ("PlatoonHillAttack").
// The commander BTree ticks each frame:
//   1. Computes up to 16 firing slots spaced 30 m apart along the firing line.
//   2. Orders all subordinates in UnitRoster to move to baseline positions.
//   3. Waits for arrival.
//   4. Queries for hostile targets in the target area polygon.
//   5. Dispatches alternate waves of tanks to firing slots.
//   6. Each tank runs HullDownAttackRun (ID 3013) to advance, fire, and reverse.
//   7. Loop repeats until the area is cleared.
```

### Example 4: Registering a Tactical Order Mapper

```csharp
// In the composition root (e.g. HrotSubsystemInitializer):
using Hrot.AI.Behaviors.Mappers;

var mapperRegistry = services.GetRequiredService<TacticalOrderMapperRegistry>();
mapperRegistry.Register(new DefendAreaMapper());
mapperRegistry.Register(new HullDownAttackMapper());

// When a commander publishes:
//   AssignTacticalIntentEvent { IntentId = "HullDownAttack", JsonParams = "..." }
// TacticalIntentResolutionSystem iterates registered mappers and calls TryMap().
// HullDownAttackMapper returns true for tank entities, producing:
//   AssignBehaviorEvent { BehaviorName = "HullDownAttackRun", JsonParams = "..." }
```

### Example 5: Using BehaviorLog in a Custom Node

```csharp
using Hrot.AI.Behaviors.Logging;

[BTreeAction]
public static NodeStatus Action_MyCustomMove(
    ref MyParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    // Guard: only allocate the log string if the level is enabled.
    if (BehaviorLog.IsDebugEnabled)
        BehaviorLog.Debug(ref ctx, "Starting custom move to (" + p.X + "," + p.Y + ").");

    if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
    {
        BehaviorLog.Error(ref ctx, "Missing LocomotionChannel.");
        return NodeStatus.Failure;
    }

    // ... write channel ...
    return NodeStatus.Running;
}
```

---

## Best Practices

### AI Performance

**Avoid heap allocations in hot-path delegates.**
Every `[BTreeAction]` and `[BTreeCondition]` method is called at simulation rate
(typically 10-60 Hz per entity). Avoid `new`, `string` concatenation, LINQ,
closures, and boxing in these methods. Gate all log statements behind level
probes (`BehaviorLog.IsDebugEnabled`).

**Use `Unsafe.As` for blackboard projection, not pointer casts.**
The bridge closures in `FbtActionRegistrar.g.cs` use `Unsafe.As` to project
`BrainBlackboard` to a typed DTO. Do not bypass this by casting `&BehaviorParameters[0]`
directly in action methods -- the source generator owns that contract.

**Write channels only when the activation state changes.**
All action nodes in this assembly check `channel.ActiveAction != desiredAction
|| channel.Status == NodeStatus.Failure` before writing. Unnecessary writes
cause `ChannelArbitrationSystem` to treat the command as a new intent every frame
and reset executor state.

**Keep `HillAttackMutableState` within 1024 bytes.**
`Blackboard1024` has a fixed 1024-byte `ByteSize`. `HillAttackMutableState` is
120 bytes; the 8-entry SoA arrays are sized to `UnitRoster.MaxSubordinates / 2`.
Adding fields or growing arrays requires verifying the total size.

**Cap `TotalSlots` at 16.**
`BurnedSlotsMask`, `WaveUsedSlotsMask`, and `BaselineReservedMask` are all
`ushort` (16 bits). The `Action_CalculateSegments` node enforces `totalSlots <= 16`.

### Determinism

**Do not use `Random.Shared` in behaviors that require replay determinism.**
`Action_Wander` and `Action_DispatchWaveWithTargets` use `Random.Shared` (non-seeded).
If the simulation requires deterministic replay, replace these with a seeded
`Random` instance derived from the entity ID and simulation tick.

**Wave parity uses `Entity.Index`, not roster index.**
`Action_DispatchWaveWithTargets` uses `sub.Index % 2 != s.CurrentWave` to
partition tanks into waves. This is deterministic as long as entity indices are
stable across a session.

**Target network IDs are stable; local entity handles are not.**
All serialized params store `long TargetNetworkId`, not local `Entity.PackedValue`.
The packed handle is resolved at the first tick via `NetworkEntityMap.TryGetEntity`.
Never cache `Entity.PackedValue` across frames in a serialized param struct.

### Hot-Reload Safety

**Behavior integer IDs are stable ABI.**
IDs 3001-3014 mirror `CgfBehaviorIds` in `Hrot.CGF`. They are written into
scenario plans and replicated over the network. Never renumber a published ID.
Add new behaviors at unused IDs.

**`BuildRegistrationAction` must be idempotent.**
Each hot-reload call discards the old `ActionRegistry` and recompiles all blobs.
Ensure no static mutable state is captured in the returned delegate (other than
the explicitly passed `geoTransform` and `entityMap`).

**Keep BTree definition methods free of side effects.**
`[BTreeDefinition]` methods are called by `Fbt.SourceGen` at compile time for
static analysis and at runtime by `FbtTreeCatalog`. They must be pure builders
that return a new `BTreeBuilder` without writing to any shared state.

---

## Related Projects

| Project | Relationship |
|---------|-------------|
| `Hrot.CGF` | Consumer of `AiBehaviorFactory`; hosts `CgfBehaviorSetup`, `TacticalIntentResolutionSystem`, `BehaviorIngressSystem`. Mirrors `CgfNodes` and `CgfBehaviorIds`. |
| `Hrot.Core` | Provides `TkbEntityTypes` used by both mapper classes. |
| `Fdp.Toolkits` | Provides all ECS channel types, behavior framework contracts (`BrainBlackboard`, `BehaviorRegistry`, `BehaviorDefinition`, `ITacticalOrderMapper`, etc.), and toolkit navigation/combat types. |
| `Fdp.Toolkits.Analyzers` | Roslyn source generator that produces `FbtActionRegistrar.g.cs`, `FbtTreeCatalog.g.cs`, `HsmActionRegistrar.g.cs`, `GizmoRegistrar.g.cs`. These generated files are written to `obj/GeneratedFiles` for debugger source resolution. |
| `FDP/ExtDeps/FastBTree` | BTree runtime: `Interpreter<TBlackboard, TContext>`, `ActionRegistry`, `NodeStatus`, `BTreeContext`, `[BTreeAction]`, `[BTreeCondition]`, `[BTreeDefinition]`. |
| `FDP/ExtDeps/FastHSM` | HSM runtime and compiler: `HsmBuilder`, `HsmEmitter`, `HsmDefinitionBlob`, `MachineMetadata`, `HsmActionDispatcher`. |
| `Hrot.Blueprints.Generators` | Roslyn analyzer that processes `*.bp.json` AdditionalFiles and emits C# behavior blueprint code. No `.bp.json` files are currently present in this project. |
| `Hrot.IG.Tests` | Test assembly granted internal access via `InternalsVisibleTo`. Contains integration tests for AI behaviors. |
| `Hrot.Subsystems.Editor` | Hosts `FbtAssemblyHotReloader` which calls `BuildRegistrationAction` on a background thread and stages the result for main-thread application. |

---

## Source Generator Output

The Roslyn analyzers emit the following generated files to
`obj/GeneratedFiles` (visible in debugger and checked into no VCS):

| Generated File | Emitter | Contents |
|---------------|---------|----------|
| `FbtActionRegistrar.g.cs` | `Fdp.Toolkits.Analyzers` | `RegisterAll(ActionRegistry<BrainBlackboard, BTreeContext>)` with bridge closures for every `[BTreeAction]` / `[BTreeCondition]` in the assembly. |
| `FbtTreeCatalog.g.cs` | `Fdp.Toolkits.Analyzers` | `Get<Name>()` methods that lazily compile and cache BTree blobs for each `[BTreeDefinition]` method. |
| `HsmActionRegistrar.g.cs` | `Fdp.Toolkits.Analyzers` | `RegisterAll()` for every `[HsmAction]` delegate. |
| `GizmoRegistrar.g.cs` | `Fdp.Toolkits.Analyzers` | Registration glue for `[GizmoProjector]`-annotated gizmo classes. |

The project sets `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>`
and `<DebugType>portable</DebugType>` so that step-through debugging of generated
code works in Visual Studio and VS Code without additional configuration.

---

## Behavior Lifecycle Summary

```
Startup / Hot-Reload
  AiBehaviorFactory.BuildRegistrationAction()
    -> compiles 7 BTree blobs via FbtTreeCatalog
    -> builds 1 HSM blob via HsmBuilder/HsmEmitter
    -> returns Action<BehaviorRegistry>
  [main thread] applyAction(behaviorRegistry)
    -> 8 BehaviorDefinition records stored in registry

Per-Entity Activation (BehaviorIngressSystem)
  ParseParams(json, ptr)       -- cold path; once per behavior assignment
  Interpreter.Initialize()     -- set root node state
  BehaviorState.ActiveBehaviorHash = behaviorId

Per-Tick (BrainTickSystem, ~10-60 Hz)
  Interpreter.Tick(ref brainBlackboard, ref behaviorTreeState, ref btreeContext)
    -> walks compiled BTree blob
    -> dispatches action/condition delegates via ActionRegistry
    -> delegates write to LocomotionChannel / WeaponChannel

Downstream (same tick or next)
  LocomotionDispatcherSystem reads LocomotionChannel -> moves entity
  WeaponFireSystem reads WeaponChannel -> fires weapon

Completion
  Node returns Success or Failure
  BehaviorFinishedEvent published
  Next behavior activated or entity idles
```
