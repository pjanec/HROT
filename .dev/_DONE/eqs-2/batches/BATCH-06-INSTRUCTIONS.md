# BATCH-06 INSTRUCTIONS — Navmesh Provider, Samples Generator, Reachable+PathCost Tests

**Batch:** BATCH-06
**Depends on:** BATCH-05 (committed as 05f9069a)
**Targets:** TASK-EQS-016 + TASK-EQS-017

---

## Mandatory Reading

Before implementing, read:

1. `.dev/eqs-2/TASK-DETAIL.md` — sections TASK-EQS-016 and TASK-EQS-017
2. `.dev/eqs-2/IMPLEM_DETAILS.md` — L:1685–1875 (INavmeshProvider, generators, tests, score math)
3. `.dev/eqs-2/reviews/BATCH-05-REVIEW.md`
4. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/ICoverProvider.cs` — pattern for managed singleton registration with `[ComponentId]`
5. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CoverPointsGenerator.cs` — pattern for positional generator
6. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/FactionFilterTest.cs` — pattern for filter tests
7. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/DistanceScoreTest.cs` — already used for ScoreCheap in the integration test
8. `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` — verify next available ID (should be 212 after ICoverProvider=211)
9. `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs` — SyncSingletonById pattern (add INavmeshProvider)

---

## TASK-EQS-016 — INavmeshProvider Interface and StubNavmeshProvider

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/INavmeshProvider.cs`

```csharp
using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Navmesh query interface consumed by the Muscle tier.
    /// Phase 4 uses StubNavmeshProvider (Euclidean distance).
    /// Phase 4+ will replace with DotRecast integration (separate workstream).
    /// </summary>
    [ComponentId(GlobalComponentIds.INavmeshProvider)]
    public interface INavmeshProvider
    {
        /// <summary>Returns true if a navmesh path exists between the two positions.</summary>
        bool IsReachable(Vector2 from, Vector2 to);

        /// <summary>
        /// Returns true and writes the path distance into <paramref name="pathDist"/>
        /// if a path exists. Returns false if the target is unreachable.
        /// </summary>
        bool TryGetPathDistance(Vector2 from, Vector2 to, out float pathDist);

        /// <summary>
        /// Samples random reachable points within radius of center.
        /// Returns the number of points written to <paramref name="results"/>.
        /// </summary>
        int GetRandomPointsInRadius(Vector2 center, float radius, Span<Vector2> results);
    }
}
```

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/StubNavmeshProvider.cs`

```csharp
using System;
using System.Numerics;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Phase 4 stub navmesh provider.
    /// IsReachable: always true. TryGetPathDistance: returns Euclidean distance.
    /// GetRandomPointsInRadius: returns a small fixed grid of sample points.
    /// </summary>
    public sealed class StubNavmeshProvider : INavmeshProvider
    {
        /// <inheritdoc/>
        public bool IsReachable(Vector2 from, Vector2 to) => true;

        /// <inheritdoc/>
        public bool TryGetPathDistance(Vector2 from, Vector2 to, out float pathDist)
        {
            pathDist = Vector2.Distance(from, to);
            return true;
        }

        /// <inheritdoc/>
        public int GetRandomPointsInRadius(Vector2 center, float radius, Span<Vector2> results)
        {
            // Stub: return a 3x3 grid of sample points within the radius.
            int count = 0;
            float step = radius / 2f;
            for (float dx = -step; dx <= step && count < results.Length; dx += step)
            {
                for (float dy = -step; dy <= step && count < results.Length; dy += step)
                {
                    var p = new Vector2(center.X + dx, center.Y + dy);
                    if (Vector2.Distance(center, p) <= radius)
                        results[count++] = p;
                }
            }
            return count;
        }
    }
}
```

### GlobalComponentIds.cs (modify)

Add after `ICoverProvider = 211`:
```csharp
/// <summary><c>INavmeshProvider</c> — managed singleton for navmesh queries (EQS v1.3).</summary>
public const int INavmeshProvider = 212;
```

### EntityRepository.Sync.cs (modify)

