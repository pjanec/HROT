using System;
using Fdp.Presentation.Icons;
using Fdp.Toolkit.Runner;
using Hrot.Editor.AiShared.Scenarios;
using Hrot.UI.Common.RunMode;
using Xunit;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-4381 (E-1, HANDOFF_Coordinator_To_Ui_Osm_Wizard_Live) — the REAL editor composes the real map tiles and place search, so File ▸ New ▸ Terrain from OpenStreetMap… is
/// no longer greyed for want of them: the wizard has no reason of its own, and the item's label names only the run gate (a headless editor with no session greys every
/// scenario-file item with "no cluster state …", the shared <c>closedReason</c>). 🔴 Red-proof (compiles): <c>Tiles: null, Search: null</c> in <c>EditorSubsystem</c>'s <c>osmServices</c> ⇒ greyed
/// "the map tiles and the place search are not built yet".
/// </summary>
public sealed class TheTerrainFromOsmItemOnTheRealEditorTests
{
    [Fact]
    public void TheRealEditor_EnablesTerrainFromOpenStreetMap()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            var wm = new WM(new IconAtlas(IntPtr.Zero, 512, 512));
            editor.RegisterWindows(wm);
            ((IProvidesRunControls)editor).ComposeScenarioActions(wm);
            var item = wm.ShellCommands.Get(ScenarioMenuCommands.NewTerrainFromOsmId);
            Assert.NotNull(item);
            Assert.Null(editor.OsmWizard!.DisabledReason);                          // tiles, search, pipeline and importer are all composed
            string label = item!.DynamicDisplayName!();
            Assert.DoesNotContain("not built yet", label);
            Assert.True(label == "Terrain from OpenStreetMap…" || label.Contains("no cluster state"), label);   // only the run gate may grey it
        }
        finally { editor.Shutdown(); }
    }
}
