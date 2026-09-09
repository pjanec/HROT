using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 2) — <c>RuntimeDetailsView</c> converted to the <c>PanelSnapshot</c>
/// contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
///
/// <para>⭐ The view id carries the pane's <c>TargetKind</c> — see
/// <see cref="RuntimeDetailsViewDescriptor.ViewIdFor"/> — so the composed address disambiguates BOTH
/// the host AND which kind of pane is hosted.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class RuntimeDetailsViewDumpsItsStateTests : IDisposable
{
    private sealed class StubPane : IRuntimeInspectorPane
    {
        public AssetKind TargetKind => AssetKind.BTree;
        public void Draw() { }
    }

    public RuntimeDetailsViewDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static string Addr(string idScope) =>
        $"{idScope}/{RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.BTree)}";

    [Fact]
    public void FirstDraw_DeclaresItInstrumented_AtTheComposedAddress()
    {
        var view = new RuntimeDetailsView(new StubPane());
        var addr = Addr("host1");
        Assert.DoesNotContain(addr, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        view.SimulateDraw("host1");

        Assert.Contains(addr, PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheTargetKind()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new RuntimeDetailsView(new StubPane());

        view.SimulateDraw("host1");

        var stored = PanelSnapshot.TryGet(Addr("host1"));
        Assert.NotNull(stored);
        Assert.Equal(Addr("host1"), stored!.PanelId);
        Assert.Equal(RuntimeDetailsViewDescriptor.ViewIdFor(AssetKind.BTree), stored.PanelKind);

        var dump = stored.Dump();
        Assert.Equal("BTree", dump["targetKind"]!.GetValue<string>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        var view = new RuntimeDetailsView(new StubPane());   // CaptureEnabled stays false

        var vm = view.SimulateDraw("host1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(Addr("host1"), PanelSnapshot.RegisteredPanels);
        Assert.Equal("BTree", vm.TargetKind);
    }
}
