using System;
using System.Linq;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Gizmos;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// ⭐ CE-4208 (R-134) — <see cref="PipelineTargetWire"/> is the wire twin of the engine's <see cref="PipelineTarget"/>
    /// (GizmoMap.Contracts). The publisher converts with a byte cast, so every engine member must have a wire member of the same
    /// name AND value, and the underlying type must match: a renamed / renumbered / added engine member fails here.
    /// </summary>
    public class PipelineTargetWireTests
    {
        [Fact]
        public void EveryEngineMember_HasAnEqualValuedWireMember()
        {
            Assert.Equal(Enum.GetUnderlyingType(typeof(PipelineTarget)), Enum.GetUnderlyingType(typeof(PipelineTargetWire)));
            foreach (var name in Enum.GetNames<PipelineTarget>())
            {
                Assert.True(Enum.IsDefined(typeof(PipelineTargetWire), name), $"PipelineTargetWire has no member '{name}'");
                Assert.Equal(Convert.ToUInt64(Enum.Parse<PipelineTarget>(name)), Convert.ToUInt64(Enum.Parse<PipelineTargetWire>(name)));
            }
        }

        [Fact]
        public void TheWireEnum_HasNoExtraMember()
            => Assert.Equal(Enum.GetNames<PipelineTarget>().OrderBy(n => n), Enum.GetNames<PipelineTargetWire>().OrderBy(n => n));
    }
}
