using System;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.UiFrameRail;
using Hrot.Editor.Windows;
using Hrot.UI.Common.AddEntity;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>⭐ CE-5006 W2 (TE-19) — the main toolbar's Toolbars split icon and Tactical Map Layers toggle: the ▾ list names both strips with ✓ on the open ones; the face toggles the strip toggled last and is lit while any is open; the layers icon follows the layer editor.</summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class EditorToolbarsSectionTests
{
    private static (EditorToolbarsSection Section, ScenarioToolsWindow A, TerrainToolsWindow B, Func<bool>[] Layers, int[] Toggles) Rig(bool layersShown = false)
    {
        var a = new ScenarioToolsWindow(() => null, () => null, () => null);
        var b = new TerrainToolsWindow(() => null, () => null, () => null, TerrainPickCatalog.FeaturePicks(), Array.Empty<(string, string, float)>());
        bool shown = layersShown; int[] toggles = { 0 };
        var s = new EditorToolbarsSection(new ToolStripWindow[] { a, b }, () => shown, () => { toggles[0]++; shown = !shown; });
        return (s, a, b, new Func<bool>[] { () => shown }, toggles);
    }

    /// <summary>The ▾ lists BOTH toolbars, ✓ = open (Scenario tools open, Terrain closed by default). 🔴 Red-proof: list only the first strip ⇒ red.</summary>
    [Fact]
    public void TheSplitList_NamesBothToolbars_WithACheckOnTheOpenOnes()
    {
        var (s, _, b, _, _) = Rig();
        Assert.Equal(new[] { ("Scenario tools", true), ("Terrain", false) }, s.Items().Select(i => (i.Label, i.Checked)).ToArray());
        s.Toggle(1);
        Assert.True(b.IsOpen);
        Assert.Equal(new[] { true, true }, s.Items().Select(i => i.Checked).ToArray());
    }

    /// <summary>The face toggles the toolbar toggled LAST, and is lit while any toolbar is open. 🔴 Red-proof: always toggle strip 0 ⇒ red.</summary>
    [Fact]
    public void TheFace_TogglesTheToolbarToggledLast_AndIsLitWhileAnyIsOpen()
    {
        var (s, a, b, _, _) = Rig();
        s.Toggle(1);            // the Terrain strip opens; it is now the face's
        s.ToggleLast();         // …and the face closes it again
        Assert.False(b.IsOpen); Assert.True(a.IsOpen); Assert.True(s.AnyOpen);
        s.Toggle(0);            // Scenario tools closes
        Assert.False(s.AnyOpen);
        s.ToggleLast(); Assert.True(a.IsOpen); Assert.True(s.AnyOpen);
    }

    /// <summary>The Tactical Map Layers icon reports the layer editor's state and asks for the toggle. 🔴 Red-proof: ToggleLayers doing nothing ⇒ red.</summary>
    [Fact]
    public void TheLayersIcon_FollowsTheLayerEditor()
    {
        var (s, _, _, _, toggles) = Rig();
        Assert.False(s.LayersShown);
        s.ToggleLayers();
        Assert.Equal(1, toggles[0]); Assert.True(s.LayersShown);
    }

    /// <summary>A real frame of the two main-toolbar icons (and the ▾ list opened) — a screenshot when <c>HROT_RAIL_SHOTS</c> names a folder.</summary>
    [SkippableFact]
    public void TheToolbarIcons_Draw()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (s, _, _, _, _) = Rig(layersShown: true);
        using var frame = UiFrameHarness.Begin();
        frame.StepN(3, () =>
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(260, 60), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.Begin("Main toolbar (rail)");
            s.DrawToolbars(); ImGuiNET.ImGui.SameLine(); s.DrawLayers();
            ImGuiNET.ImGui.End();
        });
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "main-toolbar-icons.png"));
    }
}
