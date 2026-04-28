# Replay Time Control - Onboarding Guide

## What is this workstream?

This workstream fixes a cluster of related bugs in the distributed replay subsystem and
rearchitects how the replay playback position is controlled. The complete design is in
[DESIGN.md](./DESIGN.md). The 21 implementation tasks are detailed in
[TASK-DETAIL.md](./TASK-DETAIL.md). The progress checklist is in
[TASK-TRACKER.md](./TASK-TRACKER.md).

---

## Problem in one paragraph

When an operator presses Seek in the replay UI, the orchestrator fans out a `NodeReplaySeek`
operation to all nodes but immediately declares success - it never waits for nodes to finish
seeking. The playback position on each node is driven by a push counter (`ExtraFramesThisTick`)
rather than by the active time controller, so Pause and Step do not accurately position the
replay frame. After a seek, the master's clock is not aligned to the historically correct
simulation time, so the next Resume broadcasts the wrong reference time to all nodes. The
2PC history panel also incorrectly shows seek transitions as `Idle -> Idle`.

---

## High-level solution

| Old design | New design |
|---|---|
| `PlaybackTickSystem` is driven by a push counter (`ExtraFramesThisTick`) | `PlaybackTickSystem` reads `ITimeController.TotalWallTicks` as its cursor (Pull Model) |
| Seek fan-out has no `BusTransitionAckTracker`; success is immediate | Seek is registered as a proper 2PC; master waits for all node ACKs |
| `SwitchTimeModeEvent(Deterministic)` always waits in `BarrierPending` | When barrier is already elapsed, slave instantly snaps clock and enters `Stepping` |
| `SeekToTimeAsync` returns `Task` with no payload | Returns `Task<GlobalTime>`; restored time flows back to master via ACK payload |
| Master clock is not reseeded after seek or live-from-replay branch | Master calls `SnapAndPause` after collecting the first valid `ReplaySeekResult` or `LiveBranchResult` |
| 2PC history shows `Idle -> Idle` for seek ops | Fixed: defaults to `CurrentState -> CurrentState` in `ClusterUiCache` |

---

## Repository layout (relevant to this workstream)

```
FDP/
  Engine/
    Fdp.Core/
      Orchestration/
        IRecordReplayController.cs   <- interface: SeekToTimeAsync, GetCurrentReplayTime
        GlobalTime.cs                <- the time struct; return type of SeekToTimeAsync
    Fdp.ModuleHost/
      ModuleHostKernel.cs            <- RT-001: add GetTimeController()
      Time/
        ITimeController.cs           <- GetCurrentState() -> GlobalTime
  Toolkits/
    Fdp.Toolkits/
      Replay/
        PlaybackTickSystem.cs        <- RT-003: smart cursor refactor (core change)
        ReplayModule.cs              <- RT-002: accept ITimeController
        PlaybackController.cs        <- binary search seek (no changes)
      Time/
        Controllers/
          MasterSyncController.cs    <- RT-014: add SnapAndPause
          SlaveSyncController.cs     <- RT-005, RT-006: ApplyTimeSnap + instant snap
        Messages/
          TimeMessages.cs            <- NOT modified
      Orchestration/
        NodeOpPayloads.cs            <- RT-010, RT-018: add ReplaySeekResult, LiveBranchResult
        Handlers/
          ReferenceReplayLoadHandler.cs  <- RT-013, RT-019: return result payloads

Hrot/
  Subsystems/
    Hrot.SimHost/
      Modules/Orchestration/
        EcsRecordReplayController.cs  <- RT-004, RT-012, RT-017: wiring + implementations
    Hrot.Orchestrator/
      ClusterMaster.cs               <- RT-007..RT-009, RT-015, RT-020, RT-021 (orchestrator hub)
      Panels/
        ClusterUiCache.cs            <- RT-016: SourceDsmState fix
  Network/
    Hrot.Network.Orchestration/
      ListenerRecordReplayController.cs  <- RT-012, RT-017: stub implementations
  CGF/
    Hrot.CGF/
      Modules/Orchestration/
        CgfRecordReplayController.cs    <- RT-012, RT-017: stub implementations
```

---

## Key data types

### `GlobalTime` (`Fdp.Core`)
The simulation clock snapshot. Fields used by this workstream:
- `TotalWallTicks` - NTP-synchronized wall clock ticks at this frame
- `TotalTime` - total simulation seconds (double)
- `FrameNumber` - frame index in the recording

