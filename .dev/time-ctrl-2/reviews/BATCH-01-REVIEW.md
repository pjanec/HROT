# BATCH-01 Review

**Date:** 2026-04-02  
**Reviewer:** Dev Lead  
**Status:** ✅ APPROVED

---

## Summary

All four mandatory tasks completed successfully. TC2-P2-T3 (stretch) deferred — acceptable decision.

| Task ID   | Review Result | Notes |
|-----------|---------------|-------|
| TC2-P1-T1 | ✅ All SC met | Fix is correct; two regressed tests were properly repaired; 3 new tests cover all SCs |
| TC2-P1-T2 | ✅ All SC met | DT-003 comment cleanly removed; call site unchanged |
| TC2-P2-T1 | ✅ All SC met | `_networkSimTime` backing field, computed property, FakeTimeController stub — all per spec |
| TC2-P2-T2 | ✅ All SC met | Initialisation order correct; `_masterSync` injected into `ClusterUiCache` |
| TC2-P2-T3 | ⚠️ Deferred | Stretch goal; recorded as P3 tech debt |

---

## Design Compliance

- `SwitchToDeterministic` fix matches §3.2 of DESIGN.md exactly.
- `ClusterUiCache` injection pattern matches §4.2 of DESIGN.md exactly.
- `OrchestratorSubsystem` wiring matches §4.3 of DESIGN.md.
- No scope creep or over-engineering observed.

## Test Quality

**MasterSyncControllerTests** — tests check actual `FrameNumber` values before and after ACKs, not just "no exception". The blocking behaviour and the state-replacement on second call are both rigorously verified.

**ClusterUiCacheTests** — tests verify `MasterSimTime` against expected double values, not string comparison or compilation only. The fallback path (null controller → network value) and the priority path (controller present → network pulse ignored) are both covered.

## Code Quality

- No silent error swallowing observed.
- No commented-out dead code introduced.
- `FakeTimeController` is `private sealed` inside the test class — correct scoping.
- `_networkSimTime` properly initialises to `0.0` (default(double)) — correct for the no-network-yet case.

## Issues Found

None blocking. One P3 debt item recorded (TC2-P2-T3 stretch).

---

## Tech Debt to Record

| ID | Priority | Description |
|----|----------|-------------|
| TD-001 | P3 | TC2-P2-T3: Wire slave time controllers into SimHost/IG `ClusterUiCache`. Requires verifying `ModuleHostKernel.GetTimeController()` availability at `_uiCache` construction point in each subsystem. |

---

## Suggested Git Commit Message

*(Used as-is for the actual commits)*

**FDP submodule:**
```
fix(time): MasterSyncController.SwitchToDeterministic uses runtime slave roster
```

**Root:**
```
feat(time-ctrl-2): BATCH-01 - lockstep fix + smooth SimTime UI
```
