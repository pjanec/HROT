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
using Hrot.AI.Behaviors.Devices.Rangefinder;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Definitions.Behavior;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ Simulated devices S1d-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 R2-1, the Mockups table; R-4138 / R-4146) — THE REAL FRAME of the
/// rangefinder's HUD, one per mockup panel (<c>mockups/devices-hud.svg</c> 2 idle · 3 ranged · 4 menu open · 5 paused preview): the production projector
/// run by the production gizmo system into a map buffer, drawn by the 3-D map's gizmo layer BOUND to the device (its screen pass), over the test town.
/// Each frame is measured where the mockup puts each element (the canvas fit: 1280 × 800 ⇒ the 1280 × 720 canvas at y + 40), and an UNBOUND layer
/// over the same buffer draws none of it. PNGs to <c>HROT_RAIL_SHOTS</c> as <c>rangefinder-hud-&lt;n&gt;-*.png</c>; SKIPS without a display:
/// <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~RangefinderHudFrameRail</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class RangefinderHudFrameRail
{
    private const int NetId = 4242, W = 1280, H = 800;
    private const float K = RangefinderHudGizmo.K, Top = (H - ViewCanvas.Height) / 2f;   // the canvas fit (scale 1, letterbox 40)

    private static Vector2 At(float mx, float my) => new(mx * K, Top + my * K);   // a mockup point on the frame

    private sealed class Device : IDisposable
    {
        public readonly EntityRepository W;
        public readonly Entity D;
        public readonly StatelessGizmoSystem Gizmos;
        public Device(EntityRepository world, DebugPrimitiveBuffer buffer)
        {
            W = world;
            if (!W.IsComponentTypeRegistered<BehaviorState>()) W.RegisterComponent<BehaviorState>();
            if (!W.IsComponentTypeRegistered<DeviceInfo>()) W.RegisterComponent<DeviceInfo>();
            if (!W.IsComponentTypeRegistered<PossessedBy>()) W.RegisterComponent<PossessedBy>();
            if (!W.IsComponentTypeRegistered<NetworkIdentity>()) W.RegisterComponent<NetworkIdentity>();
            BlueprintTierTable.RegisterAll(W);
            if (!W.HasSingleton<GlobalTime>()) W.SetSingleton(new GlobalTime());
            D = W.CreateEntity();
            W.AddComponent(D, new BehaviorState());
            W.SetAuthority<BehaviorState>(D, true);
            W.AddComponent(D, new DeviceInfo { DeviceType = 77, Behavior = new FixedString64(BehaviorNames.DeviceRangefinder) });
            W.AddComponent(D, new NetworkIdentity { Value = NetId });
            W.AddComponent(D, new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Operated, Epoch = 1 });
            var registry = new StatelessGizmoRegistry();
            registry.Register(new RangefinderHudGizmo(), typeof(RangefinderHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!.RequiredComponents);
            Gizmos = new StatelessGizmoSystem(registry, buffer);
        }
        public void Dispose() { }
    }

    private static (int n, Vector2 centroid) Count(Image image, int x0, int y0, int x1, int y1, Func<Color, bool> match)
    {
        // the count and the CENTRE of the matching pixels' bounding box (a glyph run's mass is not its centre: "— m" weighs right)
        int n = 0; var lo = new Vector2(float.MaxValue); var hi = new Vector2(float.MinValue);
        for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
                if (match(Raylib.GetImageColor(image, x, y))) { n++; var v = new Vector2(x, y); lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); }
        return (n, n > 0 ? (lo + hi) * 0.5f : Vector2.Zero);
    }

    private static bool Gold(Color c) => c.R > 200 && c.G > 160 && c.B < 90;
    private static bool Red(Color c) => c.R > 200 && c.G < 110 && c.B < 100;
    private static bool White(Color c) => c.R > 225 && c.G > 225 && c.B > 225;
    private static bool Blue(Color c) => c.B > 140 && c.R < 90 && c.G > 80 && c.G < 130;
    private static bool Amber(Color c) => c.R > 200 && c.G > 150 && c.G < 210 && c.B < 90;

    [SkippableFact]
    public void S1d2_TheRealFrame_OfEachRangefinderMockupPanel_PutsEachElementAtTheMockupsAnchor_AndAnUnboundViewDrawsNone()
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
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, query.GroundHeightAt(spot.X, spot.Y) + 1.7f), 60f, 0f, -0.05f);   // eye height, the horizon ahead
        using var dev = new Device(world, buffer);

        Image Render(string name, RangefinderState st, long frameNumber = 0)
        {
            UnitMemory.Ref<RangefinderState>(world, dev.D) = st;
            world.SetSingleton(new GlobalTime { FrameNumber = frameNumber });
            buffer.EndFrame(0f);
            dev.Gizmos.Execute(world, 0f);
            frame.Step(canvas.Draw);
            frame.Step(canvas.Draw);
            string png = Path.Combine(shots, $"rangefinder-hud-{name}.png");
            frame.Screenshot(png);
            Assert.True(gizmos.ScreenPass.Drawn > 60, $"{name}: the bound view's screen pass drew the HUD ({gizmos.ScreenPass.Drawn} primitives)");
            Assert.Equal(0, gizmos.ScreenPass.Skipped);
            return Raylib.LoadImage(png);
        }

        // the anchors, in frame pixels
        var range = At(230, 259 - 28 - 9);                      // the range readout's glyph centre (24 px text, baseline 231)
        var lamp = At(460 - 24, 82);
        var bracket = At(14 + 8, 14);                           // the top-left bracket's horizontal arm
        var chips = At(321, 259 - 27);                          // the MODE chip (the line is right-aligned at 446: EXIT · POWER · MODE from the right)
        int r = (int)(12 * K);

        // ── 2 · idle ──
        var img = Render("2-idle", new RangefinderState());
        try
        {
            Assert.True(Raylib.GetImageColor(img, 4, 4).R < 40, "outside the window is dark");
            Assert.True(Count(img, (int)bracket.X - 30, (int)bracket.Y - 4, (int)bracket.X + 30, (int)bracket.Y + 4, White).n > 30, "the top-left corner bracket");
            var g = Count(img, (int)(range.X - 120), (int)(range.Y - 45), (int)(range.X + 120), (int)(range.Y + 45), Gold);
            Assert.True(g.n > 150, $"'— m' in gold at the bottom centre ({g.n})");
            Assert.InRange(g.centroid.X, range.X - 12, range.X + 12);
            Assert.Equal(0, Count(img, (int)lamp.X - r, (int)lamp.Y - r, (int)lamp.X + r, (int)lamp.Y + r, Red).n);   // the lamp is dark
            Assert.True(Count(img, (int)chips.X - 50, (int)chips.Y - 30, (int)chips.X + 50, (int)chips.Y + 30, Blue).n > 200, "the MODE chip highlighted");
        }
        finally { Raylib.UnloadImage(img); }

        // ── 3 · ranged: the lamp lit (blink phase on), 452 m ──
        img = Render("3-ranged", new RangefinderState { HasRange = 1, LastRange = 452.4f, Ranging = 1 });
        try
        {
            var red = Count(img, (int)lamp.X - 16, (int)lamp.Y - r, (int)lamp.X + r, (int)lamp.Y + r, Red);   // right of the red LASER label
            Assert.True(red.n > 80, $"the LASER lamp lit ({red.n})");
            Assert.InRange(red.centroid.X, lamp.X - 4, lamp.X + 4);
            Assert.InRange(red.centroid.Y, lamp.Y - 4, lamp.Y + 4);
            // ⭐ S1d-3 — the glow is ONE round ring whatever the scene behind (S1d-2's render read a scene window through it as a dark-red square):
            //   eight points on the ring between the dot and the glow's edge are the same colour
            int lo = 255, hi = 0;
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.PI / 4f, rr = 7.2f * K;
                var c = Raylib.GetImageColor(img, (int)(lamp.X + rr * MathF.Cos(a)), (int)(lamp.Y + rr * MathF.Sin(a)));
                lo = Math.Min(lo, c.R + c.G + c.B); hi = Math.Max(hi, c.R + c.G + c.B);
                Assert.True(c.R > c.G + 15, $"the glow is red at {k * 45}° ({c.R},{c.G},{c.B})");
            }
            Assert.True(hi - lo < 40, $"the glow ring is even ({lo}..{hi})");
            var g = Count(img, (int)(range.X - 150), (int)(range.Y - 45), (int)(range.X + 150), (int)(range.Y + 45), Gold);
            Assert.True(g.n > 400, $"'452 m' in gold ({g.n})");
            Assert.InRange(g.centroid.X, range.X - 12, range.X + 12);
        }
        finally { Raylib.UnloadImage(img); }

        // ── 4 · menu open on MODE ──
        img = Render("4-menu", new RangefinderState { MenuOpen = 1, Menu = RangefinderMenuItem.Mode });
        try
        {
            var popup = At(24 + 75, 150 + 22 + 10);                // the cursor row (CONTINUOUS)
            Assert.True(Count(img, (int)popup.X - 180, (int)popup.Y - 25, (int)popup.X + 180, (int)popup.Y + 25, Blue).n > 2000, "the popup's cursor row");
            var check = At(24 + 150 - 18, 150 + 10);
            Assert.True(Count(img, (int)check.X - 25, (int)check.Y - 25, (int)check.X + 25, (int)check.Y + 25, c => c.G > 170 && c.R < 140 && c.B < 140).n > 30, "✓ on SINGLE");
            Assert.True(Count(img, (int)chips.X - 50, (int)chips.Y - 30, (int)chips.X + 50, (int)chips.Y + 30, Gold).n > 40, "the MODE chip framed gold");
        }
        finally { Raylib.UnloadImage(img); }

        // ── 5 · paused: a preview range, CONTINUOUS ──
        img = Render("5-paused", new RangefinderState { Mode = RangefinderMode.Continuous, HasRange = 1, LastRange = 452f, RangePreview = 1, Ranging = 1 });
        try
        {
            var tag = At(230 + 50 + 30, 259 - 44);
            Assert.True(Count(img, (int)tag.X - 70, (int)tag.Y - 25, (int)tag.X + 70, (int)tag.Y + 25, Amber).n > 80, "the PREVIEW tag right of the range");
        }
        finally { Raylib.UnloadImage(img); }

        // ⛔ the same buffer through an UNBOUND view (the plain eye): no HUD at all
        gizmos.BoundSource = HudScopes.None;
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        Assert.Equal(0, gizmos.ScreenPass.Drawn);
        string unbound = Path.Combine(shots, "rangefinder-hud-unbound.png");
        frame.Screenshot(unbound);
        img = Raylib.LoadImage(unbound);
        try { Assert.True(Raylib.GetImageColor(img, 640, 400).R > 40 || Raylib.GetImageColor(img, 640, 400).B > 40, "no dark window: the plain view"); }
        finally { Raylib.UnloadImage(img); }
        }
        finally { GizmoMap.Presentation.DebugPrimitiveRenderer2D.TextFont = fontBefore; }   // the font dies with this window: never left to the next rail
    }
}
