using System;
using System.Collections.Generic;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.Windows;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the five group-5 "twin" panels' Editor-perspective hosts, converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 5 (twin-diff finding: the
/// <c>Hrot.UI.Common</c> PROJECT is unreferenced dead code; this SHIPPED copy, physically inside
/// <c>Hrot.Presentation</c>, is the one converted). ⚠ The ExCon-perspective siblings
/// (<c>ExConWindows.cs</c>, <c>ExConMock.cs</c>) are NOT yet converted — reported as a finding, not
/// silently skipped; when they are, they must cite the SAME <c>PanelIds</c> constants asserted here.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class EditorSharedPanelWindowsDumpTheirModelsTests : IDisposable
{
    public EditorSharedPanelWindowsDumpTheirModelsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private sealed class FakeMapConfigController : IMapConfigController
    {
        public MapLayerState GetCurrentConfig() => new(true, true, true, true, true, true, false);
        public void ApplyConfig(MapLayerState config) { }
    }

    private sealed class FakeSpawnController : ISpawnController
    {
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null) { }
        public void StartAreaAuthoringMode(string styleOverrideJson = "") { }
        public void StartRouteAuthoringMode() { }
    }

    private sealed class FakeOrbatDataProvider : IOrbatDataProvider
    {
        public IReadOnlyList<OrbatNodeViewModel> GetVisibleNodes(string filterText, HashSet<int> expandedNodes)
            => new List<OrbatNodeViewModel> { new(1, "Alpha", 0, false, false, true) };
    }

    private sealed class FakeOrbatController : IOrbatController
    {
        public void SelectEntity(int entityId) { }
        public void CreateUnit(long tkbType) { }
        public void ToggleExpanded(int entityId) { }
        public void RequestEmbark(int passengerEntityId, int vehicleEntityId) { }
        public void RequestDisembark(int passengerEntityId) { }
        public void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId) { }
        public void RequestRemoveSubordinate(int subordinateEntityId) { }
    }

    private sealed class FakePreviewController : IPreviewController
    {
        public bool IsInPreviewMode { get; set; }
        public void EnterPreviewMode(bool startPaused = false) => IsInPreviewMode = true;
        public void ExitPreviewMode() => IsInPreviewMode = false;
    }

    private sealed class FakeZoneAuthoringController : IZoneAuthoringController
    {
        public void SetRoadNetworkPath(string activeZoneName, string assetPath) { }
        public void StartObstaclePlacementMode(string activeZoneName, float radius) { }
    }

    // ── ConfigPanel ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ConfigWindow_RegistersUnderConfigKind_AndDumpsLayerState()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = new ConfigPanel { Grid = true };
        var window = new Hrot.Presentation.Windows.ConfigPanelWindow(panel, new FakeMapConfigController(), Hrot.Presentation.Windows.ScenarioPanelWindowIds.EditorConfig, "Scenario", default);

        Assert.Contains("editor_config", PanelSnapshot.RegisteredPanels);   // declared at ctor
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("editor_config");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.Config, vm!.PanelKind);
        Assert.True(vm.Dump()["grid"]!.GetValue<bool>());
    }

    // ── SpawnerPanel ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SpawnerWindow_RegistersUnderSpawnerKind_AndDumpsTheCatalog()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = new SpawnerPanel(new[] { new TkbCatalogEntry(42, "T-72") });
        var window = new Hrot.Presentation.Windows.SpawnerPanelWindow(panel, new FakeSpawnController(), Hrot.Presentation.Windows.ScenarioPanelWindowIds.EditorSpawner, "Scenario", default);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("editor_spawner");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.Spawner, vm!.PanelKind);
        Assert.Single(vm.Dump()["filteredEntries"]!.AsArray());
    }

    // ── SharedOrbatPanel ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void SharedOrbatWindow_RegistersUnderSharedOrbatKind_AndDumpsVisibleNodes()
    {
        PanelSnapshot.CaptureEnabled = true;
        var window = new Hrot.Presentation.Windows.SharedOrbatPanelWindow(new SharedOrbatPanel(), new FakeOrbatDataProvider(), new FakeOrbatController(), Hrot.Presentation.Windows.ScenarioPanelWindowIds.EditorOrbat, "Scenario", default);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("editor_shared_orbat");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.SharedOrbat, vm!.PanelKind);
        var nodes = vm.Dump()["nodes"]!.AsArray();
        Assert.Single(nodes);
        Assert.Equal("Alpha", nodes[0]!["name"]!.GetValue<string>());
    }

    // ── PreviewPanel ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PreviewWindow_RegistersUnderPreviewKind_AndDumpsTheMode()
    {
        PanelSnapshot.CaptureEnabled = true;
        var ctrl = new FakePreviewController { IsInPreviewMode = true };
        var window = new EditorPreviewWindow(new PreviewPanel(), ctrl);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("editor_preview");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.Preview, vm!.PanelKind);
        Assert.True(vm.Dump()["isInPreviewMode"]!.GetValue<bool>());
    }

    // ── ZoneEditorPanel ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void ZoneEditorWindow_RegistersUnderZoneEditorKind_AndDumpsZoneName()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = new ZoneEditorPanel { ZoneName = "test_zone" };
        var window = new EditorZoneEditorWindow(panel, new FakeZoneAuthoringController());

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("editor_zone_editor");
        Assert.NotNull(vm);
        Assert.Equal(PanelIds.ZoneEditor, vm!.PanelKind);
        Assert.Equal("test_zone", vm.Dump()["zoneName"]!.GetValue<string>());
    }

    // ── capture-off publishes nothing, for all five ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_NoneOfTheFivePublish()
    {
        new Hrot.Presentation.Windows.ConfigPanelWindow(new ConfigPanel(), new FakeMapConfigController(), Hrot.Presentation.Windows.ScenarioPanelWindowIds.EditorConfig, "Scenario", default).SimulateDrawClientArea();
        new Hrot.Presentation.Windows.SpawnerPanelWindow(new SpawnerPanel(), new FakeSpawnController(), Hrot.Presentation.Windows.ScenarioPanelWindowIds.EditorSpawner, "Scenario", default).SimulateDrawClientArea();
        new Hrot.Presentation.Windows.SharedOrbatPanelWindow(new SharedOrbatPanel(), new FakeOrbatDataProvider(), new FakeOrbatController(), Hrot.Presentation.Windows.ScenarioPanelWindowIds.EditorOrbat, "Scenario", default).SimulateDrawClientArea();
        new EditorPreviewWindow(new PreviewPanel(), new FakePreviewController()).SimulateDrawClientArea();
        new EditorZoneEditorWindow(new ZoneEditorPanel(), new FakeZoneAuthoringController()).SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("editor_config", PanelSnapshot.RegisteredPanels);
        Assert.Contains("editor_spawner", PanelSnapshot.RegisteredPanels);
        Assert.Contains("editor_shared_orbat", PanelSnapshot.RegisteredPanels);
        Assert.Contains("editor_preview", PanelSnapshot.RegisteredPanels);
        Assert.Contains("editor_zone_editor", PanelSnapshot.RegisteredPanels);
    }
}
