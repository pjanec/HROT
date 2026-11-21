using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.UiFrameRail;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-4397 + CE-4398 (docs/UX/picker-filter-visual.svg; DESIGN_Content_Layout §6 Q-S3) — the scenario picker lists its folders in R-4185's order (My scenarios · showcases · demos with
/// its categories in the design's order · tests), and a typed filter lists the hits FLAT (🔒 user: <i>"I would expect the tree will open the branches which are containing the matching
/// scenario … acceptable: the list will flatten"</i>) — a folder collapsed before typing hid its hits. An empty filter brings the tree back.
/// 🔴 Red-proof (compiles): the flat branch of <c>TreeLayout.DrawImplicitTree</c> off ⇒ the filtered rows are folders again ⇒ red. Skips without a display (xvfb-run -a).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheScenarioPickerTests
{
    private sealed class NoIcons : IIconProvider { public bool TryGet(string key, out IconHandle handle) { handle = default; return false; } }

    private static PickerEntry E(string name, string? category) => new(name, name, "", category, null, null, null);

    private static List<PickerEntry> Entries() => new()
    {
        E("bd-destruction", "demos/buildings-and-doors"), E("wf-destroyer-duel", "demos/weapons-and-fires"), E("air-patrol", "demos/air"),
        E("nav-bridge", "demos/navigation-and-terrain"), E("inf-fireteam", "demos/infantry-and-squads"),
        E("showcase-crossfire", "showcases"), E("my-first", AssetPickerSource.MyScenarios), E("fixture-x", "tests/fixtures"),
    };

    [SkippableFact]
    public void CE4397_4398_TheFoldersAreInTheDesignsOrder_AFilterListsTheHitsFlat_AnEmptyFilterRestoresTheTree()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var registry = new PickerRegistry();
        registry.SetServices(new NoIcons(), new NodeEditor.Core.DefaultTheme());
        using var frame = UiFrameHarness.Begin(900, 600);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        registry.OpenPicker(new PickerRequest
        {
            ContextKey = "t.scenarios", Title = "Open Asset", Layout = PickerLayout.Tree, ItemsProvider = Entries,
            FolderOrder = AssetPickerLauncher.ScenarioFolderOrder,
        }, _ => { });
        for (int i = 0; i < 4; i++) frame.Step(registry.DrawFrame);

        var roots = registry.TreeRows().Where(r => r.IsFolder && r.Depth == 0).Select(r => r.Label).ToArray();
        Assert.Equal(new[] { AssetPickerSource.MyScenarios, "showcases", "demos", "tests" }, roots);   // the order of Q-S3, not the alphabet (demos · My scenarios · showcases · tests)

        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d0) { System.IO.Directory.CreateDirectory(d0); frame.Screenshot(System.IO.Path.Combine(d0, "scenario-picker-tree.png")); }
        registry.ExpandFolder("demos");
        for (int i = 0; i < 3; i++) frame.Step(registry.DrawFrame);
        var subs = registry.TreeRows().Where(r => r.IsFolder && r.Depth == 1).Select(r => r.Label).ToArray();
        Assert.Equal(new[] { "air", "infantry-and-squads", "weapons-and-fires", "buildings-and-doors", "navigation-and-terrain" }, subs);   // the design's order inside demos

        registry.SetSearch("destr");                                                                 // folders stay closed in the tree; the filter shows the hits anyway
        for (int i = 0; i < 4; i++) frame.Step(registry.DrawFrame);
        var rows = registry.TreeRows();
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { System.IO.Directory.CreateDirectory(dir); frame.Screenshot(System.IO.Path.Combine(dir, "scenario-picker-filter.png")); }   // never in git
        Assert.All(rows, r => Assert.False(r.IsFolder));                                                // flat
        Assert.Contains("bd-destruction", rows.Select(r => r.Label));                                   // the nested hit the collapsed folder hid
        Assert.Equal(new[] { "bd-destruction", "wf-destroyer-duel" }, rows.Select(r => r.Label).Take(2).OrderBy(x => x).ToArray());   // the picker's own ranking: the substring hits first
        Assert.All(rows, r => Assert.Equal(0, r.Depth));

        registry.SetSearch("");
        for (int i = 0; i < 4; i++) frame.Step(registry.DrawFrame);
        Assert.Contains(registry.TreeRows(), r => r.IsFolder && r.Label == "showcases");                // the tree is back
    }

    [Fact]
    public void CE4397_ATopLevelScenario_IsFiledUnderMyScenarios_NotLooseAtTheRoot()
    {
        Assert.Equal("My scenarios", AssetPickerSource.MyScenarios);
        Assert.Equal(new[] { "My scenarios", "showcases", "demos" }, AssetPickerLauncher.ScenarioFolderOrder.Take(3).ToArray());
        Assert.Equal("tests", AssetPickerLauncher.ScenarioFolderOrder[^1]);
    }
}
