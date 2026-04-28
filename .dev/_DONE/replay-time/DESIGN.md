# Replay Time Control - Design

## Overview

This workstream fixes a cluster of tightly-related bugs in the distributed replay subsystem
and rearchitects the time-control plane for replay to use a principled "Pull Model".

The starting point has four observable defects:

1. **Seek bypasses 2PC** - `ClusterMaster` fans out `NodeReplaySeek` with a detached random Guid
   and immediately publishes `Success`, so the orchestrator never waits for nodes to finish
   seeking and the 2PC history panel shows nothing useful.

2. **2PC history shows "Idle -> Idle"** - `ClusterUiCache.Process2PcNetworkTraffic` defaults
   unrecognized payloads (like `ReplaySeekPayload`) to `ClusterState.Idle` for both source and
   target states instead of using the cache's live `CurrentState`.

3. **Replay clock is dictated by playback, not by the time controller** - `PlaybackTickSystem`
   blindly advances frames using an `ExtraFramesThisTick` counter. Pause, step, and speed
   changes do not flow correctly into the replay position.

4. **Replay-to-live transition does not restore the correct time on the master** - When the
   cluster transitions from `OperatingReplay` to `LoadingLive`, the master has no mechanism
   to seed its `MasterSyncController` to the historically accurate simulation time that was
   current in the recording at the moment the operator initiated the branch.

---

## Architectural Principles

### Pull Model (core principle)

The `.fdp` recording is a **read-only data store**. The time controller is the **single source
of truth** for what point in the recording is currently visible. `PlaybackTickSystem` is a
**reactive smart cursor**: on every tick it reads `ITimeController.GetCurrentState().TotalWallTicks`
and positions the `PlaybackController` to the frame whose wall-clock stamp matches.

This gives Pause, Resume, Step, and Seek for free - they are just operations on the time
controller that automatically propagate to the replay position.

### Suspension Seam (historical data protection)

`ModuleHostKernel.SuspendGlobalTimePush()` is kept active throughout replay. This severs the
write-path from the active time controller's output down to the ECS `GlobalTime` singleton.
The historical `GlobalTime` (including original `TotalTime`, `DeltaTime`, and `FrameNumber`)
is deserialized directly from the `.fdp` recording chunks into the ECS repository by
`PlaybackTickSystem` and remains untouched by the time controllers.

The live `MasterSyncController` / `SlaveSyncController` continue to run during replay; they
provide the NTP-synchronized virtual wall clock that drives replay indexing. No specialized
`ReplayTimeController` is introduced.

### Atomic Snap-and-Pause (distributed clock alignment)

When the master broadcasts a `SwitchTimeModeEvent(Deterministic)` with
`BarrierWallTicks = now` (no lookahead), each slave's `SyncedWallTicks` is already >=
that barrier by the time the packet arrives. `SlaveSyncController.DrainModeSwitchEvents`
detects this condition and instantly snaps its internal baseline clock to the target time
and transitions to `Stepping` mode, bypassing the `BarrierPending` wait.

This property means no new `ForceSnap` wire flag is needed in `SwitchTimeModeEvent`.

---

## Implementation Phases

### Phase 1: Pull Model Infrastructure

**Goal:** Make `PlaybackTickSystem` use the active time controller's `TotalWallTicks` as
its sole indexing cursor, replacing the `ExtraFramesThisTick` push counter.

**Why:** This is the foundation of all other improvements. Once the cursor reads from the
time controller, pause/resume/step become first-class operations on the replay position
automatically.

Tasks: RT-001, RT-002, RT-003, RT-004

**Key components touched:**
- `Fdp.ModuleHost/ModuleHostKernel.cs` - expose active time controller
- `Fdp.Toolkits/Replay/ReplayModule.cs` - accept `ITimeController` in constructor
- `Fdp.Toolkits/Replay/PlaybackTickSystem.cs` - inject controller, implement smart cursor
- `Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` - wire controller

---

### Phase 2: Atomic Snap-and-Pause

**Goal:** When `SlaveSyncController` receives a `SwitchTimeModeEvent(Deterministic)` whose
`BarrierWallTicks` is already in the past (`SyncedWallTicks >= BarrierWallTicks`), it must
instantly snap its clock baseline and transition to `Stepping`, without waiting in
`BarrierPending`.

**Why:** After a seek, the master sets `BarrierWallTicks = _getTick()` (no lookahead). By the
time the packet arrives on the slave, its `SyncedWallTicks` is already past the barrier. The
old code would wait forever in `BarrierPending` because the barrier time has already elapsed.

