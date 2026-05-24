# BATCH-35: Phase P0 + P1 — Foundation Rename + Snapshot Orchestration

**Batch Number:** BATCH-35
**Tasks:** UBP-P0T1, UBP-P1T1, UBP-P1T2, UBP-P1T3
**Phase:** P0 Foundation rename + P1 Snapshot orchestration
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches
2. **Design Document:** `.dev/breakpoints-1/DESIGN.md` — Full architecture; focus on §4 (time-control), §5 (triple-buffer snapshot)
3. **Task Definitions:** `.dev/breakpoints-1/TASK-DETAIL.md` — See UBP-P0T1, UBP-P1T1, UBP-P1T2, UBP-P1T3
4. **Onboarding:** `.dev/breakpoints-1/ONBOARDING.md` — Source map of touched files
5. **Code Standards:** `.github/skills/CODE-STANDARDS.md`

### Source Code Locations
- **IBlueprintTimeController (rename target):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintTimeController.cs`
- **MasterSyncTimeControllerAdapter:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs`
- **BlueprintDebugSession (Slice 1, minimal touch):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/BlueprintDebugSession.cs`
- **Recommended new project folder:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/` (create new project here)
- **Existing test project for Blueprints:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`
- **IEcsModuleSystem reference:** `FDP/Engine/Fdp.ModuleHost/Abstractions/IEcsModule.cs`
- **EntityRepository:** `FDP/Engine/Fdp.Core/EntityRepository.cs` and `EntityRepository.Sync.cs`
- **MasterSyncController:** `FDP/Engine/Fdp.Core/` (time controllers)
- **SystemPhase / UpdateInPhase:** `FDP/Engine/Fdp.ModuleHost/`

### How to Build and Test
```powershell
# From repo root d:\Work\IOS-IG-SimHost-FDP-2\
dotnet build IOS-IG-SimHost.sln -c Debug

# Run relevant tests
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
dotnet test FDP/Engine/Fdp.ModuleHost.Tests/Fdp.ModuleHost.Tests.csproj

