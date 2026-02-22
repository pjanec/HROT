# Behavior Control Subsystem – Design Document

**Project:** FDP Behavior Control & Urban Combat Demo  
**Source:** See [Behavior Control Subsystem Design.json.md](../../Behavior Control Subsystem Design.json.md) (design talk, all 4802 lines)  
**Status:** Design-complete, implementation pending

---

## 1. Vision & Goals

Add a complete **entity behavior control subsystem** to FDP covering:

- A two-tier AI architecture: cheap hardcoded C# logic for background traffic, and full VM-driven (BTree/HSM) decision-making for tactical entities.
- Generic, reusable toolkits following FDP philosophy: zero-allocation hot path, data-oriented ECS, no managed heap on the simulation loop.
- A headless demo app **"Urban Ambush"** showcasing all features in a single-node, console-printable simulation suitable for AI-agent-assisted development.
- The infrastructure must be extensible toward future distributed nodes, navmesh planning, complex weapons, cooperative AI, etc. without architectural changes.

> Design talk reference: lines 1–220 (problem statement, toolkit separation strategy)

---

## 2. Toolkit Separation Strategy

The subsystem spans **five new toolkits** and one demo application:

| Package | Responsibility |
|---|---|
| `FDP.Toolkit.Behavior` | Brain orchestration: doctrine lifecycle, BT/HSM adapters, universal action channels, dispatchers |
| `FDP.Toolkit.Perception` | Senses: audio events, async vision broadphase, target memory |
| `FDP.Toolkit.Navigation` | Locomotion executor bridge: translates `LocomotionChannel` intents to `CarKinem.NavState` |
| `FDP.Toolkit.Combat` | Weapons: aim/fire executor, ballistics, damage |
| `FDP.Toolkit.Physics` | 2D batch raycast solver (line-to-circle, multi-threaded) |
| `Fdp.Examples.UrbanCombat` | Thin demo app: TKB blueprints, road graph, brain authoring, scenario director |

> Design talk reference: lines 220–440 (toolkit separation); lines 1540–1600 (toolkit layering summary)

---

## 3. FDP.Toolkit.Behavior

### 3.1 Component Types

All components are unmanaged (`struct`) to satisfy the zero-alloc hot-path requirement.

**Brain Identity**

```
DoctrineState    – active doctrine hash, version/preemption token, brain tier
BrainBlackboard  – 128 bytes of raw unmanaged AI memory (parameters + runtime state)
BrainBTreeState  – wraps FastBTree's BehaviorTreeState (64 bytes, one cache line)
BrainHsm64       – wraps Fhsm HsmInstance64
BrainHsm128      – wraps Fhsm HsmInstance128
SimTier          – byte; 1 = hardcoded traffic, 2 = VM-driven tactical
```

**Capabilities**

```
ActorCapabilityState – bitmask: CanMove | CanShoot | CanInteract
```

Capability bits are cleared by the `DamageSystem` (engine/weapon destroyed) and by the `EmbarkExecutor` (soldier inside vehicle). The dispatcher checks this *before* calling any executor, so no individual executor needs to handle it.

> Design talk reference: lines 680–750, 1390–1450 (capability checks inside dispatcher)

**Action Channels**

Each channel holds exactly one active action at a time, using fixed byte buffers to avoid the 256-component limit:

```csharp
// Shared layout for LocomotionChannel, WeaponChannel, InteractionChannel
unsafe struct XxxChannel {
    ushort  ActiveAction;         // ActionKind enum cast to ushort (0 = None)
    uint    DoctrineInstanceId;   // Must match DoctrineState.InstanceId (stale = preempt)
    uint    ActionInstanceId;     // Set by Brain on each new action request
    uint    DispatchedInstanceId; // Tracked by Dispatcher for OnEnter/OnExit detection
    NodeStatus Status;            // Running | Success | Failure
    fixed byte Params[32];        // Inputs (action parameters)
    fixed byte State[32];         // Executor-internal progress
}
```

Separate concrete channels: `LocomotionChannel`, `WeaponChannel`, `InteractionChannel`.

> Design talk reference: lines 600–650 (universal channel layout), lines 1550–1620

**Mission Planning**

```
MissionPlanQueue  – fixed array of up to 8 MissionPhase items; each has DoctrineId + MissionTrigger
```

