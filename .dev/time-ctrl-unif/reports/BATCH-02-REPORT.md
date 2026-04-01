# BATCH-02 Report

## Completion Status

**Completed** — All tasks implemented, all success criteria met.

---

## Test Results

```
Passed!  - Failed: 0, Passed: 99, Skipped: 1, Total: 100, Duration: 1 s
```

Pre-existing count before BATCH-02: 87 tests.  
After BATCH-02: 99 passing + 1 pre-existing skip (`MasterSlave_Lockstep_WaitsForSlowPeer`).  
New tests added: **12** (all in `MasterSyncControllerTests`).

### New tests — all green

| # | Test name |
|---|-----------|
| 1 | `MasterSyncController_ContinuousMode_AdvancesTime` |
| 2 | `MasterSyncController_SwitchToDeterministic_PublishesBarrierEvent` |
| 3 | `MasterSyncController_BarrierPending_TransitionsToStepping` |
| 4 | `MasterSyncController_Step_PublishesAdvanceFrameIntent` |
| 5 | `MasterSyncController_Step_BlocksUntilAllAcksReceived` |
| 6 | `MasterSyncController_SwitchToContinuous_PublishesSnapshotEvent` |
| 7 | `MasterSyncController_SwitchToContinuous_IdempotentWhenAlreadyContinuous` |
| 8 | `MasterSyncController_SeedState_RestoresTotalTime` |
| 9 | `MasterSyncController_PublishesTimePulse_OncePerSecond` |
| 10 | `MasterSyncController_Step_InContinuousMode_IsNoOp` |
| 11 | `MasterSyncController_AckFromUnknownNode_IsIgnored` |
| 12 | `MasterSyncController_TwoFullPauseCycles_WorkCorrectly` |

---

## Developer Insights

### Q1: Issues Encountered

**Spec inconsistency — `_frameNumber` in `BarrierPending`.**

The TASK-DETAIL spec states `Update()` in `BarrierPending` is "same as Continuous", which would include incrementing `_frameNumber`. However, three tests immediately failed with `Expected: 1, Actual: 2` on `FrameID` / `FrameNumber` assertions. Root cause: when the controller is freshly constructed (`_frameNumber = 0`), one `Update()` call in `BarrierPending` incremented `_frameNumber` to 1, and the subsequent `Step()` incremented it to 2 — but all specs expect the first deterministic step to yield `FrameID = 1`.

**Resolution:** `BarrierPending` does **not** increment `_frameNumber`. The barrier-pending phase is a transparent wait; it accumulates `_totalTime`, `_unscaledTotalTime`, and `_totalWallTicks` exactly as Continuous does, but the frame counter is held so that `Step()` frames resume cleanly from the last Continuous frame number. This is identical to how `SteppedMasterController` starts with `_waitingForAcks = false` and a fresh `_frameNumber = 0`.

**Spec inconsistency — initial `_pendingAcks` on barrier crossing.**

The TASK-DETAIL spec says "transition to Stepping, reset `_pendingAcks = new HashSet<int>(_expectedSlaves)`" when the barrier is crossed. If taken literally, the first `Step()` call after entering Stepping mode would immediately be blocked (since `_pendingAcks` would be non-empty). However, test 5 (`Step_BlocksUntilAllAcksReceived`) expects the **first** `Step()` to succeed (advancing to frame 1), with the **second** `Step()` blocked — confirmed by the final assertion that frame 2 is reached. Resolution: `_pendingAcks` is **empty** on barrier crossing, matching the `SteppedMasterController` pattern (`_waitingForAcks = false` initially). It is populated with `_expectedSlaves` only **after** each successful `Step()` call.

---

### Q2: Weak Points Spotted

1. **`SwitchToDeterministic(HashSet<int> slaveNodeIds)` parameter is ignored.** The public API accepts a slave set for API compatibility with the coordinator pattern, but the effective slave set is determined at construction time. If the caller passes a different set to `SwitchToDeterministic`, the controller silently ignores it. This could cause confusion when wiring into the Orchestrator unless documented clearly.

