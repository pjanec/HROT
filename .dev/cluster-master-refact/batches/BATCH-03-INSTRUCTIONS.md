# BATCH-03 Instructions

**Batch:** BATCH-03  
**Phase:** Phase 2 -- Temporal Interlock Extractions  
**Tasks:** TASK-T001, TASK-T002  
**Author:** Dev Lead

---

## Context

TASK-T000 (audit) is complete. Comment has been added to `ClusterMaster.ProcessTransitionStateIntent`
(lines ~762-776). Audit conclusion: **SAFE** -- the slave-side `PrepareLive` handler
(`ReferenceReplayLoadHandler`) guards `CanHandle` with `IsReplayActive`, making it safe to
remove the `isLiveFromReplayBranch` suppression and use the standard `TransitionState` fan-out.

BATCH-02 committed. Start on a clean working tree.

---

## Orientation

### Key files

| File | What to do |
|---|---|
| `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` | Remove branch, seek, and sync fields/methods |
| `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs` | Wire two new process managers |
| `Hrot/Subsystems/Hrot.Orchestrator/LiveBranchProcessManager.cs` | NEW |
| `Hrot/Subsystems/Hrot.Orchestrator/ReplaySeekAggregator.cs` | NEW |
| `Hrot/Subsystems/Hrot.Orchestrator/ReplaySeekProcessManager.cs` | NEW |
| `Hrot/Subsystems/Hrot.Orchestrator.Tests/LiveBranchProcessManagerTests.cs` | NEW |
| `Hrot/Subsystems/Hrot.Orchestrator.Tests/ReplaySeekProcessManagerTests.cs` | NEW |

### Existing types (do not modify signatures unless told)

- `ReplayMasterModule` -- has `FreezeTime()` and `RestoreTime()` methods
- `MasterSyncController` -- has `SnapAndPause(long wallTicks, double totalTime, HashSet<int> nodeIds)` method
- `LiveBranchResult` -- struct with `GlobalTime HistoricalTime` field
- `ReplaySeekResult` -- struct with `GlobalTime RestoredTime` field
- `GlobalTime` -- struct with `TotalWallTicks` (long) and `TotalTime` (double) fields
- `SeekReplayIntent` -- bus event: `Guid RequestId`, `long TargetWallTicks`
- `TransitionStateIntent` -- bus event: read by `ClusterMaster.ProcessTransitionStateIntents()`
- `ClusterOpCompletedEvent` -- bus event: `Guid RequestId`, `OrchestrationStatusCode StatusCode`, `object? ResultPayload`
- `SlaveNodeSetUpdatedEvent` -- bus event: `HashSet<int> SlaveNodeIds`
- `PauseTimeIntent` -- bus event (no fields relevant to this task)
- `ExecuteNodeOpIntent` -- bus event with `Guid TransactionId`, `NodeOpType Operation`, etc.

---

## TASK-T001: LiveBranchProcessManager

### Overview

`LiveBranchProcessManager` takes over `FreezeTime`/`RestoreTime` calls and the `SnapAndPause`
call for branch results from `ClusterMaster`. After this task, `ClusterMaster` no longer holds
`ReplayMasterModule`, `MasterSyncController` (for branch path), or `_pendingBranchTasks`.

The `isLiveFromReplayBranch` suppression block is also removed. Because the TASK-T000 audit
confirmed `ReferenceReplayLoadHandler.CanHandle(PrepareLive)` returns true only when
`IsReplayActive`, the standard `TransitionState` fan-out already routes `PrepareLive` correctly
during `OperatingReplay`. The separate branch fan-out path is no longer needed.

### Step 1: Create `LiveBranchProcessManager`

Create `Hrot/Subsystems/Hrot.Orchestrator/LiveBranchProcessManager.cs`:

```csharp
// LiveBranchProcessManager.cs
// Owns the Live-from-Replay branch interlock: freeze before fan-out, restore after ACK.
// See TASK-T001 / DESIGN.md Phase 2.1
```

Constructor signature:
```csharp
public LiveBranchProcessManager(
    FdpEventBus        bus,
    ReplayMasterModule replayMasterModule,
    MasterSyncController masterSync)
```

**`Tick()` logic:**