> Design talk reference: lines 4050–4130 (gap 1: queued doctrines)

### 3.2 Systems

| System | Phase | Key Responsibility |
|---|---|---|
| `DoctrineIngressSystem` | BeforeSync | Parses JSON params, writes `BrainBlackboard`, bumps `DoctrineState.InstanceId` |
| `MissionDirectorSystem` | Simulation | Evaluates `MissionPlanQueue` triggers; advances queue and replaces doctrine when met |
| `ChannelArbitrationSystem` | Simulation (before brain tick) | Clears channels whose `DoctrineInstanceId` is stale |
| `TrafficBrainSystem` (demo) | Simulation | Hardcoded C# for `SimTier=1` entities (writes LocomotionChannel directly, no VM) |
| `BTreeTickSystem` | Simulation | Steps `BrainBTreeState` for all `SimTier=2` entities with BTree brain |
| `HsmTickSystem<T>` | Simulation | Generic; instantiated for `BrainHsm64` and `BrainHsm128` separately |
| `HsmDamageBridgeSystem` | Simulation (before HSM tick) | Converts cleared `CanMove` capability to `HsmEvent` pushed into the instance queue |
| `LocomotionDispatcherSystem` | Simulation (after brains) | Checks `CanMove`; detects action changes (calls OnEnter/OnExit); calls `Execute` |
| `WeaponDispatcherSystem` | Simulation (after brains) | Checks `CanShoot`; same dispatcher pattern |
| `InteractionDispatcherSystem` | Simulation (after brains) | Same dispatcher pattern for interaction actions |

**Dispatcher pattern** (O(1) executor lookup, no per-action system):

```
IActionExecutor<TChannel> { OnEnter, Execute, OnExit }
DispatcherSystem holds IActionExecutor<T>[] _executors  (indexed by ActionKind)
```

> Design talk reference: lines 602–700 (dispatcher architecture), lines 1700–1800 (full system implementations)

### 3.3 Doctrine Registry & Parameter Flow

- `DoctrineRegistry.Register(name, BrainTier, assetId, ParseParamsDelegate)` – at startup, cold path.
- `ParseParamsDelegate(string json, byte* blackboardMemory)` – parses JSON and writes into the appropriate blackboard struct layout.
- Each doctrine has a paired blackboard struct, e.g., `AssaultBlackboard`, `ConvoyBlackboard`.
- BTree/HSM nodes reinterpret the raw bytes via `Unsafe.As<byte, XxxBlackboard>` – zero copies, zero alloc.

> Design talk reference: lines 4540–4720 (parameter flow, JSON cold path, unsafe cast in hot path)

---

## 4. FDP.Toolkit.Perception

### 4.1 Component Types

```
Faction           – TeamId byte (1=Blue, 2=Red, 0=Civilian)
PerceptionReceptor – VisionRange, FieldOfViewCos (precomputed), HearingRange
TargetMemory      – fixed array of 4 entries: EntityIds, Positions, ThreatScores, LastSeenTick
```

### 4.2 Events

```
AudioStimulusEvent  [EventId 4001]  – Origin, Intensity (radius), Source entity
LosCheckRequestEvent[EventId 4002]  – Observer, Target, ray endpoints (async→sync bridge)
TargetVisibleEvent  [EventId 4003]  – Observer, Target, Position (sync→async bridge)
```

### 4.3 Systems

| System | Thread | Phase | Responsibility |
|---|---|---|---|
| `AudioPerceptionSystem` | Main | Simulation | Consumes `AudioStimulusEvent`; queries `SpatialHashGrid` within sound radius; updates `TargetMemory` directly |
| `LosRequestBatchingSystem` | Main | BeforeSync | Transfers `LosCheckRequestEvent`s from bus into `RaycastBatchData` for the physics toolkit |
| `VisionBroadphaseSystem` | Async (SoD) | Simulation | In `PerceptionModule`; uses `SpatialHashGrid` + FOV cone test; emits `LosCheckRequestEvent` |
| `ThreatEvaluationSystem` | Async (SoD) | Simulation | Decays scores, integrates `TargetVisibleEvent` + `AudioStimulusEvent`; writes back via ECB |

### 4.4 PerceptionModule

- `ExecutionPolicy`: `SlowBackground(10Hz)`, `DataStrategy.SoD`
- Required snapshot components: `VehicleState`, `Faction`, `PerceptionReceptor`, `TargetMemory`
- Output: ECB commands to `SetComponent<TargetMemory>` on the live world

