# HROT / FDP -- High-Realism Operations Technology

> A distributed, data-oriented military simulation platform written in **C# 12 / .NET 8**.

HROT is a combined-arms tactical simulation engine built on top of **FDP** (Framework
for Distributed Processing) -- a reusable, domain-agnostic ECS runtime. Together they
deliver a full multi-node simulation cluster with visual AI authoring, a Blueprint
scripting language, a binary flight recorder, and a real-time 2D tactical image
generator -- all over a CycloneDDS pub/sub backbone.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Core ECS Framework (FDP)](#2-core-ecs-framework-fdp)
3. [Distributed Cluster & Brain-Muscle Split](#3-distributed-cluster--brain-muscle-split)
4. [AI Behavior System](#4-ai-behavior-system)
5. [Blueprint Visual Scripting](#5-blueprint-visual-scripting)
6. [Network Replication](#6-network-replication)
7. [Environment Queries & Perception](#7-environment-queries--perception)
8. [Flight Recorder & Replay](#8-flight-recorder--replay)
9. [Cluster Orchestration (2PC)](#9-cluster-orchestration-2pc)
10. [Time Management & Synchronization](#10-time-management--synchronization)
11. [Development Tools & Editors](#11-development-tools--editors)
12. [Predicate & Breakpoint Infrastructure](#12-predicate--breakpoint-infrastructure)
13. [Technology Stack](#13-technology-stack)
14. [Project Layout](#14-project-layout)
15. [Getting Started](#15-getting-started)

---

## 1. Architecture Overview

The solution is organized in two layers:

```
+========================================================================+
|                        HROT  Application Layer                         |
|                                                                        |
|  Orchestrator   SimHost (Muscle)   CGF (Brain)   IG   ExCon/IOS       |
|  2PC / state     Kinematics         BTree / HSM   Map  Operator        |
|  machine         Combat / Ballistics Mission plan  Render Console      |
|                                                                        |
|  Editor (scenario authoring)   Blueprint subsystem (visual scripting)  |
|  BTree.Editor   Hsm.Editor     Compiler / Runtime / Hot-reload         |
|                                                                        |
|  Hrot.Network.NED (30+ topics)  .BDC (2 topics)  .Orchestration       |
+========================================================================+
|                         FDP  Framework Layer                           |
|                                                                        |
|  Fdp.Core         Fdp.ModuleHost   Fdp.Toolkits (19 domains)          |
|  ECS kernel       Module scheduler Navigation / Combat / Perception    |
|  EventBus         RCU hot-plug     Geographic / Replay / Time sync     |
|  Flight recorder  Snapshots                                            |
|                                                                        |
|  Fdp.Presentation   Fdp.Network.Cyclone   Fdp.Diagnostics.*           |
|  Raylib + ImGui     CycloneDDS adapter    GizmoMap / contracts         |
|                                                                        |
|  Roslyn Analyzers & Source Generators (BTree / HSM / TKB / Gizmo)     |
+========================================================================+
|                  External Dependencies (source-included)               |
|  FastBTree   FastHSM   GizmoMap   NodeEdit   StructEdit  CycloneDDS   |
+========================================================================+
```

A **single executable** (`Hrot.ClusterRunner`) can host any combination of subsystems
in one process (development mode) or distribute them across multiple machines
(production mode). DDS peer-to-peer discovery handles topology changes with zero
reconfiguration on other nodes.

---

## 2. Core ECS Framework (FDP)

### 2.1 Entity Model

- **48-bit entity handles** -- 32-bit slot index + 16-bit generation counter. Stale
  references are detected without any hash map lookup.
- **96-byte `EntityHeader`** aligned to 32 bytes (AVX2) and 64 bytes (cache line),
  storing the component mask, authority mask, and lifecycle state.
- **Free-list allocator** -- O(1) entity creation and recycling.

### 2.2 Component Storage -- Two Tiers

| Tier | Type | Storage | Access |
|------|------|---------|--------|
| Tier 1 | Unmanaged `struct` | `NativeChunkTable<T>` -- 64 KB unmanaged pages | O(1) pointer, zero GC |
| Tier 2 | Managed `class` / `record` | `ManagedComponentTable<T>` -- GC arrays | Standard .NET |

Data policies (`[DataPolicy]`) control per-component behavior for save, network
replication, and the flight recorder without touching business logic.

### 2.3 Query Engine

- **SIMD bitmask filtering** (`BitMask256` / AVX2) evaluates component masks at the
  chunk level -- entire 64 KB blocks are skipped in O(1) if they do not match the query.
- **`ForEachParallel()`** -- adaptive batch sizes (64 or 1024 entities) distribute
  work across all CPU cores with zero GC allocation.
- **Chunk version tracking** -- delta snapshots and the flight recorder skip unchanged
  chunks in O(populated_chunks), not O(entities).

### 2.4 Entity Command Buffer (ECB)

Structural mutations from parallel threads (create entity, add component, destroy) are
recorded as typed op-code byte streams and played back on the main thread at the end of
the frame -- keeping the hot path lock-free.

### 2.5 Entity Lifecycle

| State | Meaning |
|-------|---------|
| `Ghost` | Remote replica waiting for all mandatory components to arrive over the network |
| `Constructing` | All required components present; distributed ACK handshake in progress |
| `Active` | Fully initialized and participating in the simulation |
| `TearDown` | Scheduled for destruction; cleanup modules running |

### 2.6 Double-Buffered Event Bus

- **TRUE double-buffering** -- events published in frame N are readable in frame N+1;
  the write and read buffers are pointer-swapped at the end of every frame.
- **Tier 1 native events** (unmanaged structs) -- fully lock-free writes via
  `Interlocked`; `ReadOnlySpan<T>` reads with zero allocation.
- **Tier 2 managed events** (class types / strings) -- `lock`-guarded list.
- **`EventAccumulator`** -- rolling history buffer that injects missed events into
  slow background modules or remote replicas. No event is ever dropped regardless of
  execution frequency.

### 2.7 Phase Permissions

Each simulation phase (`Input`, `BeforeSync`, `Simulation`, `PostSimulation`) carries
a compile-time permission set. Writing to a read-locked component in the wrong phase
raises a runtime exception in debug builds.

### 2.8 Module Host

- **`IEcsModule`** -- the unit of feature composition. Each module declares its
  `ExecutionPolicy` (synchronous main thread, background thread, or on-demand).
- **RCU hot-plugging** -- modules can be attached and detached at runtime without
  stopping the simulation loop.
- **Snapshot isolation** -- background modules receive a read-only copy of the world
  state and cannot accidentally race with the main thread.
- **Circuit breakers** -- a module that throws unexpectedly is quarantined; the cluster
  continues running.
- **Topological system scheduler** -- systems within a module are ordered by declared
  phase and dependency edges; no manual ordering is required.

### 2.9 Transient Knowledge Base (TKB)

- **`TkbTemplate`** -- a data-driven blueprint listing which components and base values
  an entity should have when spawned (M1 Abrams, civilian car, infantry squad, etc.).
- **`ITkbEntityTranslator`** pipeline -- N descriptor DTOs project into M ECS
  components, maintaining domain isolation between vehicle kinematics, combat, AI, and
  network descriptors.
- **Source generator** -- `[TkbDescriptor]` DTOs are auto-registered via
  `[ModuleInitializer]`; no manual factory wiring.

### 2.10 Zero-Allocation Design Philosophy

Avoiding GC pressure is a first-class, non-negotiable requirement that cuts across
every subsystem. The engine is explicitly designed to run continuous simulations at
scale with no GC hiccups:

- **Component storage** -- all Tier 1 components are unmanaged structs stored in
  `NativeChunkTable<T>` (64 KB pages of unmanaged memory). The GC never touches
  simulation hot data.
- **Query engine** -- `ForEachParallel()` allocates nothing; iteration uses
  `ref`-returning enumerators over native chunk pointers.
- **Entity Command Buffer** -- structural mutations are recorded as a typed byte stream
  in a pre-allocated native buffer; no boxing, no `List<object>`.
- **Event bus** -- Tier 1 native events are written via `Interlocked` into a
  pre-allocated ring buffer and read as `ReadOnlySpan<T>`; zero heap interaction on
  either side.
- **Flight recorder** -- the 32 MB double-buffered pipeline copies raw unmanaged chunk
  bytes directly to a background LZ4 compressor; no serialization objects created.
- **FastHSM** -- state machine execution is a tight dispatch loop over packed unmanaged
  structs (`BrainHsm64` / `BrainHsm128`) using C# function pointers; no delegates
  allocated per transition.
- **FastBTree** -- the behavior tree interpreter walks a `BehaviorTreeBlob` (blittable
  byte array) and dispatches via a source-generated integer table; no virtual dispatch
  or closure allocations.
- **AI actuation channels** -- all five channels (`LocomotionChannel`,
  `AnimationChannel`, `LookAtChannel`, `WeaponChannel`, `InteractionChannel`) are
  fixed-size unmanaged structs updated in-place; no per-command heap allocation.
- **Raycasts / EQS** -- results land in a pre-allocated `RaycastBatchData` ring buffer;
  BTree nodes poll by ID with no allocation.
- **GizmoMap debug API** -- `IDebugDrawBuilder` emits `DebugPrimitive` values
  (unmanaged) into a DDS wire channel; zero heap allocation on the hot path even when
  drawing hundreds of overlays per frame.
- **Network egress** -- `SmartEgress` dirty-flag checks and `Shadow State` unmanaged
  memory comparisons produce no objects on the no-change path.

The result: a 60 Hz simulation loop with thousands of active AI entities and full
debug instrumentation enabled produces zero Generation 0 GC collections on the hot
path.

---

## 3. Distributed Cluster & Brain-Muscle Split

### 3.1 Node Roles

| Role | Owns | Runs |
|------|------|------|
| **Brain (CGF)** | `BehaviorState`, `BrainBlackboard`, `MissionPlan`, `TargetMemory` | BTree interpreter, HSM kernel, mission director, entity spawn authority |
| **Muscle (SimHost)** | `SimTransform`, `WorldPos`, `NavigationStatus`, `PhysicsState` | Ground kinematics, combat, ballistics, spatial hash, LOS perception |
| **Orchestrator** | Cluster state machine | 2PC coordinator, NAS asset gateway, heartbeat tracker |
| **IG** | Ghost replicas | 2D tactical map rendering |
| **ExCon / IOS** | Operator state | Mission assignment, scenario lifecycle, cluster monitoring |

### 3.2 CQRS Feedback Loop (Intent vs. Status)

The Brain and Muscle never touch each other's components. They communicate through a
strict command/response pattern:

```
Brain:  NavigationIntent ----DDS----> Muscle: CarKinematicsSystem (moves entity)
Brain:  <----DDS---- NavigationStatus.Arrived     (written by Muscle)
Brain:  MoveToExecutor marks BTree node Success, advances to next task
```

The same pattern governs combat:

```
Muscle: HitResolutionSystem detects impact --> EntityHitDamage published
Brain:  HealthApplicationSystem applies HP loss, strips CanMove capability if zero
```

### 3.3 Split Component Authority

Ownership is per-component, not per-entity. A single tank lives on both nodes:

- Brain retains authority over cognitive descriptors (`EntityMission`, `NavigationIntent`).
- Muscle retains authority over physical descriptors (`WorldPos`, `NavigationStatus`).

This is enforced by `NetworkAuthority` masks on the `EntityHeader`; illegal writes are
detected at runtime.

### 3.4 AI Actuation Channels

AI behavior trees and state machines never mutate physics state directly. Instead they
write into fixed-size inline channel components:

| Channel | Actions | Node |
|---------|---------|------|
| `LocomotionChannel` | `MoveTo`, `FollowRoute`, `Flee`, `JoinFormation` | Brain |
| `WeaponChannel` | `AimAndFire` | Brain |
| `InteractionChannel` | `EjectPassengers`, `OpenDoor` | Brain |
| `AnimationChannel` | `PlayMontage`, `StopMontage`, `QueueMontage` | Brain |
| `LookAtChannel` | `SetLookAt`, `ClearLookAt` | Brain |

Each channel carries `ActiveAction` (ushort), `BehaviorInstanceId`, `ActionInstanceId`,
`Status` (Idle / Running / Success / Failure), a 32-byte `Params` payload, and a
32-byte `State` payload -- all unmanaged structs with zero heap allocation.

The **Animation channel** (`AnimationChannel` / `LookAtChannel`) is the dedicated
brain-to-muscle pipe for humanoid character animation. The Brain authors montage and
look-at commands; the Muscle-side `AnimationDispatcherSystem` routes them to the
`IAnimationBackend` (Stride, Fake, etc.) via a capability-gated executor chain.
An `AnimationMontageQueue` component buffers up to N pending montages so behaviors can
pre-schedule animation sequences without busy-waiting. Eight ECS systems run on the
Muscle in a mandatory phase order (dispatcher, queue advance, runtime bridge, notify
emitter, state reporter, cleanup, stance, look-at). Cross-node replication of intent
and status is handled by four dedicated DDS topics (~56 bytes intent / 16 bytes status
per entity per tick), keeping the animation CQRS loop aligned with locomotion and weapon
channels.

**Dispatcher systems** (`LocomotionDispatcherSystem`, `AnimationDispatcherSystem`, etc.)
read the channel each frame, validate capabilities (`ActorCapabilityState`), and route
to the matching `IActionExecutor`.

**`ChannelArbitrationSystem`** runs before dispatchers: when a behavior switch is
detected (via `BehaviorState.InstanceId` mismatch), it zeroes stale channels so the
outgoing executor's `OnExit` fires cleanly and no stale command bleeds into the new
behavior.

---

## 4. AI Behavior System

### 4.1 Three Authoring Paradigms

| Tier | Technology | Best For |
|------|-----------|----------|
| **Tier 2 -- FastBTree** | Compiled behavior tree (`BehaviorTreeBlob`) | Complex sequential behaviors: ambush, multi-phase combat, route following |
| **Tier 1 -- FastHSM** | Event-driven HSM (`HsmInstance64/128`); zero heap allocation, function pointers | Reactive behaviors: patrol loops, convoy escorts |
| **Tier 0 -- Hardcoded** | Plain `IEcsModuleSystem` | Massive crowds of simple entities (traffic, pedestrians) |

All three paradigms are interchangeable from the perspective of `MissionDirectorSystem`:
it assigns a behavior ID and awaits `BehaviorFinishedEvent`, never knowing which tier is
under the hood.

### 4.2 FastBTree Features

- **`BrainBlackboard`** -- universal cognitive bus shared by all BTree nodes; typed
  projection over a 60-byte inline buffer plus a `Blackboard1024` extension for heavy
  data.
- **Behavior parameters** -- each behavior exposes a typed `ParamsDto` projected
  safely over the blackboard via `Unsafe.AsRef<T>`.
- **Shared conditions and actions** -- reusable leaf nodes (`EnemyInRange`,
  `HealthBelow`, `MoveToLocation`, `FireAtTarget`, etc.) compose across behaviors
  without copy-paste.
- **Observer nodes** -- reactive abort: an observer watches a condition subtree; if it
  changes, it aborts the sibling running subtree immediately.
- **Source generator** -- `[BTreeDefinition]`, `[BTreeAction]`, `[BTreeCondition]`
  attributes; the `BTreeActionGenerator` emits the dispatch table automatically.
- **Hot-reload** -- `FbtAssemblyHotReloader` watches the output assembly for file
  changes and swaps blobs atomically. A hash-delta classifier (`Cosmetic / Soft / Hard`)
  decides whether running entity state is safe to preserve across the reload.

### 4.3 FastHSM Features

- **Zero allocation** on the hot path: state machine execution uses packed unmanaged
  structs (`BrainHsm64`, `BrainHsm128`) and C# function pointers.
- **Event-driven transitions** -- the machine only executes when an event is pushed
  into its unmanaged queue; it does not poll every frame.
- **History states and parallel regions** supported (Hsm128 variant).
- **HSM compiler and fluent `HsmBuilder` API** for programmatic definition.

### 4.4 Mission Layer

- **`MissionDirectorSystem`** -- routes `TacticalOrderDto` commands (issued by
  ExCon) to registered `ITacticalOrderMapper` implementations that translate operator
  intent into concrete behavior assignments.
- **Mission terminal states** -- behaviors report `Success` or `Failure` back to the
  mission layer through `BehaviorFinishedEvent`; the mission director composes
  sequential and parallel mission plans.
- **Unit hierarchy** -- entities organize into platoons and companies; mission orders
  can target individuals or entire unit groups.

### 4.5 Cognitive Interrupts

A `DecoupledInterruptSystem` lets external events (sensor contact, damage threshold,
operator command) inject interrupt signals into a running BTree or HSM without
tight-coupling event sources to AI internals.

---

## 5. Blueprint Visual Scripting

### 5.1 Overview

Blueprints are `.bp.json` graph assets providing Unreal-Blueprint-like visual
scripting for HROT entities: typed pins, exec wires, pure/impure nodes, latent
operations, and channel-command authoring nodes.

### 5.2 Three Dispatch Kinds

| Kind | State | Use |
|------|-------|-----|
| **Library** | Stateless | Shared utility functions callable from any graph or C# |
| **AiPrimitive** | `Blackboard1024` partition | Single-method graph hosted as BTree action, BTree condition, HSM action, and/or HSM guard -- multi-host from one authored graph |
| **Instance** | `BlueprintBlackboard*` partition | Entity-bound or world-singleton script with state, events, optional tick, and latent execution |

### 5.3 Compilation Pipeline (8 Stages)

1. **Parse** -- load `.bp.json`, validate schema.
2. **Resolve** -- wire types, callable peer references, channel command catalog.
3. **Validate** -- AiPrimitive conditions cannot contain latent nodes; cross-entity
   calls blocked in Slice 1.
4. **Lower** -- Wait nodes emit `NodeStatus.Running` (AiPrimitive) or
   `BlueprintLatentCursor` switch (Instance).
5. **Emit** -- `BTreeFluentEmitter` / `HsmFluentEmitter` produce `.cs` source with
   deterministic filename `{SanitizedName}_{BlueprintId:X8}_Bp.g.cs`.
6. **Roslyn MSBuild** -- incremental source generator bakes the `.g.cs` file into
   the output assembly at build time.
7. **In-process Roslyn** -- `InMemoryRoslynCompiler` for the editor Quick Reload
   workflow (runtime compilation without rebuilding).
8. **Hot-reload** -- `AiHotReloadCoordinator` swaps the assembly; per-slot
   structure-hash comparison preserves or hard-resets instance state.

### 5.4 Channel Command Authoring

Visual "Command Channel" nodes (e.g., `Locomotion / MoveTo`) compile directly to the
CQRS write sequence (`ActiveAction`, `Params`, `ActionInstanceId++`), eliminating the
most common BTree-authoring boilerplate.

### 5.5 Debug Protocol

- **Strategy B** -- .NET debugger can step through generated C# via embedded PDB /
  EmbeddedSource.
- **Strategy C** -- Blueprint debug protocol: breakpoint on a node, pause execution,
  report pin values, resume -- usable over DDS from a remote tool.

### 5.6 Cross-Blueprint Composition

Blueprints declare `callablePeers`; synchronous in-frame calls between Blueprints on
the same entity are supported with isolated blackboard partition slots.

---

## 6. Network Replication

### 6.1 CycloneDDS Backbone

- **Peer-to-peer discovery** -- no broker; adding or removing a node requires zero
  reconfiguration.
- **Anti-corruption layer** -- all DDS wire types are isolated behind
  `CycloneTranslator<TDds, TView>` and `INetworkFactory`. Application-layer code
  never imports CycloneDDS types.
- **Per-topic QoS** -- `WorldPos` is best-effort (UDP; occasional loss acceptable);
  `NodeOpCommand` is reliable (guaranteed delivery). The application never manages UDP.

### 6.2 Network Profiles

| Profile | Topics | Use Case |
|---------|--------|----------|
| **NED** (full) | 30+ DDS topics | All production multi-node runs |
| **BDC** (minimal) | 2 topics (`BDC_EntityMaster`, `BDC_WorldPos`) | Lightweight IG tracking, federation gateways |
| **Orchestration** | 7 topics | Cluster 2PC, state machine, heartbeat |

Both NED and BDC implement `INetworkFactory` / `IReplicationModule`; higher-level
code is protocol-agnostic.

### 6.3 Translator Pattern

- **`IDescriptorTranslator`** -- manages persistent entity state; owns DDS readers/writers.
- **`INetworkEventTranslator`** -- handles transient one-frame occurrences (combat
  detonations, ability triggers).
- **`NetworkEntityMap`** -- bridges 64-bit DDS network IDs to local 48-bit entity handles.

### 6.4 Ghost Promotion

When an ingress translator encounters an unknown network ID it creates a dormant
`Ghost` shell. The `GhostPromotionSystem` evaluates mandatory component requirements
via O(1) bitwise mask checks every frame. Promotion to `Constructing` (and then
`Active`) happens only when all required network data has physically arrived -- systems
never operate on partially hydrated replicas.

### 6.5 Egress Strategies

| Strategy | Mechanism | Suitable For |
|----------|-----------|-------------|
| **SmartEgress** | `MarkDirty()` flag on `EgressPublicationState`; publish only on change | Low-frequency complex data: `EntityMission`, `WeaponState` |
| **Shadow State** | Direct unmanaged memory comparison against `NetworkTransform`; publish on delta threshold or heartbeat | High-frequency kinematics: `SimTransform` (60 Hz) |

---

## 7. Environment Queries & Perception

### 7.1 Spatial Hash Grid

- **5-meter 2D cells** indexed exclusively for entities carrying `PhysicsCollider`.
- **Incremental updates** -- per-entity position deltas; free-list splice in O(1).
  No full rebuild unless entity count changes.
- **O(1) neighbor lookup** for broadphase queries and collision avoidance.

### 7.2 Autonomous Perception Pipeline (10 Hz)

Runs entirely on a background thread against a read-only snapshot-on-demand (SoD)
copy of the world. Inter-stage events flow through a module-private event bus (scoped
bus) to prevent write-back into the global frame state.

| Stage | System | What it does |
|-------|--------|-------------|
| 1 | `LocalGridBuilderSystem` | Reconstructs a private spatial grid from the snapshot |
| 2 | `VisionBroadphaseSystem` | FOV cone check using precomputed `FieldOfViewCos`; no hot-path trigonometry |
| 3 | `LosRequestBatchingSystem` | 2D segment-circle sweep for narrow-phase LOS; uses `ColliderRadiusReader` delegate for physics-accurate occlusion |
| 4 | `SensorTrackDebounceSystem` | Hysteresis: `Pending -> Acquired -> Lost` with occlusion timeout |

### 7.3 Asynchronous Raycasts

- Brain nodes emit `RaycastRequestEvent`s.
- `RaycastSolverSystem` groups requests and resolves them via `Parallel.For` --
  AABB broadphase followed by `Intersection2D.RaycastCircle` narrow phase.
- Results land in the `RaycastBatchData` pre-allocated ring buffer; BTree nodes
  poll by request ID -- lock-free retrieval.

### 7.4 Environment Query System (EQS)

- Area queries (point-in-polygon) follow the identical async ring-buffer pattern.
- `AreaQuerySolverSystem` runs on a background thread; ray-casting point-in-polygon
  for exact overlap.
- Matching entity handles packed into the flat `EqsTargetPool` native array.

### 7.5 Threat Evaluation

- `ThreatEvaluationSystem` applies continuous score boosts for acquired tracks and
  decays stale scores within `TargetMemory`.
- Sensor state changes cross the CQRS boundary via `SensorTrackStateEvent`; the
  Brain's `ActiveSensorTracksUpdateSystem` updates the cognitive buffer.

---

## 8. Flight Recorder & Replay

### 8.1 Zero-Allocation Hot Path

- **Memory-level serialization** -- the recorder copies entire 64 KB `NativeChunkTable`
  blocks directly from unmanaged memory. No C# reflection, no boxing.
- **Liveness map** -- dead entity slots are zeroed before writing, maximizing LZ4
  compression ratios and guaranteeing deterministic output.
- **32 MB double-buffered pipeline** -- the main thread writes into the front buffer,
  swaps pointers in O(1), then a background task performs LZ4 compression and disk
  I/O while the simulation proceeds immediately.

### 8.2 Keyframes vs. Deltas

- Full keyframe every 60 frames (configurable).
- Delta frames exploit chunk version numbers -- unchanged 64 KB blocks are skipped
  entirely (O(populated_chunks), not O(entities)).

### 8.3 Event Capture

Component data alone cannot represent transient occurrences (weapon fire, state
transitions). During `PostSimulation` the recorder reads raw bytes from the event
bus's pending (write) buffers, capturing events from the current frame before the
bus pointer swap.

### 8.4 Schema Validation

A `ComponentLayoutHasher` computes a deterministic 64-bit FNV-1a hash of every
component's physical memory layout (field names, types, `Marshal.OffsetOf` byte
offsets) when a recording begins. The `SchemaValidator` aborts playback if structural
drift is detected between recording and the live binary.

### 8.5 Playback and Seeking

| Strategy | Condition | Mechanism |
|----------|-----------|-----------|
| Sequential | Gap <= 3 frames | Iterative `StepForward` applying delta frames in memory |
| Random Access | Gap > 3 frames | Binary search to nearest keyframe; blast full keyframe into ECS chunk tables; apply intervening deltas |

### 8.6 Replay Browser

- Offline recording inspection, search, and JSON export.
- Predicate-based search pass over every recorded frame (see Predicate Infrastructure below).

---

## 9. Cluster Orchestration (2PC)

### 9.1 Purpose

Transitioning the cluster between major lifecycle states (load scenario, go live,
open replay) without desynchronization. If one node loads data faster than another
the simulation diverges -- the 2PC protocol prevents this.

### 9.2 Two-Phase Commit Flow

```
Orchestrator              All Slave Nodes
     |                         |
     |-- NodeOpCommand -------->|  Phase 1: Prepare
     |                         |  (async I/O: load JSON, pre-alloc net IDs)
     |<-- NodeOpStatus ACK -----|
     |                         |
     |-- CommitState ---------->|  Phase 2: Commit
     |                         |  (synchronous ECS flush on the same frame)
```

- **Phase 1 (Prepare)** -- each node performs async heavy lifting (load scenario
  JSON, extract entity descriptors, pre-allocate network IDs) and ACKs.
- **Phase 2 (Commit)** -- the Orchestrator issues `CommitState` after all ACKs are
  received; nodes flush prepared data into the live `EntityRepository` on the exact
  same simulation frame.

### 9.3 Master-Slave Components

- **`ClusterMaster`** -- runs only on the Orchestrator. Maintains global cluster state,
  tracks node heartbeats (1 Hz), maintains transaction history ring buffer.
- **`ClusterSlave`** -- runs on every node. Listens for orchestration intents and
  routes them to registered `IClusterStateHandler` implementations.

---

## 10. Time Management & Synchronization

### 10.1 GlobalTime Singleton

At the start of every frame the active time controller pushes a `GlobalTime` component
into the `EntityRepository`. All systems -- including the async flight recorder -- read
`GlobalTime.TotalWallTicks` (sub-microsecond, anchored to the CPU hardware performance
counter) instead of calling `DateTime.UtcNow`. Every parallel and sequential system in
a frame sees the exact same temporal snapshot.

### 10.2 Time Modes

| Mode | Behavior |
|------|----------|
| **Continuous** | Real-time (or scaled). Slave nodes use a Phase-Locked Loop (PLL) to smoothly steer their local clocks to the master without sudden jumps. |
| **Deterministic (Lockstep)** | Time advances only when the master issues a `FrameOrderDescriptor` and receives `FrameAckDescriptor` from all slaves. Used for replay and paused states. |

### 10.3 NTP-Style Clock Synchronization

The engine bypasses event-bus double-buffering latency by timestamping network packets
at the physical network boundary:

```
Slave:   TimeSyncRequest  (stamp t1 just before DDS send)
Master:  receive (stamp t2), send TimeSyncResponse (stamp t3)
Slave:   receive (stamp t4)
Offset = ((t2 - t1) + (t3 - t4)) / 2
```

The `SlaveSyncController` continuously applies this offset to produce `SyncedWallTicks`
mirroring the master's clock.

### 10.4 Future Barrier Protocol

When transitioning from Continuous to Deterministic (e.g., Pause), the master projects
a target time 200 ms into the future and broadcasts a `SwitchTimeModeEvent` containing
`BarrierWallTicks`. Because all slaves maintain NTP-synchronized clocks, they wait until
their local `SyncedWallTicks` crosses the exact barrier value -- at which microsecond all
nodes simultaneously snap to the master's `SimTimeSnapshot` and halt execution.

---

## 11. Development Tools & Editors

The diagnostic and authoring toolset is one of the most practically useful parts of
the platform. Every tool described below is available inside a single running
`Hrot.ClusterRunner --mode editor` process with no extra infrastructure.

### 11.1 Visual BTree Editor

- Full node-graph canvas (via NodeEdit widget) for authoring behavior trees graphically.
- **Live debug overlay** -- the currently executing node is highlighted in real time
  during simulation; the entire path from root to the active leaf is traced in green.
- **Trace ring buffer** -- a `BTreeTraceWorkingMemory1024` component stores the last N
  node transitions per entity so you can review execution history even after the fact.
- **Breakpoints** -- place a breakpoint on any node; when the interpreter reaches it
  the simulation pauses, giving you a frozen snapshot of the full blackboard.
- **Blackboard inspector** -- all 60 bytes of `BrainBlackboard` plus the 1024-byte
  extension are displayed as typed fields; values update in real time between steps.
- **Hot-reload** -- save a `.cs` behavior file; `FbtAssemblyHotReloader` detects the
  assembly change and swaps the behavior blob atomically. A hash-delta classifier
  (`Cosmetic / Soft / Hard`) decides whether running entity state is safe to preserve.

### 11.2 Visual HSM Editor

- Same node-graph canvas infrastructure as the BTree editor.
- State and transition authoring with guard condition expressions.
- **Live state highlighting** -- the active state box is highlighted every frame; the
  last 32 transitions are shown in the transition log panel.
- **Step-over** -- advance the HSM one event at a time to trace reactive logic.
- HSM breakpoints pause the simulation on a specific state entry or guard evaluation.

### 11.3 Blueprint Editor (`Hrot.Blueprints.Editor`)

- Asset browser listing all `.bp.json` files with docType and version badges.
- StructEdit form-based node editing with inline compiler diagnostics.
- **Quick Reload** -- in-process Roslyn (`InMemoryRoslynCompiler`) recompiles the
  Blueprint without an MSBuild cycle; compilation errors appear inline.
- **Debug session** -- Blueprint debug protocol (Strategy C): set a breakpoint on any
  node; when reached, execution suspends and pin values are reported to the panel.
- Runtime instance state per-slot: active cursor, blackboard partition contents,
  latent step count.
- Step and resume controls via the debug protocol, usable remotely over DDS.

### 11.4 Scenario Editor (`Hrot.Editor`)

- Offline scenario authoring -- no DDS or live cluster required; backed by
  `OfflineNetworkFactory` (null-stub).
- Entity placement, TKB type selection, component initialization, mission assignment.
- Route drawing, zone authoring (traversable zones, road networks, obstacle rings).
- ORBAT drag-and-drop unit hierarchy.
- **Preview / dry-run** -- enter a live ECS simulation from the authored state with one
  click; rewind to the pre-preview snapshot cleanly after the session.
- Scenario JSON save/load with round-trip migration adapter; schema version is
  stamped in the `$meta` block and validated on load.
- **AI hot-reload in editor** -- `AiHotReloadCoordinator` watches `Hrot.AI.Behaviors.dll`
  and swaps behavior trees at runtime without restarting the editor.

### 11.5 GizmoMap Debug Visualization

`GizmoMap` is the distributed debug-draw layer. Any node in the cluster (Brain, Muscle,
IG, Editor) can emit `DebugPrimitive` values via `IDebugDrawBuilder` and they appear as
overlays on the IG's map canvas -- even if the emitter runs in a separate process on a
separate machine.

Key properties:
- **Zero-allocation API** -- `DebugPrimitive` is an unmanaged struct; emission does not
  allocate on the heap. Hundreds of gizmos per frame have no GC impact.
- **DDS transport** -- `GizmoMap.Network` serializes primitives over a dedicated DDS
  topic; remote overlays arrive at the IG just like any other sensor data.
- **`[GizmoProjector]` source generator** -- mark a class with the attribute; the
  `GizmoRegistrarGenerator` emits the DDS registration automatically.
- **Primitive palette** -- lines, circles, arcs, text labels, bounding boxes, and
  navigation arrows; useful for visualizing LOS rays, EQS results, formation positions,
  threat tracks, and waypoints.
- **Type-forwarding safety** -- `Fdp.Diagnostics.Contracts` enforces that the
  `DebugPrimitive` CLR type is identical in every co-hosted assembly via `TypeForwards.cs`,
  preventing subtle struct-layout mismatches.

### 11.6 Entity Inspector

The **Entity Inspector** panel (ImGui, `Fdp.Presentation`) provides a per-entity
component browser with real-time values:

- **Entity picker** -- click any entity on the 2D map canvas to select it; the
  bounding-box picker resolves via the spatial hash grid.
- **Component list** -- all attached Tier 1 and Tier 2 components are enumerated in a
  scrollable tree, grouped by category (Physics, AI, Network, Animation, ...).
- **Live value display** -- component fields refresh every frame; arrays and nested
  structs expand inline.
- **Authority column** -- each component row shows the `NetworkAuthority` flag so you
  can immediately see which node owns a given field in a distributed run.
- **Write capability** -- in editor and development modes, scalar fields can be edited
  in-place to inject state for testing without restarting the simulation.

### 11.7 Event Inspector (Event Browser)

The **Event Browser** panel shows the real-time event stream flowing through
`FdpEventBus`:

- **Frame-by-frame log** -- events are captured from both Tier 1 (native) and Tier 2
  (managed) bus channels after each frame swap and appended to a scrollable history.
- **Type filter** -- filter by event type name (e.g., show only `SensorTrackStateEvent`
  or `AnimationMontageStartedEvent`) to focus on a specific subsystem.
- **Property drill-down** -- expand any event entry to inspect every field value,
  including entity handles, timestamps, and payload structs.
- **Pause on event** -- use in conjunction with the Universal Breakpoint Manager
  (`TransientEventPredicateDto`) to pause the simulation the instant a specific event
  with specific field values is emitted.
- **Export** -- copy selected events to the clipboard as JSON for offline analysis.

### 11.8 Replay Browser

The **Replay Browser** panel (`Hrot.ReplayBrowser`, also available as a standalone
`--mode replaybrowser` process) provides full post-mortem inspection of binary `.fdp`
recordings:

- **Timeline scrubber** -- seek to any frame; random-access is implemented via binary
  search to the nearest keyframe followed by delta replay, so seeking is O(log frames +
  delta_depth) regardless of recording length.
- **Predicate search** -- specify any `SearchPredicateDto` tree (compound AND/OR,
  property match, spatial bounding box, lifecycle, behavior parameter, event content)
  and scan the entire recording in a background pass; matching frames are listed with
  timestamps and highlighted on the timeline.
- **Component browser** -- at any paused frame the Entity Inspector panel is live,
  showing the exact component state as it was recorded; drill into any field.
- **Event overlay** -- all events captured during the recording (weapon fire, behavior
  transitions, sensor acquisitions, network state changes) are visible in the Event
  Browser at the replayed frame.
- **JSON export** -- the CLI `Fdp.Tools.RecordingDumper` converts any `.fdp` file to
  human-readable JSON for diff comparisons, CI regression checks, and post-mortem
  reporting.
- **Schema validation** -- `ComponentLayoutHasher` computes a 64-bit FNV-1a hash of
  every component's physical memory layout at record time; playback is aborted with a
  clear error if the binary layout has drifted between the recording and the live build.

### 11.9 Profiler Panel

- Per-system CPU time measured with the hardware performance counter, displayed as a
  sortable table and a frame-budget bar chart.
- Identifies hot systems, phase budget overruns, and background-thread imbalances
  without requiring an external profiler.

### 11.10 Breakpoint Manager Panel

The **Breakpoint Manager** is the runtime equivalent of a code debugger, but operating
on ECS data rather than source lines (see also section 12):

- **Create breakpoints** -- compose any `SearchPredicateDto` using the StructEdit
  drawers (component field match, spatial box, event content, behavior parameter, ...).
- **Enable / disable** -- individual breakpoints can be toggled without removing them.
- **Actions** -- a firing breakpoint can: (a) pause the simulation, (b) log to file,
  or (c) emit a DDS diagnostic event for remote monitoring.
- **Hit count and last-match display** -- see how many times and on which entity / frame
  a breakpoint has fired since it was created.
- **Persistence** -- breakpoint sets are serialized to JSON and restored across
  restarts.

### 11.11 Additional ImGui Panels

| Panel | Purpose |
|-------|---------|
| Map Canvas | 2D tactical map with GizmoMap overlays, bounding-box entity picker, camera pan/zoom |
| Config Panel | Runtime configuration editing without restart |
| Spawner Panel | Entity type spawning from ExCon with TKB type browser |
| ORBAT Panel | Order-of-battle unit hierarchy tree with mission assignment |
| Mission Panel | Mission status overview, tactical order issuance |

### 11.12 Recording Dumper Tool (`Fdp.Tools.RecordingDumper`)

CLI tool that converts binary `.fdp` recordings to human-readable JSON for
post-mortem analysis and CI regression comparisons.

---

## 12. Predicate & Breakpoint Infrastructure

A reusable, four-layer infrastructure used by the Replay Browser search panel and the
Universal Breakpoint subsystem -- and consumable by any future feature:

### 12.1 DTO Hierarchy (JSON-friendly)

| DTO | Matches |
|-----|---------|
| `CompoundPredicateDto` | AND / OR over a recursive list of predicates |
| `PropertyMatchDto` | A dot-notation field on any ECS component (e.g., `"Position.X"`) |
| `StructuralPredicateDto` | Component-mask transitions: Added, Removed, AnyChange |
| `SpatialBoundingPredicateDto` | 2D bounding-box entry / exit; map-canvas picker |
| `LifecyclePredicateDto` | Entity birth / death by ECS handle, network ID, or name substring |
| `BehaviorParamPredicateDto` | Typed field inside the inline `BrainBlackboard.BehaviorParameters` buffer |
| `BlueprintVariablePredicateDto` | Named variable in a `BlueprintBlackboard*` partition slot |
| `TraceBufferScanPredicateDto` | Record in the `BTreeTraceWorkingMemory` or `HsmTraceWorkingMemory` ring buffer |
| `TransientEventPredicateDto` | Any `FdpEventBus` event payload, including property-level matching |
| `ExternalHitTagPredicateDto` | Synthetic marker for external probe calls (Blueprint probes, network events) |

### 12.2 JIT Compiler

`IPredicateCompiler` compiles any `SearchPredicateDto` tree to a
`Func<EntityRepository, Entity, bool>` delegate via expression trees:

- Uses `ref readonly` chunk pointers -- no boxing, no managed allocations on the hot path.
- Short-circuit semantics: AND exits on first failure, OR exits on first success.
- Returns `ExtractMandatoryComponents()` so callers can pre-filter via the ECS query engine.

### 12.3 StructEdit UI

Any new `SearchPredicateDto` subclass gets a working ImGui editor automatically via
StructEdit reflection. Five specialised drawers cover domain-aware interactions:

- **`FilteredTypeComboFieldDrawer`** -- searchable component or event type dropdown.
- **`PropertyPathFieldDrawer`** -- typeahead for valid dot-notation paths for the selected component.
- **`BehaviorHashFieldDrawer`** -- human-readable behavior name to integer hash.
- **`BoundingBoxFieldDrawer`** -- map-canvas drag-to-draw bounding box picker.
- **`PredicateValueFieldDrawer`** -- inline scalar value editor matching the operator type.

### 12.4 Universal Breakpoints

The `DataBreakpointSystem` runs compiled predicate delegates every tick in
`PostSimulation`. When a predicate fires, it can pause the simulation, log the match,
or emit a DDS diagnostic event -- all without any hard-coded dependency in the
business logic.

---

## 13. Technology Stack

| Category | Technology |
|----------|-----------|
| Language / Runtime | C# 12, .NET 8 |
| Networking | CycloneDDS (via CycloneDDS.NET NuGet) |
| Rendering | Raylib-cs + rlImGui-cs |
| Logging | NLog |
| Behavior Trees | FastBTree (source-included) |
| State Machines | FastHSM (source-included) |
| Node Graph Canvas | NodeEdit (source-included) |
| Property Editor | StructEdit (source-included) |
| Debug Visualization | GizmoMap (source-included) |
| Compression | LZ4 (flight recorder) |
| Code Generation | Roslyn Incremental Source Generators |
| Static Analysis | Roslyn Analyzers (FDP_001 safety rules) |
| Testing | xUnit |

---

## 14. Project Layout

```
IOS-IG-SimHost.sln          -- Master solution (HROT + FDP combined)
FDP/
  FDP.sln                   -- FDP standalone solution
  Engine/
    Fdp.Core/               -- ECS kernel, event bus, phase system, flight recorder
    Fdp.ModuleHost/         -- Module lifecycle, RCU hot-plug, scheduler
    Fdp.Presentation/       -- Raylib app host, ImGui panels, map canvas
    Fdp.Diagnostics.*/      -- GizmoMap contracts and DDS transport
  Network/
    Fdp.Network.Cyclone/    -- CycloneDDS adapter, ingress/egress, ID allocator
  Toolkits/
    Fdp.Toolkits/           -- 19-domain simulation toolkit
    Fdp.Toolkits.Analyzers/ -- Roslyn analyzers and BTree/HSM/Gizmo generators
    Fdp.Toolkit.Tkb.SourceGen/ -- TKB descriptor auto-registration generator
  Tools/
    Fdp.Tools.RecordingDumper/ -- CLI binary .fdp -> JSON converter
  ExtDeps/
    FastBTree/              -- Behavior tree kernel, compiler, source gen attributes
    FastHSM/                -- HSM kernel, compiler, HsmBuilder API
    GizmoMap/               -- Debug visualization over DDS
    NodeEdit/               -- Generic node-graph canvas widget (ImGui)
    StructEdit/             -- Reflection-driven property editor widget
Hrot/
  Engine/
    Hrot.Core/              -- Domain vocabulary, network interfaces, dead reckoning
    Hrot.Common/            -- Node bootstrap, gizmo library, mission execution
    Hrot.Presentation/      -- HROT-specific renderers, scenario editor module
    Hrot.UI.Common/         -- Hexagonal UI facades, reusable ImGui panels
  Network/
    Hrot.Network.NED/       -- Full 30+ topic DDS profile
    Hrot.Network.BDC/       -- Lightweight 2-topic profile
    Hrot.Network.Orchestration/ -- Cluster 2PC, state machine, heartbeat
  Subsystems/
    Hrot.Orchestrator/      -- Cluster master, 2PC coordinator, NAS gateway
    Hrot.SimHost/           -- Ground kinematics, combat, ballistics, perception
    Hrot.CGF/               -- AI behavior trees, mission planning, spawn authority
    Hrot.IG/                -- 2D map rendering, ghost replication
    Hrot.ExCon/             -- Exercise control operator station
    Hrot.Editor/            -- Offline scenario authoring
    Hrot.AI.Behaviors/      -- 8 behavior implementations (MoveToLocation, FireAtTarget, ...)
    Hrot.ReplayBrowser/     -- Recording inspection, search, JSON export
    Hrot.StrideMock/        -- Stride engine mock node (CI / GPU-free)
    Blueprints/
      Hrot.Blueprints.Core/     -- Blueprint runtime, blackboard, in-memory Roslyn compiler
      Hrot.Blueprints.Compiler/ -- 8-stage Blueprint compiler pipeline
      Hrot.Blueprints.Editor/   -- ImGui Blueprint authoring + debug session
      Hrot.Blueprints.Generators/ -- Roslyn incremental source generator
  AI/
    Hrot.BTree.Editor/      -- Visual BTree authoring with live debug overlay
    Hrot.Hsm.Editor/        -- Visual HSM authoring with live debug overlay
    Hrot.Editor.AiShared/   -- Shared AI editor infrastructure
  Runner/
    Hrot.ClusterRunner/     -- Single entry-point executable for the entire cluster
```

---

## 15. Getting Started

### Prerequisites

- .NET 8 SDK
- CMake >= 3.20 in `PATH` (for native CycloneDDS build)
- Visual Studio 2019 / 2022 with C++ workload (or equivalent MSVC build tools)
- Raylib native libraries (included via `Raylib-cs` NuGet)

### First-Time Setup

**Step 1 -- Build native CycloneDDS libraries (required once after cloning):**

```powershell
.\FDP\ExtDeps\FastCycloneDds\build\native-win.ps1
```

This compiles the `cyclonedds` submodule and deposits the resulting binaries under
`FDP\ExtDeps\FastCycloneDds\artifacts\native\win-x64\`. The script is idempotent.

**Step 2 -- Restore NuGet packages:**

```powershell
dotnet restore IOS-IG-SimHost.sln
```

**Step 3 -- Build:**

```bat
REM Build everything
build_all_standalone.bat

REM Or build each sub-solution individually
cd FDP && dotnet build FDP.sln -c Release
dotnet build IOS-IG-SimHost.sln -c Release
```

### Run -- Editor Mode (Recommended First Step)

```bat
Hrot.ClusterRunner.exe --mode editor
```

**This is the default all-in-one authoring mode and the recommended starting point.**
`--mode editor` hosts the `EditorSubsystem` in a single process with a Raylib window.
Because the Editor subsystem co-locates the Brain and Muscle logic internally, it gives
you access to every feature of the engine without a distributed cluster:

- Scenario authoring (entity placement, routes, zones, ORBAT, mission assignment)
- Live ECS simulation preview with one click (then rewind cleanly)
- Visual BTree Editor with live debug overlay, blackboard inspector, and breakpoints
- Visual HSM Editor with live state highlighting and step-over
- Blueprint Editor with inline compiler diagnostics and debug protocol
- AI behavior hot-reload (save a `.cs` file -- the running behavior swaps in < 1 s)
- Entity Inspector, Event Browser, Replay Browser, GizmoMap overlays, Profiler -- all
  in the same window
- Universal Breakpoint Manager for data-driven simulation pausing
- No DDS configuration, no extra processes, no native build prerequisites beyond the
  initial CycloneDDS library step

### Run -- All Subsystems in One Process (Full Cluster, Single Machine)

```bat
Hrot.ClusterRunner.exe --mode all
```

Starts the Orchestrator, SimHost, IG, ExCon, and CGF as separate subsystems in one
process. Each subsystem is isolated (own entity map, own DDS participant) as if they
ran on separate machines, but sharing the frame clock. Use this to validate the full
distributed protocol on a single developer workstation.

### Run -- Distributed Across Machines (Production Mode)

```bat
REM Machine 1: headless backend
Hrot.ClusterRunner.exe --mode orchestrator,simhost,cgf --headless

REM Machine 2: image generator
Hrot.ClusterRunner.exe --mode ig

REM Machine 3: operator console
Hrot.ClusterRunner.exe --mode excon
```

DDS peer-to-peer discovery connects the nodes automatically.

### Convenience Scripts

| Script | What it starts |
|--------|---------------|
| `run_all_together.bat` | All subsystems in one process |
| `run_IG.bat` | Image generator node only |
| `run_IOS.bat` | ExCon / IOS operator station |
| `run_SimHost.bat` | SimHost (Muscle) node |

### Load a Scenario

In the ExCon window use the **Cluster Scenario** panel to select a scenario from the
path configured in `config.json`, then click **Load**. The Orchestrator executes a
2PC sequence to load the scenario across all nodes simultaneously.

---

*For detailed per-project documentation see the [`docs/`](docs/) folder and the
per-project markdown files under [`docs/projects/`](docs/projects/).*
