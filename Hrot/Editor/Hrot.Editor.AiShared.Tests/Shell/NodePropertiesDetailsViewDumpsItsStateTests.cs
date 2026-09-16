using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 2) — <c>NodePropertiesDetailsView</c> converted to the <c>PanelSnapshot</c>
/// contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
///
/// <para>⛔⛔ <b>Ahead of every raw ImGui call</b> — the original body called <c>ImGui.TextDisabled</c> /
/// <c>DrawFacetArm</c> / <c>DrawDefaultValueArm</c> with NO context guard at all, so calling
/// <c>Draw</c> headless would have hit native ImGui with no context. This rail only ever calls
/// <c>SimulateDraw</c>.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class NodePropertiesDetailsViewDumpsItsStateTests : IDisposable
{
    private sealed class AlwaysFacet : IFacetDispatcher
    {
        public object? GetFacet(IAssetSubSelection selection) => "a-facet";
        public void ApplyFacet(IAssetSubSelection selection, object facet) { }
    }

    public NodePropertiesDetailsViewDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static DetailsContext OneNodeContext() =>
        DetailsContext.Empty("BTree") with
        {
            Selection = new IAssetSubSelection[] { new BTreeNodeSelection(Guid.NewGuid()) },
        };

    private static string Addr(string idScope) =>
        $"{idScope}/{NodePropertiesDetailsViewDescriptor.ViewId}";

    [Fact]
    public void FirstDraw_DeclaresItInstrumented_AtTheComposedAddress()
    {
        var source = new NodePropertiesSource();
        source.SetFacetDispatcher(new AlwaysFacet());
        var view = new NodePropertiesDetailsView(source);
        var addr = Addr("host1");
        Assert.DoesNotContain(addr, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        view.SimulateDraw(OneNodeContext(), "host1");

        Assert.Contains(addr, PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheFacetTypeName()
    {
        PanelSnapshot.CaptureEnabled = true;
        var source = new NodePropertiesSource();
        source.SetFacetDispatcher(new AlwaysFacet());
        var view = new NodePropertiesDetailsView(source);

        view.SimulateDraw(OneNodeContext(), "host1");

        var stored = PanelSnapshot.TryGet(Addr("host1"));
        Assert.NotNull(stored);
        Assert.Equal(Addr("host1"), stored!.PanelId);
        Assert.Equal(NodePropertiesDetailsViewDescriptor.ViewId, stored.PanelKind);

        var dump = stored.Dump();
        Assert.True(dump["hasFacet"]!.GetValue<bool>());
        Assert.Equal("String", dump["facetTypeName"]!.GetValue<string>());
        Assert.False(dump["hasEditService"]!.GetValue<bool>());
    }

    [Fact]
    public void WithNoDispatcherWired_HasFacetIsFalse()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new NodePropertiesDetailsView(new NodePropertiesSource());   // no dispatcher wired

        var vm = view.SimulateDraw(OneNodeContext(), "host1");

        Assert.False(vm.HasFacet);
        Assert.Null(vm.FacetTypeName);
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        var source = new NodePropertiesSource();
        source.SetFacetDispatcher(new AlwaysFacet());
        var view = new NodePropertiesDetailsView(source);   // CaptureEnabled stays false

        var vm = view.SimulateDraw(OneNodeContext(), "host1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(Addr("host1"), PanelSnapshot.RegisteredPanels);
        Assert.True(vm.HasFacet);
    }
}