Add `SyncSingletonById(source, GlobalComponentIds.INavmeshProvider);` alongside the existing `IEqsTemplateRegistry` and `ICoverProvider` sync calls, with a brief comment explaining why:
```csharp
SyncSingletonById(source, GlobalComponentIds.INavmeshProvider); // NavmeshSamplesGenerator / NavmeshReachableTest
```

### Tests: `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/NavmeshProviderTests.cs`

**T-NP1 — StubNavmeshProvider.IsReachable always returns true:**
```csharp
var nav = new StubNavmeshProvider();
Assert.True(nav.IsReachable(Vector2.Zero, new Vector2(10, 10)));
```

**T-NP2 — StubNavmeshProvider.TryGetPathDistance returns Euclidean distance:**
```csharp
var nav = new StubNavmeshProvider();
bool ok = nav.TryGetPathDistance(Vector2.Zero, new Vector2(3, 4), out float dist);
Assert.True(ok);
Assert.True(Math.Abs(dist - 5f) < 0.001f); // 3-4-5 triangle
```

---

## TASK-EQS-017 — NavmeshSamplesGenerator, NavmeshReachableTest, PathCostScoreTest

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/NavmeshSamplesGenerator.cs`

Positional generator: queries `INavmeshProvider.GetRandomPointsInRadius`, sets `EntityId=0`.

```csharp
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Generates positional (EntityId=0) EQS candidates by sampling random reachable
    /// navmesh positions within the sensor's search radius.
    /// </summary>
    public sealed class NavmeshSamplesGenerator : IEqsGenerator
    {
        /// <inheritdoc/>
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return 0;
            if (!repo.HasSingletonManaged<INavmeshProvider>()) return 0;
            if (!repo.HasComponent<SimTransform>(observer)) return 0;

            var navmesh = repo.GetSingletonManaged<INavmeshProvider>()!;
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            var center = new Vector2(tf.Position.X, tf.Position.Y);

            // Intermediate stackalloc buffer for raw positions.
            Span<Vector2> rawPoints = stackalloc Vector2[candidates.Length];
            int rawCount = navmesh.GetRandomPointsInRadius(center, sensor.SearchRadius, rawPoints);

            for (int i = 0; i < rawCount; i++)
            {
                candidates[i] = new EqsResult
                {
                    EntityId  = 0L, // Positional candidate.
                    PositionX = rawPoints[i].X,
                    PositionY = rawPoints[i].Y,
                    Score     = 0f,
                    Flags     = 0,
                };
            }

            return rawCount;
        }
    }
}
```

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/NavmeshReachableTest.cs`

FilterExpensive: rejects unreachable candidates with `-1L`, marks reachable with flag bit 3.

