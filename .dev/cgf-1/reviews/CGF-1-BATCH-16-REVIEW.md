# CGF-1-BATCH-16 Review

**Batch:** CGF-1-BATCH-16  
**Reviewer:** Development Lead  
**Date:** 2026-03-28  
**Status:** **CONDITIONALLY APPROVED** — Part A is **complete**; Part B delivers **most** of CGF1-S0304 **in code and tests**, but **`ReplayLoadDsmHandler` is not wired into production `SimHostApp`** (same class of gap as pre-BATCH-15 checkpoint wiring). **`IRecordReplayController`** exists in **Fdp.Kernel** but **`EcsRecordReplayController` does not implement it**, and **`FinalizeRecordingAsync` signatures diverge**.

**Report:** [CGF-1-BATCH-16-REPORT.md](../reports/CGF-1-BATCH-16-REPORT.md) — verified against **source**, [CGF-1-BATCH-16-INSTRUCTIONS.md](../batches/CGF-1-BATCH-16-INSTRUCTIONS.md), [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0303 / §CGF1-S0304 / §CGF1-S0309, [CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.4.

---

## Summary

**Part A** matches the report: §S0309 path and `DryRunTestPos` in **TASK-DETAIL**; **`UnloadingDryRun_RewindsLiveRepo`** now covers **5th entity removal** and component revert; **`NodeConfiguration.LocalTempRoot`** drives `Path.Combine(..., "checkpoints")` and **`BuildOrchestration(..., localTempRoot: nodeConfig.LocalTempRoot)`**; §S0303 success text references **`TakeCompletedResults`**.

**Part B** adds **`IRecordReplayController`**, **`NetworkLifecycleSystemGroup`**, **`GhostCreationSystem.BypassLifecycle`**, **`ReplayLoadDsmHandler`**, **`RecordingModule.SetMaxNetworkId`**, **`AsyncRecorder` / `RecordingMetadata` / `PlaybackController`** plumbing, **`SeekToWallClockTicks`**, full **`LiveLoadDsmHandler`** calling **`EcsRecordReplayController`**, and the listed tests. **`RecorderSystem.EntityFilter`** and **wall-clock fields** are present in **Fdp.Kernel**; **`RecordingConfiguration`** lives in **`FDP.Toolkit.Replay`** (task text said **Fdp.Kernel** — acceptable layering if documented).

**Tests run (review):** `Hrot.SimHost.Tests` — **380 / 380** passed.

---

## Critical gap: `ReplayLoadDsmHandler` not registered in `SimHostApp`

[`NodeBootstrapper.BuildOrchestration`](../../../Hrot.SimHost/NodeBootstrapper.cs) registers **`ReplayLoadDsmHandler`** only when **`simGroup`**, **`lifecycleGroup`**, and **`ghostCreationSystem`** are all non-null **and** a **`EcsRecordReplayController`** exists.

[`SimHostApp.OnLoad`](../../../Hrot.SimHost/SimHostApp.cs) calls **`BuildOrchestration`** **before** constructing **`SimHostModule`** / **`GhostCreationSystem`** and does **not** pass the optional replay parameters (lines ~343–350). So in the **standalone SimHost** path, **`PrepareReplay` / `FinalizeReplay` are not handled** by any registered handler, even though **unit tests** construct the handler manually ([`ReplayLoadDsmHandlerTests`](../../../Hrot.SimHost.Tests/ReplayLoadDsmHandlerTests.cs)).

**Impact:** S0304 **replay load** is **not end-to-end** in the real app until bootstrap order is fixed (e.g. split orchestration build after network/sim objects exist, or register the handler in a second pass with resolved references).

---

## Additional gaps (P2 / P3)

| Topic | Finding |
|--------|---------|
| **`IRecordReplayController`** | Interface defines **`Task FinalizeRecordingAsync()`** with **no** `maxNetworkId`; **`EcsRecordReplayController`** exposes **`FinalizeRecordingAsync(long maxNetworkId = 0)`** and **does not** implement the interface — the contract is **orphaned** for polymorphic use. |
| **`LiveLoadDsmHandler.ParseExerciseId`** | Malformed JSON or bad **`ExerciseId`** → **silent `catch`** → **`Guid.NewGuid()`** — can start a recording under an **unintended** drill id instead of failing loud. |
| **`EcsRecordReplayController.FinalizeRecordingAsync`** | **`if (_activeRecordingModule == null) return;`** — **silent no-op** if **`FinalizeLive`** runs without a matching **`PrepareLive`** (may or may not be acceptable; worth logging **Warn** at minimum). |
| **`LoadingDryRun_SnapshotCapturesLiveState`** | Updated **TASK-DETAIL** asks **4 entities** and **`EntityCount == 4`** on snap; the test still uses **one** entity — **spec vs test mismatch**. |
| **Stale XML on `EcsRecordReplayController`** | Still says **`CanHandle` returns false “until S0202”** — misleading now that the class is a **factory** for S0304. |
| **Report typo** | “**CDG1**-S0303” in narrative for **`LiveLoadDsmHandler`** (cosmetic). |
| **Flaky tests** | Report notes timing sensitivity in **`CheckpointIOWorkerTests`** — track stabilization if CI sees flakes. |

---

## Verdict vs task detail §CGF1-S0304

| Item | Status |
|------|--------|
| Recording/replay modules, controller, live load, replay handler, bypass flags, group, MaxNetworkId, tests | **Delivered** |
| **`RecordingConfiguration` in Fdp.Kernel** | **In Toolkit.Replay** instead — minor spec drift |
| **Production SimHost replay registration** | **Missing** — see gap above |

---

## Suggested commit message

```
feat(cgf-1): S0304 recording/replay modules, dry-run TASK-DETAIL, checkpoint root config

- NodeConfiguration.LocalTempRoot; checkpoint path under same root as scenarios
- EcsRecordReplayController + LiveLoadDsmHandler + ReplayLoadDsmHandler + lifecycle group
- RecordingModule/ReplayModule/AsyncRecorder MaxNetworkId; SeekToWallClockTicks test
- TASK-DETAIL: S0309 path/DryRunTestPos; S0303 deferred-ACK wording; dry-run rewind test

Follow-up: register ReplayLoadDsmHandler from SimHostApp (pass sim/lifecycle/ghost);
align IRecordReplayController with EcsRecordReplayController; fail-loud ExerciseId parse;
align LoadingDryRun snapshot test with 4-entity TASK-DETAIL or relax spec.
```

---

## Next batch

**[CGF-1-BATCH-17](../batches/CGF-1-BATCH-17-INSTRUCTIONS.md)** — **Part A:** S0304 **production replay wiring** + **interface/signature** cleanup + **LiveLoad** fail-loud **ExerciseId** + dry-run **T1** test/TASK-DETAIL alignment + optional **`CheckpointIOWorkerTests`** stability. **Part B:** **CGF1-S0305** Live-from-Replay temporal interlock (builds on wired replay + recording).
