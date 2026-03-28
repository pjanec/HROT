# CGF-1-BATCH-04: S0105 completion debt + Phase 2 start (CGF1-S0201)

**Batch number:** CGF-1-BATCH-04  
**Tasks:** **Part A — BATCH-03 review debt** → **CGF1-S0201** (BFS Transition Planner)  
**Phase:** Phase 1 closure + Phase 2 entry  
**Estimated effort:** 18–24 hours (~4–6 h Part A + ~14–18 h S0201)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-03](../reviews/CGF-1-BATCH-03-REVIEW.md) — APPROVED  

---

## Onboarding

1. [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §3.5 (ImGui), §4.1 (Transition planner)  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0105 (test norms), §CGF1-S0201  
4. [.dev/cgf-1/reviews/CGF-1-BATCH-03-REVIEW.md](../reviews/CGF-1-BATCH-03-REVIEW.md) — Issues 1–5  

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-04-REPORT.md`  

---

## Part A — Debt from BATCH-03 review (do first)

### A.0 — Verify test parallel policy (already landed)

**Lead added:** `FDP/Kernel/Fdp.Kernel.Tests/xunit.runner.json` and `FDP/ModuleHost/ModuleHost.Core.Tests/xunit.runner.json` with **`parallelizeAssembly: false`**, **`parallelizeTestCollections: false`**, **`maxParallelThreads: 1`**, copied to output via csproj.

**Your job:** Confirm full **`dotnet test IOS-IG-SimHost.sln`** is improved; **document** in the report if any DDS flake remains. **Rule:** new or existing tests that open **`DdsParticipant`** must use **non-conflicting domain IDs** when run under solution-wide parallelism — domain 0 is not exclusive.

### A.1 — `ClusterConfiguration.LoadFrom` fail-fast (P2)

**File:** `Bagira.Orchestrator/ClusterConfiguration.cs`  
**Requirement:** If **`orchestrator-config.json` exists** but JSON is invalid or unreadable → **throw** a clear exception (or fail startup in `OrchestratorSubsystem`) — **do not** silently fall back to **`Default`**. Missing file may still load **`Default`** if product wants zero-config dev; state the rule in XML and onboarding.

### A.2 — Align S0105 tests with task detail (P3)

**File:** `Bagira.Orchestrator.Tests/DrillMasterBootstrapTests.cs`  
Use **`SysOpType.TransitionState`** and a **`LoadingLive`** payload where the task detail specifies it (`RejectsCommands_*`, `TransactionHistory_*`), unless the task detail is explicitly revised in the same PR.

### A.3 — `SurvivingNodes_*` per-node assertion (P3)

Either add a **second participant** (SimHost node) with its own **`DdsReader<NodeOpCommand>`** and assert **no** `PrepareState` after ejection on that reader, **or** update **CGF-1-TASK-DETAIL** to describe the broadcast limitation and keep a weaker test with an explicit comment.

### A.4 — ImGui §3.5 completeness (P3)

**File:** `OrchestratorSubsystem.DrawUI`  
Add **CPU%** and **RAM** columns to the node health table (from **`NodeHeartbeat`** fields already on the wire). Extend **2PC history** UI to surface **`DistributedTransaction.NodeAckLatencyMs`** when populated, or populate latencies with placeholder **0** and document until real ACK timing exists.

### A.5 — Documentation hygiene (P3)

Update **CGF-1-DESIGN.md** file map and **CGF-1-TASK-DETAIL** §S0102/§S0104 to **remove** obsolete **`*.Standalone`** deliverables; state **Runner-only** launch.

---

## Part B — CGF1-S0201: BFS Transition Planner

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0201](../CGF-1-TASK-DETAIL.md#cgf1-s0201--bfs-transition-planner)  
**Design:** [CGF-1-DESIGN.md §4.1](../CGF-1-DESIGN.md#41-stage-21--bfs-transition-planner)

Implement **`TransitionPlanner`** in `Bagira.Orchestrator`, adjacency per design, **`CalculateShortestPath`**, **`PlanTrajectory`**, invalid path → **`InvalidOperationException`** before DDS, wire **`DrillMaster`** to call planner when processing **`SysOpRequest`** (replace placeholder **`TargetDsmState`** logic as specified).

**Tests:** `TransitionPlannerTests` in **`Bagira.Orchestrator.Tests`** — all scenarios in the task detail (exact step sequences, seek step, impossible path, same-state empty queue).

---

## Success criteria

- [ ] Part A issues from BATCH-03 review addressed or explicitly deferred with new DEBT rows.  
- [ ] CGF1-S0201 success conditions met; solution tests green.  
- [ ] `.dev/DEBT-TRACKER.md` updated.  
- [ ] Report filed.  

---

## Reference

- [CGF-1-BATCH-03 review Issues](../reviews/CGF-1-BATCH-03-REVIEW.md#issue-1-clusterconfigurationloadfrom-swallows-errors-silent-config-failure)  
- **DDS testing:** non-parallel **assembly** config does not replace **unique domain IDs** across concurrent test hosts.

**Next preview:** CGF-1-BATCH-05 — **CGF1-S0202** (DSM handler wiring) building on planner + real 2PC stepping.