# If you create a new test project for breakpoints, run it directly
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
```

### Report Submission
**When done, submit your report to:**
`.dev/breakpoints-1/reports/BATCH-35-REPORT.md`

**If you have questions, create:**
`.dev/breakpoints-1/questions/BATCH-35-QUESTIONS.md`

---

## Context

This is the first batch of the Universal Breakpoints workstream (Slice 2 of the HROT Blueprint debug subsystem). The goal of this batch is to lay the **entire foundation** that all subsequent batches will build on:

1. **Rename the time-controller interface** so the engine's debug pausing surface is no longer Blueprint-specific.
2. **Implement the triple-buffer snapshot infrastructure** (`DebugSnapshotProvider` + `IDataBreakpointManager` skeleton with the reference-counted gate + triple-buffer pause primitives).

Read §4 and §5 of `DESIGN.md` carefully before starting. The design talk context in `design-talk.md` is long but the §4/§5 summary is self-contained.

**Related Tasks:**
- [UBP-P0T1](../TASK-DETAIL.md#ubp-p0t1--rename-iblueprinttimecontroller--ienginedebugtimecontroller) — Interface rename (backward-compatible)
- [UBP-P1T1](../TASK-DETAIL.md#ubp-p1t1--debugsnapshotprovider-system) — DebugSnapshotProvider IEcsModuleSystem
- [UBP-P1T2](../TASK-DETAIL.md#ubp-p1t2--idatabreakpointmanager-skeleton--reference-counted-gate) — Manager skeleton + gate
- [UBP-P1T3](../TASK-DETAIL.md#ubp-p1t3--triple-buffer-pause-primitives) — OnHit, RequestStep, RequestContinue

---

## Batch Objectives

- Rename `IBlueprintTimeController` to `IEngineDebugTimeController` without breaking any existing Slice 1 compilation.
- Implement `DebugSnapshotProvider` with a zero-cost dormant path.
- Implement `IDataBreakpointManager` with its full API surface (stubs for unbuilt parts) and working reference-counted gate.
- Implement the triple-buffer pause/step/continue primitives in `DataBreakpointManager`.
- All tests listed below must pass.

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests:**

1. **UBP-P0T1:** Implement rename → Write tests → ALL tests pass
2. **UBP-P1T1:** Implement DebugSnapshotProvider → Write tests → ALL tests pass
3. **UBP-P1T2:** Implement manager skeleton + gate → Write tests → ALL tests pass
4. **UBP-P1T3:** Implement triple-buffer primitives → Write tests → ALL tests pass

**DO NOT** move to the next task until current task tests pass. Run the full test suite before submitting the report. Fix all failures — do not skip or comment out tests.

---

## Tasks

### Task 1: UBP-P0T1 — Rename `IBlueprintTimeController` → `IEngineDebugTimeController`

**Design reference:** [DESIGN.md §4](../DESIGN.md#4-time-control-surface-phase-p0)
**Task detail:** [TASK-DETAIL.md UBP-P0T1](../TASK-DETAIL.md#ubp-p0t1--rename-iblueprinttimecontroller--ienginedebugtimecontroller)

**Steps:**
1. Create `IEngineDebugTimeController` in namespace `Hrot.Blueprints.Core.Debug` with the same four members as the existing `IBlueprintTimeController` (`IsPausedByDebugger`, `RequestPause`, `RequestResume`, `RequestStepOneTick`).
2. Make `IBlueprintTimeController` extend `IEngineDebugTimeController` and mark it `[Obsolete("Use IEngineDebugTimeController. IBlueprintTimeController will be removed after one batch.")]`. Do NOT delete `IBlueprintTimeController` yet — Slice 1 code must still compile unchanged.
3. Update `MasterSyncTimeControllerAdapter` to explicitly declare `IEngineDebugTimeController` in its implementation list (it still implements `IBlueprintTimeController` too, since that inherits from the new interface). No method body changes.
4. Update `BlueprintDebugSession` constructor parameter type from `IBlueprintTimeController` to `IEngineDebugTimeController`. Keep all existing logic unchanged.

**Key constraint:** Every existing Slice 1 test in `Hrot.Blueprints.Tests` must pass without modification after this task.

**Tests to write** (put in the new breakpoints test project or in `Hrot.Blueprints.Tests` if no new project yet):
- `IEngineDebugTimeController_Implements_PauseResumeStepContract` — instantiate a `MasterSyncTimeControllerAdapter` against a real or faked `MasterSyncController`; call `RequestPause()`, assert `IsPausedByDebugger == true`; call `RequestResume()`, assert `IsPausedByDebugger == false`; call `RequestStepOneTick()` while paused, assert time advanced by ~1/60 s.
- `IBlueprintTimeController_Still_Resolves_Through_Inheritance` — assert that `IBlueprintTimeController` is assignable from `IEngineDebugTimeController` (use `typeof` checks or create a variable of each type). The existing `BlueprintDebugSession` tests must pass unchanged — run them and confirm.

---

### Task 2: UBP-P1T1 — `DebugSnapshotProvider` system

**Design reference:** [DESIGN.md §5.2](../DESIGN.md#52-debugsnapshotprovider)
**Task detail:** [TASK-DETAIL.md UBP-P1T1](../TASK-DETAIL.md#ubp-p1t1--debugsnapshotprovider-system)

**Steps:**
1. Create a new C# project `Hrot.Diagnostics.Breakpoints` under `Hrot/Diagnostics/`. Add it to `IOS-IG-SimHost.sln`. It references `Fdp.Core`, `Fdp.ModuleHost`.
2. Implement `DebugSnapshotProvider : IEcsModuleSystem` in that project.
   - Decorated with `[UpdateInPhase(SystemPhase.BeforeSync)]`.
   - Constructor takes `EntityRepository preTickSnapshot` (pre-allocated, passed in from the owning manager).
   - `volatile int _isEnabled = 0;`
   - `public void SetEnabled(bool enabled) => Interlocked.Exchange(ref _isEnabled, enabled ? 1 : 0);`
   - `public void Execute(ISimulationView view, float deltaTime)`: if `_isEnabled == 0` return immediately; cast view to `EntityRepository` and call `preTickSnapshot.SyncFrom(repo)`.
3. Create a companion test project `Hrot.Diagnostics.Breakpoints.Tests`. Add it to the solution. Write the three tests from TASK-DETAIL:
   - `DebugSnapshotProvider_GateOff_DoesNoWork` — verify that with `_isEnabled == 0`, `Execute` does **not** call `SyncFrom`. Use a recording wrapper or mock that tracks whether `SyncFrom` was called on the `_preTickSnapshot` repo; or simply populate `_preTickSnapshot` with one state before calling Execute with the gate off, then change the live repo, call Execute, and verify `_preTickSnapshot` did **not** change.
   - `DebugSnapshotProvider_GateOn_SyncsEveryTick` — set `_isEnabled = 1`, provide a live repo with a known component value; call Execute; assert `_preTickSnapshot` has that value.
   - `DebugSnapshotProvider_ZeroAllocationsHotPath` — write a BenchmarkDotNet benchmark (in a separate `*.Benchmarks` project or alongside, your choice) that calls `Execute` with gate off and verifies 0 B/op and < 50 ns/op. For the automated test suite, write a non-BDN test that at minimum calls Execute 10000 times with gate off and asserts no allocations via `GC.GetTotalMemory` before/after (with a gen-0 collection forced first).

**Key implementation note:** `DebugSnapshotProvider.Execute` requires an `EntityRepository` (not just `ISimulationView`) to call `SyncFrom`. Use the `view is EntityRepository repo` pattern and throw `InvalidOperationException` if view is not a repo (matching the established pattern in this codebase).

---

### Task 3: UBP-P1T2 — `IDataBreakpointManager` skeleton + reference-counted gate

**Design reference:** [DESIGN.md §5.3](../DESIGN.md#53-idatabreakpointmanager-reference-counted-gate), [DESIGN.md §9](../DESIGN.md#9-manager-api-idatabreakpointmanager)
**Task detail:** [TASK-DETAIL.md UBP-P1T2](../TASK-DETAIL.md#ubp-p1t2--idatabreakpointmanager-skeleton--reference-counted-gate)

**Steps:**
1. Define `BreakpointId` (a value type wrapping `int`) and `Breakpoint` record in the `Hrot.Diagnostics.Breakpoints` project. The `Breakpoint` record must have the shape described in DESIGN.md §6.2: `Id`, `Condition` (typed as `SearchPredicateDto` — you'll need to reference `Fdp.Toolkits` for that DTO, or use `object` as a placeholder and add a TODO comment if the reference would be complex; see §6.1 for the DTO hierarchy location at `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs`), `FilterEntity`, `HitCount`, `OccurrenceThreshold`, `Enabled`, `DisplayName`.
2. Define `IDataBreakpointManager` interface with the complete member list from DESIGN.md §9:
   - `Add(Breakpoint) : BreakpointId`
   - `Remove(BreakpointId)`
   - `SetEnabled(BreakpointId, bool)`
   - `UpdateCondition(BreakpointId, SearchPredicateDto)`
   - `StageMutation(...)` — signature stub (will be fully implemented in P4)
   - `RequestStep()`
   - `RequestContinue()`
   - `OnExternalHit(string tag, Entity entity)` — signature stub (will be implemented in P7)
   - Events: `event Action<Breakpoint, Entity> OnBreakpointHit`, `event Action<bool> OnPauseStateChanged`
   - Properties: `bool IsPaused`, `int PendingMutationsCount`, `IReadOnlyList<Breakpoint> AllBreakpoints`
3. Implement `DataBreakpointManager` (concrete class). For this task, implement:
   - Breakpoint registry (`Dictionary<BreakpointId, Breakpoint> _breakpoints`).
   - `Add`, `Remove`, `SetEnabled`, `UpdateCondition` — full implementations.
   - Reference-counted gate: `int _activeBreakpointCount`. On 0→1 transition call `_snapshotProvider.SetEnabled(true)`. On 1→0 call `_snapshotProvider.SetEnabled(false)`.
   - Allocate `_postTickSnapshot = new EntityRepository()` once at construction.
   - Stubs for `StageMutation`, `OnExternalHit`, `RequestStep`, `RequestContinue` (throw `NotImplementedException` or leave as no-op for now — will be implemented in P1T3 and later batches).

**Tests to write:**
- `Manager_FirstBreakpointEnabled_MountsSnapshotProvider` — construct manager with a `DebugSnapshotProvider`; add one enabled breakpoint; assert `_snapshotProvider._isEnabled == 1`.
- `Manager_LastBreakpointDisabled_UnmountsSnapshotProvider` — add one breakpoint, then call `SetEnabled(id, false)`; assert `_snapshotProvider._isEnabled == 0`.
- `Manager_DisableThenReenable_KeepsCount` — add breakpoint (enabled), disable it (`_isEnabled` goes to 0), re-enable it (`_isEnabled` goes back to 1), verify count is symmetric (exactly 1 active).
- `Manager_TwoBreakpoints_DisableOne_KeepsMounted` — add two enabled breakpoints; disable one; assert `_snapshotProvider._isEnabled == 1` (count is still 1).

---

### Task 4: UBP-P1T3 — Triple-buffer pause primitives

**Design reference:** [DESIGN.md §5.4](../DESIGN.md#54-on-demand-_posttick-snapshot), [DESIGN.md §5.5](../DESIGN.md#55-clean-step-observation-only-fast-path)
**Task detail:** [TASK-DETAIL.md UBP-P1T3](../TASK-DETAIL.md#ubp-p1t3--triple-buffer-pause-primitives)

**Steps:**
1. Implement `DataBreakpointManager.OnHit(Breakpoint bp, Entity entity)`:
   - Increment `bp.HitCount`.
   - Check `OccurrenceThreshold`: if `bp.HitCount < bp.OccurrenceThreshold`, return (no pause yet).
   - `_postTickSnapshot.SyncFrom(_liveRepo)` — capture post-execution state.
   - `_liveRepo.SyncFrom(_preTickSnapshot)` — rewind live world to pre-tick state.
   - `_timeController.RequestPause()`.
   - `_isPaused = true`.
   - Fire `OnBreakpointHit?.Invoke(bp, entity)`.
   - Fire `OnPauseStateChanged?.Invoke(true)`.

   The manager needs references to `_liveRepo` (the live `EntityRepository`), `_preTickSnapshot` (from `DebugSnapshotProvider`), `_postTickSnapshot` (allocated at construction), and `_timeController` (`IEngineDebugTimeController`). Wire these in the constructor.

2. Implement `RequestContinue()`:
   - If not paused, return.
   - `_liveRepo.SyncFrom(_postTickSnapshot)` — restore end-of-tick state.
   - `_timeController.RequestResume()`.
   - `_isPaused = false`.
   - Fire `OnPauseStateChanged?.Invoke(false)`.

3. Implement `RequestStep()`:
   - If not paused, return.
   - `_liveRepo.SyncFrom(_postTickSnapshot)` — restore end-of-tick state (clean step).
   - `_timeController.RequestStepOneTick()`.
   - `_isPaused = false`.
   - Fire `OnPauseStateChanged?.Invoke(false)`.

**Tests to write:**
- `Manager_OnHit_PerformsTripleBufferRewind` — set up three repos: a pre-tick snapshot with `Health.Current = 100`, a live repo with `Health.Current = 50` (simulating mid-tick mutation), and a post-tick snapshot that starts empty. Call `OnHit`. Assert: (a) `_postTickSnapshot` has `Health.Current = 50`, (b) `_liveRepo` has `Health.Current = 100` (rewound to pre-tick), (c) `_timeController.IsPausedByDebugger == true`.
- `Manager_CleanStep_RestoresPostTickThenAdvances` — call `OnHit` first to pause; then call `RequestStep()`; assert `_liveRepo` matches `_postTickSnapshot` (step restores post-tick state) and the time controller was asked to step one tick.
- `Manager_CleanStep_NeverInjectsEvents` — use a mock/fake `IEngineDebugTimeController` and fake `EntityRepository` wrapper that records all calls; call `RequestStep()`; assert no `EventAccumulator` or event-injection method was called between pause and step. (Since `EventAccumulator` injection isn't used in this path, a simpler test: just verify `_liveRepo.SyncFrom` was the only call, followed by `RequestStepOneTick`. Document clearly what this test validates.)
- `Manager_OccurrenceThreshold_PausesOnNthHit` — set `OccurrenceThreshold = 3`; fire `OnHit` 5 times; assert `IsPaused == true` only starting from the 3rd call, and `bp.HitCount == 5` (all hits counted) but pause was triggered at hit 3.

---

## Quality Standards

**Test Quality:**
- Tests must assert actual values, not just "no exception".
- Each test must have at least one positive and, where applicable, one negative case.
- Use real `EntityRepository` instances with components, not mocks, for buffer-rewind tests.
- The gate tests must verify the actual `_isEnabled` flag value, not just "no crash".

**Code Quality:**
- No magic numbers — use named constants.
- `volatile int` for `_isEnabled` flag (not `bool`, not lock).
- Throw `InvalidOperationException` (not silent return) when `Execute` receives a non-`EntityRepository` view.
- Follow existing namespace conventions in the codebase.

**Do not stop mid-batch to ask for permission** to run tests, fix compile errors, or make obvious design choices consistent with this spec. Implement everything, make it compile and pass, then write the report.

---

## Success Criteria

This batch is DONE when:
- [ ] `IEngineDebugTimeController` defined, `IBlueprintTimeController` extends it + marked Obsolete
- [ ] `MasterSyncTimeControllerAdapter` re-targets the new interface
- [ ] `BlueprintDebugSession` ctor uses `IEngineDebugTimeController`
- [ ] All existing `Hrot.Blueprints.Tests` pass unchanged
- [ ] `DebugSnapshotProvider` implemented with zero-cost gate
- [ ] `IDataBreakpointManager` interface defined with full member list
- [ ] `DataBreakpointManager` implemented with gate + registry
- [ ] `OnHit`, `RequestStep`, `RequestContinue` implemented with triple-buffer logic
- [ ] All tests listed above passing
- [ ] Full solution builds with no errors
- [ ] Report submitted at `.dev/breakpoints-1/reports/BATCH-35-REPORT.md`

---

## Developer Insights (required in report)

**Q1:** What issues did you encounter during implementation? How did you resolved them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

---

## Reference Materials
- **Design:** `.dev/breakpoints-1/DESIGN.md` — §4 (time-control), §5 (triple-buffer), §9 (manager API)
- **Task Defs:** `.dev/breakpoints-1/TASK-DETAIL.md` — UBP-P0T1, UBP-P1T1, UBP-P1T2, UBP-P1T3
- **EntityRepository:** `FDP/Engine/Fdp.Core/EntityRepository.cs`, `EntityRepository.Sync.cs`
- **IEcsModuleSystem pattern:** `FDP/Engine/Fdp.ModuleHost/Abstractions/IEcsModule.cs`
- **System phase example:** `FDP/Toolkits/Fdp.Toolkits/Lifecycle/Systems/LifecycleSystem.cs`
- **Existing time controller:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs`
- **Code standards:** `.github/skills/CODE-STANDARDS.md`
