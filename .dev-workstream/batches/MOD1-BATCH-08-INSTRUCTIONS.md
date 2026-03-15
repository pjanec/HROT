# MOD1-BATCH-08: LOS Batching Refactor + Phase 8 Recording/Replay Module Architecture

**Batch Number:** MOD1-BATCH-08  
**Tasks:** CT-MOD1-N, DB-MOD1-16, MOD1-P8T1, MOD1-P8T2, MOD1-P8T3, MOD1-P8T4, MOD1-P8T5  
**Phase:** Phase 8 (Recording/Replay Module Architecture)  
**Estimated Effort:** 10-12 hours  
**Priority:** HIGH  
**Dependencies:** MOD1-BATCH-07

---

## 📋 Onboarding & Workflow

### Who You Are
You are a developer implementing the modularization of the IOS-IG-SimHost application. This is an ongoing, multi-batch effort. **Read this section entirely before touching any code.**

### Project Goal
The whole effort is to refactor towards better modularization and generalization. **What should be generic must come under FDP, not be left in the Bagira domain.** Bagira assemblies are application-specific. FDP assemblies are generic engine infrastructure.

### Non-Negotiable Rules
1. **The application must keep working.** We are doing refactoring, not rewriting. `Bagira.Runner -x all` integration tests must pass after every task.
2. **Tests must check real behaviour**, not just call counts or that no exception was thrown.
3. **Do not modify third-party submodules.** The path `FDP\ExtDeps\` contains Git submodules. Never modify files under that path. Check with `git status` in that directory if unsure.
4. **Component IDs belong in toolkit-local registries.** Each FDP toolkit owns a local `*ComponentIds` class (e.g. `NavigationComponentIds`, `GeographicComponentIds`). Do not add IDs to `GlobalComponentIds` in `Fdp.Kernel`.
5. **`IModule` implementations must be thread-safe and fully encapsulated.** Never expose internal systems as public fields.

### Required Reading (IN ORDER)
1. **Developer workflow guide:** `.dev-workstream/README.md`
2. **Architecture design:** `docs/modularizing/MOD1-DESIGN.md` — especially Phase 8 (§3.8)
3. **Task Details:** `docs/modularizing/MOD1-TASK-DETAIL.md` — Phase 8 tasks (MOD1-P8T1 through MOD1-P8T5)
4. **Previous Review:** `.dev-workstream/reviews/MOD1-BATCH-07-REVIEW.md`
5. **Debt Tracker:** `docs/modularizing/MOD1-DEBT-TRACKER.md`

### Source Code Location
- **Corrections:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LosRequestBatchingSystem.cs`, `FDP/Toolkits/Fdp.Toolkit.Geographic/`
- **Phase 8 primary work:** `FDP/Toolkits/FDP.Toolkit.Replay/`, `Bagira.SimHost/Modules/Orchestration/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/MOD1-BATCH-08-REPORT.md`

---

## 🚨 CRITICAL CORRECTIONS FROM BATCH-07

### Correction CT-MOD1-N: Refactor `LosRequestBatchingSystem` to `IModuleSystem`-Only

**This is the highest priority task. Fix it first.**

The current implementation has `LosRequestBatchingSystem : ComponentSystem, IModuleSystem`. This is architecturally wrong and dangerous:

- `ComponentSystem.OnUpdate()` runs on the **main thread** every simulation tick.
- `IModuleSystem.Execute()` runs on a **background thread** at 10 Hz inside `AutonomousPerceptionModule` (which has `ExecutionPolicy.SlowBackground(10)`).
- These two paths call the same `World.Bus.Consume<>()` / `view.ConsumeEvents<>()` — which means events can be consumed twice or from two competing threads.

The design intent is clear: `LosRequestBatchingSystem` is a perception sub-system that runs in the background alongside `LocalGridBuilderSystem`, `VisionBroadphaseSystem`, and `ThreatEvaluationSystem`. It must run strictly within the `AutonomousPerceptionModule` background cycle.

**What to do:**
- Remove the `ComponentSystem` base class entirely.
- Remove the `[UpdateInGroup(typeof(SimulationSystemGroup))]` attribute.
- Remove the `OnUpdate()` method (or rename and merge its logic into `IModuleSystem.Execute`).
- `LosRequestBatchingSystem` becomes a plain class implementing only `IModuleSystem`.
- Ensure `AutonomousPerceptionModule.Tick()` also calls `_losRequestBatching.Execute(view, dt)` on the background thread, consistent with the other 3 systems.
- Update all tests accordingly.

### Correction DB-MOD1-16: Create `GeographicComponentIds` + Move IDs Out of `GlobalComponentIds`

The ground clamping IDs (77–79 for `GroundClampingConfig`, `GroundClampingState`, `TerrainQueryBatchData`) were added directly to `GlobalComponentIds` in `Fdp.Kernel`. This violates the per-toolkit registry pattern.

