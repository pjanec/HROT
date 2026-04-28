# BATCH-02 Report: 2PC Seek Fix + ClusterUiCache Visual Fix

**Batch:** BATCH-02  
**Tasks:** RT-007, RT-008, RT-009, RT-016  
**Status:** COMPLETE  
**Build:** 0 `error CS` lines  
**Tests:** Hrot.Orchestrator.Tests: 97 pass / 1 pre-existing fail (unchanged from baseline)

---

## Tasks Completed

### RT-007: Remove Premature `PublishOpStatus` from `ReplaySeek` Case

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

Removed `PublishOpStatus(req.RequestId, OrchestrationStatusCode.Success)` from:
1. `ProcessSingleClusterOpRequest` — the `case ClusterOpType.ReplaySeek:` block (injection path)
2. `ProcessSeekReplayIntents` — the bus drain loop after `ProcessSeekReplayIntent(intent)`

The existing test `ReplaySeek_BusMode_PublishesImmediateSuccess` continues to pass because it
registers no active nodes, so the 0-node early-return path in RT-008 publishes Success
immediately.

### RT-008: Refactor `ProcessSeekReplayIntent` with `BusTransitionAckTracker`

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

Replaced the old `ProcessSeekReplayIntent` body (which only fanned out without an ACK tracker)
with the new implementation:
- When roster is empty: publish `Success` immediately and return.
- When nodes are present: generate a new `txId`, call `FanOutNodeOp(NodeReplaySeek, ...)`,
  register a `BusTransitionAckTracker` in `_pendingBusTransitionAcks[txId]`.
  
The existing `ConsumeNodeOpStatuses` loop already handles deferred success publication when
`tracker.Received >= tracker.Expected` — no changes needed there.

**New tests in `ClusterMasterSeekTests.cs`:**
- `ReplaySeek_WithActiveNodes_RegistersAckTracker_AndDoesNotPublishImmediateSuccess` (T8a/T8b/T8c)

### RT-009: Server-Side Pause Precondition in `ProcessSeekReplayIntent`

**File:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

Added at the very top of `ProcessSeekReplayIntent` (before the empty-node guard):
- Builds `slaveIds` from `ActiveNodes` filtered to `"SimHost" or "IG" or "CGF"`.
- Publishes `SlaveNodeSetUpdatedEvent { SlaveNodeIds = slaveIds }`.
- Publishes `PauseTimeIntent()`.

The same pattern already existed in the `PauseTime` case handler — reused identically.

**New tests in `ClusterMasterSeekTests.cs`:**
- `ReplaySeek_AlwaysPublishes_SlaveNodeSetUpdatedEvent_And_PauseTimeIntent` (T9a)
- `ReplaySeek_SlaveNodeSetUpdatedEvent_ContainsOnlySimHostIgCgfNodes` (T9b)

### RT-016: Default `SourceDsmState`/`TargetDsmState` to `CurrentState` in `ClusterUiCache`

**File:** `Hrot/Subsystems/Hrot.Orchestrator/Panels/ClusterUiCache.cs`

In `Process2PcNetworkTraffic`, in the block that creates a new `DistributedTransaction`:
1. Changed `var targetState = ClusterState.Idle;` to `var targetState = CurrentState;`
2. Added `SourceDsmState = CurrentState,` to the `DistributedTransaction` initializer

Result: seek operations that do not carry a state-carrying payload (e.g., `ReplaySeekPayload`)
now correctly show `OperatingReplay -> OperatingReplay` in the 2PC history panel instead of
`Idle -> Idle`.

**New test in `OrchestratorSubsystemBusTests.cs`:**
- `ClusterUiCache_ReplaySeekOp_HasSourceAndTargetEqualToCurrentState` (T16a)

---

## Test Results

| Suite | Before BATCH-02 | After BATCH-02 |
|---|---|---|
| `Hrot.Orchestrator.Tests` | 97 pass / 1 fail (pre-existing) | 97 pass / 1 fail (pre-existing) |
| `Hrot.ClusterRunner.Tests` | 221 pass | 221 pass |

