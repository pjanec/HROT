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

        /// <summary>
        /// ⭐⭐ <c>CE-2151</c> B-b (R-2009 B4) — a slot IS a seat on the wire too: when the passenger in seat 0 leaves, the one in seat 1 stays
        /// in seat 1 on every node (the sample carries 0 for the hole and Count = the occupied seats).
        /// ✅ Red-proof: the old packed ingress (append) ⇒ B lands in seat 0 ⇒ red.
        /// </summary>
        [Fact]
        public void CE2151_ASeatLeftEmpty_StaysAHoleOnEveryNode()
        {
            using var owner = World();
            var ownerMap = new NetworkEntityMap();
            var apc = Networked(owner, ownerMap, ApcId, vehicle: true);
            var a = Networked(owner, ownerMap, AId);
            var b = Networked(owner, ownerMap, BId);
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(owner, a, apc));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(owner, b, apc));
            Assert.True(Embarkation.Disembark(owner, a, apc));

            var sample = EntityPassengersEgressTranslator.ToSample(owner, ApcId, owner.GetComponentRO<PassengerBuffer>(apc));
            Assert.Equal((byte)1, sample.Count);
            Assert.Equal(0L, sample.Passenger0);
            Assert.Equal(BId, sample.Passenger1);

            using var other = World();
            var otherMap = new NetworkEntityMap();
            var otherApc = Networked(other, otherMap, ApcId, vehicle: true);
            var otherB = Networked(other, otherMap, BId);
            var ingress = new EntityPassengersIngressTranslator(participant: null, otherMap);
            using var cmd = new EntityCommandBuffer();
            ingress.Receive(in sample);
            ingress.Flush(cmd, other);
            cmd.Playback(other);
            var list = other.GetComponentRO<PassengerBuffer>(otherApc);
            Assert.Equal(1, list.Count);
            Assert.True(list.Passengers[0].IsNull);
            Assert.Equal(otherB, list.Passengers[1]);
        }

        /// <summary>⭐ <c>CE-2152</c> C-a (R-2011 C5) — the CARRY MODE travels with the seat: a list with a dragged man in seat 1 arrives as a
        /// dragged man in seat 1, so every node derives the same tag (and the same offset and speed). ✅ Red-proof: ingress dropping
        /// <c>Modes</c> ⇒ seated on the replica ⇒ red.</summary>
        [Fact]
        public void CE2152_TheCarryMode_TravelsWithTheSeat()
        {
            using var owner = World();
            var ownerMap = new NetworkEntityMap();
            var apc = Networked(owner, ownerMap, ApcId, vehicle: true);
            var b = Networked(owner, ownerMap, BId);
            ref var src = ref owner.GetComponentRW<PassengerBuffer>(apc);
            src.Passengers[1] = b;
            src.SetMode(1, CarryMode.Dragged);
            src.Count = 1;

            var sample = EntityPassengersEgressTranslator.ToSample(owner, ApcId, owner.GetComponentRO<PassengerBuffer>(apc));

            using var other = World();
            var otherMap = new NetworkEntityMap();
            var otherApc = Networked(other, otherMap, ApcId, vehicle: true);
            Networked(other, otherMap, BId);
            var ingress = new EntityPassengersIngressTranslator(participant: null, otherMap);
            using var cmd = new EntityCommandBuffer();
            ingress.Receive(in sample);
            ingress.Flush(cmd, other);
            cmd.Playback(other);
            Assert.Equal(CarryMode.Dragged, other.GetComponentRO<PassengerBuffer>(otherApc).ModeOf(1));
            Assert.Equal(CarryMode.Seated, other.GetComponentRO<PassengerBuffer>(otherApc).ModeOf(0));
        }

        /// <summary>⭐ Team carry T-a (R-2017 T1; R-169 — <c>EntityPassengers.Assistant</c> APPENDED) — a stretcher's second man travels with
        /// the list, and the sample is held until he is known on the other node. ✅ Red-proof: ToSample without Assistant ⇒ 0 ⇒ red.</summary>
        [Fact]
        public void TA_TheSecondMan_TravelsWithTheList_HeldUntilHeIsKnown()
        {
            const long CarrierId = 910L, CasualtyId = 911L, HelperId = 912L;
            using var owner = World();
            var ownerMap = new NetworkEntityMap();
            var carrier = Networked(owner, ownerMap, CarrierId, vehicle: true);
            var casualty = Networked(owner, ownerMap, CasualtyId);
            var helper = Networked(owner, ownerMap, HelperId);
            ref var src = ref owner.GetComponentRW<PassengerBuffer>(carrier);
            src.Passengers[0] = casualty;
            src.SetMode(0, CarryMode.Stretcher);
            src.Count = 1;
            src.Assistant = helper;

            var sample = EntityPassengersEgressTranslator.ToSample(owner, CarrierId, owner.GetComponentRO<PassengerBuffer>(carrier));
            Assert.Equal(HelperId, sample.Assistant);

            using var other = World();
            var otherMap = new NetworkEntityMap();
            var otherCarrier = Networked(other, otherMap, CarrierId, vehicle: true);
            Networked(other, otherMap, CasualtyId);
            var ingress = new EntityPassengersIngressTranslator(participant: null, otherMap);
            using (var cmd = new EntityCommandBuffer())
            {
                ingress.Receive(in sample);
                ingress.Flush(cmd, other);
                cmd.Playback(other);
            }
            Assert.Equal(1, ingress.PendingCount);   // the second man is not known here yet: held
            var otherHelper = Networked(other, otherMap, HelperId);
            using (var cmd = new EntityCommandBuffer())
            {
                ingress.Flush(cmd, other);
                cmd.Playback(other);
            }
            Assert.Equal(0, ingress.PendingCount);
            Assert.Equal(otherHelper, other.GetComponentRO<PassengerBuffer>(otherCarrier).Assistant);
            Assert.Equal(CarryMode.Stretcher, other.GetComponentRO<PassengerBuffer>(otherCarrier).ModeOf(0));
        }

        /// <summary>⭐ Air Ops slice 6a F4 (R-169 — <c>EntityPassengers.Passenger8…11</c>, <c>ModesHigh</c> APPENDED) — the list is 12 wide:
        /// seat 11 and its carry mode cross the wire, and seats 0–7 still ride the original fields. ✅ Red-proof: ToSample without the
        /// appended seats ⇒ seat 11 empty on the other node ⇒ red.</summary>
        [Fact]
        public void F4_TheTwelfthSeat_AndItsMode_CrossTheWire()
        {
            const long HeliId = 920L, FirstId = 921L, LastId = 922L;
            using var owner = World();
            var ownerMap = new NetworkEntityMap();
            var heli = Networked(owner, ownerMap, HeliId, vehicle: true);
            var first = Networked(owner, ownerMap, FirstId);
            var last = Networked(owner, ownerMap, LastId);
            ref var src = ref owner.GetComponentRW<PassengerBuffer>(heli);
            src.Passengers[0] = first;
            src.Passengers[PassengerBuffer.Capacity - 1] = last;
            src.SetMode(PassengerBuffer.Capacity - 1, CarryMode.Dragged);
            src.Count = 2;

            var sample = EntityPassengersEgressTranslator.ToSample(owner, HeliId, owner.GetComponentRO<PassengerBuffer>(heli));
            Assert.Equal((FirstId, LastId), (sample.Passenger0, sample.Passenger11));

            using var other = World();
            var otherMap = new NetworkEntityMap();
            var otherHeli = Networked(other, otherMap, HeliId, vehicle: true);
            var otherFirst = Networked(other, otherMap, FirstId);
            var otherLast = Networked(other, otherMap, LastId);
            var ingress = new EntityPassengersIngressTranslator(participant: null, otherMap);
            using (var cmd = new EntityCommandBuffer())
            {
                ingress.Receive(in sample);
                ingress.Flush(cmd, other);
                cmd.Playback(other);
            }
            var got = other.GetComponentRO<PassengerBuffer>(otherHeli);
            Assert.Equal((otherFirst, otherLast, 2), (got.Passengers[0], got.Passengers[PassengerBuffer.Capacity - 1], got.Count));
            Assert.Equal(CarryMode.Dragged, got.ModeOf(PassengerBuffer.Capacity - 1));
            Assert.Equal(CarryMode.Seated, got.ModeOf(0));
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
