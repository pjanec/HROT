using System;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.MuscleCharacter.Animation.Fake.Components;
using Hrot.MuscleCharacter.Animation.Fake.Windows;
using Xunit;

namespace Hrot.MuscleCharacter.Animation.Fake.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 6) — <c>FakeAnimBackendInspectorWindow</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 6. ⚠ This window has no
/// separate panel class — it registers ITSELF, using its own <c>Id</c>/local <c>Kind</c> literal.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class FakeAnimBackendInspectorWindowDumpsItsStateTests : IDisposable
{
    public FakeAnimBackendInspectorWindowDumpsItsStateTests()
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
        Assert.DoesNotContain("anim_backend_inspector", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        _ = new FakeAnimBackendInspectorWindow();

        Assert.Contains("anim_backend_inspector", PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void WithNoBackendSet_TheDumpSaysSo()
    {
        PanelSnapshot.CaptureEnabled = true;
        var window = new FakeAnimBackendInspectorWindow();

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("anim_backend_inspector");
        Assert.NotNull(vm);
        Assert.Equal(FakeAnimBackendInspectorWindow.Kind, vm!.PanelKind);
        Assert.Equal(0, vm.Dump()["entityCount"]!.GetValue<int>());
    }

    [Fact]
    public void WithABackend_TheDumpCarriesTheEntityCount_ARealField()
    {
        PanelSnapshot.CaptureEnabled = true;
        var repo = new EntityRepository();
        repo.RegisterComponent<FakeAnimBackendState>();
        var e1 = repo.CreateEntity();
        repo.AddComponent(e1, new FakeAnimBackendState { Generation = 3, TotalTicks = 99 });
        var e2 = repo.CreateEntity();
        repo.AddComponent(e2, new FakeAnimBackendState());
        var window = new FakeAnimBackendInspectorWindow();
        window.SetBackend(repo);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("anim_backend_inspector");
        Assert.NotNull(vm);
        var dump = vm!.Dump();
        Assert.Equal(2, dump["entityCount"]!.GetValue<int>());
        Assert.False(dump["hasSelection"]!.GetValue<bool>());   // no selection made yet
    }

    [Fact]
    public void WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var window = new FakeAnimBackendInspectorWindow();

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("anim_backend_inspector", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
    }
}
