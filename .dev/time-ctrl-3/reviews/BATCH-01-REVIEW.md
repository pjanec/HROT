# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Dev Lead  
**Date:** 2026-04-02  
**Decision:** ✅ **Approved** — merge as-is

---

## Overall Assessment

All 6 tasks were completed correctly.  The implementation is clean, the test coverage is
comprehensive (13 new tests; 90/90 pass), and the developer provided detailed insight into
edge cases.  No corrective action is required.

---

## Task-by-Task Verdict

| Task ID | Verdict | Notes |
|---------|---------|-------|
| TC3-P1-T01 | ✅ Approved | Structs added correctly.  EventIds 108/109 are free; MessagePack attributes verified. All 4 SC tests pass. |
| TC3-P1-T02 | ✅ Approved | 3 properties added with correct defaults.  Dedicated `TimeConfigTests.cs` file is clean. |
| TC3-P2-T01 | ✅ Approved | `_totalWallTicks = now` in constructor.  Debug log format correct. SC1–SC3 all verified. |
| TC3-P2-T02 | ✅ Approved | `TargetSimTime = _totalTime` placed after increment; SC3 cross-controller snap test is the most valuable proof. |
| TC3-P2-T03 | ✅ Approved | NLog `MemoryTarget` approach is accepted for now; see debt note D-TC3-01 for the follow-on concern. |
| TC3-P2-T04 | ✅ Approved (critical) | Barrier uses `_getTick()` in both `SwitchToDeterministic` and `UpdateBarrierPending`.  SC2 (barrier after 10 synthetic steps) is the key proof. |

---

## Test-Quality Assessment

**Coverage:** 13 new tests added across 3 files; all 90 tests green.  Coverage of the two
critical fixes (TC3-P2-T01 SC2 and TC3-P2-T04 SC2) is particularly strong — they would have
caught the original bugs immediately.

**Methodology:** Developer correctly used the injected `Func<long> tickSource` pattern for
deterministic physical-clock tests rather than `Thread.Sleep`.  The NLog `MemoryTarget`
approach for log-capture tests is acceptable; the fragility concern is logged as P3 debt.

**Note on `>=` vs `==` in barrier assertion (TC3-P2-T01-SC2):** Using `>=` rather than `==`
is the correct defensive choice given the test's reliance on real `Stopwatch` ticks (not a
frozen counter in that specific test).  This is **not** a relaxed assertion — it correctly
reflects the spec ("barrier must be at least `now + lookahead`").

---

## Developer Insights — Accepted and Acted On

1. **`SlaveSyncController.UpdateBarrierPending` still uses `_virtualWallTicks`** — known,
   targeted in TC3-P3-T03 (BATCH-02).  Confirmed correct.

2. **`ProcessTimePulses` and `DrainModeSwitchEvents` lack `_isTimeSynced` guards** — targeted
   in TC3-P3-T05 (BATCH-02).  Confirmed correct.

3. **`MasterSyncController.SeedState` does not reset `_lastPulseTicks`** — logged as P3 debt
   (D-TC3-02).  Low priority; will not cause data corruption, only an early spurious pulse.

4. **NLog `MemoryTarget` global-singleton fragility** — logged as P3 debt (D-TC3-01).

5. **Debug log prefix fragility (`"[TC3][Master] STEP"`)** — logged as P3 debt (D-TC3-01,
   same item).

---

## Debt Items Raised

| ID | Priority | Description | Target |
|----|----------|-------------|--------|
| D-TC3-01 | P3 | `MasterSyncController_Step_EmitsDebugLog` uses a global NLog `MemoryTarget` (not thread-safe under parallel test execution) and asserts a raw string literal `"[TC3][Master] STEP"`.  Improve by (a) wiring a test-scoped `FdpTestLogSink` instead of the global config, and (b) referencing a `public const string DebugStepPrefix` so format changes don't silently break tests. | Opportunistic |
| D-TC3-02 | P3 | `MasterSyncController.SeedState` restores `_totalWallTicks` from the saved state but does NOT reset `_lastPulseTicks`.  After a `SeedState` with a low `TotalWallTicks` value, the first `MaybePublishTimePulse()` call may fire immediately, flooding the bus with timestamp-zero pulses on the first frame.  Low-impact (pulse cadence self-corrects within one pulse interval) but worth addressing. | Opportunistic |

---

## Decision

✅ **Approved.**  FDP submodule committed at `fd9bd2c`.  Parent repo committed at `3455b8d`.

Proceed to **BATCH-02** (Phase 3 — SlaveSyncController NTP Handshake, TC3-P3-T01 through
TC3-P3-T06).
