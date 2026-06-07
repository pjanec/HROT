# BATCH-04 INSTRUCTIONS — EntitiesInRadius, Tests, Time-Sliced Solver

**Batch:** BATCH-04
**Depends on:** BATCH-03 (committed as 29abad2f)
**Targets:** TASK-EQS-009 + TASK-EQS-010 + TASK-EQS-011

---

## Mandatory Reading

Before implementing, read:

1. `.dev/eqs-2/TASK-DETAIL.md` — sections TASK-EQS-009, TASK-EQS-010, TASK-EQS-011
2. `.dev/eqs-2/IMPLEM_DETAILS.md` — L:900–970 (EntitiesInRadius generator), L:975–1100 (Faction and Distance tests), L:1105–1380 (time-sliced solver)
3. `.dev/eqs-2/reviews/BATCH-03-REVIEW.md`
4. `Hrot/Subsystems/Hrot.SimHost/Systems/AreaQuerySolverSystem.cs` — pattern for SpatialHashGrid usage, EntityInfo.ForceId, SimTransform position
5. `Hrot/Subsystems/Hrot.IG/Systems/MapLayerAssignmentSystem.cs` — `QueryTimeSliced` usage pattern
6. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs` — `WriteAndWrap` method signature
7. `FDP/Engine/Fdp.Core/EntityLifecycleState.cs` — EntityLifecycle enum values
8. `FDP/Engine/Fdp.Core/Components/EntityInfo.cs` — ForceId field type
9. `FDP/Engine/Fdp.Core/Components/ForceId.cs` — Neutral=0, Friend=1, Hostile=2
10. `FDP/Engine/Fdp.Core/CoreComponents/SimComponents.cs` — SimTransform.Position (Vector3)
11. `FDP/Toolkits/Fdp.Toolkits/CarKinem/Spatial/SpatialGridData.cs` — SpatialHashGrid API

---

## CORRECTIVE TASK (P1 from BATCH-03)

**Problem:** `EqsSolverSystem.Execute` uses `.WithLifecycle(EntityLifecycle.Ghost)` which was added to fix the distributed-topology path in BATCH-03. However, in the offline/EditorHarness path, entities are created with `EntityLifecycle.Active`, so the solver finds zero sensors and T4 fails.

**Fix (apply before TASK-EQS-011):** Change the lifecycle filter in `EqsSolverSystem` to `EntityLifecycle.All` so the solver handles both Active (offline) and Ghost (distributed Muscle) entities.

This fix will be superseded by the full TASK-EQS-011 rewrite below.

---

## TASK-EQS-009 — EntitiesInRadius Generator

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EntitiesInRadiusGenerator.cs`

Required usings: `System`, `System.Numerics`, `Fdp.Core`, `Fdp.Toolkit.Spatial` (for SpatialGridData, SpatialHashGrid).

```csharp
namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Generates entity-shaped EQS candidates from the spatial hash grid.
    /// Uses stackalloc for the intermediate (Entity, Vector2) buffer — zero heap allocation.
    /// </summary>
    public sealed class EntitiesInRadiusGenerator : IEqsGenerator
    {
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return 0;
            if (!repo.HasSingletonUnmanaged<SpatialGridData>()) return 0;
            if (!repo.HasComponent<SimTransform>(observer)) return 0;

            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(observer);
            var obsPos = new Vector2(tf.Position.X, tf.Position.Y);

            ref readonly var gridData = ref repo.GetSingletonUnmanaged<SpatialGridData>();

            // Intermediate stackalloc buffer: (Entity, Vector2) pairs from the grid.
            Span<(Entity entity, Vector2 pos)> neighbors =
                stackalloc (Entity, Vector2)[candidates.Length];

            int rawCount = gridData.Grid.QueryNeighbors(obsPos, sensor.SearchRadius, neighbors);

            int validCount = 0;
            for (int i = 0; i < rawCount; i++)
            {
                // Exclude the observer entity itself from results.
                if (neighbors[i].entity == observer) continue;

                candidates[validCount++] = new EqsResult
                {
                    EntityId  = (long)neighbors[i].entity.PackedValue,
                    PositionX = neighbors[i].pos.X,
                    PositionY = neighbors[i].pos.Y,
                    Score     = 0f,
                    Flags     = 0,
                };
            }

            return validCount;
        }
    }
}
```

