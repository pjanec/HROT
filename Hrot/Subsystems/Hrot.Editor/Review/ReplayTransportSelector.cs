using System;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser;

namespace Hrot.Editor.Review;

/// <summary>
/// ⭐ R-5 / V6 — the editor's ONE AAR transport as the drawer sees it: <b>the source decides what it drives</b>. While a participant group is open (<see cref="ReviewSource.IsOpen"/>) every call goes
/// to the source's transport (the manager's merge, a part's playback); otherwise to the own-part transport (<see cref="OwnPartReplayTransport"/> — a cluster seek). ONE drawer, ONE segment, two
/// implementations of the seam, chosen by the files that were opened (D6) — never by the layout.
/// </summary>
public sealed class ReplayTransportSelector : IReplayTransport
{
    private readonly IReplayTransport _ownPart;
    private readonly ReviewSource _source;

    public ReplayTransportSelector(IReplayTransport ownPart, ReviewSource source)
    {
        _ownPart = ownPart ?? throw new ArgumentNullException(nameof(ownPart));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>The transport in charge now.</summary>
    public IReplayTransport Active => _source.IsOpen ? _source.Transport : _ownPart;

    public bool HasRecording => Active.HasRecording;
    public int CurrentFrame => Active.CurrentFrame;
    public int TotalFrames => Active.TotalFrames;
    public void Seek(int frame) => Active.Seek(frame);
    public void Step(int delta) => Active.Step(delta);
    public bool IsPlaying => Active.IsPlaying;
    public void Play() => Active.Play();
    public void Pause() => Active.Pause();
    public float Rate { get => Active.Rate; set => Active.Rate = value; }
    public bool CanPlay => Active.CanPlay;
    public string? PlayDisabledReason => Active.PlayDisabledReason;
    public PlaybackHistoryTracker Waypoints => Active.Waypoints;
    public double SecondsOf(int frame) => Active.SecondsOf(frame);
    public void FrameTimes(out long wallClockTicks, out double relativeWallSeconds, out double simSeconds) => Active.FrameTimes(out wallClockTicks, out relativeWallSeconds, out simSeconds);
    public bool HasAxisToggle => Active.HasAxisToggle;
    public bool AxisIsSim => Active.AxisIsSim;
    public bool HasSimAxis => Active.HasSimAxis;
    public bool SimAxisActive => Active.SimAxisActive;
    public void ToggleAxis() => Active.ToggleAxis();
}
