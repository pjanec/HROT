# TASK DETAIL: ClusterMaster God-Class Refactoring

**Design Reference:** [DESIGN.md](./DESIGN.md)

---

## Phase 1: Storage and Episode Extractions

---

### TASK-S001: StorageConsensusAggregator

**Design Reference:** DESIGN.md § Phase 1.1 -- StorageConsensusAggregator

**Scope**

Implement `StorageConsensusAggregator : INodeResponseAggregator` in `Hrot.Orchestrator`. Extend
`ClusterMaster`'s `SerializeLocal` ACK completion path to call this aggregator and publish a
`ClusterOpCompletedEvent` carrying the aggregated `List<FileManifestEntry>` as `ResultPayload`.

What is NOT in this task:
- The `StorageProcessManager` (TASK-S002).
- Any NAS I/O.
- The `GlobalContextProcessManager` manifest entry coordination (TASK-P001).
- Removal of `HandleSerializeLocalCompletion` from `ClusterMaster` -- that comes in TASK-S002
  after the process manager is in place.

**Constraints**
- The aggregator must implement `INodeResponseAggregator.TargetOp` returning
  `NodeOpType.SerializeLocal` (Fdp.Toolkit.Orchestration namespace).
- `Aggregate()` must not call `PullToNasAsync`, write files, or produce any observable side-effect.
- `Aggregate()` must gracefully skip nodes whose JSON is null, empty, or not a valid
  `List<FileManifestEntry>`.
- The aggregator must be registered in `OrchestratorSubsystem.Initialize` via
  `_clusterMaster.RegisterAggregator(...)`.
- `ClusterMaster`'s `SerializeLocal` completion logic must be modified to: collect raw JSON from
  each `NodeOpCompletedEvent.ResultPayload`, invoke the registered aggregator at the end, and
  publish `ClusterOpCompletedEvent` with the aggregated list as `ResultPayload`. The existing
  NAS pull call in `HandleSerializeLocalCompletion` must remain temporarily (it is removed in
  TASK-S002), guarded so that publishing the event also happens when the aggregator is registered.

**Success Conditions**

1. Setup: a `ClusterMaster` (no-mandatory config) with two registered nodes. Register a
   `StorageConsensusAggregator`. Fan out `SerializeLocal`. Publish two `NodeOpCompletedEvent`
   samples -- one per node -- each with a serialized `List<FileManifestEntry>` containing one
   entry (distinct `RelativeDest` per node).
   Action: tick `ClusterMaster`.
   Assert: the `ClusterOpCompletedEvent` on the bus has `StatusCode == Success` and
   `ResultPayload` is a `List<FileManifestEntry>` with exactly two entries (one per node).

2. Setup: same as above but one node's payload is a malformed JSON string.
   Assert: the aggregated list contains only the one valid entry; no exception is thrown; the
   `ClusterOpCompletedEvent` is still published.

3. Setup: no `StorageConsensusAggregator` registered (default behavior).
   Assert: `ClusterOpCompletedEvent` is still published (backward-compatible); `ResultPayload` is
   null or an empty list. No exception is thrown.

4. `StorageConsensusAggregator` is registered with `RegisterAggregator()`. The
   `_aggregators` dictionary key is `NodeOpType.SerializeLocal`. A second call with the same
   aggregator type replaces the first without error.

---

### TASK-S002: StorageProcessManager

**Design Reference:** DESIGN.md § Phase 1.2 -- StorageProcessManager

**Scope**

Implement `StorageProcessManager` in `Hrot.Orchestrator`. Wire it in `OrchestratorSubsystem`.
Remove `HandleSerializeLocalCompletion`, `_pendingSerializeTasks`, `SerializeLocalTask` from
`ClusterMaster`. Remove `_gateway.PullToNasAsync` and `_gateway.WriteScenarioManifestAsync` calls
from `ClusterMaster`.

What is NOT in this task:
- The `GlobalContextProcessManager` bus-event coordination (TASK-P001) -- see the transitional
  shim constraint below.
- `SetStorageGateway` and the `StorageGateway` property stay in `ClusterMaster` (needed for
  `PublishAssetInventory`).