**Key constraints:**
- `SimTransform.Position` is a `Vector3`; use `.X` and `.Y` for the 2D grid query (X=east, Y=north).
- `SpatialGridData.Grid.QueryNeighbors(Vector2 center, float radius, Span<(Entity, Vector2)> results)` returns an int count; `results` is filled from index 0.
- `entity.PackedValue` encodes both index and generation, giving a safe EntityId.
- Return `validCount`, NOT `rawCount` (self is excluded).

### Tests: `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EntitiesInRadiusGeneratorTests.cs`

These tests use a real `EntityRepository` + `SpatialHashGrid` populated manually (no EditorHarness).

Look at `AreaQuerySolverSystem.cs` tests or existing SpatialGrid tests for how to set up `SpatialGridData` manually if possible. If the grid API requires `SpatialHashSystem` to run, use a minimal setup.

**Test 1 — Zero radius returns 0:**
- Observer at origin, 4 entities nearby, `SearchRadius = 0f`.
- Assert `Generate` returns 0.

**Test 2 — Observer excluded:**
- Observer at origin, 3 entities at distance 2, 4, 6; radius 10.
- Assert count == 3 (observer not included).

**Test 3 — Only entities within radius returned:**
- Observer at origin, 2 entities at distance 3 and 15; radius 10.
- Assert count == 1, the one at distance 3 is returned.

If the SpatialHashGrid API is too complex to set up in a unit test, write the tests as `EditorHarness` integration tests in `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EntitiesInRadiusGeneratorTests.cs` where the grid is populated by the real `SpatialHashSystem`. This is acceptable for this task.

---

## TASK-EQS-010 — FactionFilterTest and DistanceScoreTest

### CRITICAL: Rejection Sentinel

**The rejection sentinel is `EntityId = -1L`, NOT `0`.** Positional candidates (EntityId=0) are valid. The IMPLEM_DETAILS pseudocode uses `0` for rejection — that is a design bug documented in TASK-EQS-010 constraints. Always use `-1L`.

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/FactionFilterTest.cs`

```csharp
namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Rejects candidates whose <see cref="EntityInfo.ForceId"/> does not match
    /// <see cref="EqsSensor.FactionFilter"/> bitmask. Runs in the FilterCheap phase.
    /// Rejection sentinel: EntityId = -1L.
    /// </summary>
    public sealed class FactionFilterTest : IEqsTest
    {
        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip already-rejected candidates.
                if (candidate.EntityId == -1L) continue;

                // Skip positional candidates (EntityId = 0 = no entity).
                if (candidate.EntityId == 0L) continue;

                var target = new Entity((ulong)candidate.EntityId);

                if (!repo.IsAlive(target) || !repo.HasComponent<EntityInfo>(target))
                {
                    candidate.EntityId = -1L; // Reject dead or missing faction info.
                    continue;
                }

                ref readonly var info = ref repo.GetComponentRO<EntityInfo>(target);

                // ForceId: Neutral=0, Friend=1, Hostile=2.
                // FactionFilter bitmask: bit N = 1 means "include ForceId N".
                uint forceBit = 1u << (int)info.ForceId;
                if ((sensor.FactionFilter & forceBit) == 0)
                {
                    candidate.EntityId = -1L; // Reject faction mismatch.
                }
            }
        }
    }
}
```

### New file: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/DistanceScoreTest.cs`

