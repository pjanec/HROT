using System;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Gizmos;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.ScenarioEditor.Gizmos;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ B-17 P7 ② ③ ① fidelity (R-4146; sheet <c>docs/UX/map-gizmo-look-1.svg</c> rows ① ② ③) — real frames, measured in pixels and saved under <c>HROT_RAIL_SHOTS</c> (⛔ never committed): <b>drawing</b> an area (teal outline, the closing dotted segment, the first point white),
/// <b>editing</b> a route (white square handles, one grabbed amber, the centre a diamond), the placement <b>ghost</b> on the ground (the type\'s footprint box, notch) and an <b>aircraft</b> ghost (the ground ring), and the same drawing + handles + ghost in a <b>3-D</b> frame.
/// Skips without a display (run under <c>xvfb-run -a</c>). 🔴 Red-proofs (compile): the fill / outline colour not applied ⇒ the colour counts go red.
/// </summary>
[Collection(Hrot.Presentation.Tests.Map3D.Map3DFrameRail.Serial)]
public sealed class P7BGizmoLookFrameRail
{
    private const int W = 800, H = 420;
    private const float Zoom = 6f;
    private static readonly Vector2 View = new(15f, 10f);
    private static Vector2 Screen(Vector2 world) => new((world.X - (View.X - W / 2f / Zoom)) * Zoom, ((View.Y + H / 2f / Zoom) - world.Y) * Zoom);

    private static int Count(Image image, int x0, int y0, int x1, int y1, Func<Color, bool> pred)
    {
        int n = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(image.Height, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(image.Width, x1); x++)
                if (pred(Raylib.GetImageColor(image, x, y))) n++;
        return n;
    }
    private static bool Near(Color c, int r, int g, int b, int tol = 14) => Math.Abs(c.R - r) <= tol && Math.Abs(c.G - g) <= tol && Math.Abs(c.B - b) <= tol;
    private static int Around(Image im, Vector2 s, int half, Func<Color, bool> p) => Count(im, (int)s.X - half, (int)s.Y - half, (int)s.X + half + 1, (int)s.Y + half + 1, p);

    private static string Shots(string name)
    {
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-p7-look");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name + ".png");
    }

    private static void Ground(IDebugDrawBuilder draw)
        => draw.DrawBox2D(View, new Vector2(2000f, 2000f), new Rgba32(60, 110, 60, 255), 0f, 0f, SizeMode.WorldMeters, fillColor: new Rgba32(60, 110, 60, 255));

    private static (MapCanvas Canvas, Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer Layer, DebugPrimitiveBuffer Buffer) Stage()
    {
        Fdp.Presentation.Fonts.GizmoFont.Load();
        var canvas = new MapCanvas(input: null);
        var camera = new MapCamera();
        var target = new Vector2(View.X - W / 2f / Zoom, View.Y + H / 2f / Zoom);
        camera.ApplyCameraView(new MapCameraView { Target = target, Zoom = Zoom, SmoothTarget = target, SmoothZoom = Zoom });
        canvas.Camera = camera;
        var buffer = new DebugPrimitiveBuffer();
        var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
        canvas.AddLayer(layer);
        return (canvas, layer, buffer);
    }

    private static Image Render(UiFrameSession frame, MapCanvas canvas, Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer layer, string name)
    {
        layer.Update(0.016f);
        frame.StepN(3, canvas.Draw);
        string png = Shots(name);
        frame.Screenshot(png);
        Console.WriteLine("P7-B frame: " + png);
        return Raylib.LoadImage(png);
    }

