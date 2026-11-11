using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Tkb;
using Hrot.Common.Constants;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Definitions.Tkb;
using Hrot.Presentation.Tests.AddEntity;
using Hrot.Presentation.Tests.Map3D;
using Hrot.UI.Common.AddEntity;
using NodeEditor.Core.Widgets;
using NodeEditor.UI.Picker;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ Terrain authoring U-1 (mockup <c>docs/UX/terrain-authoring-2-blocks.svg</c>, the Add Entity picker at the bottom) in a REAL frame (Xvfb; a skip is not a pass): the gallery opened on <i>Blocks</i> shows one tile per shipped recipe, each with its
/// PICTURE — the recipe at its default seed (tan houses on the ground green) — drawn from the tile's own PNG. A set of tiles with no picture is the dashed glyph tile (no tan, no ground green); the rail counts both colours on screen.
/// 🔴 Red-proof (compiles): <c>EntityTypeCatalog.Build</c> not passing <c>t.Picture</c> as the thumbnail ⇒ no picture ⇒ the tan/green counts fall to 0 ⇒ red. The frame is rendered to <c>HROT_RAIL_SHOTS</c> (⛔ never committed).
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TerrainAddEntityFrameRail : IDisposable
{
    private readonly string _src = ContentLayout.TryFindSourceTree()!;
    private readonly string _pics = Path.Combine(Path.GetTempPath(), "tafr-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() { try { Directory.Delete(_pics, true); } catch { } }

    private static int Count(Image image, Func<Color, bool> what)
    {
        int n = 0;
        for (int y = 0; y < image.Height; y += 2)
            for (int x = 0; x < image.Width; x += 2)
                if (what(Raylib.GetImageColor(image, x, y))) n++;
        return n;
    }

    [SkippableFact]
    public void TheGalleryOpenedOnBlocks_ShowsATilePerRecipe_EachWithItsPicture()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1220, 640);
        new EditorFontService().Initialize(1f, 1f);   // the real editor fonts (Roboto + FontAwesome): the harness default is ASCII-only and drew no icon

        var tkb = new TkbDatabase(); NedTkbCatalog.RegisterAll(tkb);
        var terrain = TerrainPickCatalog.Build(new[] { ContentLayout.TerrainRecipeAssets(_src) }, new[] { ContentLayout.BuildingAssets(_src) }, _pics);
        var blocks = terrain.Where(p => p.Kind == TerrainPickKind.Block).ToList();
        Assert.NotEmpty(blocks);
        Assert.All(blocks, b => Assert.True(b.Picture is not null && File.Exists(b.Picture), $"{b.Id} has a picture"));

        PickerRequest? req = null;
        var action = new AddEntityAction(() => tkb, () => (r, _) => req = r, () => new NoSpawn(), gallery: () => EntityTypeCatalogTests.ShippedGallery(), terrain: () => terrain) { InitialGroup = TerrainPickCatalog.Blocks };
        Assert.True(action.Open(GlobalActionIds.AddEntityFriendly));
        var registry = EntityAuthoring.CreatePickers();
        registry.OpenPicker(req!, _ => { });
        Assert.Equal(blocks.Count, req!.ItemsProvider().Count(e => e.Category!.StartsWith(TerrainPickCatalog.Blocks + "/")));

        for (int i = 0; i < 10; i++) frame.Step(registry.DrawFrame);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "u1-add-entity-blocks.png")); }
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            int tan = Count(shot, c => Math.Abs(c.R - 236) < 14 && Math.Abs(c.G - 206) < 14 && Math.Abs(c.B - 152) < 14);
            int ground = Count(shot, c => Math.Abs(c.R - 52) < 6 && Math.Abs(c.G - 74) < 6 && Math.Abs(c.B - 44) < 6);
            Assert.True(tan > 60, $"the tiles draw their houses ({tan} tan px)");
            Assert.True(ground > 2000, $"the tiles draw their ground ({ground} green px)");
            AssertGroupIcons(shot);
        }
        finally { Raylib.UnloadImage(shot); }
    }

    /// <summary>⭐ Mockup 2: every group of the sidebar has a coloured icon — the six terrain groups at full strength in their meaning's colour (blocks and buildings orange, vegetation green, walls and clutter grey, water blue). Counted per ROW BAND in the sidebar's icon column: two orange bands, a green, a blue, two grey.</summary>
    private static void AssertGroupIcons(Image shot)
    {
        int Bands(Vector4 c)
        {
            int r = (int)Math.Round(c.X * 255), g = (int)Math.Round(c.Y * 255), b = (int)Math.Round(c.Z * 255);
            int bands = 0, gap = 99;
            for (int y = 0; y < shot.Height; y++)
            {
                int n = 0;
                for (int x = 60; x < 130; x++) { var p = Raylib.GetImageColor(shot, x, y); if (Math.Abs(p.R - r) <= 8 && Math.Abs(p.G - g) <= 8 && Math.Abs(p.B - b) <= 8) n++; }
                if (n >= 2) { if (gap > 3) bands++; gap = 0; } else gap++;
            }
            return bands;
        }
        Assert.True(Bands(IconPalette.Orange) >= 1, "the Blocks / Buildings icons (orange) are in the sidebar");
        Assert.True(Bands(IconPalette.Green) >= 1, "the Vegetation icon (green) is in the sidebar");
        Assert.True(Bands(IconPalette.Blue) >= 1, "the Water & bridges icon (blue) is in the sidebar");
        Assert.True(Bands(IconPalette.Grey) >= 1, "the Walls / Clutter icons (grey) are in the sidebar");
    }

    /// <summary>
    /// ⭐ U-7 (mockup 3, the Clutter tiles) — the gallery opened on <i>Clutter</i> lists every clutter type as a tile (the glyph tile where a model render will be, as the mockup notes) plus the greyed <i>Street clutter</i> scatter, with its reason.
    /// </summary>
    [SkippableFact]
    public void TheGalleryOpenedOnClutter_ListsEveryClutterType_AndTheGreyedScatter()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1220, 640);
        new EditorFontService().Initialize(1f, 1f);   // the real editor fonts (Roboto + FontAwesome): the harness default is ASCII-only and drew no icon
        var tkb = new TkbDatabase(); NedTkbCatalog.RegisterAll(tkb);
        var terrain = TerrainPickCatalog.Build(new[] { ContentLayout.TerrainRecipeAssets(_src) }, new[] { ContentLayout.BuildingAssets(_src) }, _pics);
        PickerRequest? req = null;
        var action = new AddEntityAction(() => tkb, () => (r, _) => req = r, () => new NoSpawn(), gallery: () => EntityTypeCatalogTests.ShippedGallery(), terrain: () => terrain) { InitialGroup = TerrainPickCatalog.Clutter };
        Assert.True(action.Open(GlobalActionIds.AddEntityFriendly));
        var rows = req!.ItemsProvider().Where(e => e.Category == TerrainPickCatalog.Clutter || e.Category!.StartsWith(TerrainPickCatalog.Clutter + "/")).ToList();
        Assert.Equal(Fdp.Toolkit.Terrain.ClutterTypes.All.Count + 1, rows.Count);
        Assert.Single(rows, e => !e.IsEnabled);   // the scatter, greyed
        var registry = EntityAuthoring.CreatePickers();
        registry.OpenPicker(req!, _ => { });
        for (int i = 0; i < 10; i++) frame.Step(registry.DrawFrame);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "u7-add-entity-clutter.png")); }

        // gap C (mockup 3): each clutter tile carries ITS OWN coloured glyph — a barrel and sandbags amber, a crate and a booth orange, a car grey — not the generic map pin on every tile
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            int Count(NodeEditor.Core.Widgets.IconGlyph g) { var c0 = g.Color; int n = 0; for (int y = 0; y < shot.Height; y++) for (int x = 0; x < shot.Width; x++) { var c = Raylib.GetImageColor(shot, x, y); if (Math.Abs(c.R - c0.X * 255) < 6 && Math.Abs(c.G - c0.Y * 255) < 6 && Math.Abs(c.B - c0.Z * 255) < 6) n++; } return n; }
            Assert.True(Count(NodeEditor.Core.Widgets.Icons.Get("clutter/barrel")) > 150, "the barrel's and the sandbags' amber glyphs are on the tiles");
            Assert.True(Count(NodeEditor.Core.Widgets.Icons.Get("clutter/crate")) > 150, "the crate's and the booth's orange glyphs are on the tiles");
        }
        finally { Raylib.UnloadImage(shot); }
    }

    private sealed class NoSpawn : Hrot.UI.Common.Facades.ISpawnController
    {
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null) { }
        public void StartAreaAuthoringMode(string styleOverrideJson = "") { }
        public void StartAreaAuthoringMode(long tkbType, string styleOverrideJson = "") { }
        public void StartRouteAuthoringMode() { }
        public void StartZoneAuthoringMode(string styleOverrideJson = "") { }
    }
}