```csharp
namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Scores candidates by proximity to the observer. Linear falloff: 1.0 at origin,
    /// 0.0 at SearchRadius. Skips rejected (-1L) candidates. Runs in the ScoreCheap phase.
    /// </summary>
    public sealed class DistanceScoreTest : IEqsTest
    {
        public EqsTestPhase Phase => EqsTestPhase.ScoreCheap;

        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasComponent<SimTransform>(observer)) return;

            ref readonly var obsTf = ref repo.GetComponentRO<SimTransform>(observer);
            var obsPos = new System.Numerics.Vector2(obsTf.Position.X, obsTf.Position.Y);

            float maxDist = sensor.SearchRadius;
            if (maxDist <= 0f) return;

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var candidate = ref candidates[i];

                // Skip rejected candidates.
                if (candidate.EntityId == -1L) continue;

                // Use the position already packed by the generator.
                var targetPos = new System.Numerics.Vector2(candidate.PositionX, candidate.PositionY);
                float dist = System.Numerics.Vector2.Distance(obsPos, targetPos);

                // Linear falloff: closer = higher score. Additive.
                float score = 1.0f - System.Math.Clamp(dist / maxDist, 0f, 1f);
                candidate.Score += score;
            }
        }
    }
}
```

### Tests: `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsFilterAndScoreTests.cs`

These are pure unit tests (no ECS; use a minimal EntityRepository for FactionFilter).

**T-F1 — FactionFilter rejects wrong faction:**
- 4 candidates: EntityId=1 (Friend), EntityId=2 (Hostile), EntityId=3 (Neutral), EntityId=0 (positional).
- Sensor.FactionFilter = 0b100 (bit 2 = Hostile included only).
- After ExecuteBatch: EntityId=1 → -1L, EntityId=3 → -1L, EntityId=2 → 2 (kept), EntityId=0 → 0 (positional untouched).
- Assert counts of rejected and surviving match expectations.

**T-F2 — FactionFilter skips already-rejected (-1L):**
- 1 candidate with EntityId=-1L.
- No ECS entity with that ID.
- Assert no exception and EntityId stays -1L.

**T-F3 — DistanceScore skips rejected candidates:**
- 2 candidates: one with EntityId=-1L and Score=0, one with EntityId=0 at pos (5,0).
- Observer at origin, SearchRadius=10.
- After ExecuteBatch: rejected candidate Score still 0; positional candidate gets score > 0.

**T-F4 — DistanceScore: closer = higher score:**
- 2 candidates at distance 2 and 8, observer at origin, SearchRadius=10.
- After ExecuteBatch: candidate at distance 2 has Score > candidate at distance 8.

---

## TASK-EQS-011 — Time-Sliced EqsSolverSystem (Phase 2 Full)

### Context

Replace the Phase 1 stub in `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs` with a full multi-phase evaluation loop.

**The BATCH-03 Ghost lifecycle regression is fixed here:** The new solver uses `.WithLifecycle(EntityLifecycle.All)` to handle both offline Active entities and distributed Ghost entities.

### Modified file: `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs`

