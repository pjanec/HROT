# BATCH-13 Review

**Verdict: APPROVED**

---

## Task Coverage Assessment

### TASK-EQS-032 — `FlagsMeaningful` in `EqsResult`

| SC | Test | Coverage |
|----|------|----------|
| SC1 struct size | T-FM1: `Marshal.SizeOf<EqsResult>() == 24` | Full |
| SC2 bypass path keeps FM=0 | T-FM2: below-threshold → all candidates have `FlagsMeaningful & 1 == 0` | Full |
| SC3 test path sets FM | T-FM3: above-threshold, covered → all surviving candidates have `FlagsMeaningful & 1 != 0` | Full |
| SC4 DDS round-trip | T-FM4: distributed harness, field survives Brain → Muscle → Brain cycle | Full |

**Minor semantic gap (P3):** `CheapLineOfSightTest` sets `FlagsMeaningful |= 1` only on the
"covered" (pass) path but NOT on the "exposed" (reject, EntityId = -1L) path. Both
`AccurateLineOfSightTest` and `NavmeshReachableTest` correctly set `FlagsMeaningful` on both
outcomes. Since rejected candidates are compacted out of the buffer by `ReduceTopK`, no
consumer ever reads a rejected candidate's `FlagsMeaningful`, making this a correctness gap
only in the intermediate span — not a bug for any current consumer. Logged as P3 in
DEBT-TRACKER.

---

### TASK-EQS-033 — `LastUpdateTimeSeconds` in `EqsCognitiveBuffer`

| SC | Test | Coverage |
|----|------|----------|
| SC1 stamps time on update | T-LUT1: Path B (unmanaged event), time=5.0f → `buffer.LastUpdateTimeSeconds == 5.0f` | Full |
| SC2 stamps on empty update | T-LUT2: Path A (managed, empty results), time=5.5f → stamps and IsReady | Full |
| SC3 layout regression | T-LUT3: `GetSpanRW()` write/read round-trip still works | Full |

Tests directly create `EqsResultUpdateSystem` and invoke it with a controlled
`EntityRepository` — this is correct (not using a harness shortcut) and ensures the
implementation path is covered end-to-end without event-bus races.

---

### TASK-EQS-034 — `ScoreDeltaThreshold` and `EqsPublishPolicy.ScoreDelta`

| SC | Test | Coverage |
|----|------|----------|
| SC1 ScoreDelta suppress/publish | T-SD1: three phases, correct tick advancement pattern | Full |
| SC2 epoch increment | T-SD2: direct struct test, changing only ScoreDeltaThreshold increments Epoch exactly once | Full |
| SC3 DDS round-trip | T-SD3: distributed harness, field survives translators | Full |

T-SD1 correctly uses `PumpFrames(40)` (200ms ≥ 2 solver cycles at 10Hz) to ensure the
solver evaluated at least once with the small-delta scores before asserting suppression.
The `PumpUntil` in Phase 3 is correctly bounded by `timeoutMs: 3000`.

T-SD2 verifies epoch is NOT incremented on tick 2 (same params) and IS incremented on
tick 3 (changed param). This precisely matches the spec requirement "exactly once".

---

## Code Quality

- `WriteResultsToPoolAndPublish` owns the `SensorEvalState` persist call on both the
  suppress and publish branches — no double-write. A comment documents this ownership.
- `TopKScoreCache` as an `[InlineArray(16)]` struct on `SensorEvalState` correctly avoids
  heap allocation; the cache comparison uses `MemoryMarshal.CreateReadOnlySpan` via
  `Unsafe.As` consistent with the existing buffer-access patterns.
- `EqsPublishPolicy` enum is properly typed `byte` and the new `ScoreDelta = 3` ordinal
  preserves the gap at position 2 (`_Reserved2`).
- `EqsParams.ScoreDeltaThreshold` is set in both the initial `AddComponent` call and the
  update comparison block in `Action_MaintainEqsSensor` — the initialization gap caught
  by T-SD2 is now fixed.

---

## Debt Tracker Updates

| # | Priority | Source | Description |
|---|---|---|---|
| D-02 | P3 | BATCH-13 review | `CheapLineOfSightTest` does not set `FlagsMeaningful` on the rejected (exposed) path. `AccurateLineOfSightTest` and `NavmeshReachableTest` do. Inconsistency; no consumer impact since rejected candidates never reach the buffer. Fix when TASK-EQS-036 rewrites the LOS tests for context slots. |
| D-03 | P3 | BATCH-13 report | `Action_MaintainEqsSensor` initial-create and update paths are separate code blocks that must be kept in sync manually. A helper that builds `EqsSensor` from `EqsParams` would eliminate duplication risk. Fix in a future cleanup batch. |

---

## Build and Test Results

```
dotnet test --filter "FullyQualifiedName~Eqs"
  Total: 43   Passed: 43   Failed: 0   Skipped: 0
```

**APPROVED for commit.**

---

## Suggested Git Commit Message

```
feat(eqs): Phase 10 schema additions — FlagsMeaningful, LastUpdateTimeSeconds, ScoreDeltaThreshold

EQS-032: Replace EqsResult._pad with FlagsMeaningful (short). Set in CheapLineOfSightTest
(covered path), AccurateLineOfSightTest (resolved path), NavmeshReachableTest (both paths).
Thread through EqsResultEntry DDS wire struct and both egress/ingress translators.

EQS-033: Add LastUpdateTimeSeconds (float) to EqsCognitiveBuffer. Stamped by
EqsResultUpdateSystem on every write including empty-result updates. IsReady semantics
unchanged (LastUpdateTick > 0). No DDS wire changes.

EQS-034: Add ScoreDeltaThreshold (float) to EqsSensor and EqsSensorConfigTopic.
Add EqsPublishPolicy enum (AlwaysPush, TopChanged, _Reserved2, ScoreDelta).
Add TopKScoreCache [InlineArray(16)] to SensorEvalState. Wire ScoreDelta suppress/publish
logic in EqsSolverSystem.WriteResultsToPoolAndPublish. Track in Action_MaintainEqsSensor.

Tests: 10 new tests (T-FM1-4, T-LUT1-3, T-SD1-3). All 43 EQS tests pass.
```
