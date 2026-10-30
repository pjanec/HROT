using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.IG.Systems;
using Hrot.IG.UI;
using Hrot.IG.Windows;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.IG.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 6) — the four plain <c>Hrot.IG</c> panels converted to the
/// <c>PanelSnapshot</c> contract, via their HOST windows in <see cref="IgWindows"/>.</b>
/// 📄 <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 6 ·
/// <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example. ⭐ Each panel is a PLAIN panel with no
/// address of its own — the host registers, per the gotcha table.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class IgWindowsDumpTheirModelsTests : IDisposable
{
    public IgWindowsDumpTheirModelsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    // ── IgDebugPanel / IgDebugWindow ─────────────────────────────────────────────────────────

    [Fact]
    public void DebugWindow_DeclaresItInstrumented_AndDumpsARealField()
    {
        Assert.DoesNotContain("ig_debug", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        PanelSnapshot.CaptureEnabled = true;
        var state = new DebugPanelState(new MapUserConfig()) { CurrentSimTime = 12.5, CurrentWallTicks = 99 };
        state.ForceHostile = true;
        var window = new IgDebugWindow(new IgDebugPanel(state));

        Assert.Contains("ig_debug", PanelSnapshot.RegisteredPanels);
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("ig_debug");
        Assert.NotNull(vm);
        Assert.Equal(IgDebugWindow.Kind, vm!.PanelKind);
        var dump = vm.Dump();
        Assert.Equal(12.5, dump["currentSimTime"]!.GetValue<double>());
        Assert.True(dump["forceHostile"]!.GetValue<bool>());
    }

    [Fact]
    public void DebugWindow_WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var window = new IgDebugWindow(new IgDebugPanel(new DebugPanelState(new MapUserConfig())));

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("ig_debug", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
    }



    // ── WaypointEditorPanel / WaypointEditorWindow (⭐ CE-1096 — shared, in Hrot.Presentation) ─────────────────────────────────────────

    private sealed class StubRouteState : IRouteWaypointEditorState
    {
        private RouteWaypoint _wp;
        public int SelectedVertexIndex { get; }
        public StubRouteState(int selectedIndex, RouteWaypoint wp) { SelectedVertexIndex = selectedIndex; _wp = wp; }
        public ref RouteWaypoint GetSelectedWaypointRef() => ref _wp;
        public void CommitEdits() { }
    }

    [Fact]
    public void WaypointEditorWindow_DeclaresItInstrumented_AndDumpsARealField()
    {
        Assert.DoesNotContain("ig_waypoint_editor", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        PanelSnapshot.CaptureEnabled = true;
        var wp = new RouteWaypoint { TargetSpeed = 7.5f };
        var window = new Hrot.Presentation.Windows.WaypointEditorWindow(new Hrot.UI.Common.Panels.WaypointEditorPanel(() => new StubRouteState(0, wp)), Hrot.Presentation.Windows.ScenarioPanelWindowIds.IgWaypoint, "IG", default);

        Assert.Contains("ig_waypoint_editor", PanelSnapshot.RegisteredPanels);
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("ig_waypoint_editor");
        Assert.NotNull(vm);
        Assert.Equal(Hrot.Presentation.Windows.WaypointEditorWindow.Kind, vm!.PanelKind);
        var dump = vm.Dump();
        Assert.True(dump["hasSelection"]!.GetValue<bool>());
        Assert.Equal(7.5f, dump["targetSpeed"]!.GetValue<float>());
    }

    [Fact]
    public void WaypointEditorWindow_WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var window = new Hrot.Presentation.Windows.WaypointEditorWindow(new Hrot.UI.Common.Panels.WaypointEditorPanel(() => null), Hrot.Presentation.Windows.ScenarioPanelWindowIds.IgWaypoint, "IG", default);

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("ig_waypoint_editor", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
    }
}
