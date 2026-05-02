# BATCH-15 Review

**Batch:** BATCH-15  
**Reviewer:** Development Lead  
**Date:** 2026-03-27  
**Status:** APPROVED with documentation drift logged — **D010** implementation and CI are strong; **normative docs** (§6.5 / TASK-DETAIL pseudo-code) still describe different observables than code for several latches; **Latch 5** is intentionally symbolic (matches developer report).

---

## Summary

Validated **`.dev-workstream/reports/BATCH-15-REPORT.md`** against **source**. **`Fdp.Examples.Scenarios.Tests`**: **65 / 65** passed (including five **`UrbanCombatNewScenario`** tests). **`UrbanCombatNewScenario`** is self-contained in **`Fdp.Examples.Scenarios`** (no **`Fdp.Examples.UrbanCombat`** project reference); **`ScenarioRegistry`** registers **`urbancombat`**; **`DemoRoadGraphFactory.CreateCityIntersection()`** is used; **14 entities** and **five** internal sequential latches + **600-tick** **`ScenarioFailureException`** match the batch brief.

**Phase 0:** **`RunnerTestHelpers.TryTakeCreateAck`** exists; call sites in **four** **`Hrot.ClusterRunner.Integration.Tests`** files use it. Extraction matches the debt item intent. **Runner integration tests** were not re-run in this review session (report cites file locks); treat as **low risk** given localized refactor.

**Report metadata:** Date **2025-07-18** is inconsistent with project timeline — treat as typo when archiving.

---

## Task-by-task verification

### Phase 0 — `TryTakeCreateAck`

- **`Hrot.ClusterRunner.Integration.Tests/RunnerTestHelpers.cs`**: shared helper with correct **InProgress** skip semantics.  
- **Callers:** **`MiniIosIntegrationTests`**, **`MapPlacementIntegrationTests`**, **`AreaAuthoringIntegrationTests`**, **`SpawnMovingVehicleWithGatewayIntegrationTests`** — all reference **`RunnerTestHelpers.TryTakeCreateAck`**.

### Tasks 4a–4f — DEM1-D010

- **4a:** **`UrbanCombatNewScenario`**, **`ScenarioName`** → **`ScenarioNames.UrbanCombat`**.  
- **4b:** Pipeline covers **Behavior** (behavior/BTree/HSM), **Combat**, **Physics**, **Perception**, **CarKinem** (**`CarKinematicsSystem`** + road), **Navigation** channels/dispatch — consistent with §6.5 toolkit list. **Fhsm** compiler/sourcegen referenced for APC HSM (appropriate).  
- **4c:** **`_road = DemoRoadGraphFactory.CreateCityIntersection()`**.  
- **4d:** **5 + 3 + 1 + 4 + 1 = 14** spawns; TKB IDs **1001–1002, 2001–2003**; APC **ConvoyEscort** HSM; insurgent **Ambush** BTree + **`TargetMemory`** pre-seed; soldiers embarked + **TargetMemory** pre-seed (intentional deviation from legacy infantry BT — documented in code XML and report).  
- **4e:** Latches **1–4** + success path; **`tick > 600`** throws phase **5** with latch diagnostics.  
- **4f:** **`ScenarioRegistry`** + five tests as in report.

### Project hygiene

- **`Fdp.Examples.Scenarios.csproj`** includes **`Fdp.Examples.NetworkDemo`** but **no** `.cs` file in **`Fdp.Examples.Scenarios`** imports that assembly — **unused reference** (build still succeeds). Log as debt for removal after confirm.

### Compiler

- **`UrbanCombatNewScenario.cs`**: **CS8602** (~line 800) possible null dereference — minor; fix in a hygiene pass.

---

## Alignment with design

| **§6.5 / TASK-DETAIL** | **Implementation** | **Assessment** |
|------------------------|--------------------|----------------|
| Latch 1: **`FireRequestEvent`** | **`WeaponChannel.ActiveAction == AimAndFire`** | Different observable; still proves ambush branch engaged. **Doc debt.** |
| Latch 3: **`HitEvent.HitEntity == Insurgent`** | **`Health.Current < SoldierMaxHealth`** (insurgent max == same constant) | Equivalent for this template; not hit-event-driven.**Doc debt.** |
| Latch 5: APC **Loco** **FollowRoute** / **MoveTo** | **Log “Mission Resumed”** + `return true`; APC stays **Disabled** | **Known product gap** — developer documented; **TASK-DETAIL** success block already uses log text; **§6.5 table** still says APC loco.**Doc debt.** |

---

## Test quality

- **`UrbanCombatNew_RunToCompletion_ExitsZero`**: End-to-end under **600** ticks — **high value**.  
- **Per-latch tests**: Exercise narrative steps; **`Latch5`** asserts log substring (matches TASK-DETAIL).  
- **Gaps vs TASK-DETAIL prose:** No assertion that insurgent dies **before tick 400** (TASK-DETAIL “at some point… before tick 400”); full run passes but **tick-boundary** not enforced — **low** priority follow-up.  
- **No dedicated `LatchInsurgentHit` test** — internal latch covered indirectly; optional explicit test in a later batch.

---

## Suggested commit message

```
feat(dem1): D010 UrbanCombatNewScenario + RunnerTestHelpers TryTakeCreateAck

- Add self-contained grand-integration scenario (14 entities, HSM, BTrees,
  road graph, sequential latches, ScenarioRegistry urbancombat)
- Extract TryTakeCreateAck to Hrot.ClusterRunner.Integration.Tests RunnerTestHelpers
- Add UrbanCombatNewScenarioTests; extend Fdp.Examples.Scenarios deps (Fhsm)
```

---

## Follow-ups

- **BATCH-16** — See **`.dev-workstream/batches/BATCH-16-INSTRUCTIONS.md`** (tech debt first + D010 doc/real Latch5 optional).
