# BATCH-10 INSTRUCTIONS

## Tasks
- **EQS-023** (partial) -- Offline round-trip test only
- **EQS-024** -- Top-K reduction and positional sentinel preservation
- **EQS-029** -- TargetMemory threat threshold bypassing

> EQS-023 distributed (HrotRunnerHarness) test is deferred to BATCH-11.

## References
- Task specs: `.dev/eqs-2/TASK-DETAIL.md` sections TASK-EQS-023, TASK-EQS-024, TASK-EQS-029
- Implementation details: `.dev/eqs-2/IMPLEM_DETAILS.md` L:2060-2200 (round-trip), L:2208-2285 (TopK), L:2643-2740 (threat threshold)
- Existing pattern (EditorHarness): `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/FindCoverFromTargetTests.cs`
- Existing pattern (AccurateLos): `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/AccurateLosPhaseTests.cs`
- Task tracker: `.dev/eqs-2/TASK-TRACKER.md`

## Constraints
- ASCII only -- no Unicode in comments or strings
- Do NOT reformat unrelated code
- Build must succeed with 0 errors before reporting
- All tests: `[Collection("EqsIntegrationTests")]` (no separate collection fixture needed)
- Tests go in ONE new file: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsRoundTripTests.cs`
- Use `[Fact(Timeout = 8_000)]` for all tests
- Follow the exact fixture and disposal pattern from `FindCoverFromTargetTests.cs`
- Do NOT use `SpawnEntityCommand` or TKB -- create entities directly with `_harness.Repo.CreateEntity()`

---

## Architecture notes (read before coding)

All tests are pure offline (`EditorHarness`). The EditorHarness already has `EqsModule` registered.

After every test that uses `EqsResultPool`, dispose must include:
```csharp
if (_harness.Repo.HasSingleton<EqsResultPool>())
{
    var pool = _harness.Repo.GetSingleton<EqsResultPool>();
    if (pool.Results.IsCreated) pool.Results.Dispose();
}
```

Template resolution pattern (copy from AccurateLosPhaseTests):
```csharp
private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
{
    private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
    public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
    public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
}
```

Entity creation pattern:
```csharp
var entity = _harness.Repo.CreateEntity();
_harness.Repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
_harness.Repo.AddComponent(entity, new NetworkIdentity { Value = 9001L });
_harness.Repo.AddComponent(entity, new EqsSensor { BlueprintId = <id>, Epoch = 1, SearchRadius = 50f, ThreatThreshold = 0f });
```

---

## 10-A: EQS-023 offline test

### Inner classes (private to `EqsRoundTripTests`)

**MockCoverProvider:**
```csharp
private sealed class MockCoverProvider : ICoverProvider
{
    public int GetCoverPointsInRadius(
        System.Numerics.Vector2 center, float radius, Span<CoverPoint> results)
    {
        if (results.Length < 2) return 0;
        results[0] = new CoverPoint { PositionX = center.X,       PositionY = center.Y + 5f };
        results[1] = new CoverPoint { PositionX = center.X + 5f,  PositionY = center.Y      };
        return 2;
    }
}
```

**MockNavmeshProvider:**
```csharp
private sealed class MockNavmeshProvider : INavmeshProvider
{
    public bool IsReachable(System.Numerics.Vector2 start, System.Numerics.Vector2 end) => true;
    public bool TryGetPathDistance(
        System.Numerics.Vector2 start, System.Numerics.Vector2 end, out float distance)
    {
        distance = System.Numerics.Vector2.Distance(start, end);
        return true;
    }
    public int GetRandomPointsInRadius(
        System.Numerics.Vector2 center, float radius, Span<System.Numerics.Vector2> points)
    {
        if (points.Length < 1) return 0;
        points[0] = new System.Numerics.Vector2(center.X + radius * 0.5f, center.Y);
        return 1;
    }
}
```

### Test T-RT1

**Name:** `Eqs_OfflineEditor_PopulatesCognitiveBufferWithCandidates`

Setup:
1. Register `MockCoverProvider` and `MockNavmeshProvider` as singletons on `_harness.Repo`
2. Register a template that uses `CoverPointsGenerator` (no filter/score tests)
3. Choose a distinct `blueprintId` (e.g., `91u`)
4. Create observer entity: `SimTransform` + `NetworkIdentity` + `EqsSensor`

Template:
```csharp
registry.Register(new EqsQueryTemplate
{
    BlueprintId = blueprintId,
    Generator   = new CoverPointsGenerator(),
    MaxCandidates = 8,
});
```

Pump: `_harness.PumpUntil(() => ... .IsReady, timeoutMs: 5000)`

Assertions:
1. `ready == true` (buffer becomes ready)
2. `buffer.Count > 0` (at least one candidate returned by mock provider)
3. `buffer.GetTop().EntityId == 0` (positional candidate, not entity-shaped)

---

## 10-B: EQS-024 TopK test

### Inner classes for EQS-024 (private to `EqsRoundTripTests`)

**DeterministicPositionalGenerator:**
```csharp
// Yields exactly 5 positional candidates at PositionX = 10, 20, 30, 40, 50.
// PositionY = 0. EntityId = 0 for all.
private sealed class DeterministicPositionalGenerator : IEqsGenerator
{
    private static readonly float[] Xs = { 10f, 20f, 30f, 40f, 50f };

