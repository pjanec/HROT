using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ U-9 (CE-4340; the user's report: "3-D map: the hover label of an entity shows ~15 m away from it — the pick volume is far larger than the entity's real size") — the 3-D hover / pick-tool hit-test measured through the REAL chain:
/// the camera's own pick (<see cref="MapCamera3D.ScreenToWorld3D"/> + the real <see cref="EntityBoxPicker"/> on the drawn box), then <c>DebugGizmoLayer.PickEntity</c> over the entity's REAL pick box (<c>EntityPresentationGizmoShared.EmitPickBox</c> at its footprint).
/// 📐 MEASURED (before): <c>PickEntity</c> read the zoom off the camera's hidden 2-D <c>InnerCamera</c> (unrelated to the 3-D view) and hit-tested the 2-D footprint box (5 m long symbol ⇒ 3 m half) plus 5 px of slack taken at THAT zoom — see the numbers in the assertion message of
/// the reach probe. ⭐ The 3-D hit is what the camera's ray hit: the entity's drawn box plus a pixel margin.
/// </summary>
public sealed class HoverPickVolumeTests
{
    private static readonly Vector2 Viewport = new(1280, 720);

    private static Entity Man(EntityRepository world, Vector3 at, int netId)
    {
        if (!world.IsComponentTypeRegistered<SimTransform>()) world.RegisterComponent<SimTransform>();
        if (!world.IsComponentTypeRegistered<TkbIdentity>()) world.RegisterComponent<TkbIdentity>();
        if (!world.IsComponentTypeRegistered<NetworkIdentity>()) world.RegisterComponent<NetworkIdentity>();
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(TkbEntityTypes.Infantry_Rifleman) });
        world.AddComponent(e, new NetworkIdentity(netId));
        return e;
    }

    /// <summary>How far (metres, along the ground, in the screen's own east direction) from the man's feet the hover still answers him, scanning the screen pixel by pixel as the mouse would. Also reports the pixel margin that corresponds to it.</summary>
    private static (float ReachM, float ReachPx, float MetresPerPixel) Reach(float distance, float innerZoom)
    {
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        var q = WorldQuery.Of(world)!;
        var tkb = Map3DTests.BuiltIn();
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var at = new Vector3(10f, 20f, q.GroundHeightAt(10f, 20f));
        var man = Man(world, at, 42);

        var cam = new MapCamera3D { ViewportOverride = Viewport, LookAt = at + new Vector3(0f, 0f, 0.9f), Distance = distance, Pitch = -0.9f, Yaw = 1.0f };
        cam.Pickers.Add(Map3DPickingTests.Ready(new TerrainPicker(() => WorldQuery.RenderGeometryOf(world))));
        cam.Pickers.Add(new EntityBoxPicker(() => world, () => tkb, bodies, metresPerPixelAt: p => 1f / cam.ZoomAt(p)));

        var two = new Fdp.Toolkit.Vis2D.Components.MapCamera();
        two.ApplyCameraView(new Fdp.Toolkit.Vis2D.Components.MapCameraView { Target = new Vector2(at.X, at.Y), Zoom = innerZoom, SmoothTarget = new Vector2(at.X, at.Y), SmoothZoom = innerZoom });
        var buffer = new DebugPrimitiveBuffer();
        var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: two, worldProvider: () => world) { CurrentCamera = () => cam };
        // the entity's pick box as the projector emits it: its 2-D footprint (no VehicleParams ⇒ 5 m long), WORLD metres
        Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmoShared.EmitPickBox(buffer, 42, at, EntityFootprint.InteractionRadiusMetres(world, man));

        // scan the screen pixel by pixel from his feet outward, as the mouse would; the reach is the farthest pixel at which he still answers, and the ground distance of that pixel
        float reach = 0f, reachPx = 0f;
        var feet = cam.WorldToScreen(at);
        float mpp = cam.MetresPerPixel;
        for (int px = 0; px < 400; px++)
        {
            var pixel = new Vector2(feet.X + px, feet.Y);
            var p = cam.ScreenToWorld3D(pixel);   // sets the camera's LastPick, what the hover reads in 3-D
            if (layer.PickEntity(new Vector2(p.X, p.Y)) is { } hit && hit == man)
            {
                reachPx = px;
                var ground = cam.Pick(pixel, PickFilter.Terrain).Point;
                reach = Vector2.Distance(new Vector2(ground.X, ground.Y), new Vector2(at.X, at.Y));
            }
        }
        return (reach, reachPx, mpp);
    }

    /// <summary>⭐ U-9 — a rifleman 0.5 m wide, at 3 camera distances and 3 inner-camera zooms: the hover answers him only within his DRAWN box plus ~6 px of margin (a few metres at the far distance, the margin being pixels), never the 15 m the report showed. Prints the measurement.
    /// 🔴 Red-proof (compiles): <c>DebugGizmoLayer.PickEntity</c> back on the 2-D footprint test at the hidden 2-D camera's zoom ⇒ the reach is the footprint + slack at that zoom (≫ the box) ⇒ red.</summary>
    [Theory]
    [InlineData(40f, 1f)]
    [InlineData(150f, 0.3f)]
    [InlineData(150f, 3f)]
    [InlineData(400f, 0.3f)]
    [InlineData(600f, 1f)]
    public void U9_TheHover_AnswersTheEntityOnlyWithinItsDrawnBoxAndAPixelMargin(float distance, float innerZoom)
    {
        var (reachM, reachPx, mpp) = Reach(distance, innerZoom);
        float boxHalfPx = 0.5f / mpp;                                   // the man's drawn half-width in pixels (≈ 0.5 m, the kit's box)
        string measured = $"camera {distance:0} m, inner zoom {innerZoom}: hover reach {reachM:0.0} m = {reachPx:0} px of screen ({mpp:0.00} m/px); his box half {boxHalfPx:0.0} px";
        Console.WriteLine("U9 measured: " + measured);
        Assert.True(reachPx <= boxHalfPx + 6f + 4f, "the hover reaches past his drawn box + the 6 px margin — " + measured);
        Assert.True(reachPx >= MathF.Min(boxHalfPx, 4f), "the hover must still answer on him (a far, tiny man keeps his pixel margin) — " + measured);
    }
}
