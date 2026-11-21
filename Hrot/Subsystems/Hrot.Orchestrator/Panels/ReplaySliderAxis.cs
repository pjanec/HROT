using System;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;
using Hrot.UI.Common.Facades;

namespace Hrot.Orchestrator.Panels;

/// <summary>
/// ⭐ CE-3330 E4 (DAR §8 row 24) — the cluster panel's replay slider on the SESSION'S AXIS (R-3058): Sim = seconds of the run's own clock
/// (pauses collapsed), Wall = seconds after the session's zero (pauses shown). Pure mapping, so it is testable headlessly; the panel only draws.
/// The seek is built by the ONE intent builder (<see cref="ReplaySessionIntents"/>) and encoded by the ONE wire encoding
/// (<see cref="SeekReplayPayloadDto.From"/>) — the panel has no facade (it sits on the Orchestrator's own cache and bus), but it adds no
/// second seek construction.
/// </summary>
internal static class ReplaySliderAxis
{
    /// <summary>The slider's current value on <paramref name="axis"/>. <paramref name="masterSimSeconds"/> is the cluster's sim clock.</summary>
    public static float Value(ReplaySession session, ReplayAxis axis, double masterSimSeconds)
    {
        if (axis == ReplayAxis.Sim || session.TimeMap is not { HasSimTime: true } map)
            return (float)masterSimSeconds;
        return (float)((map.WallAt(masterSimSeconds) - session.ZeroWallTicks) / (double)TimeSpan.TicksPerSecond);
    }

    /// <summary>The slider's upper bound on <paramref name="axis"/>: the session's length (wall seconds after the zero), as sim seconds on Sim.
    /// Falls back to <paramref name="fallbackSeconds"/> (the prepare result's duration) before a session has been heard.</summary>
    public static float Max(ReplaySession session, ReplayAxis axis, float fallbackSeconds)
    {
        if (!session.Active || session.LengthSeconds <= 0) return fallbackSeconds;
        if (axis == ReplayAxis.Wall) return (float)session.LengthSeconds;
        if (session.TimeMap is not { HasSimTime: true } map) return fallbackSeconds;
        var endWall = session.ZeroWallTicks + (long)Math.Round(session.LengthSeconds * TimeSpan.TicksPerSecond);
        return (float)map.SimAt(endWall);
    }

    /// <summary>The seek the slider sends for <paramref name="value"/> on <paramref name="axis"/>: Sim ⇒ the sim target, Wall ⇒ the wall seconds.</summary>
    public static SeekReplayPayloadDto Seek(ReplayAxis axis, float value)
    {
        var (wall, sim) = ReplaySessionIntents.OnAxis(axis, value);
        return SeekReplayPayloadDto.From(ReplaySessionIntents.Seek(wall, sim));
    }
}
