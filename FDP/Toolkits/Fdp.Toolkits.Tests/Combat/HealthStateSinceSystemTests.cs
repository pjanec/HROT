using System;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4661</c> (thermal drivers TD-S5, TD-7; docs/designs/thermal/DESIGN_Thermal_Drivers.md §4h) — <see cref="Health.StateSince"/> is the time the DERIVED
    /// injury state last changed, stamped ONCE per change on the Health owner: wounded, then incapacitated, then dead each carry their own time; a body seen
    /// first already dead keeps the time it carries; a body whose Health another node owns is left to that node; and a dead body's thermal heat fades from it.
    /// </summary>
    public sealed class HealthStateSinceSystemTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private readonly HealthStateSinceSystem _sys = new();
        private double _t;

        public HealthStateSinceSystemTests()
        {
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<InjuryThresholds>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0 });
        }

        public void Dispose() => _w.Dispose();

        private Entity Man(float hp)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new Health { Current = hp, Max = 100f });
            _w.AddComponent(e, new InjuryThresholds { WoundedBelow = 0.7f, IncapacitatedBelow = 0.3f });
            return e;
        }

        private void Step(double seconds = 0.5)
        {
            _t += seconds;
            _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = _t });
            _sys.Execute(_w, (float)seconds);
        }

        private double Since(Entity e) => _w.GetComponentRO<Health>(e).StateSince;

        /// <summary>
        /// ⭐ Each change of the derived state is stamped once, with the time it was seen; damage inside one state stamps nothing; the time of death is the last.
        /// 🔴 Red-proof: stamp on first sight too ⇒ the loaded corpse's 0 becomes 0.5 ⇒ red; stamp on any Health change ⇒ the 60 → 50 step re-stamps ⇒ red.
        /// </summary>
        [Fact]
        public void CE4661_EachInjuryStateChange_IsStampedOnce_AndALoadedCorpseKeepsItsTime()
        {
            var man = Man(100f);
            var corpse = Man(0f);
            Step();                                                   // first sight: nothing
            Assert.Equal(0.0, Since(man));
            Assert.Equal(0.0, Since(corpse));

            _w.GetComponentRW<Health>(man).Current = 60f; Step();       // ok → wounded at 1.0
            Assert.Equal(1.0, Since(man));
            _w.GetComponentRW<Health>(man).Current = 50f; Step();       // still wounded: no stamp
            Assert.Equal(1.0, Since(man));
            _w.GetComponentRW<Health>(man).Current = 20f; Step();       // → incapacitated at 2.0
            Assert.Equal(2.0, Since(man));
            _w.GetComponentRW<Health>(man).Current = 0f; Step(); Step(); // → dead at 2.5, held
            Assert.Equal(2.5, Since(man));
            Assert.Equal(0.0, Since(corpse));
            Assert.Equal(3L, _sys.Stamped);
        }

        /// <summary>⭐ A man whose Health another node owns is never stamped here (his owner's time arrives in <c>EntityDamage</c>); and the steady frame allocates nothing.</summary>
        /// <summary>⭐ <c>CE-4670</c> (VA-S1) — the kill kind lasts while it is dead and is cleared the frame it lives again (an edit, a repair).
        /// 🔴 Red-proof: without the clear a repaired tank still reads Catastrophic.</summary>
        [Fact]
        public void CE4670_ABodyThatLivesAgain_LosesItsKillKind()
        {
            var wreck = Man(0f);
            _w.GetComponentRW<Health>(wreck).Destruction = DestructionKind.Catastrophic;
            Step(); Step();
            Assert.Equal(DestructionKind.Catastrophic, _w.GetComponentRO<Health>(wreck).Destruction);   // dead: kept
            _w.GetComponentRW<Health>(wreck).Current = 80f; Step();
            Assert.Equal(DestructionKind.None, _w.GetComponentRO<Health>(wreck).Destruction);
        }

        [Fact]
        public void CE4661_OnlyTheHealthOwnerStamps_AndTheSteadyFrameAllocatesNothing()
        {
            var remote = Man(100f);
            _w.AddComponent(remote, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            Step();
            _w.GetComponentRW<Health>(remote).Current = 0f;
            Step();
            Assert.Equal(0.0, Since(remote));

            for (int i = 0; i < 5; i++) Step();   // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) Step();
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>
        /// ⭐ TD-7's consumer: a dead body's heat fades from its time of death (<see cref="ThermalDriversOps.CorpseCoolPerSecond"/>); alive (NaN) it holds; an engine's
        /// Dead is untouched by it. 🔴 Red-proof: ignore <c>diedAt</c> in <c>SourceHeat</c> ⇒ the hour-old corpse is as warm as the fresh one ⇒ red.
        /// </summary>
        [Fact]
        public void CE4661_ADeadBody_CoolsFromItsTimeOfDeath()
        {
            var man = new ThermalState { BaseSignature = 0.4f, IdleHeat = 0.2f, RunningHeat = 0.7f, ReferenceSpeed = 3f, RunningHeatPerSecond = 0.05f, CooldownPerSecond = 0.02f };
            var standing = HeatSource.Of(HeatSourceState.Standing, (SimInstant)double.NegativeInfinity, (SimInstant)double.NegativeInfinity);
            float alive = ThermalDriversOps.SourceHeat(in man, in standing, 0f, 4000.0);
            float fresh = ThermalDriversOps.SourceHeat(in man, in standing, 0f, 4000.0, diedAt: 4000.0);
            float hourOld = ThermalDriversOps.SourceHeat(in man, in standing, 0f, 4000.0, diedAt: 400.0);
            Assert.Equal(alive, fresh, 4);
            Assert.True(hourOld < fresh * 0.2f, $"an hour-old corpse {hourOld} vs a fresh one {fresh}");

            var tank = new ThermalState { HasPowerPlant = true, BaseSignature = 0.2f, IdleHeat = 0.4f, RunningHeat = 0.8f, RunningHeatPerSecond = 0.01f, CooldownPerSecond = 0.002f };
            var dead = HeatSource.Of(HeatSourceState.Dead, (SimInstant)3900.0, (SimInstant)double.NegativeInfinity);
            Assert.Equal(ThermalDriversOps.SourceHeat(in tank, in dead, 0f, 4000.0), ThermalDriversOps.SourceHeat(in tank, in dead, 0f, 4000.0, diedAt: 400.0), 6);
        }
    }
}
