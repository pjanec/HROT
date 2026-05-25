using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.SimHost;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Integration tests for <see cref="FindCoverFromTarget"/> EQS template (TASK-EQS-015).
/// Uses <see cref="EditorHarness"/> for the full pipeline.
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class FindCoverFromTargetTests : IDisposable
{
    private readonly EditorHarness _harness;

    // LOS service: returns true (exposed) for candidate positions east of x=2;
    // false (blocked) otherwise. Point A (x=5) is exposed; B and C (x=0) are occluded.
    private sealed class MockLosService : ILosService
    {
        public bool HasCheapLineOfSight(Vector2 from, Vector2 to)
            => from.X > 2f; // Points east of x=2 are exposed to threat at (20,0).
    }

    // Simple in-memory template registry.
    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    public FindCoverFromTargetTests()
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
    /// T-FCT1: Full pipeline: 1 exposed + 2 occluded cover points => 2 results.
    /// </summary>
    [Fact(Timeout = 6_000)]
    public unsafe void FindCoverFromTarget_FullPipeline_TwoOccludedPointsSurvive()
    {
        // Arrange: 3 cover points. Point A (x=5) is exposed; B and C (x=0) are occluded.
        var los = new MockLosService();
        var provider = new ManualCoverProvider(new[]
        {
            new CoverPoint { PositionX = 5f, PositionY = 0f,  Quality = 1f }, // exposed
            new CoverPoint { PositionX = 0f, PositionY = 5f,  Quality = 1f }, // occluded
            new CoverPoint { PositionX = 0f, PositionY = 10f, Quality = 1f }, // occluded
        });

        _harness.Repo.SetSingletonManaged<ICoverProvider>(provider);

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(FindCoverFromTarget.Build(los));
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Create observer at origin with TargetMemory, EqsSensor, NetworkIdentity.
        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });

        // Threat at (20, 0) with score 100.
        var mem = new TargetMemory();
        TargetMemory.AddOrUpdateTarget(ref mem, entityId: 999L, posX: 20f, posY: 0f, scoreBoost: 100f, tick: 1);
        _harness.Repo.AddComponent(observer, mem);

        // Context slot 1 entity -- provides threat position (20, 0) for CheapLineOfSightTest.
        var targetEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(targetEntity, new SimTransform
        {
            Position = new Vector3(20f, 0f, 0f),
            Rotation = Quaternion.Identity,
        });

        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = FindCoverFromTarget.BlueprintId,
            Epoch           = 1,
            SearchRadius    = 25f,
            ThreatThreshold = 50f,
            ContextSlot1    = targetEntity,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8001L });

        // Act: pump until the buffer is ready with results.
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 5000);

        Assert.True(ready, "FindCoverFromTarget should produce results within 5 s");

        // Assert: only 2 occluded points survive.
        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.Equal(2, buffer.Count);

        // All results are positional (EntityId=0).
        var span = buffer.GetSpanRO();
        for (int i = 0; i < buffer.Count; i++)
            Assert.Equal(0L, span[i].EntityId);

        // Top result should be closer (higher score) than the second result.
        Assert.True(span[0].Score >= span[1].Score, "Closer cover point should be ranked higher");
    }

    /// <summary>
    /// T-FCT2: Bypass when no threats -- all cover points survive.
    /// </summary>
    [Fact(Timeout = 6_000)]
    public unsafe void FindCoverFromTarget_BypassWhenNoThreats_AllPointsSurvive()
    {
        // Arrange: MockLosService would reject everything if bypass didn't trigger.
        var los = new MockLosService();
        var provider = new ManualCoverProvider(new[]
        {
            new CoverPoint { PositionX = 5f, PositionY = 0f,  Quality = 1f },
            new CoverPoint { PositionX = 0f, PositionY = 5f,  Quality = 1f },
            new CoverPoint { PositionX = 0f, PositionY = 10f, Quality = 1f },
        });

        _harness.Repo.SetSingletonManaged<ICoverProvider>(provider);

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(FindCoverFromTarget.Build(los));
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });

        // No threats: TargetMemory.Count == 0.
        _harness.Repo.AddComponent(observer, new TargetMemory());

        // Context slot 1 entity -- needed to reach the Count==0 bypass gate.
        var nullThreatTarget = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(nullThreatTarget, new SimTransform
        {
            Position = new Vector3(100f, 0f, 0f),
            Rotation = Quaternion.Identity,
        });

        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = FindCoverFromTarget.BlueprintId,
            Epoch           = 1,
            SearchRadius    = 25f,
            ThreatThreshold = 50f,
            ContextSlot1    = nullThreatTarget,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8002L });

        // Act: pump until ready.
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 5000);

        Assert.True(ready, "FindCoverFromTarget should produce results within 5 s");

        // Assert: all 3 cover points survive (bypass triggered, LOS not applied).
        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.Equal(3, buffer.Count);
    }
}
