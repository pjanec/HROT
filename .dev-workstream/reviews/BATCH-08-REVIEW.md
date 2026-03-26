# BATCH-08 Review

**Batch:** BATCH-08  
**Reviewer:** Development Lead  
**Date:** 2026-03-26  
**Status:** APPROVED (Tasks 1–3); Task 4 deferred as documented

---

## Summary

Verified against source (not only `.dev-workstream/reports/BATCH-08-REPORT.md`). **Tasks 1–3 match the batch instructions and design intent.** **`MissionTriggerHelper`** maps legacy `"ReachedDestination"` to **`DoctrineFinished`** without touching the obsolete enum; **`ParallelStoriesScenario`** topology is asserted via **`ModuleHostKernel.GetRegisteredModuleTypeNames()`**; **DEM1-TASK-DETAIL** § D008 and scenario XML describe **`LiveKinematicsModule`** + blocking **`AsyncRecorder`**. **Task 4 (DEM1-D009)** was correctly scoped out of this batch.

**Tests run locally:** `Fdp.Examples.Scenarios.Tests` **51/51** passed; `FDP.Toolkit.ImGui.Tests` **43/43** passed (report listed 42 — count drift, harmless); `Bagira.Map.Common.Tests` **94/94** passed.

---

## Task-by-task verification

### Task 1 — `ComponentReflector` native heap churn

**Expected:** Remove per-frame `Marshal.AllocHGlobal` on the byte-diff path; behaviour unchanged.

**Found:** `ArrayPool<byte>.Shared.Rent` + pinned `GCHandle` for `StructureToPtr`, with immediate `Return`. This meets the debt goal (no native heap alloc per frame from `AllocHGlobal`). Residual cost is pool rent + pin per component per inspector frame — acceptable for an inspector-only path; report’s note on optional `unsafe`/`stackalloc` is fair follow-up.

**Tests:** `UnmanagedComponent_ThreeFrameCycle_InPlaceCacheDetectsAllChanges` exercises the in-place cache path; aligns with the optimisation.

### Task 2a — Doctrine ID naming

**Expected:** Eliminate confusing duplicate `DemoDoctrineIds` type name in `Fdp.Examples.Scenarios`.

**Found:** Class renamed to **`BehaviorValidationDoctrineIds`** in `FDP/Examples/Fdp.Examples.Scenarios/DemoDoctrineIds.cs`. **`BehaviorValidationScenario`** references the new name. Values unchanged (`Combat = 2900`).

**Nit:** The **file** is still named `DemoDoctrineIds.cs`, which is mildly confusing for navigation — log as small hygiene for BATCH-09 (optional).

### Task 2b — `MissionTriggerHelper` CS0618

**Expected:** Map wire `"ReachedDestination"` to **`DoctrineFinished`** at ingress; tests updated.

**Found:** Switch arm and XML in `Bagira.Map.Common/Helpers/MissionTriggerHelper.cs`; `EntityMissionIngressTranslatorTests.ResolveTrigger_ReachedDestination_MapsToDoctrineFinished` asserts **`DoctrineFinished`**.

### Task 3a — ParallelStories kernel proof

**Expected:** Test must reflect **real** module registration on the main kernel after `Configure`, not an author flag.

**Found:** **`GetRegisteredModuleTypeNames()`** on `ModuleHostKernel` returns concrete type names; **`ParallelStoriesScenario`** sets **`ReplayKernelModuleTypeNames`** after **`RegisterModule(ReplayModule)`**; test asserts absence of `LiveKinematicsModule`, `GroundKinematicsModule`, `CarKinematicsModule`, and presence of `ReplayModule`. Matches **DEM1-TASK-DETAIL** success conditions.

### Task 3b — Documentation

**Found:** `docs/demos-1/DEM1-TASK-DETAIL.md` § DEM1-D008 documents **`AsyncRecorder` blocking**, avoidance of **`RecordingModule`**, and test expectations using **`GetRegisteredModuleTypeNames()`**. **`ParallelStoriesScenario`** class XML matches the implementation.

### Task 4 — DEM1-D009

**Deferred** per instructions when scope exceeds estimate — **acceptable**. **Next batch should open Phase A** (harness + loopback + one test + registry stub).

---

## Design alignment

- **BS1-T022 / ingress:** Legacy string → **`DoctrineFinished`** is consistent with **`SimHostInstance`** egress semantics from BATCH-07.
- **DEM1-D008:** Live/replay split, deterministic capture, and topology proof align with **DEM1-DESIGN** §6.3 and updated task detail.
- **`GetRegisteredModuleTypeNames`:** Documented as diagnostics-only; O(n) alloc is appropriate for tests and admin-style use.

---

## Test quality

- **ParallelStories:** Position checks plus **module topology** give meaningful regression coverage; the topology test would fail if kinematics were mistakenly registered on the replay kernel.
- **ComponentReflector:** New three-frame cycle test validates **baseline reversion** detection — good complement to existing diff tests.
- **Map.Common:** Ingress helper test locks the **wire compat** contract for `"ReachedDestination"`.

---

## Report deltas

- ImGui test **count** in report (42) vs current solution (**43**) — update future reports from CI output.
- **“Zero new warnings”** is stronger than a full solution build with pre-existing dependency warnings (e.g. Cyclone CS1591); no **new** issues identified in touched projects.

---

## Suggested commit message

```
BATCH-08: Pooled reflector buffers; doctrine IDs rename; ingress ReachedDestination→DoctrineFinished; kernel module introspection + D008 doc sync

- ComponentReflector: replace AllocHGlobal with ArrayPool + pinned buffer; add three-frame cache test
- Rename scenarios-local DemoDoctrineIds to BehaviorValidationDoctrineIds
- MissionTriggerHelper: map legacy ReachedDestination wire string to DoctrineFinished; extend ingress tests
- ModuleHostKernel: add GetRegisteredModuleTypeNames for diagnostics/tests
- ParallelStories: expose ReplayKernelModuleTypeNames; test asserts replay kernel has no kinematics modules
- DEM1-TASK-DETAIL D008: align with LiveKinematicsModule + blocking AsyncRecorder
```

---

## Follow-ups (BATCH-09)

1. **DEM1-D009** Phase A (DistributedTank): two kernels, Cyclone loopback, minimal handshake test, registry entry.  
2. **DEBT:** `LocalGridBuilderSystem` incremental grid; `PerceptionScopedView` consumption semantics; optional **`RecordingModule` blocking** flag.  
3. **Hygiene:** Rename `DemoDoctrineIds.cs` → `BehaviorValidationDoctrineIds.cs` if desired.
