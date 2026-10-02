using System;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Panels;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.Runner;
using Hrot.Core.Network;
using Hrot.ExCon;
using Hrot.ExCon.Windows;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 follow-up — <c>DerEntityInspectorPanel</c> wired as
/// <see cref="ExConDerEntityInspectorWindow"/>.</b> 📄 The panel was one of the six no-host panels
/// measured by <c>BP-467</c> — <c>docs/UX/UX_Feature_DeadUI_Removal.md:102</c> claimed "ExCon uses
/// <c>DerEntityInspectorPanel</c>", which was false until now (the panel's only production caller was
/// <c>ExConMock.DrawUI</c>'s non-window-managed path). This test suite makes that claim true and pins it.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ExConDerEntityInspectorWindowDumpsItsModelTests : IDisposable
{
    public ExConDerEntityInspectorWindowDumpsItsModelTests()
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
    public void Window_DeclaresItInstrumented_AndDumpsTheEntityList()
    {
        Assert.DoesNotContain("excon_der_entity_inspector", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        PanelSnapshot.CaptureEnabled = true;
        var repo = new DerRepo();
        var e1 = repo.CreateEntity(1, 100);
        e1.SetDescriptor(new EntityInfoDescriptor { EntityId = 1, Name = "HQ", CommanderId = 0, Affiliation = "FORCE_FRIENDLY" });
        var logic = new Mock<IExConLogic>();
        logic.Setup(l => l.Repo).Returns(repo);
        var panel = new DerEntityInspectorPanel();
        var window = new ExConDerEntityInspectorWindow(panel, logic.Object);

        Assert.Contains("excon_der_entity_inspector", PanelSnapshot.RegisteredPanels);
        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("excon_der_entity_inspector");
        Assert.NotNull(vm);
        Assert.Equal(ExConDerEntityInspectorWindow.Kind, vm!.PanelKind);
        var dump = vm.Dump();
        Assert.Equal(1, dump["totalEntityCount"]!.GetValue<int>());
        Assert.Single(dump["entityIds"]!.AsArray());
    }

    [Fact]
    public void Window_WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var logic = new Mock<IExConLogic>();
        logic.Setup(l => l.Repo).Returns(new DerRepo());
        var window = new ExConDerEntityInspectorWindow(new DerEntityInspectorPanel(), logic.Object);

        Assert.Contains("excon_der_entity_inspector", PanelSnapshot.RegisteredPanels);
        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.NotNull(vm);
    }

    // ── production composition-root rail ─────────────────────────────────────────────────────
    // ⭐⭐⭐ the whole point of this conversion: prove the host is actually REGISTERED by
    // ExConSubsystem.RegisterWindows, not just constructible in a test.

    [Fact]
    public void ExConSubsystem_RegisterWindows_RegistersTheDerEntityInspectorWindow()
    {
        var subsystem = new ExConSubsystem();
        subsystem.Initialize(new SubsystemConfig { Headless = true, DomainId = 223 });
        try
        {
            var wm = new Fdp.Presentation.WindowManager.WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f));

            subsystem.RegisterWindows(wm);

            Assert.True(wm.TryGetWindow("excon_der_entity_inspector", out var window),
                "Expected 'excon_der_entity_inspector' to be registered by ExConSubsystem.RegisterWindows.");
            Assert.IsType<ExConDerEntityInspectorWindow>(window);
        }
        finally
        {
            subsystem.Shutdown();
        }
    }
}
