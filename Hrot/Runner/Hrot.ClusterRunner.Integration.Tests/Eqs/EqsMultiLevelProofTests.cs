using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// P3D-402 — Multi-level proof fixture (Design §6 Axis-2, §9 O-2).
///
/// <para>The whole promotion exists so a position under a bridge and one on the deck above —
/// sharing X/Y and differing only in Z — are distinguishable. Before the promotion both cover
/// points collapsed to the same 2D candidate; now each carries its real <c>PositionZ</c> through
/// the EQS cover query, so the two levels are produced as distinct candidates and are never
/// merged. Flat parity alone (P3D-403) would pass even if 3D did nothing — this fixture is the
/// positive proof.</para>
///
/// <para><b>O-2 precondition (hard requirement):</b> the deck clearance MUST strictly exceed the
/// agent's configured <c>walkableHeight</c>, or a Recast-backed 3D snap could not reliably resolve
/// the two overlapping surfaces and the test would be meaningless. The fixture bakes this in as an
/// asserted precondition.</para>
///
/// <para><b>Environment note:</b> this codebase has no DotRecast-backed navmesh provider (only the
/// Fake/Stub providers), and Recast's <c>walkableHeight</c> is not surfaced as a runtime symbol.
/// The fixture therefore proves the multi-level disambiguation on the cover-query path (which now
/// carries real Z, P3D-203/204) and asserts the O-2 clearance relation against a fixture constant.
/// See DEBT-TRACKER (P3D-402-DEBT) for the DotRecast 3D-snap follow-up.</para>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsMultiLevelProofTests : IDisposable
{
    // O-2: the bridge deck clearance must strictly exceed the agent's walkableHeight.
    private const float WalkableHeight = 2.0f;   // agent capsule height (Recast walkableHeight analogue)
    private const float DeckClearance  = 6.0f;   // bridge deck height above the street

    private readonly EditorHarness _harness = new();

    public void Dispose()
    {
        if (_harness.Repo.HasSingleton<EqsResultPool>())
        {
            var pool = _harness.Repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated) pool.Results.Dispose();
        }
        _harness.Dispose();
    }

    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    // LOS service that never blocks; the scenario uses the no-threat bypass so every cover point
    // survives the filter and both levels appear in the Top-K.
    private sealed class ClearLosService : ILosService
    {
        public bool HasCheapLineOfSight(Vector2 from, Vector2 to) => false; // "blocked" => kept as cover
    }

    private int RunCoverQuery(ICoverProvider provider, out EqsResult[] results)
    {
        _harness.Repo.SetSingletonManaged<ICoverProvider>(provider);

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(FindCoverFromTarget.Build(new ClearLosService()));
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        var observer = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(observer, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        _harness.Repo.AddComponent(observer, new TargetMemory()); // no threats -> LOS filter bypass

        var threatSlot = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(threatSlot, new SimTransform { Position = new Vector3(100f, 0f, 0f), Rotation = Quaternion.Identity });

        _harness.Repo.AddComponent(observer, new EqsSensor
        {
            BlueprintId  = FindCoverFromTarget.BlueprintId,
            Epoch        = 1,
            SearchRadius = 50f,
            ThreatThreshold = 50f,
            ContextSlot1 = threatSlot,
        });
        _harness.Repo.AddComponent(observer, new NetworkIdentity { Value = 8201L });

        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(observer)
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).IsReady
               && _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer).Count > 0,
            timeoutMs: 5000);
        Assert.True(ready, "Multi-level cover query produced no results within 5 s");

        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(observer);
        var span = buffer.GetSpanRO();
        results = new EqsResult[buffer.Count];
        for (int i = 0; i < buffer.Count; i++) results[i] = span[i];
        return buffer.Count;
    }

    [Fact]
    public void DeckClearance_StrictlyExceeds_WalkableHeight_Precondition()
    {
        // O-2: if this fails the multi-level snap would be invalid and the proof meaningless.
        Assert.True(DeckClearance > WalkableHeight,
            $"O-2 precondition violated: deckClearance={DeckClearance} must exceed walkableHeight={WalkableHeight}");
    }

    [Fact]
    public void BridgeOverStreet_ProducesTwoDistinctLevels_NotMerged()
    {
        // Bridge-over-street: a cover point under the bridge (Z≈0) and one on the deck (Z≈deck)
        // share the SAME X/Y and differ only in altitude.
        const float sharedX = 10f, sharedY = 0f;
        var provider = new ManualCoverProvider(new[]
        {
            new CoverPoint { PositionX = sharedX, PositionY = sharedY, PositionZ = 0f,            Quality = 1f }, // under bridge
            new CoverPoint { PositionX = sharedX, PositionY = sharedY, PositionZ = DeckClearance, Quality = 1f }, // on deck
        });

        int count = RunCoverQuery(provider, out var results);

        // Both levels survive as DISTINCT candidates (not collapsed to one).
        Assert.Equal(2, count);
        Assert.NotEqual(results[0].PositionZ, results[1].PositionZ);

        // They share X/Y exactly...
        Assert.Equal(results[0].PositionX, results[1].PositionX);
        Assert.Equal(results[0].PositionY, results[1].PositionY);

        // ...and one is the street (≈0) and one is the deck (≈DeckClearance).
        float lo = MathF.Min(results[0].PositionZ, results[1].PositionZ);
        float hi = MathF.Max(results[0].PositionZ, results[1].PositionZ);
        Assert.Equal(0f, lo, 3);
        Assert.Equal(DeckClearance, hi, 3);

        // The two surfaces are separated by more than the agent walkable height (O-2).
        Assert.True(hi - lo > WalkableHeight,
            $"Level separation {hi - lo} must exceed walkableHeight {WalkableHeight}");
    }

    [Fact]
    public void FlatGround_ReturnsSingleLevel_NoSpuriousSecondSurface()
    {
        // The same query on flat ground (distinct X/Y, all Z≈0) returns one level per location.
        var provider = new ManualCoverProvider(new[]
        {
            new CoverPoint { PositionX = 10f, PositionY = 0f,  PositionZ = 0f, Quality = 1f },
            new CoverPoint { PositionX = 0f,  PositionY = 10f, PositionZ = 0f, Quality = 1f },
        });

        int count = RunCoverQuery(provider, out var results);

        Assert.Equal(2, count);
        // All candidates are on a single (ground) level — no spurious deck surface.
        foreach (var r in results)
            Assert.True(MathF.Abs(r.PositionZ) <= 1e-3f, $"Flat-ground candidate had non-zero Z {r.PositionZ}");
    }
}
