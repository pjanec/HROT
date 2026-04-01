# BATCH-05 Review

**Batch:** BATCH-05 — Application Wiring + Deletion + E2E Integration Test  
**Tasks:** TCU-W001, TCU-W002, TCU-W003, TCU-W004, TCU-W006, TCU-T006  
**Reviewer:** Dev Lead  
**Date:** 2026-04-01

---

## ✅ Verdict: APPROVED

All tasks complete. Zero build errors. 70 FDP time tests pass. All 6 (→ now 7) time control integration tests pass. The 6 unrelated pre-existing integration failures confirmed pre-existing (verified against BATCH-04 baseline via `git stash`).

---

## Review Findings

### Scope Check

| Task | Expected | Result |
|------|----------|--------|
| TCU-W001 — Orchestrator | MasterSyncController + MasterLockstepTranslator + TimePulseEgressTranslator | ✅ Done |
| TCU-W002 — SimHost | SlaveSyncController; no SlaveTimeModeListener; no TimePulseEgressTranslator; SlaveLockstepTranslator | ✅ Done |
| TCU-W003 — CGF | SlaveSyncController; no SlaveTimeModeListener; SlaveLockstepTranslator | ✅ Done |
| TCU-W004 — IG | SlaveSyncController | ✅ Done |
| TCU-W006 — Delete obsolete | 8 files + CreateLockstepTranslator removed | ✅ Done |
| TCU-T006 — E2E test | FullCycle_Pause_Step_Resume_NoPllLoss | ✅ Done |

### Critical Bug Fixed

`SlaveSyncController.UpdateStepping` was not refreshing `_lastUpdateRawTicks`. On resume to Continuous, all the wall-clock ticks accumulated during Stepping would be applied as a single giant delta spike, breaking PLL warm-start guarantee. **Fixed: `_lastUpdateRawTicks = _getTick()` added at top of `UpdateStepping`.**

This bug would have caused the PLL warm-start integration test to fail and would have manifested as a time jump on resume in production. Catching it during E2E test development is the correct outcome of TCU-T006.

### Examples Migration

`SwitchableTimeController` deletion required migration of `CarKinemApp`, `HeadlessCarKinemApp`, and `NetworkDemoApp`. The changes are minimal and correct: `_kernel.SetTimeController()` replaces the proxy pattern. This is expected scope creep for a deletion batch.

### Pre-existing Tests Removed

Tests for deleted classes: `MasterTimeControllerTests`, `SteppedMasterControllerTests`, `SteppedSlaveControllerTests`, `SlaveTimeControllerTests`, `SwitchableTimeControllerTests`, `LockstepIntegrationTests`, `DistributedPauseTests`, `FutureBarrierTests`, `WcrBatch02TimeTests`, `TimeControllerStepTests`. All were testing classes that no longer exist — correct to delete them.

### Integration Test Quality

`FullCycle_Pause_Step_Resume_NoPllLoss` is a rigorous end-to-end test covering all four assertions from the spec: slave mode transitions, TotalTime convergence, zero TimePulse from slave, TotalTime snap on resume. **Quality is high.**

---

## Debt Tracker Updates

- **DT-003 ✅ Resolved:** `MasterSyncController.SwitchToDeterministic(slaveNodeIds)` — documented at call site in OrchestratorSubsystem; slave set is empty at construction time (runtime join tracking is out of scope for this workstream). Acceptable for initial wiring.
- **DT-004 ✅ Partially resolved:** `UpdateStepping` ACK filtering — the stale-tick bug was actually the `_lastUpdateRawTicks` issue. The FrameID filter for stale ACKs (DT-004) is a separate future improvement; not needed to ship.
- **DT-006 ✅ Closed:** `SequenceID` in `FrameOrderDescriptor` — confirmed not used by new controllers. Field exists for backwards compat with recordings.

---

## Suggested Git Commit Message

```
dev(time-ctrl-unif): BATCH-05 complete - full wiring, deletion, E2E test

All 6 phases of time-ctrl-unif workstream complete:
- Orchestrator: MasterSyncController + MasterLockstepTranslator + TimePulseEgressTranslator
- SimHost/CGF/IG: SlaveSyncController + SlaveLockstepTranslator; no SlaveTimeModeListener
- Deleted: 8 obsolete controller classes + FrameLockstepDescriptorTranslator
- E2E test: FullCycle_Pause_Step_Resume_NoPllLoss confirms PLL warm-start and sim-time snap
- Bugfix: SlaveSyncController stale tick baseline on Stepping->Continuous transition
- IOS-IG-SimHost.sln: 0 build errors
- 70 FDP time tests pass; 7/7 time integration tests pass
```

---

## Summary: Time Controller Unification — COMPLETE

All tasks from TASK-TRACKER.md are done:

| Phase | Tasks | Status |
|-------|-------|--------|
| Phase 1 — Message Layer | TCU-M001, TCU-M002 | ✅ |
| Phase 2 — Master Controller | TCU-MC001 | ✅ |
| Phase 3 — Slave Controller | TCU-SC001 | ✅ |
| Phase 4 — Translators | TCU-TR001, TCU-TR002, TCU-TR003 | ✅ |
| Phase 5 — Wiring | TCU-W001, W002, W003, W004, W005, W006 | ✅ |
| Phase 6 — Tests | TCU-T001, T002, T003, T004, T005, T006 | ✅ |