**Constraints**
- `StorageProcessManager` holds a `StorageGatewayModule`, a `string nasBasePath`, and a
  **temporary transitional shim reference** to `GlobalContextClusterOpHandler`. The shim is used
  only to read `CommitManifestEntry` and prepend it to the incoming manifest before calling
  `PullToNasAsync`, preserving the exact behavior of the deleted `HandleSerializeLocalCompletion`.
  The reference is injected via constructor and marked with a `// TODO(TASK-P001): remove when
  GlobalContextProcessManager publishes manifest entry via bus` comment. Without this shim,
  `Orchestrator.json` would be silently absent from every NAS push between Phase 1 and Phase 3.
- `StorageProcessManager.Tick()` reads `ClusterOpCompletedEvent` from the bus each frame.
- When a `ClusterOpCompletedEvent` with `StatusCode == Success` and
  `ResultPayload is List<FileManifestEntry>` arrives, the manager prepends the orchestrator's own
  manifest entry (from the shim) to the list, then initiates `PullToNasAsync`. On successful pull
  completion it calls `WriteScenarioManifestAsync`.
- `StorageProcessManager` must be ticked in `OrchestratorSubsystem.Update()` after
  `ClusterMaster.Tick()`.
- `ClusterMaster` must not hold a reference to `StorageProcessManager`.
- After this task, `ClusterMaster` must contain zero calls to `_gateway.PullToNasAsync` in the
  `SerializeLocal` completion path.

**Success Conditions**

1. Setup: `StorageProcessManager` wired on a real or in-memory `StorageGatewayModule` and a test
   double `GlobalContextClusterOpHandler` whose `CommitManifestEntry` returns a fixed
   `FileManifestEntry` (e.g., `"Orchestrator.json"`). Publish a `ClusterOpCompletedEvent` with
   `ResultPayload = List<FileManifestEntry>{ one node file entry }` to the bus.
   Action: tick `StorageProcessManager`.
   Assert: `PullToNasAsync` is called with a manifest list that contains BOTH the node file and the
   orchestrator's own entry from `CommitManifestEntry` -- confirming the transitional shim works.

2. Setup: same, but `ResultPayload` is null.
   Assert: `PullToNasAsync` is NOT called.

3. Setup: same, but `ResultPayload` is a `List<FileManifestEntry>` that is empty.
   Assert: `PullToNasAsync` is NOT called (no files to move).

4. `ClusterMaster` contains no reference to `PullToNasAsync` in the `SerializeLocal` completion
   path. Grep / compiler verification.

5. Existing integration test `ScenarioSaveLoadTests.OrchestratorContextRestored_AfterLoad` passes
   without modification (apart from the updated wiring in `OrchestratorSubsystem.Initialize`).
   The test verifies end-to-end that `Orchestrator.json` is present in the final NAS manifest --
   proving the shim produces no regression.

---

### TASK-S003: EpisodeConsensusAggregator and EpisodeProcessManager

**Design Reference:** DESIGN.md § Phase 1.3-1.4 -- Episode extraction

**Scope**

Implement `EpisodeConsensusAggregator : INodeResponseAggregator` (two instances: StartEpisode and
StopEpisode). Implement `EpisodeProcessManager` that maintains internal `_activeEpisodes` state and
publishes `EpisodeStateChangedEvent` on the bus after each mutation. Extend `ClusterMaster`'s
`ManageEpisode` ACK completion path to call the registered aggregator and publish
`ClusterOpCompletedEvent`. Remove `_pendingManageEpisodeTasks`, `ManageEpisodeTask`,
`_activeEpisodes`, and `ActiveEpisodes` from `ClusterMaster`. Update `ClusterMasterEpisodeTests`
to assert on `EpisodeStateChangedEvent` bus messages instead of any property value.

**Constraints**
- `EpisodeConsensusAggregator(NodeOpType targetOp)` constructor sets `TargetOp`.
  The aggregator's `Aggregate()` returns a result payload that encodes the episode operation
  (episode ID + IsStart). The concrete payload type (anonymous object or named DTO) is an
  implementation detail but must be de-serializable by `EpisodeProcessManager`.
- `EpisodeProcessManager` holds a `HashSet<Guid> _activeEpisodes` internally. **It must not
  expose a public `ActiveEpisodes` property or any other state-inspection surface.** The internal
  hash set is the authoritative state; its projection is communicated exclusively via
  `EpisodeStateChangedEvent` published to the `FdpEventBus` after every add/remove operation.
  The event carries the full set of currently active episode IDs at time of publication.
