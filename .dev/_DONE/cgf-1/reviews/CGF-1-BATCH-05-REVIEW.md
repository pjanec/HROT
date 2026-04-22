# CGF-1-BATCH-05 Review

**Batch:** CGF-1-BATCH-05  
**Reviewer:** Development Lead  
**Date:** 2026-03-29  
**Status:** APPROVED (with follow-ups scheduled for BATCH-06)

**Note:** No developer report was filed for this batch; this review is based on **source inspection** and **`dotnet test`** on the affected assemblies.

---

## Summary

**Part A (planner / orchestrator debt)** is **delivered**: **CGF-1-DESIGN §4.1** adjacency prose matches **`TransitionPlanner`** (no direct **RunningEdit → LoadingLive**; footnote ¹ explains why). **`PlanTrajectory`** fails fast on empty/whitespace, invalid JSON, and JSON missing **`TargetState`**, with **three** focused unit tests. **`ClusterMaster`** advances **`_currentClusterState`** optimistically after an accepted **TransitionState** plan; limitation is documented in XML; **`ClusterMasterBootstrapTests.CurrentClusterState_AdvancesOptimistically_AfterAcceptedTransition`** exercises the behavior.

**Part B (CGF1-S0202)** is **substantively delivered**: **`ClusterStateChangedEvent`** lives in **`Hrot.Common`** (not under **`FDP/`** — verified by grep and **`ClusterSlaveHandlerTests.ClusterStateChangedEvent_IsNotInFdpNamespace`**). **`ClusterSlave`** publishes the event on **`CommitState`** when **`PayloadJson`** parses as an integer DSM enum; **duplicate `TransactionId`** commands are dropped via **`HashSet<Guid>`**. **`LiveLoadDsmHandler`** stub handles **PrepareLive** / **FinalizeLive** and registers from **`NodeBootstrapper.BuildOrchestration`** when an **`FdpEventBus`** is supplied ( **`SimHostApp.OnLoad`** passes **`_eventBus`** — satisfies the intent of “wire at app startup” even though registration is centralized in the bootstrapper).

**Tests run:** `Hrot.Orchestrator.Tests` (17 passed), `Hrot.SimHost.Tests` (363 passed).

---

## Tasks vs instructions

| Item | Verdict |
|------|---------|
| **A.1** Design §4.1 | **Done** — list + footnote align with code and normative trajectories. |
| **A.2** Payload fail-fast | **Done** — **`string.IsNullOrWhiteSpace`**, JSON parse + **`TargetState`** required; tests for empty, garbage, `{}`. |
| **A.3** **`_currentClusterState`** | **Done** — optimistic advance documented; integration-style test in **`ClusterMasterBootstrapTests`**. |
| **A.4** Payload protocol | **Done** — XML on **`PlanTrajectory`** documents int (compat) vs JSON object (preferred). |
| **A.5** DEBT closure | **Partial** — several BATCH-05 targets are ✅ in **DEBT-TRACKER**; one row (per-node **`NodeOpCommand`** isolation) was **not** in scope for code changes and should roll forward (see debt tracker). |
| **B** CGF1-S0202 | **Done** with caveats below (heartbeat field, handler stub semantics, 2PC still stub). |

---

## Issues found

### Issue 1: **`NodeHeartbeat.LocalClusterState` stuck at `Standby`** (P2)

**File:** `Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs` — **`PublishHeartbeat()`** always sets **`LocalClusterState = ClusterState.Standby`** while **`_localClusterState`** is updated on **`CommitState`**. The orchestrator and roster therefore see a **false** local DSM after commits. This contradicts the control-plane story (bootstrap latch and health already care about DSM). **Target:** **CGF-1-BATCH-06** (first).

### Issue 2: **`LiveLoadDsmHandler.Commit`** is a minimal stub (P3 / known deferral)

**File:** `LiveLoadDsmHandler.cs` — **`Commit`** always publishes **`Standby → LoadingLive`** and does not implement “only if not already published” beyond a comment; **`PrepareAsync` + `Commit`** on the slave still **fire without await** (pre-existing BATCH-02 debt, still not real 2PC). Acceptable for **Phase 2.0** milestone; track until **CGF1-S0304** / real prepare-commit sequencing.

### Issue 3: Test hygiene (P3)

**File:** `Hrot.SimHost.Tests/ClusterSlaveHandlerTests.cs` — test name **`CommitState_RaisesEsmStateChangedEvent`** typo (**Esm** vs **Dsm**). Behavior assertions are correct (payload uses **`((int)ClusterState.LoadingLive).ToString()`**, matching **`CommitState`** parsing).

### Issue 4: Instruction wording vs implementation (informational)

Task detail mentions registering the handler in **`SimHostApp.OnLoad()`**; actual registration is **`NodeBootstrapper.BuildOrchestration`** when **`eventBus != null`**. **`SimHostApp`** supplies the bus — **no gap** in behavior.

---

## Test quality

| Area | Verdict |
|------|---------|
| **`TransitionPlannerTests`** (A.2) | **Strong** — asserts **`InvalidOperationException`** for empty, garbage JSON, and missing **`TargetState`**; messages are sanity-checked where useful. |
| **`ClusterSlaveHandlerTests`** | **Good for milestone** — **`CommitState`** path, deduplication, and FDP-namespace guard are **behavior-level**. Does **not** exercise **PrepareLive** / **FinalizeLive** through the handler loop (optional hardening). |
| **`ClusterMasterBootstrapTests`** | **Strong** — optimistic **`_currentClusterState`** is validated via a **second** planned transition that would fail if **`current`** stayed **`Standby`**. |

---

## Design alignment

- **§4.1** planner graph, examples, and doc footnote match **`TransitionPlanner`**.
- **§4.2** event placement in Hrot layer and **`FdpEventBus`** publication after **`CommitState`** match implementation.
- **§4.2** “domain systems subscribe without DDS” is **unblocked** once subscribers exist; **Issue 1** should be fixed before relying on **heartbeat** for DSM truth.

---

## Verdict

**APPROVED.** **CGF1-S0202** and **Part A** batch goals are met; schedule **heartbeat `LocalClusterState`** and remaining **2PC** / handler realism under **CGF-1-BATCH-06** and later CGF tasks.

---

## Suggested commit message

```
feat(cgf-1): BATCH-05 planner strictness, optimistic DSM cursor, S0202 wiring

- TransitionPlanner: fail-fast TransitionState payloads; document JSON vs int.
- ClusterMaster: advance _currentClusterState after accepted TransitionState plans.
- ClusterSlave: CommitState publishes ClusterStateChangedEvent; drop duplicate TransactionId.
- LiveLoadDsmHandler stub; NodeBootstrapper registers when event bus present.
- Design §4.1: remove RunningEdit→LoadingLive shortcut; add footnote.
- Tests: PlanTrajectory invalid payload cases; ClusterSlaveHandlerTests; optimistic DSM test.

Related: CGF-1-DESIGN §4.1–4.2, CGF1-S0202, CGF-1-BATCH-04 review follow-ups.
```

---

**Next batch:** [CGF-1-BATCH-06](../batches/CGF-1-BATCH-06-INSTRUCTIONS.md)
