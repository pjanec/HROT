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
        public void TheSystem_FillsAnAuthoredStochasticUnit_Once_SetsItsEchelon_GivesItAContactPool_FeedsThePersonalWeapons_AndLeavesAPhysicalUnitAlone()
        {
            var before = KillerVictimTkb.PersonalSoldiers;
            KillerVictimTkb.PersonalSoldiers = null;
            try
            {
                using var w = new EntityRepository();
                w.RegisterComponent<AggregateUnit>(); w.RegisterComponent<UnitSlotTable>(); w.RegisterComponent<UnitHoldings>(); w.RegisterComponent<TkbIdentity>();
                w.RegisterComponent<Fdp.Toolkit.Squad.SquadCognitiveState>();
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
                // ⭐ CE-8021 (F-11) — its own contact pool, so its sensing reports somewhere; a physical unit is left alone
                Assert.True(w.HasComponent<Fdp.Toolkit.Squad.SquadCognitiveState>(stoch));
                Assert.False(w.HasComponent<Fdp.Toolkit.Squad.SquadCognitiveState>(phys));
                Assert.NotNull(KillerVictimTkb.PersonalSoldiers);
                Assert.Equal((TkbTypeId)TkbEntityTypes.Infantry_Rifleman, KillerVictimTkb.PersonalSoldiers![SlotRole.Rifleman]);

                // once: a hit slot stays hit
                w.GetComponentRW<UnitSlotTable>(stoch).Slots[0].Health = (Half)0f;
                sys.Execute(w, 0.1f);
                Assert.Equal(0f, (float)w.GetComponentRO<UnitSlotTable>(stoch).Slots[0].Health);
            }
            finally { KillerVictimTkb.PersonalSoldiers = before; }
        }

        /// <summary>⭐⭐ CE-8307 (R-8382 / R-8385; DESIGN_Split_One_Level.md) — the split's DIRECT children: a platoon (305) gives its 3 SQUADS, each a
        /// unit (302, echelon 0) over leaves 1–10 / 11–20 / 21–30 (the ranges tile the flattened leaves exactly); a squad (302) gives its 10 men,
        /// key == leaf (so a squad's split keys its men as before); a company (306) gives 3 platoons of 30. The share of a platoon's table for squad 2
        /// is its slots 11–20 renumbered 1–10 — the squad's OWN leaves. ✅ Red-proofs: <c>leaf += leaves</c> dropped ⇒ every squad starts at 1 ⇒ red ·
        /// <c>SplitsIntoUnits</c> true for a squad ⇒ red.</summary>
        [Fact]
        public void CE8307_ThePlatoonsComposition_IsItsThreeSquads_OverTheirLeafRanges_ASquadsIsItsMen()
        {
            var db = Db();
            var platoon = StochasticUnitFill.Composition(db, (TkbTypeId)TkbEntityTypes.Unit_InfantryPlatoon);
            Assert.Equal(3, platoon.Count);
            for (int s = 0; s < 3; s++)
            {
                Assert.Equal(s + 1, platoon[s].Key);
                Assert.Equal((TkbTypeId)TkbEntityTypes.Unit_InfantrySquad, platoon[s].Type);
                Assert.True(platoon[s].IsUnit);
                Assert.Equal(0, platoon[s].Echelon);
                Assert.Equal(1 + 10 * s, platoon[s].FirstSlot);
                Assert.Equal(10, platoon[s].Slots);
            }
            Assert.True(StochasticUnitFill.SplitsIntoUnits(db, (TkbTypeId)TkbEntityTypes.Unit_InfantryPlatoon));
            Assert.Equal(3, StochasticUnitFill.DirectChildren(db, (TkbTypeId)TkbEntityTypes.Unit_InfantryPlatoon));

            var squad = StochasticUnitFill.Composition(db, (TkbTypeId)TkbEntityTypes.Unit_InfantrySquad);
            Assert.Equal(10, squad.Count);
            var leaves = StochasticUnitFill.Leaves(db, (TkbTypeId)TkbEntityTypes.Unit_InfantrySquad);
            for (int k = 0; k < 10; k++)
            {
                Assert.False(squad[k].IsUnit);
                Assert.Equal(k + 1, squad[k].Key);
                Assert.Equal(k + 1, squad[k].FirstSlot);   // key == leaf: a squad's men keep their names and keys
                Assert.Equal(leaves[k].Type, squad[k].Type);
                Assert.Equal(leaves[k].Designation, squad[k].Designation);
            }
            Assert.False(StochasticUnitFill.SplitsIntoUnits(db, (TkbTypeId)TkbEntityTypes.Unit_InfantrySquad));

            var company = StochasticUnitFill.Composition(db, (TkbTypeId)TkbEntityTypes.Unit_InfantryCompany);
            Assert.Equal(3, company.Count);
            Assert.Equal(61, company[2].FirstSlot);
            Assert.Equal(30, company[2].Slots);
            Assert.Equal(1, company[2].Echelon);

            StochasticUnitFill.Build(db, (TkbTypeId)TkbEntityTypes.Unit_InfantryPlatoon, out var slots, out _);
            var share = BusResolutionSwitch.ShareOf(in slots, platoon[1]);
            Assert.Equal(10, share.Count);
            for (int j = 0; j < 10; j++)
            {
                Assert.Equal(j + 1, share.Slots[j].CompositeSlot);
                Assert.Equal(slots.Slots[10 + j].Role, share.Slots[j].Role);
            }
        }
    }
}
