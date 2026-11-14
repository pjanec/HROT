using System;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.ScenarioEditor.Gizmos;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ U-8 (CE-4339; the user's report: "3-D map: Paste arms the placement tool; a click-and-drag sets the orientation but NO direction gizmo is drawn — a line from the placement centre toward the drag is expected while dragging") — the REAL placement tool through the REAL
/// 3-D gizmo layer in a real frame (the entity placement tool and Paste share ONE <see cref="PlacementSession"/> and ONE <see cref="PlacementDirectionLine"/>): with the button UP no direction line; with the button DOWN and dragged 25 m east of the press point, amber pixels run along the screen line from
/// the press point to the cursor, and the ghost stands at the PRESS point. 🔴 Red-proof (compiles): the tool not calling <c>PlacementDirectionLine.Draw</c> ⇒ no amber pixels on the line ⇒ red. 📷 <c>HROT_RAIL_SHOTS</c>/<c>placement-direction-*.png</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class PlacementDirectionFrameRail
{
    private static bool Amber(Color c) => c.R > 215 && c.G is > 150 and < 235 && c.B < 120;

    private static int AmberAlong(Image image, Vector2 a, Vector2 b)
    {
        int n = 0;
        for (int i = 2; i <= 18; i++)   // the middle stretch of the segment, ±3 px across it
        {
            var p = Vector2.Lerp(a, b, i / 20f);
            bool hit = false;
            for (int dy = -3; dy <= 3 && !hit; dy++)
                for (int dx = -3; dx <= 3 && !hit; dx++)
                {
                    int x = (int)p.X + dx, y = (int)p.Y + dy;
                    if (x >= 0 && y >= 0 && x < image.Width && y < image.Height && Amber(Raylib.GetImageColor(image, x, y))) hit = true;
                }
            if (hit) n++;
        }
        return n;
    }

    [SkippableFact]
    public void U8_TheDragDrawsADirectionLine_FromThePressPointTowardTheCursor_In3D()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        var query = WorldQuery.Of(world)!;
        var tkb = Map3DTests.BuiltIn();
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
        Map3DFrameRail.StepUntil(frame, canvas, () => terrainLayer.TriangleCount > 1000);
        for (int i = 0; i < 40 && sw.IsSwitching; i++) { sw.Camera3D.Update(1f / 30f); frame.Step(canvas.Draw); }

        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus()) { CurrentCamera = () => canvas.Camera };
        layer.Renderer3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(layer);

        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        var press = new Vector3(mid.X, mid.Y, query.GroundHeightAt(mid.X, mid.Y));
        var cursor = new Vector3(mid.X + 25f, mid.Y, query.GroundHeightAt(mid.X + 25f, mid.Y));
        sw.Camera3D.Pose = new CameraPose(press + new Vector3(12f, 0f, 0f), 70f, MathF.PI, -0.9f);   // from above, looking down on both points

        var tool = new EntityPlacementGizmo(_ => { }, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, autoPopOnPlace: false);
        void Draw()
        {
            buffer.Clear();
            tool.UpdateAndDraw(world, 0.016f, buffer);
            layer.Update(0.016f);
            frame.Step(canvas.Draw);
            frame.Step(canvas.Draw);
        }

        // ① the tool armed, the cursor over the ground, the button UP: the ghost, no direction line
        tool.OnDragUpdate(cursor);
        Draw();
        string upPath = Path.Combine(shots, "placement-direction-up.png");
        frame.Screenshot(upPath);
        var a = sw.Camera3D.WorldToScreen(press); var b = sw.Camera3D.WorldToScreen(cursor);
        Assert.True(Vector2.Distance(a, b) > 60f, $"the two points are apart on screen ({Vector2.Distance(a, b):0} px)");

        // ② the button DOWN at the press point and dragged 25 m east: the direction line
        tool.OnMouseEvent(MapMouseButton.Left, true, press);
        tool.OnDragUpdate(cursor);
        Draw();
        string downPath = Path.Combine(shots, "placement-direction-dragging.png");
        frame.Screenshot(downPath);

        var up = Raylib.LoadImage(upPath); var down = Raylib.LoadImage(downPath);
        try
        {
            int none = AmberAlong(up, a, b), line = AmberAlong(down, a, b);
            Assert.True(none <= 2, $"no direction line while the button is up ({none} of 17 samples)");
            Assert.True(line >= 14, $"the direction line runs from the press point to the cursor ({line} of 17 samples amber along it)");
        }
        finally { Raylib.UnloadImage(up); Raylib.UnloadImage(down); }
    }
}
