using System;
using System.Linq;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ CE-1128 (docs/DESIGN_Visual_Effects.md §10 IM-3, the module diagram) — the marks rounds leave cross the wire as their OWN topic
    /// (<c>RoundImpact</c>, never the detonation): the muscle writes it, every viewer reads it back as a remote
    /// <see cref="RoundImpactNotification"/>, and a MuscleGround node never reads it (its own samples would draw every mark twice).
    /// </summary>
    [Collection("SimHostDds")]
    public class RoundImpactTranslatorTests
    {
        [Fact]
        public void CE1128_ARoundImpactSample_BecomesARemoteImpact_WithItsFace_AndTheShooterResolved()
        {
            using var world = new EntityRepository();
            world.RegisterEvent<RoundImpactNotification>();
            var map = new NetworkEntityMap();
            var shooter = world.CreateEntity();
            map.Register(7L, shooter);
            var view = (ISimulationView)world;
            var cmd = (EntityCommandBuffer)view.GetCommandBuffer();

            new RoundImpactIngressTranslator(participant: null, map).ProcessSample(new RoundImpact
            {
                ShooterEntityId = 7L, X = 1f, Y = 2f, Z = 3f, NormalY = -1f, TravelY = 4f, MunitionType = 6003L, Kind = 1,
            }, cmd);
            cmd.Playback(world);
            world.Bus.SwapBuffers();

            var e = Assert.Single(world.Bus.Read<RoundImpactNotification>().ToArray());
            Assert.True(e.IsRemote);
            Assert.Equal(shooter, e.Shooter);
            Assert.Equal((1f, 2f, 3f, -1f, 4f, 6003L, (byte)1), (e.X, e.Y, e.Z, e.NormalY, e.TravelY, e.Ammo, e.Kind));
        }

        private static Type[] Compose(DdsParticipant participant, NodeRole role)
            => SimHostAuxiliaryTranslatorPack
                .Create(participant, new NetworkEntityMap(), new FdpEventBus(), localNodeId: 1, role)
                .Select(t => t.GetType()).ToArray();

        [Theory]
        [InlineData(NodeRole.MuscleGround, 1, 0)]                          // the muscle writes, never reads (no loopback)
        [InlineData(NodeRole.Perception | NodeRole.MuscleGround, 1, 0)]
        [InlineData(NodeRole.Perception, 0, 1)]                            // a viewer reads
        [InlineData(NodeRole.Brain, 0, 1)]
        public void CE1128_TheMuscleWritesTheMarks_EveryViewerReadsThemOnce(NodeRole role, int egress, int ingress)
        {
            using var participant = new DdsParticipant(231u);
            var set = Compose(participant, role);
            Assert.Equal(egress, set.Count(t => t == typeof(RoundImpactEgressTranslator)));
            Assert.Equal(ingress, set.Count(t => t == typeof(RoundImpactIngressTranslator)));
        }
    }
}
