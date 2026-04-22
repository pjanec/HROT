# CGF-1-BATCH-18 Review

**Batch:** CGF-1-BATCH-18  
**Reviewer:** Development Lead  
**Date:** 2026-03-29  
**Status:** **CONDITIONALLY APPROVED** — **SimHost** dispatch (A.1), deferred **`PrepareAsync`/`Commit`** (A.3 on **SimHost** `ClusterSlave`), **`NodeBootstrapperReplayTests.ClusterSlaveDispatch_PrepareLiveWithActiveReplay_RoutesToReplayBranch`**, and **`FullBranchPipelineTests`** match the report and **source**. **CGF A.2** introduces a **serious regression**: **`FailLoudRecordReplayStub`** is registered **before** **`ScenarioLoadDsmHandler`** and **`CanHandle(PrepareLive)`** is **unconditional**, so **`ScenarioLoadDsmHandler` never runs for `PrepareLive`** on the CGF node (single-handler dispatch). **`Hrot.CGF/ClusterSlave`** was **not** updated with **`_pendingPrepare`** — **`PrepareAsync` is still fire-and-forget** there.

**Report:** [CGF-1-BATCH-18-REPORT.md](../reports/CGF-1-BATCH-18-REPORT.md) — verified against [CGF-1-BATCH-18-INSTRUCTIONS.md](../batches/CGF-1-BATCH-18-INSTRUCTIONS.md), [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0305.

---

## Summary — SimHost (APPROVED)

- **`ReplayLoadDsmHandler.CanHandle`**: **`PrepareLive`** only when **`_controller.ActiveReplayModule != null`** ([`ReplayLoadDsmHandler.cs`](../../../Hrot.SimHost/Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs)).
- **`NodeBootstrapper`**: **`ReplayLoadDsmHandler`** registered **before** **`LiveLoadDsmHandler`** ([`NodeBootstrapper.cs`](../../../Hrot.SimHost/NodeBootstrapper.cs)).
- **`ClusterSlave` (SimHost)**: **`_pendingPrepare`** defers **`Commit`** until **`PrepareAsync`** completes; faulted prepare logs **Error** and skips **`Commit`** ([`ClusterSlave.cs`](../../../Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs)).
- **`ClusterSlaveDispatch_PrepareLiveWithActiveReplay_RoutesToReplayBranch`**: uses **`ClusterSlave(eventBus)`**, correct handler order, **`EnqueueCommandForTest`**, multi-**`Tick`** loop until replay torn down and recording active — **satisfies** the instruction to exercise **real** slave dispatch (not only a bare handler).

**`FullBranchPipelineTests`:** Implements record → seek frame 50 → branch → branched record → **`RecordingReader`** frame 0 vs snapshot; timing via **`Task.Delay`** (report notes flakiness risk — acceptable with long timeout). The **branch step** still calls **`ReplayLoadDsmHandler.PrepareAsync`/`Commit` directly**; it does **not** exercise **SimHost** **`ClusterSlave`’s** deferred **`Commit`** path (that coverage is in **`NodeBootstrapperReplayTests`**).

**Tests:** `dotnet test Hrot.SimHost.Tests` **failed in this environment** (file lock on `Fhsm.SourceGen.dll`). Report’s **387/387** accepted; logic review above is from **source**.

---

## Summary — CGF (regression + parity gap)

### `PrepareLive` / scenario load

[`CgfApplication`](../../../Hrot.CGF/CgfApplication.cs) registers **`FailLoudRecordReplayStub`** **before** **`ScenarioLoadDsmHandler`**. The stub’s **`CanHandle`** returns **true for every `PrepareLive`** ([`FailLoudRecordReplayStub.cs`](../../../Hrot.CGF/Modules/Orchestration/Handlers/FailLoudRecordReplayStub.cs)). **[`Hrot.CGF/Modules/Orchestration/ClusterSlave`](../../../Hrot.CGF/Modules/Orchestration/ClusterSlave.cs)** dispatches **one** handler per op — **`ScenarioLoadDsmHandler.PrepareAsync` never runs** for **`PrepareLive`**, so **CGF scenario header-peek on load is effectively disabled** whenever the stub is registered. The XML comment in **`CgfApplication`** acknowledges only one handler runs; it does **not** restore scenario behaviour.

[`ScenarioLoadDsmHandler`](../../../Hrot.CGF/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs) **`HasExerciseId`** guard only helps if that handler is **invoked** (e.g. registration order fixed + **narrow stub `CanHandle`**).

### `ClusterSlave` async ordering

**A.3** was applied to **SimHost** **`ClusterSlave` only**. **[`Hrot.CGF/ClusterSlave`](../../../Hrot.CGF/Modules/Orchestration/ClusterSlave.cs)** still uses **`_ = handler.PrepareAsync`** then immediate **`Commit`** (lines 103–105) — **same race** the batch fixed on SimHost if CGF ever gains async DSM handlers.

---

## Verdict vs instructions

| Item | Verdict |
|------|---------|
| A.1 SimHost | **Met** |
| A.2 CGF branch visibility | **Partially** — branch hits stub, but **normal `PrepareLive`/scenario path broken** |
| A.3 `ClusterSlave` | **Met for SimHost only** |
| A.4 DEBT | Rows 168–171 marked ✅ — **add new debt** for CGF regression + CGF `ClusterSlave` |
| Part B `FullBranchPipelineTests` | **Met** (with timing flakiness note; optional **`RecordingConfiguration.Blocking`** later) |

---

## Suggested commit message

```
fix(cgf-1): BATCH-18 PrepareLive routing, SimHost ClusterSlave async prepare, FullBranch test

- ReplayLoadDsmHandler: PrepareLive only when ActiveReplayModule set; register before LiveLoad
- SimHost ClusterSlave: defer Commit until PrepareAsync completes (_pendingPrepare)
- FullBranchPipelineTests: branched .fdp frame 0 matches post-seek snapshot
- CGF: stub before ScenarioLoad + ScenarioLoad ExerciseId guard (see review: CGF PrepareLive)

Follow-up: narrow FailLoudRecordReplayStub CanHandle(PrepareLive) or reorder so ScenarioLoad
runs for ScenarioId payloads; align CGF ClusterSlave with SimHost prepare/commit ordering.
```

---

## Next batch

**[CGF-1-BATCH-19](../batches/CGF-1-BATCH-19-INSTRUCTIONS.md)** — **Part A (debt):** fix **CGF `PrepareLive`** so **`ScenarioLoadDsmHandler`** runs for **scenario** payloads and **stub** (or branch-only handler) runs for **ExerciseId-only** branch; port **`_pendingPrepare`** (or equivalent) to **`Hrot.CGF/ClusterSlave`**. **Part B:** begin **CGF1-S0308** or add **`FullBranchPipeline`** step through **`ClusterSlave`** for full stack confidence.
