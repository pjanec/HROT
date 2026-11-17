using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8008</c> (C-1, RV-1 / RV-15) — the generator's TKB adapter: the rows come from the SAME TKB records the physical weapons fire
    /// with — the mount's dispersion and round, the platform's armour — and the effective rate from the mount's PACING, never RateOfFire.
    /// ✅ Red-proof: the rate taken as one aimed round per aim, ignoring the reload ⇒ the rate assert fails.
    /// </summary>
    [Collection("FdpWorkerPool")]
    public sealed class KillerVictimTkbRails
    {
        private const long Rifleman = 7301, Ifv = 7302, Radio = 7303;

        private static TkbDatabase Db()
        {
            var db = new TkbDatabase();
            var man = new TkbTemplate("Rifleman", Rifleman);
            man.AddDescriptor(new WeaponSuiteDto { Mounts = new() { new WeaponMountDto
                { DispersionMils = 2f, Penetration = 6f, DamagePerHit = 12.5f, Range = 500f, AimSeconds = 0.8f, MagazineSize = 30, ReloadSeconds = 3f } } });
            man.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 100f });
            db.Register(man);
            var ifv = new TkbTemplate("IFV", Ifv);
            ifv.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 800, ArmorFront = 30, ArmorSide = 15, ArmorRear = 10, PresentedAreaM2 = 12.6f });
            ifv.AddDescriptor(new WeaponSuiteDto { Mounts = new() { new WeaponMountDto
                { DispersionMils = 1f, Penetration = 40f, DamagePerHit = 60f, Range = 1500f, AimSeconds = 1.5f, MagazineSize = 6, ReloadSeconds = 4f } } });
            db.Register(ifv);
            db.Register(new TkbTemplate("Radio", Radio));
            return db;
        }

        [Fact]
        public void TheRows_AreTheTkbsNumbers_AndTheRateIsThePacing()
        {
            var skipped = new List<string>();
            var t = KillerVictimTkb.Build(Db(), new[] { new TkbTypeId(Rifleman), new TkbTypeId(Ifv), new TkbTypeId(Radio) },
                                          new[] { new TkbTypeId(Ifv), new TkbTypeId(Radio) },
                                          new Dictionary<SlotRole, TkbTypeId> { [SlotRole.Rifleman] = new(Rifleman) }, skipped);
            Assert.Equal(2, skipped.Count);                                                                 // the radio fires nothing and is no hull
            Assert.True(t.TryGet(new TkbTypeId(Rifleman), KillerVictimTable.Personnel, out var rifle));
            // ⭐ CE-8009 (RV-1) — a PERSONAL weapon fires at the MEASURED rate (the closed form, a magazine aimed then the reload, said
            //   30 / (30 × 0.8 + 3) = 1.11; the physical riflemen fire ~0.4); a HELD system keeps its derived pacing until it is measured
            Assert.Equal(KillerVictimTkb.MeasuredPersonalRoundsPerSecond, rifle.EffectiveRate, 4);
            Assert.Equal(HitModel.HitChance(0.002f, 0.5f * KillerVictimTable.BandMetres, 0.3f), rifle.PHitByBand[0], 5);
            Assert.True(t.TryGet(new TkbTypeId(Ifv), new TkbTypeId(Ifv), out var gunOnHull));
            Assert.True(gunOnHull.Suitability > 0.9f);                                                       // 40 mm through 10–30 mm
            Assert.Equal(6f / (6f * 1.5f + 4f), gunOnHull.EffectiveRate, 4);                               // held: a magazine aimed, then the reload
            Assert.True(t.TryGet(new TkbTypeId(Rifleman), new TkbTypeId(Ifv), out var rifleOnHull));
            Assert.Equal(0f, rifleOnHull.Suitability);
            Assert.Equal(new TkbTypeId(Rifleman), t.PersonalWeapon(SlotRole.Rifleman));
            // ⭐ CE-8019 — the damage on a man is in SLOT health: the body's (100 HP) hit, × 25 / 100 — ✅ red-proof: unscaled ⇒ 4× ⇒ red
            Assert.Equal(ArmorModel.HitDamage(6f, 12.5f, 0f) * KillerVictimGenerator.MeanZoneFactor * UnitSplits.SlotFullHealth / 100f, rifle.DamagePerHit, 4);
        }

        /// <summary>⭐ The fire phase builds the table ONCE from the world's TKB over the held types when none is in force (no host wiring needed
        /// for the held systems).</summary>
        [Fact]
        public void TheFirePhase_LoadsTheTableFromTheWorldsTkb()
        {
            var was = KillerVictimTable.Current;
            KillerVictimTable.Current = null;
            try
            {
                using var w = new EntityRepository();
                w.RegisterComponent<AggregateUnit>(); w.RegisterComponent<UnitHoldings>(); w.RegisterComponent<FireAllocation>();
                w.RegisterComponent<UnitDetections>(); w.RegisterComponent<UnitSlotTable>(); w.RegisterComponent<SimTransform>(); w.RegisterComponent<GlobalTime>();
                w.SetSingletonManaged<ITkbDatabase>(Db());
                var u = w.CreateEntity();
                var h = new UnitHoldings { Count = 1 };
                h.Items[0] = new UnitHolding { Type = new TkbTypeId(Ifv), Count = 2, Operational = 2, Ammo = 100, Kind = HoldingKind.Vehicle, Mobility = HoldingMobility.Tracked };
                w.AddComponent(u, h);
                w.AddComponent(u, new AggregateUnit { Resolution = AggregateResolution.Stochastic });
                w.AddComponent(u, new UnitSlotTable());
                w.AddComponent(u, new UnitDetections());
                w.AddComponent(u, new SimTransform());
                w.SetSingleton(new GlobalTime { TotalTime = 1 });
                new StochasticFirePhase().Execute(w, 1f);
                Assert.NotNull(KillerVictimTable.Current);
                Assert.True(KillerVictimTable.Current!.TryGet(new TkbTypeId(Ifv), new TkbTypeId(Ifv), out _));
            }
            finally { KillerVictimTable.Current = was; }
        }
    
        /// <summary>
        /// ⭐ CE-8009 — a man's row takes his BODY ARMOUR as the physical hit model does: his zones (<c>BodyZones</c> — the platform armour is the
        /// torso's), each through <c>ArmorModel.HitDamage</c>. A vest of 5 against a 5 mm round halves the torso's share; a man without armour
        /// keeps the unarmoured mean. 📐 Found by the C-8 ladder: the standard rifleman (armour 5) fell in ONE stochastic hit and needed ~2
        /// physical ones — first loss 2 s vs 37 s. ✅ Red-proof: the zones not passed ⇒ the armoured man takes the unarmoured damage ⇒ red.
        /// </summary>
        [Fact]
        public void CE8009_AMansRow_TakesHisBodyArmour_AsThePhysicalHitModelDoes()
        {
            KvEntry Row(byte category)
            {
                var db = new TkbDatabase();
                var man = new TkbTemplate("Man", Rifleman) { DisType = new DISEntityType { Kind = 3, Domain = 1, Category = category } };
                man.AddDescriptor(new WeaponSuiteDto { Mounts = new() { new WeaponMountDto
                    { DispersionMils = 2f, Penetration = 5f, DamagePerHit = 25f, Range = 300f, AimSeconds = 0.8f, MagazineSize = 30, ReloadSeconds = 3f } } });
                man.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 25f });
                db.Register(man);
                var t = KillerVictimTkb.Build(db, new[] { new TkbTypeId(Rifleman) }, Array.Empty<TkbTypeId>(),
                                              new Dictionary<SlotRole, TkbTypeId> { [SlotRole.Rifleman] = new(Rifleman) });
                Assert.True(t.TryGet(new TkbTypeId(Rifleman), KillerVictimTable.Personnel, out var e));
                return e;
            }
            var civilian = Row(0);   // the engine's "Person" body: no armour
            // ⭐ and the table carries the ZONES a hit draws from: shares sum to 1, share × factor sums to 1 (the mean), the head is 4× a bare torso
            var db2 = new TkbDatabase();
            var sold = new TkbTemplate("Man", Rifleman) { DisType = new DISEntityType { Kind = 3, Domain = 1, Category = 1 } };
            sold.AddDescriptor(new WeaponSuiteDto { Mounts = new() { new WeaponMountDto
                { DispersionMils = 2f, Penetration = 5f, DamagePerHit = 25f, Range = 300f, AimSeconds = 0.8f, MagazineSize = 30, ReloadSeconds = 3f } } });
            sold.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 25f });
            db2.Register(sold);
            var tz = KillerVictimTkb.Build(db2, new[] { new TkbTypeId(Rifleman) }, Array.Empty<TkbTypeId>(),
                                           new Dictionary<SlotRole, TkbTypeId> { [SlotRole.Rifleman] = new(Rifleman) });
            Assert.NotNull(tz.ManZoneShare); Assert.NotNull(tz.ManZoneFactor);
            float shares = 0f, mean = 0f;
            for (int i = 0; i < tz.ManZoneShare!.Length; i++) { shares += tz.ManZoneShare[i]; mean += tz.ManZoneShare[i] * tz.ManZoneFactor![i]; }
            Assert.Equal(1f, shares, 4); Assert.Equal(1f, mean, 4);
            var back = KillerVictimTable.FromJson(tz.ToJson());
            Assert.Equal(tz.ManZoneFactor, back.ManZoneFactor);
            var soldier = Row(1);    // the "Soldier" body (DIS 3.1.*.1): a vest, front 5 on the torso (bodies.json)
            Assert.Equal(25f * KillerVictimGenerator.MeanZoneFactor, civilian.DamagePerHit, 3);   // body 25 HP = slot 25: scale 1
            // the soldier's torso share loses half (5 mm vs 5 mm: ArmorModel's penetration chance 0.5); head, arms and legs are bare in front
            float lost = KillerVictimGenerator.TorsoShare * 1f * (25f - ArmorModel.HitDamage(5f, 25f, 5f));
            Assert.Equal(civilian.DamagePerHit - lost, soldier.DamagePerHit, 3);
        }

        /// <summary>
        /// ⭐ CE-8008 — the table follows the WORLD: steps on an EMPTY world (a node boots before its scenario arrives) build nothing and
        /// allocate nothing; when a stochastic unit arrives the next step builds the table; a unit holding a NEW type later rebuilds it.
        /// ✅ Red-proof: one try only (the old <c>_loadTried</c>) ⇒ no table once the unit arrives ⇒ red.
        /// </summary>
        [Fact]
        public void CE8008_ATableIsBuiltWhenUnitsArrive_AndRebuiltForANewType()
        {
            var was = KillerVictimTable.Current;
            KillerVictimTable.Current = null;
            try
            {
                using var w = new EntityRepository();
                w.RegisterComponent<AggregateUnit>(); w.RegisterComponent<UnitHoldings>(); w.RegisterComponent<FireAllocation>();
                w.RegisterComponent<UnitDetections>(); w.RegisterComponent<UnitSlotTable>(); w.RegisterComponent<SimTransform>(); w.RegisterComponent<GlobalTime>();
                w.SetSingletonManaged<ITkbDatabase>(Db());
                var fire = new StochasticFirePhase();
                for (int s = 1; s <= 3; s++) { w.SetSingleton(new GlobalTime { TotalTime = s }); fire.Execute(w, 1f); }
                Assert.Null(KillerVictimTable.Current);                                                    // nothing to build over yet
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int s = 4; s <= 20; s++) { w.SetSingleton(new GlobalTime { TotalTime = s }); fire.Execute(w, 1f); }
                Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);                          // an empty world costs nothing

                Entity Unit(long type, HoldingKind kind)
                {
                    var u = w.CreateEntity();
                    var h = new UnitHoldings { Count = 1 };
                    h.Items[0] = new UnitHolding { Type = new TkbTypeId(type), Count = 2, Operational = 2, Ammo = 100, Kind = kind, Mobility = HoldingMobility.Tracked };
                    w.AddComponent(u, h);
                    w.AddComponent(u, new AggregateUnit { Resolution = AggregateResolution.Stochastic });
                    w.AddComponent(u, new UnitSlotTable());
                    w.AddComponent(u, new UnitDetections());
                    w.AddComponent(u, new SimTransform());
                    return u;
                }
                Unit(Rifleman, HoldingKind.Weapon);                                                          // the scenario arrives
                w.SetSingleton(new GlobalTime { TotalTime = 21 }); fire.Execute(w, 1f);
                var first = KillerVictimTable.Current;
                Assert.NotNull(first);
                Assert.True(first!.TryGet(new TkbTypeId(Rifleman), KillerVictimTable.Personnel, out _));
                Assert.False(first.TryGet(new TkbTypeId(Ifv), new TkbTypeId(Ifv), out _));
                w.SetSingleton(new GlobalTime { TotalTime = 22 }); fire.Execute(w, 1f);
                Assert.Same(first, KillerVictimTable.Current);                                              // nothing new: the same table

                Unit(Ifv, HoldingKind.Vehicle);                                                              // a new type held
                w.SetSingleton(new GlobalTime { TotalTime = 23 }); fire.Execute(w, 1f);
                Assert.NotSame(first, KillerVictimTable.Current);
                Assert.True(KillerVictimTable.Current!.TryGet(new TkbTypeId(Ifv), new TkbTypeId(Ifv), out _));
            }
            finally { KillerVictimTable.Current = was; }
        }

        /// <summary>⭐ CE-8014 (agg-behaviors' F-1) — the ammunition share from the TKB's basic load, no capacity field: two IFVs that started with
        /// 2 × 0 rounds declared ⇒ unknown; a rifle squad at half its rounds ⇒ 0.5. ✅ Red-proof: the load summed over DOWN slots too ⇒ red.</summary>
        [Fact]
        public void TheAmmunitionShare_IsRoundsLeftOverTheTkbsBasicLoad()
        {
            var db = new TkbDatabase();
            var man = new TkbTemplate("Rifleman", Rifleman);
            man.AddDescriptor(new WeaponSuiteDto { Mounts = new() { new WeaponMountDto { InitialAmmunition = 200 } } });
            db.Register(man);
            var was = KillerVictimTkb.PersonalSoldiers;
            KillerVictimTkb.PersonalSoldiers = new Dictionary<SlotRole, TkbTypeId> { [SlotRole.Rifleman] = new(Rifleman) };
            try
            {
                using var w = new EntityRepository();
                w.RegisterComponent<UnitSlotTable>(); w.RegisterComponent<UnitHoldings>();
                w.SetSingletonManaged<ITkbDatabase>(db);
                var u = w.CreateEntity();
                var s = new UnitSlotTable { Count = 3 };
                s.Slots[0] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)25f, Ammo = 100 };
                s.Slots[1] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)25f, Ammo = 100 };
                s.Slots[2] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)0f, Ammo = 0 };   // down: not counted
                w.AddComponent(u, s);
                Assert.True(UnitAmmunition.TryShare(w, u, out float share));
                Assert.Equal(0.5f, share, 3);
                var none = w.CreateEntity();
                w.AddComponent(none, new UnitHoldings());
                Assert.False(UnitAmmunition.TryShare(w, none, out _));
            }
            finally { KillerVictimTkb.PersonalSoldiers = was; }
        }
    }
}
