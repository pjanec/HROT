using System;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;

namespace Hrot.Editor.Review;

/// <summary>
/// ⭐ R-5 / V6 (docs/UX/DESIGN_Review_Perspective.md §3 D8) — <b><see cref="IReplayTransport"/> over the review</b> (⭐ CE-4498: its model and commands): the ONE AAR transport's implementation for an open participant group.
/// <list type="bullet">
/// <item><b>Merged</b> — frames are the primary participant's; a seek / step drives the manager (every sandbox seeks, the merge is brought up to date in the world). ⭐ G-7 S4 (M-7): ▶ PLAYS —
/// the toolbar's ▶ and rate advance every participant, one step per recorded frame, each merged through the same path a step takes (<see cref="ReviewSource.Update"/>); the merge is incremental
/// (<c>BinaryMerger</c>, 0 B per steady frame). ⛔ SUPERSEDED: ▶ greyed with a reason because a merge was a JSON round-trip.</item>
/// <item><b>a part</b> — that file's frames; a seek restores from its keyframe, a step applies the next frame; ▶ plays it, following the toolbar's play state and rate (the facade), as an own-part AAR does.</item>
/// </list>
/// </summary>
public sealed class ReviewSourceTransport : IReplayTransport
{
    // ⭐ CE-4498 R-4 (docs/DESIGN_Threaded_Review_Source.md §2) — over the review's MODEL and COMMANDS, never the source: the same transport serves the switch-off editor (inline channel)
    //   and mode 3 (threaded channel — a seek / step is a sim job, the frame shown is the newest published model). ▶ and the rate are the toolbar's (the UI's time facade).
    private readonly IReviewChannel _review;
    private readonly Func<Hrot.UI.Common.Facades.ITimeTransportFacade?> _facade;

    public ReviewSourceTransport(IReviewChannel review, Func<Hrot.UI.Common.Facades.ITimeTransportFacade?>? facade = null)
    {
        _review = review ?? throw new ArgumentNullException(nameof(review));
        _facade = facade ?? (() => null);
        Waypoints = new PlaybackHistoryTracker();
        Waypoints.OnWaypointRequested += wp => Seek(wp.FrameIndex);
    }

    private ReviewReadModel Model => _review.Model;

    public bool HasRecording => Model.IsOpen && Model.TotalFrames > 0;
    public int CurrentFrame => Model.IsOpen ? Model.CurrentFrame : -1;
    public int TotalFrames => Model.IsOpen ? Model.TotalFrames : 0;

    public void Seek(int frame) => _review.SeekFrame(frame);
    public void Step(int delta) => _review.StepFrame(delta);

    private bool IsOpen => Model.Kind != ReviewKind.None && Model.IsOpen;
    public bool IsPlaying => IsOpen && _facade() is { IsPaused: false };
    public void Play() { }   // ▶ is the toolbar's (the time control, ui's): the source follows it — see ReviewSource.Update
    public void Pause() { }
    public float Rate { get => _facade()?.TimeScale ?? 1f; set { } }

    /// <summary>A part plays and so does Merged (the toolbar's ▶ is enabled) — ⭐ M-7: no reason is left to give.</summary>
    public bool CanPlay => IsOpen && _facade() is { IsPlayPauseEnabled: true };
    public string? PlayDisabledReason => null;

    public PlaybackHistoryTracker Waypoints { get; }

    public double SecondsOf(int frame)
    {
        var m = Model;
        if (!m.IsOpen || m.TotalFrames <= 0) return 0d;
        frame = Math.Clamp(frame, 0, m.TotalFrames - 1);
        return (m.WallTicksAt(frame) - m.WallTicksAt(0)) / (double)TimeSpan.TicksPerSecond;   // the active file's frames: in a part the part IS the primary (SelectPart sets it)
    }

    public void FrameTimes(out long wallClockTicks, out double relativeWallSeconds, out double simSeconds)
    {
        wallClockTicks = 0; relativeWallSeconds = 0d; simSeconds = double.NaN;
        var m = Model;
        int cur = CurrentFrame;
        if (!m.IsOpen || m.TotalFrames <= 0 || cur < 0) return;
        wallClockTicks = m.WallTicksAt(cur);
        relativeWallSeconds = (wallClockTicks - m.WallTicksAt(0)) / (double)TimeSpan.TicksPerSecond;
        simSeconds = m.SimSecondsAt(cur);
    }

    public bool HasAxisToggle => false;
    public bool AxisIsSim => false;
    public bool HasSimAxis => false;
    public bool SimAxisActive => false;
    public void ToggleAxis() { }
}
