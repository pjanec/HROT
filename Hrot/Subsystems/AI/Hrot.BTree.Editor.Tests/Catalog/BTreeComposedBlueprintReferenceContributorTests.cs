using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.References;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Catalog;

/// <summary>
/// Phase C (AIE-053): tests for <see cref="BTreeComposedBlueprintReferenceContributor"/> — the
/// BTree-side half of the BTree→Blueprint cross-asset reference. Mirrors the style of
/// <see cref="ReferenceCatalogCrossAssetTests"/>.
/// <para>
/// These tests use plain-string composed FQNs (no <c>BlueprintIdHash</c>/<c>Sanitizer</c> — the
/// hash correctness is a blueprint-side concern verified in the blueprint-editor-coupled test
/// project). A composed node FQN is just
/// <c>{GeneratedNamespace}.{GeneratedClassName}.TickCore</c>.
/// </para>
/// </summary>
public sealed class BTreeComposedBlueprintReferenceContributorTests
{
    private const string ClassName = "ParamDemo_CEFE162F_Bp";
    private static string ComposedFqn(string className = ClassName, string method = "TickCore") =>
        $"{ComposedBlueprintResolver.GeneratedNamespace}.{className}.{method}";

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

    // ---- Tests --------------------------------------------------------

    [Fact]
    public void EnumerateReferences_composed_action_node_produces_ActionFqn_reference()
    {
        var asset  = MakeAsset("AssetA");
        var nodeId = Guid.NewGuid();
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = nodeId,
                KernelType   = NodeType.Action,
                DisplayLabel = "ComposedAction",
                Action       = new BTreeActionPayload
                {
                    MethodFqn     = ComposedFqn(),
                    DelegateShape = BTreeActionDelegateShape.AiPrimitiveTickCore,
                },
            },
        });

        var contributor = new BTreeComposedBlueprintReferenceContributor();
        var refs = contributor.EnumerateReferences(asset);

        refs.Should().HaveCount(1);
        refs[0].HostAssetId.Should().Be(asset.AssetId);
        refs[0].HostElementId.Should().Be(nodeId);
        refs[0].HostKind.Should().Be(AssetKind.BTree);
        refs[0].TargetKind.Should().Be(SubElementKind.ActionFqn);
        refs[0].TargetKey.Should().Be(ComposedBlueprintResolver.ElementKey(ClassName));
    }

    [Fact]
    public void EnumerateReferences_composed_condition_node_produces_ConditionFqn_reference()
    {
        const string condClassName = "GuardDemo_AABBCCDD_Bp";
        var asset  = MakeAsset("AssetB");
        var nodeId = Guid.NewGuid();
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = nodeId,
                KernelType   = NodeType.Condition,
                DisplayLabel = "ComposedCondition",
                Condition    = new BTreeConditionPayload
                {
                    MethodFqn     = ComposedFqn(condClassName),
                    DelegateShape = BTreeActionDelegateShape.AiPrimitiveTickCore,
                },
            },
        });

        var contributor = new BTreeComposedBlueprintReferenceContributor();
        var refs = contributor.EnumerateReferences(asset);

        refs.Should().HaveCount(1);
        refs[0].TargetKind.Should().Be(SubElementKind.ConditionFqn);
        refs[0].TargetKey.Should().Be(ComposedBlueprintResolver.ElementKey(condClassName));
    }

    [Fact]
    public void EnumerateReferences_ignores_non_composed_action_node()
    {
        var asset  = MakeAsset("AssetC");
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = Guid.NewGuid(),
                KernelType   = NodeType.Action,
                DisplayLabel = "HandWrittenAction",
                Action       = new BTreeActionPayload
                {
                    MethodFqn     = "Hrot.Game.Combat.CombatActions.AimAndFire",
                    DelegateShape = BTreeActionDelegateShape.ThreeParamReusable,
                },
            },
        });

        var contributor = new BTreeComposedBlueprintReferenceContributor();
        contributor.EnumerateReferences(asset).Should().BeEmpty();
    }

    [Fact]
    public void EnumerateReferences_ignores_composites_and_other_node_kinds()
    {
        var asset = MakeAsset("AssetD");
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Root },
            new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Sequence },
            new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Wait, Wait = new BTreeWaitPayload { Duration = 1f } },
        });

        var contributor = new BTreeComposedBlueprintReferenceContributor();
        contributor.EnumerateReferences(asset).Should().BeEmpty();
    }

    [Fact]
    public void EnumerateElements_never_contributes_elements_of_its_own()
    {
        var asset = MakeAsset("AssetE");
        var contributor = new BTreeComposedBlueprintReferenceContributor();
        contributor.EnumerateElements(asset).Should().BeEmpty();
    }

    [Fact]
    public void EnumerateReferences_non_BehaviorTreeAsset_returns_empty()
    {
        var contributor = new BTreeComposedBlueprintReferenceContributor();
        contributor.EnumerateReferences(new NotABehaviorTreeAsset()).Should().BeEmpty();
    }

    // ---- Cross-asset round trip through ReferenceCatalog (mirrors ReferenceCatalogCrossAssetTests) ----

    [Fact]
    public void ReferenceCatalog_FindReferences_finds_the_composed_BTree_node_by_blueprint_key()
    {
        var blueprintAssetId = Guid.NewGuid();
        var blueprintKey = ComposedBlueprintResolver.ElementKey(ClassName);

        var asset  = MakeAsset("AssetF");
        var nodeId = Guid.NewGuid();
        AddNodes(asset, new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = nodeId,
                KernelType   = NodeType.Action,
                DisplayLabel = "ComposedAction",
                Action       = new BTreeActionPayload
                {
                    MethodFqn     = ComposedFqn(),
                    DelegateShape = BTreeActionDelegateShape.AiPrimitiveTickCore,
                },
            },
        });

        var contributor = new BTreeComposedBlueprintReferenceContributor();
        var refs = contributor.EnumerateReferences(asset);

        var refCatalog = new ReferenceCatalog();
        // Contribute a stand-in blueprint element (production element is exposed by
        // BlueprintReferenceContributor in Hrot.Blueprints.Editor — verified separately) using the
        // same key format, to prove FindReferences() resolves purely by key.
        refCatalog.Contribute(new FakeBlueprintElement(blueprintKey, blueprintAssetId), refs);

        var found = refCatalog.FindReferences(blueprintKey);
        found.Should().HaveCount(1);
        found[0].HostAssetId.Should().Be(asset.AssetId);
        found[0].HostElementId.Should().Be(nodeId);
    }

    private sealed class NotABehaviorTreeAsset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name { get; } = "NotABTree";
        public AssetKind Kind => AssetKind.Blueprint;
        public string SourceFilePath { get; } = string.Empty;
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class FakeBlueprintElement : IAssetSubElement
    {
        public FakeBlueprintElement(string key, Guid sourceAssetId)
        {
            Key = key;
            SourceAssetId = sourceAssetId;
        }
        public string Key { get; }
        public SubElementKind Kind => SubElementKind.ActionFqn;
        public string DisplayName => Key;
        public Guid? SourceAssetId { get; }
    }
}
