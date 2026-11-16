using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Common.Editing;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Components;
using Hrot.Presentation.Tests.Map3D;
using Hrot.ScenarioEditor.Gizmos;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ <c>CE-4624</c> MS-U2 fidelity (R-4146; <c>minefields-visuals.svg</c> panels A and B) — the minefield gizmo in REAL frames (Xvfb; the PNGs go to <c>HROT_RAIL_SHOTS</c> as <c>minefield-*.png</c>, never committed):
/// ⓐ the truth (every mine, the dashed outline, the lane), ⓑ a Blue seat with the gate on and the field known at start (the tactical graphic: solid outline, a light fill, edge symbols, the label — never a mine),
/// ⓒ a Blue seat that knows only knowledge CELLS (real <see cref="MinefieldKnowledge"/> records through <see cref="PlaceKnowledgeTable"/>: suspected / known-mined / known-clear tints, NO outline), ⓓ the 3-D view of ⓑ (pickets and
/// red triangle signs on the ground). ⛔ SKIP without a display. 🔴 Red-proof: the cell triangles not emitted in <c>DrawCells</c> goes red on ⓒ (the suspected-cell tint).
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class MinefieldFrameRail
{
    private const int W = 1000, H = 620;
    private const float Zoom = 3f;
    private static readonly Vector2 Anchor = new(1000f, 2000f);
    private static readonly Vector2 View = new(1135f, 2060f);
    private static readonly Color Ground = new(60, 110, 60, 255);
    private static readonly Guid FieldGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly List<Vector2> Outline = new() { new(0, 0), new(230, -20), new(270, 90), new(150, 140), new(20, 120) };

    private static Vector2 Screen(Vector2 world) => new((world.X - (View.X - W / 2f / Zoom)) * Zoom, ((View.Y + H / 2f / Zoom) - world.Y) * Zoom);
    private static bool Near(Color c, int r, int g, int b, int tol) => Math.Abs(c.R - r) <= tol && Math.Abs(c.G - g) <= tol && Math.Abs(c.B - b) <= tol;
    private static bool IsField(Color c) => Near(c, 230, 85, 85, 45);

    private static int Count(Image im, Func<Color, bool> p, int x0 = 0, int y0 = 0, int x1 = int.MaxValue, int y1 = int.MaxValue)
    {
        int n = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(im.Height, y1); y++) for (int x = Math.Max(0, x0); x < Math.Min(im.Width, x1); x++) if (p(Raylib.GetImageColor(im, x, y))) n++;
        return n;
    }

    private static Color At(Image im, Vector2 s) => Raylib.GetImageColor(im, (int)s.X, (int)s.Y);

    private static EntityRepository FieldWorld(out Entity field, float scale = 1f)
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<Minefield>(); w.RegisterComponent<MinefieldStrikes>(); w.RegisterComponent<EntityGuid>(); w.RegisterComponent<MinefieldKnowledge>();
        w.RegisterManagedComponent<EditablePolyline>();
        field = AddField(w, Anchor, scale);
        return w;
    }

    private static Entity AddField(EntityRepository w, Vector2 anchor, float scale)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(anchor, 0f), Rotation = Quaternion.Identity });
        var f = new Minefield { Type = MineType.Mixed, SpacingM = 14f, OrientationDeg = -5f, KnownAtStartSides = 0 };
        f.SetLane(0, new MinefieldLane(anchor + new Vector2(120, -40) * scale, anchor + new Vector2(150, 160) * scale, 4.5f, (byte)(ForceId.Friend + 1), (ushort)(1 << (int)ForceId.Friend)));
        f.LaneCount = 1;
        w.AddComponent(e, f);
        w.AddComponent(e, new EntityGuid(FieldGuid));
        var pts = new List<Vector2>(); foreach (var p in Outline) pts.Add(p * scale);
        w.SetManagedComponent(e, new EditablePolyline { Points = pts });
        return e;
    }

    private static MinefieldGizmo Gizmo(bool gate)
    {
        var r = new GizmoSettingsRegistry();
        var g = new MinefieldGizmo(r);
        r.Write(GizmoSettingsRegistry.ComputeHash(MinefieldGizmo.SideLimitedMap), GizmoSettingValue.From(gate));
        return g;
    }

    private static string ShotsDir()
    {
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-minefield");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Draws one gizmo pass in the 2-D map and returns the frame's PNG path.</summary>
    private static string Render2D(UiFrameSession frame, string name, Action<DebugPrimitiveBuffer> fill)
    {
        var canvas = new MapCanvas(input: null);
        var camera = new MapCamera();
        var target = new Vector2(View.X - W / 2f / Zoom, View.Y + H / 2f / Zoom);
        camera.ApplyCameraView(new MapCameraView { Target = target, Zoom = Zoom, SmoothTarget = target, SmoothZoom = Zoom });
        canvas.Camera = camera;
        var buffer = new DebugPrimitiveBuffer(capacity: 1 << 16);
        var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
        canvas.AddLayer(layer);
        buffer.DrawBox2D(View, new Vector2(4000f, 4000f), new Rgba32(Ground.R, Ground.G, Ground.B, 255), 0f, 0f, SizeMode.WorldMeters, fillColor: new Rgba32(Ground.R, Ground.G, Ground.B, 255));
        fill(buffer);
        layer.Update(0.016f);
        frame.StepN(3, canvas.Draw);
        string png = Path.Combine(ShotsDir(), name);
        frame.Screenshot(png);
        return png;
    }

    [SkippableFact]
    public void TheTruth_TheKnownField_TheKnowledgeCells_AreDrawnByTheRealFrame_EachForItsViewer()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        Fdp.Presentation.Fonts.GizmoFont.Load();

        // ⓐ the truth: the gate off (the default) — every mine, the dashed red outline, the white lane
        var wa = FieldWorld(out var fa);
        var pngA = Render2D(frame, "minefield-a-truth.png", b => Gizmo(false).Draw(wa, fa, b));
        var imA = Raylib.LoadImage(pngA);
        int mines = Count(imA, c => Near(c, 216, 208, 184, 30)), lane = Count(imA, c => c.R > 235 && c.G > 235 && c.B > 235), outlineA = Count(imA, IsField);
        Raylib.UnloadImage(imA);
        Assert.True(mines > 200, $"ⓐ the truth draws every mine (pale marks): {mines} px");
        Assert.True(lane > 100, $"ⓐ and the white lane: {lane} px");
        Assert.True(outlineA > 300, $"ⓐ and the red outline: {outlineA} px");

        // ⓑ a Blue seat, the gate ON, the field known at start: the tactical graphic — solid outline, a light fill, edge symbols, the label; never a mine
        var wb = FieldWorld(out var fb);
        wb.GetComponentRW<Minefield>(fb).KnownAtStartSides = 1 << (int)ForceId.Friend;
        StationMode.Install(wb, () => EditingMode.Live, side: () => ForceId.Friend);
        var pngB = Render2D(frame, "minefield-b-known.png", b => Gizmo(true).Draw(wb, fb, b));
        var imB = Raylib.LoadImage(pngB);
        int minesB = Count(imB, c => Near(c, 216, 208, 184, 30)), outlineB = Count(imB, IsField);
        var inside = At(imB, Screen(Anchor + new Vector2(130, 60)));
        Raylib.UnloadImage(imB);
        Assert.Equal(0, minesB);
        Assert.True(outlineB > 300, $"ⓑ a solid red outline and its symbols: {outlineB} px");
        Assert.True(inside.R > Ground.R + 8 && inside.G < Ground.G, $"ⓑ the field is lightly tinted red inside ({inside.R},{inside.G},{inside.B} vs the ground)");

        // ⓒ a Blue seat that knows only CELLS (real knowledge records): suspected / known-mined / known-clear tints and NO outline
        var wc = FieldWorld(out var fc);
        StationMode.Install(wc, () => EditingMode.Live, side: () => ForceId.Friend);
        var outline = new List<Vector2>(); MinefieldKnowledgeOps.TryOutline(wc, fc, outline, out var anchor);
        var grid = MinefieldKnowledgeOps.Grid(wc.GetComponentRO<Minefield>(fc), anchor, outline);
        var rec = wc.CreateEntity();
        var k = new MinefieldKnowledge();
        k.Header.Subject = FieldGuid; k.Header.SidePlus1 = (byte)(ForceId.Friend + 1); k.Header.SubjectKind = SubjectKind.Entity; k.Header.Kind = PlaceKnowledgeKind.Minefield;
        // the picture of panel A's middle viewer: a blob of SUSPECTED cells round a strike, a KNOWN-MINED row in it, a KNOWN-CLEAR patch the engineers traced
        MinefieldKnowledgeOps.CellOf(grid, anchor + new Vector2(130, 60), out int c0, out int r0);
        for (int dr = -3; dr <= 3; dr++)
            for (int dc = -3; dc <= 3; dc++)
            {
                int cc = c0 + dc, rr = r0 + dr;
                if (cc < 0 || rr < 0 || cc >= grid.Cols || rr >= grid.Rows || !MinePattern.Inside(outline, MinefieldKnowledgeOps.CentreOf(grid, cc, rr))) continue;
                bool isMined = dr == 1 && Math.Abs(dc) <= 1, isClear = dr is -1 or -2 && Math.Abs(dc) <= 1;
                k.Set(cc, rr, isMined ? MineCellKnowledge.KnownMined : isClear ? MineCellKnowledge.KnownClear : MineCellKnowledge.Suspected);
            }
        var cells = new List<(int Col, int Row)> { (c0 + 3, r0), (c0, r0 + 1), (c0, r0 - 1), (c0 + 6, r0) };   // suspected · mined · clear · unknown
        wc.AddComponent(rec, k);
        Assert.Equal(MineCellKnowledge.Suspected, PlaceKnowledgeTable.Of(wc).Knows((int)ForceId.Friend, fc, cells[0].Col, cells[0].Row));   // the table the gizmo reads is real, keyed by ForceId
        var pngC = Render2D(frame, "minefield-c-cells.png", b => Gizmo(true).Draw(wc, fc, b));
        var imC = Raylib.LoadImage(pngC);
        Color Cell(int i) => At(imC, Screen(MinefieldKnowledgeOps.CentreOf(grid, cells[i].Col, cells[i].Row)));
        var (sus, mined, clear, unknown) = (Cell(0), Cell(1), Cell(2), Cell(3));
        int outlineC = Count(imC, IsField, 0, 0, W, H);
        Raylib.UnloadImage(imC);
        bool Differs(Color c) => Math.Abs(c.R - Ground.R) + Math.Abs(c.G - Ground.G) + Math.Abs(c.B - Ground.B) > 25;
        Assert.True(Differs(sus) && sus.R > Ground.R + 10 && sus.G > Ground.G, $"ⓒ a suspected cell is amber-tinted ({sus.R},{sus.G},{sus.B})");
        Assert.True(Differs(mined) && mined.R > Ground.R + 10 && mined.G < Ground.G, $"ⓒ a known-mined cell is red-tinted ({mined.R},{mined.G},{mined.B})");
        Assert.True(Differs(clear) && clear.G > Ground.G, $"ⓒ a known-clear cell is green-tinted ({clear.R},{clear.G},{clear.B})");
        Assert.False(Differs(unknown), $"ⓒ an unknown cell is the bare ground ({unknown.R},{unknown.G},{unknown.B})");
        Assert.True(outlineC < 50, $"ⓒ a side that knows only cells sees no outline: {outlineC} px of the field colour");
    }

    private static Vector2 OpenGround(Fdp.Toolkit.Terrain.TerrainWorld terrain, IWorldQuery query)
    {
        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        for (float r = 0f; r < 400f; r += 6f)
            for (int k = 0; k < 16; k++)
            {
                float a = k * MathF.PI / 8f;
                var p = mid + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                bool open = true;
                for (float dx = -10f; dx <= 120f && open; dx += 6f)
                    for (float dy = -10f; dy <= 70f && open; dy += 6f)
                        open = MathF.Abs(query.SurfaceZ(p.X + dx, p.Y + dy, 0) - query.GroundHeightAt(p.X + dx, p.Y + dy)) < 0.2f;
                if (open) return p;
            }
        return mid;
    }

    [SkippableFact]
    public void TheKnownField_InThe3DView_HasPickets_WithRedTriangleSigns_StandingOnTheGround()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<Minefield>(); world.RegisterComponent<MinefieldStrikes>(); world.RegisterComponent<EntityGuid>(); world.RegisterComponent<MinefieldKnowledge>();
        world.RegisterManagedComponent<EditablePolyline>();
        var query = WorldQuery.Of(world)!;
        var spot = OpenGround(terrain, query);
        var field = AddField(world, spot, 0.4f);
        world.GetComponentRW<Minefield>(field).KnownAtStartSides = 1 << (int)ForceId.Friend;
        StationMode.Install(world, () => EditingMode.Live, side: () => ForceId.Friend);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        Fdp.Presentation.Fonts.GizmoFont.Load();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        var buffer = new DebugPrimitiveBuffer(capacity: 1 << 16);
        var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus()) { CurrentCamera = () => canvas.Camera };
        gizmos.Renderer3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(gizmos);
        sw.Set(true, animate: false);
        Gizmo(true).Draw(world, field, buffer);
        float gz = query.GroundHeightAt(spot.X + 50f, spot.Y + 30f);
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 54f, spot.Y + 30f, gz), 46f, MathF.PI * 0.5f - 0.35f, -0.42f);
        frame.Step(canvas.Draw); frame.Step(canvas.Draw);
        string png = Path.Combine(ShotsDir(), "minefield-d-3d.png");
        frame.Screenshot(png);
        Assert.True(gizmos.Renderer3D.LinesDrawn > 20, $"3-D gizmo lines: {gizmos.Renderer3D.LinesDrawn}");
        Assert.Equal(0, gizmos.Renderer3D.Skipped.Total);
        var im = Raylib.LoadImage(png);
        int red = Count(im, IsField), brown = Count(im, c => Near(c, 80, 60, 40, 22));
        Raylib.UnloadImage(im);
        Assert.True(red > 150, $"red outline and the signs' triangles: {red} px");
        Assert.True(brown > 20, $"the pickets' posts: {brown} px");
    }
}
