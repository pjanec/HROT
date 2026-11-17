using System;
using System.Numerics;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Squad;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8011</c> (ST-4; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §20) — a stochastic BATTALION against a
    /// stochastic battalion (9 platoons a side, 30 men each), every engine step of the slices together — the march toward each other (ST-1),
    /// the sensing (ST-2), the attrition (ST-3) — runs to an end (one side under half strength) and is the SAME world at 1, 4 and 8 threads,
    /// byte for byte. ⭐ The scenario and its named checks are agg-behaviors' (§23.3); this is the engine's half: the per-entity phases'
    /// determinism. ✅ Red-proof: phase B's seed taken from the thread (Environment.CurrentManagedThreadId) ⇒ 4 threads differ ⇒ red.
    /// </summary>
    [Collection("FdpWorkerPool")]
    public sealed class BattalionDeterminismRails
    {
        private static KillerVictimTable Table()
        {
            var rifle = new TkbTypeId(9001);
            var t = KillerVictimGenerator.Build(
                new[] { new WeaponNumbers(rifle, 2f, 6f, 12.5f, 0.5f, 600f) },
                new[] { new TargetNumbers(KillerVictimTable.Personnel, 0.3f, 25f) });
            t.SetPersonalWeapon(SlotRole.Rifleman, rifle);
            t.SetPersonalWeapon(SlotRole.Leader, rifle);
            return t;
        }

        private static (string Hash, int Steps, int Blue, int Red) Fight(bool parallel, int threads)
        {
            bool wasPar = Fdp.Core.Jobs.ParallelEntities.Enabled;
            var wasTable = KillerVictimTable.Current;
            Fdp.Core.Jobs.ParallelEntities.Enabled = parallel;
            Fdp.Core.Jobs.FdpWorkerPool.Resize(threads);
            KillerVictimTable.Current = Table();
            try
            {
                using var w = new EntityRepository();
                w.RegisterComponent<SimTransform>(); w.RegisterComponent<EntityInfo>(); w.RegisterComponent<AggregateUnit>();
                w.RegisterComponent<UnitSlotTable>(); w.RegisterComponent<UnitHoldings>(); w.RegisterComponent<UnitDetections>();
                w.RegisterComponent<FireAllocation>(); w.RegisterComponent<SquadCognitiveState>(); w.RegisterComponent<GlobalTime>();
                w.RegisterManagedComponent<UnitRoute>();
                w.SetSingleton(new RunSeed { Value = 4242u });
                var units = new Entity[18];
                for (int i = 0; i < units.Length; i++)
                {
                    bool blue = i < 9;
                    var e = w.CreateEntity();
                    var at = new Vector3(-600f + (i % 9) * 150f, blue ? 0f : 1400f, 0f);
                    w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                    w.AddComponent(e, new EntityInfo { ForceId = blue ? ForceId.Friend : ForceId.Hostile });
                    var u = new AggregateUnit { Resolution = AggregateResolution.Stochastic, Echelon = 1, Width = 120f };
                    UnitMoves.Order(ref u, new WorldPoint(at.X, blue ? 600f : 800f, 0f), UnitFormation.Line, 0f, SimInstant.Zero);
                    w.AddComponent(e, u);
                    var s = new UnitSlotTable { Count = 30 };
                    for (int m = 0; m < 30; m++) s.Slots[m] = new UnitSlot { Role = m == 0 ? SlotRole.Leader : SlotRole.Rifleman, Health = (Half)25f, Ammo = 210 };
                    w.AddComponent(e, s);
                    w.AddComponent(e, new UnitHoldings());
                    w.AddComponent(e, new SquadCognitiveState());
                    units[i] = e;
                }
                var move = new StochasticUnitMovementSystem(); var sense = new StochasticUnitSensorSystem();
                var fire = new StochasticFirePhase(); var loss = new StochasticLossPhase();
                int Side(int from) { int n = 0; for (int i = from; i < from + 9; i++) n += w.GetComponentRO<UnitSlotTable>(units[i]).Present(); return n; }
                int step = 0;
                for (; step < 900; step++)
                {
                    w.SetSingleton(new GlobalTime { TotalTime = 1.0 + step, DeltaTime = 1f });
                    move.Execute(w, 1f); sense.Execute(w, 1f); fire.Execute(w, 1f); loss.Execute(w, 1f);
                    if (Side(0) < 135 || Side(9) < 135) break;   // one side under half strength: the end
                }
                var sb = new StringBuilder();
                foreach (var e in units)
                {
                    var u = w.GetComponentRO<AggregateUnit>(e);
                    var p = (Vector3)w.GetComponentRO<SimTransform>(e).Position;
                    sb.Append(BitConverter.SingleToInt32Bits(p.X)).Append(',').Append(BitConverter.SingleToInt32Bits(p.Y)).Append(',')
                      .Append(BitConverter.SingleToInt32Bits(u.SuppressedShare)).Append(',').Append((int)u.Move).Append(';');
                    var t = w.GetComponentRO<UnitSlotTable>(e);
                    for (int m = 0; m < t.Count; m++)
                        sb.Append(BitConverter.HalfToUInt16Bits(t.Slots[m].Health)).Append('.').Append(BitConverter.HalfToUInt16Bits(t.Slots[m].Stress)).Append('.').Append(t.Slots[m].Ammo).Append(' ');
                    var d = w.GetComponentRO<UnitDetections>(e);
                    for (int k = 0; k < d.Count; k++) sb.Append(d.Pairs[k].Target.Index).Append('=').Append(BitConverter.SingleToInt32Bits(d.Pairs[k].Level)).Append(' ');
                    sb.Append('|');
                }
                return (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))), step, Side(0), Side(9));
            }
            finally
            {
                Fdp.Core.Jobs.ParallelEntities.Enabled = wasPar;
                Fdp.Core.Jobs.FdpWorkerPool.Resize(Fdp.Core.Jobs.FdpWorkerPool.Budget());
                KillerVictimTable.Current = wasTable;
            }
        }

        [Fact]
        public void ST4_BattalionAgainstBattalion_RunsToAnEnd_TheSameAt1_4And8Threads()
        {
            var serial = Fight(false, 1);
            Assert.True(serial.Steps < 900, $"no end after {serial.Steps} steps (blue {serial.Blue}, red {serial.Red})");
            Assert.True(serial.Blue < 270 && serial.Red < 270, $"both sides fought: blue {serial.Blue}, red {serial.Red}");
            var four = Fight(true, 4);
            var eight = Fight(true, 8);
            Assert.Equal(serial, four);
            Assert.Equal(serial, eight);
        }
    }
}
