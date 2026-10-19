using System.Collections.Generic;
using System.Linq;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.Map.Common.Translators;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-1223</c> (Entity Control §8k S-2, R-1010) — a man's <see cref="UnderFire"/> stamp (set on the Muscle by the near-miss and hit
    /// producers) crosses to every other node, so a remote first-person station's <c>HitModel.Sigma</c> doubles his spread for
    /// <c>UnderFireSeconds</c> after it, as the Muscle's does. Before it the stamp was Muscle-local and the remote ring showed half the spread.
    /// </summary>
    [Collection("SimHostDds")]   // the pack test opens a real participant
    public sealed class UnderFireWireTests
    {
        private sealed class CapturingWriter : IDdsWriter<EntityUnderFire>
        {
            public List<EntityUnderFire> Written { get; } = new();
            public void Write(EntityUnderFire sample) => Written.Add(sample);
            public void DisposeInstance(EntityUnderFire key) { }
        }

        private static readonly WeaponMountDto Rifle = new() { DispersionMils = 6f };

        private static (EntityRepository World, NetworkEntityMap Map, Entity Man) Node(int primaryOwner, int localNode)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterComponent<UnderFire>();
            w.RegisterComponent<SimVelocity>();       // Sigma's inputs
            w.RegisterComponent<ShotOrdinal>();
            w.RegisterComponent<StanceIntent>();
            var map = new NetworkEntityMap();
            var man = w.CreateEntity();
            w.AddComponent(man, new NetworkIdentity { Value = 70L });
            w.AddComponent(man, new NetworkAuthority(primaryOwner, localNode));
            map.Register(70L, man);
            return (w, map, man);
        }

        private static void Apply(EntityRepository w, NetworkEntityMap map, in EntityUnderFire sample)
        {
            using var cmd = new EntityCommandBuffer();
            new EntityUnderFireIngressTranslator(participant: null, map).Apply(in sample, cmd, w);
            cmd.Playback(w);
        }

        /// <summary>The owner (the Muscle) publishes the stamp with its time when it changes, and not again while it is unchanged; a node that
        /// does not own the descriptor publishes nothing. ✅ Red-proof: the egress dropped from <c>SharedTranslatorPack</c>, or its
        /// <c>_last</c> compare removed ⇒ red.</summary>
        [Fact]
        public void CE1223_TheOwnersEgress_PublishesUnderFire_OnChange_AndNotWhenUnchanged()
        {
            var (w, _, man) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new CapturingWriter();
                var egress = new EntityUnderFireEgressTranslator(wire);
                egress.ScanAndPublish(w);
                Assert.Empty(wire.Written);                                  // no stamp yet

                HitModel.StampUnderFire(w, man, 12.5);
                egress.ScanAndPublish(w);
                var first = Assert.Single(wire.Written);
                Assert.Equal((70L, 12.5), (first.EntityId, first.LastTime));

                egress.ScanAndPublish(w);                                    // unchanged
                Assert.Single(wire.Written);

                HitModel.StampUnderFire(w, man, 14.0);                       // a fresh near miss
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Equal(14.0, wire.Written[1].LastTime);
                Assert.Equal(2, egress.SentSampleCount);
            }

            var (other, _, stamped) = Node(primaryOwner: 2, localNode: 1);   // this node does not own it
            using (other)
            {
                var wire = new CapturingWriter();
                HitModel.StampUnderFire(other, stamped, 3.0);
                new EntityUnderFireEgressTranslator(wire).ScanAndPublish(other);
                Assert.Empty(wire.Written);
            }
        }

        /// <summary>A non-owner's ingress writes the stamp onto the mapped entity — adding the component when it has none, refreshing it when it
        /// has; the recorded owner never takes its own sample back. ✅ Red-proof: the ingress dropping the time ⇒ red.</summary>
        [Fact]
        public void CE1223_ANonOwnersIngress_WritesUnderFireOntoTheMappedEntity()
        {
            var (w, map, man) = Node(primaryOwner: 2, localNode: 1);
            using (w)
            {
                Assert.False(w.HasComponent<UnderFire>(man));
                Apply(w, map, new EntityUnderFire { EntityId = 70L, LastTime = 12.5 });
                Assert.Equal(12.5, w.GetComponentRO<UnderFire>(man).LastTime);
                Apply(w, map, new EntityUnderFire { EntityId = 70L, LastTime = 20.0 });
                Assert.Equal(20.0, w.GetComponentRO<UnderFire>(man).LastTime);
                Apply(w, map, new EntityUnderFire { EntityId = 999L, LastTime = 1.0 });   // an unknown entity: dropped, nothing thrown
            }

            var (owner, ownerMap, ownMan) = Node(primaryOwner: 1, localNode: 1);
            using (owner)
            {
                Apply(owner, ownerMap, new EntityUnderFire { EntityId = 70L, LastTime = 12.5 });
                Assert.False(owner.HasComponent<UnderFire>(ownMan));
            }
        }

        /// <summary>⭐ The point of it: the receiving world's σ for a fresh <c>Now</c> inside <c>UnderFireSeconds</c> is twice the value without the
        /// stamp, and back to it once the window has passed. ✅ Red-proof: either translator not registered ⇒ the ring stays at half ⇒ red.</summary>
        [Fact]
        public void CE1223_AfterIngress_TheReceivingNodesSigma_IsDoubledWithinTheWindow()
        {
            var (w, map, man) = Node(primaryOwner: 2, localNode: 1);
            using (w)
            {
                float calm = HitModel.Sigma(w, man, Rifle, now: 12.0);
                Assert.True(calm > 0f);

                var sample = EntityUnderFireEgressTranslator.ToSample(70L, new UnderFire { LastTime = 10.0 });
                Apply(w, map, in sample);

                Assert.Equal(calm * HitModel.UnderFireFactor, HitModel.Sigma(w, man, Rifle, now: 12.0), 6);                      // 2 s after
                Assert.Equal(calm, HitModel.Sigma(w, man, Rifle, now: 10.0 + HitModel.UnderFireSeconds + 1.0), 6);             // the window has passed
            }
        }

        /// <summary>Every node runs the shared translator pack, so the pair rides it exactly once (the stamp leaves the owner, arrives
        /// everywhere); the egress declares the component it publishes, which feeds the descriptor-ownership map. ✅ Red-proof: either
        /// registration removed from <c>SharedTranslatorPack</c> ⇒ red.</summary>
        [Fact]
        public void CE1223_TheSharedPack_CarriesTheUnderFirePair_OnEveryNode()
        {
            using var participant = new DdsParticipant(231u);
            var entityMap = new NetworkEntityMap();
            var pack = SharedTranslatorPack.Create(participant, entityMap, localNodeId: 1, new FdpEventBus(),
                new Fdp.Toolkit.Replication.Systems.GhostCreationSystem(entityMap), HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0)).ToList();
            var egress = Assert.Single(pack.OfType<EntityUnderFireEgressTranslator>());
            Assert.Single(pack.OfType<EntityUnderFireIngressTranslator>());
            Assert.Equal(new[] { GlobalComponentIds.UnderFire }, egress.TargetComponentIds);
            Assert.Equal((long)EDescriptorType.dtUnderFire, egress.DescriptorOrdinal);
        }
    }
}
