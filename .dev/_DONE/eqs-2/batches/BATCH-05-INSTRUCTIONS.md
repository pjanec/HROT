# BATCH-05 INSTRUCTIONS — CoverProvider, LOS, FindCoverFromTarget

**Batch:** BATCH-05
**Depends on:** BATCH-04 (committed as f049d163)
**Targets:** TASK-EQS-012 + TASK-EQS-013 + TASK-EQS-015

---

## Mandatory Reading

Before implementing, read:

1. `.dev/eqs-2/TASK-DETAIL.md` — sections TASK-EQS-012, TASK-EQS-013, TASK-EQS-015
2. `.dev/eqs-2/IMPLEM_DETAILS.md` — L:1400–1460 (CoverPoint, ICoverProvider), L:1460–1600 (CoverPointsGenerator, ILosService, CheapLineOfSightTest), L:1603–1680 (FindCoverFromTarget)
3. `.dev/eqs-2/reviews/BATCH-04-REVIEW.md`
4. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsQueryTemplate.cs` — existing interfaces and EqsTemplateAttribute
5. `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` — TargetMemory struct (namespace Fdp.Toolkit.Perception.Components)
6. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/FactionFilterTest.cs` — existing FilterCheap pattern
7. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/DistanceScoreTest.cs` — existing ScoreCheap pattern (used in FindCoverFromTarget)
8. `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` — TargetMemory=73, verify no ID conflicts

---

## TASK-EQS-012 — ICoverProvider Interface and CoverPoint Struct

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CoverPoint.cs`

```csharp
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Represents a single static cover node in the environment.
    /// Strictly unmanaged (24 bytes) so the generator can use stackalloc.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CoverPoint
    {
        // World-space ground-plane coordinates.
        public float PositionX;
        public float PositionY;

        // Normalized direction this cover faces (direction of protection).
        public float DirectionX;
        public float DirectionY;

        // Pre-annotated quality multiplier (1.0 = concrete, 0.5 = wood).
        public float Quality;

        // 0 = Prone, 1 = Crouch, 2 = Stand.
        public byte StanceHeight;

        // Explicit padding to reach 24 bytes and maintain 4-byte alignment.
        private byte _pad0;
        private ushort _pad1;
    }
}
```

**Invariant:** `Marshal.SizeOf<CoverPoint>() == 24` — verified in unit test.

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/ICoverProvider.cs`

```csharp
using System;
using System.Numerics;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Cover database interface consumed by the Muscle tier.
    /// Implementations may be designer-authored (ManualCoverProvider) or
    /// auto-computed from navmesh edges (future stage).
    /// </summary>
    public interface ICoverProvider
    {
        /// <summary>
        /// Populates <paramref name="results"/> with cover points within <paramref name="radius"/>
        /// of <paramref name="center"/>. Returns the actual number of points written.
        /// </summary>
        int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results);
    }
}
```

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/ManualCoverProvider.cs`

```csharp
using System;
using System.Numerics;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Designer-placed cover provider backed by a flat array.
    /// Linear scan is acceptable at this stage (maps are small).
    /// Registered as a managed singleton via repo.SetSingletonManaged&lt;ICoverProvider&gt;.
    /// </summary>
    public sealed class ManualCoverProvider : ICoverProvider
    {
        private readonly CoverPoint[] _points;

        public ManualCoverProvider(CoverPoint[] points)
        {
            _points = points;
        }

        /// <inheritdoc/>
        public int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results)
        {
            float radiusSq = radius * radius;
            int count = 0;
            foreach (var point in _points)
            {
                if (count >= results.Length) break;
                float dx = point.PositionX - center.X;
                float dy = point.PositionY - center.Y;
                if (dx * dx + dy * dy <= radiusSq)
                    results[count++] = point;
            }
            return count;
        }
    }
}
```

### Tests: `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/CoverProviderTests.cs`

