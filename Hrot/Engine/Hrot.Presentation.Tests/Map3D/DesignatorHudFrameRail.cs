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
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.AI.Behaviors.Devices.Designator;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Definitions.Behavior;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ Simulated devices SD-S5c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-121, the Mockups table; R-4138 / R-4146) — THE REAL FRAME of the laser
/// target designator's HUD, one per mockup panel (<c>mockups/devices-hud-designator.svg</c> 16 STANDBY + a received target · 17 LASING · 18 COOLING · 19 paused, a
/// lase queued): the production projector run by the production gizmo system into a map buffer, drawn by the 3-D map's gizmo layer BOUND to the device (its screen
/// pass), over the test town. Each frame is measured where the mockup puts each element (the canvas fit: 1280 × 800 ⇒ the 1280 × 720 canvas at y + 40; the mockup's
/// 640 × 360 frame × 2). PNGs to <c>HROT_RAIL_SHOTS</c> as <c>designator-&lt;n&gt;-*.png</c>; SKIPS without a display:
/// <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~DesignatorHudFrameRail</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class DesignatorHudFrameRail
{
    private const int NetId = 4545, W = 1280, H = 800;
    private const float K = DesignatorHudGizmo.K, Top = (H - ViewCanvas.Height) / 2f;

    private static Vector2 At(float mx, float my) => new(mx * K, Top + my * K);

    private static (int n, Vector2 centre) Count(Image image, Vector2 c, float rx, float ry, Func<Color, bool> match)
    {
        int n = 0; var lo = new Vector2(float.MaxValue); var hi = new Vector2(float.MinValue);
        for (int y = Math.Max(0, (int)(c.Y - ry)); y < Math.Min(H, (int)(c.Y + ry)); y++)
            for (int x = Math.Max(0, (int)(c.X - rx)); x < Math.Min(W, (int)(c.X + rx)); x++)
                if (match(Raylib.GetImageColor(image, x, y))) { n++; var v = new Vector2(x, y); lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); }
        return (n, n > 0 ? (lo + hi) * 0.5f : Vector2.Zero);
    }

    private static bool Gold(Color c) => c.R > 200 && c.G > 170 && c.B < 90;
    private static bool Green(Color c) => c.G > 170 && c.R < 140 && c.B < 140;
    private static bool Red(Color c) => c.R > 190 && c.G < 110 && c.B < 110;
    private static bool Amber(Color c) => c.R > 200 && c.G > 150 && c.G < 210 && c.B < 90;
    private static bool White(Color c) => c.R > 225 && c.G > 225 && c.B > 225;
    private static bool Blue(Color c) => c.B > 140 && c.R < 90 && c.G > 80 && c.G < 130;

    [SkippableFact]
    public void SDS5c_TheRealFrame_OfEachDesignatorMockupPanel_PutsEachElementAtTheMockupsAnchor()
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
            // the trainee's eye at 1.7 m, looking level north-east (the picture is context; the HUD is what is measured)
            sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 1.7f), 45f, 0f, -0.02f);

            if (!world.IsComponentTypeRegistered<BehaviorState>()) world.RegisterComponent<BehaviorState>();
            if (!world.IsComponentTypeRegistered<DeviceInfo>()) world.RegisterComponent<DeviceInfo>();
            if (!world.IsComponentTypeRegistered<PossessedBy>()) world.RegisterComponent<PossessedBy>();
            if (!world.IsComponentTypeRegistered<NetworkIdentity>()) world.RegisterComponent<NetworkIdentity>();
            if (!world.IsComponentTypeRegistered<SimTransform>()) world.RegisterComponent<SimTransform>();
            if (!world.IsComponentTypeRegistered<DeviceLinks>()) world.RegisterComponent<DeviceLinks>();
            BlueprintTierTable.RegisterAll(world);
            world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(Hrot.Map.Common.HrotEnvironment.CreateTkb());   // the real types 643 / 640: labels, key boxes, the sender's name

            var device = world.CreateEntity();
            world.AddComponent(device, new BehaviorState());
            world.SetAuthority<BehaviorState>(device, true);
            world.AddComponent(device, new DeviceInfo { DeviceType = 643, Behavior = new FixedString64(BehaviorNames.DeviceDesignator) });
            world.AddComponent(device, new NetworkIdentity { Value = NetId });
            world.AddComponent(device, new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Operated, Epoch = 1 });
            world.AddComponent(device, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground + 1.7f), Rotation = Quaternion.Identity });
            var sender = world.CreateEntity();
            world.AddComponent(sender, new NetworkIdentity { Value = NetId + 1 });
            world.AddComponent(device, DeviceLinks.To(EntityRef.Of(world, sender)));
            var registry = new StatelessGizmoRegistry();
            registry.Register(new DesignatorHudGizmo(), typeof(DesignatorHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!.RequiredComponents);
            var system = new StatelessGizmoSystem(registry, buffer);

            Image Render(string name, DesignatorState st, bool paused = false)
            {
                st.Ready = 1; st.LaseSeconds = 60f; st.CoolSeconds = 30f;
                UnitMemory.Ref<DesignatorState>(world, device) = st;
                world.SetSingleton(new GlobalTime { FrameNumber = 0, DeltaTime = paused ? 0f : 1f / 60f, TotalTime = 100 });
                buffer.EndFrame(0f);
                system.Execute(world, 0f);
                frame.Step(canvas.Draw);
                frame.Step(canvas.Draw);
                string png = Path.Combine(shots, $"designator-{name}.png");
                frame.Screenshot(png);
                Assert.True(gizmos.ScreenPass.Drawn > 40, $"{name}: the bound view's screen pass drew the HUD ({gizmos.ScreenPass.Drawn} primitives)");
                Assert.Equal(0, gizmos.ScreenPass.Skipped);
                return Raylib.LoadImage(png);
            }

            // the mockup's anchors (its 640 × 360 frame)
            var phase = At(92 + 30, 70 - 5);
            var code = At(92 + 16, 89 - 5);
            var lamp = At(590, 52);
            var reticleArm = At(320 + 28, 180);
            var timerBar = At(166, 107.5f);
            var range = At(320, 270 - 9);
            var chipRow = At(440, 307);
            var cue = At(78, 180);
            var corner = At(26 + 8, 26);

            // ── 16 · standby, a target received 14° left, 1 240 m ──
            float a = 14f * MathF.PI / 180f;
            var origin = new Vector3(spot.X, spot.Y, ground + 1.7f);
            var img = Render("16-standby-target", new DesignatorState
            {
                Phase = DesignatorPhase.Standby, Code = 1688, HasTarget = 1, TargetFrom = EntityRef.Of(world, sender),
                Target = new Hrot.AI.Behaviors.Devices.Contracts.TargetLocation { Point = origin + new Vector3(1240f * MathF.Cos(a), 1240f * MathF.Sin(a), 0f), Range = 1240f, FromType = 640 },
            });
            try
            {
                Assert.True(Count(img, phase, 34, 9, Green).n > 120, "STANDBY in green at STATE");
                Assert.True(Count(img, code, 20, 9, Gold).n > 80, "CODE 1688 in gold");
                Assert.True(Count(img, corner, 10, 4, White).n > 20, "the window's top-left bracket");
                Assert.True(Count(img, reticleArm, 18, 3, White).n > 30, "the reticle's right arm, white");
                var c = Count(img, cue, 14, 22, Amber);
                Assert.True(c.n > 150, $"the received-target arrow at the window's left edge ({c.n})");
                Assert.True(Count(img, At(115, 198), 60, 8, Amber).n > 60, "the cue's TARGET line beside it");
                Assert.True(Count(img, range, 30, 14, Gold).n > 60, "RANGE '— m' in gold");
                Assert.True(Count(img, chipRow, 170, 12, White).n > 150, "the chips' labels");
                Assert.True(Count(img, chipRow, 170, 12, Blue).n < 100, "no chip lit");
                Assert.True(Count(img, lamp, 8, 8, Red).n < 5, "the lamp off");
            }
            finally { Raylib.UnloadImage(img); }

            // ── 17 · lasing, 42 s left, 1 240 m ──
            img = Render("17-lasing", new DesignatorState { Phase = DesignatorPhase.Lasing, Code = 1688, LaseFrom = 100 - 18, HasRange = 1, LastRange = 1240f });
            try
            {
                Assert.True(Count(img, phase, 34, 9, Red).n > 100, "LASING in red");
                Assert.True(Count(img, lamp, 8, 8, Red).n > 60, "the LASE lamp lit");
                Assert.True(Count(img, reticleArm, 18, 3, Red).n > 30, "the reticle red");
                var bar = Count(img, timerBar, 146, 6, Red);
                Assert.True(bar.n > 600, $"the lase timer bar ({bar.n})");
                Assert.InRange(bar.centre.X, At(93 + 52, 0).X - 6, At(93 + 52, 0).X + 6);   // 0.7 of 148 from x 93: centred at ~145
                Assert.True(Count(img, range, 60, 14, Gold).n > 150, "RANGE 1 240 m in gold");
                Assert.True(Count(img, chipRow, 170, 12, Blue).n > 400, "LASE lit");
            }
            finally { Raylib.UnloadImage(img); }

            // ── 18 · cooling, 19 s left of 30 ──
            img = Render("18-cooling", new DesignatorState { Phase = DesignatorPhase.Cooling, Code = 1688, CoolFrom = 100 - 11, HasRange = 1, LastRange = 1240f });
            try
            {
                Assert.True(Count(img, phase, 34, 9, Amber).n > 100, "COOLING in amber");
                var bar = Count(img, timerBar, 146, 6, Amber);
                Assert.True(bar.n > 300, $"the cool-down bar ({bar.n})");
                Assert.True(bar.centre.X < At(93 + 40, 0).X, "filled a third");
                Assert.True(Count(img, lamp, 8, 8, Red).n < 5, "the lamp off");
                Assert.True(Count(img, chipRow, 170, 12, Blue).n < 100, "nothing lit");
            }
            finally { Raylib.UnloadImage(img); }

            // ── 19 · paused, a lase queued ──
            img = Render("19-paused-queued", new DesignatorState { Phase = DesignatorPhase.Standby, Code = 1688, Queued = 1 }, paused: true);
            try
            {
                Assert.True(Count(img, phase, 34, 9, Green).n > 120, "still STANDBY");
                Assert.True(Count(img, At(440, 284), 180, 8, Gold).n > 30, "'queued' in gold over the LASE chip");
                Assert.True(Count(img, chipRow, 170, 12, Blue).n > 400, "the queued LASE chip lit");
                Assert.True(Count(img, lamp, 8, 8, Red).n < 5, "no lamp, no lase in the pause");
            }
            finally { Raylib.UnloadImage(img); }
        }
        finally { GizmoMap.Presentation.DebugPrimitiveRenderer2D.TextFont = fontBefore; }
    }
}
