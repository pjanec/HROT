using System;
using System.Collections.Generic;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;

namespace Hrot.UI.Common.Adapters;

/// <summary>
/// ⭐⭐ <c>CE-3330</c> E2 (docs/DESIGN_Director_Timeline_Editor.md §2e) — the event timeline's cursor on a CLUSTER host (the editor; any host with
/// <see cref="ITimeTransportFacade"/>): live, review and replay through THE ONE facade — ⛔ no second seek or review path (R-3059). It REPLACES
/// the editor's old live-only cursor: its Live mode is that cursor's behaviour (the index's first → last frame, "now" at the live edge, no seek),
/// plus the entry to a review.
/// <para>All times are the recording's WALL ticks (TL-2/TL-4): <see cref="TimelineAxis"/> / <c>AxisCursor</c> convert for the Sim axis. The
/// session's own zero and map turn them into the facade's seconds on the session axis (<see cref="Seek"/>); from LIVE there is no session yet, so
/// a review is entered by the frame's SIM seconds (<see cref="ITimeTransportFacade.EnterReviewAtSim"/> — "the sim target wins").</para>
/// </summary>
public sealed class ClusterTimelineCursor : ITimelineCursor
{
    private const long Tps = TimeSpan.TicksPerSecond;

    private readonly Func<ITimeTransportFacade?> _facade;
    private readonly Func<ITimelineSource?> _source;

    private TimelineIndex? _mapFor;                      // the live index the live map was built from
    private ReplayTimeMap? _liveMap;
    private ReplayTimeMap? _axisFor;                     // the map the cached Sim axis is over
    private TimelineAxis _axis = TimelineAxis.Wall;

