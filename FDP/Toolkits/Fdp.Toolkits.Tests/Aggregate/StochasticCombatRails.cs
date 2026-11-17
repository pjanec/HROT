using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Combat;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8007</c> (ST-3, C-1 … C-5, C-8; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §18) — the killer–victim
    /// table GENERATED from the physical models, and the two attrition phases: losses on both sides, ammunition spent, suppression, the draw
    /// seeded by the run seed, cover and armour cutting losses, serial = parallel, 0 B per steady step.
    /// </summary>
    [Collection("FdpWorkerPool")]
    public sealed class StochasticCombatRails
    {
        private static readonly TkbTypeId Rifle = new(9001), Ifv = new(212);

        private static KillerVictimTable Table(float damagePerHit = 12.5f)
        {
            var rifle = new WeaponNumbers(Rifle, DispersionMils: 2f, Penetration: 6f, DamagePerHit: damagePerHit, EffectiveRoundsPerSecond: 0.5f, MaxRange: 600f);
            var t = KillerVictimGenerator.Build(new[] { rifle }, new[]
            {
                new TargetNumbers(KillerVictimTable.Personnel, Radius: 0.3f, Health: 25f),
                new TargetNumbers(Ifv, Radius: 2f, Health: 100f, ArmourFront: 30f, ArmourSide: 15f, ArmourRear: 10f),
            });
            t.SetPersonalWeapon(SlotRole.Rifleman, Rifle);
            t.SetPersonalWeapon(SlotRole.Leader, Rifle);
            return t;
        }

        [Fact]
        public void C1_TheTable_IsGeneratedFromHitModelAndArmorModel()
        {
            var t = Table();
            Assert.True(t.TryGet(Rifle, KillerVictimTable.Personnel, out var men));
            for (int b = 0; b < men.PHitByBand.Length; b++)
                Assert.Equal(HitModel.HitChance(0.002f, (b + 0.5f) * KillerVictimTable.BandMetres, 0.3f), men.PHitByBand[b], 5);   // the same numbers
            Assert.Equal(ArmorModel.HitDamage(6f, 12.5f, 0f) * KillerVictimGenerator.MeanZoneFactor, men.DamagePerHit, 4);
            Assert.Equal(0f, men.PHitAt(700f));                                                                               // past its reach
            Assert.True(t.TryGet(Rifle, Ifv, out var hull));
            Assert.Equal(0f, hull.Suitability);                                                                               // 6 mm never defeats 10 mm
            Assert.Equal(0f, hull.KKillShare);
            var back = KillerVictimTable.FromJson(t.ToJson());                                                                // the data asset
            Assert.True(back.TryGet(Rifle, KillerVictimTable.Personnel, out var again));
            Assert.Equal(men.PHitByBand, again.PHitByBand);
            Assert.Equal(Rifle, back.PersonalWeapon(SlotRole.Rifleman));
        }

        [Fact]
        public void C1_TheBottomRow_AMineUnderTheHull_MeetsTheBelly_AndTheBellyIsTheRearUntilDeclared()
        {
            var mine = new TkbTypeId(9301);
            var w = new WeaponNumbers(mine, DispersionMils: 0f, Penetration: 40f, DamagePerHit: 300f, EffectiveRoundsPerSecond: 1f, MaxRange: 1f, FromBelow: true);
            var bare = new TargetNumbers(Ifv, Radius: 2f, Health: 100f, ArmourFront: 60f, ArmourSide: 30f, ArmourRear: 20f);
            var bellied = bare with { ArmourBottom = 80f };
            var t = KillerVictimGenerator.Build(new[] { w }, new[] { bare, new TargetNumbers(KillerVictimTable.Personnel, 0.3f, 25f) });
            Assert.True(t.TryGet(mine, Ifv, out var row));
            Assert.Equal(1f, row.PHitAt(0f));                                                                   // under the hull: no dispersion
            Assert.Equal(ArmorModel.HitDamage(40f, 300f, 20f), row.DamagePerHit, 4);                              // no belly declared ⇒ the rear
            Assert.Equal(ArmorModel.PenetrationChance(40f, 20f) * KillerVictimGenerator.KKillOfPenetrating, row.KKillShare, 4);
            var thick = KillerVictimGenerator.BottomAttack(in w, in bellied);
            Assert.Equal(ArmorModel.HitDamage(40f, 300f, 80f), thick.DamagePerHit, 4);                            // a declared belly is read
            Assert.True(thick.KKillShare < row.KKillShare);
            Assert.True(thick.MKillShare > 0f);                                                                   // a belly blast still immobilises
            Assert.True(t.TryGet(mine, KillerVictimTable.Personnel, out var man));
            Assert.Equal(ArmorModel.HitDamage(40f, 300f, 0f), man.DamagePerHit, 4);                               // no zone mix under a man
            // ⭐ the same belly the physical model meets: ArmorModel.ArmourFor(Facing.Bottom)
            var p = new Fdp.Toolkit.Tkb.Domain.CombatPlatformDefDto { ArmorFront = 60f, ArmorSide = 30f, ArmorRear = 20f, ArmorBottom = 80f };
            Assert.Equal(ArmorModel.ArmourFor(p, Facing.Bottom), bellied.BellyArmour);
            Assert.Equal(ArmorModel.ArmourFor(p with { ArmorBottom = 0f }, Facing.Bottom), bare.BellyArmour);
        }

        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>(); w.RegisterComponent<AggregateUnit>(); w.RegisterComponent<UnitSlotTable>();
            w.RegisterComponent<UnitHoldings>(); w.RegisterComponent<UnitDetections>(); w.RegisterComponent<FireAllocation>(); w.RegisterComponent<GlobalTime>();
            return w;
        }

        private static Entity Squad(EntityRepository w, Vector3 at, PreparedLevel prepared = PreparedLevel.Unprepared, UnitPosture posture = UnitPosture.Dismounted)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(e, new AggregateUnit { Resolution = AggregateResolution.Stochastic, Width = 60f, Prepared = prepared, Posture = posture });
            var s = new UnitSlotTable { Count = 9 };
            for (int i = 0; i < 9; i++) s.Slots[i] = new UnitSlot { Role = i == 0 ? SlotRole.Leader : SlotRole.Rifleman, Health = (Half)25f, Ammo = 210 };
            w.AddComponent(e, s);
            w.AddComponent(e, new UnitHoldings());
            w.AddComponent(e, new UnitDetections());
            return e;
        }

        private static void Sees(EntityRepository w, Entity observer, Entity target)
        {
            ref var d = ref w.GetComponentRW<UnitDetections>(observer);
            d.Pairs[d.Count++] = new DetectionPair { Target = target, Level = 1.5f, Reported = 1 };
        }

        /// <summary>One duel: <paramref name="steps"/> 1 s steps; returns (A's present, B's present, A's ammo, B's suppression).</summary>
        private static (int A, int B, int AmmoA, float SuppB, string State) Duel(uint seed, int steps, PreparedLevel bCover = PreparedLevel.Unprepared, bool parallel = false, int threads = 1,
                                                                                 float manOutBelow = 0f, float damagePerHit = 12.5f,
                                                                                 float[]? zoneShare = null, float[]? zoneFactor = null)
        {
            bool wasPar = Fdp.Core.Jobs.ParallelEntities.Enabled;
            var wasTable = KillerVictimTable.Current;
            Fdp.Core.Jobs.ParallelEntities.Enabled = parallel;
            Fdp.Core.Jobs.FdpWorkerPool.Resize(threads);
            KillerVictimTable.Current = Table(damagePerHit);
            KillerVictimTable.Current.ManOutBelow = manOutBelow;
            KillerVictimTable.Current.ManZoneShare = zoneShare; KillerVictimTable.Current.ManZoneFactor = zoneFactor;
            try
            {
                using var w = World();
                w.SetSingleton(new RunSeed { Value = seed });                                   // the recorded seed of this run (C-8)
                var a = Squad(w, Vector3.Zero);
                var b = Squad(w, new Vector3(0, 200, 0), bCover);
                Sees(w, a, b); Sees(w, b, a);
                var fire = new StochasticFirePhase(); var loss = new StochasticLossPhase();
                float peak = 0f;   // the most B was suppressed during the fight (it decays once the fire stops)
                for (int s = 1; s <= steps; s++)
                {
                    w.SetSingleton(new GlobalTime { TotalTime = s });
                    fire.Execute(w, 1f); loss.Execute(w, 1f);
                    peak = MathF.Max(peak, w.GetComponentRO<AggregateUnit>(b).SuppressedShare);
                }
                var sa = w.GetComponentRO<UnitSlotTable>(a); var sb = w.GetComponentRO<UnitSlotTable>(b);
                int ammo = 0; for (int i = 0; i < sa.Count; i++) ammo += sa.Slots[i].Ammo;
                var state = new System.Text.StringBuilder();
                foreach (var t in new[] { sa, sb })
                    for (int i = 0; i < t.Count; i++) state.Append(BitConverter.HalfToUInt16Bits(t.Slots[i].Health)).Append('/').Append(BitConverter.HalfToUInt16Bits(t.Slots[i].Stress)).Append('/').Append(t.Slots[i].Ammo).Append(',');
                return (sa.Present(), sb.Present(), ammo, peak, state.ToString());
            }
            finally
            {
                Fdp.Core.Jobs.ParallelEntities.Enabled = wasPar;
                Fdp.Core.Jobs.FdpWorkerPool.Resize(Fdp.Core.Jobs.FdpWorkerPool.Budget());
                KillerVictimTable.Current = wasTable;
            }
        }

        /// <summary>⭐ C-2 / C-3 / C-4 — two squads in the open at 200 m: both lose men, ammunition is spent, the target is suppressed; the same
        /// run seed repeats the same fight, another seed fights another (C-8). ✅ Red-proof: phase B not reading the enemy's lines ⇒ no losses ⇒ red.</summary>
        [Fact]
        public void C2_TwoSquadsInTheOpen_TradeLosses_SpendAmmunition_AndAreSuppressed()
        {
            var r = Duel(11u, 120);
            Assert.True(r.A < 9 && r.B < 9, $"losses A {9 - r.A}, B {9 - r.B}");
            Assert.True(r.AmmoA < 9 * 210, "ammunition spent");
            Assert.True(r.SuppB > 0f, "suppressed");
            Assert.Equal(r.State, Duel(11u, 120).State);                                   // one seed, one fight
            int differ = 0;
            for (uint s = 12; s < 18; s++) if (Duel(s, 120).State != r.State) differ++;
            Assert.True(differ >= 5, $"only {differ}/6 seeds fought a different fight");
        }

        /// <summary>
        /// ⭐ CE-8009 — a man hit below the soldier's INCAPACITATION line is out of the fight (down), as the physical injury model takes him
        /// out (<c>Gen.Injury IncapacitatedBelow</c>, 30 % of the slot = 7.5), not only when his health runs out: over 30 seeds at 30 s more men
        /// are down with the line than without. The line travels with the table (its JSON).
        /// ✅ Red-proof: the line not applied in the loss phase ⇒ no difference ⇒ red.
        /// </summary>
        [Fact]
        public void CE8009_AManBelowTheIncapacitationLine_IsOutOfTheFight()
        {
            int without = 0, with = 0;
            // a 9-point round: 25 → 15.7 → 6.4 — below the line after TWO hits, below DownBelow only after THREE
            for (uint s = 1; s <= 30; s++)
            {
                var a = Duel(s, 30, damagePerHit: 9f); without += 18 - a.A - a.B;
                var b = Duel(s, 30, manOutBelow: 7.5f, damagePerHit: 9f); with += 18 - b.A - b.B;
            }
            Assert.True(with > without, $"down with the line {with}, without {without}");
            var t = Table(); t.ManOutBelow = 7.5f;
            Assert.Equal(7.5f, KillerVictimTable.FromJson(t.ToJson()).ManOutBelow);
        }

        /// <summary>
        /// ⭐ CE-8009 — a hit lands on ONE zone: with a head (1 in 10, 3× the mean) and a body (the rest, below the mean) and the incapacitation
        /// line between them, the first hits mostly WOUND (body) and a few take a man out at once (head), where the mean took him out with every
        /// hit. Over 30 seeds at 10 s fewer men are down with the zones than with the mean. ✅ Red-proof: the zone draw skipped ⇒ the mean ⇒ red.
        /// </summary>
        [Fact]
        public void CE8009_AHitLandsOnOneZone_NotTheMean()
        {
            // a 20-point round: mean 20.6 (25 → 4.4, below the 7.5 line: out); body 0.75 × mean (25 → 9.6, wounded); head 3.25 × mean (out)
            var share = new[] { 0.1f, 0.9f };
            var factor = new[] { 3.25f, 0.75f };
            int mean = 0, zoned = 0;
            for (uint s = 1; s <= 30; s++)
            {
                var a = Duel(s, 10, manOutBelow: 7.5f, damagePerHit: 20f); mean += 18 - a.A - a.B;
                var b = Duel(s, 10, manOutBelow: 7.5f, damagePerHit: 20f, zoneShare: share, zoneFactor: factor); zoned += 18 - b.A - b.B;
            }
            Assert.True(zoned < mean, $"down at 10 s: zoned {zoned}, mean {mean}");
        }

        /// <summary>⭐ P-5 / C-2 — over 30 seeds a FORTIFIED squad loses far fewer men than one in the open (the cover factor), at 60 s.</summary>
        [Fact]
        public void C2_CoverCutsTheLosses_OverThirtySeeds()
        {
            int open = 0, fortified = 0;
            for (uint s = 1; s <= 30; s++) { open += 9 - Duel(s, 60).B; fortified += 9 - Duel(s, 60, PreparedLevel.Fortified).B; }
            Assert.True(open > 0);
            Assert.True(fortified < open * 0.6, $"open {open}, fortified {fortified}");
        }

        /// <summary>⭐ The parallel rule — phases A and B at 4 and 8 threads give exactly the serial fight (each writes only itself; the
        /// targets and the allocations are declared stable reads). Run under <c>HROT_READ_CHECK=1</c> an undeclared read throws.</summary>
        [Fact]
        public void ThePhases_InParallel_FightTheSerialFight()
        {
            var serial = Duel(7u, 90).State;
            Assert.Equal(serial, Duel(7u, 90, parallel: true, threads: 4).State);
            Assert.Equal(serial, Duel(7u, 90, parallel: true, threads: 8).State);
        }

        /// <summary>⭐ R-2084 — a steady step allocates nothing.</summary>
        [Fact]
        public void TheSteadyStep_AllocatesNothing()
        {
            var was = KillerVictimTable.Current;
            KillerVictimTable.Current = Table();
            try
            {
                using var w = World();
                var a = Squad(w, Vector3.Zero); var b = Squad(w, new Vector3(0, 400, 0));
                Sees(w, a, b); Sees(w, b, a);
                var fire = new StochasticFirePhase(); var loss = new StochasticLossPhase();
                for (int s = 1; s <= 3; s++) { w.SetSingleton(new GlobalTime { TotalTime = s }); fire.Execute(w, 1f); loss.Execute(w, 1f); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int s = 4; s <= 30; s++) { w.SetSingleton(new GlobalTime { TotalTime = s }); fire.Execute(w, 1f); loss.Execute(w, 1f); }
                Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            finally { KillerVictimTable.Current = was; }
        }
    }
}
