using System;
using System.Collections.Generic;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>A paused stretch on the active axis: <see cref="From"/> = <see cref="To"/> on the Sim axis (collapsed to one instant).</summary>
public readonly record struct TimelinePause(long From, long To, bool Changed);

/// <summary>
/// ⭐ <c>CE-3330</c> T13 (docs/DESIGN_Replay_Time_Axis_And_Review.md §9b R10, §10b; Director Timeline §2c rule 0) — the timeline's TIME AXIS:
/// <b>Wall</b> (the recording's wall ticks, TL-4 — paused stretches are spans) or <b>Sim</b> (the recorded sim time through the session's
/// <see cref="ReplayTimeMap"/> — a pause collapses to one instant, its ◯ / ● tick). Pure: the panel projects the index, the extent, "now" and
/// every seek through it, so nothing else knows which axis is shown. Sim ticks are sim seconds × <see cref="TimeSpan.TicksPerSecond"/>.
/// </summary>
public sealed class TimelineAxis
{
    private readonly ReplayTimeMap? _map;
    private TimelineIndex? _projectedFrom;
    private TimelineIndex _projected = TimelineIndex.Empty;

    private TimelineAxis(ReplayTimeMap? map) => _map = map;

    /// <summary>The wall axis (the identity).</summary>
    public static readonly TimelineAxis Wall = new(null);

    /// <summary>The sim axis over <paramref name="map"/>; the wall axis when the recording has no sim time (format v6).</summary>
    public static TimelineAxis Sim(ReplayTimeMap map) => map is { HasSimTime: true } ? new TimelineAxis(map) : Wall;

    public bool IsSim => _map != null;
    public ReplayTimeMap? Map => _map;

    /// <summary>A wall time on this axis.</summary>
    public long ToAxis(long wallTicks) => _map == null ? wallTicks : (long)Math.Round(_map.SimAt(wallTicks) * TimeSpan.TicksPerSecond);

    /// <summary>This axis's time as wall ticks — a sim instant that is a pause resolves to its END (what changed during it is shown, R10).</summary>
    public long ToWall(long axisTicks) => _map == null ? axisTicks : _map.WallAt(axisTicks / (double)TimeSpan.TicksPerSecond);

    /// <summary>The paused stretches on this axis: spans on Wall, one instant each on Sim (none without a map).</summary>
    public List<TimelinePause> Pauses(ReplayTimeMap? map = null)
    {
        var m = map ?? _map;
        var list = new List<TimelinePause>();
        if (m == null) return list;
        foreach (var s in m.Stretches)
        {
            if (IsSim) { long at = (long)Math.Round(s.SimSeconds * TimeSpan.TicksPerSecond); list.Add(new TimelinePause(at, at, s.Changed)); }
            else list.Add(new TimelinePause(s.StartWallTicks, s.EndWallTicks, s.Changed));
        }
        return list;
    }

    /// <summary>
    /// <paramref name="index"/> with its frame times on this axis (the lanes shared, not copied) — cached for the last index, so a panel
    /// re-projects only when a new snapshot arrives. On Sim the times are non-decreasing (a pause repeats one), so <c>FrameAt</c> holds.
    /// </summary>
    public TimelineIndex Project(TimelineIndex index)
    {
        if (_map == null) return index;
        if (ReferenceEquals(index, _projectedFrom)) return _projected;
        var ticks = new long[index.FrameTicks.Count];
        for (int i = 0; i < ticks.Length; i++) ticks[i] = ToAxis(index.FrameTicks[i]);
        _projected = index.WithFrameTicks(ticks);   // ⭐ CE-1361 ② — the lanes shared as they are (a live index's are views of a grow-only buffer)
        _projectedFrom = index;
        return _projected;
    }
}

/// <summary>
/// ⭐ <c>CE-3330</c> T13 — a host's cursor seen on an axis: the extent, "now" and every seek converted, so the panel, its menu and its keys
/// work in the axis's ticks and the host keeps seeking by wall ticks (its own transport, TL-2).
/// </summary>
public sealed class AxisCursor : ITimelineCursor
{
    private readonly ITimelineCursor _inner;
    public TimelineAxis Axis { get; }

    public AxisCursor(ITimelineCursor inner, TimelineAxis axis) { _inner = inner; Axis = axis; }

    public long ExtentFrom => Axis.ToAxis(_inner.ExtentFrom);
    public long ExtentTo => Axis.ToAxis(_inner.ExtentTo);
    public long Now => Axis.ToAxis(_inner.Now);
    public bool CanSeek => _inner.CanSeek;
    public void Seek(long ticks) => _inner.Seek(Axis.ToWall(ticks));
    public bool IsPlaying => _inner.IsPlaying;
    public bool CanPlay => _inner.CanPlay;
    public void TogglePlay() => _inner.TogglePlay();

    // ⭐ CE-3330 E2 — the session layer: times convert to the axis, a review's entry converts back to the host's wall ticks
    public TimelineMode Mode => _inner.Mode;
    public bool CanReview => _inner.CanReview;
    public void EnterReview(long ticks) => _inner.EnterReview(Axis.ToWall(ticks));
    public long? LivePoint => _inner.LivePoint is { } l ? Axis.ToAxis(l) : null;

    private IReadOnlyList<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>? _marksFor;
    private IReadOnlyList<Fdp.Core.FlightRecorder.Metadata.CheckpointMark> _marks = Array.Empty<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>();

    /// <summary>The host's checkpoints with their <c>WallTicks</c> on the axis — projected once per list the host hands out.</summary>
    public IReadOnlyList<Fdp.Core.FlightRecorder.Metadata.CheckpointMark> Checkpoints
    {
        get
        {
            var inner = _inner.Checkpoints;
            if (ReferenceEquals(inner, _marksFor)) return _marks;
            var list = new Fdp.Core.FlightRecorder.Metadata.CheckpointMark[inner.Count];
            for (int i = 0; i < list.Length; i++)
                list[i] = new Fdp.Core.FlightRecorder.Metadata.CheckpointMark { Label = inner[i].Label, Frame = inner[i].Frame, WallTicks = Axis.ToAxis(inner[i].WallTicks), TotalTime = inner[i].TotalTime };
            _marksFor = inner;
            return _marks = list;
        }
    }
}
