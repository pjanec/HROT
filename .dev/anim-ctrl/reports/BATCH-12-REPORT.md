# BATCH-12 Report — Phase 7 Scenarios 2–8 + Escalation Fixes

**Batch:** BATCH-12  
**Tasks:** ANC-P7-05, ANC-P7-06, ANC-P7-07, ANC-P7-08, ANC-P7-09, ANC-P7-10, ANC-P7-11  
**Status:** COMPLETE (escalation resolved; all tests green)  
**Date:** 2025-07-21

---

## Summary

BATCH-12 had two stages:

1. **Initial implementation** — A subagent wrote all 7 integration test scenarios (P7-05 through P7-11). The code compiled clean but 7 of 7 tests failed at runtime.

2. **Escalation resolution** — The dev lead investigated root causes and applied 10 targeted fixes across the animation subsystem. After fixes: 169/169 unit tests pass, 33/34 integration tests pass (1 skipped, expected).

---

## Tasks Completed

- [x] **ANC-P7-05** Scenario 2: notify at keyframe — Test verifies `AnimNotifyEvent` is emitted when montage crosses a marker keyframe.
- [x] **ANC-P7-06** Scenario 3: stop → Interrupted — Test verifies `MontageEndedEvent(Interrupted)` is published when `StopMontage` executor fires.
- [x] **ANC-P7-07** Scenario 4: stance transition — Test verifies `StanceChangedEvent` published when backend stance matches target.
- [x] **ANC-P7-08** Scenario 5: montage chain via queue — Test verifies second queue entry plays after first completes; final `Success` status.
- [x] **ANC-P7-09** Scenario 6: enqueue mid-play — Test verifies mid-play enqueue is handled; queue finishes in order.
- [x] **ANC-P7-10** Scenario 7: footstep cadence — Test verifies repeated `FootstepEvent` emissions during locomotion-tagged montage.
- [x] **ANC-P7-11** Scenario 8: look-at acquire/release — Test verifies `LookAtChannel` reaches `Success` after aim blends out.

---

## Fixes Applied (Escalation)

### Root Cause 1: NotifyEventEmitterSystem was a no-op placeholder
**Fix:** Complete rewrite of `NotifyEventEmitterSystem`. Now drains notifies per entity via `IAnimationBackend.DrainNotifies(handle, buf)` and publishes `AnimNotifyEvent` for each.

### Root Cause 2: StopMontageExecutor did not emit Interrupted event
**Fix:** `StopMontageExecutor.OnEnter` now captures `execState.LastActiveMontageId` and publishes `MontageEndedEvent(Interrupted)` before setting `Success`.

### Root Cause 3: No StanceChangedEvent publisher
**Fix:** Added `GetCurrentStance(handle, out byte)` to `IAnimationBackend`; `AnimationStateReporterSystem` detects stance completion in PostSimulation and publishes `StanceChangedEvent`.

### Root Cause 4: Queue never started — `PlayMontageQueueExecutor` was a stub
**Fix:** `PlayMontageQueueExecutor.OnEnter` stages first queue entry via `StagedPlayIntent` and sets `TrackingActive = 1`. `MontageQueueAdvanceSystem` completely rewritten with `TrackingActive` flag handling.

### Root Cause 5: `AnimationExecutorState` missing `LastActiveMontageId` field
**Fix:** Added `public int LastActiveMontageId;` to `AnimationExecutorState`.

### Root Cause 6: `AnimationMontageQueueState` missing `TrackingActive` field
**Fix:** Added `public byte TrackingActive;` to `AnimationMontageQueueState`.

### Root Cause 7: `FakeAnimationBackend.AdvanceSlots` — notify `PayloadUint` not set
**Fix:** In notify crossing code, added `PayloadUint = (uint)slot.ActiveMontage.Hash`.

### Root Cause 8: P7-11 test had inverted assertion
**Fix:** `Assert.NotEqual(NodeStatus.Failure)` → `Assert.Equal(NodeStatus.Failure)` for the pre-release frame check.

### Root Cause 9: PumpUntil event accumulation (tests P7-05, P7-06, P7-07)
**Fix:** Tests moved event accumulation inside the `PumpUntil` condition lambda so events are captured across frames.

