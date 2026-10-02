# DESIGN: ClusterMaster God-Class Refactoring

## Background and Goal

`ClusterMaster` (`Hrot.Orchestrator/ClusterMaster.cs`) is the distributed Two-Phase Commit (2PC)
coordinator for the simulation cluster. Its sole architectural responsibility is:

1. Fan out operations to nodes via `FdpEventBus` (`ExecuteNodeOpIntent`).
2. Count acknowledgements (`NodeOpCompletedEvent`).
3. Reduce per-node JSON payloads using registered `INodeResponseAggregator` instances.
4. Publish a final `ClusterOpCompletedEvent` with an aggregated result payload.

**The problem:** six categories of domain-specific logic have soaked into `ClusterMaster`, turning
it into a "God Class". These must be extracted into decoupled Process Managers and Aggregators so
the 2PC engine stays domain-agnostic.

**Prior work already done:**
- `INodeResponseAggregator` interface exists and is functional.
- `ReplayConsensusAggregator` is implemented (aggregates `PrepareReplay` results).
- `ReplayProcessManager` is implemented (auto-pauses clock when replay ends).
- Both are wired in `OrchestratorSubsystem.Initialize`.

The pattern established by `ReplayProcessManager` is the blueprint for all new Process Managers.

---

## Architecture Pattern

Every extracted domain concern follows the same two-layer pattern:

```
AGGREGATOR (INodeResponseAggregator)
  - Registered with ClusterMaster.RegisterAggregator()
  - Called when all node ACKs arrive for an operation
  - Reduces raw per-node JSON payloads into a strongly-typed result DTO
  - The DTO is attached to ClusterOpCompletedEvent.ResultPayload

PROCESS MANAGER
  - Holds domain dependencies (StorageGatewayModule, ReplayMasterModule, etc.)
  - Ticks every frame in OrchestratorSubsystem.Update(), after ClusterMaster.Tick()
  - Reacts to bus events (TransitionStateIntent, ClusterOpCompletedEvent, ...)
  - Executes domain side-effects entirely outside the 2PC event loop
```

**Wiring point:** `OrchestratorSubsystem.Initialize` -- the same place `ReplayProcessManager` and
`ReplayConsensusAggregator` are already registered.

---

## ClusterMaster Internal Aggregation Extension (Cross-Cutting)

`TryAggregate()` in `ClusterMaster` currently only aggregates responses for `TransitionState`
operations (from `_inflightTransitionTx.NodeResponses`). Two other operation types --
`SerializeLocal` and `ManageEpisode` -- are tracked in separate bespoke dictionaries
(`_pendingSerializeTasks`, `_pendingManageEpisodeTasks`) and bypass the registered aggregator
pipeline.

To allow aggregators to participate in `SerializeLocal` and `ManageEpisode` completion, the
bespoke completion handlers (`HandleSerializeLocalCompletion`, the ManageEpisode ACK loop) must be
modified to:

1. Collect raw `NodeOpCompletedEvent.ResultPayload` as JSON strings keyed by node ID.
2. At operation completion, call the registered aggregator for the operation type (if any).
3. Publish `ClusterOpCompletedEvent` with the aggregated result as `ResultPayload`.
4. Remove the hard-coded side-effect logic from those handlers entirely.

This is the internal ClusterMaster refactoring that enables each Phase 1 task below.

---

## Phase 1: Storage and Episode Extractions

**Goal:** Remove manifest processing, NAS I/O, and episode state tracking from `ClusterMaster`.

### 1.1 StorageConsensusAggregator (SerializeLocal)

**What:** A new class `StorageConsensusAggregator : INodeResponseAggregator` targeting
`NodeOpType.SerializeLocal`. It parses each node's JSON response (a serialized
`List<FileManifestEntry>`) and flattens them into one cluster-wide `List<FileManifestEntry>`.

**Why:** `ClusterMaster` currently owns `_pendingSerializeTasks`, `HandleSerializeLocalCompletion`,
the JSON parsing, and the `_gateway.PullToNasAsync` call. All of that is domain logic that does not
belong in the 2PC coordinator.

**Architectural rule:** The aggregator must be pure -- it does not call `PullToNasAsync`, write
files, or produce any side effects. It only reduces data.

