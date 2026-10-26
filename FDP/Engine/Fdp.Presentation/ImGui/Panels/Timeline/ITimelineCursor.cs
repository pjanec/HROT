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
}

/// <summary>
/// ⭐ <c>CE-1302</c> (§9 D1) — the Replay Browser's cursor: it fronts <see cref="FederatedReplayManager"/>, the path its own scrubber
/// takes (<c>SetBaseWallTicks</c> seeks every node). The index is one node's recording, so a seek subtracts that node's offset.
/// </summary>
public sealed class FederatedReplayCursor : ITimelineCursor
{
    private readonly Func<FederatedReplayManager?> _manager;
    private readonly Func<int> _nodeId;

    public FederatedReplayCursor(Func<FederatedReplayManager?> manager, Func<int> nodeId)
    {
        _manager = manager;
        _nodeId = nodeId;
    }

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
