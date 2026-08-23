using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.DER;
using Hrot.Editor.UI;
using Hrot.Editor.Windows;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 6) — <c>EditorToolbarPanel</c> converted to the <c>PanelSnapshot</c>
/// contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 6. ⭐ Not static chrome:
/// <c>IEditorLogic.CurrentMode</c> drives the toggle button's label, so the panel carries real state —
/// the HOST (<c>EditorToolbarWindow</c>) registers.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class EditorToolbarWindowDumpsItsModeTests : IDisposable
{
    public EditorToolbarWindowDumpsItsModeTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    /// <summary>⭐ A minimal <see cref="IEditorLogic"/> stub with a SETTABLE <c>CurrentMode</c> — every
    /// other member throws, so a rail that let the panel wander further would stop being about the
    /// toolbar's own state.</summary>
    private sealed class ModeOnlyLogic : IEditorLogic
    {
        public SimHostMode CurrentMode { get; set; } = SimHostMode.Internal;

        public void Update() => throw new NotSupportedException();
        public void NewScenario() => throw new NotSupportedException();
        public void SaveScenario(string filePath) => throw new NotSupportedException();
        public void LoadScenario(string filePath) => throw new NotSupportedException();
        public void LoadScenarioByName(string scenarioName) => throw new NotSupportedException();
        public void SaveCurrentScenario() => throw new NotSupportedException();
        public void SaveScenarioAs(string scenarioName) => throw new NotSupportedException();
        public string? LoadedScenarioName => throw new NotSupportedException();
        public IReadOnlyList<string> AvailableScenarios => throw new NotSupportedException();
        public IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario() => throw new NotSupportedException();
        public bool IsScenarioDegraded => throw new NotSupportedException();
        public void ActivateTool(EditorTool tool) => throw new NotSupportedException();
        public void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents) => throw new NotSupportedException();
        public IDerRepo View => throw new NotSupportedException();
        public Task SwitchToExternalAsync() => throw new NotSupportedException();
        public Task SwitchToInternalAsync() => throw new NotSupportedException();
        public void CenterOnEntity(long entityId) => throw new NotSupportedException();
        public void SelectEntity(long entityId) => throw new NotSupportedException();
        public void OpenRenameDialog(long entityId) => throw new NotSupportedException();
        public void RebuildAndReloadAI() => throw new NotSupportedException();
    }

    [Fact]
    public void DeclaresItInstrumented_AndDumpsTheCurrentMode()
    {
        Assert.DoesNotContain("editor_toolbar", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        PanelSnapshot.CaptureEnabled = true;
        var logic = new ModeOnlyLogic { CurrentMode = SimHostMode.External };
        var window = new EditorToolbarWindow(new EditorToolbarPanel(), logic);

        Assert.Contains("editor_toolbar", PanelSnapshot.RegisteredPanels);
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("editor_toolbar");
        Assert.NotNull(vm);
        Assert.Equal(EditorToolbarWindow.Kind, vm!.PanelKind);
        Assert.Equal("External", vm.Dump()["currentMode"]!.GetValue<string>());
    }

    [Fact]
    public void WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var window = new EditorToolbarWindow(new EditorToolbarPanel(), new ModeOnlyLogic());

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("editor_toolbar", PanelSnapshot.RegisteredPanels);
        Assert.Equal("Internal", vm.CurrentMode);
    }
}
