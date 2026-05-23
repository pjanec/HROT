# Hrot.Orchestrator

- **Project file**: `Hrot/Subsystems/Hrot.Orchestrator/Hrot.Orchestrator.csproj`
- **Root namespace**: `Hrot.Orchestrator`
- **Target framework**: net8.0
- **Date**: 2026-05-23

---

## README Validation

**Missing** -- no `README.md` exists in the project folder.

---

## Executive Overview

`Hrot.Orchestrator` is the central coordination node of the distributed HROT
military/combat simulation cluster.  It runs inside the standalone **Runner**
process (the Orchestrator node) and acts as the sole authority for:

- **Cluster lifecycle** -- driving the cluster state machine from `Idle` through
  `LoadingEdit / OperatingEdit / LoadingPreview / OperatingPreview`, the
  `LoadingLive / OperatingLive` live-exercise path, and the
  `LoadingReplay / OperatingReplay` post-exercise replay path.
- **Two-phase commit coordination (2PC)** -- fanning out `NodeOpCommand` messages
  to every registered simulation node (IOS, IG, SimHost, CGF, ExCon) and
  waiting for unanimous acknowledgement before advancing the cluster state.
- **Master time control** -- owning the `MasterSyncController` that manages
  wall-clock advancement, the deterministic barrier protocol, and manual stepping.
- **Storage gateway** -- acting as the SMB Pull Gateway that collects local
  node snapshots after a `SerializeLocal` round and copies them to the shared NAS.
- **Asset inventory** -- periodically scanning the NAS for available scenarios
  and exercises, and maintaining a local recording ledger.
- **Diagnostics collection** -- triggering cluster-wide diagnostic dumps and
  K-way merging of per-node log files.

The subsystem is intentionally headless-capable: every external dependency is
injected through `INetworkFactory`.  When the factory is absent (test or offline
mode) all DDS calls are silently replaced by null-object implementations so the
entire 2PC logic can be exercised without network infrastructure.

---

## Architecture

### 5-Phase Update Loop

`OrchestratorSubsystem.Update()` enforces a strict ordering each frame:

```
Phase 1  Network boundary
         _timeTranslators.ScanAndPublish()   -- flush bus CURRENT -> DDS
         _timeTranslators.PollIngress()      -- DDS -> bus WRITE buffer
         _translator.Tick()                  -- heartbeats, ClusterOpRequests, NodeOpStatuses

Phase 2  Bus swap (single point)
         _bus.SwapBuffers()                  -- WRITE becomes CURRENT; old CURRENT is wiped

Phase 3  Core logic (sequential, dependency ordered)
         _masterSync.Update()                -- drain time intents
         _liveBranchProcessManager.Tick()    -- freeze/restore time around Replay->Live
         _seekProcessManager.Tick()          -- preconditions + clock snap for seek
         _globalContextProcessManager.Tick() -- context save/load
         _assetPrefetchProcessManager.Tick() -- async NAS->node file copy
         _clusterMaster.Tick()               -- 2PC engine
         _replayProcessManager.Tick()        -- auto-pause at replay end
         _storageProcessManager.Tick()       -- NAS pull after SerializeLocal
         _assetInventoryProcessManager.Tick()-- NAS scan + recording ledger
         _episodeProcessManager.Tick()       -- episode set maintenance
         _diagnosticsDumpProcessManager.Tick()-- diagnostics NAS pull
         _mergeWorker.Tick()                 -- K-way log merge
         _clusterSlave.Tick()                -- slave-side state handler

Phase 4  Local observation
         _uiCache.Update()                   -- CQRS read-model drain
         _scenarioPanel.Update(dt)           -- seek debounce

Phase 5  NTP ingress
         _timeTranslators.PollNtpIngress()
```

### Cluster State Machine

`HrotStateGraph` defines a directed graph traversed by `ClusterMasterPlanner`
using breadth-first search to compute the shortest valid path between any two
cluster states.

```
                        +------------------+
                        |      Idle        |
                        +------------------+
                       /         |         \
                      v          v          v
             +-----------+ +-----------+ +-----------+
             |LoadingEdit| |LoadingLive| |LoadingReplay
             +-----------+ +-----------+ +-----------+
                  |             |              |
                  v             v              v
           +-----------+ +------------+ +------------+
           |OperatingEdit|OperatingLive| |OperatingReplay
           +-----------+ +------------+ +------------+
            |       |          |          |      |
            v       v          v          v      v
   +----------+ +--------+ +----------+ +----+ +--------+
   |LoadingPrev |UnloadEdit|UnloadingLive|Idle| |UnloadRep|
   +----------+ +--------+ +----------+ +----+ +--------+
        |             |          |               |
        v             v          v               v
  +-----------+    +----+     +----+          +----+
  |OperatingPrev|  |Idle|     |Idle|          |Idle|
  +-----------+    +----+     +----+          +----+
        |
        v
  +-----------+
  |UnloadPrev |
  +-----------+
        |
        v
  +-----------+
  |OperatingEdit
  +-----------+
```

