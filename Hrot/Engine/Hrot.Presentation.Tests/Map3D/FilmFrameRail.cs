using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Definitions.Tkb;
using Hrot.Map.Common;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-3289</c> (docs/DESIGN_Map_3D_Mode.md §6ab, R-3032) — a FILM FRAME, in a real window (Xvfb + Mesa in the cloud): the scene alone, at
/// the asked size, from the director's shot. A layer that is NOT scene — drawing a magenta cube in 3-D and a magenta square over the
/// screen, as the gizmos, cards and HUD do — is in the window's frame and absent from the film's. Skips where there is no display.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class FilmFrameRail
{
    /// <summary>Clutter: in the 3-D pass AND in the overlay pass, magenta. Not an <see cref="ISceneLayer"/>.</summary>
    private sealed class Clutter : IMapLayer
    {
        public Vector3 At;
        public string Name => "Clutter";
        public int LayerBitIndex => -1;
        public bool Has3D => true;
        public void Update(float dt) { }
        public void Draw(RenderContext ctx) { }
        public void Draw3D(RenderContext ctx) => Raylib.DrawCube(HrotToRaylib.Position(At), 4f, 4f, 4f, Color.Magenta);
        public void DrawOverlay(RenderContext ctx) => Raylib.DrawRectangle(0, 0, 80, 80, Color.Magenta);
        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;
    }

    private static int Magenta(Image image)
    {
        int n = 0;
        for (int y = 0; y < image.Height; y += 2)
            for (int x = 0; x < image.Width; x += 2)
            {
                var c = Raylib.GetImageColor(image, x, y);
                if (c.R > 200 && c.G < 60 && c.B > 200) n++;
            }
        return n;
    }

    /// <summary>
    /// 🔴 Red-proofs: <c>DrawScene</c> without the <see cref="ISceneLayer"/> filter ⇒ magenta in the film; with the overlay pass ⇒ the
    /// square; the image not flipped ⇒ the sky at the bottom (the top-row check).
    /// </summary>
    [SkippableFact]
    public void CE3289_AFilmFrame_IsTheSceneAlone_AtTheAskedSize()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        FilmStudio.Reset();

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        var query = WorldQuery.Of(world)!;
        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        var ground = new Vector3(mid.X, mid.Y, query.GroundHeightAt(mid.X, mid.Y));
        var tank = world.CreateEntity();
        world.AddComponent(tank, new SimTransform { Position = ground, Rotation = Quaternion.Identity });
        world.AddComponent(tank, new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });
        world.AddComponent(tank, new StanceIntent { TargetStance = StanceId.Standing });

        string dir = Path.Combine(Path.GetTempPath(), "hrot-film-rail");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "film-frame.png");
        if (File.Exists(path)) File.Delete(path);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var view = new MapViewSwitch(canvas, canvas.Camera);
        view.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(new EntityBodyLayer3D(() => world, Map3DTests.BuiltIn));
        canvas.AddLayer(new Clutter { At = ground + new Vector3(6f, 0f, 2f) });
        var film = new FilmCaptureLayer(canvas, view, () => world);
        canvas.AddLayer(film);

        // the director's shot: from the south-east, above, at the tank (and the cube beside it)
        FilmStudio.Direct(FilmShot.Looking(ground + new Vector3(-20f, -30f, 14f), ground + new Vector3(3f, 0f, 1f))!);
        film.Update(0.016f);
        view.Camera3D.Update(0.016f);
        frame.Step(canvas.Draw);                                    // the window's frame: clutter and all
        var window = Raylib.LoadImageFromScreen();
        int windowMagenta;
        try { windowMagenta = Magenta(window); } finally { Raylib.UnloadImage(window); }
        Assert.True(windowMagenta > 50, $"anti-vacuity: the clutter must be in the WINDOW's frame (magenta samples {windowMagenta})");

        var capture = FilmStudio.Capture(new FilmCaptureRequest(path, 320, 180));
        film.Update(0.016f);                                        // drawn last frame ⇒ this map serves it
        Assert.True(capture.IsCompletedSuccessfully, capture.Exception?.ToString() ?? "the capture was not served");
        Assert.Equal(1, film.Served);

        var image = Raylib.LoadImage(path);
        try
        {
            Assert.Equal((320, 180), (image.Width, image.Height));
            Assert.Equal(0, Magenta(image));
            var top = Raylib.GetImageColor(image, 160, 2);           // the sky is at the TOP (the GL rows were flipped)
            var bottom = Raylib.GetImageColor(image, 160, 177);
            Assert.True(top.B > top.G && top.B > top.R, $"the top row is not sky: {top.R},{top.G},{top.B}");
            Assert.False(top.R == bottom.R && top.G == bottom.G && top.B == bottom.B, "a uniform frame — nothing was drawn");
        }
        finally { Raylib.UnloadImage(image); }
        FilmStudio.Reset();
        film.Dispose();
    }
}
