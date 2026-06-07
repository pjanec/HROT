# BATCH-05 REPORT

## Summary

Batch implements **TASK-EQS-012** (`CoverPoint`, `ICoverProvider`, `ManualCoverProvider`),
**TASK-EQS-013** (`CoverPointsGenerator`, `ILosService`, `BlockedLosService`,
`CheapLineOfSightTest`), and **TASK-EQS-015** (`FindCoverFromTarget` EQS template).
Two cross-cutting fixes were also required: adding `ICoverProvider` to the SoD singleton
sync table, and stamping all EQS integration test classes with a shared
`[Collection("EqsIntegrationTests")]` to eliminate CPU-contention timeouts.

---

## TASK-EQS-012 -- CoverPoint, ICoverProvider, ManualCoverProvider

### Files Created

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CoverPoint.cs`**
- `[StructLayout(LayoutKind.Sequential)]` unmanaged struct: fields PositionX, PositionY,
  DirectionX, DirectionY (float), Quality (float), StanceHeight (byte), _pad0 (byte),
  _pad1 (ushort) = 24 bytes total.
- Namespace: `Fdp.Toolkit.Spatial.Eqs`.

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/ICoverProvider.cs`**
- Interface `ICoverProvider` with single method
  `int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results)`.
- Decorated `[ComponentId(GlobalComponentIds.ICoverProvider)]` so it can be stored in the
  managed singleton slot (required by `EntityRepository.SetSingletonManaged<T>`).
- `using Fdp.Core;` added to satisfy `ComponentId` resolution.

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/ManualCoverProvider.cs`**
- Implements `ICoverProvider` with a flat `CoverPoint[]` array and linear scan.
- Radius check: `dx*dx + dy*dy <= radius*radius`.

**`FDP/Engine/Fdp.Core/GlobalComponentIds.cs`** (modified)
- Added `public const int ICoverProvider = 211;` after `IEqsTemplateRegistry = 210`.
- Updated the range comment to `IDs 212-255 are reserved`.

**`FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/CoverProviderTests.cs`**
- Two unit tests (`T-CP1`, `T-CP2`).

### Tests

| ID | Name | Result |
|----|------|--------|
| T-CP1 | `CoverPoint_IsExactly24Bytes` | PASS |
| T-CP2 | `ManualCoverProvider_RadiusFilter_ReturnsOnlyPointsWithinRadius` | PASS |

---

## TASK-EQS-013 -- CoverPointsGenerator, ILosService, CheapLineOfSightTest

### Files Created

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/ILosService.cs`**
- Interface `ILosService { bool HasCheapLineOfSight(Vector2 observer, Vector2 target); }`.
- `BlockedLosService : ILosService` always returns `false` (all positions are occluded).

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CoverPointsGenerator.cs`**
- Implements `IEqsGenerator.Generate`.
- Guards: `HasSingletonManaged<ICoverProvider>()`, `HasComponent<SimTransform>()`.
- Uses `stackalloc CoverPoint[candidates.Length]` for zero-heap intermediate buffer.
- Seeds `EqsResult.Score = rawPoints[i].Quality`, `Flags = rawPoints[i].StanceHeight`.
- Returns positional candidates (`EntityId = 0`).

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CheapLineOfSightTest.cs`**
- Implements `IEqsTest` (Phase: `FilterCheap`).
- Method `public unsafe void ExecuteBatch(...)` to access `TargetMemory` fixed arrays.
- Bypass conditions: `mem.Count == 0` OR `mem.ThreatScores[0] < sensor.ThreatThreshold`.
- When NOT bypassing: rejects with `EntityId = -1L` if `los.HasCheapLineOfSight` returns
  `true` (candidate is exposed). Sets `Flags |= 1` when candidate is occluded (blocked).
- Skips already-rejected candidates (`EntityId == -1L`).

**`FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/CoverGeneratorAndLosTests.cs`**
- Five unit tests (`T-CG1`, `T-LOS1` through `T-LOS4`).

### Tests

| ID | Name | Result |
|----|------|--------|
| T-CG1 | `CoverPointsGenerator_ProducesPositionalCandidates` | PASS |
| T-LOS1 | `CheapLineOfSightTest_BypassesWhenNoThreats` | PASS |
| T-LOS2 | `CheapLineOfSightTest_BypassesBelowThreatThreshold` | PASS |
| T-LOS3 | `CheapLineOfSightTest_RejectsExposedCandidates` | PASS |
| T-LOS4 | `CheapLineOfSightTest_KeepsOccludedCandidates_SetsFlagBit0` | PASS |

