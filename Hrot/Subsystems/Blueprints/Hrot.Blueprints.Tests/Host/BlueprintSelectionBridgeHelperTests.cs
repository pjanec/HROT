using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared.Selection;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Unit tests for <see cref="BlueprintSelectionBridgeHelper.MapSelection"/> (BF-UX1 FIX C).
/// Tests the pure static mapping method: (SelectionState, BlueprintAsset?) → BlueprintNodeSelection?
/// All tests are headless — no ImGui, no document manager, no GraphView required.
/// </summary>
public sealed class BlueprintSelectionBridgeHelperTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (BlueprintAsset asset, Graph graph, Guid nodeId)
        MakeAssetWithNode()
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "BridgeTestBP" };
        var graph = new Graph { Id = Guid.NewGuid(), Kind = GraphKind.Event };
        var node  = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Foo" };
        graph.Nodes.Add(node);
        asset.Graphs.Add(graph);
        return (asset, graph, node.Id);
    }

    // ── MapSelection null/empty guards ────────────────────────────────────────

    [Fact]
    public void MapSelection_NullAsset_ReturnsNull()
    {
        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(Guid.NewGuid())));

        var result = BlueprintSelectionBridgeHelper.MapSelection(sel, bpAsset: null);

        Assert.Null(result);
    }

    [Fact]
    public void MapSelection_EmptySelection_ReturnsNull()
    {
        var (asset, _, _) = MakeAssetWithNode();
        var sel = new SelectionState(); // empty

        var result = BlueprintSelectionBridgeHelper.MapSelection(sel, asset);

        Assert.Null(result);
    }

    /// <summary>
    /// ⚠⚠ <b><c>L0.2</c> SPLIT THIS RAIL IN TWO, and the split is the finding.</b>
    ///
    /// <para>🔴 It was <c>MapSelection_MultipleNodesSelected_ReturnsNull</c>, and it selected <b>one
    /// real node plus one <c>Guid.NewGuid()</c></b> — ⛔ so it conflated <i>"more than one thing is
    /// selected"</i> with <i>"one of the ids is stale"</i>. 📌 <c>R-118</c>: those were two of the three
    /// facts the old <c>null</c> flattened together, and the design separates them —
    /// <i>"an unresolvable node is DROPPED, not fatal."</i></para>
    ///
    /// <para>⇒ ⭐ <b>this half is the DROP rule:</b> a stale id is skipped, the real node survives, and
    /// the selection is still a single one. ⛔ Under the old code the stale id discarded the designer's
    /// real selection too.</para>
    /// </summary>
    [Fact]
    public void MapSelections_AStaleId_IsDropped_AndTheRealNodeSurvives()
    {
        var (asset, _, nodeId) = MakeAssetWithNode();
        var sel = new SelectionState();
        sel.ReplaceWith(new[]
        {
            SelectionEntry.OfNode(new NodeId(nodeId)),
            SelectionEntry.OfNode(new NodeId(Guid.NewGuid())),   // ⛔ in no graph of this asset
        });

        var all = BlueprintSelectionBridgeHelper.MapSelections(sel, asset);

        Assert.Single(all);
        Assert.Equal(nodeId, all[0].NodeId);

        // ⭐ …and one surviving node IS a single selection, so the derived single is that node.
        Assert.Equal(all[0], BlueprintSelectionBridgeHelper.MapSelection(sel, asset));
    }

    /// <summary>
    /// ⭐⭐ <b>…and this half is the MULTI-SELECT claim the old name promised but never tested</b> —
    /// TWO nodes that both resolve. ⛔ The old rail could not tell this case from the stale-id one.
    /// </summary>
    [Fact]
    public void MapSelections_TwoRealNodes_ReportsBoth_AndTheDerivedSingleIsNull()
    {
        var (asset, graph, nodeId) = MakeAssetWithNode();
        var second = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Bar" };
        graph.Nodes.Add(second);

        var sel = new SelectionState();
        sel.ReplaceWith(new[]
        {
            SelectionEntry.OfNode(new NodeId(nodeId)),
            SelectionEntry.OfNode(new NodeId(second.Id)),
        });

        var all = BlueprintSelectionBridgeHelper.MapSelections(sel, asset);

        Assert.Equal(2, all.Count);                     // ⭐ R-118: the bridge REPORTS both
        Assert.Null(BlueprintSelectionBridgeHelper.MapSelection(sel, asset));   // ⛔ not exactly one
    }

    [Fact]
    public void MapSelection_SingleLinkSelected_NotANode_ReturnsNull()
    {
        var (asset, _, _) = MakeAssetWithNode();
        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfLink(new LinkId(Guid.NewGuid())));

        var result = BlueprintSelectionBridgeHelper.MapSelection(sel, asset);

        Assert.Null(result);
    }

    // ── MapSelection happy path ────────────────────────────────────────────────

    /// <summary>
    /// BF-UX1 FIX C: single node selected → returns BlueprintNodeSelection with the
    /// ASSET graph.Id (not a deterministic canvas GraphId) and the asset node.Id.
    /// </summary>
    [Fact]
    public void MapSelection_SingleNodeSelected_ReturnsCorrectGraphAndNodeId()
    {
        var (asset, graph, nodeId) = MakeAssetWithNode();
        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(nodeId)));

        var result = BlueprintSelectionBridgeHelper.MapSelection(sel, asset);

        Assert.NotNull(result);
        // CRITICAL: GraphId must be the asset graph.Id, not a deterministic canvas GraphId.
        Assert.Equal(graph.Id, result!.GraphId);
        Assert.Equal(nodeId,   result.NodeId);
    }

    [Fact]
    public void MapSelection_NodeNotFoundInAsset_ReturnsNull()
    {
        var (asset, _, _) = MakeAssetWithNode();
        var sel = new SelectionState();
        // A node id that does not exist in any asset graph.
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(Guid.NewGuid())));

        var result = BlueprintSelectionBridgeHelper.MapSelection(sel, asset);

        Assert.Null(result);
    }

    /// <summary>
    /// BF-UX1 FIX C: node in a second graph resolves to THAT graph's id.
    /// </summary>
    [Fact]
    public void MapSelection_NodeInSecondGraph_ReturnsSecondGraphId()
    {
        var (asset, _, _) = MakeAssetWithNode(); // graph[0] + node[0]
        // Add a second graph with its own node.
        var graph2 = new Graph { Id = Guid.NewGuid(), Kind = GraphKind.Function };
        var node2  = new FunctionCallNode { Id = Guid.NewGuid(), MethodName = "Bar" };
        graph2.Nodes.Add(node2);
        asset.Graphs.Add(graph2);

        var sel = new SelectionState();
        sel.ReplaceWith(SelectionEntry.OfNode(new NodeId(node2.Id)));

        var result = BlueprintSelectionBridgeHelper.MapSelection(sel, asset);

        Assert.NotNull(result);
        Assert.Equal(graph2.Id, result!.GraphId);
        Assert.Equal(node2.Id,  result.NodeId);
    }
}
