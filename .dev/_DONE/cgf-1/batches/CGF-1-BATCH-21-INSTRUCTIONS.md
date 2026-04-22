# CGF-1-BATCH-21: Story 2PC / ACK debt + Phase 4 (Generalization)

**Batch number:** CGF-1-BATCH-21  
**Tasks:** **Part A — BATCH-20 review tech debt (P2 first)** → **Part B — Phase 4** (orchestration generalization: **CGF1-G0401**–**CGF1-G0406** per [CGF-1-GENERALIZATION.md](../CGF-1-GENERALIZATION.md))  
**Phase:** Tech-debt closure + **Phase 4 — Generalization** (not Phase 1 S0106 / Phase 3 S0310 in this batch)  
**Estimated effort:** 8–20 h Part A + **large** Part B (expect **multiple batches** if needed; land **G0401** early — unblocks the rest)  
**Priority:** HIGH (Part A — prevents silent orchestrator / story state drift); HIGH (Part B — foundation for all future FDP DSM consumers)  
**Dependencies:** [CGF-1-BATCH-20](../reviews/CGF-1-BATCH-20-REVIEW.md) — APPROVED

---

## Sequencing note

1. **Part A** closes BATCH-20 ACK / fail-loud gaps **before** large refactors in Part B (avoid mixing behavioural fixes with mass moves).  
2. **Part B** executes **[CGF-1-GENERALIZATION.md](../CGF-1-GENERALIZATION.md)** and [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §Phase 4 (**CGF1-G0401**–**CGF1-G0406**).  
3. **CGF1-S0310** (E2E DSM test script suite) and **CGF1-S0106** (Orchestrator ImGui scenario & story controls) are **postponed until Phase 4 is fully complete** (all G04 tasks green + CI). They are **out of scope** for BATCH-21 unless the lead explicitly reprioritizes — tracker and any batch-after-21 should say so.

If Part B does not finish in one batch: file **CGF-1-BATCH-22** (or subsequent) continuing **G0402**–**G0406** with the same deferral note for S0310 / S0106.

---

## Onboarding

1. [.dev/cgf-1/reviews/CGF-1-BATCH-20-REVIEW.md](../reviews/CGF-1-BATCH-20-REVIEW.md) — fail-loud / ACK gaps (Part A)  
2. [.dev/cgf-1/CGF-1-GENERALIZATION.md](../CGF-1-GENERALIZATION.md) — design authority for Part B  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) — §Phase 4 (**CGF1-G0401**–**CGF1-G0406**); §CGF1-S0308 only if Part A touches story ACK text  
4. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-21** (Part A)  
5. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) — baseline DSM (Phase 4 supersedes handler placement for new work)

**Report:** [CGF-1-BATCH-21-REPORT.md](../reports/CGF-1-BATCH-21-REPORT.md)  
**Review:** [CGF-1-BATCH-21-REVIEW.md](../reviews/CGF-1-BATCH-21-REVIEW.md)

---

## Part A — Tech debt (BATCH-20 review + DEBT-TRACKER)

Unchanged intent from BATCH-21 planning — close P2 first.

### A.1 — ClusterMaster.ManageEpisode + NodeOpStatus (P2)

**Problem:** Nodes publish `NodeOpStatus` with `IsParticipating` for `StartEpisode` / `StopEpisode`, but `ClusterMaster` does not wait or filter ACKs before updating `ActiveStories` / completing the sys-op transaction.

**Goal:** Minimal **2PC** (or documented smaller step): after `FanOutNodeOp`, collect `NodeOpStatus` for the story transaction id, require **Success** from all targeted nodes (or only **participating** nodes per TASK-DETAIL end-state), then mutate `ActiveStories` and emit final `ClusterOpStatus`. Reuse patterns from other `NodeOpStatus` consumers in `ClusterMaster` where possible.

**Tests:** At least one test proving **non-participating** ACK does not block completion when policy is “participating-only” (if that is the chosen rule).