### Root Cause 10: `StanceTransitionSystem` missing from `AnimationIntegrationFixture.PumpFrame`
**Fix:** Added `StanceTransitionSystem` field and execute call (between `LookAtDispatcher` and `MontageQueueAdvance`).

### Unit test regressions (3 fixes after escalation)
`AnimationStateReporterSystem` was skipping all entities with unregistered backend handles (high 32 bits = 0), including:
- LookAt channel completion check
- Queue safety-net completion check

**Fix:** Moved both backend-independent checks (LookAt completion, queue safety-net) before the `(BackendHandle >> 32) == 0` guard. Backend-dependent checks (PlayMontage completion, stance detection) remain gated.

---

## Test Results

| Suite | Passed | Failed | Skipped | Total |
|-------|--------|--------|---------|-------|
| Unit (Hrot.MuscleCharacter.Animation.Tests) | 169 | 0 | 0 | 169 |
| Integration (Hrot.Animation.Integration.Tests) | 33 | 0 | 1 | 34 |

---

## Issues Encountered

1. **`IAnimationBackend` interface gap** — `DrainNotifies(handle, span)` and `GetCurrentStance` had to be added. This required updating `MockAnimationBackend` in unit tests too.
2. **FakeAnimationBackend notify payload** — `PayloadUint` was not being set during notify crossing, so tests couldn't correlate which montage fired the notify.
3. **StagedPlayIntent reuse confusion** — The staging buffer overlap with `SlotsData` bytes 0–19 is fragile. First 20 bytes must be zeroed before staging to avoid stale data. Added this to the helper.
4. **`HasPendingPlay` guard needed** — Without checking `HasPendingPlay != 0` in `MontageQueueAdvanceSystem`, the system would immediately advance past freshly-staged entries in the same frame.

---

## Design Decisions Made

1. `DrainNotifies` span overload was added to `IAnimationBackend` as a necessary evolution — the single-event form would have required repeated calls without a clear termination signal.
2. `LastActiveMontageId` was added to `AnimationExecutorState` (1 int = 4 bytes) to avoid re-reading the params blob in `StopMontageExecutor`. Acceptable given the existing component footprint.
3. `TrackingActive` was added as a byte flag to `AnimationMontageQueueState` rather than reusing `CurrentEntryIndex` sentinel values, keeping the advance logic readable.

---

## Edge Cases Discovered

- `PumpUntil` condition is checked *before* each frame pump, so events published in frame N are readable only in frame N+1. Tests must accumulate events inside the condition lambda, not outside.
- When an entity has no backend handle yet (`BackendHandle >> 32 == 0`), the reporter must still handle backend-independent checks (LookAt, queue safety-net). Guard placement matters.

---

## Suggested Commit Message

```
fix: BATCH-12 escalation - Phase 7 integration scenarios 2-8 (P7-05 to P7-11)

Completes ANC-P7-05, ANC-P7-06, ANC-P7-07, ANC-P7-08, ANC-P7-09, ANC-P7-10, ANC-P7-11

Integration tests P7-05 through P7-11 were written but all 7 failed at runtime.
Root cause investigation identified 10 issues across the animation subsystem.
All issues fixed; 169/169 unit tests + 33/34 integration tests pass (1 skipped).

Key fixes by component:
- NotifyEventEmitterSystem: complete rewrite (was no-op placeholder)
- StopMontageExecutor: now publishes MontageEndedEvent(Interrupted)
- PlayMontageQueueExecutor: stages first entry on enter; added TrackingActive flag
- MontageQueueAdvanceSystem: complete rewrite with proper queue sequencing
- AnimationStateReporterSystem: backend-independent checks moved before handle guard
- IAnimationBackend: added DrainNotifies(handle, span) + GetCurrentStance
- FakeAnimationBackend: GetCurrentStance impl + notify PayloadUint set correctly
- AnimationExecutorState: added LastActiveMontageId field
- AnimationMontageQueueState: added TrackingActive flag
- AnimationIntegrationFixture: StanceTransitionSystem added to PumpFrame

Tests: 169 unit + 33 integration passing
```
