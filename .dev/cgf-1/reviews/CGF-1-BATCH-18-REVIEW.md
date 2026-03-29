# CGF-1-BATCH-18 Review

**Batch:** CGF-1-BATCH-18  
**Reviewer:** Development Lead  
**Date:** 2026-03-29  
**Status:** **CONDITIONALLY APPROVED** — **SimHost** dispatch (A.1), deferred **`PrepareAsync`/`Commit`** (A.3 on **SimHost** `DrillSlave`), **`NodeBootstrapperReplayTests.DrillSlaveDispatch_PrepareLiveWithActiveReplay_RoutesToReplayBranch`**, and **`FullBranchPipelineTests`** match the report and **source**. **CGF A.2** introduces a **serious regression**: **`FailLoudRecordReplayStub`** is registered **before** **`ScenarioLoadDsmHandler`** and **`CanHandle(PrepareLive)`** is **unconditional**, so **`ScenarioLoadDsmHandler` never runs for `PrepareLive`** on the CGF node (single-handler dispatch). **`Bagira.CGF/DrillSlave`** was **not** updated with **`_pendingPrepare`** — **`PrepareAsync` is still fire-and-forget** there.

**Report:** [CGF-1-BATCH-18-REPORT.md](../reports/CGF-1-BATCH-18-REPORT.md) — verified against [CGF-1-BATCH-18-INSTRUCTIONS.md](../batches/CGF-1-BATCH-18-INSTRUCTIONS.md), [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0305.

---

## Summary — SimHost (APPROVED)

- **`ReplayLoadDsmHandler.CanHandle`**: **`PrepareLive`** only when **`_controller.ActiveReplayModule != null`** ([`ReplayLoadDsmHandler.cs`](../../../Bagira.SimHost/Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs)).
- **`NodeBootstrapper`**: **`ReplayLoadDsmHandler`** registered **before** **`LiveLoadDsmHandler`** ([`NodeBootstrapper.cs`](../../../Bagira.SimHost/NodeBootstrapper.cs)).
- **`DrillSlave` (SimHost)**: **`_pendingPrepare`** defers **`Commit`** until **`PrepareAsync`** completes; faulted prepare logs **Error** and skips **`Commit`** ([`DrillSlave.cs`](../../../Bagira.SimHost/Modules/Orchestration/DrillSlave.cs)).
- **`DrillSlaveDispatch_PrepareLiveWithActiveReplay_RoutesToReplayBranch`**: uses **`DrillSlave(eventBus)`**, correct handler order, **`EnqueueCommandForTest`**, multi-**`Tick`** loop until replay torn down and recording active — **satisfies** the instruction to exercise **real** slave dispatch (not only a bare handler).

**`FullBranchPipelineTests`:** Implements record → seek frame 50 → branch → branched record → **`RecordingReader`** frame 0 vs snapshot; timing via **`Task.Delay`** (report notes flakiness risk — acceptable with long timeout). The **branch step** still calls **`ReplayLoadDsmHandler.PrepareAsync`/`Commit` directly**; it does **not** exercise **SimHost** **`DrillSlave`’s** deferred **`Commit`** path (that coverage is in **`NodeBootstrapperReplayTests`**).

**Tests:** `dotnet test Bagira.SimHost.Tests` **failed in this environment** (file lock on `Fhsm.SourceGen.dll`). Report’s **387/387** accepted; logic review above is from **source**.

---

## Summary — CGF (regression + parity gap)

### `PrepareLive` / scenario load

[`CgfApplication`](../../../Bagira.CGF/CgfApplication.cs) registers **`FailLoudRecordReplayStub`** **before** **`ScenarioLoadDsmHandler`**. The stub’s **`CanHandle`** returns **true for every `PrepareLive`** ([`FailLoudRecordReplayStub.cs`](../../../Bagira.CGF/Modules/Orchestration/Handlers/FailLoudRecordReplayStub.cs)). **[`Bagira.CGF/Modules/Orchestration/DrillSlave`](../../../Bagira.CGF/Modules/Orchestration/DrillSlave.cs)** dispatches **one** handler per op — **`ScenarioLoadDsmHandler.PrepareAsync` never runs** for **`PrepareLive`**, so **CGF scenario header-peek on load is effectively disabled** whenever the stub is registered. The XML comment in **`CgfApplication`** acknowledges only one handler runs; it does **not** restore scenario behaviour.

[`ScenarioLoadDsmHandler`](../../../Bagira.CGF/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs) **`HasDrillId`** guard only helps if that handler is **invoked** (e.g. registration order fixed + **narrow stub `CanHandle`**).

### `DrillSlave` async ordering

**A.3** was applied to **SimHost** **`DrillSlave` only**. **[`Bagira.CGF/DrillSlave`](../../../Bagira.CGF/Modules/Orchestration/DrillSlave.cs)** still uses **`_ = handler.PrepareAsync`** then immediate **`Commit`** (lines 103–105) — **same race** the batch fixed on SimHost if CGF ever gains async DSM handlers.

---

## Verdict vs instructions

| Item | Verdict |
|------|---------|
| A.1 SimHost | **Met** |
| A.2 CGF branch visibility | **Partially** — branch hits stub, but **normal `PrepareLive`/scenario path broken** |
| A.3 `DrillSlave` | **Met for SimHost only** |
| A.4 DEBT | Rows 168–171 marked ✅ — **add new debt** for CGF regression + CGF `DrillSlave` |
| Part B `FullBranchPipelineTests` | **Met** (with timing flakiness note; optional **`RecordingConfiguration.Blocking`** later) |

---

## Suggested commit message

```
fix(cgf-1): BATCH-18 PrepareLive routing, SimHost DrillSlave async prepare, FullBranch test

- ReplayLoadDsmHandler: PrepareLive only when ActiveReplayModule set; register before LiveLoad
- SimHost DrillSlave: defer Commit until PrepareAsync completes (_pendingPrepare)
- FullBranchPipelineTests: branched .fdp frame 0 matches post-seek snapshot
- CGF: stub before ScenarioLoad + ScenarioLoad DrillId guard (see review: CGF PrepareLive)

Follow-up: narrow FailLoudRecordReplayStub CanHandle(PrepareLive) or reorder so ScenarioLoad
runs for ScenarioId payloads; align CGF DrillSlave with SimHost prepare/commit ordering.
```

---

## Next batch

**[CGF-1-BATCH-19](../batches/CGF-1-BATCH-19-INSTRUCTIONS.md)** — **Part A (debt):** fix **CGF `PrepareLive`** so **`ScenarioLoadDsmHandler`** runs for **scenario** payloads and **stub** (or branch-only handler) runs for **DrillId-only** branch; port **`_pendingPrepare`** (or equivalent) to **`Bagira.CGF/DrillSlave`**. **Part B:** begin **CGF1-S0308** or add **`FullBranchPipeline`** step through **`DrillSlave`** for full stack confidence.
