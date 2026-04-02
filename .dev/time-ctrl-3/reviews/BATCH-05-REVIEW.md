# BATCH-05 Review

**Date:** 2025-08-04  
**Reviewer:** Dev Lead  
**Verdict:** ✅ APPROVED — no corrective required

---

## Summary

BATCH-05 delivered two correctness fixes (Corrective-02a, Corrective-02b) that restored the
6 pre-existing `TimeControlIntegrationTests` which were broken by TC3-P3-T05 and the
side-effect removal of the old barrier-immediate-crossing behavior.

| Test Suite | Result |
|------------|--------|
| FDP.Toolkit.Time.Tests | 136/136 ✅ |
| TimeControlIntegrationTests | 6/6 ✅ |

---

## Tasks Reviewed

| Task | Status |
|------|--------|
| TC3-P6-T01 build check | ✅ 0 errors |
| TC3-P6-T01 integration tests | ✅ 6/6 pass |
| Corrective-02a guard removal | ✅ correct |
| Corrective-02b barrier-pending freeze | ✅ correct |

---

## Code Analysis

### Corrective-02a: Guard removal from `DrainModeSwitchEvents`

The `_isTimeSynced` guard was too broad: it consumed events from the bus and silently discarded
them. For same-machine slaves (integration tests, no NTP translator), this permanently blocked
pause/resume. The correct behavior for multi-machine safety is already provided by the barrier
check in `UpdateBarrierPending`. Verdict: **correct fix**.

### Corrective-02b: Freeze time in BarrierPending

The 200ms lookahead is a network-delivery buffer, not a sim-time-advancing window. Freezing
sim-time immediately on entering BarrierPending:
- Restores expected integration test behavior (delta ≈ 0 while paused)
- Is semantically consistent with Stepping mode (both freeze sim-time)
- Does not break multi-machine correctness: slaves wait for NTP offset then cross barrier,
  which happens via `SyncedWallTicks` check (physical clock), independent of sim-time.

Verdict: **correct fix**.

### Test updates

Three tests were updated to document the new correct behavior. All assertions are now
aligned with the actual production semantics. No test was removed.

---

## Issues Found

None of severity P1 or P2. TC3-P3-T05's guard removal design was overly aggressive:
it was added to protect against multi-machine pre-NTP spurious transitions, but it
broke same-machine operation. Both fixes are minimal and correct.

---

## Verdict

APPROVED. All 136 FDP tests and all 6 integration tests pass. TC3 is now feature-complete.
