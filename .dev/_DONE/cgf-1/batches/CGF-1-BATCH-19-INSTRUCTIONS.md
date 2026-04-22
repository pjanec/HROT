# CGF-1-BATCH-19: CGF `PrepareLive` / scenario regression (debt) + Phase 3 continuation

**Batch number:** CGF-1-BATCH-19  
**Tasks:** **Part A — BATCH-18 review follow-ups (P1/P2)** → **Part B — CGF1-S0308** (Runtime Story Injection & Deletion) *or* test hardening per capacity  
**Phase:** Phase 3 — persistence  
**Estimated effort:** 4–8 h Part A + 16–40 h Part B (split Part B if needed)  
**Priority:** HIGH (Part A)  
**Dependencies:** [CGF-1-BATCH-18](../reviews/CGF-1-BATCH-18-REVIEW.md) — CONDITIONALLY APPROVED

---

## Sequencing note

**Part A** unblocks correct **CGF** participation in **`LoadingLive`** / scenario **`PrepareLive`** and aligns **`Hrot.CGF/ClusterSlave`** with **SimHost** prepare/commit semantics before expanding story/runtime features.

---

## Onboarding

1. [.dev/cgf-1/reviews/CGF-1-BATCH-18-REVIEW.md](../reviews/CGF-1-BATCH-18-REVIEW.md) — CGF sections  
2. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-19**  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0308 (if doing Part B)

**Report:** [CGF-1-BATCH-19-REPORT.md](../reports/CGF-1-BATCH-19-REPORT.md) — **Review:** [CGF-1-BATCH-19-REVIEW.md](../reviews/CGF-1-BATCH-19-REVIEW.md)

---

## Part A — Tech debt (BATCH-18 review + DEBT-TRACKER)

### A.1 — **CGF: restore `ScenarioLoadDsmHandler` for normal `PrepareLive`** (P1)

**Problem:** [`FailLoudRecordReplayStub`](../../../Hrot.CGF/Modules/Orchestration/Handlers/FailLoudRecordReplayStub.cs) is registered **first** and **`CanHandle(PrepareLive)`** is **always** true. **[`Hrot.CGF/ClusterSlave`](../../../Hrot.CGF/Modules/Orchestration/ClusterSlave.cs)** invokes **only one** handler — [`ScenarioLoadDsmHandler`](../../../Hrot.CGF/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs) **never runs** for **`PrepareLive`**.

**Fix (pick one, document in XML):**

- Narrow **`FailLoudRecordReplayStub.CanHandle(PrepareLive)`** to **branch-style** payloads only (e.g. JSON has **`ExerciseId`** and **no** **`ScenarioId`**), **or**  
- Register **`ScenarioLoadDsmHandler`** **before** the stub **and** narrow **`ScenarioLoadDsmHandler.CanHandle(PrepareLive)`** to **non-branch** payloads (e.g. **`ScenarioId`** present), with stub catching branch-only ops.

**Tests:** At least one test that **`EnqueueCommand`s** (or equivalent) a **`PrepareLive`** with **`ScenarioId`** and asserts **`ScenarioLoadDsmHandler`** path (log, file peek, or test seam) — **not** only stub **`Error`**.

### A.2 — **CGF `ClusterSlave`: `PrepareAsync` before `Commit`** (P2)

Port the **SimHost** [`ClusterSlave`](../../../Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs) **`_pendingPrepare`** pattern (or shared helper) to [`Hrot.CGF/ClusterSlave`](../../../Hrot.CGF/Modules/Orchestration/ClusterSlave.cs) so **`Commit`** does not race **`PrepareAsync`** when handlers become async.

### A.3 — **DEBT-TRACKER**

Close **Part A** rows when merged (Status ✅).

---

## Part B — Phase 3 next task (pick one)

**Default:** Implement **CGF1-S0308** per [CGF-1-TASK-DETAIL.md §CGF1-S0308](../CGF-1-TASK-DETAIL.md#cgf1-s0308--runtime-story-injection--deletion).

**Alternative (if Part A or CI stability dominates the sprint):** Add optional **`FullBranchPipelineTests`** step that drives the Live-from-Replay branch through **`SimHost.ClusterSlave`** multi-**`Tick`** (see DEBT-TRACKER Opportunistic row); still file a short report explaining Part B deferral.

---

## Success criteria

- [x] Part A: CGF **`PrepareLive`** disambiguation fixed; CGF **`ClusterSlave`** ordering aligned; DEBT rows closed; tests prove scenario path.  
- [x] Part B: §S0308 artefacts + tests **or** explicit defer note + tracker.  
- [x] Solution build clean.  
- [x] **CGF-1-TASK-TRACKER** updated.  
- [x] Report filed.  
- **Lead follow-up:** [CGF-1-BATCH-19-REVIEW.md](../reviews/CGF-1-BATCH-19-REVIEW.md) — §S0308 TASK-DETAIL gaps + integration test fix → [CGF-1-BATCH-20](CGF-1-BATCH-20-INSTRUCTIONS.md).

---

## Reference

- [CGF-1-BATCH-18 review — CGF sections](../reviews/CGF-1-BATCH-18-REVIEW.md#summary--cgf-regression--parity-gap)
