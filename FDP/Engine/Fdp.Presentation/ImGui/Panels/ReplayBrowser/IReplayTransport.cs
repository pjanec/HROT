using System;
using Fdp.Toolkit.ReplayBrowser;

namespace Fdp.Presentation.Panels.ReplayBrowser;

/// <summary>
/// ⭐ R-3 / V3 (docs/UX/DESIGN_Review_Perspective.md §3 D8, §4 V3) — <b>the ONE AAR transport seam</b>: what the replay transport's ONE drawer
/// (<see cref="ReplayTransportDrawer"/>) needs from a host — where the replay stands, the seek, the step, play / pause, the rate, the history, and the axis inputs the
/// seek bar takes. The drawer calls ONLY this. Implementations: the Replay Browser's panel (over its federated manager — today's behaviour) and the editor's
/// <c>OwnPartReplay</c> transport (a cluster seek). ⛔ No second drawer, no second seek.
/// </summary>
public interface IReplayTransport
{
    /// <summary>A recording is loaded.</summary>
    bool HasRecording { get; }
    /// <summary>The shown frame; -1 when none is current.</summary>
    int CurrentFrame { get; }
    /// <summary>The recording's frame count (0 when none).</summary>
    int TotalFrames { get; }

    /// <summary>Jumps to <paramref name="frame"/> (clamped by the host).</summary>
    void Seek(int frame);
    /// <summary>One frame forward (+1) or back (-1); a no-op at the ends.</summary>
    void Step(int delta);

    /// <summary>Continuous playback is running.</summary>
    bool IsPlaying { get; }
    void Play();
    void Pause();
    /// <summary>The playback rate (× real time).</summary>
    float Rate { get; set; }
    /// <summary>Play / pause can act now.</summary>
    bool CanPlay { get; }
    /// <summary>Why play is unavailable when the reason is not simply "no recording" (the Replay Browser's Merged View); null otherwise.</summary>
    string? PlayDisabledReason { get; }

    /// <summary>The history ◀ ▶ walks. ⭐ The drawer records a waypoint on every JUMP made through the seam (a scrub's start, a rewind) — a step is not one.</summary>
    PlaybackHistoryTracker Waypoints { get; }

    /// <summary>The seconds of <paramref name="frame"/> on the active axis (the seek bar's labels).</summary>
    double SecondsOf(int frame);
    /// <summary>The current frame's wall ticks, wall seconds since the first frame and sim seconds (NaN sim = unknown: the tooltip omits it).</summary>
    void FrameTimes(out long wallClockTicks, out double relativeWallSeconds, out double simSeconds);

    /// <summary>The host offers the S / W axis toggle.</summary>
    bool HasAxisToggle { get; }
    /// <summary>The viewer's axis is sim (the toggle's face).</summary>
    bool AxisIsSim { get; }
    /// <summary>The recording has a sim axis at all (a v6 file has not: the toggle is disabled).</summary>
    bool HasSimAxis { get; }
    /// <summary>The seek bar's labels are on the sim axis now.</summary>
    bool SimAxisActive { get; }
    void ToggleAxis();
}

/// <summary>⭐ V3 — the drawer's PROFILE: which items of the one transport a host draws. The Replay Browser draws <see cref="All"/> (its look unchanged); the editor's AAR segment draws
/// <see cref="AarSegment"/> — its main toolbar's time control already draws ▶ ⏸ ■, the rate and the axis, so the segment omits them.</summary>
[Flags]
public enum ReplayTransportParts
{
    None = 0,
    History = 1,
    Rewind = 2,
    PlayPause = 4,
    Rate = 8,
    Axis = 16,
    StepBack = 32,
    StepForward = 64,
    SeekBar = 128,
    All = History | Rewind | PlayPause | Rate | Axis | StepBack | StepForward | SeekBar,
    /// <summary>The editor's AAR segment: history ◀ ▶ · step back (hold to scrub) · the seek bar.</summary>
    AarSegment = History | StepBack | SeekBar,
}
