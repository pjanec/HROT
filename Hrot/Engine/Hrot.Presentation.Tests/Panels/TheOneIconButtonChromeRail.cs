using System;
using System.Numerics;
using Fdp.Presentation.Icons;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Panels;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐⭐ <b>CE-4333 (U-2; 📄 docs/blueprints/batches/NOTE_Coordinator_To_Ui_One_Icon_Button_Chrome.md, docs/UX/DESIGN_Colour_Icons.md §I1)</b> — ONE chrome for every icon button. A REAL frame of the main toolbar's three kinds side by side — the REAL
/// <see cref="MainToolbarTimeControlSection"/> (its run-mode indicator ● ▾), a glyph button (the Save-in-a-run flag, <c>GlyphWidgets.GlyphButton</c> as <c>ToolbarCommandAdapter</c> draws it) and a bitmap-style toolbar icon
/// (<c>IconWidgets.ToggleIcon</c>): at rest NONE draws a fill (their rects read the window's own colour); hovering each draws the same white inset frame (read from the pixels); a toggled one is filled; a steady frame allocates 0 B.
/// Renders <c>u2-icon-chrome.png</c> under <c>HROT_RAIL_SHOTS</c> (no PNG in git).
/// 🔴 Mutations: <c>GlyphWidgets.Draw</c> without <c>DrawHoverFrame</c> ⇒ the flag's frame row reds; the mode indicator's <c>DrawHoverFrame</c> removed ⇒ its row reds; a fill drawn at rest (<c>DrawToggleFill(…, true)</c>) ⇒ the rest rows red.
/// </summary>
[Collection(Hrot.Presentation.Tests.Map3D.Map3DFrameRail.Serial)]
public sealed class TheOneIconButtonChromeRail
{
    private static bool Near(Color c, Color d, int tol) => Math.Abs(c.R - d.R) <= tol && Math.Abs(c.G - d.G) <= tol && Math.Abs(c.B - d.B) <= tol;

    private static void Pos(float x, float y) { ImGui.GetIO().AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    [SkippableFact]
    public void CE4333_EveryIconButtonOfTheToolbar_IsTransparentAtRest_AndFramedWhiteOnHover()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(600, 160);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        var fake = new Hrot.Presentation.Tests.FakeTimeTransportFacade { Mode = Hrot.UI.Common.Facades.TransportMode.Live, IsPlayPauseEnabled = true };
        var section = new MainToolbarTimeControlSection(fake);
        var flag = new IconGlyph(Fdp.Presentation.Fonts.IconsFontAwesome6.Flag, IconPalette.Amber);
        var bitmap = new IconHandle((nint)1, 16, 16);   // an icon whose chrome is what is measured; the texture id is not sampled
        var rects = new (Vector2 Min, Vector2 Max)[3];
        bool toggled = false; long bytes = -1;
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(600, 160));
            ImGui.Begin("##tb", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            ImGui.SetCursorPos(new Vector2(20, 20));
            long before = GC.GetAllocatedBytesForCurrentThread();
            section.Render();
            rects[0] = section.IndicatorRect;                                              // ① the run-mode indicator
            ImGui.SetCursorPos(new Vector2(20, 80));
            GlyphWidgets.GlyphButton("##flag", flag); rects[1] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax()); ImGui.SameLine();   // ② the Save flag
            IconWidgets.ToggleIcon(in bitmap, "##bmp", new Vector2(24, 24), ref toggled, true, Vector4.Zero); rects[2] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());   // ③ a bitmap toolbar icon
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        Pos(590, 150);
        for (int i = 0; i < 6; i++) frame.Step(Draw);
        var win = default(Color);
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            win = Raylib.GetImageColor(shot, 560, 130);
            for (int i = 0; i < 3; i++)
            {
                var c = Raylib.GetImageColor(shot, (int)rects[i].Min.X + 2, (int)rects[i].Min.Y + 2);
                Assert.True(Near(c, win, 3), $"button {i} draws no fill at rest ({c.R},{c.G},{c.B} vs window {win.R},{win.G},{win.B})");
            }
        }
        finally { Raylib.UnloadImage(shot); }

        for (int i = 0; i < 3; i++)
        {
            var (mn, mx) = rects[i];
            Pos((mn.X + mx.X) / 2f, (mn.Y + mx.Y) / 2f);
            for (int k = 0; k < 4; k++) frame.Step(Draw);
            var s2 = Raylib.LoadImageFromScreen();
            try
            {
                // the white inset frame: its left edge column (1 px inside the rect) is near-white along the middle of the button
                int white = 0, rows = 0;
                for (int y = (int)mn.Y + 4; y < (int)mx.Y - 4; y++)
                {
                    rows++;
                    for (int x = (int)mn.X; x <= (int)mn.X + 3; x++)
                    { var p = Raylib.GetImageColor(s2, x, y); if (p.R > 215 && p.G > 215 && p.B > 215) { white++; break; } }
                }
                Assert.True(white >= rows - 2, $"button {i}: hovering draws the white inset frame on its left edge ({white}/{rows} rows)");
                if (i == 1 && Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { System.IO.Directory.CreateDirectory(dir); frame.Screenshot(System.IO.Path.Combine(dir, "u2-icon-chrome.png")); }
            }
            finally { Raylib.UnloadImage(s2); }
        }

        // a toggled bitmap icon is filled (the accent), and nothing allocates on a steady frame
        toggled = true;
        Pos(590, 150);
        for (int k = 0; k < 4; k++) frame.Step(Draw);
        var s3 = Raylib.LoadImageFromScreen();
        try
        {
            var f = Raylib.GetImageColor(s3, (int)rects[2].Min.X + 3, (int)rects[2].Min.Y + 3);
            Assert.True(f.B > f.R + 40, $"a toggled icon is filled with the accent ({f.R},{f.G},{f.B})");
        }
        finally { Raylib.UnloadImage(s3); }
        Assert.True(bytes == 0, $"a steady toolbar frame allocates nothing ({bytes} B)");
    }
}
