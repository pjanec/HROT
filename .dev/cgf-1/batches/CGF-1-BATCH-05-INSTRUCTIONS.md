# CGF-1-BATCH-05: Planner/orchestrator correctness debt + CGF1-S0202

**Batch number:** CGF-1-BATCH-05  
**Tasks:** **Part A — BATCH-04 review & open DEBT** → **CGF1-S0202** (DSM handler wiring)  
**Phase:** Phase 2 — State & Time  
**Estimated effort:** 20–26 hours (~4–6 h Part A + ~16–20 h S0202)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-04](../reviews/CGF-1-BATCH-04-REVIEW.md) — APPROVED  

---

## Onboarding

1. [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §4.1, §4.2  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0202  
4. [.dev/cgf-1/reviews/CGF-1-BATCH-04-REVIEW.md](../reviews/CGF-1-BATCH-04-REVIEW.md) — Issues 1–3  
5. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-05**  

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-05-REPORT.md`  

---

## Mandatory workflow

Complete **Part A** (debt + correctness) **before** large S0202 surface area. Full **`dotnet test IOS-IG-SimHost.sln`** green before report.

---

## Part A — Correctness & documentation (first)

### A.1 — Fix **CGF-1-DESIGN.md §4.1** adjacency prose (DEBT)

- Edit the **Valid DSM Adjacency List** so **`RunningEdit`** does **not** list a direct edge to **`LoadingLive`** (match **`TransitionPlanner`** and the **RunningEdit → RunningLive** trajectory table).  
- Add a one-line **footnote** explaining why (unload edit session before live load).

### A.2 — **PlanTrajectory** fail-fast payload rules (DEBT P2)

**File:** `Bagira.Orchestrator/TransitionPlanner.cs`  

- For **`TransitionState`** requests, **never** silently default **`targetState`** to **`Standby`** when the payload is empty, whitespace-only, or non-parseable JSON (unless the contract explicitly allows “omit = Standby” — if so, document in XML and task detail).  
- **`InvalidOperationException`** with a clear message when the payload does not yield a valid target.  
- Add **unit tests** for: garbage JSON, empty string, `{ }` without `TargetState`.

### A.3 — **DrillMaster** authoritative **`_currentDsmState`** (DEBT P2)

**File:** `Bagira.Orchestrator/DrillMaster.cs`  

- Implement a **documented** rule for updating **`_currentDsmState`** so **`PlanTrajectory(current, …)`** matches cluster reality. Acceptable Phase 2.0 approaches (pick one, document in class XML):  
  - Track last **`SystemStateTopic.CurrentState`** the orchestrator **wrote**; **or**  
  - Advance only when a **minimal** transaction step completes (may stay stubbed until more of 2PC exists — if so, document limitation and add a **single** integration-style test or clear **`TODO`** gated by S0202).  
- **Minimum bar:** after accepting a **`TransitionState`** request whose plan’s **final `TransitionStep`** is state **T**, set **`_currentDsmState = T`** for **optimistic** planning of the **next** request, **or** prove why not and keep **`Failure`** until real ACKs land in **S0202+**.

### A.4 — Payload protocol (DEBT P3)

- Prefer **one** normative JSON shape for **`TransitionState`** payloads in docs + samples; deprecate plain-int in new code paths or document both until S0203.

### A.5 — **DEBT-TRACKER**

Mark **✅** rows closed when done; add none without a target batch.

---

## Part B — CGF1-S0202: DSM handler wiring

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0202](../CGF-1-TASK-DETAIL.md#cgf1-s0202--dsm-handler-wiring)  
**Design:** [CGF-1-DESIGN.md §4.2](../CGF-1-DESIGN.md#42-stage-22--dsm-handler-wiring)

Implement **`DsmStateChangedEvent`**, extend **`DrillSlave.Tick()`**, stub **`LiveLoadDsmHandler`**, register in **`SimHostApp`**, **duplicate `TransactionId` drop**, **`FDP/` grep audit**, and **all** success-condition tests in the task detail (**behavior-level**, not log substring tests).

---

## Success criteria

- [ ] Part A: design §4.1 fixed; planner payload tests; `_currentDsmState` rule implemented and documented.  
- [ ] Part B: CGF1-S0202 success conditions met.  
- [ ] Solution build clean; tests green.  
- [ ] DEBT-TRACKER updated.  
- [ ] Report filed.  

---

## Reference

- [CGF-1-BATCH-04 review Issues](../reviews/CGF-1-BATCH-04-REVIEW.md#issue-1-drillmaster_currentdsmstate-never-advances-p2)  

**Next preview:** **CGF-1-BATCH-06** — **CGF1-S0203** (time strategy proxying) after S0202 CI green.
