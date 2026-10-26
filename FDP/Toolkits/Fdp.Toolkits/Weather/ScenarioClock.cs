using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Weather
{
    /// <summary>
    /// ⭐ CE-3216 S1-0 (R-3012; 📄 docs/designs/weather/DESIGN_Weather_And_Time.md W-4, W-8, C-3) — the scenario's clock: the UTC
    /// time at sim time 0, i.e. the scenario START TIME. It lives on the one positionless World Environment entity (no
    /// <c>SimTransform</c>), is owned by its creator (the Brain that loaded the scenario) and replicates on <c>dtScenarioClock</c>.
    /// <para>⭐ W-8: the value is AUTHORED — saved exactly as it stands (the scenario serializer's default policy), ⛔ never filled
    /// from the wall clock and never rewritten at save. A runtime edit (the <c>"ScenarioStartUtcTicks"</c> attribute, applied by the
    /// owner) simply sets a new start time. ⛔ No day rate (W-4): <see cref="ScenarioTime.Utc"/> is start + sim time.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ScenarioClock)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct ScenarioClock
    {
        /// <summary>The scenario start time: UTC <see cref="DateTime.Ticks"/> at sim time 0.</summary>
        public long StartUtcTicks;

        /// <summary>The offset (minutes) a clock DISPLAY adds to UTC — the theatre's local time. Display only: nothing derives from it.</summary>
        public short DisplayUtcOffsetMinutes;

        /// <summary>
        /// ⭐ C-3 — the FIXED built-in start time a scenario without a World Environment gets at load: 21 June 2025, 06:00 UTC (a
        /// summer morning in daylight from the Atlantic to central Asia). ⛔ Never the wall clock (W-8): a load is reproducible.
        /// </summary>
        public static readonly DateTime DefaultStartUtc = new(2025, 6, 21, 6, 0, 0, DateTimeKind.Utc);

        /// <summary>The C-3 default clock.</summary>
        public static ScenarioClock Default => new() { StartUtcTicks = DefaultStartUtc.Ticks };

        /// <summary>The start time as a UTC <see cref="DateTime"/>.</summary>
        public DateTime StartUtc => new(StartUtcTicks, DateTimeKind.Utc);
    }

    /// <summary>
    /// ⭐ CE-3216 S1-0 — the time of day, a PURE function of replicated data on every node (W-4): <c>UTC = start + sim time</c>.
    /// Pause, time acceleration, rewind and replay follow the cluster clock for free; every node with the World Environment entity and
    /// the same sim time gets the same answer. ⛔ No system, no state.
    /// </summary>
    public static class ScenarioTime
    {
        /// <summary>The scenario clock of <paramref name="world"/> — false when no World Environment has arrived (yet).</summary>
        public static bool TryClock(ISimulationView world, out ScenarioClock clock)
        {
            if (world is EntityRepository r && !r.IsComponentTypeRegistered<ScenarioClock>()) { clock = default; return false; }
            foreach (var e in world.Query().With<ScenarioClock>().WithLifecycle(EntityLifecycle.All).Build())
            {
                clock = world.GetComponentRO<ScenarioClock>(e);
                return true;
            }
            clock = default;
            return false;
        }

        /// <summary>UTC at sim time <paramref name="simTimeSeconds"/> for <paramref name="clock"/>.</summary>
        public static DateTime Utc(in ScenarioClock clock, double simTimeSeconds)
            => new(clock.StartUtcTicks + (long)Math.Round(simTimeSeconds * TimeSpan.TicksPerSecond), DateTimeKind.Utc);

        /// <summary>
        /// UTC now in <paramref name="world"/> — its clock's start + its <see cref="GlobalTime.TotalTime"/>; without a World Environment
        /// the C-3 default start (so a consumer never has to branch on a scenario that predates the clock).
        /// </summary>
        public static DateTime Utc(EntityRepository world)
        {
            var clock = TryClock(world, out var c) ? c : ScenarioClock.Default;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            return Utc(in clock, now);
        }
    }
}
