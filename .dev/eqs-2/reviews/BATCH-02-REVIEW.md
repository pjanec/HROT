# BATCH-02 REVIEW

**Batch:** BATCH-02  
**Reviewer:** Dev Lead  
**Decision:** APPROVED  

---

## Build & Test Verification

| Check | Result |
|---|---|
| `dotnet build IOS-IG-SimHost.sln` | Build succeeded, 0 errors |
| All 7 new EQS integration tests | 7/7 PASSED |
| Pre-existing EQS pool tests (EqsResultPoolTests + EqsComponentLayoutTests) | 7/7 PASSED |
| Pre-existing failures | 32 failures in Hrot.SimHost.Tests (unrelated to EQS, verified pre-existing) |

---

## Task-by-Task Review

### Corrective-0 — `EqsResultEvent.Epoch` / `RefreshTick` int -> uint

**Verdict:** CORRECT.  
`EqsSensor.Epoch` is `uint`; the event fields now match. No ABI impact since this is an
unmanaged event struct used only within the same process path.

---

### TASK-EQS-004 — `EqsResultUpdateSystem` + Managed Event

**`EqsResultUpdateEvent.cs`** — minimal managed event. Observer + Epoch + RefreshTick + List<EqsResultEntry>. Correct.

**`EqsResultUpdateSystem.cs`** — reviewed in detail:

- **Epoch check (critical):** `if (evt.Epoch != sensor.Epoch) continue;` — CORRECT. Uses version
  counter vs version counter, not tick vs tick.
- **GetSpanRW usage:** writes go through `buffer.GetSpanRW()` in both Path A and Path B. Avoids
  the C# 12 `[InlineArray]` ldobj defensive-copy trap. CORRECT.
- **Path B offline lookup:** O(n*m) linear scan over sensor query to match `SensorNetworkId`.
  Acceptable for Phase 1 (small n). Comment acknowledges this.
- **`LastUpdateTick` guard (P3):** `buffer.LastUpdateTick = evt.RefreshTick != 0 ? evt.RefreshTick : 1u;`
  Added as defensive fallback for tick-0 edge case. See D-02 below.
- **Registration:** `EqsSensor`, `EqsCognitiveBuffer`, `EqsResultUpdateEvent` registered in
  `CognitiveComponentRegistry` (Brain-tier). Correct placement.

---

### TASK-EQS-005 — `EqsSolverSystem` Phase 1 Stub + `EqsModule` Wiring

**`EqsSolverSystem.cs`** — emits one `EqsResultEvent` per `EqsSensor + NetworkIdentity` entity
with `EntryCount = 0`. Clean stub.

- **`RefreshTick = (uint)(view.Tick + 1)` (P3):** at simulation tick 0, emitting `RefreshTick = 0`
  would make `IsReady = false` on the Brain. The `+1` avoids this but means the tick number
  carried in the event is off-by-one. Combined with the `!= 0 ? ... : 1u` guard in
  EqsResultUpdateSystem, this is double-defended. Both guards are functionally correct and harmless,
  but the duplication is a design smell. Tracked as D-02 (P3).

**`EqsModule.cs`** — drives `EqsSolverSystem` at `SlowBackground(10)`. No longer references
`AreaQuerySolverSystem`. Comment confirms `AreaQuerySolverSystem` still runs in
`CognitiveSpatialModule` unchanged. CORRECT.

**`NavigationSolverComponentRegistry.cs`** — initializes `EqsResultPool` singleton with
`new NativeArray<EqsResult>(EqsResultPool.PoolCapacity, ...)`. Correct allocation site.

**`EditorHarness.cs`** — `Kernel.RegisterModule(new EqsModule())` added before
`Kernel.Initialize()`. Correct placement following existing module registration pattern.

---

### TASK-EQS-006 — `EqsLifecycleNodes`

**`EqsParams`** — sequential struct, all four fields present. Correct.

**`Action_MaintainEqsSensor`**:
- First tick: adds `EqsSensor` with `Epoch = 1`. CORRECT (epoch starts at 1, not 0, to avoid
  false "not ready" states).