- `EpisodeProcessManager.Tick()` reads `ClusterOpCompletedEvent` and updates `_activeEpisodes`,
  then publishes `EpisodeStateChangedEvent`.
- For NAK (error status code) episodes, `_activeEpisodes` must NOT be updated and
  `EpisodeStateChangedEvent` must NOT be published.
- The zero-node-roster edge case (ClusterMaster immediately publishes Success without any ACKs)
  must still result in `EpisodeProcessManager` publishing `EpisodeStateChangedEvent`.
- `ClusterMaster.ActiveEpisodes` property is removed. All callers (only in
  `ClusterMasterEpisodeTests`) are updated to consume `EpisodeStateChangedEvent` instead.
- Two aggregator instances registered in `OrchestratorSubsystem.Initialize`:
  `EpisodeConsensusAggregator(NodeOpType.StartEpisode)` and
  `EpisodeConsensusAggregator(NodeOpType.StopEpisode)`.

**Success Conditions**

1. Setup: `ClusterMaster` (no-mandatory), one active node, both episode aggregators registered,
   `EpisodeProcessManager` ticking. Send `ManageEpisodeIntent(IsStart=true, EpisodeId=X)`.
   Tick `ClusterMaster`. Publish `NodeOpCompletedEvent(NodeOpType.StartEpisode, Success)`.
   Tick `ClusterMaster`. Tick `EpisodeProcessManager`.
   Assert: `EpisodeStateChangedEvent` was published to the bus and its active-episode set
   contains `X`. (No assertion on `EpisodeProcessManager.ActiveEpisodes`.)

2. Setup: same, then send `ManageEpisodeIntent(IsStart=false, EpisodeId=X)` and ACK it.
   Assert: a subsequent `EpisodeStateChangedEvent` is published whose set does NOT contain `X`.

3. Setup: send `ManageEpisodeIntent(IsStart=true, EpisodeId=X)`. Tick. Publish
   `NodeOpCompletedEvent(NodeOpType.StartEpisode, Failure)`. Tick twice.
   Assert: no `EpisodeStateChangedEvent` is published (NAK must not update state).

4. `ClusterMaster` does not contain `_activeEpisodes`, `_pendingManageEpisodeTasks`, or
   `ActiveEpisodes` after this task. `EpisodeProcessManager` has no public property returning
   episode IDs. Compiler and grep verification.

5. Integration test `EpisodeInjectionTests` passes without modification (wiring changes only).

---

## Phase 2: Temporal Interlock Extractions

---

### TASK-T000: Slave-Side PrepareLive Re-Entrancy Audit

**Design Reference:** DESIGN.md § Phase 2.1 -- prerequisite for LiveBranchProcessManager

**Scope**

Before TASK-T001 removes `isLiveFromReplayBranch` fan-out suppression, confirm that the
slave-side handler for `NodeOpType.PrepareLive` (likely `ReferenceLiveLoadHandler` or equivalent)
can safely process a `PrepareLive` intent while the node is in `OperatingReplay` state. This is a
read-only audit task; it produces no code changes. Its output gates TASK-T001.

**Constraints**
- Read the slave-side handler that processes `NodeOpType.PrepareLive` intents.
- Determine whether receiving `PrepareLive` during `OperatingReplay` (a) is idempotent and safe,
  (b) requires a guard, or (c) corrupts entity or timing state.
- Document the finding in a code comment in `ClusterMaster.ProcessTransitionStateIntent` near the
  `isLiveFromReplayBranch` block, e.g.:
  `// AUDIT(TASK-T000): PrepareLive during OperatingReplay -- [safe / requires guard: reason]`.

**Success Conditions**

1. A code comment as described above is present in `ClusterMaster.ProcessTransitionStateIntent`.

2. One of two outcomes is explicitly chosen and recorded in the comment:
   a. **Safe:** TASK-T001 may remove `isLiveFromReplayBranch` suppression and use the standard
      `TransitionState` fan-out path for Live-from-Replay branches.
   b. **Unsafe:** TASK-T001 must preserve the `isLiveFromReplayBranch` suppression and instead
      have `LiveBranchProcessManager` emit `ExecuteNodeOpIntent(PrepareLive, ...)` directly after
      `FreezeTime()`, bypassing the standard fan-out from `ClusterMaster`.

