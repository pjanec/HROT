using System;
using Fdp.Presentation.Raylib;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ CE-4386 (handoff L-0) — with <c>HROT_HIDDEN_WINDOW=1</c> the rail window is created HIDDEN (never shown, never focused), every frame is drawn into the off-screen target,
/// and <c>LoadImageFromScreen</c> still reads the frame just drawn (here: a known colour in a known place, drawn by ImGui and by raylib). Runs only in a process started with the
/// switch (the harness keeps one window per process): <c>HROT_HIDDEN_WINDOW=1 xvfb-run -a dotnet test … --filter TheHiddenWindowMode</c>.
/// 🔴 Red-proof (compiles): <see cref="HiddenWindow.BeginFrame"/> made a no-op ⇒ no frame is redirected and the rail reds.
/// </summary>
[Collection(Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheHiddenWindowModeTests
{
    [SkippableFact]
    public void TheRailWindow_IsHidden_AndTheFrameIsStillReadBack()
    {
        Skip.IfNot(HiddenWindow.Requested, "run with HROT_HIDDEN_WINDOW=1");
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(640, 360);
        int before = HiddenWindow.FramesRedirected;
        frame.StepN(3, () =>
        {
            Raylib.DrawRectangle(40, 40, 120, 80, new Color(10, 200, 30, 255));
            ImGui.GetForegroundDrawList().AddRectFilled(new System.Numerics.Vector2(300, 200), new System.Numerics.Vector2(400, 260), ImGui.GetColorU32(new System.Numerics.Vector4(0.9f, 0.1f, 0.6f, 1f)));
        });
        Assert.True(HiddenWindow.Active, "the switch made the window hidden");
        Assert.True(Raylib.IsWindowHidden(), "the window is hidden");
        Assert.False(Raylib.IsWindowFocused(), "the window never took focus");
        Assert.Equal(3, HiddenWindow.FramesRedirected - before);

        var img = Raylib.LoadImageFromScreen();
        try
        {
            var raylibPixel = Raylib.GetImageColor(img, 100, 80);
            var imguiPixel = Raylib.GetImageColor(img, 350, 230);
            var background = Raylib.GetImageColor(img, 600, 20);
            Assert.True(raylibPixel.R == 10 && raylibPixel.G == 200 && raylibPixel.B == 30, $"the raylib rectangle is read back ({raylibPixel.R},{raylibPixel.G},{raylibPixel.B})");
            Assert.True(imguiPixel.R > 200 && imguiPixel.G < 60 && imguiPixel.B > 120, $"the ImGui rectangle is read back ({imguiPixel.R},{imguiPixel.G},{imguiPixel.B})");
            Assert.True(background.R == Color.DarkGray.R && background.G == Color.DarkGray.G, $"the cleared background ({background.R},{background.G},{background.B})");
        }
        finally { Raylib.UnloadImage(img); }
    }
}
