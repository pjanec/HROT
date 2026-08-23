using System;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.Scenario;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 6) — <c>ScenarioComponentsView</c> converted to the <c>PanelSnapshot</c>
/// contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 6's <c>*DetailsView</c>
/// instruction: <c>PanelId = {idScope}/{ViewId}</c>, <c>PanelKind = ViewId</c>.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ScenarioComponentsViewDumpsItsTargetTests : IDisposable
{
    public ScenarioComponentsViewDumpsItsTargetTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static string Addr(string idScope) => $"{idScope}/{ScenarioComponentsViewDescriptor.ViewId}";

    private static readonly Entity One = new(31, 1);

    private static DetailsContext OneEntityContext(Entity e) =>
        DetailsContext.Empty("Editor") with { Entities = new[] { e } };

    [Fact]
    public void FirstDraw_DeclaresItInstrumented_AtTheComposedAddress()
    {
        var view = new ScenarioComponentsView(session: () => null, draw: (_, _) => { });
        var addr = Addr("host1");
        Assert.DoesNotContain(addr, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        view.SimulateDraw(OneEntityContext(One), "host1");

        Assert.Contains(addr, PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheTargetedEntity()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new ScenarioComponentsView(session: () => null, draw: (_, _) => { });

        view.SimulateDraw(OneEntityContext(One), "host1");

        var stored = PanelSnapshot.TryGet(Addr("host1"));
        Assert.NotNull(stored);
        Assert.Equal(ScenarioComponentsViewDescriptor.ViewId, stored!.PanelKind);
        var dump = stored.Dump();
        Assert.True(dump["hasTarget"]!.GetValue<bool>());
        Assert.Equal(One.Index, dump["entityIndex"]!.GetValue<int>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        var view = new ScenarioComponentsView(session: () => null, draw: (_, _) => { });   // CaptureEnabled stays false

        var vm = view.SimulateDraw(OneEntityContext(One), "host1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(Addr("host1"), PanelSnapshot.RegisteredPanels);
        Assert.True(vm.HasTarget);
    }

    [Fact]
    public void WithNoSingleEntity_TheDumpSaysSo()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new ScenarioComponentsView(session: () => null, draw: (_, _) => { });

        var vm = view.SimulateDraw(DetailsContext.Empty("Editor"), "host1");

        Assert.False(vm.HasTarget);
        Assert.Equal(-1, vm.EntityIndex);
    }
}
