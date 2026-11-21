using System;
using System.Linq;
using Fdp.Presentation.Icons;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.UI.Common.AddEntity;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ CE-4271 (docs/DESIGN_Add_Entity_Picker.md §2j) — in a REAL frame (Xvfb; a skip is not a pass) a gallery tile draws ITS OWN CELL of an atlas page, not the page. The page here is a 2048 × 2048 GREEN texture with a RED cell at (col 0, row 0) and a BLUE cell at
/// (col 1, row 0), uploaded through <see cref="ThumbnailAtlas"/>'s page loader and served to the picker's grid layout through the image cache's RESIDENT path (<c>atlas:entities/&lt;stem&gt;</c> keys) — the same chain as production. A tile that drew the whole
/// texture (UV 0..1) would be nearly all green; each tile must be its own colour and the screen must hold no page background.
/// 🔴 Red-proof (compiles): <c>PickerImage</c> built with the whole-texture UVs in <c>ThumbnailAtlas.TryGetImage</c> (or the grid's <c>AddImage</c> back to <c>Vector2.Zero, Vector2.One</c>) ⇒ green on screen ⇒ red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class ThumbnailAtlasTileFrameRail
{
    private static int Count(Image image, Func<Color, bool> what)
    {
        int n = 0;
        for (int y = 0; y < image.Height; y += 2)
            for (int x = 0; x < image.Width; x += 2)
                if (what(Raylib.GetImageColor(image, x, y))) n++;
        return n;
    }

    private static bool Red(Color c) => c.R > 200 && c.G < 80 && c.B < 90;                  // Raylib's Red (230, 41, 55)
    private static bool Blue(Color c) => c.B > 200 && c.R < 40 && c.G is > 80 and < 160;     // Raylib's Blue (0, 121, 241)
    private static bool Green(Color c) => c.G > 200 && c.R < 60 && c.B < 100;                // Raylib's Green (0, 228, 48) — the page background

    [SkippableFact]
    public void ATileDrawsItsOwnCellOfTheAtlasPage_NotThePage()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1000, 700);

        // the page: green everywhere, a red cell at col 0 / row 0, a blue cell at col 1 / row 0 (320 x 180 cells)
        var img = Raylib.GenImageColor(2048, 2048, Color.Green);
        Raylib.ImageDrawRectangle(ref img, 0, 0, 320, 180, Color.Red);
        Raylib.ImageDrawRectangle(ref img, 320, 0, 320, 180, Color.Blue);
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        try
        {
            var atlas = new ThumbnailAtlas(
                AtlasIndex.Parse("""{ "version": 1, "cell": [320,180], "page": [2048,2048], "pages": ["p.jpg"], "items": { "Red": [0,0,0], "Blue": [0,1,0] } }"""),
                _ => ((IntPtr)tex.Id, tex.Width, tex.Height));
            var registry = new PickerRegistry { Images = new PickerImageCache(_ => null, null, resident: atlas.TryGetImage) };
            registry.OpenPicker(new PickerRequest
            {
                ContextKey = "rail.atlastile", Title = "Atlas", Layout = PickerLayout.Grid, SelectionMode = PickerSelectionMode.Single,
                ItemsProvider = () => new[] { "Red", "Blue" }.Select(n => new PickerEntry(n, n, null, null, null, null, n) { ImageKey = ThumbnailAtlas.KeyOf(n) }).ToArray(),
            }, _ => { });

            for (int i = 0; i < 8; i++) frame.Step(registry.DrawFrame);
            if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "atlas-tiles.png"));
            var shot = Raylib.LoadImageFromScreen();
            try
            {
                int red = Count(shot, Red), blue = Count(shot, Blue), green = Count(shot, Green);
                Assert.True(red > 300, $"the Red tile draws its red cell ({red} px)");
                Assert.True(blue > 300, $"the Blue tile draws its blue cell ({blue} px)");
                Assert.True(green == 0, $"no tile draws the page background — a whole-page tile is green ({green} px)");
                Assert.Equal(1, atlas.PagesUploaded);                               // two tiles, one page, one upload
            }
            finally { Raylib.UnloadImage(shot); }
        }
        finally { Raylib.UnloadTexture(tex); }
    }
}
