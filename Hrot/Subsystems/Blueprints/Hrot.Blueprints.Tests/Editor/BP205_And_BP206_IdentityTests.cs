using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Editor.Windows;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// <b>BP-205</b> — the Details panel's per-node ImGui id scope, and <b>BP-206</b> — resolving the GUIDs a
/// diagnostic carries into names. Two different surfaces, one complaint: <i>the editor tells you
/// something is wrong without telling you where.</i>
/// </summary>
public sealed class BP205_And_BP206_IdentityTests
{
    // ── BP-205 ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The rule the fix depends on.</b> Two nodes must never share an id scope — that shared
    /// identity is what let ImGui hand a <c>Format String</c> the <c>Print String</c>'s live input
    /// buffer, because both drawers label the field <c>"Format"</c>.
    /// </summary>
    [Fact]
    public void TwoNodes_GetDifferentIdScopes()
    {
        var a = new PrintStringNode  { Id = Guid.NewGuid() };
        var b = new FormatStringNode { Id = Guid.NewGuid() };

        Assert.NotEqual(DetailsIdScope.For(a), DetailsIdScope.For(b));
    }

    /// <summary>
    /// ⚠ <b>Same KIND, different nodes, still different scopes.</b> Selecting one Print String after
    /// another was the same defect and the same surprise; keying on the node kind would have fixed the
    /// reported pair and left this one broken.
    /// </summary>
    [Fact]
    public void TwoNodesOfTheSameKind_GetDifferentIdScopes()
    {
        var a = new PrintStringNode { Id = Guid.NewGuid() };
        var b = new PrintStringNode { Id = Guid.NewGuid() };

        Assert.NotEqual(DetailsIdScope.For(a), DetailsIdScope.For(b));
    }

    /// <summary>
    /// ⚠ <b>And stable across frames.</b> An id that changed between frames would be worse than a
    /// shared one: ImGui would treat every frame as a new widget, so the field could never hold focus
    /// and typing would be impossible.
    /// </summary>
    [Fact]
    public void TheSameNode_GetsTheSameIdScopeEveryTime()
    {
        var node = new PrintStringNode { Id = Guid.NewGuid() };

        Assert.Equal(DetailsIdScope.For(node), DetailsIdScope.For(node));
    }

    // ── BP-206 ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The complaint, answered.</b> <c>BP1602</c> named two GUIDs and nothing else; with forty
    /// blueprints in the repo the user's response was *"I don't know what blueprint it was"*.
    /// </summary>
    [Fact]
    public void ADiagnosticNamingANode_ResolvesToAssetGraphAndNodeNames()
    {
        var (asset, graph, node) = AssetWithOnePrintString();

        var diag = Diagnostic.Error("BP1602", "Link references unknown ToPinId 2f2db7d9.",
            asset.AssetId, graph.Id, node.Id);

        Assert.Equal("SmokePatrol ▸ Tick ▸ Print String", DiagnosticIdentity.Describe(diag, asset));
    }

    /// <summary>The author's own header text wins over the generated kind name (BP-17).</summary>
    [Fact]
    public void ANodeWithACustomTitle_IsNamedByIt()
    {
        var (asset, graph, node) = AssetWithOnePrintString();
        node.EditorMetadata.CustomTitle = "log the threat count";

        var diag = Diagnostic.Warning("BP3010", "Orphan node was eliminated.",
            asset.AssetId, graph.Id, node.Id);

        Assert.Equal("SmokePatrol ▸ Tick ▸ log the threat count",
            DiagnosticIdentity.Describe(diag, asset));
    }

    /// <summary>
    /// ⚠ <b>A node id with no graph id still resolves.</b> Not every validator populates all four ids,
    /// and the node name is the most useful of the three — dropping it because a sibling id was absent
    /// would defeat the fix on exactly the diagnostics that are hardest to place.
    /// </summary>
    [Fact]
    public void ANodeIdWithoutAGraphId_StillResolvesTheNodeName()
    {
        var (asset, _, node) = AssetWithOnePrintString();

        var diag = Diagnostic.Warning("BP4001", "Unconnected required data input pin.",
            asset.AssetId, graphId: null, nodeId: node.Id);

        Assert.Equal("SmokePatrol ▸ Print String", DiagnosticIdentity.Describe(diag, asset));
    }

    /// <summary>
    /// ⚠ <b>A diagnostic from a DIFFERENT asset is not labelled with this one's name.</b> Sibling
    /// assets compile together, and a confidently wrong location is worse than none — it sends the
    /// designer to the wrong file.
    /// </summary>
    [Fact]
    public void ADiagnosticFromAnotherAsset_IsNotAttributedToThisOne()
    {
        var (asset, _, _) = AssetWithOnePrintString();

        var diag = Diagnostic.Error("BP1300", "Peer is not in CallablePeers list.",
            assetId: Guid.NewGuid());

        Assert.Equal("", DiagnosticIdentity.Describe(diag, asset));
    }

    /// <summary>A diagnostic with no asset to resolve against gets no origin, rather than a fabricated one.</summary>
    [Fact]
    public void ADiagnosticWithNoAsset_GetsNoOrigin()
    {
        var diag = Diagnostic.Error("BP0002", "Blueprint file could not be parsed.");

        Assert.Equal("", DiagnosticIdentity.Describe(diag, asset: null));
        Assert.Null(DiagnosticIdentity.Attribute(diag, asset: null).Origin);
    }

    /// <summary>
    /// ⚠ <c>Attribute</c> writes <see cref="Diagnostic.Origin"/> and leaves
    /// <see cref="Diagnostic.Message"/> byte-identical — a large number of tests assert exact message
    /// text, and rewriting messages would redden them all for no behavioural reason.
    /// </summary>
    [Fact]
    public void Attribute_FillsOrigin_AndLeavesTheMessageUntouched()
    {
        var (asset, graph, node) = AssetWithOnePrintString();
        var diag = Diagnostic.Warning("BP3010", "Orphan node was eliminated.",
            asset.AssetId, graph.Id, node.Id);

        var attributed = DiagnosticIdentity.Attribute(diag, asset);

        Assert.Equal("Orphan node was eliminated.", attributed.Message);
        Assert.Equal("SmokePatrol ▸ Tick ▸ Print String", attributed.Origin);
    }

    /// <summary>The class name is never shown raw — the palette calls it <i>Print String</i>.</summary>
    [Theory]
    [InlineData(typeof(PrintStringNode),  "Print String")]
    [InlineData(typeof(FormatStringNode), "Format String")]
    [InlineData(typeof(ReturnNode),       "Return")]
    [InlineData(typeof(EventEntryNode),   "Event Entry")]
    public void NodeDisplayName_SpacesOutTheKind(Type nodeType, string expected)
    {
        var node = (Node)Activator.CreateInstance(nodeType)!;
        node.Id = Guid.NewGuid();

        Assert.Equal(expected, DiagnosticIdentity.NodeDisplayName(node));
    }

    // ── fixture ───────────────────────────────────────────────────────────────────────────────────

    private static (BlueprintAsset Asset, Graph Graph, Node Node) AssetWithOnePrintString()
    {
        var node  = new PrintStringNode { Id = Guid.NewGuid(), Format = "{Threat}" };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function };
        graph.Nodes.Add(node);

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "SmokePatrol",
            Dispatch = BlueprintDispatchKind.Instance,
        };
        asset.Graphs.Add(graph);

        return (asset, graph, node);
    }
}
