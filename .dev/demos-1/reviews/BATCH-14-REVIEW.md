# BATCH-14 Review

**Batch:** BATCH-14  
**Reviewer:** Development Lead  
**Date:** 2026-03-27  
**Status:** APPROVED — DEM1-D009 sign-off met; minor normative doc + XML hygiene corrected post-report.

---

## Summary

Cross-checked **`.dev-workstream/reports/BATCH-14-REPORT.md`** against **source** and **`dotnet test`** on **`Fdp.Examples.Scenarios.Tests`**: **60 / 60** passed. Tasks **1–5** match the batch brief: **`DEM1-DESIGN.md` §6.4** Brain/Muscle topology, **`LocoCommandReceivedViaDds`** + dedicated Phase 2 DDS test, **`PhaseBTurretTracksHull`** + Phase 3 test, **`DistributedTankScenario`** summary aligned with **`DemoLocomotionMsg`**, **`DEM1-TASK-DETAIL`** success list updated. Task **6** (optional P3) was **retargeted** in **`DEBT-TRACKER`** per report — acceptable.

---

## Task-by-task verification

### Task 1 — `DEM1-DESIGN.md` §6.4

**Found:** Topology bullets, “Why no BehaviorToolkit / RPL on Brain” blockquote, and phase table match **`DEM1-TASK-DETAIL`** and **`DistributedTankScenario`** (Brain publishes **`EntityMasterTopic`** + **`DemoLocomotionMsg`**; Muscle **`ReplicationLogicModule`** + **`DemoTkbSetup.RegisterAll`**, loco → **`NavState`**).

**Lead corrections:**

- Phase **1** row listed milestone tick **10** while **`PhaseBElmActiveTick` is 5**. Updated **§6.4** table and **`DEM1-TASK-DETAIL`** bullet to **tick 5** so DESIGN / TASK-DETAIL / code agree.  
- Phase **3** event column incorrectly implied “Muscle kinematic integration” for **Brain** turret position — reworded to match **Brain**-side layout (hull/turret co-located; ghost moves on Muscle).

### Tasks 2–3 — DDS consumption + Phase 3 test

**Found:** **`LocoCommandReceivedViaDds`** is set only in the Muscle **`DdsReader<DemoLocomotionMsg>`** loop together with **`NavState`** write (same site as **`_locoMsgConsumed`**). **`DistributedTank_Phase2_LocoMsgConsumedViaDds`** asserts it after full 60-tick success run — correct coupling to scenario completion.

**Phase 3:** **`PhaseBTurretTracksHull`** set after distance check at **`PhaseB4TurretTrackTick`** (40); test asserts the flag. Tests run **maxTicks: 60** (not 41 only): acceptable — still exercises tick 40 path.

### Task 4 — Scenario summary

**Found:** Class **`<summary>`** describes tick-20 **`DemoLocomotionMsg`** + Muscle poll before kernel update; stale “direct **`NavState`** on ghost at tick 20” narrative removed from Phase B paragraph. Remaining **Phase C — BATCH-13** paragraph is slightly redundant (per developer note); low priority consolidation.

### Task 5 — `DEM1-TASK-DETAIL` success conditions

**Found:** Fenced block includes **`DistributedTank_Phase2_LocoMsgConsumedViaDds`**, **`DistributedTank_Phase3_BrainTurretTracksHull_AtTick40`**, and test names aligned with **`ScenarioTests.cs`**.

### Task 6 — Optional P3

**Found:** **`TryTakeCreateAck`** row retargeted **TwoAck-BATCH-04 → TwoAck-BATCH-05** with rationale — consistent with BATCH-14 scope.

---

## Test quality

- **DDS path:** The new test materially addresses the BATCH-13 gap: velocity alone could theoretically be satisfied without reading DDS; **`LocoCommandReceivedViaDds`** is only set from the reader loop.
- **Caveat:** **`NavState`** is still written in scenario code immediately after a valid DDS sample — the test proves **a sample was taken**, not that no other code path could exist in a refactor. Acceptable for this harness.
- **XML docs:** Developer left a **duplicate nested `<summary>`** on **`DistributedTank_Phase2_LocoMsgConsumedViaDds`** (invalid doc comment) and a **stale** **`DistributedTank_Phase2_MuscleNodeMovesOnCommand`** summary (“NavState injected at tick 20”). **Lead fixed** both.

**Suggested commit message:**

```
docs(dem1): close D009 sign-off — DESIGN §6.4, DDS loco test, Phase 3 test

- Sync DEM1-DESIGN §6.4 with implemented DistributedTank Brain/Muscle topology
- Add LocoCommandReceivedViaDds + PhaseBTurretTracksHull observables; two tests
- Align DEM1-TASK-DETAIL D009 success list and ELM milestone tick (5) with code
- Retarget TryTakeCreateAck debt to TwoAck-BATCH-05; fix DistributedTank test XML
```

(Adjust if committing only the lead doc/test-doc follow-ups vs full developer delta.)

---

## Follow-ups

- **BATCH-15:** DEM1-D010 + debt burndown (see **`BATCH-15-INSTRUCTIONS.md`**).
- **P3 (optional):** Consolidate **`DistributedTankScenario`** BATCH-12/13 locomotion **`<summary>`** paragraphs when convenient.