Notes:
- `Degraded` is a terminal system-imposed state with no outgoing planning edges.
- `OperatingReplay -> LoadingLive` enables the Live-from-Replay branch path.
- Failure-recovery edges (e.g. `LoadingEdit -> Idle`) are automatic rollbacks,
  not plannable transitions.

### Two-Phase Commit (2PC)

```
+--------------------+   TransitionStateIntent    +------------------+
|  ClusterMaster     |<---------------------------|  OrchestratorSub |
|  (2PC Coordinator) |                            |  system          |
+--------------------+                            +------------------+
         |
         | 1. PlanTrajectory (BFS)
         v
  +-------------+
  | Step Queue  |  OperationStep, TransitionStep, ...
  +-------------+
         |
         | 2. Fan-out NodeOpCommand to each node via DDS
         v
  +-------------+   NodeOpCommand    +----------+
  | DDS Bus     |------------------>| SimHost  |
  | (Translator)|------------------>| IG       |
  +-------------+------------------>| CGF      |
                 ------------------>| ExCon    |
         |
         | 3. Collect NodeOpStatus ACKs
         v
  +----------------------------+
  | GenericTransactionTracker  |
  |  - Expected / Received     |
  |  - AbortOnFirstFailure     |
  |  - NodeResponses dict      |
  +----------------------------+
         |
         | 4. Run INodeResponseAggregator pipeline
         v
  +----------------------------+
  | ClusterOpCompletedEvent    |
  |  ResultPayload = consensus |
  +----------------------------+
```

### Process Manager (Saga) Pattern

Each `*ProcessManager` or `*Worker` owns one concern, reads events from the bus,
and publishes further events or performs async I/O:

```
  +----------+  ExecutePrefetchIntent  +---------------------+
  |ClusterMast|----------------------->|AssetPrefetchProcess  |
  |er         |                        |Manager               |
  +----------+                        +---------------------+
                                               |
                                  PrefetchStagingCompletedEvent
                                               |
                                               v
                                  PrefetchFiles fan-out via DDS
```

```
  SeekReplayIntent            +------------------------+
  ---------------------->     |ReplaySeekProcessManager|
                              +------------------------+
                                     |
                            SlaveNodeSetUpdatedEvent
                            PauseTimeIntent
                                     |
                                     v
                              ClusterMaster fans out NodeReplaySeek
                                     |
                              ClusterOpCompletedEvent (ReplaySeekResult)
                                     |
                            MasterSyncController.SnapAndPause()
```

---

## Source Structure

All files reside under `Hrot/Subsystems/Hrot.Orchestrator/`.

### Root namespace -- `Hrot.Orchestrator`

| File | Class / Type | Role |
|------|-------------|------|
| `OrchestratorSubsystem.cs` | `OrchestratorSubsystem` | Entry point: `ISubsystem` + `IWindowRegistrar` |
| `ClusterMaster.cs` | `ClusterMaster` | 2PC coordinator, heartbeat ingestion, state machine host |
| `HrotStateGraph.cs` | `HrotStateGraph` | Builds the canonical cluster state machine graph |
| `TransitionPlanner.cs` | `ClusterMasterPlanner`, `ISysOpStep`, `TransitionStep`, `OperationStep` | BFS path-finder, step abstractions |
| `ClusterConfiguration.cs` | `ClusterConfiguration` | JSON config: mandatory nodes, timeouts, NAS path |
| `NodeRoster.cs` | `NodeRoster` | Active-node dictionary with staleness pruning |
| `NodeHealthProfile.cs` | `NodeHealthProfile` | Per-node heartbeat snapshot |
| `DistributedTransaction.cs` | `DistributedTransaction` | 2PC execution record (history ring buffer) |
| `INodeResponseAggregator.cs` | `INodeResponseAggregator` | Strategy interface for 2PC response reduction |
| `StorageConsensusAggregator.cs` | `StorageConsensusAggregator` | Flattens `SerializeLocal` file manifests |
| `EpisodeConsensusAggregator.cs` | `EpisodeConsensusAggregator`, `EpisodeConsensusPayload` | Episode start/stop consensus |
| `ReplayConsensusAggregator.cs` | `ReplayConsensusAggregator` | Max `DurationSeconds` across `PrepareReplay` ACKs |
| `ReplaySeekAggregator.cs` | `ReplaySeekAggregator` | First valid `ReplaySeekResult` |
| `DiagnosticsConsensusAggregator.cs` | `DiagnosticsConsensusAggregator` | Full + stripped diagnostic manifests |
| `LiveBranchProcessManager.cs` | `LiveBranchProcessManager` | Temporal interlock for Replay->Live transitions |
| `ReplaySeekProcessManager.cs` | `ReplaySeekProcessManager` | Seek preconditions + clock snap |
| `ReplayProcessManager.cs` | `ReplayProcessManager` | Auto-pause at replay end |
| `StorageProcessManager.cs` | `StorageProcessManager` | NAS pull after `SerializeLocal` |
| `EpisodeProcessManager.cs` | `EpisodeProcessManager` | Active episode set maintenance |
| `GlobalContextProcessManager.cs` | `GlobalContextProcessManager` | Orchestrator context save/load |
| `AssetPrefetchProcessManager.cs` | `AssetPrefetchProcessManager` | Async scenario prefetch to node staging |
| `AssetInventoryProcessManager.cs` | `AssetInventoryProcessManager` | Periodic NAS scan + recording ledger |
| `DiagnosticsDumpProcessManager.cs` | `DiagnosticsDumpProcessManager` | Diagnostics collection + NAS pull |
| `DiagnosticLogMergeWorker.cs` | `DiagnosticLogMergeWorker` | K-way chronological merge of log files |
| `StorageGatewayModule.cs` | `StorageGatewayModule`, `NodeDistributionTarget`, `GatewayResult` | SMB Pull Gateway, parallel file transfers |
| `ReplayMasterModule.cs` | `ReplayMasterModule` | Time-scale freeze/restore for Live-from-Replay |
| `GlobalContextClusterOpHandler.cs` | `GlobalContextClusterOpHandler` | Orchestrator.json save/load + DDS publish |
| `ClusterNodeOpBuilder.cs` | `ClusterNodeOpBuilder` | Factory for `NodeOpCommand` instances |
| `ClusterOpRequestAdapter.cs` | `ClusterOpRequestAdapter` | Legacy `ClusterOpRequest` -> typed intent conversion |
| `OrchestratorEventRegistry.cs` | `OrchestratorEventRegistry` | Registers internal bus event types |
| `OrchestratorInternalEvents.cs` | internal structs | Internal bus event definitions |
| `OrchestrationLogicPack.cs` | `OrchestrationLogicPack` | `IEcsModule` wrapper for `ClusterSlave` |
| `EventDrivenStorageGateway.cs` | `EventDrivenStorageGateway`, `IArchiveStorageBackend` | Bus-driven async storage dispatch |
| `RecordingLedgerEntry.cs` | `RecordingLedgerEntry` | Persistent recording metadata record |

