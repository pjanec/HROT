using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>RuntimeInspectorWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class RuntimeInspectorWindowDumpsItsStateTests : IDisposable
{
    private sealed class StubPane : IRuntimeInspectorPane
    {
        public AssetKind TargetKind => AssetKind.BTree;
        public void Draw() { }
    }

    public RuntimeInspectorWindowDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static RuntimeInspectorWindow MakeWindow(string id)
        => new(new EditorSelectionStore(), new DebugSessionRegistry(), idOverride: id);

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "ai_runtime_inspector_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(window);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheEmptyState()
    {
        const string id = "ai_runtime_inspector_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var window = MakeWindow(id);   // no active asset ⇒ NoDocument

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet(id);
        Assert.NotNull(vm);
        Assert.Equal(id, vm!.PanelId);
        Assert.Equal(RuntimeInspectorWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.Equal("No document is open.", dump["emptyState"]!.GetValue<string>());
        Assert.Equal(0, dump["registeredPaneCount"]!.GetValue<int>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        const string id = "ai_runtime_inspector_rail3";
        var window = MakeWindow(id);
        window.RegisterPane(new StubPane());   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
        Assert.Equal(1, vm.RegisteredPaneCount);
    }
}
