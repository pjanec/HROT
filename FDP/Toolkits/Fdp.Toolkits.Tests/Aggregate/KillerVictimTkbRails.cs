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
            Assert.Equal(30f / (30f * 0.8f + 3f), rifle.EffectiveRate, 4);                                 // a magazine aimed, then the reload
            Assert.Equal(HitModel.HitChance(0.002f, 50f, 0.3f), rifle.PHitByBand[0], 5);
            Assert.True(t.TryGet(new TkbTypeId(Ifv), new TkbTypeId(Ifv), out var gunOnHull));
            Assert.True(gunOnHull.Suitability > 0.9f);                                                       // 40 mm through 10–30 mm
            Assert.True(t.TryGet(new TkbTypeId(Rifleman), new TkbTypeId(Ifv), out var rifleOnHull));
            Assert.Equal(0f, rifleOnHull.Suitability);
            Assert.Equal(new TkbTypeId(Rifleman), t.PersonalWeapon(SlotRole.Rifleman));
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
    }
}
