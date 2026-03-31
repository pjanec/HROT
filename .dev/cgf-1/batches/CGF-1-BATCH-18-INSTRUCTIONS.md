# CGF-1-BATCH-18: S0305 dispatch correctness (debt) + FullBranchPipelineTests

**Batch number:** CGF-1-BATCH-18  
**Tasks:** **Part A — BATCH-17 review follow-ups (P1/P2 tech debt)** → **Part B — §CGF1-S0305 `FullBranchPipelineTests`**  
**Phase:** Phase 3 — persistence  
**Estimated effort:** 8–16 h Part A + 16–32 h Part B  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-17](../reviews/CGF-1-BATCH-17-REVIEW.md) — CONDITIONALLY APPROVED

---

## Sequencing note

**Part A** must land first: without correct `**PrepareLive`** routing and `**PrepareAsync`/`Commit` ordering**, Part B integration tests may pass for the wrong reasons or remain flaky.

---

## Onboarding

1. [.dev/cgf-1/reviews/CGF-1-BATCH-17-REVIEW.md](../reviews/CGF-1-BATCH-17-REVIEW.md) — critical gap + CGF + `ClusterSlave` sections
2. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0305 (`FullBranchPipelineTests` success condition)
3. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-18** (CGF-1 section)

**Report:** [CGF-1-BATCH-18-REPORT.md](../reports/CGF-1-BATCH-18-REPORT.md) — **Review:** [CGF-1-BATCH-18-REVIEW.md](../reviews/CGF-1-BATCH-18-REVIEW.md)

---

## Part A — Tech debt (BATCH-17 review + DEBT-TRACKER)

### A.1 — **SimHost: `PrepareLive` must reach the S0305 branch handler** (P1)

**Problem:** `[ClusterSlave](../../../Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs)` dispatches the **first** handler with `**CanHandle(PrepareLive)`**. `[NodeBootstrapper](../../../Hrot.SimHost/NodeBootstrapper.cs)` registers `[LiveLoadDsmHandler](../../../Hrot.SimHost/Modules/Orchestration/LiveLoadDsmHandler.cs)` **before** `[ReplayLoadDsmHandler](../../../Hrot.SimHost/Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs)`, so `**ReplayLoadDsmHandler`’s Live-from-Replay path never runs** on the real app.

**Fix (pick one, document in XML):**

- Register `**ReplayLoadDsmHandler` before `LiveLoadDsmHandler`** **and** narrow `**ReplayLoadDsmHandler.CanHandle(PrepareLive)`** to **only** when a replay session is active (e.g. `**EcsRecordReplayController.ActiveReplayModule != null`**) **or** local DSM / command discriminator matches the branch contract; `**LiveLoadDsmHandler`** handles **normal** cold `**PrepareLive`**; **or**
- Fold branch logic into `**LiveLoadDsmHandler`** (detect active replay → teardown + record) and remove duplicate `**PrepareLive**` from `**ReplayLoadDsmHandler.CanHandle**`; **or**
- Equivalent design with **no** double-claim on `**PrepareLive`**.

**Tests:** Add a test that uses **real `[BuildOrchestration](../../../Hrot.SimHost/NodeBootstrapper.cs)` + `[ClusterSlave](../../../Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs)` dispatch** (or shared helper), **not** a hand-built `**ReplayLoadDsmHandler`** only — assert the **branch** runs when replay is active.

### A.2 — **CGF: branch `PrepareLive` must not be swallowed by `ScenarioLoadDsmHandler`** (P2)

**Problem:** On `[CgfApplication](../../../Hrot.CGF/CgfApplication.cs)`, `**ScenarioLoadDsmHandler`** is registered first and `**CanHandle(PrepareLive)**` is always true. Branch payloads carry `**ExerciseId**` without `**ScenarioId**` → handler returns success immediately → `**FailLoudRecordReplayStub**` never runs.

**Fix:** `**ScenarioLoadDsmHandler`** should `**CanHandle` false** (or delegate) when payload is **branch-style** (e.g. `**ExerciseId` present, `ScenarioId` absent**), **or** run stub **before** scenario load for `**PrepareLive`**, **or** explicit `**PrepareLive` disambiguation** in one handler. **Goal:** branch `**PrepareLive`** on CGF is **visible** (stub **Error** or real brain replay) per architecture note.

### A.3 — `**ClusterSlave`: `PrepareAsync` completion before `Commit`** (P2)

**Problem:** `[ClusterSlave.DispatchCommand](../../../Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs)` does not **await** `**PrepareAsync`** before `**Commit**`.

**Fix:** Propagate `**async`** dispatch on the appropriate thread (main/ECS), **or** split handlers into sync prepare + deferred ACK via `**ITickableDsmHandler`**, **or** document and enforce **synchronous** prepare for paths that mutate group flags — **must** be correct for `**InstallModuleAsync`/`UninstallModuleAsync`** replay/recording flows.

### A.4 — **DEBT-TRACKER**

Close **Part A** rows when merged (Status ✅).

---

## Part B — `FullBranchPipelineTests` (§CGF1-S0305)

Implement `**FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`** per [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0305 (100 ticks live → seek → branch → 50 ticks → assert `**.fdp**` keyframe at frame 0 vs tick-50 snapshot).

**Prerequisite:** Part **A.1–A.3** stable enough that the pipeline under test matches **production** dispatch semantics.

---

## Success criteria

- [x] Part A: `PrepareLive` routing fixed; CGF branch visibility; `PrepareAsync`/`Commit` ordering addressed; DEBT rows closed.  
- [x] Part B: `FullBranchPipelineTests` green; §S0305 narrative complete in tracker.  
- [x] Solution build clean.  
- [x] **CGF-1-TASK-TRACKER** clears **S0305** residual note when Part B done.  
- [x] Report filed.  
- **Lead follow-up:** [CGF-1-BATCH-18-REVIEW.md](../reviews/CGF-1-BATCH-18-REVIEW.md) — CGF **`PrepareLive`/scenario** regression + CGF **`ClusterSlave`** parity → [CGF-1-BATCH-19](CGF-1-BATCH-19-INSTRUCTIONS.md).

---

## Reference

- [CGF-1-BATCH-17 review — critical gap](../reviews/CGF-1-BATCH-17-REVIEW.md#critical-gap---preparelive-never-reaches-replayloaddsmhandler-on-simhost)