> Design talk reference: lines 433–445 (async module idea), lines 556–580 (SoD pattern), lines 2440–2620 (full perception design)

---

## 5. FDP.Toolkit.Navigation

Acts as the **translation layer** between `LocomotionChannel` (from Behavior) and `CarKinem.NavState` (from CarKinem). It only contains executor classes – no ECS systems of its own.

### 5.1 Action IDs and Parameter Structs

```csharp
static class LocomotionActions {
    const ushort MoveTo         = 1;
    const ushort FollowRoute    = 2;
    const ushort Flee           = 3;
    const ushort FollowRoadGraph= 4;
}
```

Parameter/state structs (all < 32 bytes, stored in `LocomotionChannel.Params/State`):

| Action | Params struct | State struct |
|---|---|---|
| MoveTo | `MoveToParams` (Destination, ArrivalRadius, Speed) | none |
| Flee | `FleeParams` (Threat entity, SafeDistance, Speed) | `FleeState` (NextReplanTick) |
| FollowRoute | `FollowRouteParams` (TrajectoryId, IsLooped) | none |
| FollowRoadGraph | `FollowRoadGraphParams` (TargetNodeId, Speed) | none |

### 5.2 Executor Classes

- `MoveToExecutor`: `OnEnter` → sets `NavState.FinalDestination`; `Execute` → checks `NavState.HasArrived`.
- `FleeExecutor`: throttled replanning (checks `FleeState.NextReplanTick`); compute away-vector from threat; sets `NavState.FinalDestination`.
- `FollowRouteExecutor`: maps `TrajectoryId` → sets `NavState.Mode = CustomTrajectory`.
- `FollowRoadGraphExecutor`: sets `NavState.Mode = RoadGraph`, `NavState.CurrentSegmentId`.

All `OnExit` implementations zero `NavState.TargetSpeed = 0` to stop the entity.

> Design talk reference: lines 1960–2050 (Navigation executor implementations)

---

## 6. FDP.Toolkit.Combat

### 6.1 Component Types

```
WeaponState        – MaxRange, FireRateHz, LastFiredTick, Ammo (-1=infinite), DamagePerHit, MuzzleVelocity
Health             – Current, Max
BallisticProjectile– Shooter, PreviousPosition, Velocity, Damage, SpawnTick
PassengerBuffer    – Count + fixed Entity[8] (used by APC)
IsEmbarkedTag      – VehicleEntity (soldier inside vehicle)
```

### 6.2 Events

```
FireRequestEvent  [EventId 5001] – Shooter, Origin, Direction
HitEvent          [EventId 5002] – Shooter, Target, Damage, HitPoint
```

### 6.3 Action IDs

```csharp
static class CombatActions { const ushort AimAndFire = 1; const ushort Suppress = 2; }
```

### 6.4 Systems

| System | Phase | Responsibility |
|---|---|---|
| `AimAndFireExecutor` | — | Executor class registered to `WeaponDispatcher`; checks ammo/cooldown; emits `FireRequestEvent` |
| `FireProcessingSystem` | Simulation | Consumes `FireRequestEvent`; spawns `BallisticProjectile` entities |
| `BallisticsSystem` | PostSimulation | Moves bullets; pushes `RaycastRequest` to the physics batch |
| `HitResolutionSystem` | Input (next frame) | Reads `RaycastBatchData.Hits`; emits `HitEvent`; destroys bullet entities |
| `DamageSystem` | Simulation | Consumes `HitEvent`; lowers `Health`; strips `ActorCapabilityState` bits |

> Design talk reference: lines 2050–2410 (full Combat toolkit design, ballistics pipeline)

---

## 7. FDP.Toolkit.Physics

A minimal, FDP-native 2D physics module for bulk raycast processing (line-segment vs. circle).

### 7.1 Component Types / Singletons

```
PhysicsCollider    – Radius, CollisionLayer (bitmask)
RaycastBatchData   – NativeArray<RaycastRequest> Requests; NativeArray<RaycastHit> Hits; int Count
```

```
RaycastRequest – RayId, Start, End, LayerMask, IgnoreEntity
RaycastHit     – HasHit, RayId, HitEntity, Point, Distance
```

### 7.2 Math

