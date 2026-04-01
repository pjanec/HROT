# BATCH-03 Report

## Completion Status

**Completed**

Both tasks (TCU-SC001 and TCU-T002) are fully implemented, building with zero errors, and all
111 tests pass (plus 1 pre-existing skip).

---

## Test Results

```
Passed!  - Failed: 0, Passed: 111, Skipped: 1, Total: 112, Duration: 1 s
```

Full run: `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj`

- 99 pre-existing tests: all passing
- 12 new `SlaveSyncControllerTests` tests: all passing
- 1 pre-existing skip (`MasterSlave_Lockstep_WaitsForSlowPeer`) unchanged

### New tests

| # | Test name | Status |
|---|---|---|
| 1 | `SlaveSyncController_ContinuousMode_PLLTracksTimePulse` | ✅ |
| 2 | `SlaveSyncController_NoTimePulseEmitted` | ✅ |
| 3 | `SlaveSyncController_BarrierPending_PLLContinuesDuringWait` | ✅ |
| 4 | `SlaveSyncController_TransitionsToStepping_WhenBarrierCrossed` | ✅ |
| 5 | `SlaveSyncController_Stepping_AdvancesOnAdvanceFrameIntent` | ✅ |
| 6 | `SlaveSyncController_Stepping_WaitsWithDeltaZeroWhenNoIntent` | ✅ |
| 7 | `SlaveSyncController_Stepping_PublishesFrameStepCompletedEvent` | ✅ |
| 8 | `SlaveSyncController_Resume_SnapsToMasterSimTime` | ✅ |
| 9 | `SlaveSyncController_Resume_PLLIsWarm_NoJitterReset` | ✅ |
| 10 | `SlaveSyncController_Stepping_SnapsToTargetSimTime_WhenProvided` | ✅ |
| E1 | `SlaveSyncController_TwoConsecutivePauseResumeCycles_WithoutPLLReset` | ✅ |
| E2 | `SlaveSyncController_OutOfOrderAdvanceFrameIntent_IsIgnored` | ✅ |

---

## Developer Insights

### Q1: Issues Encountered

**Issue 1 — Frame-counter divergence on first Stepping entry.**

The slave accumulates `_frameNumber` during Continuous frames (`_frameNumber++` in
`AdvanceContinuousTime`). When the master transitions to Stepping its own `_frameNumber` may be
entirely different (the master does not increment during BarrierPending), so the first
`AdvanceFrameIntent` the slave receives may carry a `FrameID` *lower* than the slave's
local `_frameNumber`.

The fix was a dedicated `_lastAcceptedStepFrameId` variable (initialised to `-1` each time
Stepping is entered) rather than comparing against `_frameNumber`. This precisely mirrors the
`_lastReceivedOrderFrameId` pattern in `SteppedSlaveController`. The filter becomes
`intent.FrameID > _lastAcceptedStepFrameId`, which on first entry (`-1`) accepts anything ≥ 0,
and after that tracks the master's monotonically-increasing FrameID.

**Issue 2 — Test `TotalTime ≈ 0.016` assertion.**

The per-step assertion `TotalTime ≈ 0.016` requires TotalTime to be near zero at the moment
the controller enters Stepping. The original `TransitionToStepping` helper ran one full 16ms
Continuous frame before entering BarrierPending, leaving TotalTime ≈ 0.016 *before* the first
step — which then made the post-step assertion `≈ 0.032`, not `0.016`.

Fix: the helper now uses `barrierWallTicks = 0` so the first BarrierPending `Update()` crosses
the barrier immediately after adding 1 raw tick. TotalTime from that frame is ~33 ns (negligible),
keeping post-step assertions clean.

### Q2: Weak Points Spotted

1. **Mode-switch event ordering in BarrierPending.** If a `SwitchTimeModeEvent(Continuous)` arrives
   while the controller is `BarrierPending` (e.g. the orchestrator quickly reverses a pause), the
   current code's `DrainModeSwitchEvents` will see the Continuous event but the Deterministic one
   was already applied on a previous frame.  The resume path discards `_pendingBarrierWallTicks` and
   returns to Continuous cleanly, so functionally this works — but it is not tested. A rapid
   pause→resume within the same receive window could be worth a test.

2. **Multiple intents per Update().** The controller processes one intent per `Update()` call.
   The queue drains all newly-received intents then dequeues one. If the master sends a burst of
   intents (e.g. after a network jitter stall), the slave processes them one per frame, which is
   correct for lockstep but could create visible stutter. Not a bug, but worth documenting when
   wiring into SimHost so the application host knows not to call `Update()` in a tight loop
   expecting all frames to complete in one tick.

3. **`SetTimeScale` is user-callable on the slave.** The public API allows arbitrary scale
   changes, but the slave's scale is authoritative from the master (via `TimePulseDescriptor` or
   `SwitchTimeModeEvent`). An external call to `SetTimeScale()` would be silently overwritten on
   the next pulse. This is consistent with `SlaveTimeController` behaviour but could confuse
   integrators.

4. **No `Dispose` action.** The bus subscriptions (`Register<T>()`) are currently not unregistered
   on `Dispose()`. If the bus outlives the controller (e.g. hot-swap scenarios), dangling
   registrations could cause events to accumulate. This matches the existing pattern in the
   codebase and is deferred to Phase 5 wiring.

### Q3: Design Decisions Made Beyond the Spec

1. **`_lastUpdateRawTicks` initialised from `_getTick()` at construction.**  
   The spec lists `_lastUpdateRawTicks` in internal state but does not specify its initial value.
   Initialising to `_getTick()` at construction time mirrors `SlaveTimeController`'s approach and
   ensures the very first `Update()` measures a real elapsed delta from construction time, not a
   spurious large delta from tick 0.

2. **`_virtualWallTicks` also initialised from `_getTick()` at construction.**  
   To make the barrier-crossing check (`_virtualWallTicks >= barrierWallTicks`) work correctly in
   tests that start with `ticks=0`, `_virtualWallTicks` is seeded from the tick source at
   construction. Tests that pass `barrierWallTicks=0` must add at least 1 tick before `Update()`.
   A comment in the test helper explains this contract.

3. **BarrierPending increments `_frameNumber`.**  
   Unlike `MasterSyncController` (which deliberately does NOT increment `_frameNumber` in
   BarrierPending), the slave increments it via `AdvanceContinuousTime`. This keeps the slave's
   continuous frame count consistent — the slave is a passive observer during the barrier wait,
   still advancing time and frames for its local consumers. The master avoids incrementing because
   its `_frameNumber` is used as the deterministic step counter; the slave's `_frameNumber` during
   Continuous is a different semantic (local frame count vs. lockstep frame ID). The
   `_lastAcceptedStepFrameId` variable cleanly separates these two semantics.

4. **Single-intent-per-Update processing.**  
   The spec says "for each intent" which could imply processing all queued intents in one call.
   However, processing one per `Update()` is the safer interpretation for lockstep: it keeps
   the returned `GlobalTime.DeltaTime` meaningful (not a sum of N deltas), matches
   `SteppedSlaveController`'s pattern, and ensures one `FrameStepCompletedEvent` is published
   per `Update()` — predictable for the master's ACK logic.
