using System;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.Orchestration;
using Hrot.Orchestrator.Panels;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐ CE-3330 E4 (DAR §8 row 24) — the cluster panel's replay slider on the session's axis: value, range and seek mean sim seconds on Sim
/// and seconds after the zero on Wall. Pure mapping (<see cref="ReplaySliderAxis"/>); the panel only draws it.
/// </summary>
public sealed class ReplaySliderAxisTests
{
    private const long Zero = 1000L * TimeSpan.TicksPerSecond;
    private static long At(double wallSeconds) => Zero + (long)(wallSeconds * TimeSpan.TicksPerSecond);

    /// <summary>A 30 s replay with an operator pause from wall 10 s to 20 s: sim runs 0→10, stands at 10, then 10→20.</summary>
    private static ReplaySession Session(bool active = true) => new()
    {
        Active = active,
        ZeroWallTicks = Zero,
        LengthSeconds = 30,
        TimeMap = new ReplayTimeMap(
            new[]
            {
                new ReplayTimeMap.Point(At(0), 0), new ReplayTimeMap.Point(At(10), 10),
                new ReplayTimeMap.Point(At(20), 10), new ReplayTimeMap.Point(At(30), 20),
            },
            Array.Empty<ReplayTimeMap.Stretch>()),
    };

    [Fact]
    public void Value_IsSimSecondsOnSim_AndSecondsAfterTheZeroOnWall()
    {
        Assert.Equal(15f, ReplaySliderAxis.Value(Session(), ReplayAxis.Sim, 15));
        Assert.Equal(25f, ReplaySliderAxis.Value(Session(), ReplayAxis.Wall, 15), 3);   // sim 15 is wall 25 s: the 10 s pause is shown
    }

    [Fact]
    public void Max_IsTheSimLengthOnSim_AndTheWallLengthOnWall_FallbackBeforeASession()
    {
        Assert.Equal(20f, ReplaySliderAxis.Max(Session(), ReplayAxis.Sim, 3600f), 3);
        Assert.Equal(30f, ReplaySliderAxis.Max(Session(), ReplayAxis.Wall, 3600f), 3);
        Assert.Equal(3600f, ReplaySliderAxis.Max(Session(active: false), ReplayAxis.Wall, 3600f));
    }

    [Fact]
    public void Seek_IsTheSimTargetOnSim_AndTheWallSecondsOnWall()
    {
        var sim = ReplaySliderAxis.Seek(ReplayAxis.Sim, 15f);
        Assert.Equal(15d, sim.TargetSimSeconds);
        Assert.Equal(0, sim.TargetWallTicks);

        var wall = ReplaySliderAxis.Seek(ReplayAxis.Wall, 25f);
        Assert.Null(wall.TargetSimSeconds);
        Assert.Equal(25 * TimeSpan.TicksPerSecond, wall.TargetWallTicks);
    }
}
