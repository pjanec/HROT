using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Editor.UiFrameRail;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ CE-8613 (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md UI-8, mockup ④) — the DETECTION LEVELS overlay: a stochastic observer's line to each
/// target with the level as 5 boxes. Read from the primitives: a reported pair seen now is a GREEN solid line with 5 boxes, an accumulating one AMBER dashed
/// with its level's boxes, one out of sight GREY; off the layer (the default) nothing; 2-D only; 0 B steady. Then a REAL frame: the meter's green boxes and
/// the amber line are on the map. 🔴 Red-proof (compiles): <c>StateOf</c> ignoring <c>Reported</c> ⇒ the contact pair is amber ⇒ red.
/// </summary>
[Collection(Hrot.Presentation.Tests.Map3D.Map3DFrameRail.Serial)]
public sealed class DetectionLevelsFrameRail : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly Entity _observer;

    public DetectionLevelsFrameRail()
    {
        _w.RegisterComponent<SimTransform>(); _w.RegisterComponent<UnitDetections>();
        _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 100.0 });
        Entity At(Vector2 p) { var e = _w.CreateEntity(); _w.AddComponent(e, new SimTransform { Position = new Vector3(p, 0f) }); return e; }
        _observer = At(new Vector2(60, 40));
        var a = At(new Vector2(320, 160)); var b = At(new Vector2(330, 20)); var c = At(new Vector2(120, 190));
        var d = new UnitDetections { Count = 3 };
        d.Pairs[0] = new DetectionPair { Target = a, Level = 1f, Reported = 1, LastSeenAt = new SimInstant(100.0) };   // contact
        d.Pairs[1] = new DetectionPair { Target = b, Level = 0.4f, LastSeenAt = new SimInstant(99.0) };               // building: 2 boxes
        d.Pairs[2] = new DetectionPair { Target = c, Level = 0.6f, Reported = 1, LastSeenAt = new SimInstant(80.0) }; // fading
        _w.AddComponent(_observer, d);
    }

    public void Dispose() => _w.Dispose();

    private static LayerMask256 Mask(bool detection) { var m = new LayerMask256(); for (int i = 0; i < 256; i++) if (detection || i != DebugTraceLayers.DetectionLevels) m.SetBit(i); return m; }

    [Fact]
    public void CE8613_EachPair_IsALineAndAMeter_ColouredByItsState_OnlyWithTheLayer()
    {
        var b = new DebugPrimitiveBuffer(256);
        b.SetActiveLayers(Mask(false));
        new DetectionLevelsGizmo().Draw(_w, _observer, b);
        Assert.Equal(0, b.GetFrame().Length);                                   // off by default: nothing

        b = new DebugPrimitiveBuffer(256);
        b.SetActiveLayers(Mask(true));
        new DetectionLevelsGizmo().Draw(_w, _observer, b);
        var prims = b.GetFrame().ToArray();
        var lines = prims.Where(p => p.Shape == DebugPrimitiveShape.Line).ToArray();
        var meters = prims.Where(p => p.Shape == DebugPrimitiveShape.Icon).ToArray();
        Assert.Equal(3, lines.Length); Assert.Equal(3, meters.Length);
        Assert.Equal(new[] { DetectionLevelsGizmo.Contact, DetectionLevelsGizmo.Building, DetectionLevelsGizmo.Fading }, lines.Select(l => l.Color).ToArray());
        Assert.Equal(new[] { LineStyle.Solid, LineStyle.Dashed, LineStyle.Dashed }, lines.Select(l => l.LineStyle).ToArray());
        Assert.Equal(new[] { "meter/5", "meter/2", "meter/3" }, meters.Select(m => m.TextContent.ToString()).ToArray());
        Assert.All(prims, p => Assert.Equal(PipelineTarget.Map2D | PipelineTarget.FlatOnly, p.TargetView));   // never in 3-D
        Assert.All(prims, p => Assert.Equal(DebugTraceLayers.DetectionLevels, p.DebugLayer));

        var g = new DetectionLevelsGizmo();
        for (int k = 0; k < 3; k++) { b.Clear(); g.Draw(_w, _observer, b); }
        b.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        g.Draw(_w, _observer, b);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [SkippableFact]
    public void CE8613_TheOverlay_DrawsOnTheRealMap()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        const int W = 640, H = 400; const float Zoom = 1.6f;
        var view = new Vector2(190f, 110f);
        using var frame = UiFrameHarness.Begin(W, H);
        Fdp.Presentation.Fonts.GizmoFont.Load();
        var canvas = new MapCanvas(input: null);
        var camera = new MapCamera();
        var target = new Vector2(view.X - W / 2f / Zoom, view.Y + H / 2f / Zoom);
        camera.ApplyCameraView(new MapCameraView { Target = target, Zoom = Zoom, SmoothTarget = target, SmoothZoom = Zoom });
        canvas.Camera = camera;
        var buffer = new DebugPrimitiveBuffer();
        buffer.SetActiveLayers(Mask(true));
        var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
        canvas.AddLayer(layer);
        buffer.DrawBox2D(view, new Vector2(3000f, 3000f), new Rgba32(60, 90, 50, 255), 0f, 0f, SizeMode.WorldMeters, fillColor: new Rgba32(60, 90, 50, 255));
        new DetectionLevelsGizmo().Draw(_w, _observer, buffer);
        layer.Update(0.016f);
        frame.StepN(3, canvas.Draw);
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-agg-look");
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, "agg-detection.png");
        frame.Screenshot(png);
        var image = Raylib.LoadImage(png);
        try
        {
            Vector2 Screen(Vector2 w) => new((w.X - target.X) * Zoom, (target.Y - w.Y) * Zoom);
            int greenAtMeter = 0, amber = 0;
            var meter = Screen((new Vector2(60, 40) + new Vector2(320, 160)) * 0.5f);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    var c = Raylib.GetImageColor(image, x, y);
                    bool green = Math.Abs(c.R - 91) < 20 && Math.Abs(c.G - 208) < 20 && Math.Abs(c.B - 106) < 20;
                    if (green && Math.Abs(x - meter.X) <= 34 && Math.Abs(y - meter.Y) <= 6) greenAtMeter++;
                    if (Math.Abs(c.R - 242) < 20 && Math.Abs(c.G - 192) < 20 && Math.Abs(c.B - 51) < 20) amber++;
                }
            Assert.True(greenAtMeter > 250, $"the contact's meter: five green boxes at the line's middle ({greenAtMeter} px)");
            Assert.True(amber > 60, $"the accumulating pair's amber line and boxes ({amber} px)");
        }
        finally { Raylib.UnloadImage(image); }
    }
}
