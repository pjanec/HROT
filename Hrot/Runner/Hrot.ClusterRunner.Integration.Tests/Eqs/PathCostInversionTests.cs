using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.SimHost;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Integration test for path-cost inversion: B (farther Euclidean, shorter path) beats
/// A (closer Euclidean, longer detour). C (unreachable) is rejected (TASK-EQS-017).
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class PathCostInversionTests : IDisposable
{
    private readonly EditorHarness _harness;

    // Mock navmesh: A at (0,5) costs 50, B at (0,10) costs 10, C at (0,2) unreachable.
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

    // Simple in-memory template registry.
    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _templates = new();
        public void Register(EqsQueryTemplate template) => _templates[template.BlueprintId] = template;
        public bool TryGetTemplate(uint blueprintId, out EqsQueryTemplate template)
            => _templates.TryGetValue(blueprintId, out template);
    }

    public PathCostInversionTests()
    {
        _harness = new EditorHarness();
    }

    public void Dispose()
    {
        if (_harness.Repo.HasSingleton<EqsResultPool>())
        {
            var pool = _harness.Repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated)
                pool.Results.Dispose();
        }
        _harness.Dispose();
    }

    /// <summary>
    /// T-PCI1: B (farther Euclidean, shorter navmesh path) is ranked #1; C (unreachable) is rejected.
    /// Score math (SearchRadius=60):
    ///   A: DistanceScore = 1-(5/60)=0.917, PathScore = 1-(50/60)=0.167, Total = 1.083
    ///   B: DistanceScore = 1-(10/60)=0.833, PathScore = 1-(10/60)=0.833, Total = 1.667
    ///   B wins because PathCostScoreTest reveals A's detour.
    /// </summary>
    [Fact(Timeout = 6_000)]
    public void PathCostInversion_BRankedFirst_CRejected()
    {
        // Arrange: register the navmesh provider singleton.
        _harness.Repo.SetSingletonManaged<INavmeshProvider>(new MockNavmeshProvider());

        // Register template: EntitiesInRadius + DistanceScore (cheap) + NavmeshReachable (filter) + PathCost (score).
        const uint blueprintId = 0xABCD1234u;
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId     = blueprintId,
            Generator       = new EntitiesInRadiusGenerator(),
            FilterExpensive = new IEqsTest[] { new NavmeshReachableTest() },
            ScoreCheap      = new IEqsTest[] { new DistanceScoreTest() },
            ScoreExpensive  = new IEqsTest[] { new PathCostScoreTest() },
            MaxCandidates   = 32,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Create observer at origin with EqsSensor.
        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });
        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId  = blueprintId,
            Epoch        = 1,
            SearchRadius = 60f,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 9001L });

        // Spawn target A: (0,5) -- close but long detour.
        var targetA = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(targetA, new SimTransform
        {
            Position = new Vector3(0f, 5f, 0f),
            Rotation = Quaternion.Identity,
        });
        _harness.Repo.AddComponent(targetA, new PhysicsCollider { Radius = 1f });

        // Spawn target B: (0,10) -- farther but short path.
        var targetB = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(targetB, new SimTransform
        {
            Position = new Vector3(0f, 10f, 0f),
            Rotation = Quaternion.Identity,
        });
        _harness.Repo.AddComponent(targetB, new PhysicsCollider { Radius = 1f });

        // Spawn target C: (0,2) -- unreachable.
        var targetC = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(targetC, new SimTransform
        {
            Position = new Vector3(0f, 2f, 0f),
            Rotation = Quaternion.Identity,
        });
        _harness.Repo.AddComponent(targetC, new PhysicsCollider { Radius = 1f });

        // Act: pump frames until the cognitive buffer is ready with results.
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 5000);

        Assert.True(ready, "PathCostInversion: buffer should be ready within 5 s");

        // Assert: exactly 2 results (C rejected as unreachable).
        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.Equal(2, buffer.Count);

        // Inversion: B should be ranked #1 (higher total score despite greater Euclidean distance).
        Assert.Equal((long)targetB.PackedValue, buffer.GetSpanRO()[0].EntityId);
    }
}
