using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Catalog;

/// <summary>
/// BATCH-14 / AIE-051: verifies that the <see cref="ReferenceCatalog"/> correctly exposes
/// cross-asset references so that <see cref="Hrot.Editor.AiShared.Refactor.RefactorService"/>
/// and <see cref="Hrot.Editor.AiShared.Windows.FindResultsWindow"/> can resolve references
/// between assets.
///
/// Tests use a mix of:
/// (a) Direct <see cref="ReferenceCatalog.Contribute"/> calls — for explicit cross-asset
///     reference wiring (not tied to a single contributor's format).
/// (b) <see cref="BTreeBlackboardVariableContributor"/> — to verify the real production
///     contributor populates the catalog correctly and that intra-asset variable references
///     round-trip through the multi-index.
/// </summary>
public sealed class ReferenceCatalogCrossAssetTests
{
    // ---- helpers -------------------------------------------------------

    private static BehaviorTreeBlob EmptyBlob() => new BehaviorTreeBlob
    {
        TreeName        = "test",
        Nodes           = Array.Empty<NodeDefinition>(),
        MethodNames     = Array.Empty<string>(),
        FloatParams     = Array.Empty<float>(),
        IntParams       = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };

    private static BehaviorTreeAsset MakeAsset(string name = "Tree") =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), name, $"/trees/{name}.cs",
            /*isBlackboardEditorManaged*/ true,
            "Blackboard", "Context",
            EmptyBlob());

    private static void AddNodes(BehaviorTreeAsset asset, List<BTreeEditorNode> nodes) =>
        asset.ReplaceAll(nodes, new List<BTreeEditorPill>(), EmptyBlob());

    // Minimal IAssetCatalog that holds a list of assets and fires Changed.
    private sealed class TestCatalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _assets;
        public TestCatalog(params IEditableAsset[] assets) => _assets = new List<IEditableAsset>(assets);
        public IReadOnlyList<IEditableAsset> All => _assets;
        public IEditableAsset? FindByAssetId(Guid id) => _assets.FirstOrDefault(a => a.AssetId == id);
        public IEditableAsset? FindByName(string name) => _assets.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
        public event Action<AssetKind>? Changed;
        public void FireChanged(AssetKind kind = AssetKind.Blueprint) => Changed?.Invoke(kind);
    }

    // ---- AIE-051 test: cross-asset via Contribute -----------------------

    /// <summary>
    /// AIE-051: A reference authored in one asset (assetB) that targets a sub-element
    /// declared in another asset (assetA) is discoverable via FindReferences.
    /// Uses <see cref="ReferenceCatalog.Contribute"/> to wire up the reference directly,
    /// asserting both the host and target ids are correct.
    /// </summary>
    [Fact]
    public void ReferenceCatalog_FindReferences_AcrossAssets()
    {
        // Arrange: assetA declares a blackboard variable sub-element.
        var assetAId  = Guid.NewGuid();
        var elementKey = $"{assetAId:D}::speed";

        var element = new FakeElement
        {
            Key           = elementKey,
            Kind          = SubElementKind.BlackboardVariable,
            DisplayName   = "speed",
            SourceAssetId = assetAId,
        };

        // assetB has a node that references assetA's variable.
        var assetBId  = Guid.NewGuid();
        var nodeId    = Guid.NewGuid();
        var reference = new AssetReference(
            HostAssetId:     assetBId,
            HostKind:        AssetKind.BTree,
            HostElementId:   nodeId,
            HostDisplayPath: "UseSpeedNode",
            TargetKey:       elementKey,
            TargetKind:      SubElementKind.BlackboardVariable);

        var refCatalog = new ReferenceCatalog();

        // Act: manually contribute both the element and the reference.
        refCatalog.Contribute(element, new[] { reference });

        // Assert — element for the variable exists (host = assetA).
        var found = refCatalog.FindElement(elementKey);
        found.Should().NotBeNull("the variable sub-element should be registered");
        found!.SourceAssetId.Should().Be(assetAId, "sub-element belongs to assetA");

        // Assert — reference from assetB pointing to assetA's variable.
        var refs = refCatalog.FindReferences(elementKey);
        refs.Should().HaveCount(1, "exactly one node in assetB references the variable");
        refs[0].HostAssetId.Should().Be(assetBId, "reference is authored in assetB");
        refs[0].TargetKey.Should().Be(elementKey, "target key identifies the variable in assetA");
        refs[0].HostElementId.Should().Be(nodeId, "reference element id matches the action node");
    }

    [Fact]
    public void ReferenceCatalog_FindReferences_AcrossAssets_MultipleRefs_AllFound()
    {
        // Two assets (B and C) both reference a variable declared in asset A.
        var assetAId  = Guid.NewGuid();
        var elementKey = $"{assetAId:D}::hp";

        var element = new FakeElement { Key = elementKey, Kind = SubElementKind.BlackboardVariable, SourceAssetId = assetAId };

        var assetBId = Guid.NewGuid();
        var assetCId = Guid.NewGuid();
        var nodeB    = Guid.NewGuid();
        var nodeC    = Guid.NewGuid();

        var refFromB = new AssetReference(assetBId, AssetKind.BTree, nodeB, "NodeB", elementKey, SubElementKind.BlackboardVariable);
        var refFromC = new AssetReference(assetCId, AssetKind.Hsm,  nodeC, "NodeC", elementKey, SubElementKind.BlackboardVariable);

        var refCatalog = new ReferenceCatalog();
        refCatalog.Contribute(element, new[] { refFromB, refFromC });

        var refs = refCatalog.FindReferences(elementKey);

        refs.Should().HaveCount(2, "both assetB and assetC reference the variable");
        refs.Select(r => r.HostAssetId).Should().BeEquivalentTo(new[] { assetBId, assetCId });
        refs.Select(r => r.HostElementId).Should().BeEquivalentTo(new[] { nodeB, nodeC });
    }

    // ---- Production contributor test: intra-asset BTree references -----

    /// <summary>
    /// Verifies that <see cref="BTreeBlackboardVariableContributor"/> correctly produces
    /// elements and references for intra-asset variable bindings, and that these are
    /// discoverable via <see cref="ReferenceCatalog.FindReferences"/> when contributed directly.
    /// </summary>
    [Fact]
    public void BTreeBlackboardVariableContributor_PopulatesCatalog_IntraAssetRef()
    {
        // Arrange: assetA declares "speed" and has an action node with ExpressionTargetField = "speed".
        var assetA  = MakeAsset("AssetA");
        assetA.IsBlackboardEditorManaged = true;
        assetA.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("speed", typeof(float), null),
        });

        var nodeId = Guid.NewGuid();
        AddNodes(assetA, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = nodeId,
                KernelType   = NodeType.Action,
                DisplayLabel = "UseSpeed",
                Action       = new BTreeActionPayload { MethodFqn = "AI.Actions.UseSpeed", ExpressionTargetField = "speed" },
            },
        });

        var contributor = new BTreeBlackboardVariableContributor();

        // Verify the contributor produces the expected elements and references.
        var elements = contributor.EnumerateElements(assetA);
        var refs     = contributor.EnumerateReferences(assetA);

        var targetKey = $"{assetA.AssetId:D}::speed";

        elements.Should().HaveCount(1, "one variable declared");
        elements[0].Key.Should().Be(targetKey, "element key uses assetId::variableName format");
        elements[0].SourceAssetId.Should().Be(assetA.AssetId);

        refs.Should().HaveCount(1, "one action node with ExpressionTargetField");
        refs[0].HostAssetId.Should().Be(assetA.AssetId);
        refs[0].HostElementId.Should().Be(nodeId);
        refs[0].TargetKey.Should().Be(targetKey, "reference target matches the variable element key");

        // Now verify these round-trip through the ReferenceCatalog via Contribute.
        var refCatalog = new ReferenceCatalog();
        foreach (var el in elements)
            refCatalog.Contribute(el, refs);

        var foundElement = refCatalog.FindElement(targetKey);
        foundElement.Should().NotBeNull("element registered via Contribute");
        foundElement!.SourceAssetId.Should().Be(assetA.AssetId);

        var foundRefs = refCatalog.FindReferences(targetKey);
        foundRefs.Should().HaveCount(1, "reference found via FindReferences");
        foundRefs[0].HostElementId.Should().Be(nodeId);
    }

    // ---- Helpers -------------------------------------------------------

    private sealed class FakeElement : IAssetSubElement
    {
        public string      Key           { get; init; } = string.Empty;
        public SubElementKind Kind       { get; init; } = SubElementKind.BlackboardVariable;
        public string      DisplayName   { get; init; } = string.Empty;
        public Guid?       SourceAssetId { get; init; }
    }
}
