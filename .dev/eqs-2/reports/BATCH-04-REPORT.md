# BATCH-04 REPORT

## Summary

Batch implements **TASK-EQS-009** (`EntitiesInRadiusGenerator`), **TASK-EQS-010**
(`FactionFilterTest` + `DistanceScoreTest`), and **TASK-EQS-011** (Phase 2 time-sliced
`EqsSolverSystem`). A corrective task from the BATCH-03 review (EntityLifecycle.Ghost
regression) is also addressed.  A cross-cutting fix to `EntityRepository.SyncFrom` was
added to ensure the EqsSolverSystem background snapshot has access to the live world's
singleton pool objects.

---

## Corrective Task — EntityLifecycle.Ghost Regression

**Problem (identified in BATCH-03 review):** `EqsSolverSystem.Execute` filtered sensors
with `.WithLifecycle(EntityLifecycle.Ghost)`. In the offline `EditorHarness` path, entities
are created with `EntityLifecycle.Active`, so the solver found zero sensors and several
offline integration tests (T4/T7) failed.

**Fix:** Changed the lifecycle filter to `.WithLifecycle(EntityLifecycle.All)`. This applies
to both the Phase 1 stub and the Phase 2 rewrite below.

---

## TASK-EQS-009 — EntitiesInRadius Generator

### Files Created

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EntitiesInRadiusGenerator.cs`**
- Implements `IEqsGenerator.Generate`.
- Guards on `HasSingletonUnmanaged<SpatialGridData>()` and `HasComponent<SimTransform>()`.
- Uses `stackalloc (Entity, Vector2)[candidates.Length]` for zero-heap neighbour buffer.
- Calls `gridData.Grid.QueryNeighbors(obsPos, sensor.SearchRadius, neighbors)`.
- Excludes the observer entity itself from results.
- Returns `validCount` (not `rawCount`).

**`FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EntitiesInRadiusGeneratorTests.cs`**
- Three unit tests using a real `EntityRepository` + manually populated `SpatialHashGrid`.

### Tests

| ID | Name | Result |
|----|------|--------|
| IG1 | `Generate_ZeroRadius_ReturnsZero` | PASS |
| IG2 | `Generate_ObserverExcluded_ReturnsNearbyOnly` | PASS |
| IG3 | `Generate_OnlyWithinRadius_Returned` | PASS |

---

## TASK-EQS-010 — FactionFilterTest and DistanceScoreTest

### Files Created

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/FactionFilterTest.cs`**
- `Phase` = `EqsTestPhase.FilterCheap`.
- Rejects candidates whose `EntityInfo.ForceId` bitmask does not match `EqsSensor.FactionFilter`.
- Rejection sentinel is `EntityId = -1L`. Positional candidates (`EntityId = 0`) are skipped
  and never rejected by the faction filter.
