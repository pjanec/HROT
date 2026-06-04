using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Blueprints.Editor.Windows;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BATCH-03D2 — headless tests for <see cref="GraphSignatureWindow"/>.
///
/// All tests run without ImGui; they exercise the window's headless seam
/// (<see cref="GraphSignatureWindow.ResolveEditModels"/>), construction, and
/// rebinding to different graphs.
/// </summary>
public sealed class GraphSignatureWindowTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static BlueprintAsset MakeAsset(params Graph[] graphs)
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestBP" };
        foreach (var g in graphs)
            asset.Graphs.Add(g);
        return asset;
    }

    private static Graph MakeFunctionGraph(string name = "Func1") => new()
    {
        Id   = Guid.NewGuid(),
        Name = name,
        Kind = GraphKind.Function,
    };

    private static Graph MakeEventGraph() => new()
    {
        Id   = Guid.NewGuid(),
        Name = "EventGraph",
        Kind = GraphKind.Event,
    };

    private static (GraphSignatureWindow window, EditorSelectionStore store) MakeWindow()
    {
        var store  = new EditorSelectionStore();
        var dirty  = new DirtyTracker();
        var window = new GraphSignatureWindow(store, dirty);
        return (window, store);
    }

    // ── Construction without ImGui ───────────────────────────────────────────

    [Fact]
    public void Window_ConstructsWithoutImGui()
    {
        var (window, _) = MakeWindow();
        Assert.NotNull(window);
    }

    [Fact]
    public void Window_HasExpected_IdAndPerspective()
    {
        var (window, _) = MakeWindow();
        Assert.Equal("ai_graph_signature_blueprint", window.Id);
        Assert.Equal("Blueprint", window.OwningPerspective);
    }

    // ── ResolveEditModels — no asset ─────────────────────────────────────────

    [Fact]
    public void ResolveEditModels_NoAsset_ReturnsNull()
    {
        var (window, _) = MakeWindow();

        var result = window.ResolveEditModels();

        Assert.Null(result);
    }

    // ── ResolveEditModels — asset with Function graph ─────────────────────────

    [Fact]
    public void ResolveEditModels_WithFunctionGraph_ReturnsNonNullPair()
    {
        var (window, store) = MakeWindow();
        var graph  = MakeFunctionGraph("MyFunc");
        var asset  = MakeAsset(graph);
        store.SelectAsset(asset);

        var result = window.ResolveEditModels();

        Assert.NotNull(result);
    }

    [Fact]
    public void ResolveEditModels_InputsModel_EditsBoundToGraphInputs()
    {
        var (window, store) = MakeWindow();
        var graph = MakeFunctionGraph("Compute");
        var asset = MakeAsset(graph);
        store.SelectAsset(asset);

        var (inputsModel, _) = window.ResolveEditModels()!.Value;
        inputsModel.AddParameter("x", "System.Int32");

        Assert.Single(graph.Inputs);
        Assert.Equal("x", graph.Inputs[0].Name);
    }

    [Fact]
    public void ResolveEditModels_OutputsModel_EditsBoundToGraphOutputs()
    {
        var (window, store) = MakeWindow();
        var graph = MakeFunctionGraph("Compute");
        var asset = MakeAsset(graph);
        store.SelectAsset(asset);

        var (_, outputsModel) = window.ResolveEditModels()!.Value;
        outputsModel.AddParameter("result", "System.Single");

        Assert.Single(graph.Outputs);
        Assert.Equal("result", graph.Outputs[0].Name);
    }

    // ── ResolveEditModels — no Function graphs ───────────────────────────────

    [Fact]
    public void ResolveEditModels_AssetHasOnlyEventGraphs_ReturnsNull()
    {
        var (window, store) = MakeWindow();
        var asset = MakeAsset(MakeEventGraph());
        store.SelectAsset(asset);

        var result = window.ResolveEditModels();

        Assert.Null(result);
    }

    // ── Retarget ─────────────────────────────────────────────────────────────

    [Fact]
    public void Retarget_NewAsset_ChangesResolutionToNewAsset()
    {
        var (window, store) = MakeWindow();

        var graph1 = MakeFunctionGraph("FuncA");
        var asset1 = MakeAsset(graph1);
        window.Retarget(asset1);

        var (inputsA, _) = window.ResolveEditModels()!.Value;
        inputsA.AddParameter("p", "System.Int32");
        Assert.Single(graph1.Inputs);

        // Retarget to a different asset.
        var graph2 = MakeFunctionGraph("FuncB");
        var asset2 = MakeAsset(graph2);
        window.Retarget(asset2);

        var (inputsB, _) = window.ResolveEditModels()!.Value;
        inputsB.AddParameter("q", "System.Single");

        // asset1 was not touched.
        Assert.Single(graph1.Inputs);   // still only "p"
        Assert.Single(graph2.Inputs);   // "q" added to asset2
        Assert.Equal("q", graph2.Inputs[0].Name);
    }

    [Fact]
    public void Retarget_Null_ClearsResolution()
    {
        var (window, store) = MakeWindow();
        var graph = MakeFunctionGraph();
        var asset = MakeAsset(graph);
        window.Retarget(asset);

        window.Retarget(null);

        var result = window.ResolveEditModels();
        Assert.Null(result);
    }

    // ── Dirty tracking ───────────────────────────────────────────────────────

    [Fact]
    public void ResolveEditModels_InputsMutation_MarksDirtyViaTracker()
    {
        var store  = new EditorSelectionStore();
        var dirty  = new DirtyTracker();
        var window = new GraphSignatureWindow(store, dirty);

        var graph  = MakeFunctionGraph();
        var asset  = MakeAsset(graph);
        var assetId = asset.AssetId;
        store.SelectAsset(asset);

        var (inputsModel, _) = window.ResolveEditModels()!.Value;
        inputsModel.AddParameter("param", "System.Int32");

        Assert.True(dirty.IsDirty(assetId), "Asset should be marked dirty after AddParameter.");
    }
}
