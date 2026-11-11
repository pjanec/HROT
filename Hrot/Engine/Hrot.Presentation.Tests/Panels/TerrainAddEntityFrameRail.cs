using System;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Tkb;
using Hrot.Common.Constants;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Definitions.Tkb;
using Hrot.Presentation.Tests.AddEntity;
using Hrot.Presentation.Tests.Map3D;
using Hrot.UI.Common.AddEntity;
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