```csharp
using System;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Phase 2 EQS solver system (Muscle-tier, time-sliced).
    ///
    /// <para>Reads <see cref="IEqsTemplateRegistry"/> from the repo's managed singleton slot
    /// (registered by EqsModule.Initialize or tests). If no registry is found or the template
    /// lookup fails, falls back to Phase 1 stub behaviour (empty event).</para>
    ///
    /// <para>Pool lazy-init: creates <see cref="EqsResultPool"/> singleton on first Execute
    /// if not already present.</para>
    ///
    /// <para>Driven at 10 Hz by <see cref="Modules.EqsModule"/>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class EqsSolverSystem : IEcsModuleSystem
    {
        // ── IteratorState for time-sliced entity traversal ────────────────────
        private readonly IteratorState _iteratorState = new IteratorState();

        // ── Query cached after first use ──────────────────────────────────────
        private EntityQuery? _sensorQuery;

        // ── Pre-allocated context fields to prevent hidden closure allocations ─
        // EvaluateSensor is passed as Action<Entity> to QueryTimeSliced.
        // Storing context in fields avoids a heap-allocated closure class.
        private IEntityCommandBuffer _currentCmd = null!;
        private uint _currentTick;
        private EntityRepository _currentRepo = null!;

        /// <summary>Wall-clock budget in milliseconds per Execute call.</summary>
        public double EqsBudgetMs { get; set; } = 4.0;

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;

            // Lazy-init pool singleton (allocated once, lives on the repo).
            if (!repo.HasSingleton<EqsResultPool>())
            {
                var pool = new EqsResultPool
                {
                    NextFreeIndex = 0,
                    Results = new NativeArray<EqsResult>(EqsResultPool.PoolCapacity, Allocator.Persistent),
                };
                repo.SetSingletonUnmanaged(pool);
            }

            // Build sensor query once; use All lifecycle so it works both offline (Active)
            // and in the distributed Muscle node (Ghost).
            _sensorQuery ??= repo.Query()
                .With<EqsSensor>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            // Store frame context in fields to avoid closure allocation.
            _currentCmd  = view.GetCommandBuffer();
            _currentTick = view.Tick;
            _currentRepo = repo;

            // Time-sliced iteration: yields if EqsBudgetMs is exceeded.
            repo.QueryTimeSliced(
                _sensorQuery,
                _iteratorState,
                EqsBudgetMs,
                TimeSliceMetric.WallClockTime,
                EvaluateSensor);
        }

        private void EvaluateSensor(Entity entity)
        {
            var repo = _currentRepo;

            ref readonly var sensor = ref repo.GetComponentRO<EqsSensor>(entity);
            ref readonly var netId  = ref repo.GetComponentRO<NetworkIdentity>(entity);

            // Try to look up the template from the registry singleton.
            IEqsTemplateRegistry? registry = repo.HasSingletonManaged<IEqsTemplateRegistry>()
                ? repo.GetSingletonManaged<IEqsTemplateRegistry>()
                : null;

            if (registry == null || !registry.TryGetTemplate(sensor.BlueprintId, out var template))
            {
                // No registry or unknown template: Phase 1 stub fallback (empty result).
                _currentCmd.PublishEvent(new EqsResultEvent
                {
                    SensorNetworkId = netId.Value,
                    Epoch           = sensor.Epoch,
                    RefreshTick     = (uint)(_currentTick + 1),
                    ResultHandle    = 0,
                    EntryCount      = 0,
                });
                return;
            }

            // 1. Generation.
            Span<EqsResult> candidates = stackalloc EqsResult[template.MaxCandidates];
            int count = template.Generator.Generate(entity, ref Unsafe.AsRef(in sensor), repo, candidates);
            if (count == 0)
            {
                // Nothing generated: still publish an empty event so Brain's IsReady ticks.
                _currentCmd.PublishEvent(new EqsResultEvent
                {
                    SensorNetworkId = netId.Value,
                    Epoch           = sensor.Epoch,
                    RefreshTick     = (uint)(_currentTick + 1),
                    ResultHandle    = 0,
                    EntryCount      = 0,
                });
                return;
            }

            var activeCandidates = candidates.Slice(0, count);

            // 2. FilterCheap.
            if (template.FilterCheap != null)
                foreach (var test in template.FilterCheap)
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), repo, activeCandidates);

            // 3. FilterExpensive (stubs go here in Phase 3+).
            if (template.FilterExpensive != null)
                foreach (var test in template.FilterExpensive)
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), repo, activeCandidates);

            // 4. Top-K reduction: compact and truncate.
            activeCandidates = ReduceTopK(activeCandidates, EqsResultPool.MaxTopK);

            // 5. ScoreCheap.
            if (template.ScoreCheap != null)
                foreach (var test in template.ScoreCheap)
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), repo, activeCandidates);

            // 6. ScoreExpensive (stubs go here in Phase 5+).
            if (template.ScoreExpensive != null)
                foreach (var test in template.ScoreExpensive)
                    test.ExecuteBatch(entity, ref Unsafe.AsRef(in sensor), repo, activeCandidates);

            // 7. Sort descending by Score.
            MemoryExtensions.Sort(activeCandidates, (a, b) => b.Score.CompareTo(a.Score));

            // 8. Write to pool and publish.
            WriteResultsToPoolAndPublish(netId.Value, sensor.Epoch, activeCandidates);
        }

        // Returns a compacted + top-K truncated span.
        // Checks EntityId != -1L (NOT 0) to preserve valid positional candidates (EntityId=0).
        private static Span<EqsResult> ReduceTopK(Span<EqsResult> candidates, int maxTopK)
        {
            int validCount = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].EntityId != -1L)
                    candidates[validCount++] = candidates[i];
            }

            var validSpan = candidates.Slice(0, validCount);

            if (validSpan.Length > maxTopK)
            {
                // Pre-sort to find best candidates before truncating.
                MemoryExtensions.Sort(validSpan, (a, b) => b.Score.CompareTo(a.Score));
                return validSpan.Slice(0, maxTopK);
            }

            return validSpan;
        }

        private void WriteResultsToPoolAndPublish(long sensorNetId, uint epoch, Span<EqsResult> finalCandidates)
        {
            ref var pool = ref _currentRepo.GetSingletonUnmanaged<EqsResultPool>();
            // WriteAndWrap takes ReadOnlySpan<EqsResult>.
            int handle = pool.WriteAndWrap((ReadOnlySpan<EqsResult>)finalCandidates);

            _currentCmd.PublishEvent(new EqsResultEvent
            {
                SensorNetworkId = sensorNetId,
                Epoch           = epoch,
                RefreshTick     = (uint)(_currentTick + 1),
                ResultHandle    = handle,
                EntryCount      = finalCandidates.Length,
            });
        }
    }
}
```

