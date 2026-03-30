# CGF-1-BATCH-21: Story 2PC / ACK debt (BATCH-20 follow-up) + S0310 / S0106

**Batch number:** CGF-1-BATCH-21  
**Tasks:** **Part A — BATCH-20 review tech debt (P2 first)** → **Part B — CGF1-S0310** (E2E DSM test script suite) *and/or* **CGF1-S0106** (Orchestrator ImGui scenario & story controls) per capacity  
**Phase:** Phase 1 (S0106) + Phase 3 (S0310) + orchestration correctness  
**Estimated effort:** 8–20 h Part A + 24–48 h Part B (split Part B if needed)  
**Priority:** HIGH (Part A — prevents silent orchestrator / story state drift)  
**Dependencies:** [CGF-1-BATCH-20](../reviews/CGF-1-BATCH-20-REVIEW.md) — APPROVED

---

## Sequencing note

**Part A** must land **before** heavy **S0106** UI work that assumes **`ActiveStories`** and **`SysOpStatus`** reflect completed node work. **Part B** continues Phase 1 / Phase 3 open tasks deferred from BATCH-20.

---

## Onboarding

1. [.dev/cgf-1/reviews/CGF-1-BATCH-20-REVIEW.md](../reviews/CGF-1-BATCH-20-REVIEW.md) — fail-loud / ACK gaps  
2. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-21** (Part A)  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §**CGF1-S0308**, §**CGF1-S0310** (~1145), §**CGF1-S0106** (~257)  
4. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) — story / orchestrator UI §§

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-21-REPORT.md` (when complete)

---

## Part A — Tech debt (BATCH-20 review + DEBT-TRACKER)

### A.1 — DrillMaster.ManageStory + NodeOpStatus (P2)

**Problem:** Nodes publish **`NodeOpStatus`** with **`IsParticipating`** for **`StartStory`/`StopStory`**, but **`DrillMaster`** does not **wait** or **filter** ACKs before updating **`ActiveStories`** / completing the sys-op transaction.

**Goal:** Minimal **2PC** (or documented smaller step): after **`FanOutNodeOp`**, collect **`NodeOpStatus`** for the story **transaction id**, require **Success** from all targeted nodes (or only **participating** nodes per TASK-DETAIL end-state), then mutate **`ActiveStories`** and emit final **`SysOpStatus`**. Reuse patterns from other **`NodeOpStatus`** consumers in **`DrillMaster`** where possible.

**Tests:** At least one integration or unit test proving **non-participating** ACK does not block completion when policy is “participating-only” (if that is the chosen rule).

### A.2 — StoryLoadDsmHandler (SimHost): always ACK or fail loud (P2)

**Problem:** Invalid prepare payloads or **null** **`EntityRepository`** can leave **no** **`NodeOpStatus`** for a transaction.

**Goal:**

- Every **`PrepareAsync → Commit`** path for **`StartStory`/`StopStory`** either publishes an ACK (**`OpStatus`** as appropriate) or throws (no silent **Commit** no-op when the orchestrator expects an ACK).
- **`Commit*`** when repo is unavailable: **NAK** or **throw**, not **Warn + return** only.

### A.3 — DESIGN + TASK-DETAIL hygiene (P3)

- Add to **CGF-1-DESIGN.md** a short **ManageStory / story ACK** note mirroring the **MVP delta** already in **TASK-DETAIL** §CGF1-S0308 (or update TASK-DETAIL if behaviour changes in A.1).
- Rename **`RecordReplayIntegrationTests.NodeBootstrapper_BrainRole_RegistersEcsRecordReplayController`** to match **`LiveLoadDsmHandler`** assertion.

### A.4 — DEBT-TRACKER

Close **Part A** rows when merged (Status ✅).

---

## Part B — S0310 / S0106

Implement per **extended** [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md):

- **Default priority:** **CGF1-S0310** — E2E DSM test script suite (Runner hooks / automation as specified).  
- **CGF1-S0106** — Orchestrator ImGui scenario & story controls (depends on stable **`ActiveStories`** / story ops — **after Part A** is strongly preferred).

If Part B must split: land **S0310** harness first; **S0106** can slip to **BATCH-22** with tracker note.

---

## Success criteria

- [ ] Part A: **`DrillMaster`** story path consumes **`NodeOpStatus`** per agreed policy; SimHost **`StoryLoadDsmHandler`** has no “missing ACK” holes; DESIGN delta documented; test rename done; DEBT rows closed.  
- [ ] Part B: §S0310 and/or §S0106 per TASK-DETAIL success conditions (or explicit defer + tracker).  
- [ ] Solution build clean.  
- [ ] **CGF-1-TASK-TRACKER** updated.  
- [ ] Report filed.

---

## Reference

- [CGF-1-BATCH-20 review — fail-loud / ACK gaps](../reviews/CGF-1-BATCH-20-REVIEW.md)
