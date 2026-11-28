using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.AI.Behaviors.Devices.Binoculars;
using Hrot.AI.Behaviors.Devices.Rangefinder;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Definitions.Behavior;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ Simulated devices S1d-3 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10 D-42 / D-75, §3.6 D-38, the Mockups table; R-4138 / R-4146) — THE
/// REAL FRAME of the FPS STATION looking through its devices: a possessed man who operates the binoculars and the rangefinder (their Operated rows); the
/// production projectors run by the production gizmo system into the map's buffer; the station's view cycle binds the map's gizmo layer
/// (<c>PossessionStation.ViewLayer</c>, as the map pack wires it). Panel 1 — bound to the binoculars: the two-eyepiece frame, the mil reticle at the
/// 7× field (and the camera at that field), the zoom list with 7× lit, the name. Panel 6 — switched to the rangefinder: its HUD, the <c>VIEW ▸</c> label
/// with the crosshairs icon in red and the cycle strip (eye · binoculars · rangefinder) with the rangefinder framed. Each element is measured at the
/// mockup's anchor (the canvas fit: 1280 × 800 ⇒ the canvas at y + 40). PNGs to <c>HROT_RAIL_SHOTS</c> as <c>devices-view-&lt;n&gt;-*.png</c>; SKIPS
/// without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~DevicesViewFrameRail</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class DevicesViewFrameRail
{
    private const int W = 1280, H = 800;
    private const float K = BinocularsHudGizmo.K, Top = (H - ViewCanvas.Height) / 2f;

    private static Vector2 At(float mx, float my) => new(mx * K, Top + my * K);

    private static (int n, Vector2 centre) Count(Image image, float cx, float cy, float rx, float ry, Func<Color, bool> match)
    {
        int n = 0; var lo = new Vector2(float.MaxValue); var hi = new Vector2(float.MinValue);
        for (int y = Math.Max(0, (int)(cy - ry)); y < Math.Min(H, (int)(cy + ry)); y++)
            for (int x = Math.Max(0, (int)(cx - rx)); x < Math.Min(W, (int)(cx + rx)); x++)
                if (match(Raylib.GetImageColor(image, x, y))) { n++; var v = new Vector2(x, y); lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); }
        return (n, n > 0 ? (lo + hi) * 0.5f : Vector2.Zero);
    }

    private static bool Gold(Color c) => c.R > 200 && c.G > 160 && c.B < 90;
    private static bool White(Color c) => c.R > 225 && c.G > 225 && c.B > 225;
    private static bool Teal(Color c) => c.G > 150 && c.B > 130 && c.R < 120;
    private static bool Red(Color c) => c.R > 190 && c.G < 120 && c.B < 120;
    private static bool Dark(Color c) => c.R < 30 && c.G < 30 && c.B < 30;

    [SkippableFact]
    public void S1d3_TheRealFrame_ThroughTheBinoculars_AndSwitchingToTheRangefinder_PutsEachElementAtTheMockupsAnchor()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        void Reg<T>() where T : unmanaged { if (!world.IsComponentTypeRegistered<T>()) world.RegisterComponent<T>(); }
        Reg<SimTransform>(); Reg<PossessedBy>(); Reg<BehaviorState>(); Reg<DeviceInfo>(); Reg<NetworkIdentity>(); Reg<DevicePose>();
        Reg<Fdp.Toolkit.Combat.Components.Health>(); Reg<Fdp.Toolkit.Combat.Components.WeaponAmmo>();
        BlueprintTierTable.RegisterAll(world);
        if (!world.HasSingleton<GlobalTime>()) world.SetSingleton(new GlobalTime());
        var query = WorldQuery.Of(world)!;
        var spot = Map3DFrameRail.FindOpenGround(terrain, query);
        var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
        world.SetSingletonManaged(map);
        var me = world.CreateEntity();
        var at = new Vector3(spot.X, spot.Y, query.GroundHeightAt(spot.X, spot.Y));
        world.AddComponent(me, new SimTransform { Position = at, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.05f) });
        world.AddComponent(me, new Fdp.Toolkit.Combat.Components.Health { Current = 90f, Max = 100f });
        world.AddComponent(me, new PossessedBy { StationNodeId = (StationId)0, Mode = PossessionMode.Fps, Epoch = 1 });
        map.Register(1001, me);
        Entity Device(int netId, string behaviour, float fov)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = at + new Vector3(0f, 0f, 1.5f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.05f) });
            world.AddComponent(e, new BehaviorState());
            world.SetAuthority<BehaviorState>(e, true);
            world.AddComponent(e, new DeviceInfo { DeviceType = 78, Behavior = new FixedString64(behaviour) });
            world.AddComponent(e, new NetworkIdentity { Value = netId });
            world.AddComponent(e, new PossessedBy { StationNodeId = (StationId)0, Mode = PossessionMode.Operated, Epoch = 1 });
            world.AddComponent(e, new DevicePose { Orientation = Quaternion.Identity, FovDegrees = fov });
            map.Register(netId, e);
            return e;
        }
        var binoculars = Device(7001, BehaviorNames.DeviceBinoculars, BinocularsZoom.FovDegrees(1));
        var rangefinder = Device(7002, BehaviorNames.DeviceRangefinder, RangefinderBehavior.FovDegrees);
        UnitMemory.Ref<BinocularsState>(world, binoculars) = new BinocularsState { Zoom = 1, Ready = 1 };
        UnitMemory.Ref<RangefinderState>(world, rangefinder) = new RangefinderState();

        using var frame = UiFrameHarness.Begin(W, H);
        LitShader.Reset();
        var fontBefore = GizmoMap.Presentation.DebugPrimitiveRenderer2D.TextFont;
        Fdp.Presentation.Fonts.GizmoFont.Load();
        try
        {
            var canvas = new MapCanvas(input: null);
            var two = new MapCamera();
            canvas.Camera = two;
            var terrainLayer = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world));
            canvas.AddLayer(terrainLayer);
            var buffer = new DebugPrimitiveBuffer(8192);
            var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus()) { CurrentCamera = () => canvas.Camera };
            canvas.AddLayer(gizmos);
            var sw = new MapViewSwitch(canvas, two, new MapCamera3D { ViewportOverride = new Vector2(W, H) });
            sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
            var station = new PossessionStation(sw, () => world) { OverShoulder = false, ViewLayer = gizmos };   // as MapInteractionPack wires it
            canvas.AddLayer(station);
            var registry = new StatelessGizmoRegistry();
            registry.Register(new BinocularsHudGizmo(), typeof(BinocularsHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!.RequiredComponents);
            registry.Register(new RangefinderHudGizmo(), typeof(RangefinderHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!.RequiredComponents);
            var system = new StatelessGizmoSystem(registry, buffer);

            station.Update(1f / 30f);
            Assert.True(station.InFps);
            frame.Step(canvas.Draw);
            Map3DFrameRail.StepUntil(frame, canvas, () => terrainLayer.TriangleCount > 1000);

            Image Render(string name)
            {
                buffer.EndFrame(0f);
                system.Execute(world, 0f);
                sw.Camera3D.Update(1f / 30f);
                frame.Step(canvas.Draw);
                frame.Step(canvas.Draw);
                string png = Path.Combine(shots, $"devices-view-{name}.png");
                frame.Screenshot(png);
                return Raylib.LoadImage(png);
            }

            // ── 1 · through the binoculars (the strip gone: its time passed) ──
            station.CycleView(+1);
            Assert.Equal(binoculars, station.BoundDevice);
            Assert.Equal(7001, gizmos.BoundSource);
            station.Update(PossessionStation.SwitchStripSeconds + 0.1f);
            Assert.False(station.Switching);
            var img = Render("1-binoculars");
            try
            {
                Assert.Equal(BinocularsZoom.FovDegrees(1), sw.Camera3D.FovyDegrees, 3);       // the camera at the bound source's field (7×)
                Assert.True(gizmos.ScreenPass.Drawn > 200, $"the bound view drew the binoculars HUD ({gizmos.ScreenPass.Drawn})");
                Assert.True(Dark(Raylib.GetImageColor(img, 30, 400)) && Dark(Raylib.GetImageColor(img, 640, (int)At(0, 8).Y)), "dark outside the eyepieces");
                Assert.False(Dark(Raylib.GetImageColor(img, (int)At(230 - 60, 160).X, (int)At(230 - 60, 160).Y)), "the left eyepiece is clear");
                var step = At(460 - 14 - 10, BinocularsHudGizmo.StepsBaseline + 18 - 5);         // "7×", right-aligned at 446, baseline 150
                var g = Count(img, step.X, step.Y, 40, 22, Gold);
                Assert.True(g.n > 60, $"7× lit in gold ({g.n})");
                Assert.InRange(g.centre.Y, step.Y - 8, step.Y + 8);
                var five = At(446 - 10, BinocularsHudGizmo.StepsBaseline - 4);
                Assert.Equal(0, Count(img, five.X, five.Y, 30, 12, Gold).n);                     // 5× dim
                Assert.True(Count(img, five.X, five.Y, 30, 12, c => c.R > 100 && c.R < 160 && Math.Abs(c.R - c.B) < 20).n > 10, "5× drawn dim, clear of the VIEW label");
                var name = At(230, BinocularsHudGizmo.NameBaseline - 4);
                Assert.True(Count(img, name.X, name.Y, 120, 18, c => c.R > 170 && c.G > 170 && c.B > 175).n > 60, "BINOCULARS bottom-centre");
                float tick = 259f / 2f + 10f * BinocularsHudGizmo.PxPerMil(BinocularsZoom.FovDegrees(1));
                var ten = At(230 + 4, tick);                                                     // the 10-mil tick's right half, under the centre
                Assert.True(Count(img, ten.X, ten.Y, 8, 3, White).n > 4, "the 10-mil tick at the 7× scale");
                var mark = At(460 - 36 + 7, 78 + 8);
                Assert.True(Count(img, mark.X, mark.Y, 24, 24, Teal).n > 40, "the binoculars mark in teal");
            }
            finally { Raylib.UnloadImage(img); }

            // ── 6 · switched to the rangefinder: its HUD, the VIEW label and the strip ──
            station.CycleView(+1);
            Assert.Equal(rangefinder, station.BoundDevice);
            Assert.True(station.Switching);
            img = Render("6-switching");
            try
            {
                Assert.Equal(RangefinderBehavior.FovDegrees, sw.Camera3D.FovyDegrees, 3);
                var lamp = At(460 - 24, 82);
                Assert.True(Count(img, lamp.X, lamp.Y, 14, 14, c => c.R < 60 && c.G < 60).n > 20, "the rangefinder's HUD (its dark lamp)");
                var label = At(460 - 14 - 60, 104);                                              // the label box, right column, under the lamp
                Assert.True(Count(img, label.X, label.Y, 70, 10, White).n > 60, "LASER RANGEFINDER in the label");
                var icon = Count(img, At(460 - 14 - 165, 104).X, At(460 - 14 - 165, 104).Y, 60, 14, Red);
                Assert.True(icon.n > 30, $"the label's crosshairs in red ({icon.n})");
                float sx = 230f - 45f * 3 + 5f;                                                  // the strip (3 sources)
                var framed = Count(img, At(sx + 180 + 43, 141 + 12).X, At(sx + 180 + 43, 141 + 12).Y, 125, 36, c => c.R > 180 && c.G > 180 && c.B > 190 && c.R < 225);
                Assert.True(framed.n > 40, $"the rangefinder framed in the strip ({framed.n})");
                Assert.True(Count(img, At(sx + 90 + 12, 153).X, At(sx + 90 + 12, 153).Y, 22, 22, c => c.G > 100 && c.B > 90 && c.R < 100).n > 20, "the binoculars' icon, teal (dimmed)");
                Assert.True(Count(img, At(sx + 180 + 12, 153).X, At(sx + 180 + 12, 153).Y, 22, 22, Red).n > 20, "the rangefinder's icon, red");
            }
            finally { Raylib.UnloadImage(img); }

            // ⛔ back to the plain eye: no device HUD, no label, the map's field
            station.CycleView(+1);
            Assert.True(station.BoundDevice.IsNull);
            station.Update(PossessionStation.SwitchStripSeconds + 0.1f);
            img = Render("eye");
            try
            {
                Assert.Equal(0, gizmos.ScreenPass.Drawn);
                Assert.Equal(FpsZoom.NormalFovDegrees, sw.Camera3D.FovyDegrees, 3);
            }
            finally { Raylib.UnloadImage(img); }
        }
        finally { GizmoMap.Presentation.DebugPrimitiveRenderer2D.TextFont = fontBefore; }
    }
}
