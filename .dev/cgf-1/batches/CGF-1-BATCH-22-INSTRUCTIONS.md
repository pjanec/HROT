# CGF-1-BATCH-22: ManageStory hardening + Phase 4 completion (G0404–G0406)

**Batch number:** CGF-1-BATCH-22  
**Tasks:** **Part A — BATCH-21 review tech debt (P2 first)** → **Part B — Phase 4** finish (**CGF1-G0404** remainder, **G0405**, **G0406**)  
**Phase:** Correctness / observability for story 2PC + **Generalization** completion  
**Estimated effort:** 6–12 h Part A + **large** Part B (split **G0406** if needed)  
**Priority:** HIGH (Part A — story sys-op and NAK semantics); HIGH (Part B — eliminate duplicate `DrillSlave` / wire toolkit)  
**Dependencies:** [CGF-1-BATCH-21](../reviews/CGF-1-BATCH-21-REVIEW.md) — APPROVED

---

## Sequencing note

1. **Part A** closes [CGF-1-BATCH-21 review](../reviews/CGF-1-BATCH-21-REVIEW.md) gaps so **`ManageStory`** does not complete “successfully” on node **NAKs** or leave **orphan** node ops when payload parsing fails.  
2. **Part B** completes **CGF-1-GENERALIZATION.md** / TASK-DETAIL §Phase 4: remaining reference handlers, record/replay/dry-run/checkpoint references, **`NodeBootstrapper`** / **`CgfApplication`** wiring, then **delete or thin** legacy **`DrillSlave`** copies per playbook.  
3. **CGF1-S0310** / **CGF1-S0106** remain **post–Phase 4** — do not start until **G0406** + CI are green.

---

## Onboarding

1. [.dev/cgf-1/reviews/CGF-1-BATCH-21-REVIEW.md](../reviews/CGF-1-BATCH-21-REVIEW.md)  
2. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-22** (Part A)  
3. [.dev/cgf-1/CGF-1-GENERALIZATION.md](../CGF-1-GENERALIZATION.md) §7 migration playbook  
4. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §**CGF1-G0404**–**G0406**  
5. [CGF-1-BATCH-21 report](../reports/CGF-1-BATCH-21-REPORT.md) — deferred work table

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-22-REPORT.md` (when complete)

---

## Part A — Tech debt (BATCH-21 review + DEBT-TRACKER)

### A.1 — ManageStory: `NodeOpStatus.StatusCode` + `SysOpStatus` lifecycle (P2)

- **`ConsumeNodeOpStatuses`** / **`ManageStoryTask`:** If a node ACK indicates **failure** (`OrchestrationStatusCode.IsError` or equivalent), **do not** treat it as benign completion unless policy explicitly documents that — prefer **abort** pending story task, **`ActiveStories` unchanged**, and emit **`SysOpStatus`** **Rejected** / **Failure** with stable **`ErrorCode`**.  
- **`SysOpStatus`:** After all node ACKs for the story transaction are accounted for, publish **`SysOpStatus.Completed`** (or **Rejected** on policy failure) keyed to the **originating `RequestId`** — today only **`InProgress`** is written at accept time.

**Tests:** Extend **`DrillMasterStoryTests`** (or add unit tests with injected reader scope): NAK path; optional timeout / stuck-node behaviour if within scope.

### A.2 — ManageStory: fail loud on bad payload (P2)

- When **`ManageStory` payload JSON** is invalid or **`StoryId`** cannot be parsed: **reject** the **`SysOpRequest`** (mirror **`InvalidOperationException`** path) **or** register a **`_pendingManageStoryTasks`** entry that completes without mutating **`ActiveStories`** while still correlating ACKs — **no** silent **`FanOutNodeOp`** without orchestrator state.

### A.3 — CI / verification (P3)

- Run **`Bagira.SimHost.Tests`** (especially **`StoryLoadDsmHandlerTests`**) and **`Bagira.SimHost.Integration.Tests`** once **`Fhsm.SourceGen`** DLL lock allows; fix any regression from BATCH-21.

### A.4 — DEBT-TRACKER

Close **Part A** rows when merged (Status ✅).

---

## Part B — Phase 4 completion

Per [CGF-1-BATCH-21 report](../reports/CGF-1-BATCH-21-REPORT.md) deferred table:

| Task | Work |
|------|------|
| **G0404** (remainder) | `ReferenceScenarioLoadHandler`, `ReferenceEditLoadHandler`, `ReferenceStoryLoadHandler`; **`NodeBootstrapper`** / **`CgfApplication`** wiring as design allows |
| **G0405** | Reference dry-run, checkpoint, record/replay handlers per **GENERALIZATION** catalogue |
| **G0406** | Remove or consolidate duplicate **`DrillSlave`** implementations; **`BagiraHandlerAdapter`** only if still required; full solution **CI** validation |

**Constraint:** **`FDP.Toolkit.*`** must **not** reference **`Bagira.*`**.

---

## Success criteria

- [ ] Part A: story 2PC honours **NAK** / **StatusCode**; **`SysOpStatus`** reflects completion; bad **`ManageStory`** payloads **fail loud**; SimHost tests green when CI permits; DEBT rows closed.  
- [ ] Part B: **G0404**–**G0406** per TASK-DETAIL + **GENERALIZATION** (or explicit defer of **G0406** subset + tracker note).  
- [ ] **S0310** / **S0106** still not claimed unless Phase 4 is **fully** complete.  
- [ ] Solution build clean.  
- [ ] **CGF-1-TASK-TRACKER** Phase 4 checkboxes updated.  
- [ ] Report filed.

---

## Reference

- [CGF-1-BATCH-21 review](../reviews/CGF-1-BATCH-21-REVIEW.md)  
- [CGF-1-GENERALIZATION.md](../CGF-1-GENERALIZATION.md)
