using System;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser;
using Hrot.UI.Common.Facades;

namespace Hrot.Editor.Review;

/// <summary>
/// ⭐ R-3 / V3 (docs/UX/DESIGN_Review_Perspective.md §3 D8, §4 V3) — the editor's <see cref="IReplayTransport"/>: over <see cref="OwnPartReplay"/>, <b>the editor's ONE seek</b>.
/// Where the replay stands is <see cref="OwnPartReplay.CurrentFrame"/>; a seek and a step are <see cref="OwnPartReplay.SeekToFrame"/> (a cluster seek — the step back is the previous frame's
/// moment); play / pause / rate are the toolbar's own time control (the facade — the AAR segment does not draw them). ⛔ No second seek path.
/// </summary>
public sealed class OwnPartReplayTransport : IReplayTransport
{
    private readonly OwnPartReplay _replay;
    private readonly Func<ITimeTransportFacade?> _facadeOf;
    private ITimeTransportFacade? _facade => _facadeOf();

    /// <param name="replay">The editor's own-part replay.</param>
    /// <param name="facade">The toolbar's transport (play / pause / rate), read lazily (the host builds it after the windows); null ⇒ the transport reports paused at 1×.</param>
    /// <param name="history">The history ◀ ▶ walks; default a new tracker whose waypoint request seeks through <paramref name="replay"/>.</param>
    public OwnPartReplayTransport(OwnPartReplay replay, Func<ITimeTransportFacade?>? facade = null, PlaybackHistoryTracker? history = null)
    {
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _facadeOf = facade ?? (() => null);
        Waypoints = history ?? new PlaybackHistoryTracker();
        Waypoints.OnWaypointRequested += wp => Seek(wp.FrameIndex);
    }

    private Fdp.Core.FlightRecorder.PlaybackController? Playback => _replay.ScanContext(out _)?.Playback;

    public bool HasRecording => Playback is { TotalFrames: > 0 };
    public int CurrentFrame => (int)_replay.CurrentFrame();
    public int TotalFrames => Playback?.TotalFrames ?? 0;

    /// <summary>⭐ A cluster seek to <paramref name="frame"/> (<see cref="OwnPartReplay.SeekToFrame"/>); refused quietly outside a replay.</summary>
    public void Seek(int frame) => _replay.SeekToFrame(frame, "transport");

    /// <summary>⭐ Step ±1 = a seek to the neighbouring frame (clamped to the file): ONE seek path.</summary>
    public void Step(int delta)
    {
        int cur = CurrentFrame, total = TotalFrames;
        if (cur < 0 || total <= 0 || delta == 0) return;
        int target = cur + delta;
        if (target < 0 || target >= total) return;
        Seek(target);
    }

    public bool IsPlaying => _facade is { IsPaused: false };
    public void Play() { if (_facade is { IsPaused: true }) _facade.TogglePlayPause(); }
    public void Pause() { if (_facade is { IsPaused: false }) _facade.TogglePlayPause(); }
    public float Rate { get => _facade?.TimeScale ?? 1f; set => _facade?.SetTimeScale(value); }
    public bool CanPlay => _facade is { IsPlayPauseEnabled: true };
    public string? PlayDisabledReason => null;

    public PlaybackHistoryTracker Waypoints { get; }

    /// <summary>Wall seconds since the file's first frame (the AAR segment has no axis toggle).</summary>
    public double SecondsOf(int frame)
    {
        var pb = Playback;
        if (pb is null || pb.TotalFrames <= 0) return 0d;
        frame = Math.Clamp(frame, 0, pb.TotalFrames - 1);
        return (pb.GetFrameMetadata(frame).WallClockTicks - pb.GetFrameMetadata(0).WallClockTicks) / (double)TimeSpan.TicksPerSecond;
    }

    /// <summary>The current frame's wall ticks and seconds; the sim seconds are the replay position's (the replay's own clock), NaN when the host does not know them.</summary>
    public void FrameTimes(out long wallClockTicks, out double relativeWallSeconds, out double simSeconds)
    {
        wallClockTicks = 0; relativeWallSeconds = 0d; simSeconds = double.NaN;
        var pb = Playback;
        int cur = CurrentFrame;
        if (pb is null || pb.TotalFrames <= 0 || cur < 0) return;
        wallClockTicks = pb.GetFrameMetadata(cur).WallClockTicks;
        relativeWallSeconds = (wallClockTicks - pb.GetFrameMetadata(0).WallClockTicks) / (double)TimeSpan.TicksPerSecond;
        if (_replay.Position is { SimSeconds: { } sim }) simSeconds = sim;
    }

    public bool HasAxisToggle => false;
    public bool AxisIsSim => false;
    public bool HasSimAxis => false;
    public bool SimAxisActive => false;
    public void ToggleAxis() { }
}