Also, reuse the time-anchoring logic by extracting `ApplyTimeSnap(evt)` from `ApplyResume`,
so both the resume path and the instant-snap path share identical clock anchoring.

Tasks: RT-005, RT-006

**Key component touched:**
- `Fdp.Toolkits/Time/Controllers/SlaveSyncController.cs`

---

### Phase 3: 2PC Seek Fix in the Orchestrator

**Goal:** Make `ClusterMaster` treat `NodeReplaySeek` as a proper 2PC operation: register a
`BusTransitionAckTracker`, fan out with a tracked transaction ID, remove the premature
`PublishOpStatus(Success)`, and inject a server-side pause precondition before fanning out.

**Why:**
- Without a tracker, the master never waits for nodes to finish seeking.
- The premature success call causes the `ClusterUiCache` to clear its `_inFlight` dict before
  any node responses arrive.
- A server-side pause ensures the simulation clock stops advancing before heavy O(log N) state
  reconstruction starts, preventing temporal tearing regardless of UI behavior.

Tasks: RT-007, RT-008, RT-009

**Key component touched:**
- `Hrot.Orchestrator/ClusterMaster.cs`

---

### Phase 4: Seek Time Payload Propagation

**Goal:** At the end of a seek, the master must know the exact `GlobalTime` restored from the
recording. It then seeds its own `MasterSyncController` to that time and broadcasts an atomic
snap-and-pause to the cluster.

**Why:** After seeking, the replay indexing time must equal the seek target. Without seeding
the master clock, subsequent Resume commands would broadcast the wrong `SimTimeSnapshot`.
All slave nodes (including diskless ExCon and IG) receive the corrected time via DDS.

Implementation steps:
1. `IRecordReplayController.SeekToTimeAsync` changes return type from `Task` to `Task<GlobalTime>`.
2. `EcsRecordReplayController` reads `GlobalTime` from ECS repo after the seek task completes.
3. `ListenerRecordReplayController` and `CgfRecordReplayController` return `default(GlobalTime)`.
4. `ReferenceReplayLoadHandler` packages the result in a new `ReplaySeekResult` payload.
5. `MasterSyncController` gains a `SnapAndPause(long, double, HashSet<int>)` method.
6. `ClusterMaster.ConsumeNodeOpStatuses` extracts the first valid `ReplaySeekResult` and calls
   `SnapAndPause` before publishing the final 2PC success.

Tasks: RT-010, RT-011, RT-012, RT-013, RT-014, RT-015

**Key components touched:**
- `Fdp.Core/Orchestration/IRecordReplayController.cs`
- `Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`
- `Hrot.Network.Orchestration/ListenerRecordReplayController.cs`
- `Hrot.CGF/Modules/Orchestration/CgfRecordReplayController.cs`
- `Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs`
- `Fdp.Toolkits/Orchestration/NodeOpPayloads.cs` (new `ReplaySeekResult`)
- `Fdp.Toolkits/Time/Controllers/MasterSyncController.cs` (new `SnapAndPause`)
- `Hrot.Orchestrator/ClusterMaster.cs`

---

### Phase 5: ClusterUiCache Visual Fix

**Goal:** In `ClusterUiCache.Process2PcNetworkTraffic`, default both `SourceDsmState` and
`TargetDsmState` of a new `DistributedTransaction` to `CurrentState` instead of
`ClusterState.Idle`. Out-of-band operations like `ReplaySeekPayload` then appear as
`OperatingReplay -> OperatingReplay` instead of `Idle -> Idle`.

**Why:** The `DistributedTransaction` already has a `SourceDsmState` field. The fix is a
single-line initialization change; it requires no new types or dependencies.

Tasks: RT-016

**Key component touched:**
- `Hrot.Orchestrator/Panels/ClusterUiCache.cs`

---

### Phase 6: Replay-to-Live Time Handover

**Goal:** When the cluster transitions from `OperatingReplay` to `LoadingLive`
(the "live-from-replay" branch), the master must seed its `MasterSyncController` to the
historically accurate `GlobalTime` from the recording and broadcast an atomic snap-and-pause
*before* publishing the 2PC success event. The cluster enters `OperatingLive` with all nodes
perfectly aligned in time, paused, ready for the operator to press Resume.

**Why:** Without this handover, the master's clock remains at whatever time it was at when the
replay started. Any `SwitchTimeModeEvent(Continuous)` issued at Resume would carry a stale
`SimTimeSnapshot`, causing all nodes to jump back to the wrong historical time.

