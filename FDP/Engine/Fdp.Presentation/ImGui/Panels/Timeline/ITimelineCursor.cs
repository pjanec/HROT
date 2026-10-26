using System;
using Fdp.Toolkit.ReplayBrowser.Federation;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>
/// ⭐ <c>CE-1302</c> (docs/DESIGN_Director_Timeline_Editor.md §9 TL-2) — how the timeline panel learns where time is and moves it. A
/// one-method adapter each host binds to ITS OWN transport — ⛔ never a second transport, no state of its own. All times are the
/// recording's wall-clock ticks (TL-4).
/// </summary>
public interface ITimelineCursor
{
    long ExtentFrom { get; }
    long ExtentTo { get; }
    long Now { get; }
    bool CanSeek { get; }
    void Seek(long ticks);

    /// <summary>⭐ <c>CE-3330</c> — whether time is running (the panel pages to keep the playhead in view, T12); false when the host cannot say.</summary>
    bool IsPlaying => false;

    /// <summary>⭐ <c>CE-3330</c> — whether this cursor can play / pause (Space, "Play from here"); a host binds it to its own transport.</summary>
    bool CanPlay => false;

    /// <summary>⭐ <c>CE-3330</c> — play / pause through the host's transport (no-op when <see cref="CanPlay"/> is false).</summary>
    void TogglePlay() { }

    /// <summary>⭐ <c>CE-3330</c> T13 (R-3058) — the axis the timeline is drawn on: the session's (a replay / review) or the viewer's (the
    /// Replay Browser). Wall by default.</summary>
    TimelineAxis Axis => TimelineAxis.Wall;

    /// <summary>⭐ <c>CE-3330</c> T13 — the session's wall ↔ sim map (its paused stretches are drawn on either axis); null when unknown.</summary>
    Fdp.Core.FlightRecorder.ReplayTimeMap? TimeMap => null;
}

/// <summary>
/// ⭐ <c>CE-1302</c> (§9 D1) — the Replay Browser's cursor: it fronts <see cref="FederatedReplayManager"/>, the path its own scrubber
/// takes (<c>SetBaseWallTicks</c> seeks every node). The index is one node's recording, so a seek subtracts that node's offset.
/// </summary>
public sealed class FederatedReplayCursor : ITimelineCursor
{
    private readonly Func<FederatedReplayManager?> _manager;
    private readonly Func<int> _nodeId;

    private readonly Func<bool>? _isPlaying;
    private readonly Action? _togglePlay;

    /// <param name="isPlaying">⭐ <c>CE-3330</c> — the host's transport says whether the replay plays (null = never).</param>
    /// <param name="togglePlay">⭐ <c>CE-3330</c> — the host's play / pause (null = the panel offers none).</param>
    /// <param name="timeMap">⭐ <c>CE-3330</c> T13 — the loaded group's wall ↔ sim map (null = none).</param>
    /// <param name="simAxis">⭐ <c>CE-3330</c> T13 — whether the viewer's axis is Sim (the Replay Browser keeps its own, R-3058).</param>
    public FederatedReplayCursor(Func<FederatedReplayManager?> manager, Func<int> nodeId, Func<bool>? isPlaying = null, Action? togglePlay = null,
        Func<Fdp.Core.FlightRecorder.ReplayTimeMap?>? timeMap = null, Func<bool>? simAxis = null)
    {
        _manager = manager;
        _nodeId = nodeId;
        _isPlaying = isPlaying;
        _togglePlay = togglePlay;
        _timeMap = timeMap;
        _simAxis = simAxis;
    }

    private readonly Func<Fdp.Core.FlightRecorder.ReplayTimeMap?>? _timeMap;
    private readonly Func<bool>? _simAxis;
    private Fdp.Core.FlightRecorder.ReplayTimeMap? _axisFor;
    private TimelineAxis _axis = TimelineAxis.Wall;

    public Fdp.Core.FlightRecorder.ReplayTimeMap? TimeMap => _timeMap?.Invoke();

    /// <summary>The viewer's axis over the group's map — one <see cref="TimelineAxis"/> per map, so the panel's projection cache holds.</summary>
    public TimelineAxis Axis
    {
        get
        {
            var map = TimeMap;
            if (map == null || _simAxis?.Invoke() != true) return TimelineAxis.Wall;
            if (!ReferenceEquals(map, _axisFor)) { _axis = TimelineAxis.Sim(map); _axisFor = map; }
            return _axis;
        }
    }

    public bool IsPlaying => _isPlaying?.Invoke() ?? false;
    public bool CanPlay => _togglePlay != null && Playback != null;
    public void TogglePlay() => _togglePlay?.Invoke();

    private Fdp.Core.FlightRecorder.PlaybackController? Playback =>
        _manager() is { } m && m.Contexts.TryGetValue(_nodeId(), out var ctx) ? ctx.Playback : null;

    public long ExtentFrom => Playback is { TotalFrames: > 0 } p ? p.GetFrameMetadata(0).WallClockTicks : 0;
    public long ExtentTo => Playback is { TotalFrames: > 0 } p ? p.GetFrameMetadata(p.TotalFrames - 1).WallClockTicks : 0;

    public long Now
    {
        get
        {
            var m = _manager();
            if (m == null || !m.Contexts.TryGetValue(_nodeId(), out var ctx) || ctx.Playback == null) return 0;
            return ctx.CurrentFrame >= 0 ? ctx.Playback.GetFrameMetadata(ctx.CurrentFrame).WallClockTicks : ExtentFrom;
        }
    }

    public bool CanSeek => Playback != null;

    public void Seek(long ticks)
    {
        var m = _manager();
        if (m == null || Playback == null) return;
        long offset = m.NodeOffsets.TryGetValue(_nodeId(), out long o) ? o : 0L;
        m.SetBaseWallTicks(ticks - offset);
    }
}

/// <summary>
/// ⭐ <c>CE-3330</c> (DAR row 28; docs/DESIGN_Director_Timeline_Editor.md §2d) — a LIVE host's cursor over its <c>LiveTimelineSource</c>:
/// the extent is the index's first → last frame, "now" its last (the live edge), and it never seeks — ⭐ a live host seeks by ENTERING A
/// REVIEW (R-3059), which waits on backend's <c>EnterReview</c>; until then the panel offers no seek on a live host and its arrows move the
/// selection. Play / pause go through the host's own transport (Space).
/// </summary>
public sealed class LiveTimelineCursor : ITimelineCursor
{
    private readonly Func<Fdp.Toolkit.ReplayBrowser.Timeline.ITimelineSource?> _source;
    private readonly Func<bool>? _isPlaying;
    private readonly Action? _togglePlay;

    public LiveTimelineCursor(Func<Fdp.Toolkit.ReplayBrowser.Timeline.ITimelineSource?> source, Func<bool>? isPlaying = null, Action? togglePlay = null)
    {
        _source = source;
        _isPlaying = isPlaying;
        _togglePlay = togglePlay;
    }

    private System.Collections.Generic.IReadOnlyList<long> Ticks => _source()?.Index.FrameTicks ?? Array.Empty<long>();
    public long ExtentFrom => Ticks.Count > 0 ? Ticks[0] : 0;
    public long ExtentTo => Ticks.Count > 0 ? Ticks[^1] : 0;
    public long Now => ExtentTo;
    public bool CanSeek => false;
    public void Seek(long ticks) { }
    public bool IsPlaying => _isPlaying?.Invoke() ?? false;
    public bool CanPlay => _togglePlay != null;
    public void TogglePlay() => _togglePlay?.Invoke();
}