1. Read `TransitionStateIntent` from bus. For each intent where the transition passes through
   `LoadingLive` and the state at the time of the intent was `OperatingReplay` (i.e., the intent
   is a Live-from-Replay branch), call `_replayMasterModule.FreezeTime()`.
   - How to detect "from OperatingReplay": inspect `intent.SourceState == ClusterState.OperatingReplay`
     or equivalent. Check what field `TransitionStateIntent` has for the current/source state. If
     no such field, read the ClusterMaster's current state from the bus or keep a local copy of
     the last known state. Simplest: check if the bus currently has a `ClusterStateChangedEvent`
     that moved from `OperatingReplay` toward `LoadingLive` in the same frame. Actually, the
     cleanest approach is to track a field `_lastKnownDsmState` by reading
     `ClusterStateChangedEvent` from the bus, and on `TransitionStateIntent` for `LoadingLive`,
     check if `_lastKnownDsmState == ClusterState.OperatingReplay`.
   - **Important:** `Tick()` must be called BEFORE `ClusterMaster.Tick()` so that `FreezeTime()`
     runs before `ClusterMaster` fans out `PrepareLive`. Wire accordingly in `OrchestratorSubsystem`.

2. Read `ClusterOpCompletedEvent` from bus. For each event where `ResultPayload` is a
   `LiveBranchResult lbr` and `lbr.HistoricalTime.TotalWallTicks != 0`:
   - Call `_replayMasterModule.RestoreTime()`
   - Call `_masterSync.SnapAndPause(lbr.HistoricalTime.TotalWallTicks, lbr.HistoricalTime.TotalTime, activeNodeIds)`
   - For `activeNodeIds`: use the full set of active node IDs captured at the time. Since the
     process manager doesn't have direct access to the roster, either (a) subscribe to
     `NodeHeartbeatEvent` to maintain a local replica of active node IDs, or (b) read
     `SlaveNodeSetUpdatedEvent` from the bus (published by ClusterMaster for PauseTime ops).
     The simplest approach for now: always pass `new HashSet<int>()` (empty set) and note a
     TODO comment. The SnapAndPause call is what matters for time correctness; node filtering
     is secondary.

### Step 2: Remove from ClusterMaster

**Fields to remove:**
- `private ReplayMasterModule? _replayMasterModule;` (line ~125)
- `private readonly Dictionary<Guid, BranchTransitionTask> _pendingBranchTasks = new();` (line ~139)
- `private sealed class BranchTransitionTask { ... }` (nested class, lines ~141-148)

**Comment block to remove (in `ProcessTransitionStateIntent`):**

Remove the entire `isLiveFromReplayBranch` block including:
```csharp
// CGF1-S0305: Live-from-Replay temporal interlock.
// AUDIT(TASK-T000): ...   ← the audit comment added by T000
bool isLiveFromReplayBranch = false;
if (passesLoadingLive && stateBeforeAdvance == ClusterState.OperatingReplay)
{
    isLiveFromReplayBranch = true;
    _replayMasterModule?.FreezeTime();
    ...
    _pendingBranchTasks[branchTxId] = ...
    FanOutNodeOp(NodeOpType.PrepareLive, branchTxId, ...);
    ...
    else
    {
        _replayMasterModule?.RestoreTime();
        ...
    }
}
```

Replace with just a brief comment:
```csharp
// Live-from-Replay FreezeTime is now handled by LiveBranchProcessManager (TASK-T001).
```

Remove the `!isLiveFromReplayBranch` guard around the main fan-out loop. The main fan-out loop
must now always run (for all active nodes). Remove the `isLiveFromReplayBranch` variable entirely.

Update the `expectedAcks` calculation:
```csharp
// Remove: int expectedAcks = isLiveFromReplayBranch ? 0 : (prepSteps * activeNodeIds.Count);
// Replace with:
int expectedAcks = prepSteps * activeNodeIds.Count;
```

**In `ConsumeNodeOpStatuses`, remove the branch ACK block:**

