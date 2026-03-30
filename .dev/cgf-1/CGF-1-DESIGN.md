# CGF-1 Design Document
## Distributed Drill Management & CGF Subsystem — Phases 1–5

> **Source:** Derived from [design-talk.md](./design-talk.md),
> [mgmt-DESIGN.md](./mgmt-DESIGN.md), [design-review-2.md](./design-review-2.md),
> and [design-review-3.md](./design-review-3.md).  
> **Scope:** Phases 1–5 (Skeleton, State & Time, Persistence, Generalization, Operational UI & CQRS).  
> See [CGF-1-GENERALIZATION.md](./CGF-1-GENERALIZATION.md) for the Phase 4 design authority.  
> See [CGF-1-ADDENDUM-3.md](./CGF-1-ADDENDUM-3.md) for the Phase 5 design authority.  
> Phase 6 (Urban Combat AI) is out of scope until Phases 1–5 are stable.
>
> **Critical architectural constraint:**  
> _FDP infrastructure (Fdp.Kernel and all FDP.Toolkit.* projects) must never reference
> any `Bagira.*` assembly._ This boundary is hard. The Drill State Machine (DSM) lives
> entirely in the Bagira application layer. FDP kernel/toolkits expose only
> application-agnostic abstractions (`ITimeController`, `IRecordReplayController`,
> `IEntityRefPatchable`, etc.) that Bagira code then implements and wires together.

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Architectural Boundaries](#2-architectural-boundaries)
3. [Phase 1 — Skeleton: Control-Plane Foundation](#3-phase-1--skeleton-control-plane-foundation)
   - [Stage 1.1 — Orchestration DDS Schema](#31-stage-11--orchestration-dds-schema)
   - [Stage 1.2 — Bagira.Orchestrator Bootstrapping](#32-stage-12--bagiraorchestrator-bootstrapping)
   - [Stage 1.3 — Centralized Identity Migration](#33-stage-13--centralized-identity-migration)
   - [Stage 1.4 — DrillSlave Foundation](#34-stage-14--drillslave-foundation)
   - [Stage 1.5 — Orchestrator Health Monitoring & Bootstrap Recovery](#35-stage-15--orchestrator-health-monitoring--bootstrap-recovery)
   - [Stage 1.6 — Orchestrator ImGui Scenario & Story Controls](#36-stage-16--orchestrator-imgui-scenario--story-controls)
4. [Phase 2 — State & Time: DSM and Synchronization](#4-phase-2--state--time-dsm-and-synchronization)
   - [Stage 2.1 — BFS Transition Planner](#41-stage-21--bfs-transition-planner)
   - [Stage 2.2 — DSM Handler Wiring](#42-stage-22--dsm-handler-wiring)
   - [Stage 2.3 — Time Strategy Proxying](#43-stage-23--time-strategy-proxying)
   - [Stage 2.4 — Future Barrier Implementation](#44-stage-24--future-barrier-implementation)
   - [Stage 2.5 — Deterministic CI Hookup](#45-stage-25--deterministic-ci-hookup)
5. [Phase 3 — Persistence: Scenarios, Checkpoints & Replay](#5-phase-3--persistence-scenarios-checkpoints--replay)
   - [Stage 3.1 — Storage Gateway](#51-stage-31--storage-gateway)
   - [Stage 3.2 — Portable Scenario Loading](#52-stage-32--portable-scenario-loading)
   - [Stage 3.3 — 3-Step Binary Checkpointing](#53-stage-33--3-step-binary-checkpointing)
   - [Stage 3.4 — Dynamic Recording Modules](#54-stage-34--dynamic-recording-modules)
   - [Stage 3.5 — Live-from-Replay Temporal Interlock](#55-stage-35--live-from-replay-temporal-interlock)
   - [Stage 3.6 — Scenario/Story Serialization Toolkit](#56-stage-36--scenariostory-serialization-toolkit)
   - [Stage 3.7 — Application-Layer Scenario Save/Load Wiring](#57-stage-37--application-layer-scenario-saveload-wiring)
   - [Stage 3.8 — Runtime Story Injection & Deletion](#58-stage-38--runtime-story-injection--deletion)
   - [Stage 3.9 — Dry Run DSM Handler](#59-stage-39--dry-run-dsm-handler)
   - [Stage 3.10 — E2E DSM Test Script Suite](#510-stage-310--e2e-dsm-test-script-suite)
6. [Phase 4 — Generalization: FDP Toolkit Orchestration](#6-phase-4--generalization-fdp-toolkit-orchestration)
7. [New Projects & File Map](#7-new-projects--file-map)
8. [Modified Files](#8-modified-files)
9. [Phase 5 — Operational UI, Real Network Dispatch & CQRS Architecture](#9-phase-5--operational-ui-real-network-dispatch--cqrs-architecture)
10. [Deferred Features (Phase 6+)](#10-deferred-features-phase-6)

---

## 1. System Overview

The CGF-1 workstream introduces a **distributed control plane** for the Bagira/FDP
platform, enabling:

- A new **`Bagira.Orchestrator`** subsystem that acts as supreme state and time authority
  over all simulation nodes.
- A new **`Bagira.CGF`** subsystem that acts as the "Brain" for the UrbanCombat scenario
  (acting as a `DrillSlave` in Phases 1–3; its AI logic is wired in Phase 4).
- **Drill State Machine (DSM)** — a directed graph of cluster-wide lifecycle states
  (`Standby`, `LoadingLive`, `RunningLive`, etc.) governed by Two-Phase Commit (2PC)
  distributed transactions.
- **Distributed time control** — seamless switching between real-time and deterministic
  lockstep modes without blocking the simulation hot-path.
- **Recording, replay, checkpointing, and scenario management** backed by an
  LZ4-compressed binary format and an SMB Pull Gateway pattern for multi-node file I/O.

```
┌─────────────────────────────────────────────────────────────────────┐
│                     BAGIRA DISTRIBUTED PLATFORM                      │
│                                                                      │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────┐  ┌──────────┐  │
│  │     IOS      │  │   SimHost    │  │     IG     │  │   CGF    │  │
│  │  (Control)   │  │ (Simulation) │  │ (Render)   │  │ (Brain)  │  │
│  │  DrillSlave  │  │  DrillSlave  │  │ DrillSlave │  │DrillSlave│  │
│  └──────┬───────┘  └──────┬───────┘  └──────┬─────┘  └────┬─────┘  │
│         │                 │                  │              │        │
│  ┌──────▼─────────────────▼──────────────────▼──────────────▼─────┐ │
│  │              Bagira.Orchestrator (DrillMaster)                  │ │
│  │  DSM │ TransitionPlanner │ StorageGatewayModule │ DrillId Alloc │ │
│  └──────────────────────────────────────────────────────────────── ┘ │
│                                                                      │
│  ──────────────────────── CycloneDDS Bus ──────────────────────── │
└──────────────────────────────────────────────────────────────────────┘
```

### Control Planes

| Plane | Direction | Topic |
|-------|-----------|-------|
| Control — Request | IOS → Master | `SysOpRequest` |
| Control — Response | Master → IOS | `SysOpStatus` |
| Command | Master → All Nodes | `NodeOpCommand` |
| Command ACK | All Nodes → Master | `NodeOpStatus` |
| Health | Each Node → All | `NodeHeartbeat` |
| State (persistent) | Master → All | `SystemStateTopic` |
| Context (persistent) | Orchestrator → All | `OrchestratorContextTopic` |
| Time | Time Master → All | `TimePulseDescriptor` (existing) |
| Time Mode | Coordinator → All | `SwitchTimeModeEvent` (new) |

---

## 2. Architectural Boundaries

```
┌──────────────────────────────────────────────────────────────────────────┐
│  FDP LAYER  (Fdp.Kernel, FDP.Toolkit.*)                                  │
│  ─ application-agnostic abstractions and implementations only ─           │
│                                                                          │
│  Fdp.Kernel:                                                             │
│    IRecordReplayController, IEntityRefPatchable, ComponentPatchMap,      │
│    CheckpointIOWorker, RecordingConfiguration,                           │
│    NetworkLifecycleSystemGroup  (generic scheduling primitive)           │
│                                                                          │
│  FDP.Toolkit.Scenario (new):                                             │
│    IEntityScenarioTranslator (non-generic, N:M, consumption mask),      │
│    IGuidResolver, FdpAutoSerializer (JIT 1:1 fallback),                  │
│    ScenarioSerializerBuilder, ScenarioSerializer,                        │
│    [ScenarioIgnore] attribute, ScenarioIgnoreTag                         │
│    (StoryTag lives in FDP.Toolkit.Replay — Guid StoryId; shared w/ replay) │
│                                                                          │
│  FDP.Toolkit.Time:                                                       │
│    ITimeController, SwitchableTimeController,                            │
│    DistributedTimeCoordinator, SlaveTimeModeListener,                    │
│    SwitchTimeModeEvent (internal blit message), TimeMode enum           │
│    MasterTimeController, SlaveTimeController (existing, extended)        │
│                                                                          │
│  FDP.Toolkit.Replay (Fdp.Kernel/FlightRecorder):                         │
│    AsyncRecorder, PlaybackController, RecorderSystem, FrameMetadata      │
│    (all extended; no new Bagira references added)                        │
└──────────────────────────────────────────────────────────────────────────┘
        ▲  consumed by (no reverse dependency)
┌──────────────────────────────────────────────────────────────────────────┐
│  BAGIRA APPLICATION LAYER  (Bagira.*)                                    │
│  ─ DSM, orchestration wiring, entity logic, UI ─                         │
│                                                                          │
│  Bagira.DDS.DataModel:    OrchestrationMessages.cs  (new DDS topics)    │
│  Bagira.Orchestrator:     DrillMaster, TransitionPlanner,               │
│                           StorageGatewayModule, ReplayMasterModule       │
│  Bagira.SimHost:          DrillSlave, DSM handlers, EcsRecordReplay…    │
│  Bagira.IG:               DrillSlave, DSM handlers                      │
│  Bagira.IOS:              DrillSlave (no-ECS lightweight variant)        │
│  Bagira.CGF (new):        DrillSlave, CGF subsystem scaffold             │
└──────────────────────────────────────────────────────────────────────────┘
```

**Rules enforced at compile time:**
- No `Bagira.*` `using` statement or project reference in any `FDP/` project.
- `DSMState`, `SysOpType`, `NodeOpType`, `OpStatus`, and all DDS orchestration structs live in
  `Bagira.DDS.DataModel` — not in any FDP library.
- `DsmStateChangedEvent` carries `DSMState` fields and therefore lives in`Bagira.Runner` or
  `Bagira.Common` — **not** in `Fdp.Kernel`.
- `IRecordReplayController` in `Fdp.Kernel` uses only `Guid`, `string`, and `GlobalTime`
  (already in `Fdp.Kernel`); no `DSMState`, no Bagira references.
- `FDP.Toolkit.Scenario` is generic; it knows neither JSON file paths nor DSM states.

---

## 3. Phase 1 — Skeleton: Control-Plane Foundation

**Goal:** Establish raw network scaffolding; prove that the Orchestrator can watch nodes
and that nodes can register with the Orchestrator.

### 3.1 Stage 1.1 — Orchestration DDS Schema

**Location:** `Bagira.DDS.DataModel/Orchestration/OrchestrationMessages.cs` (new file)  
**IDL file name:** `bdc-sst-orchestration`  
**C# namespace:** `Bagira.BDC.SSTD.Orchestration`

#### Enumerations

```csharp
public enum DSMState : int
{
    Standby        = 0,
    LoadingEdit    = 10, RunningEdit    = 11, UnloadingEdit    = 12,
    LoadingDryRun  = 20, RunningDryRun  = 21, UnloadingDryRun  = 22,
    LoadingLive    = 30, RunningLive    = 31, UnloadingLive    = 32,
    LoadingReplay  = 40, RunningReplay  = 41, UnloadingReplay  = 42,
    Degraded       = 99,
}

public enum SysOpType : int
{
    TransitionState   = 1, SaveScenario      = 2, LoadBattlespace  = 3,
    TakeCheckpoint    = 4, CollectCheckpoint = 5, ExportArchive    = 6,
    ImportArchive     = 7, ManageStory       = 8, ReplaySeek       = 9,
    PauseTime         = 10, ResumeTime       = 11,
}

public enum NodeOpType : int
{
    PrepareState         = 1, CommitState           = 2, AbortTransaction    = 3,
    TakeSnapshot         = 4, RestoreSnapshot       = 5,
    PrepareBattlespace   = 7, CommitBattlespace     = 8,
    PrepareLive          = 9, FinalizeLive          = 10,
    PrepareReplay        = 11, FinalizeReplay        = 12,
    ReplaySeek           = 13, UploadChunk          = 14,  // ← design name; C# implementation uses NodeReplaySeek to avoid IDL literal clash with SysOpType.ReplaySeek (value 9)
    SerializeLocal       = 15, CleanupTempFiles     = 16,
    StartStory           = 20, StopStory            = 21,
    ReplayStory          = 22, ForgetStory          = 23,
    LoadStoryAssets      = 24,
}

public enum OpStatus : int { Pending = 0, InProgress = 1, Success = 2, Failure = 3, Rejected = 4 }
```

#### DDS Topics

```csharp
[DdsTopic("SystemState")]
[DdsQos(Reliability=Reliable, Durability=TransientLocal, HistoryKind=KeepLast, HistoryDepth=1)]
public partial struct SystemStateTopic
{
    public DSMState CurrentState;
    public Guid     DrillId;
    public long     StateStartWallTicks;
    public int      TransactionEpoch;
}

[DdsTopic("SysOpRequest")]
[DdsQos(Reliability=Reliable, Durability=Volatile)]
public partial struct SysOpRequest
{
    public Guid      RequestId;
    public SysOpType OperationType;
    public string    PayloadJson;
}

[DdsTopic("SysOpStatus")]
[DdsQos(Reliability=Reliable, Durability=Volatile)]
public partial struct SysOpStatus
{
    public Guid     RequestId;
    public OpStatus Status;
    public int      ErrorCode;
    public string   ResultJson;
}

[DdsTopic("NodeOpCommand")]
[DdsQos(Reliability=Reliable, Durability=Volatile)]
public partial struct NodeOpCommand
{
    public Guid       TransactionId;
    public NodeOpType Operation;
    public string     PayloadJson;
}

[DdsTopic("NodeOpStatus")]
[DdsQos(Reliability=Reliable, Durability=Volatile)]
public partial struct NodeOpStatus
{
    public Guid     TransactionId;
    public int      NodeId;
    public OpStatus Status;
    public bool     IsParticipating;
    public int      ErrorCode;
    public string   ResultJson;
}

[DdsTopic("NodeHeartbeat")]
[DdsQos(Reliability=BestEffort, Durability=TransientLocal, HistoryKind=KeepLast, HistoryDepth=1)]
public partial struct NodeHeartbeat
{
    [DdsKey] public int     NodeId;
    public string           SubsystemName;
    public DSMState         LocalDsmState;
    public long             WallTicksUtc;
    public float            CpuUsagePercent;
    public long             RamUsedBytes;
    public bool             SimTickAdvancing;
    public string           SubsystemsJson;
}

[DdsTopic("OrchestratorContext")]
[DdsQos(Reliability=Reliable, Durability=TransientLocal, HistoryKind=KeepLast, HistoryDepth=1)]
public partial struct OrchestratorContextTopic
{
    public DSMState CurrentState;
    public Guid     DrillId;
    public int      TransactionEpoch;
    public string   ScenarioId;
    public string   ArchiveBasePath;
    public string   RequiredNodeIdsJson;
    public long     StateStartWallTicks;
}
```

**Milestone validation:** Reflection tests assert all new structs carry `[DdsTopic]`
attributes and that the IDL file constant matches `bdc-sst-orchestration`. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0101).

---

### 3.2 Stage 1.2 — Bagira.Orchestrator Bootstrapping

**New project:** `Bagira.Orchestrator` (solution root)  
**Entry point:** launched by `Bagira.Runner` when `--mode orchestrator` is passed, or
as a subsystem hosted inside `Bagira.Runner`.

#### DrillMaster Responsibilities

- Sole writer of `SystemStateTopic` — the cluster's single source of truth.
- Manages `NodeRoster` by consuming `NodeHeartbeat` (prunes nodes missing for > 5 s).
- Executes the 2PC transaction loop without blocking the main ECS loop.
- Generates a new `DrillId` GUID on every transition out of `Standby`.
- Validates and rejects invalid `SysOpRequest` messages before any `NodeOpCommand` is broadcast.
- Hosts the `DdsIdAllocatorServer` (relocated from SimHost — see §3.3).
- Hosts `StorageGatewayModule` (Phases 3+).
- Hosts `ReplayMasterModule` (Phases 3+).

#### Internal Data Structures (excerpt)

```csharp
// Polymorphic DSM step in a planned trajectory
abstract class ISysOpStep { public abstract string Label { get; } }
class TransitionStep : ISysOpStep { public DSMState TargetState; public NodeOpType PrepareOp; public NodeOpType CommitOp; }
class OperationStep  : ISysOpStep { public SysOpType OperationType; public string PayloadJson; }

// Active distributed transaction
class DistributedTransaction
{
    public Guid                   TransactionId;
    public Guid                   OriginRequestId;
    public DSMState               TargetDsmState;
    public Queue<ISysOpStep>      PlannedSteps;
    public int                    TotalSteps;
    public int                    CompletedSteps;
    public HashSet<int>           PendingNodes;
    public float                  ElapsedSeconds;
    public float                  TimeoutSeconds;    // default 30 s
    public bool                   AllowPartialSuccess;
}
```

**Milstone validation:** Headless `--mode orchestrator` run; an out-of-process DDS
reader asserts `SystemStateTopic.CurrentState == Standby` within 3 s of startup.
See [CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0102).

---

### 3.3 Stage 1.3 — Centralized Identity Migration

**Problem:** `DdsIdAllocatorServer` currently lives in `SimHostSubsystem`. If SimHost
crashes and restarts, split-brain entity ID allocation occurs.

**Fix:** Move `DdsIdAllocatorServer` to `DrillMaster` (in-process). Slave nodes keep
only `DdsIdAllocator` clients as before. The server's DDS request/response topics are
unchanged; only the hosting process changes.

`Bagira.SimHost/SimHostApp.cs` must no longer register `DdsIdAllocatorServer`.

**Milestone validation:** Cross-node RPC test. Orchestrator is launched first;
SimHost boots, requests an ID batch, and receives ID `1`. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0103).

---

### 3.4 Stage 1.4 — DrillSlave Foundation

**New files (one per subsystem):**
- `Bagira.SimHost/Modules/Orchestration/DrillSlave.cs`
- `Bagira.IG/Modules/Orchestration/DrillSlave.cs`
- `Bagira.IOS/Orchestration/DrillSlave.cs` (no-ECS lightweight variant)
- `Bagira.CGF/Modules/Orchestration/DrillSlave.cs` (new project)

#### Responsibilities
- Consumes `NodeOpCommand` on a background DDS thread; enqueues to a
  `ConcurrentQueue<PendingMainThreadAction>` for safe main-thread execution.
- Publishes autonomous `NodeHeartbeat` at 1 Hz based on wall-clock `Stopwatch`
  (independent of sim time).
- Dispatches committed `NodeOpCommand` to registered `IDsmHandler` implementations.
- Publishes `NodeOpStatus` responses.

#### IDsmHandler Interface (Bagira application layer)

```csharp
// Location: Bagira.Runner or a shared Bagira.Common — NOT in FDP
public interface IDsmHandler
{
    bool CanHandle(NodeOpType op);
    Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct);
    void Commit(NodeOpCommand cmd, EntityRepository repo);
    void Abort(NodeOpCommand cmd, EntityRepository repo);
}
```

> The `IDsmHandler` interface is declared in the Bagira application layer. FDP
> knows nothing about it. `EntityRepository` is an FDP type but is passed as a
> parameter — no reverse dependency is created.

**Milestone validation:** Integration test; Orchestrator monitors 2 heartbeats from
SimHost and CGF within a 2-second wall-clock window. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0104).

---

### 3.5 Stage 1.5 — Orchestrator Health Monitoring & Bootstrap Recovery

**Location:** `Bagira.Orchestrator/DrillMaster.cs` (extensions) and
`Bagira.Orchestrator/ClusterConfiguration.cs` (new)

#### Mandatory Node Configuration

`DrillMaster` reads a `ClusterConfiguration` at startup from `orchestrator-config.json`.
Only subsystem names are listed — `NodeId` values are discovered dynamically via heartbeats:

```json
{
  "mandatory": ["SimHost", "CGF"],
  "optional":  ["IG", "DataLogger"],
  "heartbeatTimeoutSeconds": 5,
  "transactionHistoryCapacity": 50
}
```

Nodes whose `SubsystemName` is in neither list are treated as transient observers.

#### Bootstrap Phase

`DrillMaster` starts in an internal `Bootstrapping` latch state (not a DSM state —
purely local bookkeeping):
- All incoming `SysOpRequest` commands are rejected with `OpStatus.Rejected`.
- Once **all** subsystems named in `mandatory` appear in the heartbeat roster with
  `LocalDsmState == Standby`, the latch clears.
- On latch clear: publish `SystemStateTopic { CurrentState = Standby }` and begin
  accepting `SysOpRequest` commands.
- If a mandatory node later drops off, the latch re-engages.

#### Emergency Eviction Path (Dead-Node 2PC Deadlock Prevention)

Standard 2PC requires ACKs from every node in `PendingNodes`. If a mandatory node
crashes mid-transaction, the loop hangs forever. When the heartbeat monitor detects
`SecondsSinceLastHeartbeat > HeartbeatTimeoutSeconds` for any node:

```
EjectNode(nodeId):
  1. Cancel active DistributedTransaction for that nodeId — remove from PendingNodes.
     If PendingNodes becomes empty, proceed with Commit as normal.
  2. Remove nodeId from NodeRoster.ActiveNodes.
  3. If node was mandatory:
       a. Abort current transaction if still open.
       b. Publish SystemStateTopic(Degraded).
       c. Send NodeOpCommand(AbortTransaction) to all surviving nodes.
       d. Send NodeOpCommand(PrepareState, Standby) to surviving nodes.
          Evaluate this recovery 2PC only against the reduced roster.
       e. Re-engage bootstrap latch: system is locked until mandatory nodes return.
```

#### Orchestrator ImGui Panel

The Orchestrator process **bypasses `WaitingRoomCoordinator`** (boots instantly, renders
immediately). UI behaviour:
- While `!bootstrapComplete`: a prominent banner "Waiting for: SimHost, CGF" is shown;
  all simulation control buttons are **disabled**.
- Once operational: full control panel is enabled.
- **System health table:** one row per known node — NodeId, SubsystemName, ms since last
  heartbeat, LocalDsmState, CPU%, RAM used.
- **2PC history table:** reads `TransactionHistory` ring-buffer directly (same-process
  memory, no lock needed for ImGui read). Shows per-node ACK latency in milliseconds
  for the last N transactions.
- **Time control:** Pause / Resume / SetSpeed / Step — invokes `DistributedTimeCoordinator`
  methods directly.
- **Scenario controls:** Initialize Live / Load Scenario / Save Scenario / Init Replay
  (selection from recorded DrillId list) / Story list + inject/unload.

#### 2PC History Ring Buffer

`DrillMaster` maintains a `DistributedTransaction[]` circular buffer of capacity
`TransactionHistoryCapacity`. Completed and aborted transactions are written here
immediately. The ImGui panel reads the buffer directly; no serialization required.

**Milestone validation:** See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0105).

---

### 3.6 Stage 1.6 — Orchestrator ImGui Scenario & Story Controls

**Extends:** Stage 1.5 (Orchestrator Health Monitoring & Bootstrap Recovery)

The existing health-monitoring panel covers system status, 2PC history, and time
controls. This stage adds the **scenario and story management frontend**: the ImGui
layer that translates operator intent into `SysOpRequest` DDS messages dispatched to
`DrillMaster`. The panel has zero knowledge of the transition planner or 2PC protocol —
it simply constructs `SysOpRequest` DTOs and calls `DrillMaster.HandleSysOpRequestAsync()`.

**Location:** `Bagira.Orchestrator/UI/OrchestratorScenarioPanel.cs`

#### Panel Layout

| Section | Controls |
|---------|----------|
| **Status Banner** | Read-only: current `DSMState`, active `DrillId` (short hex), in-flight transaction ID + elapsed ms. Always rendered. |
| **Drill Control** | Buttons: [Init Live] [Stop Live] [Init Edit] [Stop Edit] [Dry Run] [Stop Dry Run] [Init Replay] [Stop Replay]. Each emits the appropriate `SysOpRequest(TransitionState, TargetState)`. Entire row is disabled while `!bootstrapComplete` or while a transaction is in-flight; hovering shows a tooltip explaining the reason. |
| **Checkpoint** | [Take Checkpoint] → `SysOpRequest(TakeSnapshot)`. Button disabled outside `RunningLive`. |
| **Scenario** | [Save Scenario] text input + button → `SysOpRequest(SaveScenario, scenarioId)`. [Load Scenario] dropdown (names pulled from NAS via `StorageGatewayModule.ListScenariosAsync()`) + [Load into Edit] / [Load into Live] split button. |
| **Replay** | Dropdown listing recorded `DrillId`s from NAS. [Load Replay] → `SysOpRequest(TransitionState, RunningReplay, drillId)`. When `CurrentState == RunningReplay`, a seek slider (in whole-second steps, converted to wall ticks) is shown; dragging emits `SysOpRequest(ReplaySeek, targetWallTicks)`. |
| **Stories** | Scrollable list of active story GUIDs from `OrchestratorContextTopic.ActiveStories`. [Inject Story] two text inputs (ScenarioId + StoryId) + button → `SysOpRequest(ManageStory, {Mode:Start, StoryId, ScenarioId})`. Per-row [Unload] button → `SysOpRequest(ManageStory, {Mode:Stop, StoryId})`. |

#### Interaction Contract

```
OrchestratorScenarioPanel
   ↓ builds SysOpRequest
DrillMaster.HandleSysOpRequestAsync()
   ↓ BFS → 2PC → DDS broadcast
All leaf DrillSlaves
```

`OrchestratorScenarioPanel` holds no async state machines. It fires requests and the
panel's status banner (polling `DrillMaster.CurrentState` each frame) reflects the
outcome when the transaction completes.

**Milestone validation:** See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0106--orchestrator-imgui-scenario--story-controls).

---

## 4. Phase 2 — State & Time: DSM and Synchronization

**Goal:** Prove the cluster can traverse the DSM safely; prove deterministic lockstep
across nodes; validate the Future Barrier time-mode swap is frame-perfect.

### 4.1 Stage 2.1 — BFS Transition Planner

**Location:** `Bagira.Orchestrator/TransitionPlanner.cs`

Every `SysOpRequest(TransitionState, Target)` is resolved by a **Breadth-First Search**
over the DSM directed graph into a `Queue<ISysOpStep>`. This is a pure Bagira
application-layer class — no FDP dependency.

#### Valid DSM Adjacency List

```
Standby          → LoadingEdit, LoadingLive, LoadingReplay
LoadingEdit      → RunningEdit, Standby (failure)
RunningEdit      → LoadingDryRun, UnloadingEdit
LoadingDryRun    → RunningDryRun, RunningEdit (failure)
RunningDryRun    → UnloadingDryRun
UnloadingDryRun  → RunningEdit
UnloadingEdit    → Standby
LoadingLive      → RunningLive, Standby (failure)
RunningLive      → UnloadingLive
UnloadingLive    → Standby
LoadingReplay    → RunningReplay, Standby (failure)
RunningReplay    → UnloadingReplay, LoadingLive (Live-from-Replay)
UnloadingReplay  → Standby
```

> **¹ RunningEdit → LoadingLive removed:** An active Edit session must be explicitly
> unloaded before a Live load can begin. The trajectory table row
> `RunningEdit → RunningLive = [UnloadingEdit, Standby, LoadingLive, RunningLive]`
> is normative; appending a direct `RunningEdit → LoadingLive` shortcut would bypass
> the unload phase and leave edit-session resources dangling.

#### Example Trajectories

| From | To | Planned Steps |
|------|-----|---------------|
| `Standby` | `LoadingEdit` | `[TransitionStep(LoadingEdit)]` |
| `RunningLive` | `RunningReplay` | `[UnloadingLive, Standby, LoadingReplay, RunningReplay]` |
| `RunningLive` | `RunningReplay + seek T15` | same 4 + `OperationStep(ReplaySeek, T15)` |
| `RunningEdit` | `RunningLive` | `[UnloadingEdit, Standby, LoadingLive, RunningLive]` |

If BFS exhausts the graph without finding the target, it throws
`InvalidOperationException` **before** any `NodeOpCommand` is broadcast.

**Optional operation hints** from `SysOpRequest.PayloadJson` (e.g. `TargetWallTicks`)
cause the planner to append `OperationStep` entries after the final `TransitionStep`.

**Milestone validation:** Pure unit test — no DDS involved. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0201).

---

### 4.2 Stage 2.2 — DSM Handler Wiring

For each `NodeOpCommand` that arrives, `DrillSlave` dispatches to matching
`IDsmHandler` implementations and bridges async results back to the main thread.

#### DSM State Change Internal Event

```csharp
// Location: FDP/Kernel/Fdp.Kernel/Events/EsmStateChangedEvent.cs  (new)
// This is a generic FDP event — it uses DSMState from Bagira.DDS.DataModel
// via a project reference that Fdp.Kernel does NOT have.
// Therefore EsmStateChangedEvent is declared in Bagira application layer, NOT in Fdp.Kernel.
// Correct location: Bagira.Runner/Events/DsmStateChangedEvent.cs  or Bagira.Common
public struct DsmStateChangedEvent
{
    public DSMState Previous;
    public DSMState Next;
}
```

> **Layering note:** Because `DSMState` is defined in `Bagira.DDS.DataModel`
> (Bagira application layer), any event carrying `DSMState` fields must also live in
> the Bagira layer. `DsmStateChangedEvent` is therefore placed in a shared
> `Bagira.Common` or `Bagira.Runner` project — **not** in `Fdp.Kernel`.

After a `CommitState` command is processed, `DrillSlave` publishes
`DsmStateChangedEvent` to the local `FdpEventBus`. Domain systems (e.g. physics,
recording, AI) subscribe without knowing about DDS or orchestration:

```csharp
_eventBus.Subscribe<DsmStateChangedEvent>(OnDsmStateChanged);
```

**Milestone validation:** Unit test with mock `NodeOpCommand(CommitState, LoadingLive)`;
assert the event bus receives `DsmStateChangedEvent{Next = LoadingLive}`. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0202).

---

### 4.3 Stage 2.3 — Time Strategy Proxying

#### ITimeController Interface

> **Note:** `ITimeController` and all concrete strategies (`MasterTimeController`,
> `SlaveTimeController`, `SteppedMasterController`, `SteppedSlaveController`,
> `SwitchableTimeController`) already exist in `FDP.Toolkit.Time`. Stage 2.3 extends
> them with `SeedState()` and `TotalWallTicks` — it does not create them from scratch.

```csharp
// Location: FDP/Toolkits/FDP.Toolkit.Time/ITimeController.cs  (already exists; extended)
// Pure FDP — no Bagira reference
public interface ITimeController
{
    GlobalTime Update();
    void       SetTimeScale(float scale);
    float      GetTimeScale();
    TimeMode   GetMode();
    GlobalTime GetCurrentState();
    void       SeedState(GlobalTime seed);   // Abrupt reset — bypasses PLL
    void       Dispose();
}

public enum TimeMode : int { Continuous = 0, Deterministic = 1 }
```

#### SwitchableTimeController Proxy

Already exists at `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SwitchableTimeController.cs`.  
Verify it exposes `SwitchTo(ITimeController)` which gracefully transfers `GetCurrentState()`
to the new strategy. **Only** the coordinator layer (`DistributedTimeCoordinator`,
`SlaveTimeModeListener`) calls `SwitchTo()`. Domain code is unaware.

The `ModuleHostKernel` holds a stable reference to the `SwitchableTimeController` proxy.

**Milestone validation:** Proxy isolation unit test. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0203).

---

### 4.4 Stage 2.4 — Future Barrier Implementation

Switching between continuous real-time and deterministic lockstep must happen on the
**exact same ECS frame** on every node — not at the moment each node receives a
DDS message (which would differ by network latency).

#### SwitchTimeModeEvent DDS Message

```csharp
// Location: FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs  (addition)
// Internal to the time toolkit. Not a public orchestration topic.
[DdsTopic("SwitchTimeModeEvent")]
[DdsIdlFile("bdc-time")]
[DdsQos(Reliability=Reliable, Durability=Volatile)]
public struct SwitchTimeModeEvent
{
    public TimeMode TargetMode;
    public long     BarrierWallTicks;  // GlobalTime.TotalWallTicks at which each node must swap
    public float    FixedDelta;        // Only meaningful when TargetMode == Deterministic
}
```

> **Why `GlobalTime.TotalWallTicks`, not ECS frame counter or `DateTime.UtcNow.Ticks`:**
>
> In non-deterministic (real-time) mode nodes run their ECS loops **asynchronously at
> different frame rates**. There is no globally shared frame counter; each node's local
> counter advances independently with CPU load and rendering overhead. A frame-number
> barrier would fire at different simulation instants on each node, destroying determinism.
>
> Raw OS clock (`DateTime.UtcNow`) is also unsuitable: NTP does not guarantee sub-100 ms
> precision and introduces unpredictable slew jumps that are fatal to frame-perfect
> simulation coordination.
>
> `GlobalTime.TotalWallTicks` is the FDP **virtual wall clock**: a high-resolution
> Stopwatch-based timestamp maintained by `MasterTimeController` and synchronized to
> every slave via the `SlaveTimeController` Phase-Locked Loop (PLL). The PLL filters
> network jitter to keep all slaves' `TotalWallTicks` within milliseconds of the master,
> making it the only globally coherent timestamp available in asynchronous
> non-deterministic mode.

#### Future Barrier Protocol

```
Master (DistributedTimeCoordinator):
  1. Read currentState = _masterTime.GetCurrentState()
  2. BarrierWallTicks = currentState.TotalWallTicks + LookaheadTicks
     (LookaheadTicks = e.g. 200ms × Stopwatch.Frequency / 1000 — configurable;
      must be large enough for DDS delivery across the LAN)
  3. Optionally call SetTimeScale(0.0) if the switch requires a sim freeze first
  4. Publish SwitchTimeModeEvent { TargetMode, BarrierWallTicks, FixedDelta }
     via BlitEventTranslator (zero-allocation raw memcpy)
  5. Each update: when _masterTime.GetCurrentState().TotalWallTicks >= BarrierWallTicks →
     _switchableTime.SwitchTo(new SteppedMasterController(...))
  6. Restore saved TimeScale

Slave (SlaveTimeModeListener — FDP.Toolkit.Time):
  1. Receives SwitchTimeModeEvent from DDS; stores BarrierWallTicks
  2. Simulates normally; each tick reads _kernel.CurrentTime.TotalWallTicks
  3. When TotalWallTicks >= BarrierWallTicks →
     _switchableTime.SwitchTo(new SteppedSlaveController(...) or SlaveTimeController)
```

Because all slaves' `TotalWallTicks` are PLL-synchronized to the master virtual clock,
the swap fires within one ECS tick of each other across the entire cluster, regardless
of individual frame rates.

**Milestone validation:** `GlobalTime.TotalWallTicks`-based barrier test — master sets
`BarrierWallTicks = currentTime.TotalWallTicks + 200ms`; slave asserts `SwitchTo()` is
called only after slave's `TotalWallTicks >= BarrierWallTicks` and not before. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0204).

---

### 4.5 Stage 2.5 — Deterministic CI Hookup

Wire the minimalist CGF and SimHost to accept `"TimeMode": "Deterministic"` in the
`LoadingLive` payload and run headlessly via `SteppedMasterController` /
`SteppedSlaveController`.

```json
// SysOpRequest.PayloadJson example
{
  "TargetState": "LoadingLive",
  "ScenarioId": "MinimalCI_01",
  "TimeMode": "Deterministic",
  "FixedDeltaSeconds": 0.016667
}
```

The existing `IScenario` CI contract provides the deterministic test harness. Exit codes:
`0` = success, `1` = assertion failure, `2` = timeout.

**Milestone validation:** `dotnet run --project Bagira.Runner -- --mode ci
--scenario MinimalCI_01` exits with code 0 within 30 s. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0205).

---

## 5. Phase 3 — Persistence: Scenarios, Checkpoints & Replay

**Goal:** Add non-blocking recording, replay, binary checkpointing, and scenario file management.

### 5.1 Stage 3.1 — Storage Gateway

**Location:** `Bagira.Orchestrator/StorageGatewayModule.cs`

Single component co-located with `DrillMaster`. Owns all bulk file movement (Scenarios,
Checkpoints, Archive Export/Import) using the **SMB Pull Gateway Pattern**:

- Receives a UNC manifest from the Master after all node ACKs.
- Opens **one outbound SMB connection** to the central NAS.
- Pulls files from leaf nodes via parallel outbound reads (`Parallel.ForEach`,
  `MaxDegreeOfParallelism = 8`).
- Streams bytes into the NAS connection via `RelativeDest` path.

This pattern avoids the OS inbound SMB connection limit (~20 on client SKUs) that
would be exceeded if all nodes wrote to the NAS simultaneously.

**Milestone validation:** Local mock test with 5 simulated leaf node manifests; assert
all 5 files appear in the target directory via a single SMB pass. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0301).

---

### 5.2 Stage 3.2 — Portable Scenario Loading

**Pre-fetch barrier:** `TransitionPlanner` injects a storage gateway pre-fetch step
before entering `LoadingEdit` (when `ScenarioId != null`). Scenario JSON files are
pushed to each leaf node's local `C:\FDP_Temp\` before the 2PC `PrepareState` command
is issued. Leaf nodes parse local files only — the ECS tick is never blocked by network I/O.

**EditLoadDsmHandler** (per-node, Bagira layer):
- Reads `IsNewScenario`, `ScenarioId`, `Overrides` from `NodeOpCommand.PayloadJson`.
- On new scenario: bootstraps blank world from `BaseTerrain`.
- On load existing: opens pre-fetched JSON, applies `Overrides`.
- Entities spawned by `LoadingEdit` are non-default overrides only — not the full raw ECS chunk.

**Milestone validation:** JSON instantiation test; assert entity count after
`EditLoadDsmHandler.Commit(...)` matches the scenario JSON without blocking the
main thread. See [CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0302).

---

### 5.3 Stage 3.3 — 3-Step Binary Checkpointing

Checkpointing must satisfy two competing constraints:
1. The 60 Hz hot-path **must never stall** on disk I/O (LZ4 + SSD write: 0.5–3 s).
2. `NodeOpStatus(Success)` must not be sent until bytes are **physically on disk** (2PC ACID contract).

#### Three-Step Protocol

```
Step 1 — Immediate InProgress ACK   (network thread, instant)
Step 2 — Synchronous RAM clone       (main thread, BeforeSync, ~2 ms)
          snap.SyncFrom(liveRepo)    — unmanaged NativeChunkTable memcpy
Step 3 — Deferred Success ACK        (background CheckpointIOWorker thread)
          LZ4 compress → SSD write → CompletionResult[ReqId] = Success
          DrillSlave.Tick() monitor → publish NodeOpStatus(Success) when found
```

**CheckpointIOWorker** (`FDP/Kernel/Fdp.Kernel/Orchestration/CheckpointIOWorker.cs`):
- Drains a `ConcurrentQueue<(EntityRepository snapshot, Guid requestId)>` one item at a time.
- Prevents CPU-cache thrashing and disk contention under overlapping checkpoint requests.
- Exposes `DrainAsync()` for the `UnloadingLive` teardown barrier (blocks until queue empty).

**Concurrent checkpoint support:** `TakeCheckpoint` is non-exclusive. Multiple
`DistributedTransaction` instances run concurrently. ACKs arrive asynchronously in
disk-write completion order.

**Milestone validation:** Two overlapping `TakeCheckpoint` requests; assert (a) both
`InProgress` ACKs arrive immediately, (b) both `Success` ACKs are deferred until after
disk I/O, and (c) the second snapshot captures state that postdates the first. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0303).

---

### 5.4 Stage 3.4 — Dynamic Recording Modules

The recording/replay subsystem is split across a **Control Plane** (factory &
orchestrator) and a **Data Plane** (disk I/O and ECS memory blasting):

```
Control Plane (Bagira layer):
  EcsRecordReplayController — IDsmHandler
    PrepareRecordingAsync(drillId) → new RecordingModule(config) → kernel.InstallModule()
    FinalizeRecordingAsync()       → kernel.UninstallModule() → RecordingModule.Dispose()
    PrepareReplayAsync(drillId)    → new ReplayModule(config)  → kernel.InstallModule()
    TeardownReplayAsync()          → kernel.UninstallModule() → ReplayModule.Dispose()

Data Plane (Bagira layer, managed by ModuleHostKernel):
  RecordingModule   — IModule + IDisposable; strictly owns AsyncRecorder
  ReplayModule      — IModule + IDisposable; strictly owns PlaybackController
```

**No recording during `RunningEdit`:** Uninstalling `RecordingModule` at `LoadingEdit`
physically removes `RecorderTickSystem` from the 60 Hz scheduler — zero `if (isRecording)`
boolean checks on the hot path.

**IRecordReplayController** (FDP layer, application-agnostic):

```csharp
// Location: FDP/Kernel/Fdp.Kernel/Orchestration/IRecordReplayController.cs
public interface IRecordReplayController
{
    Task PrepareRecordingAsync(Guid drillId, string storageDirectory);
    Task FinalizeRecordingAsync();
    Task PrepareReplayAsync(Guid drillId, string storageDirectory);
    Task SeekToTimeAsync(long targetWallClockTicks);
    void ProcessPlaybackTick(GlobalTime currentTime);
    Task TeardownReplayAsync();
}
```

> `IRecordReplayController` lives in `Fdp.Kernel` and is generic—it uses only `Guid`,
> `string`, and `GlobalTime` (already in `Fdp.Kernel`). No `DSMState`, no `DrillId`
> type beyond `Guid`, no Bagira references.

**RecordingConfiguration** (FDP layer):

```csharp
// Location: FDP/Kernel/Fdp.Kernel/Orchestration/RecordingConfiguration.cs
public sealed class RecordingConfiguration
{
    public required string FilePath    { get; init; }
    public EntityQuery?    EntityFilter { get; init; }  // null = record all above MinRecordableId
    public required Guid   DrillId     { get; init; }
}
```

**Milestone validation:** Module topology test; after `PrepareRecordingAsync`, assert
`Kernel.GetRegisteredModuleTypeNames()` contains `RecorderTickSystem`. After
`FinalizeRecordingAsync`, assert it is absent. See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0304).

---

### 5.5 Stage 3.5 — Live-from-Replay Temporal Interlock

Branching from `RunningReplay` into a new live session requires the entire cluster to
be **hard-frozen** while disk adapters are swapped, so every node branches at the
identical simulation timestamp.

#### Protocol

1. **Master hard-freezes time** → `SetTimeScale(0.0)`, halts `TimePulseDescriptor` broadcast.
2. **Master generates new `DrillId`** (e.g. `Drill_999_Branch1`).
3. **2PC `PrepareLive` command** → nodes call `TeardownReplayAsync()` then
   `PrepareRecordingAsync(newDrillId)`.  
   `TeardownReplayAsync()` uninstalls `ReplayModule` (disposes `PlaybackController`)
   while **leaving `EntityRepository` memory intact** — zero-copy state retention.  
   `PrepareRecordingAsync()` installs a fresh `RecordingModule`. The opening keyframe
   captures the preserved historical ECS state.
4. **Re-arm live pipeline** → `SimulationSystemGroup.Enabled = true`,
   `NetworkLifecycleSystemGroup.Enabled = true`,
   `GhostCreationSystem.BypassLifecycle = false`.
5. **Commit → `RunningLive`**, publish updated `SystemStateTopic`.
6. **Master resumes time** → `SetTimeScale(savedScale)`.

**Key guarantee:** AI and physics systems simply see `GlobalTime.DeltaTime == 0`
for the freeze duration. When the pulse resumes, they execute from the injected
historical state without any awareness of the branch.

**Milestone validation:** Zero-allocation branch test; after `TeardownReplayAsync`,
assert the `EntityRepository.NativeChunkTable` entity count is unchanged (historical
state preserved in-place). See [CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0305).

---

### 5.6 Stage 3.6 — Scenario/Story Serialization Toolkit

**New project:** `FDP/Toolkits/FDP.Toolkit.Scenario`  
Pure FDP toolkit — **no Bagira references**. Provides the format-agnostic DOM
serialization engine. Operates on in-memory DOM objects (compatible with
`System.Text.Json.Nodes.JsonObject`). Knows nothing about files, DDS, or the DSM.

#### JSON Schema (Format Contract)

```json
{
  "Header": {
    "SubsystemType": "Bagira.CGF",
    "SchemaVersion": 1
  },
  "Entities": {
    "<persistable-guid>": {
      "ComponentName": { "field1": "...", "field2": "..." }
    }
  }
}
```

- `SubsystemType` is the **edge-filter key**: a subsystem whose canonical name does
  not match this header ignores the file entirely and returns `Success` immediately
  from its DSM handler — no full DOM parse required.
- A story file uses the **exact same schema**. The loader stamps **`FDP.Toolkit.Replay.StoryTag`**
  (`Guid StoryId`) on each created entity when called with `asStory: true` — **one** canonical
  story marker type shared with **`StoryRecorderModule`** (do not duplicate a second `StoryTag` in
  `FDP.Toolkit.Scenario`).
- `ComponentName` entries come from **both** custom translators and the 1:1
  auto-serializer fallback; a single entity can have N ECS components serialize
  into M scenario components (N:M).

#### IEntityScenarioTranslator (Non-Generic, N:M)

Custom translators handle cases where ECS component groups must be compressed or
restructured for the scenario file (e.g. `BallisticProjectile` + `PhysicsCollider`
→ single `"OrdnanceDef"` JSON entry, or `NavigationStatus` + `NavState` + `LocomotionChannel`
→ `"ScenarioMovement"` + `"ScenarioPath"`).

```csharp
// FDP/Toolkits/FDP.Toolkit.Scenario/IEntityScenarioTranslator.cs
public interface IEntityScenarioTranslator
{
    // Which ECS component type IDs this translator will consume.
    // The serializer clears these bits from the consumption mask so the
    // auto-serializer fallback skips them entirely.
    BitMask256 GetConsumedComponentsMask();

    bool CanTranslate(EntityRepository repo, Entity entity);

    // Returns Dict keyed by desired scenario component name (1 or more entries).
    Dictionary<string, object> Extract(
        EntityRepository repo, Entity entity, IGuidResolver guidResolver);

    // Reconstitutes N ECS components from the saved Dict entries.
    void Inject(
        EntityRepository repo, Entity entity,
        Dictionary<string, object> scenarioData, IGuidResolver guidResolver);
}
```

#### IGuidResolver

Passed into `Extract` and `Inject` so translators can patch volatile `Entity`
handles ↔ persistent `Guid` strings without coupling to serialization plumbing:

```csharp
// FDP/Toolkits/FDP.Toolkit.Scenario/IGuidResolver.cs
public interface IGuidResolver
{
    string Resolve(Entity entity);           // save-time: Entity → stable Guid string
    Entity Resolve(string guidString);       // load-time: Guid string → live Entity
}
```

The `ScenarioSerializer` builds the `IGuidResolver` during the first pass (entity
enumeration), before any `Extract` or `Inject` calls.

#### FdpAutoSerializer — 1:1 JIT Fallback

`FdpAutoSerializer` handles the majority of components that map directly 1:1 to
scenario JSON objects. It operates on whatever component bits remain in the
**consumption mask** after all custom translators have run.

- `Build()` is called once at startup. For each component type registered in the
  kernel's `ComponentTypeRegistry`, it uses `Expression.Property` to compile
  typed delegates (`Func<object, JsonObject>` extract, `Action<JsonObject, object>`
  inject). No `Type.GetProperties()` calls on the hot path.
- It respects `[DataPolicy(DataPolicy.NoSave)]` (already filtered out by
  `EntityRepository.GetSaveableMask()` before the auto-serializer even runs).
- It respects `[ScenarioIgnore]` on individual fields: those property delegates are
  simply not compiled into the extraction function.
- `Entity`-typed fields in any component are automatically patched via `IGuidResolver`.

#### Data Policy and Exclusion Mechanisms

Three complementary exclusion mechanisms, each operating at a different granularity:

| Granularity | Mechanism | Where declared |
|-------------|-----------|----------------|
| Whole component excluded from all saves | `[DataPolicy(DataPolicy.NoSave)]` on the struct | FDP component definition (or registration-time override — see below) |
| Individual field excluded from scenario | `[ScenarioIgnore]` on the field | FDP component definition |
| Whole entity excluded from scenario | `[DataPolicy(DataPolicy.NoSave)] public struct ScenarioIgnoreTag {}` + `.Without<ScenarioIgnoreTag>()` query filter | Application layer |

**Registration-time policy override:** The application layer can override the
attribute-based `DataPolicy` at startup:

```csharp
// In SimHostApp or CgfApp module setup:
repo.RegisterComponent<SharedToolkitComponent>(DataPolicy.Default);
// Forces inclusion even if the struct has [DataPolicy(DataPolicy.NoSave)]
```

This is the correct intercept point for cases where a shared FDP toolkit component
carries `NoSave` by default but a specific subsystem needs it in its scenario file.
Custom `IEntityScenarioTranslator` supremacy overrides even this: if a translator's
`CanTranslate` returns `true`, its bits are consumed regardless of `DataPolicy`.

#### Orchestrated Save Pipeline Per Entity

```
GetSaveableMask(entity)          // FDP already filters DataPolicy.NoSave
  → run each registered IEntityScenarioTranslator:
      if CanTranslate → Extract → add named entries → clear consumed bits
  → run FdpAutoSerializer on remaining set bits:
      for each bit → auto-extract with [ScenarioIgnore] field skips
                     + IGuidResolver patches on Entity-typed fields
```

#### ScenarioSerializerBuilder

```csharp
var serializer = new ScenarioSerializerBuilder()
    .RegisterTranslator(new MissileOrdnanceTranslator())   // no type param
    .RegisterTranslator(new InfantryBrainTranslator())
    .Build();   // compiles FdpAutoSerializer delegates; freezes translator list
```

`Build()` compiles `FdpAutoSerializer` delegates for every component registered in
`ComponentTypeRegistry` that is not fully covered by a custom translator's
`GetConsumedComponentsMask()`. Custom translator objects are stored as-is; their
`Extract`/`Inject` methods run at hot-path without any additional wrapper allocation.

#### DOM-aware FdpAutoserializer
To maintain a strict separation of concerns, we must distinguish between the existing `FdpAutoSerializer` utility and the actual scenario serialization pipeline we are building. 

The native `FdpAutoSerializer` strictly produces and consumes a raw binary byte stream via `System.IO.BinaryWriter` and `System.IO.BinaryReader`. It is a highly optimized tool built specifically for the Flight Recorder to capture binary snapshots of memory. Because the architectural design explicitly dictates that scenarios must use a forward- and backward-compatible JSON format—rather than a raw binary memory dump—we cannot use the output of the `FdpAutoSerializer` directly for saving scenarios.

However, we reuse its brilliant underlying architectural paradigm: JIT-compiled Expression Trees. 

Instead of routing data to a `BinaryWriter`, we build a scenario-specific 1:1 fallback serializer that generates expression trees tailored for a Document Object Model (DOM). By compiling these delegates at runtime, we avoid the heavy CPU and allocation overhead of standard reflection on the hot path. 

Here is exactly how the data formats flow through this scenario-specific engine:

**1. Extraction (Saving)**
*   **Consumes:** Pure, unmanaged ECS structs read directly from the highly-optimized chunk tables via `GetComponentRO<T>()`. 
*   **Produces:** An intermediate, format-agnostic DOM representation (such as a nested `Dictionary<string, object>` or `JObject`). The JIT-compiled delegate extracts the fields of the struct (ignoring any marked with `[ScenarioIgnore]`) and maps them to JSON primitives where keys are the property names. Any volatile `Entity` handles are resolved into persistable GUIDs during this step.

**2. Injection (Loading)**
*   **Consumes:** The in-memory DOM object parsed from the JSON scenario file. 
*   **Produces:** Reconstructed, raw ECS structs. The JIT-compiled loading delegate reads the JSON primitives, resolves the persistent GUIDs back into live `Entity` indices, populates the unmanaged struct, and injects it back into the ECS world via the `EntityCommandBuffer` or `EntityRepository`.

This design creates a perfect architectural boundary. It leverages the zero-allocation, high-performance JIT techniques of the `FdpAutoSerializer`, while strictly adhering to the requirement that the application layer handles the final JSON schema representation.


**Milestone validation:** See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0306).

---

### 5.7 Stage 3.7 — Application-Layer Scenario Save/Load Wiring

Wires `FDP.Toolkit.Scenario` into the Bagira nodes (SimHost, CGF, Orchestrator). Each
node owns a single file in the multi-file scenario bundle.

#### Orchestrator's Own DrillSlave + GlobalContextDsmHandler

The Orchestrator has no ECS, but it still participates in scenario save/load. It holds
its own `DrillSlave` instance and registers a `GlobalContextDsmHandler`:

- **Save path** (`SerializeLocal`): serializes global context (simulation start wall
  ticks, global weather descriptor, scene identifier) to
  `C:\FDP_Temp\<DrillId>\Orchestrator.json`. Returns the path as a UNC manifest entry.
- **Load path** (`CommitState(LoadingLive|LoadingEdit)`): parses the pre-fetched
  `Orchestrator.json`, writes `GlobalTime` epoch into `MasterTimeController.SeedState()`,
  publishes weather + scene metadata to the `OrchestratorContextTopic`.

#### SubsystemType Header for Edge Filtering

Every scenario JSON file carries `Header.SubsystemType`. Each subsystem's DSM handler
peeks at this field before embarking on a full DOM parse:

```csharp
if (header.SubsystemType != _subsystemTypeName)
    return null;  // PrepareAsync success — skip gracefully
```

StorageGateway pushes the full set of scenario files to **all** nodes without
routing logic; each node self-selects its own file.

#### Multi-File Scenario Save Flow

1. Orchestrator broadcasts `NodeOpCommand(SerializeLocal, scenarioId)`.
2. Each node serializes its own files to local SSD.
3. Each node returns a UNC manifest in `NodeOpStatus.ResultJson`.
4. `StorageGatewayModule` pulls all manifests and copies all files to NAS under
   `\\NAS\Scenarios\<scenarioId>\`.
5. `scenario_manifest.json` (written by Orchestrator) lists all participating files.

#### Multi-File Scenario Load Flow

1. `TransitionPlanner` inserts a Storage Gateway pre-fetch step when `ScenarioId != null`.
2. All scenario files for that ID are copied from NAS to each node's
   `C:\FDP_Temp\<scenarioId>\`.
3. Each node's DSM handler finds its own file via header match, deserializes, and
   spawns entities.

**Milestone validation:** See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0307).

---

### 5.8 Stage 3.8 — Runtime Story Injection & Deletion

Stories execute while the global cluster remains in `RunningLive`. The
`TransitionPlanner` models story injection as an **`OperationStep(ManageStory)`** —
not a `TransitionStep` — so the cluster state does not change.

#### Injection Flow

1. IOS/UI sends `SysOpRequest(ManageStory, { StoryId, ScenarioId, Mode:Start })`.
2. `TransitionPlanner` validates `CurrentState == RunningLive`; appends
   `OperationStep(StartStory, payload)`.
3. **Pre-fetch:** `StorageGatewayModule` copies story files from NAS to all node
   local temp dirs before the 2PC broadcast.
4. Master broadcasts `NodeOpCommand(StartStory, payload)` to all nodes.
5. Each `DrillSlave` receives the command:
   - Peeks at `Header.SubsystemType` in the story JSON.
   - **Mismatch:** replies `NodeOpStatus(Success, IsParticipating: false)` immediately.
   - **Match:** calls `ScenarioSerializer.Deserialize(..., asStory: true, storyId)` (**`storyId`: `Guid`**):
     - Spawns entities; stamps **`FDP.Toolkit.Replay.StoryTag { StoryId }`** on each.
     - Replies `NodeOpStatus(Success, IsParticipating: true)`.
6. Master records the story as active in `OrchestratorContextTopic.ActiveStories`.

#### Deletion Flow

1. IOS/UI sends `SysOpRequest(ManageStory, { StoryId, Mode:Stop })`.
2. `TransitionPlanner` appends `OperationStep(StopStory, { StoryId })`.
3. Master broadcasts `NodeOpCommand(StopStory, { StoryId })`.
4. Each matching `DrillSlave` queries all entities with **`FDP.Toolkit.Replay.StoryTag.StoryId == storyId`**
   (**`Guid`**) and destroys them.  Non-matching nodes reply `IsParticipating: false`.
5. Master removes the story from `OrchestratorContextTopic.ActiveStories`.

#### `IsParticipating` Opt-Out Semantics

`NodeOpStatus.IsParticipating` signals intentional non-participation (not a failure).
The Master only waits for ACKs from nodes that replied with `IsParticipating: true`
during the Prepare phase. Nodes without matching story content opt out cleanly.

**Milestone validation:** See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0308).

#### ManageStory 2PC — MVP Implementation Note

**BATCH-21:** `DrillMaster` now defers `ActiveStories` mutation until all
targeted-node `NodeOpStatus` ACKs have been consumed via the
`_pendingManageStoryTasks` dictionary (matching the `_pendingSerializeTasks` /
`_pendingBranchTasks` pattern). Policy: every ACK — `IsParticipating=true` or
`false` — removes the node from the pending `RemainingNodeIds` set.  When the
set reaches zero the `ActiveStories` list is updated atomically.  See
[CGF1-S0308](./CGF-1-TASK-DETAIL.md#cgf1-s0308) for the normative end-state.

---

### 5.9 Stage 3.9 — Dry Run DSM Handler

A Dry Run is a **RAM-only, zero-disk variant of checkpointing** that lets an operator
preview a live simulation and then instantly rewind the world back to its exact
pre-dry-run state. It reuses the `EntityRepository.SyncFrom()` primitive that was
introduced for 3-step binary checkpointing (Stage 3.3); the only architectural
difference is that the cloned repository is **never passed to `CheckpointIOWorker`**
— it lives entirely in process RAM for the duration of the dry run.

#### State Machine Integration

The dry-run lifecycle sits entirely within the `RunningEdit` bubble:

```
RunningEdit → LoadingDryRun → RunningDryRun → UnloadingDryRun → RunningEdit
```

`LoadingDryRun` failure falls back to `RunningEdit` (BFS adjacency already
specified in §4.1).

#### Two-Act Protocol

**Act 1 — Snapshot (at `CommitState(LoadingDryRun)`):**
1. Main thread, BeforeSync phase: `_snap = new EntityRepository();`
2. `_snap.SyncFrom(liveRepo)` — unmanaged `NativeChunkTable` memcpy, ~2 ms.
3. Snapshot stays in RAM; `CheckpointIOWorker` is **not** involved.
4. Simulation resumes in `RunningDryRun` — physics, AI, recording all behave
   identically to `RunningLive`.

**Act 2 — Restore (at `CommitState(UnloadingDryRun)`):**
1. Main thread, BeforeSync phase: `liveRepo.SyncFrom(_snap)` — restores the
   world to the frame that was captured in Act 1; ~2 ms.
2. `_snap.Dispose()` — frees the unmanaged chunk memory.
3. `_snap = null`.  Cluster transitions back to `RunningEdit`.

> **Performance contract:** both acts execute synchronously on the main thread
> at BeforeSync, exactly like the Step 2 clone in `CheckpointDsmHandler`.
> Total hot-path cost is two `SyncFrom()` calls (~4 ms each direction);
> no disk I/O, no background threads, no `ConcurrentQueue`.

#### DryRunDsmHandler

**Location:** `Bagira.SimHost/Modules/Orchestration/Handlers/DryRunDsmHandler.cs`

```csharp
// Bagira.SimHost — Bagira application layer only; no Fdp.Kernel references
public sealed class DryRunDsmHandler : IDsmHandler
{
    private readonly EntityRepository?        _liveRepo;
    private          EntityRepository?        _snap;    // null when no dry run is active

    public bool CanHandle(NodeOpType op) => op == NodeOpType.PrepareState;

    // PrepareAsync: no async work for either act — snapshot & restore are
    // synchronous memcpys performed in Commit().
    public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        => Task.FromResult<string?>(null);

    // Commit — Act 1 (LoadingDryRun) or Act 2 (UnloadingDryRun).
    public void Commit(NodeOpCommand cmd, EntityRepository? repo) { /* see task */ }

    public void Abort(NodeOpCommand cmd, EntityRepository? repo)
    {
        // Abort during LoadingDryRun: discard the in-progress snap if any.
        _snap?.Dispose();
        _snap = null;
    }
}
```

`DryRunDsmHandler` does **not** implement `ITickableDsmHandler` — there are no
deferred ACKs to poll because no background I/O thread is involved.

#### Absence of Time Control Changes

The simulation time controller is **not paused** when entering `LoadingDryRun`.
The clock continues exactly as in `RunningLive`. If the operator needs to freeze
time during the dry run they use the existing `SetTimeScale(0.0)` control path —
this is an optional, user-driven action, not a protocol requirement.

**Milestone validation:** See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0309).

---

### 5.10 Stage 3.10 — E2E DSM Test Script Suite

**Depends on:** Stages 3.3, 3.4, 3.5, 3.9, and the existing `HeadlessTestExecutor`
infrastructure in `FDP.Framework.Runner`.

End-to-end validation of the full distributed control plane requires driving the DSM
through its realistic operator workflows and asserting outcomes at the ECS level.
This stage extends the data-driven `HeadlessTestExecutor` pipeline with a DSM-aware
action handler and four focused test scripts that map to the primary Mermaid-diagram
paths in the design.

#### SysopActionHandler

A new `"sysop"` action handler registered alongside the existing `spawn`, `move`, and
`assert_position` handlers in `Bagira.Runner.Testing`:

```csharp
// Location: Bagira.Runner/Testing/OrchestratorActionHandlers.cs
// Constructs a SysOpRequest, dispatches to a local in-process DrillMaster,
// and blocks (up to configurable timeout, default 10 s wall-clock) until
// SysOpStatus(Success) or SysOpStatus(Failure) is received.
new SysopActionHandler(drillMaster, sysOpStatusReader, timeoutSeconds: 10)
```

`Args.TargetState` accepts all `DSMState` string names plus the pseudo-target
`"TakeCheckpoint"` (maps to `SysOpType.TakeSnapshot`) and `"ReplaySeek"` (requires
`Args.TargetWallTicks`). On `SysOpStatus(Failure)` the handler throws
`TestAssertionException`, failing the script immediately with a descriptive message.

#### MovingEntitySystem

A lightweight headless-only ECS system installed only during E2E test boots:

```csharp
// Location: Bagira.Runner.Integration.Tests/Systems/MovingEntitySystem.cs
// Each tick: SimTransform.Position.X += VelocityX * DeltaTime
// Entities tagged with MovingTestTag are automatically driven.
// Provides deterministic per-frame position change for replay-seek assertions.
```

#### E2E Test Scripts

Four JSON scripts installed in `Bagira.Runner.Integration.Tests/TestScripts/`:

| Script | DSM path exercised | Key assertion |
|--------|-------------------|---------------|
| `e2e_record_and_replay_seek.json` | `Standby → RunningLive → RunningReplay` + seek | Replayed `SimTransform.Position.X` within 0.001 m of recorded value at targeted tick |
| `e2e_dryrun_state_restore.json` | `RunningEdit → RunningDryRun → RunningEdit` | Entity position reverts after dry run; 5th entity spawned during dry run is gone |
| `e2e_live_from_replay_branch.json` | `RunningReplay → RunningLive` (branch) | New entity spawnable post-branch; `MaxNetworkId` watermark not colliding |
| `e2e_overlapping_checkpoints.json` | Two rapid `TakeSnapshot` operations in `RunningLive` | Both checkpoints succeed (deferred ACKs arrive); entity state intact post-checkpoint |

All scripts run under `RunnerOptions.Headless = true` with `SteppedMasterController`
(deterministic time-stepping, no wall-clock dependency). Each script is executed by a
standalone xUnit `[Fact]` in `DsmE2eScriptTests` that boots the full in-process stack
(Orchestrator + SimHost via `SubsystemOrchestrator`) and asserts process exit code 0.

**Milestone validation:** See
[CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md#cgf1-s0310--e2e-dsm-test-script-suite).

---

## 6. Phase 4 — Generalization: FDP Toolkit Orchestration

Phase 4 lifts the reusable orchestration engine — `IDsmHandler`, the `DrillSlave`
dispatch loop, the BFS `TransitionPlanner`, and all reference handler implementations
— out of the Bagira application layer into a new `FDP.Toolkit.Orchestration` toolkit
project. The full design is maintained in a dedicated companion document:

> **→ [CGF-1-GENERALIZATION.md](./CGF-1-GENERALIZATION.md)**

The generalization is organized as six tasks in
[CGF-1-TASK-DETAIL.md §Phase 4](./CGF-1-TASK-DETAIL.md#phase-4--generalization-fdp-toolkit-orchestration):

| Task | Title | Key deliverable |
|------|-------|-----------------|
| CGF1-G0401 | Core Contracts | New `FDP.Toolkit.Orchestration` project; `IDsmHandler`, `IOrchestrationTransport`, `ITransitionGraph`, `IScenarioStorageProvider`, `OrchestrationCommand`; unified `int StatusCode` scheme (`OrchestrationStatusCode`) replacing `OpStatus` enum + `ErrorCode`; `NodeOpStatus`/`SysOpStatus` DDS structs updated |
| CGF1-G0402 | Generic DrillSlave | Single canonical `DrillSlave` + `DdsOrchestrationTransport`; 4 Bagira copies removed |
| CGF1-G0403 | TransitionPlanner + BagiraStateGraph | BFS planner in toolkit; hardcoded adjacency dict replaced by injectable `ITransitionGraph` |
| CGF1-G0404 | Scenario/Story/Prefetch handlers | `ReferencePrefetchHandler`, `ReferenceScenarioLoadHandler`, `ReferenceEditLoadHandler`, `ReferenceStoryLoadHandler` + `IScenarioStorageProvider` |
| CGF1-G0405 | DryRun/Checkpoint/RecordReplay handlers | `ReferenceDryRunHandler`, `ReferenceCheckpointHandler`, `ReferenceLiveLoadHandler`, `ReferenceReplayLoadHandler`; `CheckpointIOWorker` relocated |
| CGF1-G0406 | Cleanup & CI | Dead code deleted; layer boundary verified; full test suite green |

---

## 7. New Projects & File Map

### New Projects

| Project | Description |
|---------|-------------|
| `Bagira.Orchestrator` | DrillMaster, TransitionPlanner, StorageGatewayModule, ReplayMasterModule — hosted in Runner |
| `Bagira.CGF` | CGF subsystem scaffold; DrillSlave, future AI content — hosted in Runner |
| `Bagira.CGF.Tests` | CGF unit and integration tests |
| `FDP.Toolkit.Scenario` | Format-agnostic scenario/story DOM serialization engine; no Bagira refs |
| `FDP.Toolkit.Orchestration` | Generic DrillSlave, IDsmHandler, IOrchestrationTransport, ITransitionGraph, IScenarioStorageProvider, reference handlers — Phase 4; no Bagira refs |

### New Files in Existing Projects

| File | Project | Stage |
|------|---------|-------|
| `Orchestration/OrchestrationMessages.cs` | `Bagira.DDS.DataModel` | 1.1 |
| `DrillMaster.cs` | `Bagira.Orchestrator` | 1.2 |
| `TransitionPlanner.cs` | `Bagira.Orchestrator` | 2.1 |
| `StorageGatewayModule.cs` | `Bagira.Orchestrator` | 3.1 |
| `ReplayMasterModule.cs` | `Bagira.Orchestrator` | 3.4 |
| `Modules/Orchestration/DrillSlave.cs` | `Bagira.SimHost` | 1.4 |
| `Modules/Orchestration/DrillSlave.cs` | `Bagira.IG` | 1.4 |
| `Orchestration/DrillSlave.cs` | `Bagira.IOS` | 1.4 |
| `Modules/Orchestration/DrillSlave.cs` | `Bagira.CGF` | 1.4 |
| `Modules/Orchestration/EcsRecordReplayController.cs` | `Bagira.SimHost` | 3.4 |
| `Modules/Orchestration/RecordingModule.cs` | `Bagira.SimHost` | 3.4 |
| `Modules/Orchestration/ReplayModule.cs` | `Bagira.SimHost` | 3.4 |
| `Modules/Orchestration/Handlers/LiveLoadDsmHandler.cs` | `Bagira.SimHost` | 3.4 |
| `Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs` | `Bagira.SimHost` | 3.4/3.5 |
| `Modules/Orchestration/Handlers/EditLoadDsmHandler.cs` | `Bagira.SimHost` | 3.2 |
| `Modules/Orchestration/Handlers/CheckpointDsmHandler.cs` | `Bagira.SimHost` | 3.3 |
| `Events/DsmStateChangedEvent.cs` | `Bagira.Runner` or `Bagira.Common` | 2.2 |
| `ITimeController.cs` | `FDP.Toolkit.Time` | 2.3 (already exists — verify & extend) |
| `Orchestration/IRecordReplayController.cs` | `Fdp.Kernel` | 3.4 |
| `Orchestration/RecordingConfiguration.cs` | `Fdp.Kernel` | 3.4 |
| `Orchestration/CheckpointIOWorker.cs` | `Fdp.Kernel` | 3.3 |
| `Orchestration/IEntityRefPatchable.cs` | `Fdp.Kernel` | 3.4 (pre-req for Phase 5 Stories) |
| `Scheduling/NetworkLifecycleSystemGroup.cs` | `ModuleHost.Core` | 3.4/3.5 |
| `Messages/TimeMessages.cs` (addition: `SwitchTimeModeEvent` with `BarrierWallTicks`) | `FDP.Toolkit.Time` | 2.4 |
| `ClusterConfiguration.cs` | `Bagira.Orchestrator` | 1.5 |
| `IEntityScenarioTranslator.cs` | `FDP.Toolkit.Scenario` | 3.6 |
| `IGuidResolver.cs` | `FDP.Toolkit.Scenario` | 3.6 |
| `FdpAutoSerializer.cs` | `FDP.Toolkit.Scenario` | 3.6 |
| `ScenarioSerializerBuilder.cs` | `FDP.Toolkit.Scenario` | 3.6 |
| `ScenarioSerializer.cs` | `FDP.Toolkit.Scenario` | 3.6 |
| `ScenarioEntityDto.cs` | `FDP.Toolkit.Scenario` | 3.6 |
| `ScenarioIgnoreAttribute.cs` | `FDP.Toolkit.Scenario` | 3.6 |
| `StoryTag.cs` | `FDP.Toolkit.Replay` only (`Guid StoryId`) — **not** duplicated under `FDP.Toolkit.Scenario` | 3.6 / 3.8 |
| `GlobalContextDsmHandler.cs` | `Bagira.Orchestrator` | 3.7 |
| `Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs` | `Bagira.SimHost` | 3.7 |
| `Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs` | `Bagira.CGF` | 3.7 |
| `Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs` | `Bagira.SimHost` | 3.8 |
| `Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs` | `Bagira.CGF` | 3.8 |
| `Modules/Orchestration/Handlers/DryRunDsmHandler.cs` | `Bagira.SimHost` | 3.9 |
| `Modules/Orchestration/Handlers/DryRunDsmHandler.cs` | `Bagira.CGF` | 3.9 |
| `UI/OrchestratorScenarioPanel.cs` | `Bagira.Orchestrator` | 1.6 |
| `Testing/OrchestratorActionHandlers.cs` | `Bagira.Runner` | 3.10 |
| `TestScripts/e2e_record_and_replay_seek.json` | `Bagira.Runner.Integration.Tests` | 3.10 |
| `TestScripts/e2e_dryrun_state_restore.json` | `Bagira.Runner.Integration.Tests` | 3.10 |
| `TestScripts/e2e_live_from_replay_branch.json` | `Bagira.Runner.Integration.Tests` | 3.10 |
| `TestScripts/e2e_overlapping_checkpoints.json` | `Bagira.Runner.Integration.Tests` | 3.10 |
| `Systems/MovingEntitySystem.cs` | `Bagira.Runner.Integration.Tests` | 3.10 |

---

### New Files in Existing Projects (Phase 4 additions)

| File | Project | Stage |
|------|---------|-------|
| `IDsmHandler.cs` | `FDP.Toolkit.Orchestration` | 4.1 (moved from `Bagira.Common`) |
| `ITickableDsmHandler.cs` | `FDP.Toolkit.Orchestration` | 4.1 (moved) |
| `IOrchestrationTransport.cs` | `FDP.Toolkit.Orchestration` | 4.1 (new) |
| `ITransitionGraph.cs` + `TransitionGraphBuilder.cs` | `FDP.Toolkit.Orchestration` | 4.1 (new) |
| `IScenarioStorageProvider.cs` | `FDP.Toolkit.Orchestration` | 4.1 (new) |
| `OrchestrationCommand.cs` + `OrchestrationStatus.cs` | `FDP.Toolkit.Orchestration` | 4.1 (new) |
| `TkDsmStateChangedEvent.cs` | `FDP.Toolkit.Orchestration` | 4.1 (new) |
| `DrillSlave.cs` | `FDP.Toolkit.Orchestration` | 4.2 (consolidated from 4 copies) |
| `DdsOrchestrationTransport.cs` | `Bagira.Common` | 4.2 (new) |
| `TransitionPlanner.cs` | `FDP.Toolkit.Orchestration` | 4.3 (moved from `Bagira.Orchestrator`) |
| `BagiraStateGraph.cs` | `Bagira.Orchestrator` | 4.3 (new) |
| `LocalDiskStorageProvider.cs` | `Bagira.Common` | 4.4 (new) |
| `Handlers/ReferencePrefetchHandler.cs` | `FDP.Toolkit.Orchestration` | 4.4 |
| `Handlers/ReferenceScenarioLoadHandler.cs` | `FDP.Toolkit.Orchestration` | 4.4 |
| `Handlers/ReferenceEditLoadHandler.cs` | `FDP.Toolkit.Orchestration` | 4.4 |
| `Handlers/ReferenceStoryLoadHandler.cs` | `FDP.Toolkit.Orchestration` | 4.4 |
| `CheckpointIOWorker.cs` | `FDP.Toolkit.Orchestration` | 4.5 (moved from `Bagira.SimHost`) |
| `Handlers/ReferenceDryRunHandler.cs` | `FDP.Toolkit.Orchestration` | 4.5 |
| `Handlers/ReferenceCheckpointHandler.cs` | `FDP.Toolkit.Orchestration` | 4.5 |
| `Handlers/ReferenceLiveLoadHandler.cs` | `FDP.Toolkit.Orchestration` | 4.5 |
| `Handlers/ReferenceReplayLoadHandler.cs` | `FDP.Toolkit.Orchestration` | 4.5 |

---

## 8. Modified Files

| File | Change | Stage |
|------|--------|-------|
| `FDP.Toolkit.Time/Controllers/MasterTimeController.cs` | Implement `ITimeController`; add `SeedState(GlobalTime)` with immediate `TimePulseDescriptor` publish | 2.3 |
| `FDP.Toolkit.Time/Controllers/SlaveTimeController.cs` | Implement `ITimeController`; add `SeedState(GlobalTime)` bypassing `JitterFilter`; expose `TotalWallTicks` | 2.3 |
| `FDP.Toolkit.Time/Controllers/SwitchableTimeController.cs` | Verify `SwitchTo()` calls `GetCurrentState()` then `SeedState()` on new instance | 2.3 |
| `FDP.Toolkit.Time/Controllers/DistributedTimeCoordinator.cs` | Add BarrierWallTicks computation using `GlobalTime.TotalWallTicks + lookaheadTicks`; publish `SwitchTimeModeEvent` via `BlitEventTranslator` | 2.4 |
| `FDP.Toolkit.Time/Controllers/SlaveTimeModeListener.cs` | Subscribe to `SwitchTimeModeEvent`; check `_kernel.CurrentTime.TotalWallTicks >= BarrierWallTicks`; call `SwitchTo()` at barrier | 2.4 |
| `Fdp.Kernel/GlobalTime.cs` | Add `long TotalWallTicks` field | 2.3/3.4 |
| `Fdp.Kernel/FlightRecorder/RecorderSystem.cs` | Add `EntityFilter` predicate; add `long WallClockTicks` to frame header; add `CaptureEventFrame(long, …)` | 3.4 |
| `Fdp.Kernel/FlightRecorder/AsyncRecorder.cs` | Accept `EntityFilter`; expose `MaxNetworkId` at finalization | 3.4 |
| `Fdp.Kernel/FlightRecorder/PlaybackController.cs` | Add `WallClockTicks` to `FrameMetadata`; add `SeekToWallClockTicks()` with binary search | 3.4 |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostCreationSystem.cs` | Add `bool BypassLifecycle` property | 3.5 |
| `Bagira.Runner/Services/OrchestratorSubsystem.cs` | Wire `OrchestratorScenarioPanel` into `DrawUI()` alongside health and 2PC panels | 1.6 |
| `Bagira.SimHost/SimHostApp.cs` | Remove `DdsIdAllocatorServer`; register `DrillSlave` | 1.3/1.4 |
| `Bagira.IG/IgApplication.cs` | Register `DrillSlave` | 1.4 |
| `Bagira.Runner/Services/SimHostSubsystem.cs` | Integrate DSM `Standby` entry; launch Orchestrator subprocess if enabled | 1.2 |

---

## 9. Phase 5 — Operational UI, Real Network Dispatch & CQRS Architecture

Phase 5 corrects critical runtime gaps and decouples the cluster UI from local service
instances. The full design is in a dedicated companion document:

> **→ [CGF-1-ADDENDUM-3.md](./CGF-1-ADDENDUM-3.md)**

| Task | Title | Key deliverable |
|------|-------|-----------------|
| CGF1-S0501 | ImGui Window & 2PC History | Beige title bar; `ImGui.Begin` wrapper; `DistributedTransaction.{PayloadJson,NodeResponses,SourceDsmState}`; 5-col scrollable 2PC table; JSON hover tooltip; context-menu clipboard |
| CGF1-S0502 | Real Network Dispatch + Fan-out | `DdsWriter<SysOpRequest>` in `OrchestratorSubsystem`; all panel `HandleSysOpRequest` calls replaced; `DrillMaster` fan-out loop for `PrepareXxx`/`CommitState` |
| CGF1-S0503 | Time Control Section | `SysOpType.{StepTime,SetTimeScale}`; `DrillMaster.TimeControlRequested` event; Pause/Resume/Step/Speed UI; replay seek debounce |
| CGF1-S0504 | Asset Combo Selection | `RefreshLocalAssets()` scanning `C:\FDP_Temp`; scenario/drill/story combos; auto-generated `StoryId` |
| CGF1-S0505 | Archive Export/Import Pipeline | `SysOpType.CancelOperation`; `PrefetchArchiveAsync`; `ReferenceArchiveHandler`; DrillMaster archive branches; Archive Management UI with progress bar + cancel |
| CGF1-S0506 | CQRS: AssetInventoryTopic + ClusterUiCache | `AssetInventoryTopic` DDS struct; DrillMaster publishes every 5 s; `ClusterUiCache`; `OrchestratorScenarioPanel` → `ClusterScenarioPanel`; `OrchestratorSubsystem` uses cache |
| CGF1-S0507 | IOS Remote Cluster Control Panel | Time ingress handlers on IOS; `IIosLogic` time API; `IosSubsystem` renders `ClusterScenarioPanel` over pure DDS |

---

## 10. Deferred Features (Phase 6+)

The following features from `mgmt-DESIGN.md` are explicitly out of scope for Phases 1–3.
None of them are required before Phase 4 (Urban Combat AI):

| Feature | Reason for deferral |
|---------|---------------------|
| **Battlespaces** (§11 of mgmt-DESIGN) | Urban Combat demo uses a static city intersection; staged terrain loading is unnecessary for Phase 5 |
| **"Always Recording" event frames during Edit** (§8.12) | Basic simulation-time recording is sufficient for CI validation; paused-time UTC event capture introduces edge cases distracting from Phase 3 correctness |
| **Full Story Playback Controller** | `StoryPlaybackController` (timed/conditional story events that replay as narrative) is deferred until multi-tenant training sessions are needed; Stage 3.8 covers injection/deletion only |