### `Hrot.Orchestrator.Panels`

| File | Class | Role |
|------|-------|------|
| `Panels/ClusterUiCache.cs` | `ClusterUiCache` | CQRS read-model; drains bus events into observable properties |
| `Panels/ClusterScenarioPanel.cs` | `ClusterScenarioPanel` | ImGui cluster/scenario/episode control panel |
| `Panels/ClusterDiagnosticsPanel.cs` | `ClusterDiagnosticsPanel` | ImGui diagnostic dump + log merge panel |

### `Hrot.Orchestrator.Windows`

| File | Class | Role |
|------|-------|------|
| `Windows/OrchestratorWindow.cs` | `OrchestratorWindow` | `ManagedWindow` wrapper for `ClusterScenarioPanel` |
| `Windows/DiagnosticsWindow.cs` | `DiagnosticsWindow` | `ManagedWindow` wrapper for `ClusterDiagnosticsPanel` |
| `Windows/ClusterControlWindow.cs` | `ClusterControlWindow` | `ManagedWindow` for ExCon cluster control |

### `Hrot.Orchestrator.Events`

| File | Type | Role |
|------|------|------|
| `Events/DiagnosticsMergeEvents.cs` | `MergeLogsIntent`, `LogMergeCompletedEvent` | K-way log merge trigger and completion |

---

## Public API Reference

### `OrchestratorSubsystem`

```csharp
public sealed class OrchestratorSubsystem : ISubsystem, IWindowRegistrar
```

| Member | Description |
|--------|-------------|
| `OrchestratorSubsystem()` | Default constructor (headless/test mode). |
| `OrchestratorSubsystem(INetworkFactory)` | Production constructor with factory injection. |
| `string Name { get; }` | Returns `"Orchestrator"`. |
| `Vector4 TitleBarColor { get; }` | Beige title bar colour (S0501). |
| `void Initialize(SubsystemConfig)` | Loads config, wires all sub-components, drains initial bus state. |
| `void Update(float deltaTime)` | Executes the 5-phase update loop. |
| `void DrawWorld()` | No-op (Orchestrator has no 3D view). |
| `void DrawUI()` | No-op (panels registered via `IWindowRegistrar`). |
| `void RegisterWindows(WindowManager)` | Registers `OrchestratorWindow` and `DiagnosticsWindow`. |
| `void Shutdown()` | Disposes all sub-components in reverse dependency order. |
| `FdpEventBus? TimeBusForTest` | Internal test hook: the shared event bus. |
| `ClusterUiCache? UiCacheForTest` | Internal test hook: the CQRS cache. |
| `ClusterMaster? TestHook_ClusterMaster` | Internal test hook: the cluster master. |
| `double TestHook_CurrentSimTime` | Internal test hook: current master sim time in seconds. |
| `static string FormatPrettyJson(string)` | Indents a JSON string for tooltip display. |
| `static float ParseStepDelta(string, float)` | Parses `FixedDelta` from a `StepTime` payload. |

