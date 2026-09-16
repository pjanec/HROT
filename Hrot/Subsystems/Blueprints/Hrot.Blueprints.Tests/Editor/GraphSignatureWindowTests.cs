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

    // ── ResolveEditModels — Event graphs (BP-72) ─────────────────────────────

    /// <summary>
    /// BP-72 — this test previously asserted <c>Assert.Null</c>: Event graphs were filtered out, so
    /// a custom event's body graph (auto-created by BP-24) had its Inputs editable <b>nowhere</b>.
    /// They are now editable here, which is the whole point of the item.
    /// </summary>
    [Fact]
    public void ResolveEditModels_AssetHasOnlyEventGraphs_ResolvesTheEventGraph()
    {
        var (window, store) = MakeWindow();
        var evt   = MakeEventGraph();
        var asset = MakeAsset(evt);
        store.SelectAsset(asset);

        var result = window.ResolveEditModels();

        Assert.NotNull(result);
        result!.Value.Inputs.AddParameter("Damage", "System.Single");
        Assert.Single(evt.Inputs);
        Assert.Equal("Damage", evt.Inputs[0].Name);
    }

    [Fact]
    public void ResolveEditModels_AssetHasNoFunctionOrEventGraphs_ReturnsNull()
    {
        var (window, store) = MakeWindow();
        var asset = MakeAsset(new Graph
        {
            Id = Guid.NewGuid(), Name = "Ctor", Kind = GraphKind.Construction,
        });
        store.SelectAsset(asset);

        Assert.Null(window.ResolveEditModels());
    }

    // ── BP-72: the picker follows the canvas ─────────────────────────────────

    /// <summary>
    /// The defect: after a BP-24 graph switch the window kept editing <c>functionGraphs[0]</c>, so
    /// the designer changed the signature of a graph they were not looking at.
    /// </summary>
    [Fact]
    public void ResolveEditModels_FollowsTheCanvasGraph()
    {
        var (window, _) = MakeWindow();
        var funcA = MakeFunctionGraph("FuncA");
        var funcB = MakeFunctionGraph("FuncB");
        var asset = MakeAsset(funcA, funcB);

        var canvasGraphId = funcB.Id;
        window.Retarget(asset, () => canvasGraphId);

        var (inputs, _) = window.ResolveEditModels()!.Value;
        inputs.AddParameter("p", "System.Int32");

        Assert.Single(funcB.Inputs);   // the graph the canvas is showing
        Assert.Empty(funcA.Inputs);    // NOT graphs[0]
    }

    /// <summary>
    /// A switch mid-session must move the window too — the provider is polled, not sampled once.
    /// </summary>
    [Fact]
    public void ResolveEditModels_ReSnapsWhenTheCanvasGraphChanges()
    {
        var (window, _) = MakeWindow();
        var funcA = MakeFunctionGraph("FuncA");
        var funcB = MakeFunctionGraph("FuncB");
        var asset = MakeAsset(funcA, funcB);

        var canvasGraphId = funcA.Id;
        window.Retarget(asset, () => canvasGraphId);
        window.ResolveEditModels()!.Value.Inputs.AddParameter("a", "System.Int32");

        canvasGraphId = funcB.Id;                       // the designer switches graphs
        window.ResolveEditModels()!.Value.Inputs.AddParameter("b", "System.Single");

        Assert.Single(funcA.Inputs);
        Assert.Equal("a", funcA.Inputs[0].Name);
        Assert.Single(funcB.Inputs);
        Assert.Equal("b", funcB.Inputs[0].Name);
    }

    /// <summary>
    /// With no provider (headless callers, or before a document opens) behaviour is unchanged.
    /// </summary>
    [Fact]
    public void ResolveEditModels_NoCanvasProvider_FallsBackToFirstGraph()
    {
        var (window, _) = MakeWindow();
        var funcA = MakeFunctionGraph("FuncA");
        var funcB = MakeFunctionGraph("FuncB");
        var asset = MakeAsset(funcA, funcB);

        window.Retarget(asset);

        window.ResolveEditModels()!.Value.Inputs.AddParameter("p", "System.Int32");
        Assert.Single(funcA.Inputs);
    }

    // ── BP-72: Event-graph Inputs mirror the custom-event declaration ────────

    /// <summary>
    /// An Event graph's Inputs and its paired <see cref="CustomEventDecl.Parameters"/> must stay in
    /// lockstep: <c>Stage2.V_CustomEventHandlers</c> (BP1408) errors when the counts disagree, so an
    /// unmirrored parameter edit would turn authoring into a compile failure.
    /// </summary>
    [Fact]
    public void EventGraphInputEdit_MirrorsIntoTheCustomEventDeclaration()
    {
        var (window, _) = MakeWindow();
        var evt   = new Graph { Id = Guid.NewGuid(), Name = "OnPing", Kind = GraphKind.Event };
        var asset = MakeAsset(evt);
        asset.CustomEvents.Add(new CustomEventDecl
        {
            Id = Guid.NewGuid(), Name = "OnPing", Parameters = new List<ParameterDecl>(),
        });

        window.Retarget(asset, () => evt.Id);
        var (inputs, _) = window.ResolveEditModels()!.Value;

        inputs.AddParameter("Damage", "System.Single");

        var decl = asset.CustomEvents.Single();
        Assert.Single(decl.Parameters);                              // BP1408 counts now agree
        Assert.Equal("Damage",        decl.Parameters[0].Name);
        Assert.Equal("System.Single", decl.Parameters[0].Type.TypeId);
    }

    [Fact]
    public void EventGraphInputRemoval_MirrorsIntoTheCustomEventDeclaration()
    {
        var (window, _) = MakeWindow();
        var evt   = new Graph { Id = Guid.NewGuid(), Name = "OnPing", Kind = GraphKind.Event };
        var asset = MakeAsset(evt);
        asset.CustomEvents.Add(new CustomEventDecl
        {
            Id = Guid.NewGuid(), Name = "OnPing", Parameters = new List<ParameterDecl>(),
        });

        window.Retarget(asset, () => evt.Id);
        var (inputs, _) = window.ResolveEditModels()!.Value;
        inputs.AddParameter("A", "System.Int32");
        inputs.AddParameter("B", "System.Single");
        inputs.RemoveParameter("A");

        var decl = asset.CustomEvents.Single();
        Assert.Single(decl.Parameters);
        Assert.Equal("B", decl.Parameters[0].Name);
    }

    /// <summary>
    /// Renaming must not re-mint the surviving parameters' ids — the mirror matches by name and
    /// keeps existing ids, so anything keyed on them survives an unrelated edit.
    /// </summary>
    [Fact]
    public void EventGraphMirror_PreservesParameterIdsOfUnchangedNames()
    {
        var (window, _) = MakeWindow();
        var evt   = new Graph { Id = Guid.NewGuid(), Name = "OnPing", Kind = GraphKind.Event };
        var asset = MakeAsset(evt);
        asset.CustomEvents.Add(new CustomEventDecl
        {
            Id = Guid.NewGuid(), Name = "OnPing", Parameters = new List<ParameterDecl>(),
        });

        window.Retarget(asset, () => evt.Id);
        var (inputs, _) = window.ResolveEditModels()!.Value;
        inputs.AddParameter("Keep",  "System.Int32");
        inputs.AddParameter("Extra", "System.Int32");

        var keepId = asset.CustomEvents.Single().Parameters.Single(p => p.Name == "Keep").Id;

        inputs.RemoveParameter("Extra");

        Assert.Equal(keepId, asset.CustomEvents.Single().Parameters.Single(p => p.Name == "Keep").Id);
    }

    /// <summary>
    /// A Function graph has no declaration to mirror into, and an Event graph that is not a
    /// custom-event body (no name match) must not invent one.
    /// </summary>
    [Fact]
    public void Mirror_IsANoOp_ForFunctionGraphsAndUnpairedEventGraphs()
    {
        var (window, _) = MakeWindow();
        var func = MakeFunctionGraph("FuncA");
        var evt  = new Graph { Id = Guid.NewGuid(), Name = "Unpaired", Kind = GraphKind.Event };
        var asset = MakeAsset(func, evt);
        asset.CustomEvents.Add(new CustomEventDecl
        {
            Id = Guid.NewGuid(), Name = "SomethingElse", Parameters = new List<ParameterDecl>(),
        });

        var canvasId = func.Id;
        window.Retarget(asset, () => canvasId);
        window.ResolveEditModels()!.Value.Inputs.AddParameter("p", "System.Int32");

        canvasId = evt.Id;
        window.ResolveEditModels()!.Value.Inputs.AddParameter("q", "System.Int32");

        Assert.Empty(asset.CustomEvents.Single().Parameters);   // untouched
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
