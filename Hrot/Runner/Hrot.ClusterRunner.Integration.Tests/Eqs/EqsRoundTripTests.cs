using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Integration round-trip tests for EQS (TASK-EQS-023, TASK-EQS-024, TASK-EQS-029).
/// All tests use EditorHarness (no DDS). Blueprint IDs: 92-95.
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsRoundTripTests : IDisposable
{
    private readonly EditorHarness _harness;

    // ── Inner types ────────────────────────────────────────────────────────────

    // Simple in-memory template registry used by all tests.
    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    // ── T-RT1 helpers ──────────────────────────────────────────────────────────

    // Returns 2 positional candidates relative to the query center.
    private sealed class MockCoverProvider : ICoverProvider
    {
        public int GetCoverPointsInRadius(
            Vector2 center, float radius, Span<CoverPoint> results)
        {
            if (results.Length < 2) return 0;
            results[0] = new CoverPoint { PositionX = center.X,       PositionY = center.Y + 5f };
            results[1] = new CoverPoint { PositionX = center.X + 5f,  PositionY = center.Y      };
            return 2;
        }
    }

    // Navmesh stub: all positions reachable; distance = Euclidean; 1 random point.
    private sealed class MockNavmeshProvider : INavmeshProvider
    {
        public bool IsReachable(Vector2 start, Vector2 end) => true;

        public bool TryGetPathDistance(Vector2 start, Vector2 end, out float distance)
        {
            distance = Vector2.Distance(start, end);
            return true;
        }

        public int GetRandomPointsInRadius(Vector2 center, float radius, Span<Vector2> points)
        {
            if (points.Length < 1) return 0;
            points[0] = new Vector2(center.X + radius * 0.5f, center.Y);
            return 1;
        }
    }

    // ── T-RT2 helpers ──────────────────────────────────────────────────────────

    // Yields exactly 5 positional candidates at PositionX = 10, 20, 30, 40, 50.
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

    // Verifies the span it receives has exactly 3 entries and none are -1L.
    // AssertionPassed is checked by the test after pump.
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
                {
                    if (candidates[i].EntityId == -1L)
                    {
                        AssertionPassed = false;
                        break;
                    }
                }
            }
        }
    }

    // ── T-RT3a / T-RT3b helpers ────────────────────────────────────────────────

    // HasCheapLineOfSight always returns true (all candidates exposed to threat).
    private sealed class ExposedLosServiceMock : ILosService
    {
        public bool HasCheapLineOfSight(Vector2 from, Vector2 to) => true;
    }

    // ── Fixture ────────────────────────────────────────────────────────────────

    public EqsRoundTripTests()
    {
        _harness = new EditorHarness();
    }

    public void Dispose()
    {
        if (_harness.Repo.HasSingleton<EqsResultPool>())
        {
            var pool = _harness.Repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated) pool.Results.Dispose();
        }
        _harness.Dispose();
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// T-RT1 (EQS-023): Offline EditorHarness round-trip -- cover provider populates
    /// the cognitive buffer with at least one positional candidate.
    /// </summary>
    [Fact(Timeout = 8_000)]
    public void Eqs_OfflineEditor_PopulatesCognitiveBufferWithCandidates()
    {
        const uint blueprintId = 92u;

        // Arrange: register providers.
        _harness.Repo.SetSingletonManaged<ICoverProvider>(new MockCoverProvider());
        _harness.Repo.SetSingletonManaged<INavmeshProvider>(new MockNavmeshProvider());

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new CoverPointsGenerator(),
            MaxCandidates = 8,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 9200L });
        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = blueprintId,
            Epoch           = 1,
            SearchRadius    = 50f,
            ThreatThreshold = 0f,
        });

        // Act: pump until buffer is ready with at least one candidate.
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 5000);

        // Assert.
        Assert.True(ready, "T-RT1: CognitiveBuffer should be ready with candidates within 5 s");

        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.True(buffer.Count > 0, "T-RT1: buffer must contain at least one candidate");
        Assert.Equal(0L, buffer.GetTop().EntityId); // positional candidate
    }

    /// <summary>
    /// T-RT2 (EQS-024): ReduceTopK compacts sentinel-rejected entries before ScoreCheap runs.
    /// After filtering out indices 1 and 3, exactly 3 entries survive with the correct X-coords.
    /// </summary>
    [Fact(Timeout = 8_000)]
    public void Eqs_TopKReduction_PreservesPositionalSentinels()
    {
        const uint blueprintId = 93u;

        var scorer = new DummyScoreTest();

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new DeterministicPositionalGenerator(),
            FilterCheap   = new IEqsTest[] { new SentinelRejectionFilterTest() },
            ScoreCheap    = new IEqsTest[] { scorer },
            MaxCandidates = 8,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 9300L });
        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = blueprintId,
            Epoch           = 1,
            SearchRadius    = 50f,
            ThreatThreshold = 0f,
        });

        // Act: pump until buffer is ready.
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 5000);

        Assert.True(ready, "T-RT2: CognitiveBuffer should be ready within 5 s");

        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);

        // Exactly 3 entries survive (indices 0, 2, 4 of original 5).
        Assert.Equal(3, buffer.Count);

        // All are positional (EntityId == 0).
        var span = buffer.GetSpanRO();
        Assert.Equal(0L, span[0].EntityId);
        Assert.Equal(0L, span[1].EntityId);
        Assert.Equal(0L, span[2].EntityId);

        // X-coordinates are exactly {10, 30, 50} (any order).
        var xs = new HashSet<float>();
        for (int i = 0; i < buffer.Count; i++) xs.Add(span[i].PositionX);
        Assert.Contains(10f, xs);
        Assert.Contains(30f, xs);
        Assert.Contains(50f, xs);

        // ReduceTopK ran before ScoreCheap.
        Assert.True(scorer.AssertionPassed,
            "T-RT2: DummyScoreTest must receive exactly 3 compacted entries with no -1L sentinels");
    }

    /// <summary>
    /// T-RT3a (EQS-029): Threat score (100) above threshold (50) -- LOS filter active.
    /// All cover candidates are exposed (ExposedLosServiceMock), so none survive.
    /// </summary>
    [Fact(Timeout = 8_000)]
    public unsafe void Eqs_ThreatThreshold_AboveThreshold_RejectsAllExposedCandidates()
    {
        const uint blueprintId = 94u;

        var los = new ExposedLosServiceMock();
        var provider = new ManualCoverProvider(new[]
        {
            new CoverPoint { PositionX = 5f, PositionY = 0f },
        });

        _harness.Repo.SetSingletonManaged<ICoverProvider>(provider);

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId  = blueprintId,
            Generator    = new CoverPointsGenerator(),
            FilterCheap  = new IEqsTest[] { new CheapLineOfSightTest(los) },
            MaxCandidates = 8,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Threat entity: position provides the slot-based threat position for CheapLineOfSightTest.
        var threatEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(threatEntity, new SimTransform
        {
            Position = new Vector3(30f, 0f, 0f),
            Rotation = Quaternion.Identity,
        });

        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 9400L });
        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = blueprintId,
            Epoch           = 1,
            SearchRadius    = 50f,
            ThreatThreshold = 50f,
            ContextSlot1    = threatEntity,
        });

        // Threat score 100 > threshold 50: LOS filter must activate.
        var mem = new TargetMemory();
        unsafe
        {
            mem.Count           = 1;
            mem.ThreatScores[0] = 100f;
            mem.PositionsX[0]   = 30f;
            mem.PositionsY[0]   = 0f;
        }
        _harness.Repo.AddComponent(observer, mem);

        // Act: pump until buffer is ready (may have Count == 0).
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady,
            timeoutMs: 5000);

        Assert.True(ready, "T-RT3a: CognitiveBuffer should become ready within 5 s");

        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.Equal(0, buffer.Count);
    }

    /// <summary>
    /// T-RT3b (EQS-029): Threat score (10) below threshold (50) -- LOS filter bypassed.
    /// The single cover point survives because the filter is not applied.
    /// </summary>
    [Fact(Timeout = 8_000)]
    public unsafe void Eqs_ThreatThreshold_BelowThreshold_BypassesFilter()
    {
        const uint blueprintId = 95u;

        var los = new ExposedLosServiceMock();
        var provider = new ManualCoverProvider(new[]
        {
            new CoverPoint { PositionX = 5f, PositionY = 0f },
        });

        _harness.Repo.SetSingletonManaged<ICoverProvider>(provider);

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId  = blueprintId,
            Generator    = new CoverPointsGenerator(),
            FilterCheap  = new IEqsTest[] { new CheapLineOfSightTest(los) },
            MaxCandidates = 8,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 9500L });

        // Threat entity: provides the slot-based threat position for CheapLineOfSightTest.
        var threatEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(threatEntity, new SimTransform
        {
            Position = new Vector3(30f, 0f, 0f),
            Rotation = Quaternion.Identity,
        });

        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = blueprintId,
            Epoch           = 1,
            SearchRadius    = 50f,
            ThreatThreshold = 50f,
            ContextSlot1    = threatEntity,
        });

        // Threat score 10 < threshold 50: LOS filter must be bypassed.
        var mem = new TargetMemory();
        unsafe
        {
            mem.Count           = 1;
            mem.ThreatScores[0] = 10f;
            mem.PositionsX[0]   = 30f;
            mem.PositionsY[0]   = 0f;
        }
        _harness.Repo.AddComponent(observer, mem);

        // Act: pump until buffer is ready with the surviving candidate.
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 5000);

        Assert.True(ready, "T-RT3b: CognitiveBuffer should be ready with 1 candidate within 5 s");

        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        Assert.Equal(1, buffer.Count);
    }
}
