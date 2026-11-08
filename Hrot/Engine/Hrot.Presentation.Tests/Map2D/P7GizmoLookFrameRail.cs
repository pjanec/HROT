using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Gizmos;
using Hrot.Editor.UiFrameRail;
using Hrot.ScenarioEditor.Tools;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ B-17 P7 ④ ⑥ fidelity (R-4146; sheets <c>docs/UX/map-gizmo-look-1.svg</c> row ② and <c>-2.svg</c> rows ④ ⑥) — the REAL gizmos' primitives through the REAL 2-D gizmo layer in a real frame:
/// <b>drawing</b> = the keys hint on a backdrop under a Done button (blue box, green tick, the word); <b>pick</b> = the banner (a Cancel BUTTON box, the prompt on a backdrop), the refusal on a backdrop at the cursor and the
/// crosshair of two pixel bars at the cursor. Each state is measured in pixels (never eyeballed) and saved under <c>HROT_RAIL_SHOTS</c> (⛔ never committed). Skips without a display (run under <c>xvfb-run -a</c>).
/// 🔴 Red-proofs (compile): the drawer not boxing a style ⇒ no dark pixels under the hint; <c>DrawCrosshair</c> not called ⇒ no white bars; Cancel left Plain ⇒ no button-blue box.
/// </summary>
[Collection(Hrot.Presentation.Tests.Map3D.Map3DFrameRail.Serial)]
public sealed class P7GizmoLookFrameRail
{
    private const int W = 800, H = 420;
    private const float Zoom = 6f;
    private static readonly Vector2 View = new(15f, 10f);   // the world point that lands mid-screen

    private static Vector2 Screen(Vector2 world) => new((world.X - (View.X - W / 2f / Zoom)) * Zoom, ((View.Y + H / 2f / Zoom) - world.Y) * Zoom);

    private static int Count(Image image, int x0, int y0, int x1, int y1, Func<Color, bool> pred)
    {
        int n = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(image.Height, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(image.Width, x1); x++)
                if (pred(Raylib.GetImageColor(image, x, y))) n++;
        return n;
    }

    private static readonly Color Ground = new(60, 110, 60, 255);

    /// <summary>The ground the proposal sheets draw on, as a world box that fills the view (so a backdrop is visibly DARKER than what is behind it).</summary>
    private static void GroundBox(IDebugDrawBuilder draw)
        => draw.DrawBox2D(View, new Vector2(2000f, 2000f), new Rgba32(Ground.R, Ground.G, Ground.B, 255), 0f, 0f, SizeMode.WorldMeters, fillColor: new Rgba32(Ground.R, Ground.G, Ground.B, 255), layer: 0);

