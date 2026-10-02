using System;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.DER;
using Hrot.Core.Network;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Windows;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>InteractionPanel</c>/<c>ExConDataMonitorWindow</c> and
/// <c>OrbatPanel</c>/<c>ExConOrbatWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 6 (the two LIVE panels in
/// the ExCon ×5 set — the other three are <c>DiagnosticsPanel</c> (converted earlier) and the two
/// measured-dead <c>[Obsolete]</c> panels).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ExConDataMonitorAndOrbatWindowsDumpTheirModelsTests : IDisposable
{
    public ExConDataMonitorAndOrbatWindowsDumpTheirModelsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    // ── InteractionPanel / ExConDataMonitorWindow ────────────────────────────────────────────

    [Fact]
    public void DataMonitorWindow_DeclaresItInstrumented_AndDumpsALogEntry()
    {
        Assert.DoesNotContain("excon_data_monitor", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        PanelSnapshot.CaptureEnabled = true;
        var panel = new InteractionPanel();
        panel.AddLog("RX", "MapClickEvent", "hello");
        panel.DrainPendingLogs();
        var window = new ExConDataMonitorWindow(panel, Mock.Of<IExConLogic>());

        Assert.Contains("excon_data_monitor", PanelSnapshot.RegisteredPanels);
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("excon_data_monitor");
        Assert.NotNull(vm);
        Assert.Equal(ExConDataMonitorWindow.Kind, vm!.PanelKind);
        var entries = vm.Dump()["entries"]!.AsArray();
        Assert.Single(entries);
        Assert.Equal("MapClickEvent", entries[0]!["topic"]!.GetValue<string>());
    }

    [Fact]
    public void DataMonitorWindow_WithCaptureOff_PublishesNothing()
    {
        var window = new ExConDataMonitorWindow(new InteractionPanel(), Mock.Of<IExConLogic>());

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.NotNull(vm);
    }

    // ── OrbatPanel / ExConOrbatWindow ─────────────────────────────────────────────────────────

    [Fact]
    public void OrbatWindow_DeclaresItInstrumented_AndDumpsTheHierarchy()
    {
        Assert.DoesNotContain("excon_orbat", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        PanelSnapshot.CaptureEnabled = true;
        var repo = new DerRepo();
        var parent = repo.CreateEntity(1, 100);
        parent.SetDescriptor(new EntityInfoDescriptor { EntityId = 1, Name = "HQ", CommanderId = 0, Affiliation = "FORCE_FRIENDLY" });
        var child = repo.CreateEntity(2, 101);
        child.SetDescriptor(new EntityInfoDescriptor { EntityId = 2, Name = "Tank1", CommanderId = 1, Affiliation = "FORCE_FRIENDLY" });
        var logic = new Mock<IExConLogic>();
        logic.Setup(l => l.Repo).Returns(repo);
        var panel = new OrbatPanel();
        panel.ToggleExpanded(1);   // children only appear under an expanded parent — DrawContent's own rule
        var window = new ExConOrbatWindow(panel, logic.Object);

        Assert.Contains("excon_orbat", PanelSnapshot.RegisteredPanels);
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("excon_orbat");
        Assert.NotNull(vm);
        Assert.Equal(ExConOrbatWindow.Kind, vm!.PanelKind);
        var nodes = vm.Dump()["nodes"]!.AsArray();
        Assert.Equal(2, nodes.Count);
        Assert.Contains(nodes, n => n!["name"]!.GetValue<string>() == "Tank1" && n["depth"]!.GetValue<int>() == 1);
    }

    [Fact]
    public void OrbatWindow_WithCaptureOff_PublishesNothing()
    {
        var logic = new Mock<IExConLogic>();
        logic.Setup(l => l.Repo).Returns(new DerRepo());
        var window = new ExConOrbatWindow(new OrbatPanel(), logic.Object);

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.NotNull(vm);
    }
}
