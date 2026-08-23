using System;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.Navigation;
using Hrot.SimHost.Windows;
using Xunit;

namespace Hrot.SimHost.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 6) — <c>FakeNavigationInspectorWindow</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 6. ⚠ This window has no
/// separate panel class — it IS a <c>ManagedWindow</c> — so it registers ITSELF, using its own
/// <c>Id</c>/local <c>Kind</c> literal (no sibling host of this window exists).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class FakeNavigationInspectorWindowDumpsItsStateTests : IDisposable
{
    public FakeNavigationInspectorWindowDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    [Fact]
    public void DeclaresItInstrumented_AtConstruction()
    {
        Assert.DoesNotContain("fake_nav_inspector", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        _ = new FakeNavigationInspectorWindow(() => null);

        Assert.Contains("fake_nav_inspector", PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void WithNoWorld_TheDumpSaysSo()
    {
        PanelSnapshot.CaptureEnabled = true;
        var window = new FakeNavigationInspectorWindow(() => null);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("fake_nav_inspector");
        Assert.NotNull(vm);
        Assert.Equal(FakeNavigationInspectorWindow.Kind, vm!.PanelKind);
        Assert.False(vm.Dump()["hasWorld"]!.GetValue<bool>());
    }

    [Fact]
    public void WithAWorld_TheDumpCarriesTheCorridorPreviewCount_ARealField()
    {
        PanelSnapshot.CaptureEnabled = true;
        var repo = new EntityRepository();
        repo.SetSingletonManaged<INavmeshProvider>(null);   // "not set" is a real production state (paranoid-mode requires an explicit set)
        repo.RegisterComponent<NavigationCorridorPreview>();
        var e1 = repo.CreateEntity();
        repo.AddComponent(e1, new NavigationCorridorPreview());
        var e2 = repo.CreateEntity();
        repo.AddComponent(e2, new NavigationCorridorPreview());
        var window = new FakeNavigationInspectorWindow(() => repo);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("fake_nav_inspector");
        Assert.NotNull(vm);
        var dump = vm!.Dump();
        Assert.True(dump["hasWorld"]!.GetValue<bool>());
        Assert.Equal(2, dump["corridorPreviewCount"]!.GetValue<int>());
        Assert.Equal("Backend: none (no providers registered)", dump["backendLabel"]!.GetValue<string>());
    }

    [Fact]
    public void WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var window = new FakeNavigationInspectorWindow(() => null);

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("fake_nav_inspector", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
    }
}
