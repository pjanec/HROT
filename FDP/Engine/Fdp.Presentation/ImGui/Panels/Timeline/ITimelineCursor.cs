using System;
using Fdp.Toolkit.ReplayBrowser.Federation;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>⭐ <c>CE-3330</c> E2 (docs/DESIGN_Director_Timeline_Editor.md §2e) — what the host's cluster shows, as the panel's menu and marks need to
/// know it: a live run (enters a review from the menu), a review of it, a replay; None for every host that has no such session.</summary>
public enum TimelineMode { None, Live, Review, Replay }

/// <summary>
/// ⭐ CE-4325 (E7, docs/UX/DESIGN_Bookmarks_And_Notes.md §3) — one NOTE the ruler shows: an amber dot beside its mark's ⚑ (<paramref name="Mark"/> = the checkpoint label), or alone at its time (an AAR note, <paramref name="Mark"/> empty).
/// <paramref name="Title"/> / <paramref name="Text"/> / <paramref name="Detail"/> are the tooltip's three lines (name · time, the words, unit · who); the host words them, the panel only draws.
/// </summary>
public readonly record struct TimelineNote(long WallTicks, string Mark, string Title, string Text, string Detail);

/// <summary>
/// ⭐ CE-4325 (E7, docs/UX/DESIGN_Bookmarks_And_Notes.md §3, R-4177) — the two small icons at the TOP RIGHT of the Event Timeline's title bar, drawn by the window and bound by the host: <b>⚑</b> (mark now in Live · <i>Add note here</i> in an AAR)
/// and <b>S / W</b> (the sim / wall axis of the whole session — moved here from the main toolbar). ⛔ The window knows no run mode: every word, state and action is the binding's.
/// </summary>
public interface ITimelineCorner
{
    /// <summary>Whether the ⚑ is shown at all (a host with no marks: false).</summary>
    bool HasMark { get; }
    /// <summary>The ⚑'s tooltip — ⭐ already worded with the reason when it is greyed (the window draws it as is; a cached string, 0 B per frame).</summary>
    string MarkTip { get; }
    /// <summary>Why the ⚑ is greyed now (null = it acts).</summary>
    string? MarkDisabledReason { get; }
    /// <summary>The ⚑ was pressed.</summary>
    void OnMark();

