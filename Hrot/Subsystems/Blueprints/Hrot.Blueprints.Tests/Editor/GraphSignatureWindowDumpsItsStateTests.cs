using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Windows;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 3) — <c>GraphSignatureWindow</c> and <c>GraphSignatureDetailsView</c>
/// converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class GraphSignatureWindowDumpsItsStateTests : System.IDisposable
{
    private static BlueprintAsset MakeAsset(params Graph[] graphs)
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestBP" };
        foreach (var g in graphs) asset.Graphs.Add(g);
        return asset;
    }

    private static Graph MakeFunctionGraph(string name = "Func1") => new()
    {
        Id      = Guid.NewGuid(),
        Name    = name,
        Kind    = GraphKind.Function,
        Inputs  = new List<ParameterDecl> { new() { Name = "a", Type = new BlueprintTypeRef { TypeId = "System.Int32" } } },
        Outputs = new List<ParameterDecl>(),
    };

    private static (GraphSignatureWindow window, EditorSelectionStore store) MakeWindow(string? id = null)
    {
        var store  = new EditorSelectionStore();
        var dirty  = new DirtyTracker();
        var window = new GraphSignatureWindow(store, dirty, idOverride: id);
        return (window, store);
    }

    public GraphSignatureWindowDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    // ══ GraphSignatureWindow ══════════════════════════════════════════════

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "graph_sig_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var (window, _) = MakeWindow(id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.NotNull(window);
    }

    [Fact]
    public void WithNoAssetSelected_TheDumpSaysSo()
    {
        const string id = "graph_sig_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var (window, _) = MakeWindow(id);

        window.SimulateDrawContent();

        var dump = PanelSnapshot.TryGet(id)!.Dump();
        Assert.False(dump["hasAsset"]!.GetValue<bool>());
        Assert.Equal(0, dump["graphCount"]!.GetValue<int>());
        Assert.Null(dump["selectedGraphName"]);
    }

    [Fact]
    public void AfterRetarget_TheDumpCarriesTheSelectedGraph()
    {
        const string id = "graph_sig_rail3";
        PanelSnapshot.CaptureEnabled = true;
        var (window, _) = MakeWindow(id);
        var graph = MakeFunctionGraph("Foo");
        var asset = MakeAsset(graph);
        window.Retarget(asset);

        var vm = window.SimulateDrawContent();

        Assert.Equal(id, vm.PanelId);
        Assert.Equal(GraphSignatureWindow.Kind, vm.PanelKind);
        Assert.True(vm.HasAsset);
        Assert.Equal(1, vm.GraphCount);
        Assert.Equal("Foo", vm.SelectedGraphName);

        var dump = PanelSnapshot.TryGet(id)!.Dump();
        Assert.Equal("Function", dump["selectedGraphKind"]!.GetValue<string>());
        Assert.Equal(1, dump["inputCount"]!.GetValue<int>());
        Assert.Equal(0, dump["outputCount"]!.GetValue<int>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        const string id = "graph_sig_rail4";
        var (window, _) = MakeWindow(id);   // CaptureEnabled stays false
        window.Retarget(MakeAsset(MakeFunctionGraph()));

        var vm = window.SimulateDrawContent();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.True(vm.HasAsset);
    }

    // ══ GraphSignatureDetailsView (hosted, thin wrapper) ═════════════════

    [Fact]
    public void DetailsView_FirstDraw_DeclaresItInstrumented_AtTheComposedAddress()
    {
        var (window, _) = MakeWindow("graph_sig_host");
        var view = new GraphSignatureDetailsView(window);
        var addr = $"host1/{GraphSignatureDetailsViewDescriptor.ViewId}";
        Assert.DoesNotContain(addr, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        view.SimulateDraw("host1");

        Assert.Contains(addr, PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void DetailsView_AfterABuild_TheDumpCarriesTheHostWindowId()
    {
        PanelSnapshot.CaptureEnabled = true;
        var (window, _) = MakeWindow("graph_sig_host");
        var view = new GraphSignatureDetailsView(window);

        var vm = view.SimulateDraw("host1");

        var addr = $"host1/{GraphSignatureDetailsViewDescriptor.ViewId}";
        Assert.Equal(addr, vm.PanelId);
        Assert.Equal(GraphSignatureDetailsViewDescriptor.ViewId, vm.PanelKind);

        var dump = PanelSnapshot.TryGet(addr)!.Dump();
        Assert.Equal("graph_sig_host", dump["hostWindowId"]!.GetValue<string>());
    }

    [Fact]
    public void DetailsView_WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var (window, _) = MakeWindow("graph_sig_host");
        var view = new GraphSignatureDetailsView(window);   // CaptureEnabled stays false

        var vm = view.SimulateDraw("host1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains($"host1/{GraphSignatureDetailsViewDescriptor.ViewId}", PanelSnapshot.RegisteredPanels);
        Assert.Equal("graph_sig_host", vm.HostWindowId);
    }
}
