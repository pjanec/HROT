# Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: Pull Model Infrastructure

**Goal:** Refactor `PlaybackTickSystem` to use `ITimeController.TotalWallTicks` as the
sole replay cursor; wire the controller through `ReplayModule` and `EcsRecordReplayController`.

- [ ] **RT-001** Expose `GetTimeController()` on `ModuleHostKernel` [details](./TASK-DETAIL.md#task-rt-001-expose-gettimecontroller-on-modulehostkernel)
- [ ] **RT-002** `ReplayModule` constructor accepts `ITimeController` [details](./TASK-DETAIL.md#task-rt-002-replaymodule-constructor-accepts-itimecontroller)
- [ ] **RT-003** Refactor `PlaybackTickSystem` smart cursor [details](./TASK-DETAIL.md#task-rt-003-refactor-playbbackticksystem-to-use-itimecontrollertotalwallclkoticks)
- [ ] **RT-004** Wire time controller through `EcsRecordReplayController` [details](./TASK-DETAIL.md#task-rt-004-wire-time-controller-through-ecsrecordreplaycontroller)

---

## Phase 2: Atomic Snap-and-Pause

**Goal:** `SlaveSyncController` instantly snaps its clock baseline when a Deterministic
barrier arrives that has already elapsed, enabling zero-latency replay seek alignment.

- [ ] **RT-005** Extract `ApplyTimeSnap` from `SlaveSyncController.ApplyResume` [details](./TASK-DETAIL.md#task-rt-005-extract-applytimesnap-from-slavesynccontrollerapplyresume)
- [ ] **RT-006** Instant snap-and-pause in `DrainModeSwitchEvents` [details](./TASK-DETAIL.md#task-rt-006-instant-snap-and-pause-in-drainmodeswitchevents)

---

## Phase 3: 2PC Seek Fix in the Orchestrator

**Goal:** `ClusterMaster` treats `NodeReplaySeek` as a proper tracked 2PC operation;
server-side pause is injected before seek fan-out.

- [ ] **RT-007** Remove premature `PublishOpStatus` from `ReplaySeek` case [details](./TASK-DETAIL.md#task-rt-007-remove-premature-publishopstatus-from-replaysee-case)
- [ ] **RT-008** Refactor `ProcessSeekReplayIntent` with `BusTransitionAckTracker` [details](./TASK-DETAIL.md#task-rt-008-refactor-processseekereplayintent-with-bustransitionacktracker)
- [ ] **RT-009** Server-side pause precondition in `ProcessSeekReplayIntent` [details](./TASK-DETAIL.md#task-rt-009-server-side-pause-precondition-in-processseekereplayintent)

---

## Phase 4: Seek Time Payload Propagation

**Goal:** Restored `GlobalTime` flows from node ACKs up to the master, which snaps its
clock and broadcasts an atomic pause to all nodes at the end of every seek.

- [ ] **RT-010** Define `ReplaySeekResult` payload struct [details](./TASK-DETAIL.md#task-rt-010-define-replayseekresult-payload-struct)
- [ ] **RT-011** Change `IRecordReplayController.SeekToTimeAsync` return type to `Task<GlobalTime>` [details](./TASK-DETAIL.md#task-rt-011-change-irecordreplaycontrollerseektotimeasync-return-type)
- [ ] **RT-012** Update all `SeekToTimeAsync` implementations [details](./TASK-DETAIL.md#task-rt-012-update-all-seektotimeasync-implementations)
- [ ] **RT-013** `ReferenceReplayLoadHandler` returns `ReplaySeekResult` for `NodeReplaySeek` [details](./TASK-DETAIL.md#task-rt-013-referencereplayloadhandler-returns-replayseekresult-for-nodereplayseek)
- [ ] **RT-014** Add `SnapAndPause` method to `MasterSyncController` [details](./TASK-DETAIL.md#task-rt-014-add-snapandpause-method-to-masterysynccontroller)
- [ ] **RT-015** Master clock snap in `ConsumeNodeOpStatuses` after seek [details](./TASK-DETAIL.md#task-rt-015-master-clock-snap-in-consumernodeopstatuses-after-seek)

---

## Phase 5: ClusterUiCache Visual Fix

**Goal:** Seek operations appear as `OperatingReplay -> OperatingReplay` in the 2PC
history panel instead of the misleading `Idle -> Idle`.

- [ ] **RT-016** Default `SourceDsmState`/`TargetDsmState` to `CurrentState` [details](./TASK-DETAIL.md#task-rt-016-default-sourcedsmsatetargetdsmstate-to-currentstate)

---

## Phase 6: Replay-to-Live Time Handover

**Goal:** When the cluster branches from replay to live, the master seeds its clock
from the historical `GlobalTime` and broadcasts an atomic snap before entering `OperatingLive`.

- [ ] **RT-017** Add `GetCurrentReplayTime()` to `IRecordReplayController` [details](./TASK-DETAIL.md#task-rt-017-add-getcurrentreplaytime-to-irecordreplaycontroller)
- [ ] **RT-018** Define `LiveBranchResult` payload struct [details](./TASK-DETAIL.md#task-rt-018-define-livebranchresult-payload-struct)
- [ ] **RT-019** `ReferenceReplayLoadHandler` returns `LiveBranchResult` on `PrepareLive` [details](./TASK-DETAIL.md#task-rt-019-referencereplayloadhandler-returns-livebranchresult-on-preparelive)
- [ ] **RT-020** Add `TimeExtracted` flag to `BranchTransitionTask` [details](./TASK-DETAIL.md#task-rt-020-add-timeextracted-flag-to-branchtransitiontask)
- [ ] **RT-021** Master atomic snap on branch completion [details](./TASK-DETAIL.md#task-rt-021-master-atomic-snap-on-branch-completion-in-consumernodeopstatuses)
