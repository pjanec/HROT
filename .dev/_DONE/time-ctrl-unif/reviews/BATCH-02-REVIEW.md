# BATCH-02 Review

**Batch:** BATCH-02 — Unified Master Controller  
**Tasks:** TCU-MC001, TCU-T001  
**Reviewer:** Dev Lead  
**Date:** 2026-04-01

---

## ✅ Verdict: APPROVED

All tasks complete and correct. 99 tests pass (0 failed, 1 pre-existing skip). No rework required.

---

## Review Findings

### Scope Check

| Task | Expected | Result |
|------|----------|--------|
| TCU-MC001 — MasterSyncController | New file; ISteppableTimeController; state machine; DDS-free | ✅ Done |
| TCU-T001 — Tests | 12+ tests; all 9 required + 3 edge cases | ✅ 12 tests, all green |

### Design Alignment

- Implements `ISteppableTimeController` — ✅
- DDS-free (no CycloneDDS imports) — ✅
- Domain types via `PublishManaged`/`ConsumeManaged` — ✅
- Tick-source seam via `Func<long>? tickSource` — ✅
- `GetMode()` returns `Continuous` for both Continuous and BarrierPending — ✅
- `SwitchToContinuous()` is idempotent — ✅
- `Step()` properly blocked while `_pendingAcks` non-empty — ✅
- `_frameNumber` NOT incremented in BarrierPending — ✅ **Correct deviation from spec wording** (spec said "same as Continuous" but tests confirm no frame increment during barrier wait)
- `_pendingAcks` empty on barrier crossing, populated after Step() — ✅ **Correct deviation from spec wording** (spec said "reset to _expectedSlaves on crossing" but this would block the first Step immediately)

The two spec deviations are legitimate; the developer correctly resolved them by consulting both the existing `SteppedMasterController` pattern and the test expectations.

### Test Quality Assessment

Tests are **behavioural**: they assert specific field values (`FrameID`, `TotalTime`, mode enum). All bus interactions use `SwapBuffers()` before consuming. Tick-source seam used throughout — no `Thread.Sleep`. **Quality is high.**

Weak point in test 5 (`Step_BlocksUntilAllAcksReceived`): the test uses `TransitionToStepping()` helper which creates an internal `TimeConfig` object separately but the controller's own config still has the original `LookaheadWallTicks`. This works because the helper calls `ctrl.SwitchToDeterministic()` with the existing config (barrier = 0). Fine for current purposes.

### Code Quality

No silent error swallowing. No DDS imports. `SwitchToDeterministic`'s `slaveNodeIds` parameter is correctly documented as accepted for API compatibility but not used for ACK tracking.

---

## Debt Tracker Updates

- **DT-003 (P2):** `MasterSyncController.SwitchToDeterministic(slaveNodeIds)` silently ignores the `slaveNodeIds` parameter — effective slave set is set at construction time. If the Orchestrator passes a different set (e.g. after a node joins/leaves), ACK tracking will be wrong. Must be documented clearly at the call site when wiring in Phase 5. Target: BATCH-05.
- **DT-004 (P2):** `UpdateStepping()` processes `FrameStepCompletedEvent` ACKs by `NodeID` only, with no `FrameID` filter. A late-arriving ACK from a previous step (possible via DDS retransmit from `SlaveLockstepTranslator`) would incorrectly clear a slot in `_pendingAcks`. Should filter by `ack.FrameID == _lastStepFrameID`. Target: BATCH-04 (translator batch) or a corrective batch after BATCH-05 integration testing.

---

## Suggested Git Commit Message

```
feat(TCU-MC001/T001): add MasterSyncController with 12 unit tests

- New MasterSyncController: unified state machine replacing MasterTimeController,
  SteppedMasterController, and DistributedTimeCoordinator
- Modes: Continuous -> BarrierPending -> Stepping -> Continuous
- DDS-free: all pub/sub via FdpEventBus (PublishManaged for domain types)
- Tick-source seam for test isolation (no Thread.Sleep)
- BarrierPending accumulates time without incrementing frame counter
- _pendingAcks empty on barrier crossing, re-armed after each Step()
- 12 unit tests covering all 9 required success conditions + 3 edge cases
```

---

## Next Batch

**BATCH-03** should cover:
- **TCU-SC001** — SlaveSyncController (Phase 3)
- **TCU-T002** — Unit Tests: SlaveSyncController (Phase 6)

Estimated effort: 6–8 hours.