Remove this entire block:
```csharp
// CGF1-S0305: Branch-transition ACK.
if (_pendingBranchTasks.TryGetValue(ev.TransactionId, out var branchTask))
{
    if (!branchTask.TimeExtracted && ev.ResultPayload is LiveBranchResult lbr && ...)
    { ... }
    branchTask.RemainingAcks--;
    if (branchTask.RemainingAcks <= 0)
    {
        _pendingBranchTasks.Remove(ev.TransactionId);
        if (branchTask.TimeExtracted) { _masterSync?.SnapAndPause(...); }
        _replayMasterModule?.RestoreTime();
        PublishOpStatus(branchTask.RequestId, OrchestrationStatusCode.Success);
        ...
    }
    continue;
}
```

The `LiveBranchResult` payload is now handled by `LiveBranchProcessManager` which reads
`ClusterOpCompletedEvent`. The main transition ACK handler (`_pendingBusTransitionAcks` path)
will handle the `PrepareLive` ACK like any other prepare step.

**Remove `SetReplayMasterModule`** (public method ~line 1229):
```csharp
public void SetReplayMasterModule(ReplayMasterModule module)
```

**Note:** `_masterSync` and `SetMasterSync` are NOT removed in T001 -- they are still used by
`ProcessSeekReplayIntent`. They will be removed in T002.

### Step 3: Wire in OrchestratorSubsystem

In `OrchestratorSubsystem`:
- Construct `LiveBranchProcessManager` with the existing `_replayMasterModule` and `_masterSync`
  instances.
- Tick it in `Update()` BEFORE `ClusterMaster.Tick()` (so FreezeTime runs before fan-out).
- Remove the `_master.SetReplayMasterModule(...)` call.

### Step 4: Unit Tests

Create `Hrot/Subsystems/Hrot.Orchestrator.Tests/LiveBranchProcessManagerTests.cs`.

Import conventions (same as existing tests):
```csharp
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
```

Use mock objects for `ReplayMasterModule` and `MasterSyncController` (simple counter-based
mocks with `FreezeCount`, `RestoreCount`, `SnapAndPauseCallCount` fields).

**SC1 (FreezeTime called on branch transition)**

Setup: `ClusterMaster` with one mandatory node ("SimHost"). Bootstrap to `OperatingReplay` state
(heartbeat → Tick → transition to OperatingReplay via `ClusterOpRequest`). Create
`LiveBranchProcessManager` with mock `ReplayMasterModule` and mock `MasterSyncController`. Tick
`LiveBranchProcessManager` BEFORE `ClusterMaster.Tick()`.

Action: publish a `TransitionStateIntent` to the bus (or issue it via `HandleClusterOpRequest`
with `OperatingReplay → LoadingLive`). Tick the pair `[liveBranchMgr.Tick(); bus.SwapBuffers(); master.Tick(); bus.SwapBuffers()]`.

Assert: `mockReplayMasterModule.FreezeCount == 1`.

**SC2 (RestoreTime + SnapAndPause after ACK)**

Continuation of SC1 (or new setup in OperatingLive after a branch). Publish
`ClusterOpCompletedEvent(Success, ResultPayload=LiveBranchResult{HistoricalTime.TotalWallTicks=42, TotalTime=1.5})`.
Tick `liveBranchMgr`.

Assert: `mockReplayMasterModule.RestoreCount == 1`.
Assert: `mockMasterSync.SnapAndPauseCallCount == 1`.
Assert: `mockMasterSync.LastWallTicks == 42`.
Assert: `mockMasterSync.LastTotalTime == 1.5`.

**SC3 (No FreezeTime for non-Replay branch)**

Setup: Bootstrap `ClusterMaster` to `OperatingLive` (not `OperatingReplay`). Issue a
`LoadingLive` transition. Tick `liveBranchMgr` before `ClusterMaster`.

Assert: `mockReplayMasterModule.FreezeCount == 0`.

**SC4 (Compiler verification)**

`ClusterMaster` must not contain `_replayMasterModule`, `_pendingBranchTasks`,
`BranchTransitionTask`, or `SetReplayMasterModule`. Verify by attempting to compile
(build must succeed). Add a comment in the test file: `// SC4: Verified by successful build`.

---

## TASK-T002: ReplaySeekAggregator and ReplaySeekProcessManager

### Overview