### `SwitchTimeModeEvent` (`Fdp.Toolkits`)
Broadcast by `MasterSyncController` when the cluster switches to `Deterministic` (step) or
`Continuous` (free-running) mode. Key fields:
- `TargetMode` - `Deterministic` | `Continuous`
- `BarrierWallTicks` - wall tick at which slaves must snap (instant-snap if already past)
- `SimTimeSnapshot` - reference simulation time to anchor from
- `TimeScale` - replay speed multiplier

### `ReplaySeekPayload` / `ReplaySeekResult` (`Fdp.Toolkits`)
Fan-out payload for `NodeReplaySeek` and the ACK result carrying restored `GlobalTime`.

### `LiveBranchResult` (`Fdp.Toolkits`)
ACK result from `PrepareLive` carrying the last `GlobalTime` before the replay module was
torn down.

---

## Pull Model: how the cursor works

`PlaybackTickSystem.Execute` is called every PostSimulation frame. It:
1. Reads `targetTicks = _timeController.GetCurrentState().TotalWallTicks`.
2. Reads `currentTicks = _playback.GetFrameMetadata(_playback.CurrentFrame).WallClockTicks`.
3. If `targetTicks <= currentTicks`: returns (paused or at exact position).
4. If gap is `<= StrategyBThreshold` frames ahead: calls `StepForward` in a loop.
5. Otherwise: calls `SeekToWallClockTicks` + `ForceMarkAllDirty` + `_afterSeek`.

`SuspendGlobalTimePush` keeps the live time controllers from overwriting the historical
`GlobalTime` that gets written into the ECS repo by `StepForward`/`SeekToWallClockTicks`.

---

## 2PC seek flow (after this workstream)

```
Operator: SeekReplayIntent
  |
  v
ClusterMaster.ProcessSeekReplayIntent
  1. Publish SlaveNodeSetUpdatedEvent + PauseTimeIntent (server-side pause)
  2. FanOutNodeOp(NodeReplaySeek, txId, ReplaySeekPayload, allNodeIds)
  3. _pendingBusTransitionAcks[txId] = BusTransitionAckTracker(requestId, expected)
  |
  v  (parallel, on each node)
ReferenceReplayLoadHandler.PrepareAsync (NodeReplaySeek)
  1. await _controller.SeekToTimeAsync(targetTicks) -> GlobalTime
  2. return ReplaySeekResult(restoredTime)
  |
  v  (back on master)
ClusterMaster.ConsumeNodeOpStatuses
  - accumulate first non-default ReplaySeekResult
  - when all ACKs received:
      _masterSync.SnapAndPause(restoredTime)
      PublishOpStatus(requestId, Success)
```

---

## Replay-to-live branch flow (after this workstream)

```
Operator: BranchToLiveIntent
  |
  v
ClusterMaster starts branch transition (2PC)
  |
  v  (on SimHost node)
ReferenceReplayLoadHandler.PrepareAsync (PrepareLive)
  1. historicalTime = _controller.GetCurrentReplayTime()   <- BEFORE teardown
  2. TeardownReplayAsync()
  3. PrepareRecordingAsync()
  4. return LiveBranchResult(historicalTime)
  |
  v  (back on master)
ClusterMaster.ConsumeNodeOpStatuses (BranchTransitionTask)
  - extract first non-default LiveBranchResult (TimeExtracted flag)
  - when all ACKs received:
      _masterSync.SnapAndPause(historicalTime)
      _replayMasterModule.RestoreTime()
      PublishOpStatus(requestId, Success)
```

---

## Build and test

Build the full solution:
```
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet
```

Run FDP engine tests only (fast, no Hrot dependencies):
```
dotnet test FDP/FDP.sln --no-build
```

Run specific test projects relevant to this workstream:
```
dotnet test FDP/Engine/Fdp.ModuleHost.Tests/Fdp.ModuleHost.Tests.csproj --no-build
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build
dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj --no-build
```

---

## What does NOT change

- `SwitchTimeModeEvent` wire format (no new fields; instant-snap is derived from barrier timing)
- `TogglableSimulationGroup` disabling logic during replay (already correct)
- `ModuleHostKernel.SuspendGlobalTimePush` logic (already correct)
- `PlaybackController` binary search implementation (no changes)
- `ReplayMasterModule.FreezeTime` / `RestoreTime` calls in `ClusterMaster` (kept as-is)
