using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Fdp.Presentation.Fonts;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ U-2 (docs/UX/DESIGN_Colour_Icons.md §2–§3.1, I1) — the colour icons: the ONE key → glyph map, the glyph button / switch on a neutral background, and the icon tab. The frame rails run with the REAL fonts
/// (<see cref="EditorFontService"/>: Roboto + FontAwesome, the app's) under Xvfb, and read the rendered pixels — a tab's icon sits INSIDE its item rect in its colour and LEFT of the label, the id is stable when the label text
/// changes, the tab bar is neutral (#24242C / selected #3A3A48), the switch options keep their own colour (the unselected at ~60 %), 0 B on a steady frame.
/// 🔴 Red-proof (compiles): <c>IconTab.Begin</c> without the reserved spaces ⇒ the label overdraws the icon ⇒ the tab rail red.
/// Renders (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>colour-icons-tabs.png</c>, <c>colour-icons-switch.png</c>, <c>colour-icons-sheet.png</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class ColourIconsFrameRail
{
    private const ImGuiWindowFlags Bare = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;

    private static bool Near(Color c, Vector4 v, int tol) =>
        Math.Abs(c.R - v.X * 255f) <= tol && Math.Abs(c.G - v.Y * 255f) <= tol && Math.Abs(c.B - v.Z * 255f) <= tol;

    private static bool IsInk(Color c) => c.R > 170 && c.G > 170 && c.B > 170;

    private static (int Min, int Max, int Count) Extent(Image shot, int x0, int y0, int x1, int y1, Func<Color, bool> what)
    {
        int min = -1, max = -1, n = 0;
        for (int x = Math.Max(0, x0); x < Math.Min(shot.Width, x1); x++)
            for (int y = Math.Max(0, y0); y < Math.Min(shot.Height, y1); y++)
                if (what(Raylib.GetImageColor(shot, x, y))) { if (min < 0) min = x; max = x; n++; }
        return (min, max, n);
    }

    private static void Shoot(UiFrameSession frame, string name)
    {
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, name));
    }

    /// <summary>The app's fonts (Roboto + FontAwesome 6 merged), baked as the shell does.</summary>
    private static void RealFonts() => new EditorFontService().Initialize(1f, 1f);

    // ── the map ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Icons_EveryKeyAnswers_EveryGlyphIsANamedFontAwesomeConstant_AndTheModesAreTheirDarColours()
    {
        var named = typeof(IconsFontAwesome6).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        foreach (var key in Icons.Keys)
        {
            Assert.True(Icons.TryGet(key, out var g), $"key {key} answers");
            Assert.True(named.Contains(g.Glyph), $"{key}: its glyph is a named constant of IconsFontAwesome6 (the copy in NodeEditor.Core has not drifted)");
            Assert.True(g.Color.W == 1f && (g.Color.X + g.Color.Y + g.Color.Z) > 0f, $"{key}: a real colour (static initialisers in order)");
        }
        Assert.False(Icons.TryGet("no/such-key", out var u)); Assert.Equal(Icons.Unknown, u);

        // spot checks of §3 / §3.1
        Assert.Equal(new IconGlyph(IconsFontAwesome6.Sitemap, IconPalette.Teal), Icons.Get("orbat/command"));
        Assert.Equal(new IconGlyph(IconsFontAwesome6.Thumbtack, IconPalette.Amber), Icons.Get("details/pin"));
        Assert.Equal(new IconGlyph(IconsFontAwesome6.CircleXmark, IconPalette.Error), Icons.Get("form/error"));
        Assert.Equal(new IconGlyph(IconsFontAwesome6.Filter, IconPalette.Blue), Icons.Get("tab/when"));
        Assert.Equal(new IconGlyph(IconsFontAwesome6.Brain, IconPalette.Pink), Icons.Get("tab/brain"));

        // the mode indicator is what ModeGlyph gave before it folded into the map (the pre-fold literals)
        void Mode(TransportMode m, string glyph, float r, float g, float b)
        {
            var i = MainToolbarTimeControlSection.ModeGlyph(m)!;
            Assert.Equal(glyph, i.Glyph); Assert.Equal(new Vector4(r, g, b, 1f), i.Color);
        }
        Mode(TransportMode.Edit, IconsFontAwesome6.Pen, 0.60f, 0.60f, 0.60f);
        Mode(TransportMode.Preview, IconsFontAwesome6.CircleHalfStroke, 0.66f, 0.45f, 0.92f);
        Mode(TransportMode.Live, IconsFontAwesome6.Circle, 0.25f, 0.80f, 0.30f);
        Mode(TransportMode.Review, IconsFontAwesome6.ClockRotateLeft, 1.00f, 0.70f, 0.20f);
        Mode(TransportMode.Replay, IconsFontAwesome6.Play, 0.30f, 0.60f, 1.00f);
    }

    // ── the icon tab ─────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void IconTab_TheIconIsInsideTheItemRectInItsColour_LeftOfTheLabel_TheIdSurvivesALabelChange_TheBarIsNeutral_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(800, 200);
        RealFonts();
        var when = Icons.Get("tab/when"); var play = Icons.Get("tab/do"); var gear = Icons.Get("tab/settings");
        string firstLabel = "When", renamed = "When (3 rules)", label = firstLabel;
        var rects = new (Vector2 Min, Vector2 Max)[3]; uint idBefore = 0, idAfter = 0; bool selectedAfter = false; long bytes = -1; int frameNo = 0;
        void Draw()
        {
            frameNo++;
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(800, 200));
            ImGui.Begin("##tabs", Bare);
            long before = GC.GetAllocatedBytesForCurrentThread();
            if (IconTab.BeginBar("##bar"))
            {
                bool s0 = IconTab.Begin("when", label, when);
                rects[0] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                if (frameNo == 4) idBefore = ImGui.GetItemID();
                if (frameNo == 8) { idAfter = ImGui.GetItemID(); selectedAfter = s0; }
                if (s0) ImGui.EndTabItem();
                if (IconTab.Begin("do", "Do", play)) ImGui.EndTabItem();
                rects[1] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                if (IconTab.Begin("settings", "Settings", gear)) ImGui.EndTabItem();
                rects[2] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                IconTab.EndBar();
            }
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        for (int i = 0; i < 4; i++) frame.Step(Draw);
        label = renamed;
        for (int i = 0; i < 4; i++) frame.Step(Draw);
        Shoot(frame, "colour-icons-tabs.png");

        Assert.True(bytes == 0, $"a steady frame of three icon tabs allocates nothing ({bytes} bytes)");
        Assert.NotEqual(0u, idBefore);
        Assert.Equal(idBefore, idAfter);                                  // ###id: the id does not change with the label text
        Assert.True(selectedAfter, "...so the tab is still the selected one after the rename");

        var shot = Raylib.LoadImageFromScreen();
        try
        {
            int y0 = (int)rects[0].Min.Y + 3, y1 = (int)rects[0].Max.Y - 1, x0 = (int)rects[0].Min.X, x1 = (int)rects[0].Max.X;
            var icon = Extent(shot, x0, y0, x1, y1, c => Near(c, when.Color, 45));
            Assert.True(icon.Count >= 8, $"the When icon is drawn in its colour inside the tab ({icon.Count} px)");
            var label0 = Extent(shot, x0, y0, x1, y1, IsInk);                 // scanned from the tab's left edge: a label drawn over the icon would show ink LEFT of it
            Assert.True(label0.Min > icon.Max, $"the label ({label0.Min}) starts right of the icon ({icon.Max})");
            var gearIcon = Extent(shot, (int)rects[2].Min.X, (int)rects[2].Min.Y + 3, (int)rects[2].Max.X, (int)rects[2].Max.Y - 1, c => Near(c, gear.Color, 25));
            Assert.True(gearIcon.Count >= 8, "the Settings icon is grey, in its own tab");

            // the neutral palette: the selected tab #3A3A48, an unselected one #24242C
            var sel = Raylib.GetImageColor(shot, (int)rects[0].Max.X - 4, (int)(rects[0].Min.Y + rects[0].Max.Y) / 2 + 2);
            var uns = Raylib.GetImageColor(shot, (int)rects[1].Max.X - 4, (int)(rects[1].Min.Y + rects[1].Max.Y) / 2 + 2);
            Assert.True(Near(sel, new Vector4(0x3A / 255f, 0x3A / 255f, 0x48 / 255f, 1f), 6), $"selected tab ≈ #3A3A48 (got {sel.R:X2}{sel.G:X2}{sel.B:X2})");
            Assert.True(Near(uns, new Vector4(0x24 / 255f, 0x24 / 255f, 0x2C / 255f, 1f), 6), $"unselected tab ≈ #24242C (got {uns.R:X2}{uns.G:X2}{uns.B:X2})");
        }
        finally { Raylib.UnloadImage(shot); }
    }

    // ── the glyph button / switch ────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void GlyphToggle_ASwitchKeepsEveryOptionsColour_TheSelectedIsFramedAtFullStrength_TheOthersAtSixtyPercent_OnANeutralBackground()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(500, 150);
        RealFonts();
        var a = Icons.Get("orbat/command"); var b = Icons.Get("orbat/structure"); var c = Icons.Get("orbat/scenario");
        int sel = 1; var rects = new (Vector2 Min, Vector2 Max)[3]; long bytes = -1;
        void Draw()
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(500, 150));
            ImGui.Begin("##sw", Bare);
            long before = GC.GetAllocatedBytesForCurrentThread();
            if (GlyphWidgets.GlyphToggle("##a", a, sel == 0)) sel = 0; rects[0] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax()); ImGui.SameLine();
            if (GlyphWidgets.GlyphToggle("##b", b, sel == 1)) sel = 1; rects[1] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax()); ImGui.SameLine();
            if (GlyphWidgets.GlyphToggle("##c", c, sel == 2)) sel = 2; rects[2] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax()); ImGui.SameLine();
            GlyphWidgets.GlyphButton("##d", Icons.Get("details/pin"));
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        for (int i = 0; i < 6; i++) frame.Step(Draw);
        Shoot(frame, "colour-icons-switch.png");
        Assert.True(bytes == 0, $"a steady frame of a switch allocates nothing ({bytes} bytes)");

        var shot = Raylib.LoadImageFromScreen();
        try
        {
            Color BgOf(int i) => Raylib.GetImageColor(shot, (int)rects[i].Min.X + 3, (int)rects[i].Min.Y + 3);
            Assert.True(Near(BgOf(0), new Vector4(0x1C / 255f, 0x1C / 255f, 0x22 / 255f, 1f), 6), "an unselected option's background is #1C1C22");
            Assert.True(Near(BgOf(1), new Vector4(0x34 / 255f, 0x34 / 255f, 0x3E / 255f, 1f), 6), "the selected option's background is #34343E");
            int frameHits = Extent(shot, (int)rects[1].Min.X - 1, (int)rects[1].Min.Y + 4, (int)rects[1].Min.X + 3, (int)rects[1].Max.Y - 4, p => p.R > 140 && p.G > 140 && p.B > 140).Count;
            Assert.True(frameHits > 4, $"the selected option has a light frame ({frameHits} px)");

            int Full(int i, Vector4 col) => Extent(shot, (int)rects[i].Min.X, (int)rects[i].Min.Y, (int)rects[i].Max.X, (int)rects[i].Max.Y, p => Near(p, col, 22)).Count;
            Assert.True(Full(1, b.Color) >= 8, "the selected option's glyph is at full strength in ITS colour (orange)");
            Assert.True(Full(0, a.Color) == 0, "an unselected option is NOT at full strength...");
            var dimmed = Vector4.Lerp(new Vector4(0x1C / 255f, 0x1C / 255f, 0x22 / 255f, 1f), a.Color, GlyphWidgets.UnselectedAlpha);
            Assert.True(Extent(shot, (int)rects[0].Min.X, (int)rects[0].Min.Y, (int)rects[0].Max.X, (int)rects[0].Max.Y, p => Near(p, dimmed, 22)).Count >= 8, "...it keeps its own colour (teal) at ~60 %");
            Assert.True(Extent(shot, (int)rects[2].Min.X, (int)rects[2].Min.Y, (int)rects[2].Max.X, (int)rects[2].Max.Y, p => p.B > p.R + 25 && p.R > 60).Count >= 8, "the third option is violet-ish, not grey");
        }
        finally { Raylib.UnloadImage(shot); }
    }

    // ── the sheet (evidence: every key, drawn) ───────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Sheet_EveryKeyDrawsAGlyphInItsColour()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(1100, 520);
        RealFonts();
        void Draw()
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(1100, 520));
            ImGui.Begin("##sheet", Bare);
            for (int i = 0; i < Icons.Keys.Length; i++)
            {
                var g = Icons.Get(Icons.Keys[i]);
                if (i % 4 != 0) ImGui.SameLine(((i % 4) * 270f) + 8f);
                ImGui.TextColored(g.Color, g.Glyph); ImGui.SameLine(); ImGui.TextUnformatted(Icons.Keys[i]);
            }
            ImGui.End();
        }
        for (int i = 0; i < 4; i++) frame.Step(Draw);
        Shoot(frame, "colour-icons-sheet.png");
        var shot = Raylib.LoadImageFromScreen();
        try { Assert.True(Extent(shot, 0, 0, 270, 40, p => Near(p, IconPalette.Grey, 20)).Count > 5, "the first row (mode/edit) is drawn grey"); }
        finally { Raylib.UnloadImage(shot); }
    }
}
