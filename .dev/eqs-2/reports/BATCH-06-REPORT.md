# BATCH-06 REPORT

**Batch:** BATCH-06
**Tasks:** TASK-EQS-016 + TASK-EQS-017
**Status:** COMPLETE

---

## 1. Summary of Changes

### TASK-EQS-016 -- INavmeshProvider Interface and StubNavmeshProvider

**Files modified:**
- `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` -- added `INavmeshProvider = 212`
- `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs` -- added `SyncSingletonById(source, GlobalComponentIds.INavmeshProvider)` alongside existing ICoverProvider sync

**Files created:**
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/INavmeshProvider.cs` -- interface with `[ComponentId(GlobalComponentIds.INavmeshProvider)]`, three methods: `IsReachable`, `TryGetPathDistance`, `GetRandomPointsInRadius`
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/StubNavmeshProvider.cs` -- Phase 4 stub: always reachable, Euclidean path distance, 3x3 grid sampler
- `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/NavmeshProviderTests.cs` -- T-NP1, T-NP2

### TASK-EQS-017 -- NavmeshSamplesGenerator, NavmeshReachableTest, PathCostScoreTest

**Files created:**
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/NavmeshSamplesGenerator.cs` -- positional generator (EntityId=0), stackalloc intermediate buffer
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/NavmeshReachableTest.cs` -- FilterExpensive; skips -1L, rejects unreachable with -1L, sets flag bit 3
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/PathCostScoreTest.cs` -- ScoreExpensive; inverse-linear falloff, rejects with -1L when no path
- `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/NavmeshTests.cs` -- T-NS1, T-NR1, T-NR2, T-NR3, T-PC1, T-PC2
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/PathCostInversionTests.cs` -- T-PCI1

---

## 2. Test Results

### Unit Tests (FDP/Toolkits/Fdp.Toolkits.Tests)

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~Eqs"
Total tests: 35 -- Passed: 35
```

New tests (BATCH-06):

| Test | Status |
|------|--------|
| T-NP1: StubNavmeshProvider_IsReachable_AlwaysTrue | PASS |
| T-NP2: StubNavmeshProvider_TryGetPathDistance_ReturnsEuclidean | PASS |
| T-NS1: NavmeshSamplesGenerator_ProducesPositionalCandidates | PASS |
| T-NR1: NavmeshReachableTest_UnreachableCandidates_GetRejected | PASS |
| T-NR2: NavmeshReachableTest_ReachableCandidates_GetFlagBit3 | PASS |
| T-NR3: NavmeshReachableTest_SkipsAlreadyRejected | PASS |
| T-PC1: PathCostScoreTest_NoPath_RejectsCandidate | PASS |
| T-PC2: PathCostScoreTest_ShorterPathScoresHigher | PASS |

Existing tests (BATCH-01 through BATCH-05): all 27 still pass.

### Integration Tests (Hrot/Runner/Hrot.ClusterRunner.Integration.Tests)

```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs"
Total tests: 16 -- Passed: 16
```

New test (BATCH-06):

| Test | Status |
|------|--------|
| T-PCI1: PathCostInversion_BRankedFirst_CRejected | PASS |

Existing tests (BATCH-01 through BATCH-05): all 15 still pass.

---

## 3. Score Math Verification (T-PCI1)

SearchRadius = 60f. Observer at origin. Entities A at (0,5), B at (0,10), C at (0,2).

**MockNavmeshProvider path costs:**
- A at (0,5): Euclidean = 5, path cost = 50
- B at (0,10): Euclidean = 10, path cost = 10
- C at (0,2): unreachable

**Pipeline execution order:**
1. Generator (EntitiesInRadiusGenerator): A, B, C produced
2. FilterExpensive (NavmeshReachableTest): C rejected (EntityId=-1L); A and B get flag bit 3
3. ScoreCheap (DistanceScoreTest): inverse-linear from Euclidean distance
4. ScoreExpensive (PathCostScoreTest): inverse-linear from navmesh path distance

**Score calculation:**
- A: DistanceScore = 1 - (5/60) = 0.9167; PathScore = 1 - (50/60) = 0.1667; **Total = 1.083**
- B: DistanceScore = 1 - (10/60) = 0.8333; PathScore = 1 - (10/60) = 0.8333; **Total = 1.667**

B wins despite being farther in Euclidean space. The solver correctly inverts ranking when
navmesh path cost diverges from straight-line distance.

---

## 4. Deviations

None. Implementation matches BATCH-06-INSTRUCTIONS.md exactly.

---

## 5. Suggested Commit Message

```
feat(eqs): navmesh provider, reachable filter, path-cost scorer (BATCH-06)

EQS v1.3 Phase 4 navmesh integration:
- INavmeshProvider interface with [ComponentId(212)]; StubNavmeshProvider (Euclidean stub)
- GlobalComponentIds.INavmeshProvider = 212
- EntityRepository.Sync: SyncSingletonById for INavmeshProvider
- NavmeshSamplesGenerator: positional candidates via GetRandomPointsInRadius (stackalloc)
- NavmeshReachableTest (FilterExpensive): rejects unreachable (-1L), sets flag bit 3
- PathCostScoreTest (ScoreExpensive): inverse-linear path cost, rejects on no-path
- 8 new unit tests (T-NP1, T-NP2, T-NS1, T-NR1, T-NR2, T-NR3, T-PC1, T-PC2) -- 35 pass
- 1 new integration test T-PCI1 (PathCostInversion) -- 16 pass

TASK-EQS-016: DONE
TASK-EQS-017: DONE
```