- Subsequent ticks: param-change detection compares all four fields. If any differ, updates all
  fields AND increments `Epoch`. CORRECT.
- Always returns `Running`. CORRECT.

**`Deactivate_MaintainEqsSensor`**:
- Decorated with `[BTreeDeactivator("...EqsLifecycleNodes.Action_MaintainEqsSensor@0")]`.
  The `@0` suffix is required for 3-param bridge actions. CORRECT.
- Removes BOTH `EqsSensor` AND `EqsCognitiveBuffer`. CORRECT (prevents stale buffer accumulation).

**`Action_WaitForSensor`**:
- No buffer: Running. Buffer with `LastUpdateTick = 0` (IsReady = false): Running.
  Buffer with `LastUpdateTick > 0` (IsReady = true): Success. CORRECT.

---

## Test Quality Review

### T1 — StaleEpoch_IgnoresEvent
Verifies that an event with `Epoch = 1` against a sensor with `Epoch = 2` does NOT create a
`EqsCognitiveBuffer`. Tests the most important safety property of the system. SOLID.

### T2 — MatchingEpoch_PopulatesBuffer
Verifies count, `IsReady`, `LastUpdateTick`, and actual field values on slot 0 (`EntityId`,
`PositionX`, `PositionY`, `Score`). Data-level assertions, not just presence checks. SOLID.

### T3 — GetSpanRW_WritesPersist
Single-entry test verifying all five `EqsResult` fields (`EntityId`, `PositionX`, `PositionY`,
`Score`, `Flags`) persist after system returns. Directly targets the `[InlineArray]`
defensive-copy trap. SOLID.

### T4 — Phase1Stub_PopulatesBufferAfterSolverFires
Full round-trip via `EditorHarness`. `PumpUntil` with 2 s timeout. Asserts `IsReady == true`
and `Count == 0`. Tests the wiring of the entire offline pipeline (EqsModule -> EqsSolverSystem
-> EqsResultUpdateSystem -> EqsCognitiveBuffer). SOLID.

### T5 — WaitForSensor_ReturnsSuccessWhenReady
Three-step probe: no buffer, buffer with `LastUpdateTick=0`, buffer with `LastUpdateTick=1`.
Covers the exact transition boundary. SOLID.

### T6 — Deactivator_RemovesComponentsOnAbort
Adds sensor via `Action_MaintainEqsSensor`, manually adds buffer (as solver would), fires
deactivator, asserts BOTH components absent. SOLID.

### T7 — EpochIncrementsOnlyOnParamChange
Four-tick sequence: add (epoch=1), no-change (epoch=1), change `SearchRadius` (epoch=2),
no-change (epoch=2). Covers all transitions. SOLID.

---

## Debt Tracker Updates

| ID | Priority | Description |
|---|---|---|
| D-01 (existing) | P3 | `GetSpanRW_NoDefensiveCopy` test relies on struct copy semantics, not [InlineArray] readonly-receiver trap directly |
| D-02 (new) | P3 | Dual tick-0 defense: solver emits `RefreshTick = view.Tick + 1` AND updater guards `!= 0 ? ... : 1u`. Functionally correct; Phase 2 time-sliced solver should emit real ticks and drop the `+1` offset |

---

## Decision

**APPROVED — commit as-is.**

Suggested commit message from report:
```
feat(eqs): BATCH-02 - EqsResultUpdateSystem + EqsSolverSystem stub + lifecycle nodes

- Corrective-0: fix EqsResultEvent.Epoch/RefreshTick int -> uint
- TASK-EQS-004: EqsResultUpdateEvent + EqsResultUpdateSystem (Path A managed + Path B offline)
- TASK-EQS-005: EqsSolverSystem Phase 1 stub + EqsModule wiring + EqsResultPool init
- TASK-EQS-006: EqsLifecycleNodes (EqsParams, Action_MaintainEqsSensor + deactivator, Action_WaitForSensor)
- 7 new integration tests, all passing
```