**Note on GlobalContextHandler manifest entry — transitional shim (Phase 1→3):**
`GlobalContextClusterOpHandler.CommitManifestEntry` is a side-channel that
`HandleSerializeLocalCompletion` currently reads to append the orchestrator's own file to the NAS
manifest. Removing the NAS pull from `ClusterMaster` in Phase 1 while deferring the bus-based
coordination to Phase 3 would create a broken intermediate state where `Orchestrator.json` is
silently dropped from every NAS push.

To prevent this, `StorageProcessManager` (TASK-S002) **must** carry a temporary direct reference
to `GlobalContextClusterOpHandler` that it uses only to read `CommitManifestEntry` and prepend it
to the incoming manifest before calling `PullToNasAsync`. This shim is explicitly flagged with a
`// TODO(TASK-P001): replace with bus event` comment and is removed when
`GlobalContextProcessManager` (TASK-P001) takes over manifest-entry publication via the bus.

This constraint is non-negotiable: the `ScenarioSaveLoadTests` integration test must pass at the
end of Phase 1 (TASK-S002), which includes verifying that `Orchestrator.json` is present on the
NAS after a save round.

### 1.2 StorageProcessManager (NAS pull side-effects)

**What:** A new class `StorageProcessManager` that owns the `StorageGatewayModule` reference for
NAS pull operations and reacts to `ClusterOpCompletedEvent` carrying a `List<FileManifestEntry>`
payload.

**Why:** `ClusterMaster.HandleSerializeLocalCompletion` invokes `_gateway.PullToNasAsync` and
`_gateway.WriteScenarioManifestAsync` -- these are I/O side-effects inside the ACK loop.

**What moves out:**
- `HandleSerializeLocalCompletion` method -- deleted from `ClusterMaster`.
- `_pendingSerializeTasks` dictionary and `SerializeLocalTask` inner class -- deleted.
- The `_gateway.PullToNasAsync` / `WriteScenarioManifestAsync` call chain.

**What stays in ClusterMaster:**
- `_gateway` field and `StorageGateway` property remain for asset inventory publishing
  (`PublishAssetInventory`). Only the 2PC-coupled NAS pull calls are extracted.
- `SetStorageGateway` injection method stays (still needed for inventory scanning).
- `FanOutSerializeLocal` method stays (ClusterMaster still initiates the SerializeLocal fan-out;
  it just no longer processes the results itself).

**Archive export path:** The `ExportArchive` operation's `ArchiveCts` tracking in the current
`SerializeLocalTask` also moves into `StorageProcessManager`. The process manager distinguishes
SaveScenario vs ExportArchive paths by checking whether the incoming `ClusterOpCompletedEvent`
carries a `CancellationToken`-tagged context -- the exact mechanism is an implementation detail.

### 1.3 EpisodeConsensusAggregator (ManageEpisode)

**What:** A new class `EpisodeConsensusAggregator : INodeResponseAggregator` that targets
`NodeOpType.StartEpisode` or `NodeOpType.StopEpisode` (operation injected via constructor). Two
instances are registered: one per episode direction.

**Why:** `ClusterMaster._pendingManageEpisodeTasks` and the `_activeEpisodes` mutation logic are
application-layer state tracking, not 2PC infrastructure.

**Architectural rule:** The aggregator signals consensus (all nodes ACKed without error). It does
not mutate episode state. The result payload it returns is used by `EpisodeProcessManager` to
decide whether to add or remove an episode ID.

### 1.4 EpisodeProcessManager (episode state tracking)

**What:** A new class `EpisodeProcessManager` that maintains `ActiveEpisodes` state and reacts to
`ClusterOpCompletedEvent` carrying episode consensus payloads.

**Why:** `_activeEpisodes` and `_pendingManageEpisodeTasks` in `ClusterMaster` are
application-layer state that has no place in the 2PC coordinator.

**What moves out of ClusterMaster:**
- `_activeEpisodes` HashSet.
- `_pendingManageEpisodeTasks` dictionary and `ManageEpisodeTask` inner class.
- `ActiveEpisodes` public property (removed entirely -- see below).
- The `_activeEpisodes.Add` / `_activeEpisodes.Remove` calls in `ConsumeNodeOpStatuses`.