`ReplaySeekProcessManager` takes over `SlaveNodeSetUpdatedEvent`/`PauseTimeIntent` publication
and `SnapAndPause` for seek results from `ClusterMaster.ProcessSeekReplayIntent` /
`ConsumeNodeOpStatuses`. After this task, `_masterSync` is fully removed from `ClusterMaster`.

### Step 1: Create `ReplaySeekAggregator`

Create `Hrot/Subsystems/Hrot.Orchestrator/ReplaySeekAggregator.cs`:

```csharp
// ReplaySeekAggregator: INodeResponseAggregator for NodeOpType.NodeReplaySeek.
// Returns the first ReplaySeekResult where RestoredTime.TotalWallTicks != 0.
```

`TargetOp` returns `NodeOpType.NodeReplaySeek`.

`Aggregate()` iterates `nodeResponses`, deserializes each value to `ReplaySeekResult` using
`OrchestrationJsonOptions.Default`, and returns the first where
`result.RestoredTime.TotalWallTicks != 0`. Returns null if none found. Skips malformed JSON
without throwing.

Register `ReplaySeekAggregator` in `OrchestratorSubsystem.Initialize` via
`_master.RegisterAggregator(new ReplaySeekAggregator())`.

### Step 2: Create `ReplaySeekProcessManager`

Create `Hrot/Subsystems/Hrot.Orchestrator/ReplaySeekProcessManager.cs`:

Constructor:
```csharp
public ReplaySeekProcessManager(FdpEventBus bus, MasterSyncController masterSync)
```

**`Tick()` logic:**

1. Read `SeekReplayIntent` from bus. For each intent:
   - Build `slaveIds` from... The process manager doesn't have the roster. It needs a way to
     get the current active slave node IDs. Options:
     (a) Subscribe to `NodeHeartbeatEvent` to maintain `_activeSlaveIds` locally (preferred).
     (b) Accept a `Func<HashSet<int>>` delegate from `OrchestratorSubsystem`.
   - Use option (a): maintain `_activeSlaveIds` by reading `NodeHeartbeatEvent` in `Tick()`.
     On each heartbeat, update a local dict. On node timeout, ClusterMaster handles eviction
     but the process manager keeps stale IDs -- acceptable since this is a precondition event.
     Actually, just keep all IDs seen in heartbeats. This mirrors what ClusterMaster does:
     `_roster.ActiveNodes.Where(kv => kv.Value.SubsystemName is "SimHost" or "IG" or "CGF")`.
     Since the process manager doesn't have the roster, simplify: publish
     `SlaveNodeSetUpdatedEvent` with the IDs from the last `NodeHeartbeatEvent` seen for
     each node -- filter by checking `SubsystemName` from the heartbeat.
   - Actually the simplest approach: `ReplaySeekProcessManager` reads
     `NodeHeartbeatEvent` to maintain a local `Dictionary<int, string> _nodeSubsystems`
     map (NodeId → SubsystemName). Then on `SeekReplayIntent`, builds slaveIds from that map
     filtered by "SimHost", "IG", "CGF".
   - Publish `SlaveNodeSetUpdatedEvent { SlaveNodeIds = slaveIds }`.
   - Publish `PauseTimeIntent()`.
   - Note: the actual `NodeReplaySeek` fan-out is STILL done by `ClusterMaster.ProcessSeekReplayIntent`
     after this task. The process manager only handles the SlaveNodeSetUpdated + PauseTime
     preconditions. This means `ClusterMaster.ProcessSeekReplayIntent` is reduced to fan-out only
     (remove the `SlaveNodeSetUpdatedEvent` and `PauseTimeIntent` publications from it).

2. Read `ClusterOpCompletedEvent` from bus. For each event where `ResultPayload` is a
   `ReplaySeekResult sr` and `sr.RestoredTime.TotalWallTicks != 0`:
   - Call `_masterSync.SnapAndPause(sr.RestoredTime.TotalWallTicks, sr.RestoredTime.TotalTime, new HashSet<int>(_activeNodeIds))`.
   - For `_activeNodeIds`: use the same local replica built from heartbeats.

### Step 3: Modify ClusterMaster

