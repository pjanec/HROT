# Distributed Exercise Management System — Architecture Design

> **Document scope:** Architectural design for implementing the Exercise State Machine (ESM),
> distributed recording/replay, checkpoints, dry runs, stories, battlespaces, node health
> monitoring, and archive management across the Bagira/FDP platform.
>
> Based on the [design-talk.md](./design-talk.md) conversation; cross-referenced against the
> existing codebase as of March 2026.

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [DDS Message Schema — bdc-sst-orchestration](#2-dds-message-schema)
3. [Exercise State Machine (ESM)](#3-exercise-state-machine)
4. [SysOp / Two-Phase Commit (2PC) Orchestration Pattern](#4-sysop--two-phase-commit-orchestration-pattern)
5. [SystemMasterModule](#5-systemmastermodule)
   - [5.5 Transition Planner (Macro-Transitions)](#55-transition-planner-macro-transitions)
   - [5.5.2 TransitionPlanner — BFS Implementation](#552-transitionplanner--bfs-implementation)
   - [5.6 Time Control Architecture](#56-time-control-architecture)
   - [5.6.5 Deterministic Mode Switching — DistributedTimeCoordinator and Future Barrier](#565-deterministic-mode-switching--distributedtimecoordinator-and-future-barrier)
   - [5.7 Centralized Network Identity Authority (DdsIdAllocatorServer)](#57-centralized-network-identity-authority)
6. [SystemSlaveModule](#6-systemslavemodule)
   - [6.6 Time Slave Integration (SlaveTimeController + Kernel Adapter)](#66-time-slave-integration)
7. [Node Health Monitoring (Heartbeat & BIT)](#7-node-health-monitoring)
8. [Replay Subsystem](#8-replay-subsystem)
   - [8.11 Live-from-Replay Temporal Interlock](#811-live-from-replay-temporal-interlock)
   - [8.12 "Always Recording" — Event Capture During Paused Simulation Time](#812-always-recording--event-capture-during-paused-simulation-time)
9. [Checkpoints & Dry Runs](#9-checkpoints--dry-runs)
10. [Stories — Multi-Tenant Micro-Scenarios](#10-stories--multi-tenant-micro-scenarios)
11. [Battlespaces](#11-battlespaces)
12. [Scenario Editing & Management](#12-scenario-editing--management)
13. [Archive Export / Import — Storage Gateway Pattern](#13-archive-export--import--storage-gateway-pattern)
14. [Deterministic Batch Runs](#14-deterministic-batch-runs)
15. [Key 12-Step Exercise Sequence Flow](#15-key-12-step-exercise-sequence-flow)
16. [Required Code Changes Summary](#16-required-code-changes-summary)

---

## 1. System Overview

### 1.1 Node Roles

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         BAGIRA DISTRIBUTED PLATFORM                         │
│                                                                             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │     IOS      │  │  SimHost     │  │  IG          │  │ Orchestrator │ │
│  │  (Control)   │  │ (Simulation) │  │ (Visualise)  │  │  (Master)    │ │
│  │              │  │              │  │              │  │              │ │
│  │  SysOpReq ──►│  │ ◄─NodeOpCmd  │  │ ◄─NodeOpCmd  │  │ NodeOpCmd──► │ │
│  │ ◄─SysOpUpd   │  │  NodeOpSts──►│  │  NodeOpSts──►│  │◄─NodeOpSts  │ │
│  │  Heartbeat──►│  │  Heartbeat──►│  │  Heartbeat──►│  │  (all nodes) │ │
│  └──────────────┘  └──────────────┘  └──────────────┘  └──────┬───────┘ │
│         │                  ▲                  ▲                │          │
│         └──────────────────┴──────────────────┴────────────────┘          │
│                                                                             │
│  Orchestrator (Bagira.Orchestrator, subsystem of Bagira.Runner):            │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │ SystemMasterModule │ ESM State Machine │ Transaction Mgr │ Watchdog  │  │
│  │ OrchestratorContextTopic (TransientLocal) — late-joiner context      │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  ─────────────────────────── CycloneDDS Bus ──────────────────────────── │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Control Planes

| Plane | Direction | Topic |
|-------|-----------|-------|
| Control Plane — Request | IOS → Master | `SysOpRequest` |
| Control Plane — Response | Master → IOS | `SysOpStatus` |
| Command Plane — Command | Master → All Nodes | `NodeOpCommand` |
| Command Plane — Status | All Nodes → Master | `NodeOpStatus` |
| Health Plane | Each Node → All | `NodeHeartbeat` |
| State Plane | Master → All (persistent) | `SystemStateTopic` |
| Context Plane | Orchestrator → All (persistent) | `OrchestratorContextTopic` |
| Time Plane | Time Master → All | `TimePulseDescriptor` (existing) |
| Time Mode Plane | `DistributedTimeCoordinator` → All | `SwitchTimeModeEvent` (new — internal to time toolkit, see §5.6.5) |

---

## 2. DDS Message Schema

**IDL file:** `bdc-sst-orchestration`  
**C# namespace:** `Bagira.BDC.SSTD.Orchestration`  
**Project:** `Bagira.DDS.DataModel`

### 2.1 Enumerations

```csharp
// ─── Exercise State Machine States ───────────────────────────────────────────
public enum ESMState : int
{
    Standby           = 0,

    // Editing flow
    LoadingEdit       = 10,
    RunningEdit       = 11,
    UnloadingEdit     = 12,

    // Dry-run sub-loop (inside editing)
    LoadingDryRun     = 20,
    RunningDryRun     = 21,
    UnloadingDryRun   = 22,

    // Live simulation flow
    LoadingLive       = 30,
    RunningLive       = 31,
    UnloadingLive     = 32,

    // Replay flow
    LoadingReplay     = 40,
    RunningReplay     = 41,
    UnloadingReplay   = 42,

    // Error / degraded
    Degraded          = 99,
}

// ─── SysOp types (IOS → Master) ──────────────────────────────────────────────
public enum SysOpType : int
{
    TransitionState   = 1,   // Load/Unload/switch ESM states
    SaveScenario      = 2,   // Serialize scenario JSON
    LoadBattlespace   = 3,   // Load high-res terrain area
    TakeCheckpoint    = 4,   // Fast RAM snapshot
    CollectCheckpoint = 5,   // Gather checkpoint files to archive
    ExportArchive     = 6,   // Export drill recordings to cold storage
    ImportArchive     = 7,   // Import recordings from cold storage
    ManageStory       = 8,   // Start/Stop/Eval/Forget micro-scenario
    ReplaySeek        = 9,   // Jump replay to a specific wall-clock time
    PauseTime         = 10,
    ResumeTime        = 11,
}

// ─── Node operation types (Master → Nodes) ───────────────────────────────────
public enum NodeOpType : int
{
    PrepareState     = 1,
    CommitState      = 2,
    AbortTransaction = 3,
    TakeSnapshot     = 4,   // Non-blocking: node calls SyncFrom, passes buf to async thread
    RestoreSnapshot  = 5,
    PrepareBattlespace   = 7,
    CommitBattlespace    = 8,
    PrepareLive          = 9,
    FinalizeLive         = 10,
    PrepareReplay        = 11,
    FinalizeReplay       = 12,
    ReplaySeek           = 13,
    UploadChunk          = 14,  // Storage Gateway — node embeds UNC manifest in NodeOpStatus ACK
    SerializeLocal       = 15,  // Serialize scenario/checkpoint data to local SSD (SaveScenario / Archive)
    CleanupTempFiles     = 16,  // Delete local temp files after Gateway confirms NAS transfer
    StartStory           = 20,
    StopStory            = 21,
    ReplayStory          = 22,
    ForgetStory          = 23,
    LoadStoryAssets      = 24,
}

public enum OpStatus : int
{
    Pending    = 0,
    InProgress = 1,
    Success    = 2,
    Failure    = 3,
    Rejected   = 4,
}
```

### 2.2 DDS Topics

```csharp
// ─── Persistent system state (late-joiner safe) ──────────────────────────────
[DdsTopic("SystemState")]
[DdsIdlFile("bdc-sst-orchestration")]
[DdsQos(Reliability  = DdsReliability.Reliable,
        Durability   = DdsDurability.TransientLocal,
        HistoryKind  = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct SystemStateTopic
{
    public ESMState CurrentState;
    public Guid     DrillId;          // Unique key per exercise run
    public long     StateStartWallTicks;  // wall-clock start of current state
    public int      TransactionEpoch; // Increments on each successful transition
}

// ─── IOS → Master ────────────────────────────────────────────────────────────
[DdsTopic("SysOpRequest")]
[DdsIdlFile("bdc-sst-orchestration")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
public partial struct SysOpRequest
{
    public Guid       RequestId;
    public SysOpType  OperationType;
    public string     PayloadJson;   // Operation-specific params (nullable)
}

// ─── Master → IOS ────────────────────────────────────────────────────────────
[DdsTopic("SysOpStatus")]
[DdsIdlFile("bdc-sst-orchestration")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
public partial struct SysOpStatus
{
    public Guid      RequestId;
    public OpStatus  Status;
    public int       ErrorCode;
    public string    ResultJson;
}

// ─── Master → All Nodes ──────────────────────────────────────────────────────
[DdsTopic("NodeOpCommand")]
[DdsIdlFile("bdc-sst-orchestration")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
public partial struct NodeOpCommand
{
    public Guid        TransactionId;
    public NodeOpType  Operation;
    public string      PayloadJson;
}

// ─── All Nodes → Master ──────────────────────────────────────────────────────
[DdsTopic("NodeOpStatus")]
[DdsIdlFile("bdc-sst-orchestration")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
public partial struct NodeOpStatus
{
    public Guid      TransactionId;
    public int       NodeId;
    public OpStatus  Status;
    public bool      IsParticipating;  // false = node opted out, Master skips it
    public int       ErrorCode;
    public string    ResultJson;
}

// ─── Health / BIT (all nodes, autonomous 1 Hz) ───────────────────────────────
[DdsTopic("NodeHeartbeat")]
[DdsIdlFile("bdc-sst-orchestration")]
[DdsQos(Reliability  = DdsReliability.BestEffort,
        Durability   = DdsDurability.TransientLocal,
        HistoryKind  = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct NodeHeartbeat
{
    [DdsKey]
    public int     NodeId;
    public string  SubsystemName;
    public ESMState LocalEsmState;       // What the node thinks the ESM state is
    public long    WallTicksUtc;
    public float   CpuUsagePercent;
    public long    RamUsedBytes;
    public bool    SimTickAdvancing;     // false if ECS main loop is stalled
    public string  SubsystemsJson;       // JSON dict: { "Recorder": "Healthy", ... }
}

// ─── Late-joiner / exercise context (Orchestrator → All, TransientLocal) ──────
// Published/updated by the Orchestrator whenever the exercise context changes.
// Late-joining nodes read this once to catch up on what exercise is running.
[DdsTopic("OrchestratorContext")]
[DdsIdlFile("bdc-sst-orchestration")]
[DdsQos(Reliability  = DdsReliability.Reliable,
        Durability   = DdsDurability.TransientLocal,
        HistoryKind  = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct OrchestratorContextTopic
{
    public ESMState CurrentState;
    public Guid     DrillId;
    public int      TransactionEpoch;
    public string   ScenarioId;           // Which scenario is loaded (nullable)
    public string   ArchiveBasePath;      // Where recordings are stored
    public string   RequiredNodeIdsJson;  // JSON int[] — which NodeIds are expected
    public long     StateStartWallTicks;
}

// ─── Replay-specific time pulse (Orchestrator → All, during RunningReplay) ───
// NOTE: During RunningReplay the ReplayMasterModule seeds MasterTimeController
// with the recording epoch (StartWallTicks = recording.StartWallTicks) and sets
// an appropriate TimeScale, then calls MasterTimeController.Update() normally.
// MasterTimeController publishes TimePulseDescriptor at 1 Hz (or on SetTimeScale).
// Slaves run SlaveTimeController PLL as usual — SimTimeSnapshot carries seconds
// elapsed since recording epoch.  No new DDS topic is needed; the consumer may
// check SystemStateTopic.CurrentState == RunningReplay to interpret TotalTime as
// recording-relative seconds rather than live simulation time.
```

---

## 3. Exercise State Machine

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                         EXERCISE STATE MACHINE (ESM)                         │
└──────────────────────────────────────────────────────────────────────────────┘

                      ┌──────────┐
                      │ Standby  │◄─────────────────────────────────┐
                      └────┬─────┘                                   │
            ┌──────────────┼──────────────┐                          │
            │              │              │                          │
      Load  │        Load  │       Load   │                          │
      Edit  ▼        Live  ▼       Rplay  ▼                          │
    ┌────────────┐  ┌─────────────┐  ┌────────────────┐             │
    │LoadingEdit │  │ LoadingLive │  │ LoadingReplay  │             │
    └─────┬──────┘  └──────┬──────┘  └───────┬────────┘             │
          │                │                  │                      │
          ▼                ▼                  ▼                      │
    ┌────────────┐  ┌─────────────┐  ┌────────────────┐             │
    │RunningEdit │  │ RunningLive │  │ RunningReplay  │             │
    └──┬──────┬──┘  └──┬──────┬───┘  └──┬──────┬──────┘             │
       │      │        │  ▲   │         │  ▲   │                    │
  Start│      │Unload  │  │   │Unload   │  │   │Unload             │
  DryRun      │Edit    │Pause │Live     │ Pause│Replay             │
       │      │        │  │   │         │  │   │                    │
       ▼      │        ▼  │   ▼         ▼  │   ▼                    │
  ┌──────────┐│   ┌──────────────┐  ┌──────────────────┐           │
  │LoadingDR ││   │UnloadingLive │  │UnloadingReplay   │           │
  └────┬─────┘│   └──────┬───────┘  └──────────┬───────┘           │
       │      │          │                      │                    │
       ▼      │          └──────────────────────┴────────────────────┘
  ┌───────────┐                         (→ Standby)
  │RunningDR  │  ("DryRun")
  └────┬──────┘
       │ Stop DryRun
       ▼
  ┌────────────────┐
  │UnloadingDryRun │
  └────────┬───────┘
           │ RAM snapshot restored
           └──────────────────────► RunningEdit
```

### 3.1 Valid State Transitions

```
Standby            → LoadingEdit, LoadingLive, LoadingReplay
LoadingEdit        → RunningEdit, Standby (failure)
RunningEdit        → LoadingDryRun, LoadingLive, UnloadingEdit
LoadingDryRun      → RunningDryRun, RunningEdit (failure)
RunningDryRun      → UnloadingDryRun
UnloadingDryRun    → RunningEdit
UnloadingEdit      → Standby
LoadingLive        → RunningLive, Standby (failure)
RunningLive        → UnloadingLive
UnloadingLive      → Standby
LoadingReplay      → RunningReplay, Standby (failure)
RunningReplay      → UnloadingReplay, LoadingLive (Live-from-Replay)
UnloadingReplay    → Standby
Any                → Degraded (health failure)
```

> **Asset caching across Standby:** When transitioning through Standby back to a new
> `LoadingX` state, nodes MUST NOT force a full asset reload. Assets loaded in RAM are
> retained; only assets that differ between old and new scenario are unloaded/reloaded.
> This means `RunningEdit → LoadingLive` (via direct or via Standby) avoids re-scanning
> files that are already resident.

> **Mermaid version** (copy into a Mermaid renderer):

```mermaid
stateDiagram-v2
    [*] --> Standby

    Standby --> LoadingEdit    : Edit Scenario
    Standby --> LoadingLive    : Load Scenario / Snapshot
    Standby --> LoadingReplay  : Load Recording

    LoadingEdit --> RunningEdit    : Assets Loaded
    LoadingEdit --> Standby        : Failure

    state RunningEdit {
        [*] --> ScenarioPaused
        note right of ScenarioPaused : Time frozen. Entities placed/modified.
    }

    RunningEdit --> LoadingDryRun  : Start Dry Run
    RunningEdit --> LoadingLive    : Promote to Live
    RunningEdit --> UnloadingEdit  : Save & Exit Edit

    LoadingDryRun --> RunningDryRun   : RAM Snapshot Taken
    LoadingDryRun --> RunningEdit     : Failure

    RunningDryRun --> UnloadingDryRun : Stop Dry Run
    UnloadingDryRun --> RunningEdit   : RAM Snapshot Restored

    UnloadingEdit --> Standby

    LoadingLive --> RunningLive   : Init Complete
    LoadingLive --> Standby       : Failure

    state RunningLive {
        [*] --> LiveRunning
        LiveRunning --> LivePaused   : Pause Time
        LivePaused  --> LiveRunning  : Resume Time
    }

    RunningLive  --> UnloadingLive   : End Live Exercise
    UnloadingLive --> Standby

    LoadingReplay --> RunningReplay  : Init Complete
    LoadingReplay --> Standby        : Failure

    state RunningReplay {
        [*] --> ReplayRunning
        ReplayRunning --> ReplayPaused : Pause
        ReplayPaused  --> ReplayRunning : Resume
    }

    RunningReplay --> UnloadingReplay : End Replay
    RunningReplay --> LoadingLive     : Live-from-Replay
    UnloadingReplay --> Standby
```

---

## 4. SysOp / Two-Phase Commit Orchestration Pattern

The entire distributed coordination uses a **Two-Phase Commit (2PC)** pattern:

```
IOS              Master                     All Slave Nodes
 │                 │                               │
 │ SysOpRequest    │                               │
 ├────────────────►│                               │
 │                 │── validates state ─┐          │
 │                 │◄──────────────────┘          │
 │                 │                               │
 │                 │  NodeOpCommand(PREPARE)       │
 │                 ├──────────────────────────────►│
 │                 │                               │── bg Task ──►
 │                 │  NodeOpStatus(InProgress)     │
 │                 │◄──────────────────────────────┤
 │ SysOpStatus     │                               │── heavy work...
 │ (InProgress) ◄──┤                               │
 │                 │  NodeOpStatus(Success)        │◄── bg Task done
 │                 │◄──────────────────────────────┤
 │                 │  (all nodes acked)            │
 │                 │                               │
 │                 │  NodeOpCommand(COMMIT)        │
 │                 ├──────────────────────────────►│
 │                 │── updates SystemStateTopic    │
 │ SysOpStatus     │                               │
 │ (Success) ◄─────┤                               │
 │                 │                               │
```

### 4.1 Failure Path (Abort)

```
 │                 │   NodeOpStatus(Failure)       │
 │                 │◄──────────────────────────────┤
 │                 │                               │
 │                 │  NodeOpCommand(ABORT)         │
 │                 ├──────────────────────────────►│── drop StagedAssetPayload
 │ SysOpStatus     │                               │
 │ (Failure) ◄─────┤                               │
```

### 4.2 Transaction Epoch (Split-Brain Recovery)

`SystemStateTopic.TransactionEpoch` is incremented on every successful commit.  
A node that "missed" a commit detects this on the next heartbeat cycle by comparing
its local epoch against the published epoch. It self-heals if it completed the
Prepare phase; otherwise it transitions to `Degraded`.

---

## 5. SystemMasterModule

**Location:** `Bagira.Orchestrator/SystemMasterModule.cs` (new — lives in a dedicated
`Bagira.Orchestrator` project, registered as a subsystem of `Bagira.Runner`).
`Bagira.Runner` is just a shell; `Bagira.Orchestrator` runs as a separate process.

> **Late-joiner support:** On every ESM state transition the Orchestrator publishes an
> updated `OrchestratorContextTopic` (TransientLocal, HistoryDepth=1). Any node that
> joins after the transition reads the latest sample immediately and executes its internal
> join procedure (load assets, sync state, etc.).

### 5.1 Responsibilities
- Owns the **ESM** — sole writer of `SystemStateTopic`
- Manages the **Node Roster** via `NodeHeartbeat` consumption
- Executes **2PC transactions** without blocking the main ECS loop
- Generates new `DrillId` GUID when entering any non-Standby session
- Provides **`SysOpRequest`** validation and rejection logic

### 5.2 Internal Data Structures

```csharp
// ─── Polymorphic step in a planned trajectory (see §5.5) ─────────────────────
//
// A trajectory is a Queue<ISysOpStep>. Each step is either:
//   TransitionStep  — a standard ESM state change executed via 2PC NodeOpCommand
//   OperationStep   — a distributed operation that runs *within* a resident ESM
//                     state (e.g. ReplaySeek after arriving in RunningReplay)
//
// This distinction is critical: ReplaySeek is NOT a state — it is an operation
// that is only valid while the system is already in RunningReplay. A queue typed
// strictly to ESMState cannot encode it, so the planner always returns
// Queue<ISysOpStep>.
abstract class ISysOpStep
{
    public abstract string Label { get; }   // Shown in SysOpStatus "Step X of Y: {Label}"
}

class TransitionStep : ISysOpStep
{
    public ESMState   TargetState;
    public NodeOpType PrepareOp;
    public NodeOpType CommitOp;
    public override string Label => TargetState.ToString();
}

class OperationStep : ISysOpStep
{
    public SysOpType  OperationType;    // e.g. ReplaySeek
    public string     PayloadJson;      // forwarded verbatim to NodeOpCommand
    public override string Label => OperationType.ToString();
}

// ─── Active distributed transaction ──────────────────────────────────────────
class DistributedTransaction
{
    public Guid       TransactionId;
    public Guid       OriginRequestId;     // back-ref to SysOpRequest

    // ── Macro-transition support (see §5.5) ──────────────────────────────────
    // PlannedSteps is populated by the TransitionPlanner for every request,
    // even single-step ones (queue length 1 = simple transition).
    // TargetEsmState is the final ESM state goal; operations appended after it
    // (e.g. ReplaySeek) are OperationSteps and do not change the ESM state.
    public ESMState            TargetEsmState;  // final ESM state goal
    public Queue<ISysOpStep>   PlannedSteps;    // ordered polymorphic steps
    public int                 TotalSteps;      // snapshot of initial queue length — for "Step X of Y"
    public int                 CompletedSteps;  // incremented after each step commits

    public HashSet<int> PendingNodes;      // cloned from ActiveNodes at T=0
    public float      ElapsedSeconds;
    public float      TimeoutSeconds;      // configurable, default 30s
    public bool       AllowPartialSuccess; // e.g. non-critical loggers
}

// ─── Node health profile ──────────────────────────────────────────────────────
class NodeHealthProfile
{
    public int     NodeId;
    public string  SubsystemName;
    public ESMState LastReportedState;
    public float   SecondsSinceLastHeartbeat;
    public bool    IsCritical;   // if true and goes offline → fault the ESM
}
```

### 5.3 Tick() Logic (runs every ECS frame at BeforeSync phase)

```
┌─ TICK ──────────────────────────────────────────────────────────────────┐
│                                                                          │
│  1. Consume NodeHeartbeat DDS queue                                      │
│     ├─ Update NodeHealthProfile.SecondsSinceLastHeartbeat = 0            │
│     └─ Detect missed heartbeats (> 5s) → prune or fault                  │
│                                                                          │
│  2. Consume SysOpRequest DDS queue (from IOS)                            │
│     ├─ Hand request to TransitionPlanner → generates Queue<ISysOpStep>   │
│     └─ If valid path found → spawn DistributedTransaction, begin Phase 1 │
│        (see §5.5 for Transition Planner details)                         │
│                                                                          │
│  3. Consume NodeOpStatus DDS queue (from slave nodes)                    │
│     ├─ Match by TransactionId                                            │
│     ├─ InProgress: forward as SysOpStatus(InProgress, "Step X of Y")    │
│     │              to IOS so the UI can show a progress bar              │
│     ├─ Success / IsParticipating=false: remove from PendingNodes         │
│     └─ Failure: abort transaction, clear PlannedSteps queue,             │
│                 publish SystemStateTopic(SafeFallbackState),             │
│                 send SysOpStatus(Failure) to IOS                         │
│                                                                          │
│  4. For each active transaction                                           │
│     ├─ Increment ElapsedSeconds                                          │
│     ├─ If timeout → abort (treat as Failure)                             │
│     └─ If PendingNodes.Count == 0 → COMMIT current step:                 │
│         ├─ If current step is TransitionStep:                            │
│         │   ├─ Publish NodeOpCommand(CommitOp)                           │
│         │   └─ Write SystemStateTopic (committed ESM state)              │
│         ├─ If current step is OperationStep:                             │
│         │   └─ (SystemStateTopic unchanged — ESM state stays resident)   │
│         ├─ transaction.PlannedSteps.Dequeue(); CompletedSteps++          │
│         ├─ If PlannedSteps is EMPTY → publish SysOpStatus(Success)       │
│         │   to IOS and close transaction                                 │
│         └─ Else → pop next step, reset PendingNodes, dispatch next       │
│                  NodeOpCommand for the step — chain continues            │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

### 5.4 DrillId Generation

```
Whenever transitioning OUT of Standby (to LoadingEdit, LoadingLive, LoadingReplay):
  _currentDrillId = Guid.NewGuid()

The DrillId is embedded in:
  - SystemStateTopic
  - NodeOpCommand payload JSON  (so nodes can name their .fdp files correctly)
  - AsyncRecorder filePath:  /archives/{DrillId}/node_{NodeId}.fdp
```

---

### 5.5 Transition Planner (Macro-Transitions)

The `SystemMasterModule` acts as a **Process Manager**: every `SysOpRequest(TransitionState,
TargetState)` from the IOS is given to an internal **`TransitionPlanner`** that treats
the ESM as a directed graph and calculates the shortest valid path to the requested target
state.  The result is always a `Queue<ISysOpStep>` loaded into `DistributedTransaction.PlannedSteps`.

A simple, direct single-step request (e.g. `Standby → LoadingEdit`) is just a queue of
length 1.  A "wild" multi-step request (e.g. `RunningLive → RunningReplay`) produces a
longer queue.  The Tick() execution loop is completely agnostic to queue length: it pops
one state, runs the standard two-phase commit, and chains to the next entry when all nodes
ACK success.

**Key benefits of this unified design:**
- **Dumb IOS:** the client fires a single `SysOpRequest` naming only the desired final
  state and receives `SysOpStatus(InProgress, "Step X of Y: …")` progress updates
  autonomously.  It requires zero knowledge of ESM graph rules.
- **DRY execution path:** timeouts, watchdog monitoring, and distributed rollback are
  written exactly once in `DistributedTransaction` regardless of queue length.
- **Open/Closed for trajectories:** adding a new mandatory intermediate state to the ESM
  later only requires updating the graph definition inside `TransitionPlanner`; all client
  code and the 2PC loop are unchanged.
- **Compensatory rollback:** if a node fails at step N, the Master aborts the remaining
  queue, publishes `SystemStateTopic(SafeFallbackState)`, and sends `SysOpStatus(Failed)`
  with a human-readable description.

Every request — simple or wild — is resolved by the planner into a
`Queue<ISysOpStep>`.  Each entry is either a `TransitionStep` (ESM state change, executed
via 2PC `NodeOpCommand`) or an `OperationStep` (a distributed operation run while
residing in a state, e.g. `ReplaySeek`).  This distinction is essential: `ReplaySeek` is
strictly an operation, not an ESM state — it is only valid once the system is already in
`RunningReplay`.

**Example trajectories resolved by the planner:**

| Current State | Target / Hints | Planned Queue (`ISysOpStep` items) |
|---------------|----------------|-------------------------------------|
| `Standby` | `LoadingEdit` | `[TransitionStep(LoadingEdit)]` |
| `RunningLive` | `RunningReplay` | `[TransitionStep(UnloadingLive), TransitionStep(Standby), TransitionStep(LoadingReplay), TransitionStep(RunningReplay)]` |
| `RunningLive` | `RunningReplay` + `TargetWallTicks=T15` | `[TransitionStep(UnloadingLive), TransitionStep(Standby), TransitionStep(LoadingReplay), TransitionStep(RunningReplay), OperationStep(ReplaySeek, T15)]` |
| `RunningEdit` | `RunningLive` | `[TransitionStep(UnloadingEdit), TransitionStep(Standby), TransitionStep(LoadingLive), TransitionStep(RunningLive)]` |
| `RunningReplay` | `RunningEdit` | `[TransitionStep(UnloadingReplay), TransitionStep(Standby), TransitionStep(LoadingEdit), TransitionStep(RunningEdit)]` |

> **Transition hints:** the `SysOpRequest.PayloadJson` may carry optional metadata (e.g.
> `"DrillId"`, `"ScenarioId"`, `"TargetWallTicks"`) that the planner threads through the
> `NodeOpCommand` payloads for each step.  This lets the IOS say "go to RunningReplay
> of drill 999, seek to T+15 min" in a single request without embedding sequencing logic
> in the client.  The `TargetWallTicks` hint causes the planner to append an
> `OperationStep(ReplaySeek, TargetWallTicks)` after the final `TransitionStep(RunningReplay)`.  The 2PC execution loop dispatches it with a `NodeOpCommand(ReplaySeek, TargetWallTicks)` and waits for all nodes to ACK exactly as it does for a state transition — the loop is oblivious to step type.

#### 5.5.1 Sequence Diagram — Complex (Wild) Transition

The following diagram illustrates the IOS firing a wild `RunningLive → RunningReplay`
request.  The Master resolves the 4-step trajectory internally; the IOS only monitors
`SysOpStatus` progress updates.

```mermaid
sequenceDiagram
    autonumber
    participant IOS as IOS (Client)
    participant Master as SystemMasterModule
    participant Topic as SystemStateTopic (DDS)
    participant Slaves as SystemSlaveModule (All Nodes)

    IOS->>Master: SysOpRequest(TransitionState, Target=RunningReplay)
    Note over IOS: Locks UI — waits for final SysOpStatus

    Note over Master: TransitionPlanner evaluates ESM graph:<br/>Current=RunningLive, Target=RunningReplay<br/>→ PlannedSteps = [TransitionStep(UnloadingLive), TransitionStep(Standby),<br/>   TransitionStep(LoadingReplay), TransitionStep(RunningReplay)]<br/>   (+ optional OperationStep(ReplaySeek, T15) if TargetWallTicks hint present)
    Note over Master: Creates DistributedTransaction (TotalSteps=4 or 5)

    loop Saga Execution — drain PlannedSteps queue
        Note over Master: Pop next step (TransitionStep or OperationStep)
        Master->>Slaves: NodeOpCommand(PrepareState, NextState)

        Slaves-->>Master: NodeOpStatus(InProgress)
        Master-->>IOS: SysOpStatus(InProgress, "Step 1 of 4: UnloadingLive")
        Note over IOS: Updates progress bar dynamically

        Note over Slaves: Heavy work on background thread<br/>(flush recorder, clear ECS, open playback file…)

        alt Happy Path — all nodes succeed
            Slaves-->>Master: NodeOpStatus(Success)
            Master->>Topic: Publish SystemStateTopic(NextState)
            Note over Master: Step committed. CompletedSteps++.<br/>TransitionStep → updates SystemStateTopic.<br/>OperationStep → SystemStateTopic unchanged.<br/>If queue not empty → pop next step, chain continues.
        else Failure Path — any node fails or times out
            Slaves-->>Master: NodeOpStatus(Failed)
            Note over Master: Watchdog triggers compensatory rollback.<br/>Clear remaining PlannedSteps queue.
            Master->>Slaves: NodeOpCommand(AbortTransaction)
            Master->>Topic: Publish SystemStateTopic(SafeFallbackState)
            Master-->>IOS: SysOpStatus(Failed, "Step 1 failed: Node 200 timed out")
            Note over IOS: Unlocks UI — shows error modal
        end
    end

    Note over Master: PlannedSteps queue empty — goal reached
    Master-->>IOS: SysOpStatus(Success)
    Note over IOS: Unlocks UI — transition complete
```

#### 5.5.2 TransitionPlanner — BFS Implementation

**Why BFS?**  
All ESM state transitions have equal "weight" (one 2PC round-trip per hop). Dijkstra
or A\* are anti-patterns here. **Breadth-First Search (BFS)** is the optimal O(V+E)
algorithm, guaranteeing the shortest path in an unweighted directed graph and cleanly
throwing before any network command is issued if the target is unreachable.

**Graph Definition (Adjacency List)**

The `TransitionPlanner` owns the authoritative single-source-of-truth for valid
lifecycles.  Adding a new intermediate state tomorrow requires touching only this
dictionary — the BFS algorithm and the 2PC execution loop remain unchanged (Open/Closed
Principle):

```csharp
// Location: Bagira.Orchestrator/TransitionPlanner.cs
public class TransitionPlanner
{
    // Directed adjacency list — defines every legal ESM edge.
    private readonly Dictionary<ESMState, HashSet<ESMState>> _validTransitions = new()
    {
        { ESMState.Standby,         new() { ESMState.LoadingEdit, ESMState.LoadingLive, ESMState.LoadingReplay } },
        { ESMState.LoadingEdit,     new() { ESMState.RunningEdit,   ESMState.Standby } },
        { ESMState.RunningEdit,     new() { ESMState.LoadingDryRun, ESMState.LoadingLive, ESMState.UnloadingEdit } },
        { ESMState.LoadingDryRun,   new() { ESMState.RunningDryRun, ESMState.RunningEdit } },
        { ESMState.RunningDryRun,   new() { ESMState.UnloadingDryRun } },
        { ESMState.UnloadingDryRun, new() { ESMState.RunningEdit } },
        { ESMState.UnloadingEdit,   new() { ESMState.Standby } },
        { ESMState.LoadingLive,     new() { ESMState.RunningLive,   ESMState.Standby } },
        { ESMState.RunningLive,     new() { ESMState.UnloadingLive } },
        { ESMState.UnloadingLive,   new() { ESMState.Standby } },
        { ESMState.LoadingReplay,   new() { ESMState.RunningReplay, ESMState.Standby } },
        { ESMState.RunningReplay,   new() { ESMState.UnloadingReplay, ESMState.LoadingLive } },
        { ESMState.UnloadingReplay, new() { ESMState.Standby } },
    };
```

**BFS Pathfinding**

```csharp
    private List<ESMState> CalculateShortestPath(ESMState current, ESMState target)
    {
        if (current == target) return new List<ESMState>();

        var frontier  = new Queue<ESMState>();
        var cameFrom  = new Dictionary<ESMState, ESMState>();

        frontier.Enqueue(current);
        cameFrom[current] = current;   // mark visited

        while (frontier.Count > 0)
        {
            var node = frontier.Dequeue();
            if (node == target)
                return ReconstructPath(cameFrom, current, target);

            if (_validTransitions.TryGetValue(node, out var neighbors))
                foreach (var next in neighbors)
                    if (!cameFrom.ContainsKey(next))
                    {
                        frontier.Enqueue(next);
                        cameFrom[next] = node;
                    }
        }

        throw new InvalidOperationException(
            $"No valid ESM trajectory found from {current} to {target}.");
    }

    private static List<ESMState> ReconstructPath(
        Dictionary<ESMState, ESMState> cameFrom, ESMState start, ESMState target)
    {
        var path    = new List<ESMState>();
        var current = target;
        while (current != start) { path.Add(current); current = cameFrom[current]; }
        path.Reverse();
        return path;
    }
```

**Assembling the Polymorphic Command Queue**

Once BFS returns the state path (e.g. `[UnloadingLive, Standby, LoadingReplay,
RunningReplay]`) the planner wraps each state in a `TransitionStep` and then appends
any optional `OperationStep` entries derived from the request's `PayloadJson` hints:

```csharp
    public Queue<ISysOpStep> PlanTrajectory(ESMState currentState, SysOpRequest request)
    {
        var steps = new Queue<ISysOpStep>();

        // 1. Pathfind — state transitions only
        var statePath = CalculateShortestPath(currentState, request.TargetState);
        foreach (var state in statePath)
            steps.Enqueue(new TransitionStep(state, request.PayloadJson));

        // 2. Append hint-driven operations after the final state
        if (request.TargetState == ESMState.RunningReplay
            && TryExtractSeekTarget(request.PayloadJson, out long targetTick))
            steps.Enqueue(new OperationStep(SysOpType.ReplaySeek, targetTick));

        return steps;
    }
}
```

> **Safety guarantee:** If the IOS sends an impossible request (e.g. `RunningDryRun →
> RunningReplay`) the BFS exhausts the frontier and throws `InvalidOperationException`
> before any `NodeOpCommand` is broadcast over DDS.  The 2PC execution loop is never
> entered, and the cluster state is left completely unchanged.

---

### 5.6 Time Control Architecture

The `SystemMasterModule` is the **Time Authority** for the distributed cluster.  All time
logic is encapsulated behind an `ITimeController` interface, enabling hot-swapping between
real-time and deterministic stepping modes without disrupting the rest of the system.

#### 5.6.1 `ITimeController` Interface

```csharp
// Location: FDP/Toolkits/FDP.Toolkit.Time/ITimeController.cs  (new)
public interface ITimeController
{
    GlobalTime Update();                    // Advance internal clock, return current state
    void       SetTimeScale(float scale);   // 0.0 = pause, 1.0 = realtime, 2.0 = fast-fwd
    GlobalTime GetCurrentState();           // Non-advancing read
    void       SeedState(GlobalTime seed);  // Abrupt reset — bypasses PLL / error filters
}
```

#### 5.6.2 `SwitchableTimeController` Proxy

A `SwitchableTimeController` wraps an active `ITimeController` instance.  The `ModuleHostKernel`
holds a stable reference to the proxy; the underlying strategy can be hot-swapped at any
frame-perfect moment without any other code being aware of the change.

The proxy exposes a single dedicated swap method, `SwitchTo()`, which is **never** called
directly by `SystemMasterModule` or `SystemSlaveModule` — it is called exclusively by the
`DistributedTimeCoordinator` (Master node) and `SlaveTimeModeListener` (Slave nodes) at
exactly the correct Barrier Frame (see §5.6.5):

```csharp
public class SwitchableTimeController : ITimeController
{
    private ITimeController _activeController;

    public SwitchableTimeController(ITimeController initial)
    {
        _activeController = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    /// <summary>
    /// The only public API specific to this class.
    /// Called by the coordinator layer when the Future Barrier frame is reached.
    /// Gracefully transfers the exact current GlobalTime state to the new strategy.
    /// </summary>
    public void SwitchTo(ITimeController newController)
    {
        if (newController == null) throw new ArgumentNullException(nameof(newController));
        if (_activeController == newController) return;

        var currentState = _activeController.GetCurrentState();
        newController.SeedState(currentState);
        _activeController = newController;
    }

    public ITimeController ActiveController => _activeController;

    // ─── ITimeController Proxy — all calls forwarded to active strategy ─────
    public GlobalTime Update()                  => _activeController.Update();
    public void       SetTimeScale(float scale) => _activeController.SetTimeScale(scale);
    public float      GetTimeScale()            => _activeController.GetTimeScale();
    public TimeMode   GetMode()                 => _activeController.GetMode();
    public GlobalTime GetCurrentState()         => _activeController.GetCurrentState();
    public void       SeedState(GlobalTime s)   => _activeController.SeedState(s);
    public void       Dispose()                 => _activeController.Dispose();
}
```

> **Key design rule:** `SwitchableTimeController` knows nothing about DDS, future barriers,
> or the ESM.  It is a pure Proxy + Strategy implementation.  Network synchronisation of
> the swap is fully handled by the coordinator layer described in §5.6.5.

#### 5.6.3 Master-Side Time Strategies

| Strategy | Used When | Behaviour |
|----------|-----------|-----------|
| `MasterTimeController` | `RunningLive`, `RunningReplay`, `RunningDryRun` | Driven by `Stopwatch`; publishes `TimePulseDescriptor` at 1 Hz and on every `SetTimeScale()` call |
| `SteppedMasterController` | Deterministic / debug mode | Halts real-time progression; publishes `FrameOrderDescriptor` per logical tick; waits for `FrameAckDescriptor` before advancing |

During `RunningReplay`, `MasterTimeController` is seeded with the recording epoch via
`SeedState()` so that `GlobalTime.TotalTime` measures seconds from the start of the
recording (see §8.3).

#### 5.6.4 Abrupt Reset (Jump-To-Time Interlock)

Discontinuous operations (seeking during replay, branching live-from-replay) require a
strict **Control-Plane / Data-Plane interlock** to prevent the distributed cluster from
diverging at different timestamps:

```
Master receives jump/seek request
  │
  ├─ 1. Hard freeze: SetTimeScale(0.0), halt TimePulseDescriptor broadcast
  │
  ├─ 2. SeedState(targetTime) on local MasterTimeController
  │      (establishes the new reference epoch before any node is commanded)
  │
  ├─ 3. Broadcast NodeOpCommand(ReplaySeek / PrepareState, targetTime)
  │
  ├─ 4. Wait for NodeOpStatus(Success) from ALL participating nodes
  │      (slaves call SlaveTimeController.SeedState() — bypassing PLL error
  │       filters — then execute their heavy data reconstruction)
  │
  └─ 5. Resume: SetTimeScale(savedScale), restart TimePulseDescriptor
         (time only flows once every node has confirmed it is temporally coherent)
```

> **Why explicit SeedState bypass matters:**  
> The `SlaveTimeController` PLL uses a `JitterFilter` to distinguish network jitter from
> intentional magnitude jumps.  A 15-minute seek looks like a catastrophic clock error to
> the PLL.  `SeedState()` provides a dedicated path that bypasses the filter entirely,
> guaranteeing instant deterministic snap without any slew interpolation artefacts.

#### 5.6.5 Deterministic Mode Switching — `DistributedTimeCoordinator` and Future Barrier

Switching between continuous real-time and deterministic lockstep is **completely
independent of the ESM**.  It is available at any point while the simulation is in a
*running* state (`RunningLive`, `RunningReplay`, `RunningDryRun`).  Because a standard
2PC `SysOp` round-trip would cause different nodes to swap at slightly different simulation
frames (destroying determinism), the switch is instead coordinated via a lightweight
**Future Barrier** on the dedicated **Time Plane**.

**Why 2PC SysOp cannot be used here:**  
Network latency means Node A might receive a `NodeOpCommand(SwitchMode)` at Frame 100 and
Node B at Frame 102.  If each node swaps its controller on receipt, determinism is instantly
destroyed.  Blocking the main simulation thread to wait for ACKs is equally unacceptable.

**The Future Barrier approach:**

```
Request arrives at DistributedTimeCoordinator (Master node)
  │
  ├─ 1. If sim clock is currently running (TimeScale > 0):
  │       Call SetTimeScale(0.0) on the active ITimeController to pause first.
  │       (The coordinator owns time — it pauses before negotiating the barrier.)
  │
  ├─ 2. Read current ECS frame counter (e.g. Frame 100).
  │      Add lookahead: BarrierFrame = 100 + N  (e.g. N=10 → BarrierFrame = 110).
  │
  ├─ 3. Publish SwitchTimeModeEvent { TargetMode, BarrierFrame } over DDS
  │       (via BlitEventTranslator<SwitchTimeModeEvent> — zero-allocation raw memcpy)
  │
  └─ 4. Continue simulating normally until local frame counter reaches BarrierFrame.
         On that exact tick → call _switchableTime.SwitchTo(new SteppedMasterController(...))
         Restore saved TimeScale.

On each Slave node — SlaveTimeModeListener:
  ├─ Receives SwitchTimeModeEvent from DDS.
  ├─ Continues simulating normally.
  └─ When local frame counter reaches BarrierFrame → call _switchableTime.SwitchTo(...)
     Because all nodes derive their frame counter from the same master clock, the swap
     occurs at the identical simulation instant on every node simultaneously.
```

**`SwitchTimeModeEvent` DDS message:**

```csharp
// Location: FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs  (addition)
// Visibility: internal to the time toolkit — not part of the public orchestration API.
[DdsTopic("SwitchTimeModeEvent")]
[DdsIdlFile("bdc-time")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
public struct SwitchTimeModeEvent
{
    public TimeMode TargetMode;     // Continuous | Deterministic
    public ulong    BarrierFrame;   // ECS global frame counter at which to swap
    public float    FixedDelta;     // Only used when TargetMode = Deterministic
}

public enum TimeMode : int
{
    Continuous    = 0,   // Real-time PLL (SlaveTimeController)
    Deterministic = 1,   // Lockstep fixed-delta (SteppedSlaveController)
}
```

**`BlitEventTranslator<SwitchTimeModeEvent>` — network registration (composition root):**

`SwitchTimeModeEvent` must travel from the `DistributedTimeCoordinator` on the Master
node to all Slave `SlaveTimeModeListener` instances over CycloneDDS.  Because writing
a full custom ECS translator for an internal struct would be heavyweight, the platform
provides `BlitEventTranslator<T>`, a generic zero-allocation bridge that performs a raw
`memcpy` of the unmanaged struct both on the write (FdpEventBus → CycloneDDS) and read
(CycloneDDS → FdpEventBus) sides.  Registration at the NetworkModule composition root:

```csharp
// In TimeNetworkModule.RegisterTranslators() — or equivalent composition root
// called during application startup on EVERY node (Master and Slaves).

// Publish side — Master node publishes SwitchTimeModeEvent to FdpEventBus;
// BlitEventTranslator serialises it as raw bytes and writes to CycloneDDS.
networkModule.RegisterEgressBlit<SwitchTimeModeEvent>(
    topicName: "SwitchTimeModeEvent",
    qos:       new DdsQos { Reliability = DdsReliability.Reliable,
                            Durability  = DdsDurability.Volatile });

// Subscribe side — incoming CycloneDDS bytes are blit-copied back into
// SwitchTimeModeEvent and published to the local FdpEventBus.
// SlaveTimeModeListener already subscribes to FdpEventBus<SwitchTimeModeEvent>.
networkModule.RegisterIngressBlit<SwitchTimeModeEvent>(
    topicName: "SwitchTimeModeEvent");
```

> **Why `BlitEventTranslator` is safe for `SwitchTimeModeEvent`:**  
> `SwitchTimeModeEvent` is a plain unmanaged struct (no managed references, no padding
> alignment surprises across nodes because all Bagira nodes share the same compiled
> binary).  The two-field `ulong BarrierFrame` + `float FixedDelta` layout is stable
> across process restarts.  A schema version field is **not** required here because
> `SwitchTimeModeEvent` is an internal ephemeral signal — not stored on disk — so layout
> drift between live nodes is impossible in a correctly deployed cluster.

**Architectural boundaries — who calls what:**

| Layer | Component | Responsibility |
|-------|-----------|----------------|
| Pure math | `SlaveTimeController`, `SteppedSlaveController`, `MasterTimeController`, `SteppedMasterController` | Calculate `DeltaTime` only; no swap logic |
| Proxy | `SwitchableTimeController` | Hold active strategy; `SwitchTo()` transfers state to new strategy |
| Coordinator | `DistributedTimeCoordinator` (Master), `SlaveTimeModeListener` (Slave) | Compute barrier frame; publish / receive `SwitchTimeModeEvent`; call `SwitchTo()` at the right frame |
| ESM / domain | `SystemMasterModule`, `SystemSlaveModule`, physics, AI | Completely unaware of time-mode switching; just read `GlobalTime.DeltaTime` |

---

### 5.7 Centralized Network Identity Authority (`DdsIdAllocatorServer`) {#57-centralized-network-identity-authority}

**Why the Master owns the ID server:**  
The `DdsIdAllocatorServer` is relocated from `SimHostSubsystem` into
`SystemMasterModule`.  Hosting it on individual simulation nodes allows split-brain
ID allocation if a node crashes or is replaced.  The Master is the sole state
authority of the cluster — it is therefore the only logical place to host the
identity authority.

**Replay Collision Problem:**  
When a user branches into a live session from a replay, or injects a Story into a
replaying world, newly spawned entities receive fresh `NetworkIdentity` IDs.  If the
allocator's counter has not advanced past the historical high-water mark of the
recording, the new IDs will collide with entity IDs hard-coded inside the `.fdprec`
file, causing catastrophic `NetworkEntityMap` key collisions.

**Solution — Orchestrated High-Water Reset:**

The reset is embedded in the 2PC `LoadingReplay` transaction:

```
Phase 1 — SCATTER (PrepareState, LoadingReplay):
  Each SlaveModule opens the .fdprec companion .meta.json manifest.
  The manifest exposes MaxNetworkId (persisted by AsyncRecorder during FinalizeRecordingAsync).
  Slave replies: NodeOpStatus(Success, ResultJson={"MaxNetworkId": 145000})

Phase 2 — GATHER (Master collects all ACKs):
  Master parses ResultJson across all nodes → finds absolute max.
  SafeStartId = max(AllMaxNetworkIds) + ReplayIdSafetyBuffer  // e.g. + 10 000

Phase 3 — RESET & BROADCAST:
  Master calls DdsIdAllocatorServer.Reset(SafeStartId) directly (in-process).
  Server publishes IdResponse { Type = Resp_Reset, Start = SafeStartId } over DDS.
  Every DdsIdAllocator client on every node receives this, flushes its
  pre-allocated _availableIds queue, and re-fetches a fresh chunk starting at SafeStartId.

Phase 4 — COMMIT → RunningReplay.
```

**Architectural rules:**
- Slave nodes hold no `DdsIdAllocatorServer` instance — only `DdsIdAllocator` clients.
- `RecordingMetadata` (written by `AsyncRecorder.FinalizeRecordingAsync`) **must**
  include a `MaxNetworkId` field captured at the moment the live recording ends.
- The safety buffer of 10 000 IDs protects against any entities allocated just before
  `FinalizeLive` that were not flushed to the metadata yet.
- During `OperationStep(ReplaySeek)` (§5.5) the ID allocator is **not** reset again —
  the reset occurs once at `LoadingReplay` and persists for the entire replay session.

**`RecordingMetadata` addition:**

```csharp
// In .meta.json schema — persisted by EcsRecordReplayController.FinalizeRecordingAsync
public class RecordingMetadata
{
    public Guid   DrillId;
    public long   StartWallTicks;
    public long   EndWallTicks;
    public int    MaxEntityId;          // existing
    public long   MaxNetworkId;         // NEW — used for replay collision avoidance
    public int    NodeId;
    public string ComponentSchemaHash;  // existing — layout drift guard
}
```

---

## 6. SystemSlaveModule

**Location:** `Bagira.SimHost/Modules/Orchestration/SystemSlaveModule.cs` (new)  
Also instantiated inside `Bagira.IG` and any other FDP node.

**IOS variant:** `Bagira.IOS` uses a **lightweight slave** — it has no ECS / FDP world,
so `SystemSlaveModule` must support a no-ECS mode.  The IOS slave still registers with
the Orchestrator (via heartbeat), participates in SysOps (replies `NodeOpStatus`), and
reacts to ESM state changes; it just skips any `IEsmHandler` that touches an
`EntityRepository`.

### 6.1 Responsibilities
- Listens to `NodeOpCommand` and dispatches to **registered ESM handlers**
- Manages idempotency (drops duplicate `TransactionId` commands)
- Publishes autonomous `NodeHeartbeat` at 1 Hz (wall-clock, independent of sim time)
- Bridges background async Task results back to the synchronous ECS loop
- Publishes `NodeOpStatus` responses

### 6.2 ESM Handler Registration

```csharp
public interface IEsmHandler
{
    // Returns true if this handler participates in the given operation
    bool CanHandle(NodeOpType op);

    // Called on background thread. Must not touch EntityRepository.
    // Returns null on success, error string on failure.
    Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct);

    // Called on main thread at BeforeSync phase after all nodes Prepare-acked.
    void Commit(NodeOpCommand cmd, EntityRepository repo);

    // Called on main thread at BeforeSync phase if transaction is aborted.
    void Abort(NodeOpCommand cmd, EntityRepository repo);
}
```

Example handlers in `Bagira.SimHost`:
- `LiveLoadEsmHandler` — loads scenario assets, initialises `AsyncRecorder`
- `ReplayLoadEsmHandler` — opens `PlaybackController`
- `EditLoadEsmHandler` — loads static terrain, disables physics systems
- `CheckpointEsmHandler` — on TakeSnapshot: calls `destRepo.SyncFrom(liveRepo)` on
  main thread (no pause), then hands `destRepo` to background task for compression
- `BattlespaceEsmHandler` — manages staged terrain loading

### 6.3 Main Thread Command Queue

Because `NodeOpCommand` arrives on the DDS network thread, and ECS mutations must
happen at `BeforeSync`, the slave uses an internal pending-action queue:

```
Network Thread             SystemSlaveModule.Tick() (BeforeSync)
     │                                  │
     │  receives NodeOpCommand          │
     ├──► enqueue PendingMainThreadAction │
     │                                  │
     │                                  ├──► dequeue PendingMainThreadAction
     │                                  ├──► call handler.Commit(cmd, repo)
     │                                  └──► publish NodeOpStatus(Success)
```

### 6.4 Heartbeat Timer

```csharp
// SystemSlaveModule owns a Stopwatch (NOT sim time based)
private readonly Stopwatch _heartbeatTimer = Stopwatch.StartNew();

// In Tick():
if (_heartbeatTimer.Elapsed.TotalSeconds >= 1.0)
{
    _heartbeatTimer.Restart();
    _ddsWriter.Publish(new NodeHeartbeat
    {
        NodeId            = _config.NodeId,
        SubsystemName     = _config.Name,
        LocalEsmState     = _localEsmState,
        WallTicksUtc      = DateTime.UtcNow.Ticks,
        CpuUsagePercent   = GetCpuPercent(),
        RamUsedBytes      = GC.GetTotalMemory(false) + _unmanagedBytesUsed,
        SimTickAdvancing  = _lastTickVersion != _repo.GlobalVersion,
        SubsystemsJson    = BuildSubsystemsJson(),
    });
    _lastTickVersion = _repo.GlobalVersion;
}
```

### 6.5 ESM State Change Notifications (Internal)

Whenever `SystemSlaveModule` commits a new ESM state (after receiving a
`NodeOpCommand(CommitState, targetState)`), it raises an **internal `FdpEventBus`
event** — no new `IModule` interface hook is needed.

```csharp
// New internal event — published via FdpEventBus (not over DDS)
public struct EsmStateChangedEvent
{
    public ESMState Previous;
    public ESMState Next;
}

// In SystemSlaveModule, after commit:
_eventBus.Publish(new EsmStateChangedEvent { Previous = prev, Next = _localEsmState });
```

Any system that needs to react to ESM transitions (e.g. RecorderSystem,
PhysicsSystem) subscribes:

```csharp
_eventBus.Subscribe<EsmStateChangedEvent>(OnEsmStateChanged);
```

This decouples modules from the orchestration system entirely.

---

### 6.6 Time Slave Integration — `SlaveTimeController` and `ModuleHostKernel` Adapter

The `SystemSlaveModule` is deliberately ECS-agnostic and must not inject `GlobalTime`
directly into the `EntityRepository`.  Time injection is the responsibility of the
`ModuleHostKernel`, enforcing the Single Responsibility Principle:

```
┌───────────────────────────────────────────────────────────────────────────┐
│  MAIN UPDATE LOOP — ModuleHostKernel.Tick()                               │
│                                                                           │
│  1. _slaveTime.Update()                                                   │
│     └─ SlaveTimeController runs PLL against latest TimePulseDescriptor    │
│        → produces GlobalTime { TotalTime, TotalWallTicks, TimeScale }     │
│                                                                           │
│  2. _liveWorld.SetSingletonUnmanaged(globalTime)                          │
│     └─ Blasts GlobalTime into ECS as an unmanaged singleton               │
│        → single source of truth for physics, AI, recording                │
│                                                                           │
│  3. _scheduler.Execute(deltaTime)                                         │
│     └─ All domain modules read World.GetSingletonUnmanaged<GlobalTime>()  │
│        — none of them know about DDS, SlaveTimeController, or ESM         │
└───────────────────────────────────────────────────────────────────────────┘
```

#### 6.6.1 `SlaveTimeController` — Phase-Locked Loop (PLL)

The slave uses a `SlaveTimeController` with an internal PLL to synchronise its virtual
clock to the master's `TimePulseDescriptor` between pulses, eliminating network jitter:

```
Incoming TimePulseDescriptor (1 Hz):
  MasterWallTicks = T_master
  SimTimeSnapshot = S_master  (seconds from recording epoch, or live elapsed time)
  TimeScale       = 1.0

PLL error = S_master - SlaveLocalTime.TotalTime
  │
  ├─ Within JitterFilter.Threshold (e.g. ±200 ms):
  │    Slew: gradually correct local clock toward master over N frames
  │    → smooth visual interpolation, no pops
  │
  └─ Beyond JitterFilter.Threshold (large network gap or intentional seek):
       Hard snap safety threshold triggers — or SeedState() is called explicitly
       for intentional jumps (see §5.6.4)
```

`SeedState()` provides the explicit path for all orchestrated jumps.  The PLL filter is
bypassed so the clock teleports to the target time without any slew lag.

#### 6.6.2 Continuous vs Deterministic Slave Modes

| Slave Mode | Controller | Source of Time Signal |
|------------|------------|-----------------------|
| Real-time | `SlaveTimeController` (PLL) | `TimePulseDescriptor` from Master |
| Deterministic | `SteppedSlaveController` | `FrameOrderDescriptor`; publishes `FrameAckDescriptor` before advancing |

Switching between modes is **independent of the ESM**.  It is available at any point
while the simulation is in a running state and is triggered entirely within the time
toolkit via the **Future Barrier** mechanism (see §5.6.5).

On a slave node, the `SlaveTimeModeListener` subscribes to `SwitchTimeModeEvent` over DDS.
When the event arrives, it waits silently until the local ECS frame counter reaches the
aggreed `BarrierFrame`, then calls `_switchableTime.SwitchTo(new SteppedSlaveController(...))`
(or back to `SlaveTimeController` for continuous mode).  `SystemSlaveModule` is completely
unaware of this — it never calls `SwitchTo()` directly.

---

## 7. Node Health Monitoring

```
┌─────────────────────────────────────────────────────────────────────────┐
│  HEALTH MONITORING FLOW                                                  │
│                                                                          │
│  Each Node                   Master (SystemMasterModule)                 │
│    │                                │                                   │
│    │  NodeHeartbeat (1 Hz BestEffort)│                                   │
│    ├──────────────────────────────► │                                   │
│    │                                │── update NodeHealthProfile         │
│    │                                │── reset SecondsSinceLastHeartbeat  │
│    :   (silence > 5s)               │                                   │
│                                     │── SecondsSinceLastHB > 5          │
│                                     │                                   │
│                                     │ if node.IsCritical:               │
│                                     │   → publish SystemState(Degraded)│
│                                     │   → publish SysOpStatus(Failure)  │
│                                     │     to any active SysOp           │
│                                     │ else:                             │
│                                     │   → prune from ActiveNodes        │
│                                     │   → log warning                   │
└─────────────────────────────────────────────────────────────────────────┘
```

### 7.1 Node Criticality

Nodes are classified in the system configuration (`config.json`):

```json
{
  "nodes": [
    { "nodeId": 100, "name": "SimHost",  "critical": true  },
    { "nodeId": 200, "name": "IG",       "critical": true  },
    { "nodeId": 300, "name": "IOS",      "critical": false },
    { "nodeId": 400, "name": "DataLogger","critical": false }
  ]
}
```

---

## 8. Replay Subsystem

### 8.1 Components

| Component | Location | Role |
|-----------|----------|------|
| `ReplayMasterModule` | `Bagira.Orchestrator` (Time Master node) | Drives the replay playhead by seeding `MasterTimeController` with the recording epoch; controls speed via `SetTimeScale()`; publishes the existing `TimePulseDescriptor` — no new DDS topic needed |
| `IRecordReplayController` | `FDP/Kernel/Fdp.Kernel/Orchestration/` (new) | Generic abstraction over any recording/playback subsystem; ECS-based and custom-module controllers implement this |
| `EcsRecordReplayController` | `Bagira.SimHost/Modules/Orchestration/` (new) | FDP ECS adapter; wraps `AsyncRecorder` + `PlaybackController`; registered with `SystemSlaveModule` as one of potentially several `IRecordReplayController` instances |
| `ReplayLoadEsmHandler` | Each FDP node (`Bagira.SimHost`, `Bagira.IG`) | `IEsmHandler` for `PrepareReplay` / `FinalizeReplay`; disables sim groups; toggles `GhostCreationSystem.BypassLifecycle`; creates and owns `EcsRecordReplayController` |
| `NetworkLifecycleSystemGroup` | `FDP/ModuleHost/ModuleHost.Core/Scheduling/` (new) | Concrete `ISystemGroup` with `bool Enabled` toggle; encapsulates `LifecycleSystem`, `GhostPromotionSystem`, `NetworkGatewaySystem` so the orchestrator can disable all three with one O(1) assignment |

### 8.2 Integration with Existing PlaybackController

The existing `PlaybackController` (in `FDP/Kernel/Fdp.Kernel/FlightRecorder/PlaybackController.cs`)
already provides `SeekToFrame()` and `SeekToTick()`. However:

> ⚠️ **Gap 1:** Frame headers currently store only `(ulong)repo.GlobalVersion` (ECS tick) and
> frame type byte.  A `long WallClockTicks` (UTC ticks at capture time) must be added to both
> `RecorderSystem.RecordDeltaFrame` / `RecordKeyframe` and the `FrameMetadata` struct used
> by `PlaybackController.BuildFrameIndex()`.
>
> ⚠️ **Gap 2:** The existing `SeekToTick(EntityRepository, ulong)` does a **linear forward
> scan**.  `SeekToWallClockTicks` needs a proper binary search over `_frameIndex`.
>
> ⚠️ **Gap 3:** `GlobalTime` does not currently expose `TotalWallTicks`.  Both
> `MasterTimeController` and `SlaveTimeController` must populate a new `long TotalWallTicks`
> field so that `EcsRecordReplayController.ProcessPlaybackTick` has a clock value to pass to
> `SeekToWallClockTicks`.

After fixing those gaps, the `PlaybackController` gains `SeekToWallClockTicks(EntityRepository, long wallTicks)`, and `GlobalTime` exposes `TotalWallTicks` for PLL-derived seeking.

### 8.3 ReplayMasterModule Internals

The `ReplayMasterModule` reuses the existing `MasterTimeController` rather than maintaining
a raw independent wall-clock counter.  On entering `RunningReplay` it seeds the controller
with the recording epoch so that `TotalTime` measures seconds from the start of the recording:

```
┌─ REPLAY MASTER: Entering RunningReplay ──────────────────────────────────┐
│  // Seed master clock to start of recording (or seek target).            │
│  // recording.StartWallTicks read from .fdp global header.               │
│  _masterTime.SeedState(new GlobalTime                                    │
│  {                                                                        │
│      TotalTime      = 0.0,                  // seconds from epoch start  │
│      TimeScale      = _playbackSpeed,       // 1.0 = realtime            │
│      StartWallTicks = recording.StartWallTicks,                          │
│  });                                                                      │
└──────────────────────────────────────────────────────────────────────────┘

┌─ REPLAY MASTER TICK (BeforeSync phase) ──────────────────────────────────┐
│  // Speed changes are a single call; pulse is sent immediately:          │
│  //   _masterTime.SetTimeScale(0.0f)  // pause                           │
│  //   _masterTime.SetTimeScale(2.0f)  // 2× fast-forward                 │
│                                                                           │
│  GlobalTime time = _masterTime.Update();                                 │
│  // MasterTimeController automatically publishes TimePulseDescriptor     │
│  // at 1 Hz (and on every SetTimeScale call), carrying:                  │
│  //   MasterWallTicks  = Stopwatch.GetTimestamp()                        │
│  //   SimTimeSnapshot  = time.TotalTime  (seconds from recording epoch)  │
│  //   TimeScale        = current playback speed                          │
│                                                                           │
│  // For heavy seeks: SetTimeScale(0) BEFORE issuing ReplaySeek NodeOp;  │
│  // restore saved speed AFTER all nodes report NodeOpStatus(Success).    │
└────────────────────────────────────────────────────────────────────────────┘
```

### 8.4 ReplaySlaveModule Internals

Rather than consuming `TimePulseDescriptor` once per frame (which would lag by one pulse
interval), the slave reads its PLL-synchronized local clock maintained by
`SlaveTimeController`, giving zero-latency visual synchronization between pulses:

```
┌─ REPLAY SLAVE TICK (BeforeSync phase) ────────────────────────────────────┐
│  // SlaveTimeController.Update() has already run in this BeforeSync pass; │
│  // GlobalTime.TotalTime is PLL-adjusted seconds from the recording epoch. │
│  // (Gap: GlobalTime.TotalWallTicks needs to be exposed from               │
│  //  SlaveTimeController._virtualWallTicks — see §15.2)                   │
│                                                                            │
│  GlobalTime time = _kernel.CurrentTime;                                   │
│                                                                            │
│  foreach (var controller in _replayControllers)                           │
│      controller.ProcessPlaybackTick(time);                                │
│      // EcsRecordReplayController internally calls:                       │
│      //   Strategy A (small gap): StepForward() loop until wallTicks ok  │
│      //   Strategy B (large gap): SeekToWallClockTicks() keyframe anchor  │
└─────────────────────────────────────────────────────────────────────────────┘
```

> **Why PLL rather than direct pulse consumption?**  Consuming `TimePulseDescriptor` once
> per frame puts the slave at least one pulse-interval (1 Hz default) behind the master.
> The PLL predicts master time locally between pulses, giving millisecond-accurate
> synchronization independent of network jitter.  Fast-forward, slow-motion, and 0× pause
> are handled entirely by the existing `TimeScale` math in `GlobalTime` — no replay-specific
> network messages are needed.

### 8.5 Disabling the Simulation During Replay

Pipeline isolation is implemented in two layers because the codebase uses two distinct
scheduling architectures:

**Layer 1 — Fdp.Kernel system groups (ECS simulation computation)**  
`SimulationSystemGroup` and `PostSimulationSystemGroup` are Fdp.Kernel `SystemGroup`
subclasses that inherit `ComponentSystem.Enabled` (line 48, `ComponentSystem.cs`).  Flipping
this boolean is O(1) with zero registration churn:

```csharp
// In ReplayLoadEsmHandler.Commit() — main thread, BeforeSync phase
_simulationGroup.Enabled     = false;   // AI, kinematics stop
_postSimulationGroup.Enabled = false;   // physics integration stops

// On TeardownReplayAsync() (e.g. Live-from-Replay transition):
_simulationGroup.Enabled     = true;
_postSimulationGroup.Enabled = true;
// Both groups restart from the injected historical ECS state on the very next tick.
```

**Layer 2 — ModuleHost `IModuleSystem` ELM systems (entity lifecycle)**  
`LifecycleSystem`, `GhostPromotionSystem`, and `NetworkGatewaySystem` are `IModuleSystem`
instances registered with `SystemScheduler`.  `IModuleSystem` has no `Enabled` property.
Instead, these are encapsulated in a new `NetworkLifecycleSystemGroup` — a concrete
`ISystemGroup` implementation with a `bool Enabled` field.  `SystemScheduler.ExecuteGroup`
iterates `group.GetSystems()`; when `Enabled = false` the group simply skips all iterations:

```csharp
// At composition root (SimHostApp.OnLoad)
var networkLifecycleGroup = new NetworkLifecycleSystemGroup();
networkLifecycleGroup.AddSystem(lifecycleSystem);
networkLifecycleGroup.AddSystem(ghostPromotionSystem);
networkLifecycleGroup.AddSystem(networkGatewaySystem);
_scheduler.RegisterSystem(networkLifecycleGroup);   // registers as one IModuleSystem

var slaveModule = new SystemSlaveModule(networkLifecycleGroup, ...); // injected

// In ReplayLoadEsmHandler.Commit():
_networkLifecycleGroup.Enabled = false;

// In TeardownReplayAsync():
_networkLifecycleGroup.Enabled = true;
```

**Ghost creation bypass — bound to the entire `RunningReplay` state**  
`GhostCreationSystem.BypassLifecycle` is set `true` on entry to `RunningReplay` and held
`true` until the ESM exits that state.  Making this consistent across both seek and
continuous playback is critical: if ELM were re-enabled between seeks, entities in-flight
over DDS would stall in `Constructing` waiting for ACKs from a node that is only replaying
recorded data, not executing live handshake logic.

```csharp
// In ReplayLoadEsmHandler.Commit():
_ghostCreationSystem.BypassLifecycle = true;

// In TeardownReplayAsync():
_ghostCreationSystem.BypassLifecycle = false;
```

> **Why `GhostCreationSystem.BypassLifecycle` is safe to toggle globally:**  
> `GhostCreationSystem.CreateGhost()` is called **only** by ingress translators, which by
> definition only handle remote/unowned entities.  Locally owned entity creation goes through
> `NetworkSpawningSystem → EntityLifecycleModule`, a separate code path that is unaffected
> by this property.  During replay, locally owned entities are restored by
> `PlaybackController` chunk blasting — `NetworkSpawningSystem` is never invoked.

### 8.6 Heavy Seek (SysOp-coordinated)

Because seeking may take seconds on non-FDP nodes (volumetric IG, particle systems),
seeking is treated as a full `SysOpRequest(ReplaySeek)`:

```mermaid
sequenceDiagram
    participant IOS
    participant Master as ReplayMasterModule
    participant FDPSlave as FDP Slave (SimHost/IG)
    participant HeavySlave as Heavy IG (particles)

    IOS->>Master: SysOpRequest(ReplaySeek, TargetWallTicks=T15)
    Note over Master: Freeze TimePulseDescriptor emission
    Master->>FDPSlave: NodeOpCommand(ReplaySeek, T15)
    Master->>HeavySlave: NodeOpCommand(ReplaySeek, T15)

    activate HeavySlave
    HeavySlave->>Master: NodeOpStatus(InProgress)
    FDPSlave->>FDPSlave: PlaybackController.SeekToWallClockTicks(T15) ~10ms
    FDPSlave->>Master: NodeOpStatus(Success)
    Note over Master: Forward InProgress to IOS (show spinner)
    Master->>IOS: SysOpStatus(InProgress)

    Note over HeavySlave: Reconstruct particles/smoke ~2.5s
    HeavySlave->>Master: NodeOpStatus(Success)
    deactivate HeavySlave

    Note over Master: All nodes acked
    Master->>IOS: SysOpStatus(Success)
    Note over Master: Resume TimePulseDescriptor from T15
```

---

### 8.7 `IRecordReplayController` Interface

Every recording/playback subsystem (ECS-based or custom) exposes this interface to the
`SystemSlaveModule`.  The orchestrator calls lifecycle methods; each implementation manages
its own storage medium and internal state.

```csharp
// Location: FDP/Kernel/Fdp.Kernel/Orchestration/IRecordReplayController.cs
public interface IRecordReplayController
{
    // ─── Recording lifecycle ────────────────────────────────────────────────

    /// Called during LoadingLive. Opens output file, validates storage path.
    Task PrepareRecordingAsync(Guid drillId, string storageDirectory);

    /// Called during UnloadingLive. Flushes buffers, writes .meta.json manifest.
    Task FinalizeRecordingAsync();

    // ─── Replay lifecycle ────────────────────────────────────────────────────

    /// Called during LoadingReplay. Opens .fdp, validates schema manifest,
    /// pre-allocates decompression buffers.  All slow I/O happens here so
    /// ProcessPlaybackTick stays allocation-free on the hot path.
    Task PrepareReplayAsync(Guid drillId, string storageDirectory);

    /// Heavy, orchestrated jump (Two-Phase Commit ReplaySeek SysOp).
    /// Must not return until the module's internal state fully reflects the
    /// requested wall-clock position.  May run for seconds on heavy nodes.
    Task SeekToTimeAsync(long targetWallClockTicks);

    /// Lightweight per-frame catch-up for continuous playback.
    /// Called every BeforeSync; must complete within one frame budget (~16 ms).
    /// Implementation decides internally: sequential delta apply (small gap)
    /// or keyframe anchor + delta replay (large gap).
    void ProcessPlaybackTick(GlobalTime currentTime);

    /// Called during UnloadingReplay or before a Live-from-Replay branch.
    Task TeardownReplayAsync();
}
```

> **Design rules:**
> - All lifecycle methods return `Task` so `SystemSlaveModule` aggregates controllers via `Task.WhenAll`.
> - `ProcessPlaybackTick` is synchronous and on the hot path; heavy seeks go through `SeekToTimeAsync`.
> - Custom non-ECS modules (e.g. a legacy physics engine or IG particle system) implement `IRecordReplayController` directly and manage their own disk I/O and state injection.

---

### 8.8 `EcsRecordReplayController`

The FDP ECS implementation of `IRecordReplayController`.  Bridges the generic orchestration
API to the low-level `AsyncRecorder` + `PlaybackController`.

**Recording phase**

```csharp
// PrepareRecordingAsync
_recorder = new AsyncRecorder($"{storageDir}/{drillId}/node_{nodeId}.fdp");
_recorder.MinRecordableId = FdpConfig.SYSTEM_ID_RANGE;  // skip system entities

// Hot path — called from within ExportSystemGroup every frame
void ProcessRecordTick(EntityRepository repo, uint prevTick)
{
    if (++_framesSinceKeyframe >= KEYFRAME_INTERVAL)   // e.g. every 60 frames
    {
        _recorder.CaptureKeyframe(repo);
        _framesSinceKeyframe = 0;
    }
    else
    {
        _recorder.CaptureFrame(repo, prevTick);
        // Zero-allocation: raw memcpy to front-buffer; LZ4 compression on BG worker
    }
}

// FinalizeRecordingAsync → _recorder.Dispose()
// Blocks until BG worker finishes; writes .meta.json schema manifest
```

**Replay phase — initialization (`PrepareReplayAsync`)**

```csharp
_playback = new PlaybackController($"{storageDir}/{drillId}/node_{nodeId}.fdp");
// SchemaValidator.Validate() runs inside ctor.
// Throws InvalidDataException if struct layouts have drifted since recording.
```

**Replay phase — continuous playback (`ProcessPlaybackTick`)**  
Dual-strategy implementation to handle micro-lag and extreme time-scale differences without corrupting delta chains:

```
Strategy A — Sequential catch-up (small gap, e.g. node dropped 1–3 frames):
  while (nextFrameWallTicks(repo) <= targetWallTicks)
      _playback.StepForward(repo)
  // All deltas are applied in-memory; intermediate frames are never rendered.
  // Result: node catches up and presents the correct historical state.

Strategy B — Keyframe anchor (large gap, e.g. TimeScale >= 4× or multi-second lag):
  _playback.SeekToWallClockTicks(repo, targetWallTicks);
  // → binary search _frameIndex for closest preceding keyframe
  //   (gap: FrameMetadata.WallClockTicks field must be added — see §15.2)
  // → blast keyframe chunks directly into NativeChunkTable (memcpy)
  // → apply at most ~59 delta frames (guaranteed by KEYFRAME_INTERVAL = 60)
  // → completes in ~5–15 ms regardless of timeline jump magnitude
```

**Replay phase — heavy seek (`SeekToTimeAsync`)**

```csharp
public Task SeekToTimeAsync(long targetWallClockTicks) =>
    Task.Run(() => _playback.SeekToWallClockTicks(_repo, targetWallClockTicks));
// Wrapping as Task lets SystemSlaveModule fan-out via Task.WhenAll.
```

**Live-from-Replay transition** — `TeardownReplayAsync` disposes `PlaybackController` but leaves `EntityRepository` intact at the historical state.  `PrepareRecordingAsync` then opens a new `AsyncRecorder` for the branched DrillId path; on the next tick the live simulation groups resume from that injected state.

---

### 8.9 Node-Local Fan-Out/Fan-In (Scatter-Gather)

A single node may host both `EcsRecordReplayController` (seek: ~5 ms) and one or more
custom module controllers (seek: potentially seconds).  `NodeOpStatus` is a per-**node**
contract, so `SystemSlaveModule` must not report `Success` until every local controller
has fully converged:

```csharp
// Inside SystemSlaveModule command dispatcher — NodeOpCommand(ReplaySeek, targetTicks)

// 1. Immediately satisfy Master watchdog
_ddsWriter.Publish(new NodeOpStatus { TransactionId = cmd.TransactionId,
                                       NodeId = _nodeId,
                                       Status = OpStatus.InProgress });

// 2. Fan-out: start all controllers concurrently
var seekTasks = _replayControllers
    .Select(c => c.SeekToTimeAsync(cmd.PayloadAs<long>()))
    .ToArray();

ActiveNodeOperation.BackgroundTask = Task.WhenAll(seekTasks);

// 3. Fan-in — checked inside Tick() on main thread:
if (ActiveNodeOperation.BackgroundTask.IsCompleted)
{
    var status = ActiveNodeOperation.BackgroundTask.IsFaulted
        ? OpStatus.Failure : OpStatus.Success;
    _ddsWriter.Publish(new NodeOpStatus { ..., Status = status });
    ActiveNodeOperation = null;
}
```

Only when `Task.WhenAll` resolves (i.e. the **slowest** local controller finishes) does the
node report `Success`.  The Master then restores the saved `TimeScale` on
`MasterTimeController` once **all** nodes in the roster have reported `Success`.

---

### 8.10 Distributed Entity Lifecycle During Replay

**Authoritative nodes (own the recorded entities)**  
`EcsRecordReplayController.ProcessPlaybackTick` blasts `FrameMetadata` chunks directly into
`NativeChunkTable`.  `EntityHeader.LifecycleState` is part of the recorded chunk, so
entities instantly materialise as `Active`; the ELM pipeline is never invoked.  On a
discontinuous jump, the preceding keyframe contains the correct entity set; incremental
destruction / creation logs in delta frames are applied automatically by `PlaybackSystem.ApplyFrame()`.

**Unowned nodes (ghost entities populated via DDS egress)**  
No egress code changes are needed: the `ExportSystemGroup` egress translators
(`CycloneEgressSystem`) see the restored `Active` entities on the very next frame after a
seek and immediately flood DDS as they do in live mode.

On unowned nodes (e.g. an IG whose only copy of entity state comes from the network),
`GhostCreationSystem.BypassLifecycle = true` (set at `RunningReplay` entry, see §8.5)
causes `CreateGhost()` to place new arrivals directly into `EntityLifecycle.Active`,
bypassing `Ghost → Constructing → Active`.  `NetworkLifecycleSystemGroup.Enabled = false`
ensures `LifecycleSystem`, `GhostPromotionSystem`, and `NetworkGatewaySystem` never run.

**Heavy seek — clear and resync strategy**  
During a `ReplaySeek` SysOp (§8.6), unowned nodes purge their ghost world before
authoritative nodes flood the network:

```
1. Receive NodeOpCommand(ReplaySeek, targetWallClockTicks)
2. Iterate NetworkEntityMap; call repo.DestroyEntity(e) for all ghost entities
   (ELM teardown disabled → no DestructionOrder DDS round-trip required)
3. Publish NodeOpStatus(InProgress) immediately
4. Authoritative nodes seek, then CycloneEgressSystem floods DDS with restored state
5. Ingress translators call GhostCreationSystem.CreateGhost() with BypassLifecycle=true
   → entities materialise Active instantly, no distributed ACK handshake
6. This node publishes NodeOpStatus(Success) once local purge + seek completes
```

---

### 8.11 Live-from-Replay Temporal Interlock

When an operator transitions from `RunningReplay` to `RunningLive` ("Take Control"), the
Master must **hard-freeze the simulation time before any node is commanded to swap
pipelines**.  If the time pulse continued while nodes were tearing down the
`PlaybackController` and initialising the `AsyncRecorder`, each node would branch at a
slightly different timestamp depending on its disk I/O latency, destroying determinism.

The complete sequence is:

```mermaid
sequenceDiagram
    autonumber

    box "Client Layer (View)"
        participant IOS
    end

    box "Control Plane (Orchestration)"
        participant Master as SystemMasterModule
        participant Topic as SystemStateTopic
    end

    box "Data Plane (Node Executor)"
        participant Slave as SystemSlaveModule
        participant Handler as IEsmHandler (ReplayLoadEsmHandler)
        participant Controller as EcsRecordReplayController
        participant ECS as EntityRepository (NativeChunkTable)
    end

    Note over IOS, ECS: Current State: RunningReplay — time playing normally

    IOS->>Master: SysOpRequest(TransitionState, LoadingLive)
    Note over IOS: Locks UI

    Note over Master: ① Hard Freeze Timeline<br/>SetTimeScale(0.0) — halt TimePulseDescriptor broadcast

    Note over Master: ② Generate branched DrillId<br/>(e.g. Drill_999_Branch1 from Drill_999)

    Master->>Slave: NodeOpCommand(PrepareState, LoadingLive, NewDrillId)
    Slave-->>Master: NodeOpStatus(InProgress)
    Master-->>IOS: SysOpStatus(InProgress, "Step 1 of 1: LoadingLive")

    Note over Slave: Dispatch to registered IEsmHandler
    Slave->>Handler: Handle(PrepareState, LoadingLive)

    Note over Handler: Background thread — time is frozen, no race conditions

    Handler->>Controller: TeardownReplayAsync()
    Note over Controller: Disposes PlaybackController.<br/>Closes .fdprec read handles.<br/>EntityRepository memory intentionally untouched.

    Note over ECS: ③ Zero-Copy State Retention<br/>Historical NativeChunkTable chunks remain intact<br/>— no deserialization, no memcpy needed.

    Handler->>Controller: PrepareRecordingAsync(NewDrillId)
    Note over Controller: Initialises AsyncRecorder → /archives/Drill_999_Branch1/node_N.fdp<br/>Captures current frozen ECS memory as root Keyframe.

    Note over Handler: ④ Re-arm live pipeline<br/>SimulationSystemGroup.Enabled = true<br/>NetworkLifecycleSystemGroup.Enabled = true<br/>GhostCreationSystem.BypassLifecycle = false

    Handler-->>Slave: Task completed
    Slave-->>Master: NodeOpStatus(Success)

    Note over Master: ⑤ All participating nodes reported Success

    Master->>Topic: Publish SystemStateTopic(RunningLive, NewDrillId)

    Note over Master: ⑥ Resume Timeline<br/>SetTimeScale(1.0) — restart TimePulseDescriptor broadcast

    Master-->>IOS: SysOpStatus(Success)
    Note over IOS: Unlocks UI

    Note over ECS: Next ECS tick: AI and Physics systems wake up from<br/>the preserved historical state and execute live logic.
```

**Key architectural guarantees enforced by this sequence:**
- **Temporal determinism:** time is frozen at step ①; no ECS mutations occur while disk
  adapters are being swapped in nodes running at different IO speeds.
- **Zero-allocation branch:** `EcsRecordReplayController` does not touch the
  `EntityRepository` during `TeardownReplayAsync()`.  The historical world state is
  preserved in-place in unmanaged 64 KB `NativeChunkTable` blocks.
- **Clean domain separation:** AI, physics, and network systems are entirely unaware of
  the branch.  `GlobalTime.DeltaTime` is simply zero for the duration; when the time
  pulse resumes, domain modules wake up and execute from the injected historical state.

---

### 8.12 "Always Recording" — Event Capture During Paused Simulation Time

The `AsyncRecorder` does not go idle when simulation time is paused.
**Absolute wall-clock (UTC) time continues even while sim-time is
frozen**, so any state-changing event that occurs while the operator is editing the
scenario or while a live exercise is paused must still be captured and timestamped.

**What keeps accruing while sim-time is paused:**

| Event category | Example | Storage |
|----------------|---------|---------|
| Operator tactical graphics | Operator draws a new threat axis on the map | Event delta frame, UTC timestamp |
| Scenario entity edits | Entity placement, attribute change | Event delta frame, UTC timestamp |
| UI-triggered state changes | Operator marks an objective complete | Event delta frame, UTC timestamp |
| DDS messages (non-physics) | Formation command from IOS | Event delta frame, UTC timestamp |

**Recording mechanics:**

```
AsyncRecorder.CaptureEventFrame(wallClockTicks, payload):
  ├─ Appends a delta frame tagged FrameType = Event (not Physics/Keyframe)
  ├─ Stores WallClockTicks from DateTime.UtcNow.Ticks (NOT GlobalTime.TotalWallTicks)
  │    Reason: GlobalTime.TotalWallTicks is frozen while sim-time is paused.
  │    UTC wall-clock guarantees monotonically increasing timestamps for events
  │    that occur between physics ticks.
  └─ Zero-allocation: payload is memcpy'd to the front-buffer ring;
     LZ4 compression runs on BG worker thread, same as physics frames.
```

**Replay of event frames:**

During replay, `PlaybackController.SeekToWallClockTicks()` treats Event frames the same as
Physics frames when building the `_frameIndex` — they are ordered by their `WallClockTicks`
field.  `StepForward()` applies them in chronological order.  Because event frames carry
no physics delta (entities do not move), they are typically tiny; they are never skipped
during Strategy B keyframe anchoring — the apply-delta loop after the keyframe blast
handles them in the ≤ 59 frame window.

**Impact on `RunningEdit` recording:**

During `RunningEdit`, the simulation clock is always paused (`TimeScale = 0.0`).  The
`AsyncRecorder` is *not* active in edit mode AT ALL - no recording during scenario editing takes place.

> **Key invariant:** A recording interval always starts at `WallClockTicks ≥ 0` and is
> always ordered by UTC wall-clock time, regardless of whether simulation time is running,
> paused, or stepping deterministically.  Consumers of the playback file must never assume
> a one-to-one mapping between simulation ticks and wall-clock time.

---

## 9. Checkpoints & Dry Runs

### 9.1 Checkpoint — Non-Blocking Snapshot with Deferred Acknowledgement

**Design rationale:**  
A checkpoint is a non-mutating operation from the ESM perspective — taking five
snapshots in a row while `RunningLive` does not alter the exercise state.  Two
competing constraints must both be satisfied:

1. **The 60 Hz hot-path must never block on disk I/O.**  LZ4 compression + SSD write
   of a large `EntityRepository` takes 0.5–3 seconds; we cannot stall the main thread.
2. **The `NodeOpStatus(Success)` ACK must not be sent before the bytes are physically
   flushed to disk.**  Acknowledging before the write completes gives a false sense
   of data safety and violates the ACID contract of the 2PC transaction.

The solution is to split the operation across three execution boundaries:
- **Immediate `InProgress` ACK** on command receipt (satisfies Master watchdog)
- **~2 ms synchronous RAM clone** on the main thread at the next `BeforeSync` phase
- **Deferred `Success` / `Failure` ACK** emitted only when the background I/O thread
  finishes writing — monitored via the `SystemSlaveModule.Tick()` loop

**Concurrent checkpoint support:**  
Because `TakeCheckpoint` does not mutate the ESM, the `SystemMasterModule` does not
lock the ESM for each request.  The IOS may fire successive checkpoint requests freely;
the Master spawns a separate `DistributedTransaction` per request (tracked in
`Dictionary<Guid, DistributedTransaction>`).  Each transaction proceeds independently
through the pipeline.  ACKs arrive asynchronously, staggered by background I/O
completion order.

**`CheckpointIOWorker` — Serialized I/O Queue:**  
Multiple overlapping checkpoint requests may arrive before earlier disk writes finish.
To prevent CPU-cache thrashing and disk contention, a single background worker thread
drains a `ConcurrentQueue<(EntityRepository, Guid)>` one item at a time.  This
guarantees sequential, predictable I/O load regardless of how quickly the operator
fires new requests.

```
┌─ CHECKPOINT PROTOCOL ──────────────────────────────────────────────────────────┐
│                                                                                  │
│  MASTER: Receives SysOpRequest(TakeCheckpoint, Req_A)                           │
│  ├─ Validates ESMState == RunningLive                                            │
│  ├─ Spawns DistributedTransaction(Req_A)       ← non-exclusive, concurrent       │
│  └─ Broadcasts NodeOpCommand(TakeSnapshot, Req_A) to all slaves                 │
│                                                                                  │
│  SLAVE — NETWORK THREAD (on command receipt):                                   │
│  ├─ Publishes NodeOpStatus(InProgress, Req_A)   ← immediate, satisfies watchdog │
│  └─ Queues PendingMainThreadAction(TakeSnapshot, Req_A) for next BeforeSync     │
│                                                                                  │
│  SLAVE — MAIN THREAD (next BeforeSync tick):                                    │
│  ├─ snap = new EntityRepository(liveRepo.Schema)                                │
│  ├─ snap.SyncFrom(liveRepo)   // ~2ms unmanaged NativeChunkTable memcpy         │
│  │                            // managed components deep-cloned via AutoSerializer│
│  ├─ ECS main thread immediately resumes — 60 Hz is never blocked                │
│  └─ Enqueues (snap, Req_A) into CheckpointIOWorker ConcurrentQueue             │
│                                                                                  │
│  SLAVE — CheckpointIOWorker THREAD (serialized, one item at a time):            │
│  ├─ Pops (snap, Req_A) from queue                                                │
│  ├─ Captures in-flight DDS ingress for ~50ms → checkpoint_A_nodeN.dds          │
│  ├─ LZ4-compresses snap → checkpoint_A_nodeN.fdp                               │
│  └─ On success → sets CompletionResult[Req_A] = Success                         │
│     On IOException → sets CompletionResult[Req_A] = Failure                    │
│                                                                                  │
│  SLAVE — Tick() MONITOR LOOP (main thread, every frame):                        │
│  ├─ Checks CompletionResult for any pending transaction IDs                     │
│  └─ When found → publishes NodeOpStatus(Success/Failure, Req_A)                 │
│                  DEFERRED — only after actual disk flush                         │
│                                                                                  │
│  MASTER — on all nodes ACK Success:                                             │
│  └─ Closes DistributedTransaction(Req_A), sends SysOpStatus(Success) to IOS    │
│                                                                                  │
└──────────────────────────────────────────────────────────────────────────────────┘
```

**Sequence diagram — overlapping concurrent checkpoint requests:**

The diagram illustrates the key ordering requirement: `Req_B` arrives *after* the RAM
clone for `Req_A` has already been taken so that the two snapshots capture distinct
simulation states (not duplicates):

```mermaid
sequenceDiagram
    autonumber

    box "Client Layer"
        participant IOS
    end
    box "Control Plane"
        participant Master as SystemMasterModule
    end
    box "Data Plane"
        participant Slave as SystemSlaveModule
        participant ECS as CheckpointEsmHandler (Main Thread)
        participant Worker as CheckpointIOWorker (Background)
    end

    Note over IOS, Worker: 1. User requests first checkpoint
    IOS->>Master: SysOpRequest(TakeCheckpoint, Req_A)
    Note over Master: Spawns DistributedTransaction A (non-exclusive)
    Master->>Slave: NodeOpCommand(TakeSnapshot, Req_A)

    Slave-->>Master: NodeOpStatus(InProgress, Req_A)

    Note over ECS: Frame 1000 — BeforeSync
    ECS->>ECS: destRepoA.SyncFrom(liveRepo)  [~2ms]
    ECS->>Worker: Enqueue(destRepoA, Req_A)
    Note over ECS: Main thread resumes 60 Hz immediately

    Note over Worker: Worker pops Req_A, begins LZ4 + SSD write...

    Note over IOS, ECS: 2. Simulation ticks forward ~2 s; state changes materially
    IOS->>Master: SysOpRequest(TakeCheckpoint, Req_B)
    Note over Master: Spawns DistributedTransaction B (concurrent with A)
    Master->>Slave: NodeOpCommand(TakeSnapshot, Req_B)

    Slave-->>Master: NodeOpStatus(InProgress, Req_B)

    Note over ECS: Frame 1120 — BeforeSync
    ECS->>ECS: destRepoB.SyncFrom(liveRepo)  [captures NEW distinct state]
    ECS->>Worker: Enqueue(destRepoB, Req_B)
    Note over ECS: Main thread continues...

    Note over Worker: Finishes writing checkpoint_A.fdp to SSD
    Worker-->>Slave: CompletionResult[Req_A] = Success

    Note over Slave: Tick() monitor detects Req_A done
    Slave-->>Master: NodeOpStatus(Success, Req_A)   ← DEFERRED — after actual flush
    Note over Master: Commits DistributedTransaction A
    Master-->>IOS: SysOpStatus(Success, Req_A)

    Note over Worker: Worker pops Req_B, begins LZ4 + SSD write...
    Worker-->>Slave: CompletionResult[Req_B] = Success

    Note over Slave: Tick() monitor detects Req_B done
    Slave-->>Master: NodeOpStatus(Success, Req_B)
    Note over Master: Commits DistributedTransaction B
    Master-->>IOS: SysOpStatus(Success, Req_B)
```

**Teardown Barrier (graceful `UnloadingLive`):**  
The `CheckpointIOWorker` may still be draining its queue when the operator ends the
exercise.  The `LiveLoadEsmHandler` must not destroy the `EntityRepository` or close
`AsyncRecorder` handles until the queue is empty:

```
Slave receives NodeOpCommand(FinalizeLive):
  ├─ Publishes NodeOpStatus(InProgress, "Flushing checkpoints to disk…")
  ├─ Awaits CheckpointIOWorker.DrainAsync()
  │    (each pending snapshot in the queue writes to disk; no new items arrive
  │     because the Master has held the ESM at UnloadingLive — no new TakeSnapshot
  │     commands can be issued once FinalizeLive is in flight)
  ├─ Once queue empty → FinalizeRecordingAsync() — flush AsyncRecorder
  └─ Publishes NodeOpStatus(Success)
```

### 9.2 Storage Gateway Integration (Collecting Checkpoints)

Checkpoint files on local node SSDs are made durable via an explicit
`SysOpRequest(CollectCheckpoint, CheckpointId)`.  This uses the **Storage Gateway
Pattern** (see §13.1) — the Master requests each node's UNC manifest, and the Gateway
pulls the files to the central NAS using a single outbound SMB connection, avoiding
OS-imposed inbound SMB connection limits.

### 9.3 Dry Run vs Named Checkpoint

| Feature | Dry Run Checkpoint | Named Checkpoint |
|---------|-------------------|------------------|
| Trigger | `LoadingDryRun` transition | `SysOpRequest(TakeCheckpoint)` |
| Storage | RAM only (`EntityRepository` in memory) | RAM + async disk (.fdp + .dds supplement) |
| Restore | Automatic on `UnloadingDryRun` | Manual via `SysOpRequest(RestoreCheckpoint)` |
| DrillId context | In-progress edit session | Linked to current DrillId |
| Purpose | Quick scenario preview / rapid prototyping | Bug capture, branch point, session recovery |
| Concurrent | No (edit mode, single session) | Yes (IOS can fire in quick succession) |
| ACK timing | Synchronous (RAM only, no disk) | Deferred until local SSD write complete |

> **Dry Run mechanics:** On `LoadingDryRun` the slave calls `snap.SyncFrom(liveRepo)`
> (same path as named checkpoints) but retains the snapshot purely in RAM.  The
> simulation is unpaused → `RunningDryRun`.  On `UnloadingDryRun` the slave calls
> `liveRepo.SyncFrom(snap)` to blast the backup back into the live repository, exactly
> rewinding the world to the pre-dry-run state.  RAM is freed after the restore.

---

## 10. Stories — Multi-Tenant Micro-Scenarios

### 10.0 Concept & Definition

A **Story** is a highly isolated, localized micro-scenario that executes concurrently
while the global ESM remains in the `RunningLive` state.  This architecture allows
multiple trainees to execute independent sub-exercises in non-overlapping battlespaces
without incurring the massive latency of tearing down and re-initializing the global
simulation.  Stories are **ephemeral**: their recordings are saved to fast local disk,
replayed for immediate trainee feedback, and then explicitly deleted (`ForgetStory`).

**Key architectural properties:**
- Multiple stories can run simultaneously in the same ECS world with full isolation.
- The global simulation clock is never paused for story management; only the story's
  specific entities can be "frozen" by stripping actor capability flags.
- Replayed story entities appear as **holograms** (tagged `StoryReplayTag`) alongside
  live actors; AI/physics systems skip them entirely.

### 10.1 ECS Components (NEW — FDP.Toolkit)

```csharp
// Added to FDP.Toolkit.Behavior (or Bagira.IG/Bagira.SimHost component registry)

[ComponentId(GlobalComponentIds.StoryTag)]        // NEW id needed
public struct StoryTag
{
    public Guid StoryId;
}

[ComponentId(GlobalComponentIds.StoryReplayTag)]  // NEW id needed
public struct StoryReplayTag
{
    public Guid StoryId;
    public int  OriginalEntityId;   // For debug/inspection
}
```

**Isolation rules:**
- Every entity spawned for a specific story receives `StoryTag { StoryId = storyGuid }`.
- **Inheritance:** Systems that spawn child entities (e.g. a soldier firing a bullet)
  **must** propagate the parent's `StoryTag` to the child.  Failing to do so creates
  "orphan" entities that bleed into the global recorder.
- **Event isolation:** Transient combat events (`FireInteractionEvent`, `HitEvent`,
  formation commands, etc.) are augmented with the `StoryId` at their origin site.
  Evaluator and scoring modules validate this ID, safely ignoring events that belong to
  other concurrent stories, preventing cross-story scoring contamination.
- **Simulated pauses:** If an instructor pauses a story while the global clock continues,
  the orchestrator strips `ActorCapabilities.CanMove` and `CanShoot` from the story's
  entities.  The `StoryRecorder` continues logging the frozen state with advancing UTC
  timestamps for the duration of the pause.

### 10.2 Architecture

```
┌───────────────────────────────────────────────────────────────────────────┐
│  LIVE SIMULATION (RunningLive)                                             │
│                                                                            │
│  Global ECS World                                                          │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  Entity 1  (global terrain NPC — no StoryTag)                       │  │
│  │  Entity 2  (global terrain NPC — no StoryTag)                       │  │
│  │  Entity 10 [StoryTag: A1]  tank in Story A1                        │  │
│  │  Entity 11 [StoryTag: A1]  missile from Story A1 → chases E10     │  │
│  │  Entity 20 [StoryTag: B7]  helicopter in Story B7                  │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                            │
│  StoryRecorder(A1) → filters Query().With<StoryTag(A1)>()                 │
│  StoryRecorder(B7) → filters Query().With<StoryTag(B7)>()                 │
│  GlobalRecorder    → records everything (or everything above MinId)       │
└───────────────────────────────────────────────────────────────────────────┘
```

### 10.3 Filtered AsyncRecorder

The current `AsyncRecorder` uses `RecorderSystem.MinRecordableId` as the only filter.
A new `EntityQuery`-based predicate must be added:

```csharp
// RecorderSystem.cs addition
public Predicate<int>? EntityFilter { get; set; } = null;
// In RecordDeltaFrame: skip entity if Filter != null && !Filter(entityId)

// Story recorder setup:
var storyFilter = new StoryEntityFilter(storyId, repo);
storyRecorder.RecorderSystem.EntityFilter = storyFilter.Matches;
```

### 10.4 StoryPlaybackController — Entity Remapping

Global replay blasts raw `NativeChunkTable` memory (`memcpy`). This is safe because
global replay **owns the entire ECS** during `RunningReplay`.

Story replay is different: the global world is still **live**. We cannot overwrite
live entity memory. Instead:

```
Recording File: entity 10 → Position, Rotation, Damage
                entity 11 → Position, TargetId=10

StoryPlaybackController:
  1. Allocate new Ghost Entity 8000 → copy data for recorded entity 10
  2. Allocate new Ghost Entity 8001 → copy data for recorded entity 11
  3. Byte-patch entity 8001's TargetId field: 10 → 8000
     (using ComponentPatchMap for the missile component struct)
  4. Add StoryReplayTag to 8000, 8001

AI/Physics systems: skip any entity with StoryReplayTag
```

### 10.5 ComponentPatchMap (Entity Reference Patching)

The `ComponentPatchMap` makes Story replay viable in an unmanaged ECS memory layout
without any heap allocation on the hot path.  It uses a two-phase approach: **startup-time
reflection** to cache byte offsets, and a **zero-allocation raw-byte patching loop** at
replay time.

#### 10.5.1 Startup: Byte-Offset Caching (Unmanaged Structs)

```csharp
// Location: FDP/Kernel/Fdp.Kernel/Orchestration/ComponentPatchMap.cs  (new)

/// <summary>
/// Immutable map of byte offsets at which Entity-typed fields reside inside
/// a specific unmanaged component struct.  Built once at startup via
/// reflection; never allocated again on the hot path.
/// </summary>
public sealed class ComponentPatchMap
{
    public int   ComponentTypeId;

    /// <summary>
    /// Byte offsets of every Entity (int) field within the raw struct layout.
    /// Populated from Marshal.OffsetOf() at component registration time.
    /// Includes offsets for NetworkIdentity fields (also int entity handles)
    /// that cross-reference other entities.
    /// </summary>
    public int[] EntityFieldByteOffsets;  // never null; empty if no Entity fields
}

// ─── Population in ComponentTypeRegistry (startup only) ──────────────────────

private static ComponentPatchMap BuildPatchMap<T>(int typeId) where T : unmanaged
{
    var offsets = typeof(T)
        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(f => f.FieldType == typeof(Entity) || f.FieldType == typeof(NetworkIdentity))
        .Select(f => (int)Marshal.OffsetOf<T>(f.Name))
        .ToArray();

    return new ComponentPatchMap { ComponentTypeId = typeId,
                                   EntityFieldByteOffsets = offsets };
}
// Called once per component type during app startup.  Zero cost at runtime.
```

#### 10.5.2 Hot Path: Zero-Allocation Raw Byte Patching Loop

When `StoryPlaybackController` allocates a Ghost entity and copies a component from the
recorded buffer, it calls `PatchEntityRefs()` before writing the component into the live
`NativeChunkTable`.  The method iterates only the pre-computed byte-offset array; no
reflection, no boxing, no LINQ:

```csharp
// Called for every component on every replayed entity — must be zero-allocation.
public static unsafe void PatchEntityRefs(
    byte*            componentDataPtr,   // raw pointer to the component bytes being written
    ComponentPatchMap map,
    NativeHashMap<int, int> oldToNewId)  // recorded entity ID → new Ghost entity ID
{
    foreach (int offset in map.EntityFieldByteOffsets)
    {
        int* fieldPtr = (int*)(componentDataPtr + offset);
        int  oldId    = *fieldPtr;

        // Only patch if the referenced entity was part of the same Story recording.
        // Cross-story or global-entity references are left intact.
        if (oldToNewId.TryGetValue(oldId, out int newId))
            *fieldPtr = newId;
    }
}
// NativeHashMap<int,int> is a Burst-compatible, unmanaged structure —
// zero GC pressure.  The map is built once per PlaybackController.Open() call.
```

`SetComponentRaw(ghostEntityId, typeId, componentDataPtr, sizeInBytes)` is then called on
the patched bytes.  The `NativeChunkTable` copy happens from already-patched memory, so
there is never a post-write fixup pass.

#### 10.5.3 `IEntityRefPatchable` — Complex Unmanaged and Managed Components

The automated byte-offset scanner (§10.5.1) has two critical blind spots:

1. **Unmanaged structs with inline arrays or logical counts** — `FormationRoster` uses
   a `fixed long MemberEntities[3]` buffer reinterpreted as `Entity` via unsafe casting.
   Reflection looking for `typeof(Entity)` fields will **completely miss** this `long[]`.
   Even if the scanner could find them, blindly patching all capacity slots would mutate
   uninitialised (garbage) memory beyond the logical `Count` boundary.  C# 12
   `[InlineArray]` hides its elements behind a single compiler-generated `_element`
   backing field — equally invisible to standard reflection.

2. **Managed (class) ECS components** — fields of type `List<Entity>` or other managed
   reference containers cannot be addressed as raw byte offsets.

For both categories, the component must implement `IEntityRefPatchable`.  This delegates
the patching responsibility to the component itself, which understands exactly how many
real elements are present and how the memory is structured.

**The Interface:**

```csharp
// Location: FDP/Kernel/Fdp.Kernel/Orchestration/IEntityRefPatchable.cs  (new)

/// <summary>
/// Implemented by ECS components that contain Entity-typed fields which cannot
/// be discovered or safely patched by the automated byte-offset scanner.
/// Required for:
///  - Unmanaged structs with fixed buffers / [InlineArray] fields holding entity IDs.
///  - Unmanaged structs where the logical element count is less than physical capacity.
///  - Managed (class) components with Entity fields in managed object graphs.
/// </summary>
public interface IEntityRefPatchable
{
    /// <summary>
    /// Replace all internal entity ID references using the provided remap table.
    /// Only iterate up to the logical Count — never patch dead/uninitialised capacity slots.
    /// </summary>
    void PatchEntityRefs(ref EntityRemapTable remapTable);
}
```

**Zero-Allocation Dispatch (avoiding boxing):**  
Calling `((IEntityRefPatchable)unmanagedStruct).PatchEntityRefs(...)` would box the
struct, allocating on the heap.  To bypass this, `ComponentTypeRegistry` compiles a
strongly-typed generic delegate at startup using the same JIT expression-tree pattern
as `FdpAutoSerializer` and `UnsafeShim`:

```csharp
// Internal generic delegate cached per component type in ComponentTypeRegistry
private delegate void PatchDelegate<T>(ref T component, ref EntityRemapTable map);
// Compiled once via Expression.Lambda at startup — zero cost at runtime.
```

**Unmanaged struct example (`FormationRoster`):**

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.FormationRoster)]
public unsafe struct FormationRoster : IEntityRefPatchable
{
    public int           Count;         // logical member count
    public int           TemplateId;
    public FormationType Type;
    public FormationParams Params;

    public fixed long  MemberEntities[3];  // reinterpreted as Entity (obfuscated fixed buffer)
    public fixed ushort SlotIndices[3];

    public void PatchEntityRefs(ref EntityRemapTable remapTable)
    {
        // Iterate only up to logical Count — never touch uninitialised capacity slots
        for (int i = 0; i < Count; i++)
        {
            Entity oldEntity = this.GetMember(i);
            if (remapTable.TryRemap(oldEntity, out Entity newEntity))
                this.SetMember(i, newEntity);
        }
    }
}
```

**Managed component example (`MissileAIState`):**

```csharp
public class MissileAIState : EcsComponent, IEntityRefPatchable
{
    public int    TargetEntityId;
    public Entity[] CollidedWith;

    public void PatchEntityRefs(ref EntityRemapTable remapTable)
    {
        if (remapTable.TryRemap(TargetEntityId, out int newTarget))
            TargetEntityId = newTarget;
        for (int i = 0; i < CollidedWith.Length; i++)
            if (remapTable.TryRemap(CollidedWith[i].Id, out int newId))
                CollidedWith[i] = new Entity(newId);
    }
}
```

**Registration Enforcement:**  
When `ComponentTypeRegistry.Register<T>()` encounters a type:
- Managed type with `Entity`/`NetworkIdentity` fields but **without** `IEntityRefPatchable`
  → throws `NotSupportedException` at startup.
- Unmanaged type where the scanner finds offsets → automated path (§10.5.2).
- Either type with both offsets and `IEntityRefPatchable` → interface wins; byte-offset
  scanner result is discarded for that type.

`StoryPlaybackController` dispatches on component kind per entity per replayed frame:
- Flat unmanaged, no `IEntityRefPatchable` → `PatchEntityRefs(byte*, map, oldToNewId)` (§10.5.2)
- Implements `IEntityRefPatchable` (managed or complex unmanaged) → compiled `PatchDelegate<T>` (this section)
- Managed without Entity fields → skip

### 10.6 Story Lifecycle Sequence

```mermaid
sequenceDiagram
    participant IOS
    participant Master as SystemMasterModule
    participant Slave as SystemSlaveModule
    participant ECS

    IOS->>Master: SysOpRequest(ManageStory, StartStory A1)
    Master->>Slave: NodeOpCommand(LoadStoryAssets, A1)
    Slave->>Master: NodeOpStatus(InProgress)
    Note over Slave: BG Task: load models/navmesh for Story A1
    Slave->>Master: NodeOpStatus(Success)

    Master->>Slave: NodeOpCommand(StartStory, A1, DrillId)
    Slave->>ECS: Spawn entities with StoryTag(A1)
    Slave->>ECS: Create StoryRecorder(A1) → /archives/{DrillId}/stories/A1_node{N}.fdp

    Note over ECS: Live simulation continues (global clock ticks)

    IOS->>Master: SysOpRequest(ManageStory, StopStory A1)
    Master->>Slave: NodeOpCommand(StopStory, A1)
    Slave->>ECS: Flush StoryRecorder(A1). Destroy StoryTag(A1) entities.
    Note over Slave: Recording retained at /archives/{DrillId}/stories/A1_node{N}.fdp

    IOS->>Master: SysOpRequest(ManageStory, ReplayStory A1)
    Master->>Slave: NodeOpCommand(ReplayStory, A1)
    Slave->>ECS: StoryPlaybackController(A1): allocate Ghost entities
    Note over ECS: Ghost entities visible alongside live world

    IOS->>Master: SysOpRequest(ManageStory, ForgetStory A1)
    Master->>Slave: NodeOpCommand(ForgetStory, A1)
    Slave->>ECS: Destroy StoryReplayTag(A1) entities
    Slave->>ECS: Delete /archives/{DrillId}/stories/A1_node{N}.fdp immediately

    Note over Master: If exercise ends (UnloadingLive) while a story has never<br/>been stopped (StopStory not called), the partial recording is<br/>auto-deleted. Cleanup is the node's responsibility, not Orchestrator's.
```

---

## 11. Battlespaces

A **battlespace** is a named high-resolution area in the world, defined by a 2D polygon
of `GeoPosition` vertices. Loading its high-res terrain/navmesh may take seconds.

### 11.1 Staged Loading (2PC)

```
Phase 1 — PREPARE:
  NodeOpCommand(PrepareBattlespace, { id, bounds, dataPath })
  Node: Background Task loads NavMesh + high-res terrain into StagedAssetPayload
        (completely disconnected from active pointers)
  Node: reply NodeOpStatus(Success) when loaded

Phase 2 — COMMIT:
  NodeOpCommand(CommitBattlespace, { id })
  Node: At BeforeSync, push local ECS event CmdSwapBattlespace { id }
  Next Frame: PhysicsSystem and RenderSystem consume CmdSwapBattlespace,
              swap active pointer from old to new terrain
  (Old terrain pointer released for GC / NativeMemoryAllocator.Free)

ABORT (if any node fails Prepare):
  NodeOpCommand(AbortTransaction, txId)
  Node: Free StagedAssetPayload — no ECS mutation occurred
```

### 11.2 Battlespace DDS Message

```csharp
// In bdc-sst-orchestration IDL or a dedicated bdc-sst-terrain IDL
[DdsStruct]
public struct BattlespaceSpec
{
    public string     BattlespaceId;
    public GeoPosition[] Bounds;       // 2D polygon vertices
    public string     DataPath;        // Path to high-res terrain data
}
```

---

## 12. Scenario Editing & Management

### 12.1 ESM Integration

Scenario editing is a **distributed, collaborative** session governed by the ESM.
The cluster transitions into `LoadingEdit` to load static assets (base terrain,
boundaries) and into `RunningEdit` where **global simulation time is completely frozen
(`TimeScale = 0.0`)**.  AI behaviour trees and kinematic state machines do not tick;
only the `NetworkSpawningSystem` remains active to replicate entity placements across
the cluster via DDS.

### 12.2 Forward-Compatible JSON Serialization

Scenario persistence uses a different strategy from binary RAM snapshots:

| Mechanism | Use Case | Format |
|-----------|----------|--------|
| `EntityRepository.SyncFrom()` | Dry Run / Checkpoint | Binary unmanaged dump — fast but not portable |
| Scenario save | Long-term named scenarios | **JSON** with versioned schema — backwards/forwards compatible |

Nodes serialize only non-default entity overrides and domain-specific schematic
instructions needed to reconstruct the world (e.g. entity placement, attributes,
formation configurations) — not the full raw ECS chunk table.

### 12.3 Scenario Creation vs Loading via `SysOpRequest` Payload

Both creating a new scenario and editing an existing one share a single unified ESM
transition request.  The orchestrator stays **agnostic** to the content distinction;
differentiation happens entirely inside `PayloadJson`.

**Case A — Create New Scenario:**

```json
{
  "TargetState": "LoadingEdit",
  "ScenarioId": null,
  "IsNewScenario": true,
  "BaseTerrain": "Desert_01",
  "Battlespaces": [
    { "Id": "Zone_A", "Bounds": [ ... ] }
  ]
}
```

**Case B — Edit Existing Scenario (with optional overrides):**

```json
{
  "TargetState": "LoadingEdit",
  "ScenarioId": "Scenario_Alpha",
  "IsNewScenario": false,
  "Overrides": {
    "Weather": "HeavyRain",
    "TimeOfDay": "0400Z"
  }
}
```

**Processing separation of concerns:**
- `SystemMasterModule` routes the payload opaquely — validates the ESM transition and
  threads `PayloadJson` into every `NodeOpCommand` without inspecting its contents.
- `TransitionPlanner` checks if `ScenarioId` is present: if yes, it triggers the
  **Storage Gateway pre-fetch** before entering `LoadingEdit` (see §12.5); if
  `IsNewScenario = true`, the pre-fetch step is skipped entirely.
- `EditLoadEsmHandler` on each leaf node deserializes the JSON and either bootstraps
  a blank world from `BaseTerrain` or loads the pre-fetched files and applies `Overrides`.

> **Open/Closed:** Adding a new dynamic override (e.g. `"CyberJammingLevel"`) requires
> no changes to the DDS schema, `SystemMasterModule`, or `TransitionPlanner` — only the
> relevant domain handler needs updating.

### 12.4 `SaveScenario` — SMB Pull Gateway (Scatter, Manifest, Pull)

**Problem:** If 50+ nodes simultaneously write scenario files to a central Windows NAS,
the OS-imposed inbound SMB connection limit (~20 on client SKUs) causes cascading
connection failures.  The platform uses the **SMB Pull Gateway Pattern** to eliminate
this: a single `StorageGatewayModule` (co-located with the Master) pulls data from
leaf nodes using strictly *outbound* connections.

**Phase 1 — Local Serialization (Scatter):**
- IOS fires `SysOpRequest(SaveScenario, "Scenario_Alpha")`
- Master broadcasts `NodeOpCommand(SerializeLocal, "Scenario_Alpha")`
- Nodes independently serialize to fast local SSD: `C:\FDP_Temp\Scenario_Alpha\`

**Phase 2 — Opaque UNC Manifest (Gather):**  
Each node embeds its file locations in the `NodeOpStatus(Success)` payload:

```json
{
  "Manifest": [
    {
      "SourceUnc": "\\\\Node_100\\FDP_Temp\\Scenario_Alpha\\map_data.json",
      "RelativeDest": "Node_100/map_data.json"
    }
  ]
}
```
The Master treats these as **opaque byte streams** — it has zero knowledge of the
JSON format inside.

**Phase 3 — Gateway Pull:**  
The `StorageGatewayModule` opens one outbound connection to the NAS
(`\\Central_NAS\Scenarios\Scenario_Alpha\`) and *outbound* reads from each leaf node
under a controlled `Parallel.ForEach(MaxDegreeOfParallelism = 8)`.  The NAS sees
exactly **1 inbound connection**.

**Phase 4 — Cleanup & Commit:**  
Master broadcasts `NodeOpCommand(CleanupTempFiles)`.  Sends `SysOpStatus(Success)` to IOS.

### 12.5 `LoadScenario` — Storage Gateway Pre-Fetch

Nodes must never stream scenario files from a remote NAS during the 60 Hz ECS tick.
A pre-fetch barrier decouples the file transfer from scenario parsing:

**Phase 1 — Pre-Fetch Barrier:**  
`TransitionPlanner` detects a non-null `ScenarioId`.  Before entering `LoadingEdit`,
it commands the `StorageGatewayModule` to distribute files.

**Phase 2 — Gateway Push:**  
The Gateway reads the required scenario files from the NAS using its single outbound
connection, then pushes them into each leaf node's `C:\FDP_Temp\` via parallel outbound
SMB writes.  Master publishes `SysOpStatus(InProgress, "Pre-fetching scenario…")`.

**Phase 3 — Local Execution (ESM Transition):**  
Only after all pre-fetch acks are received does the Master broadcast
`NodeOpCommand(PrepareState, LoadingEdit)`.  Nodes parse the local files without any
network I/O — the ECS main loop is never blocked.

### 12.6 `StorageGatewayModule`

Single component co-located with `SystemMasterModule` that owns all bulk file
movement for Scenarios, Checkpoints, and Archive Export/Import:

```
StorageGatewayModule responsibilities:
  ├─ Receives a manifest list from the Master after all node ACKs
  ├─ Opens exactly ONE outbound SMB connection to the central NAS
  ├─ Performs parallel outbound reads from source UNCs (leaf nodes) up to
  │  MaxDegreeOfParallelism to saturate bandwidth without triggering inbound limits
  ├─ Streams bytes into the single NAS connection, routed by RelativeDest path
  └─ Reports completion back to SystemMasterModule
```

**Why not DDS for file streaming?** DDS (CycloneDDS) is optimised for state
and real-time events — piping gigabytes of opaque file bytes through it pollutes the
real-time data plane with bulk transfers.  Standard SMB on a local switch easily
delivers 1+ Gbps without DDS queue back-pressure or fragmentation overhead.

---

## 13. Archive Export / Import — Storage Gateway Pattern

**Problem context:**  
Moving exercise recordings (`.fdprec` files, `.meta.json` manifests) to cold storage
presents the same "Thundering Herd" hazard as Scenario management.  The superseded
Token-Bucket Upload approach was insufficient: even with serialized tokens, individual
SMB connections linger in TCP `TIME_WAIT` state after closure, causing the NAS's
*inbound* connection limit to be exhausted over successive exercises.  The Storage
Gateway Pattern (§12.6) unifies all bulk file operations under a single component.

### 13.1 Archive Export (Gathering to Cold Storage)

**Phase 1 — Local Finalization (Scatter):**  
IOS requests archive export.  Master broadcasts `NodeOpCommand(ExportArchive, DrillId)`.
Each `SystemSlaveModule` commands its `AsyncRecorder` to flush buffers, close file
handles, and write the `.meta.json` manifest to local SSD.

**Phase 2 — Opaque UNC Manifest (Gather):**  
Each node replies `NodeOpStatus(Success)` with its manifest embedded in `ResultJson`:
```json
{
  "Manifest": [
    {
      "SourceUnc": "\\\\Node_100\\FDP_Temp\\Drill_999\\node_100.fdprec",
      "RelativeDest": "Drill_999/node_100.fdprec"
    },
    {
      "SourceUnc": "\\\\Node_100\\FDP_Temp\\Drill_999\\node_100.meta.json",
      "RelativeDest": "Drill_999/node_100.meta.json"
    }
  ]
}
```

**Phase 3 — Gateway Pull:**  
Master passes all manifests to `StorageGatewayModule`.  Gateway opens one outbound
connection to `\\Central_NAS\Archives\` and pulls files in parallel from leaf node
UNCs.  The NAS sees exactly 1 inbound connection.

**Phase 4 — Commit & Cleanup:**  
Gateway confirms all bytes written.  Master sends `NodeOpCommand(CleanupTempFiles)`,
then `SysOpStatus(Success)` to IOS.

### 13.2 Archive Import / Restore (Pre-Fetching for Replay)

**Phase 1 — Pre-Fetch Barrier:**  
IOS requests `LoadingReplay` for a specific `DrillId`.  `TransitionPlanner` intercepts
before the ESM enters `LoadingReplay` and commands the `StorageGatewayModule` to
distribute the archive.

**Phase 2 — Gateway Push:**  
Gateway reads `.fdprec` and `.meta.json` for the DrillId from the NAS and pushes them
to each respective node's `C:\FDP_Temp\` directory via parallel outbound SMB.  Master
publishes `SysOpStatus(InProgress, "Pre-fetching recording…")`.

**Phase 3 — Local Initialization:**  
Only after all pre-fetch acks are received does the Master broadcast
`NodeOpCommand(PrepareState, LoadingReplay)`.  `ReplayLoadEsmHandler` on each node
opens `PlaybackController` against the local file.  `SchemaValidator` runs against
`.meta.json` to guard against ECS struct layout drift.

**Phase 4 — Commit → `RunningReplay`.**

Additionally the Gateway injects the `MaxNetworkId` metadata into the 2PC ACK payload
during `LoadingReplay` so the Master can reset the `DdsIdAllocatorServer` (§5.7).

### 13.3 Recording Folder Structure

```
/archives/
└── {DrillId}/
    ├── node_100_SimHost.fdprec
    ├── node_100_SimHost.meta.json
    ├── node_200_IG.fdprec
    ├── node_200_IG.meta.json
    ├── checkpoints/
    │   ├── {CheckpointId}_node_100.fdp
    │   ├── {CheckpointId}_node_100.dds          ← in-flight DDS supplement
    │   └── {CheckpointId}_node_200.fdp
    └── drill_manifest.json     ← DrillId, timestamps, node list, ESM lifecycle log
```

---

## 14. Deterministic Batch Runs

Existing infrastructure (`SteppedMasterController`, `SteppedSlaveController`,
`FrameOrderDescriptor`, `FrameAckDescriptor`) supports this.

### 13.1 Integration with SysOp

The `SystemMasterModule` **intercepts** all heavy `SysOpRequests` during deterministic
mode by signalling the `SteppedMasterController` to **halt frame emission**.

```
SysOp Intercept (Control Plane Superiority):
  1. SysOpRequest arrives (e.g., LoadBattlespace)
  2. SystemMasterModule → SteppedMasterController.HaltEmission()
  3. Slaves complete their current frame and freeze (no more FrameOrder received)
  4. 2PC executes safely (no concurrent ECS mutations)
  5. On success/abort → SteppedMasterController.ResumeEmission()
  6. Deterministic stepping resumes from next frame
```

### 13.2 LoadingLive with Deterministic Mode

```json
// SysOpRequest.PayloadJson
{
  "TargetState": "LoadingLive",
  "ScenarioId": "Desert_01",
  "TimeMode": "Deterministic",
  "FixedDeltaSeconds": 0.016667
}
```

---

## 15. Key 12-Step Exercise Sequence Flow

Below is the full IOS → Master → Slaves sequence for the canonical exercise scenario
described in the design talk.

```mermaid
sequenceDiagram
    autonumber
    participant IOS
    participant Master as SystemMasterModule
    participant Slave as SystemSlaveModule (All Nodes)
    participant ECS as NativeChunkTable / Recorder / Playback

    Note over IOS,ECS: ══ 1. STANDBY ══
    Master->>Master: SystemState(Standby). Monitor Heartbeats.

    Note over IOS,ECS: ══ 2. START EDITING ══
    IOS->>Master: SysOpRequest(TransitionState → LoadingEdit)
    Master->>Slave: NodeOpCommand(PrepareState, LoadingEdit)
    Slave->>ECS: BG Load terrain/static assets
    Slave->>Master: NodeOpStatus(Success)
    Master->>Master: Commit → RunningEdit (time frozen)

    Note over IOS,ECS: ══ 3. DRY RUN ══
    IOS->>Master: SysOpRequest(TransitionState → LoadingDryRun)
    Master->>Slave: NodeOpCommand(TakeSnapshot)
    Slave->>ECS: snap = new EntityRepository(schema)
    Slave->>ECS: snap.SyncFrom(liveRepo)  [~2ms, time stays frozen in edit]
    Slave->>Master: NodeOpStatus(Success)
    Master->>Master: Commit → RunningDryRun (simulation ticks)

    Note over IOS,ECS: ══ 4. STOP DRY RUN + SAVE ══
    IOS->>Master: SysOpRequest(TransitionState → UnloadingDryRun)
    Master->>Slave: NodeOpCommand(RestoreSnapshot)
    Slave->>ECS: liveRepo.SyncFrom(snap)  [restore from RAM snapshot]
    Master->>Master: Commit → RunningEdit (rewound)
    IOS->>Master: SysOpRequest(SaveScenario)
    Slave->>ECS: Serialize entity overrides → Scenario_Alpha.json

    Note over IOS,ECS: ══ 5. LOAD LIVE (RECORDING) ══
    IOS->>Master: SysOpRequest(TransitionState → LoadingLive)
    Master->>Master: Generate DrillId = Drill_999
    Master->>Slave: NodeOpCommand(PrepareLive, DrillId=Drill_999)
    Slave->>ECS: Init AsyncRecorder → /archives/Drill_999/node_N.fdp
    Slave->>Master: NodeOpStatus(Success)
    Master->>Master: Commit → RunningLive

    Note over IOS,ECS: ══ 6. CHECKPOINT (non-blocking) ══
    IOS->>Master: SysOpRequest(TakeCheckpoint, "Bug01")
    Master->>Slave: NodeOpCommand(TakeSnapshot, "Bug01")
    Slave->>ECS: snap = new EntityRepository(schema)
    Slave->>ECS: snap.SyncFrom(liveRepo)  [~2ms, sim keeps running]
    Slave->>Master: NodeOpStatus(Success)
    Note over Slave: BG Thread: watch DDS ingress 50ms → .dds supplement<br/>BG Thread: LZ4 compress snap → /checkpoints/Bug01_nodeN.fdp

    Note over IOS,ECS: ══ 7. FINISH LIVE ══
    IOS->>Master: SysOpRequest(TransitionState → UnloadingLive)
    Master->>Slave: NodeOpCommand(FinalizeLive)
    Slave->>ECS: Flush AsyncRecorder. Close .fdp.
    Master->>Master: Commit → Standby

    Note over IOS,ECS: ══ 8. INIT REPLAY ══
    IOS->>Master: SysOpRequest(TransitionState → LoadingReplay, DrillId=Drill_999)
    Master->>Slave: NodeOpCommand(PrepareReplay, Drill_999)
    Slave->>ECS: Open PlaybackController(/archives/Drill_999/node_N.fdp)
    Slave->>Master: NodeOpStatus(Success)
    Master->>Master: Commit → RunningReplay (playhead @ T=0, paused)

    Note over IOS,ECS: ══ 9. PLAY + SEEK ══
    IOS->>Master: Click Play → ReplayMasterModule advances playhead
    Master->>Slave: TimePulseDescriptor(MasterWallTicks=T1)
    Slave->>ECS: PlaybackController.SeekToWallClockTicks(T1)
    IOS->>Master: SysOpRequest(ReplaySeek, T=15min)
    Master->>Slave: NodeOpCommand(ReplaySeek, T15)
    Slave->>ECS: PlaybackController.SeekToWallClockTicks(T15) ~10ms
    Slave->>Master: NodeOpStatus(Success)

    Note over IOS,ECS: ══ 10. LIVE-FROM-REPLAY ══
    IOS->>Master: SysOpRequest(TransitionState → LoadingLive)
    Master->>Master: New DrillId = Drill_999_Branch1
    Master->>Slave: NodeOpCommand(PrepareLiveFromReplay, Branch1)
    Slave->>ECS: Dispose PlaybackController. Keep ECS state. Init new AsyncRecorder.
    Master->>Master: Commit → RunningLive (from replay state)

    Note over IOS,ECS: ══ 11. FINISH BRANCHED LIVE ══
    IOS->>Master: SysOpRequest(TransitionState → UnloadingLive)
    Master->>Slave: NodeOpCommand(FinalizeLive)
    Slave->>ECS: Flush branched recording. Close .fdp.
    Master->>Master: Commit → Standby

    Note over IOS,ECS: ══ 12. EDIT FROM CHECKPOINT ══
    IOS->>Master: SysOpRequest(TransitionState → LoadingEdit, Checkpoint=Bug01)
    Master->>Slave: NodeOpCommand(PrepareEdit, Checkpoint_Bug01)
    Slave->>ECS: Load checkpoint_Bug01_nodeN.fdp → PlaybackSystem.ApplyFrame() → repo
    Slave->>Master: NodeOpStatus(Success)
    Master->>Master: Commit → RunningEdit
```

---

## 16. Required Code Changes Summary

### 16.1 New Files

| File | Project | Description |
|------|---------|-------------|
| `Bagira.DDS.DataModel/Orchestration/OrchestrationMessages.cs` | `Bagira.DDS.DataModel` | All new DDS topics from §2 (incl. `OrchestratorContextTopic`) |
| `Bagira.Orchestrator/SystemMasterModule.cs` | `Bagira.Orchestrator` (new project) | Master orchestrator — runs as separate process via `Bagira.Runner`; hosts `DdsIdAllocatorServer` (§5.7) |
| `Bagira.Orchestrator/TransitionPlanner.cs` | `Bagira.Orchestrator` | BFS-based directed ESM graph (§5.5.2); resolves any target state into `Queue<ISysOpStep>`; appends `OperationStep` entries when hint payload present; pre-fetch barrier injection for Scenario/Archive loads (§12.5, §13.2) |
| `Bagira.Orchestrator/StorageGatewayModule.cs` | `Bagira.Orchestrator` | SMB Pull Gateway: receives UNC manifests from Master after node ACKs, pulls files from leaf nodes via parallel outbound SMB, writes to central NAS via single outbound connection; used for SaveScenario, LoadScenario, ExportArchive, ImportArchive, CollectCheckpoint (§12.4, §12.5, §13) |
| `Bagira.Orchestrator/ReplayMasterModule.cs` | `Bagira.Orchestrator` | Replay playhead controller |
| `Bagira.IG/Modules/Orchestration/SystemSlaveModule.cs` | `Bagira.IG` | IG slave (FDP mode) |
| `Bagira.SimHost/Modules/Orchestration/SystemSlaveModule.cs` | `Bagira.SimHost` | SimHost slave (FDP mode) |
| `Bagira.IOS/Orchestration/IosSystemSlaveModule.cs` | `Bagira.IOS` | IOS slave (no-ECS lightweight variant) |
| `FDP/Kernel/Fdp.Kernel/FlightRecorder/StoryPlaybackController.cs` | `Fdp.Kernel` | Story entity-remapping playback |
| `FDP/Kernel/Fdp.Kernel/Orchestration/ComponentPatchMap.cs` | `Fdp.Kernel` | Entity ref offset patching (startup reflection, zero-alloc hot path) |
| `FDP/Kernel/Fdp.Kernel/Orchestration/CheckpointIOWorker.cs` | `Fdp.Kernel` | Serialized background I/O queue for checkpoint writes; drains one snapshot at a time to prevent disk thrashing; supports concurrent transactions; exposes `DrainAsync()` for `UnloadingLive` teardown barrier (§9.1) |
| `FDP/ModuleHost/ModuleHost.Core/Abstractions/IEsmHandler.cs` | `ModuleHost.Core` | ESM handler interface |
| `FDP/Toolkits/FDP.Toolkit.Time/ITimeController.cs` | `FDP.Toolkit.Time` | `ITimeController` interface: `Update()`, `SetTimeScale()`, `GetMode()`, `SeedState()` (§5.6.1) |
| `FDP/Toolkits/FDP.Toolkit.Time/SwitchableTimeController.cs` | `FDP.Toolkit.Time` | Proxy wrapper; public API is `SwitchTo(ITimeController)`; called only by coordinator layer (§5.6.2) |
| `FDP/Toolkits/FDP.Toolkit.Time/DistributedTimeCoordinator.cs` | `FDP.Toolkit.Time` | Master-side coordinator: computes BarrierFrame, publishes `SwitchTimeModeEvent`, calls `SwitchTo()` at barrier (§5.6.5) |
| `FDP/Toolkits/FDP.Toolkit.Time/SlaveTimeModeListener.cs` | `FDP.Toolkit.Time` | Slave-side listener: receives `SwitchTimeModeEvent`, waits for BarrierFrame, calls `SwitchTo()` (§5.6.5) |
| `FDP/Kernel/Fdp.Kernel/Events/EsmStateChangedEvent.cs` | `Fdp.Kernel` | Internal FdpEventBus event for ESM transitions |
| `FDP/Kernel/Fdp.Kernel/Orchestration/IRecordReplayController.cs` | `Fdp.Kernel` | Generic recording/playback abstraction (§8.7) |
| `FDP/Kernel/Fdp.Kernel/Orchestration/IEntityRefPatchable.cs` | `Fdp.Kernel` | Interface for **both** complex unmanaged structs (fixed buffers, `[InlineArray]`, logical-count arrays) and managed components containing `Entity`/`NetworkIdentity` fields; prevents over-patching uninitialised capacity slots (§10.5.3) |
| `Bagira.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` | `Bagira.SimHost` | FDP ECS adapter (§8.8); wraps `AsyncRecorder` + `PlaybackController`; registered with `SystemSlaveModule`; exposes `RecordingMetadata.MaxNetworkId` in pre-replay ACK payload |
| `FDP/ModuleHost/ModuleHost.Core/Scheduling/NetworkLifecycleSystemGroup.cs` | `ModuleHost.Core` | Concrete `ISystemGroup` with `bool Enabled` toggle (§8.5) |
| `Bagira.SimHost/Modules/Orchestration/Handlers/LiveLoadEsmHandler.cs` | `Bagira.SimHost` | Scenario load + recorder init |
| `Bagira.SimHost/Modules/Orchestration/Handlers/ReplayLoadEsmHandler.cs` | `Bagira.SimHost` | PlaybackController init; publishes `MaxNetworkId` in ACK |
| `Bagira.SimHost/Modules/Orchestration/Handlers/EditLoadEsmHandler.cs` | `Bagira.SimHost` | Scenario editing handler; parses `IsNewScenario` / `ScenarioId` / `Overrides` from PayloadJson; bootstraps blank world or loads pre-fetched JSON files (§12.3) |
| `Bagira.SimHost/Modules/Orchestration/Handlers/CheckpointEsmHandler.cs` | `Bagira.SimHost` | Non-blocking SyncFrom snapshot; feeds `CheckpointIOWorker`; defers ACK until background write completes (§9.1) |
| `Bagira.SimHost/Modules/Orchestration/Handlers/BattlespaceEsmHandler.cs` | `Bagira.SimHost` | Staged terrain loader |

### 16.2 Modified Files

| File | Change |
|------|--------|
| `FDP/Kernel/Fdp.Kernel/FlightRecorder/RecorderSystem.cs` | Add `EntityFilter` predicate + UTC wall-clock tick (`long WallClockTicks`) to frame header; add `CaptureEventFrame(long wallClockTicks, …)` (§8.12) |
| `FDP/Kernel/Fdp.Kernel/FlightRecorder/AsyncRecorder.cs` | Thread `EntityFilter` through to `RecorderSystem`; expose `MaxNetworkId` snapshot at `FinalizeRecordingAsync` for `RecordingMetadata` (§5.7) |
| `FDP/Kernel/Fdp.Kernel/FlightRecorder/PlaybackController.cs` | Add `WallClockTicks` to `FrameMetadata`; add `SeekToWallClockTicks(EntityRepository, long)` with binary search; upgrade `SeekToTick` linear scan to binary search |
| `FDP/Kernel/Fdp.Kernel/GlobalTime.cs` | Add `long TotalWallTicks` field |
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostCreationSystem.cs` | Add `bool BypassLifecycle` property (§8.5) |
| `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` | Add `SwitchTimeModeEvent` struct; document replay semantics of `TimePulseDescriptor` fields |
| `FDP/Toolkits/FDP.Toolkit.Time/MasterTimeController.cs` | Add `SeedState(GlobalTime)` path; immediate `TimePulseDescriptor` publish on `SeedState()` / `SetTimeScale()` |
| `FDP/Toolkits/FDP.Toolkit.Time/SlaveTimeController.cs` | Add `SeedState(GlobalTime)` bypassing `JitterFilter`; expose `_virtualWallTicks` as `TotalWallTicks` |
| `Bagira.SimHost/SimHostApp.cs` | Register `SystemSlaveModule`; remove `DdsIdAllocatorServer` (moved to Orchestrator) |
| `Bagira.IG/IgApplication.cs` | Register `SystemSlaveModule` |
| `Bagira.Runner/Services/WaitingRoomCoordinator.cs` | Integrate ESM Standby entry; launch Orchestrator subprocess |

### 16.3 Batch Implementation Order

```
Batch 1: DDS Message Schema + ESM enums
         → OrchestrationMessages.cs, ESMState enum, SystemStateTopic

Batch 2: SystemMasterModule + TransitionPlanner + SystemSlaveModule skeleton
         → NodeHeartbeat loop, SysOpRequest/Status, NodeOpCommand dispatch
         → TransitionPlanner: BFS adjacency list (§5.5.2), macro-transitions, OperationStep
           Test: wild RunningLive → RunningReplay produces correct ISysOpStep queue
         → ITimeController + SwitchableTimeController proxy
         → DistributedTimeCoordinator + SlaveTimeModeListener + SwitchTimeModeEvent
         → BlitEventTranslator<SwitchTimeModeEvent> wiring
         → MasterTimeController.SeedState() + SlaveTimeController.SeedState() + JitterFilter
           Test: SeedState() bypasses PLL

Batch 3: Wall-clock timestamps in FlightRecorder + GlobalTime
         → RecorderSystem WallClockTicks + CaptureEventFrame
         → FrameMetadata.WallClockTicks + BuildFrameIndex update
         → PlaybackController.SeekToWallClockTicks() + binary search upgrade
         → GlobalTime.TotalWallTicks + MasterTimeController/SlaveTimeController population
         → Schema version bump + unit tests

Batch 4: Checkpoint Protocol + Dry Run
         → CheckpointIOWorker serialized queue (§9.1)
         → CheckpointEsmHandler: SyncFrom + enqueue + deferred ACK via Tick() monitor
         → DDS supplement capture for in-flight messages
         → Dry Run flow (SyncFrom + restore)
         Test: two overlapping TakeCheckpoint requests; verify ACKs arrive deferred

Batch 5: Live + Replay ESM Handlers + ID Authority
         → DdsIdAllocatorServer relocated to SystemMasterModule (§5.7)
         → RecordingMetadata.MaxNetworkId field in AsyncRecorder
         → ReplayLoadEsmHandler: MaxNetworkId extraction + ID allocator reset
         → IRecordReplayController interface (§8.7)
         → EcsRecordReplayController (§8.8) inc. CaptureEventFrame (§8.12)
         → IEntityRefPatchable + ComponentTypeRegistry enforcement (§10.5.3)
         → NetworkLifecycleSystemGroup + GhostCreationSystem.BypassLifecycle
         → LiveLoadEsmHandler
         → SystemSlaveModule fan-out/fan-in Task.WhenAll (§8.9)
         Test: full 8-12 step integration test

Batch 6: Stories + ComponentPatchMap
         → StoryTag, StoryReplayTag components
         → Filtered AsyncRecorder EntityFilter
         → ComponentPatchMap startup byte-offset caching (§10.5.1)
         → PatchEntityRefs() zero-alloc unsafe loop (§10.5.2)
         → IEntityRefPatchable compiled PatchDelegate<T> dispatch (§10.5.3)
         → StoryPlaybackController + Entity Remapping (NativeHashMap<int,int>)

Batch 7: Scenario Editing & Management
         → EditLoadEsmHandler: IsNewScenario / ScenarioId / Overrides parsing (§12.3)
         → StorageGatewayModule: SMB Pull Gateway (§12.4 / §12.5 / §13)
         → TransitionPlanner: pre-fetch barrier injection for LoadingEdit and LoadingReplay
         → NodeOpType additions: SerializeLocal, CleanupTempFiles (or reuse UploadChunk)
         Test: SaveScenario end-to-end; LoadScenario pre-fetch path

Batch 8: Battlespaces + Archive
         → BattlespaceEsmHandler (staged loading)
         → StorageGatewayModule: ExportArchive / ImportArchive paths (§13.1, §13.2)
```
