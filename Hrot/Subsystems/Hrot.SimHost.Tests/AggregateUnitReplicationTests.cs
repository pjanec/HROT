using System;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-8001</c> (CE-4616 ST-0 ③, RV-10; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md) — a stochastic unit's
    /// state, slot table and holdings reach every other node whole: the egress wire form applied by the ingress on a second world gives back
    /// the same component. 🔴 Red-proofs: the slot egress dropping <c>Stress</c> ⇒ the stress assert fails; the holdings ingress losing the
    /// type ⇒ the type assert fails.
    /// </summary>
    public sealed class AggregateUnitReplicationTests
    {
        private static (EntityRepository World, Entity Unit, NetworkEntityMap Map) Node(int netId)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<AggregateUnit>(); w.RegisterComponent<UnitSlotTable>(); w.RegisterComponent<UnitHoldings>();
            var unit = w.CreateEntity();
            w.AddComponent(unit, new NetworkIdentity { Value = netId });
            var map = new NetworkEntityMap();
            map.Register(netId, unit);
            return (w, unit, map);
        }

        [Fact]
        public void CE8001_TheUnitsState_ReachesTheOtherNode()
        {
            var sent = new AggregateUnit
            {
                Resolution = AggregateResolution.Stochastic, Echelon = 1, Formation = UnitFormation.Column, Posture = UnitPosture.Mounted,
                Activity = UnitActivity.Moving | UnitActivity.Firing, Prepared = PreparedLevel.Hasty, Splitting = SplitStage.Planning, Pin = ResolutionPin.PinKeep,
                Move = UnitMoveState.OnRoute, Width = 40f, Depth = 350f, Heading = 270f, Destination = new WorldPoint(100f, -50f, 3f),
                PathDistanceM = 120.5f, SpeedMps = 8f, SuppressedShare = 0.4f, RouteRequestedAt = (SimInstant)3.0, LastFiredAt = (SimInstant)4.0,
                LastContactAt = (SimInstant)5.0, PreparedSince = (SimInstant)6.0, SplittingSince = (SimInstant)7.0,
            };
            var sample = EntityAggregateUnitEgressTranslator.ToSample(51, sent);
            var (node, unit, map) = Node(51);
            using (node)
            {
                using var cmd = new EntityCommandBuffer();
                new EntityAggregateUnitIngressTranslator(participant: null, map).Apply(in sample, cmd, node);
                cmd.Playback(node);
                Assert.Equal(sent, node.GetComponentRO<AggregateUnit>(unit));   // ⭐ every field but the node-local ring and step stamp (both default here)
            }
        }

        [Fact]
        public void CE8001_TheSlotTable_AndTheHoldings_ReachTheOtherNode()
        {
            var slots = new UnitSlotTable { Count = 3 };
            slots.Slots[0] = new UnitSlot { Role = SlotRole.Leader, Health = (Half)25f, Stress = (Half)0.1f, Ammo = 210, CompositeSlot = 0 };
            slots.Slots[1] = new UnitSlot { Role = SlotRole.MachineGunner, Health = (Half)12.5f, Stress = (Half)0.55f, Ammo = 640, CompositeSlot = 4 };
            slots.Slots[2] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)0f, Stress = (Half)1f, Ammo = 30, CompositeSlot = 5 };
            var holdings = new UnitHoldings { Count = 2 };
            holdings.Items[0] = new UnitHolding { Type = new TkbTypeId(212), Count = 4, Operational = 3, Ammo = 600, Kind = HoldingKind.Vehicle, Mobility = HoldingMobility.Wheeled, Immobile = 1 };
            holdings.Items[1] = new UnitHolding { Type = new TkbTypeId(5101), Count = 1, Operational = 1, Kind = HoldingKind.Sensor, Sensor = HoldingSensor.Thermal };

            var s1 = EntityUnitSlotTableEgressTranslator.ToSample(52, slots);
            var s2 = EntityUnitHoldingsEgressTranslator.ToSample(52, holdings);
            Assert.Equal(3, s1.Slots.Count);                       // only the slots in use cross
            var (node, unit, map) = Node(52);
            using (node)
            {
                using var cmd = new EntityCommandBuffer();
                new EntityUnitSlotTableIngressTranslator(participant: null, map).Apply(in s1, cmd, node);
                new EntityUnitHoldingsIngressTranslator(participant: null, map).Apply(in s2, cmd, node);
                cmd.Playback(node);
                var t = node.GetComponentRO<UnitSlotTable>(unit);
                Assert.True(Fdp.Network.Cyclone.Translators.SampleEquality.Same(slots, t));     // byte for byte
                Assert.Equal(0.55f, (float)t.Slots[1].Stress, 3);
                Assert.Equal(2, t.Present());
                var h = node.GetComponentRO<UnitHoldings>(unit);
                Assert.True(Fdp.Network.Cyclone.Translators.SampleEquality.Same(holdings, h));
                Assert.Equal(new TkbTypeId(212), h.Items[0].Type);
            }
        }

        [Fact]
        public void CE8001_AnUnknownUnit_IsDropped()
        {
            var (node, _, map) = Node(53);
            using (node)
            {
                var ingress = new EntityUnitSlotTableIngressTranslator(participant: null, map);
                using var cmd = new EntityCommandBuffer();
                ingress.Apply(EntityUnitSlotTableEgressTranslator.ToSample(999, new UnitSlotTable()), cmd, node);
                Assert.Equal(1, ingress.DroppedSampleCount);
            }
        }
    }
}
