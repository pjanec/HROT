# CGF-1-BATCH-13: S0307 follow-up debt + CGF1-S0302 (Portable Scenario Loading)

**Batch number:** CGF-1-BATCH-13  
**Tasks:** **Part A — CGF-1-BATCH-12 follow-ups (tech debt)** → **CGF1-S0302**  
**Phase:** Phase 3 — persistence  
**Estimated effort:** 28–38 hours (~8–14 h Part A + ~20–24 h S0302)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-12](../reviews/CGF-1-BATCH-12-REVIEW.md) — APPROVED (S0307 landed; execution gaps remain)

---

## Sequencing note

**CGF1-S0302** was **not** completed in BATCH-12. BATCH-12 added a **`PrefetchScenario`** **planner** step and **`ScenarioLoadDsmHandler`** for **`PrepareLive`**, but **S0302** targets **`LoadingEdit`**, **`EditLoadDsmHandler`**, the **task-detail minimal JSON** shape (or an explicit adapter from that shape to **`ScenarioSerializer`** DOM), and **named unit tests**. Complete **Part A** so prefetch and orchestrator context behaviour are **real**, not only planned.

---

## Onboarding

1. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.2  
2. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0302  
3. [.dev/cgf-1/reviews/CGF-1-BATCH-12-REVIEW.md](../reviews/CGF-1-BATCH-12-REVIEW.md) — gaps + S0302 overlap table  
4. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-13**

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-13-REPORT.md`

---

## Part A — Tech debt (BATCH-12 review)

### A.1 — **Execute `PrefetchScenario` in `ClusterMaster`** (P2)

When processing **`ClusterOpRequest`** / transaction steps, **run** **`StorageGatewayModule.PrefetchScenarioAsync`** when the plan contains **`OperationStep(ClusterOpType.PrefetchScenario, scenarioId)`** — build **`NodeDistributionTarget`** list from **`NodeRoster`** (or documented substitute) and use the configured NAS root. **Fail loud** if prefetch is required but NAS path or config is invalid.

### A.2 — **`NodeOpType.PrefetchFiles` on nodes** (P2)

Implement a **`ClusterSlave`** / **`IDsmHandler`** path (SimHost at minimum) that receives **`PrefetchFiles`**, applies the manifest (copy from staged path or UNC per design), and ACKs **`NodeOpStatus`**. Wire together with **A.1** so an end-to-end test (integration or narrow harness) proves files land under **`C:\FDP_Temp\<scenarioId>\`**.

### A.3 — **`GlobalContextDsmHandler` contract** (P2)

- **Either** call **`MasterTimeController.SeedState`** from the real orchestrator composition when **`LoadedStartWallTicks`** is set **or** remove/adjust XML that promises **`SeedState`** until wiring exists.  
- Replace **silent** returns on missing **`Orchestrator.json`** / null DTO with **`InvalidOperationException`** (or structured **`OpStatus.Failure`**) when load was **required** — distinguish “optional context” vs “mandatory scenario load” in API if needed.

### A.4 — **`SimHostApp` (+ Runner) serializer wiring** (P2)

Build or resolve a **`ScenarioSerializer`** (subsystem type **`Hrot.SimHost`**, translators, **`FdpAutoSerializer.Build`**) in **`SimHostApp.OnLoad`** (or equivalent) and pass it into **`NodeBootstrapper.BuildOrchestration`** so **`ScenarioLoadDsmHandler`** is registered in **production**, not only tests.

### A.5 — **Fail-loud polish** (P3)

- **`ClusterMaster.ConsumeNodeOpStatuses`**: consider **failing the save transaction** (or incrementing **`FailureCount`** at orchestrator level) when **`ResultJson` is malformed**, instead of only logging.  
- **`TransitionPlanner`**: do **not** swallow **`JsonException`** when parsing **`ScenarioId`** — either omit the prefetch step with a **logged warning** only if payload is intentionally non-object, or **surface** parse failure consistently.

### A.6 — **DEBT-TRACKER**

Close **Part A** rows when merged.

---

## Part B — CGF1-S0302: Portable Scenario Loading

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0302](../CGF-1-TASK-DETAIL.md#cgf1-s0302--portable-scenario-loading)  
**Design:** [CGF-1-DESIGN.md §5.2](../CGF-1-DESIGN.md#52-stage-32--portable-scenario-loading)

1. **`EditLoadDsmHandler`** in **`Hrot.SimHost`** for **`LoadingEdit`** ( **`PrepareAsync` / `Commit`** per task detail: **`IsNewScenario`**, verify pre-fetched JSON when **`ScenarioId != null`**, **`BaseTerrain`** blank world or deserialize + **`EntityCommandBuffer`** ).  
2. **Schema:** implement the **task-detail minimal JSON** **or** document that the **portable** format is the **§5.6 / `ScenarioSerializer`** DOM and provide a **small adapter** from the minimal array form to that DOM for backward tests — pick one in the **report** and align **success-condition tests**.  
3. **`TransitionPlanner`**: ensure **`PrefetchScenario`** (or equivalent gateway step) is injected **before `TransitionStep(LoadingEdit)`** when **`ScenarioId`** is present — adjust **A.1** if the step must differ between **LoadingLive** vs **LoadingEdit**.  
4. **Unit tests** ( **`Hrot.SimHost.Tests`** or as specified in task detail):  
   - **`EditLoadDsmHandlerTests`** — all three behaviours from §CGF1-S0302.  
   - **`TransitionPlannerTests.PlanWithScenarioId_InjectsStorageGatewayStep`** — assert first step is prefetch **before** **`LoadingEdit`**.

---

## Success criteria

- [x] Part A: prefetch **executed**; node **PrefetchFiles** path; **GlobalContext** / **SeedState** / XML honest; **SimHost** serializer wired; DEBT rows closed.  
- [x] Part B: CGF1-S0302 success conditions + tests green.  
- [x] Solution build clean.  
- [x] **CGF-1-TASK-TRACKER** marks **S0302** `[x]`; **DEBT-TRACKER** updated.  
- [x] Report filed.

---

## Reference

- [CGF-1-BATCH-12 review — S0302 overlap](../reviews/CGF-1-BATCH-12-REVIEW.md#overlap-with-cgf1-s0302-portable-scenario-loading)  
- **Next:** [CGF-1-BATCH-14](CGF-1-BATCH-14-INSTRUCTIONS.md) — prefetch hardening first, then **CGF1-S0303** (checkpointing).
