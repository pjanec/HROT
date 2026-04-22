# CGF-1-BATCH-06 Report

**Batch:** CGF-1-BATCH-06  
**Developer:** AI  
**Date:** 2026-03-28  
**Status:** COMPLETE

---

## Summary

Part A (correctness debt + BATCH-05 follow-ups) and Part B (CGF1-S0203 time strategy proxying) both complete. Solution builds with zero new errors. All new tests pass. Pre-existing parallel DDS contention flakes in `ModuleHost.Core.Tests` and `Fdp.Tests` that appear only under full-solution parallel runs are unchanged pre-existing infrastructure issues (tracked separately).

---

## Part A — Tech debt & BATCH-05 follow-ups

### A.1 — `ClusterSlave` heartbeat must reflect local DSM state (P2 — BATCH-05 Issue 1)

**Files modified:**
- `Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs`
- `Hrot.SimHost.Tests/ClusterSlaveHandlerTests.cs`

**Change:** `PublishHeartbeat()` previously hardcoded `LocalClusterState = ClusterState.Standby`. Now uses `_localClusterState` which is updated on every successful `CommitState` dispatch.

An `internal ClusterState LocalClusterStateForTest => _localClusterState` test-seam property was added (consistent with the `EnqueueCommandForTest` pattern already present) to allow the new unit test to assert the committed value without DDS.

**New test:** `ClusterSlaveHandlerTests.LocalClusterState_ReflectsCommittedState_AfterCommitState`
- Constructs a DDS-less `ClusterSlave` via the internal constructor.
- Asserts initial state is `Standby`.
- Enqueues `CommitState(LoadingLive)` and calls `Tick()`.
- Asserts `LocalClusterStateForTest == ClusterState.LoadingLive` — confirming the next heartbeat would carry the updated value.

### A.2 — DEBT-TRACKER hygiene

**File modified:** `.dev/DEBT-TRACKER.md`

Changes:
1. **`LocalClusterState` heartbeat row** (Source: CGF-1-BATCH-05 review) — marked ✅ with `CGF-1-BATCH-06` fix note.
2. **`CommitState_RaisesEsmStateChangedEvent` typo row** (Source: CGF-1-BATCH-05 review) — marked ✅ after rename in A.3.
3. **PrepareAsync/Commit fire-and-forget row** (Source: CGF-1-BATCH-02 review) — description refreshed to say "CGF1-S0202 delivered event/handler wiring only; the fire-and-forget stub is the current intended behaviour"; target remains `CGF1-S0304`.

No CGF rows with `CGF-1-BATCH-05` as an open target were found (all had been resolved or rolled forward to BATCH-07 in prior batches).

### A.3 — Optional quick wins (both done)

**A.3a — Rename test** (`Hrot.SimHost.Tests/ClusterSlaveHandlerTests.cs`)  
`CommitState_RaisesEsmStateChangedEvent` → `CommitState_RaisesClusterStateChangedEvent`.

**A.3b — Add whitespace payload test** (`Hrot.Orchestrator.Tests/TransitionPlannerTests.cs`)  
New test `PlanTrajectory_WhitespaceOnlyPayload_Throws` — verifies `IsNullOrWhiteSpace` guard throws `InvalidOperationException` for `PayloadJson = "   "`.

---

## Part B — CGF1-S0203: Time strategy proxying

### Verification

`ITimeController` interface confirmed in `FDP/ModuleHost/ModuleHost.Core/Time/ITimeController.cs`. `SwitchableTimeController`, `MasterTimeController`, `SlaveTimeController`, `SteppedMasterController`, `SteppedSlaveController` all exist and implement it. `GlobalTime.TotalWallTicks` (`long`) confirmed in `FDP/Kernel/Fdp.Kernel/GlobalTime.cs`.

`SwitchableTimeController.SwitchTo`: already calls `newController.SeedState(currentState)` before assigning and already has no-op guard for same-instance. ✅

`GlobalTime.TotalWallTicks` populated on every `Update()`: `MasterTimeController` accumulates from `_wallClock.ElapsedTicks`; `SlaveTimeController` returns `_virtualWallTicks`. ✅

### Code changes

**`FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterTimeController.cs`**  
`SeedState()` now calls `PublishTimePulse(now, _totalTime)` immediately before returning (instead of forcing the pulse by rewinding `_lastEventsTicks`). `_lastEventsTicks` is set to `now` so the 1 Hz throttle is correct after the seed. `_totalWallTicks` is also seeded from `state.TotalWallTicks`.

**`FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveTimeController.cs`**  
`SeedState()` now sets `_virtualWallTicks = state.TotalWallTicks` before resetting the wall clock and filter, bypassing PLL slew. The stale comment "No, wait for pulse to sync PLL" was removed.

### New tests (all in `FDP/Toolkits/FDP.Toolkit.Time.Tests/`)

| Test class | Test | Covers |
|---|---|---|
| `SwitchableTimeControllerTests` | `SwitchTo_TransfersCurrentStateToNewController` | `SwitchTo` seeds new controller with master's TotalTime |
| `SwitchableTimeControllerTests` | `SwitchTo_SameInstance_IsNoOp` | No-op guard; reference + TotalTime unchanged |
| `MasterTimeControllerTests` | `SeedState_PublishesTimePulseImmediately` | Exactly one `TimePulseDescriptor` with `SimTimeSnapshot ≈ 100.0` published in `SeedState` |
| `SlaveTimeControllerTests` | `SeedState_BypassesJitterFilter` | Next `Update()` after seed returns `TotalTime ≈ 900.0` with no slew |
| `GlobalTimeTests` | `TotalWallTicks_IsPopulatedByMasterController` | `TotalWallTicks > 0` after `Update()` |

A new file `SwitchableTimeControllerTests.cs` was created for the two `SwitchableTimeController` tests. The other three were appended to their respective existing test classes.

---

## Test runs

| Assembly | Passed | Failed | Notes |
|---|---|---|---|
| `Hrot.SimHost.Tests` | 364 | 0 | +1 new heartbeat test, +1 rename |
| `Hrot.Orchestrator.Tests` | 18 | 0 | +1 whitespace payload test |
| `FDP.Toolkit.Time.Tests` | 57 | 0 (+1 skip) | +5 S0203 tests; skip is pre-existing |
| Full solution (serial) | all individual assemblies pass | — | 2 flakes under parallel run are pre-existing DDS domain contention (tracked) |

---

## Success criteria

- [x] `LocalClusterState` on SimHost heartbeats matches `_localClusterState` after commits.
- [x] Test added verifying heartbeat state seam (`LocalClusterState_ReflectsCommittedState_AfterCommitState`).
- [x] DEBT rows updated: heartbeat ✅, typo ✅, PrepareAsync description refreshed.
- [x] CGF1-S0203 success conditions met: all 5 named tests pass.
- [x] Solution builds clean (0 errors, existing warning count unchanged).
- [x] Report filed.