**Encapsulation rule -- no public state on the Process Manager:**
`EpisodeProcessManager` must not expose an `ActiveEpisodes` property. A Process Manager is a black
box; tests must not crack it open to read its internal hash set. Instead, after updating episode
state the manager publishes a dedicated `EpisodeStateChangedEvent` onto the `FdpEventBus` that
carries the full current set of active episode IDs. Tests and other consumers (including
`ClusterUiCache`, which already tracks episodes independently from bus events) subscribe to this
event rather than reading private state.

**Consumers of ActiveEpisodes:** `ClusterMasterEpisodeTests` currently asserts on
`ClusterMaster.ActiveEpisodes`. Those tests must be rewritten to observe
`EpisodeStateChangedEvent` on the bus instead. `ClusterUiCache.ActiveEpisodes` already
independently tracks episode state from `ExecuteNodeOpIntent` events; it is unaffected by this
change.

---

## Phase 2: Temporal Interlock Extractions

**Goal:** Remove `ReplayMasterModule` and `MasterSyncController` dependencies from `ClusterMaster`.

### 2.1 LiveBranchProcessManager (Live-from-Replay time freezing)

**What:** A new class `LiveBranchProcessManager` that subscribes to the event bus and manages the
time-freezing interlock during `OperatingReplay -> LoadingLive` transitions.

**Why:** `ClusterMaster` currently holds `_replayMasterModule`, calls `FreezeTime()` inline when
it detects a Live-from-Replay branch, tracks branch ACKs in `_pendingBranchTasks`, and calls
`RestoreTime()` when all ACKs arrive. This is domain business logic inside the 2PC engine.

**What moves out of ClusterMaster:**
- `_replayMasterModule` field and `SetReplayMasterModule()` injection method.
- `_pendingBranchTasks` dictionary and `BranchTransitionTask` inner class.
- The `isLiveFromReplayBranch` flag, `FreezeTime()` / `RestoreTime()` calls, and the branch ACK
  tracking loop in `ConsumeNodeOpStatuses`.

**Event-driven design:**
- `LiveBranchProcessManager` observes `TransitionStateIntent` on the bus. When the intent
  routes from `OperatingReplay` to `LoadingLive`, the manager calls `FreezeTime()`.
- `ClusterMaster` fans out `NodeOpType.PrepareLive` normally (no special branch path).
- `LiveBranchProcessManager` observes `ClusterOpCompletedEvent`. When a successful completion
  carries a `LiveBranchResult` payload (already emitted by nodes), it calls `RestoreTime()` and
  `_masterSync.SnapAndPause()` with the historical time from that payload.

**MasterSyncController ownership:** `LiveBranchProcessManager` explicitly owns a
`MasterSyncController` dependency alongside `ReplayMasterModule`. When the branch completes, this
manager is the sole caller of `SnapAndPause` for `LiveBranchResult` times. This is not shared with
or deferred to `ReplaySeekProcessManager`, which independently owns its own `MasterSyncController`
reference for seek operations. Both Process Managers receive the same `MasterSyncController`
instance injected from `OrchestratorSubsystem.Initialize`; the instance is shared but each manager
is the unambiguous owner of its own specific clock-manipulation call site.

**Constraint on fan-out removal -- prerequisite audit required:** Before removing the
`isLiveFromReplayBranch` special-case fan-out suppression, the slave-side `ReferenceLiveLoadHandler`
must be audited to confirm it can safely process a `PrepareLive` intent while the node is in
`OperatingReplay` state without corrupting entity state or causing timing collisions. This audit is
captured as TASK-T000 and is a hard prerequisite for TASK-T001.

### 2.2 ReplaySeekAggregator + ReplaySeekProcessManager

**What:** A new `ReplaySeekAggregator : INodeResponseAggregator` targeting `NodeOpType.NodeReplaySeek`
that reduces per-node `ReplaySeekResult` payloads to the first non-default result. A new
`ReplaySeekProcessManager` that handles pre-conditions (pause/slave-set) and post-conditions
(clock snapping) for seek operations.

**Why:** `ClusterMaster.ProcessSeekReplayIntent` directly publishes `SlaveNodeSetUpdatedEvent` and
`PauseTimeIntent`. `ConsumeNodeOpStatuses` fishes for `ReplaySeekResult` payloads and calls
`_masterSync?.SnapAndPause(...)`. A 2PC coordinator has no business manipulating the distributed
lockstep clock.