Implementation steps:
1. Add `GetCurrentReplayTime()` to `IRecordReplayController` and all implementations.
2. Define `LiveBranchResult` payload struct.
3. `ReferenceReplayLoadHandler.PrepareAsync` reads the current `GlobalTime` via
   `GetCurrentReplayTime()` *before* calling `TeardownReplayAsync()` and returns it wrapped
   in `LiveBranchResult`.
4. Add `TimeExtracted` flag to `BranchTransitionTask` in `ClusterMaster` so only the first
   valid node response seeds the clock.
5. `ClusterMaster.ConsumeNodeOpStatuses` extracts `LiveBranchResult` from the first valid
   response, calls `_masterSync.SeedState` and `SnapAndPause`, then publishes success.

Tasks: RT-017, RT-018, RT-019, RT-020, RT-021

**Key components touched:**
- `Fdp.Core/Orchestration/IRecordReplayController.cs`
- `Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`
- `Hrot.Network.Orchestration/ListenerRecordReplayController.cs`
- `Hrot.CGF/Modules/Orchestration/CgfRecordReplayController.cs`
- `Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs`
- `Fdp.Toolkits/Orchestration/NodeOpPayloads.cs` (new `LiveBranchResult`)
- `Hrot.Orchestrator/ClusterMaster.cs`

---

## Architectural Constraints

### Do not introduce ReplayTimeController

The design talk considered but explicitly rejected a dedicated `ReplayTimeController`.
The live `SlaveSyncController` / `MasterSyncController` are retained as-is. They drive the
NTP-synchronized virtual wall clock that becomes the replay indexing time. `SuspendGlobalTimePush`
protects the ECS state from being overwritten.

### SlaveSyncController must stay network-only

`SlaveSyncController` must not read from the ECS `GlobalTime` singleton. It must receive all
time information exclusively via DDS network messages. This keeps it usable on diskless observer
nodes (ExCon, IG) that have no `.fdp` recording and no `EntityRepository`.

### PlaybackController.SeekToWallClockTicks is thread-safe for background tasks

`ReplayModule.SeekToWallClockTicksAsync` wraps the seek in a `Task.Run`. After the task
completes, the main thread's `PlaybackTickSystem` will see the updated frame position. The
ECS repo is not accessed from the background task after the seek finishes.

### No wire contract changes to SwitchTimeModeEvent

`SwitchTimeModeEvent` and `SwitchTimeModeWireDto` are not modified. The instant-snap behaviour
in `SlaveSyncController` is derived purely from comparing `SyncedWallTicks` against
`BarrierWallTicks`.

### Simulation systems remain disabled during replay

`ReferenceReplayLoadHandler.Commit` already sets `Enabled = false` on
`TogglableSimulationGroup` and `TogglablePostSimulationGroup`. No change is needed here.
These must not be re-enabled for seek operations.

---

## Project Dependency Verification

| Component | Project | Can reference |
|---|---|---|
| `ITimeController` | `Fdp.ModuleHost` | Core only |
| `PlaybackTickSystem` | `Fdp.Toolkits` | Depends on `Fdp.ModuleHost` -> OK to use `ITimeController` |
| `ReplayModule` | `Fdp.Toolkits` | Depends on `Fdp.ModuleHost` -> OK to use `ITimeController` |
| `MasterSyncController` | `Fdp.Toolkits` | Depends on `Fdp.ModuleHost` |
| `SlaveSyncController` | `Fdp.Toolkits` | Depends on `Fdp.ModuleHost` |
| `EcsRecordReplayController` | `Hrot.SimHost` | Depends on `Fdp.Toolkits` |
| `ClusterMaster` | `Hrot.Orchestrator` | Depends on `Hrot.Network.Orchestration` |
| `ReplaySeekResult`, `LiveBranchResult` | `Fdp.Toolkits` | Same assembly as `ReplayPrepareResult` |

No circular dependencies are introduced. `IRecordReplayController` is in `Fdp.Core` which has
no `Hrot.*` references; `GlobalTime` (return type of `SeekToTimeAsync`) is also in `Fdp.Core`.

---

## What does NOT change

- `SwitchTimeModeEvent` wire contract (no new fields)
- `TogglableSimulationGroup` / `TogglablePostSimulationGroup` disabling logic (already correct)
- `ModuleHostKernel.SuspendGlobalTimePush` / `ResumeGlobalTimePush` logic (already correct)
- `PlaybackController` binary search implementation (already O(log N), no changes needed)
- `ReplayMasterModule.FreezeTime` / `RestoreTime` calls in `ClusterMaster` (kept as-is)
- `ReferenceReplayLoadHandler.Commit` phase (already correct for enabling/disabling groups)