**Notes:**
- `ref Unsafe.AsRef(in sensor)` converts `ref readonly` to `ref` for the interface methods that take `ref EqsSensor`. Add `using System.Runtime.CompilerServices;`.
- `pool.WriteAndWrap(Span<EqsResult>)` — check the actual signature of `EqsResultPool.WriteAndWrap`. If it takes a span and returns the handle int, use it directly. If not, implement inline with the ring-buffer logic.
- `HasSingletonManaged<IEqsTemplateRegistry>()` — verify this method exists on EntityRepository. If not, use `TryGetSingletonManaged` or check if a pattern exists in tests.
- The `EqsModule` does NOT need to change (still creates `new EqsSolverSystem()` with no args).

### Tests: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsSolverSystemPhase2Tests.cs`

Use `EditorHarness` for integration tests.

**T-S1 — Full pipeline with 5 enemies populates buffer:**
- Register an `IEqsTemplateRegistry` managed singleton on `harness.Repo`:
  ```csharp
  var registry = new SimpleEqsTemplateRegistry();
  registry.Register(new EqsQueryTemplate
  {
      BlueprintId   = 42u,
      Generator     = new EntitiesInRadiusGenerator(),
      FilterCheap   = new IEqsTest[] { new FactionFilterTest() },
      ScoreCheap    = new IEqsTest[] { new DistanceScoreTest() },
      MaxCandidates = 64,
  });
  harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);
  ```
- Create observer entity with `SimTransform` (at origin), `EqsSensor { BlueprintId=42u, Epoch=1, SearchRadius=25f, FactionFilter=0b100 }` (bit 2 = Hostile), and `NetworkIdentity`.
- Create 5 entities with `SimTransform` at (2,0,0), (4,0,0), (6,0,0), (8,0,0), (10,0,0), each with `EntityInfo { ForceId = ForceId.Hostile }`.
- `PumpUntil` (timeout 4000 ms): `harness.Repo.HasComponent<EqsCognitiveBuffer>(observer) && harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0`.
- Assert `Count > 0` and the top result EntityId matches one of the enemy entities.

