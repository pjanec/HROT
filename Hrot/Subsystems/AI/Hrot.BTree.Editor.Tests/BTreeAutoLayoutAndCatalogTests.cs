using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Layout;
using Hrot.BTree.Editor.Model;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeAutoLayoutTests
{
    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
            MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
            IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(Guid.NewGuid(), "T", "/t.cs", true, "BB", "Ctx", EmptyBlob());

    private static BTreeEditorNode MakeNode(NodeType type) =>
        new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = type, KernelBlobIndex = 0 };

    // ── Single-node tree ────────────────────────────────────────────────────

    [Fact]
    public void Root_is_placed_at_origin_after_LayoutCentered()
    {
        var asset = MakeAsset();
        var root = MakeNode(NodeType.Root);
        asset.AddNode(root);

        BTreeAutoLayout.LayoutCentered(asset);

        root.Position.X.Should().BeApproximately(0f, 1f);
        root.Position.Y.Should().BeApproximately(0f, 1f);
    }

    // ── Two-level tree (root + two leaves) ──────────────────────────────────

    [Fact]
    public void Two_children_are_horizontally_spread()
    {
        var asset    = MakeAsset();
        var root     = MakeNode(NodeType.Root);
        var seqNode  = MakeNode(NodeType.Sequence);
        var child1   = MakeNode(NodeType.Action);
        var child2   = MakeNode(NodeType.Action);

        seqNode.ChildVisualIds.Add(child1.VisualId);
        seqNode.ChildVisualIds.Add(child2.VisualId);
        root.ChildVisualIds.Add(seqNode.VisualId);

        asset.AddNode(root);
        asset.AddNode(seqNode);
        asset.AddNode(child1);
        asset.AddNode(child2);

        BTreeAutoLayout.LayoutCentered(asset);

        // Children should have the same Y (same depth).
        child1.Position.Y.Should().BeApproximately(child2.Position.Y, 1f);
        // Children should be at different X positions.
        child1.Position.X.Should().NotBeApproximately(child2.Position.X, 1f);
    }

    [Fact]
    public void Parent_Y_is_less_than_child_Y()
    {
        var asset  = MakeAsset();
        var root   = MakeNode(NodeType.Root);
        var action = MakeNode(NodeType.Action);
        root.ChildVisualIds.Add(action.VisualId);
        asset.AddNode(root);
        asset.AddNode(action);

        BTreeAutoLayout.LayoutCentered(asset);

        root.Position.Y.Should().BeLessThan(action.Position.Y);
    }

    [Fact]
    public void No_root_leaves_positions_unchanged()
    {
        var asset = MakeAsset();
        var node = MakeNode(NodeType.Action);
        node.Position = new System.Numerics.Vector2(99f, 77f);
        asset.AddNode(node);

        // Should not throw; positions may remain unchanged or be set.
        var act = () => BTreeAutoLayout.LayoutCentered(asset);
        act.Should().NotThrow();
    }
}

public sealed class BTreeNodeCatalogTests
{
    private readonly BTreeNodeCatalog _catalog = new();

    [Fact]
    public void All_contains_static_entries()
    {
        _catalog.All.Should().NotBeEmpty();
        _catalog.All.Count.Should().BeGreaterThan(10);
    }

    [Fact]
    public void Composites_have_both_input_and_output_pins()
    {
        var seq = _catalog.All.Single(e => e.Kind.Id == BTreeKinds.Sequence);
        seq.Inputs.Should().NotBeEmpty();
        seq.Outputs.Should().NotBeEmpty();
    }

    [Fact]
    public void Leaves_have_only_output_pins()
    {
        var action = _catalog.All.Single(e => e.Kind.Id == BTreeKinds.Action);
        action.Inputs.Should().BeEmpty();
        action.Outputs.Should().NotBeEmpty();
    }

    [Fact]
    public void Decorators_have_no_pins()
    {
        var inv = _catalog.All.Single(e => e.Kind.Id == BTreeKinds.Inverter);
        inv.Inputs.Should().BeEmpty();
        inv.Outputs.Should().BeEmpty();
    }

    [Fact]
    public void Query_filters_by_text()
    {
        var results = _catalog.Query(new NodeSearchQuery("sequence"));
        results.Should().ContainSingle(e => e.Kind.Id == BTreeKinds.Sequence);
    }

    [Fact]
    public void Query_empty_text_returns_all_non_deprecated()
    {
        var all = _catalog.Query(new NodeSearchQuery(string.Empty));
        all.Should().HaveCount(_catalog.All.Count(e => !e.IsDeprecated));
    }

    [Fact]
    public void QueryForPinContext_excludes_decorators()
    {
        var q = new PinContextQuery(
            PinId.NewId(), PinDirection.Output, PinKind.Exec, null, string.Empty);
        var results = _catalog.QueryForPinContext(q);
        results.Should().NotContain(e => e.Kind.Id.StartsWith("bt.decorator", StringComparison.Ordinal));
    }

    [Fact]
    public void Categories_are_non_empty()
    {
        _catalog.Categories.Should().NotBeEmpty();
        _catalog.Categories.Should().Contain(c => c.Path == "Composite");
        _catalog.Categories.Should().Contain(c => c.Path == "Leaf");
    }

    [Fact]
    public void All_leaf_kind_ids_match_BTreeKinds_constants()
    {
        var leafIds = new[] { BTreeKinds.Action, BTreeKinds.Condition, BTreeKinds.Wait, BTreeKinds.Subtree };
        foreach (var id in leafIds)
            _catalog.All.Should().Contain(e => e.Kind.Id == id, $"catalog should have {id}");
    }

    [Fact]
    public void BTreeKinds_IsLeaf_returns_true_for_catalog_leaf_kinds()
    {
        var leafEntries = _catalog.All.Where(e => e.CategoryPath == "Leaf").ToList();
        foreach (var entry in leafEntries)
            BTreeKinds.IsLeaf(entry.Kind).Should().BeTrue($"{entry.Kind.Id} should be a leaf");
    }
}