    /// <summary>Whether the S / W toggle is shown.</summary>
    bool HasAxis { get; }
    /// <summary>"S" (sim) or "W" (wall).</summary>
    string AxisFace { get; }
    /// <summary>The toggle's tooltip.</summary>
    string AxisTip { get; }
    /// <summary>Whether the toggle acts now.</summary>
    bool AxisEnabled { get; }
    /// <summary>The toggle was pressed: S ⇄ W for the whole session.</summary>
    void OnToggleAxis();
}

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

    /// <summary>⭐ <c>CE-3330</c> E2 — the mode the host is in (§2e): the menu offers "Review from here" on Live, the seek items on Review / Replay.</summary>
    TimelineMode Mode => TimelineMode.None;

    /// <summary>⭐ <c>CE-3330</c> E2 — the review's live point (WALL ticks, like every cursor time; converted by <see cref="AxisCursor"/>); null outside a review.</summary>
    long? LivePoint => null;

    /// <summary>⭐ <c>CE-3330</c> E2 — the session's named checkpoints (their <c>WallTicks</c> are this cursor's ticks); drawn as flags on the ruler.</summary>
    System.Collections.Generic.IReadOnlyList<Fdp.Core.FlightRecorder.Metadata.CheckpointMark> Checkpoints
        => System.Array.Empty<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>();

    /// <summary>⭐ CE-4325 — the exercise's notes (wall ticks of THIS cursor), drawn as amber dots on the ruler; their tooltips are the host's words. Empty by default (a host with no notes).</summary>
    System.Collections.Generic.IReadOnlyList<TimelineNote> Notes => System.Array.Empty<TimelineNote>();

    /// <summary>⭐ <c>CE-3330</c> E2 — whether the host can enter a review of its live run (the menu's "Review from here").</summary>
    bool CanReview => false;

    /// <summary>⭐ <c>CE-3330</c> E2 — reviews the live run from the frame at / before <paramref name="wallTicks"/> (no-op when <see cref="CanReview"/> is false).</summary>
    void EnterReview(long wallTicks) { }

    /// <summary>⭐ <c>CE-3330</c> E5 (§2e) — the MASTER's loop of the open replay / review (WALL ticks, like every cursor time; converted by
    /// <see cref="AxisCursor"/>); null = none. A session setting every host draws (DAR §12n), ⛔ never the Replay Browser's own clock loop.</summary>
    (long From, long To)? Loop => null;

    /// <summary>⭐ <c>CE-3330</c> E5 — whether the host can set the session's loop (a review / replay on the cluster).</summary>
    bool CanLoop => false;

    /// <summary>⭐ <c>CE-3330</c> E5 — loops the session between the two WALL times (no-op when <see cref="CanLoop"/> is false).</summary>
    void SetLoop(long fromWall, long toWall) { }

    /// <summary>⭐ <c>CE-3330</c> E5 — clears the session's loop (no-op when <see cref="CanLoop"/> is false).</summary>
    void ClearLoop() { }

    /// <summary>
    /// ⭐ <c>CE-4097</c> part two (AQ108 D21) — the host's RUN-MODE items for the clicked moment (<paramref name="wallTicks"/>, this cursor's ticks; <paramref name="time"/> is its label):
    /// in Live <i>AAR from here</i>, in an AAR <i>Go live from here</i>, in any run <i>Take checkpoint here</i> — each changes mode at the moment, <b>without asking</b> (the user picked it). A
    /// host binds them to its own run choice; ⛔ the panel never switches on a mode. Default: none (the Replay Browser).
    /// </summary>
    System.Collections.Generic.IEnumerable<Fdp.Toolkit.ReplayBrowser.Timeline.TimelineMenuItem> ModeMenuItems(long wallTicks, string time)
        => System.Array.Empty<Fdp.Toolkit.ReplayBrowser.Timeline.TimelineMenuItem>();

    /// <summary>
    /// ⭐ A6 (Action Catalogue §9) — the same, knowing the clicked EVENT (its time in this cursor's ticks, its lane's display name; null = a moment,
    /// not an event). A host that composes its run-mode moves from its action catalogue overrides this one. Default: the moment's items.
    /// </summary>
    System.Collections.Generic.IEnumerable<Fdp.Toolkit.ReplayBrowser.Timeline.TimelineMenuItem> ModeMenuItems(long wallTicks, string time, long? eventTicks, string? eventLane)
        => ModeMenuItems(wallTicks, time);

    /// <summary>⭐ A6 — the host composes the <i>DAR from …</i> rows itself (with its other run-mode moves): the panel then draws none of its own. Default false.</summary>
    bool ComposesReviewItems => false;
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
    /// <param name="checkpoints">⭐ CE-1467 (a) — the recording's checkpoints (its ⚑ marks); null = none drawn.</param>
    /// <param name="notes">⭐ CE-1467 (a) — the recording's notes as the ruler draws them (read beside the files); null = none.</param>
    public FederatedReplayCursor(Func<FederatedReplayManager?> manager, Func<int> nodeId, Func<bool>? isPlaying = null, Action? togglePlay = null,
        Func<Fdp.Core.FlightRecorder.ReplayTimeMap?>? timeMap = null, Func<bool>? simAxis = null,
        Func<System.Collections.Generic.IReadOnlyList<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>>? checkpoints = null,
        Func<System.Collections.Generic.IReadOnlyList<TimelineNote>>? notes = null)
    {
        _checkpoints = checkpoints;
        _notes = notes;
        _manager = manager;
        _nodeId = nodeId;
        _isPlaying = isPlaying;
        _togglePlay = togglePlay;
        _timeMap = timeMap;
        _simAxis = simAxis;
    }

    private readonly Func<Fdp.Core.FlightRecorder.ReplayTimeMap?>? _timeMap;
    private readonly Func<bool>? _simAxis;
    private readonly Func<System.Collections.Generic.IReadOnlyList<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>>? _checkpoints;
    private readonly Func<System.Collections.Generic.IReadOnlyList<TimelineNote>>? _notes;

    /// <summary>⭐ CE-1467 (a) — the recording's ⚑ marks (the host's), else none.</summary>
    public System.Collections.Generic.IReadOnlyList<Fdp.Core.FlightRecorder.Metadata.CheckpointMark> Checkpoints
        => _checkpoints?.Invoke() ?? System.Array.Empty<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>();

    /// <summary>⭐ CE-1467 (a) — the recording's notes (read beside its files), else none.</summary>
    public System.Collections.Generic.IReadOnlyList<TimelineNote> Notes => _notes?.Invoke() ?? System.Array.Empty<TimelineNote>();
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
