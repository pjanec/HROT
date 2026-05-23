using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeSubtreeResolverTests
{
    // ---- Fake catalog -------------------------------------------------------

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly List<IEditableAsset> _assets = new();
        public IReadOnlyList<IEditableAsset> All => _assets;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
        public IEditableAsset? FindByAssetId(Guid id) =>
            _assets.FirstOrDefault(a => a.AssetId == id);
        public IEditableAsset? FindByName(string name) =>
            _assets.FirstOrDefault(a => a.Name == name);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) =>
            System.Array.Empty<IEditableAsset>();
        public void Add(IEditableAsset asset) => _assets.Add(asset);
    }

    // ---- Helpers ------------------------------------------------------------

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(string name = "Host") =>
        new BehaviorTreeAsset(Guid.NewGuid(), name, $"/{name}.cs", true, "BB", "Ctx", EmptyBlob());

    private static BTreeEditorNode MakeSubtreeNode(string subtreeName)
    {
        return new BTreeEditorNode
        {
            VisualId    = Guid.NewGuid(),
            KernelType  = NodeType.Subtree,
            KernelBlobIndex = -1,
            Subtree     = new BTreeSubtreePayload { SubtreeName = subtreeName },
        };
    }

    private static BTreeEditorNode MakeActionNode()
    {
        return new BTreeEditorNode
        {
            VisualId    = Guid.NewGuid(),
            KernelType  = NodeType.Action,
            KernelBlobIndex = -1,
            Action      = new BTreeActionPayload { MethodFqn = "Ns.C.M" },
        };
    }

    // ---- Tests --------------------------------------------------------------

    [Fact]
    public void Resolve_known_subtree_name_sets_is_resolved_true()
    {
        var catalog = new FakeCatalog();
        var subAsset = MakeAsset("SubTree1");
        catalog.Add(subAsset);

        var hostAsset = MakeAsset("Host");
        var subtreeNode = MakeSubtreeNode("SubTree1");
        hostAsset.AddNode(subtreeNode);

        BTreeSubtreeResolver.Resolve(hostAsset, catalog);

        subtreeNode.Subtree!.IsResolved.Should().BeTrue();
        subtreeNode.Subtree.SubtreeAssetId.Should().Be(subAsset.AssetId);
    }

    [Fact]
    public void Resolve_unknown_subtree_name_sets_is_resolved_false()
    {
        var catalog   = new FakeCatalog();
        var hostAsset = MakeAsset("Host");
        var subtreeNode = MakeSubtreeNode("DoesNotExist");
        hostAsset.AddNode(subtreeNode);

        BTreeSubtreeResolver.Resolve(hostAsset, catalog);

        subtreeNode.Subtree!.IsResolved.Should().BeFalse();
        subtreeNode.Subtree.SubtreeAssetId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Resolve_non_subtree_nodes_are_unchanged()
    {
        var catalog   = new FakeCatalog();
        var hostAsset = MakeAsset("Host");
        var actionNode = MakeActionNode();
        hostAsset.AddNode(actionNode);

        BTreeSubtreeResolver.Resolve(hostAsset, catalog);

        // Action node must be unchanged; no subtree payload.
        actionNode.Subtree.Should().BeNull();
        actionNode.Action.Should().NotBeNull();
        actionNode.Action!.MethodFqn.Should().Be("Ns.C.M");
    }
}
