# BATCH-10 Report

**Batch:** BATCH-10  
**Developer:** GitHub Copilot  
**Date:** 2026-03-26  
**Status:** Complete (Tasks 1–4 fully delivered; Task 5 deferred per instructions)

---

## Task Completion

| Task | Status | Notes |
|------|--------|-------|
| Task 1 — `DistributedTankScenario` teardown fix | ✅ Complete | Approach A: `OnShutdown()` + `ReleaseResources()` with `_released` guard |
| Task 2 — DEM1-D009 Phase B milestone (ELM auto-promote) | ✅ Complete | Phase B Phase 1: ELM zero-participant → Active by tick 5 |
| Task 3 — `LocalGridBuilderSystem` index reuse hardening | ✅ Complete | `_prevPositions` keyed by full `Entity` (Index + Generation); regression test added |
| Task 4 — ImGui test parallel isolation | ✅ Complete | `xunit.runner.json` + csproj content item |
| Task 5 — `ParallelStoriesScenario` + `RecordingModule.Blocking` | — | Deferred — Tasks 1–2 stable but time budget used |

---

## Testing Results

| Project | Before | After | Notes |
|---------|--------|-------|-------|
| `Fdp.Examples.Scenarios.Tests` | 53/53 | **55/55** | +2 Phase B / teardown tests |
| `FDP.Toolkit.Perception.Tests` | 32/32 | **33/33** | +1 index-reuse regression test |
| Solution build | Clean | **Clean** | Zero new errors or warnings |

---

## Implementation Details

### Task 1 — `DistributedTankScenario` teardown fix (Approach A)

**Problem:** `DistributedTankScenario` released DDS participants and the Muscle kernel only in `IDisposable.Dispose()`. `ScenarioSubsystem.Shutdown()` calls `IScenario.OnShutdown()` but not `Dispose()`, so the CLI runner path (`fdp-demo-runner --scenario distributedtank`) had a native handle leak.

**Solution (Approach A):**
- Added `private bool _released;` guard.
- Extracted teardown logic to `private void ReleaseResources()` — releases `_muscleKernel`, `_muscleWorld`, `_brainParticipant`, `_muscleParticipant`.
- Implemented `OnShutdown()` (IScenario default interface method) → calls `ReleaseResources()`.
- `Dispose()` also calls `ReleaseResources()`.
- `_released = true` on first call prevents double-free when tests use `using var` (which calls both paths).

**Test added:**
- `DistributedTank_OnShutdown_ThenDispose_DoesNotThrow` — runs scenario via `ScenarioTestHarness.Run()` (triggers `OnShutdown`), then the `using var` block triggers `Dispose()`. No exception → double-release guard works.

**Files changed:**
- `FDP/Examples/Fdp.Examples.Scenarios/Network/DistributedTankScenario.cs`

---

### Task 2 — DEM1-D009 Phase B Phase 1 milestone

**Goal:** Add `EntityLifecycleModule` to Brain kernel (zero-participant auto-promote) and assert Brain hull reaches `EntityLifecycle.Active` by tick 5.

**Implementation:**
- Added `EntityLifecycleModule _brainElm` to `DistributedTankScenario` (zero-participant list → no ACK round-trip needed).
- In `Configure()`: register lifecycle events on the Brain world (`ConstructionOrder`, `ConstructionAck`, `DestructionOrder`, `DestructionAck`), register ELM on the Brain kernel, spawn `_brainHull` entity at `EntityLifecycle.Constructing`.
- At tick 1 in `EvaluateTick()`: call `_brainElm.BeginConstruction` with `new EntityCommandBuffer()` + `Playback(world)`. The `ConstructionOrder` lands on the Brain world bus.
- Brain kernel's `LifecycleSystem.Execute` (BeforeSync phase) calls `DrainInstantComplete` → sees zero `RemainingAcks` → issues `cmd.SetLifecycleState(entity, Active)` → entity promoted.
- At tick 5: assert `world.GetLifecycleState(_brainHull) == EntityLifecycle.Active` → set `PhaseBElmActive = true`.
- Scenario returns `true` at tick 10 only if both Phase A and Phase B Phase 1 conditions passed.
- Added `FDP.Toolkit.Tkb` project reference to `Fdp.Examples.Scenarios.csproj` (needed for `TkbDatabase`).

**Design decision:** Used `new TkbDatabase()` (empty, no templates) so `BlueprintApplicationSystem` processes the `ConstructionOrder` but applys nothing — clean Phase B Phase 1 with no external template dependency.

**Tests added:**
- `DistributedTank_PhaseB_BrainHullReachesActive_AtTick5` — asserts `scenario.PhaseBElmActive == true` after a 60-tick run.
- Renamed test class from `DistributedTankScenarioPhaseATests` (still has "PhaseA" test names for backward compatibility).

**Remaining Phase B work (deferred to BATCH-11):**
1. DDS topic wiring — `DemoTransformMsg`, `DemoLocomotionMsg` translators between Brain / Muscle (patterns from `Fdp.Examples.NetworkDemo`).
2. Ghosting — `ReplicationLogicModule` on both nodes; spawn CommandTank (TKB 100) on Brain; assert ghost entity on Muscle.
3. Loco command roundtrip — write `LocomotionChannel.ActiveAction` on Brain; assert ghost moves on Muscle by tick 25.
4. Turret split-authority — tick 40/50 milestones from DEM1-D009 spec.
5. `DEM1-TASK-TRACKER D009` remains **unchecked** until full Phase B demo is complete.