---

## TASK-EQS-015 -- FindCoverFromTarget Template

### Files Created

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/FindCoverFromTarget.cs`**
- `[EqsTemplate("f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d")]` static class.
- `public const uint BlueprintId = 0x7F3A2B1Cu;`
- `Build(ILosService los)` factory returns:
  - Generator: `CoverPointsGenerator`
  - FilterCheap: `CheapLineOfSightTest(los)`
  - ScoreCheap: `DistanceScoreTest`
  - MaxCandidates: 32

**`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/FindCoverFromTargetTests.cs`**
- Two integration tests using `EditorHarness` + `PumpUntil` (T-FCT1, T-FCT2).
- `MockLosService`: returns exposed for `from.X > 2f` (east of x=2).
- `SimpleEqsTemplateRegistry`: in-memory dictionary-backed registry.

### Tests

| ID | Name | Result |
|----|------|--------|
| T-FCT1 | `FindCoverFromTarget_FullPipeline_TwoOccludedPointsSurvive` | PASS |
| T-FCT2 | `FindCoverFromTarget_BypassWhenNoThreats_AllPointsSurvive` | PASS |

---

## Cross-Cutting Fixes

### 1. ICoverProvider SoD Singleton Sync

**File:** `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs`

The background EQS solver runs on a SoD (Separation of Duties) snapshot repository.
`IEqsTemplateRegistry` (ID 210) was already synced to the snapshot via
`SyncSingletonById`; `ICoverProvider` (ID 211) was not. Without it, the background
solver's `HasSingletonManaged<ICoverProvider>()` returned false and
`CoverPointsGenerator` produced zero candidates, causing T-FCT1 and T-FCT2 to time out.

**Fix:** Added `SyncSingletonById(source, GlobalComponentIds.ICoverProvider);` alongside
the existing `IEqsTemplateRegistry` call.

### 2. EQS Integration Test Collection

**Files:** All 6 `.cs` files under
`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/`

Without a shared collection, xUnit ran all 15 EQS integration tests in parallel. Each
test creates an `EditorHarness` with a background EQS solver thread (10 Hz), so 15
concurrent solver threads saturated the thread pool, causing timing-sensitive tests with
6-8 second outer timeouts to miss their `PumpUntil` windows. This pre-existing latent
flaw was exposed by BATCH-05 tests completing faster (< 300 ms each) and increasing
contention during the test window.

**Fix:** Added `[Collection("EqsIntegrationTests")]` to all 6 test classes, matching the
`[Collection("HeavyE2ETests")]` / `[Collection("EditorOfflineTests")]` pattern used
elsewhere in the project.

Additionally, `EqsSolverSystemTests.EqsSolverSystem_Phase1Stub_PopulatesBufferAfterSolverFires`
had a `PumpUntil(timeoutMs: 2000)` that was tighter than the 5000 ms used in every
other EQS integration test. This was corrected to `timeoutMs: 5000` for consistency.

---

## Full Test Results

### Unit Tests -- `Fdp.Toolkits.Tests` (EQS filter)

27 tests total: 20 pre-existing + 7 new (T-CP1, T-CP2, T-CG1, T-LOS1 to T-LOS4).

| # | Test | Result |
|---|------|--------|
| 1 | T-CP1 `CoverPoint_IsExactly24Bytes` | PASS |
| 2 | T-CP2 `ManualCoverProvider_RadiusFilter_ReturnsOnlyPointsWithinRadius` | PASS |
| 3 | T-CG1 `CoverPointsGenerator_ProducesPositionalCandidates` | PASS |
| 4 | T-LOS1 `CheapLineOfSightTest_BypassesWhenNoThreats` | PASS |
| 5 | T-LOS2 `CheapLineOfSightTest_BypassesBelowThreatThreshold` | PASS |
| 6 | T-LOS3 `CheapLineOfSightTest_RejectsExposedCandidates` | PASS |
| 7 | T-LOS4 `CheapLineOfSightTest_KeepsOccludedCandidates_SetsFlagBit0` | PASS |
| 8-27 | (all 20 pre-existing EQS unit tests) | PASS |

**Total: 27/27 PASS**

### Integration Tests -- `Hrot.ClusterRunner.Integration.Tests` (EQS filter)

15 tests total: 13 pre-existing + 2 new (T-FCT1, T-FCT2).

| # | Test | Result |
|---|------|--------|
| 1 | T-FCT1 `FindCoverFromTarget_FullPipeline_TwoOccludedPointsSurvive` | PASS |
| 2 | T-FCT2 `FindCoverFromTarget_BypassWhenNoThreats_AllPointsSurvive` | PASS |
| 3-15 | (all 13 pre-existing EQS integration tests) | PASS |

**Total: 15/15 PASS**

### Full Solution Build

`dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` -- **Build succeeded. 0 errors.**

---

## Design Decisions

### `[ComponentId]` on `ICoverProvider` interface

`EntityRepository.SetSingletonManaged<T>()` reflects on `T` at registration time to find
a `[ComponentId]` attribute that maps the type to a stable integer slot. This is the same
requirement that `IEqsTemplateRegistry` satisfies with `[ComponentId(210)]`. Without it
the runtime throws `InvalidOperationException: Component type 'ICoverProvider' is missing
a [ComponentId] attribute`. The constant `ICoverProvider = 211` was appended to
`GlobalComponentIds.cs`, and the attribute was placed on the interface rather than the
concrete implementation because the singleton is stored and retrieved by the interface
type.

### `unsafe void ExecuteBatch` on `CheapLineOfSightTest`

`TargetMemory` is an `unsafe struct` with `fixed float[]` buffers (`ThreatScores`,
`PositionsX`, `PositionsY`). Accessing them requires an `unsafe` method context.
The pattern follows `ThreatEvaluationSystem.Execute` from BATCH-03. The `unsafe` scope
is confined to `ExecuteBatch` only.

### Positional constructor for `[EqsTemplate]`

`EqsTemplateAttribute` exposes `AssetId` as a `string` property on a get-only auto
property backed by a constructor parameter. Named-parameter syntax
(`[EqsTemplate(AssetId = "...")]`) therefore does not compile. The positional constructor
form `[EqsTemplate("f8a3c1d2-...")]` is the correct usage.

### Score seeding from `CoverPoint.Quality`

`CoverPointsGenerator` seeds `EqsResult.Score = rawPoints[i].Quality` before handing
candidates to the pipeline. `DistanceScoreTest` (a `ScoreCheap` stage) adds a
distance-based component on top. This gives cover quality a baseline influence on
ranking even if two points are equidistant.

---

## Deviations from Instructions

| Deviation | Reason |
|-----------|--------|
| `[ComponentId(211)]` on `ICoverProvider` not mentioned in instructions | Required at runtime; without it `SetSingletonManaged<ICoverProvider>()` throws. Pattern is identical to `IEqsTemplateRegistry`. |
| `SyncSingletonById(ICoverProvider)` added to `EntityRepository.Sync.cs` | Required for the background SoD snapshot to see the cover database; analogous to the `IEqsTemplateRegistry` sync added in BATCH-04. |
| `[Collection("EqsIntegrationTests")]` on all 6 EQS test classes | Pre-existing latent flaw exposed by faster BATCH-05 tests increasing thread-pool contention. Matches pattern used in the wider integration test project. |
| `PumpUntil(timeoutMs: 5000)` on Phase1Stub (was 2000) | Pre-existing too-tight timeout; all other EQS integration tests use 5000 ms. |

---

## Suggested Commit Message

```
feat(eqs): cover points, LOS filter, FindCoverFromTarget template (BATCH-05)

- Add CoverPoint struct, ICoverProvider interface, ManualCoverProvider (TASK-EQS-012)
- Add CoverPointsGenerator, ILosService, BlockedLosService, CheapLineOfSightTest (TASK-EQS-013)
- Add FindCoverFromTarget EQS query template (TASK-EQS-015)
- Register ICoverProvider (ID 211) in GlobalComponentIds and add [ComponentId] attribute
- Sync ICoverProvider singleton to SoD snapshot in EntityRepository.Sync.cs
- Stamp all EQS integration test classes with [Collection("EqsIntegrationTests")]
  to prevent CPU-contention timeouts when running in parallel
- Fix Phase1Stub PumpUntil timeout from 2000 ms to 5000 ms for consistency

Tests: 27/27 unit + 15/15 integration PASS; full solution build succeeds.
```
