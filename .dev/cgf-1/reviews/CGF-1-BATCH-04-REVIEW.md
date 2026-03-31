# CGF-1-BATCH-04 Review

**Batch:** CGF-1-BATCH-04  
**Reviewer:** Development Lead  
**Date:** 2026-03-29  
**Status:** APPROVED (with correctness follow-ups in BATCH-05)

---

## Summary

Part A matches the BATCH-03 review: **`ClusterConfiguration.LoadFrom`** fails fast when the file exists but read/JSON fails; **`ClusterMasterBootstrapTests`** use **`TransitionState`** with **`LoadingLive`** payload; ImGui adds **CPU %** / **RAM (MB)** and **ACK latency** column; **`NodeHealthProfile`** and **`IngestHeartbeats`** propagate heartbeat metrics; **Standalone** references removed from **CGF-1-DESIGN** / **CGF-1-TASK-DETAIL**; **S0105** broadcast limitation is documented in task detail. **CGF1-S0201** delivers **`TransitionPlanner`** with BFS, **`TransitionStep`** / **`OperationStep`**, **`PlanTrajectory`** (int + JSON payload), eight **`TransitionPlannerTests`**, and **`ClusterMaster.ProcessClusterOpRequests`** integration (planner on **`TransitionState`**, **`Failure`** on unreachable path). **CGF-1-DESIGN §4.1** adjacency text still lists **`RunningEdit → LoadingLive`** while the **code** (correctly) omits that edge to match normative trajectories — already flagged for doc fix.

---

## Issues found

### Issue 1: `ClusterMaster._currentClusterState` never advances (P2)

**File:** `Hrot.Orchestrator/ClusterMaster.cs`  
**Problem:** `_currentClusterState` stays **`Standby`** after construction. Every **`PlanTrajectory`** uses this as **`current`**, not the last published **`SystemStateTopic`** or last completed transition. Sequential **`TransitionState`** requests from a cluster that has moved (e.g. after future 2PC) will be **planned from the wrong source state**.  
**Target:** **CGF-1-BATCH-05** — define authoritative source (last written **`SystemStateTopic.CurrentState`**, **`NodeOpStatus`** aggregation, or optimistic update after successful command) and add tests.

### Issue 2: `PlanTrajectory` — malformed / empty payload (P2)

**File:** `Hrot.Orchestrator/TransitionPlanner.cs`  
**Problem:** Non-empty payload that fails **`JsonDocument.Parse`** falls through with **`targetState == Standby`** (default). Empty **`PayloadJson`** skips both branches and also defaults to **Standby**. That can produce a **valid-looking plan to Standby** from e.g. **`RunningLive`** for **garbage input** instead of throwing.  
**Target:** **CGF-1-BATCH-05** — for **`TransitionState`**, require a successful parse (int or JSON with **`TargetState`**); otherwise **`InvalidOperationException`**.

### Issue 3: Design doc vs code (P3 — already in debt)

**File:** `.dev/cgf-1/CGF-1-DESIGN.md` §4.1 line 493 — **`RunningEdit → … LoadingLive …`** contradicts **`TransitionPlanner`** and the **RunningEdit → RunningLive** four-step table row.  
**Target:** **CGF-1-BATCH-05** (DEBT row present).

### Issue 4: Naming hygiene (P3)

**File:** `TransitionPlanner.cs` — **`public abstract class ISysOpStep`** is easy to mistake for an interface. Low priority rename to e.g. **`SysOpStep`** in a later batch if it does not churn generated docs.

---

## Test quality

| Area | Verdict |
|------|---------|
| **TransitionPlannerTests** | **Strong:** exact path vectors, seek step, **`Degraded`** impossible source, **`Standby → Standby`** empty queue, extra **`RunningDryRun → RunningReplay`** documents design correction. Assertions are on **state sequences**, not strings. |
| **ClusterMaster bootstrap** | **Aligned** with task detail op type/payload for reject/accept phases. |
| **SurvivingNodes** | **Documented limitation** (broadcast); acceptable for Phase 1 with explicit task-detail note. |

---

## Design alignment

- **§4.1** planner behavior matches **example trajectories** in code; **adjacency list prose** still wrong for **RunningEdit** (Issue 3).  
- **§3.5** ImGui: CPU/RAM and ACK column present; real ACK values still **S0202**.

---

## Verdict

**APPROVED.** **CGF1-S0201** is **substantively delivered**; remaining gaps are **orchestrator current-state bookkeeping**, **strict payload validation**, and **design §4.1 text** — schedule **first** in **CGF-1-BATCH-05**, then **CGF1-S0202**.

---

## Commit message

```
feat(cgf-1): BATCH-04 debt closure + TransitionPlanner + ClusterMaster wiring (CGF1-S0201)

- ClusterConfiguration.LoadFrom: throw when file exists but invalid; missing file → Default.
- TransitionPlanner: BFS adjacency (no RunningEdit→LoadingLive), PlanTrajectory, steps.
- ClusterMaster: plan TransitionState requests; Failure on unreachable path; CPU/RAM in roster.
- OrchestratorSubsystem ImGui: 6-col health + ACK latency column.
- TransitionPlannerTests (8); ClusterMaster tests use TransitionState/LoadingLive.
- Docs: remove Standalone deliverables; S0105 broadcast note; S0201 impossible-case errata.

Related: CGF-1-DESIGN §4.1, CGF-1-TASK-DETAIL §CGF1-S0201.
```

---

**Next batch:** [CGF-1-BATCH-05](../batches/CGF-1-BATCH-05-INSTRUCTIONS.md)