**What moves out of ClusterMaster:**
- `_masterSync` field and `SetMasterSync()` injection method.
- `SeekResult` field from `BusTransitionAckTracker`.
- The `SlaveNodeSetUpdatedEvent` + `PauseTimeIntent` publications from `ProcessSeekReplayIntent`.
- The `SnapAndPause()` call from the `_pendingBusTransitionAcks` completion handler.

**Event-driven design:**
- `ReplaySeekProcessManager` observes `SeekReplayIntent`. When seen, it publishes
  `SlaveNodeSetUpdatedEvent` and `PauseTimeIntent` before the seek fan-out.
- `ClusterMaster.ProcessSeekReplayIntent` is reduced to only fan out `NodeOpType.NodeReplaySeek`.
- `ReplaySeekAggregator` reduces per-node `ReplaySeekResult` payloads.
- `ReplaySeekProcessManager` observes `ClusterOpCompletedEvent` with `ReplaySeekResult` payload
  and calls `_masterSync.SnapAndPause()`.

**Note:** `_masterSync` is also used in `ConsumeNodeOpStatuses` for the Live-from-Replay branch
ACK path (`SnapAndPause` after `LiveBranchResult`). After Phase 2.1, that call moves to
`LiveBranchProcessManager`, so `_masterSync` can be fully removed from `ClusterMaster`.

---

## Phase 3: Persistence and Prefetch Extractions

**Goal:** Remove file I/O subroutines and NAS staging from `ClusterMaster`.

### 3.1 GlobalContextProcessManager (orchestrator context save/load)

**What:** A new class `GlobalContextProcessManager` that owns `GlobalContextClusterOpHandler` and
manages the local orchestrator context file I/O in response to bus events.

**Why:** `ClusterMaster` currently holds `_globalContextHandler`, calls `PrepareAsync()` and
`Commit()` inside `ProcessStorageOpIntent` (for SaveScenario), and also calls `Commit()` inside
`ProcessTransitionStateIntent` (for LoadingLive / LoadingEdit transitions). This is file I/O
orchestration inside the 2PC engine.

**What moves out of ClusterMaster:**
- `_globalContextHandler` field and `SetGlobalContextHandler()` injection method.
- `PrepareAsync()` + `Commit()` calls from `ProcessStorageOpIntent`.
- `Commit()` calls from `ProcessTransitionStateIntent` for `LoadingLive` / `LoadingEdit` targets.

**Event-driven design:**
- For save: `GlobalContextProcessManager` observes `ExecuteStorageOpIntent` (SaveScenario). It
  calls `PrepareAsync()` and `Commit()` on the handler. After commit, it publishes a bus event
  carrying the `FileManifestEntry` from `CommitManifestEntry` so that `StorageProcessManager`
  can include the orchestrator's own file in the NAS pull.
- For load: `GlobalContextProcessManager` observes `TransitionStateIntent` or
  `ClusterStateTransitionedEvent` for `LoadingLive` / `LoadingEdit` targets and calls `Commit()`
  to restore the context from `Orchestrator.json` and fire `OnContextLoaded`.

**Wiring:** `OrchestratorSubsystem` currently subscribes to `handler.OnContextLoaded` to seed
`MasterSyncController`. That subscription moves to `GlobalContextProcessManager` (or remains in
`OrchestratorSubsystem` -- implementation detail).

### 3.2 AssetPrefetchProcessManager (NAS staging)

**What:** A new class `AssetPrefetchProcessManager` (Saga) that owns the `StorageGatewayModule`
reference for prefetch operations and coordinates file staging before node fan-outs.

**Why:** `ClusterMaster` currently maintains `_pendingPrefetch` (a `PendingPrefetchOp` state
machine), runs `DrainPendingPrefetch()` every tick, and calls `_gateway.PrefetchScenarioAsync`
directly in `ExecutePrefetchScenario`. This blocks 2PC fan-outs waiting on disk I/O -- a
fundamental violation of SRP.