```csharp
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Rejects candidates that are not reachable via the navmesh.
    /// Reachable candidates are marked with flag bit 3. Runs in FilterExpensive phase.
    /// Rejection sentinel: EntityId = -1L.
    /// </summary>
    public sealed class NavmeshReachableTest : IEqsTest
    {
        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterExpensive;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingletonManaged<INavmeshProvider>()) return;
            if (!repo.HasComponent<SimTransform>(observer)) return;

            var navmesh = repo.GetSingletonManaged<INavmeshProvider>()!;
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            var obsPos = new Vector2(tf.Position.X, tf.Position.Y);

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                var targetPos = new Vector2(candidate.PositionX, candidate.PositionY);

                if (!navmesh.IsReachable(obsPos, targetPos))
                {
                    candidate.EntityId = -1L; // Reject: unreachable.
                }
                else
                {
                    candidate.Flags |= (1 << 3); // Bit 3: NavmeshReachable.
                }
            }
        }
    }
}
```

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/PathCostScoreTest.cs`

ScoreExpensive: adds path-cost inverse-linear score; rejects candidates with no path.

```csharp
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Scores candidates by inverse-linear path cost (closer path = higher score).
    /// Rejects candidates where no navmesh path exists (EntityId = -1L).
    /// Runs in ScoreExpensive phase.
    /// </summary>
    public sealed class PathCostScoreTest : IEqsTest
    {
        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.ScoreExpensive;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingletonManaged<INavmeshProvider>()) return;
            if (!repo.HasComponent<SimTransform>(observer)) return;

            var navmesh = repo.GetSingletonManaged<INavmeshProvider>()!;
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            var obsPos = new Vector2(tf.Position.X, tf.Position.Y);

            float maxDist = sensor.SearchRadius;
            if (maxDist <= 0f) return;

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                var targetPos = new Vector2(candidate.PositionX, candidate.PositionY);

                if (navmesh.TryGetPathDistance(obsPos, targetPos, out float pathDist))
                {
                    // Inverse-linear falloff: shorter path = higher score. Additive.
                    float score = 1.0f - Math.Clamp(pathDist / maxDist, 0f, 1f);
                    candidate.Score += score;
                }
                else
                {
                    candidate.EntityId = -1L; // Reject: no path.
                }
            }
        }
    }
}
```

### Unit Tests: `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/NavmeshTests.cs`

**T-NS1 — NavmeshSamplesGenerator produces positional candidates (EntityId=0):**
- Register `StubNavmeshProvider` as `ICoverProvider`... no, as `INavmeshProvider` managed singleton.
- Observer at origin with `SimTransform`, `SearchRadius=10`.
- Call `Generate` with span of 16.
- Assert: count > 0 and all candidates have `EntityId == 0L`.

**T-NR1 — NavmeshReachableTest: unreachable candidates get EntityId=-1L:**
- Mock navmesh: `IsReachable` returns `false` for all targets.
- 2 candidates with EntityId=0.
- After `ExecuteBatch`: both have EntityId=-1L.

**T-NR2 — NavmeshReachableTest: reachable candidates get flag bit 3:**
- Mock navmesh: `IsReachable` always returns `true`.
- 2 candidates.
- After `ExecuteBatch`: both have EntityId unchanged and `(Flags & (1 << 3)) != 0`.

**T-NR3 — NavmeshReachableTest skips already-rejected candidates:**
- 1 candidate with EntityId=-1L.
- Mock navmesh: `IsReachable` returns `true` (would normally set flag).
- After `ExecuteBatch`: EntityId still -1L, Flags unchanged.

**T-PC1 — PathCostScoreTest rejects candidates with no path:**
- Mock navmesh: `TryGetPathDistance` returns `false`.
- 1 candidate with EntityId=0.
- After `ExecuteBatch`: EntityId == -1L.

**T-PC2 — PathCostScoreTest scores shorter path higher:**
- `StubNavmeshProvider` (Euclidean).
- Observer at origin, 2 candidates at (5,0) and (20,0), SearchRadius=60.
- After `ExecuteBatch`: candidate at (5,0) has higher Score.

---

## Integration Test: Path-Cost Inversion

### File: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/PathCostInversionTests.cs`

Uses `EditorHarness` + a `MockNavmeshProvider` where path costs diverge from Euclidean distance.

**Setup:**
- Observer at origin.
- `MockNavmeshProvider`: always reachable EXCEPT for entity C; path costs:
  - A at (0,5): path cost = 50 (long detour despite being close)
  - B at (0,10): path cost = 10 (short path despite being farther)
  - C at (0,2): unreachable (IsReachable → false)
- Template composed of:
  - `EntitiesInRadiusGenerator`
  - `DistanceScoreTest` (ScoreCheap)
  - `NavmeshReachableTest` (FilterExpensive)
  - `PathCostScoreTest` (ScoreExpensive)
  - `SearchRadius = 60f`

**MockNavmeshProvider:**
```csharp
private sealed class MockNavmeshProvider : INavmeshProvider
{
    public bool IsReachable(Vector2 from, Vector2 to)
    {
        // Unreachable only for the entity at (0,2).
        return !(Math.Abs(to.Y - 2f) < 0.1f && Math.Abs(to.X) < 0.1f);
    }

    public bool TryGetPathDistance(Vector2 from, Vector2 to, out float pathDist)
    {
        if (!IsReachable(from, to)) { pathDist = 0f; return false; }
        // A at (0,5): path = 50. B at (0,10): path = 10. Others: Euclidean.
        if (Math.Abs(to.Y - 5f) < 0.1f)  { pathDist = 50f; return true; }
        if (Math.Abs(to.Y - 10f) < 0.1f) { pathDist = 10f; return true; }
        pathDist = Vector2.Distance(from, to);
        return true;
    }

    public int GetRandomPointsInRadius(Vector2 center, float radius, Span<Vector2> results) => 0;
}
```

