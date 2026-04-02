# BATCH-02 Review

**Batch:** BATCH-02  
**Reviewer:** Dev Lead  
**Date:** 2026-04-02  
**Decision:** ✅ **Approved with one P2 corrective task** (Corrective-01, added to BATCH-03)

---

## Overall Assessment

All 6 tasks implemented correctly.  109/109 tests pass (+19 new tests, +15 existing tests updated
with sync preambles).  The NTP offset calculation, pre-sync guards, SyncedWallTicks barrier, and
stray-drain are all correct.  One P2 correctness issue was spotted in the hard-snap path and is
carried forward as a pre-task correction in BATCH-03.

---

## Task-by-Task Verdict

| Task ID | Verdict | Notes |
|---------|---------|-------|
| TC3-P3-T01 | ✅ Approved | Fields + property correct.  Both bus registrations present.  Initial `SendTimeSyncRequest()` call verified. |
| TC3-P3-T02 | ✅ Approved | NTP formula correct (RTT spike guard, hard-snap on first sync, gentle-steer verified by SC3/SC4). |
| TC3-P3-T03 | ✅ Approved | Barrier uses `SyncedWallTicks` at line 304; debug log on hit is present.  SC1 cross-machine test passes. |
| TC3-P3-T04 | ✅ Approved | `currentAbsTicks = SyncedWallTicks` at line 393. SC1 (`TotalTime < 1.0` after zero-latency pulse) is a strong proof. |
| TC3-P3-T05 | ✅ Approved | Guards correct.  Developer correctly updated 15 tests in 4 files with `InjectSyncResponse` preamble. |
| TC3-P3-T06 | ✅ Approved | Drain calls present at top of `UpdateContinuous` and `UpdateBarrierPending`.  Developer's concern about "vacuous" assertions is rejected — managed-bus events are single-buffered and are genuinely drained by the drain calls. |

---

## P2 Bug Found: Hard-Snap Sets Wrong _lastUpdateRawTicks

**Location:** `OnTimePulseReceived`, hard-snap branch:
```csharp
_lastUpdateRawTicks = currentAbsTicks;  // currentAbsTicks = SyncedWallTicks = getTick() + offset
```

**Problem:** `UpdateContinuous` computes `rawDelta = _getTick() - _lastUpdateRawTicks`.  If
`_lastUpdateRawTicks` was set to `SyncedWallTicks` (master domain), then the next frame's
`rawDelta = (_getTick_new - _getTick_old) - offset`.  With a non-zero offset this produces a
massively wrong delta (on the order of `offset` ticks), causing a burst of sim time advancement
(positive offset) or near-zero delta (negative offset) for one frame.

**Scenario:** Unlikely but real — a hard snap requires `|simTimeError| > 500 ms`, which can
occur on startup (before PLL converges) if a response happens to arrive very late.
The `_isTimeSynced` guard means offset is already established, making the corruption certain.

**Fix (1 line):** In `OnTimePulseReceived` hard-snap path, change:
```csharp
_lastUpdateRawTicks = currentAbsTicks;      // WRONG: SyncedWallTicks
```
to:
```csharp
_lastUpdateRawTicks = _getTick();           // CORRECT: raw local tick
```

**Added as Corrective-01 in BATCH-03 instructions.**

---

## Developer Insights — Responded To

1. **FdpLog max 4 args** — Dropping the 5th arg is accepted.  The critical fields (RTT, offset,
   HARD-SNAP/gentle-steer) are all present.

2. **`_lastUpdateRawTicks = SyncedWallTicks` hard-snap bug** — Escalated to P2, fixed in BATCH-03
   Corrective-01.

3. **hardSnap sentinel `== 0` always fires for same-machine tests** — Added as D-TC3-03 (P3
   debt).  Functionally correct; snapping to 0 repeatedly is idempotent.

4. **Drain test assertion may be vacuous** — Rejected.  Managed-bus events are single-buffered
   (no `SwapBuffers` required).  `PublishManaged` writes to the same list that `ConsumeManaged`
   drains.  The drain tests do genuinely verify drain behaviour.

5. **Periodic resync trigger in long tests** — Noted.  At the test deltas used (0.016s) and
   `SyncRefreshIntervalTicks = 1s`, tests with > 62 frames will trigger a re-sync.  This is
   acceptable — the re-sync only publishes a `TimeSyncRequest` to the bus; uncollected requests
   do not affect test outcomes.  The `NoTimePulseEmitted` regression test (`200 frames`) already
   runs cleanly at 109.

---

## New Debt Items

| ID | Priority | Description | Target |
|----|----------|-------------|--------|
| D-TC3-03 | P3 | `hardSnap = _masterWallClockOffset == 0` fires on every update in same-machine tests (offset = 0 is both "uninitialized" and "legitimate same-machine value"). Use a separate `_firstSyncDone` sentinel to distinguish the two cases. | Opportunistic |
| **Corrective-01** | **P2** | **`OnTimePulseReceived` hard-snap sets `_lastUpdateRawTicks = SyncedWallTicks` (master-domain tick). Must be `_getTick()` (local raw tick). Corrupts `rawDelta` by −offset on the next frame.** | **BATCH-03 (pre-task correction)** |

---

## Decision

✅ **Approved.**  FDP submodule committed at `0cea5df`.

Proceed to **BATCH-03** (Phase 4 + Corrective-01).  BATCH-03 must fix Corrective-01 as its
first task before proceeding to Phase 4 (Translators) work.