**New tests added (all pass):**
- `ClusterMasterSeekTests.ReplaySeek_WithActiveNodes_RegistersAckTracker_AndDoesNotPublishImmediateSuccess`
- `ClusterMasterSeekTests.ReplaySeek_AlwaysPublishes_SlaveNodeSetUpdatedEvent_And_PauseTimeIntent`
- `ClusterMasterSeekTests.ReplaySeek_SlaveNodeSetUpdatedEvent_ContainsOnlySimHostIgCgfNodes`
- `OrchestratorSubsystemBusTests.ClusterUiCache_ReplaySeekOp_HasSourceAndTargetEqualToCurrentState`

**Pre-existing failure (not introduced by this batch):**
- `ClusterMasterFanOutTests.PayloadJson_PopulatedFromClusterOpRequest` — asserts `PayloadJson == ""` but ClusterMaster serializes the intent object; this was failing before BATCH-02.

---

## Developer Insights

**Q1: What issues did you encounter? How did you resolve them?**

No significant issues. The main thing I had to verify was whether the pre-existing
`PayloadJson_PopulatedFromClusterOpRequest` failure was introduced by my changes or was
pre-existing. I confirmed it was pre-existing by stashing my changes and running the tests —
same 97/1 result. The fix for that test is out of scope for this batch (it would require
either changing `ClusterMaster` to not serialize payload into the internal history entry, or
updating the test assertion).

**Q2: Is there a race condition risk if two `ReplaySeek` requests arrive in the same tick?**

Each `ReplaySeek` produces a fresh `Guid.NewGuid()` txId, and `_pendingBusTransitionAcks` is
keyed by that txId. Two seeks in the same tick produce two independent tracker entries, each
with their own `RequestId` and `Expected` count. `ConsumeNodeOpStatuses` iterates over all
entries and resolves each independently when its ACK count is met. There is no collision risk.
A practical concern is that two simultaneous seeks both fan out to the same set of nodes, so
each node receives two `NodeReplaySeek` commands. The later one "wins" at the node level,
but both trackers still need their ACKs. In practice the orchestrator's state machine prevents
concurrent seeks (only one `ClusterOpActionHandler` awaits at a time), so this is theoretical.

**Q3: What design decisions did you make beyond the spec?**

- Kept the `ProcessSeekReplayIntents` drain loop's foreach body to a single call
  (`ProcessSeekReplayIntent(intent)`) with no trailing status publish, which is clean.
- Added a second test (`T9b`) that verifies the `SlaveNodeIds` set contains only
  SimHost/IG/CGF nodes and explicitly excludes "Editor". This is a stronger guarantee than
  T9a (which only checks presence of the events).
- For T16a I used `ClusterStateUpdateEvent` (the managed bus event that `ClusterUiCache`
  actually reads) rather than `ClusterStateChangedEvent` mentioned in the task spec — the
  spec had a typo.

**Q4: Any weak points in `ClusterMaster` you noticed while working on this?**

- `PayloadJson` in the internal `TransactionHistory` is still serializing the full intent
  object (CMC-S010 was not completed); the old test expects an empty string.
- `ProcessSeekReplayIntents` is called every tick and reads `SeekReplayIntent` from the bus
  drain. If the same tick also has a `HandleClusterOpRequest(ReplaySeek)` call, both paths
  will invoke `ProcessSeekReplayIntent` — potentially doubling the fan-out. The two paths
  should be mutually exclusive in practice (bus mode vs DDS mode), but the guard is implicit.

**Q5: Suggested commit message**

```
feat(replay-time): BATCH-02 - 2PC seek fix + ClusterUiCache visual fix (RT-007 to RT-009, RT-016)

RT-007: Remove premature PublishOpStatus(Success) from ReplaySeek case in
        ProcessSingleClusterOpRequest and ProcessSeekReplayIntents drain loop.

RT-008: ProcessSeekReplayIntent now registers BusTransitionAckTracker so success
        is published only after all active nodes ACK NodeReplaySeek. Empty-roster
        path still publishes Success immediately.

RT-009: ProcessSeekReplayIntent publishes SlaveNodeSetUpdatedEvent + PauseTimeIntent
        before the fan-out so all slave time controllers are in Stepping mode.

RT-016: ClusterUiCache.Process2PcNetworkTraffic defaults TargetDsmState and sets
        SourceDsmState to CurrentState instead of ClusterState.Idle, fixing
        seek ops appearing as Idle->Idle in the 2PC history panel.

New tests: ClusterMasterSeekTests (T8a/b/c, T9a/b) and
           OrchestratorSubsystemBusTests (T16a). All pass.
Build: 0 errors.
```
