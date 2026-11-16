using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.Map.Common.Tests
{
    /// <summary>⭐ CE-8016 (agg-behaviors' F-9) — a stochastic unit's slots and holdings from the SAME composite the physical units are built from.</summary>
    [Collection("FdpWorkerPool")]   // ⚠ sets the process-wide KillerVictimTkb.PersonalSoldiers — serial with C8LadderStochasticTests
    public class StochasticUnitFillTests
    {
        private static TkbDatabase Db()
        {
            var db = new TkbDatabase();
            NedTkbCatalog.RegisterAll(db);
            return db;
        }

        [Fact]
        public void APlatoon_Flattens_ToThirtyMen_ThreeLeaders_EachSlotKeyedByItsLeaf_AndChildTypeInvertsIt()
        {
            var db = Db();
            int dropped = StochasticUnitFill.Build(db, (TkbTypeId)TkbEntityTypes.Unit_InfantryPlatoon, out var slots, out var holdings);
            Assert.Equal(0, dropped);
            Assert.Equal(30, slots.Count);
            Assert.Equal(0, holdings.Count);
            int leaders = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots.Slots[i];
                if (s.Role == SlotRole.Leader) leaders++;
                Assert.True(slots.IsUp(i));
                Assert.Equal(i + 1, s.CompositeSlot);   // every leaf is a man here, so leaf index = slot index
                var expected = i % 10 == 0 ? TkbEntityTypes.Infantry_Officer : TkbEntityTypes.Infantry_Rifleman;
                Assert.Equal((TkbTypeId)expected, StochasticUnitFill.ChildType(db, (TkbTypeId)TkbEntityTypes.Unit_InfantryPlatoon, s.CompositeSlot));
                Assert.Equal(UnitAmmunition.BasicLoad(db, (TkbTypeId)expected), s.Ammo);
            }
            Assert.Equal(3, leaders);
            Assert.True(slots.Slots[1].Ammo > 0, "a rifleman carries his basic load");
        }

        [Fact]
        public void ATankPlatoon_IsOneHoldingOfFour_TrackedVehicles_NoSlots()
        {
            var db = Db();
            StochasticUnitFill.Build(db, (TkbTypeId)TkbEntityTypes.Unit_TankPlatoon, out var slots, out var holdings);
            Assert.Equal(0, slots.Count);
            Assert.Equal(1, holdings.Count);
            var h = holdings.Items[0];
            Assert.Equal((TkbTypeId)TkbEntityTypes.Tank_M1Abrams, h.Type);
            Assert.Equal(4, h.Count);
            Assert.Equal(4, h.Operational);
            Assert.Equal(HoldingKind.Vehicle, h.Kind);
            Assert.Equal(HoldingMobility.Tracked, h.Mobility);
            Assert.Equal((uint)(4 * UnitAmmunition.BasicLoad(db, (TkbTypeId)TkbEntityTypes.Tank_M1Abrams)), h.Ammo);
        }

        [Fact]
        public void ACompany_IsNinetyMen_AndItsEchelonIsCompany()
        {
            var db = Db();
            StochasticUnitFill.Build(db, (TkbTypeId)TkbEntityTypes.Unit_InfantryCompany, out var slots, out _);
            Assert.Equal(90, slots.Count);
            Assert.Equal(2, StochasticUnitFill.EchelonOf("Company"));
            Assert.Equal(1, StochasticUnitFill.EchelonOf("Platoon"));
            Assert.Equal(0, StochasticUnitFill.EchelonOf("Squad"));
        }

        [Fact]
        public void TheSystem_FillsAnAuthoredStochasticUnit_Once_SetsItsEchelon_FeedsThePersonalWeapons_AndLeavesAPhysicalUnitAlone()
        {
            var before = KillerVictimTkb.PersonalSoldiers;
            KillerVictimTkb.PersonalSoldiers = null;
            try
            {
                using var w = new EntityRepository();
                w.RegisterComponent<AggregateUnit>(); w.RegisterComponent<UnitSlotTable>(); w.RegisterComponent<UnitHoldings>(); w.RegisterComponent<TkbIdentity>();
                w.SetSingletonManaged<ITkbDatabase>(Db());
                var stoch = w.CreateEntity();
                w.AddComponent(stoch, new AggregateUnit { Resolution = AggregateResolution.Stochastic });
                w.AddComponent(stoch, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Unit_InfantryPlatoon });
                var phys = w.CreateEntity();
                w.AddComponent(phys, new AggregateUnit { Resolution = AggregateResolution.Physical });
                w.AddComponent(phys, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Unit_InfantryPlatoon });

                var sys = new StochasticUnitFillSystem();
                sys.Execute(w, 0.1f);

                Assert.Equal(30, w.GetComponentRO<UnitSlotTable>(stoch).Count);
                Assert.Equal(1, w.GetComponentRO<AggregateUnit>(stoch).Echelon);
                Assert.False(w.HasComponent<UnitSlotTable>(phys));
                Assert.NotNull(KillerVictimTkb.PersonalSoldiers);
                Assert.Equal((TkbTypeId)TkbEntityTypes.Infantry_Rifleman, KillerVictimTkb.PersonalSoldiers![SlotRole.Rifleman]);

                // once: a hit slot stays hit
                w.GetComponentRW<UnitSlotTable>(stoch).Slots[0].Health = (Half)0f;
                sys.Execute(w, 0.1f);
                Assert.Equal(0f, (float)w.GetComponentRO<UnitSlotTable>(stoch).Slots[0].Health);
            }
            finally { KillerVictimTkb.PersonalSoldiers = before; }
        }
    }
}
