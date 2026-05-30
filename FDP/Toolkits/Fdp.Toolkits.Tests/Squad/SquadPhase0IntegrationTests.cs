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
    ///   <item>Blackboard write-through via <see cref="SquadCognitiveState.Project"/>.</item>
    ///   <item>Danger-area sensor filling a full-capacity buffer.</item>
    ///   <item><see cref="DecisionKind.ManeuverSelect"/> having the correct numeric value.</item>
    /// </list>
    /// </summary>
    public unsafe class SquadPhase0IntegrationTests
    {
        [Fact]
        public void SquadCognitiveState_WriteThroughBb()
        {
            // Write a sentinel value via the projection and confirm it appears in raw bb bytes.
            Blackboard1024 bb = default;
            ref SquadCognitiveState scs = ref SquadCognitiveState.Project(ref bb);
            scs.ManeuverKind   = 7;
            scs.PhaseId        = 3;
            scs.ActiveFeatureId = 0xDEAD_C0DEu;

            // Re-project and read back.
            ref SquadCognitiveState readBack = ref SquadCognitiveState.Project(ref bb);
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
