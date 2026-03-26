# BATCH-09 Review

**Batch:** BATCH-09  
**Reviewer:** Development Lead  
**Date:** 2026-03-26  
**Status:** APPROVED with **one corrective follow-up** (native teardown on runner path)

---

## Summary

Cross-checked **`.dev-workstream/reports/BATCH-09-REPORT.md`** against the tree. **Tasks 1–4 and Task 5 Phase A are implemented in source** and match the batch brief. **Automated tests** for the touched areas pass locally: `Fdp.Examples.Scenarios.Tests` **53/53**, `FDP.Toolkit.Perception.Tests` **32/32**, `FDP.Toolkit.Replay.Tests` **14/14**.

**Blocking issue (lifecycle):** `DistributedTankScenario` releases DDS participants and the Muscle stack in **`IDisposable.Dispose`**, but **`ScenarioSubsystem.Shutdown`** only calls **`IScenario.OnShutdown()`**, and **`Program.cs`** never disposes the scenario. **`ScenarioTestHarness`** tests use **`using var`**, so they are clean; **`fdp-demo-runner --scenario distributedtank`** can **leak Cyclone native handles** until this is fixed.

---

## Task-by-task verification

### Task 1 — `LocalGridBuilderSystem` / `SpatialHashGrid`

**Expected:** Avoid full-grid clear on every movement; document strategy.

**Found:** `LocalGridBuilderSystem` uses **`_prevPositions`**, **`_lastEntityCount`**, incremental **`Remove` + `Add`** when count stable, and **`FullRebuild`** when entity count changes. **`SpatialHashGrid`** adds **`FreeList`**, **`Remove`**, and **`Add`** reuse. XML on `LocalGridBuilderSystem` documents complexity cases. Matches the report.

**Residual risk (low, document / future test):** `_prevPositions` is keyed by **`entity.Index`**. If entity count stays constant while an index is **recycled** (destroy + create, same count), the incremental path could theoretically desync from the old design; production churn usually changes count or is rare. Worth a **follow-up test or full-rebuild on generation change** if this grid is stressed with slot reuse.

### Task 2 — `AutonomousPerceptionModule` scoped bus

**Expected:** Clarify whitelist / contract **and/or** dual-read; tests.

**Found:** **`PerceptionScopedView.ConsumeEvents<T>`** XML explicitly lists **`LosCheckRequestEvent`** and **`TargetVisibleEvent`** and documents extension protocol. **`AutonomousPerceptionModule_ScopedEvents_DoNotLeakToWorldBus`** asserts those event types are **not** consumable from the world bus after a tick — a meaningful isolation check aligned with the stated design.

### Task 3 — `RecordingConfiguration.Blocking`

**Expected:** Opt-in blocking through `RecordingModule` / `RecorderTickSystem`.

**Found:** **`RecordingConfiguration.Blocking`**, **`RecorderTickSystem(..., blocking)`**, **`CaptureKeyframe` / `CaptureFrame`** forward **`blocking: _blocking`**. **`RecordingModule.RegisterSystems`** passes **`_config.Blocking`**. **`StoryRecorderModule`** delegates to **`RecordingModule`**, so blocking applies there too. **`RecordingModule_BlockingTrue_WritesFileSuccessfully`** covers the path.

### Task 4 — File rename

**Found:** **`BehaviorValidationDoctrineIds.cs`** at repo root of `Fdp.Examples.Scenarios` (report-accurate).

### Task 5 — DEM1-D009 Phase A

**Expected:** Two kernels, two DDS participants Domain 0, registry, tests, D009 **unchecked** on tracker until full demo.

**Found:** **`Network/DistributedTankScenario.cs`**: two **`DdsParticipant`**, Brain = main kernel / world, Muscle = separate **`ModuleHostKernel`** + **`SteppingTimeController`**, **`EvaluateTick`** steps Muscle and succeeds at tick **10**. **`ScenarioRegistry`** + **`ScenarioNames.DistributedTank`**. Tests assert exit **0** and **`BrainInitialized` / `MuscleInitialized`**. Participants are **not** yet used for pub/sub — acceptable **Phase A** harness.

**Design alignment:** Matches **DEM1-D009 Phase A** intent (harness + loopback + teardown discipline). Full **§6.4** brain/muscle/ghosting remains **Phase B**.

---

## Test quality

- **Local grid / `SpatialHashGrid`:** New tests target **remove/splice**, **free-list reuse**, and **incremental builder** behaviour — they exercise what changed.
- **Scoped bus:** Test validates **non-leakage to world bus**, which is the real invariant for the debt item.
- **Blocking recorder:** File existence after ticks proves the flag is wired, not just stored.
- **DistributedTank:** Proves **configure + dual init + run**; they rely on **`using`** for native cleanup — **runner path still needs `OnShutdown` (or subsystem `IDisposable` dispatch)**.

---

## Suggested commit message

```
BATCH-09: incremental perception grid; scoped-bus contract; recording Blocking; D009 Phase A harness

- SpatialHashGrid: Remove + free-list; LocalGridBuilderSystem incremental updates + full rebuild on count change
- AutonomousPerceptionModule: document scoped event whitelist; test world-bus non-leakage
- RecordingConfiguration.Blocking + RecorderTickSystem wiring; Replay test for blocking recording
- Rename DemoDoctrineIds.cs to BehaviorValidationDoctrineIds.cs
- Add DistributedTankScenario (two DDS participants, dual kernels), registry + Phase A tests
```

---

## Follow-ups (BATCH-10)

1. **`DistributedTankScenario`:** Implement **`OnShutdown()`** (call shared teardown with **`Dispose`**) **or** have **`ScenarioSubsystem`** invoke **`IDisposable`** on scenarios — fix **CLI native leak**.  
2. **DEM1-D009 Phase B:** ELM, replication modules, spawn/ghost milestones per **`DEM1-TASK-DETAIL`**.  
3. **Optional:** Harden **`LocalGridBuilderSystem`** against **index reuse** at stable entity count; **`FDP.Toolkit.ImGui.Tests`** parallel isolation (**BD1-BATCH-04** debt row).  
4. **Optional:** Migrate **`ParallelStoriesScenario`** to **`RecordingModule` + `Blocking: true`** to exercise the product path.
