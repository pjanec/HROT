# CGF-1 Generalization Addendum
## Lifting DSM Orchestration into FDP.Toolkit.Orchestration

> **Companion to:** [CGF-1-DESIGN.md](./CGF-1-DESIGN.md) — this document is the design authority for Phase 4 (Generalization).  
> **Tasks:** [CGF-1-TASK-DETAIL.md §Phase 4](./CGF-1-TASK-DETAIL.md#phase-4--generalization-fdp-toolkit-orchestration) — CGF1-G0401 through CGF1-G0406.  
> **Source of this design:** [design-review-2.md](./design-review-2.md).

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Architectural Boundary Recap](#2-architectural-boundary-recap)
3. [Before / After Diagram](#3-before--after-diagram)
4. [New Project: FDP.Toolkit.Orchestration](#4-new-project-fdptoolkitorchestration)
   - [4.1 Core Interfaces](#41-core-interfaces)
   - [4.2 OrchestrationCommand and OrchestrationStatus](#42-orchestrationcommand-and-orchestrationstatus)
   - [4.3 Generic ClusterSlave](#43-generic-clustslave)
   - [4.4 ITransitionGraph and TransitionGraphBuilder](#44-itransitiongraph-and-transitiongraphbuilder)
   - [4.5 IScenarioStorageProvider](#45-iscenariосtorageprovider)
   - [4.6 IOrchestrationTransport](#46-iorchestrationtransport)
   - [4.7 Reference Handler Catalogue](#47-reference-handler-catalogue)
5. [Hrot App Layer After Migration](#5-hrot-app-layer-after-migration)
   - [5.1 DdsOrchestrationTransport](#51-ddsorchestrationtransport)
   - [5.2 LocalDiskStorageProvider](#52-localdiskstorageprovider)
   - [5.3 HrotStateGraph](#53-hrotstrategraph)
   - [5.4 NodeBootstrapper After Migration](#54-nodebootstrapper-after-migration)
6. [Project Dependency Graph](#6-project-dependency-graph)
7. [Migration Playbook](#7-migration-playbook)
8. [Files Deleted vs Retained](#8-files-deleted-vs-retained)

---

## 1. Motivation

Phases 1–3 of CGF-1 established a complete, working Drill State Machine (DSM) for the
Hrot platform — but the orchestration engine ended up scattered across three layers:

| Layer | What lives there today |
|-------|----------------------|
| `Hrot.NED` | `ClusterState` enum, `NodeOpType` enum, DDS message structs |
| `Hrot.Common.Orchestration` | `IDsmHandler`, `ITickableDsmHandler`, `ClusterStateChangedEvent`, `DryRunDsmHandler` |
| `Hrot.SimHost` / `Hrot.CGF` / `Hrot.IG` / `Hrot.ExCon` | `ClusterSlave` (4 near-identical copies), handler implementations duplicated across subsystems |
| `Hrot.Orchestrator` | `TransitionPlanner` with hardcoded `ClusterState` adjacency dict, `ClusterMaster`, `NodeRoster`, etc. |

The result: any new FDP application that wants to participate in a 2PC state machine
must copy-paste the ClusterSlave, rediscover all the async-prepare ordering subtleties,
and re-implement scenario load/prefetch handlers from scratch.

**Goal:** extract the reusable orchestration engine into `FDP.Toolkit.Orchestration`.
The Hrot app layer shrinks to:  
1. Define its state graph via `TransitionGraphBuilder`.  
2. Build `ScenarioSerializer` instances with app-specific translators.  
3. Wire the toolkit's reference handlers with constructor-injected paths, serializers, and transport.  
4. Implement `IOrchestrationTransport` using CycloneDDS (or any other transport).

Everything else — the dispatch loop, async-prepare → commit ordering, deduplication,
the BFS path planner, the 2PC handler protocol — lives in the toolkit and is shared.

---

## 2. Architectural Boundary Recap

> _FDP infrastructure (`Fdp.Kernel` and all `FDP.Toolkit.*` projects) must **never**
> reference any `Hrot.*` assembly._ This hard constraint is unchanged.

The new `FDP.Toolkit.Orchestration` project sits inside `FDP/Toolkits/` and may only
reference:
- `Fdp.Kernel`
- `FDP.Toolkit.Scenario`
- `FDP.Toolkit.Replay`
- `ModuleHost.Core`

It must not reference `CycloneDDS.Runtime`, `Hrot.NED`, or any other
`Hrot.*` project.

---

## 3. Before / After Diagram

```
BEFORE (Phases 1–3)                          AFTER (Phase 4)
─────────────────────────────────────────    ────────────────────────────────────────────
Hrot.Common
  IDsmHandler            ← interface         FDP.Toolkit.Orchestration
  ITickableDsmHandler    ← interface           IDsmHandler               ← moved here
  ClusterStateChangedEvent                         ITickableDsmHandler       ← moved here
  DryRunDsmHandler       ← handler             IOrchestrationTransport   ← new
                                               ITransitionGraph          ← new
Hrot.Orchestrator                            IScenarioStorageProvider  ← new
  TransitionPlanner      ← hardcoded adj.      OrchestrationCommand      ← new
  ClusterMaster                                  OrchestrationStatus       ← new
                                               ClusterSlave (generic)      ← one copy
Hrot.SimHost                                 TransitionPlanner (BFS)   ← moved here
  ClusterSlave (copy 1)    ← 285 lines           TransitionGraphBuilder    ← new
  Handlers/*.cs          ← 7 handlers          Handlers/
                                                 ReferencePrefetchHandler
Hrot.CGF                                       ReferenceScenarioLoadHandler
  ClusterSlave (copy 2)    ← 220 lines            ReferenceEditLoadHandler
  Handlers/*.cs          ← 4 handlers           ReferenceStoryLoadHandler
                                                 ReferenceDryRunHandler
Hrot.IG                                        ReferenceCheckpointHandler
  ClusterSlave (copy 3)    ← 115 lines            ReferenceLiveLoadHandler
                                                 ReferenceReplayLoadHandler
Hrot.ExCon
  ClusterSlave (copy 4)    ← 115 lines        Hrot.Common (retained, now thinner)
                                               ClusterStateChangedEvent      ← kept (uses ClusterState)
                                               LocalDiskStorageProvider  ← new
                                               DdsOrchestrationTransport ← new

                                            Hrot.Orchestrator (retained, now thinner)
                                               HrotStateGraph          ← new
                                               ClusterMaster               ← kept
                                               TransitionPlanner         ← removed (now FDP)
```

---

## 4. New Project: FDP.Toolkit.Orchestration

**Location:** `FDP/Toolkits/FDP.Toolkit.Orchestration/FDP.Toolkit.Orchestration.csproj`

### 4.1 Core Interfaces

#### `IDsmHandler`
Moved verbatim from `Hrot.Common.Orchestration.IDsmHandler`:

```csharp
// FDP.Toolkit.Orchestration
public interface IDsmHandler
{
    bool CanHandle(int operationId);
    Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct);
    void Commit(OrchestrationCommand cmd, EntityRepository? repo);
    void Abort(OrchestrationCommand cmd, EntityRepository? repo);
}
```

> **Layering note:** The `operationId` parameter replaces `NodeOpType op`. Hrot
> handlers cast `cmd.OperationId` back to `NodeOpType` for their internal switch
> statements (the integer values are identical and stable). This is a pure namespace
> change at the interface boundary — no semantic change.

#### `ITickableDsmHandler`
Moved verbatim from `Hrot.Common.Orchestration.ITickableDsmHandler`:

```csharp
// FDP.Toolkit.Orchestration
public interface ITickableDsmHandler : IDsmHandler
{
    void DrainDeferredAcks();
}
```

### 4.2 OrchestrationCommand and OrchestrationStatus

Toolkit-owned plain structs that serve as the currency exchanged between a transport
implementation and the toolkit ClusterSlave — no DDS types at the boundary.

```csharp
// FDP.Toolkit.Orchestration
public readonly record struct OrchestrationCommand(
    Guid   TransactionId,
    int    TargetNodeId,
    int    OperationId,
    string PayloadJson);

public readonly record struct OrchestrationStatus(
    Guid   TransactionId,
    int    NodeId,
    int    StatusCode,        // unified code — see OrchestrationStatusCode ranges
    bool   IsParticipating,
    string ResultJson);
```

These types live in the FDP toolkit. `DdsOrchestrationTransport` (Hrot layer) maps
to/from `NodeOpCommand`/`NodeOpStatus` DDS structs.

#### 4.2.1 Unified Status Code Scheme

The current Hrot DDS contract splits outcome information across two fields:
`OpStatus Status` (enum: `Pending`, `InProgress`, `Success`, `Failure`) and
`int ErrorCode` (arbitrary per-handler integer). Consumers must check both in concert:
`if (status == Failure && errorCode == 1001)` — which scatters error-handling logic.

Phase 4 consolidates these into a **single `int StatusCode`** with tiered ranges:

| Range | Meaning | Examples |
|-------|---------|----------|
| 0 – 9 | Lifecycle | `0` Success, `1` InProgress, `2–9` reserved (e.g. `2` Pending) |
| 10 – 99 | Generic errors | `10` Rejected, `11` Timeout, `12` Cancelled |
| 100 – 999 | Federation errors | `101` InvalidZone, `102` ExerciseMismatch |
| 1000+ | Node / Slave errors | `1001` OutOfMemory, `1002` AssetNotFound |

Pros of consolidation:
- Single conditional gate: `statusCode >= 10` means «not yet successful».
- No silent mismatch where `Status=Success` but `ErrorCode != 0` (a bug attractor).
- Extensible ranges without enum changes — new error domains just pick their range.
- The toolkit `OrchestrationStatus` struct is one field smaller; serializers, loggers,
  and test assertions become simpler.

Potential cons (mitigated here):
- Range 0–9 is sparse (only 0,1,2 used today) — reserved for future lifecycle states.
- Collision risk between ranges is eliminated by the tiered assignment and a
  `static class OrchestrationStatusCode` with named constants.

**`OrchestrationStatusCode` constants class** (in `FDP.Toolkit.Orchestration`):

```csharp
public static class OrchestrationStatusCode
{
    // ── Lifecycle (0–9) ───────────────────────────────────────────────────────
    // 0 = Success is the default-initialised wire value → a zero-initialised
    // struct on the network is always «clean OK». Matches SstStatusCode convention.
    public const int Success    = 0;
    public const int InProgress = 1;
    public const int Pending    = 2;  // reserved range 2–9 for future lifecycle states

    // ── Generic errors (10–99) ────────────────────────────────────────────────
    public const int Rejected   = 10;
    public const int Timeout    = 11;
    public const int Cancelled  = 12;

    // ── Federation errors (100–999) ───────────────────────────────────────────
    public const int InvalidZone       = 101;
    public const int ExerciseMismatch  = 102;

    // ── Node / slave errors (1000+) ───────────────────────────────────────────
    public const int OutOfMemory   = 1000;
    public const int AssetNotFound = 1001;

    /// <summary>Returns true when the code represents a terminal failure.</summary>
    public static bool IsError(int code) => code >= 10;
}
```

**DDS message model update:** To take full advantage of the unified scheme, the
`Hrot.NED` `NodeOpStatus` and `ClusterOpStatus` IDL structs are simplified
as part of CGF1-G0401:
- Remove the `OpStatus Status` enum field and the `int ErrorCode` field from both structs.
- Replace with a single `int StatusCode` field.
- The `OpStatus` enum type in `OrchestrationMessages.cs` is deleted (no longer needed).
- `DdsOrchestrationTransport.PublishStatus` passes `status.StatusCode` directly;
  no combination or casting required.

### 4.3 Generic ClusterSlave

`FDP.Toolkit.Orchestration.ClusterSlave` is the single canonical implementation of the
dispatch engine, replacing the four Hrot-layer copies.

```csharp
// FDP.Toolkit.Orchestration
public sealed class ClusterSlave : IDisposable
{
    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Production constructor. Transport drives DDS heartbeat/command I/O.
    /// </summary>
    public ClusterSlave(
        IOrchestrationTransport transport,
        int nodeId,
        string subsystemName,
        FdpEventBus? eventBus = null);

    /// <summary>
    /// Test constructor. No transport; use EnqueueCommandForTest.
    /// </summary>
    internal ClusterSlave(FdpEventBus? eventBus = null);

    // ── Handler registration ──────────────────────────────────────────────────
    public void RegisterHandler(IDsmHandler handler);
    public bool IsHandlerRegistered<T>() where T : IDsmHandler;
    public IReadOnlyList<IDsmHandler> RegisteredHandlers { get; }

    // ── Per-frame pump ────────────────────────────────────────────────────────
    public void Tick();

    // ── Test helpers ──────────────────────────────────────────────────────────
    internal void EnqueueCommandForTest(OrchestrationCommand cmd);
    internal int LocalStateIdForTest { get; }
}
```

**Behaviour contract (unchanged from SimHost.ClusterSlave BATCH-18):**
- `Tick()` polls `IOrchestrationTransport.DequeueCommand()` and drains the queue.
- When `PrepareAsync` returns an incomplete `Task`, the result is stored in
  `_pendingPrepare`; no further commands are dequeued until the task completes.
- `ITickableDsmHandler.DrainDeferredAcks()` is called every tick before processing
  new commands.
- Duplicate `TransactionId` values are silently discarded.
- On `CommitState` commands the `_localStateId` is updated and `eventBus` receives a
  `TkClusterStateChangedEvent { int PreviousStateId, int NextStateId }`.

**`TkClusterStateChangedEvent`** — a new toolkit-level event:

```csharp
// FDP.Toolkit.Orchestration
[EventId(7002)]
public struct TkClusterStateChangedEvent
{
    public int PreviousStateId;
    public int NextStateId;
}
```

Hrot's `ClusterStateChangedEvent` (which uses `ClusterState` enum) is published by a
forwarding subscription registered at the Hrot wiring layer:

```csharp
// Hrot.Common — wiring helper
eventBus.Register<TkClusterStateChangedEvent>();
eventBus.SubscribeForward<TkClusterStateChangedEvent>(e =>
    new ClusterStateChangedEvent
    {
        Previous = (ClusterState)e.PreviousStateId,
        Next     = (ClusterState)e.NextStateId,
    });
```

This keeps `ClusterStateChangedEvent` and the `ClusterState` enum in the Hrot layer
(they reference the Hrot DDS contract) while giving toolkit consumers a generic
alternative.

### 4.4 ITransitionGraph and TransitionGraphBuilder

The BFS engine in `TransitionPlanner` is completely generic — it just needs an
adjacency source. Currently the adjacency set is hardcoded in a private `static readonly
Dictionary` inside `Hrot.Orchestrator.TransitionPlanner`. Extracting it yields:

```csharp
// FDP.Toolkit.Orchestration
public interface ITransitionGraph
{
    /// <summary>Returns the set of state IDs reachable directly from <paramref name="fromStateId"/>.</summary>
    IReadOnlyList<int> GetNeighbors(int fromStateId);

    /// <summary>Returns all known state IDs (for path validation).</summary>
    IReadOnlyList<int> AllStates { get; }
}

public sealed class TransitionGraphBuilder
{
    public TransitionGraphBuilder AddState(int stateId, string debugName = "");
    public TransitionGraphBuilder AddTransition(int fromStateId, int toStateId);
    public ITransitionGraph Build();
}
```

`TransitionPlanner` (moved to `FDP.Toolkit.Orchestration`) is updated:

```csharp
// FDP.Toolkit.Orchestration
public sealed class TransitionPlanner
{
    public TransitionPlanner(ITransitionGraph graph);
    public IReadOnlyList<int> CalculateShortestPath(int fromStateId, int toStateId);
}
```

`Hrot.Orchestrator` constructs the graph from `ClusterState` at startup:

```csharp
// Hrot.Orchestrator — HrotStateGraph.cs
public static class HrotStateGraph
{
    public static ITransitionGraph Build()
    {
        return new TransitionGraphBuilder()
            .AddTransition((int)ClusterState.Standby,          (int)ClusterState.LoadingEdit)
            .AddTransition((int)ClusterState.LoadingEdit,      (int)ClusterState.RunningEdit)
            .AddTransition((int)ClusterState.RunningEdit,      (int)ClusterState.LoadingLive)
            // … all edges …
            .Build();
    }
}
```

### 4.5 IScenarioStorageProvider

Abstracts the "where and how" of scenario file staging, replacing the raw
`localTempRoot` string currently threaded through all handler constructors.

```csharp
// FDP.Toolkit.Orchestration
public interface IScenarioStorageProvider
{
    /// <summary>
    /// Returns a read-only stream for the named file within a scenario's staging
    /// directory. Returns null when the file does not exist.
    /// </summary>
    Stream? OpenScenarioFile(string scenarioId, string fileName);

    /// <summary>
    /// Ensures the staging directory for <paramref name="scenarioId"/> exists and
    /// returns its absolute path (used by reference handlers that need a local fs path,
    /// e.g. for recording file output).
    /// </summary>
    string EnsureStagingDirectory(string scenarioId);

    /// <summary>
    /// Enumerates all files in the staging directory for <paramref name="scenarioId"/>.
    /// Returns an empty sequence when the directory does not exist.
    /// </summary>
    IEnumerable<string> EnumerateScenarioFiles(string scenarioId);
}
```

**Reference implementation — `LocalDiskStorageProvider`** lives in `Hrot.Common`:

```csharp
// Hrot.Common.Orchestration — LocalDiskStorageProvider.cs
public sealed class LocalDiskStorageProvider : IScenarioStorageProvider
{
    private readonly string _localTempRoot;

    public LocalDiskStorageProvider(string localTempRoot = @"C:\FDP_Temp")
    {
        _localTempRoot = localTempRoot;
    }

    public Stream? OpenScenarioFile(string scenarioId, string fileName)
    {
        var path = Path.Combine(_localTempRoot, scenarioId, fileName);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public string EnsureStagingDirectory(string scenarioId)
    {
        var dir = Path.Combine(_localTempRoot, scenarioId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public IEnumerable<string> EnumerateScenarioFiles(string scenarioId)
    {
        var dir = Path.Combine(_localTempRoot, scenarioId);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.json")
            : Enumerable.Empty<string>();
    }
}
```

### 4.6 IOrchestrationTransport

Abstracts the DDS writers/readers embedded in each copy of ClusterSlave today, keeping
the generic ClusterSlave 100% agnostic to the underlying wire protocol.

```csharp
// FDP.Toolkit.Orchestration
public interface IOrchestrationTransport : IDisposable
{
    /// <summary>
    /// Publishes a liveness heartbeat for this node.
    /// Called approximately once per second by ClusterSlave.Tick().
    /// </summary>
    void PublishHeartbeat(int nodeId, string subsystemName, int localStateId, long wallTicksUtc);

    /// <summary>
    /// Publishes an operation status ACK back to the orchestrator.
    /// Handlers call this via the transport reference injected at construction.
    /// </summary>
    void PublishStatus(OrchestrationStatus status);

    /// <summary>
    /// Attempts to dequeue one pending command from the inbound queue.
    /// Returns false when the queue is empty.
    /// Called by ClusterSlave.Tick() from the main thread.
    /// </summary>
    bool TryDequeueCommand(out OrchestrationCommand cmd);
}
```

**`DdsOrchestrationTransport`** lives in `Hrot.Common` (or `Hrot.NED`)
and bridges CycloneDDS to these three operations:

```csharp
// Hrot.Common.Orchestration — DdsOrchestrationTransport.cs
public sealed class DdsOrchestrationTransport : IOrchestrationTransport
{
    private readonly DdsWriter<NodeHeartbeat>  _heartbeatWriter;
    private readonly DdsReader<NodeOpCommand>  _commandReader;
    private readonly DdsWriter<NodeOpStatus>   _statusWriter;
    private readonly ConcurrentQueue<OrchestrationCommand> _inboundQueue = new();
    private readonly Thread _listenerThread;
    private readonly CancellationTokenSource _cts = new();

    public DdsOrchestrationTransport(DdsParticipant participant, int nodeId)
    {
        _heartbeatWriter = new DdsWriter<NodeHeartbeat>(participant);
        _commandReader   = new DdsReader<NodeOpCommand>(participant);
        _statusWriter    = new DdsWriter<NodeOpStatus>(participant);
        _commandReader.SetFilter(cmd => cmd.TargetNodeId == nodeId);

        _listenerThread = new Thread(() => RunListener(_cts.Token))
        { IsBackground = true, Name = $"Node{nodeId}-ClusterSlave-Transport" };
        _listenerThread.Start();
    }

    public void PublishHeartbeat(int nodeId, string subsystemName, int localStateId, long wallTicksUtc)
    {
        _heartbeatWriter.Write(new NodeHeartbeat
        {
            NodeId        = nodeId,
            SubsystemName = subsystemName,
            LocalClusterState = (ClusterState)localStateId,
            WallTicksUtc  = wallTicksUtc,
            // … other fields …
        });
    }

    public void PublishStatus(OrchestrationStatus status)
    {
        _statusWriter.Write(new NodeOpStatus
        {
            TransactionId   = status.TransactionId,
            NodeId          = status.NodeId,
            StatusCode      = status.StatusCode,   // unified — no OpStatus cast needed
            IsParticipating = status.IsParticipating,
            ResultJson      = status.ResultJson,
        });
    }

    public bool TryDequeueCommand(out OrchestrationCommand cmd) =>
        _inboundQueue.TryDequeue(out cmd);

    private void RunListener(CancellationToken ct) { /* identical to ClusterSlave.RunCommandListener */ }

    public void Dispose() { _cts.Cancel(); _listenerThread.Join(TimeSpan.FromSeconds(2)); }
}
```

Handlers that currently accept `DdsWriter<NodeOpStatus>?` are updated to accept
`IOrchestrationTransport?` and call `transport?.PublishStatus(...)` instead.

### 4.7 Reference Handler Catalogue

All reference handlers live under `FDP.Toolkit.Orchestration.Handlers`. They depend
only on FDP interfaces and take app-specific config via constructor injection.
No Hrot-layer type may appear in their signatures.

| Reference Handler | Moved From | `CanHandle` condition | Key constructor deps |
|---|---|---|---|
| `ReferencePrefetchHandler` | `Hrot.SimHost/…/PrefetchFilesDsmHandler` | `PrefetchFiles (op=25)` | `IOrchestrationTransport?`, `int nodeId`, `IScenarioStorageProvider` |
| `ReferenceScenarioLoadHandler` | `Hrot.SimHost/…/ScenarioLoadDsmHandler` | `PrepareLive (op=9)` | `ScenarioSerializer`, `IScenarioStorageProvider`, `EntityRepository?` |
| `ReferenceEditLoadHandler` | `Hrot.SimHost/…/EditLoadDsmHandler` | `PrepareState (op=1)` targeting LoadingEdit | `ScenarioSerializer`, `IScenarioStorageProvider`, `EntityRepository?` |
| `ReferenceStoryLoadHandler` | `Hrot.SimHost/…/StoryLoadDsmHandler` | `StartEpisode (op=20)`, `StopEpisode (op=21)` | `ScenarioSerializer`, `IScenarioStorageProvider`, `EntityRepository?`, `IOrchestrationTransport?`, `int nodeId` |
| `ReferenceDryRunHandler` | `Hrot.Common/…/DryRunDsmHandler` | `PrepareState (op=1)` targeting DryRun states | `EntityRepository?` |
| `ReferenceCheckpointHandler` | `Hrot.SimHost/…/CheckpointDsmHandler` | `TakeSnapshot (op=4)` | `CheckpointIOWorker`, `EntityRepository?`, `IOrchestrationTransport?`, `int nodeId` |
| `ReferenceLiveLoadHandler` | `Hrot.SimHost/…/LiveLoadDsmHandler` | `PrepareLive (op=9)`, `FinalizeLive (op=10)` | `ClusterSlave`, `FdpEventBus`, `CheckpointIOWorker?`, `EcsRecordReplayController?`, `string storageDir` |
| `ReferenceReplayLoadHandler` | `Hrot.SimHost/…/ReplayLoadDsmHandler` | `PrepareReplay (op=11)`, `FinalizeReplay (op=12)`, `PrepareLive (op=9)` when replay active | `EcsRecordReplayController`, `SimulationSystemGroup`, `NetworkLifecycleSystemGroup`, `GhostCreationSystem`, `IOrchestrationTransport?`, `int nodeId`, `string storageDir` |

> **Note on `ReferenceLiveLoadHandler` and `ReferenceReplayLoadHandler`:** These two
> depend on `EcsRecordReplayController`, `SimulationSystemGroup`, and
> `NetworkLifecycleSystemGroup` — all FDP toolkit types — so they can safely live in
> `FDP.Toolkit.Orchestration`.  The `ClusterSlave` parameter in `ReferenceLiveLoadHandler`
> is the toolkit `ClusterSlave`, which is also an FDP type.

> **Note on `CheckpointIOWorker`:** `CheckpointIOWorker` is currently defined in
> `Hrot.SimHost`. As part of G0405 it is relocated to `FDP.Toolkit.Orchestration`
> (it has no Hrot-specific dependencies), making `ReferenceCheckpointHandler` fully
> self-contained within the toolkit.

---

## 5. Hrot App Layer After Migration

The Hrot layer retains only what is truly app-specific: the DDS transport bridge, the
concrete state graph definition, and the `ClusterStateChangedEvent` → ClusterState forwarding.

### 5.1 DdsOrchestrationTransport

**File:** `Hrot.Common/Orchestration/DdsOrchestrationTransport.cs`  
**Implements:** `IOrchestrationTransport`  
Handles CycloneDDS reader/writer lifecycle and bridges between toolkit plain types and
Hrot DDS message types (see §4.6 above for the full implementation sketch).

### 5.2 LocalDiskStorageProvider

**File:** `Hrot.Common/Orchestration/LocalDiskStorageProvider.cs`  
**Implements:** `IScenarioStorageProvider`  
Wraps `C:\FDP_Temp\<scenarioId>\` as the staging root (see §4.5 above).
Accepts a configurable `localTempRoot` constructor parameter — apps that mount their
staging area elsewhere just pass a different path.

### 5.3 HrotStateGraph

**File:** `Hrot.Orchestrator/HrotStateGraph.cs`  
Calls `TransitionGraphBuilder` with all valid `ClusterState` edges (the complete set
currently hardcoded in `Hrot.Orchestrator.TransitionPlanner`'s private `_adjacency`
dictionary). Returns an `ITransitionGraph` consumed by the generic `TransitionPlanner`.

### 5.4 NodeBootstrapper After Migration

The `NodeBootstrapper.BuildOrchestration` signature becomes dramatically sparser —
all handler construction moves to toolkit one-liners:

```csharp
// Hrot.SimHost.NodeBootstrapper — after Phase 4
public ClusterSlave BuildOrchestration(
    NodeRole role,
    ModuleHostKernel kernel,
    EntityRepository world,
    int nodeId,
    DdsParticipant? participant = null,
    string subsystemName = "SimHost",
    FdpEventBus? eventBus = null,
    ScenarioSerializer? scenarioSerializer = null,
    string localTempRoot = @"C:\FDP_Temp",
    CheckpointIOWorker? checkpointWorker = null,
    SimulationSystemGroup? simGroup = null,
    NetworkLifecycleSystemGroup? lifecycleGroup = null,
    GhostCreationSystem? ghostCreationSystem = null)
{
    IOrchestrationTransport? transport = participant != null
        ? new DdsOrchestrationTransport(participant, nodeId)
        : null;

    var drillSlave = transport != null
        ? new ClusterSlave(transport, nodeId, subsystemName, eventBus)
        : new ClusterSlave(eventBus);  // test path

    var storageProvider = new LocalDiskStorageProvider(localTempRoot);

    // Register reference handlers (same registration order as before):
    if (controller != null && simGroup != null && lifecycleGroup != null && ghostCreationSystem != null)
        drillSlave.RegisterHandler(new ReferenceReplayLoadHandler(
            controller, simGroup, lifecycleGroup, ghostCreationSystem,
            transport, nodeId, localTempRoot));

    if (eventBus != null)
        drillSlave.RegisterHandler(new ReferenceLiveLoadHandler(
            drillSlave, eventBus, checkpointWorker, controller, localTempRoot));

    if (checkpointWorker != null)
        drillSlave.RegisterHandler(new ReferenceCheckpointHandler(
            checkpointWorker, world, transport, nodeId));

    drillSlave.RegisterHandler(new ReferenceDryRunHandler(world));

    drillSlave.RegisterHandler(new ReferencePrefetchHandler(transport, nodeId, storageProvider));

    if (scenarioSerializer != null)
    {
        drillSlave.RegisterHandler(new ReferenceScenarioLoadHandler(
            scenarioSerializer, storageProvider, world));

        drillSlave.RegisterHandler(new ReferenceEditLoadHandler(
            scenarioSerializer, storageProvider, world));

        drillSlave.RegisterHandler(new ReferenceStoryLoadHandler(
            scenarioSerializer, storageProvider, world, transport, nodeId));
    }

    return drillSlave;
}
```

---

## 6. Project Dependency Graph

```
                      ┌──────────────────────────────────┐
                      │  FDP.Toolkit.Orchestration        │
                      │                                   │
                      │  IDsmHandler / ITickableDsmHandler│
                      │  IOrchestrationTransport          │
                      │  ITransitionGraph                 │
                      │  IScenarioStorageProvider         │
                      │  ClusterSlave (generic)             │
                      │  TransitionPlanner (BFS)          │
                      │  TransitionGraphBuilder           │
                      │  OrchestrationCommand             │
                      │  OrchestrationStatus              │
                      │  TkClusterStateChangedEvent           │
                      │  Reference Handlers               │
                      └──────────────┬───────────────────┘
                             depends on
          ┌────────────────┬──────────────────┬────────────────┐
          ▼                ▼                  ▼                ▼
   Fdp.Kernel    FDP.Toolkit.Scenario  FDP.Toolkit.Replay  ModuleHost.Core
          ▲                ▲                  ▲                ▲
          └────────────────┴──────────────────┴────────────────┘
                      (already exist; no change)

  ┌─────────────────────────────────────────────────────────────┐
  │  Hrot.Common (retains ClusterStateChangedEvent, adds transport)│
  │                                                             │
  │  DdsOrchestrationTransport    ← new, implements IOrch.Trans.│
  │  LocalDiskStorageProvider     ← new, implements IStorage    │
  │  ClusterStateChangedEvent         ← kept (refs ClusterState)        │
  │                                                             │
  │  depends on: FDP.Toolkit.Orchestration, Hrot.NED│
  └─────────────────────────────────────────────────────────────┘

  ┌──────────────────────────────────────────────────────────────┐
  │  Hrot.Orchestrator                                          │
  │                                                              │
  │  HrotStateGraph             ← new, builds ITransitionGraph │
  │  ClusterMaster                  ← kept, modified               │
  │  NodeRoster, DistributedTx…   ← kept                         │
  │  TransitionPlanner            ← REMOVED (now in FDP toolkit) │
  │                                                              │
  │  depends on: FDP.Toolkit.Orchestration, Hrot.NED │
  └──────────────────────────────────────────────────────────────┘

  Hrot.SimHost / Hrot.CGF / Hrot.IG / Hrot.ExCon
    ← all ClusterSlave copies REMOVED
    ← all handler .cs files REMOVED (replaced by reference handlers)
    ← depends on FDP.Toolkit.Orchestration, Hrot.Common
```

---

## 7. Migration Playbook

The six tasks in Phase 4 execute the migration in dependency order:

| Task | Description | Pre-requisite |
|------|-------------|---------------|
| **CGF1-G0401** | Create `FDP.Toolkit.Orchestration.csproj`; move `IDsmHandler`, `ITickableDsmHandler`; define `IOrchestrationTransport`, `ITransitionGraph`, `IScenarioStorageProvider`, `OrchestrationCommand`, `OrchestrationStatus` (unified `StatusCode`), `OrchestrationStatusCode`, `TkClusterStateChangedEvent`; remove `OpStatus` enum; update `NodeOpStatus`/`ClusterOpStatus` DDS structs to single `int StatusCode`. Update all Hrot using-directives. | Phases 1–3 complete |
| **CGF1-G0402** | Implement generic `ClusterSlave` in FDP toolkit (with `IOrchestrationTransport` + async-prepare + dedup). Implement `DdsOrchestrationTransport` in `Hrot.Common`. Remove the 4 Hrot ClusterSlave copies and replace their wiring sites with the toolkit version. | G0401 |
| **CGF1-G0403** | Move `TransitionPlanner` (BFS) to FDP toolkit; implement `TransitionGraphBuilder`; add `HrotStateGraph` in `Hrot.Orchestrator`; inject graph into `ClusterMaster`. | G0401 |
| **CGF1-G0404** | Add `LocalDiskStorageProvider` in `Hrot.Common`; move scenario/story/prefetch handlers to toolkit as reference implementations. Update `NodeBootstrapper`, `CgfApplication` wiring. Delete superseded Hrot handler files. | G0401, G0402 |
| **CGF1-G0405** | Relocate `CheckpointIOWorker` to FDP toolkit; move `DryRunDsmHandler`, `CheckpointDsmHandler`, `LiveLoadDsmHandler`, `ReplayLoadDsmHandler` to toolkit as reference implementations. Update wiring. | G0401, G0402, G0404 |
| **CGF1-G0406** | Final cleanup: update `.csproj` project references; delete now-empty Hrot handler directories; run full CI; verify no `Hrot.*` → `FDP.*` downward leaks. | G0401–G0405 |

---

## 8. Files Deleted vs Retained

### Deleted (replaced by toolkit reference implementations)

| Deleted file | Replaced by |
|---|---|
| `Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs` | `FDP.Toolkit.Orchestration.ClusterSlave` |
| `Hrot.CGF/Modules/Orchestration/ClusterSlave.cs` | same |
| `Hrot.IG/Modules/Orchestration/ClusterSlave.cs` | same |
| `Hrot.ExCon/Orchestration/ClusterSlave.cs` | same |
| `Hrot.Common/Orchestration/IDsmHandler.cs` | `FDP.Toolkit.Orchestration.IDsmHandler` |
| `Hrot.Common/Orchestration/ITickableDsmHandler.cs` | `FDP.Toolkit.Orchestration.ITickableDsmHandler` |
| `Hrot.Common/Orchestration/Handlers/DryRunDsmHandler.cs` | `FDP.Toolkit.Orchestration.Handlers.ReferenceDryRunHandler` |
| `Hrot.SimHost/Modules/Orchestration/Handlers/PrefetchFilesDsmHandler.cs` | `FDP.Toolkit.Orchestration.Handlers.ReferencePrefetchHandler` |
| `Hrot.SimHost/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs` | `FDP.Toolkit.Orchestration.Handlers.ReferenceScenarioLoadHandler` |
| `Hrot.SimHost/Modules/Orchestration/Handlers/EditLoadDsmHandler.cs` | `FDP.Toolkit.Orchestration.Handlers.ReferenceEditLoadHandler` |
| `Hrot.SimHost/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs` | `FDP.Toolkit.Orchestration.Handlers.ReferenceStoryLoadHandler` |
| `Hrot.SimHost/Modules/Orchestration/Handlers/CheckpointDsmHandler.cs` | `FDP.Toolkit.Orchestration.Handlers.ReferenceCheckpointHandler` |
| `Hrot.SimHost/Modules/Orchestration/LiveLoadDsmHandler.cs` | `FDP.Toolkit.Orchestration.Handlers.ReferenceLiveLoadHandler` |
| `Hrot.SimHost/Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs` | `FDP.Toolkit.Orchestration.Handlers.ReferenceReplayLoadHandler` |
| `Hrot.CGF/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs` | `ReferenceScenarioLoadHandler` (same instance via CGF bootstrap) |
| `Hrot.CGF/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs` | `ReferenceStoryLoadHandler` (same instance) |
| `Hrot.Orchestrator/TransitionPlanner.cs` | `FDP.Toolkit.Orchestration.TransitionPlanner` |
| `Hrot.SimHost/Modules/Orchestration/IDsmHandler.cs` | (stub comment file — already mostly empty) |

### Retained

| Retained file | Notes |
|---|---|
| `Hrot.Common/Orchestration/ClusterStateChangedEvent.cs` | References `ClusterState` — stays in Hrot layer |
| `Hrot.CGF/Modules/Orchestration/Handlers/FailLoudRecordReplayStub.cs` | Hrot-specific diagnostic stub; kept until CGF acquires a real kernel |
| `Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` | Wraps `ModuleHostKernel`; may be promoted to toolkit in a later pass if fully decoupled |
| `Hrot.SimHost/Modules/Orchestration/CheckpointIOWorker.cs` | **Relocated** to `FDP.Toolkit.Orchestration` in G0405 |
| `Hrot.Orchestrator/ClusterMaster.cs` | Kept — app-specific orchestration master |
| `Hrot.Orchestrator/NodeRoster.cs` | Kept |
| `Hrot.Orchestrator/StorageGatewayModule.cs` | Kept |
| `Hrot.Orchestrator/ReplayMasterModule.cs` | Kept |
| `Hrot.Orchestrator/GlobalContextDsmHandler.cs` | Kept (Hrot-specific save/load context) |
| `Hrot.NED/Orchestration/OrchestrationMessages.cs` | `ClusterState`, `NodeOpType`, DDS structs — kept as Hrot foundation |
