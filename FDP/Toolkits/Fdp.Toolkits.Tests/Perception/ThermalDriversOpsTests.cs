using System;
using Fdp.Core;
using Fdp.Toolkit.Perception.Signatures;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4657</c> (thermal drivers TD-S1; docs/designs/thermal/DESIGN_Thermal_Drivers.md §4) — THE calculator in closed form: an entity's heat from its replicated
    /// <see cref="HeatSource"/> (the decision and its times) and the speed every node has, a barrel's heat from its last shot. Every node computes the same value at the
    /// same instant without integrating frames (a late joiner included), and it allocates nothing.
    /// </summary>
    public class ThermalDriversOpsTests
    {
        private static readonly double Never = double.NegativeInfinity;

        private static ThermalState Tank() => new()
        {
            BaseSignature = 0.3f, RunningHeatPerSecond = 0.05f, CooldownPerSecond = 0.02f, ReferenceSpeed = 5f, IdleHeat = 0.35f, RunningHeat = 0.8f, HasPowerPlant = true,
        };

        private static ThermalState Man() => new()
        {
            BaseSignature = 0.4f, RunningHeatPerSecond = 0.05f, CooldownPerSecond = 0.02f, ReferenceSpeed = 3f, IdleHeat = 0.2f, RunningHeat = 0.7f,
        };

        private static float Sig(in ThermalState t, HeatSourceState s, double since, double motion, float speed, double now)
            => ThermalDriversOps.Signature(in t, HeatSource.Of(s, (SimInstant)since, (SimInstant)motion), speed, now);

        /// <summary>⭐ An engine that never ran reads its base; one idling a while reads warmer; one that switched off cools back toward the base.</summary>
        [Fact]
        public void CE4657_AnIdlingEngine_IsWarm_AParkedColdOne_IsTheBase_AndOneSwitchedOff_Cools()
        {
            var tank = Tank();
            Assert.Equal(0.3f, Sig(in tank, HeatSourceState.Off, Never, Never, 0f, 1000.0), 4);
            float idling = Sig(in tank, HeatSourceState.Idle, 0.0, Never, 0f, 600.0);
            Assert.True(idling > 0.5f, $"an engine idling 10 min reads warm: {idling}");
            float justOff = Sig(in tank, HeatSourceState.Off, 600.0, Never, 0f, 601.0);
            float longOff = Sig(in tank, HeatSourceState.Off, 600.0, Never, 0f, 900.0);
            Assert.True(justOff > longOff && longOff > 0.3f && longOff < 0.31f, $"{justOff} > {longOff} → the base");
        }

        /// <summary>⭐ Running is warmer than idling at the same age, and a faster vehicle is warmer than a slow one.</summary>
        [Fact]
        public void CE4657_ARunningEngine_WarmsByItsLoad()
        {
            var tank = Tank();
            float idle = Sig(in tank, HeatSourceState.Idle, 0.0, Never, 0f, 300.0);
            float slow = Sig(in tank, HeatSourceState.Running, 0.0, 0.0, 1f, 300.0);
            float fast = Sig(in tank, HeatSourceState.Running, 0.0, 0.0, 5f, 300.0);
            Assert.True(fast > slow && slow > idle, $"{fast} > {slow} > {idle}");
        }

        /// <summary>⭐ TD-S2's body case (R-205): a man who just stopped running reads warmer than one who stood all along; a minute's rest later he cools back.</summary>
        [Fact]
        public void CE4657_AManWhoJustRan_ReadsWarmerThanOneWhoStood()
        {
            var man = Man();
            float stood = Sig(in man, HeatSourceState.Standing, 0.0, Never, 0f, 600.0);
            float justRan = Sig(in man, HeatSourceState.Standing, 590.0, 590.0, 0f, 600.0);
            float rested = Sig(in man, HeatSourceState.Standing, 300.0, 300.0, 0f, 600.0);
            Assert.True(justRan > stood + 0.2f, $"{justRan} vs {stood}");
            Assert.True(rested < justRan && rested - stood < 0.01f, $"{rested}");
            float running = Sig(in man, HeatSourceState.Moving, 0.0, 0.0, 3f, 120.0);
            Assert.True(running > stood, $"{running}");
        }

        /// <summary>⭐ The sensor sees the hottest source: a burning wreck or a hot barrel lifts a cold entity's signature.</summary>
        [Fact]
        public void CE4657_TheSensorSeesTheHottestSource_FireAndBarrel()
        {
            var tank = Tank();
            var off = HeatSource.Of(HeatSourceState.Dead, (SimInstant)Never, (SimInstant)Never);
            Assert.Equal(0.3f, ThermalDriversOps.Signature(in tank, off, 0f, 100.0), 4);
            Assert.Equal(1f, ThermalDriversOps.Signature(in tank, off, 0f, 100.0, fire: 1f), 4);
            Assert.Equal(0.3f + 0.5f * 0.7f, ThermalDriversOps.Signature(in tank, off, 0f, 100.0, barrelHeat: 0.5f), 4);
        }

        /// <summary>⭐ The barrel rule in closed form matches the per-frame one (<c>Combat.BarrelHeat.Tick</c>: linear cooling): 0.9 cooling at 0.05/s reads 0.4 ten
        /// seconds later, 0 once cooled, 0 before the shot.</summary>
        [Fact]
        public void CE4657_BarrelHeat_CoolsLinearlyFromTheLastShot()
        {
            Assert.Equal(0.4f, ThermalDriversOps.BarrelHeat(0.9f, 100.0, 0.05f, 110.0), 4);
            Assert.Equal(0f, ThermalDriversOps.BarrelHeat(0.9f, 100.0, 0.05f, 200.0));
            Assert.Equal(0f, ThermalDriversOps.BarrelHeat(0.9f, 100.0, 0.05f, 90.0));
            Assert.Equal(0f, ThermalDriversOps.BarrelHeat(0.9f, Never, 0.05f, 90.0));
        }

        /// <summary>⭐ 0 B: the calculator allocates nothing (CLAUDE.md hot path).</summary>
        [Fact]
        public void CE4657_TheCalculator_AllocatesNothing()
        {
            var tank = Tank();
            var src = HeatSource.Of(HeatSourceState.Running, (SimInstant)0.0, (SimInstant)0.0);
            float sink = ThermalDriversOps.Signature(in tank, src, 3f, 10.0) + ThermalDriversOps.BarrelHeat(0.5f, 1.0, 0.1f, 2.0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                sink += ThermalDriversOps.Signature(in tank, src, 3f, 10.0 + i) + ThermalDriversOps.BarrelHeat(0.5f, 1.0, 0.1f, 2.0 + i);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(sink > 0f);
        }
    }
}
