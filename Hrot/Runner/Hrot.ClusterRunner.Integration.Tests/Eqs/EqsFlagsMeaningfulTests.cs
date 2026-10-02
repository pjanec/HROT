using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Tests for TASK-EQS-032: <see cref="EqsResult.FlagsMeaningful"/> field.
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsFlagsMeaningfulTests : IDisposable
{
    // Domain range: 211-219 (free gap after EqsDistributedTests 201-210).
    private static int _domainCounter = 210;
    private static int NextDomain() => Interlocked.Increment(ref _domainCounter);

    // A LOS service that always reports no line-of-sight (all cover is "good cover").
    private sealed class AlwaysBlockedLosService : ILosService
    {
        public bool HasCheapLineOfSight(Vector2 observer, Vector2 target) => false;
    }

    // ── Inner types ────────────────────────────────────────────────────────────

    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    // A score test that unconditionally sets FlagsMeaningful bit 0 on every candidate.
    // Used in DDS round-trip test (T-FM4) to avoid the TargetMemory requirement.
    private sealed class AlwaysSetFlagsMeaningfulTest : IEqsTest
    {
        public EqsTestPhase Phase => EqsTestPhase.ScoreCheap;

        public void ExecuteBatch(Entity observer, ref EqsSensor sensor,
            ISimulationView view, Span<EqsResult> candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
                candidates[i].FlagsMeaningful |= 1;
        }
    }

    // Simple positional generator: yields N candidates at fixed positions.
    private sealed class FixedPositionalGenerator : IEqsGenerator
    {
        private readonly float[] _xs;

        public FixedPositionalGenerator(params float[] xs) => _xs = xs;

        public int Generate(Entity observer, ref EqsSensor sensor,
            ISimulationView view, Span<EqsResult> candidates)
        {
            int count = Math.Min(_xs.Length, candidates.Length);
            for (int i = 0; i < count; i++)
                candidates[i] = new EqsResult { EntityId = 0L, PositionX = _xs[i], PositionY = 0f, Score = 1f };
            return count;
        }
    }

    private readonly EditorHarness _harness;

    public EqsFlagsMeaningfulTests()
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

    // ── T-FM1: struct size unchanged ──────────────────────────────────────────

    /// <summary>
    /// T-FM1: replacing <c>_pad</c> with <see cref="EqsResult.FlagsMeaningful"/> must not GROW the
    /// struct — the new field has to live in the tail padding.
    ///
    /// <para>⭐ <c>QA-014</c> — <b>the number is 32, not 24, and that is a deliberate change this
    /// assertion never tracked.</b> 📐 <c>P3D-201</c> promoted the result to 3-D and added
    /// <see cref="EqsResult.PositionZ"/>: <c>long</c>(8) + 4×<c>float</c>(16) + 2×<c>short</c>(4) = 28,
    /// padded to <b>32</b> on the 8-byte alignment the <c>long</c> forces. Before <c>PositionZ</c> the
    /// same arithmetic gave exactly 24. ⚠ This project's own csproj already carries
    /// <c>EQS_HAS_POSITIONZ</c> for that promotion — only this one assertion was left behind.</para>
    ///
    /// <para>⭐⭐ <b>The invariant T-FM1 exists for still HOLDS</b>, and is now stated so it cannot drift
    /// again: with <c>_pad</c> the struct was 8+16+2+2 = 28 → 32 as well, so <c>FlagsMeaningful</c> is
    /// still free. ⛔ A jump to 40 would mean the field stopped fitting the padding — that is the
    /// regression this guards, not the literal 24.</para>
    /// </summary>
    [Fact]
    public void EqsResult_FlagsMeaningful_StructSizeUnchanged()
    {
        Assert.Equal(32, Marshal.SizeOf<EqsResult>());
    }

    // ── T-FM2: bypass path does NOT set FlagsMeaningful ─────────────────────

    /// <summary>
    /// T-FM2: When the threat score is below <see cref="EqsSensor.ThreatThreshold"/>,
    /// <see cref="CheapLineOfSightTest"/> bypasses evaluation and <c>FlagsMeaningful</c>
    /// must remain zero (bit 0 not set).
    /// </summary>
    [Fact(Timeout = 6_000)]
    public unsafe void CheapLosTest_BelowThreshold_FlagsMeaningfulZero()
    {
        // Arrange: template with CheapLOS (always-blocked service).
        const uint blueprintId = 2110001u;
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new FixedPositionalGenerator(1f, 2f, 3f),
            FilterCheap   = new IEqsTest[] { new CheapLineOfSightTest(new AlwaysBlockedLosService()) },
            MaxCandidates = 8,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero, Rotation = Quaternion.Identity,
        });

        // Threat score 10 < threshold 50 → bypass.
        var mem1 = new TargetMemory();
        TargetMemory.AddOrUpdateTarget(ref mem1, entityId: 1L, posX: 20f, posY: 0f, scoreBoost: 10f, tick: 1);
        _harness.Repo.AddComponent(observer, mem1);

        // Context slot 1 entity -- needed to reach the threshold bypass gate.
        var targetEntity1 = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(targetEntity1, new SimTransform
        {
            Position = new Vector3(20f, 0f, 0f), Rotation = Quaternion.Identity,
        });

        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = blueprintId,
            Epoch           = 1,
            SearchRadius    = 25f,
            ThreatThreshold = 50f, // threat (10) < threshold (50) → bypass
            ContextSlot1    = targetEntity1,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8200L });

        // Act: pump until buffer is ready.
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady,
            timeoutMs: 5000);

        Assert.True(ready, "CognitiveBuffer must become ready within timeout");

        // Assert: all surviving candidates have FlagsMeaningful bit 0 == 0 (test bypassed).
        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        var span = buffer.GetSpanRO();
        for (int i = 0; i < buffer.Count; i++)
        {
            Assert.Equal(0, span[i].FlagsMeaningful & 1);
        }
    }

    // ── T-FM3: above-threshold path sets FlagsMeaningful ────────────────────

    /// <summary>
    /// T-FM3: When the threat score is above <see cref="EqsSensor.ThreatThreshold"/>,
    /// <see cref="CheapLineOfSightTest"/> runs, keeps covered candidates, and sets
    /// <c>FlagsMeaningful</c> bit 0.
    /// </summary>
    [Fact(Timeout = 6_000)]
    public unsafe void CheapLosTest_AboveThreshold_FlagsMeaningfulSet()
    {
        // Arrange: same template, threat score 100 > threshold 50.
        const uint blueprintId = 2120001u;
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new FixedPositionalGenerator(1f, 2f, 3f),
            FilterCheap   = new IEqsTest[] { new CheapLineOfSightTest(new AlwaysBlockedLosService()) },
            MaxCandidates = 8,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform
        {
            Position = Vector3.Zero, Rotation = Quaternion.Identity,
        });

        var mem3 = new TargetMemory();
        TargetMemory.AddOrUpdateTarget(ref mem3, entityId: 2L, posX: 20f, posY: 0f, scoreBoost: 100f, tick: 1);
        _harness.Repo.AddComponent(observer, mem3);

        // Context slot 1 entity -- provides threat position for CheapLineOfSightTest.
        var targetEntity3 = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(targetEntity3, new SimTransform
        {
            Position = new Vector3(20f, 0f, 0f), Rotation = Quaternion.Identity,
        });

        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId     = blueprintId,
            Epoch           = 1,
            SearchRadius    = 25f,
            ThreatThreshold = 50f, // threat (100) > threshold (50) → test runs
            ContextSlot1    = targetEntity3,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8201L });

        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 5000);

        Assert.True(ready, "CognitiveBuffer must be ready with results within timeout");

        // Assert: all surviving candidates have FlagsMeaningful bit 0 set.
        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        var span = buffer.GetSpanRO();
        for (int i = 0; i < buffer.Count; i++)
        {
            Assert.NotEqual(0, span[i].FlagsMeaningful & 1);
        }
    }

    // ── T-FM4: FlagsMeaningful survives DDS round-trip ────────────────────────

    /// <summary>
    /// T-FM4: Verifies that <see cref="EqsResult.FlagsMeaningful"/> is carried through the full
    /// DDS serialisation path (Muscle EqsResultEventEgressTranslator → Brain EqsResultIngressTranslator
    /// → EqsResultUpdateSystem) and arrives intact in the Brain's <see cref="EqsCognitiveBuffer"/>.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void FlagsMeaningful_SurvivesDdsRoundTrip()
    {
        int domainId = NextDomain();
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        // Register template on Muscle (SimHost) world.
        // AlwaysSetFlagsMeaningfulTest sets bit 0 on every candidate without needing TargetMemory.
        const uint blueprintId = 2130001u;
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new FixedPositionalGenerator(5f, 10f, 15f),
            ScoreCheap    = new IEqsTest[] { new AlwaysSetFlagsMeaningfulTest() },
            MaxCandidates = 8,
        });
        harness.SimHost.World!.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Spawn entity with split authority (Brain = CGF, Muscle = SimHost).
        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for the Muscle ghost entity to appear.
        bool entityReady = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            timeoutFrames: 2000);
        Assert.True(entityReady, "Muscle ghost entity must appear within timeout");

        // Attach EqsSensor to Brain entity.
        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);
        harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor
        {
            BlueprintId  = blueprintId,
            Epoch        = 1u,
            SearchRadius = 50f,
        });

        // Pump until Brain EqsCognitiveBuffer is ready with at least one result.
        bool bufferReady = harness.PumpUntil(() =>
        {
            var world = harness.Cgf!.World;
            if (world == null) return false;
            if (!world.HasComponent<EqsCognitiveBuffer>(cgfEntity)) return false;
            ref readonly var buf = ref world.GetComponentRO<EqsCognitiveBuffer>(cgfEntity);
            return buf.IsReady && buf.Count > 0;
        }, timeoutFrames: 2000);

        Assert.True(bufferReady, "Brain EqsCognitiveBuffer must be ready with at least one result");

        // Assert: top result has FlagsMeaningful bit 0 set (field survived DDS round-trip).
        ref readonly var buffer = ref harness.Cgf!.World!.GetComponentRO<EqsCognitiveBuffer>(cgfEntity);
        Assert.NotEqual(0, buffer.GetTop().FlagsMeaningful & 1);
    }
}
