using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Icons;
using Fdp.Toolkit.Runner;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.RunMode;
using ImGuiNET;
using Raylib_cs;
using Xunit;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐ <b>U-1 (📄 docs/blueprints/batches/NOTE_Coordinator_To_Ui_Start_Screen_Real_Menu.md; AQ108 §0, storyboard ① / ⑧)</b> — the Start screen drawn over the REAL editor's own File menu (<c>EditorSubsystem.RegisterWindows</c> +
/// <c>ComposeScenarioActions</c>: <i>Load Scenario… · New Scenario… · Load Checkpoint… · Open Exercise Recording… · Close · Exit</i>, and the shell's Help ▸ About), ⛔ never a test menu with made-up labels (the user's Windows screenshot: no icons,
/// labels past their buttons, the row glyphs on the words, the heading cut — all invisible on a synthetic <c>"Open Scenario"</c> menu). At UI scale 1 AND above 1 (<c>FontGlobalScale</c>: the user's font is larger than the rails').
/// Renders <c>u1-start-real-editor-x&lt;scale&gt;.png</c> under <c>HROT_RAIL_SHOTS</c> (no PNG in git).
/// 🔴 Mutations (each compiles): <c>ActionLook</c> keyed back on <c>b.Label</c> ⇒ no glyphs; <c>_actionW</c> pinned at 220 ⇒ the long label overflows at scale 1.5; the row button's words placed at <c>min.X + 0.72 * width</c> ⇒ the glyph/word rail reds;
/// <c>ToButton</c> keeping the full label ⇒ the "(unavailable" assert reds.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheStartScreenOnTheRealEditorTests
{
    private static StartSurfaceWindow.StartButton B(string label, bool on, string? why, string glyph, Vector4 c)
        => new(label, on, () => { }, why, new NodeEditor.Core.Widgets.IconGlyph(glyph, c));

    private static StartSurfaceWindow.StartRecentRow[] Rows() => new[]
    {
        new StartSurfaceWindow.StartRecentRow("Night attack", "Live · just now · exercise 3",
            B("Live", true, null, "", new(0.9f, 0.3f, 0.3f, 1f)), B("Edit", true, null, "", new(0.95f, 0.75f, 0.2f, 1f)), B("AAR", true, null, "", new(0.3f, 0.6f, 1f, 1f)),
            JustRun: true, Hint: "Night attack > AAR = the replay of exercise 3, at its start"),
        new StartSurfaceWindow.StartRecentRow("Bridgehead", "edited yesterday · never run",
            B("Live", true, null, "", new(0.9f, 0.3f, 0.3f, 1f)), B("Edit", false, "editing needs an author seat", "", new(0.95f, 0.75f, 0.2f, 1f)), B("AAR", false, "never run", "", new(0.3f, 0.6f, 1f, 1f))),
    };

    [SkippableTheory]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    public void U1_TheRealEditorsStartScreen_HasItsIcons_FitsItsLabels_AndKeepsGlyphAndWordsApart(float scale)
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        using var frame = UiFrameHarness.Begin();
        float oldScale = ImGui.GetIO().FontGlobalScale;
        try
        {
            var wm = new WM(new IconAtlas(nint.Zero, 1, 1, 16f));
            editor.RegisterWindows(wm);                                                    // the editor's own windows + menus …
            ((IProvidesRunControls)editor).ComposeScenarioActions(wm);                     // … and its scenario File menu, composed by the host that owns ▶ (LocalWindowController.ComposeRunControls)
            MenuCommandAdapter.RegisterAction(wm.GlobalMenu, wm.ShellCommands, "shell.exit", "File/Exit", () => { }, allowedInIdle: true);   // LocalWindowController.ComposeStartSurface's own Exit item
            var rows = Rows();
            var surface = new StartSurfaceWindow(wm.GlobalMenu, () => StartVariant.Actions, () => wm.StatusBar.Height, recent: () => rows);
            wm.RegisterWindow(surface);
            wm.SwitchPerspective(WM.StartPerspectiveName);

            ImGui.GetIO().FontGlobalScale = scale;
            Vector2 pos = default, size = default;
            frame.StepN(4, () => { wm.Render(); var vp = ImGui.GetMainViewport(); pos = vp.WorkPos; size = vp.WorkSize; });

            // the actions are the REAL menu's: the editor's own labels, in its own order
            var model = surface.Model();
            string[] ids = model.Buttons.Select(b => b.CommandId ?? "(" + b.IconKey + ")").ToArray();
            Assert.Equal(new[] { "scenario.load", "scenario.new", "scenario.loadCheckpoint", "scenario.openRecording", "scenario.close", "shell.exit" }, ids.Take(6).ToArray());
            Assert.Contains("(status/info)", ids);   // Help ▸ About, a contributed item keyed on its icon

            // ① every action has its icon
            var probes = surface.ActionProbes;
            Assert.Equal(model.Buttons.Count, probes.Count);
            Assert.All(probes, p => Assert.True(p.HasGlyph, $"'{p.Label}' has no icon"));

            // ② no label overflows its button (the right edge of the words + the button's own padding is inside the box); the reason is NOT in the label
            Assert.All(probes, p => Assert.True(p.TextRight + 6f <= p.Max.X, $"'{p.Label}' runs past its button ({p.TextRight:0} + 6 > {p.Max.X:0}) at scale {scale}"));
            Assert.All(model.Buttons, b => Assert.DoesNotContain("(unavailable", b.Label));
            Assert.All(model.Buttons.Where(b => !b.Enabled), b => Assert.False(string.IsNullOrEmpty(b.Why), $"disabled '{b.Label}' says why in its tooltip"));
            // one column: every action at one x and one width
            Assert.All(probes, p => Assert.InRange(p.Min.X - probes[0].Min.X, -0.5f, 0.5f));
            Assert.All(probes, p => Assert.InRange(p.Max.X - probes[0].Max.X, -0.5f, 0.5f));

            // ③ each Recent row button's words start right of its glyph (and inside the button)
            var rb = surface.RowButtonProbes;
            Assert.Equal(6, rb.Count);
            Assert.All(rb, p =>
            {
                Assert.True(p.HasGlyph);
                Assert.True(p.TextLeft >= p.GlyphRight + 2f, $"'{p.Label}': the words start at {p.TextLeft:0} but the glyph ends at {p.GlyphRight:0}");
                Assert.True(p.TextRight + 4f <= p.Max.X, $"'{p.Label}' runs past its button at scale {scale}");
            });

            // ④ the whole panel (both columns, the wrapped heading) stays inside the surface, and the Recent boxes inside the panel
            var (pmin, pmax) = surface.PanelRect;
            Assert.True(pmin.X >= pos.X && pmax.X <= pos.X + size.X, $"the panel ({pmin.X:0}..{pmax.X:0}) fits the {size.X:0} px surface at scale {scale}");
            Assert.All(surface.RecentRowRects, r => Assert.True(r.Max.X <= pmax.X, "a Recent row inside the panel"));
            Assert.True(surface.RecentColumnRight <= pmax.X);

            string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-start");
            Directory.CreateDirectory(dir);
            frame.Screenshot(Path.Combine(dir, $"u1-start-real-editor-x{scale:0.0}.png"));
        }
        finally
        {
            ImGui.GetIO().FontGlobalScale = oldScale;
            editor.Shutdown();
        }
    }
}