    /// <param name="facade">The host's ONE time facade (null before it exists).</param>
    /// <param name="source">The index the panel draws — ⭐ the SAME func the window hands the panel (the editor's returns null while a replay shows).</param>
    public ClusterTimelineCursor(Func<ITimeTransportFacade?> facade, Func<ITimelineSource?> source)
    {
        _facade = facade ?? throw new ArgumentNullException(nameof(facade));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    private TimelineIndex Index => _source()?.Index ?? TimelineIndex.Empty;

    private ReplaySession? ActiveSession => _facade()?.Session is { Active: true } s ? s : null;

    public TimelineMode Mode => _facade()?.Mode switch
    {
        TransportMode.Live => TimelineMode.Live,
        TransportMode.Review => TimelineMode.Review,
        TransportMode.Replay => TimelineMode.Replay,
        _ => TimelineMode.None,
    };

    // ── the extent and "now" ─────────────────────────────────────────────────────────────────────────────────

    public long ExtentFrom => Mode == TimelineMode.Replay && ActiveSession is { } s ? s.ZeroWallTicks : IndexFrom;
    public long ExtentTo => Mode == TimelineMode.Replay && ActiveSession is { } s ? s.ZeroWallTicks + (long)Math.Round(s.LengthSeconds * Tps) : IndexTo;

    private long IndexFrom => Index.FrameTicks is { Count: > 0 } t ? t[0] : 0;
    private long IndexTo => Index.FrameTicks is { Count: > 0 } t ? t[^1] : 0;

    /// <summary>
    /// Live / none: the live edge. Review / replay: the frame on screen — 📐 measured (2026-10-26): the cluster clock the facade reads is, in a
    /// review or replay, <b>wall seconds after the session's zero</b> (<c>ReferenceReplayLoadHandler</c> overwrites the seek's
    /// <c>RestoredTime.TotalTime</c> with exactly that, <c>ReplaySeekProcessManager</c> snaps the master clock to it, and
    /// <c>ReplayClockProcessManager</c> reads it back as <c>anchor + TotalTime</c>) — NOT sim seconds; so the playhead is the zero plus it.
    /// </summary>
    public long Now
    {
        get
        {
            var m = Mode;
            if (m is not (TimelineMode.Review or TimelineMode.Replay) || ActiveSession is not { } s || _facade() is not { } f) return ExtentTo;
            return Math.Clamp(s.ZeroWallTicks + (long)Math.Round(f.ClockSeconds * Tps), ExtentFrom, ExtentTo);   // ⭐ CE-3351 — the raw clock; TotalTime is sim now
        }
    }

    // ── seeking ──────────────────────────────────────────────────────────────────────────────────────────────

    public bool CanSeek => Mode is TimelineMode.Review or TimelineMode.Replay;

    public void Seek(long ticks)
    {
        if (!CanSeek || _facade() is not { } f || ActiveSession is not { } s) return;
        f.Seek(ToFacadeAxis(f, s, ticks));
    }

    /// <summary>⭐ The ONE wall → facade conversion (Seek and SetLoop share it): Sim = sim seconds through the session's map; Wall (or a map-less
    /// v6 session) = seconds after the session's zero.</summary>
    internal static double ToFacadeAxis(ITimeTransportFacade f, ReplaySession s, long wallTicks)
        => f.Axis == ReplayAxis.Sim && s.TimeMap is { HasSimTime: true } map
            ? map.SimAt(wallTicks)
            : (wallTicks - s.ZeroWallTicks) / (double)Tps;

    /// <summary>The inverse of <see cref="ToFacadeAxis"/>: a time on the facade's axis as wall ticks.</summary>
    private static long FromFacadeAxis(ITimeTransportFacade f, ReplaySession s, double t)
        => f.Axis == ReplayAxis.Sim && s.TimeMap is { HasSimTime: true } map
            ? map.WallAt(t)
            : s.ZeroWallTicks + (long)Math.Round(t * Tps);

    // ── the master's loop (E5, DAR §12n) ─────────────────────────────────────────────────────────────────────

    public bool CanLoop => Mode is TimelineMode.Review or TimelineMode.Replay;

    public (long From, long To)? Loop
        => CanLoop && _facade() is { } f && ActiveSession is { } s && f.Loop is { } l
            ? (FromFacadeAxis(f, s, l.From), FromFacadeAxis(f, s, l.To))
            : null;

    public void SetLoop(long fromWall, long toWall)
    {
        if (!CanLoop || _facade() is not { } f || ActiveSession is not { } s) return;
        f.SetLoop(ToFacadeAxis(f, s, fromWall), ToFacadeAxis(f, s, toWall));
    }

    public void ClearLoop()
    {
        if (CanLoop && _facade() is { } f) f.ClearLoop();
    }

    // ── review ───────────────────────────────────────────────────────────────────────────────────────────────

    public bool CanReview
    {
        get
        {
            if (Mode != TimelineMode.Live) return false;
            var index = Index;
            return index.FrameTicks.Count > 0 && index.FrameSim.Count == index.FrameTicks.Count;
        }
    }

    public void EnterReview(long wallTicks)
    {
        if (!CanReview || _facade() is not { } f) return;
        var index = Index;
        f.EnterReviewAtSim(index.FrameSim[index.FrameAt(wallTicks)]);   // the frame at / before the click
    }

    // ── ⭐ CE-4097 part two (AQ108 D21) — the run-mode items at the clicked moment, through the host's ONE run choice; none asks ───────────

    /// <summary>
    /// In Live: <i>AAR from here</i> (ends the live run; its recording opens at that frame's sim time — the same column <see cref="EnterReview"/> reads). In an AAR: <i>Go live from here</i>
    /// (seek, then the built branch). In any run: <i>Take checkpoint here</i> — ⚠ a checkpoint is a keyframe of the LIVE recorder, so it exists only at the live edge (Live); a past moment,
    /// a DAR and an AAR show the item greyed with the reason. The DAR item of Live is the panel's own (<see cref="EnterReview"/>).
    /// </summary>
    public IEnumerable<TimelineMenuItem> ModeMenuItems(long wallTicks, string time) => ModeMenuItems(wallTicks, time, null, null);

    /// <summary>⭐ A6 — this host composes the DAR rows with the other run-mode moves (the panel draws none of its own).</summary>
    public bool ComposesReviewItems => true;

    /// <summary>
    /// ⭐ A6 (📄 docs/UX/DESIGN_Action_Catalogue.md §9) — the run-mode moves at the clicked moment / event, COMPOSED from the action catalogue
    /// (<see cref="Hrot.Common.Actions.RunModeActions"/>: the declaration and the words, once) and BOUND here, through the host's ONE run choice:
    /// <i>DAR from {t}</i> / <i>from this event</i> (Live), <i>AAR from {t}</i> (Live), <i>Go live from {t}</i> (an AAR), <i>Take checkpoint here</i>
    /// (any run — a keyframe of the LIVE recorder, so only at the live edge; elsewhere greyed with the reason). None asks.
    /// </summary>
    public IEnumerable<TimelineMenuItem> ModeMenuItems(long wallTicks, string time, long? eventTicks, string? eventLane)
    {
        if (_facade() is not { RunChoice: { } choice } f) return Array.Empty<TimelineMenuItem>();
        var mode = Mode;
        var target = eventTicks is null ? Hrot.Common.Actions.ActionTarget.Moment : Hrot.Common.Actions.ActionTarget.Event;
        var ctx = Hrot.Common.Actions.ActionContext.ForMoment(target, RunModeBinding.ModeOf(f.Mode), time, wallTicks, eventLane);
        var rows = RunModeBinding.Rows(ctx, dto => Bind(dto.Id, choice, mode, wallTicks, eventTicks));
        var items = new List<TimelineMenuItem>(rows.Count);
        foreach (var r in rows) items.Add(new TimelineMenuItem(r.ShownWithIcon, r.Run, r.Why is null));
        return items;
    }

    private RunModeBinding.Binding? Bind(int id, RunModeChoice choice, TimelineMode mode, long wallTicks, long? eventTicks)
    {
        switch (id)
        {
            case Hrot.Common.Constants.GlobalActionIds.DarFromHere:
            case Hrot.Common.Constants.GlobalActionIds.DarFromEvent:
            {
                if (mode != TimelineMode.Live) return null;
                long at = id == Hrot.Common.Constants.GlobalActionIds.DarFromEvent && eventTicks is { } e ? e : wallTicks;
                var why = choice.DarDisabledReason ?? (CanReview ? null : "the timeline has no sim time yet");
                return new(() => EnterReview(at), why);
            }
            case Hrot.Common.Constants.GlobalActionIds.AarFromHere:
            {
                if (mode != TimelineMode.Live) return null;
                var index = Index;
                var why = choice.AarFromHereDisabledReason ?? (CanReview ? null : "the timeline has no sim time yet");
                return new(() => { if (CanReview) choice.AarFromHere(index.FrameSim[index.FrameAt(wallTicks)]); }, why);
            }
            case Hrot.Common.Constants.GlobalActionIds.GoLiveFromHere:
                return mode == TimelineMode.Replay ? new(() => choice.GoLiveFromHere(() => Seek(wallTicks)), choice.GoLiveFromHereDisabledReason) : null;   // a DAR goes back with ⏏
            case Hrot.Common.Constants.GlobalActionIds.TakeCheckpointHere:
            {
                if (mode is not (TimelineMode.Live or TimelineMode.Review or TimelineMode.Replay)) return null;
                var why = choice.CheckpointDisabledReason
                          ?? (mode == TimelineMode.Live && Math.Abs(wallTicks - Now) > Tps ? "a checkpoint captures the live state now — click the live edge" : null)
                          ?? (mode == TimelineMode.Review ? "go back to live to take one" : null);
                return new(choice.TakeCheckpoint, why);
            }
            default:
                return null;   // a run-mode move this surface does not offer (the playhead's)
        }
    }

    public long? LivePoint => ActiveSession is { IsReview: true, LivePointSeconds: { } l } s ? s.ZeroWallTicks + (long)Math.Round(l * Tps) : null;

    /// <summary>
    /// The session's marks. ⭐ CE-4325 — in LIVE too: the master carries the live run's checkpoints on the INACTIVE session (<c>ClusterMaster.LiveCheckpoints</c>), and a ⚑ taken with <i>B</i> must appear on every station's timeline at once.
    /// </summary>
    public IReadOnlyList<CheckpointMark> Checkpoints
        => ActiveSession?.Checkpoints ?? (Mode == TimelineMode.Live && _facade()?.Session is { } live ? live.Checkpoints : Array.Empty<CheckpointMark>());

    // ── ⭐ CE-4325 (E7) — the notes as the ruler draws them: rebuilt only when the master's list, the marks or the map CHANGED (reference compare — 0 B steady) ──

    private readonly Hrot.UI.Common.Marks.TimelineNotesCache _notes = new();   // ⭐ CE-1467 (a) — the ONE mapping, shared with the Replay Browser's cursor

    public IReadOnlyList<TimelineNote> Notes => _notes.Of(_facade()?.Notes, Checkpoints, ActiveSession?.TimeMap);

    // ── the axis ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The session's map in a review / replay; in live a map built from the live index's own sim column (cached per index snapshot).</summary>
    public ReplayTimeMap? TimeMap
    {
        get
        {
            switch (Mode)
            {
                case TimelineMode.Review:
                case TimelineMode.Replay:
                    return ActiveSession?.TimeMap;
                case TimelineMode.Live:
                    var index = Index;
                    if (index.FrameSim.Count == 0 || index.FrameSim.Count != index.FrameTicks.Count) return null;
                    if (!ReferenceEquals(index, _mapFor))
                    {
                        var wall = index.FrameTicks is SharedList<long> sl ? sl.Span : new ReadOnlySpan<long>(ToArray(index.FrameTicks));
                        _liveMap = ReplayTimeMap.FromFrames(wall, index.FrameSim.Span, new FrameFlags[wall.Length]);
                        _mapFor = index;
                    }
                    return _liveMap;
                default:
                    return null;
            }
        }
    }

    /// <summary>Sim over <see cref="TimeMap"/> when the facade's axis is Sim (one <see cref="TimelineAxis"/> per map, so the panel's projection cache holds), else Wall.</summary>
    public TimelineAxis Axis
    {
        get
        {
            var map = TimeMap;
            if (map == null || _facade()?.Axis != ReplayAxis.Sim) return TimelineAxis.Wall;
            if (!ReferenceEquals(map, _axisFor)) { _axis = TimelineAxis.Sim(map); _axisFor = map; }
            return _axis;
        }
    }

    // ── play / pause: the facade's ─────────────────────────────────────────────────────────────────────────────

    public bool IsPlaying => _facade() is { IsPaused: false };
    public bool CanPlay => _facade()?.IsUserPlayEnabled() == true;   // ⭐ CE-4097 part two (D22): Space is a USER control — from Edit it is the armed mode's ▶
    public void TogglePlay() => _facade()?.UserPlayPause();

    private static long[] ToArray(IReadOnlyList<long> list)
    {
        var a = new long[list.Count];
        for (int i = 0; i < a.Length; i++) a[i] = list[i];
        return a;
    }
}
