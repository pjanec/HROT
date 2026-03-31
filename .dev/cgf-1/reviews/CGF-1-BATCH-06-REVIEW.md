# CGF-1-BATCH-06 Review

**Batch:** CGF-1-BATCH-06  
**Reviewer:** Development Lead  
**Date:** 2026-03-29  
**Status:** APPROVED (minor follow-ups for BATCH-07)

**Report:** [CGF-1-BATCH-06-REPORT.md](../reports/CGF-1-BATCH-06-REPORT.md) — cross-checked against **source**.

---

## Summary

**Part A** matches the report and instructions: **`ClusterSlave.PublishHeartbeat`** sets **`LocalClusterState = _localClusterState`**; **`LocalClusterStateForTest`** is a narrow internal seam consistent with **`EnqueueCommandForTest`**; **`LocalClusterState_ReflectsCommittedState_AfterCommitState`** proves the post-**CommitState** value is what the next heartbeat would publish. **`CommitState_RaisesClusterStateChangedEvent`** rename and **`PlanTrajectory_WhitespaceOnlyPayload_Throws`** are present. **DEBT-TRACKER** already marks the heartbeat and typo rows ✅ (verified).

**Part B (CGF1-S0203)** is **delivered**: **`MasterTimeController.SeedState`** publishes **`TimePulseDescriptor`** immediately and seeds **`_totalWallTicks`** from **`state.TotalWallTicks`**; **`SlaveTimeController.SeedState`** assigns **`_virtualWallTicks = state.TotalWallTicks`** and resets the jitter filter. **`SwitchableTimeController.SwitchTo`** still seeds the new controller from **`GetCurrentState()`** with a same-instance no-op. All five named success-condition tests exist; **`FDP.Toolkit.Time.Tests`** passes (**57** passed, **1** skip — pre-existing).

**Note:** **`ITimeController`** lives in **`ModuleHost.Core`** (not under **`FDP.Toolkit.Time`**); controllers in **`FDP.Toolkit.Time`** implement that contract — consistent with the task’s “verify / extend” wording.

**Tests run (review):** `dotnet test FDP.Toolkit.Time.Tests` — green.

---

## Tasks vs instructions

| Item | Verdict |
|------|---------|
| **A.1** Heartbeat **`LocalClusterState`** | **Done** — see `PublishHeartbeat` + test + seam. |
| **A.2** DEBT hygiene | **Done** — per report; open **SurvivingNodes** row remains **CGF-1-BATCH-07** (unchanged). |
| **A.3** Rename + whitespace test | **Done**. |
| **B** CGF1-S0203 | **Done** — code + tests align with [CGF-1-TASK-DETAIL §S0203](../CGF-1-TASK-DETAIL.md#cgf1-s0203--time-strategy-proxying). |

---

## Issues / follow-ups (not blocking approval)

### Issue 1: **`SteppedMasterController` vs `TotalWallTicks` after `SwitchTo`** (P3)

**`SwitchableTimeControllerTests.SwitchTo_TransfersCurrentStateToNewController`** only asserts **`TotalTime`**. **`SteppedMasterController.SeedState`** copies **`FrameNumber`**, **`TotalTime`**, **`UnscaledTotalTime`**, **`TimeScale`** but **`GetCurrentTime()`** derives **`TotalWallTicks`** as **`(long)(_unscaledTotalTime * Stopwatch.Frequency)`**, while the continuous master’s **`TotalWallTicks`** is a **Stopwatch tick accumulator** — they can **differ** from **`state.TotalWallTicks`**. **CGF1-S0204** (barrier on **`TotalWallTicks`**) should treat this as **continuity debt**: either persist **`TotalWallTicks`** into stepped mode explicitly or document the mapping. **Target:** **CGF-1-BATCH-07** (Part A, before or alongside S0204).

### Issue 2: **`SeedState_BypassesJitterFilter` and wall ticks** (P3)

The test seeds **`TotalWallTicks = 0`**, so it does not prove **`TotalWallTicks`** is preserved when **non-zero**. Low risk given **`SlaveTimeController.SeedState`** implementation, but a **non-zero** seed assertion would tighten the **S0203** contract. **Target:** **CGF-1-BATCH-07** (small test extension).

### Issue 3: Heartbeat test uses a seam, not a writer mock (informational)

Instructions allowed a test seam; the implementation matches. A future **integration** test with a fake **`DdsWriter<NodeHeartbeat>`** could assert the **serialized** field, but is **not** required for this batch.

---

## Test quality

| Area | Verdict |
|------|---------|
| **ClusterSlave** | **Good** — committed DSM is tied to heartbeat payload via **`LocalClusterStateForTest`** and clear comments. |
| **TransitionPlanner** | **Good** — whitespace case explicitly covered. |
| **Switchable / Master / Slave / GlobalTime** | **Strong** — **`SeedState`** immediate pulse, swap continuity (**`TotalTime`**), no-op **`SwitchTo`**, slave snap, **`TotalWallTicks > 0`** on master **Update**. |

---

## Design alignment

- **§4.2** (DSM visible on heartbeats): **Aligned** after **`LocalClusterState = _localClusterState`**.  
- **§4.3** (time strategy proxying, **`TotalWallTicks`**, **`SeedState`**, **`SwitchTo`**): **Aligned** for continuous master/slave; **stepped** wall-tick continuity (**Issue 1**) should be closed before relying on **barrier** semantics.

---

## Verdict

**APPROVED.** **CGF1-S0203** and **Part A** are complete; schedule **stepped `TotalWallTicks` continuity** and the **slave wall-tick test** hardening in **CGF-1-BATCH-07** ahead of **S0204**.

---

## Suggested commit message

```
feat(cgf-1): BATCH-06 heartbeat DSM + S0203 time SeedState/SwitchTo tests

- ClusterSlave: heartbeat LocalClusterState from _localClusterState; LocalClusterStateForTest seam + test.
- TransitionPlannerTests: whitespace-only TransitionState payload throws.
- MasterTimeController.SeedState: immediate TimePulse + seed TotalWallTicks; throttle baseline.
- SlaveTimeController.SeedState: set virtual wall ticks from seed; reset jitter filter.
- FDP.Toolkit.Time.Tests: SwitchableTimeController + SeedState + TotalWallTicks coverage.

Related: CGF1-S0203, CGF-1-BATCH-05 review Issue 1.
```

---

**Next batch:** [CGF-1-BATCH-07](../batches/CGF-1-BATCH-07-INSTRUCTIONS.md)