---

### `ClusterMaster`

```csharp
public sealed class ClusterMaster : IDisposable
```

| Member | Description |
|--------|-------------|
| `ClusterMaster(FdpEventBus, ClusterConfiguration?)` | Creates a master with bus + optional config. |
| `void Tick()` | Processes one frame: heartbeats, latch, evictions, intents, ACKs. |
| `void HandleClusterOpRequest(ClusterOpRequest)` | Thread-safe injection of a cluster op (UI / test path). |
| `Task HandleClusterOpRequestAsync(ClusterOpRequest)` | Async wrapper; completes immediately after enqueue. |
| `void RegisterAggregator(INodeResponseAggregator)` | Registers a response aggregator for a `NodeOpType`. |
| `NodeRoster NodeRoster { get; }` | Live roster of active cluster nodes. |
| `bool BootstrapComplete { get; }` | `true` once all mandatory nodes have reached `Standby`. |
| `ClusterState CurrentClusterState { get; }` | Optimistic current cluster DSM state. |
| `Guid ActiveExerciseId { get; }` | The GUID of the currently running exercise. |
| `bool HasInFlightTransaction { get; }` | `true` while a 2PC round is in progress. |
| `DistributedTransaction? ActiveTransaction { get; }` | Most recent in-flight transaction, or `null`. |
| `string? PendingTimeMode { get; }` | `"Deterministic"` or `null`; consumed by `OrchestratorSubsystem`. |
| `IReadOnlyList<ClusterState> GetReachableTargets()` | Returns plannable next states from the current state. |
| `IReadOnlyList<DistributedTransaction> TransactionHistory { get; }` | Completed/aborted transactions in chronological order. |
| `void Dispose()` | Releases managed resources. |

---

### `ClusterMasterPlanner`

```csharp
public sealed class ClusterMasterPlanner
```

| Member | Description |
|--------|-------------|
| `ClusterMasterPlanner(ITransitionGraph)` | Creates a planner backed by the given graph. |
| `IReadOnlyList<ClusterState> GetReachableTargets(ClusterState)` | One-step neighbours of `current`. |
| `IReadOnlyList<ClusterState> CalculateShortestPath(ClusterState, ClusterState)` | BFS shortest path. Throws if unreachable. |
| `Queue<ISysOpStep> PlanTrajectory(ClusterState, TransitionStateIntent)` | Full step queue including optional `OperationStep`s. |

---

### `HrotStateGraph`

```csharp
public static class HrotStateGraph
```

| Member | Description |
|--------|-------------|
| `static ITransitionGraph Build()` | Constructs the canonical Hrot cluster transition graph. |

---

### `ClusterConfiguration`

```csharp
public sealed class ClusterConfiguration
```

| Member | Description |
|--------|-------------|
| `string[] Mandatory { get; init; }` | Subsystem names that must reach `Standby` before bootstrap. |
| `string[] Optional { get; init; }` | Known optional subsystem names. |
| `float HeartbeatTimeoutSeconds { get; init; }` | Timeout before a node is ejected (default 5 s). |
| `int TransactionHistoryCapacity { get; init; }` | Ring buffer capacity (default 50). |
| `string NasBasePath { get; init; }` | Shared NAS root directory. |
| `static ClusterConfiguration Default { get; }` | Zero-config instance (empty mandatory list). |
| `static ClusterConfiguration LoadFrom(string)` | Loads from JSON file; returns `Default` if absent. |

---

### `NodeRoster`

```csharp
public sealed class NodeRoster
```

| Member | Description |
|--------|-------------|
| `IReadOnlyDictionary<int, NodeHealthProfile> ActiveNodes { get; }` | Live node map keyed by DDS node ID. |
| `void Upsert(NodeHealthProfile)` | Inserts or updates a node profile. |
| `void Remove(int)` | Removes a node by ID. |
| `void PruneStale(double, double)` | Evicts nodes whose last heartbeat is too old. |

---

### `NodeHealthProfile`

```csharp
public sealed class NodeHealthProfile
```

| Member | Description |
|--------|-------------|
| `int NodeId { get; set; }` | DDS participant node ID. |
| `string SubsystemName { get; set; }` | Human-readable name (e.g. `"SimHost"`, `"IG"`). |
| `ClusterState LocalClusterState { get; set; }` | Last reported local DSM state. |
| `double LastHeartbeatUtcSeconds { get; set; }` | UTC timestamp of last heartbeat. |
| `float CpuUsagePercent { get; set; }` | CPU load 0-100. |
| `long RamUsedBytes { get; set; }` | Process RSS in bytes. |

---

### `DistributedTransaction`

```csharp
public sealed class DistributedTransaction
```

