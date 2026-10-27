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

    // ── ⭐ CE-1380 / CE-1379 (docs/DESIGN_Director_Timeline_Editor.md §11t "CE-1380 / CE-1379 as built") — ONE film pass in both modes ─────────────────

    private static readonly Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 Cyan = new(0, 255, 255);
    private static readonly Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 Pink = new(255, 0, 128);

    /// <summary>Not a scene layer: a cyan square in the 2-D pass (at a world point) and over the screen — what the cursor readout, the HUD and the
    /// tools are to a film.</summary>
    private sealed class Clutter2D : IMapLayer
    {
        public Vector2 At;
        public string Name => "Clutter (2-D)";
        public int LayerBitIndex => -1;
        public void Update(float dt) { }
        public void Draw(RenderContext ctx) => Raylib.DrawRectangleV(At - new Vector2(6f, 6f), new Vector2(12f, 12f), new Color(0, 255, 255, 255));
        public void DrawOverlay(RenderContext ctx) => Raylib.DrawRectangle(0, 0, 60, 60, new Color(0, 255, 255, 255));
        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;
    }

    private static bool IsCyan(Color c) => c.R < 60 && c.G > 200 && c.B > 200;
    private static bool IsPink(Color c) => c.R > 200 && c.G < 70 && c.B > 80 && c.B < 180;
    private static bool IsMagenta(Color c) => c.R > 200 && c.G < 70 && c.B > 200;
    // anything drawn over the cleared background: the window clears dark grey (UiFrameSession), a film frame black (FilmCaptureLayer.Serve)
    private static bool IsGroundOnGrey(Color c) => Math.Abs(c.R - 80) > 12 || Math.Abs(c.G - 80) > 12 || Math.Abs(c.B - 80) > 12;
    private static bool IsGroundOnBlack(Color c) => c.R + c.G + c.B > 45;

    private static int Count(Image image, Func<Color, bool> what, (int X, int Y)? near = null, int radius = 0)
    {
        int n = 0;
        int x0 = 0, y0 = 0, x1 = image.Width, y1 = image.Height;
        if (near is { } p) { x0 = Math.Max(0, p.X - radius); y0 = Math.Max(0, p.Y - radius); x1 = Math.Min(image.Width, p.X + radius); y1 = Math.Min(image.Height, p.Y + radius); }
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (what(Raylib.GetImageColor(image, x, y))) n++;
        return n;
    }

    /// <summary>The production map (<c>MapInteractionPack.AttachMapLayers</c>) over test-town with a unit at its middle — its symbol drawn by the production
    /// projector (<see cref="Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmo"/>) — a cyan SELECTION box (layer 255) beside it, a pink tactical AREA
    /// (<c>DebugTraceLayers.TacticalGraphics</c>) east of it, and the non-scene clutter.</summary>
    private static (MapCanvas Canvas, Hrot.ScenarioEditor.Map.MapLayers Layers, FilmCaptureLayer Film, Vector3 Unit, Vector3 Area, EntityRepository World) FilmSet(
        Action<Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer, EntityRepository, Vector3>? alsoEmit = null)
    {
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<NetworkIdentity>();
        var query = WorldQuery.Of(world)!;
        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        var unitAt = new Vector3(mid.X, mid.Y, query.GroundHeightAt(mid.X, mid.Y));
        var areaAt = new Vector3(mid.X + 40f, mid.Y, query.GroundHeightAt(mid.X + 40f, mid.Y));
        var unit = world.CreateEntity();
        world.AddComponent(unit, new SimTransform { Position = unitAt, Rotation = Quaternion.Identity });
        world.AddComponent(unit, new NetworkIdentity(77));

        var canvas = new MapCanvas(input: null);
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer(Hrot.ScenarioEditor.Map.MapInteractionContext.DefaultBufferCapacity);
        var layers = Hrot.ScenarioEditor.Map.MapInteractionPack.AttachMapLayers(canvas, buffer, new FdpEventBus(), () => world);
        canvas.AddLayer(new Clutter2D { At = new Vector2(mid.X, mid.Y - 30f) });
        var film = canvas.Layers.OfType<FilmCaptureLayer>().Single();

        new Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmo().Draw(world, unit, buffer);
        var selection = Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeBox2D(new Vector2(mid.X - 30f, mid.Y), new Vector2(6f, 6f), Cyan,
            sizeMode: Fdp.Toolkit.Diagnostics.Gizmos.SizeMode.WorldMeters, fillColor: Cyan);
        selection.DebugLayer = Fdp.Toolkit.Diagnostics.Gizmos.DebugTraceLayers.Selection;
        buffer.AppendRaw(selection);
        var a = new Vector2(areaAt.X - 10f, areaAt.Y - 10f);
        var b = new Vector2(areaAt.X + 10f, areaAt.Y - 10f);
        var c = new Vector2(areaAt.X + 10f, areaAt.Y + 10f);
        var d = new Vector2(areaAt.X - 10f, areaAt.Y + 10f);
        buffer.DrawFilledTriangle(a, b, c, Pink, layer: Fdp.Toolkit.Diagnostics.Gizmos.DebugTraceLayers.TacticalGraphics);
        buffer.DrawFilledTriangle(a, c, d, Pink, layer: Fdp.Toolkit.Diagnostics.Gizmos.DebugTraceLayers.TacticalGraphics);
        alsoEmit?.Invoke(buffer, world, unitAt);
        return (canvas, layers, film, unitAt, areaAt, world);
    }

    /// <summary>Holds the shot, draws the window's frame (returned as an image, the caller unloads it), then captures the same frame size.</summary>
    private static (Image Window, Image Film) WindowAndFilm(UiFrameSession frame, MapCanvas canvas, FilmCaptureLayer film, string path, int scale = 1)
    {
        for (int i = 0; i < 3; i++)
        {
            film.Update(0.016f);
            canvas.Camera.Update(0.016f);   // the director places the 3-D camera in its update (MapCanvas.Update's first step)
            frame.Step(canvas.Draw);
        }
        var window = Raylib.LoadImageFromScreen();
        if (File.Exists(path)) File.Delete(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Raylib.ExportImage(window, Path.ChangeExtension(path, null) + "-window.png");   // evidence: the window beside the film
        var capture = FilmStudio.Capture(new FilmCaptureRequest(path, Raylib.GetScreenWidth() * scale, Raylib.GetScreenHeight() * scale));
        film.Update(0.016f);
        Assert.True(capture.IsCompletedSuccessfully, capture.Exception?.ToString() ?? "the capture was not served");
        return (window, Raylib.LoadImage(path));
    }

    /// <summary>
    /// ⭐ <c>CE-1380</c> — a 2-D FILM frame is the 2-D map as the window draws it: the ground plan's fills (as many as the window shows), the unit's
    /// symbol at the unit's projected position — and NONE of what is not the scene (the window has the cyan clutter and the cyan selection; the film
    /// has neither). 🔴 Mutation: <c>FilmCaptureLayer.Serve</c> back to <c>_canvas.Draw()</c> in 2-D (the as-before path) ⇒ the clutter and the
    /// selection are filmed ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE1380_A2DFilmFrame_IsTheWindowsMap_TheGroundPlanAndTheUnitSymbol_AndNoToolSelectionOrHud()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        FilmStudio.Reset();
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var (canvas, layers, film, unit, _, world) = FilmSet();
        try
        {
            FilmStudio.TwoD = true;
            FilmStudio.Direct(new FilmShot { Pose = new CameraPose(unit, 160f, 0f, -0.6f) });
            var (window, image) = WindowAndFilm(frame, canvas, film, Path.Combine(Path.GetTempPath(), "hrot-film-rail", "film-2d.png"));
            try
            {
                Assert.False(layers.ViewSwitch.Is3D, "the film holds the 2-D map");
                var s = canvas.Camera.WorldToScreen(new Vector2(unit.X, unit.Y));
                var at = ((int)s.X, (int)s.Y);
                Assert.True(Count(window, IsCyan) > 50, "anti-vacuity: the WINDOW shows the clutter and the selection");
                Assert.True(Count(window, IsMagenta, at, 24) > 6, "anti-vacuity: the window shows the unit's symbol at its position");

                int windowGround = Count(window, IsGroundOnGrey), filmGround = Count(image, IsGroundOnBlack);
                Assert.True(filmGround > 20_000, $"the film's ground plan is drawn ({filmGround} non-black pixels)");
                Assert.True(filmGround > windowGround * 0.9, $"the film draws the window's ground plan (film {filmGround}, window {windowGround})");
                Assert.True(Count(image, IsMagenta, at, 24) > 6, $"the unit's symbol is in the film at its position {at}");
                Assert.Equal(0, Count(image, IsCyan));   // no clutter, no selection
            }
            finally { Raylib.UnloadImage(window); Raylib.UnloadImage(image); }
        }
        finally { FilmStudio.Reset(); film.Dispose(); world.Dispose(); }
    }

    /// <summary>
    /// ⭐ <c>CE-1379</c> — a scenario's TACTICAL GRAPHIC (the pink area on <c>TacticalGraphics</c>) is in the film frame in 3-D and in 2-D, where the window
    /// shows it; the SELECTION (layer 255, cyan) never is. 🔴 Mutations: <c>DebugGizmoLayer</c> not an <c>ISceneLayer</c> (the as-before 3-D film) ⇒ no
    /// pink in the films ⇒ red; <c>DebugGizmoLayer.InFilm</c> returning true for every primitive ⇒ the selection is filmed ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE1379_AFilmFrame_DrawsTheScenariosTacticalGraphic_In3DAnd2D_AndNeverTheSelection()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        FilmStudio.Reset();
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var (canvas, layers, film, unit, area, world) = FilmSet();
        try
        {
            // 3-D: from above and south of the unit, looking at the area
            FilmStudio.Direct(FilmShot.Looking(area + new Vector3(-20f, -90f, 70f), area)!);
            var (window3, film3) = WindowAndFilm(frame, canvas, film, Path.Combine(Path.GetTempPath(), "hrot-film-rail", "film-3d-graphic.png"));
            try
            {
                Assert.True(layers.ViewSwitch.Is3D);
                var s = layers.ViewSwitch.Camera3D.WorldToScreen(area);
                var at = ((int)s.X, (int)s.Y);
                Assert.True(Count(window3, IsPink, at, 60) > 100, $"anti-vacuity: the window shows the area at {at}");
                Assert.True(Count(film3, IsPink, at, 60) > 100, $"the 3-D film shows the scenario's area at {at}");
                Assert.Equal(0, Count(film3, IsCyan));
            }
            finally { Raylib.UnloadImage(window3); Raylib.UnloadImage(film3); }

            // 2-D: the same set, from overhead
            FilmStudio.TwoD = true;
            FilmStudio.AnimateViewSwitch = false;
            FilmStudio.Direct(new FilmShot { Pose = new CameraPose(unit, 160f, 0f, -0.6f) });
            var (window2, film2) = WindowAndFilm(frame, canvas, film, Path.Combine(Path.GetTempPath(), "hrot-film-rail", "film-2d-graphic.png"));
            try
            {
                Assert.False(layers.ViewSwitch.Is3D);
                var s = canvas.Camera.WorldToScreen(new Vector2(area.X, area.Y));
                var at = ((int)s.X, (int)s.Y);
                Assert.True(Count(window2, IsCyan) > 50, "anti-vacuity: the window shows the selection");
                Assert.True(Count(window2, IsPink, at, 30) > 100, $"anti-vacuity: the window shows the area at {at}");
                Assert.True(Count(film2, IsPink, at, 30) > 100, $"the 2-D film shows the scenario's area at {at}");
                Assert.Equal(0, Count(film2, IsCyan));
            }
            finally { Raylib.UnloadImage(window2); Raylib.UnloadImage(film2); }
        }
        finally { FilmStudio.Reset(); film.Dispose(); world.Dispose(); }
    }
    /// <summary>
    /// ⭐ <c>CE-1382</c> — an ACTIVE tool's handles are on the window's screen and never in a film frame, while the unit's symbol is in both. The tool is a
    /// REAL one (<c>MeasureGizmo</c>: its cyan crosshair at the cursor) driven by the production arbiter (<c>GlobalGizmoManager.Execute</c> — what moves a
    /// tool's drawing onto <c>DebugTraceLayers.ToolHandles</c>, above the toggled rows a film shows). 🔴 Mutation: drop the
    /// <c>MoveDefaultLayer</c> call from <c>GlobalGizmoManager.Execute</c> (the handles stay on layer 0 with the symbols) ⇒ the crosshair is filmed ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE1382_AFilmFrame_NeverDrawsAnActiveToolsHandles_ButDrawsTheUnitSymbol()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        FilmStudio.Reset();
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        Vector3 cursor = default;
        var (canvas, layers, film, unit, _, world) = FilmSet((buffer, w, unitAt) =>
        {
            cursor = unitAt + new Vector3(30f, 30f, 0f);
            var measure = new Hrot.ScenarioEditor.Gizmos.MeasureGizmo();
            measure.OnDragUpdate(cursor);
            var arbiter = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(buffer);
            arbiter.Register(Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager.NewId(), measure);
            arbiter.Execute(w, 0.016f);
        });
        try
        {
            FilmStudio.TwoD = true;
            FilmStudio.Direct(new FilmShot { Pose = new CameraPose(unit, 160f, 0f, -0.6f) });
            var (window, image) = WindowAndFilm(frame, canvas, film, Path.Combine(Path.GetTempPath(), "hrot-film-rail", "film-2d-tool.png"));
            try
            {
                var tool = canvas.Camera.WorldToScreen(new Vector2(cursor.X, cursor.Y));
                var toolAt = ((int)tool.X, (int)tool.Y);
                var s = canvas.Camera.WorldToScreen(new Vector2(unit.X, unit.Y));
                var unitScreen = ((int)s.X, (int)s.Y);
                Assert.True(Count(window, IsCyan, toolAt, 18) > 10, $"anti-vacuity: the WINDOW shows the tool's crosshair at {toolAt}");
                Assert.True(Count(image, IsMagenta, unitScreen, 24) > 6, $"the unit's symbol is in the film at {unitScreen}");
                Assert.Equal(0, Count(image, IsCyan, toolAt, 18));   // the tool's handles are not filmed
            }
            finally { Raylib.UnloadImage(window); Raylib.UnloadImage(image); }
        }
        finally { FilmStudio.Reset(); film.Dispose(); world.Dispose(); }
    }

    /// <summary>
    /// ⭐ <c>CE-1383</c> — a capture TWICE the window's size has the 2-D plan's outlines and the grid's lines in its bottom-right quadrant, which the
    /// window-sized cull (<c>GetScreenWidth/Height</c>) lost. 🔴 Mutation: <c>FilmCaptureLayer.Serve</c> calls <c>DrawScene()</c> without the target size
    /// (the layers cull to the window again) ⇒ no outline or grid pixels in the quadrant ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE1383_ACaptureLargerThanTheWindow_KeepsThe2DOutlinesAndGridInItsFarQuadrant()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        FilmStudio.Reset();
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var (canvas, layers, film, unit, _, world) = FilmSet();
        try
        {
            FilmStudio.TwoD = true;
            FilmStudio.Direct(new FilmShot { Pose = new CameraPose(unit, 160f, 0f, -0.6f) });
            var (window, image) = WindowAndFilm(frame, canvas, film, Path.Combine(Path.GetTempPath(), "hrot-film-rail", "film-2d-big.png"), scale: 2);
            try
            {
                Assert.Equal((2560, 1600), (image.Width, image.Height));
                int far = QuadrantCount(image, IsOutlineOrGrid, image.Width / 2, image.Height / 2);
                int near = QuadrantCount(image, IsOutlineOrGrid, 0, 0);
                Assert.True(near > 200, $"anti-vacuity: the film's top-left quadrant has outline / grid pixels ({near})");
                Assert.True(far > 200, $"the film's bottom-right quadrant has outline / grid pixels ({far}; top-left {near})");
            }
            finally { Raylib.UnloadImage(window); Raylib.UnloadImage(image); }
        }
        finally { FilmStudio.Reset(); film.Dispose(); world.Dispose(); }
    }

    // ── ⭐ CE-1420 / CE-1381 (§11t "CE-1381 sample") — the 2-D grid in a clean take, and the 2-D ground colour option ──────────────────────────────────

    /// <summary>The production layer control (the gizmo that hands the buffer its mask each frame), standing in for the host's: <paramref name="buffer"/>
    /// gets its mask, as <c>LayerControlGizmo.UpdateAndDraw</c> does each frame.</summary>
    private static void Mask(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer buffer, Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo gizmo)
    {
        var mask = gizmo.ActiveLayers;
        buffer.SetActiveLayers(in mask);
    }

    private static Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo LayerControl() =>
        new(NetIdOf(1), new FdpEventBus(), new StructEdit.Reflection.ComponentEditServiceBuilder().Build());
    private static long NetIdOf(long n) => n;

    private static int Differing(Image a, Image b)
    {
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
        int n = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                var p = Raylib.GetImageColor(a, x, y); var q = Raylib.GetImageColor(b, x, y);
                if (Math.Abs(p.R - q.R) > 2 || Math.Abs(p.G - q.G) > 2 || Math.Abs(p.B - q.B) > 2) n++;
            }
        return n;
    }

    /// <summary>
    /// ⭐ <c>CE-1420</c> — a CLEAN take (the layer control's override: every overlay off, the scene as the person has it) films the 2-D map WITH its grid, as
    /// the screen shows it; a take that switches the grid off films none. The two films differ by the grid's lines only. 🔴 Mutation: drop
    /// <c>"Map.Grid"</c> from <c>LayerControlSets.SceneRows</c> ⇒ the clean take has no grid ⇒ the two films are identical ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE1420_A2DFilmFrame_OfACleanTake_HasTheGrid_AndATakeThatTurnsItOff_HasNone()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        FilmStudio.Reset();
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var gizmo = LayerControl();
        Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer? buf = null;
        var (canvas, layers, film, unit, _, world) = FilmSet((buffer, _, _) => buf = buffer);
        try
        {
            FilmStudio.TwoD = true;
            FilmStudio.Direct(new FilmShot { Pose = new CameraPose(unit, 160f, 0f, -0.6f) });
            string dir = Path.Combine(Path.GetTempPath(), "hrot-film-rail");

            Assert.Empty(gizmo.SetOverride(Array.Empty<string>(), Array.Empty<string>()));   // the clean take, the person's grid on (the default)
            Mask(buf!, gizmo);
            var (w1, withGrid) = WindowAndFilm(frame, canvas, film, Path.Combine(dir, "film-2d-grid.png"));
            Raylib.UnloadImage(w1);
            Assert.True(layers.Grid!.LinesDrawn > 0, "the grid layer drew its lines in the film pass");

            Assert.Empty(gizmo.SetOverride(Array.Empty<string>(), new[] { "Map.Grid" }));      // the same take, the grid switched off
            Mask(buf!, gizmo);
            var (w2, withoutGrid) = WindowAndFilm(frame, canvas, film, Path.Combine(dir, "film-2d-nogrid.png"));
            Raylib.UnloadImage(w2);
            Assert.Equal(0, layers.Grid!.LinesDrawn);
            try
            {
                int differ = Differing(withGrid, withoutGrid);
                Assert.True(differ > 400, $"a clean take's 2-D film frame shows the grid's lines ({differ} pixels differ from the grid-less frame)");
                Assert.True(QuadrantCount(withGrid, IsOutlineOrGrid, 0, 0) > QuadrantCount(withoutGrid, IsOutlineOrGrid, 0, 0), "the lines are neutral grey");
            }
            finally { Raylib.UnloadImage(withGrid); Raylib.UnloadImage(withoutGrid); }
        }
        finally { FilmStudio.Reset(); film.Dispose(); world.Dispose(); }
    }

    /// <summary>
    /// ⭐ <c>CE-1420</c> — the 2-D GROUND COLOUR is OFF by default (the open ground is black, the look of every host today) and, ticked, fills the open ground with the
    /// 3-D ground colour darkened to <see cref="Hrot.ScenarioEditor.Gizmos.TerrainPlan2D.DefaultGroundFillFactor"/> — in the window and in a clean take's film.
    /// 🔴 Mutations: <c>TerrainLayer2D.UploadMesh</c> not adding the ground triangles ⇒ no ground pixels when ticked ⇒ red; the row's default <c>true</c> ⇒ ground
    /// pixels by default ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE1420_The2DGroundColour_IsOffByDefault_AndWhenTickedFillsTheOpenGround_InTheWindowAndInTheFilm()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        FilmStudio.Reset();
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var gizmo = LayerControl();
        Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer? buf = null;
        var (canvas, layers, film, unit, _, world) = FilmSet((buffer, _, _) => buf = buffer);
        try
        {
            var expected = Hrot.ScenarioEditor.Gizmos.TerrainPlan2D.MutedGround(new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(
                TerrainLayer3D.GroundColor.R, TerrainLayer3D.GroundColor.G, TerrainLayer3D.GroundColor.B, 255));
            bool IsGround(Color c) => Math.Abs(c.R - expected.R) <= 3 && Math.Abs(c.G - expected.G) <= 3 && Math.Abs(c.B - expected.B) <= 3;
            FilmStudio.TwoD = true;
            FilmStudio.Direct(new FilmShot { Pose = new CameraPose(unit, 160f, 0f, -0.6f) });
            string dir = Path.Combine(Path.GetTempPath(), "hrot-film-rail");

            // default: the person's set, the row unticked
            Mask(buf!, gizmo);
            var (w0, f0) = WindowAndFilm(frame, canvas, film, Path.Combine(dir, "film-2d-ground-off.png"));
            int offWindow = Count(w0, IsGround), offFilm = Count(f0, IsGround);
            Raylib.UnloadImage(w0); Raylib.UnloadImage(f0);
            Assert.False(layers.Terrain2D!.GroundFilled);
            Assert.True(offWindow < 100 && offFilm < 100, $"off by default: no ground colour (window {offWindow}, film {offFilm})");

            // ticked, then a clean take: the film keeps the person's tick
            gizmo.SetRows(is3D: false, new[] { "Map.GroundFill" }, Array.Empty<string>());
            Assert.Empty(gizmo.SetOverride(Array.Empty<string>(), Array.Empty<string>()));
            Mask(buf!, gizmo);
            var (w1, f1) = WindowAndFilm(frame, canvas, film, Path.Combine(dir, "film-2d-ground-on.png"));
            int onWindow = Count(w1, IsGround), onFilm = Count(f1, IsGround);
            Raylib.UnloadImage(w1); Raylib.UnloadImage(f1);
            Assert.True(layers.Terrain2D!.GroundFilled);
            Assert.True(onWindow > 20_000, $"ticked: the window's open ground is the muted ground colour ({onWindow} pixels of {expected.R},{expected.G},{expected.B})");
            Assert.True(onFilm > 20_000, $"ticked: a clean take's film frame keeps it ({onFilm} pixels)");
        }
        finally { FilmStudio.Reset(); film.Dispose(); world.Dispose(); }
    }

    // grey-ish, neutral pixels over the black ground: the grid's translucent grey lines (and the plan's neutral outlines) — never a coloured fill
    private static bool IsOutlineOrGrid(Color c) => c.R + c.G + c.B > 30 && Math.Abs(c.R - c.G) < 12 && Math.Abs(c.G - c.B) < 12;

    private static int QuadrantCount(Image image, Func<Color, bool> what, int x0, int y0)
    {
        int n = 0;
        for (int y = y0; y < y0 + image.Height / 2; y++)
            for (int x = x0; x < x0 + image.Width / 2; x++)
                if (what(Raylib.GetImageColor(image, x, y))) n++;
        return n;
    }
}
