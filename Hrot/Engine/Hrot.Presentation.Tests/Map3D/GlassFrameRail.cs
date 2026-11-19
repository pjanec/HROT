using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-5034</c> GW-3 (📄 docs/DESIGN_Glass_Windows.md §5, mockup <c>docs/UX/glass-windows.svg</c>) — the glass in House A's windows on bt-range, drawn from the
/// replicated window state exactly as a map host holds it: the south facade with an INTACT pane, a pane with ONE crack and one with TWO; the east window BROKEN (the
/// open hole, a few shards in the frame); then the moment a pane turns broken — the shard burst. Frames in <c>HROT_RAIL_SHOTS</c>; the counts are asserted, so the
/// rail is red when the layer draws the wrong state. Skips where there is no display (run under <c>xvfb-run -a</c>).
/// </summary>
public sealed class GlassFrameRail
{
    private static int PaneAt(TerrainWorld t, float x, float y, float z)
    {
        for (int i = 0; i < t.PaneLeaves.Count; i++)
        {
            var p = t.PaneLeaves[i];
            var c = (p.Min + p.Max) * 0.5f;
            if (Vector2.Distance(c, new Vector2(x, y)) < 0.8f && z >= p.BaseZ && z <= p.TopZ) return i;
        }
        throw new InvalidOperationException($"no pane at ({x}, {y}, {z})");
    }

    [SkippableFact]
    public void CE5034_Glass_IntactCrackedBrokenAndTheBurst()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.BtRange();
        Assert.NotEmpty(terrain.PaneLeaves);
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<WindowDamage>();
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 1.0 });

        int ground = PaneAt(terrain, 102.1f, 100f, 1.5f);   // House A, south, ground floor: intact
        int upWest = PaneAt(terrain, 102.1f, 100f, 4.5f);   // upstairs west: one crack
        int upEast = PaneAt(terrain, 107.1f, 100f, 4.5f);   // upstairs east: two cracks
        int east = PaneAt(terrain, 110f, 103.6f, 1.5f);     // the east wall's window: broken
        var w = new WindowDamage { Building = terrain.PaneLeaves[upWest].Building };
        WindowDamages.Hit(ref w, terrain.Panes[upWest].Panel, terrain.Panes[upWest].Opening, 0.34f, 0.58f);
        WindowDamages.Hit(ref w, terrain.Panes[upEast].Panel, terrain.Panes[upEast].Opening, 0.3f, 0.7f);
        WindowDamages.Hit(ref w, terrain.Panes[upEast].Panel, terrain.Panes[upEast].Opening, 0.66f, 0.34f);
        WindowDamages.Break(ref w, terrain.Panes[east].Panel, terrain.Panes[east].Opening);
        var damage = world.CreateEntity();
        world.AddComponent(damage, w);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var sw = new MapViewSwitch(canvas, two);
        var query = WorldQuery.Of(world)!;
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        var glass = new GlassLayer3D(() => world, () => sw.Camera3D.Eye);
        canvas.AddLayer(glass);
        sw.Set(true, animate: false);

        // ① the south facade: intact (ground), one crack, two cracks (upstairs)
        sw.Camera3D.Pose = new CameraPose(new Vector3(104.6f, 100f, 3.2f), 15f, MathF.PI / 2f + 0.12f, -0.08f);   // outside, looking north (yaw = where the camera looks)
        for (int k = 0; k < 3; k++) frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "glass-1-south-intact-cracked.png"));
        Assert.True(glass.PanesDrawn >= 3, $"panes drawn {glass.PanesDrawn}");
        Assert.Equal(3, glass.CracksDrawn);
        Assert.Equal(GlassLayer3D.PaneLook.Glass, GlassLayer3D.LookOf(terrain, DoorStates.Of(world, terrain), upEast, out var two2));
        Assert.Equal((byte)2, two2.Hits);

        // ② the east window: broken — the open hole, shards in its frame; no burst (it was broken when the layer first looked: a late joiner plays none)
        sw.Camera3D.Pose = new CameraPose(new Vector3(110f, 103.6f, 1.5f), 6f, MathF.PI + 0.3f, -0.1f);   // outside, looking west
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "glass-2-east-broken.png"));
        Assert.Equal(1, glass.BrokenDrawn);
        Assert.Equal(0, glass.Bursts);

        // ③ the upstairs east pane takes its third round: broken NOW ⇒ one burst, shards flying out
        var d2 = world.GetComponentRO<WindowDamage>(damage);
        WindowDamages.Hit(ref d2, terrain.Panes[upEast].Panel, terrain.Panes[upEast].Opening, 0.5f, 0.5f);
        world.SetComponent(damage, d2);
        sw.Camera3D.Pose = new CameraPose(new Vector3(107.1f, 100f, 4.3f), 7f, -MathF.PI / 2f + 0.3f, -0.08f);
        double t = 1.0;
        void Tick() { t += 1.0 / 30.0; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = t }); }   // sim time drives the shards (a pause freezes them)
        for (int k = 0; k < 9; k++) { Tick(); frame.Step(canvas.Draw); }
        frame.Screenshot(Path.Combine(shots, "glass-3-the-burst.png"));
        Assert.Equal(1, glass.Bursts);
        Assert.True(glass.ShardsFlying > 0, "shards in the air");
        Assert.Equal(2, glass.BrokenDrawn);   // both broken panes drawn as holes (each within the draw radius)
        int flying = glass.ShardsFlying;
        for (int k = 0; k < 60; k++) { glass.Update(1f / 30f); frame.Step(canvas.Draw); }   // PAUSED: frames go by, sim time does not
        Assert.Equal(flying, glass.ShardsFlying);                                           // the shards wait in the air
        for (int k = 0; k < 60; k++) { Tick(); frame.Step(canvas.Draw); }
        Assert.Equal(0, glass.ShardsFlying);   // the burst is over
    }
}