    public int Generate(Entity observer, ref EqsSensor sensor,
        ISimulationView view, Span<EqsResult> candidates)
    {
        int count = Math.Min(Xs.Length, candidates.Length);
        for (int i = 0; i < count; i++)
            candidates[i] = new EqsResult { EntityId = 0L, PositionX = Xs[i], PositionY = 0f };
        return count;
    }
}
```

**SentinelRejectionFilterTest:**
```csharp
// Rejects candidates at indices 1 and 3 (X=20 and X=40) by setting EntityId = -1L.
private sealed class SentinelRejectionFilterTest : IEqsTest
{
    public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

    public void ExecuteBatch(Entity observer, ref EqsSensor sensor,
        ISimulationView view, Span<EqsResult> candidates)
    {
        if (candidates.Length > 1) candidates[1].EntityId = -1L;
        if (candidates.Length > 3) candidates[3].EntityId = -1L;
    }
}
```

**DummyScoreTest:**
```csharp
// Verifies the span it receives has exactly 3 entries and none are -1L.
// Records a flag if the assertion holds (checked by the test after pump).
private sealed class DummyScoreTest : IEqsTest
{
    public EqsTestPhase Phase => EqsTestPhase.ScoreCheap;
    public bool AssertionPassed { get; private set; }

    public void ExecuteBatch(Entity observer, ref EqsSensor sensor,
        ISimulationView view, Span<EqsResult> candidates)
    {
        AssertionPassed = candidates.Length == 3;
        if (AssertionPassed)
        {
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i].EntityId == -1L) { AssertionPassed = false; break; }
        }
    }
}
```

**IMPORTANT:** `DummyScoreTest.ExecuteBatch` is called by the solver with the post-reduction span. Verify after pump that `scorer.AssertionPassed == true`.

### Test T-RT2

**Name:** `Eqs_TopKReduction_PreservesPositionalSentinels`

Setup:
1. Instantiate `DummyScoreTest scorer = new DummyScoreTest()`
2. Register a template:
   ```csharp
   registry.Register(new EqsQueryTemplate
   {
       BlueprintId  = blueprintId,
       Generator    = new DeterministicPositionalGenerator(),
       FilterCheap  = new IEqsTest[] { new SentinelRejectionFilterTest() },
       ScoreCheap   = new IEqsTest[] { scorer },
       MaxCandidates = 8,
   });
   ```
3. No providers needed (generator is self-contained)
4. Create observer with `SimTransform` + `NetworkIdentity` + `EqsSensor`
5. Pump until `IsReady`

**Note:** The template field names are `FilterCheap`, `ScoreCheap`, etc., matching `EqsTestPhase` enum names. Check the actual `EqsQueryTemplate` struct field names before coding (read `EqsQueryTemplate.cs`).

Assertions after pump:
1. `buffer.Count == 3` (exact)
2. `buffer.GetSpanRO()[0].EntityId == 0` && `[1].EntityId == 0` && `[2].EntityId == 0`
3. X-coordinates are exactly {10f, 30f, 50f} in the buffer (any order -- the solver may reorder by score). Check that the set of X-values matches.
4. `scorer.AssertionPassed == true` (proves ReduceTopK ran before scoring)

For assertion 3, use a set check:
```csharp
var xs = new HashSet<float>();
var span = buffer.GetSpanRO();
for (int i = 0; i < buffer.Count; i++) xs.Add(span[i].PositionX);
Assert.Contains(10f, xs);
Assert.Contains(30f, xs);
Assert.Contains(50f, xs);
```

---

## 10-C: EQS-029 threat threshold test

### Inner classes for EQS-029 (private to `EqsRoundTripTests`)

**ExposedLosServiceMock:**
```csharp
// HasCheapLineOfSight always returns true (all candidates exposed to threat).
private sealed class ExposedLosServiceMock : ILosService
{
    public bool HasCheapLineOfSight(
        System.Numerics.Vector2 from, System.Numerics.Vector2 to) => true;
}
```

### Tests T-RT3a and T-RT3b (two separate `[Fact]` methods)

**T-RT3a -- `Eqs_ThreatThreshold_AboveThreshold_RejectsAllExposedCandidates`:**

Setup:
- `ManualCoverProvider` with 1 cover point at (5f, 0f)
- `ExposedLosServiceMock` los (always exposed)
- Template: `CoverPointsGenerator` + `CheapLineOfSightTest(los)`, no score tests
- Observer: `SimTransform` + `NetworkIdentity` + `EqsSensor` with `ThreatThreshold = 50f`
- `TargetMemory` with `Count = 1`, `ThreatScores[0] = 100f`, `PositionsX[0] = 30f`, `PositionsY[0] = 0f`

**Important:** `TargetMemory` has fixed-size arrays (unsafe). Use the same pattern as `AccurateLosPhaseTests.cs` `CreateTestObserver`:
```csharp
var mem = new TargetMemory();
unsafe
{
    mem.Count           = 1;
    mem.ThreatScores[0] = 100f;
    mem.PositionsX[0]   = 30f;
    mem.PositionsY[0]   = 0f;
}
_harness.Repo.AddComponent(observer, mem);
```

Pump until `IsReady`.

Assertion: `buffer.Count == 0` (all cover exposed, rejected by `CheapLineOfSightTest`)

**T-RT3b -- `Eqs_ThreatThreshold_BelowThreshold_BypassesFilter`:**

Same setup but `ThreatScores[0] = 10f` (below threshold of 50f).
`EqsSensor.ThreatThreshold = 50f` (unchanged).

Since `10f < 50f`, `CheapLineOfSightTest` should bypass → cover point survives.

Pump until `IsReady`.

Assertion: `buffer.Count == 1`

**Note:** These two tests must use separate harness instances. Add a second `EditorHarness` field or create them as separate `[Fact]` methods within the same fixture, each creating a new entity. The simplest approach: since the fixture creates one harness per class, put T-RT3a and T-RT3b in separate classes or create the entities in independent test methods that reset state. Actually, given the harness is shared per fixture, different blueprint IDs must be used or each test creates its own entity. Check `FindCoverFromTargetTests.cs` - each test creates its own entities with independent state.

**Recommended approach:** Use the same fixture, but different blueprint IDs per test. The harness state persists between tests in the same class, so use unique blueprint IDs (92u, 93u, 94u, 95u, etc.) for each test.

---

## File structure

### New file: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsRoundTripTests.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

[Collection("EqsIntegrationTests")]
public sealed class EqsRoundTripTests : IDisposable
{
    private readonly EditorHarness _harness;

    // Private inner types: SimpleEqsTemplateRegistry, MockCoverProvider,
    // MockNavmeshProvider, DeterministicPositionalGenerator,
    // SentinelRejectionFilterTest, DummyScoreTest, ExposedLosServiceMock

    public EqsRoundTripTests() { _harness = new EditorHarness(); }

    public void Dispose()
    {
        if (_harness.Repo.HasSingleton<EqsResultPool>())
        {
            var pool = _harness.Repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated) pool.Results.Dispose();
        }
        _harness.Dispose();
    }

    // Tests: T-RT1, T-RT2, T-RT3a, T-RT3b
}
```

All 4 inner type classes and 4 test methods go inside this single class.

---

## Check EqsQueryTemplate struct fields before coding

Read `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsQueryTemplate.cs` to confirm field names for the test arrays (`FilterCheap`, `ScoreCheap`, etc.). They should match `EqsTestPhase` enum names.

---

## Build and test verification

```
dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-restore
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --no-build --filter "FullyQualifiedName~EqsRoundTripTests"
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --no-build --filter "FullyQualifiedName~Eqs"
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --filter "FullyQualifiedName~Eqs"
```

Expected:
- T-RT1, T-RT2, T-RT3a, T-RT3b: 4/4 pass
- Existing EQS integration tests: 21/21 (no regressions)
- Existing EQS unit tests: 49/49 (no regressions)

---

## Report

Write to `.dev/eqs-2/reports/BATCH-10-REPORT.md` including:
- Files created/modified
- Test counts (T-RT1, T-RT2, T-RT3a, T-RT3b)
- Deviations from plan (with justification)
- Build confirmation (0 errors)