    private static (UiFrameSession Frame, MapCanvas Canvas, Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer Layer, DebugPrimitiveBuffer Buffer) Stage(UiFrameSession frame)
    {
        Fdp.Presentation.Fonts.GizmoFont.Load();   // the shared loader: Roboto 32 px with · ° — – × …
        var canvas = new MapCanvas(input: null);
        var camera = new MapCamera();
        var target = new Vector2(View.X - W / 2f / Zoom, View.Y + H / 2f / Zoom);
        camera.ApplyCameraView(new MapCameraView { Target = target, Zoom = Zoom, SmoothTarget = target, SmoothZoom = Zoom });
        canvas.Camera = camera;
        var buffer = new DebugPrimitiveBuffer();
        var layer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new FdpEventBus(), camera: camera);
        canvas.AddLayer(layer);
        return (frame, canvas, layer, buffer);
    }

    private static string Shots(string name)
    {
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-p7-look");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name + ".png");
    }

    [SkippableFact]
    public void P7_Drawing_TheHintIsOnABackdrop_AndDoneIsABlueButtonWithATickAndTheWord()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frameSession = UiFrameHarness.Begin(W, H);
        var (frame, canvas, layer, buffer) = Stage(frameSession);

        var s = new ShapeEditSession(minPoints: 3, closed: true, noun: "an area");
        s.Add(new Vector2(0, 0)); s.Add(new Vector2(30, 0)); s.Add(new Vector2(30, 20));
        GroundBox(buffer);
        for (int i = 0; i < s.Points.Count; i++)
            buffer.DrawBox2D(s.Points[i], new Vector2(4f, 4f), new Rgba32(60, 200, 190, 255), 0f, 1.5f, SizeMode.ScreenPixels, fillColor: new Rgba32(255, 255, 255, 255));
        ShapeDoneMark.Draw(buffer, s);
        const float hintX = 20f, hintY = 380f;
        buffer.DrawTextLong(hintX, hintY, "3 points · Enter / Done: finish (3+) · Esc: cancel", new Rgba32(255, 255, 255, 255), CoordinateSpace.Screen, style: GizmoTextStyle.Backdrop);
        layer.Update(0.016f);
        frame.StepN(3, canvas.Draw);
        string png = Shots("p7-drawing");
        frame.Screenshot(png);
        Console.WriteLine("P7 drawing frame: " + png);

        var image = Raylib.LoadImage(png);
        try
        {
            // the backdrop: black at ~60 % over the ground (60,110,60) ⇒ about (24,44,24) — a block under the hint, which is ~330 px wide and 13 px tall plus the 3 px padding
            int dark = Count(image, (int)hintX - 6, (int)hintY - 3, (int)hintX + 340, (int)hintY + 16, c => c.R < 40 && c.G is > 25 and < 70 && c.B < 40);
            Assert.True(dark > 4500, $"the hint stands on a dark backdrop ({dark} backdrop-coloured px)");
            // the Done button: its centre in screen px, half extents × zoom
            var c0 = Screen(s.DonePosition); float hw = ShapeDoneMark.HalfWidth(s.DoneHalfSize) * Zoom, hh = ShapeDoneMark.HalfHeight(s.DoneHalfSize) * Zoom;
            int blue = Count(image, (int)(c0.X - hw) + 3, (int)(c0.Y - hh) + 3, (int)(c0.X + hw) - 3, (int)(c0.Y + hh) - 3, c => c.R is > 20 and < 60 && c.G is > 55 and < 85 && c.B is > 85 and < 115);
            Assert.True(blue > hw * hh * 4f * 0.45f, $"the button is a blue box ({blue} px of {hw * hh * 4f:0})");
            int tick = Count(image, (int)(c0.X - hw), (int)(c0.Y - hh), (int)c0.X, (int)(c0.Y + hh), c => c.R is > 60 and < 130 && c.G > 190 && c.B is > 80 and < 150);
            Assert.True(tick > 6, $"a green tick sits at the button's left ({tick} px)");
            int word = Count(image, (int)c0.X - (int)hw / 2, (int)(c0.Y - hh), (int)(c0.X + hw), (int)(c0.Y + hh), c => c.R > 225 && c.G > 225 && c.B > 225);
            Assert.True(word > 25, $"the word 'Done' is drawn in white on the button ({word} white px)");
        }
        finally { Raylib.UnloadImage(image); }
    }

    [SkippableFact]
    public void P7_Pick_TheBannerIsAButtonAndABackdrop_TheRefusalIsOnABackdrop_AndACrosshairStandsAtTheCursor()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frameSession = UiFrameHarness.Begin(W, H);
        var (frame, canvas, layer, buffer) = Stage(frameSession);

        using var repo = new EntityRepository();
        repo.RegisterEvent<GizmoDragUpdateEvent>(); repo.RegisterEvent<GizmoMouseEvent>(); repo.RegisterEvent<GizmoKeyEvent>();
        var global = new GlobalGizmoManager(buffer);
        var controller = new ToolController(() => global, () => null);
        var pickers = new PickerToolHost(() => controller, () => global, mouseScreen: () => null, clock: () => 0.0);
        _ = pickers.RunPickAsync<Vector2>(ScenarioToolIds.PickLocation,
            (tcs, remove) => new FdpLocationPickerGizmo(onPicked: w => tcs.TrySetResult(w), onRemove: remove, onCancelled: () => tcs.TrySetCanceled()),
            default, "Pick a unit for Target  (Alpha 1-1)");
        var cursor = new Vector2(15f, 10f);
        repo.Bus.Publish(new GizmoDragUpdateEvent { WorldPos = new Vector3(cursor.X, cursor.Y, 0f) });
        repo.Bus.SwapBuffers();
        global.Execute(repo, 0.016f);   // the cursor reaches the picker
        pickers.Reject("a unit, not a building");
        buffer.Clear();
        GroundBox(buffer);
        global.Execute(repo, 0.016f);   // the frame that draws the banner, the crosshair and the refusal
        layer.Update(0.016f);
        frame.StepN(3, canvas.Draw);
        string png = Shots("p7-pick");
        frame.Screenshot(png);
        Console.WriteLine("P7 pick frame: " + png);

        var image = Raylib.LoadImage(png);
        try
        {
            // Cancel: the Button box (fill 0x23415F, border 0x3369AD) round the word
            int x0 = (int)PickerToolHost.CancelBoxLeftPx, y0 = (int)PickerToolHost.CancelBoxTopPx, x1 = (int)PickerToolHost.CancelBoxRightPx, y1 = (int)PickerToolHost.CancelBoxBottomPx;
            int fill = Count(image, x0, y0, x1 + 1, y1 + 1, c => Math.Abs(c.R - 0x23) < 8 && Math.Abs(c.G - 0x41) < 8 && Math.Abs(c.B - 0x5F) < 8);
            int border = Count(image, x0 - 1, y0 - 1, x1 + 2, y1 + 2, c => Math.Abs(c.R - 0x33) < 10 && Math.Abs(c.G - 0x69) < 10 && Math.Abs(c.B - 0xAD) < 10);
            Assert.True(fill > (x1 - x0) * (y1 - y0) * 0.45f, $"Cancel is a button-blue box ({fill} px of {(x1 - x0) * (y1 - y0)})");
            Assert.True(border > 40, $"the box has its 1 px border ({border} px)");
            // the prompt stands on a backdrop of its own, to the right of the button
            int px0 = (int)PickerToolHost.PromptLeftPx - 6, py0 = (int)PickerToolHost.BannerTopPx - 3;
            int dark = Count(image, px0, py0, px0 + 260, py0 + 19, c => c.R < 40 && c.G is > 25 and < 70 && c.B < 40);
            Assert.True(dark > 2500, $"the prompt is on a dark backdrop ({dark} px)");
            // ONE cross: the location picker's own sky-blue cross stands at the cursor and the banner adds no white bars of its own
            var s = Screen(cursor);
            int cyan = Count(image, (int)s.X - 12, (int)s.Y - 12, (int)s.X + 13, (int)s.Y + 13, c => Math.Abs(c.R - 102) < 12 && Math.Abs(c.G - 191) < 12 && c.B > 235);
            int white = Count(image, (int)s.X - 12, (int)s.Y - 12, (int)s.X + 13, (int)s.Y + 13, c => c.R > 235 && c.G > 235 && c.B > 235);
            Assert.True(cyan >= 20, $"the picker's own aiming cross stands at the cursor ({cyan} px)");
            Assert.True(white < 6, $"no second (banner) cross over it ({white} white px)");
            // the refusal, 22 px below the cursor, on a backdrop
            int refuse = Count(image, (int)s.X - 6, (int)s.Y + 18, (int)s.X + 170, (int)s.Y + 40, c => c.R < 40 && c.G is > 25 and < 70 && c.B < 40);
            Assert.True(refuse > 1200, $"the refusal is on a dark backdrop at the cursor ({refuse} px)");
        }
        finally { Raylib.UnloadImage(image); }
    }
}
