using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3264</c>..<c>CE-3266</c> (ui's people-display G1 / G2 / G3; docs/DESIGN_Civilians_And_Crowd_Control.md §7.18) — what a man says,
    /// how a civilian seems and a blow struck each reach the other node with every field; an entity crosses as a NETWORK id.
    /// </summary>
    public sealed class PeopleDisplayWireTests
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private static (EntityRepository World, NetworkEntityMap Map, Entity A, Entity B) Node()
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<Utterance>();
            w.RegisterComponent<Demeanour>();
            w.RegisterEvent<StrikeNotification>();
            var map = new NetworkEntityMap();
            Entity a = w.CreateEntity(), b = w.CreateEntity();
            map.Register(60L, a); map.Register(61L, b);
            return (w, map, a, b);
        }

        /// <summary>✅ Red-proof: the ingress dropping the target ⇒ the command is addressed to nobody ⇒ red.</summary>
        [Fact]
        public void CE3264_AnUtterance_ReachesTheOtherNode_ItsTargetMappedToThatNodesEntity()
        {
            var sample = EntityUtteranceEgressTranslator.ToSample(60L, new Utterance { Kind = UtteranceKind.CmdHandsUp, Until = 42.5 }, targetId: 61L);
            var (node, map, officer, man) = Node();
            using (node)
            {
                using var cmd = new EntityCommandBuffer();
                new EntityUtteranceIngressTranslator(participant: null, map).Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.True(Utterances.Addressed(node, man, 40, out var who, out var kind));
                Assert.Equal((officer, UtteranceKind.CmdHandsUp), (who, kind));
                Assert.Equal(42.5, node.GetComponentRO<Utterance>(officer).Until);
            }
        }

        /// <summary>✅ Red-proof: the ingress dropping the anger ⇒ red.</summary>
        [Fact]
        public void CE3265_ADemeanour_ReachesTheOtherNode()
        {
            var sample = EntityDemeanourEgressTranslator.ToSample(60L, new Demeanour { Mode = CivilianMode.Riot, Fear = 1, Anger = 4 });
            var (node, map, man, _) = Node();
            using (node)
            {
                using var cmd = new EntityCommandBuffer();
                new EntityDemeanourIngressTranslator(participant: null, map).Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.True(Demeanours.Of(node, man, out var d));
                Assert.Equal((CivilianMode.Riot, (byte)1, (byte)4), (d.Mode, d.Fear, d.Anger));
            }
        }

        /// <summary>A blow struck on node 1 crosses with its weapon and arrives on node 2 as a remote notification; node 1's own sample and a
        /// remote one are never re-sent. ✅ Red-proof: the egress not filtering remote events ⇒ an echo ⇒ red.</summary>
        [Fact]
        public void CE3266_ABlow_CrossesWithItsWeapon_AndNeverEchoes()
        {
            var (src, srcMap, striker, victim) = Node();
            var (dst, dstMap, dStriker, dVictim) = Node();
            using (src) using (dst)
            {
                var writer = new CapturingWriter<MeleeStrike>();
                src.Bus.Publish(new StrikeNotification { Striker = striker, Target = victim, Weapon = MeleeWeapon.MeleeKnife });
                src.Bus.Publish(new StrikeNotification { Striker = striker, Target = victim, IsRemote = true });   // an arrival: not re-sent
                src.Bus.SwapBuffers();
                new MeleeStrikeEgressTranslator(writer, srcMap, localNodeId: 1).ScanAndPublish(src);
                var sent = Assert.Single(writer.Written);
                Assert.Equal((1L, 60L, 61L, (byte)MeleeWeapon.MeleeKnife), (sent.SourceNodeId, sent.StrikerEntityId, sent.TargetEntityId, sent.Weapon));

                Assert.True(new MeleeStrikeIngressTranslator(null, dstMap, localNodeId: 2).ToEvent(in sent, out var evt));
                Assert.Equal((dStriker, dVictim, MeleeWeapon.MeleeKnife, true), (evt.Striker, evt.Target, evt.Weapon, evt.IsRemote));
                Assert.False(new MeleeStrikeIngressTranslator(null, srcMap, localNodeId: 1).ToEvent(in sent, out _));   // its own sample
            }
        }
    }
}
