# CGF-1-BATCH-15 Review

**Batch:** CGF-1-BATCH-15  
**Reviewer:** Development Lead  
**Date:** 2026-03-28  
**Status:** **APPROVED** — Part A and Part B match the batch instructions; remaining issues are **spec/test polish**, not functional reversals.

**Report:** [CGF-1-BATCH-15-REPORT.md](../reports/CGF-1-BATCH-15-REPORT.md) — verified against **source**, [CGF-1-BATCH-15-INSTRUCTIONS.md](../batches/CGF-1-BATCH-15-INSTRUCTIONS.md), [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0309, [CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.3 / §5.9.

---

## Summary

**Part A** closes the BATCH-14 wiring gap: [`SimHostApp.OnLoad`](../../../Hrot.SimHost/SimHostApp.cs) creates [`CheckpointIOWorker`](../../../FDP/Kernel/Fdp.Kernel/Orchestration/CheckpointIOWorker.cs) under a fixed checkpoint directory, passes it into [`NodeBootstrapper.BuildOrchestration`](../../../Hrot.SimHost/NodeBootstrapper.cs), which registers [`CheckpointDsmHandler`](../../../Hrot.SimHost/Modules/Orchestration/Handlers/CheckpointDsmHandler.cs) and [`LiveLoadDsmHandler(..., checkpointWorker)`](../../../Hrot.SimHost/Modules/Orchestration/LiveLoadDsmHandler.cs). [`Shutdown`](../../../Hrot.SimHost/SimHostApp.cs) disposes `ClusterSlave` before the worker, which is a sensible teardown order. **Empty NAS scenario directory** now throws in [`StorageGatewayModule.PrefetchScenarioAsync`](../../../Hrot.Orchestrator/StorageGatewayModule.cs) so `DrainPendingPrefetch` will surface failure instead of fanning empty staging.

**Part B** implements [`DryRunDsmHandler`](../../../Hrot.Common/Orchestration/Handlers/DryRunDsmHandler.cs) in **Hrot.Common** (as recommended in BATCH-15 instructions), registers it on SimHost (live `world`), CGF / IG / Runner `IosSubsystem` with `liveRepo: null`, and adds six focused unit tests. Rewind test correctly calls [`EntityRepository.Tick()`](../../../Hrot.SimHost.Tests/DryRunDsmHandlerTests.cs) before mutating so `SyncFrom` sees a version change — this matches real engine behaviour and is **better** than a naive test that would miss `SyncDirtyChunks` semantics.

**Tests run (review):** `Hrot.SimHost.Tests` — **377 / 377** passed (includes 6 `DryRunDsmHandler*` tests).

---

## Verdict vs instructions

| Area | Assessment |
|------|------------|
| **A.1 Checkpoint wiring** | **Met** for **SimHostApp** production path. Optional “bootstrap registration assertion” test was not added — acceptable; debt can track a narrow integration assert if desired. |
| **A.2 Empty prefetch dir** | **Met** — `InvalidOperationException` + test per report. |
| **A.3 DEBT** | **Met** — wiring + empty-dir rows marked ✅ in `DEBT-TRACKER.md`. |
| **B S0309 behaviour** | **Met** for snapshot, rewind, abort, no-op states, null snap unload (warn, no throw), null `liveRepo` on IG/CGF/IOS. |
| **Design §5.9** | **Aligned** — RAM-only `SyncFrom` / restore; no checkpoint worker; no `ITickableDsmHandler`. |

---

## Gaps (P3 — fail-loud / spec / tests)

1. **§CGF1-S0309 TASK-DETAIL** still names [`Hrot.SimHost/.../DryRunDsmHandler.cs`](../CGF-1-TASK-DETAIL.md) and **`SimPosition`** / **four entities** / **fifth entity removed** in success text. Implementation uses **`Hrot.Common`**, **`DryRunTestPos`**, and **`UnloadingDryRun_RewindsLiveRepo` does not add a 5th entity or assert `EntityCount == 4`**. That leaves **entity-spawn removal during dry run** unproven by tests even if `SyncFrom` likely restores the full index — **tighten test + update TASK-DETAIL** in a small follow-up.

2. **`ParseTargetState`** in `DryRunDsmHandler` uses the same **silent `catch` → `Standby`** pattern as [`EditLoadDsmHandler`](../../../Hrot.SimHost/Modules/Orchestration/Handlers/EditLoadDsmHandler.cs). Malformed `PrepareState` payloads can **no-op** dry-run acts — consistent with existing handlers but not “fail loud” for bad control-plane JSON.

3. **Checkpoint directory** is a **literal** `@"C:\FDP_Temp\checkpoints"` in `SimHostApp`, not derived from node config / `localTempRoot` — works on Windows dev boxes; **config debt** for multi-root or non-`C:` deployments.

4. **Class XML** on `DryRunDsmHandler` implies the handler helps **ACK** 2PC; ACKs are still **`ClusterSlave`**’s responsibility — minor documentation imprecision.

---

## Suggested commit message

```
feat(cgf-1): wire checkpoint worker in SimHost, empty prefetch guard, dry-run handler

- SimHostApp: CheckpointIOWorker lifecycle; NodeBootstrapper registers CheckpointDsmHandler
  and passes worker into LiveLoadDsmHandler
- StorageGateway: fail prefetch when NAS scenario directory has no files
- Hrot.Common: DryRunDsmHandler (LoadingDryRun snapshot / UnloadingDryRun restore)
- Register DryRun on SimHost, CGF, IG, IosSubsystem; SimHost.Tests coverage

Follow-up: align CGF1-S0309 TASK-DETAIL path + strengthen rewind test (entity count);
consider configurable checkpoint root.
```

---

## Next batch

**[CGF-1-BATCH-16](batches/CGF-1-BATCH-16-INSTRUCTIONS.md)** — **tech debt first** (S0309 spec/test + checkpoint path + optional S0303 task-detail wording), then **CGF1-S0304** (dynamic recording modules).
