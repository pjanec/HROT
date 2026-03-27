# BATCH-10 Review

**Batch:** BATCH-10  
**Reviewer:** Development Lead  
**Date:** 2026-03-26  
**Status:** APPROVED (Task 5 deferred as planned)

---

## Summary

Validated **`.dev-workstream/reports/BATCH-10-REPORT.md`** against the repository. **Tasks 1–4 are implemented as described.** **Task 5** (ParallelStories → `RecordingModule.Blocking`) was explicitly deferred — acceptable.

**Tests run locally:** `Fdp.Examples.Scenarios.Tests` **55/55**, `FDP.Toolkit.Perception.Tests` **33/33**, `FDP.Toolkit.ImGui.Tests` **43/43**.

---

## Task-by-task verification

### Task 1 — Scenario teardown (Approach A)

**Found:** `DistributedTankScenario` uses **`_released`**, **`ReleaseResources()`**, **`OnShutdown()`** and **`Dispose()`** both delegating to the same path. **`ScenarioSubsystem.Shutdown`** already calls **`_scenario.OnShutdown()`** before disposing the Brain world, so **`fdp-demo-runner`** releases DDS participants and the Muscle stack without requiring **`IDisposable`** at the host.

**Tests:** **`DistributedTank_OnShutdown_ThenDispose_DoesNotThrow`** exercises harness (**`OnShutdown`**) plus **`using`** (**`Dispose`**); no double-free.

**Doc nit:** `IScenario` XML still says **`OnShutdown`** runs *after* kernel **and** world disposal, but **`ScenarioSubsystem`** order is **`_kernel.Dispose()` → `OnShutdown()` → `_world.Dispose()`**. Behaviour is fine for this scenario; the interface comment is misleading (pre-existing / should be corrected in a small follow-up).

### Task 2 — DEM1-D009 Phase B (incremental)

**Batch goal** allowed choosing a slice: **ELM zero-participant auto-promote** on the Brain kernel is implemented end-to-end — lifecycle events registered, **`EntityLifecycleModule`** with empty participant list and **`TkbDatabase`**, **`BeginConstruction`** at tick 1 via **`EntityCommandBuffer`**, assertion **`Active`** by tick 5, combined success at tick 10.

**Not done (reported for BATCH-11):** DDS topic wiring between Brain/Muscle, **`ReplicationLogicModule`**, ghosting, and channel milestones from **`DEM1-TASK-DETAIL`**. That matches the instruction to defer heavy Phase B work; **participants remain unused** for data-plane traffic — still consistent with **Phase A + ELM slice**.

**Design alignment:** The long-form **DEM1-D009** spec (toolkits on both nodes, TKB spawn, multi-phase ticks) is **not** complete; tracker must stay **unchecked** until those milestones exist.

### Task 3 — `LocalGridBuilderSystem` index reuse

**Found:** **`Dictionary<Entity, Vector2> _prevPositions`** with XML describing index-recycle behaviour. Regression test **`LocalGridBuilder_IndexReuse_NewEntityAtSamePosition_IsInserted`** asserts index reuse preconditions and neighbour query finds **`e2`**.

**Known limitation** (report + honest assessment): a **dead** entity may remain in the **spatial hash** until a **count-change** full rebuild — acceptable for stated perception use; log as follow-up if ghost queries become user-visible.

### Task 4 — ImGui test isolation

**Found:** **`xunit.runner.json`** with **`parallelizeAssembly`** / **`parallelizeTestCollections`** false and **CopyToOutputDirectory** on the **`.csproj`**.

**Note:** This addresses **parallelism inside the ImGui test assembly**. Whether **`dotnet test`** on the **full solution** still stresses native ImGui from **multiple processes** depends on the runner; if CI ever runs a **single** test host over merged assemblies, consider **runsettings** / pipeline ordering. No regression observed in local ImGui run.

### Task 5 — Optional ParallelStories migration

**Deferred** per report — no code change; **no objection**.

---

## Test quality

- **DistributedTank:** Phase B test ties to **`PhaseBElmActive`** (observable scenario state), not only exit code.
- **Teardown:** Double-invocation test is minimal but **targets the actual bug class**.
- **Local grid:** Test reproduces **index + generation** reuse — the right failure mode for the BATCH-09 review item.

---

## Suggested commit message

```
BATCH-10: DistributedTank OnShutdown teardown; ELM Phase B slice; Entity-keyed local grid; ImGui xunit isolation

- DistributedTankScenario: ReleaseResources + OnShutdown/Dispose guard; ELM on Brain (zero-participant promote); Phase B tick-5 Active check
- LocalGridBuilderSystem: key _prevPositions by Entity; index-reuse regression test
- FDP.Toolkit.ImGui.Tests: xunit.runner.json disable parallelization; copy to output
- Scenarios: Tkb reference for ELM; DistributedTank tests for Phase B and teardown
```

---

## Follow-ups (BATCH-11)

1. **DEM1-D009:** DDS translators / topics, **`ReplicationLogicModule`**, ghost spawn, locomotion + weapon milestones per task detail.  
2. **Optional:** **`ParallelStoriesScenario`** → **`RecordingModule` + `Blocking: true`**.  
3. **Optional:** **`IScenario.OnShutdown`** XML vs **`ScenarioSubsystem.Shutdown`** order.  
4. **Optional:** Spatial hash **stale slot** cleanup after index reuse without count change.
