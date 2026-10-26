using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>
/// ⭐ <c>CE-1302</c> — U-obs-5: the whole of what <see cref="TimelinePanel"/> shows, this frame: how far the index is, the visible window,
/// "now", and each lane's events in the window and in total — a pure projection, no ImGui.
/// </summary>
public sealed record TimelinePanelViewModel(
    string PanelId,
    string PanelKind,
    bool HasIndex,
    bool IsComplete,
    int ScannedFrames,
    int TotalFrames,
    long ExtentFrom,
    long ExtentTo,
    long WindowFrom,
    long WindowTo,
    long Now,
    bool CanSeek,
    IReadOnlyDictionary<string, int> InWindow,
    IReadOnlyDictionary<string, int> Total,
    string? Error,
    IReadOnlyDictionary<string, string> LaneErrors) : IPanelViewModel
{
    public JsonNode Dump() => PanelDump.Of(this);

    /// <param name="lanes">The registered lanes the panel shows (R-4079) — the dump counts each by its id.</param>
    public static TimelinePanelViewModel Build(string id, string kind, ITimelineSource? source, ITimelineCursor? cursor, TimelineWindow window,
        IReadOnlyList<TimelineLaneDescriptor> lanes)
    {
        var index = source?.Index ?? TimelineIndex.Empty;
        var inWindow = new Dictionary<string, int>();
        var total = new Dictionary<string, int>();
        var (f0, f1) = TimelineLayout.FramesIn(index, window);
        foreach (var lane in lanes)
        {
            total[lane.Id] = index.Lane(lane.Id).Count;
            inWindow[lane.Id] = f1 > f0 ? index.Count(lane.Id, f0, f1) : 0;
        }
        return new TimelinePanelViewModel(id, kind, source != null, source?.IsComplete ?? false, index.ScannedFrames, index.TotalFrames,
            cursor?.ExtentFrom ?? 0, cursor?.ExtentTo ?? 0, window.From, window.To, cursor?.Now ?? 0, cursor?.CanSeek ?? false, inWindow, total, source?.Error,
            new Dictionary<string, string>(index.LaneErrors));
    }
}
