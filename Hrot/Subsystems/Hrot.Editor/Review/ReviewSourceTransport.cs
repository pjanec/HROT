using System;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser;

namespace Hrot.Editor.Review;

/// <summary>
/// ⭐ R-5 / V6 (docs/UX/DESIGN_Review_Perspective.md §3 D8) — <b><see cref="IReplayTransport"/> over a <see cref="ReviewSource"/></b>: the ONE AAR transport's implementation for an open participant group.
/// <list type="bullet">
/// <item><b>Merged</b> — frames are the primary participant's; a seek / step drives the manager (every sandbox seeks, the merge is brought up to date in the world). ⭐ G-7 S4 (M-7): ▶ PLAYS —
/// the toolbar's ▶ and rate advance every participant, one step per recorded frame, each merged through the same path a step takes (<see cref="ReviewSource.Update"/>); the merge is incremental
/// (<c>BinaryMerger</c>, 0 B per steady frame). ⛔ SUPERSEDED: ▶ greyed with a reason because a merge was a JSON round-trip.</item>
/// <item><b>a part</b> — that file's frames; a seek restores from its keyframe, a step applies the next frame; ▶ plays it, following the toolbar's play state and rate (the facade), as an own-part AAR does.</item>
/// </list>
/// </summary>
public sealed class ReviewSourceTransport : IReplayTransport
{
    private readonly ReviewSource _source;

    public ReviewSourceTransport(ReviewSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Waypoints = new PlaybackHistoryTracker();
        Waypoints.OnWaypointRequested += wp => Seek(wp.FrameIndex);
    }

    public bool HasRecording => _source.HasRecording;
    public int CurrentFrame => _source.CurrentFrame;
    public int TotalFrames => _source.TotalFrames;

    public void Seek(int frame) => _source.SeekFrame(frame);
    public void Step(int delta) => _source.StepFrame(delta);

    private bool IsOpen => _source.Kind != ReviewSourceKind.None;
    public bool IsPlaying => IsOpen && _source.Facade is { IsPaused: false };
    public void Play() { }   // ▶ is the toolbar's (the time control, ui's): a part follows it — see ReviewSource.Update
    public void Pause() { }
    public float Rate { get => _source.Facade?.TimeScale ?? 1f; set { } }

    /// <summary>A part plays and so does Merged (the toolbar's ▶ is enabled) — ⭐ M-7: no reason is left to give.</summary>
    public bool CanPlay => IsOpen && _source.Facade is { IsPlayPauseEnabled: true };
    public string? PlayDisabledReason => null;

    public PlaybackHistoryTracker Waypoints { get; }

    public double SecondsOf(int frame)
    {
        var pb = _source.ActivePlayback;
        if (pb is null || pb.TotalFrames <= 0) return 0d;
        frame = Math.Clamp(frame, 0, pb.TotalFrames - 1);
        return (pb.GetFrameMetadata(frame).WallClockTicks - pb.GetFrameMetadata(0).WallClockTicks) / (double)TimeSpan.TicksPerSecond;
    }

    public void FrameTimes(out long wallClockTicks, out double relativeWallSeconds, out double simSeconds)
    {
        wallClockTicks = 0; relativeWallSeconds = 0d; simSeconds = double.NaN;
        var pb = _source.ActivePlayback;
        int cur = CurrentFrame;
        if (pb is null || pb.TotalFrames <= 0 || cur < 0) return;
        var meta = pb.GetFrameMetadata(cur);
        wallClockTicks = meta.WallClockTicks;
        relativeWallSeconds = (meta.WallClockTicks - pb.GetFrameMetadata(0).WallClockTicks) / (double)TimeSpan.TicksPerSecond;
        if (meta.SimTime > 0 || cur == 0) simSeconds = meta.SimTime;
    }

    public bool HasAxisToggle => false;
    public bool AxisIsSim => false;
    public bool HasSimAxis => false;
    public bool SimAxisActive => false;
    public void ToggleAxis() { }
}