    [SkippableFact]
    public void P7B_Drawing_AnAreaIsTeal_TheFirstPointWhite_TheClosingSegmentDotted()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        var (canvas, layer, buffer) = Stage();
        var g = new PointSequenceGizmo(_ => { }, () => { }, minPoints: 3, closed: true, noun: "an area");
        var pts = new[] { new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 20) };
        foreach (var p in pts) g.Session.Add(p);
        g.OnDragUpdate(new Vector3(5, 18, 0));
        Ground(buffer);
        g.UpdateAndDraw(new EntityRepository(), 0.016f, buffer);
        var image = Render(frame, canvas, layer, "p7b-drawing");
        try
        {
            var first = Screen(pts[0]);
            Assert.True(Around(image, first, 5, c => c.R > 245 && c.G > 245 && c.B > 245) >= 40, "the first point is a white square (closing target)");
            var second = Screen(pts[1]);
            Assert.True(Around(image, second, 5, c => Near(c, 0x10, 0x30, 0x2B, 10)) >= 20, "a later point is dark inside");
            // the segment between points 0 and 1 is solid teal; the one from the last point to the cursor is dashed (about half of its samples)
            int solid = 0; for (int i = 5; i < 95; i++) { var s = Vector2.Lerp(Screen(pts[0]), Screen(pts[1]), i / 100f); solid += Around(image, s, 1, c => Near(c, 0x3E, 0xC6, 0xB0, 40)) > 0 ? 1 : 0; }
            Assert.True(solid >= 80, $"a placed segment is solid teal ({solid} of 90 samples)");
            int dashed = 0; for (int i = 2; i < 98; i++) { var s = Vector2.Lerp(Screen(pts[2]), Screen(new Vector2(5, 18)), i / 100f); dashed += Around(image, s, 0, c => Near(c, 0x3E, 0xC6, 0xB0, 60)) > 0 ? 1 : 0; }
            Assert.InRange(dashed, 15, 80);
            int dotted = 0; for (int i = 2; i < 98; i++) { var s = Vector2.Lerp(Screen(new Vector2(5, 18)), Screen(pts[0]), i / 100f); dotted += Around(image, s, 0, c => Near(c, 0x3E, 0xC6, 0xB0, 60)) > 0 ? 1 : 0; }
            Assert.InRange(dotted, 10, 80);   // the closing segment (cursor -> first point) is dotted: fewer lit samples than the dashed one
            Assert.True(Count(image, 0, 0, W, H, c => c.R is >= 55 and <= 70 && c.G is >= 116 and <= 128 && c.B is >= 68 and <= 82) > 1000, "a faint teal fill tints the area");
        }
        finally { Raylib.UnloadImage(image); }
    }

    [SkippableFact]
    public void P7B_Editing_RouteHandlesAreWhiteSquares_OneGrabbedAmber_TheCentreADiamond()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        var (canvas, layer, buffer) = Stage();
        var wp = new[] { new Vector2(0, 0), new Vector2(15, 14), new Vector2(30, 6) };
        Ground(buffer);
        for (int i = 0; i < wp.Length - 1; i++) GizmoEditStyle.Segment(buffer, new Vector3(wp[i], 0f), new Vector3(wp[i + 1], 0f), ShapeKind.Route, SegmentKind.Placed);
        for (int i = 0; i < wp.Length; i++) GizmoEditStyle.Handle(buffer, wp[i], ShapeKind.Route, i == 1 ? HandleState.Grabbed : HandleState.Idle, 4242L, (ushort)(i + 1));
        GizmoEditStyle.Centre(buffer, new Vector2(15, 7), 4242L, 99);
        var image = Render(frame, canvas, layer, "p7b-editing");
        try
        {
            Assert.True(Around(image, Screen(wp[0]), 4, c => c.R > 245 && c.G > 245 && c.B > 245) >= 30, "an idle handle is a white square");
            Assert.True(Around(image, Screen(wp[1]), 4, c => Near(c, 0xF2, 0xC0, 0x33, 12)) >= 30, "the grabbed handle is amber");
            Assert.True(Around(image, Screen(wp[1]), 4, c => c.R > 245 && c.G > 245 && c.B > 245) < 6, "…and not white");
            var cs = Screen(new Vector2(15, 7));
            Assert.True(Around(image, cs, 4, c => Near(c, 0x10, 0x30, 0x2B, 10)) >= 12, "the centre is dark inside");
            Assert.True(Around(image, cs, 8, c => c.R > 245 && c.G > 245 && c.B > 245) >= 12, "…with a white outline");
            // a DIAMOND: the corner of the 10 px box (cx + 5, cy + 5) is background, the tip (cx + 7, cy) is outline
            Assert.True(Around(image, cs + new Vector2(5, 5), 0, c => Near(c, 60, 110, 60, 12)) == 1, "the box corner is empty (a diamond, not a square)");
        }
        finally { Raylib.UnloadImage(image); }
    }

    private static (EntityRepository Repo, long Tank, long Jet) Templates()
    {
        var db = new Fdp.Toolkit.Tkb.TkbDatabase();
        var tank = new Fdp.Interfaces.TkbTemplate("Tank", 777);
        tank.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.Tank, Length = 8f, Width = 3.4f });
        var jet = new Fdp.Interfaces.TkbTemplate("Jet", 778);
        jet.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.PersonalCar, Length = 6f, Width = 7f });
        jet.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.FlightParamsDto { HoverHeightM = 300f, CruiseSpeedMps = 200f });
        db.Register(tank); db.Register(jet);
        var repo = new EntityRepository();
        repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
        return (repo, 777, 778);
    }

    [SkippableFact]
    public void P7B_TheGhost_IsTheFootprintBox_WithANotch_AndAnAircraftAddsAGroundRing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        var (canvas, layer, buffer) = Stage();
        var (repo, tank, jet) = Templates();
        using var _r = repo;
        var at = new Vector3(View.X, View.Y, 0f);

        var g = new EntityPlacementGizmo(_ => { }, tank, autoPopOnPlace: false);
        g.OnDragUpdate(at);
        Ground(buffer);
        g.UpdateAndDraw(repo, 0.016f, buffer);
        var image = Render(frame, canvas, layer, "p7b-ghost-ground");
        try
        {
            var c = Screen(new Vector2(at.X, at.Y));
            int box = Around(image, c, 6, p => p.G > 150 && p.R < 90 && p.B < 90);   // the friendly-green footprint (its fill is the side colour at alpha 100)
            Assert.True(box > 40, $"a translucent footprint box tints the ground at the cursor ({box} px)");
            // 8 m × 3.4 m at 6 px/m: 48 × 20 px, its long axis NORTH (screen vertical) until a drag sets the heading
            int tall = Count(image, (int)c.X - 8, (int)c.Y - 22, (int)c.X + 9, (int)c.Y + 23, p => !Near(p, 60, 110, 60, 6));
            int wide = Count(image, (int)c.X - 26, (int)c.Y - 6, (int)c.X + 27, (int)c.Y + 7, p => !Near(p, 60, 110, 60, 6));
            Assert.True(tall > 300 && tall > 0.6 * (17 * 45), $"the box is tall (north): {tall} px in the vertical strip");
            Assert.True(Count(image, (int)c.X - 8, (int)c.Y - 34, (int)c.X + 9, (int)c.Y - 25, p => p.G > 200 && p.R < 90 && p.B < 90) > 4, "the notch stands beyond the front edge");
            _ = wide;
        }
        finally { Raylib.UnloadImage(image); }

        buffer.Clear();
        var air = new EntityPlacementGizmo(_ => { }, jet, autoPopOnPlace: false);
        air.OnDragUpdate(at);
        Ground(buffer);
        air.UpdateAndDraw(repo, 0.016f, buffer);
        image = Render(frame, canvas, layer, "p7b-ghost-aircraft");
        try
        {
            var c = Screen(new Vector2(at.X, at.Y));
            // the shadow ring: radius max(6,7)/2 = 3.5 m = 21 px, sampled on its circle
            int on = 0; for (int i = 0; i < 90; i++) { float a = i * MathF.PI * 2f / 90f; on += Around(image, c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 21f, 1, p => p.G > 180 && p.R < 90 && p.B < 90) > 0 ? 1 : 0; }
            Assert.True(on >= 60, $"the aircraft's ground ring is drawn ({on} of 90 samples)");
        }
        finally { Raylib.UnloadImage(image); }
    }

    [SkippableFact]
    public void P7B_In3D_TheDrawnAreaHandlesAndGhost_AreDrapedAndPixelSized()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var terrain = Hrot.Presentation.Tests.Map3D.Map3DTests.TestTown();
        var world = Hrot.Presentation.Tests.Map3D.Map3DTests.WorldWith(terrain);
        var query = WorldQuery.Of(world)!;
        var tkb = Hrot.Presentation.Tests.Map3D.Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var terrainLayer = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world));
        canvas.AddLayer(terrainLayer);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        sw.Set(true);
        frame.Step(canvas.Draw);
        Hrot.Presentation.Tests.Map3D.Map3DFrameRail.StepUntil(frame, canvas, () => terrainLayer.TriangleCount > 1000);
        for (int i = 0; i < 40 && sw.IsSwitching; i++) { sw.Camera3D.Update(1f / 30f); frame.Step(canvas.Draw); }

        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus()) { CurrentCamera = () => canvas.Camera };
        layer.Renderer3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(layer);

        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        var press = new Vector3(mid.X, mid.Y, query.GroundHeightAt(mid.X, mid.Y));
        var cursor = new Vector3(mid.X + 25f, mid.Y, query.GroundHeightAt(mid.X + 25f, mid.Y));
        sw.Camera3D.Pose = new CameraPose(press + new Vector3(12f, 0f, 0f), 70f, MathF.PI, -0.9f);   // from above, looking down on both points


        buffer.Clear();
        var g = new PointSequenceGizmo(_ => { }, () => { }, minPoints: 3, closed: true, noun: "an area");
        var o = new Vector2(mid.X, mid.Y);
        foreach (var d in new[] { new Vector2(-12, -8), new Vector2(12, -8), new Vector2(14, 10) }) g.Session.Add(o + d);
        g.OnDragUpdate(new Vector3(o.X - 6, o.Y + 12, query.GroundHeightAt(o.X - 6, o.Y + 12)));
        g.UpdateAndDraw(world, 0.016f, buffer);
        GizmoEditStyle.Handle(buffer, o + new Vector2(-12, 8), ShapeKind.Route, HandleState.Grabbed, 1L, 1);
        layer.Update(0.016f);
        frame.Step(canvas.Draw); frame.Step(canvas.Draw);
        string png = Shots("p7b-3d");
        frame.Screenshot(png);
        Console.WriteLine("P7-B 3-D frame: " + png);
        var image = Raylib.LoadImage(png);
        try
        {
            int teal = Count(image, 0, 0, image.Width, image.Height, c => Near(c, 0x3E, 0xC6, 0xB0, 24));
            int amber = Count(image, 0, 0, image.Width, image.Height, c => Near(c, 0xF2, 0xC0, 0x33, 12));
            // ⭐ P7-C — the Done button is a constant-pixel Button text in 3-D (about 40 × 20 px), not the world-metre slab (thousands of px)
            int blue = Count(image, 0, 0, image.Width, image.Height, c => Near(c, 0x23, 0x41, 0x5F, 8));
            Assert.InRange(blue, 150, 1500);
            Assert.True(teal > 150, $"the drawn area's teal outline and points are in the 3-D frame ({teal} px)");
            Assert.True(amber > 20, $"a grabbed handle's amber square is in the 3-D frame ({amber} px)");
        }
        finally { Raylib.UnloadImage(image); }
    }
}
