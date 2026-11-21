using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.RunMode;
using ImGuiNET;
using Raylib_cs;
using Xunit;
using static Hrot.Presentation.Tests.RunChoiceRig;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐⭐ <c>CE-3406</c> / <c>CE-3409</c> / <c>CE-3408</c> (AQ108 §0, ind.png) — the REAL time-control toolbar (<see cref="MainToolbarTimeControlSection.Render"/>) on a real frame under Xvfb (the app's fonts), one row per state of
/// <c>docs/UX/run-indicator-visual.svg</c>; the pixels are read back. Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>run-toolbar-states.png</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class RunToolbarFrameRail
{
    private const ImGuiWindowFlags Bare = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;
    private sealed class Ender : IExerciseEnder { public bool CanEnd => false; public bool HasEnded { get; set; } public bool End() => false; }

    private static bool Near(Color c, Vector4 v, int tol) =>
        Math.Abs(c.R - v.X * 255f) <= tol && Math.Abs(c.G - v.Y * 255f) <= tol && Math.Abs(c.B - v.Z * 255f) <= tol;

    internal static int Count(Image shot, int x0, int y0, int x1, int y1, Func<Color, bool> what)
    {
        int n = 0;
        for (int x = Math.Max(0, x0); x < Math.Min(shot.Width, x1); x++)
            for (int y = Math.Max(0, y0); y < Math.Min(shot.Height, y1); y++)
                if (what(Raylib.GetImageColor(shot, x, y))) n++;
        return n;
    }

    internal sealed record Row(string Name, Rig Rig, Vector4 Indicator);

    internal static List<Row> Rows()
    {
        var red = new Vector4(0.90f, 0.22f, 0.22f, 1f);
        var rows = new List<Row>();
        rows.Add(new("edit-saveable", With(ClusterState.OperatingEdit, RunArmed.Preview), NodeEditor.Core.Widgets.IconPalette.Yellow));
        var variant = With(ClusterState.OperatingEdit, RunArmed.Preview); variant.C.SaveDisabledReason = "variant 3";
        rows.Add(new("edit-variant", variant, NodeEditor.Core.Widgets.IconPalette.Violet));
        rows.Add(new("preview-running", With(ClusterState.OperatingPreview, running: true, clock: 5), NodeEditor.Core.Widgets.IconPalette.Violet));
        rows.Add(new("live-briefing", With(ClusterState.OperatingLive), red));
        rows.Add(new("live-running", With(ClusterState.OperatingLive, running: true, clock: 60), red));
        rows.Add(new("live-notrecorded", With(ClusterState.OperatingLive, running: true, clock: 60, runNotRecorded: true), NodeEditor.Core.Widgets.IconPalette.Green));
        var ended = With(ClusterState.OperatingLive, clock: 754); ended.F.Ender = new Ender { HasEnded = true }; ended.Choice.Update();
        rows.Add(new("live-ended", ended, red));
        rows.Add(new("dar", With(ClusterState.OperatingReview, clock: 30), NodeEditor.Core.Widgets.IconPalette.Amber));
        rows.Add(new("aar", With(ClusterState.OperatingReplay, clock: 30), NodeEditor.Core.Widgets.IconPalette.Blue));
        return rows;
    }

    private const float RowH = 34f, W = 560f;

    [SkippableFact]
    public void R3_EveryState_DrawsItsIndicatorGlyphInItsColour_OnARealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var rows = Rows();
        using var frame = UiFrameHarness.Begin((int)W, (int)(RowH * rows.Count + 4));
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        var sections = rows.ConvertAll(r => new MainToolbarTimeControlSection(r.Rig.F, choice: r.Rig.Choice));
        void Draw()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                ImGui.SetNextWindowPos(new Vector2(0, i * RowH)); ImGui.SetNextWindowSize(new Vector2(W, RowH));
                ImGui.Begin("##row" + i, Bare);
                sections[i].Render();
                ImGui.End();
            }
        }
        for (int i = 0; i < 5; i++) frame.Step(Draw);
        var shot = Raylib.LoadImageFromScreen();
        try
        {
            if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "run-toolbar-states.png"));
            for (int i = 0; i < rows.Count; i++)
            {
                var want = rows[i].Indicator;
                // the indicator button sits at the row's left edge (window padding .. ~48 px); blinking glyphs are caught at any alpha ≥ 0.3 — look for the hue, not the exact value
                int hits = Count(shot, 0, (int)(i * RowH), 52, (int)((i + 1) * RowH), p => Near(p, want, 40) || (rows[i].Rig.Choice.Indicator.Level == IndicatorLevel.Blink && Near(p, want * 0.5f + new Vector4(0.1f, 0.1f, 0.1f, 0), 40)));
                Assert.True(hits > 12, $"{rows[i].Name}: the indicator glyph is drawn in its colour ({hits} px)");
            }
        }
        finally { Raylib.UnloadImage(shot); }
    }
}