**T-CP1 — CoverPoint is 24 bytes:**
```csharp
Assert.Equal(24, System.Runtime.InteropServices.Marshal.SizeOf<CoverPoint>());
```

**T-CP2 — ManualCoverProvider radius filter:**
- 3 CoverPoints at distances 5, 15, 20 from origin.
- Query: center=(0,0), radius=12.
- Assert: returns exactly 1 point (the one at distance 5).
- Assert: returned point.PositionX matches expected.

---

## TASK-EQS-013 — CoverPointsGenerator, ILosService, CheapLineOfSightTest

### CRITICAL NOTES

**Rejection sentinel:** `-1L` NOT `0`. Positional candidates use `EntityId = 0` (no entity). Reject with `-1L`.

**`CoverPointsGenerator` produces positional candidates (`EntityId = 0`).**

**`CheapLineOfSightTest` rejects EXPOSED positions** (where the attacker has clear LOS to the candidate position). If `HasCheapLineOfSight(candidatePos, threatPos)` returns `true` (clear = exposed), the candidate is rejected with `-1L`. If it returns `false` (blocked = cover is valid), the candidate is kept and flag bit 0 is set.

**Stub LOS service always returns `false` (blocked)** — so all candidates pass through in this phase.

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/ILosService.cs`

```csharp
using System.Numerics;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Line-of-sight service interface. Phase 3 uses a stub (always blocked).
    /// Phase 5 will replace with raycast against the occluder grid.
    /// </summary>
    public interface ILosService
    {
        /// <summary>
        /// Returns true if there is a clear line of sight from <paramref name="observer"/>
        /// to <paramref name="target"/> (no occluders between them).
        /// Returns false if the line is blocked (occluded = cover is valid).
        /// </summary>
        bool HasCheapLineOfSight(Vector2 observer, Vector2 target);
    }

    /// <summary>
    /// Phase 3 stub: always reports LOS as blocked (cover always valid).
    /// </summary>
    public sealed class BlockedLosService : ILosService
    {
        public bool HasCheapLineOfSight(Vector2 observer, Vector2 target) => false;
    }
}
```

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CoverPointsGenerator.cs`

