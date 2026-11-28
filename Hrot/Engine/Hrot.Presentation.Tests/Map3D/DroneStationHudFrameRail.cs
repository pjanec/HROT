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
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Devices.DroneStation;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Definitions.Behavior;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ Simulated devices SD-S2d (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-111, the Mockups table; R-4138 / R-4146) — THE REAL FRAME of the
/// drone ground station's OSD, one per mockup panel (<c>mockups/devices-hud-drone.svg</c> 12 flying · 13 battery low · 14 LINK LOST + failsafe · 15 paused): the
/// production projector run by the production gizmo system into a map buffer, drawn by the 3-D map's gizmo layer BOUND to the station (its screen pass), over
/// the test town. Each frame is measured where the mockup puts each element (the canvas fit: 1280 × 800 ⇒ the 1280 × 720 canvas at y + 40; the mockup's
/// 640 × 360 frame × 2). PNGs to <c>HROT_RAIL_SHOTS</c> as <c>drone-osd-&lt;n&gt;-*.png</c>; SKIPS without a display:
/// <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~DroneStationHudFrameRail</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class DroneStationHudFrameRail
{
    private const int NetId = 4343, W = 1280, H = 800;
    private const float K = DroneStationHudGizmo.K, Top = (H - ViewCanvas.Height) / 2f;

    private static Vector2 At(float mx, float my) => new(mx * K, Top + my * K);

    private static (int n, Vector2 centre) Count(Image image, float cx, float cy, float rx, float ry, Func<Color, bool> match)
    {
        int n = 0; var lo = new Vector2(float.MaxValue); var hi = new Vector2(float.MinValue);
        for (int y = Math.Max(0, (int)(cy - ry)); y < Math.Min(H, (int)(cy + ry)); y++)
            for (int x = Math.Max(0, (int)(cx - rx)); x < Math.Min(W, (int)(cx + rx)); x++)
                if (match(Raylib.GetImageColor(image, x, y))) { n++; var v = new Vector2(x, y); lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); }
        return (n, n > 0 ? (lo + hi) * 0.5f : Vector2.Zero);
    }

    private static bool Gold(Color c) => c.R > 200 && c.G > 170 && c.B < 90;
    private static bool Green(Color c) => c.G > 170 && c.R < 140 && c.B < 140;
    private static bool Red(Color c) => c.R > 190 && c.G < 120 && c.B < 120;
    private static bool Amber(Color c) => c.R > 200 && c.G > 150 && c.G < 210 && c.B < 90;
    private static bool White(Color c) => c.R > 225 && c.G > 225 && c.B > 225;
    private static bool Blue(Color c) => c.B > 140 && c.R < 90 && c.G > 80 && c.G < 130;

    [SkippableFact]
    public void SDS2d_TheRealFrame_OfEachDroneOsdMockupPanel_PutsEachElementAtTheMockupsAnchor()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        var query = WorldQuery.Of(world)!;

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
            var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus()) { CurrentCamera = () => canvas.Camera, BoundSource = NetId };
            canvas.AddLayer(gizmos);
            var sw = new MapViewSwitch(canvas, two);
            sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
            sw.Set(true, animate: false);
            frame.Step(canvas.Draw);
            Map3DFrameRail.StepUntil(frame, canvas, () => terrainLayer.TriangleCount > 1000);
            var spot = Map3DFrameRail.FindOpenGround(terrain, query);
            float ground = query.GroundHeightAt(spot.X, spot.Y);
            // the drone's camera: 42 m up, looking down 30° (the picture is context; the OSD is what is measured)
            sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 42f), 60f, 0f, -0.5f);

            if (!world.IsComponentTypeRegistered<BehaviorState>()) world.RegisterComponent<BehaviorState>();
            if (!world.IsComponentTypeRegistered<DeviceInfo>()) world.RegisterComponent<DeviceInfo>();
            if (!world.IsComponentTypeRegistered<PossessedBy>()) world.RegisterComponent<PossessedBy>();
            if (!world.IsComponentTypeRegistered<NetworkIdentity>()) world.RegisterComponent<NetworkIdentity>();
            if (!world.IsComponentTypeRegistered<SimTransform>()) world.RegisterComponent<SimTransform>();
            if (!world.IsComponentTypeRegistered<SimVelocity>()) world.RegisterComponent<SimVelocity>();
            if (!world.IsComponentTypeRegistered<DroneLink>()) world.RegisterComponent<DroneLink>();
            if (!world.IsComponentTypeRegistered<DroneBattery>()) world.RegisterComponent<DroneBattery>();
            BlueprintTierTable.RegisterAll(world);
            world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(Hrot.Map.Common.HrotEnvironment.CreateTkb());   // the real type 642: the chips' labels and key boxes

            var station = world.CreateEntity();
            world.AddComponent(station, new BehaviorState());
            world.SetAuthority<BehaviorState>(station, true);
            world.AddComponent(station, new DeviceInfo { DeviceType = 642, Behavior = new FixedString64(BehaviorNames.DeviceDroneStation) });
            world.AddComponent(station, new NetworkIdentity { Value = NetId });
            world.AddComponent(station, new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Operated, Epoch = 1 });
            var drone = world.CreateEntity();
            var home = new Vector3(spot.X, spot.Y, ground);
            var registry = new StatelessGizmoRegistry();
            registry.Register(new DroneStationHudGizmo(), typeof(DroneStationHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!.RequiredComponents);
            var system = new StatelessGizmoSystem(registry, buffer);

            void Set<T>(T v) where T : unmanaged { if (world.HasComponent<T>(drone)) world.SetComponent(drone, v); else world.AddComponent(drone, v); }

            Image Render(string name, DroneStationState st, float north, float alt, float heading, float speed, float quality, float battery, PilotPhase phase,
                         bool jammed = false, bool paused = false, long frameNumber = 0, double failsafeUntil = 0)
            {
                var at = new Vector3(spot.X, spot.Y + north, query.GroundHeightAt(spot.X, spot.Y + north) + alt);
                Set(new SimTransform { Position = at, Rotation = WorldRotation.FromHeadingDeg(heading) });
                world.SetAuthority<SimTransform>(drone, true);
                Set(new SimVelocity { Linear = new Vector3(0, speed, 0) });
                Set(new DroneLink { Quality = quality, Jammed = jammed ? (byte)1 : (byte)0, Lost = jammed || quality <= 0f ? (byte)1 : (byte)0, Phase = phase });
                Set(new DroneBattery { Remaining01 = battery });
                UnitMemory.Ref<RemotePilotState>(world, drone) = new RemotePilotState { Ready = 1, Home = home, Phase = phase, FailsafeUntil = (SimInstant)failsafeUntil };
                st.Drone = drone; st.Power = 1;
                UnitMemory.Ref<DroneStationState>(world, station) = st;
                world.SetSingleton(new GlobalTime { FrameNumber = frameNumber, DeltaTime = paused ? 0f : 1f / 60f, TotalTime = 100 });
                buffer.EndFrame(0f);
                system.Execute(world, 0f);
                frame.Step(canvas.Draw);
                frame.Step(canvas.Draw);
                string png = Path.Combine(shots, $"drone-osd-{name}.png");
                frame.Screenshot(png);
                Assert.True(gizmos.ScreenPass.Drawn > 60, $"{name}: the bound view's screen pass drew the OSD ({gizmos.ScreenPass.Drawn} primitives)");
                Assert.Equal(0, gizmos.ScreenPass.Skipped);
                return Raylib.LoadImage(png);
            }

            // the mockup's anchors (its 640 × 360 frame)
            var modeChip = At(14 + 40, 26);           // MANUAL's chip
            var battery = At(626 - 20, 26);            // "78 %"
            var bars = At(480 + 11, 26);
            var caret = At(320, 41);
            var hdgText = At(320, 54);
            var marker = At(22 - 5, 92 + 160f / 3f);   // the gimbal marker at −30°
            var centre = At(320, 180);
            var alt = At(175 + 20, 335);
            var chipRow = At(560, 294 + 11);
            var banner = At(330, 101);
            var lost = At(320, 168);

            // ── 12 · flying ──
            var img = Render("12-flying", new DroneStationState { Mode = DroneStationMode.Flying, Command = RemoteMode.Fly, CameraPitch = -30f * MathF.PI / 180f },
                             180f, 42f, 24f, 8.4f, 1f, 0.78f, PilotPhase.Manual);
            try
            {
                Assert.True(Count(img, modeChip.X, modeChip.Y, 50, 14, Green).n > 150, "the MANUAL chip in green, top-left");
                var b = Count(img, battery.X, battery.Y, 30, 14, Green);
                Assert.True(b.n > 80, $"the battery % in green, top-right ({b.n})");
                Assert.True(Count(img, bars.X, bars.Y, 20, 16, Green).n > 60, "four link bars, green");
                var g = Count(img, caret.X, caret.Y, 12, 8, Gold);
                Assert.True(g.n > 10, "the gold caret under the tape's centre");
                Assert.InRange(g.centre.X, caret.X - 3, caret.X + 3);
                Assert.True(Count(img, hdgText.X, hdgText.Y, 30, 12, Gold).n > 40, "the heading 024 in gold");
                var m = Count(img, marker.X, marker.Y, 12, 14, Gold);
                Assert.True(m.n > 20, $"the gimbal marker at −30° ({m.n})");
                Assert.InRange(m.centre.Y, marker.Y - 4, marker.Y + 4);
                Assert.True(Count(img, centre.X, centre.Y + 20, 6, 22, White).n > 20, "the centre mark's lower arm");
                Assert.True(Count(img, alt.X, alt.Y, 40, 16, White).n > 60, "ALT's value, white");
                Assert.True(Count(img, chipRow.X, chipRow.Y, 140, 18, White).n > 60, "the command chips' labels");
                Assert.True(Count(img, chipRow.X, chipRow.Y, 140, 18, Blue).n < 100, "no command lit");
            }
            finally { Raylib.UnloadImage(img); }

            // ── 13 · battery low ──
            img = Render("13-battery-low", new DroneStationState { Mode = DroneStationMode.Returning, Command = RemoteMode.ReturnHome, CameraPitch = -30f * MathF.PI / 180f },
                         640f, 38f, 204f, 11.2f, 0.75f, 0.12f, PilotPhase.ReturnHome);
            try
            {
                Assert.True(Count(img, battery.X, battery.Y, 30, 14, Red).n > 60, "12 % in red");
                var bn = Count(img, banner.X, banner.Y, 150, 16, Red);
                Assert.True(bn.n > 400, $"the BATTERY LOW banner ({bn.n})");
                Assert.True(Count(img, At(36, 26).X, At(36, 26).Y, 30, 14, Amber).n > 60, "the RTH chip in amber");
                Assert.True(Count(img, At(626 - 20, 305).X, At(626 - 20, 305).Y, 30, 18, Blue).n > 400, "the RTH command lit");
            }
            finally { Raylib.UnloadImage(img); }

            // ── 14 · link lost, jammed ──
            Render("pre-14", new DroneStationState { Mode = DroneStationMode.Flying, Command = RemoteMode.Fly }, 1240f, 51f, 312f, 0f, 1f, 0.64f, PilotPhase.Manual);
            img = Render("14-link-lost", new DroneStationState { Mode = DroneStationMode.Failsafe, Command = RemoteMode.Fly }, 1300f, 60f, 312f, 0f, 0f, 0.64f,
                         PilotPhase.FailsafeHover, jammed: true, failsafeUntil: 103);
            try
            {
                var r = Count(img, lost.X, lost.Y, 130, 26, Red);
                Assert.True(r.n > 800, $"LINK LOST in red, centred ({r.n})");
                Assert.InRange(r.centre.X, lost.X - 12, lost.X + 12);
                Assert.True(Count(img, At(320, 196).X, At(320, 196).Y, 60, 12, Amber).n > 60, "JAMMED in amber");
                Assert.True(Count(img, At(320, 220).X, At(320, 220).Y, 160, 12, White).n > 60, "the failsafe's line");
                Assert.True(Count(img, At(320, 132).X, At(320, 132).Y, 24, 18, Red).n > 150, "the camera-slash glyph");
                Assert.True(Count(img, chipRow.X, chipRow.Y, 140, 18, White).n < 10, "no commands while lost");
                Assert.True(Count(img, modeChip.X + 20, modeChip.Y, 60, 14, Red).n > 150, "the FAILSAFE chip in red");
                var dark = Raylib.GetImageColor(img, 900, 400);
                Assert.True(dark.R + dark.G + dark.B < 260, $"the picture darkened ({dark.R},{dark.G},{dark.B})");
            }
            finally { Raylib.UnloadImage(img); }

            // ── 15 · paused, LAND queued, the gimbal at −45° ──
            img = Render("15-paused", new DroneStationState
            {
                Mode = DroneStationMode.Flying, Command = RemoteMode.Fly, CameraPitch = -45f * MathF.PI / 180f, Queued = DroneStationChannels.Bit(DroneStationChannels.Land),
            }, 180f, 42f, 24f, 8.4f, 1f, 0.78f, PilotPhase.Manual, paused: true);
            try
            {
                var m = Count(img, marker.X, At(0, 172).Y, 12, 14, Gold);
                Assert.True(m.n > 20, "the gimbal marker at −45°");
                Assert.InRange(m.centre.Y, At(0, 172).Y - 4, At(0, 172).Y + 4);
                Assert.True(Count(img, chipRow.X, At(0, 286).Y, 160, 12, Gold).n > 30, "'queued' in gold over the LAND chip");
                Assert.True(Count(img, chipRow.X, chipRow.Y, 160, 18, Blue).n > 400, "the queued LAND chip lit");
            }
            finally { Raylib.UnloadImage(img); }
        }
        finally { GizmoMap.Presentation.DebugPrimitiveRenderer2D.TextFont = fontBefore; }
    }
}