`Intersection2D.RaycastCircle(start, end, center, radius) → (bool, float t)` — classic quadratic, branchless.

### 7.3 Systems

| System | Phase | Responsibility |
|---|---|---|
| `RaycastSolverSystem` | Input | Reads `RaycastBatchData.Requests`; fans out to `Parallel.For` across CPU cores; writes `Hits` |

Reads spatial grid built in previous `PostSimulation` phase → deterministic one-frame lag, acceptable for bullets and LOS.

> Design talk reference: lines 2820–3100 (Physics toolkit design), lines 3060–3120 (edge cases)

---

## 8. FDP.Toolkit.Behavior – Advanced Features

### 8.1 Mission Plan Queue

`MissionPlanQueue` component stores up to 8 `MissionPhase` items inline:

```
MissionPhase { DoctrineId, MissionTrigger, TriggerParam }
MissionTrigger : TimerElapsed | ReachedDestination | UnderAttack | HealthCritical
```

`MissionDirectorSystem` runs before `ChannelArbitrationSystem`. When trigger fires → `DoctrineState.InstanceId++` → existing preemption pipeline triggers naturally.

> Design talk reference: lines 4058–4135 (gap 1 detail)

### 8.2 Interaction Executors

```csharp
InteractionActions { EmbarkVehicle=1, DisembarkVehicle=2, EjectPassengers=3 }
```

- `EmbarkExecutor`: distance check → add to `PassengerBuffer`, strip `CanMove|CanShoot`, add `IsEmbarkedTag`, signal to `SpatialHashSystem` to exclude entity.
- `EjectPassengersExecutor`: iterate `PassengerBuffer`, restore capabilities, set positions near vehicle, remove `IsEmbarkedTag`.

> Design talk reference: lines 880–990 (embark/disembark design), lines 4580–4680 (gap 3 detail)

---

## 9. Demo Application – Fdp.Examples.UrbanCombat

### 9.1 Scenario: "Urban Ambush"

Headless, single-node, deterministic 10-second (600-frame) simulation.

**Actors:**
| Entity | Count | Brain | Doctrine |
|---|---|---|---|
| `CivilianPedestrian` | 5 | Tier 1 hardcoded | "Wander" (MoveTo random) → "Panic" (Flee) on noise |
| `CivilianCar` | 3 | Tier 1 hardcoded | FollowRoadGraph loop |
| `MilitaryAPC` | 1 | Tier 2 – HsmInstance128 | "ConvoyEscort_HSM" |
| `InfantrySoldier` | 4 | Tier 2 – BTree | "InfantryCombat_BT" (embarked in APC initially) |
| `Insurgent` | 1 | Tier 2 – BTree | "Ambush_BT" |

**Timeline:**
1. Frames 1–150: Civilians wander/drive. APC drives north through intersection. Insurgent waits.
2. Frame ~180: Insurgent's `VisionBroadphase` detects APC. `TargetMemory` populated.
3. Frame ~181: Insurgent BTree executes `AimAndFire`. `FireRequestEvent` published.
4. Frame ~182: Ballistics resolves hit: `HitEvent` on APC for 500 damage.
5. Frame ~182: `DamageSystem`: APC Health→0, clears `CanMove`.
6. Frame ~183: `LocomotionDispatcher` fails APC's `FollowRoute`. HSM receives `MobilityLost` event → transitions `[Cruising] → [Disabled]`.
7. Frame ~184: `OnEnter_Disabled` writes `EjectPassengers` to `InteractionChannel`. Soldiers spawn.
8. Frame ~185: `AudioStimulusEvent` (from RPG) heard by civilians. `TargetMemory` updated. Pedestrians Flee.
9. Frames 186+: Soldiers find Insurgent via perception, engage with AimAndFire.

### 9.2 TKB Blueprints

**CivilianPedestrian (ID 1001):**  
`SimTier(1)`, `DoctrineState`, `ActorCapabilityState(CanMove)`, `LocomotionChannel`, `VehicleState`, `VehicleParams(Pedestrian)`, `NavState`, `PerceptionReceptor(vision=30, hear=100)`, `TargetMemory`, `PhysicsCollider(r=0.4, layer=1)`

