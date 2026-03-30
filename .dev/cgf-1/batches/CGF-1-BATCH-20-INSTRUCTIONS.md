# CGF-1-BATCH-20: S0308 TASK-DETAIL residual (debt) + S0310 / S0106

**Batch number:** CGF-1-BATCH-20  
**Tasks:** **Part A — BATCH-19 review follow-ups (P2 tech debt)** → **Part B — CGF1-S0310** (E2E DSM test script suite) *and/or* **CGF1-S0106** (Orchestrator ImGui scenario & story controls) per capacity  
**Phase:** Phase 1 (S0106) + Phase 3 (S0308 residual, S0310)  
**Estimated effort:** 8–16 h Part A + 24–48 h Part B (split across batches if needed)  
**Priority:** HIGH (Part A)  
**Dependencies:** [CGF-1-BATCH-19](../reviews/CGF-1-BATCH-19-REVIEW.md) — CONDITIONALLY APPROVED

---

## Sequencing note

**Part A** closes honest gaps vs [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §**CGF1-S0308** and restores **green** `Bagira.SimHost.Integration.Tests` for the **`EcsRecordReplayController`** registration assertion.

---

## Onboarding

1. [.dev/cgf-1/reviews/CGF-1-BATCH-19-REVIEW.md](../reviews/CGF-1-BATCH-19-REVIEW.md) — Part B vs TASK-DETAIL table  
2. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §**CGF1-S0308**, §**CGF1-S0310**, §**CGF1-S0106**  
3. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) — matching §§ for story / orchestrator UI  
4. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-20**

**Report:** [CGF-1-BATCH-20-REPORT.md](../reports/CGF-1-BATCH-20-REPORT.md)  
**Review:** [CGF-1-BATCH-20-REVIEW.md](../reviews/CGF-1-BATCH-20-REVIEW.md)

---

## Part A — Tech debt (BATCH-19 review + DEBT-TRACKER)

### A.1 — **§S0308: `StoryLoadDsmHandler` on `Bagira.CGF`** (P2)

Per TASK-DETAIL: CGF nodes that carry story files for the brain subsystem must participate in **`StartStory`/`StopStory`** (or explicitly document why SimHost-only is normative and update TASK-DETAIL).

### A.2 — **`NodeOpStatus.IsParticipating` + `DrillMaster` ACK gating** (P2)

Wire **`StoryLoadDsmHandler`** to publish **`NodeOpStatus`** (where writer exists) with **`IsParticipating`**; extend **`DrillMaster`** story path to **wait only for participating nodes** (TASK-DETAIL item 4). If full 2PC is too large, document the **intentional MVP** delta in TASK-DETAIL **and** DESIGN in the same PR.

### A.3 — **`RecordReplayIntegrationTests.NodeBootstrapper_BrainRole_RegistersEcsRecordReplayController`** (P2)

Replace **`IsHandlerRegistered<EcsRecordReplayController>`** with assertions that match **`BuildOrchestration`** (**`ReplayLoadDsmHandler`**, **`LiveLoadDsmHandler`**, shared controller) **or** register the controller only if product requires it (unlikely).

### A.4 — **DEBT-TRACKER**

Close **Part A** rows when merged (Status ✅).

---

## Part B — S0310 / S0106

Implement per **extended** [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md):

- **Default priority:** **CGF1-S0310** — E2E DSM test script suite (automation / Runner hooks as specified).  
- **CGF1-S0106** — Orchestrator ImGui scenario & story controls (depends on stable **`ActiveStoriesJson`** / story ops — can overlap Part A).

If Part B must split: land **S0310** test harness first, then **S0106** UI in **BATCH-21** with tracker note.

---

## Success criteria

- [x] Part A: §S0308 residual addressed or TASK-DETAIL revised with lead approval; integration test suite **0** failures from **`RecordReplayIntegrationTests`** regression; DEBT rows closed.  
- [x] Part B: §S0310 and/or §S0106 per TASK-DETAIL success conditions (or explicit defer + tracker).  
- [x] Solution build clean (subject to pre-existing **`Fhsm.SourceGen`** DLL lock in some environments).  
- [x] **CGF-1-TASK-TRACKER** updated (Phase 1 / Phase 3 progress).  
- [x] Report filed; review: [CGF-1-BATCH-20-REVIEW.md](../reviews/CGF-1-BATCH-20-REVIEW.md).

---

## Reference

- [CGF-1-BATCH-19 review — Part B gaps](../reviews/CGF-1-BATCH-19-REVIEW.md#part-b--mvp-vs-cgf1-s0308)
