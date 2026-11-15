using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ <c>CE-4601</c> (B-4) — the bulldozer's blade-cut marker crosses the wire (<c>dtBladeCut</c>, Brain group) and the Muscle's cut reads the hull's mover by <c>dtWorldPos</c>.</summary>
    public sealed class BladeCutWireTests
    {
        [Fact]
        public void TheOrdinals_AreTheDescriptors()
        {
            Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtWorldPos, DozerCutSystem.WorldPosOrdinal);
            Assert.Equal(186L, (long)Hrot.NED.Descriptors.EDescriptorType.dtBladeCut);
        }

        [Fact]
        public void TheMarker_RoundTrips()
        {
            var c = new BladeCut { Active = 1, Target = BladeCutTarget.Wall, At = new Vector3(1f, 2f, 3f), PushSeconds = 7f };
            var s = EntityBladeCutEgressTranslator.ToSample(42, in c);
            Assert.True(EntityBladeCutEgressTranslator.Same(in s, in c));
            var back = EntityBladeCutIngressTranslator.FromSample(in s);
            Assert.Equal(c.At, back.At); Assert.Equal(c.Target, back.Target); Assert.Equal(c.Active, back.Active); Assert.Equal(c.PushSeconds, back.PushSeconds);
        }
    }
}
