using System;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Editor.UiFrameRail;
using Hrot.ScenarioEditor.Gizmos;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ CE-8603 fidelity (R-4146; mockup <c>docs/designs/aggregate-units/agg-ui-visuals.svg</c> ③) — the REAL <see cref="StochasticUnitGizmo"/> through the REAL 2-D gizmo layer:
/// a friend platoon heading 035° and a hostile company in a real frame. Measured, never eyeballed: inside the friend footprint (away from its frame) the ground is
/// TINTED blue; outside it is the bare ground; the frame's centre is the 2525 friend fill. Saved under <c>HROT_RAIL_SHOTS</c> (⛔ never committed). Skips without a
/// display (run under <c>xvfb-run -a</c>). 🔴 Red-proof (compiles): <c>FillAlpha</c> 0 ⇒ no tint ⇒ red.
/// </summary>
[Collection(Hrot.Presentation.Tests.Map3D.Map3DFrameRail.Serial)]
public sealed class StochasticUnitLookFrameRail
{
    private const int W = 900, H = 560;
    private const float Zoom = 1.6f;
    private static readonly Vector2 View = new(200f, 100f);
    private static readonly Color Ground = new(60, 90, 50, 255);

    private static Vector2 Screen(Vector2 world) => new((world.X - (View.X - W / 2f / Zoom)) * Zoom, ((View.Y + H / 2f / Zoom) - world.Y) * Zoom);

    [SkippableFact]
    public void CE8603_AStochasticPlatoon_IsATintedFootprintWithAFrame_OnTheRealMap()
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
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<EntityInfo>(); w.RegisterComponent<AggregateUnit>(); w.RegisterComponent<UnitSlotTable>();
        w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 10.0 });
        Entity Unit(Vector2 at, string name, ForceId side, byte echelon, float width, float depth, float heading, int men, int down)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = new Vector3(at, 0) });
            w.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
            w.AddComponent(e, new AggregateUnit { Resolution = AggregateResolution.Stochastic, Echelon = echelon, Formation = UnitFormation.Line, Width = width, Depth = depth, Heading = new Bearing(heading) });
            var t = new UnitSlotTable { Count = (byte)men };
            for (int i = 0; i < men; i++) t.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)(i < down ? 0f : 25f) };
            w.AddComponent(e, t);
            return e;
        }
        var plt = Unit(new Vector2(120, 60), "2 Plt", ForceId.Friend, 1, 200, 80, 35, 30, 4);
        var coy = Unit(new Vector2(330, 170), "3 Coy", ForceId.Hostile, 2, 260, 90, 215, 100, 45);

        buffer.DrawBox2D(View, new Vector2(3000f, 3000f), new Rgba32(Ground.R, Ground.G, Ground.B, 255), 0f, 0f, SizeMode.WorldMeters, fillColor: new Rgba32(Ground.R, Ground.G, Ground.B, 255));
        var g = new StochasticUnitGizmo();
        g.Draw(w, plt, buffer); g.Draw(w, coy, buffer);
        layer.Update(0.016f);
        frame.StepN(3, canvas.Draw);
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-agg-look");
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, "agg-footprint.png");
        frame.Screenshot(png);

        var image = Raylib.LoadImage(png);
        try
        {
            // inside the friend footprint, 60 m right of its centre along its frontage (clear of the frame): tinted toward blue
            var inside = Screen(new Vector2(120, 60) + (new Vector2(MathF.Cos(-35f * MathF.PI / 180f), MathF.Sin(-35f * MathF.PI / 180f)) * 60f));
            var outside = Screen(new Vector2(-30, -60));
            var pin = Raylib.GetImageColor(image, (int)inside.X, (int)inside.Y);
            var pout = Raylib.GetImageColor(image, (int)outside.X, (int)outside.Y);
            Assert.True(pin.B > pout.B + 20, $"inside {pin.R},{pin.G},{pin.B} vs ground {pout.R},{pout.G},{pout.B}");
            Assert.InRange(pout.G, Ground.G - 8, Ground.G + 8);
            // the frame's centre: the 2525 friend fill (128, 224, 255), drawn over the footprint
            var c = Screen(new Vector2(120, 60));
            var pf = Raylib.GetImageColor(image, (int)c.X + 8, (int)c.Y + 6);
            Assert.True(pf.R > 100 && pf.G > 190 && pf.B > 230, $"frame {pf.R},{pf.G},{pf.B}");
        }
        finally { Raylib.UnloadImage(image); }
    }
}