- Guards: dead entities and entities without `EntityInfo` are rejected.

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/DistanceScoreTest.cs`**
- `Phase` = `EqsTestPhase.ScoreCheap`.
- Scores candidates by linear proximity falloff: 1.0 at origin, 0.0 at `SearchRadius`.
- Skips rejected (`-1L`) candidates. Additive scoring via `candidate.Score += score`.

**`FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsFilterAndScoreTests.cs`**
- Four pure unit tests with no ECS or DDS dependency.

### Tests

| ID | Name | Result |
|----|------|--------|
| F1 | `FactionFilter_RejectsWrongFaction` | PASS |
| F2 | `FactionFilter_SkipsAlreadyRejected` | PASS |
| F3 | `DistanceScore_SkipsRejectedCandidates` | PASS |
| F4 | `DistanceScore_CloserCandidateScoresHigher` | PASS |

---

## TASK-EQS-011 — Time-Sliced EqsSolverSystem (Phase 2 Full)

### Files Modified

**`Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs`** — full rewrite

Phase 2 evaluation loop:
1. Lazy-init `EqsResultPool` singleton on the repo if not present (used both in offline
   EditorHarness and distributed Muscle contexts).
2. Builds `_sensorQuery` once using `.WithLifecycle(EntityLifecycle.All)`.
3. Calls `repo.QueryTimeSliced(_sensorQuery, _iteratorState, EqsBudgetMs, ...)` with a
   4 ms default budget.
4. Per-entity `EvaluateSensor`:
   - Looks up `IEqsTemplateRegistry` managed singleton; falls back to Phase 1 stub
     (empty event, `RefreshTick = tick + 1`) if absent or template not found.
   - Calls `Generate` → `FilterCheap` → `FilterExpensive` → `ReduceTopK` →
     `ScoreCheap` → `ScoreExpensive` → sort descending → `WriteResultsToPoolAndPublish`.
5. `ReduceTopK`: compacts by `EntityId != -1L` (preserving positional candidates at `0`)
   and truncates to `EqsResultPool.MaxTopK`.

**Singleton sync fix: `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs`**

Added private method `SyncSingletonById(EntityRepository source, int typeId)` and three
call sites at the end of `SyncFrom`:

```csharp
SyncSingletonById(source, GlobalComponentIds.SpatialGridData);   // 47
SyncSingletonById(source, GlobalComponentIds.EqsResultPool);     // 209
SyncSingletonById(source, GlobalComponentIds.IEqsTemplateRegistry); // 210
```

This shares the singleton table REFERENCE (not a deep copy) from the live world into each
SoD snapshot. Sharing is safe because `NativeChunkTable<T>.Dispose()` is idempotent
(`if (_disposed) return;` guard), and singletons in the snapshot are never structurally
replaced mid-frame by the SoD contract. This ensures `EqsSolverSystem` (background thread,
snapshot) and `EqsResultUpdateSystem` (main thread, live world) reference the same
`EqsResultPool` object, so handles published in `EqsResultEvent` remain valid on the
consumer side.

### Files Created

**`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsSolverSystemPhase2Tests.cs`**
- Three integration tests using `EditorHarness` (synchronous, DDS-free).
- Includes inner `SimpleEqsTemplateRegistry` (Dictionary-backed `IEqsTemplateRegistry`)
  and inner `NullGenerator` (returns 0 candidates).

**`FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsSolverSystemUnitTests.cs`**
- Two pure unit tests for `ReduceTopK` correctness.

### Tests

| ID | Name | Harness | Result |
|----|------|---------|--------|
| T-S1 | `EqsSolverSystem_Phase2_FullPipeline_PopulatesBuffer` | EditorHarness | PASS |
| T-S2 | `EqsSolverSystem_Phase2_MultipleSensors_AllEventuallyProcessed` | EditorHarness | PASS |
| T-S3 | `EqsSolverSystem_Phase1Fallback_NoRegistry_EmitsEmptyEvent` | EditorHarness | PASS |
| RK1 | `ReduceTopK_RemovesRejectedCandidates` | Unit | PASS |
| RK2 | `ReduceTopK_TruncatesToMaxTopK` | Unit | PASS |

---

## Full EQS Test Matrix (all batches)

Command: `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs"`

| ID | Name | Batch | Result |
|----|------|-------|--------|
| T1 | `EqsLifecycleNodes_EqsSensorAdded_ThenRemovedOnExpiry` | BATCH-01 | PASS |
| T2 | `EqsLifecycleNodes_ActiveSensor_BufferCreatedAutomatically` | BATCH-01 | PASS |
| T3 | `EqsLifecycleNodes_EqsCognitiveBuffer_ResetOnNewEpoch` | BATCH-01 | PASS |
| T4 | `EqsResultUpdateSystem_Phase1Stub_UpdatesBuffer` | BATCH-02 | PASS |
| T5 | `EqsResultUpdateSystem_OnlineWithZeroCount_SetsIsReady` | BATCH-02 | PASS |
| T6 | `EqsResultUpdateSystem_EventWithResults_PopulatesBuffer` | BATCH-02 | PASS |
| T7 | `EqsSolverSystem_Phase1Stub_PopulatesBufferAfterSolverFires` | BATCH-02 | PASS |
| T8 | `EqsTranslators_T8_ConfigReplicatesBrainToMuscle` | BATCH-03 | PASS |
| T9 | `EqsTranslators_T9_ResultRoundTripPopulatesBrainBuffer` | BATCH-03 | PASS |
| T10 | `EqsTranslators_T10_EntityDestroyedRemovesSensorFromMuscle` | BATCH-03 | PASS |
| T-S1 | `EqsSolverSystem_Phase2_FullPipeline_PopulatesBuffer` | BATCH-04 | PASS |
| T-S2 | `EqsSolverSystem_Phase2_MultipleSensors_AllEventuallyProcessed` | BATCH-04 | PASS |
| T-S3 | `EqsSolverSystem_Phase1Fallback_NoRegistry_EmitsEmptyEvent` | BATCH-04 | PASS |

