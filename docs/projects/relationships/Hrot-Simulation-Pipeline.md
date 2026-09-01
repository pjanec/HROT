# Hrot Simulation Pipeline - IOS/IG/SimHost Architecture

**Date:** 2026-05-23
**Scope:** Distributed HROT simulation system covering Hrot.Core, Hrot.Common,
Hrot.Orchestrator, Hrot.SimHost, Hrot.CGF, Hrot.IG, and Hrot.ExCon subsystems,
with the NED network layer (Hrot.Network.NED / Hrot.Network.Orchestration) tying
them together over CycloneDDS.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Node Topology](#2-node-topology)
3. [The Brain/Muscle Split](#3-the-brainmuscle-split)
4. [Scenario Lifecycle](#4-scenario-lifecycle)
5. [Key Data Flows](#5-key-data-flows)
6. [Orchestration Protocol](#6-orchestration-protocol)
7. [The NED Network Layer](#7-the-ned-network-layer)
8. [Integration Examples](#8-integration-examples)
9. [Operational Guide](#9-operational-guide)
10. [Links to Individual Project Docs](#10-links-to-individual-project-docs)

---

## 1. Architecture Overview

The HROT simulation system is a **distributed, role-partitioned simulation cluster**
built on top of CycloneDDS. Each process hosts one or more subsystems; subsystems
exchange state exclusively through DDS topics (no shared memory across processes).

The design separates _cognitive authority_ (what an entity decides to do) from
_physical authority_ (what an entity's body does), giving rise to the
**Brain/Muscle** model. A third class of node, the **Ghost** (IG), observes the
shared DDS space and renders the world without participating in simulation logic.

### What each node does

| Node | Project | Role enum | Primary concern |
|------|---------|-----------|-----------------|
| Orchestrator | `Hrot.Orchestrator` | -- | Cluster state machine, 2PC, asset I/O |
| SimHost | `Hrot.SimHost` | `MuscleGround \| Perception` | Ground kinematics, physics, combat, LOS |
| CGF | `Hrot.CGF` | `Brain` | AI behaviour trees, mission control, broadcast arbiter for *unowned* create requests |
| IG | `Hrot.IG` | `ImageGenerator` | 2-D / 3-D rendering, ghost replication, map interaction |
| ExCon | `Hrot.ExCon` | observer | Operator UI, scenario control, time control |

### The Brain/Muscle split in one sentence

> The **Brain** (CGF) decides; the **Muscle** (SimHost) executes. The Brain never
> touches kinematics; the Muscle never touches behaviour trees.

### Clean architecture boundary

Every node is built from two layers:

1. **Pure logic packs** -- ECS systems that operate on local `EntityRepository`
   memory and the internal `FdpEventBus`. Zero DDS dependency.

2. **Translator packs (ACL)** -- thin adapters that convert CycloneDDS wire
   structs into managed events / ECS component mutations, and vice versa.

This is the same Anti-Corruption Layer (ACL) pattern described in
[`HROT architecture.md`](../../HROT%20architecture.md).

---

## 2. Node Topology

### Physical layout

```
+------------------+           DDS (domain 0 or configured)          +------------------+
|  Orchestrator    |<------------------------------------------------>|    ExCon (IOS)   |
|  (ClusterMaster) |                                                  | (operator panel) |
+--------+---------+                                                  +------------------+
         |  ClusterState, NodeOpCommand,                                       |
         |  NodeHeartbeat, SysOpStatus                          ClusterOpRequest,
         |                                                      CreateEntityRequest,
         v                                                      MissionControlRequest
+--------+---------+      NavigationIntent      +------------------+
|    CGF           |<-------------------------->|    SimHost       |
|  (Brain / AI)    |      TacticalIntentRequest  |  (Muscle / Phys) |
|                  |<-- WorldPos (ghost) ------->|                  |
+------------------+                            +--------+---------+
         |                                               |
         |  EntityMaster, WorldPos                       | WorldPos, EntityDamage,
         |  NavigationStatus, EntityDamage               | NavigationStatus
         v                                               v
+--------------------------------------------+
|              IG (Image Generator)          |
|  (ghost replication + 2-D/3-D render)      |
+--------------------------------------------+
```

### DDS topics grouped by direction

```
+----------------------------+----------------------------------------------+
| Topic name                 | Direction / Owner                            |
+----------------------------+----------------------------------------------+
| EntityMaster               | CGF writes (create/destroy)                  |
| WorldPos                   | SimHost writes (authoritative position)       |
| NavigationIntent           | CGF writes -> SimHost reads                  |
| NavigationStatus           | SimHost writes -> CGF reads                  |
| TacticalIntentRequest      | CGF (commander) -> CGF (subordinate) or SimHost|
| MissionControlRequest/Ack  | ExCon -> CGF (request/response)              |
| EntityDamage               | SimHost writes                               |
| CreateEntityRequest/Ack    | ExCon -> CGF -> broadcast                    |
| UpdateEntityDescriptor     | any node -> owner (fire and forget)          |
| OwnershipUpdate            | any node (ownership transfer)                |
| ClusterOpRequest           | ExCon -> Orchestrator                        |
| NodeOpCommand              | Orchestrator -> individual nodes             |
| NodeOpStatus               | individual nodes -> Orchestrator             |
| SysOpStatus                | Orchestrator -> all (2PC result)             |
| NodeHeartbeat              | every node -> Orchestrator                   |
| ClusterState               | Orchestrator -> all (state broadcast)        |
| OrchestratorContext        | Orchestrator -> all (exercise/scenario IDs)  |
| AssetInventory             | Orchestrator -> all (scenario/archive lists) |
+----------------------------+----------------------------------------------+
```

### QoS summary

| Topic | Reliability | Durability |
|-------|-------------|------------|
| WorldPos | BestEffort | TransientLocal (KeepLast 1) |
| NavigationIntent | Reliable | TransientLocal (KeepLast 1) |
| EntityMaster | Reliable | TransientLocal (KeepLast 1) |
| ClusterState | Reliable | TransientLocal (KeepLast 1) |
| NodeOpCommand | Reliable | Volatile (KeepAll) |
| MissionControlRequest | Reliable | Volatile (KeepAll) |
| NodeHeartbeat | BestEffort | TransientLocal (KeepLast 1) |

---

## 3. The Brain/Muscle Split

### What the Brain (CGF) owns

- **Behavior Trees (BTree)** -- high-level entity AI (patrol, attack, defend, etc.)
- **Mission planning** -- `MissionPlan`, `MissionTask`, `CMD_REPLACE_MISSION`, etc.
- **TacticalIntent dispatch** -- commander entities broadcast `TacticalIntentRequest`
  to subordinates, resolved by `TacticalIntentResolutionSystem`
- **Broadcast arbitration for _unowned_ create requests** -- `CreateEntityRequestSystem` runs here
  as the _default processor_ (`isDefaultProcessor = true`), so a request with
  `OwnerAppInstanceId == 0` is serviced exactly once instead of by every node.
  Other nodes set `isDefaultProcessor = false`.
  **This is a tiebreaker, not spawn authority** -- any ECS node processes a request
  targeted at itself (`OwnerAppInstanceId == localNodeId`) unconditionally, and network IDs
  come from the distributed `DdsIdAllocatorServer`, not from CGF. See
  [`RULINGS.md` `R-138`](../../blueprints/RULINGS.md).
- **Cognitive ECS components** -- `BehaviorState`, `BrainBTreeState`,
  `BrainBlackboard`, `MissionPlan`

### What the Muscle (SimHost) owns

> **"Owns" here means the CONVENTIONAL default for entities CGF spawned, not a fixed property of
> SimHost.** Ownership is held **per component** (`AuthorityMask` + `DescriptorOwnership`) and is
> **transferable at runtime** over the `OwnershipUpdate` topic. SimHost holds `SimTransform` for
> CGF-spawned entities because CGF's `BrainMuscleOwnershipStrategy` *delegates* kinematics to it
> (`DeferredTakeOwnership` → `DeferredTakeoverSystem`). A node that originates its own entity keeps
> what it creates and delegates nothing. See [`RULINGS.md` `R-138`](../../blueprints/RULINGS.md).

- **Ground kinematics** -- CarKinem-backed vehicle physics, trajectory following
- **Navigation execution** -- receives `NavigationIntent` from CGF, writes back
  `NavigationStatus`
- **Combat resolution** -- weapon fire, damage application, `EntityDamage`
- **Perception** -- `NodeRole.Perception` flag; LOS queries, broadphase,
  area queries via `AreaQuerySolverSystem`
- **Authoritative world position** -- `WorldPos` DDS topic is the single source of
  truth for position/orientation/velocity

### Authority gate pattern

Translators use an authority gate to prevent cross-node duplication:

```
// TacticalIntentEgressTranslator - Brain node only publishes for
// entities it does NOT own locally (remote Muscle target).
if (repo.HasAuthority<BehaviorState>(evt.Entity)) continue;
```

On the Muscle side the same check uses kinematic components:

```
// If SimHost has authority over kinematics, process locally.
// If not, the NavigationIntent arrived from a remote Brain.
```

### Command flow: Brain to Muscle

```
  CGF (Brain)                          SimHost (Muscle)
  -----------                          ----------------
  BehaviorSystem                            |
    |                                       |
    | AssignBehaviorEvent (bus)             |
    v                                       |
  NavigationOrderMapper                     |
    |                                       |
    | NavigationIntent (ECS component)      |
    v                                       |
  NavigationIntentEgressTranslator          |
    |                                       |
    | NavigationIntent (DDS topic) -------->|
                                            v
                                    NavigationIntentIngressTranslator
                                            |
                                            v
                                    KinematicsSystem
                                    (trajectory computation)
```

### State flow: Muscle to Brain

```
  SimHost (Muscle)                     CGF (Brain)
  ----------------                     -----------
  KinematicsSystem                          |
    |                                       |
    | WorldPos (ECS component)              |
    v                                       |
  WorldPosEgressTranslator                  |
    |                                       |
    | WorldPos (DDS topic BestEffort) ----->|
                                            v
                                    EntityStatesIngressTranslator
                                            |
                                            v
                                    Ghost ECS entity updated
                                    BTree sensor input refreshed
```

---

## 4. Scenario Lifecycle

### Cluster state machine

The Orchestrator owns a directed state machine (see `HrotStateGraph`):

```
                  +-------+
                  |  Idle |
                  +---+---+
           /----------|----------\-----------\
          v           v           v           v
  +-----------+ +----------+ +---------+ (Degraded)
  | LoadingEdit| |LoadingLive| |LoadingRpl|
  +-----------+ +----------+ +---------+
          |           |           |
          v           v           v
  +----------+  +----------+ +--------+
  |OperatingEdit| |OperatingLive| |Operating|
  |             | |             | |Replay  |
  +----------+  +----------+ +--------+
     |   |            |           |   |
     |   v            v           v   v
     | LoadingPreview  Unloading  Unloading
     |     |          Live       Replay
     |     v               \       /
     | OperatingPreview      \     /
     |     |                  v   v
     |     v                  Idle
     | UnloadingPreview
     |     |
     |     v
     +-> OperatingEdit
         |
         v
     UnloadingEdit
         |
         v
        Idle
```

Full adjacency is defined in `HrotStateGraph.Build()` (see
`Hrot.Orchestrator/HrotStateGraph.cs`).

### 4.1 Loading a scenario (Live mode)

The sequence from an ExCon button press to all nodes running:

```
ExCon                   Orchestrator             All cluster nodes
-----                   ------------             -----------------
[User presses Load]
ClusterOpRequest ------>
(TransitionState=       ClusterMasterPlanner
 LoadingLive,           performs BFS from Idle
 PayloadJson=scenarioId) to OperatingLive:
                          [Idle -> LoadingLive]
                          [LoadingLive -> OperatingLive]
                        For each step, fan-out NodeOpCommand:

                        NodeOpCommand ----------> ClusterSlave.ProcessNodeOp
                        (PrepareState,            (PrepareLiveHandler)
                         targetNodeId=each)        - load TKB, road network,
                                                   - warmup ECS systems
                                                   - signal ready

                        <---------- NodeOpStatus (node ACK)
                        GenericTransactionTracker
                        collects ACKs (2PC round 1)
                        When all ACK:
                        NodeOpCommand ----------> ClusterSlave.ProcessNodeOp
                        (FinalizeLive)             - spawn scenario entities
                                                   - start sim tick
                        <---------- NodeOpStatus (node ACK)
                        All ACK collected:
                        ClusterState broadcast ---> all nodes update local state
SysOpStatus <----------
(Success)
```

### 4.2 Per-tick simulation pipeline

```
60 Hz tick (SimHost main loop)
------------------------------
1. DDS poll: read NavigationIntent, MissionControlAck, area query results
2. Input phase ECS systems:
   a. NavigationIntentIngressTranslator applies incoming intents
   b. CreateEntityRequestSystem drains new entity requests (non-default)
3. Simulation phase ECS systems:
   a. KinematicsSystem  - integrate velocity/acceleration
   b. AreaQuerySolverSystem - resolve LOS/perception queries
   c. CombatResolutionSystem - apply damage
4. Output phase ECS systems:
   a. WorldPosEgressTranslator  - publish WorldPos (BestEffort)
   b. NavigationStatusEgressTranslator - publish NavigationStatus
   c. EntityDamageEgressTranslator  - publish EntityDamage (Reliable)
5. Orchestration tick: ClusterSlave.Tick() - heartbeat, state polling
```

On CGF the equivalent tick drives the AI pipeline:

```
60 Hz tick (CGF main loop)
--------------------------
1. DDS poll: read WorldPos ghosts, NavigationStatus, EntityDamage
2. Input phase ECS systems:
   a. EntityStatesIngressTranslator updates ghost positions
   b. TacticalIntentIngressTranslator publishes AssignTacticalIntentEvent
3. Simulation phase ECS systems:
   a. TacticalIntentResolutionSystem -> AssignBehaviorEvent
   b. BehaviorIngressSystem (BTree tick start)
   c. BTreeSystem (behavior evaluation)
   d. MissionAdapterSystem (mission -> behavior mapping)
4. Output phase ECS systems:
   a. NavigationIntentEgressTranslator publishes NavigationIntent
   b. TacticalIntentEgressTranslator re-broadcasts intents to subordinate nodes
```

### 4.3 Entity spawning flow

Full chain from ExCon click to entity live in SimHost and IG:

```
ExCon
  [User selects entity type + clicks map]
  ExConLogic.StartPlacementMode -> MapInteractionConfig DDS ->
  IG MapPickEvent DDS -------->
  ExConLogic receives MapPickEvent
  NedCommandGateway.CreateEntityAsync(CreateEntityRequest)
         |
         | DDS: CreateEntityRequest (RequestId, TkbType, Lat/Lon/Alt)
         v
CGF (CreateEntityRequestSystem)
  - isDefaultProcessor = true
  - Validate TkbType in ITkbDatabase
  - INetworkIdAllocator.AllocateId() [DDS request/response to HostedIdAllocatorServer]
  - Send Phase-1 InProgress ACK back to ExCon
  - Register with EntityRequestFinalizationSystem
  - Enqueue SpawnEntityCommand for NetworkSpawningSystem
         |
         | SpawnEntityCommand (bus event)
         v
NetworkSpawningSystem (CGF)
  - Create local ECS entity (Brain owns cognitive components)
  - Apply TKB template components
  - DeferredTakeOwnership routing: kinematics -> SimHost node
         |
         | EntityMaster DDS (Reliable, TransientLocal) broadcast
         v
SimHost (GhostCreationSystem)
  - GhostCreationSystem sees EntityMaster with DeferredTakeOwnership flag
  - Creates local ghost entity
  - Takes ownership of kinematic descriptors
  - Initialises NavigationStatus, WorldPos
         |
         | EntityMaster DDS broadcast
         v
IG (GhostCreationSystem)
  - Creates render ghost entity
  - Begins accepting WorldPos updates for dead-reckoning
         |
  Phase-2 ACK from SimHost -> EntityRequestFinalizationSystem
  Final CreateUpdateDeleteEntityAck -> ExCon
```

### 4.4 Entity destruction

```
ExCon
  NedCommandGateway sends DeleteEntityRequest
         |
         v
CGF (DeleteEntityRequestSystem)
  - Validates entity exists in NetworkEntityMap
  - Publishes DestroyEntityCommand (bus)
         |
         v
NetworkSpawningSystem (CGF)
  - Removes local ECS entity
  - Writes EntityMaster DDS DISPOSE sample
         |
         | EntityMaster DISPOSE (Reliable) broadcast
         v
SimHost / IG
  - GhostCreationSystem detects DISPOSE
  - Removes ghost entity, releases resources
  - SimHost publishes final WorldPos / EntityDamage cleanup
```

---

## 5. Key Data Flows

### 5.1 Scenario load sequence

```
ExCon              Orchestrator          CGF           SimHost          IG
  |                     |                 |               |              |
  |--ClusterOpRequest-->|                 |               |              |
  |  (TransitionState   |                 |               |              |
  |   LoadingLive,      |                 |               |              |
  |   scenarioId)       |                 |               |              |
  |                     |--NodeOpCommand->|               |              |
  |                     |  (PrepareState) |               |              |
  |                     |                 |--validates TKB|              |
  |                     |                 |  loads road   |              |
  |                     |                 |  network      |              |
  |                     |<--NodeOpStatus--|               |              |
  |                     |  (Ready)        |               |              |
  |                     |                     |           |              |
  |                     |--NodeOpCommand------>|           |              |
  |                     |  (PrepareState)      |           |              |
  |                     |                      |--init ECS-|              |
  |                     |<---NodeOpStatus-------|           |              |
  |                     |                                  |              |
  |                     |--NodeOpCommand------------------->|              |
  |                     |  (PrepareState)                   |             |
  |                     |<---NodeOpStatus-------------------|              |
  |                     |                                                  |
  |                     |--- all PrepareState ACKs collected (2PC round 1)|
  |                     |                                                  |
  |                     |--NodeOpCommand->|--NodeOpCommand->|--NodeOpCommand-->|
  |                     |  (FinalizeLive) |  (FinalizeLive) |  (FinalizeLive)  |
  |                     |                 | spawn scenario  | spawn scenario   |
  |                     |                 | entities from   | ghost entities   |
  |                     |                 | staging data    |                  |
  |                     |<--ACKs from all nodes-----------------------------|
  |                     |                                                  |
  |                     |-- ClusterState broadcast (OperatingLive) ------->
  |<--SysOpStatus-------|                                                  |
  |  (Success)          |                                                  |
```

### 5.2 Per-tick simulation pipeline

```
  SimHost (60 Hz)                DDS                   CGF (60 Hz)
  ---------------                ---                   -----------
  KinematicsSystem               |                     |
  computes new WorldPos          |                     |
        |                        |                     |
        |--WorldPos (BestEffort)->|                     |
                                 |--WorldPos ---------->EntityStatesIngressTranslator
                                 |                     |  updates ghost positions
                                 |                     BTreeSystem evaluates
                                 |                     sensors/conditions
                                 |                     BehaviorSystem selects action
                                 |                     NavigationOrderMapper:
                                 |                     ECS NavigationIntent set
                                 |<--NavigationIntent--|
  NavigationIntentIngressTranslator                    |
  updates NavigationIntent component                   |
  KinematicsSystem reads intent                        |
  integrates trajectory towards goal                   |
        |                                              |
        |--WorldPos (next frame) -------------------->  (feedback loop)
```

### 5.3 Entity spawn flow (ExCon -> NED -> SimHost -> IG)

```
  ExCon             NED (CGF)               SimHost             IG
  -----             ---------               -------             --
  MapPickEvent
  received
        |
  CreateEntityRequest->CreateEntityRequest
                        topic (DDS)
                              |
                        CreateEntityRequestSystem
                        allocates NetworkId
                        sends InProgress ACK
                              |
                        SpawnEntityCommand
                        (ECS bus event)
                              |
                        NetworkSpawningSystem
                        creates local entity
                        DeferredTakeOwnership
                        -> SimHost node
                              |
                        EntityMaster topic ----->GhostCreationSystem
                        (DDS, Reliable)          creates Muscle ghost
                              |                  takes kinematic ownership
                              |                        |
                              |                  WorldPos topic -------->GhostCreationSystem
                              |                  (DDS)                   creates IG ghost
                              |                        |                 renders entity
                        CreateUpdateDeleteAck -------->
                        (Phase 2, to ExCon)
```

### 5.4 AI decision -> movement execution

```
  CGF BehaviorSystem       CGF Translator         SimHost Translator    SimHost Kinematics
  ------------------       --------------         ------------------    ------------------
  BTreeSystem:
    condition: target
    within range? -> NO
    action: MOVE TO
    waypoint[0]
        |
  AssignBehaviorEvent
  (bus)
        |
  NavigationOrderMapper
  produces NavigationIntent
  (ECS component on ghost)
        |
  NavigationIntentEgressTranslator
  reads intent from ghost
  (authority gate: NOT local)
        |
        | DDS NavigationIntent
        | (Reliable, TransientLocal)
        |------------------------------->NavigationIntentIngressTranslator
                                         reads DDS NavigationIntent
                                         sets ECS NavigationIntent
                                         component on local entity
                                                |
                                         KinematicsSystem
                                         reads NavigationIntent
                                         .Mode = NAV_DIRECT_POINT
                                         integrates velocity
                                         publishes WorldPos (DDS)
                                                |
        EntityStatesIngressTranslator<---------- WorldPos DDS
        updates ghost position
        BTree sensor: distance
        to waypoint shrinking
        -> arrival condition
        eventually triggers next
        behavior step
```

---

## 6. Orchestration Protocol

### 6.1 The 2-phase commit protocol

The Orchestrator (`ClusterMaster`) uses a single unified `GenericTransactionTracker`
for all in-flight operations. The two-round protocol is:

**Round 1 -- Prepare:**

```
Orchestrator                              Cluster Nodes
------------                              -------------
Generate Guid TransactionId
For each node in roster:
  Write NodeOpCommand(
    TargetNodeId = node.NodeId,
    Operation    = PrepareState / PrepareLive / PrepareEdit / etc.,
    PayloadJson  = scenario or context data)

                                    Each ClusterSlave reads NodeOpCommand
                                    filtered by TargetNodeId == local node ID
                                    Handler executes (e.g. load TKB, validate assets)
                                    Write NodeOpStatus(
                                      TransactionId,
                                      NodeId = local,
                                      StatusCode = 0 (success) or error)

Collect NodeOpStatus ACKs via
GenericTransactionTracker:
  tracker.Expected = roster.Count
  tracker.Received++ on each ACK
  if AbortOnFirstFailure and error ->
    send AbortTransaction to all nodes
  else collect all ACKs then proceed
```

**Round 2 -- Commit:**

```
Orchestrator                              Cluster Nodes
------------                              -------------
Write NodeOpCommand(
  Operation = CommitState / FinalizeLive / FinalizeEdit / etc.)

                                    Handler executes commit step
                                    Write NodeOpStatus(Success)

All ACKs collected:
Publish ClusterStateTopic (new state)
Publish SysOpStatus(RequestId, Success)
```

**Abort path:**

If any node returns an error and `AbortOnFirstFailure = true` (e.g. ManageEpisode):
```
Orchestrator writes NodeOpCommand(AbortTransaction) to all participating nodes.
Each node reverts its local changes.
Orchestrator publishes SysOpStatus(RequestId, Error).
```

### 6.2 Node state machine (per node)

Each `ClusterSlave` node tracks a local `ClusterState` that shadows the master.
Heartbeats report `LocalClusterState` to the Orchestrator:

```
  NodeOpCommand received:
  PrepareState    -> handler loads assets; returns Success or error
  CommitState     -> handler activates simulation; advances local state
  AbortTransaction-> handler reverts; stays at previous state
  PrepareEdit     -> loads scenario in edit mode
  FinalizeEdit    -> activates editing
  PrepareLive     -> loads scenario in live mode
  FinalizeLive    -> activates live simulation
  PrepareReplay   -> loads replay archive
  FinalizeReplay  -> activates replay playback
  SerializeLocal  -> snapshots local ECS state to disk
  TakeSnapshot    -> checkpoints ECS for recording
  RestoreSnapshot -> restores ECS from recording snapshot
```

Node local state is reported through `NodeHeartbeat` every N milliseconds:

```csharp
// NodeHeartbeat topic fields (DDS wire type)
// [DdsKey] public int NodeId;
// [DdsManaged] public string SubsystemName;
// public ClusterState LocalClusterState;
// public long WallTicksUtc;
// public float CpuUsagePercent;
// public long RamUsedBytes;
// public bool SimTickAdvancing;
```

### 6.3 Bootstrap latch

The Orchestrator will not accept any `ClusterOpRequest` until every subsystem
listed in `ClusterConfiguration.Mandatory` has sent at least one `NodeHeartbeat`
with `LocalClusterState == Idle`. This prevents race conditions where ExCon
issues a load command before SimHost is ready.

```csharp
// ClusterConfiguration fields
// string[] Mandatory   -- e.g. ["SimHost", "CGF", "IG"]
// string[] Optional    -- e.g. ["ExCon"]
// float HeartbeatTimeoutSeconds = 5f
```

### 6.4 Failure handling / node eviction

`NodeRoster.PruneStale(nowUtcSeconds, maxSilenceSeconds)` removes nodes that
have not sent a heartbeat within `HeartbeatTimeoutSeconds`. If a pruned node is
listed in `Mandatory`, the cluster transitions to `ClusterState.Degraded`
(a terminal state with no outgoing planning edges).

```
Normal: NodeHeartbeat arrives within timeout window
         -> NodeRoster.Upsert(profile)

Timeout: nowUtc - LastHeartbeatUtcSeconds > HeartbeatTimeoutSeconds
          -> NodeRoster.Remove(nodeId)
          -> if node was Mandatory -> ClusterState = Degraded
```

### 6.5 Transaction history

The `ClusterMaster` maintains a ring buffer of completed transactions for
diagnostics (capacity configured via `ClusterConfiguration.TransactionHistoryCapacity`,
default 50). Each `DistributedTransaction` records:
- source and target cluster state
- per-node ACK latency in milliseconds
- per-node result JSON
- whether the transaction was aborted

---

## 7. The NED Network Layer

**NED** (Network Exchange Description) is the primary DDS protocol adapter for HROT.
It is implemented in `Hrot.Network.NED` and exposed through the `INetworkFactory`
interface (also in `Hrot.Core.Network`). A secondary protocol, BDC, uses the same
interface and is selected via `--network bdc` at the CLI.

### 7.1 INetworkFactory

`INetworkFactory` is the composition root's single point of network creation. Every
subsystem receives an `INetworkFactory` instance injected at construction time. The
factory creates all DDS participants, translators, and module assemblies for that
node's role:

```csharp
// Key INetworkFactory methods (Hrot.Core.Network.INetworkFactory)
IReplicationModule CreateReplicationModule();
ICommandGateway CreateCommandGateway();
IExConEgressWriters CreateExConEgressWriters();
ITimeControlGateway CreateTimeControlGateway();
ISimHostMissionSender CreateSimHostMissionSender();
ISimHostAuxiliaryTranslators CreateSimHostAuxiliaryTranslators();
ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators(...);
ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators(...);
IIgTranslators CreateIgTranslators();
IIgNetworkAdapter CreateIgNetworkAdapter(...);
ICgfEntityLifecycleAdapters CreateCgfEntityLifecycleAdapters(...);
```

### 7.2 NedReplicationModule

`NedReplicationModule` is the central ECS module that bundles all translator packs
with their tightly-coupled ECS systems. It is constructed per-node by
`NedNetworkFactory.CreateReplicationModule()` and registered with the kernel.

Role-to-translator mapping:

| Role flag | Translators installed |
|-----------|----------------------|
| `MuscleGround` | Shared pack + kinematic packs + SmartEgressSystem |
| `ImageGenerator` | Shared pack + EntityStatesIngressPack + GhostCreationSystem + DeadReckoningSyncSystem |
| `Brain` | Shared pack + cognitive packs + SmartEgressSystem |

### 7.3 Entity replication (NED SST architecture)

An entity in NED is not a single object -- it is the **aggregation of its descriptors**
across multiple DDS topics. Each topic shares a common `EntityId` key:

```
EntityId=42:
  EntityMaster   topic  -> existence (ALIVE = exists, DISPOSE = deleted)
  WorldPos       topic  -> position, orientation, velocity
  NavigationIntent topic-> current nav command (Brain-owned)
  NavigationStatus topic-> execution result (Muscle-owned)
  EntityDamage   topic  -> health / damage level
```

**Ownership** is granular per descriptor. The last node to write a sample owns it.
To update a descriptor owned by another node, use `UpdateEntityDescriptorRequest`
(fire-and-forget) or request an explicit ownership transfer via `OwnershipUpdate`.

**Ghost entities** on IG and CGF are ECS proxy entities created by
`GhostCreationSystem` when an `EntityMaster` sample arrives. They are updated by
ingress translators and removed when an `EntityMaster` DISPOSE sample is received.

### 7.4 DDS-backed ID allocation

Network entity IDs are allocated by a central server hosted in the Orchestrator
process (`HostedIdAllocatorServer` wrapping `DdsIdAllocatorServer`). Clients
(CGF's `CreateEntityRequestSystem`) send a request over DDS and receive a unique
ID in response. Pre-allocated IDs are used during scenario load to guarantee
deterministic entity IDs across cluster nodes.

### 7.5 Orchestration translators

The orchestration plane uses its own translator set:

- `ClusterOpEgressTranslator` -- ExCon/Orchestrator sends `ClusterOpRequest`
- `ClusterOpMasterTranslator` -- Orchestrator reads `ClusterOpRequest`
- `NodeOpMasterTranslator` -- Orchestrator writes `NodeOpCommand`
- `NodeOpSlaveTranslator` -- nodes read `NodeOpCommand`, write `NodeOpStatus`
- `OrchestrationObserverTranslator` -- ExCon passively reads cluster traffic

The ExCon node uses a **dedicated observer bus** (`_observerBus`) to prevent DDS
echo loops. The UI observation layer (observer translators) is isolated from the
active command layer (`_bus`) so that incoming `NodeOpStatus` samples do not trigger
the slave translator to re-publish them.

---

## 8. Integration Examples

### 8.1 Sending a mission plan from ExCon to an entity

The ExCon operator selects an entity in the UI and assigns a mission plan.
`ExConLogic` dispatches through `NedCommandGateway`:

```csharp
// Hrot.ExCon / ExConLogic (simplified)

public async Task AssignMissionAsync(int entityNetworkId, MissionPlan plan)
{
    var request = new MissionControlRequest
    {
        RequestId      = Guid.NewGuid(),
        TargetEntityId = entityNetworkId,
        BaseVersion    = 0, // no optimistic lock check
        Payload        = new MissionCommandUnion
        {
            _d              = eMissionCommandType.CMD_REPLACE_MISSION,
            FullMissionData = plan,
        },
    };

    MissionControlAck ack = await _gateway.SendMissionControlRequestAsync(request,
                                            timeoutMs: 5000);
    if (ack.ErrorCode != 0)
        throw new InvalidOperationException(ack.ErrorMessage);
}
```

On the CGF side, `MissionControlIngressTranslator` converts the DDS sample into an
internal `MissionControlCommand` and publishes it to the bus for
`MissionAdapterSystem` to apply to the entity's `MissionPlan` ECS component.

### 8.2 CGF spawning an entity from a loaded scenario

During `FinalizeLive`, CGF processes `EntityCreationRequest` objects pre-extracted
from the scenario file by `StagingEntityExtractor`. Pre-allocated IDs are used to
guarantee consistent entity IDs across all nodes:

```csharp
// Hrot.CGF.Systems.CreateEntityRequestSystem (simplified spawn path)

// Pre-allocated network ID from scenario staging data.
// The StagingEntityExtractor set PreAllocatedNetworkId = scenarioEntityId.
var request = new EntityCreationRequest
{
    RequestId            = Guid.NewGuid(),
    TkbType              = entityDef.TkbType,
    PreAllocatedNetworkId = entityDef.NetworkId,  // non-zero => skip DDS allocation
    InitialAttributesJson = entityDef.AttributesJson,
    ChildComponentOverrides = entityDef.ChildOverrides,
};

// Phase 1: InProgress ACK (immediate)
_ackSink.SendAck(request.RequestId, request.OwnerAppInstanceId,
                 EntityOperationStatus.InProgress);

// Enqueue for NetworkSpawningSystem via SpawnEntityCommand bus event
_pendingQueue.Enqueue(new PendingRequest(request, networkId));
```

### 8.3 SimHost moving an entity via ISimHostMissionSender

The SimHost 2-D visualization panel allows direct "click to move" without going
through CGF's behavior system. It uses `ISimHostMissionSender`, which is a
protocol-neutral adapter for dispatching navigation missions:

```csharp
// Hrot.SimHost.SimHostVisualization (simplified)

void OnMapClick(Vector2 worldPosition, Entity selectedEntity)
{
    if (!_repo.TryGetComponent<NetworkIdentity>(selectedEntity, out var id))
        return;

    // Send a "navigate to point" behavior mission directly to the entity.
    // Under NED this writes a MissionControlRequest DDS message to CGF.
    _missionSender.SendNavigateToPoint(
        entityNetworkId: id.NetworkId,
        destination:     worldPosition,
        speed:           10.0f,       // m/s
        arrivalRadius:   5.0f);       // metres
}
```

The NED implementation (`NedSimHostMissionSender`) constructs and sends a
`MissionControlRequest` with a `CMD_REPLACE_MISSION` payload containing a single
"NavigateToPoint" behavior task, dispatching it to the CGF node which then issues a
`NavigationIntent` back to the Muscle.

### 8.4 Registering a cluster slave handler (Orchestrator integration test pattern)

Integration tests and the NodeBootstrapper use this pattern to wire orchestration
handlers:

```csharp
// Hrot.SimHost.NodeBootstrapper.BuildOrchestration (simplified)

var slave = new ClusterSlave(participant, nodeId, subsystemName, bus);

// Register handlers for each NodeOpType the node participates in
slave.RegisterHandler(new PrepareLiveClusterOpHandler(
    world, tkbDb, roadNetwork, nodeId));

slave.RegisterHandler(new FinalizeLiveClusterOpHandler(
    world, scenarioLoader, ghostCreation));

slave.RegisterHandler(new SerializeLocalClusterOpHandler(
    world, localTempRoot));

slave.RegisterHandler(new TakeSnapshotClusterOpHandler(
    recordReplayController));

// SlaveTranslator bridges DDS NodeOpCommand <-> ClusterSlave event bus
SlaveTranslator = new NedSlaveOrchestrationTranslator(participant, slave, nodeId);
```

---

## 9. Operational Guide

### 9.1 Running the full cluster (ClusterRunner)

The `Hrot.ClusterRunner` executable (`Program.cs`) is the single entry point for
all deployment configurations. The `--mode` argument selects which subsystems
are hosted in the process.

**Single-process all-in-one (development):**
```
Hrot.ClusterRunner.exe --mode all --domain 0
```
Expands `all` to `orchestrator,simhost,ig,excon,cgf`. All subsystems share the
same process; each gets an isolated `NetworkEntityMap`, `FdpEventBus`, and
`DdsParticipant`.

**Separate processes per node (production / distributed):**
```
# Machine 1: Orchestrator + ExCon
Hrot.ClusterRunner.exe --mode orchestrator,excon --domain 42 --node-id 1

# Machine 2: SimHost
Hrot.ClusterRunner.exe --mode simhost --domain 42 --node-id 2

# Machine 3: CGF
Hrot.ClusterRunner.exe --mode cgf --domain 42 --node-id 3

# Machine 4: IG
Hrot.ClusterRunner.exe --mode ig --domain 42 --node-id 4
```

**CI headless scenario run:**
```
Hrot.ClusterRunner.exe --mode ci --scenario MinimalCI_01 --headless
```

### 9.2 CLI argument reference

| Argument | Short | Default | Description |
|----------|-------|---------|-------------|
| `--mode` | `-m` | required | Subsystem selection (see 9.1) |
| `--domain` | `-d` | 0 | CycloneDDS domain ID |
| `--headless` | | false | Run without Raylib window |
| `--no-wait` | | false | Skip waiting-room sync between nodes |
| `--wait-for` | | -- | Comma-separated peer names to wait for at startup |
| `--config` | `-c` | -- | JSON config file path (overrides CLI defaults) |
| `--node-id` | | auto | Integer node ID; defaults to a well-known ID per role |
| `--network` | | `ned` | Protocol: `ned` or `bdc` |
| `--log-dir` | | `logs/` | Directory for NLog file output |
| `--scenario` | `-s` | -- | Scenario name (CI mode only) |

### 9.3 Configuration files

**Cluster-level (Orchestrator):**
`orchestrator-config.json` -- loaded by `ClusterConfiguration.LoadFrom()`:
```json
{
  "Mandatory": ["SimHost", "CGF", "IG"],
  "Optional":  ["ExCon"],
  "HeartbeatTimeoutSeconds": 5.0,
  "TransactionHistoryCapacity": 50,
  "NasBasePath": "\\\\nas\\hrot\\shared"
}
```

**Node-level (SimHost / CGF):**
`config.json` -- loaded by `NodeConfiguration.LoadFrom()`:
```json
{
  "DdsDomainId": 42,
  "CycloneDdsConfigPath": "cyclone.xml",
  "SimulationRateHz": 60,
  "GeodeticOrigin": {
    "Latitude":  32.0853,
    "Longitude": 34.7818,
    "Altitude":  10.0
  },
  "RoadNetworkBlobPath": "Assets/road-network.blob",
  "EntityTemplatePath":  "Assets/tkb.json"
}
```

**CycloneDDS transport (`cyclone.xml`):**
Set `CYCLONEDDS_URI` environment variable or use `CycloneDdsConfigPath` in the node
config. See the CycloneDDS documentation for multicast/unicast peer lists when
running across machines.

### 9.4 Monitoring

**Cluster state:** ExCon's main panel shows the `ClusterState` DDS topic value
(`Idle`, `OperatingLive`, etc.) and all node heartbeats with CPU/RAM metrics.

**Transaction log:** The Orchestrator exposes a ring-buffer window showing the last
`TransactionHistoryCapacity` 2PC transactions, their durations, and per-node ACK
latencies.

**DDS diagnostics:** Each `IDescriptorTranslator` implementation exposes
`ReceivedSampleCount` and `SentSampleCount` properties, visible in the in-process
diagnostics panels.

**NLog:** Each node writes a log file named `<subsystem>_<nodeId>.log` in the
configured `--log-dir`. Log entries include the node ID in scope context:
```
[2026-05-23 14:01:00] [INFO] [CreateEntityRequestSystem] [Node-3]
    Entity 42 spawned (TkbType=1001, NetworkId=42)
```

**Heartbeat timeout eviction:** When a mandatory node stops sending heartbeats
(default 5 s), the Orchestrator evicts it from the roster and transitions to
`ClusterState.Degraded`. The ExCon UI displays a visible error banner.

### 9.5 Known deployment topology patterns

| Pattern | Mode string | Use case |
|---------|-------------|----------|
| All-in-one | `all` | Development, demo, CI |
| Brain + Muscle | `cgf` + `simhost` | Two-machine performance test |
| Full distributed | `orchestrator,excon` + `simhost` + `cgf` + `ig` | Production cluster |
| Replay browser | `replaybrowser` | Offline replay analysis without live DDS |
| Scenario editor | `editor` | Scenario authoring without simulation |

---

## 10. Links to Individual Project Docs

The following per-project documentation files provide deeper detail on individual
subsystems:

| Project | Documentation |
|---------|---------------|
| FDP Core Framework | [FDP-Core-Framework.md](FDP-Core-Framework.md) |
| HROT Architecture overview | [HROT architecture.md](../../HROT%20architecture.md) |
| AI dev guide | [AI_DEV_GUIDE.md](../../AI_DEV_GUIDE.md) |
| Project checklist | [00-PROJECT-CHECKLIST.md](../../00-PROJECT-CHECKLIST.md) |

### Key source files for further reading

| File | Purpose |
|------|---------|
| [Hrot.Engine/Hrot.Core/NodeRole.cs](../../../Hrot/Engine/Hrot.Core/NodeRole.cs) | Node role enum (Brain, MuscleGround, ImageGenerator, Perception, NavigationSolver) |
| [Hrot.Engine/Hrot.Core/Network/Commands.cs](../../../Hrot/Engine/Hrot.Core/Network/Commands.cs) | Protocol-neutral command DTOs (CreateEntityCommand, MissionControlCommand, etc.) |
| [Hrot.Engine/Hrot.Core/Network/INetworkFactory.cs](../../../Hrot/Engine/Hrot.Core/Network/INetworkFactory.cs) | Factory interface for all DDS infrastructure |
| [Hrot.Subsystems/Hrot.Orchestrator/HrotStateGraph.cs](../../../Hrot/Subsystems/Hrot.Orchestrator/HrotStateGraph.cs) | Cluster state machine transitions |
| [Hrot.Subsystems/Hrot.Orchestrator/ClusterMaster.cs](../../../Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs) | 2PC tracker, node roster, bootstrap latch |
| [Hrot.Subsystems/Hrot.Orchestrator/ClusterConfiguration.cs](../../../Hrot/Subsystems/Hrot.Orchestrator/ClusterConfiguration.cs) | Cluster config (mandatory nodes, timeouts) |
| [Hrot.Subsystems/Hrot.SimHost/SimHostSubsystem.cs](../../../Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs) | SimHost subsystem lifecycle adapter |
| [Hrot.Subsystems/Hrot.SimHost/NodeBootstrapper.cs](../../../Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs) | Orchestration composition root for sim nodes |
| [Hrot.Subsystems/Hrot.CGF/CgfSubsystem.cs](../../../Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs) | CGF subsystem; HrotNodeBuilder wiring |
| [Hrot.Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs](../../../Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs) | Entity spawn authority (default processor) |
| [Hrot.Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs](../../../Hrot/Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs) | Intent -> behavior translation |
| [Hrot.Subsystems/Hrot.IG/IgSubsystem.cs](../../../Hrot/Subsystems/Hrot.IG/IgSubsystem.cs) | IG subsystem lifecycle |
| [Hrot.Subsystems/Hrot.ExCon/ExConSubsystem.cs](../../../Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs) | ExCon subsystem; dual-bus architecture |
| [Hrot.Subsystems/Hrot.ExCon/ExConLogic.cs](../../../Hrot/Subsystems/Hrot.ExCon/ExConLogic.cs) | Core ExCon logic and command dispatch |
| [Hrot.Network/Hrot.Network.NED/Factory/NedNetworkFactory.cs](../../../Hrot/Network/Hrot.Network.NED/Factory/NedNetworkFactory.cs) | NED INetworkFactory implementation |
| [Hrot.Network/Hrot.Network.NED/Replication/NedReplicationModule.cs](../../../Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs) | Role-mapped translator bundle |
| [Hrot.Network/Hrot.Network.NED/SimDescriptors.cs](../../../Hrot/Network/Hrot.Network.NED/SimDescriptors.cs) | WorldPos, NavigationIntent, EntityDamage DDS wire types |
| [Hrot.Network/Hrot.Network.NED/MissionMessages.cs](../../../Hrot/Network/Hrot.Network.NED/MissionMessages.cs) | MissionControlRequest/Ack DDS wire types |
| [Hrot.Network/Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs](../../../Hrot/Network/Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs) | ClusterState, NodeOpCommand, NodeOpStatus, NodeHeartbeat DDS wire types |
| [Hrot.Runner/Hrot.ClusterRunner/Program.cs](../../../Hrot/Runner/Hrot.ClusterRunner/Program.cs) | ClusterRunner entry point and subsystem composition |
| [Hrot.Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs](../../../Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs) | CLI argument schema and mode expansion |

---

## Appendix A: Glossary

| Term | Meaning |
|------|---------|
| ACL | Anti-Corruption Layer -- translator pack that converts DDS wire types to internal ECS events |
| Brain | Node role owning cognitive simulation (CGF = Computer Generated Forces) |
| BTree | Behavior Tree; executed by CGF to drive AI entity actions |
| CGF | Computer Generated Forces; the AI/Brain subsystem |
| ClusterMaster | Orchestrator component that owns the 2PC state machine |
| ClusterSlave | Per-node component that listens to NodeOpCommand and ACKs back |
| Dead reckoning | IG-side position extrapolation between WorldPos DDS updates |
| DDS | Data Distribution Service; the publish/subscribe middleware (CycloneDDS) |
| Default processor | The unique node (CGF) that handles broadcast CreateEntityRequest messages |
| DeferredTakeOwnership | Pre-genesis routing that assigns kinematic descriptor ownership to Muscle nodes |
| ECS | Entity Component System; the simulation data model (`EntityRepository`, `FdpEventBus`) |
| ExCon | Exercise Control; the operator station (IOS -- Interactive Operations Station) |
| Ghost | ECS proxy entity created by ingress translators from incoming DDS samples |
| IG | Image Generator; the visualization node |
| IOS | Interactive Operations Station; legacy alias for ExCon |
| Muscle | Node role owning physical simulation (SimHost) |
| NED | Network Exchange Description; the primary DDS protocol adapter project |
| QoS | Quality of Service; DDS delivery guarantees (reliability, durability) |
| SimHost | The physics/kinematics simulation host; the Muscle node |
| TKB | Template Knowledge Base; entity type definition database |
| 2PC | Two-Phase Commit; the distributed transaction protocol used by the Orchestrator |

## Appendix B: DDS topic index

| Topic | IDL file | QoS Reliability | QoS Durability |
|-------|----------|-----------------|----------------|
| WorldPos | hrot-sim-desc | BestEffort | TransientLocal |
| NavigationIntent | hrot-sim-desc | Reliable | TransientLocal |
| NavigationStatus | hrot-sim-desc | Reliable | TransientLocal |
| EntityDamage | hrot-sim-desc | Reliable | TransientLocal |
| EntityMaster | hrot-generic-msgs | Reliable | TransientLocal |
| OwnershipUpdate | hrot-generic-msgs | Reliable | Volatile |
| MissionControlRequest | hrot-missions-msgs | Reliable | Volatile |
| MissionControlAck | hrot-missions-msgs | Reliable | Volatile |
| TacticalIntentRequest | hrot-tactical-intent | (managed) | Volatile |
| CreateEntityRequest | hrot-generic-msgs | Reliable | Volatile |
| CreateUpdateDeleteEntityAck | hrot-generic-msgs | Reliable | Volatile |
| ClusterState | hrot-orchestration | Reliable | TransientLocal |
| OrchestratorContext | hrot-orchestration | Reliable | TransientLocal |
| AssetInventory | hrot-orchestration | Reliable | TransientLocal |
| NodeHeartbeat | hrot-orchestration | BestEffort | TransientLocal |
| ClusterOpRequest | hrot-orchestration | Reliable | Volatile |
| SysOpStatus | hrot-orchestration | Reliable | TransientLocal |
| NodeOpCommand | hrot-orchestration | Reliable | Volatile |
| NodeOpStatus | hrot-orchestration | Reliable | Volatile |
