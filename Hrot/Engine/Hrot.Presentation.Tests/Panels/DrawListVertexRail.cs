using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core.Diagnostics;
using Fdp.Presentation.Panels;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ CE-4223 (W-R4 Part 0 c) — MEASURES which ImGui draw list builds how many vertices. W-R5b's live check died on <c>ImGui assertion: Too many vertices in ImDrawList using 16-bit indices</c> (65 536) right after a run started, with the
/// Event Browser, the Entity Inspector and the Rules window open. The Event Browser is the suspect (it draws every captured row, no <c>ImGuiListClipper</c>); its history is capped at <c>DiagnosticEventHistoryService.Capacity</c> (500) rows.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class DrawListVertexRail
{
    private readonly ITestOutputHelper _out;
    public DrawListVertexRail(ITestOutputHelper o) => _out = o;

    private sealed class Fake : IDiagnosticEventHistoryService
    {
        public CapturedEventDto[] Events = Array.Empty<CapturedEventDto>();
        public void Capture(string providerName, Fdp.Core.FdpEventBus eventBus, uint currentFrame) { }
        public CapturedEventDto[] GetHistory(IReadOnlyList<string>? providerFilter = null) => Events;
        public void ClearHistory() { }
        public void RewindHistory(uint toFrame) { }
    }

    /// <summary>The most vertices ANY draw list of the last rendered frame holds (windows and their children each own one).</summary>
    internal static unsafe int MaxListVertices()
    {
        var dd = ImGui.GetDrawData();
        int max = 0;
        for (int i = 0; i < dd.CmdListsCount; i++) max = Math.Max(max, dd.CmdLists[i].VtxBuffer.Size);
        return max;
    }

    [SkippableFact]
    public void TheEventBrowser_WithAFullHistory_StaysUnderTheSixteenBitVertexLimit()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var svc = new Fake
        {
            Events = Enumerable.Range(0, DiagnosticEventHistoryService.Capacity)
                .Select(i => new CapturedEventDto((uint)i, "World", i % 2 == 0 ? "RaycastResultEvent" : "AssignBehaviorEvent", i % 2 == 1, "Fdp.Toolkit.Physics.RaycastResultEvent RayId:-922337 Hit:True Distance:12.5", null)).ToArray(),
        };
        var panel = new EventBrowserPanel(svc);
        using var frame = UiFrameHarness.Begin(1400, 1000);
        int worst = 0;
        for (int i = 0; i < 4; i++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(560, 900));
                ImGui.Begin("Event Browser", ImGuiWindowFlags.NoCollapse);
                panel.DrawContent();
                ImGui.End();
            });
        worst = MaxListVertices();
        _out.WriteLine($"Event Browser, {svc.Events.Length} rows, window 560x900: largest draw list = {worst} vertices (limit 65536)");
        Assert.True(worst < 65536, $"{worst} vertices");
    }

    private sealed class HugeTrack : Fdp.Presentation.Panels.Timeline.ITimelineTrack
    {
        public HugeTrack(string id, Fdp.Presentation.Panels.Timeline.TimelineTrackItem item) { Id = id; Items = new[] { item }; }
        public string Id { get; }
        public string Label => Id;
        public IReadOnlyList<Fdp.Presentation.Panels.Timeline.TimelineTrackItem> Items { get; }
        public void Select(string itemId) { }
        public void Move(string itemId, double toSimSeconds, bool commit) { }
        public bool Key(char key, double atSimSeconds) => false;
        public IEnumerable<Fdp.Toolkit.ReplayBrowser.Timeline.TimelineMenuItem> MenuItems(string? itemId) => Array.Empty<Fdp.Toolkit.ReplayBrowser.Timeline.TimelineMenuItem>();
    }

    /// <summary>
    /// ⭐ CE-4223 — THE CAUSE of the editor's death on <c>Too many vertices in ImDrawList</c> (found in W-R4 by reading the panel: the live <c>FrameStats</c> line names the largest list that SURVIVED a frame — the Event Browser, ≈ 10 k — never the one that killed it):
    /// an Expected / Hatched item whose end is far outside the window was dashed / hatched over its WHOLE length. A rule with a deadline an hour away, in a 10-second window, is some 200 000 px of dashes. Drawn now only inside the row.
    /// ✅ Red-proof: the clip removed ⇒ ImGui's assert kills the test host (measured; reverted).
    /// </summary>
    [SkippableFact]
    public void AnExpectedAndAHatchedItemFarOutsideTheWindow_DrawOnlyTheirVisiblePart()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var tracks = Enumerable.Range(0, 20).Select(i => (Fdp.Presentation.Panels.Timeline.ITimelineTrack)new HugeTrack("t" + i, new Fdp.Presentation.Panels.Timeline.TimelineTrackItem("i", 2, 5_000_000,
            "far", Fdp.Presentation.Panels.Timeline.TimelineTrackItemKind.Segment, false, null,
            i % 2 == 0 ? Fdp.Presentation.Panels.Timeline.TimelineTrackItemStyle.Expected : Fdp.Presentation.Panels.Timeline.TimelineTrackItemStyle.Hatched))).ToList();
        // a second item pins the window to a few seconds: the near item defines the extent, the far one runs off it
        var panel = new Fdp.Presentation.Panels.Timeline.TimelinePanel { Tracks = () => tracks };
        var cursor = new FarCursor();
        using var frame = UiFrameHarness.Begin(1280, 700);
        for (int i = 0; i < 4; i++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(1280, 700));
                ImGui.Begin("tl", ImGuiWindowFlags.NoCollapse);
                panel.Draw(null, cursor, Array.Empty<Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneDescriptor>());
                ImGui.End();
            });
        int worst = MaxListVertices();
        _out.WriteLine($"20 far Expected / Hatched items: largest draw list = {worst} vertices");
        Assert.True(worst < 20_000, $"{worst} vertices");
    }

    private sealed class FarCursor : Fdp.Presentation.Panels.Timeline.ITimelineCursor
    {
        public long ExtentFrom => 0; public long ExtentTo => 0; public long Now => 0; public bool CanSeek => false;
        public void Seek(long ticks) { }
    }
}