**What to do:**
- Create `FDP/Toolkits/Fdp.Toolkit.Geographic/GeographicComponentIds.cs` as a standalone static class with the three constants. Keep the same numeric values (77, 78, 79) to avoid ECS registry collisions at runtime.
- Update `[ComponentId(...)]` attributes on those three structs to reference `GeographicComponentIds.*` instead of `GlobalComponentIds.*`.
- Remove the three constants from `GlobalComponentIds` in `Fdp.Kernel/GlobalComponentIds.cs`.
- Run full suite to confirm no ID collision.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **CT-MOD1-N:** Refactor `LosRequestBatchingSystem` to `IModuleSystem`-only → **ALL tests pass** ✅
2. **DB-MOD1-16:** Create `GeographicComponentIds`, remove IDs from `GlobalComponentIds` → **ALL tests pass** ✅
3. **MOD1-P8T1:** `RecordingConfiguration` + `EcsRecordReplayController` skeleton → **ALL tests pass** ✅
4. **MOD1-P8T2:** `RecordingModule` + `RecorderSystem.EntityFilter` extension → **ALL tests pass** ✅
5. **MOD1-P8T3:** `StoryRecorderModule` + `StoryTag`/`StoryReplayTag` components → **ALL tests pass** ✅
6. **MOD1-P8T4:** `ReplayModule` → **ALL tests pass** ✅
7. **MOD1-P8T5:** `NodeBootstrapper` integration + `DrillSlave` registration → **ALL tests pass + integration tests pass** ✅

---

## ✅ Tasks

### Corrective Task CT-MOD1-N: See critical correction above.

---

### Corrective Task DB-MOD1-16: See critical correction above.

---

### Task 1: MOD1-P8T1 — `RecordingConfiguration` + `EcsRecordReplayController` Skeleton

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P8T1](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p8t1--recordingconfiguration--ecsrecordreplaycontroller-skeleton)

**Assembly:** `EcsRecordReplayController` stays in `Bagira.SimHost.Modules.Orchestration` (Bagira `IDsmHandler` binding). `RecordingConfiguration` lands in `FDP.Toolkit.Replay`.

---

### Task 2: MOD1-P8T2 — `RecordingModule` + `RecorderSystem.EntityFilter` Extension

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P8T2](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p8t2--recordingmodule--recordersystementityfilter-extension)

**Assembly:** `RecordingModule` in `FDP.Toolkit.Replay`. Zero-cost idle path: no allocations when not recording.

---

### Task 3: MOD1-P8T3 — `StoryRecorderModule` + `StoryTag`/`StoryReplayTag` Components

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P8T3](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p8t3--storyrecordermodule--storytag--storyreplaytag-components)

**Assembly:** `FDP.Toolkit.Replay`. Concurrent per-story I/O isolation is the key design goal.

---

### Task 4: MOD1-P8T4 — `ReplayModule`

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P8T4](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p8t4--replaymodule)

**Assembly:** `FDP.Toolkit.Replay`. ACID-safe `Dispose()` is mandatory.

---

### Task 5: MOD1-P8T5 — `NodeBootstrapper` Integration + `DrillSlave` Registration

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P8T5](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p8t5--nodebootstrapper-integration--drillslave-registration)

Wire `RecordingModule`, `StoryRecorderModule`, `ReplayModule` into `NodeBootstrapper` for appropriate `NodeRole` values. Ensure `DrillSlave` integration does not break existing `Bagira.Runner -x all` tests.

---

## 📊 Report Requirements

Submit `.dev-workstream/reports/MOD1-BATCH-08-REPORT.md` with:

**Developer Insights**

**Q1:** For CT-MOD1-N — describe the execution ordering of the four systems inside `AutonomousPerceptionModule.Tick()` after the refactor. Why is `LosRequestBatchingSystem` last in the Tick sequence (after `ThreatEvaluation`)?

**Q2:** For the replay modules (P8T2-P8T4) — how did you achieve the zero-cost idle path? Specifically, what does `RecorderSystem.OnUpdate()` do when no recording is active?

**Q3:** For P8T5 — did the `DrillSlave` integration cause any timing or registration order issues in the `Bagira.Runner -x all` integration test run?

**Q4:** Were there any circular dependency issues when referencing `FDP.Toolkit.Replay` from `Bagira.SimHost.Modules.Orchestration`?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `LosRequestBatchingSystem` implements ONLY `IModuleSystem` — no `ComponentSystem` base, no `OnUpdate`, no `[UpdateInGroup]` attribute. `AutonomousPerceptionModule.Tick()` drives all 4 systems uniformly.
- [ ] Ground clamping component IDs live in `FDP.Toolkit.Geographic.GeographicComponentIds`, NOT in `GlobalComponentIds`.
- [ ] `RecordingModule`, `StoryRecorderModule`, and `ReplayModule` all live in `FDP.Toolkit.Replay`.
- [ ] `EcsRecordReplayController` stays in `Bagira.SimHost.Modules.Orchestration` (Bagira binding layer).
- [ ] `Bagira.Runner -x all` integration tests pass unconditionally.
- [ ] All unit and integration test suites pass with 0 failures.