**Spawn entities:**
```csharp
var targetA = harness.Repo.CreateEntity();
harness.Repo.AddComponent(targetA, new SimTransform { Position = new Vector3(0, 5f, 0), ... });
harness.Repo.AddComponent(targetA, new PhysicsCollider { Radius = 1f });

var targetB = harness.Repo.CreateEntity();
harness.Repo.AddComponent(targetB, new SimTransform { Position = new Vector3(0, 10f, 0), ... });
harness.Repo.AddComponent(targetB, new PhysicsCollider { Radius = 1f });

var targetC = harness.Repo.CreateEntity();
harness.Repo.AddComponent(targetC, new SimTransform { Position = new Vector3(0, 2f, 0), ... });
harness.Repo.AddComponent(targetC, new PhysicsCollider { Radius = 1f });
```

**Assertions:**
```csharp
// After pump: exactly 2 results (C rejected as unreachable).
Assert.Equal(2, buffer.Count);

// Inversion: B (farther Euclidean but shorter path) should be ranked #1.
// Score math (SearchRadius=60):
//   A: EuclideanScore = 1-(5/60)=0.916, PathScore = 1-(50/60)=0.166, Total = 1.082
//   B: EuclideanScore = 1-(10/60)=0.833, PathScore = 1-(10/60)=0.833, Total = 1.666
// B wins because PathCostScoreTest reveals A's detour.
Assert.Equal((long)targetB.PackedValue, buffer.GetSpanRO()[0].EntityId);
```

**IMPORTANT:** Use `targetB.PackedValue` (not `targetB.Index`) to compare EntityId. The generator stores `(long)entity.PackedValue` which includes the generation number.

---

## Key Constraints

- **Rejection sentinel: `-1L`** — both `NavmeshReachableTest` and `PathCostScoreTest` use this.
- **Positional candidates: `EntityId=0`** — `NavmeshSamplesGenerator` sets this; `NavmeshReachableTest` must process them (no special `EntityId==0` skip).
- **`NavmeshReachableTest` skips `-1L` candidates** but does NOT skip `EntityId==0`.
- **`[ComponentId(GlobalComponentIds.INavmeshProvider)]`** on the `INavmeshProvider` interface — required for `SetSingletonManaged<INavmeshProvider>()`. Pattern identical to `ICoverProvider`.
- **`INavmeshProvider = 212`** in GlobalComponentIds.cs (after ICoverProvider=211).
- **`SyncSingletonById(source, GlobalComponentIds.INavmeshProvider)`** in `EntityRepository.Sync.cs` — required for SoD snapshot.
- **MockNavmeshProvider for the integration test** should be a private class inside the test file, not a permanent type.
- **`DistanceScoreTest` already exists** from BATCH-04 — import and reuse directly in the composed template.
- **`EntitiesInRadiusGenerator` already exists** from BATCH-04 — use directly.
- **`PhysicsCollider`** may be needed for entities to appear in the spatial hash grid. Check `EqsSolverSystemPhase2Tests` from BATCH-04 to see how entities were set up (they used `PhysicsCollider { Radius = 1.0f }`).
- **`[Collection("EqsIntegrationTests")]`** on the new test class — mandatory to avoid thread-pool contention (see BATCH-05 fix).

---

## Build and Test Verification

1. `dotnet build IOS-IG-SimHost.sln` — 0 errors.
2. `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~Eqs"` — 27 existing + T-NP1, T-NP2, T-NS1, T-NR1, T-NR2, T-NR3, T-PC1, T-PC2 = 35 tests pass.
3. `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs"` — 15 existing + path-cost inversion test = 16 tests pass.

---

## Report

Write to `.dev/eqs-2/reports/BATCH-06-REPORT.md`. Include:
1. Summary per task
2. Test results (names and PASS/FAIL)
3. Score math verification for path-cost inversion test
4. Deviations
5. Suggested commit message