**Note:** After Phase 4, `ClusterMaster` may consume toolkit types — keep changes compatible with the migration playbook in **CGF-1-GENERALIZATION.md** §7.

### A.2 — StoryLoadDsmHandler (SimHost): always ACK or fail loud (P2)

**Problem:** Invalid prepare payloads or null `EntityRepository` can leave no `NodeOpStatus` for a transaction.

**Goal:**

- Every `PrepareAsync` → `Commit` path for `StartEpisode` / `StopEpisode` either publishes an ACK (`OpStatus` as appropriate) or throws (no silent `Commit` no-op when the orchestrator expects an ACK).  
- `Commit*` when repo is unavailable: **NAK** or **throw**, not **Warn + return** only.

### A.3 — DESIGN + TASK-DETAIL hygiene (P3)

- Add to **CGF-1-DESIGN.md** a short **ManageEpisode / story ACK** note mirroring the MVP delta in **TASK-DETAIL** §CGF1-S0308 (or update TASK-DETAIL if behaviour changes in A.1).  
- Rename **`RecordReplayIntegrationTests.NodeBootstrapper_BrainRole_RegistersEcsRecordReplayController`** to match the **`LiveLoadDsmHandler`** assertion.

### A.4 — DEBT-TRACKER

Close **Part A** rows when merged (Status ✅).

---

## Part B — Phase 4: FDP Toolkit Orchestration (full track)

Implement per **[CGF-1-GENERALIZATION.md](../CGF-1-GENERALIZATION.md)** and task detail:

| Task | Short description |
|------|-------------------|
| **CGF1-G0401** | `FDP.Toolkit.Orchestration` core contracts (interfaces, message DTOs if any that stay toolkit-pure) |
| **CGF1-G0402** | Generic `ClusterSlave` + transport seam (`IOrchestrationTransport` / DDS adapter lives in Hrot per doc) |
| **CGF1-G0403** | `TransitionPlanner` generalized on `ITransitionGraph` (`HrotStateGraph` for Hrot) |
| **CGF1-G0404** | Reference scenario / story / prefetch handlers |
| **CGF1-G0405** | Reference dry-run, checkpoint, record/replay handlers |
| **CGF1-G0406** | Final wiring cleanup, remove duplicate `ClusterSlave` copies where safe, **CI validation** |

Follow the **migration playbook** and dependency rules in **CGF-1-GENERALIZATION.md** (`FDP.Toolkit.*` must not reference `Hrot.*`).

**Do not** start **CGF1-S0310** or **CGF1-S0106** until **all** Phase 4 tasks above are done and passing CI — see tracker.

---

## Success criteria

- [x] **Part A:** `ClusterMaster` story path consumes `NodeOpStatus` per agreed policy; SimHost `StoryLoadDsmHandler` has no missing-ACK holes; DESIGN delta documented; test rename done; DEBT rows for Part A closed.  
- [x] **Part B:** Phase 4 tasks implemented per **CGF-1-GENERALIZATION.md** / TASK-DETAIL (subset: G0401–G0403 + partial G0404; remainder → **CGF-1-BATCH-22**).  
- [x] **Explicit:** **CGF1-S0310** and **CGF1-S0106** **not** claimed; deferred until Phase 4 complete.  
- [x] Solution build clean (subject to environmental **`Fhsm.SourceGen`** lock in some environments).  
- [x] **CGF-1-TASK-TRACKER** updated.  
- [x] Report filed; review: [CGF-1-BATCH-21-REVIEW.md](../reviews/CGF-1-BATCH-21-REVIEW.md).

---

## Reference

- [CGF-1-BATCH-20 review — fail-loud / ACK gaps](../reviews/CGF-1-BATCH-20-REVIEW.md)  
- [CGF-1-GENERALIZATION.md — Phase 4 design authority](../CGF-1-GENERALIZATION.md)
