# CGF-1-BATCH-19 Review

**Batch:** CGF-1-BATCH-19  
**Reviewer:** Development Lead  
**Date:** 2026-03-29  
**Status:** **CONDITIONALLY APPROVED** — **Part A** matches the report and **source** and clears the BATCH-18 CGF regressions with **good tests**. **Part B** delivers a **credible MVP** for §CGF1-S0308 on **SimHost** (handler, planner, `ClusterMaster` fan-out, `ActiveStoriesJson`, integration tests) but **does not** meet several **normative TASK-DETAIL** items (**CGF** handler, **`NodeOpStatus.IsParticipating`** on the wire, **`ClusterMaster`** ACK filtering). **`RecordReplayIntegrationTests.NodeBootstrapper_BrainRole_RegistersEcsRecordReplayController`** remains **red** (assertion does not match **factory-only** `EcsRecordReplayController`).

**Report:** [CGF-1-BATCH-19-REPORT.md](../reports/CGF-1-BATCH-19-REPORT.md) — verified against [CGF-1-BATCH-19-INSTRUCTIONS.md](../batches/CGF-1-BATCH-19-INSTRUCTIONS.md), [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0308.

---

## Part A — APPROVED

- **`FailLoudRecordReplayStub`**: **`PrepareLive`** removed from **`CanHandle`**; XML documents delegation to **`ScenarioLoadDsmHandler`** ([`FailLoudRecordReplayStub.cs`](../../../Hrot.CGF/Modules/Orchestration/Handlers/FailLoudRecordReplayStub.cs)).
- **`Hrot.CGF/ClusterSlave`**: **`_pendingPrepare`**, drain in **`Tick()`**, fault logging, test constructor, **`EnqueueCommandForTest`** — aligned with SimHost BATCH-18 ([`ClusterSlave.cs`](../../../Hrot.CGF/Modules/Orchestration/ClusterSlave.cs)).
- **`CgfPrepareLiveDispatchTests`**: **`PrepareCallCountForTest`** proves **`ScenarioLoadDsmHandler`** runs for **`ScenarioId`** and **ExerciseId-only** payloads ([`CgfPrepareLiveDispatchTests.cs`](../../../Hrot.SimHost.Integration.Tests/CgfPrepareLiveDispatchTests.cs)).

**Branch `PrepareLive`:** Still **log-only** visibility (no **NAK**); acceptable until CGF has **`NodeOpStatus`** — unchanged from prior policy.

---

## Part B — MVP vs §CGF1-S0308

| TASK-DETAIL / design | Implementation |
|----------------------|----------------|
| **`StoryLoadDsmHandler` on `Hrot.CGF`** | **Not implemented** — SimHost only. |
| **`NodeOpStatus(Success, IsParticipating: …)`** from handler | **`IsParticipatingForTest`** seam only; **no** writer passed (report notes; matches **`ScenarioLoadDsmHandler`** pattern but below §S0308 text). |
| **`ClusterMaster` waits only for participating node ACKs** | **Not implemented** — **`ManageEpisode`** fans out and immediately completes **`ClusterOpStatus.InProgress`** with **`CompletedSteps == totalSteps`** ([`ClusterMaster.cs`](../../../Hrot.Orchestrator/ClusterMaster.cs) ~649–666) without **`NodeOpStatus`** round-trip. |
| **`PrefetchStory` step** | **`PrefetchScenario`** used in **`PlanManageEpisode`** ([`TransitionPlanner.cs`](../../../Hrot.Orchestrator/TransitionPlanner.cs)) — reasonable **DRY**; TASK-DETAIL name drift. |
| **`OrchestratorContextTopic.ActiveStories`** | **`ActiveStoriesJson`** string — acceptable wire form. |

**[`StoryLoadDsmHandler`](../../../Hrot.SimHost/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs):** **`Deserialize`** failure **rethrows** after **Error** log — good. **`Parse*Payload` `catch` → empty** — callers **`Warn`** and no-op; acceptable but not **throw**-loud. **`Commit`** guards **`_pendingTransactionId`** — avoids wrong commit.

**Tests:** Five **`StoryInjectionTests`** cover spawn, stop, non-matching subsystem (**`IsParticipatingForTest`**), **`PlanManageEpisode`** invalid state, two stories — **substance is right**; TASK-DETAIL’s **`NodeOpStatus.IsParticipating`** assertion is **not** literally implemented.

---

## CI / tests

Report: **37/38** pass in **`Hrot.SimHost.Integration.Tests`**; one failure is **`NodeBootstrapper_BrainRole_RegistersEcsRecordReplayController`** ([`RecordReplayIntegrationTests.cs`](../../../Hrot.SimHost.Integration.Tests/RecordReplayIntegrationTests.cs)) expecting **`EcsRecordReplayController`** **registered** as **`IDsmHandler`** — **`BuildOrchestration`** only injects it into **`LiveLoad`/`ReplayLoad`** handlers (**by design**). **Fix the test** (assert handler types present) or document — do **not** register the controller as a handler solely to satisfy the test.

**Build:** Not re-run here; report notes external **DLL lock** noise.

---

## Verdict

| Area | Verdict |
|------|---------|
| A.1–A.3 | **Met** |
| S0308 SimHost + planner + orchestrator fan-out + tests | **Met (MVP)** |
| S0308 TASK-DETAIL completeness | **Gap** — CGF handler, **`IsParticipating`** on DDS, ACK gating |

Tracker may mark **S0308** **`[x]`** with a **footnoted residual** in **BATCH-20**, or leave **`[ ]`** until normative gaps close — this review recommends **`[x]`** + **debt** for remaining §S0308 items so Phase 3 closure is honest.

---

## Suggested commit message

```
feat(cgf-1): BATCH-19 CGF PrepareLive routing, CGF ClusterSlave prepare latch, S0308 stories

- FailLoudRecordReplayStub: drop PrepareLive; ScenarioLoad sole handler; dispatch tests
- CGF ClusterSlave: _pendingPrepare + Tick drain (parity with SimHost)
- StoryLoadDsmHandler (StartEpisode/StopEpisode); PlanManageEpisode + ClusterMaster ManageEpisode
- OrchestratorContextTopic.ActiveStoriesJson; NodeBootstrapper wiring

Follow-up: S0308 TASK-DETAIL — CGF StoryLoadDsmHandler, NodeOpStatus IsParticipating +
ClusterMaster ACK filter; fix RecordReplayIntegrationTests EcsRecordReplayController assertion.
```

---

## Next batch

**[CGF-1-BATCH-20](../batches/CGF-1-BATCH-20-INSTRUCTIONS.md)** — **Part A (debt):** §S0308 **residual** (CGF handler, **`NodeOpStatus`** participation, **`ClusterMaster`** wait logic); **`RecordReplayIntegrationTests`** fix. **Part B:** **CGF1-S0310** (E2E DSM scripts) and/or **CGF1-S0106** per extended **TASK-DETAIL** / **DESIGN**.
