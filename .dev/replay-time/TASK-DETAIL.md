# Replay Time Control - Task Details

**Reference:** See [DESIGN.md](./DESIGN.md) for architecture and phase descriptions.

---

## Phase 1: Pull Model Infrastructure

---

### TASK-RT-001: Expose `GetTimeController()` on `ModuleHostKernel`

**Design Reference:** [Phase 1](./DESIGN.md#phase-1-pull-model-infrastructure)

**Scope:**
- Add `public ITimeController GetTimeController()` to `ModuleHostKernel`.
- Returns `_timeController` (throws `InvalidOperationException` if not yet set,
  consistent with existing guard in `UpdateInternal`).
- No other changes to the kernel.

**Constraints:**
- Return type must be `ITimeController` (from `Fdp.ModuleHost.Time`), not a concrete type.
- Must not allow callers to replace the controller via this getter (read-only access only).

**Files:**
- `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs`

**Success Conditions:**
- T1a: `kernel.SetTimeController(controller); kernel.GetTimeController()` returns the same
  instance.
- T1b: Calling `GetTimeController()` before `SetTimeController()` throws
  `InvalidOperationException`.

---

### TASK-RT-002: `ReplayModule` constructor accepts `ITimeController`

**Design Reference:** [Phase 1](./DESIGN.md#phase-1-pull-model-infrastructure)

**Scope:**
- Add `ITimeController timeController` parameter to `ReplayModule(string, EntityRepository, ...)`.
- Store it as a field `_timeController`.
- Pass it to `PlaybackTickSystem` in `RegisterSystems`.

**Constraints:**
- `timeController` must not be null (throw `ArgumentNullException`).
- `ReplayModule.SetExtraFramesThisTick` can be kept for backward compat during the transition
  but is no longer the primary driving mechanism once `PlaybackTickSystem` is refactored.

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Replay/ReplayModule.cs`

**Success Conditions:**
- T2a: Constructing `ReplayModule` without a `timeController` throws `ArgumentNullException`.
- T2b: After construction, `RegisterSystems` passes the controller to the created
  `PlaybackTickSystem`.

---

### TASK-RT-003: Refactor `PlaybackTickSystem` to use `ITimeController.TotalWallTicks`

**Design Reference:** [Phase 1](./DESIGN.md#phase-1-pull-model-infrastructure)

**Scope:**
- Add `ITimeController _timeController` field, injected via constructor.
- Remove `ExtraFramesThisTick` public property (or mark it `[Obsolete]`).
- Replace `Execute` body with the smart-cursor algorithm:

  ```
  1. Read targetTicks = _timeController.GetCurrentState().TotalWallTicks
  2. Read currentTicks = (_playback.CurrentFrame >= 0)
                         ? _playback.GetFrameMetadata(_playback.CurrentFrame).WallClockTicks
                         : long.MinValue
  3. If targetTicks <= currentTicks -> return (paused or no advance)
  4. If gap is small (next frame wall ticks <= targetTicks and targetTicks is within
     StrategyBThreshold frames' worth): StepForward loop
  5. Otherwise: SeekToWallClockTicks + ForceMarkAllDirty + _afterSeek?.Invoke()
  ```

  "Small gap" definition: advance at most `StrategyBThreshold` (currently 3) frames by
  stepping forward; if the target requires more than 3 steps, use `SeekToWallClockTicks`.

**Constraints:**
- If `_playback.IsAtStart` (current frame = -1), treat `currentTicks = long.MinValue` so
  the first tick always advances to position 0 or the matching frame.
- `SeekToWallClockTicks` and `StepForward` are synchronous; do NOT spawn `Task.Run`.
- `ForceMarkAllDirty` and `_afterSeek` must only be called on the seek/jump path (not on
  single-step forward path).

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Replay/PlaybackTickSystem.cs`

**Success Conditions:**
- T3a: When `_timeController.GetCurrentState().TotalWallTicks == currentFrameWallClockTicks`,
  `Execute` returns without touching the repo.
- T3b: When `targetTicks` equals the next frame's wall ticks (1 frame gap), `StepForward` is
  called exactly once and `SeekToWallClockTicks` is NOT called.
- T3c: When `targetTicks` is far ahead (more than `StrategyBThreshold` frames), only
  `SeekToWallClockTicks` is called, then `ForceMarkAllDirty`, then `_afterSeek`.
- T3d: When `targetTicks` is at the start of the recording (before any frame), `Execute`
  calls `StepForward` to advance to frame 0.
- T3e: `ExtraFramesThisTick` setter (if kept) has no effect on the cursor logic when a
  time controller is present.

---

### TASK-RT-004: Wire time controller through `EcsRecordReplayController`

**Design Reference:** [Phase 1](./DESIGN.md#phase-1-pull-model-infrastructure)

**Scope:**
- In `EcsRecordReplayController.PrepareReplayAsync`, pass `_kernel.GetTimeController()` as
  the `timeController` argument when constructing `ReplayModule`.

**Constraints:**
- This is the only change to `EcsRecordReplayController` in this task.
- `_kernel` is already available as a field; no new constructor parameters are needed.

**Files:**
- `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`

**Success Conditions:**
- T4a: Integration test: after `PrepareReplayAsync`, the `ReplayModule` created internally
  holds the same `ITimeController` instance that was active on the kernel at call time.

---

## Phase 2: Atomic Snap-and-Pause

---

### TASK-RT-005: Extract `ApplyTimeSnap` from `SlaveSyncController.ApplyResume`

**Design Reference:** [Phase 2](./DESIGN.md#phase-2-atomic-snap-and-pause)

**Scope:**
- Extract the time-anchoring block from `ApplyResume` into a new private method
  `ApplyTimeSnap(SwitchTimeModeEvent evt)`.
- `ApplyResume` calls `ApplyTimeSnap` and then sets `_mode = SlaveMode.Continuous`.
- `ApplyTimeSnap` contains only the clock-baseline assignments:
  - `_baselineSimTime`, `_baselineUnscaledTime` from `evt.SimTimeSnapshot`
  - `_timeScale` from `evt.TimeScale`
  - `_baselineWallTicks` from `evt.BarrierWallTicks` (or fallback)
  - Clear `_pendingBarrierWallTicks = -1`

**Constraints:**
- Existing behavior of `ApplyResume` must be identical after refactor (all existing
  unit tests must pass unchanged).
- `ApplyTimeSnap` must remain `private`.

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SlaveSyncController.cs`

**Success Conditions:**
- T5a: All existing `SlaveSyncController` unit tests pass without modification.
- T5b: `ApplyResume` still transitions `_mode` to `Continuous` after calling `ApplyTimeSnap`.

---

### TASK-RT-006: Instant snap-and-pause in `DrainModeSwitchEvents`

**Design Reference:** [Phase 2](./DESIGN.md#phase-2-atomic-snap-and-pause)

**Scope:**
- In `SlaveSyncController.DrainModeSwitchEvents`, for `evt.TargetMode == Deterministic`:
  - If `SyncedWallTicks >= evt.BarrierWallTicks`:
    - Call `ApplyTimeSnap(evt)`.
    - Set `_mode = SlaveMode.Stepping`.
    - Clear `_pendingIntents`.
    - Reset `_lastAcceptedStepFrameId = -1L`.
  - Else (barrier still in the future, existing behaviour):
    - Set `_pendingBarrierWallTicks = evt.BarrierWallTicks`.
    - Set `_mode = SlaveMode.BarrierPending`.
    - Clear `_pendingIntents`.
    - Reset `_lastAcceptedStepFrameId = -1L`.

**Constraints:**
- The `else` branch for a future barrier must be **identical** to the existing code. All
  existing unit tests (which set `BarrierWallTicks` to a large future value) must pass.
- The instant-snap path is triggered ONLY when `SyncedWallTicks >= evt.BarrierWallTicks`
  for a `Deterministic` event. It is not triggered for `Continuous` events.

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SlaveSyncController.cs`

**Success Conditions:**
- T6a: When a `SwitchTimeModeEvent(Deterministic)` arrives with `BarrierWallTicks = now`
  (i.e. `_getTick()` at send time), the slave transitions to `Stepping` in the same frame
  it processes the event (no frame in `BarrierPending`).
- T6b: The slave's `_baselineSimTime` and `_baselineWallTicks` are snapped to the values
  carried in the event's `SimTimeSnapshot` and `BarrierWallTicks`.
- T6c: When `BarrierWallTicks` is in the future (`_getTick() + largeValue`), the slave
  transitions to `BarrierPending` (existing behaviour unchanged).
- T6d: `GetMode()` returns `TimeMode.Deterministic` immediately after the instant snap.

---

## Phase 3: 2PC Seek Fix in the Orchestrator

---

### TASK-RT-007: Remove premature `PublishOpStatus` from `ReplaySeek` case

**Design Reference:** [Phase 3](./DESIGN.md#phase-3-2pc-seek-fix-in-the-orchestrator)

**Scope:**
- In `ClusterMaster.ProcessSingleClusterOpRequest`, in the `ClusterOpType.ReplaySeek` case,
  delete the line `PublishOpStatus(req.RequestId, OrchestrationStatusCode.Success);`.
- The remaining call to `ProcessSeekReplayIntent` must stay.

**Constraints:**
- No other cases in the switch may be changed.

**Files:**
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

**Success Conditions:**
- T7a: After calling `ProcessSingleClusterOpRequest` with `ClusterOpType.ReplaySeek`,
  no `ClusterOpCompletedEvent` with `Success` is published before any node ACK arrives.

---

### TASK-RT-008: Refactor `ProcessSeekReplayIntent` with `BusTransitionAckTracker`

**Design Reference:** [Phase 3](./DESIGN.md#phase-3-2pc-seek-fix-in-the-orchestrator)

**Scope:**
- Refactor `ProcessSeekReplayIntent(SeekReplayIntent intent)` to:
  1. Collect `seekNodeIds = new List<int>(_roster.ActiveNodes.Keys)`.
  2. If `seekNodeIds.Count == 0`: `PublishOpStatus(intent.RequestId, Success)` and return.
  3. Generate `txId = Guid.NewGuid()`.
  4. Fan out: `FanOutNodeOp(NodeOpType.NodeReplaySeek, txId, new ReplaySeekPayload(intent.TargetWallTicks), seekNodeIds)`.
  5. Register: `_pendingBusTransitionAcks[txId] = new BusTransitionAckTracker { RequestId = intent.RequestId, Expected = seekNodeIds.Count }`.

**Constraints:**
- The existing `ConsumeNodeOpStatuses` resolution loop is the completion path; no new loop
  is needed.
- The `SeekReplayIntent` overload in `ProcessSeekReplayIntents()` (drain loop) calls the
  same method - no change needed there.

**Files:**
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

**Success Conditions:**
- T8a: After `ProcessSeekReplayIntent`, `_pendingBusTransitionAcks` contains exactly one
  entry keyed by the generated `txId` with `Expected == seekNodeIds.Count`.
- T8b: When all nodes ACK via `NodeOpCompletedEvent` with the tracked `txId`, the
  `ConsumeNodeOpStatuses` loop publishes a final `ClusterOpCompletedEvent(Success)` with
  the original `intent.RequestId`.
- T8c: With zero active nodes, `PublishOpStatus(Success)` is called immediately and nothing
  is inserted into `_pendingBusTransitionAcks`.

---

### TASK-RT-009: Server-side pause precondition in `ProcessSeekReplayIntent`

**Design Reference:** [Phase 3](./DESIGN.md#phase-3-2pc-seek-fix-in-the-orchestrator)

**Scope:**
- At the top of `ProcessSeekReplayIntent`, before the fan-out:
  1. Build `slaveIds`: filter `_roster.ActiveNodes` to nodes with `SubsystemName` in
     `{ "SimHost", "IG", "CGF" }` (same filter used in the existing `PrepareReplay`
     transition block, lines ~437-438 of `ClusterMaster.cs`).
  2. `_eventBus.PublishManaged(new SlaveNodeSetUpdatedEvent { SlaveNodeIds = slaveIds })`.
  3. `_eventBus.PublishManaged(new PauseTimeIntent())`.
- These events are drained by `MasterSyncController.Update()` on the next kernel frame,
  which calls `SwitchToDeterministic` and broadcasts `SwitchTimeModeEvent(Deterministic)`.

**Constraints:**
- Do not call `SwitchToDeterministic` directly; keep the existing event-driven decoupling.
- The pause is issued even if `seekNodeIds.Count == 0` (guard for that returns early before
  this code but the pause itself does no harm if issued; it is simpler to keep the ordering
  clean by placing the guard after the pause lines).
  Actually: place the pause BEFORE the empty-check so the guard early-return does not
  skip it; or reorder to pause first, guard second. Discuss in implementation.

**Files:**
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

**Success Conditions:**
- T9a: `ProcessSeekReplayIntent` always publishes `SlaveNodeSetUpdatedEvent` and
  `PauseTimeIntent` on the event bus before fanning out `NodeReplaySeek`.
- T9b: Unit test: after calling `ProcessSeekReplayIntent`, the managed event bus contains
  `SlaveNodeSetUpdatedEvent` and `PauseTimeIntent` events.

---

## Phase 4: Seek Time Payload Propagation

---

### TASK-RT-010: Define `ReplaySeekResult` payload struct

**Design Reference:** [Phase 4](./DESIGN.md#phase-4-seek-time-payload-propagation)

**Scope:**
- Add `public readonly record struct ReplaySeekResult(GlobalTime RestoredTime)` to
  `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs` (same file as
  `ReplaySeekPayload` and other payload types).

**Constraints:**
- Must be JSON-serializable (record struct with a single `GlobalTime` field).
- `GlobalTime` is in `Fdp.Core`; no new project reference is needed.

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs`

**Success Conditions:**
- T10a: `System.Text.Json.JsonSerializer.Serialize(new ReplaySeekResult(gt))` and
  `Deserialize<ReplaySeekResult>(json)` round-trip correctly.

---

### TASK-RT-011: Change `IRecordReplayController.SeekToTimeAsync` return type

**Design Reference:** [Phase 4](./DESIGN.md#phase-4-seek-time-payload-propagation)

**Scope:**
- Change `Task SeekToTimeAsync(long targetWallClockTicks)` to
  `Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks)` in
  `IRecordReplayController`.
- Update all XML docs to note the return value.

**Constraints:**
- `IRecordReplayController` is in `Fdp.Core` which must not reference `Hrot.*` types.
  `GlobalTime` is already in `Fdp.Core` so no new dependency is introduced.

**Files:**
- `FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs`

**Success Conditions:**
- T11a: Compiler error if any implementation still returns `Task` (void) after this change.

---

### TASK-RT-012: Update `SeekToTimeAsync` implementations

**Design Reference:** [Phase 4](./DESIGN.md#phase-4-seek-time-payload-propagation)

**Scope:**
- `EcsRecordReplayController.SeekToTimeAsync`:
  - Await `_activeReplayModule.SeekToWallClockTicksAsync(targetWallClockTicks)`.
  - After the task completes, read `_repo.GetSingletonUnmanaged<GlobalTime>()` and return it.
  - If `_activeReplayModule == null`: log a warning and return `default(GlobalTime)`.
- `ListenerRecordReplayController.SeekToTimeAsync`: return `Task.FromResult(default(GlobalTime))`.
- `CgfRecordReplayController.SeekToTimeAsync`: return `Task.FromResult(default(GlobalTime))`.

**Constraints:**
- The ECS repo read happens on the **same thread** that awaits the background task; the
  background `Task.Run` in `ReplayModule.SeekToWallClockTicksAsync` finishes before the
  `await` returns, so the repo is in a consistent post-seek state.
- Listener and CGF controllers never have ECS state - `default(GlobalTime)` is correct.

**Files:**
- `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`
- `Hrot/Network/Hrot.Network.Orchestration/ListenerRecordReplayController.cs`
- `Hrot/Subsystems/Hrot.CGF/Modules/Orchestration/CgfRecordReplayController.cs`

**Success Conditions:**
- T12a: `EcsRecordReplayController.SeekToTimeAsync` returns a `GlobalTime` whose
  `TotalWallTicks` equals the wall-clock tick of the frame the `PlaybackController` landed on.
- T12b: `ListenerRecordReplayController.SeekToTimeAsync` returns `default(GlobalTime)`.
- T12c: `CgfRecordReplayController.SeekToTimeAsync` returns `default(GlobalTime)`.

---

### TASK-RT-013: `ReferenceReplayLoadHandler` returns `ReplaySeekResult` for `NodeReplaySeek`

**Design Reference:** [Phase 4](./DESIGN.md#phase-4-seek-time-payload-propagation)

**Scope:**
- In `PrepareAsync`, the `NodeReplaySeek` branch currently returns `null`. Change it to:
  1. `GlobalTime restoredTime = await _controller.SeekToTimeAsync(targetTicks)`.
  2. Return `new ReplaySeekResult(restoredTime)`.

**Constraints:**
- `PrepareAsync` return type is already `Task<object?>`, so returning a `ReplaySeekResult`
  is a widening - no signature change needed.
- Log statement may keep existing content; add `restoredTime.TotalWallTicks` to the log.

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs`

**Success Conditions:**
- T13a: `PrepareAsync` for `NodeReplaySeek` returns a non-null `ReplaySeekResult` with
  `RestoredTime.TotalWallTicks` equal to the seek target (within one frame's tolerance).
- T13b: `ListenerRecordReplayController` and `CgfRecordReplayController` paths return a
  `ReplaySeekResult` with `RestoredTime == default(GlobalTime)`.

---

### TASK-RT-014: Add `SnapAndPause` method to `MasterSyncController`

**Design Reference:** [Phase 4](./DESIGN.md#phase-4-seek-time-payload-propagation)

**Scope:**
- Add `public void SnapAndPause(long targetWallTicks, double targetSimTime, HashSet<int> slaveNodeIds)`.
- Implementation:
  1. Update internal state: `_totalWallTicks = targetWallTicks`, `_totalTime = targetSimTime`.
  2. Set `_mode = MasterMode.Stepping` directly (no barrier pending).
  3. Clear `_pendingAcks`.
  4. Update `_expectedSlaves`: clear and union with `slaveNodeIds`.
  5. Publish `SwitchTimeModeEvent`:
     - `TargetMode = Deterministic`
     - `BarrierWallTicks = _getTick()` (current real tick - in the past by the time slaves receive it)
     - `SimTimeSnapshot = targetSimTime`
     - `TimeScale = _timeScale`
     - `FixedDelta = _config.FixedDeltaSeconds`
  6. Reset `_lastTickSample = _getTick()`.

**Constraints:**
- `_getTick()` is the existing `Func<long>` injected at construction; use it here.
- Setting `_mode = MasterMode.Stepping` directly bypasses `_pendingBarrierWallTicks`.
  Existing `UpdateBarrierPending` and `UpdateStepping` are unaffected.
- `slaveNodeIds` may be null (treat as empty set).

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs`

**Success Conditions:**
- T14a: After `SnapAndPause(t, s, slaves)`, `GetCurrentState().TotalWallTicks == t`.
- T14b: After `SnapAndPause`, `GetMode() == TimeMode.Deterministic`.
- T14c: `SnapAndPause` publishes exactly one `SwitchTimeModeEvent` with `TargetMode == Deterministic`
  and `SimTimeSnapshot == targetSimTime` and `BarrierWallTicks <= _getTick()` at the time of
  the call.
- T14d: After `SnapAndPause`, calling `Update()` keeps the controller in `Stepping` mode
  (does not advance time until `Step()` is called).

---

### TASK-RT-015: Master clock snap in `ConsumeNodeOpStatuses` after seek

**Design Reference:** [Phase 4](./DESIGN.md#phase-4-seek-time-payload-propagation)

**Scope:**
- In `ClusterMaster.ConsumeNodeOpStatuses`, within the `_pendingBusTransitionAcks` resolution
  block (when `tracker.Received >= tracker.Expected`):
  - Before calling `PublishOpStatus`, check if the resolved transaction was a seek by
    attempting to extract a `ReplaySeekResult` from the ACK payloads collected by
    `ConsumeNodeOpStatuses`.
  - To do this, the `BusTransitionAckTracker` gains an optional `ReplaySeekResult? SeekResult`
    field. In the ACK accumulation loop, when `ev.ResultPayload is ReplaySeekResult sr` and
    `tracker.SeekResult == null`, assign `tracker.SeekResult = sr`.
  - When finalizing: if `tracker.SeekResult.HasValue`, call
    `_masterSync?.SnapAndPause(sr.RestoredTime.TotalWallTicks, sr.RestoredTime.TotalTime, new HashSet<int>(_roster.ActiveNodes.Keys))`.

**Constraints:**
- Only the first non-default `ReplaySeekResult` is used (listener / CGF nodes return
  `default(GlobalTime)` which has `TotalWallTicks == 0`; skip those).
- `_masterSync` is the `MasterSyncController`; it is already available as a field in
  `ClusterMaster`.
- This must not break non-seek transactions: for those, `tracker.SeekResult` remains null
  and `SnapAndPause` is never called.

**Files:**
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

**Success Conditions:**
- T15a: After all nodes ACK a seek with valid `ReplaySeekResult`, `_masterSync.GetCurrentState().TotalWallTicks`
  equals the `RestoredTime.TotalWallTicks` from the first non-zero result.
- T15b: `_masterSync.GetMode() == TimeMode.Deterministic` after the seek resolution.
- T15c: For non-seek transactions (no `ReplaySeekResult` in ACKs), `SnapAndPause` is not
  called and the master time controller is unchanged.
- T15d: If all nodes return `default(GlobalTime)` (i.e. all are listener/CGF nodes),
  `SnapAndPause` is not called.

---

## Phase 5: ClusterUiCache Visual Fix

---

### TASK-RT-016: Default `SourceDsmState`/`TargetDsmState` to `CurrentState`

**Design Reference:** [Phase 5](./DESIGN.md#phase-5-clusteruicache-visual-fix)

**Scope:**
- In `ClusterUiCache.Process2PcNetworkTraffic`, in the block that creates a new
  `DistributedTransaction` when `!_inFlight.ContainsKey(txId)`:
  - Change the initial assignment `var targetState = ClusterState.Idle` to
    `var targetState = CurrentState`.
  - Add `SourceDsmState = CurrentState` to the `DistributedTransaction` initializer
    (currently `SourceDsmState` is left as default `Idle`).

**Constraints:**
- The payload-type matching block (`EditLoadHandlerPayload`, `CommitStatePayload`, `int raw`)
  must remain unchanged. Those known state-mutating payloads override `targetState`.
- `ReplaySeekPayload` and other out-of-band payloads will naturally fall through, leaving
  `targetState == CurrentState` and `sourceState == CurrentState`.

**Files:**
- `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterUiCache.cs`

**Success Conditions:**
- T16a: When an `ExecuteNodeOpIntent` carrying `ReplaySeekPayload` is processed while
  `CurrentState == OperatingReplay`, the created `DistributedTransaction` has
  `SourceDsmState == OperatingReplay` and `TargetDsmState == OperatingReplay`.
- T16b: When an `ExecuteNodeOpIntent` carrying `CommitStatePayload(TargetStateId = LoadingLive)`
  is processed, `TargetDsmState == LoadingLive` (existing behavior unchanged).
- T16c: For unknown payloads while in `Idle` state, both source and target are `Idle`
  (consistent with old behavior, since `CurrentState == Idle`).

---

## Phase 6: Replay-to-Live Time Handover

---

### TASK-RT-017: Add `GetCurrentReplayTime()` to `IRecordReplayController`

**Design Reference:** [Phase 6](./DESIGN.md#phase-6-replay-to-live-time-handover)

**Scope:**
- Add `GlobalTime GetCurrentReplayTime()` to `IRecordReplayController`.
- `EcsRecordReplayController`: return `_repo.GetSingletonUnmanaged<GlobalTime>()` when
  `_activeReplayModule != null`; return `default` otherwise.
- `ListenerRecordReplayController`, `CgfRecordReplayController`: return `default(GlobalTime)`.

**Constraints:**
- This method is synchronous (no `Task`).
- Must be called BEFORE `TeardownReplayAsync()` in the live-from-replay branch; after teardown,
  the replay module is uninstalled and the ECS state may change.

**Files:**
- `FDP/Engine/Fdp.Core/Orchestration/IRecordReplayController.cs`
- `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`
- `Hrot/Network/Hrot.Network.Orchestration/ListenerRecordReplayController.cs`
- `Hrot/Subsystems/Hrot.CGF/Modules/Orchestration/CgfRecordReplayController.cs`

**Success Conditions:**
- T17a: `EcsRecordReplayController.GetCurrentReplayTime()` returns the `GlobalTime` singleton
  from the ECS repo (verified by injecting a known `GlobalTime` into the repo and asserting
  the method returns it).
- T17b: Called after `TeardownReplayAsync`, the method returns `default(GlobalTime)` (since
  `_activeReplayModule == null`).
- T17c: `ListenerRecordReplayController.GetCurrentReplayTime()` returns `default(GlobalTime)`.

---

### TASK-RT-018: Define `LiveBranchResult` payload struct

**Design Reference:** [Phase 6](./DESIGN.md#phase-6-replay-to-live-time-handover)

**Scope:**
- Add `public readonly record struct LiveBranchResult(GlobalTime HistoricalTime)` to
  `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs`.

**Constraints:**
- Must be JSON-serializable (same constraint as `ReplaySeekResult`).

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/NodeOpPayloads.cs`

**Success Conditions:**
- T18a: `JsonSerializer.Serialize/Deserialize<LiveBranchResult>` round-trips correctly.

---

### TASK-RT-019: `ReferenceReplayLoadHandler` returns `LiveBranchResult` on `PrepareLive`

**Design Reference:** [Phase 6](./DESIGN.md#phase-6-replay-to-live-time-handover)

**Scope:**
- In `ReferenceReplayLoadHandler.PrepareAsync`, in the `NodeOpType.PrepareLive` branch
  (live-from-replay path):
  1. Call `GlobalTime historicalTime = _controller.GetCurrentReplayTime()` BEFORE calling
     `TeardownReplayAsync()`.
  2. Continue with existing `TeardownReplayAsync()` and `PrepareRecordingAsync()` calls.
  3. Return `new LiveBranchResult(historicalTime)` instead of `null`.

**Constraints:**
- `GetCurrentReplayTime()` must be called before teardown; add a comment explaining why.
- The log statement must be kept; add `historicalTime.TotalWallTicks` to its content.

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs`

**Success Conditions:**
- T19a: `PrepareAsync` for `PrepareLive` (live-from-replay) returns a `LiveBranchResult`
  with a non-default `HistoricalTime` (when ECS has a valid `GlobalTime`).
- T19b: `GetCurrentReplayTime()` is called before `TeardownReplayAsync()` (verifiable via
  mock ordering in unit tests).

---

### TASK-RT-020: Add `TimeExtracted` flag to `BranchTransitionTask`

**Design Reference:** [Phase 6](./DESIGN.md#phase-6-replay-to-live-time-handover)

**Scope:**
- Add `public bool TimeExtracted` field to the `BranchTransitionTask` nested class inside
  `ClusterMaster`.
- In `ConsumeNodeOpStatuses`, when processing a `BranchTransitionTask` ACK and `ev.ResultPayload`
  is `LiveBranchResult res` and `!branchTask.TimeExtracted`:
  1. Set `branchTask.TimeExtracted = true`.
  2. Capture `res.HistoricalTime` for use in the clock snap (see RT-021).

**Constraints:**
- Only the first non-default `LiveBranchResult` is used (listener nodes return
  `default(GlobalTime)` with `TotalWallTicks == 0`; skip those when `TimeExtracted == false`).

**Files:**
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

**Success Conditions:**
- T20a: `TimeExtracted` is set to `true` exactly once per branch task, on first non-default
  `LiveBranchResult`.
- T20b: Subsequent non-default `LiveBranchResult` ACKs from other nodes do not overwrite
  the captured historical time.

---

### TASK-RT-021: Master atomic snap on branch completion in `ConsumeNodeOpStatuses`

**Design Reference:** [Phase 6](./DESIGN.md#phase-6-replay-to-live-time-handover)

**Scope:**
- In `ClusterMaster.ConsumeNodeOpStatuses`, in the `_pendingBranchTasks` resolution block,
  when `branchTask.RemainingAcks <= 0`:
  1. Before `_replayMasterModule?.RestoreTime()` and `PublishOpStatus`, if `TimeExtracted`
     was set: call `_masterSync?.SnapAndPause(historicalTime.TotalWallTicks, historicalTime.TotalTime, new HashSet<int>(_roster.ActiveNodes.Keys))`.
  2. Then call `_replayMasterModule?.RestoreTime()`.
  3. Then `PublishOpStatus`.
  - `historicalTime` must be stored as a field in `BranchTransitionTask` (added in RT-020).

**Constraints:**
- `SnapAndPause` is called BEFORE `RestoreTime` - the clock is snapped first so the restored
  time scale applies to the correct position.
- `_masterSync` may be null in non-orchestrator contexts; use null-conditional `?.`.

**Files:**
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

**Success Conditions:**
- T21a: After all branch ACKs are received with a valid `LiveBranchResult`, `_masterSync`'s
  `TotalWallTicks == historicalTime.TotalWallTicks`.
- T21b: `_masterSync.GetMode() == TimeMode.Deterministic` after branch completion.
- T21c: `PublishOpStatus` is called AFTER `SnapAndPause`, ensuring the cluster enters
  `OperatingLive` with the clock already snapped.
- T21d: If no `LiveBranchResult` was received (all listener nodes), `SnapAndPause` is not
  called and the existing behaviour is preserved.
