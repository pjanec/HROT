using System;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Fonts;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐ U-3 (docs/UX/DESIGN_Colour_Icons.md §3 / §3.1 / §4 I3) — the Details panel's tab icons and header buttons, and the AI document tabs, drawn through the ONE icon map, read off a REAL frame (real fonts, Xvfb).
/// 🔴 Red-proof (compiles): the AI document tabs passing a null icon to <c>IconTab.Begin</c> ⇒ no icon colour in the tab bar ⇒ red.
/// Renders (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>colour-icons-details-tabs.png</c>, <c>-details-stacked.png</c>, <c>-ai-tabs.png</c>.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheColourIconSurfacesDrawTheirIconsTests
{
    private sealed class Body : IDetailsViewInstance
    {
        public void Draw(DetailsContext context, string idScope) { ImGui.TextUnformatted("body"); }
        public void Dispose() { }
    }

    private static bool Near(Color c, Vector4 v, int tol) =>
        Math.Abs(c.R - v.X * 255f) <= tol && Math.Abs(c.G - v.Y * 255f) <= tol && Math.Abs(c.B - v.Z * 255f) <= tol;

    /// <summary>The x of the first and last pixel of <paramref name="v"/>'s colour in the band; (-1,-1) when none.</summary>
    private static (int Min, int Max, int Count) Find(Image shot, int x0, int y0, int x1, int y1, Vector4 v, int tol = 26)
    {
        int min = -1, max = -1, n = 0;
        for (int x = Math.Max(0, x0); x < Math.Min(shot.Width, x1); x++)
            for (int y = Math.Max(0, y0); y < Math.Min(shot.Height, y1); y++)
                if (Near(Raylib.GetImageColor(shot, x, y), v, tol)) { if (min < 0) min = x; max = x; n++; }
        return (min, max, n);
    }

    private static void Shoot(UiFrameSession frame, string name)
    {
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, name));
    }

    private static DetailsWindow MakeShell(string id)
    {
        var views = new DetailsViewRegistry();
        views.Add(new DetailsViewDescriptor("variables", "Variables", 10, _ => true, () => new Body()));
        views.Add(new DetailsViewDescriptor("scenario", "Scenario", 20, _ => true, () => new Body()));
        views.Add(new DetailsViewDescriptor("components", "Components", 30, _ => true, () => new Body()));
        views.Add(new DetailsViewDescriptor("event", "Event", 40, _ => true, () => new Body()));
        var store = new EditorSelectionStore();
        var shell = new DetailsWindow(id: id, owningPerspective: "Scenario", formatter: new VariableValueFormatter(RawValueDecoder.Instance), views: views,
            context: new LiveContextSource(() => DetailsContextBuilder.Build(store, "Scenario", VariableRunState.Planning)));
        new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f)).RegisterWindow(shell);   // float + pin draw
        return shell;
    }

    [Fact]
    public void TabIconOf_MapsTheTitlesTheDesignNames_AndLeavesTheRestWithoutAnIcon()
    {
        Assert.Equal(Icons.Get("tab/scenario"), DetailsWindow.TabIconOf("Scenario"));
        Assert.Equal(Icons.Get("tab/event"), DetailsWindow.TabIconOf("Event"));
        Assert.Equal(Icons.Get("tab/components"), DetailsWindow.TabIconOf("Components"));
        foreach (var t in new[] { "Variables", "Summary", "Mission", "Zones", "Blackboard", "Objectives", "Playable", "Runtime", "Macro" })
            Assert.Null(DetailsWindow.TabIconOf(t));
    }

    [SkippableFact]
    public void DetailsTabs_AndTheHeaderButtons_AreColoured_ThenStacked_TheSwitchFloatAndPinAre()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var shell = MakeShell("details_u3_frame");
        using var frame = UiFrameHarness.Begin(900, 400);
        new EditorFontService().Initialize(1f, 1f);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(640, 300));
            ImGui.Begin("Details##u3");
            shell.DrawClientAreaForRail();
            ImGui.End();
        }
        frame.StepN(6, Draw);
        Shoot(frame, "colour-icons-details-tabs.png");
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            // the tab row: y ≈ 30..52 (below the title bar). The bar lists the views by DESCENDING rank: Event amber · Components grey · Scenario blue · Variables (no icon)
            var scen = Find(shot, 12, 28, 500, 56, IconPalette.Blue);
            var comp = Find(shot, 12, 28, 500, 56, IconPalette.Grey, 14);
            var evt = Find(shot, 12, 28, 500, 56, IconPalette.Amber);
            Assert.True(scen.Count >= 8 && comp.Count >= 8 && evt.Count >= 8, $"the three titled tabs draw their icons (blue {scen.Count}, grey {comp.Count}, amber {evt.Count} px)");
            Assert.True(evt.Min < comp.Min && comp.Min < scen.Min, "left to right: Event, Components, Scenario");
            Assert.True(scen.Max < 255, $"the Variables tab (no row in the design's table) carries no icon: nothing blue right of the Scenario tab (last blue px at {scen.Max})");

            // the header buttons, right-aligned on the tab bar's row: layout (violet) · float (blue) · pin (amber), each on the neutral #1C1C22
            int right = 10 + 640;
            var violet = Find(shot, right - 130, 28, right, 56, IconPalette.Violet);
            var blueBtn = Find(shot, right - 130, 28, right, 56, IconPalette.Blue);
            var pin = Find(shot, right - 130, 28, right, 56, IconPalette.Amber);
            Assert.True(violet.Count >= 6 && blueBtn.Count >= 6 && pin.Count >= 6, $"layout violet {violet.Count}, float blue {blueBtn.Count}, pin amber {pin.Count} px");
            Assert.True(violet.Min < blueBtn.Min && blueBtn.Min < pin.Min, "layout, then float, then pin");
            Assert.True(Find(shot, right - 130, 28, right, 56, new Vector4(0x1C / 255f, 0x1C / 255f, 0x22 / 255f, 1f), 5).Count > 100, "on the neutral glyph-button background");
        }
        finally { Raylib.UnloadImage(shot); }

        shell.Layout = DetailsLayout.Stacked;
        frame.StepN(6, Draw);
        Shoot(frame, "colour-icons-details-stacked.png");
        shot = Raylib.LoadImageFromScreen();
        try
        {
            int right = 10 + 640;
            var tabsIcon = Find(shot, right - 120, 28, right, 56, IconPalette.Blue);
            Assert.True(tabsIcon.Count >= 6, $"stacked: the layout switch (to tabs) is a blue glyph ({tabsIcon.Count} px)");
            var floats = Find(shot, right - 120, 56, right, 300, IconPalette.Blue);
            var pins = Find(shot, right - 120, 56, right, 300, IconPalette.Amber);
            Assert.True(floats.Count >= 20 && pins.Count >= 20, $"stacked: every header carries a blue float and an amber pin (blue {floats.Count}, amber {pins.Count} px)");
        }
        finally { Raylib.UnloadImage(shot); }
    }

    // ── the AI document tabs ─────────────────────────────────────────────────────────────────────────────────────

    private sealed class Seam : ICanvasRenderSeam { public void Render(NodeEditor.Core.View.GraphView view) { } }

    private sealed class IconAsset : IEditableAsset, IAssetIconKeyProvider
    {
        public IconAsset(string name, string iconKey) { Name = name; IconKey = iconKey; }
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name { get; }
        public AssetKind Kind => AssetKind.Blueprint;
        public string SourceFilePath => "/fake.cs";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public string? IconKey { get; }
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    [SkippableFact]
    public void AiDocumentTabs_AreIconTabs_EachKindInItsColour_NoGlyphPrefixInTheLabel()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var dm = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
        var win = new AiGraphCanvasWindow("Blueprint", dm, new Seam());
        var action = dm.Open(new IconAsset("DoAction", AssetKindIcons.BlueprintActionIconKey));
        var cond = dm.Open(new IconAsset("CheckIt", AssetKindIcons.BlueprintConditionIconKey));
        var func = dm.Open(new IconAsset("Helper", AssetKindIcons.BlueprintFunctionIconKey));
        Assert.DoesNotContain(Icons.Get("doc/blueprint").Glyph, AiGraphCanvasWindow.GetTabLabel(action));   // the icon is painted, not typed
        Assert.Equal("doc/blueprint", AiGraphCanvasWindow.GetTabIconKey(action));
        Assert.Equal("doc/condition", AiGraphCanvasWindow.GetTabIconKey(cond));
        Assert.Equal("doc/utility", AiGraphCanvasWindow.GetTabIconKey(func));

        using var frame = UiFrameHarness.Begin(700, 200);
        new EditorFontService().Initialize(1f, 1f);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(640, 120));
            ImGui.Begin("Docs##u3", ImGuiWindowFlags.NoSavedSettings);
            win.DrawTabBarForRail();
            ImGui.End();
        }
        frame.StepN(6, Draw);
        Shoot(frame, "colour-icons-ai-tabs.png");
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            var amber = Find(shot, 12, 28, 600, 56, IconPalette.Amber);
            var blue = Find(shot, 12, 28, 600, 56, IconPalette.Blue);
            var grey = Find(shot, blue.Max + 1, 28, 600, 56, IconPalette.Grey, 14);   // right of the blue one: the tab-list ▾ button on the far left is grey too
            Assert.True(amber.Count >= 8 && blue.Count >= 8 && grey.Count >= 8, $"each tab draws its icon (amber {amber.Count}, blue {blue.Count}, grey {grey.Count} px)");
            Assert.True(amber.Min < blue.Min && blue.Min < grey.Min, "in open order: action (amber), condition (blue), function (grey)");
        }
        finally { Raylib.UnloadImage(shot); }
    }
}
