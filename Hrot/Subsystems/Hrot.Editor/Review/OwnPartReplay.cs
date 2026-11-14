using System;
using System.Text.Json.Nodes;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared.Review;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;

namespace Hrot.Editor.Review;

/// <summary>
/// ⭐ R-2 / V2 (docs/UX/DESIGN_Review_Perspective.md §3 D3 / D4, §4 V2) — <b>the ONE access to this node's own-part replay</b>: the file it replays, a throwaway reader over it,
/// where the replay stands, and THE seek (a frame becomes a moment through the file's index and the replay's zero, then the cluster master's seek round).
/// Extracted from <c>DebugApiService</c> (its <c>ScanContext</c>, <c>SeekReplayToFrame</c> and the seek round of <c>SeekReplayTo</c>) so the HTTP API AND the editor's review
/// windows call ONE implementation. ⛔ No second seek path.
/// </summary>
public sealed class OwnPartReplay : IDisposable
{
    public const string NoMaster =
        "No subsystem on this host has an orchestration bus that reaches the ClusterMaster, so it cannot publish this "
      + "request. Check the 'scenario.load' cell in GET /capabilities.";

    public const string NoRecording =
        "This node is not in a replay (POST /replay {exerciseId} enters one), so there is no recording to scan.";

    private readonly Func<string?> _path;
    private readonly Func<ReplayPosition?> _position;
    private readonly Func<IClusterMasterControl?> _master;
    private readonly Func<ClusterState?> _state;
    private string? _cachedPath;
    private string[] _cachedFiles = Array.Empty<string>();

    /// <summary>The file this node replays now; where the replay stands; the cluster master; the cluster state (null when unknown).</summary>
    public OwnPartReplay(Func<string?> recordingPath, Func<ReplayPosition?> position, Func<IClusterMasterControl?> master, Func<ClusterState?> state, RecordingFileReader? reader = null)
    {
        _path = recordingPath; _position = position; _master = master; _state = state;
        Reader = reader ?? new RecordingFileReader();
    }

    /// <summary>The throwaway reader over the replayed file (D4), shared with the review windows.</summary>
    public RecordingFileReader Reader { get; }

    /// <summary>The file this node replays, or null.</summary>
    public string? RecordingPath => _path();

    /// <summary>The file as <see cref="ReviewHostServices.RecordingFiles"/>: empty outside a replay. ⚠ The array is cached (read every frame) and replaced only when the path changes.</summary>
    public string[] RecordingFiles()
    {
        string? p = _path();
        if (p != _cachedPath) { _cachedPath = p; _cachedFiles = p is null ? Array.Empty<string>() : new[] { p }; }
        return _cachedFiles;
    }

    /// <summary>A read-only reader over this node's replay recording, kept while the file is the same.</summary>
    public Fdp.Toolkit.ReplayBrowser.ReplayBrowserContext? ScanContext(out string? path)
    {
        path = _path();
        return Reader.Of(path);
    }

    /// <summary>Where this replay stands, or null.</summary>
    public ReplayPosition? Position => _position();

    /// <summary>The replay's current frame in this node's file (the last frame at or before the replay's wall moment); -1 when not replaying.</summary>
    public long CurrentFrame()
    {
        if (_position() is not { } position) return -1;
        var ctx = Reader.Of(_path());
        return ctx?.Playback is { } playback ? FrameAt(playback, position.NowWallTicks) : -1;
    }

    /// <summary>The last frame of the recording at or before <paramref name="wallTicks"/> (0 before its first).</summary>
    public static int FrameAt(PlaybackController playback, long wallTicks)
    {
        int lo = 0, hi = playback.TotalFrames - 1, found = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (playback.GetFrameMetadata(mid).WallClockTicks <= wallTicks) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    /// <summary>
    /// ⭐ THE seek round: seeks the replay to <paramref name="at"/> wall seconds after its zero (or to <paramref name="atSim"/> recorded sim seconds — it wins), through the cluster master.
    /// </summary>
    public (JsonNode? Result, string? Error, string? HintCategory) SeekAt(double at, double? atSim)
    {
        if (_state() is { } s && !ClusterStates.IsReplaying(s))   // ⭐ CE-3329 — a review seeks the same way
            return (null, $"The cluster is in {s}, not a replay: POST /replay (or POST /review) first.", DebugApiHints.Scenario);
        var master = _master();
        if (master is null) return (null, NoMaster, DebugApiHints.Panel);
        // ⭐ CE-3329 R11 — atSimSeconds wins; the master (or the Replay Browser) resolves it through the replay's time map
        var intent = Hrot.UI.Common.Facades.ReplaySessionIntents.Seek(at, atSim);   // ⭐ CE-4045 H1b — the ONE construction
        try { master.SeekReplay(intent); }
        catch (InvalidOperationException ex) { return (null, ex.Message, DebugApiHints.Scenario); }
        var result = new JsonObject { ["published"] = true, ["requestId"] = intent.RequestId.ToString() };
        if (atSim is { } simTarget) result["atSimSeconds"] = simTarget; else result["atSeconds"] = at;
        return (result, null, null);
    }

    /// <summary>Main thread: seeks the replay to frame <paramref name="frame"/> of THIS node's recording (clamped), as the ordinary seek.</summary>
    public (JsonNode? Result, string? Error, string? HintCategory) SeekToFrame(int frame, string resolvedFrom)
    {
        var ctx = ScanContext(out _);
        if (ctx?.Playback is not { } playback) return (null, NoRecording, DebugApiHints.Scenario);
        if (_position() is not { } position)
            return (null, "This host does not know where its replay stands (no replay position).", DebugApiHints.Scenario);
        int target = Math.Clamp(frame, 0, Math.Max(0, playback.TotalFrames - 1));
        long ticks = playback.GetFrameMetadata(target).WallClockTicks;
        double at = Math.Max(0, (ticks - position.ZeroWallTicks) / (double)TimeSpan.TicksPerSecond);
        var seek = SeekAt(at, null);
        if (seek.Result is JsonObject o)
        {
            o["frame"] = target;
            o["resolvedFrom"] = resolvedFrom;
        }
        return seek;
    }

    /// <summary>Where the replay of an editor-side record/replay controller stands: its zero (the anchor, else the recording's start), now, and the sim seconds.</summary>
    public static ReplayPosition? PositionOf(Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController? rr)
        => rr is { IsReplayActive: true }
            ? new ReplayPosition(
                  rr.ReplayAnchorWallTicks > 0 ? rr.ReplayAnchorWallTicks : rr.ActiveRecordingStartWallTicks,
                  rr.GetCurrentReplayTime().TotalWallTicks,
                  rr.CurrentReplaySimSeconds)
            : null;

    public void Dispose() => Reader.Dispose();
}
