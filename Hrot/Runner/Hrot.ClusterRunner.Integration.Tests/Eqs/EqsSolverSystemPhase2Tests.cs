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
/// Integration tests for <see cref="EqsSolverSystem"/> Phase 2 (time-sliced, multi-phase)
/// via the offline <see cref="EditorHarness"/>.
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsSolverSystemPhase2Tests : IDisposable
{
    private readonly EditorHarness _harness;

    // Simple in-memory IEqsTemplateRegistry backed by a dictionary.
    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _templates = new();

        public void Register(EqsQueryTemplate template)
            => _templates[template.BlueprintId] = template;

        public bool TryGetTemplate(uint blueprintId, out EqsQueryTemplate template)
            => _templates.TryGetValue(blueprintId, out template);
    }

    public EqsSolverSystemPhase2Tests()
    {
        _harness = new EditorHarness();
    }

    public void Dispose()
    {
        // Dispose EqsResultPool native array if the solver created it.
        if (_harness.Repo.HasSingleton<EqsResultPool>())
        {
            var pool = _harness.Repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated)
                pool.Results.Dispose();
        }
        _harness.Dispose();
    }

    /// <summary>
    /// T-S1: Full pipeline with 5 hostile enemies populates the cognitive buffer.
    /// </summary>
    [Fact(Timeout = 6_000)]
    public void EqsSolverSystem_Phase2_FullPipeline_PopulatesBuffer()
    {
        // Arrange: register a template that uses EntitiesInRadius + FactionFilter + DistanceScore.
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = 42u,
            Generator     = new EntitiesInRadiusGenerator(),
            FilterCheap   = new IEqsTest[] { new FactionFilterTest() },
            ScoreCheap    = new IEqsTest[] { new DistanceScoreTest() },
            MaxCandidates = 64,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Create observer with SimTransform at origin, EqsSensor (Hostile only), NetworkIdentity.
        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });
        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId   = 42u,
            Epoch         = 1,
            SearchRadius  = 25f,
            FactionFilter = 4u, // bit 2 = Hostile only
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 5001L });

        // Create 5 hostile entities at known positions.
        var enemyPositions = new[]
        {
            new Vector3(2f,  0f, 0f),
            new Vector3(4f,  0f, 0f),
            new Vector3(6f,  0f, 0f),
            new Vector3(8f,  0f, 0f),
            new Vector3(10f, 0f, 0f),
        };

        var enemyEntities = new Entity[enemyPositions.Length];
        for (int i = 0; i < enemyPositions.Length; i++)
        {
            var e = _harness.Repo.CreateEntity();
            _harness.Repo.AddComponent(e, new SimTransform
            {
                Position = enemyPositions[i],
                Rotation = Quaternion.Identity,
            });
            _harness.Repo.AddComponent(e, new PhysicsCollider { Radius = 1.0f });
            _harness.Repo.AddComponent(e, new EntityInfo { ForceId = ForceId.Hostile });
            enemyEntities[i] = e;
        }

        // Act: pump frames until the EqsCognitiveBuffer on the observer is ready.
        _harness.Repo.AddComponent(observer, new EqsCognitiveBuffer());
        bool ready = _harness.PumpUntil(
            () =>
            {
                ref readonly var buf = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
                return buf.IsReady && buf.Count > 0;
            },
            timeoutMs: 5000);

        // Assert
        Assert.True(ready, "EqsCognitiveBuffer should have at least one hostile result within 5 s");
        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.True(buffer.Count > 0);

        // The top result should be one of the enemy entities.
        long topId = buffer.GetSpanRO()[0].EntityId;
        bool topIsEnemy = false;
        for (int i = 0; i < enemyEntities.Length; i++)
        {
            if ((long)enemyEntities[i].PackedValue == topId)
            {
                topIsEnemy = true;
                break;
            }
        }
        Assert.True(topIsEnemy, "Top result should be one of the hostile enemy entities");
    }

    /// <summary>
    /// T-S2: Multiple sensors are all eventually processed by the time-sliced solver.
    /// </summary>
    [Fact(Timeout = 6_000)]
    public void EqsSolverSystem_Phase2_MultipleSensors_AllEventuallyProcessed()
    {
        // Arrange: register a trivial template with no generator (MaxCandidates=1).
        // The solver will emit empty events (count=0 from generator), which still marks IsReady.
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = 99u,
            Generator     = new NullGenerator(),
            MaxCandidates = 1,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Create 10 sensor entities.
        const int SensorCount = 10;
        var sensors = new Entity[SensorCount];
        for (int i = 0; i < SensorCount; i++)
        {
            var e = _harness.Repo.CreateEntity();
            _harness.Repo.AddComponent(e, new EqsSensor { BlueprintId = 99u, Epoch = 1 });
            _harness.Repo.AddComponent(e, new NetworkIdentity { Value = 6000L + i });
            sensors[i] = e;
        }

        // Act: pump until all 10 sensors have IsReady buffers.
        bool allReady = _harness.PumpUntil(
            () =>
            {
                for (int i = 0; i < sensors.Length; i++)
                {
                    if (!_harness.Repo.HasComponent<EqsCognitiveBuffer>(sensors[i])) return false;
                    ref readonly var b = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(sensors[i]);
                    if (!b.IsReady) return false;
                }
                return true;
            },
            timeoutMs: 5000);

        Assert.True(allReady, "All 10 sensors should have IsReady buffers within 5 s");
    }

    /// <summary>
    /// T-S3: Phase 1 fallback -- no registry registered, solver emits empty events.
    /// </summary>
    [Fact(Timeout = 4_000)]
    public void EqsSolverSystem_Phase1Fallback_NoRegistry_EmitsEmptyEvent()
    {
        // Arrange: no registry registered (fallback path).
        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new EqsSensor { BlueprintId = 1u, Epoch = 1 });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 7001L });

        // Act: pump until buffer IsReady.
        bool ready = _harness.PumpUntil(
            () =>
            {
                if (!_harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)) return false;
                ref readonly var b = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
                return b.IsReady;
            },
            timeoutMs: 3000);

        // Assert: IsReady but Count == 0 (Phase 1 stub fallback).
        Assert.True(ready, "EqsCognitiveBuffer should become ready within 3 s via Phase 1 fallback");
        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.True(buffer.IsReady);
        Assert.Equal(0, buffer.Count);
    }

    // Trivial generator that returns 0 candidates (for T-S2 multi-sensor test).
    private sealed class NullGenerator : IEqsGenerator
    {
        public int Generate(Entity observer, ref EqsSensor sensor, Fdp.ModuleHost.Abstractions.ISimulationView view, System.Span<EqsResult> candidates)
            => 0;
    }
}
