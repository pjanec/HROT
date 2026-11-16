using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Squad;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8006</c> (ST-2, N-1 … N-5; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §15) — the stochastic units'
    /// sensing: the step's chance follows N-1's curve against range; the chance ACCUMULATES to a contact in the observer's own squad pool
    /// (N-2) after ⌈1 / p⌉ steps, not before; a friend is never detected; a dead target leaves the pool; 0 B per steady step.
    /// </summary>
    [Collection("FdpWorkerPool")]
    public sealed class UnitSensingRails
    {
        private static Entity Unit(EntityRepository w, Vector3 at, ForceId force, int men = 9, UnitActivity activity = UnitActivity.Quiet)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(e, new EntityInfo { ForceId = force });
            w.AddComponent(e, new AggregateUnit { Resolution = AggregateResolution.Stochastic, Width = 100f, Activity = activity });
            var t = new UnitSlotTable { Count = (byte)men };
            for (int i = 0; i < men; i++) t.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)25f };
            w.AddComponent(e, t);
            w.AddComponent(e, new UnitHoldings());
            w.AddComponent(e, new SquadCognitiveState());
            return e;
        }

        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>(); w.RegisterComponent<EntityInfo>(); w.RegisterComponent<AggregateUnit>();
            w.RegisterComponent<UnitSlotTable>(); w.RegisterComponent<UnitHoldings>(); w.RegisterComponent<UnitDetections>();
            w.RegisterComponent<SquadCognitiveState>(); w.RegisterComponent<GlobalTime>();
            return w;
        }

        private static void At(EntityRepository w, double t) => w.SetSingleton(new GlobalTime { TotalTime = t });

        /// <summary>⭐ ST-2's acceptance: the step's chance against range IS N-1's curve — Pmax at 0, half at R50, a sixteenth at 2·R50, nothing
        /// beyond the reach; a bigger, moving, firing target is seen farther. ✅ Red-proof: the exponent squared dropped ⇒ the 2·R50 value moves ⇒ red.</summary>
        [Fact]
        public void N1_TheStepChance_FollowsTheCurve()
        {
            float r50 = UnitDetection.EyeR50;
            Assert.Equal(UnitDetection.PMax, UnitDetection.StepChance(0f, r50), 4);
            Assert.Equal(UnitDetection.PMax / 2f, UnitDetection.StepChance(r50, r50), 4);
            Assert.Equal(UnitDetection.PMax / 16f, UnitDetection.StepChance(2f * r50, r50), 4);
            for (float r = 0; r < 3 * r50; r += 37f)
                Assert.True(UnitDetection.StepChance(r + 37f, r50) < UnitDetection.StepChance(r, r50));   // monotone
            Assert.Equal(0f, UnitDetection.StepChance(UnitDetection.MaxRange + 1f, r50));
            Assert.Equal(UnitDetection.PMax / 4f, UnitDetection.StepChance(0f, r50, 0.25f), 4);         // the woods' transmittance
            var still = new AggregateUnit(); var busy = new AggregateUnit { Activity = UnitActivity.Moving | UnitActivity.Firing };
            var nine = new UnitSlotTable { Count = 9 };
            for (int i = 0; i < 9; i++) nine.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)25f };
            Assert.Equal(1f, UnitDetection.Signature(in still, in nine, default), 3);                   // a squad of 9 in the open = 1
            Assert.Equal(UnitDetection.MovingFactor * UnitDetection.FiringFactor, UnitDetection.Signature(in busy, in nine, default), 3);
            var sensors = new UnitHoldings { Count = 1 };
            sensors.Items[0] = new UnitHolding { Kind = HoldingKind.Sensor, Sensor = HoldingSensor.Thermal, Count = 1, Operational = 1 };
            Assert.Equal(UnitDetection.ThermalR50, UnitDetection.SensorR50(in sensors, false, out bool thermal));
            Assert.True(thermal);
        }

        /// <summary>⭐ N-5 / N-2 — the chance accumulates: at R50 (p = 0.45 a step) the contact lands in the observer's pool on the third step,
        /// not the second; a friend is never detected; the target's death takes it out of the pool. ✅ Red-proof: the level REPLACED by the step's
        /// chance instead of added ⇒ never reaches 1 ⇒ red.</summary>
        [Fact]
        public void N5_TheChanceAccumulates_ToAContactInTheSquadPool()
        {
            using var w = World();
            var me = Unit(w, Vector3.Zero, ForceId.Friend);
            var friend = Unit(w, new Vector3(0, 100, 0), ForceId.Friend);
            var enemy = Unit(w, new Vector3(0, UnitDetection.EyeR50, 0), ForceId.Hostile);
            var sys = new StochasticUnitSensorSystem();
            int Pool() => w.GetComponentRO<SquadCognitiveState>(me).Contacts.Count;
            At(w, 1); sys.Execute(w, 1f);
            At(w, 2); sys.Execute(w, 1f);
            Assert.Equal(0, Pool());                                                               // 0.9 < 1
            At(w, 3); sys.Execute(w, 1f);
            Assert.Equal(1, Pool());                                                               // 1.35 ≥ 1 on the third step
            var c = SquadContacts.Slots(w.GetComponentRO<SquadCognitiveState>(me).Contacts)[0];
            Assert.Equal((long)enemy.PackedValue, c.EntityId);
            Assert.True(w.GetComponentRO<SquadCognitiveState>(friend).Contacts.Count <= 1);       // the friend sees the enemy, never us
            for (int i = 0; i < w.GetComponentRO<SquadCognitiveState>(friend).Contacts.Count; i++)
                Assert.NotEqual((long)me.PackedValue, SquadContacts.Slots(w.GetComponentRO<SquadCognitiveState>(friend).Contacts)[i].EntityId);
            At(w, 3.5); sys.Execute(w, 1f);                                                        // within the step: nothing runs
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int s = 4; s < 30; s++) { At(w, s); sys.Execute(w, 1f); }
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            w.DestroyEntity(enemy);
            At(w, 31); sys.Execute(w, 1f);
            Assert.Equal(0, Pool());
            Assert.Equal(0, w.GetComponentRO<UnitDetections>(me).Count);
        }

        /// <summary>
        /// ⭐ CE-8018 (N-3) — a stochastic observer sees PHYSICAL bodies, one way: an enemy man at ONE man's half-chance range (R50 × √(1/9))
        /// lands in its pool on the third step, as a unit at its own R50 does; a friendly man, a dead one and a part (a mount) never; the man's
        /// death takes him out. ✅ Red-proof: the bodies not gathered ⇒ the pool stays empty ⇒ red.
        /// </summary>
        [Fact]
        public void N3_AStochasticObserver_SeesAPhysicalEnemy_OneWay()
        {
            using var w = World();
            w.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>(); w.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            var me = Unit(w, Vector3.Zero, ForceId.Friend);
            Entity Man(Vector3 at, ForceId f, float hp)
            {
                var m = w.CreateEntity();
                w.AddComponent(m, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                w.AddComponent(m, new EntityInfo { ForceId = f });
                w.AddComponent(m, new Fdp.Toolkit.Combat.Components.Health { Current = hp, Max = 100f });
                return m;
            }
            float r50 = UnitDetection.EyeR50 * MathF.Sqrt(UnitDetection.PerMan);
            var enemy = Man(new Vector3(0, r50, 0), ForceId.Hostile, 100f);
            Man(new Vector3(10, 20, 0), ForceId.Friend, 100f);                       // a friend
            Man(new Vector3(-10, 20, 0), ForceId.Hostile, 0f);                       // a dead man
            var mount = Man(new Vector3(5, 20, 0), ForceId.Hostile, 100f);
            w.AddComponent(mount, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = enemy });   // a part, not a body
            var sys = new StochasticUnitSensorSystem();
            int Pool() => w.GetComponentRO<SquadCognitiveState>(me).Contacts.Count;
            At(w, 1); sys.Execute(w, 1f);
            At(w, 2); sys.Execute(w, 1f);
            Assert.Equal(0, Pool());
            At(w, 3); sys.Execute(w, 1f);
            Assert.Equal(1, Pool());                                                 // the same curve as a unit's
            Assert.Equal((long)enemy.PackedValue, SquadContacts.Slots(w.GetComponentRO<SquadCognitiveState>(me).Contacts)[0].EntityId);
            Assert.Equal(1, w.GetComponentRO<UnitDetections>(me).Count);             // nobody else is even a pair
            w.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(enemy).Current = 0f;
            At(w, 4); sys.Execute(w, 1f);
            Assert.Equal(0, Pool());
            Assert.Equal(0, w.GetComponentRO<UnitDetections>(me).Count);
        }

        /// <summary>⭐ A unit at twice R50 needs ⌈1 / (Pmax / 16)⌉ = 18 steps; a moving, firing one is seen far sooner (N-4's signature).</summary>
        [Fact]
        public void N4_AMovingFiringTarget_IsDetectedSooner()
        {
            int StepsTo(UnitActivity a)
            {
                using var w = World();
                var me = Unit(w, Vector3.Zero, ForceId.Friend);
                Unit(w, new Vector3(2f * UnitDetection.EyeR50, 0, 0), ForceId.Hostile, activity: a);
                var sys = new StochasticUnitSensorSystem();
                for (int s = 1; s < 100; s++) { At(w, s); sys.Execute(w, 1f); if (w.GetComponentRO<SquadCognitiveState>(me).Contacts.Count > 0) return s; }
                return 999;
            }
            Assert.Equal(18, StepsTo(UnitActivity.Quiet));
            Assert.True(StepsTo(UnitActivity.Moving | UnitActivity.Firing) <= 2);
        }
    
        /// <summary>⭐ The parallel rule — the sensing phase at 4 and 8 threads gives exactly the serial detections and pools (each observer writes
        /// only itself; the targets are a declared stable read). Run under <c>HROT_READ_CHECK=1</c> an undeclared cross-unit read throws.</summary>
        [Fact]
        public void TheParallelPhase_GivesTheSerialDetections()
        {
            string Run(bool parallel, int threads)
            {
                bool was = Fdp.Core.Jobs.ParallelEntities.Enabled;
                Fdp.Core.Jobs.ParallelEntities.Enabled = parallel;
                Fdp.Core.Jobs.FdpWorkerPool.Resize(threads);
                try
                {
                    using var w = World();
                    var rng = new Random(8006);
                    var units = new Entity[40];
                    for (int i = 0; i < units.Length; i++)
                        units[i] = Unit(w, new Vector3(rng.Next(0, 3000), rng.Next(0, 3000), 0), i % 2 == 0 ? ForceId.Friend : ForceId.Hostile,
                                        men: 3 + i % 9, activity: (UnitActivity)(i % 4));
                    var sys = new StochasticUnitSensorSystem();
                    for (int s = 1; s <= 12; s++) { At(w, s); sys.Execute(w, 1f); }
                    var sb = new System.Text.StringBuilder();
                    foreach (var u in units)
                    {
                        var d = w.GetComponentRO<UnitDetections>(u);
                        sb.Append(u.Index).Append(':');
                        for (int k = 0; k < d.Count; k++) sb.Append(d.Pairs[k].Target.Index).Append('=').Append(d.Pairs[k].Level.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                        var pool = w.GetComponentRO<SquadCognitiveState>(u).Contacts;
                        sb.Append('|').Append(pool.Count).Append(';');
                    }
                    return sb.ToString();
                }
                finally { Fdp.Core.Jobs.ParallelEntities.Enabled = was; Fdp.Core.Jobs.FdpWorkerPool.Resize(Fdp.Core.Jobs.FdpWorkerPool.Budget()); }
            }
            var serial = Run(false, 1);
            Assert.Contains("|", serial);
            Assert.Equal(serial, Run(true, 4));
            Assert.Equal(serial, Run(true, 8));
        }
    
        /// <summary>⭐ N-2 — the squad perception merge never wipes a STOCHASTIC unit's pool (it has no members to merge; its own sensing writes
        /// it). ✅ Red-proof: the guard in <c>SquadPerceptionMergeSystem.Run</c> removed ⇒ the pool is merged from the empty roster to 0 ⇒ red.</summary>
        [Fact]
        public void N2_ThePerceptionMerge_KeepsAStochasticUnitsPool()
        {
            using var w = World();
            w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
            var me = Unit(w, Vector3.Zero, ForceId.Friend);
            var enemy = Unit(w, new Vector3(0, 100, 0), ForceId.Hostile);
            w.AddComponent(me, new Fdp.Core.CommandHierarchy.UnitRoster());
            StochasticUnitSensorSystem.Report(w, me, enemy, new Vector3(0, 100, 0), 0.5f, (SimInstant)1.0);
            Assert.Equal(1, w.GetComponentRO<SquadCognitiveState>(me).Contacts.Count);
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(w, me, 1000u, 6u);
            Assert.Equal(1, w.GetComponentRO<SquadCognitiveState>(me).Contacts.Count);
            w.GetComponentRW<AggregateUnit>(me).Resolution = AggregateResolution.Physical;            // a physical unit merges from its men
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(w, me, 2000u, 6u);
            Assert.Equal(0, w.GetComponentRO<SquadCognitiveState>(me).Contacts.Count);
        }
    }
}
