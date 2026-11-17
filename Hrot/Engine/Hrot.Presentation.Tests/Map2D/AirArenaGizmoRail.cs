using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.ScenarioEditor.Gizmos;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐⭐ <c>CE-4647</c> (AC-15 step 6; docs/designs/air-attack/DESIGN_Air_Combat.md §3 AC-10, §4d; mockup <c>air-combat-arena.svg</c>) — the air arena's gizmo: its drawing (2-D ring + fill + label, 3-D floor and ceiling rings), its four handles
/// (centre, radius, floor, ceiling) and the ONE validation the form shares. 🔧 Drawn through the data seam <see cref="ArenaShape"/> (the air lane's <c>AirArena</c> component is not merged: re-bind = its projector; see <see cref="AirArenaGizmo"/>).
/// ⛔ The frame tests SKIP without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~AirArenaGizmoRail</c>; the PNGs go to <c>HROT_RAIL_SHOTS</c> as <c>airarena-*.png</c>, never committed.
/// 🔴 Red-proof: the floor ring's emit removed from <see cref="AirArenaGizmo.Draw"/> fails the 3-D frame (no floor).
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class AirArenaGizmoRail
{
    private static readonly ArenaShape Arena = new(new Vector2(1000f, 2000f), 6000f, 150f, 4000f);

    private static int Lines(DebugPrimitiveBuffer b, Rgba32 colour)
    {
        int n = 0; var f = b.GetFrame();
        for (int i = 0; i < f.Length; i++) if (f[i].Shape == DebugPrimitiveShape.Line && f[i].Color.Equals(colour)) n++;
        return n;
    }

    private static int Any(DebugPrimitiveBuffer b, Rgba32 colour)
    {
        int n = 0; var f = b.GetFrame();
        for (int i = 0; i < f.Length; i++) if (f[i].Color.Equals(colour)) n++;
        return n;
    }

    private static int Triangles(DebugPrimitiveBuffer b)
    {
        int n = 0; var f = b.GetFrame();
        for (int i = 0; i < f.Length; i++) if (f[i].Shape == DebugPrimitiveShape.FilledTriangle) n++;
        return n;
    }

    [Fact]
    public void TheDrawing_IsTheTwoRingsTheFillTheTicksAndSpokes_AndTheHandlesOnlyWhileSelected()
    {
        var b = new DebugPrimitiveBuffer(capacity: 1 << 14);
        AirArenaGizmo.Draw(b, Arena, 0f, selected: false);
        Assert.Equal(2 * AirArenaGizmo.Segments, Lines(b, AirArenaGizmo.RingColour));          // the flat ring and the 3-D floor ring
        Assert.Equal(AirArenaGizmo.Segments + 8, Lines(b, AirArenaGizmo.CeilingColour));         // the ceiling ring, four ticks, four floor spokes
        Assert.Equal(AirArenaGizmo.Segments, Triangles(b));                                       // the faint fill
        Assert.Equal(0, Any(b, AirArenaGizmo.HandleColour));                                      // not selected: no handles
        b.Clear();
        AirArenaGizmo.Draw(b, Arena, 0f, selected: true);
        Assert.Equal(4, Any(b, AirArenaGizmo.HandleColour));                                      // a square, a diamond, a flat bar, a tall bar
    }

    [Fact]
    public void TheDrawing_AllocatesNothing_InTheSteadyState()
    {
        var b = new DebugPrimitiveBuffer(capacity: 1 << 14);
        for (int i = 0; i < 20; i++) { b.Clear(); AirArenaGizmo.Draw(b, Arena, 10f, true); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) { b.Clear(); AirArenaGizmo.Draw(b, Arena, 10f, true); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // ⭐ HOT PATH — 0 B
    }

    [Fact]
    public void TheHandles_AreOnTheRing_TheCentreFirst_HitByTheNearest_AndDraggingThemEditsTheFourNumbers()
    {
        Assert.Equal(new Vector3(1000f, 2000f, 5f), AirArenaGizmo.Handles.Position(Arena, ArenaHandle.Centre, 5f));
        Assert.Equal(new Vector3(7000f, 2000f, 0f), AirArenaGizmo.Handles.Position(Arena, ArenaHandle.Radius, 0f));
        var floor = AirArenaGizmo.Handles.Position(Arena, ArenaHandle.Floor, 0f); var ceiling = AirArenaGizmo.Handles.Position(Arena, ArenaHandle.Ceiling, 0f);
        Assert.True(floor.X < 1000f && floor.Y < 2000f && ceiling.X > 1000f && ceiling.Y < 2000f, "south-west and south-east");
        Assert.InRange(Vector2.Distance(new Vector2(floor.X, floor.Y), Arena.Centre), 5999f, 6001f);   // on the ring
        Assert.Equal(ArenaHandle.Centre, AirArenaGizmo.Handles.HitTest(Arena, new Vector2(1010f, 1995f), 40f));
        Assert.Equal(ArenaHandle.Radius, AirArenaGizmo.Handles.HitTest(Arena, new Vector2(6990f, 2020f), 40f));
        Assert.Equal(ArenaHandle.Floor, AirArenaGizmo.Handles.HitTest(Arena, new Vector2(floor.X + 5f, floor.Y), 40f));
        Assert.Equal(ArenaHandle.Ceiling, AirArenaGizmo.Handles.HitTest(Arena, new Vector2(ceiling.X, ceiling.Y - 5f), 40f));
        Assert.Equal(ArenaHandle.None, AirArenaGizmo.Handles.HitTest(Arena, new Vector2(3000f, 3000f), 40f));
        Assert.Equal(new Vector2(500f, 600f), AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.Centre, new Vector2(500f, 600f)).Centre);
        Assert.Equal(3000f, AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.Radius, new Vector2(4000f, 2000f)).Radius);
        Assert.Equal(1000f, AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.Floor, new Vector2(1000f + 4000f, 2000f)).FloorM);        // 4000 m out × 0.25
        Assert.Equal(1500f, AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.Ceiling, new Vector2(1000f + 6000f, 2000f)).CeilingM);
        Assert.Equal(Arena, AirArenaGizmo.Handles.Drag(Arena, ArenaHandle.None, new Vector2(9f, 9f)));
    }

    [Fact]
    public void TheValidation_KeepsTheStageWatchable_RadiusInTheLimits_TheCeilingAboveTheFloor_NaNHealed()
    {
        var c = AirArenaGizmo.Clamp(new ArenaShape(Vector2.Zero, 10f, -5f, 20f));
        Assert.Equal((AirArenaGizmo.MinRadius, 0f), (c.Radius, c.FloorM)); Assert.True(c.CeilingM >= c.FloorM + AirArenaGizmo.MinBand);
        c = AirArenaGizmo.Clamp(new ArenaShape(Vector2.Zero, 1e9f, 5000f, 4000f));
        Assert.Equal(AirArenaGizmo.MaxRadius, c.Radius); Assert.True(c.CeilingM >= c.FloorM + AirArenaGizmo.MinBand, $"{c}");
        c = AirArenaGizmo.Clamp(new ArenaShape(Vector2.Zero, float.NaN, float.NaN, float.NaN));
        Assert.True(float.IsFinite(c.Radius + c.FloorM + c.CeilingM));
        Assert.Equal(Arena, AirArenaGizmo.Clamp(Arena));   // a valid arena is untouched
    }

    [Fact]
    public void TheLabel_IsCultureNeutral_NoDecimalComma_UnderCzech()
    {
        var old = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo("cs-CZ"); Assert.Equal("AIR ARENA  r 6.0 km  floor 150 m  ceiling 4000 m", AirArenaGizmo.LabelOf(Arena)); }
        finally { CultureInfo.CurrentCulture = old; }
    }

    // ── the real frames ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static string ShotsDir()
    {
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-airarena");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static int Count(Image im, Func<Color, bool> p)
    {
        int n = 0;
        for (int y = 0; y < im.Height; y++) for (int x = 0; x < im.Width; x++) if (p(Raylib.GetImageColor(im, x, y))) n++;
        return n;
    }

    private static readonly Color Ground = new(60, 110, 60, 255);
    private static bool IsRing(Color c) => c.B > c.R + 45 && c.B > Ground.B + 45;
    private static bool IsHandle(Color c) => c.R > 220 && c.G > 170 && c.B < 110;

    [SkippableFact]
    public void The2DMap_ShowsTheRingAndTheFaintFill_AndTheFourHandlesOnlyWhenSelected_OnTheRealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        const int W = 1000, H = 620; const float Zoom = 0.05f;   // 20 m a pixel: the 6 km radius is 300 px
        using var frame = UiFrameHarness.Begin(W, H);
        Fdp.Presentation.Fonts.GizmoFont.Load();
        string Render(string name, bool selected)
        {
            var canvas = new MapCanvas(input: null); var camera = new MapCamera();
            var target = new Vector2(Arena.Centre.X - W / 2f / Zoom, Arena.Centre.Y + H / 2f / Zoom);
            camera.ApplyCameraView(new MapCameraView { Target = target, Zoom = Zoom, SmoothTarget = target, SmoothZoom = Zoom });
            canvas.Camera = camera;
            var buffer = new DebugPrimitiveBuffer(capacity: 1 << 16);
            var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
            canvas.AddLayer(layer);
            buffer.DrawBox2D(Arena.Centre, new Vector2(40000f, 40000f), new Rgba32(Ground.R, Ground.G, Ground.B, 255), 0f, 0f, SizeMode.WorldMeters, fillColor: new Rgba32(Ground.R, Ground.G, Ground.B, 255));
            AirArenaGizmo.Draw(buffer, Arena, 0f, selected);
            layer.Update(0.016f); frame.StepN(3, canvas.Draw);
            string png = Path.Combine(ShotsDir(), name); frame.Screenshot(png); return png;
        }
        var unselected = Raylib.LoadImage(Render("airarena-2d.png", false));
        int ring = Count(unselected, IsRing), handles0 = Count(unselected, IsHandle);
        var centre = Raylib.GetImageColor(unselected, W / 2, H / 2);
        Raylib.UnloadImage(unselected);
        var selectedIm = Raylib.LoadImage(Render("airarena-2d-selected.png", true));
        int handles = Count(selectedIm, IsHandle);
        Raylib.UnloadImage(selectedIm);
        Assert.True(ring > 600, $"the dashed ring (300 px radius): {ring} px");
        Assert.True(centre.B > Ground.B + 3 && centre.B < Ground.B + 40, $"inside the ring the ground is faintly blue-tinted ({centre.R},{centre.G},{centre.B})");
        Assert.Equal(0, handles0);
        Assert.True(handles > 150, $"the four handles in amber: {handles} px");
    }

    [SkippableFact]
    public void The3DMap_ShowsTheFloorRingAndTheCeilingRing_JoinedByTicks_OnTheRealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        var query = WorldQuery.Of(world)!;
        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        float gz = query.GroundHeightAt(mid.X, mid.Y);
        var arena = new ArenaShape(mid, 350f, 60f, 260f);
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        Fdp.Presentation.Fonts.GizmoFont.Load();
        var canvas = new MapCanvas(input: null); var two = new MapCamera(); canvas.Camera = two;
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        var buffer = new DebugPrimitiveBuffer(capacity: 1 << 16);
        var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus()) { CurrentCamera = () => canvas.Camera };
        gizmos.Renderer3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(gizmos);
        sw.Set(true, animate: false);
        AirArenaGizmo.Draw(buffer, arena, gz, selected: true);
        sw.Camera3D.Pose = new CameraPose(new Vector3(mid.X, mid.Y, gz + 120f), 850f, MathF.PI * 0.5f - 0.2f, -0.28f);
        frame.Step(canvas.Draw); frame.Step(canvas.Draw);
        string png = Path.Combine(ShotsDir(), "airarena-3d.png"); frame.Screenshot(png);
        Assert.True(gizmos.Renderer3D.LinesDrawn > 200, $"3-D gizmo lines: {gizmos.Renderer3D.LinesDrawn}");
        Assert.Equal(0, gizmos.Renderer3D.Skipped.Total);
        var im = Raylib.LoadImage(png);
        int blue = Count(im, c => c.B > c.R + 50 && c.B > c.G + 15 && c.R < 150 && c.G > 90);
        Raylib.UnloadImage(im);
        Assert.True(blue > 300, $"the rings (blue, dashed): {blue} px");
    }
}
