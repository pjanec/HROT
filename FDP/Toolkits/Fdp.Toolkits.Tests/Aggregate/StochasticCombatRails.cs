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

        private static KillerVictimTable Table()
        {
            var rifle = new WeaponNumbers(Rifle, DispersionMils: 2f, Penetration: 6f, DamagePerHit: 12.5f, EffectiveRoundsPerSecond: 0.5f, MaxRange: 600f);
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
            Assert.Contains("MS-T1", KillerVictimGenerator.BottomFacingTodo);                                                  // the named TODO
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
        private static (int A, int B, int AmmoA, float SuppB, string State) Duel(uint seed, int steps, PreparedLevel bCover = PreparedLevel.Unprepared, bool parallel = false, int threads = 1)
        {
            bool wasPar = Fdp.Core.Jobs.ParallelEntities.Enabled;
            var wasTable = KillerVictimTable.Current;
            Fdp.Core.Jobs.ParallelEntities.Enabled = parallel;
            Fdp.Core.Jobs.FdpWorkerPool.Resize(threads);
            KillerVictimTable.Current = Table();
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
