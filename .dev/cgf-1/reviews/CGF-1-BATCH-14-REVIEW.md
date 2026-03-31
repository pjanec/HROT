# CGF-1-BATCH-14 Review

**Batch:** CGF-1-BATCH-14  
**Reviewer:** Development Lead  
**Date:** 2026-03-28  
**Status:** **CONDITIONALLY APPROVED** — Part A prefetch/gateway/edit-load work is **sound**; Part B **checkpoint core + tests** are **strong**, but **production SimHost does not register checkpoint plumbing** (same class of gap as pre-BATCH-13 `ScenarioSerializer` wiring).

**Report:** [CGF-1-BATCH-14-REPORT.md](../reports/CGF-1-BATCH-14-REPORT.md) — verified against **source**, [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0302 / §CGF1-S0303, [CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.2 / §5.3, and [CGF-1-BATCH-14-INSTRUCTIONS.md](../batches/CGF-1-BATCH-14-INSTRUCTIONS.md).

---

## Summary

**Part A** matches the report: `_pendingPrefetch` + `DrainPendingPrefetch()` at the start of `Tick()` defers `PrefetchFiles` until `PrefetchScenarioAsync` completes; faults and `FailureCount > 0` publish `ClusterOpStatus.Failure` with the originating `RequestId`; missing NAS `sourceDir` throws `DirectoryNotFoundException`; `EditLoadDsmHandler` throws when a pending DOM requires a repo but both parameters are null; `EditLoadDsmHandlerTests` assert position round-trip; §CGF1-S0302 task text updated toward the ScenarioSerializer DOM.

**Part B** delivers `CheckpointIOWorker` (dedicated thread, LZ4 via `RecorderSystem.RecordKeyframe`, `TakeCompletedResults`), `ITickableDsmHandler`, `CheckpointDsmHandler` (InProgress → enqueue → deferred Success/Failure), `ClusterSlave.Tick()` polling, and `LiveLoadDsmHandler` optional `DrainAsync` on `FinalizeLive`. Unit tests cover drain, overlap, live unload barrier, and null repo.

**Tests run (review):** `Hrot.Orchestrator.Tests` — **25 / 25** passed; `Hrot.SimHost.Tests` — **371 / 371** passed.

---

## Verdict vs instructions

| Area | Assessment |
|------|------------|
| **A.1 Prefetch ordering** | **Met** for **PrefetchFiles vs gateway copy**. `PrefetchFiles` is not sent until the gateway task completes successfully. |
| **A.2 Gateway fail-loud (missing dir)** | **Met.** |
| **A.3–A.4 EditLoad + TASK-DETAIL** | **Met.** |
| **A.5 DEBT** | Rows targeting BATCH-14 are marked closed in `DEBT-TRACKER.md` (verified). |
| **B S0303 implementation** | **Partial vs “application wiring” expectation.** Classes and tests exist; **bootstrap does not hook them up.** |
| **Design §5.3** | **Aligned** for the **three-step protocol** and reuse of the recorder keyframe pipeline in Fdp.Kernel. |

---

## Critical gap: checkpoint path not registered in production SimHost

[`NodeBootstrapper.BuildOrchestration`](../../../Hrot.SimHost/NodeBootstrapper.cs) does **not** create a `CheckpointIOWorker`, does **not** register `CheckpointDsmHandler`, and constructs [`LiveLoadDsmHandler(drillSlave, eventBus)`](../../../Hrot.SimHost/NodeBootstrapper.cs) **without** the optional `CheckpointIOWorker` ([`SimHostApp.OnLoad`](../../../Hrot.SimHost/SimHostApp.cs) uses the same call pattern).

Effects:

- **`TakeSnapshot`** from the orchestrator will **not** be handled** by SimHost production nodes (no handler).
- **`FinalizeLive`** will **not** await checkpoint drain in production (`_checkpointWorker` is always null).

This mirrors the historical **ScenarioLoad** wiring gap: **tests prove components**, **Runner/SimHost does not exercise them end-to-end**.

**Recommendation:** Treat **wiring + disposal** of `CheckpointIOWorker` as **P2 debt**, fixed in **CGF-1-BATCH-15 Part A** before or together with **CGF1-S0309** (task detail already assumes checkpoint registration exists).

---

## Additional notes (P3 / polish)

- **Optimistic `_currentClusterState`:** Still advanced in `ProcessClusterOpRequests` **before** prefetch completes on a later tick. **PrefetchFiles** ordering is fixed; **orchestrator-local DSM cursor** can still read “ahead” of staging — document or tighten if clients rely on it for sequencing.
- **Empty NAS scenario directory:** `PrefetchScenarioAsync` returns `SuccessCount = 0`, `FailureCount = 0`; `DrainPendingPrefetch` treats that as **success** and fans `PrefetchFiles` — **no files copied**. Consider failing or requiring `SuccessCount > 0` when a load transition implied non-empty scenario (policy choice).
- **§CGF1-S0303 success text** cites `OnItemWritten`; implementation uses **`TakeCompletedResults`** — tests still validate deferred behaviour; **align task-detail wording** opportunistically.
- **`SecondSnapshotCaptures_DifferentState_thanFirst`:** Uses **file size** proxy rather than deserializing both checkpoints to assert component values — weaker than the task-detail narrative but acceptable as a smoke test.

---

## Suggested commit message

```
fix(cgf-1): prefetch latch, gateway fail-loud, checkpoint worker, and edit-load hardening

- ClusterMaster: pending prefetch op; drain before SysOp processing; Failure on gateway fault
- StorageGateway: DirectoryNotFoundException when NAS scenario dir missing
- EditLoadDsmHandler: throw when deserialize required but repo/world null
- CheckpointIOWorker + CheckpointDsmHandler + ITickableDsmHandler; LiveLoad DrainAsync hook
- Tests: ClusterMaster prefetch ordering, checkpoint overlap/drain, edit-load positions
- TASK-DETAIL §CGF1-S0302: canonical ScenarioSerializer DOM

Follow-up: wire CheckpointIOWorker + CheckpointDsmHandler + LiveLoad(worker) in
NodeBootstrapper/SimHostApp (BATCH-15 Part A).
```

---

## Next batch

**[CGF-1-BATCH-15](batches/CGF-1-BATCH-15-INSTRUCTIONS.md)** — **Part A:** checkpoint **production wiring** (+ any prefetch empty-dir policy); **Part B:** **CGF1-S0309** Dry Run DSM handler (per §5.9 and task detail).
