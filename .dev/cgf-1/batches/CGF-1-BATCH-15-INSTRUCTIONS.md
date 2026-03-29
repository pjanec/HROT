# CGF-1-BATCH-15: S0303 production wiring (debt) + CGF1-S0309 Dry Run DSM Handler

**Batch number:** CGF-1-BATCH-15  
**Tasks:** **Part A — BATCH-14 checkpoint wiring + prefetch polish (tech debt)** → **Part B — CGF1-S0309** (Dry Run DSM handler)  
**Phase:** Phase 3 — persistence  
**Estimated effort:** 6–12 h Part A + 12–18 h Part B  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-14](../reviews/CGF-1-BATCH-14-REVIEW.md) — CONDITIONALLY APPROVED (checkpoint code landed; **SimHost bootstrap gap**)

---

## Sequencing note

Complete **Part A** first so **`TakeSnapshot`** and **`FinalizeLive` drain** work in **production SimHost**, then **Part B** (dry run) builds on the same **`EntityRepository.SyncFrom`** story without touching **`CheckpointIOWorker`**.

---

## Onboarding

1. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.3 (checkpointing), §5.9 (dry run)  
2. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0309  
3. [.dev/cgf-1/reviews/CGF-1-BATCH-14-REVIEW.md](../reviews/CGF-1-BATCH-14-REVIEW.md) — production wiring gap  
4. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-15**

**Report:** [.dev/cgf-1/reports/CGF-1-BATCH-15-REPORT.md](../reports/CGF-1-BATCH-15-REPORT.md)  
**Review:** [.dev/cgf-1/reviews/CGF-1-BATCH-15-REVIEW.md](../reviews/CGF-1-BATCH-15-REVIEW.md)

---

## Part A — Tech debt (BATCH-14 review + DEBT-TRACKER)

### A.1 — **Wire `CheckpointIOWorker` + `CheckpointDsmHandler` + `LiveLoadDsmHandler` in production SimHost** (P2)

**Problem:** [BATCH-14 review](../reviews/CGF-1-BATCH-14-REVIEW.md#critical-gap-checkpoint-path-not-registered-in-production-simhost) — `NodeBootstrapper.BuildOrchestration` / `SimHostApp.OnLoad` never create the worker, never register `CheckpointDsmHandler`, and pass **`checkpointWorker: null`** into `LiveLoadDsmHandler`.

**Required:**

- Choose a **checkpoint storage directory** (e.g. under `localTempRoot`, configurable; document in report).  
- **Own** `CheckpointIOWorker` lifetime in **`SimHostApp`** (or bootstrap): create, pass into `BuildOrchestration`, **`Dispose`** on application shutdown.  
- Register **`CheckpointDsmHandler`** for orchestration-capable roles (mirror **`PrefetchFiles`** / scenario handler rules).  
- Pass the **same worker instance** into **`LiveLoadDsmHandler(..., checkpointWorker)`** so **`FinalizeLive`** awaits **`DrainAsync()`**.  
- Ensure **`DrillSlave.Tick()`** runs (already polls **`ITickableDsmHandler`**) so deferred checkpoint ACKs publish.  
- Add a **narrow test** if feasible: e.g. bootstrap/build helper asserts **`CheckpointDsmHandler`** is registered when participant + world exist — or document **manual** Runner verification.

### A.2 — **Prefetch empty NAS scenario directory** (P3, optional if time-boxed)

When NAS `sourceDir` exists but contains **no files**, **`SuccessCount == 0`** and **`FailureCount == 0`** — **`DrainPendingPrefetch`** currently treats as success. Decide: **fail** the transition (`SysOpStatus.Failure`) vs **allow** (empty scenario). Implement + test.

### A.3 — **DEBT-TRACKER**

Close **Part A** rows when merged.

---

## Part B — CGF1-S0309: Dry Run DSM Handler

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0309](../CGF-1-TASK-DETAIL.md#cgf1-s0309--dry-run-dsm-handler)  
**Design:** [CGF-1-DESIGN.md §5.9](../CGF-1-DESIGN.md#59-stage-39--dry-run-dsm-handler)

**Implementation notes:**

1. **`DryRunDsmHandler`** behaviour and success-condition tests per task detail (`LoadingDryRun` / `UnloadingDryRun`, `Abort`, no-op for other `PrepareState` targets, no `ITickableDsmHandler`, no checkpoint worker).  
2. **Layering:** Task text names `Bagira.SimHost/.../DryRunDsmHandler.cs`. **`Bagira.IG`** does **not** reference **`Bagira.SimHost`**. **Preferred:** implement the handler in **`Bagira.Common`** (Fdp.Kernel + orchestration types already referenced) under e.g. `Orchestration/Handlers/DryRunDsmHandler.cs`, register from SimHost / IG / IOS / CGF — and **update TASK-DETAIL path** in the report if moved. **Avoid** duplicating 100+ lines across assemblies.  
3. **Component for tests:** Task mentions **`SimPosition`**; if no such type exists, use a **minimal test-only struct** with `[ComponentId]` (same pattern as `CheckpointDsmHandlerTests` / `EditLoadDsmHandlerTests`) and state that in the report.  
4. **Registrations:**  
   - **SimHost:** `NodeBootstrapper` and/or `SimHostApp` — pass **live `EntityRepository`**.  
   - **`CgfApplication`:** register with **`liveRepo: null`** (no ECS world in shell).  
   - **IG / IOS:** register with **`liveRepo: null`** for 2PC participation.  
5. **IG / IOS `DrillSlave`:** if **`Tick()`** does not yet poll **`ITickableDsmHandler`**, dry run does not need it; only ensure **handler dispatch** matches SimHost ordering conventions.

**Success conditions:** all tests listed in §CGF1-S0309.

---

## Success criteria

- [x] Part A: production **checkpoint** path wired + disposed; optional empty-dir prefetch policy; DEBT rows closed.  
- [x] Part B: **CGF1-S0309** complete + tests green.  
- [x] Solution build clean.  
- [x] **CGF-1-TASK-TRACKER** marks **S0309** `[x]`; clarify **S0303** line if wiring completes this batch.  
- [x] Report filed.

---

## Reference

- [CGF-1-BATCH-14 review — checkpoint wiring](../reviews/CGF-1-BATCH-14-REVIEW.md#critical-gap-checkpoint-path-not-registered-in-production-simhost)  
- **Next:** [CGF-1-BATCH-16](CGF-1-BATCH-16-INSTRUCTIONS.md)
