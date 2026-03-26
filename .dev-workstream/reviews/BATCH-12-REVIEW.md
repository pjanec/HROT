# BATCH-12 Review

**Batch:** BATCH-12  
**Reviewer:** Development Lead  
**Date:** 2026-03-27  
**Status:** APPROVED with **documented DEM1-D009 spec gaps** (D009 remains incomplete vs task detail)

---

## Summary

Validated **`.dev-workstream/reports/BATCH-12-REPORT.md`** against the tree. **All four tasks are reflected in source.** **`Fdp.Examples.Scenarios.Tests`** **58/58** and **`FDP.Toolkit.Perception.Tests`** **34/34** passed locally.

---

## Task-by-task verification

### Task 1 — `ModuleHostKernel.Update(float)` removal

**Found:** **`DistributedTankScenario.EvaluateTick`** calls **`_muscleTimeController!.Step(FixedDelta)`** then **`_muscleKernel!.Update()`**. **`_muscleTimeController`** is the same instance passed to **`SetTimeController`**. No **`Update(float)`** remains in this scenario (grep clean).

**Important:** The report is correct that **`Step()`** before **`Update()`** is required so **`GlobalTime.DeltaTime`** is non-zero for **`CarKinematicsSystem`**. This is a real fix, not cosmetic.

### Task 2 — DEM1-D009 Phase B continuation

**Found in code:** Muscle-side **`TkbDatabase`** with a hand-authored **CommandTank** template; **`TkbIdentity`** on ghost; **`GhostPromotionSystem`** path via **`NavState`** presence; **`MuscleDirectSystemsModule`** runs **`SpatialHashSystem`** + **`CarKinematicsSystem`**; **`NavState`** injected on the **Muscle ghost** at tick **20**; velocity asserted at **25**; **Brain turret** **`WeaponChannel`** at tick **30**; turret–hull distance at **40**; **`SplitAuthorityActive`** at tick **50** with success **`return true`**.

**Design alignment (gaps vs `DEM1-TASK-DETAIL.md`):**

| Spec | Current scenario |
|------|------------------|
| Brain: **BehaviorToolkit** (cognitive) | **Not** registered — harness is ELM + manual entities only. |
| Brain: **ReplicationLogicModule** | Only **Muscle** runs **`ReplicationLogicModule`**. |
| **`DemoTkbSetup.RegisterAll`** | Replaced by **inline `TkbTemplate`** registration (acceptable engineering substitute; not the shared helper). |
| Tick **20:** **`LocomotionChannel.ActiveAction = MoveTo`** on **Brain hull** | **NavState** on **Muscle ghost** (no Brain-side locomotion channel inject; no **DDS** hop). |

Observable milestones (**ghost, motion, split checks**) match the **intent** of phases B2–B4 at the **tick numbers** in the doc, but the **topology** is still a **thin harness**, not the full two-toolkit + dual-replication description. **DEM1-D009 must stay unchecked** until agreed spec items are met or the task detail is formally trimmed.

### Task 3 — `ParallelStoriesScenario` + **`RecordingModule.Blocking`**

**Found:** **`RunLivePhase`** uses **`using var recordingModule = new RecordingModule(...)`** with **`Blocking = true`**, registers **`LiveKinematicsModule`** + **`recordingModule`**, **`Step` + `Update`** loop; LIFO **`using`** order matches report (**kernel** disposed before **recording** flush). Class XML updated. **FlightRecorder** direct **`AsyncRecorder`** removed from the live loop.

### Task 4 — `LocalGridBuilderSystem` XML

**Found:** Class **`summary`** includes **`_liveByIndex`** stale-slot eviction bullet (BATCH-11 behaviour documented).

---

## Test quality

- **Locomotion / split-authority:** Assertions bind to **`LocoObservable`** and **`SplitAuthorityActive`** — stronger than exit code alone.
- **DistributedTank tests** still use **`maxTicks: 60`** while scenarios can succeed at **50** — fine (early exit on success).
- **Phase 4 turret-vs-hull** at tick **40:** both entities start at origin with no Brain kinematics — the check mostly guards **accidental** divergence; low signal but harmless.

---

## Suggested commit message

```
BATCH-12: Muscle stepping via SteppingTimeController; DistributedTank loco + split-authority; ParallelStories RecordingModule

- DistributedTankScenario: Step+Update for Muscle; TKB template + ghost promotion; CarKinematics on Muscle; NavState inject; turret weapon + tick-50 split-authority
- ParallelStoriesScenario: RecordingModule with Blocking=true; LIFO using disposal order with live kernel
- LocalGridBuilderSystem: document _liveByIndex stale-slot eviction in class summary
- Scenarios.Tests: DistributedTank phase 2/4 milestone assertions
```

---

## Follow-ups (BATCH-13)

1. **DEM1-D009 fidelity:** Brain **`LocomotionChannel`** (or spec-aligned command path), **`DemoLocomotionMsg`** (or translators), **`DemoTkbSetup.RegisterAll`**, **BehaviorToolkit** / **ReplicationLogicModule** on Brain per **§D009** — or update **task detail** if the harness scope is intentionally smaller.  
2. **`SteppingTimeController`:** Document or fix **`SeedState`** / first-**`Update`** **`DeltaTime`** behaviour so other call sites do not rediscover the footgun.  
3. **`ModuleHostKernel`:** Consider **disposing `IDisposable` modules** (report insight) — track as product debt if scenarios must juggle **`using`**.
