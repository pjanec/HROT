using System;
using System.Collections.Generic;
using System.Threading;
using Fbt;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Common;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Tests for TASK-EQS-034: <see cref="EqsSensor.ScoreDeltaThreshold"/> and
/// <see cref="EqsPublishPolicy.ScoreDelta"/> publish suppression.
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsScoreDeltaTests : IDisposable
{
    // Domain range: 221-229.
    private static int _domainCounter = 220;
    private static int NextDomain() => Interlocked.Increment(ref _domainCounter);

    // ── Inner types ────────────────────────────────────────────────────────────

    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    // Generator whose scores can be changed between solver evaluations.
    private sealed class MutableScoreGenerator : IEqsGenerator
    {
        // Scores to emit. Sorted descending by the caller to match the solver's output order.
        public float[] Scores = { 1.0f, 0.8f, 0.6f };

        public int Generate(Entity observer, ref EqsSensor sensor,
            ISimulationView view, Span<EqsResult> candidates)
        {
            int count = Math.Min(Scores.Length, candidates.Length);
            for (int i = 0; i < count; i++)
                candidates[i] = new EqsResult { EntityId = 0L, PositionX = (float)i, PositionY = 0f, Score = Scores[i] };
            return count;
        }
    }

    private readonly EditorHarness _harness;

    public EqsScoreDeltaTests()
    {
        _harness = new EditorHarness();
    }

    public void Dispose()
    {
        if (_harness.Repo.HasSingletonUnmanaged<EqsResultPool>())
        {
            ref var rp = ref _harness.Repo.GetSingletonUnmanaged<EqsResultPool>();
            if (rp.Results.IsCreated) rp.Results.Dispose();
        }
        _harness.Dispose();
    }

    // ── T-SD1: ScoreDelta policy suppresses small score changes ──────────────

    /// <summary>
    /// T-SD1: When <see cref="EqsSensor.PublishPolicy"/> is <see cref="EqsPublishPolicy.ScoreDelta"/>
    /// and all top-K score deltas stay within <see cref="EqsSensor.ScoreDeltaThreshold"/>,
    /// the solver must suppress the publish (<see cref="EqsCognitiveBuffer.LastUpdateTick"/> unchanged).
    /// When a delta exceeds the threshold, the solver must publish (tick advances).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ScoreDelta_SuppressesSmallChanges_PublishesLargeChanges()
    {
        // Arrange: template with mutable generator (no filters, no score tests).
        const uint blueprintId = 2210001u;
        var generator = new MutableScoreGenerator();
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = generator,
            MaxCandidates = 8,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = System.Numerics.Vector3.Zero,
            Rotation = System.Numerics.Quaternion.Identity,
        });
        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId         = blueprintId,
            Epoch               = 1u,
            SearchRadius        = 50f,
            PublishPolicy       = (byte)EqsPublishPolicy.ScoreDelta,
            ScoreDeltaThreshold = 0.1f,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8400L });

        // Phase 1: pump until buffer has first result (initial publish).
        bool phase1 = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 5000);
        Assert.True(phase1, "Phase 1: buffer must become ready with initial results");
        uint tick1 = _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).LastUpdateTick;
        Assert.True(tick1 > 0, "Phase 1: LastUpdateTick must be > 0 after initial publish");

        // Phase 2: change scores by small deltas (all <= 0.1) and pump ≥ 40 frames.
        // The solver should evaluate at least once but suppress the publish.
        generator.Scores = new float[] { 1.02f, 0.79f, 0.61f };
        _harness.PumpFrames(40); // 40 * 5ms = 200ms >= 2 solver periods (solver = 10Hz)
        uint tickAfterSmallChange = _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).LastUpdateTick;
        Assert.Equal(tick1, tickAfterSmallChange);

        // Phase 3: change scores by large deltas (max delta = 0.2 > 0.1) and pump until publish.
        generator.Scores = new float[] { 1.0f, 0.6f, 0.4f };
        bool phase3 = _harness.PumpUntil(
            () => _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).LastUpdateTick > tick1,
            timeoutMs: 3000);
        Assert.True(phase3, "Phase 3: large score change must trigger a new publish");
        Assert.True(
            _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).LastUpdateTick > tick1,
            "Phase 3: LastUpdateTick must advance after large score change");
    }

    // ── T-SD2: ScoreDeltaThreshold change increments Epoch ───────────────────

    /// <summary>
    /// T-SD2: Changing <see cref="EqsParams.ScoreDeltaThreshold"/> in <c>Action_MaintainEqsSensor</c>
    /// must increment <see cref="EqsSensor.Epoch"/> exactly once.
    /// </summary>
    [Fact]
    public void MaintainEqsSensor_ScoreDeltaThresholdChange_IncrementsEpoch()
    {
        var repo   = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(repo);
        try
        {
            var entity = repo.CreateEntity();
            var p      = new EqsParams { BlueprintId = 1u, SearchRadius = 50f, ScoreDeltaThreshold = 0.1f };
            var state  = new BehaviorTreeState();
            var ctx    = new BTreeContext { Self = entity, World = repo };

            // First tick: sensor added with Epoch=1.
            EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
            Assert.Equal(1u, repo.GetComponentRO<EqsSensor>(entity).Epoch);
            Assert.Equal(0.1f, repo.GetComponentRO<EqsSensor>(entity).ScoreDeltaThreshold);

            // Second tick with same params: Epoch stays at 1.
            EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
            Assert.Equal(1u, repo.GetComponentRO<EqsSensor>(entity).Epoch);

            // Third tick: change ScoreDeltaThreshold only. Epoch must increment to 2.
            p.ScoreDeltaThreshold = 0.25f;
            EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
            Assert.Equal(2u, repo.GetComponentRO<EqsSensor>(entity).Epoch);
            Assert.Equal(0.25f, repo.GetComponentRO<EqsSensor>(entity).ScoreDeltaThreshold);

            // Fourth tick: same params again. Epoch stays at 2.
            EqsLifecycleNodes.Action_MaintainEqsSensor(ref p, ref state, ref ctx);
            Assert.Equal(2u, repo.GetComponentRO<EqsSensor>(entity).Epoch);
        }
        finally
        {
            repo.Dispose();
        }
    }

    // ── T-SD3: ScoreDeltaThreshold survives DDS round-trip ───────────────────

    /// <summary>
    /// T-SD3: Verifies that <see cref="EqsSensor.ScoreDeltaThreshold"/> is correctly
    /// serialised by <c>EqsSensorConfigEgressTranslator</c> and deserialised by
    /// <c>EqsSensorConfigIngressTranslator</c> so that the field arrives intact on
    /// the Muscle (SimHost) ghost entity.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void ScoreDeltaThreshold_SurvivesDdsRoundTrip()
    {
        int domainId = NextDomain();
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for Muscle ghost to appear.
        bool entityReady = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            timeoutFrames: 2000);
        Assert.True(entityReady, "Muscle ghost entity must appear within timeout");

        // Attach EqsSensor with a specific ScoreDeltaThreshold to Brain entity.
        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);
        const float expectedThreshold = 0.25f;
        harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor
        {
            BlueprintId         = 221u,
            Epoch               = 1u,
            SearchRadius        = 25f,
            PublishPolicy       = (byte)EqsPublishPolicy.ScoreDelta,
            ScoreDeltaThreshold = expectedThreshold,
        });

        // Pump until Muscle ghost has EqsSensor with the expected ScoreDeltaThreshold.
        bool replicated = harness.PumpUntil(() =>
        {
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity simEntity))
                return false;
            if (!harness.SimHost.World!.HasComponent<EqsSensor>(simEntity))
                return false;
            ref readonly var s = ref harness.SimHost.World.GetComponentRO<EqsSensor>(simEntity);
            return MathF.Abs(s.ScoreDeltaThreshold - expectedThreshold) < 0.0001f;
        }, timeoutFrames: 2000);

        Assert.True(replicated, $"EqsSensor.ScoreDeltaThreshold={expectedThreshold} must replicate to Muscle within timeout");

        // Final assertion: confirm exact value.
        harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity finalSimEntity);
        ref readonly var sensor = ref harness.SimHost.World!.GetComponentRO<EqsSensor>(finalSimEntity);
        Assert.Equal(expectedThreshold, sensor.ScoreDeltaThreshold, precision: 4);
    }
}