| Member | Description |
|--------|-------------|
| `Guid TransactionId { get; set; }` | Unique transaction identifier. |
| `Guid OriginRequestId { get; set; }` | Originating `ClusterOpRequest` ID. |
| `ClusterState TargetDsmState { get; set; }` | Intended final cluster state. |
| `ClusterState SourceDsmState { get; set; }` | Cluster state when the transaction started. |
| `int TotalSteps / CompletedSteps { get; set; }` | Step progress tracking. |
| `float ElapsedSeconds / TimeoutSeconds { get; set; }` | Elapsed time; default timeout 30 s. |
| `bool AllowPartialSuccess { get; set; }` | Whether partial node ACKs are acceptable. |
| `bool IsAborted { get; set; }` | Set by `ClusterMaster` on local abort. |
| `bool Completed { get; set; }` | Set by `ClusterUiCache` when final DDS status arrives. |
| `string PayloadJson { get; set; }` | Original request JSON for audit log. |
| `Dictionary<int, Dictionary<NodeOpType, string>> NodeResponses { get; }` | Per-node ACK payloads. |
| `Dictionary<int, float> NodeAckLatencyMs { get; }` | Per-node ACK latency. |

---

### `INodeResponseAggregator`

```csharp
public interface INodeResponseAggregator
```

| Member | Description |
|--------|-------------|
| `NodeOpType TargetOp { get; }` | The per-node operation whose JSON this aggregator handles. |
| `object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>>)` | Reduces per-node responses to one consensus payload. |

Implementations:

| Class | TargetOp | Returns |
|-------|----------|---------|
| `StorageConsensusAggregator` | `SerializeLocal` | `List<FileManifestEntry>` |
| `EpisodeConsensusAggregator` | `StartEpisode` / `StopEpisode` | `EpisodeConsensusPayload` |
| `ReplayConsensusAggregator` | `PrepareReplay` | `ReplayPrepareResult` (max duration) |
| `ReplaySeekAggregator` | `NodeReplaySeek` | `ReplaySeekResult` (first non-zero) |
| `DiagnosticsConsensusAggregator` | `CollectDiagnostics` | `List<FileManifestEntry>` (stripped) |

---

### `StorageGatewayModule`

```csharp
public sealed class StorageGatewayModule
```

| Member | Description |
|--------|-------------|
| `const int MaxParallelCopies = 8` | Maximum concurrent file-copy operations. |
| `Task<GatewayResult> PullToNasAsync(IReadOnlyList<FileManifestEntry>, string, CancellationToken)` | Pulls node files to NAS in parallel. |
| `Task<GatewayResult> PushToNodesAsync(...)` | Pushes files from NAS to nodes. |
| `Task<GatewayResult> PrefetchScenarioAsync(string, List<NodeDistributionTarget>, string)` | Copies scenario files to per-node staging dirs. |
| `Task<GatewayResult> PrefetchArchiveAsync(string, List<NodeDistributionTarget>, string, CancellationToken)` | Copies exercise archive to per-node staging dirs. |
| `IReadOnlyList<string> ScanLocalScenarios(string)` | Returns scenario subdirectory names found at the path. |
| `IReadOnlyList<ExerciseInventoryItem> ScanNasExercises(string)` | Returns exercise metadata from NAS exercise directory. |

---

### `ClusterUiCache`

```csharp
public sealed class ClusterUiCache : IDisposable
```

| Member | Description |
|--------|-------------|
| `ClusterUiCache(FdpEventBus, ITimeController?)` | Creates the cache; optionally binds a local time controller. |
| `void Update()` | Drains all bus event types into published properties. |
| `ClusterState CurrentState { get; }` | Latest cluster state from the bus. |
| `Guid ActiveExerciseId { get; }` | Currently active exercise GUID. |
| `bool IsBootstrapped { get; }` | `true` when cluster is not `Degraded`. |
| `bool HasInFlightTransaction { get; }` | `true` while 2PC is in progress. |
| `string[] AvailableScenarios { get; }` | NAS scenario list from last inventory scan. |
| `ExerciseInventoryItem[] AvailableExercises { get; }` | Unarchived local exercises. |
| `ExerciseInventoryItem[] ArchivedExercises { get; }` | Exercises present on NAS. |
| `double MasterSimTime { get; }` | Current simulation time in seconds. |
| `long MasterWallTicks { get; }` | Latest master wall clock ticks. |
| `float MasterTimeScale { get; }` | Current time scale (1.0 = real-time). |
| `bool IsPaused { get; }` | `true` when in Deterministic / paused mode. |
| `IReadOnlyDictionary<int, NodeHeartbeat> ActiveNodes { get; }` | Live node heartbeat map. |
| `IReadOnlyList<DistributedTransaction> TxHistory { get; }` | Completed transaction history. |
| `DistributedTransaction? ActiveTransaction { get; }` | Most recent in-flight transaction. |
| `IReadOnlySet<Guid> ActiveEpisodes { get; }` | Currently active episode IDs. |
| `IReadOnlyList<FileManifestEntry> LastDiagnosticManifest { get; }` | Stripped manifest from last dump. |
| `float ReplayDuration { get; }` | Duration in seconds of loaded replay. |
| `IReadOnlyList<ClusterState> ReachableTargets { get; }` | Next states reachable from current. |
| `long GetNodeLastSeenMs(int)` | UTC-ms timestamp of last heartbeat for a node ID. |