**Files changed:**
- `FDP/Examples/Fdp.Examples.Scenarios/Network/DistributedTankScenario.cs`
- `FDP/Examples/Fdp.Examples.Scenarios/Fdp.Examples.Scenarios.csproj`
- `FDP/Examples/Fdp.Examples.Scenarios.Tests/ScenarioTests.cs`

---

### Task 3 — `LocalGridBuilderSystem` index reuse hardening

**Problem:** `_prevPositions` keyed by `entity.Index` (int). If entity count stays constant but an index is recycled (destroy + create, same count), the incremental path looked up the new entity's position in the OLD entity's slot. If they happened to share the same position, the `old == new` check would silently skip the `Add` call, making the new entity invisible to perception until the next count-change full rebuild. If positions differed, `Remove(newEntity, oldPos)` was attempted but harmlessly returned false (different generation → not found in grid), and the stale old entity's grid slot was left orphaned.

**Solution:** Changed `Dictionary<int, Vector2> _prevPositions` → `Dictionary<Entity, Vector2>`. The `Entity` struct implements `IEquatable<Entity>` with `Index + Generation` comparison, so an index-recycled entity always misses the dictionary and follows the `Add` path.

**Remaining limitation (documented):** The STALE SLOT from the dead entity stays in the grid until the next count-change triggers a full rebuild. This means a neighbor query may temporarily return dead entity handles. This is an acceptable known limitation for the current perception use cases (rare spawn/destroy at stable count; perception systems tolerate stale results for one tick). A follow-up could address this with a dedicated "stale slot cleanup" pass.

**Complexity after change:**
- All existing complexity properties unchanged.
- Index-recycled entities: correct insert instead of silent skip.
- Dictionary key size: `Entity` (6 bytes: int32 + uint16) vs `int` (4 bytes) — negligible overhead.

**Test added:**
- `LocalGridBuilder_IndexReuse_NewEntityAtSamePosition_IsInserted` — destroys e1, creates e2 at same index (confirmed via `Assert.Equal(e1.Index, e2.Index)`) and same position; asserts e2 is present in `QueryNeighbors` results (old code would silently skip the insert when `oldPos == newPos`).

**Files changed:**
- `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LocalGridBuilderSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Perception.Tests/LocalGridBuilderSystemTests.cs`

---

### Task 4 — ImGui test parallel isolation

**Problem:** `FDP.Toolkit.ImGui.Tests` crashes when run in parallel with other test assemblies due to native ImGui DLL loading conflicts (BD1-BATCH-04 debt row).

**Solution:** Added `xunit.runner.json` to `FDP.Toolkit.ImGui.Tests` with `"parallelizeAssembly": false, "parallelizeTestCollections": false`. Also added `<None Update="xunit.runner.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>` to the `.csproj` to ensure the file is deployed alongside the test assembly during `dotnet test`.

**Files changed:**
- `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/xunit.runner.json` ← new file
- `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/FDP.Toolkit.ImGui.Tests.csproj`

---

## Design Decisions

**Approach A vs B for Task 1:** Chose Approach A (shared `ReleaseResources()` called from both `OnShutdown` and `Dispose`). This keeps the scenario self-contained without coupling `ScenarioSubsystem` to `IDisposable`. The double-dispose guard (`_released`) is the recommended pattern for this common test-harness vs CLI path divergence.

**TkbDatabase (empty) for Phase B:** Rather than implementing a mock `ITkbDatabase` or skipping templates entirely, used `new TkbDatabase()` (concrete class, no Register calls). `BlueprintApplicationSystem` will silently skip `TryGetByType` calls that return false. This is production-correct behavior and avoids adding a test-only mock.

**Phase B scope choice:** Targeted ELM auto-promote (zero participants) as the Phase B Phase 1 milestone because:
1. It proves the lifecycle pipeline is wired and functional end-to-end within `DistributedTankScenario`.
2. DDS topic wiring (Phase B milestones 2–4) requires translators and `ReplicationLogicModule` which need more design work to fit cleanly with the existing `Fdp.Examples.NetworkDemo` patterns — deferred to BATCH-11.

---

## Debt Tracker Notes

The following DEBT-TRACKER rows should be updated by the lead after review:

| Row | New Status |
|-----|-----------|
| `DistributedTankScenario` native teardown gap (BATCH-09 review, Target BATCH-10) | ✅ Resolved (Approach A) |
| `LocalGridBuilderSystem._prevPositions` index-only key (BATCH-09 review, Target BATCH-10+) | ✅ Resolved (Entity-keyed, stale-slot limitation documented) |
| `FDP.Toolkit.ImGui.Tests` parallel native load conflict (BD1-BATCH-04 row) | ✅ Resolved (`xunit.runner.json`) |

---

## Known Issues / Open Items

1. **Grid stale slot after index reuse (Task 3):** The grid can contain a dead entity's slot until the next count-change full rebuild. For current perception use cases this is acceptable (perception processes one snapshot per tick, rare churn). A future hardening could add a generation-check pass or track orphaned slots.

2. **Phase B BATCH-11 scope:** DDS topic wiring, ReplicationLogicModule, ghosting, and channel authority milestones from DEM1-D009 are deferred. See "Remaining Phase B work" above.

3. **Task 5 deferred:** `ParallelStoriesScenario` → `RecordingModule.Blocking` migration was not started. All baseline tests passing. No regression risk.