**T-S2 — Budget yield test:**
- Set `harness.Solver.EqsBudgetMs = 0.001` before pumping (harness needs to expose the solver or this must be set via module access).
- Alternatively: register 100 entities, budget=0.001 ms, assert that after 1 frame the IteratorState didn't complete a full pass (check that not all 100 were visited).

**T-S3 — Phase 1 fallback (no registry):**
- Don't register any registry.
- Observer with `EqsSensor { BlueprintId=1 }`.
- `PumpUntil` buffer IsReady.
- Assert `Count == 0` (Phase 1 stub fallback).

**Note on SimpleEqsTemplateRegistry:** Define it as a private class inside the test or in a shared test helper. It's a simple `Dictionary<uint, EqsQueryTemplate>` backed `IEqsTemplateRegistry`.

**Note on T-S1 spatial grid:** In EditorHarness, `SpatialHashSystem` runs as part of the logic packs. Entity positions in `SimTransform` are automatically indexed. Add entities before pumping so the grid is populated.

---

## Additional Unit Test: ReduceTopK Correctness

Add a dedicated unit test in `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsSolverSystemUnitTests.cs`:

**T-RK1 — ReduceTopK preserves positional (EntityId=0), removes rejected (EntityId=-1L):**
- Span of 3: { EntityId=-1L, EntityId=0, EntityId=5 }.
- After ReduceTopK(maxTopK=16): result has 2 entries: EntityId=0 and EntityId=5.
- Assert EntityId=-1L is not in the output.

**T-RK2 — ReduceTopK truncates to maxTopK:**
- Span of 20 candidates all with EntityId=1.
- After ReduceTopK(maxTopK=16): result length == 16.

---

## Build and Test Verification

After implementing all changes:

1. `dotnet build IOS-IG-SimHost.sln` — must succeed with 0 errors.
2. `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs"` — ALL existing EQS tests (T1-T10 + new T-S1, T-S2, T-S3) must pass, including T4 (Phase1Stub).
3. `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~Eqs"` — all unit tests must pass (including EntitiesInRadius, Filter+Score, ReduceTopK).

---

## Key Constraints Summary

- **Rejection sentinel is `-1L`** — NEVER use `0`. EntityId=0 is a VALID positional candidate.
- **ReduceTopK must check `!= -1L`** (not `!= 0`), or positional candidates are silently removed.
- **DistanceScoreTest** skips `EntityId == -1L` candidates (not `== 0`).
- **EqsSolverSystem lifecycle: `WithLifecycle(EntityLifecycle.All)`** — must handle both Active (offline) and Ghost (distributed Muscle) entities.
- **No constructor change to EqsSolverSystem** — EqsModule still creates `new EqsSolverSystem()` with no args; registry is read from repo managed singleton.
- **Pool lazy-init** — use `HasSingleton<EqsResultPool>()` guard in Execute.
- **`WriteAndWrap`** — signature is `int WriteAndWrap(ReadOnlySpan<EqsResult>)`. Cast the working span when calling: `pool.WriteAndWrap((ReadOnlySpan<EqsResult>)finalCandidates)`.
- **Phase 1 fallback** — if no registry or unknown BlueprintId, emit empty EqsResultEvent with `RefreshTick = view.Tick + 1` (same tick +1 guard as the old stub).

---

## Report

Write to `.dev/eqs-2/reports/BATCH-04-REPORT.md`. Include:
1. Summary per task + corrective task
2. Test results (pass/fail)
3. Design decisions (especially for pool init, registry access, entity lifecycle filter)
4. Any deviations
5. Suggested commit message