---

### `ReplayMasterModule`

```csharp
public sealed class ReplayMasterModule
```

| Member | Description |
|--------|-------------|
| `ReplayMasterModule(Action<float>, Func<float>)` | Injects set/get time-scale callbacks. |
| `void FreezeTime()` | Saves current scale and sets it to 0.0 (hard freeze). |
| `void RestoreTime()` | Restores the scale saved by `FreezeTime()`. |
| `float CurrentTimeScale { get; }` | Current scale from the active time controller. |
| `float SavedTimeScale { get; }` | Scale captured at last `FreezeTime()` call. |

---

### `OrchestrationLogicPack`

```csharp
public sealed class OrchestrationLogicPack : IEcsModule
```

| Member | Description |
|--------|-------------|
| `OrchestrationLogicPack(ClusterSlave)` | Wraps a fully configured `ClusterSlave`. |
| `string Name { get; }` | `"OrchestrationLogicPack"`. |
| `ExecutionPolicy Policy { get; }` | `ExecutionPolicy.Synchronous()`. |
| `void RegisterSystems(ISystemRegistry)` | No-op: `ClusterSlave` is not an ECS system. |
| `void Tick(ISimulationView, float)` | Delegates to `ClusterSlave.Tick()`. |

---

### `GlobalContextClusterOpHandler`

```csharp
public sealed class GlobalContextClusterOpHandler : IClusterOpHandler
```

| Member | Description |
|--------|-------------|
| `string LocalTempRoot { get; set; }` | Override path for tests (default `C:\FDP_Temp`). |
| `long LoadedStartWallTicks { get; }` | Wall ticks read from most recently loaded `Orchestrator.json`. |
| `string? LoadedSceneId { get; }` | Scene identifier from loaded context. |
| `double LoadedScenarioTimeSeconds { get; }` | Saved simulation time in seconds. |
| `string? LoadedScenarioId { get; }` | Scenario identifier from loaded context. |
| `FileManifestEntry? CommitManifestEntry { get; }` | Manifest entry produced by `Commit()` for `SerializeLocal`. |
| `event Action<long, double>? OnContextLoaded` | Raised on successful load with `(startWallTicks, simTimeSeconds)`. |

---

### `EventDrivenStorageGateway`

```csharp
public sealed class EventDrivenStorageGateway
```

| Member | Description |
|--------|-------------|
| `EventDrivenStorageGateway(FdpEventBus, IArchiveStorageBackend)` | Injects bus + storage backend. |
| `void Tick()` | Dispatches pending ops and handles cancellation. |

---

### Internal Event Types

| Type | Direction | Description |
|------|-----------|-------------|
| `GlobalContextManifestReadyEvent` | `GlobalContextProcessManager` -> `StorageProcessManager` | Orchestrator's own manifest entry after `SerializeLocal`. |
| `ExecutePrefetchIntent` | `ClusterMaster` -> `AssetPrefetchProcessManager` | Triggers NAS->node scenario prefetch. |
| `PrefetchStagingCompletedEvent` | `AssetPrefetchProcessManager` -> self | Gateway task result; triggers `PrefetchFiles` fan-out. |
| `ExportArchiveBegunEvent` | `ClusterMaster` -> `StorageProcessManager` | Routes completed NAS pull to archive request. |
| `ImportArchiveBegunEvent` | `ClusterMaster` -> `StorageProcessManager` | Triggers NAS-to-node archive prefetch. |
| `MergeLogsIntent` | `ClusterDiagnosticsPanel` -> `DiagnosticLogMergeWorker` | Triggers K-way log merge. |
| `LogMergeCompletedEvent` | `DiagnosticLogMergeWorker` -> `ClusterDiagnosticsPanel` | Merged log file path. |

---

## Dependencies

### Project References

| Project | Namespace / Assembly | Role |
|---------|---------------------|------|
| `Hrot.Common` | `Hrot.Common` | Shared HROT types, diagnostics contracts, `HrotNodeConfig` |
| `Hrot.Network.Orchestration` | `Hrot.Network.Orchestration` | DDS orchestration protocol layer (`IOrchestrationTranslator`, `FileManifestEntry`, `ReplaySeekResult`, etc.) |
| `Fdp.Core` | `Fdp.Core` | `FdpEventBus`, `FdpLog<T>`, ECS fundamentals |
| `Fdp.Toolkits` | `Fdp.Toolkit.*` | `ClusterSlave`, `ClusterState`, `TransitionPlanner`, `MasterSyncController`, time controllers, orchestration events |
| `Fdp.Presentation` | `Fdp.Presentation.*` | `WindowManager`, `ManagedWindow`, `IFileDialogService` |

