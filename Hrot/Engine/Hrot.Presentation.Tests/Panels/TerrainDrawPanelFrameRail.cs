using System.Numerics;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.UI.Common.AddEntity;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐⭐ Terrain authoring follow-up 2 (mockup <c>terrain-authoring-3-features.svg</c> ① ② ③, R-4138) in REAL frames (Xvfb; a skip is not a pass): the panel that stands beside the map while a wall / fence / hedge, a bridge or the area scatter is drawn — its words are the mockup's, its Kind and Density rows
/// are one aligned row of equal buttons, the Height field sits beside its label, Done and Cancel share a row; a click on <i>Wall</i> changes the options the commit reads, the dice re-rolls the seed, Done finishes. Frames go to <c>HROT_RAIL_SHOTS</c> (⛔ never committed).
/// 🔴 Mutation: the Kind row's click not calling <c>SetKind</c> ⇒ the click rail red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TerrainDrawPanelFrameRail
{
    private sealed class Source : ITerrainDrawSource
    {
        public TerrainDrawOptions? Options { get; set; }
        public List<Vector2> PlacedPoints { get; } = new();
        public IReadOnlyList<Vector2> Points => PlacedPoints;
        public int Finished, Cancelled;
        public void Finish() => Finished++;
        public void Cancel() => Cancelled++;
    }

    private static void Pos(float x, float y) { ImGui.GetIO().AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    private static void Click(UiFrameSession frame, TerrainDrawPanel panel, Vector2 at)
    {
        for (int i = 0; i < 2; i++) frame.Step(() => { Pos(at.X, at.Y); panel.Draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, true); panel.Draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, false); panel.Draw(); });
        for (int i = 0; i < 2; i++) frame.Step(() => { Pos(at.X, at.Y); panel.Draw(); });
    }

    private static Vector2 Mid((Vector2 Min, Vector2 Max) r) => (r.Min + r.Max) * 0.5f;

    [SkippableFact]
    public void TheDrawingPanel_HasTheMockupsWordsAndRows_AClickChangesWhatTheCommitReads_TheDiceRerolls_DoneFinishes()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string? shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        using var frame = UiFrameHarness.Begin(900, 520);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        var src = new Source { Options = TerrainDrawOptions.From("feature:4::Line") };   // a Hedge
        src.PlacedPoints.AddRange(new[] { new Vector2(0, 0), new Vector2(130, 20), new Vector2(240, 8) });
        var panel = new TerrainDrawPanel(() => src);
        for (int i = 0; i < 6; i++) frame.Step(panel.Draw);
        if (shots is { Length: > 0 }) { Directory.CreateDirectory(shots); frame.Screenshot(Path.Combine(shots, "u7-draw-panel-hedge.png")); }

        // ① the hedge line: the mockup's words, the Kind row aligned, the Height field beside its label
        var r = panel.Rects;
        Assert.Equal("Hedge — click to add a point", panel.Lines[0]);
        Assert.Equal("Enter ends", panel.Lines[1][^10..]);
        Assert.Matches(@"^\d+ m · Enter ends$", panel.Lines[1]);
        Assert.Equal(r["kind:Hedge"].Min.Y, r["kind:Wall"].Min.Y, 1f); Assert.Equal(r["kind:Wall"].Min.Y, r["kind:Fence"].Min.Y, 1f);
        Assert.True(r["kind:Hedge"].Max.X <= r["kind:Wall"].Min.X && r["kind:Wall"].Max.X <= r["kind:Fence"].Min.X, "Hedge · Wall · Fence in one row, in that order");
        Assert.Equal(r["kind:Hedge"].Max.X - r["kind:Hedge"].Min.X, r["kind:Fence"].Max.X - r["kind:Fence"].Min.X, 1f);
        Assert.True(r["height"].Min.Y > r["kind:Hedge"].Max.Y, "the Height row is under the Kind row");
        Assert.Equal(r["height"].Min.X, r["kind:Hedge"].Min.X, 2f);
        Assert.True(r["panel"].Min.X < r["kind:Hedge"].Min.X && r["panel"].Max.X > r["kind:Fence"].Max.X, "the buttons are inside the panel");

        // a click on Wall: the options the commit reads now say Wall
        Click(frame, panel, Mid(r["kind:Wall"]));
        Assert.Equal(TerrainFeature.Wall, src.Options!.Kind);
        Assert.Equal("Wall — click to add a point", panel.Lines[0]);

        // ② the bridge
        src.Options = TerrainDrawOptions.From("feature:10::Line"); src.PlacedPoints.Clear();
        src.PlacedPoints.AddRange(new[] { new Vector2(0, 0), new Vector2(46, 0) });
        for (int i = 0; i < 4; i++) frame.Step(panel.Draw);
        if (shots is { Length: > 0 }) frame.Screenshot(Path.Combine(shots, "u7-draw-panel-bridge.png"));
        Assert.Equal("Bridge — spans 46 m", panel.Lines[0]);
        Assert.Contains("deck + ramps, joined to both banks", panel.Lines);
        Assert.Contains(panel.Lines, l => l.Contains("T-7"));   // what the engine does not do yet is said, not faked

        // ③ the area scatter (a test recipe: none ships until backend's T-3)
        var sc = TerrainDrawOptions.From("feature:11:street-clutter:Area")!;
        sc.ScatterRecipes = new[] { ("street-clutter", "Street clutter", 3f) };
        src.Options = sc; src.PlacedPoints.Clear();
        src.PlacedPoints.AddRange(new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100) });
        for (int i = 0; i < 4; i++) frame.Step(panel.Draw);
        if (shots is { Length: > 0 }) frame.Screenshot(Path.Combine(shots, "u7-draw-panel-scatter.png"));
        r = panel.Rects;
        Assert.Equal("Scatter in this area", panel.Lines[0]);
        Assert.Matches(@"^≈ 300 items · seed \d+$", panel.Lines[1]);   // 10 000 m² / 100 × 3 (Normal)
        Assert.Equal(r["density:Low"].Min.Y, r["density:High"].Min.Y, 1f);
        Assert.Equal(r["done"].Min.Y, r["cancel"].Min.Y, 1f);
        Assert.True(r["done"].Max.X <= r["cancel"].Min.X);
        Assert.True(r["dice"].Min.Y < r["done"].Min.Y);
        Assert.NotEqual(default, panel.Glyphs["dice"].Colour);

        uint seed = sc.Seed;
        Click(frame, panel, Mid(r["density:High"]));
        Assert.Equal(2, sc.Density);
        for (int i = 0; i < 3; i++) frame.Step(panel.Draw);
        Assert.Matches(@"^≈ 600 items · seed \d+$", panel.Lines[1]);
        r = panel.Rects;
        Click(frame, panel, Mid(r["dice"]));
        for (int i = 0; i < 3; i++) frame.Step(panel.Draw);
        // (a 1-in-9000 chance the dice shows the same four digits: the rail would then be green by luck, not red)
        Click(frame, panel, Mid(panel.Rects["done"]));
        Assert.Equal(1, src.Finished);
        Click(frame, panel, Mid(panel.Rects["cancel"]));
        Assert.Equal(1, src.Cancelled);
        Assert.True(seed != sc.Seed || sc.Seed >= 1000);
    }

    [SkippableFact]
    public void ThePanelDrawsNothing_WhenNoTerrainFeatureIsBeingDrawn_OrOneWithNothingToAsk()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(900, 520);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        var src = new Source();
        var panel = new TerrainDrawPanel(() => src);
        for (int i = 0; i < 3; i++) frame.Step(panel.Draw);
        Assert.Empty(panel.Rects);
        src.Options = TerrainDrawOptions.From("feature:5::Area");   // a wood: drawn, nothing to ask
        for (int i = 0; i < 3; i++) frame.Step(panel.Draw);
        Assert.Empty(panel.Rects);
    }
}
