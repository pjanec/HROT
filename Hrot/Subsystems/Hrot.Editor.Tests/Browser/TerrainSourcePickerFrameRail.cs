using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Fonts;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Recipes;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using NodeEditor.UI.Picker;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.Tests.Browser;

/// <summary>
/// ⭐ Terrain authoring U-5 / E-2 (mockup <c>terrain-authoring-4-save.svg</c>, the terrain picker) — the New Scenario picker's terrain rows: a terrain that keeps its source wears the green SOURCE chip and its right-click menu offers <i>Edit terrain…</i>; one that does not
/// has no chip and the entry greyed with its reason; <i>File ▸ Edit Terrain…</i> lists the terrains with a source only. Rows and chips are the data the picker is given; the frame (real fonts, Xvfb — a skip is not a pass) proves the chip DRAWS in green.
/// 🔴 Red-proof (compiles): <c>TerrainChips</c> returning null ⇒ no chip ⇒ the data cell and the green-pixel cell red.
/// </summary>
[Collection(Hrot.Editor.Tests.Windows.PanelSnapshotTestCollection.Name)]
public sealed class TerrainSourcePickerFrameRail
{
    private sealed class NoIcons : NodeEditor.Core.Interfaces.IIconProvider
    {
        public bool TryGet(string key, out NodeEditor.Core.Interfaces.IconHandle handle) { handle = default; return false; }
    }

    private sealed class Svc : INewAssetService
    {
        public AssetKind Kind => AssetKind.Scenario;
        public IReadOnlyList<IEditableAsset> AvailableRecipes() => Array.Empty<IEditableAsset>();
        public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath) => throw new NotSupportedException();
    }

    private static NewAssetLauncher Launcher(Action<PickerRequest> sink, List<string> edited)
        => new((r, _) => sink(r), new Dictionary<AssetKind, INewAssetService> { [AssetKind.Scenario] = new Svc() }, (_, _) => { },
            terrains: () => new[] { "riverside-3", "demo-city", "kolin-centre" }, showNewBlankDialog: _ => { },
            terrainSource: t => t switch
            {
                "riverside-3" => new TerrainSourceOffer(true, null, () => edited.Add(t)),
                "demo-city" => new TerrainSourceOffer(false, "This terrain keeps no source scenario.", () => edited.Add(t)),
                _ => new TerrainSourceOffer(true, "Its ground is gone.", () => edited.Add(t)),
            });

    [Fact]
    public void E2_ASourceTerrainWearsTheChip_AndOffersEditTerrain_AndFileEditTerrainListsOnlyThose()
    {
        PickerRequest? request = null; var edited = new List<string>();
        var launcher = Launcher(r => request = r, edited);
        launcher.Open(AssetKind.Scenario);
        var rows = request!.ItemsProvider().Where(e => e.Tag is BlankTerrainChoice).ToDictionary(e => e.Name);
        Assert.Equal(new PickerChip("terrain/source", "SOURCE"), Assert.Single(rows["riverside-3"].Chips!));
        Assert.Null(rows["demo-city"].Chips);
        var act = Assert.Single(rows["riverside-3"].Actions!);
        Assert.Equal("Edit terrain…", act.Label); Assert.Null(act.DisabledReason);
        act.Run(); Assert.Equal(new[] { "riverside-3" }, edited);
        Assert.Equal("This terrain keeps no source scenario.", Assert.Single(rows["demo-city"].Actions!).DisabledReason);   // greyed, with why
        Assert.Equal("Its ground is gone.", Assert.Single(rows["kolin-centre"].Actions!).DisabledReason);

        PickerRequest? edit = null;
        var l2 = Launcher(r => edit = r, edited);
        Assert.True(l2.CanOpenEditTerrain);
        l2.OpenEditTerrain();
        var listed = edit!.ItemsProvider().ToList();
        Assert.Equal(new[] { "riverside-3", "kolin-centre" }, listed.Select(e => e.Name).ToArray());   // only the terrains that keep a source
        Assert.True(listed[0].IsEnabled); Assert.False(listed[1].IsEnabled);                            // the one whose ground is gone is listed but not choosable
    }

    [SkippableFact]
    public void E2_TheSourceChipDrawsInGreen_OnItsRow()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        PickerRequest? request = null; var edited = new List<string>();
        Launcher(r => request = r, edited).Open(AssetKind.Scenario);
        var registry = new PickerRegistry();
        registry.SetServices(new NoIcons(), new NodeEditor.Core.DefaultTheme());
        using var frame = UiFrameHarness.Begin(900, 560);
        new EditorFontService().Initialize(1f, 1f);
        registry.OpenPicker(request!, _ => { });
        for (int i = 0; i < 4; i++) frame.Step(registry.DrawFrame);
        registry.ExpandFolder(NewAssetLauncher.BlankGroup);
        for (int i = 0; i < 6; i++) frame.Step(registry.DrawFrame);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "u5-new-scenario-terrains.png")); }
        // the right-click on a row opens its menu: Edit terrain… (the second shot)
        void Pos(float x, float y) { ImGui.GetIO().AddMousePosEvent(x, y); Raylib.SetMousePosition((int)x, (int)y); }
        frame.Step(() => { Pos(160, 188); registry.DrawFrame(); });
        frame.Step(() => { Pos(160, 188); ImGui.GetIO().AddMouseButtonEvent(1, true); registry.DrawFrame(); });
        frame.Step(() => { Pos(160, 188); ImGui.GetIO().AddMouseButtonEvent(1, false); registry.DrawFrame(); });
        for (int i = 0; i < 3; i++) frame.Step(() => { Pos(160, 188); registry.DrawFrame(); });
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir2) frame.Screenshot(Path.Combine(dir2, "u5-edit-terrain-menu.png"));
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            var g = IconPalette.Green; int r = (int)Math.Round(g.X * 255), gg = (int)Math.Round(g.Y * 255), b = (int)Math.Round(g.Z * 255), n = 0;
            for (int y = 0; y < shot.Height; y++)
                for (int x = 0; x < shot.Width; x++) { var p = Raylib.GetImageColor(shot, x, y); if (Math.Abs(p.R - r) <= 8 && Math.Abs(p.G - gg) <= 8 && Math.Abs(p.B - b) <= 8) n++; }
            Assert.True(n >= 8, $"the SOURCE chip's green is on screen ({n} px)");
        }
        finally { Raylib.UnloadImage(shot); }
    }
}
