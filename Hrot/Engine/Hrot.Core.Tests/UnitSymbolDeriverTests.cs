using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using GizmoMap.Contracts.Symbology;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Tkb;
using Hrot.Map.Common;
using Fdp.Interfaces;
using Xunit;

namespace Hrot.Core.Tests
{
    /// <summary>
    /// ⭐ CE-8609 (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §6 S-1…S-3, mockup ⑧) — a unit's NATO symbol DERIVED from what it is made of: the real
    /// catalog's tank platoon is ARMOUR (tracked), its squad / platoon / company INFANTRY at their echelons; the mockup's four examples come out as drawn
    /// (mechanised + AT, motorised, armour, infantry + M); an authored role wins; a body that is not a unit has none; the second call is a cached lookup, 0 B.
    /// 🔴 Red-proof (compiles): the carrier rule dropped ⇒ the IFV platoon stays plain infantry ⇒ red.
    /// </summary>
    public class UnitSymbolDeriverTests
    {
        private const long Truck = 90_001, MechPlt = 90_010, MotPlt = 90_011, TankCoy = 90_012, EnemyCoy = 90_013, ReconMan = 90_020, ReconSquad = 90_021;

        private static TkbDatabase Db()
        {
            var db = new TkbDatabase();
            NedTkbCatalog.RegisterAll(db);
            var truck = new TkbTemplate("Test truck", Truck) { DisType = new DISEntityType { Kind = 1, Domain = 1, Category = 7 } };
            truck.AddDescriptor(new CarrierDefDto { Seats = new List<SeatDto> { new(), new(), new(), new(), new(), new(), new(), new(), new(), new() } });
            db.Register(truck);
            Unit(db, MechPlt, "Platoon", (TkbEntityTypes.Infantry_Rifleman, 30), (TkbEntityTypes.IFV_Bradley, 4), (TkbEntityTypes.WeaponSystem_TowTripod, 2));
            Unit(db, MotPlt, "Platoon", (TkbEntityTypes.Infantry_Rifleman, 28), (Truck, 3));
            Unit(db, TankCoy, "Company", (TkbEntityTypes.Tank_M1Abrams, 14), (TkbEntityTypes.IFV_Bradley, 1));
            Unit(db, EnemyCoy, "Company", (TkbEntityTypes.Infantry_Rifleman, 90), (TkbEntityTypes.WeaponSystem_Mortar81, 6));
            var scout = new TkbTemplate("Test scout", ReconMan) { DisType = new DISEntityType { Kind = 3, Domain = 1, Category = 1 } };
            scout.AddDescriptor(new MapVisualDef { SymbolRole = SymbolRole.Reconnaissance });
            db.Register(scout);
            Unit(db, ReconSquad, "Squad", (ReconMan, 6));
            return db;
        }

        private static void Unit(TkbDatabase db, long id, string echelon, params (long Type, int Count)[] slots)
        {
            var t = new TkbTemplate("Test unit " + id, id);
            var b = new TkbCompositionDefBuilder { Echelon = echelon, AutoCreateChildren = false };
            foreach (var (type, count) in slots) b.Subordinates.Add(new TkbChildSlot { TkbType = (TkbTypeId)type, Count = count });
            t.AddDescriptor(b.Build());
            db.Register(t);
        }

        private static SymbolCode Of(TkbDatabase db, long type) { Assert.True(UnitSymbolDeriver.TryOfType(db, type, out var s), $"type {type} has a symbol"); return s; }

        [Fact]
        public void CE8609_TheCatalogsUnits_AreDerivedFromTheirContent()
        {
            var db = Db();
            Assert.Equal(new SymbolCode(SymbolAffiliation.Unknown, SymbolRole.Armour, SymbolEchelon.Platoon, SymbolMobility.Tracked), Of(db, TkbEntityTypes.Unit_TankPlatoon));
            Assert.Equal(new SymbolCode(SymbolAffiliation.Unknown, SymbolRole.Infantry, SymbolEchelon.Squad), Of(db, TkbEntityTypes.Unit_InfantrySquad));
            Assert.Equal(new SymbolCode(SymbolAffiliation.Unknown, SymbolRole.Infantry, SymbolEchelon.Platoon), Of(db, TkbEntityTypes.Unit_InfantryPlatoon));
            Assert.Equal(new SymbolCode(SymbolAffiliation.Unknown, SymbolRole.Infantry, SymbolEchelon.Company), Of(db, TkbEntityTypes.Unit_InfantryCompany));
            Assert.False(UnitSymbolDeriver.TryOfType(db, TkbEntityTypes.Infantry_Rifleman, out _));   // a man is not a unit (S-4)
        }

        [Fact]
        public void CE8609_TheMockupsFourExamples_ComeOutAsDrawn_AndAnAuthoredRoleWins()
        {
            var db = Db();
            Assert.Equal(new SymbolCode(SymbolAffiliation.Unknown, SymbolRole.Mechanized, SymbolEchelon.Platoon, SymbolMobility.Tracked, SymbolRole.AntiArmour), Of(db, MechPlt));
            Assert.Equal(new SymbolCode(SymbolAffiliation.Unknown, SymbolRole.Motorized, SymbolEchelon.Platoon, SymbolMobility.Wheeled), Of(db, MotPlt));
            Assert.Equal(new SymbolCode(SymbolAffiliation.Unknown, SymbolRole.Armour, SymbolEchelon.Company, SymbolMobility.Tracked), Of(db, TankCoy));
            Assert.Equal(new SymbolCode(SymbolAffiliation.Unknown, SymbolRole.Infantry, SymbolEchelon.Company, SymbolMobility.NoMobility, SymbolRole.Mortar), Of(db, EnemyCoy));
            Assert.Equal(SymbolRole.Reconnaissance, Of(db, ReconSquad).Role);
            Assert.Equal(SymbolAffiliation.Hostile, UnitSymbolDeriver.WithSide(Of(db, EnemyCoy), ForceId.Hostile).Affiliation);
        }

        [Fact]
        public void CE8609_ASecondLookup_IsCached_AndAllocatesNothing()
        {
            var db = Db();
            UnitSymbolDeriver.TryOfType(db, TkbEntityTypes.Unit_InfantryCompany, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) UnitSymbolDeriver.TryOfType(db, TkbEntityTypes.Unit_InfantryCompany, out _);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
