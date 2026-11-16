using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Tkb;
using Hrot.Core.Tkb;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐ <c>CE-8009</c> (C-8, RV-3) — the STOCHASTIC side of the ladder's rung 1: 9 v 9 riflemen (the InfantrySoldier, 2002, the type the physical
    /// rungs <c>scenarios/tests/c8-ladder/c8-open-*</c> use) in the open at 100 / 200 / 300 m, 30 run seeds each, through the real phases
    /// (sensing, fire, losses) and the table generated from the SAME TKB. Records, per run, the first time 1…9 men of each side are down — the
    /// physical rungs' check rows, read the same way by <c>scripts/c8-ladder.py</c>. With <c>HROT_C8_OUT</c> set the rows are written there (JSON).
    /// ⭐ The rail itself asserts only what is not the calibration: both sides lose men at 100 m, the closer fight is the bloodier, and a seed is
    /// a seed (the same seed twice gives the same run); and CE-8009's calibration — the winner loses men too, and nobody fights past the eye.
    /// </summary>
    [Collection("FdpWorkerPool")]
    public sealed class C8LadderStochasticTests
    {
        public const int Seeds = 30, RunFor = 120;
        private static readonly int[] Ranges = { 100, 200, 300 };
        private static readonly TkbTypeId Soldier = (TkbTypeId)UrbanCombatTkbCatalog.TkbInfantrySoldier;

        /// <summary>One run: the first second at which k men of Blue / Red are down (k = 1…9; −1 = never).</summary>
        public static (int[] Blue, int[] Red) Run(ITkbDatabase db, int range, uint seed)
        {
            var wasTable = KillerVictimTable.Current;
            var wasPersonal = KillerVictimTkb.PersonalSoldiers;
            bool wasPar = Fdp.Core.Jobs.ParallelEntities.Enabled;
            Fdp.Core.Jobs.ParallelEntities.Enabled = false;
            KillerVictimTkb.PersonalSoldiers = new Dictionary<SlotRole, TkbTypeId> { [SlotRole.Rifleman] = Soldier, [SlotRole.Leader] = Soldier };
            KillerVictimTable.Current = null;
            try
            {
                using var w = new EntityRepository();
                w.RegisterComponent<SimTransform>(); w.RegisterComponent<EntityInfo>(); w.RegisterComponent<AggregateUnit>();
                w.RegisterComponent<UnitSlotTable>(); w.RegisterComponent<UnitHoldings>(); w.RegisterComponent<UnitDetections>();
                w.RegisterComponent<FireAllocation>(); w.RegisterComponent<SquadCognitiveState>(); w.RegisterComponent<GlobalTime>();
                w.SetSingletonManaged<ITkbDatabase>(db);
                w.SetSingleton(new RunSeed { Value = seed });
                var blue = Side(w, db, new Vector3(300, 300, 0), ForceId.Friend, 0f);
                var red = Side(w, db, new Vector3(300, 300 + range, 0), ForceId.Hostile, 180f);
                var sense = new StochasticUnitSensorSystem(); var fire = new StochasticFirePhase(); var loss = new StochasticLossPhase();
                var b = new int[9]; var r = new int[9];
                Array.Fill(b, -1); Array.Fill(r, -1);
                for (int s = 1; s <= RunFor; s++)
                {
                    w.SetSingleton(new GlobalTime { TotalTime = s });
                    sense.Execute(w, 1f); fire.Execute(w, 1f); loss.Execute(w, 1f);
                    Mark(9 - w.GetComponentRO<UnitSlotTable>(blue).Present(), s, b);
                    Mark(9 - w.GetComponentRO<UnitSlotTable>(red).Present(), s, r);
                }
                return (b, r);
            }
            finally
            {
                KillerVictimTable.Current = wasTable;
                KillerVictimTkb.PersonalSoldiers = wasPersonal;
                Fdp.Core.Jobs.ParallelEntities.Enabled = wasPar;
            }
        }

        private static void Mark(int down, int s, int[] firstAt)
        {
            for (int k = 1; k <= Math.Min(9, down); k++) if (firstAt[k - 1] < 0) firstAt[k - 1] = s;
        }

        private static Entity Side(EntityRepository w, ITkbDatabase db, Vector3 at, ForceId force, float heading)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(e, new EntityInfo { ForceId = force });
            w.AddComponent(e, new AggregateUnit { Resolution = AggregateResolution.Stochastic, Width = 40f, Heading = heading });   // 9 abreast, 5 m apart
            var s = new UnitSlotTable { Count = 9 };
            ushort load = (ushort)Math.Clamp(UnitAmmunition.BasicLoad(db, Soldier), 0, ushort.MaxValue);
            for (int i = 0; i < 9; i++)
                s.Slots[i] = new UnitSlot { Role = i == 4 ? SlotRole.Leader : SlotRole.Rifleman, Health = (Half)UnitSplits.SlotFullHealth, Ammo = load, CompositeSlot = (byte)(i + 1) };
            w.AddComponent(e, s);
            w.AddComponent(e, new UnitHoldings());
            w.AddComponent(e, new UnitDetections());
            w.AddComponent(e, new SquadCognitiveState());
            return e;
        }

        [Fact]
        public void C8_Rung1_TheStochasticSide_FightsAndIsMeasured()
        {
            var db = new TkbDatabase();
            UrbanCombatTkbCatalog.RegisterAll(db);
            Assert.True(UnitAmmunition.BasicLoad(db, Soldier) > 0, "the soldier carries a basic load");
            var json = new StringBuilder("{\"resolution\":\"stochastic\",\"runFor\":").Append(RunFor).Append(",\"rungs\":{");
            var meanLosses = new Dictionary<int, double>();
            double winnerLosses100 = 0, firstLoss100 = 0;
            for (int ri = 0; ri < Ranges.Length; ri++)
            {
                int range = Ranges[ri];
                json.Append(ri == 0 ? "" : ",").Append('"').Append("c8-open-").Append(range).Append("\":[");
                double losses = 0;
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    var (b, r) = Run(db, range, seed);
                    losses += Count(b) + Count(r);
                    if (range == 100)
                    {
                        winnerLosses100 += Math.Min(Count(b), Count(r));
                        firstLoss100 += Math.Min(b[0] < 0 ? RunFor : b[0], r[0] < 0 ? RunFor : r[0]);
                    }
                    json.Append(seed == 1 ? "" : ",").Append("{\"seed\":").Append(seed)
                        .Append(",\"Blue\":[").Append(string.Join(",", b)).Append("],\"Red\":[").Append(string.Join(",", r)).Append("]}");
                }
                json.Append(']');
                meanLosses[range] = losses / Seeds;
            }
            json.Append("}}");
            var outPath = Environment.GetEnvironmentVariable("HROT_C8_OUT");
            if (!string.IsNullOrEmpty(outPath)) System.IO.File.WriteAllText(outPath, json.ToString());

            Assert.True(meanLosses[100] >= 2.0, $"at 100 m both sides lose men (mean {meanLosses[100].ToString("0.0", CultureInfo.InvariantCulture)} down in all)");
            Assert.True(meanLosses[100] >= meanLosses[300], "the closer fight is not the less bloody");
            // ⭐ CE-8009's calibration, pinned: the winner PAYS (the physical 3.3 men — focused hits, a broken side still fires from the ground)
            Assert.True(winnerLosses100 / Seeds >= 2.0, $"the winner loses men too (mean {(winnerLosses100 / Seeds).ToString("0.00", CultureInfo.InvariantCulture)})");
            // ⭐ the first man falls early (the physical 5.1 s): hits CONCENTRATE on the man under fire — spread evenly, nobody falls for ~9 s
            Assert.True(firstLoss100 / Seeds <= 7.0, $"the first loss comes early (mean {(firstLoss100 / Seeds).ToString("0.0", CultureInfo.InvariantCulture)} s)");
            // ⭐ and nobody sees past the soldier's own eye (the TKB VisionRange, 150 m): at 200 m there is no fight, as there is none physically
            Assert.Equal(0.0, meanLosses[200]);
            var again = Run(db, 100, 7); var first = Run(db, 100, 7);
            Assert.Equal(first.Blue, again.Blue);                                               // a seed is a seed (C-8: the run seed)
            Assert.Equal(first.Red, again.Red);
        }

        private static int Count(int[] firstAt)
        {
            int n = 0;
            foreach (int t in firstAt) if (t >= 0) n++;
            return n;
        }
    }
}