### InternalsVisibleTo

The `[assembly: InternalsVisibleTo]` attribute exposes internal members to four
test assemblies:

- `Hrot.ClusterRunner.Tests`
- `Hrot.ClusterRunner.Integration.Tests`
- `Hrot.Orchestrator.Tests`
- `Hrot.Orchestrator.Integration.Tests`

### NuGet / Implicit Dependencies

No direct NuGet package references in the `.csproj`.  Dependencies arrive
transitively through `Fdp.Core` (ImGui.NET) and `Hrot.Network.Orchestration`
(CycloneDDS.Runtime).

---

## Usage Examples

### Example 1 -- Standalone Orchestrator startup

This is the production path through `OrchestratorSubsystem`.

```csharp
// Program.cs in the Orchestrator Runner process
var factory = new CycloneDdsNetworkFactory(participant);
var subsystem = new OrchestratorSubsystem(factory);

var config = new SubsystemConfig { NodeId = 300 };
subsystem.Initialize(config);

// Main loop (driven by the host application)
while (running)
{
    float dt = timer.ElapsedSeconds;
    subsystem.Update(dt);
    subsystem.DrawUI();
}

subsystem.Shutdown();
```

---

### Example 2 -- Headless integration test: transition cluster to OperatingLive

```csharp
// In a test that uses InternalsVisibleTo
var bus = new FdpEventBus();
Fdp.Toolkit.Orchestration.OrchestrationEventRegistry.RegisterAll(bus);
OrchestratorEventRegistry.RegisterInternalEvents(bus);

// Build master with empty mandatory list so bootstrap latch is pre-cleared.
var config = new ClusterConfiguration
{
    Mandatory            = Array.Empty<string>(),
    NasBasePath          = @"C:\FDP_Temp\shared",
    TransactionHistoryCapacity = 10,
};
var master = new ClusterMaster(bus, config);

// Register aggregators
master.RegisterAggregator(new StorageConsensusAggregator());
master.RegisterAggregator(new EpisodeConsensusAggregator(NodeOpType.StartEpisode));

// Enqueue a TransitionState request to LoadingLive
var request = new ClusterOpRequest
{
    RequestId     = Guid.NewGuid(),
    OperationType = ClusterOpType.TransitionState,
    PayloadJson   = """{"TargetState":30,"ScenarioId":"hill-attack","ExerciseId":"00000000-0000-0000-0000-000000000000"}""",
};
master.HandleClusterOpRequest(request);

// Execute one tick
bus.SwapBuffers();
master.Tick();

// Assert: transition fan-out was initiated
Assert.AreEqual(ClusterState.LoadingLive, master.CurrentClusterState);
Assert.IsTrue(master.HasInFlightTransaction);
```

---

### Example 3 -- Registering a custom INodeResponseAggregator

```csharp
// Custom aggregator that extracts the minimum free memory from all nodes
public sealed class MinMemoryAggregator : INodeResponseAggregator
{
    public NodeOpType TargetOp => NodeOpType.PrepareReplay;

    public object? Aggregate(
        IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        long minBytes = long.MaxValue;
        foreach (var nodeDict in nodeResponses.Values)
        {
            if (!nodeDict.TryGetValue(TargetOp, out var json)) continue;
            var dto = JsonSerializer.Deserialize<PrepareReplayAck>(json);
            if (dto?.FreeMemoryBytes < minBytes)
                minBytes = dto.FreeMemoryBytes;
        }
        return minBytes == long.MaxValue ? null : minBytes;
    }
}

// Registration (before first Tick())
master.RegisterAggregator(new MinMemoryAggregator());
```

---

### Example 4 -- Using ClusterUiCache as a read-only status monitor

```csharp
// ExCon remote observer (bus-only path, no ClusterMaster reference)
var cache = new ClusterUiCache(sharedBus, localTimeController: null);

void RenderStatus()
{
    cache.Update(); // call once per frame after bus swap

    Console.WriteLine($"Cluster state  : {cache.CurrentState}");
    Console.WriteLine($"Bootstrapped   : {cache.IsBootstrapped}");
    Console.WriteLine($"Master sim time: {cache.MasterSimTime:F1} s");
    Console.WriteLine($"Active nodes   : {cache.ActiveNodes.Count}");
    Console.WriteLine($"Paused         : {cache.IsPaused}");
    Console.WriteLine($"In-flight tx   : {cache.HasInFlightTransaction}");

    foreach (var tx in cache.TxHistory.Take(5))
    {
        Console.WriteLine(
            $"  Tx {tx.TransactionId:B}  " +
            $"target={tx.TargetDsmState}  " +
            $"completed={tx.Completed}  " +
            $"aborted={tx.IsAborted}");
    }
}
```

---

### Example 5 -- OrchestrationLogicPack inside a SimHost node