3. The integration test `ClusterOpE2eScriptTests.LiveFromReplayBranch_Passes` is run (read-only,
   no code change yet) and its current pass/fail status is recorded in the audit comment.

---

### TASK-T001: LiveBranchProcessManager

**Design Reference:** DESIGN.md § Phase 2.1 -- LiveBranchProcessManager

**Prerequisite:** TASK-T000 must be complete before starting this task.

**Scope**

Implement `LiveBranchProcessManager` that owns **both** `ReplayMasterModule` and
`MasterSyncController` dependencies and manages the time-freeze interlock for Live-from-Replay
branch transitions. Remove `_replayMasterModule`, `_pendingBranchTasks`, `BranchTransitionTask`,
and `SetReplayMasterModule` from `ClusterMaster`. Remove `FreezeTime()` / `RestoreTime()` calls
from `ClusterMaster`. Remove the `SnapAndPause` call for `LiveBranchResult` from
`ClusterMaster.ConsumeNodeOpStatuses`. The `isLiveFromReplayBranch` fan-out suppression in
`ProcessTransitionStateIntent` is removed only if the TASK-T000 audit found the slave-side
handler safe; otherwise it is preserved per the TASK-T000 outcome.

What is NOT in this task:
- The `_masterSync.SnapAndPause()` call for seek results -- that is owned exclusively by
  `ReplaySeekProcessManager` (TASK-T002).
- `_masterSync` removal from `ClusterMaster` -- that completes in TASK-T002 after
  `ReplaySeekProcessManager` takes over all remaining `SnapAndPause` call sites.

**Constraints**
- `LiveBranchProcessManager` constructor takes `FdpEventBus`, `ReplayMasterModule`, and
  `MasterSyncController`. This is the explicit and non-negotiable dependency list.
- On `TransitionStateIntent` where the trajectory goes from `OperatingReplay` to `LoadingLive`,
  the manager calls `FreezeTime()` before the 2PC fan-out happens. Because the bus uses a
  double-buffer, the manager must observe the intent in the same frame as `ClusterMaster` to
  guarantee ordering. Wiring in `OrchestratorSubsystem.Update()` must ensure the manager ticks
  before `_bus.SwapBuffers()` if needed, or uses the write buffer directly.
  (The existing `ReplayProcessManager` pattern ticks after `ClusterMaster.Tick()`; the developer
  should verify whether the same ordering works for freeze-before-fan-out.)
- When a `ClusterOpCompletedEvent` arrives with a `LiveBranchResult` payload where
  `HistoricalTime.TotalWallTicks != 0`, the manager calls **both** `RestoreTime()` on
  `ReplayMasterModule` and `_masterSync.SnapAndPause(...)` with the historical time. This manager
  is the sole and authoritative call site for `SnapAndPause` in the branch-result path.
- `LiveBranchProcessManager` is ticked in `OrchestratorSubsystem.Update()` after
  `ClusterMaster.Tick()`.

**Success Conditions**

1. Setup: `ClusterMaster`, one active node in `OperatingReplay`. A `LiveBranchProcessManager`
   wired with a mock `ReplayMasterModule` (tracks `FreezeTime` / `RestoreTime` call counts) and
   a mock `MasterSyncController` (tracks `SnapAndPause` call count and arguments).
   Publish `TransitionStateIntent(OperatingReplay -> LoadingLive)` to the bus.
   Tick the subsystem (bus swap + ClusterMaster.Tick + process manager tick).
   Assert: `FreezeTime()` was called exactly once.

2. Publish `ClusterOpCompletedEvent(Success, ResultPayload=LiveBranchResult{HistoricalTime.TotalWallTicks=42})`.
   Tick the process manager.
   Assert: `RestoreTime()` was called exactly once.
   Assert: `_masterSync.SnapAndPause(42, ...)` was called exactly once by this manager.
   Assert: `ClusterMaster.ConsumeNodeOpStatuses` does NOT contain a `SnapAndPause` call for
   `LiveBranchResult`. Compiler verification.

