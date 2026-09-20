using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Squad.DangerArea.Fake;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests
{
    /// <summary>
    /// P0-05: Thin integration slice that exercises the three cross-cutting concerns
    /// introduced by BATCH-20 together:
    /// <list type="bullet">
    ///   <item>Squad state round-trips through its OWN component (O1 — it was a blackboard projection).</item>
    ///   <item>Danger-area sensor filling a full-capacity buffer.</item>
    ///   <item><see cref="DecisionKind.ManeuverSelect"/> having the correct numeric value.</item>
    /// </list>
    /// </summary>
    public unsafe class SquadPhase0IntegrationTests
    {
        [Fact]
        public void SquadCognitiveState_RoundTripsThroughItsOwnComponent()
        {
            // ⭐ O1 (2026-09-20) — this used to write through SquadCognitiveState.Project(ref bb) and
            //   confirm the bytes appeared in a raw Blackboard1024. That aliasing is exactly what O1
            //   removed; the claim that survives is the round trip itself.
            using var repo = new Fdp.Core.EntityRepository();
            repo.RegisterComponent<SquadCognitiveState>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, default(SquadCognitiveState));

            ref SquadCognitiveState scs = ref repo.GetComponentRW<SquadCognitiveState>(e);
            scs.ManeuverKind   = 7;
            scs.PhaseId        = 3;
            scs.ActiveFeatureId = 0xDEAD_C0DEu;

            ref SquadCognitiveState readBack = ref repo.GetComponentRW<SquadCognitiveState>(e);
            Assert.Equal((ushort)7,          readBack.ManeuverKind);
            Assert.Equal((ushort)3,          readBack.PhaseId);
            Assert.Equal(0xDEAD_C0DEu,       readBack.ActiveFeatureId);
        }

        [Fact]
        public void DangerAreaProvider_FourFeatures_FillsBuffer()
        {
            var provider = new FakeDangerAreaProvider()
                .Add("alpha",   DangerAreaKind.OpenGround,    0.1f)
                .Add("beta",    DangerAreaKind.StreetCrossing, 0.6f)
                .Add("gamma",   DangerAreaKind.Intersection,   0.8f)
                .Add("delta",   DangerAreaKind.ChokePoint,     0.9f);

            Span<DangerAreaDescriptor> buf = stackalloc DangerAreaDescriptor[4];
            provider.Refresh(default, default, buf, out int count);

            Assert.Equal(4, count);
            for (int i = 0; i < 4; i++)
                Assert.NotEqual(0u, buf[i].FeatureId);
        }

        [Fact]
        public void DecisionKind_ManeuverSelect_ValueIs3()
        {
            Assert.Equal(3, (int)DecisionKind.ManeuverSelect);
        }
    }
}
