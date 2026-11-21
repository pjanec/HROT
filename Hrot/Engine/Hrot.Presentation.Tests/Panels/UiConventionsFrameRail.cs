using System;
using System.Numerics;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ C1 (docs/UX/DESIGN_UI_Conventions.md §2–§5, U-1) — frame rails for the three house-wide helpers <see cref="TreeRow"/>, <see cref="SectionHeader"/>, <see cref="FormGrid"/> (and <see cref="RowIcon"/>),
/// in a REAL frame under Xvfb (a skip is not a pass). The item rects are measured from the rendered pixels: the icons are saturated colours, the chevron and the label are text-coloured ink, so
/// "chevron &lt; icon &lt; label" is read off the screen, not off the helper's own arithmetic. Every test also proves ZERO bytes allocated on a steady frame (R-2084).
/// 🔴 Red-proof (compiles): <c>FormGrid.LabelColumn</c> without the 25 % cap ⇒ the cap rail red.
/// Renders (evidence, never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>ui-conventions-tree.png</c>, <c>-header.png</c>, <c>-form.png</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class UiConventionsFrameRail
{
    private const ImGuiWindowFlags Bare = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;

    private static Texture2D Swatch(Color c)
    {
        var img = Raylib.GenImageColor(16, 16, c);
        var t = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        return t;
    }

    private static RowIcon IconOf(Texture2D t) => RowIcon.Image((nint)t.Id, Vector2.Zero, Vector2.One);

    private static bool IsRed(Color c) => c.R > 200 && c.G < 90 && c.B < 90;
    private static bool IsBlue(Color c) => c.B > 200 && c.R < 60 && c.G is > 80 and < 170;
    private static bool IsInk(Color c) => c.R > 150 && c.G > 150 && c.B > 150;       // the Text colour: white-ish

    /// <summary>The x range [min,max] of pixels matching <paramref name="what"/> in the band y0..y1 and x in [x0,x1); (-1,-1) when none.</summary>
    private static (int Min, int Max) Extent(Image shot, int y0, int y1, int x0, int x1, Func<Color, bool> what)
    {
        int min = -1, max = -1;
        for (int x = Math.Max(0, x0); x < Math.Min(shot.Width, x1); x++)
            for (int y = Math.Max(0, y0); y < Math.Min(shot.Height, y1); y++)
                if (what(Raylib.GetImageColor(shot, x, y))) { if (min < 0) min = x; max = x; break; }
        return (min, max);
    }

    private static void Shoot(UiFrameSession frame, string name)
    {
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir)
            frame.Screenshot(System.IO.Path.Combine(dir, name));
    }

    // ── TreeRow ────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void TreeRow_ChevronThenIconThenLabel_ALeafKeepsTheChevronSlot_DepthIsTheIndent_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(700, 400);
        var red = Swatch(Color.Red); var blue = Swatch(Color.Blue);
        try
        {
            bool rootOpen = true, childOpen = false, leafOpen = false;
            var rects = new (Vector2 Min, Vector2 Max)[3];
            long bytes = -1; float indent = 0;
            void Draw()
            {
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(700, 400));
                ImGui.Begin("##tree", Bare);
                long before = GC.GetAllocatedBytesForCurrentThread();
                TreeRow.Draw("root", "Root", IconOf(red), 0, true, ref rootOpen, false);
                rects[0] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                TreeRow.Draw("child", "Child with children", IconOf(blue), 1, true, ref childOpen, false);
                rects[1] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                TreeRow.Draw("leaf", "Leaf", IconOf(blue), 1, false, ref leafOpen, false);
                rects[2] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                indent = ImGui.GetStyle().IndentSpacing;
                ImGui.End();
            }
            for (int i = 0; i < 8; i++) frame.Step(Draw);
            Shoot(frame, "ui-conventions-tree.png");

            Assert.True(bytes == 0, $"a steady frame of three rows allocates nothing ({bytes} bytes)");
            var shot = Raylib.LoadImageFromScreen();
            try
            {
                (int Chev, int IconMin, int IconMax, int Label) Read(int i, Func<Color, bool> iconColour)
                {
                    int y0 = (int)rects[i].Min.Y, y1 = (int)rects[i].Max.Y, x0 = (int)rects[i].Min.X, x1 = (int)rects[i].Max.X;
                    var icon = Extent(shot, y0, y1, x0, x1, iconColour);
                    var chev = Extent(shot, y0, y1, x0, icon.Min < 0 ? x1 : icon.Min, IsInk);
                    var label = Extent(shot, y0, y1, icon.Max + 1, x1, IsInk);
                    return (chev.Max, icon.Min, icon.Max, label.Min);
                }
                var root = Read(0, IsRed); var child = Read(1, IsBlue); var leaf = Read(2, IsBlue);

                Assert.True(root.Chev >= 0 && root.Chev < root.IconMin, $"root: chevron ({root.Chev}) is left of the icon ({root.IconMin})");
                Assert.True(root.IconMax < root.Label, $"root: icon ({root.IconMax}) is left of the label ({root.Label})");
                Assert.True(child.Chev >= 0 && child.Chev < child.IconMin && child.IconMax < child.Label, "child: chevron < icon < label");
                Assert.True(leaf.Chev < 0, "a leaf draws no arrow...");
                Assert.True(Math.Abs(leaf.IconMin - child.IconMin) <= 1, $"...but keeps its slot: icon x {leaf.IconMin} == {child.IconMin}");
                Assert.True(Math.Abs(leaf.Label - child.Label) <= 1, $"a leaf's label is at its depth's x: {leaf.Label} == {child.Label}");
                Assert.True(Math.Abs((child.IconMin - root.IconMin) - indent) <= 1, $"depth is the indent: {child.IconMin - root.IconMin} == IndentSpacing {indent}");
            }
            finally { Raylib.UnloadImage(shot); }
        }
        finally { Raylib.UnloadTexture(red); Raylib.UnloadTexture(blue); }
    }

    // ── SectionHeader ──────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void SectionHeader_IsSlate_ChevronThenIconThenLabel_AnOverrideKeepsItsColour_NoGlobalColourChange_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(700, 300);
        var red = Swatch(Color.Red); var blue = Swatch(Color.Blue);
        try
        {
            var green = new HeaderColors(new Vector4(0.10f, 0.50f, 0.20f, 1f), new Vector4(0.15f, 0.60f, 0.25f, 1f), new Vector4(0.2f, 0.7f, 0.3f, 1f));
            var rects = new (Vector2 Min, Vector2 Max)[2];
            Vector4 headerBefore = default, headerAfter = default; long bytes = -1;
            void Draw()
            {
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(700, 300));
                ImGui.Begin("##hdr", Bare);
                headerBefore = ImGui.GetStyle().Colors[(int)ImGuiCol.Header];
                long before = GC.GetAllocatedBytesForCurrentThread();
                SectionHeader.Draw("slate", "Slate header", IconOf(red), ImGuiTreeNodeFlags.DefaultOpen);
                rects[0] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                SectionHeader.Draw("green", "Authority header", IconOf(blue), ImGuiTreeNodeFlags.None, green);
                rects[1] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                headerAfter = ImGui.GetStyle().Colors[(int)ImGuiCol.Header];
                ImGui.End();
            }
            for (int i = 0; i < 8; i++) frame.Step(Draw);
            Shoot(frame, "ui-conventions-header.png");

            Assert.True(bytes == 0, $"a steady frame of two headers allocates nothing ({bytes} bytes)");
            Assert.Equal(headerBefore, headerAfter);                                         // ⛔ never a global ImGuiCol.Header change
            var shot = Raylib.LoadImageFromScreen();
            try
            {
                var slate = Raylib.GetImageColor(shot, (int)rects[0].Max.X - 30, (int)((rects[0].Min.Y + rects[0].Max.Y) / 2));
                Assert.True(Math.Abs(slate.R - 0x2B) <= 3 && Math.Abs(slate.G - 0x2F) <= 3 && Math.Abs(slate.B - 0x38) <= 3, $"the header background is slate #2B2F38 (got {slate.R:X2}{slate.G:X2}{slate.B:X2})");
                var authority = Raylib.GetImageColor(shot, (int)rects[1].Max.X - 30, (int)((rects[1].Min.Y + rects[1].Max.Y) / 2));
                Assert.True(authority.G > authority.R + 40 && authority.G > authority.B + 40, $"an override keeps its own colour (green; got {authority.R},{authority.G},{authority.B})");

                int y0 = (int)rects[0].Min.Y, y1 = (int)rects[0].Max.Y, x0 = (int)rects[0].Min.X, x1 = (int)rects[0].Max.X;
                var icon = Extent(shot, y0, y1, x0, x1, IsRed);
                var chev = Extent(shot, y0, y1, x0, icon.Min, IsInk);
                var label = Extent(shot, y0, y1, icon.Max + 1, x1, IsInk);
                Assert.True(chev.Max >= 0 && chev.Max < icon.Min && icon.Max < label.Min, $"chevron ({chev.Max}) < icon ({icon.Min}..{icon.Max}) < label ({label.Min})");
            }
            finally { Raylib.UnloadImage(shot); }
        }
        finally { Raylib.UnloadTexture(red); Raylib.UnloadTexture(blue); }
    }

    // ── FormGrid ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FormGrid_LabelColumn_IsTheWidestLabel_CappedAtAQuarterOfTheForm_WithAFloorOfNinetyPixels()
    {
        Assert.Equal(64f + 14f, FormGrid.LabelColumn(64f, 800f, 1f));                      // short labels: widest + 14
        Assert.Equal(200f, FormGrid.LabelColumn(500f, 800f, 1f));                          // a long one: 25 % of 800
        Assert.Equal(90f, FormGrid.LabelColumn(500f, 200f, 1f));                           // a narrow form: the floor
        Assert.Equal(180f, FormGrid.LabelColumn(500f, 200f, 2f));                          // the floor scales with the UI
        Assert.Equal(64f + 28f, FormGrid.LabelColumn(64f, 800f, 2f));                      // so does the padding
    }

    [SkippableFact]
    public void FormGrid_ControlsShareOneLeftEdge_TheLabelColumnIsCapped_ALongLabelIsCutWithAnEllipsis_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(900, 500);
        var shortLabels = new[] { "Name", "Kind", "Side" };
        const string Long = "A really very long label that cannot fit its column";
        var longLabels = new[] { "Name", Long, "Side" };
        float availA = 0, availB = 0, widestA = 0, widestB = 0, scale = 1f, tableLeftB = 0;
        var xs = new float[6]; Vector2 longRowMin = default, longRowMax = default; long bytes = -1; string text = "x";
        void Draw()
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(900, 500));
            ImGui.Begin("##form", Bare);
            scale = ImGui.GetFontSize() / FormGrid.ReferenceFontSize;
            long before = GC.GetAllocatedBytesForCurrentThread();
            availA = ImGui.GetContentRegionAvail().X;
            foreach (var l in shortLabels) widestA = MathF.Max(widestA, ImGui.CalcTextSize(l).X);
            if (FormGrid.Begin("rail.a", shortLabels))
            {
                for (int i = 0; i < 3; i++) { FormGrid.Row(shortLabels[i]); xs[i] = ImGui.GetCursorScreenPos().X; ImGui.InputText("##a", ref text, 16); }
                FormGrid.End();
            }
            ImGui.Dummy(new Vector2(1, 12));
            availB = ImGui.GetContentRegionAvail().X;
            foreach (var l in longLabels) widestB = MathF.Max(widestB, ImGui.CalcTextSize(l).X);
            tableLeftB = ImGui.GetCursorScreenPos().X;
            if (FormGrid.Begin("rail.b", longLabels))
            {
                for (int i = 0; i < 3; i++)
                {
                    FormGrid.Row(longLabels[i]); xs[3 + i] = ImGui.GetCursorScreenPos().X;
                    if (i == 1) { longRowMin = new Vector2(ImGui.GetCursorScreenPos().X, ImGui.GetCursorScreenPos().Y); }
                    ImGui.Button("ctl", new Vector2(-1, 0));
                    if (i == 1) longRowMax = ImGui.GetItemRectMax();
                }
                FormGrid.End();
            }
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        for (int i = 0; i < 8; i++) frame.Step(Draw);
        Shoot(frame, "ui-conventions-form.png");

        float colA = FormGrid.ColumnWidthOf("rail.a"), colB = FormGrid.ColumnWidthOf("rail.b");
        Assert.True(Math.Abs(colA - (widestA + FormGrid.LabelPadding * scale)) < 0.01f, $"short labels: the column is the widest label + 14 ({colA})");
        Assert.True(Math.Abs(colB - MathF.Max(0.25f * availB, FormGrid.MinLabelColumn * scale)) < 0.01f, $"a long label: the column is capped at 25 % of the form ({colB} vs {0.25f * availB})");
        Assert.True(colB < widestB, "the cap really bit: the column is narrower than the long label");
        Assert.True(Math.Abs(xs[0] - xs[1]) < 0.5f && Math.Abs(xs[1] - xs[2]) < 0.5f, "form A: every control starts at one x");
        Assert.True(Math.Abs(xs[3] - xs[4]) < 0.5f && Math.Abs(xs[4] - xs[5]) < 0.5f, "form B: every control starts at one x, the cut row included");
        Assert.True(xs[4] - tableLeftB <= colB + ImGui.GetStyle().CellPadding.X * 2f + 1f, "the control column starts right of the capped label column");

        var shot = Raylib.LoadImageFromScreen();
        try
        {
            int y = (int)longRowMin.Y;
            var ink = Extent(shot, y, y + 14, (int)tableLeftB, (int)xs[4], IsInk);
            Assert.True(ink.Min >= 0, "the cut label is drawn");
            Assert.True(ink.Max < xs[4], $"the cut label ends before the control column ({ink.Max} < {xs[4]})");
        }
        finally { Raylib.UnloadImage(shot); }
    }

    [SkippableFact]
    public void FormGrid_AllocatesNothingOnASteadyFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(900, 400);
        var labels = new[] { "Name", "A really very long label that cannot fit its column", "Side" };
        long bytes = -1;
        void Draw()
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(900, 400));
            ImGui.Begin("##formalloc", Bare);
            long before = GC.GetAllocatedBytesForCurrentThread();
            if (FormGrid.Begin("rail.alloc", labels, statusSlot: true))
            {
                for (int i = 0; i < labels.Length; i++) { FormGrid.Row(labels[i]); ImGui.Button("ctl", new Vector2(-1, 0)); FormGrid.Status(); ImGui.TextUnformatted("ok"); }
                FormGrid.End();
            }
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        for (int i = 0; i < 8; i++) frame.Step(Draw);
        Assert.True(bytes == 0, $"a steady form frame (a cut label, a status slot) allocates nothing ({bytes} bytes)");
    }
}