**In `ProcessSeekReplayIntent`:** Remove the `SlaveNodeSetUpdatedEvent` and `PauseTimeIntent`
publications. Keep the fan-out (`FanOutNodeOp(NodeOpType.NodeReplaySeek, ...)`) and the
`_pendingBusTransitionAcks` setup. The method becomes:

```csharp
private void ProcessSeekReplayIntent(SeekReplayIntent intent)
{
    // RT-008: fan-out with ACK tracker; immediate Success when roster is empty
    var seekNodeIds = new List<int>(_roster.ActiveNodes.Keys);
    if (seekNodeIds.Count == 0)
    {
        PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
        return;
    }
    var txId = Guid.NewGuid();
    FanOutNodeOp(NodeOpType.NodeReplaySeek, txId,
        new ReplaySeekPayload(intent.TargetWallTicks), seekNodeIds);
    _pendingBusTransitionAcks[txId] = new BusTransitionAckTracker
    {
        RequestId = intent.RequestId,
        Expected  = seekNodeIds.Count,
    };
}
```

Also remove the duplicate `SlaveNodeSetUpdatedEvent` + `PauseTimeIntent` publications in
`ProcessSingleClusterOpRequest` (PauseTime case, lines ~444-445). Wait -- those are for the
`PauseTime` operation, NOT `ReplaySeek`. Do NOT remove those; they are correct as-is.

**In `ConsumeNodeOpStatuses`, remove the seek-result SnapAndPause block:**

In the `_pendingBusTransitionAcks` path, remove:
```csharp
if (tracker.SeekResult == null &&
    ev.ResultPayload is ReplaySeekResult sr &&
    sr.RestoredTime.TotalWallTicks != 0)
{
    tracker.SeekResult = sr;
}
```
and the subsequent:
```csharp
if (tracker.SeekResult.HasValue)
{
    var sr2 = tracker.SeekResult.Value;
    _masterSync?.SnapAndPause(...);
}
```

**Remove `BusTransitionAckTracker.SeekResult` field** (line ~190).

**Remove `_masterSync` field and `SetMasterSync` method:**
- Remove `private MasterSyncController? _masterSync;` (line ~131)
- Remove `public void SetMasterSync(MasterSyncController sync)` (line ~1238)

**Also update `ConsumeNodeOpStatuses` aggregation call:** After removing the SeekResult block,
the code that calls `TryAggregate` should use the `ReplaySeekAggregator` to aggregate the result.
Verify that `ReplaySeekAggregator` is registered and that `TryAggregate` (or the aggregator
lookup) correctly returns `ReplaySeekResult` as `ResultPayload` in `ClusterOpCompletedEvent`.
`ReplaySeekProcessManager` reads this `ClusterOpCompletedEvent` with `ReplaySeekResult` payload
and calls `SnapAndPause`. The flow:
1. All `NodeReplaySeek` ACKs arrive → `tracker.Received >= tracker.Expected`
2. `TryAggregate` calls `ReplaySeekAggregator.Aggregate()` → returns `ReplaySeekResult`
3. `PublishOpStatus(requestId, Success, aggregated)` → publishes `ClusterOpCompletedEvent(Success, ReplaySeekResult)`
4. `ReplaySeekProcessManager.Tick()` reads `ClusterOpCompletedEvent` → calls `SnapAndPause`

### Step 4: Wire in OrchestratorSubsystem

- Construct `ReplaySeekProcessManager(bus, masterSync)`.
- Tick it in `Update()` AFTER `ClusterMaster.Tick()` (same as episode/storage managers).
- Register `ReplaySeekAggregator` via `_master.RegisterAggregator(...)`.
- Remove `_master.SetMasterSync(...)` call.

### Step 5: Unit Tests

Create `Hrot/Subsystems/Hrot.Orchestrator.Tests/ReplaySeekProcessManagerTests.cs`.

**SC1 (SlaveNodeSetUpdatedEvent + PauseTimeIntent published on SeekReplayIntent)**

Setup: `ClusterMaster` with one mandatory node ("SimHost"). Bootstrap to `OperatingReplay`.
Create `ReplaySeekProcessManager` with mock `MasterSyncController`. Register
`ReplaySeekAggregator`. Tick `ReplaySeekProcessManager` BEFORE `ClusterMaster.Tick()`.