3. `ClusterMaster` does not contain `_replayMasterModule`, `_pendingBranchTasks`,
   `BranchTransitionTask`, or `SetReplayMasterModule`. Compiler verification.

4. Integration test `ClusterOpE2eScriptTests.LiveFromReplayBranch_Passes` passes. This test verifies
   that entities spawned after the branch are present in the live state.

5. After the branch ACK, the master clock is snapped to historical time by
   `LiveBranchProcessManager` calling `_masterSync.SnapAndPause()`. The call must not originate
   from `ClusterMaster.ConsumeNodeOpStatuses`. Compiler and integration-test verification.

---

### TASK-T002: Replay Seek Extraction

**Design Reference:** DESIGN.md § Phase 2.2 -- ReplaySeek extraction

**Scope**

Implement `ReplaySeekAggregator : INodeResponseAggregator` targeting `NodeOpType.NodeReplaySeek`.
Implement `ReplaySeekProcessManager` that owns `MasterSyncController` and publishes seek
pre-conditions. Remove `_masterSync`, `SetMasterSync`, `SeekResult` field from
`BusTransitionAckTracker`, and `SlaveNodeSetUpdatedEvent`/`PauseTimeIntent` publications from
`ProcessSeekReplayIntent`. Remove `_masterSync?.SnapAndPause()` from `ConsumeNodeOpStatuses`.

**Constraints**
- `ReplaySeekAggregator.Aggregate()` returns the first `ReplaySeekResult` found across node
  responses where `RestoredTime.TotalWallTicks != 0`, or null if none.
- `ReplaySeekProcessManager` subscribes to `SeekReplayIntent` and publishes
  `SlaveNodeSetUpdatedEvent` and `PauseTimeIntent` before the seek ACKs arrive.
- `ReplaySeekProcessManager` subscribes to `ClusterOpCompletedEvent`. When a success event
  carries a `ReplaySeekResult` payload with `TotalWallTicks != 0`, it calls
  `_masterSync.SnapAndPause()`.
- `ProcessSeekReplayIntent` in `ClusterMaster` is reduced to fan-out `NodeOpType.NodeReplaySeek`
  and set up the ACK tracker. No time-control logic remains in it.
- `SeekResult` field is removed from `BusTransitionAckTracker`. `ClusterMaster` no longer
  inspects `ReplaySeekResult` from node payloads.
- `ReplaySeekProcessManager` is ticked in `OrchestratorSubsystem.Update()` after
  `ClusterMaster.Tick()`.
- After this task, `_masterSync` is fully removed from `ClusterMaster`.

**Success Conditions**

1. Setup: `ClusterMaster`, one active node in `OperatingReplay`. `ReplaySeekAggregator`
   registered. `ReplaySeekProcessManager` wired with a mock `MasterSyncController`.
   Publish `SeekReplayIntent(TargetWallTicks=1000)` to the bus. Tick the subsystem.
   Assert: `SlaveNodeSetUpdatedEvent` and `PauseTimeIntent` were published to the bus by
   the process manager (not by `ClusterMaster`). The `NodeOpType.NodeReplaySeek` fan-out was
   emitted by `ClusterMaster`.

2. Publish `NodeOpCompletedEvent(NodeOpType.NodeReplaySeek, Success,
   ResultPayload=ReplaySeekResult{RestoredTime.TotalWallTicks=5000, TotalTime=10.0})`.
   Tick `ClusterMaster`. Tick `ReplaySeekProcessManager`.
   Assert: `_masterSync.SnapAndPause(5000, 10.0, ...)` was called by `ReplaySeekProcessManager`.
   Assert: `ClusterMaster` did NOT call `SnapAndPause`.

3. Setup: node returns `ReplaySeekResult{RestoredTime.TotalWallTicks=0}` (default/no-op result).
   Assert: `SnapAndPause` is NOT called.

4. `ClusterMaster` does not contain `_masterSync`, `SetMasterSync`, or any call to `SnapAndPause`.
   Compiler verification.

5. Integration tests `ClusterOpE2eScriptTests.RecordAndReplaySeek_Passes` and
   `CgfRecordingIntegrationTests` pass.

---

## Phase 3: Persistence and Prefetch Extractions

---

### TASK-P001: GlobalContextProcessManager

