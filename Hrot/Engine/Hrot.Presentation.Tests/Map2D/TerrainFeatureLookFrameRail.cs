using System;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.UI.Common.AddEntity;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ CE-5011 (R-4146; mockup <c>docs/UX/terrain-editor.svg</c> panel F) in a REAL frame (Xvfb; saved as <c>ce5011-feature-look.png</c> under <c>HROT_RAIL_SHOTS</c>, never committed): a placed tree is a green handle dot with a WHITE centre; a selected wall is a solid BLUE line, an idle one a dashed
/// wall-coloured one; the drawing ghost of a tree Row is a dot per tree and a wall's a THICK line. 🔴 Mutation: the dot's white centre not drawn ⇒ the white-centre assert goes red.
/// </summary>
[Collection(Hrot.Presentation.Tests.Map3D.Map3DFrameRail.Serial)]
public sealed class TerrainFeatureLookFrameRail
{
    private const int W = 800, H = 420;
    private const float Zoom = 6f;
    private static readonly Vector2 View = new(15f, 10f);
    private static Vector2 Screen(Vector2 world) => new((world.X - (View.X - W / 2f / Zoom)) * Zoom, ((View.Y + H / 2f / Zoom) - world.Y) * Zoom);

    private static int Around(Image im, Vector2 s, int half, Func<Color, bool> p)
    {
        int n = 0;
        for (int y = (int)s.Y - half; y <= (int)s.Y + half; y++) for (int x = (int)s.X - half; x <= (int)s.X + half; x++)
            if (x >= 0 && y >= 0 && x < im.Width && y < im.Height && p(Raylib.GetImageColor(im, x, y))) n++;
        return n;
    }
    private static bool Near(Color c, int r, int g, int b, int tol) => Math.Abs(c.R - r) <= tol && Math.Abs(c.G - g) <= tol && Math.Abs(c.B - b) <= tol;

    [SkippableFact]
    public void CE5011_ATreeIsADotWithAWhiteCentre_ASelectedWallIsSolidBlue_TheGhostIsDotsAndAThickLine()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(W, H);
        Fdp.Presentation.Fonts.GizmoFont.Load();
        var canvas = new MapCanvas(input: null);
        var camera = new MapCamera();
        var target = new Vector2(View.X - W / 2f / Zoom, View.Y + H / 2f / Zoom);
        camera.ApplyCameraView(new MapCameraView { Target = target, Zoom = Zoom, SmoothTarget = target, SmoothZoom = Zoom });
        canvas.Camera = camera;
        var buffer = new DebugPrimitiveBuffer();
        var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
        canvas.AddLayer(layer);

        using var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<TerrainFeature>(); w.RegisterComponent<SelectionState>(); w.RegisterManagedComponent<EditablePolyline>();
        TerrainLock.SetOpen(w, true);
        Entity Make(byte kind, Vector2 at, int net, bool selected, params Vector2[] rel)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = new Vector3(at, 0f), Rotation = Quaternion.Identity }); w.AddComponent(e, new NetworkIdentity(net));
            w.AddComponent(e, new TerrainFeature { Kind = kind }); w.AddComponent(e, new SelectionState { IsSelected = selected });
            if (rel.Length > 0) w.SetManagedComponent(e, new EditablePolyline { Points = rel, Version = 1 });
            return e;
        }
        var tree = Make(TerrainFeature.Tree, new Vector2(0, 30), 1, false);
        var treeSel = Make(TerrainFeature.Tree, new Vector2(10, 30), 2, true);
        var wallIdle = Make(TerrainFeature.Wall, new Vector2(-40, 15), 3, false, new Vector2(0, 0), new Vector2(30, 5));
        var wallSel = Make(TerrainFeature.Wall, new Vector2(20, 10), 4, true, new Vector2(0, 0), new Vector2(40, 5));

        buffer.DrawBox2D(View, new Vector2(2000f, 2000f), new Rgba32(60, 110, 60, 255), 0f, 0f, SizeMode.WorldMeters, fillColor: new Rgba32(60, 110, 60, 255));
        var gizmo = new TerrainFeatureGizmo();
        foreach (var e in new[] { tree, treeSel, wallIdle, wallSel }) gizmo.Draw(w, e, buffer);
        var ghost = new TerrainDrawGhost(() => w);
        var row = TerrainDrawOptions.From("feature:14:tree:Line")!; var wallGhost = TerrainDrawOptions.From("feature:2::Line")!;
        ghost.UnderlayOf(row)(buffer, new[] { new Vector2(-40, -8), new Vector2(30, -8) });
        ghost.UnderlayOf(wallGhost)(buffer, new[] { new Vector2(-40, -18), new Vector2(30, -18) });

        layer.Update(0.016f);
        frame.StepN(3, canvas.Draw);
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-ce5011");
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, "ce5011-feature-look.png");
        frame.Screenshot(png);
        var image = Raylib.LoadImage(png);
        try
        {
            Assert.True(Around(image, Screen(new Vector2(0, 30)), 2, c => c.R > 240 && c.G > 240 && c.B > 240) >= 6, "a tree: a white centre");
            Assert.True(Around(image, Screen(new Vector2(0, 30)), 6, c => Near(c, 91, 208, 106, 40) || Near(c, 45, 104, 53, 40)) >= 20, "…in the family green");
            Assert.True(Around(image, Screen(new Vector2(10, 30)), 8, c => Near(c, 86, 166, 250, 30)) >= 8, "a selected tree: a blue ring");
            int blue = 0; for (int i = 5; i < 95; i++) blue += Around(image, Vector2.Lerp(Screen(new Vector2(20, 10)), Screen(new Vector2(60, 15)), i / 100f), 1, c => Near(c, 86, 166, 250, 40)) > 0 ? 1 : 0;
            Assert.True(blue >= 80, $"a selected wall is a solid blue line ({blue} of 90)");
            int idle = 0; for (int i = 5; i < 95; i++) idle += Around(image, Vector2.Lerp(Screen(new Vector2(-40, 15)), Screen(new Vector2(-10, 20)), i / 100f), 0, c => Near(c, 176, 176, 190, 30)) > 0 ? 1 : 0;
            Assert.InRange(idle, 20, 75);   // dashed: some samples lit, some not
            int thick = Around(image, Screen(new Vector2(-5, -18)), 6, c => c.R > 90 && c.R < 200 && !Near(c, 60, 110, 60, 12));
            Assert.True(thick >= 20, $"the wall's ghost is a THICK line ({thick} px)");
        }
        finally { Raylib.UnloadImage(image); }
    }
}
