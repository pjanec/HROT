# CGF-1-BATCH-03 Review

**Batch:** CGF-1-BATCH-03  
**Reviewer:** Development Lead  
**Date:** 2026-03-29  
**Status:** APPROVED (with documented gaps below)

---

## Summary

Part A matches policy: **Standalone** projects removed from the solution and tree; **IG** no longer catches DDS init into “offline”; **SimHost** throws from **`EnsureIdAllocatorRouting`** when no allocator match; **`LocalIdAllocatorFallbackHost`** and fallback config removed; **`DrillSlave()`** internal + **`BuildOrchestration`** throws for Brain/AllInOne without participant; **`OrchestrationSchemaTests`** no longer uses `Contains('_')`. **CGF1-S0105** is implemented in **`DrillMaster`** (bootstrap latch, ejection, **`SysOpRequest`/`SysOpStatus`**, history ring, **`EjectNode(int)`**), **`ClusterConfiguration`**, **`OrchestratorSubsystem` ImGui**, and **four** new tests. **CycloneDDS.CodeGen** `@value` emission and **`NodeOpCommand` KeepAll** are real fixes aligned with runtime behavior.

---

## Issues found

### Issue 1: `ClusterConfiguration.LoadFrom` swallows errors (silent config failure)

**File:** `Bagira.Orchestrator/ClusterConfiguration.cs`  
**Problem:** `LoadFrom` catches all exceptions and returns **`Default`**; corrupt JSON or IO failure yields **no** loud failure — contradicts fail-fast posture for orchestrator config.  
**Target:** **CGF-1-BATCH-04** — throw or log fatal when `orchestrator-config.json` is present but invalid; only default when file is **missing** (if that remains desired).

### Issue 2: S0105 tests vs task detail (operation type)

**File:** `Bagira.Orchestrator.Tests/DrillMasterBootstrapTests.cs`  
**Problem:** Success conditions in **CGF-1-TASK-DETAIL** name **`SysOpRequest(TransitionState, LoadingLive)`**; tests use **`SysOpType.PauseTime`**. Behavior under test (reject / accept / history) is still exercised, but **normative alignment** with the task doc is missing.  
**Target:** **CGF-1-BATCH-04** — switch requests to **`TransitionState`** with payload consistent with planner expectations (or update task detail if PauseTime is intentionally interim).

### Issue 3: `SurvivingNodes_CommandedToStandby_AfterEjection` — per-node delivery

**Problem:** Task requires **SimHost does not receive** commands after ejection; the test uses a **single** `DdsReader<NodeOpCommand>` on the orchestrator participant and asserts command **types** exist. That does **not** prove the ejected node’s reader sees zero samples (DDS broadcast semantics).  
**Target:** **CGF-1-BATCH-04** — add a second participant acting as SimHost reader, or document the limitation and tighten when per-node topics exist.

### Issue 4: ImGui health / history vs design §3.5

**File:** `Bagira.Runner/Services/OrchestratorSubsystem.cs`  
**Problem:** Design calls for **CPU%** and **RAM** in the health table and **per-node ACK latency** in the 2PC history UI. Current tables show **NodeId, Subsystem, ms ago, DSM** and **Tx id / target / aborted** — **CPU/RAM and ACK latency columns are missing**. **`NodeAckLatencyMs`** on **`DistributedTransaction`** is never populated.

### Issue 5: Stale docs — Standalone projects

**Files:** `.dev/cgf-1/CGF-1-DESIGN.md` (file map), **CGF-1-TASK-DETAIL** (S0102/S0104 still mention `.Standalone` deliverables).  
**Target:** **CGF-1-BATCH-04** — align docs with Runner-only reality.

### Issue 6: `ProcessSysOpRequests` / transaction semantics (expected stub)

**File:** `Bagira.Orchestrator/DrillMaster.cs`  
**Note:** Accepted requests append history and reply **`InProgress`**; no full 2PC or **`Success`** yet — acceptable **Phase 1.5** stub until **S0201/S0202**. **`BroadcastNodeOp`** documents single-writer broadcast — OK for current DDS model.

---

## Test quality

| Test | Assessment |
|------|------------|
| `RejectsCommands_UntilMandatoryNodesReady` | Strong: real **`OpStatus.Rejected`**, latch, then non-rejected response. |
| `EjectsMandatoryNode_EntersDegraded` | Strong: **`Degraded`** on **`SystemStateTopic`**, latch reset. |
| `SurvivingNodes_CommandedToStandby_AfterEjection` | Good for roster + command **presence**; weak on **“SimHost does not receive”** (Issue 3). |
| `TransactionHistory_RecordsCompletedTransaction` | Good for **`IsAborted`** and **`OriginRequestId`**; operation type mismatch (Issue 2). |
| `OrchestratorPublishesStandbyOnStartup` | Still valid with **`ClusterConfiguration.Default`** (empty mandatory → immediate **`PublishStandby`**). |

---

## Design alignment

- **§3.5** bootstrap latch, mandatory names, timeout ejection, **`Degraded`**, broadcasts, history buffer — **core behavior** present.  
- **ImGui** subset incomplete vs written spec (CPU/RAM, ACK latency).  
- **Docs** still reference removed Standalone entry points.

---

## Infra (lead)

**`Fdp.Tests`** and **`ModuleHost.Core.Tests`:** added **`xunit.runner.json`** (`parallelizeAssembly` / `parallelizeTestCollections` **false**, **`maxParallelThreads`: 1**) and **CopyToOutputDirectory** in csproj to reduce **DDS domain-0 collisions** under high solution-wide parallelism, per stakeholder direction. Residual cross-assembly contention still requires **distinct domain IDs** for any new DDS tests.

---

## Verdict

**APPROVED.** Close **CGF1-S0105** on the tracker; track **UI completeness**, **strict config load**, **test/doc alignment**, and **per-node command assertion** in **CGF-1-BATCH-04**.

---

## Commit message

```
feat(cgf-1): fail-fast DDS, remove allocator fallback + Standalone, CGF1-S0105 (CGF-1-BATCH-03)

Completes CGF1-S0105 and Part A policy work from BATCH-02 review.

- Remove Orchestrator/CGF Standalone projects; Runner-only orchestrator/CGF.
- IgApplication: fail-fast on DDS init when network enabled (no offline catch).
- SimHost: EnsureIdAllocatorRouting throws without allocator match; delete local fallback.
- DrillMaster: ClusterConfiguration, bootstrap latch, SysOpRequest/Status, EjectNode(int),
  degraded path, transaction history, NodeOpCommand KeepAll QoS.
- OrchestratorSubsystem: orchestrator-config.json, ImGui banner / health / history tables.
- CycloneDDS.CodeGen: @value for non-sequential enum values (DSMState wire fix).
- Tests: four S0105 scenarios + DrillMaster fixtures in SimHost/Runner tests; IG domain IDs.

Test infra: Fdp.Tests + ModuleHost.Core.Tests xunit.runner.json (non-parallel assembly).

Related: CGF-1-DESIGN §3.5, CGF-1-TASK-DETAIL §CGF1-S0105.
```

---

**Next batch:** [CGF-1-BATCH-04](../batches/CGF-1-BATCH-04-INSTRUCTIONS.md)