**Design Reference:** DESIGN.md § Phase 3.1 -- GlobalContextProcessManager

**Scope**

Implement `GlobalContextProcessManager` that owns `GlobalContextClusterOpHandler` and manages
local orchestrator context save/load via bus events. Remove `_globalContextHandler` field,
`SetGlobalContextHandler()`, and all `_globalContextHandler.*` call sites from `ClusterMaster`.

**Constraints**
- `GlobalContextProcessManager` holds a `GlobalContextClusterOpHandler` and subscribes to the
  `FdpEventBus`.
- For save: the manager observes `ExecuteStorageOpIntent(StorageOpType.SaveScenario)`. It calls
  `PrepareAsync()` then `Commit()` on the handler. After commit, it publishes a bus event (new or
  reused type) carrying `CommitManifestEntry` so that `StorageProcessManager` can include the
  orchestrator's own file in the subsequent NAS pull. The exact bus event type is an
  implementation detail; the success condition verifies the end-to-end manifest inclusion.
- For load: the manager observes `TransitionStateIntent` or `ClusterStateTransitionedEvent` for
  `LoadingLive` / `LoadingEdit` target states. It calls `Commit()` on the handler to restore
  context.
- `OnContextLoaded` subscription for seeding `MasterSyncController` either remains in
  `OrchestratorSubsystem` or moves into `GlobalContextProcessManager` -- developer's choice.
- `GlobalContextProcessManager` is ticked in `OrchestratorSubsystem.Update()` after
  `ClusterMaster.Tick()`.
- `ClusterMaster` must contain zero references to `GlobalContextClusterOpHandler` after this task.
- `ClusterMaster.HandleSerializeLocalCompletion` currently reads
  `_globalContextHandler?.CommitManifestEntry`. After this task, that read is replaced by the
  `StorageProcessManager` receiving the orchestrator manifest entry via the bus.

**Success Conditions**

1. Setup: `GlobalContextProcessManager` with a `GlobalContextClusterOpHandler` (real or test
   double with temp directory). Publish `ExecuteStorageOpIntent(SaveScenario)` to the bus.
   Tick the process manager.
   Assert: `Orchestrator.json` is written to the expected directory.
   Assert: a bus event carrying `FileManifestEntry` referencing the written file is published.

2. Setup: publish `TransitionStateIntent(TargetState=LoadingLive, ScenarioId="test_scenario")`.
   Pre-condition: `Orchestrator.json` exists at the expected path.
   Tick the process manager.
   Assert: `OnContextLoaded` fires with the saved wall ticks and sim-time values.
   Assert: `ClusterMaster` does NOT call `_globalContextHandler.Commit()` directly.

3. Integration test `ScenarioSaveLoadTests.OrchestratorContextRestored_AfterLoad` passes.
   Integration test `ClusterMasterContextHandlerTests.TransitionState_LoadingLive_InvokesLocalContextHandler`
   passes (test may need update to wire `GlobalContextProcessManager` instead of
   `ClusterMaster.SetGlobalContextHandler`).

4. `ClusterMaster` does not contain `_globalContextHandler`, `SetGlobalContextHandler`, or any
   reference to `GlobalContextClusterOpHandler`. Compiler verification.

5. End-to-end: SaveScenario → SerializeLocal fan-out → all ACKs → `StorageConsensusAggregator`
   produces manifest including the orchestrator's file → `StorageProcessManager` calls
   `PullToNasAsync` with all files including `Orchestrator.json`.

---

### TASK-P002: AssetPrefetchProcessManager

**Design Reference:** DESIGN.md § Phase 3.2 -- AssetPrefetchProcessManager

**Scope**

Implement `AssetPrefetchProcessManager` (Saga) that owns `StorageGatewayModule` for prefetch
operations. Remove `_pendingPrefetch`, `PendingPrefetchOp`, `DrainPendingPrefetch()`,
`ExecutePrefetchScenario()`, and `_gateway.PrefetchScenarioAsync` calls from `ClusterMaster`.
Remove `DrainPendingPrefetch()` from `ClusterMaster.Tick()`.

**Constraints**
- `AssetPrefetchProcessManager` holds a `StorageGatewayModule` and subscribes to the
  `FdpEventBus`.