```csharp
// NodeBootstrapper.cs (SimHost side)
public static OrchestrationLogicPack BuildOrchestration(
    FdpEventBus bus,
    int nodeId)
{
    var slave = new ClusterSlave(nodeId, "SimHost", bus);
    slave.RegisterHandler(new CommitStateHandler(/* ... */));
    slave.RegisterHandler(new SerializeLocalHandler(/* ... */));
    // ... additional handlers ...
    return new OrchestrationLogicPack(slave);
}

// In the ECS module host:
kernel.Install(NodeBootstrapper.BuildOrchestration(bus, nodeId));
```

---

## Best Practices

### Bus swap discipline

`FdpEventBus.SwapBuffers()` must be called **exactly once per frame**, between
the DDS ingress/egress phase and the core logic phase.  Process managers that
must run before `ClusterMaster.Tick()` (e.g. `LiveBranchProcessManager`,
`ReplaySeekProcessManager`) must be ticked in Phase 3 *before* `_clusterMaster.Tick()`.
Those that handle `ClusterOpCompletedEvent` (e.g. `ReplayProcessManager`,
`StorageProcessManager`) must run *after* it.

### Aggregator registration order

All `INodeResponseAggregator` instances must be registered with
`ClusterMaster.RegisterAggregator()` during `Initialize()`, before the first
`Tick()`.  Only one aggregator per `NodeOpType` is supported; later registrations
overwrite earlier ones.

### NAS path configuration

Set `NasBasePath` in `orchestrator-config.json` to a UNC path reachable by all
cluster nodes (`\\NAS\share\FDP_Temp`).  Leave it empty in single-machine
development mode; `AssetPrefetchProcessManager` will skip the gateway copy and
assume files are pre-staged locally.

### Bootstrap latch

`ClusterMaster` rejects all `ClusterOpRequest` messages until
`BootstrapComplete == true`.  List every mandatory subsystem name in
`ClusterConfiguration.Mandatory`.  In headless/test scenarios, pass an empty
`Mandatory` array to skip the bootstrap wait.

### 2PC abort-on-first-failure

`ManageEpisode` operations set `AbortOnFirstFailure = true` in the
`GenericTransactionTracker`: the first node error immediately rejects the
transaction.  All other operations (TransitionState, SerializeLocal,
TakeCheckpoint, ReplaySeek) collect all ACKs before publishing the final status.

### Thread safety

`ClusterMaster.HandleClusterOpRequest()` is the only thread-safe entry point;
it enqueues into a `ConcurrentQueue<ClusterOpRequest>` drained on the next
`Tick()`.  All other methods on `ClusterMaster` and `ClusterUiCache` must be
called from the simulation main thread.

### Diagnostics NAS pull and stripped manifests

`DiagnosticsConsensusAggregator` retains the full manifest (with `SourceUnc`)
internally and exposes it only through `TakeFullManifest()` for
`DiagnosticsDumpProcessManager` to feed into `PullToNasAsync`.  The stripped
manifest (no `SourceUnc`) is what gets embedded in the DDS `ClusterOpStatus`
payload sent to ExCon.

---

## Related Projects

| Project | Relationship |
|---------|-------------|
| `Hrot.Network.Orchestration` | DDS transport layer for all orchestration messages: `ClusterOpRequest/Status`, `NodeOpCommand/Status`, heartbeats, time-sync topics. Implements `IOrchestrationTranslator` and `IMasterTimeTranslators` consumed by `OrchestratorSubsystem`. |
| `Hrot.Common` | Shared HROT infrastructure: `HrotNodeConfig`, `IClusterOpHandler` base types, `DiagnosticsDumpClusterOpHandler`, `LogArchiveExtractionService`. |
| `Fdp.Toolkits` (Fdp.Toolkit.Orchestration) | `ClusterSlave`, `ClusterState` enum, `TransitionPlanner`, `FdpEventBus` orchestration event types (`TransitionStateIntent`, `ClusterOpCompletedEvent`, etc.), `MasterSyncController`. |
| `Fdp.Core` | `FdpEventBus` core, `FdpLog<T>`, `ISubsystem`, ECS contracts. |
| `Fdp.Presentation` | `WindowManager`, `ManagedWindow`, `IFileDialogService`, `WinFormsFileDialogService`. |
| `Hrot.Orchestrator.Tests` | Unit tests for `ClusterMaster`, `ClusterMasterPlanner`, aggregators, and process managers. |
| `Hrot.Orchestrator.Integration.Tests` | End-to-end integration tests that exercise the full 2PC pipeline without DDS. |
| `Hrot.ClusterRunner.Tests` | Runner-level integration tests that instantiate `OrchestratorSubsystem` in headless mode. |
| `Hrot.ClusterRunner.Integration.Tests` | Full cluster integration tests with multiple simulated node stubs. |
| `Hrot.SimHost` | Uses `OrchestrationLogicPack` as its embedded orchestration client; builds `ClusterSlave` via `NodeBootstrapper`. |
| `Hrot.Editor` | Embeds a local `ClusterMaster` for offline single-node editing; uses `ClusterScenarioPanel` and `ClusterControlWindow`. |
