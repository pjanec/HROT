using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ Airborne insertion B-1 (docs/designs/airborne/DESIGN_Airborne_Insertion.md D-1) — a body's <see cref="Descent"/> crosses the wire
    /// from its position's owner: the other node holds the same descent, its anchor resolved to THAT node's carrier, so the man is a
    /// Parachute / Rope body there too (<see cref="Locomotions.Of"/>); the "over" sample (Kind 0) takes it away again.
    /// </summary>
    public sealed class DescentReplicationTests
    {
        private static (EntityRepository World, Entity Man, Entity Heli, NetworkEntityMap Map) Node(long manId, long heliId)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<Descent>();
            w.RegisterComponent<Locomotion>();
            var man = w.CreateEntity();
            w.AddComponent(man, new NetworkIdentity { Value = manId });
            w.AddComponent(man, new Locomotion { Class = LocomotionClass.Pedestrian });
            var heli = w.CreateEntity();
            w.AddComponent(heli, new NetworkIdentity { Value = heliId });
            var map = new NetworkEntityMap();
            map.Register(manId, man);
            map.Register(heliId, heli);
            return (w, man, heli, map);
        }

        [Fact]
        public void B1_ADescent_ReachesTheOtherNode_WithItsAnchorResolved_AndItsEndRemovesIt()
        {
            var (owner, ownerMan, ownerHeli, _) = Node(51L, 52L);
            var (replica, man, heli, map) = Node(51L, 52L);
            using (owner)
            using (replica)
            {
                var d = new Descent
                {
                    Kind = DescentKind.FreeFall, Phase = DescentPhase.Canopy, Canopy = CanopyKind.RamAir, AnchorIndex = 1,
                    PhaseSince = 12.5, Anchor = ownerHeli, Target = new Vector3(400, -250, 3),
                };
                var sample = EntityDescentEgressTranslator.ToSample(51L, d, owner);
                Assert.Equal(52L, sample.AnchorNetworkId);

                var ingress = new EntityDescentIngressTranslator(participant: null, map);
                using (var cmd = new EntityCommandBuffer())
                {
                    ingress.Apply(in sample, cmd, replica);
                    cmd.Playback(replica);
                }
                var got = replica.GetComponentRO<Descent>(man);
                Assert.Equal((d.Kind, d.Phase, d.Canopy, d.AnchorIndex, d.PhaseSince, d.Target), (got.Kind, got.Phase, got.Canopy, got.AnchorIndex, got.PhaseSince, got.Target));
                Assert.Equal(heli, got.Anchor);                                                // THIS node's carrier
                Assert.Equal(LocomotionClass.Parachute, Locomotions.Of(replica, man));

                using (var cmd = new EntityCommandBuffer())
                {
                    ingress.Apply(new Hrot.NED.Descriptors.EntityDescent { EntityId = 51L }, cmd, replica);   // the descent is over
                    cmd.Playback(replica);
                }
                Assert.False(replica.HasComponent<Descent>(man));
                Assert.Equal(LocomotionClass.Pedestrian, Locomotions.Of(replica, man));
            }
        }

        /// <summary>
        /// ⭐ <c>CE-2210</c> — a man CREATED coming down (the ejected pilot) is published while he is still CONSTRUCTING: the creator owns his
        /// descent only then (it yields its Muscle group before he turns Active, Ownership Groups D-7), so an Active-only scan sent nothing,
        /// ever, and the node taking his position got a Pedestrian. 🔴 Red-proof: without <c>WithLifecycle(All)</c> nothing is sent.
        /// </summary>
        [Fact]
        public void CE2210_AManCreatedComingDown_IsPublishedWhileConstructing()
        {
            var (owner, man, _, _) = Node(61L, 62L);
            using (owner)
            {
                owner.SetLifecycleState(man, EntityLifecycle.Constructing);
                owner.AddComponent(man, new Descent { Kind = DescentKind.Ejection, Phase = DescentPhase.Exit, Canopy = CanopyKind.Round });
                var egress = new EntityDescentEgressTranslator(participant: null);
                egress.ScanAndPublish(owner);
                Assert.Equal(1, egress.SentSampleCount);
            }
        }
    }
}