```csharp
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Generates positional (EntityId=0) EQS candidates from the ICoverProvider singleton.
    /// Uses stackalloc for the intermediate CoverPoint buffer -- zero heap allocation.
    /// </summary>
    public sealed class CoverPointsGenerator : IEqsGenerator
    {
        /// <inheritdoc/>
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return 0;
            if (!repo.HasSingletonManaged<ICoverProvider>()) return 0;

            ICoverProvider provider = repo.GetSingletonManaged<ICoverProvider>()!;

            if (!repo.HasComponent<SimTransform>(observer)) return 0;
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            var center = new Vector2(tf.Position.X, tf.Position.Y);

            // Intermediate stackalloc buffer for raw cover points.
            Span<CoverPoint> rawPoints = stackalloc CoverPoint[candidates.Length];
            int rawCount = provider.GetCoverPointsInRadius(center, sensor.SearchRadius, rawPoints);

            for (int i = 0; i < rawCount; i++)
            {
                // EntityId = 0 marks a positional candidate (no entity attached).
                candidates[i] = new EqsResult
                {
                    EntityId  = 0L,
                    PositionX = rawPoints[i].PositionX,
                    PositionY = rawPoints[i].PositionY,
                    Score     = rawPoints[i].Quality, // Seed score with cover quality.
                    Flags     = rawPoints[i].StanceHeight,
                };
            }

            return rawCount;
        }
    }
}
```

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/CheapLineOfSightTest.cs`

This test runs in the `FilterCheap` phase. It reads the primary threat position from `TargetMemory[0]` and rejects candidates that are exposed (have clear LOS to the threat).

```csharp
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Rejects cover candidates that are exposed to the primary threat in TargetMemory.
    /// "Exposed" = HasCheapLineOfSight returns true (clear LOS from candidate to threat).
    /// "Covered" = returns false (LOS blocked); flag bit 0 is set.
    ///
    /// Bypass conditions:
    ///   - TargetMemory.Count == 0 (no threats tracked)
    ///   - ThreatScores[0] &lt; sensor.ThreatThreshold (threat not significant enough)
    ///
    /// Rejection sentinel: EntityId = -1L (NOT 0 -- positional candidates use 0).
    /// </summary>
    public sealed class CheapLineOfSightTest : IEqsTest
    {
        private readonly ILosService _los;

        public CheapLineOfSightTest(ILosService los)
        {
            _los = los;
        }

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;

            // Bypass: no TargetMemory on observer.
            if (!repo.HasComponent<TargetMemory>(observer)) return;
            ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(observer);

            // Bypass: no threats tracked.
            if (mem.Count == 0) return;

            // Bypass: primary threat score is below threshold (not significant).
            if (mem.ThreatScores[0] < sensor.ThreatThreshold) return;

            // Primary threat position.
            var threatPos = new Vector2(mem.PositionsX[0], mem.PositionsY[0]);

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                var candidatePos = new Vector2(candidate.PositionX, candidate.PositionY);

                // HasCheapLineOfSight: true = clear (exposed) = reject.
                //                      false = blocked (cover valid) = keep + set flag bit 0.
                if (_los.HasCheapLineOfSight(candidatePos, threatPos))
                {
                    candidate.EntityId = -1L; // Exposed: reject.
                }
                else
                {
                    candidate.Flags |= 1; // Covered: set flag bit 0.
                }
            }
        }
    }
}
```

**Key points:**
- `TargetMemory` is in namespace `Fdp.Toolkit.Perception.Components`. Add `using Fdp.Toolkit.Perception.Components;`.
- `mem.ThreatScores[0]` accesses the primary threat (slot 0 is always the highest score after AddOrUpdateTarget sorts).
- The test uses `EntityId == -1L` to skip already-rejected, NOT `== 0`. Positional candidates have EntityId=0 and MUST be processed (they're the whole point of this test).

### Tests: `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/CoverGeneratorAndLosTests.cs`

These tests use a minimal `EntityRepository` (no EditorHarness needed).

**T-CG1 — CoverPointsGenerator produces positional candidates (EntityId=0):**
- `ManualCoverProvider` with 2 cover points at (3,0) and (7,0).
- Observer at origin with `SimTransform`, `SearchRadius=10`.
- Register as `repo.SetSingletonManaged<ICoverProvider>(provider)`.
- Call `Generate` with a span of 16.
- Assert: count==2, both candidates have `EntityId==0`.
- Assert: PositionX values match the provider points.

**T-LOS1 — CheapLineOfSightTest skips bypassed threats (Count==0):**
- Observer with `TargetMemory { Count=0 }`, 2 cover candidates with EntityId=0.
- After `ExecuteBatch`: both candidates unchanged (Score and EntityId unmodified).

**T-LOS2 — CheapLineOfSightTest skips bypassed threats (score below threshold):**
- Observer with `TargetMemory { Count=1, ThreatScores[0]=10f }`, `EqsSensor.ThreatThreshold=50f`.
- 2 cover candidates.
- Use `ExposedLosService` (always returns `true`).
- After `ExecuteBatch`: both candidates unchanged (bypass triggered, no rejection).

**T-LOS3 — CheapLineOfSightTest rejects exposed candidates:**
- Observer with `TargetMemory { Count=1, ThreatScores[0]=100f }`, `EqsSensor.ThreatThreshold=50f`.
- 1 candidate with EntityId=0.
- Use `ExposedLosService` (always returns `true`).
- After `ExecuteBatch`: candidate.EntityId == -1L (rejected as exposed).

**T-LOS4 — CheapLineOfSightTest keeps occluded candidates and sets flag bit 0:**
- Same setup but `BlockedLosService` (always returns `false`).
- After `ExecuteBatch`: candidate.EntityId == 0L (kept), Flags has bit 0 set (`(candidate.Flags & 1) == 1`).

Define `ExposedLosService` as a private class inside the test: `bool HasCheapLineOfSight(...) => true`.

---

## TASK-EQS-015 — FindCoverFromTarget Starter Template

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/FindCoverFromTarget.cs`

