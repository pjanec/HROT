using System;
using System.Globalization;
using Fdp.Core;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Weather;

namespace Hrot.UI.Common.Weather;

/// <summary>
/// ⭐ CE-1161 (docs/designs/weather/DESIGN_Weather_And_Time.md §7, E-2) — reads and edits the scenario's START TIME on the World
/// Environment (<see cref="ScenarioClock"/>, W-8: an authored value, saved as is, never the wall clock).
/// <para>⭐ The owner writes directly (the CE-1047 rule, <c>MapCameraLayer</c>); any other node asks the owner with the
/// <c>"ScenarioStartUtcTicks"</c> / <c>"ScenarioUtcOffsetMinutes"</c> attribute patch. 📐 Measured: the patch is only PUBLISHED — the
/// networkless editor has no system that applies it (<c>OfflineNetworkFactory.CreateSimHostAttributeUpdateSystems</c> is empty), so
/// a patch-only edit would silently do nothing there.</para>
/// </summary>
public static class ScenarioClockEdit
{
    /// <summary>What a view shows: the World Environment, its clock, whether this node writes it directly.</summary>
    public readonly record struct State(Entity Environment, ScenarioClock Clock, bool Direct)
    {
        public DateTime StartUtc => Clock.StartUtc;
        public DateTime StartLocal => Clock.StartUtc.AddMinutes(Clock.DisplayUtcOffsetMinutes);
    }

    /// <summary>The World Environment of <paramref name="world"/> and its clock; false before one exists.</summary>
    public static bool TryRead(EntityRepository? world, out State state)
    {
        state = default;
        if (world == null || !world.IsComponentTypeRegistered<ScenarioClock>()) return false;
        foreach (var e in world.Query().With<ScenarioClock>().WithLifecycle(EntityLifecycle.All).Build())
        {
            if (!world.IsAlive(e)) continue;
            state = new State(e, world.GetComponentRO<ScenarioClock>(e), world.HasAuthority(e));
            return true;
        }
        return false;
    }

    /// <summary>Sets the scenario start time (UTC). True when written here or the request was sent to the owner.</summary>
    public static bool SetStartUtc(EntityRepository world, DateTime utc)
    {
        if (!TryRead(world, out var s)) return false;
        long ticks = DateTime.SpecifyKind(utc, DateTimeKind.Utc).Ticks;
        if (s.Direct)
        {
            var clock = s.Clock;
            clock.StartUtcTicks = ticks;
            world.SetComponent(s.Environment, clock);
            return true;
        }
        return EntityAttributeChangeRequests.TryPublishJsonPatch(world, s.Environment,
            "{\"ScenarioStartUtcTicks\":" + ticks.ToString(CultureInfo.InvariantCulture) + "}");
    }

    /// <summary>Sets the display offset (minutes from UTC to the theatre's local time). Display only (nothing derives from it).</summary>
    public static bool SetOffsetMinutes(EntityRepository world, int minutes)
    {
        if (!TryRead(world, out var s)) return false;
        minutes = Math.Clamp(minutes, -14 * 60, 14 * 60);
        if (s.Direct)
        {
            var clock = s.Clock;
            clock.DisplayUtcOffsetMinutes = (short)minutes;
            world.SetComponent(s.Environment, clock);
            return true;
        }
        return EntityAttributeChangeRequests.TryPublishJsonPatch(world, s.Environment,
            "{\"ScenarioUtcOffsetMinutes\":" + minutes.ToString(CultureInfo.InvariantCulture) + "}");
    }

    /// <summary>The text format the view edits ("yyyy-MM-dd HH:mm", UTC).</summary>
    public const string Format = "yyyy-MM-dd HH:mm";

    /// <summary>Parses the view's text as a UTC time; false for anything that is not exactly <see cref="Format"/>.</summary>
    public static bool TryParse(string text, out DateTime utc)
    {
        bool ok = DateTime.TryParseExact(text?.Trim(), Format, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
        if (ok) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return ok;
    }
}
