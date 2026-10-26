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
    public FederatedReplayCursor(Func<FederatedReplayManager?> manager, Func<int> nodeId, Func<bool>? isPlaying = null, Action? togglePlay = null)
    {
        _manager = manager;
        _nodeId = nodeId;
        _isPlaying = isPlaying;
        _togglePlay = togglePlay;
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