**What moves out of ClusterMaster:**
- `_pendingPrefetch` field and `PendingPrefetchOp` inner class.
- `DrainPendingPrefetch()` method (removed from `Tick()`).
- `ExecutePrefetchScenario()` method.
- Direct `_gateway.PrefetchScenarioAsync` calls for the prefetch path (note: the import archive
  path in `ProcessStorageOpIntent` also calls `PrefetchArchiveAsync` -- that also moves to the
  process manager or stays, depending on whether it needs the same deferral pattern).

**Choreography (Saga pattern):**
1. `ClusterMaster` emits a `ExecutePrefetchIntent` (new or existing intent type) for
   `PrefetchScenario` operations instead of calling `ExecutePrefetchScenario` inline.
2. `AssetPrefetchProcessManager` observes the intent and calls `PrefetchScenarioAsync`. Rather
   than polling `Task.IsCompleted` on every tick, it attaches a `ContinueWith` continuation that
   publishes a `PrefetchStagingCompletedEvent` (new event type) onto the `FdpEventBus` when the
   async task finishes -- whether successfully or faulted. The manager's `Tick()` method only
   reads bus events; it never polls a `Task` directly.
3. When `Tick()` reads a `PrefetchStagingCompletedEvent` with success status, the manager fans out
   `NodeOpType.PrefetchFiles` by publishing `ExecuteNodeOpIntent(PrefetchFiles, ...)` for each
   active node ID (sourced from the event payload or from a snapshot taken at intent time).
4. The manager observes the resulting `ClusterOpCompletedEvent` (PrefetchFiles completion) and
   re-emits the original `TransitionStateIntent` or `ManageEpisodeIntent` to resume the halted
   2PC flow.
5. When `Tick()` reads a `PrefetchStagingCompletedEvent` with failure status, the manager
   publishes `ClusterOpCompletedEvent(requestId, Timeout)`.

**Why `ContinueWith` and not `Task.IsCompleted` polling:** Burning CPU cycles on a 60 Hz game-loop
`Tick()` by polling `Task.IsCompleted` is the same anti-pattern the current `DrainPendingPrefetch`
loop suffers from, just relocated. Publishing a completion event via `ContinueWith` is reactive,
requires zero per-frame CPU overhead while the async copy runs, and is consistent with how all
other state transitions in this architecture are driven -- via bus events, not polling.

---

## Existing Integration Test Safety Net

The following headless integration tests must continue to pass at the end of every phase. They
are the authoritative regression guard for this refactoring:

| Test class | Location | Covers |
|---|---|---|
| `ClusterOpE2eScriptTests` | `Hrot.ClusterRunner.Integration.Tests` | Live-from-Replay branch, seek |
| `CgfRecordingIntegrationTests` | `Hrot.ClusterRunner.Integration.Tests` | ReplaySeek in OperatingReplay |
| `ScenarioSaveLoadTests` | `Hrot.Orchestrator.Integration.Tests` | GlobalContextHandler save/load |
| `EpisodeInjectionTests` | `Hrot.SimHost.Integration.Tests` | StartEpisode / StopEpisode 2PC |
| `DistributedScenarioLoadTests` | `Hrot.ClusterRunner.Integration.Tests` | Prefetch + staging |

Unit tests in `Hrot.Orchestrator.Tests` (`ClusterMasterEpisodeTests`, `ClusterMasterSeekTests`,
`ClusterMasterPrefetchTests`, `ClusterMasterContextHandlerTests`) must also be updated where they
assert on `ClusterMaster` properties that migrate to new classes.

---

## Architectural Constraints

- All new classes live in the `Hrot.Orchestrator` namespace.
- No new project references are required. All dependencies are already in `Hrot.Orchestrator`.
- Process Managers must not call `ClusterMaster` methods directly -- only the `FdpEventBus`.
- Process Managers are ticked in `OrchestratorSubsystem.Update()` after `ClusterMaster.Tick()`,
  following the same pattern as `ReplayProcessManager`.
- Aggregators registered with `ClusterMaster.RegisterAggregator()` must be pure (no side-effects).
- The `INodeResponseAggregator` aggregation pipeline must be extended to cover standalone
  operations (`SerializeLocal`, `ManageEpisode`) in addition to `TransitionState`. This is done
  by modifying the completion logic in `ClusterMaster` for those operations: instead of running
  hard-coded completion handlers, call the registered aggregator (if any) and publish
  `ClusterOpCompletedEvent` with the aggregated payload.