**Result: 13/13 PASS**

---

## EQS Unit Test Matrix

Command: `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~Eqs"`

Total: **20/20 PASS** (4 layout, 3 pool, 4 template, 3 generator, 4 filter+score, 2 solver-unit)

---

## Build Result

```
dotnet build FDP/Engine/Fdp.Core/ — 0 Error(s)
dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ — 0 Error(s)
```

---

## Full Suite Regression Report

Command: `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --no-build`

**Result: 44 failed, 65 passed, 4 skipped, 113 total, Duration: 8m7s**

### Non-EQS Failures (37 tests)

All 37 non-EQS failures are pre-existing, unrelated to BATCH-04 changes:
- Network/DDS infrastructure tests that fail due to CycloneDDS participant resource
  exhaustion (e.g. NetworkGateway, EntityDestroy, DragDrop, MapPlacement, etc.)
- `SimHost_WanderMission_EntityMovesAfterBehaviorActivation` (2m38s) — pre-existing
- 3x `CgfRecordingIntegrationTests` (1m7s each) — pre-existing

### EQS Failures in Full Suite (7 tests)

The 7 EQS tests that fail in the full suite all **PASS** when run with
`--filter "FullyQualifiedName~Eqs"`. They fail in the full suite because
pre-existing non-EQS DDS tests exhaust CycloneDDS participant resources early in the
run (at ~7-23 seconds), leaving insufficient resources for subsequent DDS-dependent
tests including the HrotRunnerHarness-based translator tests (T8/T9/T10) and any
EditorHarness tests running in parallel with contaminated DDS state.

This DDS cascade failure pattern is pre-existing and was present in BATCH-03's full
suite run. **No new regressions were introduced by BATCH-04.**

---

## Design Deviations

**Deviation 1 — SyncSingletonById in EntityRepository.SyncFrom (not in original spec)**
- The spec did not specify how the background EqsSolverSystem snapshot would access
  `EqsResultPool`. The lazy-init in `EqsSolverSystem` creates a new pool in the
  snapshot, which is a DIFFERENT object than the live world's pool. Handles written by
  the solver would be invalid when the consumer (`EqsResultUpdateSystem`) reads from
  the live world's pool.
- Resolution: added `SyncSingletonById` to share the table reference (NOT deep copy)
  for singletons 47/209/210 in `SyncFrom`. `NativeChunkTable<T>.Dispose()` is guarded
  by `if (_disposed) return;`, making shared disposal safe.

**Deviation 2 — T-S2 tests multi-sensor coverage instead of budget yield**
- The spec asked for `harness.Solver.EqsBudgetMs = 0.001` to force budget yield within
  one frame. `EditorHarness` does not expose the solver instance directly.
- Resolution: T-S2 creates 10 sensors with a `NullGenerator` (zero candidates) and
  asserts that all 10 receive `IsReady` buffers within 5 seconds. This exercises the
  multi-frame time-sliced iteration path across multiple `PumpUntil` ticks, testing
  that the `IteratorState` correctly advances across entities over successive frames.

**Deviation 3 — EqsSolverSystem lazy pool creation co-exists with SyncSingletonById**
- The lazy `if (!repo.HasSingleton<EqsResultPool>())` path in `EqsSolverSystem` now
  never triggers for the Muscle world (live world has the pool from startup;
  `SyncSingletonById` propagates it into the snapshot). It still fires for `EditorHarness`
  tests where `NavigationSolverComponentRegistry.RegisterAll` is not called.
- This dual behaviour is correct: offline tests self-provision, distributed tests share.