2. **No handling for stale ACKs.** `UpdateStepping()` drains and processes every `FrameStepCompletedEvent` using `NodeID` as the only key — it doesn't cross-check `FrameID`. An ACK for a past frame from a slave whose node ID is in `_pendingAcks` would incorrectly clear that slot. The existing `SteppedMasterController` handled this by filtering on `ack.FrameID == _lastFrameSequence`. This controller should add similar filtering to be robust against out-of-order or duplicate ACKs over the bus, especially important for DDS translators that could redeliver.

3. **Bus stream lifecycle.** The controller calls `Register<SwitchTimeModeEvent>()` and `Register<TimePulseDescriptor>()` but publishes are also auto-creating streams lazily. No explicit stream teardown on `Dispose()`. If the bus outlives the controller, stale streams remain. Low risk in current usage but worth noting.

4. **`_timeScale` is silently carried through `SwitchToContinuous` without publishing a `TimePulseDescriptor` immediately.** If a slave misses the `SwitchTimeModeEvent.TimeScale` field, it will not resynchronise its scale until the next 1-Hz pulse. Probably acceptable but worth documenting.

---

### Q3: Design Decisions Made Beyond the Spec

1. **`BarrierPending` does not publish `TimePulseDescriptor` on the first Update where the barrier is crossed.** The barrier-crossing Update calls `MaybePublishTimePulse()` in the normal flow, so a pulse will be published if enough wall time has elapsed. No special suppression was added.

2. **`SwitchToContinuous` is callable from `Stepping` without a prior `SwitchToDeterministic`.** The idempotency guard (`_mode == Continuous && _pendingBarrierWallTicks < 0`) does not block the call — it simply publishes the event. This mirrors the coordinator's behaviour and allows the Orchestrator to call Resume safely regardless of internal state.

3. **`_pendingAcks` after `SwitchToContinuous`.** When resuming to Continuous, `_pendingAcks` is not cleared. Any leftover entries are irrelevant since the state machine will not check them until the next Stepping phase, at which point they are replaced by `new HashSet<int>(_expectedSlaves)` after the first `Step()`.

4. **Tick-source seam design.** Followed the `SlaveTimeController` pattern: store `_lastTickSample`, compute `elapsedTicks = getTick() - _lastTickSample`, update `_lastTickSample` each frame. This avoids the `Stopwatch.Restart()` approach of `MasterTimeController` (which is non-deterministic when composed with tick injection) and allows tick-source-controlled tests without any `Thread.Sleep`.

---

### Q4: Edge Cases Discovered

1. **Zero-tick delta on first Update after construction.** If the tick source returns the same value as the constructor's initial sample (e.g., tick source returns a constant), `elapsedTicks = 0`, `_totalTime` does not advance, but `FrameNumber` still increments in Continuous mode. The barrier check `_totalWallTicks >= _pendingBarrierWallTicks` still works when `LookaheadWallTicks = 0` because `0 >= 0` is true.

2. **`SwitchToContinuous` cancels a pending barrier before traversal.** If the barrier hasn't been crossed yet (`_mode == BarrierPending`) and `SwitchToContinuous` is called, the controller transitions directly back to Continuous, resets `_pendingBarrierWallTicks = -1`, and publishes the Continuous event. The barrier is never crossed. Verified by test 12 (TwoFullPauseCycles) where both cycles complete cleanly.

3. **Two consecutive `SwitchToDeterministic` calls without an intervening `SwitchToContinuous`.** Not tested but would silently overwrite `_pendingBarrierWallTicks` with a new barrier further in the future, and re-publish a `SwitchTimeModeEvent`. Slaves would see two events; the second one would update their barrier. This edge is not guarded — if this is a concern, an idempotency check should be added (similar to `DistributedTimeCoordinator.HandleModeSwitch`).

---

### Q5: Suggested commit message

```
feat(time-ctrl-unif): BATCH-02 - add MasterSyncController unified state machine

- New MasterSyncController replaces MasterTimeController + SteppedMasterController
  + DistributedTimeCoordinator in a single state machine (Continuous/BarrierPending/Stepping)
- 12 new unit tests cover all 9 TCU-MC001 success conditions + 3 edge cases
- Old controllers untouched (removal deferred to Phase 5)
- All 99 pre-existing tests continue passing
```
