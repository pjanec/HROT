# BATCH-13 Review

**Batch:** BATCH-13  
**Reviewer:** Development Lead  
**Date:** 2026-03-27  
**Status:** APPROVED — **lead acknowledges** implement-and-document choice for Brain topology; **follow-up:** sync **`DEM1-DESIGN.md`**

---

## Summary

Validated **`.dev-workstream/reports/BATCH-13-REPORT.md`** against the tree. **Tasks 1–3 are implemented** as described (with Task 2–3 as **documentation-only**, as the report states). **Task 4** was deferred. **`Fdp.Examples.Scenarios.Tests`** **58/58** passed locally.

---

## Task-by-task verification

### Task 1 — DEM1-D009 alignment

**Found:**

- **`DemoTkbSetup.RegisterAll`** in **`Fdp.Examples.Common/Setup/DemoTkbSetup.cs`** registers **CommandTank (100)** with **`SimTransform`**, **`SimVelocity`**, **`VehicleState`**, **`VehicleParams`**, **`NavState`**, **`LocomotionChannel`** — matches Muscle ghost promotion needs.
- **`DistributedTankScenario`**: **`DemoTkbSetup.RegisterAll(muscleTkb)`**; **`DdsWriter`/`Reader<DemoLocomotionMsg>`**; tick **20** sets **Brain hull** **`LocomotionChannel`** and **`Write(DemoLocomotionMsg)`**; **poll at start of `EvaluateTick`** (before **`Step` + Muscle `Update`**) translates to **`NavState`** on the ghost; **`ReleaseResources`** disposes loco writer/reader before participants.
- **`docs/demos-1/DEM1-TASK-DETAIL.md` § D009** rewritten with **architecture note** (no Brain **`ReplicationLogicModule`**, no **`BehaviorToolkit`**) and success conditions referencing **`DemoLocomotionMsg`**.
- **`DEM1-TASK-TRACKER.md`** marks **D009** **[x]** with BATCH-13 footnote.

**Lead position:** Omission of Brain **`ReplicationLogicModule`** / **`BehaviorToolkit`** is **acceptable** for this demo **provided normative docs agree**. **`DEM1-TASK-DETAIL`** now matches code. **`DEM1-DESIGN.md` §6.4** still states *“Brain Node: `BehaviorToolkit` + `ReplicationLogicModule`”* — **that is now wrong** and must be updated (see debt / BATCH-14).

**Design fidelity:** Hierarchical **turret** and **split authority** behaviour remain **scenario-level** (manual Brain entities + tick 30 weapon inject), not full **ChildBlueprintDefinition** auto-spawn as in the old design prose — acceptable given **`DEM1-TASK-DETAIL`** scope after edit.

### Task 2 — `SteppingTimeController` first-frame `DeltaTime`

**Found:** Class **`summary`** documents **first-frame `DeltaTime = 0`** and **`Step` before `Update`**. **`SeedState`** comment explains **`_lastDeltaTime = 0`**. No behaviour change — consistent with report.

### Task 3 — `ModuleHostKernel` disposal contract

**Found:** **`RegisterModule`** and **`Dispose`** XML describe **no automatic disposal** of **`IDisposable` modules**, with **`UninstallModuleAsync`** called out as exception — matches code paths (kernel **`Dispose`** disposes providers; drain path can dispose modules).

### Task 4 — Optional P3

**Deferred** per report — acceptable.

---

## Test quality

- Count unchanged (**58**): existing **DistributedTank** tests still gate **ELM**, **ghost**, **`LocoObservable`**, **split authority**. **`LocoObservable`** now implicitly depends on **`DemoLocomotionMsg`** timing; failures would still surface as velocity assertions.
- **Gap (low):** No dedicated test that **`_locoMsgConsumed`** / DDS sample was used (vs accidental **`NavState`**); acceptable given green milestones.

---

## Suggested commit message

```
BATCH-13: DemoTkbSetup + DemoLocomotionMsg path; DEM1-D009 doc; time/kernel contracts

- DemoTkbSetup.RegisterAll for CommandTank template; DistributedTank uses DDS loco + Brain LocomotionChannel
- DEM1-TASK-DETAIL §D009 + DEM1-TASK-TRACKER: align with implemented Brain/Muscle topology
- SteppingTimeController: document first-frame DeltaTime / Step-then-Update
- ModuleHostKernel: document module ownership / disposal contract
```

---

## Follow-ups

**BATCH-14 (DEM1-D009 sign-off):** See **`.dev-workstream/batches/BATCH-14-INSTRUCTIONS.md`** — design §6.4 sync, **`DemoLocomotionMsg`** consumption assertion in CI, explicit Phase 3 (tick 40) test + TASK-DETAIL success list, scenario comment cleanup, optional P3.

**BATCH-15 (DEM1-D010):** See **`.dev-workstream/batches/BATCH-15-INSTRUCTIONS.md`** — UrbanCombat new / grand integration (not part of BATCH-14).