**CivilianCar (ID 1002):**  
`SimTier(1)`, `DoctrineState`, `ActorCapabilityState(CanMove)`, `LocomotionChannel`, `VehicleState`, `VehicleParams(PersonalCar)`, `NavState`, `PhysicsCollider(r=2, layer=1)`

**MilitaryAPC (ID 2001):**  
`SimTier(2)`, `DoctrineState(BrainTier=2)`, `BrainHsm128`, `BrainBlackboard`, `ActorCapabilityState(CanMove|CanInteract)`, `LocomotionChannel`, `InteractionChannel`, `VehicleState`, `VehicleParams(Tank)`, `NavState`, `Health(500)`, `PhysicsCollider(r=3.5, layer=1)`, `PassengerBuffer`, `Faction(TeamId=1)`

**InfantrySoldier (ID 2002):**  
`SimTier(2)`, `DoctrineState(BrainTier=2)`, `BrainBTreeState`, `BrainBlackboard`, `ActorCapabilityState(CanMove|CanShoot)`, `LocomotionChannel`, `WeaponChannel`, `InteractionChannel`, `VehicleState`, `VehicleParams(Pedestrian)`, `NavState`, `Health(100)`, `WeaponState(ammo=30, rate=5Hz, range=200, damage=25)`, `PerceptionReceptor(vision=150, hear=200)`, `TargetMemory`, `PhysicsCollider(r=0.4, layer=1)`, `Faction(TeamId=1)`

**Insurgent (ID 2003):**  
Same as `InfantrySoldier` but `Faction(TeamId=2)`, `WeaponState(ammo=1, range=300, damage=500, rate=0.1Hz)` (RPG)

### 9.3 Road Graph

A 4-way intersection (`DemoEnvironmentSetup.CreateCityIntersection()`):
- 5 nodes: center + 4 endpoints (N/S/E/W at 100m)
- 8 segments: 4 inbound + 4 outbound

### 9.4 Brain Authoring

**Insurgent BTree ("Ambush_BT" JSON):**
```
Selector
  |- Sequence
  |    |- Condition_HasTarget
  |    |- Action_AimAndFire
  |- Action_HoldPosition
```
C# action nodes: `InsurgentNodes.Condition_HasTarget`, `InsurgentNodes.Action_AimAndFire` (writes `WeaponChannel`), `InsurgentNodes.Action_HoldPosition`.

**APC HSM ("ConvoyEscort_HSM"):**
- Built via `Fhsm.Compiler.HsmBuilder`
- States: `[Cruising]` (initial), `[Disabled]`
- Action: `Activity_Cruise` → writes `LocomotionChannel(MoveTo, northward)`
- Transition: `Cruising` --`MobilityLost`--> `Disabled`
- Entry action: `OnEnter_Disabled` → writes `InteractionChannel(EjectPassengers)`, clears `LocomotionChannel`

> Design talk reference: lines 3640–3830 (brain authoring detail)

### 9.5 TelemetryReporterSystem

Runs in `Export` phase. Consumes events (`FireRequestEvent`, `HitEvent`, `AudioStimulusEvent`) and prints structured lines:

```
[FRAME 0181] GUNFIRE: Entity 2 fired at <0, -1>
[FRAME 0182] HIT: Entity 2 hit Entity 3 for 500 damage
[FRAME 0183] HSM TRANSITION: Entity 3 -> [Disabled]
[FRAME 0185] FLEE: Entity 1 fleeing from AudioStimulus at <0,-15>
```

This enables integration tests to assert on console output (AI agent observable).

---

## 10. Frame Execution Pipeline

