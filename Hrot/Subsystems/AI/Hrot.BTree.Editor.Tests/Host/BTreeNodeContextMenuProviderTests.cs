using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

/// <summary>
/// Tests for <see cref="BTreeNodeContextMenuProvider"/> (DEC-03b).
/// </summary>
public sealed class BTreeNodeContextMenuProviderTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "test",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), "TestTree", "/TestTree.cs", true,
            "BB", "Ctx", EmptyBlob());

    private static (BehaviorTreeAsset asset, BTreeGraphModel model, BTreeCommandSink sink, BTreeNodeContextMenuProvider provider) Build()
    {
        var asset    = MakeAsset();
        var model    = new BTreeGraphModel(asset);
        var sink     = new BTreeCommandSink(asset, model);
        var provider = new BTreeNodeContextMenuProvider(sink, model);
        return (asset, model, sink, provider);
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetItemsFor_returns_one_parent_item()
    {
        var (_, _, sink, provider) = Build();
        var nodeId = NodeId.NewId();
        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));

        var items = provider.GetItemsFor(nodeId, new[] { nodeId });

        items.Should().HaveCount(1);
        items[0].Label.Should().Be("Add Decorator");
    }

    [Fact]
    public void GetItemsFor_parent_has_seven_children()
    {
        var (_, _, sink, provider) = Build();
        var nodeId = NodeId.NewId();
        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));

        var items = provider.GetItemsFor(nodeId, new[] { nodeId });

        items[0].Children.Should().NotBeNull();
        items[0].Children!.Should().HaveCount(7);
    }

    [Fact]
    public void GetItemsFor_children_include_all_decorator_types()
    {
        var (_, _, sink, provider) = Build();
        var nodeId = NodeId.NewId();
        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));

        var items    = provider.GetItemsFor(nodeId, new[] { nodeId });
        var children = items[0].Children!;
        var labels   = new HashSet<string>(children.Select(c => c.Label));

        labels.Should().Contain("Inverter");
        labels.Should().Contain("Repeater");
        labels.Should().Contain("Cooldown");
        labels.Should().Contain("Force Success");
        labels.Should().Contain("Force Failure");
        labels.Should().Contain("Until Success");
        labels.Should().Contain("Until Failure");
    }

    [Fact]
    public void Execute_repeater_child_adds_repeater_pill_to_node()
    {
        var (asset, _, sink, provider) = Build();
        var nodeId = NodeId.NewId();
        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));

        var children     = provider.GetItemsFor(nodeId, new[] { nodeId })[0].Children!;
        var repeaterItem = children.First(c => c.Label == "Repeater");
        repeaterItem.Execute();

        var pills = asset.Pills.Where(p => p.HostNodeVisualId == nodeId.Value).ToList();
        pills.Should().HaveCount(1);
        pills[0].DecoratorType.Should().Be(NodeType.Repeater);
        pills[0].HostNodeVisualId.Should().Be(nodeId.Value);
    }

    [Fact]
    public void Execute_second_add_increments_stack_index()
    {
        var (asset, _, sink, provider) = Build();
        var nodeId = NodeId.NewId();
        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));

        // GetItemsFor must be called each time because stackIndex is computed at execute time
        provider.GetItemsFor(nodeId, new[] { nodeId })[0].Children!
            .First(c => c.Label == "Inverter").Execute();
        provider.GetItemsFor(nodeId, new[] { nodeId })[0].Children!
            .First(c => c.Label == "Repeater").Execute();

        var pills = asset.Pills.Where(p => p.HostNodeVisualId == nodeId.Value)
                               .OrderBy(p => p.StackIndex)
                               .ToList();
        pills.Should().HaveCount(2);
        pills[0].StackIndex.Should().Be(0);
        pills[1].StackIndex.Should().Be(1);
    }
}
