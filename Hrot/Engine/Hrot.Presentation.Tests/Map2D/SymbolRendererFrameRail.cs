using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using GizmoMap.Contracts.Symbology;
using GizmoMap.Presentation;
using Hrot.Editor.UiFrameRail;
using Raylib_cs;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ CE-8608 fidelity (R-4146; mockup <c>docs/designs/aggregate-units/agg-ui-visuals.svg</c> ⑦, <c>docs/UX/unit-symbols-visual.svg</c>) — the composed NATO symbol through
/// the REAL 2-D gizmo layer on the north-up map: a friend mechanised platoon (tracked, AT) as a <c>MilStd2525</c> primitive and a hostile armour company as an
/// <c>Icon</c>. Measured: the friend frame's inside is the 2525 friend fill; the platoon's dots stand ABOVE the frame (upright, S-7 — a missing un-flip puts them
/// below); the secondary box is white; the diamond's inside is the hostile fill. Then the cost of 500 symbols drawn in one frame (R-8603) and 0 B.
/// Saved under <c>HROT_RAIL_SHOTS</c> (⛔ never committed). Skips without a display (<c>xvfb-run -a</c>). 🔴 Red-proof (compiles): DrawWorld's rows
/// not un-mirrored on a north-up map ⇒ the dots land below the frame ⇒ red.
/// </summary>
[Collection(Hrot.Presentation.Tests.Map3D.Map3DFrameRail.Serial)]
public sealed class SymbolRendererFrameRail
{
    private const int W = 700, H = 420;
    private const float Zoom = 1.6f;
    private static readonly Vector2 View = new(200f, 100f);
    private static readonly Color Ground = new(60, 90, 50, 255);
    private readonly ITestOutputHelper _out;

    public SymbolRendererFrameRail(ITestOutputHelper output) => _out = output;

    private static Vector2 Screen(Vector2 world) => new((world.X - (View.X - W / 2f / Zoom)) * Zoom, ((View.Y + H / 2f / Zoom) - world.Y) * Zoom);

    private static DebugPrimitive Symbol(DebugPrimitiveShape shape, Vector2 at, string code, float heightPx)
    {
        var p = new DebugPrimitive { Shape = shape, Space = CoordinateSpace.World, TargetView = PipelineTarget.All, Color = new Rgba32(255, 255, 255, 255), ThicknessU16 = (ushort)(heightPx * 10f) };
        p.IconWorldPosX = at.X; p.IconWorldPosY = at.Y;   // = MilWorldPosX/Y (same offsets)
        p.TextContent = new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32(code);           // = SidcCode / IconAtlasCoord
        return p;
    }

    [SkippableFact]
    public void CE8608_TheComposedSymbol_IsDrawnUpright_InTheStandardFills_AndFiveHundredAreCheap()
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

        Vector2 friendAt = new(110, 100), hostileAt = new(300, 100);
        buffer.DrawBox2D(View, new Vector2(3000f, 3000f), new Rgba32(Ground.R, Ground.G, Ground.B, 255), 0f, 0f, SizeMode.WorldMeters, fillColor: new Rgba32(Ground.R, Ground.G, Ground.B, 255));
        buffer.AppendRaw(Symbol(DebugPrimitiveShape.MilStd2525, friendAt, "SFGPUCIZ---D---|T|AT", 40f));
        buffer.AppendRaw(Symbol(DebugPrimitiveShape.Icon, hostileAt, "SHGPUCA----E---", 40f));
        layer.Update(0.016f);
        frame.StepN(3, canvas.Draw);
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-agg-look");
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, "agg-symbols.png");
        frame.Screenshot(png);

        var image = Raylib.LoadImage(png);
        try
        {
            Color At(Vector2 world, float dx, float dy) { var s = Screen(world); return Raylib.GetImageColor(image, (int)(s.X + dx), (int)(s.Y + dy)); }
            static bool Near(Color c, int r, int g, int b) => Math.Abs(c.R - r) <= 12 && Math.Abs(c.G - g) <= 12 && Math.Abs(c.B - b) <= 12;
            static bool Dark(Color c) => c.R < 70 && c.G < 70 && c.B < 70;

            var friendInside = At(friendAt, -22, 0);           // inside the 60 × 40 px rectangle, clear of the cross and the oval
            Assert.True(Near(friendInside, 128, 224, 255), $"friend fill at the frame's inside: {friendInside.R},{friendInside.G},{friendInside.B}");
            var dotAbove = At(friendAt, 0, -29); var bandInside = At(friendAt, 0, 28);
            Assert.True(Dark(dotAbove), $"the platoon's middle dot stands above the frame: {dotAbove.R},{dotAbove.G},{dotAbove.B}");
            Assert.False(Dark(bandInside), $"below the frame is the (hollow) track band, not a dot: {bandInside.R},{bandInside.G},{bandInside.B}");
            var box = At(friendAt, 36, 7);
            Assert.True(box.R > 225 && box.G > 225 && box.B > 225, $"the AT secondary box is white: {box.R},{box.G},{box.B}");
            var hostileInside = At(hostileAt, -20, 0);
            Assert.True(Near(hostileInside, 255, 128, 128), $"hostile fill inside the diamond: {hostileInside.R},{hostileInside.G},{hostileInside.B}");
        }
        finally { Raylib.UnloadImage(image); }

        // ⭐ R-8603 — the cost, measured: 500 composed symbols (1 000 men ≈ 165 unit symbols, S-4), drawn as vectors with nothing cached.
        SymbolCode.TryParse("SFGPUCIZ---D---|T|AT".AsSpan(), out var code);
        long bytes = 0; double ms = 0;
        frame.StepN(4, () =>
        {
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < 500; i++) SymbolRenderer.DrawScreen(code, new Vector2(20 + (i % 25) * 27, 20 + (i / 25) * 19), 24f);
            ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
        });
        _out.WriteLine($"500 symbols: {ms.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} ms CPU submit, {bytes} B");
        frame.Screenshot(Path.Combine(dir, "agg-symbols-500.png"));
        Assert.Equal(0L, bytes);
        Assert.True(ms < 4.0, $"500 symbols took {ms:0.00} ms to submit");
    }
}