```csharp
using System;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Starter EQS template: finds cover positions that provide occlusion from the
    /// primary tracked threat. Composed of CoverPointsGenerator + CheapLineOfSightTest
    /// + DistanceScoreTest.
    ///
    /// BlueprintId is the FNV-1a 32-bit hash of the AssetId GUID below.
    /// </summary>
    [EqsTemplate(AssetId = "f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d")]
    public static class FindCoverFromTarget
    {
        /// <summary>
        /// FNV-1a 32-bit hash of the AssetId GUID "f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d".
        /// Used as the BlueprintId key in IEqsTemplateRegistry.
        /// </summary>
        public const uint BlueprintId = 0x7F3A2B1Cu;

        /// <summary>
        /// Builds the compiled template. Static and pure: no runtime state read.
        /// </summary>
        /// <param name="los">LOS service (inject BlockedLosService for Phase 3 stub).</param>
        public static EqsQueryTemplate Build(ILosService los)
        {
            return new EqsQueryTemplate
            {
                BlueprintId    = BlueprintId,
                Generator      = new CoverPointsGenerator(),
                FilterCheap    = new IEqsTest[] { new CheapLineOfSightTest(los) },
                ScoreCheap     = new IEqsTest[] { new DistanceScoreTest() },
                MaxCandidates  = 32,
            };
        }
    }
}
```

**Notes:**
- The GUID `f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d` is unique (generated for this task). Do not reuse.
- The `BlueprintId` constant `0x7F3A2B1Cu` must be computed as FNV-1a of the GUID string, OR you can use any unique 32-bit non-zero value and document it. Verify the value doesn't collide with existing blueprints.
- `EqsTemplateAttribute` is already defined in `EqsQueryTemplate.cs` from BATCH-03. Use it directly.
- `DistanceScoreTest` is already implemented from BATCH-04. Import from same namespace.
- `Build(ILosService los)` takes the LOS service so callers (tests or EqsModule) can inject the stub or real implementation.

### Tests: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/FindCoverFromTargetTests.cs`

Use `EditorHarness` for the integration test.

**T-FCT1 — Full pipeline: 1 exposed + 2 occluded cover points → 2 results:**

Setup:
- `ManualCoverProvider` with 3 cover points:
  - Point A at (5, 0): position near observer but in the "exposed" direction
  - Point B at (0, 5): occluded position
  - Point C at (0, 10): occluded position
- Primary threat at position (20, 0) — 20m east of observer at origin.
- `MockLosService`: returns `true` (clear) when candidate is to the east (PositionX > 2), `false` otherwise.
  - This makes Point A (x=5) exposed → rejected.
  - Points B and C (x=0) occluded → kept.
- `TargetMemory` on observer with Count=1, ThreatScores[0]=100f, PositionsX[0]=20f, PositionsY[0]=0f.

Implementation:
```csharp
private sealed class MockLosService : ILosService
{
    public bool HasCheapLineOfSight(Vector2 from, Vector2 to)
        => from.X > 2f; // Points east of x=2 are exposed to threat at (20,0).
}
```

