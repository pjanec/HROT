# CGF-1 Design Document
## Distributed Drill Management & CGF Subsystem — Phases 1–3

> **Source:** Derived from [design-talk.md](./design-talk.md) and
> [mgmt-DESIGN.md](./mgmt-DESIGN.md).  
> **Scope:** Phases 1–3 only (Skeleton, State & Time, Persistence).  
> Phase 4 (Urban Combat AI) is out of scope and will be addressed in a separate workstream
> once the management infrastructure established here is stable and regression-tested.
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
6. [New Projects & File Map](#6-new-projects--file-map)
7. [Modified Files](#7-modified-files)
8. [Deferred Features (Phase 5+)](#8-deferred-features-phase-5)

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
│    CheckpointIOWorker, RecordingConfiguration, EsmStateChangedEvent,     │
│    NetworkLifecycleSystemGroup  (generic scheduling primitive)           │
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
- DSMState, SysOpType, NodeOpType, OpStatus, and all DDS orchestration structs live in
  `Bagira.DDS.DataModel` — not in any FDP library.
- The generic `IRecordReplayController` in `Fdp.Kernel` uses `GlobalTime` (which is
  already in `Fdp.Kernel`) but has no knowledge of `DSMState` or `DrillId`.

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
    ReplaySeek           = 13, UploadChunk          = 14,
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
as a separate process via `Bagira.Orchestrator.Standalone`.

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
RunningEdit      → LoadingDryRun, LoadingLive, UnloadingEdit
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

```csharp
// Location: FDP/Toolkits/FDP.Toolkit.Time/ITimeController.cs  (new)
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
    public long     BarrierWallTicks;  // DateTime.UtcNow.Ticks at which each node must swap
    public float    FixedDelta;        // Only meaningful when TargetMode == Deterministic
}
```

> **Why wall-clock, not ECS frame counter:**  
> In non-deterministic (real-time) mode, nodes run their ECS loops asynchronously at
> different rates depending on CPU load. There is no globally shared frame counter;
> each node's `globalFrameCounter` advances independently. Using an ECS frame number
> as a barrier would cause nodes to swap at different simulation instants, destroying
> determinism. Wall-clock UTC time (`DateTime.UtcNow.Ticks`) is synchronized across
> the cluster via NTP and is the only truly global reference available during
> asynchronous operation.

#### Future Barrier Protocol

```
Master (DistributedTimeCoordinator):
  1. SetTimeScale(0.0) — pause before negotiating
  2. BarrierWallTicks = DateTime.UtcNow.Ticks + LookaheadTicks
     (LookaheadTicks configurable; default ≈ 200 ms — enough for DDS delivery
      across the LAN even under moderate load)
  3. Publish SwitchTimeModeEvent { TargetMode, BarrierWallTicks, FixedDelta }
     via BlitEventTranslator (zero-allocation raw memcpy)
  4. Simulate normally; when DateTime.UtcNow.Ticks >= BarrierWallTicks →
     _switchableTime.SwitchTo(new SteppedMasterController(...))
  5. Restore saved TimeScale

Slave (SlaveTimeModeListener — FDP.Toolkit.Time):
  1. Receives SwitchTimeModeEvent from DDS
  2. Simulates normally (each tick checks wall clock)
  3. When DateTime.UtcNow.Ticks >= BarrierWallTicks →
     _switchableTime.SwitchTo(new SteppedSlaveController(...) or SlaveTimeController)
```

All nodes check the same absolute UTC timestamp, so the swap converges to within one
ECS tick of each other across the cluster regardless of individual frame rates.

**Milestone validation:** Wall-clock barrier test — master publishes event with
`BarrierWallTicks = now + 200ms`; slave asserts `SwitchTo()` is called only after
`DateTime.UtcNow.Ticks >= BarrierWallTicks` and not before. See
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

## 6. New Projects & File Map

### New Projects

| Project | Description |
|---------|-------------|
| `Bagira.Orchestrator` | DrillMaster, TransitionPlanner, StorageGatewayModule, ReplayMasterModule |
| `Bagira.Orchestrator.Standalone` | Process entry point for standalone Orchestrator |
| `Bagira.CGF` | CGF subsystem scaffold; DrillSlave, future AI content |
| `Bagira.CGF.Standalone` | Process entry point for standalone CGF |
| `Bagira.CGF.Tests` | CGF unit and integration tests |

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
| `ITimeController.cs` | `FDP.Toolkit.Time` | 2.3 |
| `Orchestration/IRecordReplayController.cs` | `Fdp.Kernel` | 3.4 |
| `Orchestration/RecordingConfiguration.cs` | `Fdp.Kernel` | 3.4 |
| `Orchestration/CheckpointIOWorker.cs` | `Fdp.Kernel` | 3.3 |
| `Orchestration/IEntityRefPatchable.cs` | `Fdp.Kernel` | 3.4 (pre-req for Phase 5 Stories) |
| `Scheduling/NetworkLifecycleSystemGroup.cs` | `ModuleHost.Core` | 3.4/3.5 |
| `Messages/TimeMessages.cs` (addition: `SwitchTimeModeEvent` with `BarrierWallTicks`) | `FDP.Toolkit.Time` | 2.4 |

---

## 7. Modified Files

| File | Change | Stage |
|------|--------|-------|
| `FDP.Toolkit.Time/Controllers/MasterTimeController.cs` | Implement `ITimeController`; add `SeedState(GlobalTime)` with immediate `TimePulseDescriptor` publish | 2.3 |
| `FDP.Toolkit.Time/Controllers/SlaveTimeController.cs` | Implement `ITimeController`; add `SeedState(GlobalTime)` bypassing `JitterFilter`; expose `TotalWallTicks` | 2.3 |
| `FDP.Toolkit.Time/Controllers/SwitchableTimeController.cs` | Verify `SwitchTo()` calls `GetCurrentState()` then `SeedState()` on new instance | 2.3 |
| `FDP.Toolkit.Time/Controllers/DistributedTimeCoordinator.cs` | Add BarrierFrame computation; publish `SwitchTimeModeEvent` via `BlitEventTranslator` | 2.4 |
| `FDP.Toolkit.Time/Controllers/SlaveTimeModeListener.cs` | Subscribe to `SwitchTimeModeEvent`; call `SwitchTo()` at barrier frame | 2.4 |
| `Fdp.Kernel/GlobalTime.cs` | Add `long TotalWallTicks` field | 2.3/3.4 |
| `Fdp.Kernel/FlightRecorder/RecorderSystem.cs` | Add `EntityFilter` predicate; add `long WallClockTicks` to frame header; add `CaptureEventFrame(long, …)` | 3.4 |
| `Fdp.Kernel/FlightRecorder/AsyncRecorder.cs` | Accept `EntityFilter`; expose `MaxNetworkId` at finalization | 3.4 |
| `Fdp.Kernel/FlightRecorder/PlaybackController.cs` | Add `WallClockTicks` to `FrameMetadata`; add `SeekToWallClockTicks()` with binary search | 3.4 |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostCreationSystem.cs` | Add `bool BypassLifecycle` property | 3.5 |
| `Bagira.SimHost/SimHostApp.cs` | Remove `DdsIdAllocatorServer`; register `DrillSlave` | 1.3/1.4 |
| `Bagira.IG/IgApplication.cs` | Register `DrillSlave` | 1.4 |
| `Bagira.Runner/Services/SimHostSubsystem.cs` | Integrate DSM `Standby` entry; launch Orchestrator subprocess if enabled | 1.2 |

---

## 8. Deferred Features (Phase 5+)

The following features from `mgmt-DESIGN.md` are explicitly out of scope for Phases 1–3.
None of them are required before Phase 4 (Urban Combat AI):

| Feature | Reason for deferral |
|---------|---------------------|
| **Stories** (§10 of mgmt-DESIGN) | Requires `ComponentPatchMap`, `IEntityRefPatchable`, `StoryPlaybackController`, zero-alloc entity-ref patching — massive complexity not needed until multi-tenant training sessions are required |
| **Battlespaces** (§11) | Urban Combat demo uses a static city intersection; staged terrain loading is unnecessary for Phase 4 |
| **"Always Recording" event frames during Edit** (§8.12) | Basic simulation-time recording is sufficient for CI validation; paused-time UTC event capture introduces edge cases distracting from Phase 3 correctness |
| **Full Node Health Monitoring + Criticality** (§7) | Development uses simple heartbeat timeout; full fault-tolerance classifications are a deployment concern, not a CI concern |
| **Archive Export/Import** (§13) | Pre/post replay file management can be manual during development; fully automated archive operations belong to deployment phase |
