using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core.Diagnostics;
using Fdp.Presentation.Panels;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.Presentation.Tests.TerrainOsm;
using Hrot.Terrain.Osm;
using Hrot.UI.Common.TerrainOsm;
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
    // ── ⭐ CE-1495 — the OSM wizard's map preview: thousands of buildings, roads, trees and walls in ONE window draw list ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A dense synthetic area over the fake place's box: <paramref name="buildings"/> 8 m houses on a 10 m grid round its middle, a road net, rail, walls and lone trees (local metres).</summary>
    private static OsmPreview DensePreview(int buildings = 6000, int trees = 3000)
    {
        var rnd = new Random(7);
        var blds = new List<PreviewBuilding>(buildings);
        double cx = OsmFakeWorld.WidthM / 2, cy = OsmFakeWorld.HeightM / 2;
        const int cols = 100;
        for (int i = 0; i < buildings; i++)
        {
            double x = cx - 500 + (i % cols) * 10, y = cy - 300 + (i / cols) * 10;
            var fp = new[] { new Vector2((float)x, (float)y), new Vector2((float)x + 8, (float)y), new Vector2((float)x + 8, (float)y + 8), new Vector2((float)x, (float)y + 8) };
            blds.Add(new PreviewBuilding("w" + (2000 + i), fp, 1 + i % 3, i % 11 != 0) { Type = "house" });
        }
        var roads = new List<PreviewRoad>();
        for (int i = 0; i < 400; i++)
        {
            var pts = new Vector2[6]; var p = new Vector2((float)(cx - 520 + rnd.NextDouble() * 1040), (float)(cy - 320 + rnd.NextDouble() * 640));
            for (int k = 0; k < pts.Length; k++) { pts[k] = p; p += new Vector2((float)(rnd.NextDouble() * 60 - 30), (float)(rnd.NextDouble() * 60 - 30)); }
            roads.Add(new PreviewRoad(i % 25 == 0 ? "rail" : i % 5 == 0 ? "primary" : "residential", pts));
        }
        roads.Add(new PreviewRoad("rail", new[] { new Vector2(0, 100), new Vector2(4800, 3400) }));   // a 5.8 km rail line: dashed over its whole length, before the fix
        var lines = new List<PreviewLine>();
        for (int i = 0; i < 500; i++)
        {
            var p = new Vector2((float)(cx - 500 + rnd.NextDouble() * 1000), (float)(cy - 300 + rnd.NextDouble() * 600));
            lines.Add(new PreviewLine("wall", new[] { p, p + new Vector2(12, 3), p + new Vector2(20, -6) }));
        }
        var tr = new List<Vector2>(trees);
        for (int i = 0; i < trees; i++) tr.Add(new Vector2((float)(cx - 500 + rnd.NextDouble() * 1000), (float)(cy - 300 + rnd.NextDouble() * 600)));
        return OsmFakeWorld.Preview() with { Buildings = blds, Roads = roads, Lines = lines, Trees = tr };
    }

    /// <summary>Draws the map in its OWN window at <paramref name="zoomOffset"/> levels from Fit, N frames; returns the map and the largest draw list.</summary>
    private (OsmMapView Map, int Worst, long Bytes) DrawDense(OsmPreview pv, double zoomOffset, int frames = 4)
    {
        var tmp = System.IO.Path.GetTempPath();
        var model = new OsmWizardModel(new OsmWizardServices(new FakeOsmPipeline(), new FakeTileSource(), new FakePlaceSearch(), null, tmp, tmp));
        model.SetBox(OsmFakeWorld.Box);
        var map = new OsmMapView();
        map.Fit(OsmFakeWorld.Box);
        map.Zoom += zoomOffset;
        // Fit centres the map on the box's middle — where DensePreview puts its cluster, so a zoomed-in view is full of buildings
        long bytes = 0;
        using var frame = UiFrameHarness.Begin(1400, 900);
        for (int i = 0; i < frames; i++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(900, 560));
                ImGui.Begin("osm", ImGuiWindowFlags.NoCollapse);
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                map.Draw(model, null, new Vector2(830, 476), false, pv, 1f);
                if (i == frames - 1) bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
                ImGui.Dummy(new Vector2(1, 1));   // the map moves the cursor about; an item after it keeps ImGui's boundary check quiet
                ImGui.End();
            });
        return (map, MaxListVertices(), bytes);
    }

    /// <summary>
    /// ⭐ CE-1495 (the SECOND "Too many vertices in ImDrawList" crash — the OSM wizard's preview, right after the download; CE-4223 fixed one panel) — a dense synthetic area (6 000 buildings, ~400 roads, a 5.8 km rail line, 500 walls, 3 000 trees)
    /// keeps the window's largest draw list WELL under 65 536 at the overview zoom, at a zoom where every building is a few pixels (the budget), and zoomed in (the cull). ⭐ The map degrades by level of detail — a building under 2 px is one rectangle, a street
    /// is decimated, the rail's dashes are generated for the visible part only, what is off the canvas is skipped — and ⛔ never by crashing. 0 B per steady frame (R-2084).
    /// ✅ Red-proof: the LOD / budget removed (<c>TinyPx</c> = 0, caps = int.MaxValue) ⇒ the list holds ≫ 65 536 vertices.
    /// </summary>
    [SkippableTheory]
    [InlineData(0.0)]    // the overview: Fit — a 10 m house is ~1 px
    [InlineData(3.0)]    // every house a few px: the budget degrades them to rectangles
    [InlineData(6.0)]    // zoomed in: the box is far larger than the canvas — the cull
    public void TheOsmPreview_OfADenseArea_StaysWellUnderTheSixteenBitVertexLimit(double zoomOffset)
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (map, worst, bytes) = DrawDense(DensePreview(), zoomOffset, 6);
        _out.WriteLine($"dense OSM preview at Fit{zoomOffset:+0;-0;+0}: largest list {worst} vertices (preview adds {map.PreviewVertices}); culled {map.CulledOffCanvas}, rectangles {map.DrawnAsRectangles}, dropped {map.DroppedForBudget}; cumulative after areas/roads/walls/trees/buildings {string.Join("/", map.VerticesAfterLayer)}; map.Draw allocated {bytes} B in a steady frame");
        Assert.True(worst < 50_000, $"{worst} vertices — over the margin the budget keeps (limit 65 536)");
        Assert.True(map.PreviewVertices <= OsmMapView.RectCap + 4_000, $"the preview added {map.PreviewVertices}");
        Assert.Equal(0, bytes);   // R-2084: no allocation in the steady frame
    }

    /// <summary>⭐ CE-1495 — the level of detail is what keeps it small: at the overview the houses are rectangles, and the cull / dash budget are doing their work.</summary>
    [SkippableFact]
    public void TheOsmPreview_AtTheOverview_DrawsTinyHousesAsRectangles_AndAtAZoomInSkipsWhatIsOffTheCanvas()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var pv = DensePreview();
        var (overview, _, _) = DrawDense(pv, 0, 3);
        Assert.True(overview.DrawnAsRectangles >= 5000, $"the 6 000 houses of ≈1 px are rectangles: {overview.DrawnAsRectangles}");
        var (zoomed, _, _) = DrawDense(pv, 6, 3);
        Assert.True(zoomed.CulledOffCanvas > 5000, $"zoomed in, the houses and roads off the canvas are skipped: {zoomed.CulledOffCanvas}");
    }

    /// <summary>⭐ CE-1495 — a dashed line is generated for its VISIBLE part, on a budget (the CE-4223 TimelinePanel approach): a 5 800 px rail line costs at most <see cref="OsmMapView.MaxDashesPerSegment"/> dashes, and a line wholly outside none.</summary>
    [SkippableFact]
    public void ADashedLine_IsGeneratedForItsVisiblePartOnly_AndOnABudget()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1000, 600);
        int[] sizes = new int[3];
        for (int i = 0; i < 4; i++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(900, 500));
                ImGui.Begin("dash", ImGuiWindowFlags.NoCollapse);
                var dl = ImGui.GetWindowDrawList();
                var lo = new Vector2(100, 100); var hi = new Vector2(500, 300);
                int v0 = dl.VtxBuffer.Size;
                OsmMapView.DashLine(dl, new Vector2(-1_000_000, 200), new Vector2(1_000_000, 200), 0xFFFFFFFF, 1, 6, 4, lo, hi);   // two million px long, 400 px visible
                sizes[0] = dl.VtxBuffer.Size - v0; v0 = dl.VtxBuffer.Size;
                OsmMapView.DashLine(dl, new Vector2(-1_000_000, 900), new Vector2(1_000_000, 900), 0xFFFFFFFF, 1, 6, 4, lo, hi);       // wholly outside
                sizes[1] = dl.VtxBuffer.Size - v0; v0 = dl.VtxBuffer.Size;
                OsmMapView.DashLine(dl, new Vector2(100, 150), new Vector2(500, 150), 0xFFFFFFFF, 1, 6, 4, lo, hi);                   // fits: dashed as ever (40 dashes)
                sizes[2] = dl.VtxBuffer.Size - v0;
                ImGui.End();
            });
        Assert.InRange(sizes[2], 40 * 4, 42 * 8);   // 400 px at a 10 px pitch: ~40 dashes
        Assert.InRange(sizes[0], sizes[2] - 16, sizes[2] + 16);   // ⭐ the 400 visible px of a two-million-px line dash like a 400 px line — not 64 dashes spread over the whole length, and not the 200 000 of the pitch as it was
        Assert.Equal(0, sizes[1]);
    }
}
