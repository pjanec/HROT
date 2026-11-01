using System;
using System.IO;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Raylib_cs;
using Xunit;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ <c>CE-4112</c> (AQ108 §2.6) — the Start surface draws the HROT wallpaper UNDER its menu: a REAL frame with the REAL <see cref="StartSurfaceWindow"/>; pixels near the window's corners equal the decoded
/// wallpaper's pixels through the SAME cover math, and the menu's text is still on top. (The pure cover rail and the embedded-resource rail live in the Start suite, <c>StartGateTests</c> in Fdp.Presentation.Tests; the
/// pixel rail needs the GL frame harness, which only this assembly references.)
/// 🔴 Mutations: <c>DrawWallpaper</c> not called ⇒ the corner cells red; <c>Math.Max</c> → <c>Math.Min</c> in <c>Cover</c> ⇒ red; the menu band / text not drawn after the image ⇒ the text cell reds.
/// </summary>
[Collection(ImGuiTestCollection.Name)]
public sealed class TheStartScreenShowsTheWallpaperTests
{
    [SkippableFact]
    public void CE4112_TheStartSurface_DrawsTheWallpaperUnderItsMenu()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin();
        var wm = new WM(new IconAtlas(nint.Zero, 1, 1, 16f));
        wm.GlobalMenu.RegisterItem("File/Open Scenario", () => { }, allowedInIdle: true);
        var surface = new StartSurfaceWindow(wm.GlobalMenu, () => StartVariant.Actions, () => wm.StatusBar.Height);
        wm.RegisterWindow(surface);
        wm.SwitchPerspective(WM.StartPerspectiveName);
        Assert.True(wm.IsStartShowing);

        System.Numerics.Vector2 pos = default, size = default;
        frame.StepN(4, () => { wm.Render(); var vp = ImGui.GetMainViewport(); pos = vp.WorkPos; size = vp.WorkSize; });

        var wall = Raylib.LoadImageFromMemory(".png", EmbeddedAtlasResources.GetWallpaperPngBytes());
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            var c = StartSurfaceWindow.Cover(size.X, size.Y, wall.Width, wall.Height);
            int near(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
            // points near the window corners / edges (the status bar covers the bottom strip), away from the centred menu band
            foreach (var (lx, ly) in new[] { (10f, 10f), (size.X - 12f, 12f), (size.X * 0.12f, size.Y * 0.75f), (size.X * 0.9f, size.Y * 0.75f) })
            {
                float u = c.U0 + lx / size.X * (c.U1 - c.U0), v = c.V0 + ly / size.Y * (c.V1 - c.V0);
                var expect = Raylib.GetImageColor(wall, (int)(u * wall.Width), (int)(v * wall.Height));
                var got = Raylib.GetImageColor(shot, (int)(pos.X + lx), (int)(pos.Y + ly));
                Assert.True(near(expect, got) <= 40, $"corner ({lx:0},{ly:0}): wallpaper {expect.R},{expect.G},{expect.B} vs drawn {got.R},{got.G},{got.B}");
            }

            // the menu is still on top: the white title / button text is drawn over the band
            int white = 0;
            for (int y = (int)(pos.Y + size.Y * 0.3f); y < pos.Y + size.Y * 0.7f; y++)
                for (int x = (int)(size.X * 0.35f); x < size.X * 0.65f; x++)
                { var p = Raylib.GetImageColor(shot, x, y); if (p.R > 235 && p.G > 235 && p.B > 235) white++; }
            Assert.True(white >= 40, $"the menu text is drawn over the wallpaper ({white} near-white pixels)");

            // ⭐ 2026-10-30 (user: "seem off center" · "the transparency need to be much less") — the panel is centred on the SURFACE (both axes, ±1.5 px) and
            //   opaque enough that the wallpaper does not show through its padding (the arrow under it is bright)
            var (pmin, pmax) = surface.PanelRect;
            Assert.InRange((pmin.X + pmax.X) / 2f - (pos.X + size.X / 2f), -1.5f, 1.5f);
            Assert.True(wm.StatusBar.Height > 0f, "the status bar is drawn here, so the centring must account for it");
            Assert.InRange((pmin.Y + pmax.Y) / 2f - (pos.Y + (size.Y - wm.StatusBar.Height) / 2f), -1.5f, 1.5f);   // centred on the VISIBLE area
            foreach (var (px, py) in new[] { (pmin.X + 12f, pmax.Y - 5f), (pmax.X - 12f, pmin.Y + 5f), (pmin.X + 5f, (pmin.Y + pmax.Y) / 2f) })
            {
                var p = Raylib.GetImageColor(shot, (int)px, (int)py);
                Assert.True(Math.Max(p.R, Math.Max(p.G, p.B)) <= 40, $"the panel at ({px:0},{py:0}) lets the wallpaper through: {p.R},{p.G},{p.B}");
            }

            string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-start");
            Directory.CreateDirectory(dir);
            frame.Screenshot(Path.Combine(dir, "ce4112-start.png"));
        }
        finally { Raylib.UnloadImage(wall); Raylib.UnloadImage(shot); }
    }
}