Register on harness:
```csharp
var los = new MockLosService();
var provider = new ManualCoverProvider(new[]
{
    new CoverPoint { PositionX = 5f, PositionY = 0f, Quality = 1f },  // exposed
    new CoverPoint { PositionX = 0f, PositionY = 5f, Quality = 1f },  // occluded
    new CoverPoint { PositionX = 0f, PositionY = 10f, Quality = 1f }, // occluded
});

harness.Repo.SetSingletonManaged<ICoverProvider>(provider);

var registry = new SimpleEqsTemplateRegistry();
registry.Register(FindCoverFromTarget.Build(los));
harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

// Create observer with TargetMemory + EqsSensor.
// TargetMemory must be registered (repo.RegisterComponent<TargetMemory>() if needed).
var observer = harness.Repo.CreateEntity();
harness.Repo.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
var mem = new TargetMemory();
TargetMemory.AddOrUpdateTarget(ref mem, entityId: 999L, posX: 20f, posY: 0f, scoreBoost: 100f, tick: 1);
harness.Repo.AddComponent(observer, mem);
harness.Repo.AddComponent(observer, new EqsSensor
{
    BlueprintId      = FindCoverFromTarget.BlueprintId,
    Epoch            = 1,
    SearchRadius     = 25f,
    ThreatThreshold  = 50f,
});
harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8001L });
```

Pump:
```csharp
bool ready = harness.PumpUntil(
    () => harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
       && harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
       && harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
    timeoutMs: 5000);
Assert.True(ready, "FindCoverFromTarget should produce results within 5 s");
```

Assert:
```csharp
ref readonly var buffer = ref harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
Assert.Equal(2, buffer.Count); // Only 2 occluded points survive.
// All results are positional (EntityId=0).
var span = buffer.GetSpanRO();
for (int i = 0; i < buffer.Count; i++)
    Assert.Equal(0L, span[i].EntityId);
// Top result is at (0,5) — closer to observer — higher score.
Assert.True(span[0].Score >= span[1].Score, "Closer cover point should be ranked higher");
```

**T-FCT2 — Bypass when no threats: all cover points survive:**
- Same setup but observer has `TargetMemory { Count=0 }` (no threats).
- `MockLosService` that always returns `true` (would reject everything).
- Assert: buffer Count == 3 (all cover points survive bypass).

---

## SimpleEqsTemplateRegistry Helper

If not already available from BATCH-04 tests, define locally (private class inside the test):
```csharp
private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
{
    private readonly System.Collections.Generic.Dictionary<uint, EqsQueryTemplate> _t = new();
    public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
    public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
}
```

---

## Build and Test Verification

After implementing all changes:

1. `dotnet build IOS-IG-SimHost.sln` — 0 errors.
2. `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~Eqs"` — all EQS unit tests pass (20 existing + 6 new = 26+).
3. `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs"` — all 13 existing + 2 new integration tests pass.

---

## Key Constraints Summary

- **Rejection sentinel is `-1L`** throughout (not `0`).
- **Positional candidates have `EntityId = 0`** — generated by CoverPointsGenerator, preserved by ReduceTopK in solver.
- **`CheapLineOfSightTest` skips `EntityId == -1L`** (already rejected), processes `EntityId == 0` (positional candidates are the primary target).
- **`CheapLineOfSightTest` bypass conditions:** `mem.Count == 0` OR `mem.ThreatScores[0] < sensor.ThreatThreshold` — no changes made to candidates in these cases.
- **`ILosService` stub (`BlockedLosService`)** always returns `false` (all cover positions considered valid).
- **`ManualCoverProvider` does NOT need a spatial index** — linear scan is correct for this stage.
- **`FindCoverFromTarget.Build(ILosService los)`** is static and pure; `los` is injected so tests can use `MockLosService`.
- **`TargetMemory` is in namespace `Fdp.Toolkit.Perception.Components`** — add this using statement to `CheapLineOfSightTest.cs`.
- **Project file for `CheapLineOfSightTest.cs`** may need to reference `Fdp.Toolkits` (perception) project. Check existing usages of `TargetMemory` in the FDP toolkit to confirm project dependencies.

---

## Report

Write to `.dev/eqs-2/reports/BATCH-05-REPORT.md`. Include:
1. Summary per task
2. Test results (all names and PASS/FAIL)
3. Design decisions (especially for CheapLineOfSightTest bypass logic)
4. Any deviations
5. Suggested commit message
