# CGF-1-BATCH-17 Review

**Batch:** CGF-1-BATCH-17  
**Reviewer:** Development Lead  
**Date:** 2026-04-10  
**Status:** **CONDITIONALLY APPROVED** — Part A items (A.1–A.6) and orchestrator-side S0305 behaviour are largely **verified in source**; **S0305 Live-from-Replay on SimHost is not actually wired through `DrillSlave`** because **`LiveLoadDsmHandler` wins every `PrepareLive`** before **`ReplayLoadDsmHandler`**. **CGF** branch **`PrepareLive`** is **silently treated as success** by **`ScenarioLoadDsmHandler`** (no `ScenarioId` in payload). **`DrillSlave` still does not `await` `PrepareAsync`**, so async replay/recording work can race **`Commit`**. **`FullBranchPipelineTests`** correctly deferred per instructions.

**Report:** [CGF-1-BATCH-17-REPORT.md](../reports/CGF-1-BATCH-17-REPORT.md) — verified against [CGF-1-BATCH-17-INSTRUCTIONS.md](../batches/CGF-1-BATCH-17-INSTRUCTIONS.md), [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §S0305, [CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.5–5.7.

---

## Summary

**Part A (verified):** Two-phase **`SimHostApp.OnLoad`** builds **`GhostCreationSystem`**, **`SimulationSystemGroup`**, **`NetworkLifecycleSystemGroup`** before **`BuildOrchestration`** and passes the same ghost system into **`SimHostModule`** ([`SimHostApp.cs`](../../../Bagira.SimHost/SimHostApp.cs)). **`NodeBootstrapperReplayTests`** assert **`ReplayLoadDsmHandler`** registration. **`IRecordReplayController`** / **`EcsRecordReplayController`** aligned (**`FinalizeRecordingAsync(long maxNetworkId = 0)`**). **`ParseDrillId`** throws in **`LiveLoadDsmHandler`** and **`ReplayLoadDsmHandler`**. **`FinalizeRecordingAsync`** logs **Warn** when no active recording module. **`LoadingDryRun_SnapshotCapturesLiveState`** uses **4** entities. **`EcsRecordReplayController`** XML updated. **`FailLoudRecordReplayStub`** on CGF logs **Error** for unsupported ops (architecture note partially satisfied).

**Part B (orchestrator):** **`ReplayMasterModule`**, **`DrillMaster`** branch freeze / restore with **`_pendingBranchTasks`**, and **`DrillMasterReplayTests`** behave as described in the report.

**Tests run (review):** `Bagira.SimHost.Tests` — **385 / 385** passed; `Bagira.Orchestrator.Tests` — **28 / 28** passed.

---

## Critical gap — `PrepareLive` never reaches `ReplayLoadDsmHandler` on SimHost

[`DrillSlave.DispatchCommand`](../../../Bagira.SimHost/Modules/Orchestration/DrillSlave.cs) invokes the **first** handler with **`CanHandle(op)`** true. [`NodeBootstrapper.BuildOrchestration`](../../../Bagira.SimHost/NodeBootstrapper.cs) registers **`LiveLoadDsmHandler`** **before** **`ReplayLoadDsmHandler`**. Both handle **`NodeOpType.PrepareLive`**.

Therefore **all** `PrepareLive` commands, including the **Live-from-Replay branch** (payload with **`DrillId`** only), are handled by **`LiveLoadDsmHandler`**, which calls **`PrepareRecordingAsync`** only — **no** **`TeardownReplayAsync`**. The **`ReplayLoadDsmHandler`** branch implementation ([`ReplayLoadDsmHandler.cs`](../../../Bagira.SimHost/Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs) `PrepareLive` case) is **dead on the production dispatch path**.

**[`LiveFromReplayTests`](../../../Bagira.SimHost.Tests/LiveFromReplayTests.cs)** construct **`ReplayLoadDsmHandler` in isolation** and call **`PrepareAsync`/`Commit` directly** — they **do not** reproduce **`DrillSlave`** registration order, so they **miss** this bug.

---

## CGF — branch `PrepareLive` silent success

[`CgfApplication`](../../../Bagira.CGF/CgfApplication.cs) registers **`ScenarioLoadDsmHandler`** before **`FailLoudRecordReplayStub`**. **`ScenarioLoadDsmHandler.CanHandle`** is true for **`PrepareLive`**. For orchestrator branch payloads **`ParseScenarioId`** returns **null** (no **`ScenarioId`**); **`PrepareAsync`** returns success immediately without logging the stub’s **Error**. **`FailLoudRecordReplayStub` never runs** for that op — **brain-side gap remains invisible** on the default CGF path with serializer.

---

## Structural gap — `PrepareAsync` not awaited

[`DrillSlave.DispatchCommand`](../../../Bagira.SimHost/Modules/Orchestration/DrillSlave.cs) uses **`_ = handler.PrepareAsync(...)`** then **`handler.Commit`** immediately. Any handler whose **`PrepareAsync`** actually **awaits** (e.g. **`InstallModuleAsync`**) can see **`Commit`** run **before** async preparation finishes. This predates BATCH-17 but **amplifies** risk for replay/recording handlers.

---

## Other notes

| Topic | Finding |
|--------|---------|
| **CGF stub ACK policy** | Report accurately states **no `NodeOpStatus` NAK** — **Error** log only; document until CGF has a writer and real 2PC. |
| **`FullBranchPipelineTests`** | Correctly deferred to BATCH-18 per instructions; **S0305** should stay annotated until that test lands. |
| **Design §S0305** | **`ReplayMasterModule`** uses callbacks instead of referencing **`MasterTimeController`** directly — acceptable to keep **`Bagira.Orchestrator`** thin. |
| **`DryRunDsmHandler.ParseTargetState`** | Still **`catch` → Standby** for malformed JSON — pre-existing fail-soft; unrelated to BATCH-17 scope. |

---

## Verdict vs instructions

| Area | Verdict |
|------|---------|
| A.1–A.6, CGF stub, DEBT closure | **Met** (with CGF branch caveat above) |
| S0305 SimHost end-to-end via `DrillSlave` | **Not met** — handler order blocks branch path |
| S0305 orchestrator freeze/restore | **Met** (tests + code) |
| All §S0305 success tests | **`FullBranchPipelineTests`** deferred (allowed); remaining tests **do not** cover real dispatch |

---

## Suggested commit message

```
fix(cgf-1): BATCH-17 replay wiring, S0305 freeze/restore, CGF record-replay stub

- SimHostApp: two-phase bootstrap; shared GhostCreationSystem for replay + SimHostModule
- IRecordReplayController alignment; fail-loud ParseDrillId; finalize Warn; dry-run 4 entities
- ReplayMasterModule + DrillMaster branch tasks; ReplayLoadDsmHandler PrepareLive branch
- FailLoudRecordReplayStub on CgfApplication; NodeBootstrapperReplayTests, LiveFromReplayTests,
  DrillMasterReplayTests

Follow-up (BATCH-18): DrillSlave handler order or conditional PrepareLive; CGF branch vs
ScenarioLoad; await PrepareAsync or sync barrier; FullBranchPipelineTests.
```

---

## Next batch

**[CGF-1-BATCH-18](../batches/CGF-1-BATCH-18-INSTRUCTIONS.md)** — **Part A:** fix **`PrepareLive`** dispatch (replay branch vs normal live load), CGF **`PrepareLive`** when payload is **`DrillId`**-only, and **`DrillSlave`/`PrepareAsync`** sequencing; **Part B:** **`FullBranchPipelineTests`** + close **S0305** residual in tracker.