```
FRAME START
├── [Input]
│   ├── RaycastSolverSystem        ← Physics: solves batch raycasts from previous frame (parallel)
│   └── HitResolutionSystem        ← Combat: destroys bullets, emits HitEvent
│
├── [BeforeSync]
│   ├── DoctrineIngressSystem      ← Behavior: JSON → BrainBlackboard
│   └── LosRequestBatchingSystem   ← Perception: bus LosCheckRequestEvents → RaycastBatch
│
├── [SYNC: EventAccumulator swap, SoD snapshot]
│
├── [Simulation]  ← main thread + async PerceptionModule in parallel
│   ├── DamageSystem               ← Combat: applies HitEvents to Health, strips capabilities
│   ├── AudioPerceptionSystem      ← Perception: noise → TargetMemory
│   ├── MissionDirectorSystem      ← Behavior: advance mission queue
│   ├── ChannelArbitrationSystem   ← Behavior: preempt stale channels
│   ├── HsmDamageBridgeSystem      ← Behavior: CanMove lost → HsmEvent
│   ├── TrafficBrainSystem         ← Demo: Tier 1 brains write channels
│   ├── BTreeTickSystem            ← Behavior: step FastBTree VMs
│   ├── HsmTickSystem<BrainHsm128> ← Behavior: step FastHSM VMs
│   ├── HsmTickSystem<BrainHsm64>  ← Behavior: step small HSM VMs
│   ├── InteractionDispatcher      ← Behavior: Embark/Eject executors
│   ├── LocomotionDispatcher       ← Behavior: checks CanMove, routes to NavigationExecutors
│   ├── WeaponDispatcher           ← Behavior: checks CanShoot, routes to AimAndFireExecutor
│   └── FireProcessingSystem       ← Combat: FireRequestEvent → spawn BallisticProjectile
│
│   [PARALLEL ASYNC: PerceptionModule (10Hz, SoD)]
│       ├── VisionBroadphaseSystem ← spatial hash + FOV → LosCheckRequestEvent
│       └── ThreatEvaluationSystem ← integrate visible/audio → TargetMemory ECB
│
├── [PostSimulation]
│   ├── BallisticsSystem           ← Combat: move bullets, push RaycastRequests
│   ├── CarKinematicsSystem        ← CarKinem: RVO + bicycle model → VehicleState
│   └── SpatialHashSystem          ← CarKinem: rebuild spatial grid
│
└── [Export]
    └── TelemetryReporterSystem    ← Demo: console debug output
```

---

## 11. Key Architectural Constraints

1. **256-component limit**: Addressed by fixed byte buffers in channels (`Params[32]`, `State[32]`). Action parameters live inside the channel, not as separate components.
2. **Zero allocation on hot path**: All structs, fixed buffers, `Unsafe.As` reinterpretation casts. JSON parsing only on doctrine assignment (cold path).
3. **Preemption via version token**: `DoctrineState.InstanceId` is the single source of truth. `ChannelArbitrationSystem` enforces coherence every frame.
4. **Capability-driven gating**: `ActorCapabilityState` bits are checked at dispatcher level – individual executor classes never query capabilities.
5. **Executor lifecycle**: `OnEnter`/`OnExit` called by dispatcher on `ActionInstanceId` change, preventing dangling async state in `channel.State` bytes.
6. **Deferred physics**: `RaycastSolverSystem` runs in `Input` phase of _next_ frame, resolving bullet path data from previous-frame `PostSimulation` positions. One-frame lag is acceptable.
7. **Frustration / timeout**: Each executor must detect stuck conditions (velocity near-zero for N ticks while far from goal) and set `channel.Status = Failure` to prevent AI soft-lock.

---

## 12. Implementation Phases

| Phase | Focus | Tasks |
|---|---|---|
| **Phase 1** | `FDP.Toolkit.Behavior` – Core Infrastructure | P1-T1 … P1-T7 |
| **Phase 2** | `FDP.Toolkit.Perception` | P2-T1 … P2-T4 |
| **Phase 3** | `FDP.Toolkit.Navigation` | P3-T1 … P3-T5 |
| **Phase 4** | `FDP.Toolkit.Physics` | P4-T1 … P4-T4 |
| **Phase 5** | `FDP.Toolkit.Combat` | P5-T1 … P5-T5 |
| **Phase 6** | `FDP.Toolkit.Behavior` – Advanced (Mission Queue, Interaction) | P6-T1 … P6-T3 |
| **Phase 7** | `Fdp.Examples.UrbanCombat` – Demo App | P7-T1 … P7-T9 |

See [TASK-DETAIL.md](./TASK-DETAIL.md) for per-task descriptions and success criteria.

---

## 13. Out of Scope (Future Extensions)

- NavMesh-based pathfinding (mock `MoveTo` sufficient for demo)
- Real LOS (mock: `SpatialHashGrid` broadphase only; full LOS via `RaycastSolver` in a later iteration)
- Root-motion animation coupling (`AnimationState` component + pipeline reorder) – designed but not implemented
- Network distribution (DDS replication per-component ownership split)
- Cooperative / group AI behaviors
- Vehicle entering/exiting for soldiers mid-combat beyond the basic demo case
