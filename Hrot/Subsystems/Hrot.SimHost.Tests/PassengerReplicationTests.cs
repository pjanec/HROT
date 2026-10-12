using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3110</c> I-2b (R-224) — a vehicle's passenger list crosses the wire: the owner of <c>dtPassengerBuffer</c> publishes it on
    /// change (never an untouched empty list), every other node applies it once every passenger is known there (holding the sample until
    /// then), and the recorded owner never takes its own sample back. 📄 docs/DESIGN_Entity_Interactions.md §7.1c.
    /// </summary>
    public sealed class PassengerReplicationTests
    {
        private const long ApcId = 900L, AId = 901L, BId = 902L;

        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterComponent<PassengerBuffer>();
            w.RegisterComponent<IsEmbarkedTag>();
            return w;
        }

        private static Entity Networked(EntityRepository w, NetworkEntityMap map, long netId, bool vehicle = false)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new NetworkIdentity { Value = netId });
            if (vehicle) w.AddComponent(e, new PassengerBuffer());
            map.Register(netId, e);
            return e;
        }

        [Fact]
        public void CE3110_I2b_TheOwnersList_ReachesTheOtherNode_OnceEveryPassengerIsKnown()
        {
            using var owner = World();
            var ownerMap = new NetworkEntityMap();
            var apc = Networked(owner, ownerMap, ApcId, vehicle: true);
            var a = Networked(owner, ownerMap, AId);
            var b = Networked(owner, ownerMap, BId);
            var egress = new EntityPassengersEgressTranslator(participant: null);

            egress.ScanAndPublish(owner);
            Assert.Equal(0, egress.SentSampleCount);   // an empty list nobody filled is never sent

            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(owner, a, apc));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(owner, b, apc));
            egress.ScanAndPublish(owner);
            Assert.Equal(1, egress.SentSampleCount);
            egress.ScanAndPublish(owner);
            Assert.Equal(1, egress.SentSampleCount);   // unchanged ⇒ said once

            var sample = EntityPassengersEgressTranslator.ToSample(owner, ApcId, owner.GetComponentRO<PassengerBuffer>(apc));
            Assert.Equal((byte)2, sample.Count);
            Assert.Equal(AId, sample.Passenger0);
            Assert.Equal(BId, sample.Passenger1);

            using var other = World();
            var otherMap = new NetworkEntityMap();
            var otherApc = Networked(other, otherMap, ApcId, vehicle: true);
            var otherA = Networked(other, otherMap, AId);
            var ingress = new EntityPassengersIngressTranslator(participant: null, otherMap);
            using var cmd = new EntityCommandBuffer();

            ingress.Receive(in sample);
            ingress.Flush(cmd, other);
            cmd.Playback(other);
            Assert.Equal(0, other.GetComponentRO<PassengerBuffer>(otherApc).Count);   // B is not known here yet: held, not half-applied
            Assert.Equal(1, ingress.PendingCount);

            var otherB = Networked(other, otherMap, BId);
            ingress.Flush(cmd, other);
            cmd.Playback(other);
            var list = other.GetComponentRO<PassengerBuffer>(otherApc);
            Assert.Equal(2, list.Count);
            Assert.Equal(otherA, list.Passengers[0]);
            Assert.Equal(otherB, list.Passengers[1]);
            Assert.Equal(0, ingress.PendingCount);

            // everyone leaves: one sample with Count 0 empties the list there
            Embarkation.EjectAll(owner, apc);
            egress.ScanAndPublish(owner);
            Assert.Equal(2, egress.SentSampleCount);
            var empty = EntityPassengersEgressTranslator.ToSample(owner, ApcId, owner.GetComponentRO<PassengerBuffer>(apc));
            ingress.Receive(in empty);
            ingress.Flush(cmd, other);
            cmd.Playback(other);
            Assert.Equal(0, other.GetComponentRO<PassengerBuffer>(otherApc).Count);
        }

        [Fact]
        public void CE3110_I2b_OnlyTheDescriptorOwnerPublishes_AndTheOwnerNeverTakesItsOwnSampleBack()
        {
            using var w = World();
            w.RegisterManagedComponent<DescriptorOwnership>();
            var map = new NetworkEntityMap();
            var apc = Networked(w, map, ApcId, vehicle: true);
            var a = Networked(w, map, AId);
            w.AddComponent(apc, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            var ownership = new DescriptorOwnership();
            ownership.Map[Embarkation.PassengerBufferKey] = 2;   // the brain group is on node 2
            w.SetManagedComponent(apc, ownership);
            var b = new PassengerBuffer();
            b.Passengers[b.Count++] = a;
            w.SetComponent(apc, b);

            var egress = new EntityPassengersEgressTranslator(participant: null);
            egress.ScanAndPublish(w);
            Assert.Equal(0, egress.SentSampleCount);   // the primary owner is not the list's owner
            Assert.False(Embarkation.OwnsVehicle(w, apc));

            ownership.Map[Embarkation.PassengerBufferKey] = 1;   // now it is ours
            egress.ScanAndPublish(w);
            Assert.Equal(1, egress.SentSampleCount);

            // our own sample comes back off the wire with a stale list: it must not overwrite the live one
            var ingress = new EntityPassengersIngressTranslator(participant: null, map);
            using var cmd = new EntityCommandBuffer();
            ingress.Receive(new EntityPassengers { EntityId = ApcId, Count = 0 });
            ingress.Flush(cmd, w);
            cmd.Playback(w);
            Assert.Equal(1, w.GetComponentRO<PassengerBuffer>(apc).Count);
            Assert.Equal(0, ingress.PendingCount);
        }

        [Fact]
        public void CE3110_I2b_TheToolkitsOrdinals_AreNedsDescriptors()
        {
            Assert.Equal((long)EDescriptorType.dtPassengerBuffer, Embarkation.PassengerBufferDescriptorOrdinal);
            Assert.Equal((long)EDescriptorType.dtWorldPos, Embarkation.PositionDescriptorOrdinal);
        }
    }
}