- When `ClusterMaster` encounters a `PrefetchScenario` step in a trajectory, it emits a bus event
  (e.g., `ExecutePrefetchIntent` with `ScenarioId` and originating `RequestId`) instead of calling
  `ExecutePrefetchScenario` inline.
- `AssetPrefetchProcessManager.Tick()` observes this intent. It calls `PrefetchScenarioAsync`,
  then immediately attaches a `ContinueWith` continuation (using `TaskScheduler.Default` to avoid
  context-capture issues) that publishes a `PrefetchStagingCompletedEvent` onto the `FdpEventBus`
  when the task finishes -- success or failure. The `Tick()` method must contain **zero** direct
  `Task.IsCompleted` polls or `Task.Wait` calls. The entire async completion flow is reactive.
- A new event type `PrefetchStagingCompletedEvent` is defined in `Hrot.Orchestrator`. Fields:
  `Guid RequestId`, `string ScenarioId`, `bool IsSuccess`.
- When `Tick()` reads a `PrefetchStagingCompletedEvent` with `IsSuccess = true`, the manager
  fans out `NodeOpType.PrefetchFiles` by publishing `ExecuteNodeOpIntent(PrefetchFiles, ...)`
  for each active node ID (captured at intent observation time).
- The manager then observes the resulting `ClusterOpCompletedEvent` for the PrefetchFiles
  round-trip and re-emits the original `TransitionStateIntent` or `ManageEpisodeIntent` to resume
  the halted trajectory.
- When `Tick()` reads a `PrefetchStagingCompletedEvent` with `IsSuccess = false`, it publishes
  `ClusterOpCompletedEvent(RequestId, StatusCode=Timeout)`.
- `ClusterMaster` must not call `_gateway.PrefetchScenarioAsync` or hold `_pendingPrefetch` after
  this task.
- `ClusterMaster` retains the `_gateway` field and `StorageGateway` property for
  `PublishAssetInventory` (no change to asset scanning).
- `AssetPrefetchProcessManager` is ticked in `OrchestratorSubsystem.Update()` after
  `ClusterMaster.Tick()`.

**Success Conditions**

1. Setup: `AssetPrefetchProcessManager` with a real or stubbed `StorageGatewayModule` that
   returns a completed `Task` immediately. Publish a
   `ExecutePrefetchIntent(ScenarioId="s1", RequestId=X)` to the bus.
   Action: tick the process manager once to pick up the intent; then advance one more tick to pick
   up the `PrefetchStagingCompletedEvent` that the `ContinueWith` continuations placed on the bus.
   Assert: `PrefetchScenarioAsync` was called exactly once with `ScenarioId="s1"`.
   Assert: `ExecuteNodeOpIntent(PrefetchFiles)` events are published to the bus (one per active node).
   Assert: the process manager's `Tick()` contains no `Task.IsCompleted` call. Code review or
   grep verification.

2. Setup: `PrefetchScenarioAsync` is stubbed to fail (throws or returns a faulted task).
   Tick the process manager until `PrefetchStagingCompletedEvent` is processed.
   Assert: `ClusterOpCompletedEvent(RequestId=X, StatusCode=Timeout)` is published.
   Assert: no `ExecuteNodeOpIntent(PrefetchFiles)` was published.

3. `ClusterMaster` does not contain `_pendingPrefetch`, `PendingPrefetchOp`,
   `DrainPendingPrefetch`, or `ExecutePrefetchScenario`. Compiler verification.

4. Integration test `DistributedScenarioLoadTests` (prefetch + staging) passes.

3. Setup: full saga -- `ExecutePrefetchIntent` → `PrefetchScenarioAsync` success → `PrefetchFiles`
   ACKs from nodes → process manager re-emits original `TransitionStateIntent`.
   Assert: `TransitionStateIntent` is published exactly once (the re-emission drives the
   continuation).

4. `ClusterMaster` does not contain `_pendingPrefetch`, `PendingPrefetchOp`,
   `DrainPendingPrefetch`, or `ExecutePrefetchScenario` after this task. Compiler verification.

5. `ClusterMaster.Tick()` does not call `DrainPendingPrefetch`. Compiler verification.

6. Integration test `DistributedScenarioLoadTests` passes end-to-end, verifying that network
   references are correctly patched after the prefetch and staging phases complete.