Publish `SeekReplayIntent(TargetWallTicks=1000, RequestId=X)` to bus. Swap. Tick pair
`[seekMgr.Tick(); bus.SwapBuffers(); master.Tick(); bus.SwapBuffers()]`.

Assert: `SlaveNodeSetUpdatedEvent` is on the bus (check `bus.ReadManaged<SlaveNodeSetUpdatedEvent>()`).
Assert: `PauseTimeIntent` is on the bus.
Assert: `ExecuteNodeOpIntent` with `Operation == FdpNodeOpType.NodeReplaySeek` is on the bus
(published by `ClusterMaster`).
Assert: `ClusterMaster` does NOT publish `SlaveNodeSetUpdatedEvent` or `PauseTimeIntent`.
(Verify by checking that the only publisher is `ReplaySeekProcessManager`.)

**SC2 (SnapAndPause called on successful seek ACK)**

Continuation of SC1. Capture `txId` from the `NodeReplaySeek` intent.
Publish `NodeOpCompletedEvent(txId, NodeReplaySeek, NodeId=1, Success, ResultPayload=...)`.

Wait -- the `ResultPayload` in `NodeOpCompletedEvent` is a string (JSON) not a typed object.
The aggregator deserializes it. So publish an ACK with the JSON serialized `ReplaySeekResult`:
```csharp
bus.PublishManaged(new NodeOpCompletedEvent
{
    TransactionId = txId,
    Operation     = FdpNodeOpType.NodeReplaySeek,
    NodeId        = 1,
    StatusCode    = OrchestrationStatusCode.Success,
    IsParticipating = true,
    // ResultPayload is string JSON for the aggregator:
});
```

But wait -- `NodeOpCompletedEvent.ResultPayload` is `object?`. For the aggregator to work,
the raw JSON string must be stored. Look at how `ConsumeNodeOpStatuses` populates
`nodeResponses` for the transition ACK tracker... Actually looking at the code, 
`_inflightTransitionTx.NodeResponses` is populated in the transition ACK path. 
For seek, `_pendingBusTransitionAcks[txId]` does NOT have `NodeResponses` -- it uses `SeekResult`
directly read from `ev.ResultPayload as ReplaySeekResult`. 

After T002, `SeekResult` is removed from `BusTransitionAckTracker`. The aggregator path requires
that `NodeResponses` dictionary is populated for seek ACKs. Look at what `ConsumeNodeOpStatuses`
does for the transition ACK tracker path to see if `NodeResponses` is already populated there.

**Developer action required:** Investigate how the seek ACK feeds the aggregator. Specifically:
- Does `_pendingBusTransitionAcks[txId]` have a `NodeResponses` dict?
- Is there a `TryAggregate(txId)` call in the seek completion path?
- If not, add a `NodeResponses = new Dictionary<int, Dictionary<NodeOpType, string>>()` field to
  `BusTransitionAckTracker` and populate it in the seek ACK loop.

After ACK handling, `ReplaySeekProcessManager.Tick()` reads `ClusterOpCompletedEvent` where
`ResultPayload is ReplaySeekResult sr` and calls `SnapAndPause`.

Assert: `mockMasterSync.SnapAndPauseCallCount == 1`.
Assert: `mockMasterSync.LastWallTicks == 5000`.

**SC3 (No SnapAndPause when TotalWallTicks == 0)**

Publish seek ACK with `ReplaySeekResult{RestoredTime.TotalWallTicks=0}`.
Assert: `SnapAndPause` NOT called.

---

## Build and Test Verification

After implementation:

```
dotnet build Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj
dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj --no-build
```

Expected: 0 build errors. 3 pre-existing failures only (Archive, FanOut, Prefetch). All new
tests pass.

---

## Report Template

```
# BATCH-03 Report

## Tasks
- [x/~] TASK-T001: LiveBranchProcessManager
- [x/~] TASK-T002: ReplaySeekAggregator + ReplaySeekProcessManager

## Build
0 errors / N warnings

## Test Results
- Total: N passed, M failed
- New tests: [list]
- Pre-existing failures: Archive, FanOut, Prefetch

## Deviations / Notes
[...]
```
